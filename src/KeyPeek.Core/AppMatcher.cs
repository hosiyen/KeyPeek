namespace KeyPeek.Core;

/// <summary>Resolves a foreground process name to its app definition.</summary>
public static class AppMatcher
{
    public static string NormalizeProcessName(string name)
    {
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name.ToLowerInvariant();
    }

    /// <summary>The (non-global) definition matching this process, or null if unknown.
    /// When several definitions share a process name, one whose optional titleRegex
    /// matches the window title wins over a plain process match.</summary>
    public static AppDefinition? FindForProcess(IEnumerable<AppDefinition> apps,
        string processName, string? windowTitle = null)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return null;
        string wanted = NormalizeProcessName(processName);
        var candidates = apps.Where(a =>
            !a.IsGlobal && !a.IsFallback &&
            a.ProcessNames.Any(p => NormalizeProcessName(p) == wanted)).ToList();
        if (candidates.Count == 0)
            return null;

        foreach (AppDefinition candidate in candidates.Where(c => c.TitleRegex is not null))
        {
            try
            {
                if (windowTitle is not null &&
                    System.Text.RegularExpressions.Regex.IsMatch(windowTitle, candidate.TitleRegex!,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // invalid user regex — reported at load time; ignore here
            }
        }
        return candidates.FirstOrDefault(c => c.TitleRegex is null) ?? candidates[0];
    }

    /// <summary>Global (Windows-wide) definitions — always shown.</summary>
    public static IReadOnlyList<AppDefinition> Globals(IEnumerable<AppDefinition> apps) =>
        apps.Where(a => a.IsGlobal && !a.IsFallback).ToList();

    /// <summary>The "common to most apps" definition, used for the app zone when the
    /// focused app has no definition of its own.</summary>
    public static AppDefinition? Fallback(IEnumerable<AppDefinition> apps) =>
        apps.FirstOrDefault(a => a.IsFallback);
}
