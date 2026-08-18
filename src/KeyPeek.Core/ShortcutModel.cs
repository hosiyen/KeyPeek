namespace KeyPeek.Core;

/// <summary>One key combination: a modifier set plus at most one base key.
/// <see cref="Key"/> is a canonical name ("P", "Enter", "F5", "+", …) or "" for
/// modifier-only chords like a bare Win press.</summary>
public sealed record KeyChord(Modifiers Mods, string Key)
{
    /// <summary>Canonical display order: Win, Ctrl, Alt, Shift.</summary>
    public IEnumerable<string> Parts()
    {
        if (Mods.HasFlag(Modifiers.Win)) yield return "Win";
        if (Mods.HasFlag(Modifiers.Ctrl)) yield return "Ctrl";
        if (Mods.HasFlag(Modifiers.Alt)) yield return "Alt";
        if (Mods.HasFlag(Modifiers.Shift)) yield return "Shift";
        if (Key.Length > 0) yield return Key;
    }

    public string ToDisplayString() => string.Join("+", Parts());
}

/// <summary>Which of the four library layers an entry (or file) came from.
/// Later layers win conflicts; User beats everything and is never auto-touched.</summary>
public enum LibraryLayer
{
    Bundled = 0,    // ships inside the exe, read-only, replaced wholesale on app update
    Downloaded = 1, // community library fetched over HTTPS on the user's schedule
    Discovered = 2, // read at runtime from apps' own config (adapters, WinGet folder)
    User = 3,       // the user's own files and edits
}

public sealed record ShortcutEntry
{
    /// <summary>Usually one chord. Several chords mean either a multi-step sequence
    /// (VS Code's Ctrl+K Ctrl+S) or alternate bindings (Ctrl+L / Alt+D / F4) — see
    /// <see cref="ChordsAreAlternatives"/>; the manifest format cannot distinguish them,
    /// so the loader applies a heuristic (equal modifier sets = sequence).</summary>
    public required IReadOnlyList<KeyChord> Chords { get; init; }
    public required string Description { get; init; }
    public bool Recommended { get; init; }
    /// <summary>The original "keys" string from the data file (for error messages/search).</summary>
    public required string RawKeys { get; init; }

    /// <summary>True = the chords are interchangeable alternates, not steps.</summary>
    public bool ChordsAreAlternatives { get; init; }
    /// <summary>Extra context from the manifest ("While pressing Tab multiple times").</summary>
    public string? Note { get; init; }
    /// <summary>False for display-only entries whose keys aren't real, sendable keys
    /// (PowerToys prose tokens like "&lt;Underlined letter&gt;"). Click-to-run skips these.</summary>
    public bool Executable { get; init; } = true;

    /// <summary>Where this entry came from (library browser shows this; reset-to-shipped
    /// uses it).</summary>
    public LibraryLayer Layer { get; init; } = LibraryLayer.Bundled;
    /// <summary>Human label for the source: a filename, or an adapter name.</summary>
    public string Origin { get; init; } = "";
    /// <summary>True when this entry replaced a lower-layer entry for the same chord.</summary>
    public bool OverridesShipped { get; init; }

    public string KeysText => string.Join(" ", Chords.Select(c => c.ToDisplayString()));
}

/// <summary>A named group of shortcuts. <see cref="Table"/> says which held modifier's
/// view this group belongs to (v2 format: tables.ctrl / tables.alt / …).
/// <see cref="Modifiers.None"/> = the "plain" table (modifier-less keys like F2) or a
/// legacy v1 section — both are selected for any hold and rely on chord filtering.</summary>
public sealed record ShortcutSection(
    string Name,
    IReadOnlyList<ShortcutEntry> Shortcuts,
    Modifiers Table = Modifiers.None);

public sealed record AppDefinition
{
    public required string AppName { get; init; }
    /// <summary>PowerToys manifest PackageName; the stable identity for cross-layer merging.</summary>
    public string? PackageName { get; init; }
    /// <summary>Global (Windows-wide) definitions populate the system zone.</summary>
    public bool IsGlobal { get; init; }
    /// <summary>KeyPeek extension ("Fallback: true"): shortcuts common to most Windows
    /// apps, shown in the app zone when the focused app has no definition of its own —
    /// Ctrl+C/F/S work in an unknown app even though nobody wrote a manifest for it.</summary>
    public bool IsFallback { get; init; }
    /// <summary>Process names this definition applies to (case-insensitive, ".exe" optional).</summary>
    public IReadOnlyList<string> ProcessNames { get; init; } = Array.Empty<string>();
    /// <summary>Optional window-title regex to disambiguate apps sharing a process.</summary>
    public string? TitleRegex { get; init; }
    public required IReadOnlyList<ShortcutSection> Sections { get; init; }
    /// <summary>Path of the file this came from (shown in errors and the UI hint).</summary>
    public required string SourceFile { get; init; }
    /// <summary>Layer of the file itself (entries carry their own after merging).</summary>
    public LibraryLayer Layer { get; init; } = LibraryLayer.Bundled;

    /// <summary>KeyPeek extension: the app version this definition was checked against.
    /// Compared with the focused exe's version for the staleness badge.</summary>
    public string? VerifiedAgainst { get; init; }
    /// <summary>KeyPeek extension: when the definition was last reviewed (ISO date).</summary>
    public string? Updated { get; init; }

    public int ShortcutCount => Sections.Sum(s => s.Shortcuts.Count);

    /// <summary>Identity used to merge definitions across layers. Process-based (not
    /// PackageName-based) so that an adapter reading VS Code's own keybindings merges
    /// into the bundled VS Code definition even though the adapter knows no PackageName.</summary>
    public string MergeKey =>
        (IsFallback ? "::fallback"
            : IsGlobal ? "::global"
            : ProcessNames.Count > 0 ? AppMatcher.NormalizeProcessName(ProcessNames[0]) : AppName.ToLowerInvariant())
        + "|" + (TitleRegex ?? "");
}

public enum ConflictKind
{
    /// <summary>An app-specific chord that also exists as a system-wide chord.</summary>
    AppVsGlobal,
    /// <summary>The same chord defined in two definitions that can match the same process.</summary>
    OverlappingApps,
}

public sealed record Conflict(
    ConflictKind Kind, string Chord,
    string AppA, string DescriptionA,
    string AppB, string DescriptionB)
{
    /// <summary>The parsed chords. <see cref="Chord"/> is a DISPLAY string that can
    /// contain placeholder glyphs ("↑↓←→") and must never be re-parsed.</summary>
    public IReadOnlyList<KeyChord> Chords { get; init; } = Array.Empty<KeyChord>();

    public override string ToString() =>
        $"{Chord}: \"{DescriptionA}\" ({AppA}) vs \"{DescriptionB}\" ({AppB})";
}

public sealed record LibraryError(string File, int Line, string Message)
{
    public override string ToString() =>
        Line > 0 ? $"{Path.GetFileName(File)}:{Line}: {Message}" : $"{Path.GetFileName(File)}: {Message}";
}

public sealed record LibraryLoadResult(IReadOnlyList<AppDefinition> Apps, IReadOnlyList<LibraryError> Errors)
{
    public static readonly LibraryLoadResult Empty = new(Array.Empty<AppDefinition>(), Array.Empty<LibraryError>());
    public int TotalShortcuts => Apps.Sum(a => a.ShortcutCount);
}
