using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

/// <summary>
/// One test per defect found by the 2026-08-14 adversarial review. Each names the user-
/// visible failure it prevents, because that is the thing worth keeping true.
/// </summary>
public class ReviewRegressionTests
{
    private static AppDefinition Load(string yaml, out List<LibraryError> errors)
    {
        errors = new List<LibraryError>();
        AppDefinition? app = PowerToysManifestLoader.LoadText(yaml, "x.yml", errors);
        Assert.NotNull(app);
        return app!;
    }

    [Fact]
    public void An_empty_manifest_loads_instead_of_vanishing()
    {
        // "Add app" and the shortcut editor both write the file before it has any content.
        // Rejecting it made the new app disappear from the library with only a log line.
        AppDefinition app = Load("""
            Name: Foobar
            WindowFilter: foobar.exe
            Shortcuts:
              - SectionName: General
                Properties: []
            """, out List<LibraryError> errors);

        Assert.Empty(errors);
        Assert.Equal("Foobar", app.AppName);
        Assert.Equal(0, app.ShortcutCount);
    }

    [Fact]
    public void A_manifest_whose_entries_are_all_broken_is_still_an_error()
    {
        var errors = new List<LibraryError>();
        AppDefinition? app = PowerToysManifestLoader.LoadText("""
            Name: Foobar
            WindowFilter: foobar.exe
            Shortcuts:
              - SectionName: General
                Properties:
                  - Name: Broken
            """, "x.yml", errors);

        Assert.Null(app); // declared a shortcut and produced none: that is a bad file
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void A_fallback_definition_survives_a_save_and_reload()
    {
        // The editor writes user files with Serialize. Losing the Fallback flag produced a
        // file the loader rejected, so every edit to "Common shortcuts" was silently lost.
        AppDefinition original = Load("""
            PackageName: +KeyPeek.CommonApps
            Name: Common shortcuts
            WindowFilter: "*"
            Fallback: true
            Shortcuts:
              - SectionName: Editing
                Properties:
                  - Name: Copy
                    Shortcut:
                    - { Win: false, Ctrl: true, Shift: false, Alt: false, Keys: [ C ] }
            """, out _);
        Assert.True(original.IsFallback);

        AppDefinition reloaded = Load(PowerToysManifestLoader.Serialize(original), out List<LibraryError> errors);

        Assert.Empty(errors);
        Assert.True(reloaded.IsFallback);
        Assert.False(reloaded.IsGlobal); // must not become a system-wide definition either
        Assert.Equal(original.MergeKey, reloaded.MergeKey);
    }

    [Fact]
    public void The_user_file_name_is_defined_for_a_definition_with_no_process()
    {
        // The library pencil used to index ProcessNames[0] directly and threw on the
        // fallback definition, so clicking it did nothing at all.
        AppDefinition fallback = Load("""
            Name: Common shortcuts
            WindowFilter: "*"
            Fallback: true
            Shortcuts:
              - SectionName: Editing
                Properties:
                  - Name: Copy
                    Shortcut:
                    - { Win: false, Ctrl: true, Shift: false, Alt: false, Keys: [ C ] }
            """, out _);

        string name = UserManifest.FileNameFor(fallback);
        Assert.EndsWith(".user.yml", name);
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), name.Contains);
    }

    [Theory]
    // Every navigation key, in both directions, must be answerable without a panel.
    [InlineData(ExplorePolicy.VkUp)]
    [InlineData(ExplorePolicy.VkDown)]
    [InlineData(ExplorePolicy.VkLeft)]
    [InlineData(ExplorePolicy.VkRight)]
    [InlineData(ExplorePolicy.VkHome)]
    [InlineData(ExplorePolicy.VkEnd)]
    [InlineData(ExplorePolicy.VkPageUp)]
    [InlineData(ExplorePolicy.VkPageDown)]
    [InlineData(ExplorePolicy.VkEnter)]
    public void Explore_keys_are_only_swallowed_while_a_panel_is_up(int vk)
    {
        // The hook now refuses to take a key mid-press; the policy still has to agree that
        // nothing is swallowed with no panel on screen, whatever the setting says.
        Assert.False(ExplorePolicy.ShouldSwallow(vk, overlayVisible: false, exploreEnabled: true));
        Assert.True(ExplorePolicy.ShouldSwallow(vk, overlayVisible: true, exploreEnabled: true));
    }

    [Fact]
    public void An_app_that_runs_in_the_background_is_still_just_an_app()
    {
        // Telegram's manifest sets BackgroundProcess: true next to a real WindowFilter.
        // Reading that as "system-wide" published every Telegram shortcut — including
        // "Quit Telegram" — into the system rail of every other app, which is what the
        // user saw while using Edge.
        AppDefinition telegram = Load("""
            PackageName: Telegram.TelegramDesktop
            Name: Telegram Desktop
            WindowFilter: "telegram.exe"
            BackgroundProcess: true
            Shortcuts:
              - SectionName: Chat
                Properties:
                  - Name: Quit Telegram
                    Shortcut:
                    - { Win: false, Ctrl: true, Shift: false, Alt: false, Keys: [ Q ] }
            """, out _);

        Assert.False(telegram.IsGlobal);
        Assert.Equal(new[] { "telegram" }, telegram.ProcessNames);
        Assert.Empty(AppMatcher.Globals(new[] { telegram }));
        Assert.NotNull(AppMatcher.FindForProcess(new[] { telegram }, "telegram"));
    }

    [Fact]
    public void A_star_filter_is_still_the_way_to_say_system_wide()
    {
        AppDefinition shell = Load("""
            PackageName: +WindowsNT.Shell
            WindowFilter: "*"
            Shortcuts:
              - SectionName: Desktop
                Properties:
                  - Name: Open File Explorer
                    Shortcut:
                    - { Win: true, Ctrl: false, Shift: false, Alt: false, Keys: [ E ] }
            """, out _);

        Assert.True(shell.IsGlobal);
        Assert.Single(AppMatcher.Globals(new[] { shell }));
    }

    [Fact]
    public void A_recorded_sequence_survives_a_save_and_reload()
    {
        // "Ctrl+K then Z": the second step has no modifier, so the modifier heuristic read
        // the pair as alternatives — the panel showed only "K" and click-to-run sent only
        // Ctrl+K. The written file now says which it is.
        var entry = new ShortcutEntry
        {
            Chords = new[] { new KeyChord(Modifiers.Ctrl, "K"), new KeyChord(Modifiers.None, "Z") },
            Description = "Toggle Zen Mode",
            RawKeys = "Ctrl+K Z",
            ChordsAreAlternatives = false,
        };
        var app = new AppDefinition
        {
            AppName = "Visual Studio Code",
            ProcessNames = new[] { "code" },
            Sections = new[] { new ShortcutSection("My shortcuts", new[] { entry }) },
            SourceFile = "code.user.yml",
        };

        AppDefinition reloaded = Load(PowerToysManifestLoader.Serialize(app), out List<LibraryError> errors);

        Assert.Empty(errors);
        ShortcutEntry roundTripped = Assert.Single(reloaded.Sections.SelectMany(s => s.Shortcuts));
        Assert.False(roundTripped.ChordsAreAlternatives);
        Assert.Equal(2, roundTripped.Chords.Count);
        Assert.Equal("Ctrl+K Z", roundTripped.KeysText);
    }

    [Fact]
    public void The_selection_starts_on_the_first_row_so_Enter_always_has_a_visible_target()
    {
        var selection = new ExploreSelection(4, new[] { 0, 2 });
        Assert.True(selection.HasSelection);
        Assert.Equal(0, selection.Index);

        // Up/Home at the top report "no movement" — which is why the initial highlight has
        // to be painted when the panel opens, not on the first successful move.
        Assert.False(selection.Apply(ExplorePolicy.VkUp));
        Assert.False(selection.Apply(ExplorePolicy.VkHome));
    }
}
