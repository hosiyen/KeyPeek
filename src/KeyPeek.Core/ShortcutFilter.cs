namespace KeyPeek.Core;

/// <summary>
/// Table selection, progressive filtering (R5) and search (R7), as pure functions.
///
/// Holding a modifier selects that modifier's authored table(s): hold Ctrl in Chrome and
/// you get Chrome's "ctrl" table; hold Alt and you get a genuinely different "alt" table.
/// Legacy sections and the "plain" table (Table == None) are selected for any hold and
/// rely on the chord filter alone.
///
/// Within the selected tables, an entry stays visible while its full chord's modifiers
/// are a superset of everything held — so adding Shift narrows to Ctrl+Shift+…, and
/// releasing it widens back. An empty held set (pinned/search mode, library browser)
/// selects every table and shows everything.
/// </summary>
public static class ShortcutFilter
{
    public static bool MatchesModifiers(ShortcutEntry entry, Modifiers held)
    {
        if (held == Modifiers.None)
            return true;
        // A sequence is entered via its first chord; alternates match if ANY of them
        // starts with the held modifiers (Ctrl shows Ctrl+L, Alt shows the Alt+D form).
        return entry.ChordsAreAlternatives
            ? entry.Chords.Any(c => (c.Mods & held) == held)
            : (entry.Chords[0].Mods & held) == held;
    }

    public static bool MatchesSearch(ShortcutEntry entry, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;
        query = query.Trim();
        // The description is matched in BOTH languages: on a Vietnamese UI the row displays
        // its translation, and a search that only understood the hidden English ("new tab"
        // finds it, "thẻ mới" does not) would look broken to the person actually typing.
        return entry.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || ShortcutL10n.T(entry.Description).Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.KeysText.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.RawKeys.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Select tables for the held modifiers, apply both filters, sort
    /// recommended-first (stable), drop empty sections, and de-duplicate entries that
    /// were authored into more than one selected table.</summary>
    public static IReadOnlyList<ShortcutSection> Apply(
        IEnumerable<ShortcutSection> sections, Modifiers held, string? query)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ShortcutSection>();
        foreach (ShortcutSection section in sections)
        {
            if (held != Modifiers.None &&
                section.Table != Modifiers.None &&
                (section.Table & held) == Modifiers.None)
                continue; // a table for a modifier that isn't held

            var entries = section.Shortcuts
                .Where(e => MatchesModifiers(e, held) && MatchesSearch(e, query))
                .Where(e => seen.Add($"{e.KeysText}|{e.Description}"))
                .OrderByDescending(e => e.Recommended) // OrderBy is stable: file order preserved
                .ToList();
            if (entries.Count > 0)
                result.Add(new ShortcutSection(section.Name, entries, section.Table));
        }
        return result;
    }
}
