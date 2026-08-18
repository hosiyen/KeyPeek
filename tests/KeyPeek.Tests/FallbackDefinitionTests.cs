using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class FallbackDefinitionTests
{
    private const string CommonYaml = """
        PackageName: +KeyPeek.CommonApps
        Name: Common shortcuts
        WindowFilter: "*"
        Fallback: true
        Shortcuts:
          - SectionName: Editing
            Properties:
              - Name: Find
                Recommended: true
                Shortcut:
                - { Win: false, Ctrl: true, Shift: false, Alt: false, Keys: [ F ] }
        """;

    private static AppDefinition Load(string yaml)
    {
        var errors = new List<LibraryError>();
        AppDefinition? app = PowerToysManifestLoader.LoadText(yaml, "common.yml", errors);
        Assert.True(errors.Count == 0, string.Join("; ", errors));
        return app!;
    }

    [Fact]
    public void Fallback_manifest_is_not_global_and_matches_no_process()
    {
        AppDefinition common = Load(CommonYaml);

        Assert.True(common.IsFallback);
        Assert.False(common.IsGlobal); // must NOT land in the system zone
        Assert.Empty(common.ProcessNames);

        var library = new[] { common };
        Assert.Empty(AppMatcher.Globals(library));
        Assert.Null(AppMatcher.FindForProcess(library, "claude"));
        Assert.Same(common, AppMatcher.Fallback(library));
    }

    [Fact]
    public void A_real_app_definition_still_wins_over_the_fallback()
    {
        AppDefinition common = Load(CommonYaml);
        AppDefinition notepad = Load("""
            Name: Notepad
            WindowFilter: "Notepad.exe"
            Shortcuts:
              - SectionName: File
                Properties:
                  - Name: New tab
                    Shortcut:
                    - { Win: false, Ctrl: true, Shift: false, Alt: false, Keys: [ N ] }
            """);

        var library = new[] { common, notepad };
        Assert.Equal("Notepad", AppMatcher.FindForProcess(library, "notepad")?.AppName);
        Assert.Null(AppMatcher.FindForProcess(library, "claude")); // unknown → caller uses fallback
    }

    [Fact]
    public void Fallback_merges_as_its_own_identity()
    {
        AppDefinition common = Load(CommonYaml);
        AppDefinition windows = Load("""
            PackageName: +WindowsNT.Shell
            WindowFilter: "*"
            BackgroundProcess: true
            Shortcuts:
              - SectionName: Essentials
                Properties:
                  - Name: Open File Explorer
                    Shortcut:
                    - { Win: true, Ctrl: false, Shift: false, Alt: false, Keys: [ E ] }
            """);

        var merged = LibraryMerger.Merge(new[]
        {
            (LibraryLayer.Bundled, (IReadOnlyList<AppDefinition>)new[] { common, windows }),
        });

        Assert.Equal(2, merged.Count); // fallback and global stay separate
        Assert.Single(merged, a => a.IsFallback);
        Assert.Single(merged, a => a.IsGlobal && !a.IsFallback);
    }
}
