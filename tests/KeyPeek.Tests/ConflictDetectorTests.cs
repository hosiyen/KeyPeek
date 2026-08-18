using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class ConflictDetectorTests
{
    private static ShortcutEntry Entry(string keys, string description) => new()
    {
        Chords = KeyChordParser.Parse(keys),
        Description = description,
        RawKeys = keys,
    };

    private static AppDefinition App(string name, bool global, string[] processes, params ShortcutEntry[] entries) => new()
    {
        AppName = name,
        IsGlobal = global,
        ProcessNames = processes,
        Sections = new[] { new ShortcutSection("S", entries) },
        SourceFile = name + ".json",
    };

    [Fact]
    public void App_chord_colliding_with_global_is_reported()
    {
        var apps = new[]
        {
            App("Windows", true, Array.Empty<string>(), Entry("Win+E", "Open File Explorer")),
            App("MyApp", false, new[] { "myapp" }, Entry("Win+E", "Export everything")),
        };
        var conflict = Assert.Single(ConflictDetector.Detect(apps));
        Assert.Equal(ConflictKind.AppVsGlobal, conflict.Kind);
        Assert.Equal("Win+E", conflict.Chord);
    }

    [Fact]
    public void Same_chord_same_description_is_not_a_conflict()
    {
        var apps = new[]
        {
            App("Windows", true, Array.Empty<string>(), Entry("Ctrl+C", "Copy")),
            App("MyApp", false, new[] { "myapp" }, Entry("Ctrl+C", "Copy")),
        };
        Assert.Empty(ConflictDetector.Detect(apps));
    }

    [Fact]
    public void App_chord_shadowing_a_generic_global_is_not_reported()
    {
        // The focused app wins Ctrl+F; reporting this buried the real findings under
        // hundreds of false positives (311 on the real library).
        var apps = new[]
        {
            App("Windows", true, Array.Empty<string>(), Entry("Ctrl+F", "Open domain search")),
            App("MyApp", false, new[] { "myapp" }, Entry("Ctrl+F", "Find in document")),
        };
        Assert.Empty(ConflictDetector.Detect(apps));
    }

    [Theory]
    [InlineData("Alt+Tab")]
    [InlineData("Ctrl+Shift+Esc")]
    [InlineData("Win+E")]
    public void App_chord_the_OS_intercepts_first_is_reported(string keys)
    {
        var apps = new[]
        {
            App("Windows", true, Array.Empty<string>(), Entry(keys, "System action")),
            App("MyApp", false, new[] { "myapp" }, Entry(keys, "App action that never fires")),
        };
        Assert.Single(ConflictDetector.Detect(apps));
    }

    [Fact]
    public void Overlapping_process_matchers_with_same_chord_are_reported()
    {
        var apps = new[]
        {
            App("Chrome", false, new[] { "chrome" }, Entry("Ctrl+T", "New tab")),
            App("Chrome apps", false, new[] { "Chrome.exe" }, Entry("Ctrl+T", "Do something else")),
        };
        var conflict = Assert.Single(ConflictDetector.Detect(apps));
        Assert.Equal(ConflictKind.OverlappingApps, conflict.Kind);
    }

    [Fact]
    public void Conflicts_carry_parsed_chords_for_rendering()
    {
        // Regression: the UI used to re-parse Conflict.Chord, which is a DISPLAY string
        // and can contain placeholder glyphs ("↑↓←→") that the parser rejects.
        var apps = new[]
        {
            App("Windows", true, Array.Empty<string>(), Entry("Win+Left", "Snap window left")),
            App("MyApp", false, new[] { "myapp" }, Entry("Win+Left", "Rotate left")),
        };
        var conflict = Assert.Single(ConflictDetector.Detect(apps));
        var chord = Assert.Single(conflict.Chords);
        Assert.Equal(new KeyChord(Modifiers.Win, "Left"), chord);
    }

    [Fact]
    public void Unrelated_apps_sharing_a_chord_are_not_a_conflict()
    {
        var apps = new[]
        {
            App("Chrome", false, new[] { "chrome" }, Entry("Ctrl+T", "New tab")),
            App("Figma", false, new[] { "figma" }, Entry("Ctrl+T", "Transform")),
        };
        Assert.Empty(ConflictDetector.Detect(apps));
    }
}
