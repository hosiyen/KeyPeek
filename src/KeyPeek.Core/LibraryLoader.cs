using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyPeek.Core;

/// <summary>
/// Loads a folder of per-app shortcut JSON files. Two formats are accepted:
///
/// v2 (preferred) — organized by trigger modifier, the axis the overlay reads along:
///   { "app": "...", "match": {...}, "tables": {
///       "ctrl": { "Tabs": [ { "key": "T", "description": "New tab", "recommended": true },
///                            { "key": "T", "mods": ["shift"], "description": "Reopen tab" } ] },
///       "alt":  { "Navigation": [ { "key": "Left", "description": "Back" } ] },
///       "plain": { "Files": [ { "key": "F2", "description": "Rename" } ] } } }
///   A row may instead carry a full "keys" string ("Ctrl+K Ctrl+S") for sequences or
///   bare-modifier entries.
///
/// v1 (legacy) — flat "sections" with full "keys" strings; still loads, selected for
/// any held modifier via chord filtering alone.
///
/// Malformed input is reported loudly with file and line, and never silently drops
/// other entries: a bad row is skipped with an error, the rest of its file still loads.
/// </summary>
public static class LibraryLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Load every definition in a folder: PowerToys YAML manifests (*.yml/*.yaml,
    /// the native format) and legacy KeyPeek JSON files. "index.yml" is skipped — it is
    /// the WinGet folder's routing index, not a manifest.</summary>
    public static LibraryLoadResult LoadDirectory(string directory)
    {
        var apps = new List<AppDefinition>();
        var errors = new List<LibraryError>();

        if (!Directory.Exists(directory))
            return new LibraryLoadResult(apps, errors);

        IEnumerable<string> files = Directory.EnumerateFiles(directory, "*.yml")
            .Concat(Directory.EnumerateFiles(directory, "*.yaml"))
            .Concat(Directory.EnumerateFiles(directory, "*.json"))
            .Where(f => !Path.GetFileName(f).Equals("index.yml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            AppDefinition? app = file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? LoadFile(file, errors)
                : PowerToysManifestLoader.LoadFile(file, errors);
            if (app is not null)
                apps.Add(app);
        }

        return new LibraryLoadResult(apps, errors);
    }

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

        FileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FileDto>(raw, JsonOptions);
        }
        catch (JsonException ex)
        {
            errors.Add(new LibraryError(path, (int)((ex.LineNumber ?? 0) + 1), $"invalid JSON: {ex.Message}"));
            return null;
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.App))
        {
            errors.Add(new LibraryError(path, 0, "missing \"app\" (the display name)"));
            return null;
        }

        bool isGlobal = dto.Match?.Global == true;
        var processNames = ReadProcessNames(dto.Match);
        if (!isGlobal && processNames.Count == 0)
        {
            errors.Add(new LibraryError(path, 0,
                "\"match\" needs either \"global\": true or a \"processName\" (string or array)"));
            return null;
        }

        string? titleRegex = dto.Match?.TitleRegex;
        if (titleRegex is not null)
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(titleRegex);
            }
            catch (ArgumentException ex)
            {
                errors.Add(new LibraryError(path, 0, $"invalid titleRegex: {ex.Message}"));
                titleRegex = null;
            }
        }

        int errorsBefore = errors.Count;
        int searchFrom = 0; // for line lookups: errors are reported in file order
        var sections = new List<ShortcutSection>();

        if (dto.Tables is not null)
            LoadTables(dto.Tables, path, raw, sections, errors, ref searchFrom);
        if (dto.Sections is not null)
            LoadLegacySections(dto.Sections, path, raw, sections, errors, ref searchFrom);

        if (dto.Tables is null && dto.Sections is null)
        {
            errors.Add(new LibraryError(path, 0, "no \"tables\" (or legacy \"sections\") defined"));
            return null;
        }

        if (sections.Count == 0)
        {
            // Only add the generic error if nothing more specific was already reported.
            if (errors.Count == errorsBefore)
                errors.Add(new LibraryError(path, 0, "file contains no usable shortcuts"));
            return null;
        }

        return new AppDefinition
        {
            AppName = dto.App.Trim(),
            IsGlobal = isGlobal,
            ProcessNames = processNames,
            TitleRegex = titleRegex,
            Sections = sections,
            SourceFile = path,
        };
    }

    private static void LoadTables(Dictionary<string, Dictionary<string, List<RowDto>>> tables,
        string path, string raw, List<ShortcutSection> sections, List<LibraryError> errors, ref int searchFrom)
    {
        foreach ((string tableName, Dictionary<string, List<RowDto>> groups) in tables)
        {
            Modifiers tableMod;
            if (tableName.Trim().ToLowerInvariant() is "plain" or "none")
                tableMod = Modifiers.None;
            else if (!KeyChordParser.TryParseModifierName(tableName, out tableMod))
            {
                errors.Add(new LibraryError(path, FindLine(raw, $"\"{tableName}\"", ref searchFrom),
                    $"unknown table \"{tableName}\" (use ctrl, alt, win, shift or plain)"));
                continue;
            }

            foreach ((string groupName, List<RowDto> rows) in groups)
            {
                var entries = new List<ShortcutEntry>();
                foreach (RowDto row in rows ?? new List<RowDto>())
                {
                    ShortcutEntry? entry = BuildRow(row, tableMod, groupName, path, raw, errors, ref searchFrom);
                    if (entry is not null)
                        entries.Add(entry);
                }
                if (entries.Count > 0)
                    sections.Add(new ShortcutSection(
                        string.IsNullOrWhiteSpace(groupName) ? "Shortcuts" : groupName.Trim(),
                        entries, tableMod));
            }
        }
    }

    private static ShortcutEntry? BuildRow(RowDto row, Modifiers tableMod, string groupName,
        string path, string raw, List<LibraryError> errors, ref int searchFrom)
    {
        if (string.IsNullOrWhiteSpace(row.Description))
        {
            string anchor = row.Keys ?? row.Key ?? "\"description\"";
            errors.Add(new LibraryError(path, FindLine(raw, anchor, ref searchFrom),
                $"row in group \"{groupName}\" is missing \"description\""));
            return null;
        }

        try
        {
            IReadOnlyList<KeyChord> chords;
            if (!string.IsNullOrWhiteSpace(row.Keys))
            {
                chords = KeyChordParser.Parse(row.Keys);
            }
            else if (!string.IsNullOrWhiteSpace(row.Key))
            {
                Modifiers mods = tableMod;
                foreach (string m in row.Mods ?? new List<string>())
                {
                    if (!KeyChordParser.TryParseModifierName(m, out Modifiers flag))
                        throw new FormatException($"unknown modifier \"{m}\" in mods");
                    mods |= flag;
                }
                chords = new[] { new KeyChord(mods, KeyChordParser.CanonicalizeBaseKey(row.Key)) };
            }
            else
            {
                throw new FormatException($"row \"{row.Description}\" needs a \"key\" or \"keys\"");
            }

            return new ShortcutEntry
            {
                Chords = chords,
                Description = row.Description.Trim(),
                Recommended = row.Recommended == true,
                RawKeys = row.Keys?.Trim() ?? string.Join(" ", chords.Select(c => c.ToDisplayString())),
            };
        }
        catch (FormatException ex)
        {
            string anchor = row.Keys ?? row.Key ?? row.Description;
            errors.Add(new LibraryError(path, FindLine(raw, anchor, ref searchFrom), ex.Message));
            return null;
        }
    }

    private static void LoadLegacySections(List<SectionDto> sectionDtos,
        string path, string raw, List<ShortcutSection> sections, List<LibraryError> errors, ref int searchFrom)
    {
        foreach (SectionDto sectionDto in sectionDtos)
        {
            string sectionName = string.IsNullOrWhiteSpace(sectionDto.Name) ? "Shortcuts" : sectionDto.Name!;
            var entries = new List<ShortcutEntry>();
            foreach (ShortcutDto s in sectionDto.Shortcuts ?? new List<ShortcutDto>())
            {
                if (string.IsNullOrWhiteSpace(s.Keys))
                {
                    errors.Add(new LibraryError(path, FindLine(raw, "\"keys\"", ref searchFrom),
                        $"shortcut in section \"{sectionName}\" is missing \"keys\""));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(s.Description))
                {
                    errors.Add(new LibraryError(path, FindLine(raw, s.Keys, ref searchFrom),
                        $"shortcut \"{s.Keys}\" is missing \"description\""));
                    continue;
                }
                try
                {
                    var chords = KeyChordParser.Parse(s.Keys);
                    entries.Add(new ShortcutEntry
                    {
                        Chords = chords,
                        Description = s.Description.Trim(),
                        Recommended = s.Recommended == true,
                        RawKeys = s.Keys.Trim(),
                    });
                }
                catch (FormatException ex)
                {
                    errors.Add(new LibraryError(path, FindLine(raw, s.Keys, ref searchFrom), ex.Message));
                }
            }
            if (entries.Count > 0)
                sections.Add(new ShortcutSection(sectionName, entries));
        }
    }

    // ---- writing (starter files, in-place edits, v1→v2 migration) ----------------------

    /// <summary>Serialize a definition to v2 JSON. Sections keep their table assignment;
    /// legacy sections land in "plain". Sequences and bare-modifier chords use "keys".</summary>
    public static string Serialize(AppDefinition app)
    {
        var root = new JsonObject { ["app"] = app.AppName };

        var match = new JsonObject();
        if (app.IsGlobal)
            match["global"] = true;
        else
            match["processName"] = new JsonArray(app.ProcessNames.Select(p => (JsonNode)p!).ToArray());
        if (app.TitleRegex is not null)
            match["titleRegex"] = app.TitleRegex;
        root["match"] = match;

        var tables = new JsonObject();
        foreach (var tableGroup in app.Sections.GroupBy(s => s.Table))
        {
            var tableObj = new JsonObject();
            foreach (ShortcutSection section in tableGroup)
            {
                var rows = new JsonArray();
                foreach (ShortcutEntry e in section.Shortcuts)
                    rows.Add(RowJson(e, tableGroup.Key));
                tableObj[section.Name] = rows;
            }
            tables[TableName(tableGroup.Key)] = tableObj;
        }
        root["tables"] = tables;

        using var buffer = new MemoryStream();
        // Relaxed escaping keeps "&" and "+" readable — these files are meant to be
        // edited by hand in a text editor.
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
            root.WriteTo(writer);
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static JsonObject RowJson(ShortcutEntry e, Modifiers tableMod)
    {
        var row = new JsonObject();
        KeyChord first = e.Chords[0];
        bool simple = e.Chords.Count == 1 && first.Key.Length > 0 && (first.Mods & tableMod) == tableMod;
        if (simple)
        {
            row["key"] = first.Key;
            Modifiers extra = first.Mods & ~tableMod;
            if (extra != Modifiers.None)
            {
                var mods = new JsonArray();
                if (extra.HasFlag(Modifiers.Win)) mods.Add("win");
                if (extra.HasFlag(Modifiers.Ctrl)) mods.Add("ctrl");
                if (extra.HasFlag(Modifiers.Alt)) mods.Add("alt");
                if (extra.HasFlag(Modifiers.Shift)) mods.Add("shift");
                row["mods"] = mods;
            }
        }
        else
        {
            row["keys"] = string.Join(" ", e.Chords.Select(c => c.ToDisplayString()));
        }
        row["description"] = e.Description;
        if (e.Recommended)
            row["recommended"] = true;
        return row;
    }

    private static string TableName(Modifiers table) => table switch
    {
        Modifiers.Ctrl => "ctrl",
        Modifiers.Alt => "alt",
        Modifiers.Win => "win",
        Modifiers.Shift => "shift",
        _ => "plain",
    };

    /// <summary>A starter definition for an unknown app, ready to open in an editor.</summary>
    /// <summary>A new, EMPTY definition for an app. It deliberately ships no sample
    /// shortcut: an "Example — replace me" entry loads like any other and shows up in the
    /// panel as if the app really had that shortcut, which is worse than an empty file.</summary>
    public static string StarterFile(string appName, string processName) =>
        Serialize(new AppDefinition
        {
            AppName = appName,
            ProcessNames = new[] { processName },
            Sections = new[] { new ShortcutSection("General", Array.Empty<ShortcutEntry>(), Modifiers.Ctrl) },
            SourceFile = "",
        });

    private static List<string> ReadProcessNames(MatchDto? match)
    {
        var result = new List<string>();
        if (match?.ProcessName is not JsonElement el)
            return result;
        if (el.ValueKind == JsonValueKind.String)
        {
            string? s = el.GetString();
            if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in el.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    result.Add(item.GetString()!);
        }
        return result;
    }

    /// <summary>1-based line of the next occurrence of <paramref name="needle"/> at or after
    /// <paramref name="searchFrom"/>; falls back to the first occurrence, then 0 (unknown).</summary>
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

    private sealed class FileDto
    {
        public string? App { get; set; }
        public MatchDto? Match { get; set; }
        public Dictionary<string, Dictionary<string, List<RowDto>>>? Tables { get; set; }
        public List<SectionDto>? Sections { get; set; } // legacy v1
    }

    private sealed class MatchDto
    {
        public JsonElement? ProcessName { get; set; } // string or array of strings
        public bool? Global { get; set; }
        public string? TitleRegex { get; set; }
    }

    private sealed class RowDto
    {
        public string? Key { get; set; }
        public List<string>? Mods { get; set; }
        public string? Keys { get; set; } // sequences / bare modifiers
        public string? Description { get; set; }
        public bool? Recommended { get; set; }
    }

    private sealed class SectionDto
    {
        public string? Name { get; set; }
        public List<ShortcutDto>? Shortcuts { get; set; }
    }

    private sealed class ShortcutDto
    {
        public string? Keys { get; set; }
        public string? Description { get; set; }
        public bool? Recommended { get; set; }
    }
}
