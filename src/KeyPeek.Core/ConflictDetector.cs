namespace KeyPeek.Core;

/// <summary>
/// Finds chords that are defined more than once in ways that genuinely collide:
///
///  - an app chord that Windows itself intercepts first (Win+…, Alt+Tab, Ctrl+Shift+Esc,
///    Ctrl+Alt+Del): the documented app shortcut simply will not fire, which is worth
///    telling the user about, and
///  - the same chord in two definitions whose process matchers overlap (both could be
///    chosen for the same window — almost always an authoring mistake).
///
/// Deliberately NOT reported: an app chord that merely also exists in the system-wide
/// table (Ctrl+C, Ctrl+F…). The focused app wins those, it is completely normal, and
/// reporting them buried the real findings under hundreds of false positives.
/// Two unrelated apps using Ctrl+T is NOT a conflict either; they can never be focused
/// at the same time.
/// </summary>
public static class ConflictDetector
{
    public static IReadOnlyList<Conflict> Detect(IEnumerable<AppDefinition> apps)
    {
        var list = apps.ToList();
        var conflicts = new List<Conflict>();

        var globalChords = new Dictionary<string, (string App, string Desc)>(StringComparer.OrdinalIgnoreCase);
        foreach (AppDefinition g in list.Where(a => a.IsGlobal))
            foreach (ShortcutEntry e in g.Sections.SelectMany(s => s.Shortcuts))
                globalChords.TryAdd(ChordKey(e), (g.AppName, e.Description));

        var appDefs = list.Where(a => !a.IsGlobal).ToList();

        foreach (AppDefinition app in appDefs)
            foreach (ShortcutEntry e in app.Sections.SelectMany(s => s.Shortcuts))
                if (WindowsInterceptsFirst(e) &&
                    globalChords.TryGetValue(ChordKey(e), out var g) &&
                    !SameMeaning(e.Description, g.Desc))
                    conflicts.Add(new Conflict(ConflictKind.AppVsGlobal, ChordKey(e),
                        app.AppName, e.Description, g.App, g.Desc) { Chords = e.Chords });

        for (int i = 0; i < appDefs.Count; i++)
            for (int j = i + 1; j < appDefs.Count; j++)
            {
                if (!MatchersOverlap(appDefs[i], appDefs[j]))
                    continue;
                var chordsB = appDefs[j].Sections.SelectMany(s => s.Shortcuts)
                    .GroupBy(ChordKey, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                foreach (ShortcutEntry e in appDefs[i].Sections.SelectMany(s => s.Shortcuts))
                    if (chordsB.TryGetValue(ChordKey(e), out ShortcutEntry? other))
                        conflicts.Add(new Conflict(ConflictKind.OverlappingApps, ChordKey(e),
                            appDefs[i].AppName, e.Description, appDefs[j].AppName, other.Description)
                        { Chords = e.Chords });
            }

        return conflicts;
    }

    /// <summary>Chords the OS claims before any app sees them, so an app definition
    /// documenting them is documenting something that cannot fire.</summary>
    private static bool WindowsInterceptsFirst(ShortcutEntry e)
    {
        KeyChord first = e.Chords[0];
        if (first.Mods.HasFlag(Modifiers.Win))
            return true;
        return (first.Mods, first.Key) switch
        {
            (Modifiers.Alt, "Tab") => true,
            (Modifiers.Alt, "Esc") => true,
            (Modifiers.Ctrl | Modifiers.Shift, "Esc") => true,
            (Modifiers.Ctrl | Modifiers.Alt, "Delete") => true,
            _ => false,
        };
    }

    private static string ChordKey(ShortcutEntry e) =>
        string.Join(" ", e.Chords.Select(c => c.ToDisplayString()));

    private static bool MatchersOverlap(AppDefinition a, AppDefinition b)
    {
        if (!a.ProcessNames.Select(AppMatcher.NormalizeProcessName)
                .Intersect(b.ProcessNames.Select(AppMatcher.NormalizeProcessName)).Any())
            return false;
        // A titleRegex SEPARATES definitions sharing a process — that is the fix this
        // detector's own advice recommends ("give one a titleRegex"), and the matcher
        // resolves it deterministically: the regex one wins while its title matches, the
        // plain one otherwise. Gmail-in-a-browser vs the browser is layering, not a fight.
        // Only identical matchers (same regex, or both none) still collide.
        return string.Equals(a.TitleRegex, b.TitleRegex, StringComparison.Ordinal);
    }

    /// <summary>An identical description is the same shortcut documented twice, not a fight.</summary>
    private static bool SameMeaning(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
