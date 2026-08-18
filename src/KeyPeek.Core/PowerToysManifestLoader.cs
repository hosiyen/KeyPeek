using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace KeyPeek.Core;

/// <summary>
/// Reads and writes PowerToys Shortcut Guide YAML manifests — KeyPeek's native on-disk
/// format. Verified against the real corpus in microsoft/PowerToys (MIT):
///
///   PackageName: +WindowsNT.Notepad
///   Name: Notepad
///   WindowFilter: "Notepad.exe"     # process image; "*" = system-wide
///   BackgroundProcess: false        # optional; "this app keeps running in the
///                                   # background" — NOT a claim that its shortcuts
///                                   # are system-wide (see isGlobal below)
///   Shortcuts:
///     - SectionName: File
///       Properties:
///         - Name: New tab
///           Recommended: true       # optional; only ever true in the corpus
///           Description: extra note # optional
///           Shortcut:
///             - Win: false
///               Ctrl: true
///               Shift: false
///               Alt: false
///               Keys: [ N ]
///
/// Corpus quirks handled here: the Shell manifest has no Name and uses WindowFilter "*";
/// Explorer omits BackgroundProcess; a Keys list may hold several simultaneous keys
/// ("&lt;Office&gt;", W); an empty-string key means a bare-modifier chord (lone Win); key
/// spellings vary wildly (see <see cref="PowerToysKeyMap"/>); and a multi-chord Shortcut
/// list can mean a SEQUENCE (VS Code Ctrl+K, Ctrl+S) or ALTERNATES (Edge Ctrl+L / Alt+D
/// / F4) with no distinguishing field — chords with identical modifier sets are treated
/// as a sequence, anything else as alternates, which classifies every known example in
/// the corpus correctly.
///
/// KeyPeek extensions (optional fields, ignored by PowerToys): VerifiedAgainst, Updated,
/// TitleRegex, AdditionalWindowFilters.
/// </summary>
public static class PowerToysManifestLoader
{
    // YamlDotNet's deserializer is NOT thread-safe, and manifests are parsed from several
    // threads: the library watcher, the downloader validating a fresh cache, and the app's
    // own startup load. Sharing one instance produced sporadic "Exception during
    // deserialization" failures that looked like corrupt files. One per thread is cheap.
    [ThreadStatic] private static IDeserializer? _deserializer;

    private static IDeserializer Deserializer =>
        _deserializer ??= new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    public static AppDefinition? LoadFile(string path, List<LibraryError> errors)
    {
        string raw;
        try
        {
            raw = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            errors.Add(new LibraryError(path, 0, $"cannot read file: {ex.Message}"));
            return null;
        }
        return LoadText(raw, path, errors);
    }

    public static AppDefinition? LoadText(string raw, string path, List<LibraryError> errors)
    {
        ManifestDto? dto;
        try
        {
            dto = Deserializer.Deserialize<ManifestDto>(raw);
        }
        catch (YamlException ex)
        {
            errors.Add(new LibraryError(path, (int)ex.Start.Line, $"invalid YAML: {ex.Message}"));
            return null;
        }

        if (dto is null)
        {
            errors.Add(new LibraryError(path, 0, "empty manifest"));
            return null;
        }

        bool isFallback = dto.Fallback == true;
        // ONLY WindowFilter "*" means system-wide. BackgroundProcess used to count too, and
        // that was wrong: Telegram's manifest sets BackgroundProcess: true alongside
        // WindowFilter "telegram.exe", so every one of its shortcuts — "Quit Telegram",
        // chat navigation, text formatting — was published into the system-wide rail of
        // every other app. The flag says the app keeps running in the background, not that
        // its shortcuts work everywhere; nothing in the format tells us WHICH of them do.
        bool isGlobal = !isFallback && dto.WindowFilter?.Trim() == "*";
        var processNames = new List<string>();
        if (!isGlobal && !isFallback)
        {
            if (string.IsNullOrWhiteSpace(dto.WindowFilter))
            {
                errors.Add(new LibraryError(path, 0, "missing WindowFilter (process name, or \"*\" for system-wide)"));
                return null;
            }
            processNames.Add(AppMatcher.NormalizeProcessName(dto.WindowFilter));
            foreach (string extra in dto.AdditionalWindowFilters ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(extra))
                    processNames.Add(AppMatcher.NormalizeProcessName(extra));
        }

        string appName = !string.IsNullOrWhiteSpace(dto.Name) ? dto.Name!.Trim()
            : isFallback ? "Common shortcuts"
            : isGlobal ? "Windows"
            : dto.PackageName is { } pkg ? pkg.Split('.')[^1]
            : Path.GetFileNameWithoutExtension(path);

        int errorsBefore = errors.Count;
        int searchFrom = 0;
        var sections = new List<ShortcutSection>();
        foreach (SectionDto sectionDto in (dto.Shortcuts ?? new List<SectionDto>()).Where(s => s is not null))
        {
            string sectionName = string.IsNullOrWhiteSpace(sectionDto.SectionName)
                ? "Shortcuts" : SanitizeSectionName(sectionDto.SectionName!);
            var entries = new List<ShortcutEntry>();
            foreach (PropertyDto prop in (sectionDto.Properties ?? new List<PropertyDto>()).Where(p => p is not null))
            {
                ShortcutEntry? entry = BuildEntry(prop, sectionName, path, raw, errors, ref searchFrom);
                if (entry is not null)
                    entries.Add(entry);
            }
            if (entries.Count > 0)
                sections.Add(new ShortcutSection(sectionName, entries));
        }

        if (sections.Count == 0)
        {
            // An empty manifest is legitimate: "Add app" and the shortcut editor both create
            // the file before there is anything in it, and rejecting it made the new app
            // vanish from the library with only a log line to explain. A file that DECLARED
            // shortcuts but produced none is still an error — that is a broken file, not an
            // empty one.
            bool declaredNothing = (dto.Shortcuts ?? new List<SectionDto>())
                .Where(s => s is not null)
                .All(s => (s.Properties ?? new List<PropertyDto>()).Count == 0);
            if (!declaredNothing)
            {
                if (errors.Count == errorsBefore)
                    errors.Add(new LibraryError(path, 0, "manifest contains no usable shortcuts"));
                return null;
            }
            sections.Add(new ShortcutSection("Shortcuts", Array.Empty<ShortcutEntry>()));
        }

        return new AppDefinition
        {
            AppName = appName,
            PackageName = string.IsNullOrWhiteSpace(dto.PackageName) ? null : dto.PackageName!.Trim(),
            IsGlobal = isGlobal,
            IsFallback = isFallback,
            ProcessNames = processNames,
            TitleRegex = string.IsNullOrWhiteSpace(dto.TitleRegex) ? null : dto.TitleRegex,
            VerifiedAgainst = string.IsNullOrWhiteSpace(dto.VerifiedAgainst) ? null : dto.VerifiedAgainst!.Trim(),
            Updated = string.IsNullOrWhiteSpace(dto.Updated) ? null : dto.Updated!.Trim(),
            Sections = sections,
            SourceFile = path,
        };
    }

    private static ShortcutEntry? BuildEntry(PropertyDto prop, string sectionName,
        string path, string raw, List<LibraryError> errors, ref int searchFrom)
    {
        if (string.IsNullOrWhiteSpace(prop.Name))
        {
            errors.Add(new LibraryError(path, FindLine(raw, "Properties", ref searchFrom),
                $"a property in section \"{sectionName}\" has no Name"));
            return null;
        }

        // A truncated file (the watcher can read one mid-save) yields list items that are
        // null rather than objects — "Shortcut:\n    - " parses to [null]. Drop those before
        // touching their fields; the entry then fails the "no Shortcut" check below like any
        // other incomplete entry, instead of throwing on the loader's thread.
        var chordDtos = (prop.Shortcut ?? new List<ChordDto>()).Where(c => c is not null).ToList();
        if (chordDtos.Count == 0)
        {
            errors.Add(new LibraryError(path, FindLine(raw, prop.Name, ref searchFrom),
                $"\"{prop.Name}\" has no Shortcut"));
            return null;
        }

        var chords = new List<KeyChord>();
        bool executable = true;
        bool multiKeyChord = false; // one chord listed several keys pressed together
        bool officeAlias = false;   // an <Office>-key chord rewritten to its real modifiers
        foreach (ChordDto c in chordDtos)
        {
            Modifiers mods = Modifiers.None;
            if (c.Win == true) mods |= Modifiers.Win;
            if (c.Ctrl == true) mods |= Modifiers.Ctrl;
            if (c.Shift == true) mods |= Modifiers.Shift;
            if (c.Alt == true) mods |= Modifiers.Alt;

            // Some upstream manifests (Adobe After Effects, 126 rows) write the whole chord
            // into the key token — Keys: ["Ctrl A"] with every modifier flag false. Taken
            // literally that renders one wide cap reading "Ctrl A" and can never be sent.
            // Fold leading modifier words into the modifier set instead; the remainder is
            // the real key.
            var normalizedKeys = new List<string>();
            foreach (string k in c.Keys ?? new List<string>())
            {
                string token = (k ?? "").Trim();
                while (SplitLeadingModifier(token) is ({ } mod, { } rest))
                {
                    mods |= mod;
                    token = rest;
                }
                if (token.Length > 0)
                    normalizedKeys.Add(token);
            }
            var keyTokens = normalizedKeys.Select(PowerToysKeyMap.Map).ToList();

            // The Office key is an alias: the hardware key on Microsoft keyboards emits
            // Ctrl+Shift+Alt+Win. Writing the chord out as those four modifiers does two
            // things a bare "Office" cap cannot: it tells everyone WITHOUT the key how to
            // press it, and it makes the row sendable, so click-to-run actually runs it.
            // (Reported as both: "nên ghi ra" and "tôi nhấn vào run lại k có run".)
            if (keyTokens.Count >= 2 && keyTokens[0].Key == "Office")
            {
                mods |= Modifiers.Ctrl | Modifiers.Shift | Modifiers.Alt | Modifiers.Win;
                keyTokens.RemoveAt(0);
                officeAlias = true;
            }
            if (keyTokens.Any(t => !t.Executable))
                executable = false;

            if (mods == Modifiers.None && keyTokens.Count == 0)
            {
                errors.Add(new LibraryError(path, FindLine(raw, prop.Name, ref searchFrom),
                    $"\"{prop.Name}\" has a chord with no modifiers and no keys"));
                return null;
            }

            if (keyTokens.Count <= 1)
            {
                chords.Add(new KeyChord(mods, keyTokens.Count == 1 ? keyTokens[0].Key : ""));
            }
            else
            {
                // Several Keys in ONE chord = pressed together ("<Office>" + W). Each key
                // gets its own cap: joining them into a single "Office+W" cap drew one wide
                // key that reads like a key nobody has. Not sendable either way — we can't
                // synthesize a hardware Office/Copilot key.
                multiKeyChord = true;
                executable = false;
                for (int k = 0; k < keyTokens.Count; k++)
                    chords.Add(new KeyChord(k == 0 ? mods : Modifiers.None, keyTokens[k].Key));
            }
        }

        // Sequence vs alternates (structurally identical in the format): an explicit
        // Sequence flag wins — KeyPeek writes one, so a shortcut the user recorded here
        // survives a round-trip. Otherwise fall back to the heuristic that reads the
        // upstream corpus correctly: identical modifier sets on every chord → sequence.
        // Keys expanded from one chord are neither: they are one press, so never "or".
        bool isSequence = prop.Sequence ?? (chords.Count > 1 &&
            chords.All(c => c.Mods == chords[0].Mods && c.Mods != Modifiers.None && c.Key.Length > 0));

        return new ShortcutEntry
        {
            Chords = chords,
            ChordsAreAlternatives = chords.Count > 1 && !isSequence && !multiKeyChord,
            Description = prop.Name.Trim(),
            Note = string.IsNullOrWhiteSpace(prop.Description)
                ? (officeAlias ? OfficeAliasNote : null)
                : prop.Description!.Trim(),
            Recommended = prop.Recommended == true,
            Executable = executable,
            RawKeys = string.Join(chords.Count > 1 && !isSequence ? " / " : " ",
                chords.Select(c => c.ToDisplayString())),
        };
    }

    /// <summary>Tooltip for chords rewritten from the Office key to its real modifiers.
    /// Goes through L10n at display time like every note.</summary>
    public const string OfficeAliasNote =
        "The Office key on some Microsoft keyboards presses this whole combo for you.";

    /// <summary>"Ctrl A" → (Ctrl, "A"). Peels ONE leading modifier word off a key token,
    /// or (null, null) when the token has no space or does not start with a modifier.
    /// Never consumes the last word: a token that is all modifiers ("Ctrl Shift") keeps its
    /// final word as a display-only key rather than becoming a bare-modifier chord that
    /// click-to-run would happily send.</summary>
    private static (Modifiers? Mod, string? Remainder) SplitLeadingModifier(string token)
    {
        int space = token.IndexOf(' ');
        if (space <= 0 || space == token.Length - 1)
            return (null, null);
        Modifiers? mod = token[..space] switch
        {
            "Ctrl" or "Control" => Modifiers.Ctrl,
            "Shift" => Modifiers.Shift,
            "Alt" => Modifiers.Alt,
            "Win" or "Windows" => Modifiers.Win,
            _ => null,
        };
        return mod is null ? (null, null) : (mod, token[(space + 1)..].TrimStart());
    }

    // ---- writing (starter files, user-file edits, conversions) --------------------------

    public static string Serialize(AppDefinition app)
    {
        var dto = new ManifestDto
        {
            PackageName = app.PackageName,
            Name = app.AppName,
            // A fallback definition has no process and must keep its flag: writing it
            // without one produced a file the loader then rejected, so every edit to
            // "Common shortcuts" was saved into a file that silently never loaded again.
            WindowFilter = app.IsGlobal || app.IsFallback ? "*"
                : app.ProcessNames.Count > 0 ? app.ProcessNames[0] + ".exe" : "",
            BackgroundProcess = app.IsGlobal,
            Fallback = app.IsFallback ? true : null,
            VerifiedAgainst = app.VerifiedAgainst,
            Updated = app.Updated,
            TitleRegex = app.TitleRegex,
            AdditionalWindowFilters = app.ProcessNames.Count > 1
                ? app.ProcessNames.Skip(1).Select(p => p + ".exe").ToList()
                : null,
            Shortcuts = new List<SectionDto>(),
        };

        // Emit flat sections; entries authored into several per-modifier tables are
        // de-duplicated (the by-modifier index is rebuilt at load anyway).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in app.Sections.GroupBy(s => s.Name))
        {
            var props = new List<PropertyDto>();
            foreach (ShortcutEntry e in group.SelectMany(s => s.Shortcuts))
            {
                if (!seen.Add($"{group.Key}|{e.KeysText}|{e.Description}"))
                    continue;
                bool sequence = e.Chords.Count > 1 && !e.ChordsAreAlternatives;
                props.Add(new PropertyDto
                {
                    Name = e.Description,
                    Recommended = e.Recommended ? true : null,
                    Description = e.Note,
                    // The format cannot tell a sequence (Ctrl+K then Z) from alternatives
                    // (Ctrl+L or Alt+D) — both are a list of chords — and the modifier
                    // heuristic that reads them apart guesses wrong for a sequence whose
                    // later steps have no modifier. Say it outright when we know: without
                    // this, every sequence the editor writes came back as "or", and the
                    // panel then showed only its first step.
                    Sequence = sequence ? true : null,
                    Shortcut = e.Chords.Select(c => new ChordDto
                    {
                        Win = c.Mods.HasFlag(Modifiers.Win),
                        Ctrl = c.Mods.HasFlag(Modifiers.Ctrl),
                        Shift = c.Mods.HasFlag(Modifiers.Shift),
                        Alt = c.Mods.HasFlag(Modifiers.Alt),
                        Keys = new List<string> { EmitKey(c.Key) },
                    }).ToList(),
                });
            }
            if (props.Count > 0)
                dto.Shortcuts.Add(new SectionDto { SectionName = group.Key, Properties = props });
        }

        var serializer = new SerializerBuilder()
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
        return serializer.Serialize(dto);
    }

    private static string EmitKey(string canonical) => canonical switch
    {
        "+" => "Plus",
        "-" => "Minus",
        "=" => "Equals",
        _ => canonical, // bare named keys (PageUp, Esc, Tab…) are valid corpus spellings
    };

    /// <summary>A starter manifest for an unknown app, ready to open in an editor.</summary>
    /// <summary>A new, EMPTY manifest for an app. No sample shortcut on purpose: a placeholder
    /// entry parses like a real one and appears in the panel as though the app had it.</summary>
    public static string StarterFile(string appName, string processName) =>
        Serialize(new AppDefinition
        {
            AppName = appName,
            ProcessNames = new[] { processName },
            Updated = DateTime.Now.ToString("yyyy-MM-dd"),
            Sections = new[] { new ShortcutSection("General", Array.Empty<ShortcutEntry>()) },
            SourceFile = "",
        });

    /// <summary>The Shell manifest embeds UI-templating tokens in some section titles
    /// ("&lt;TASKBAR1-9&gt;Taskbar Shortcuts") — strip any leading &lt;...&gt; marker.</summary>
    private static string SanitizeSectionName(string name)
    {
        name = name.Trim();
        if (name.StartsWith('<'))
        {
            int end = name.IndexOf('>');
            if (end > 0 && end < name.Length - 1)
                name = name[(end + 1)..].Trim();
        }
        return name.Length == 0 ? "Shortcuts" : name;
    }

    private static int FindLine(string text, string needle, ref int searchFrom)
    {
        int idx = searchFrom < text.Length ? text.IndexOf(needle, searchFrom, StringComparison.Ordinal) : -1;
        if (idx < 0)
            idx = text.IndexOf(needle, StringComparison.Ordinal);
        if (idx < 0)
            return 0;
        searchFrom = idx + needle.Length;
        int line = 1;
        for (int i = 0; i < idx; i++)
            if (text[i] == '\n')
                line++;
        return line;
    }

    // DTOs mirror the manifest schema exactly (casing matters to YamlDotNet's default
    // naming). Unknown fields in files are ignored; our extensions are optional.
    private sealed class ManifestDto
    {
        public string? PackageName { get; set; }
        public string? Name { get; set; }
        public string? WindowFilter { get; set; }
        public bool? BackgroundProcess { get; set; }
        public bool? Fallback { get; set; }
        public string? VerifiedAgainst { get; set; }
        public string? Updated { get; set; }
        public string? TitleRegex { get; set; }
        public List<string>? AdditionalWindowFilters { get; set; }
        public List<SectionDto>? Shortcuts { get; set; }
    }

    private sealed class SectionDto
    {
        public string? SectionName { get; set; }
        public List<PropertyDto>? Properties { get; set; }
    }

    private sealed class PropertyDto
    {
        public string? Name { get; set; }
        public bool? Recommended { get; set; }
        public string? Description { get; set; }
        public List<ChordDto>? Shortcut { get; set; }

        /// <summary>KeyPeek extension. True = the chords are pressed one after another,
        /// false = they are alternatives. Absent = fall back to the modifier heuristic,
        /// which is all the upstream format gives us.</summary>
        public bool? Sequence { get; set; }
    }

    private sealed class ChordDto
    {
        public bool? Win { get; set; }
        public bool? Ctrl { get; set; }
        public bool? Shift { get; set; }
        public bool? Alt { get; set; }
        public List<string>? Keys { get; set; }
    }
}
