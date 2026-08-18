using System.Reflection;
using KeyPeek.Core;
using Xunit;
using Xunit.Abstractions;

namespace KeyPeek.Tests;

/// <summary>
/// Audits the library the way the panel sees it — bundled manifests merged with whatever
/// the machine's WinGet folder supplies — rather than one file at a time. The two data
/// defects a user found (a hardware key drawn as one wide cap; every Telegram shortcut
/// published system-wide) were both invisible per-file and obvious in the merged view.
/// </summary>
public class MergedLibraryAuditTests
{
    private readonly ITestOutputHelper _out;
    public MergedLibraryAuditTests(ITestOutputHelper output) => _out = output;

    private static string RepoLibrary()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "library")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "library");
    }

    private static string WinGetLibrary() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WinGet", "KeyboardShortcuts");

    private static IReadOnlyList<AppDefinition> Merged()
    {
        var layers = new List<(LibraryLayer, IReadOnlyList<AppDefinition>)>
        {
            (LibraryLayer.Bundled, LibraryLoader.LoadDirectory(RepoLibrary()).Apps),
        };
        if (Directory.Exists(WinGetLibrary()))
            layers.Add((LibraryLayer.Discovered, LibraryLoader.LoadDirectory(WinGetLibrary()).Apps));
        return LibraryMerger.Merge(layers);
    }

    [Fact]
    public void Only_star_filtered_definitions_reach_the_system_wide_rail()
    {
        // Everything in this list is shown in EVERY app's panel, so it has to be shortcuts
        // that really do work everywhere. An app that merely runs in the background does
        // not qualify — that mistake put "Quit Telegram" in Microsoft Edge.
        var globals = Merged().Where(a => a.IsGlobal).Select(a => a.AppName).OrderBy(n => n).ToList();
        _out.WriteLine("system-wide definitions: " + string.Join(", ", globals));

        Assert.Equal(new[] { "Windows" }, globals);
    }

    [Fact]
    public void The_system_wide_rail_stays_a_rail()
    {
        // A flood here is the symptom of a definition being mis-classified as global; the
        // Windows table itself is ~140 entries. The bound is generous but finite.
        int systemEntries = Merged().Where(a => a.IsGlobal).Sum(a => a.ShortcutCount);
        _out.WriteLine($"system-wide entries: {systemEntries}");
        Assert.InRange(systemEntries, 1, 260);
    }

    [Fact]
    public void No_definition_swallows_another_app_by_sharing_its_process()
    {
        // Merge identity is process + titleRegex: two definitions with the SAME identity
        // become one app, and one app's shortcuts then appear under the other's name.
        // Sharing a process with DIFFERENT titleRegexes is the web-app layering (Gmail
        // rides msedge next to Microsoft Edge) and is exactly how the matcher separates
        // them — not a collision.
        var byKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (AppDefinition app in Merged())
        foreach (string process in app.ProcessNames)
        {
            string key = $"{process}|{app.TitleRegex}";
            if (!byKey.TryGetValue(key, out List<string>? names))
                byKey[key] = names = new List<string>();
            if (!names.Contains(app.AppName))
                names.Add(app.AppName);
        }

        var collisions = byKey.Where(p => p.Value.Count > 1)
            .Select(p => $"{p.Key}: {string.Join(" + ", p.Value)}").ToList();
        Assert.True(collisions.Count == 0,
            "Two apps share one process+titleRegex identity:\n  " + string.Join("\n  ", collisions));
    }

    // ---- web apps: definitions that ride a browser process, separated by tab title ----

    [Fact]
    public void The_web_apps_win_only_when_their_tab_is_actually_open()
    {
        IReadOnlyList<AppDefinition> merged = Merged();

        // Tab titles as browsers really render them, including the localized product
        // names a Vietnamese system shows.
        Assert.Equal("Gmail", AppMatcher.FindForProcess(merged, "msedge",
            "Hộp thư đến (3.211) - ai@gmail.com - Gmail — Microsoft Edge")?.AppName);
        Assert.Equal("YouTube", AppMatcher.FindForProcess(merged, "chrome",
            "(473) YouTube - Google Chrome")?.AppName);
        Assert.Equal("Google Docs", AppMatcher.FindForProcess(merged, "msedge",
            "Tài liệu không có tiêu đề - Google Tài liệu")?.AppName);
        Assert.Equal("Google Sheets", AppMatcher.FindForProcess(merged, "msedge",
            "Bảng tính - Google Trang tính")?.AppName);
        Assert.Equal("Facebook", AppMatcher.FindForProcess(merged, "msedge",
            "(1) Facebook — Microsoft Edge")?.AppName);

        // Any other tab: the browser's own definition, exactly as before.
        Assert.Equal("Microsoft Edge", AppMatcher.FindForProcess(merged, "msedge",
            "Home • Threads — Microsoft Edge")?.AppName);
        Assert.Equal("Google Chrome", AppMatcher.FindForProcess(merged, "chrome",
            "New Tab - Google Chrome")?.AppName);
        // No title at all (warm-up, odd windows) must never crash into a web app.
        Assert.Equal("Microsoft Edge", AppMatcher.FindForProcess(merged, "msedge", null)?.AppName);
    }

    [Fact]
    public void Web_apps_do_not_flood_the_conflicts_page()
    {
        // Gmail-on-msedge vs Microsoft Edge share a process on purpose; the titleRegex
        // separates them deterministically. If this fails, ConflictDetector has regressed
        // into reporting the layering itself as a fight.
        var conflicts = ConflictDetector.Detect(Merged());
        var webAppNames = new[] { "Gmail", "YouTube", "Google Docs", "Google Sheets", "Facebook" };
        var offenders = conflicts
            .Where(c => webAppNames.Contains(c.AppA) || webAppNames.Contains(c.AppB))
            .Select(c => $"{c.AppA} vs {c.AppB}: {c.Chord}")
            .ToList();
        Assert.True(offenders.Count == 0,
            "Web-app layering reported as conflicts:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_merged_app_has_shortcuts_and_a_name()
    {
        var broken = Merged()
            .Where(a => a.ShortcutCount == 0 || string.IsNullOrWhiteSpace(a.AppName))
            .Select(a => $"{a.AppName} ({Path.GetFileName(a.SourceFile)}): {a.ShortcutCount}")
            .ToList();
        Assert.True(broken.Count == 0, "Apps that would show up empty:\n  " + string.Join("\n  ", broken));
    }

    [Fact]
    public void The_merged_library_summary_is_recorded()
    {
        // Not an assertion so much as a printed inventory: when a number moves, the diff
        // says which app moved it.
        IReadOnlyList<AppDefinition> merged = Merged();
        _out.WriteLine($"{merged.Count} apps, {merged.Sum(a => a.ShortcutCount)} shortcuts");
        foreach (AppDefinition app in merged.OrderByDescending(a => a.ShortcutCount))
            _out.WriteLine($"  {app.ShortcutCount,5}  {app.AppName}" +
                           (app.IsGlobal ? "  [system-wide]" : "") +
                           (app.IsFallback ? "  [fallback]" : ""));
        Assert.NotEmpty(merged);
    }
}
