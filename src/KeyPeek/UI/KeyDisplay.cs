using System.Windows.Media;
using KeyPeek.Core;

namespace KeyPeek.UI;

// What one rendered chord looks like: a run of key cap labels. Prefix is the muted
// separator drawn before alternates ("or").
public sealed record ChordVm(IReadOnlyList<string> Keys, string Prefix = "");

/// <param name="KeysColumn">Width the card wants its key column to be, so every row in a
/// card lines up. 0 = no card preference (the row uses its own width).</param>
public sealed record EntryVm(
    string Description,
    bool Recommended,
    IReadOnlyList<ChordVm> Chords,
    IReadOnlyList<KeyChord> ChordData, // what click-to-execute sends
    bool Executable,
    string Tooltip,
    double KeysColumn = 0);

public sealed record SectionCardVm(string SectionName, IReadOnlyList<EntryVm> Entries);

/// <summary>One of the two panel zones (section 4): the focused app's shortcuts, or the
/// system-wide rail.</summary>
public sealed record ZoneVm(string Header, IReadOnlyList<SectionCardVm> Cards, int Count);

/// <summary>Everything the overlay window renders for one hold.</summary>
public sealed record OverlayVm(
    IReadOnlyList<string> HeroCaps,   // the held modifier(s), drawn large
    string Title,
    string? Subtitle,                 // e.g. "chrome.exe"
    ImageSource? Icon,
    ZoneVm? AppZone,
    ZoneVm? SystemZone,
    IReadOnlyList<EntryVm> Frequent,  // "frequently used" strip (empty = hidden)
    int AppColumns,                   // card columns the app zone gets (0-2)
    int SystemColumns,                // card columns the system rail gets (0-2)
    string? HintText,                 // unknown-app / elevated notices
    bool ShowCreateButton,
    string FooterLeft,
    string FooterRight);

/// <summary>Canonical key names → what the key caps actually show (symbols where a
/// symbol reads better than a word).</summary>
internal static class KeyDisplay
{
    public static string For(string canonical) => canonical switch
    {
        "Up" => "↑",
        "Down" => "↓",
        "Left" => "←",
        "Right" => "→",
        "Enter" => "⏎",
        "Backspace" => "⌫",
        "Tab" => "⇥",
        "Delete" => "Del",
        "Insert" => "Ins",
        "PageUp" => "PgUp",
        "PageDown" => "PgDn",
        "PrintScreen" => "PrtScn",
        "CapsLock" => "Caps",
        "ScrollLock" => "ScrLk",
        _ => canonical,
    };

    public static List<string> ModifierLabels(Modifiers mods)
    {
        var labels = new List<string>(4);
        if (mods.HasFlag(Modifiers.Win)) labels.Add("Win");
        if (mods.HasFlag(Modifiers.Ctrl)) labels.Add("Ctrl");
        if (mods.HasFlag(Modifiers.Alt)) labels.Add("Alt");
        if (mods.HasFlag(Modifiers.Shift)) labels.Add("⇧");
        return labels;
    }

    /// <summary>Plain-ASCII modifier names for the log. The UI labels use glyphs (⇧), which
    /// made log lines depend on a visual choice and unreadable to the verification scripts
    /// (PowerShell 5.1 reads the BOM-less log as ANSI, so a glyph arrives as mojibake).</summary>
    public static string ModifierLogText(Modifiers mods)
    {
        var names = new List<string>(4);
        if (mods.HasFlag(Modifiers.Win)) names.Add("Win");
        if (mods.HasFlag(Modifiers.Ctrl)) names.Add("Ctrl");
        if (mods.HasFlag(Modifiers.Alt)) names.Add("Alt");
        if (mods.HasFlag(Modifiers.Shift)) names.Add("Shift");
        return names.Count > 0 ? string.Join("+", names) : "none";
    }

    /// <summary>Render a chord relative to the held modifiers: the header already states
    /// what's held, so rows only show what's EXTRA (a held Ctrl turns "Ctrl+Shift+N"
    /// into ⇧ N). Bare-modifier chords fall back to their full labels.</summary>
    public static ChordVm ToChordVm(KeyChord chord, Modifiers hide, string prefix = "")
    {
        List<string> keys = ModifierLabels(chord.Mods & ~hide);
        if (chord.Key.Length > 0)
            keys.Add(For(chord.Key));
        if (keys.Count == 0)
            keys = ModifierLabels(chord.Mods);
        return new ChordVm(keys, prefix);
    }

    public static EntryVm ToEntryVm(ShortcutEntry entry, Modifiers hide)
    {
        // Alternates under a held modifier collapse to the matching form: the Ctrl view
        // shows Ctrl+L, the Alt view shows Alt+D. With nothing held (search/pinned/
        // library) all alternates render, separated by a muted "or".
        IReadOnlyList<KeyChord> shown = entry.Chords;
        if (entry.ChordsAreAlternatives && hide != Modifiers.None)
        {
            KeyChord match = entry.Chords.FirstOrDefault(c => (c.Mods & hide) == hide) ?? entry.Chords[0];
            shown = new[] { match };
        }

        var chordVms = shown
            .Select((c, i) => ToChordVm(c, hide,
                entry.ChordsAreAlternatives && i > 0 ? L10n.T("or") : ""))
            .ToList();

        // The description goes through the corpus table (ShortcutL10n): translated when the
        // UI is Vietnamese and the table knows it, English otherwise. Notes are usually
        // library data (left as-is), but a few are authored by the loader itself — the
        // Office-key alias explanation — and those translate through the chrome table.
        string description = ShortcutL10n.T(entry.Description);
        string tooltip = description
            + (entry.Note is null ? "" : Environment.NewLine + L10n.T(entry.Note))
            + (entry.Executable
                ? Environment.NewLine + L10n.T("Click to run")
                : Environment.NewLine + L10n.T("(display only — not sendable)"));

        return new EntryVm(description, entry.Recommended, chordVms,
            shown, entry.Executable, tooltip);
    }
}
