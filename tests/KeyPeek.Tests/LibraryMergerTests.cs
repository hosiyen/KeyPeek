using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class LibraryMergerTests
{
    private static ShortcutEntry Entry(string keys, string description, bool recommended = false) => new()
    {
        Chords = KeyChordParser.Parse(keys),
        Description = description,
        Recommended = recommended,
        RawKeys = keys,
    };

    private static AppDefinition Chrome(string sourceFile, params (string Section, ShortcutEntry[] Entries)[] sections) => new()
    {
        AppName = "Google Chrome",
        PackageName = "Google.Chrome",
        ProcessNames = new[] { "chrome" },
        Sections = sections.Select(s => new ShortcutSection(s.Section, s.Entries)).ToList(),
        SourceFile = sourceFile,
    };

    private static IReadOnlyList<AppDefinition> Merge(
        params (LibraryLayer, AppDefinition[])[] layers) =>
        LibraryMerger.Merge(layers.Select(l =>
            ((LibraryLayer)l.Item1, (IReadOnlyList<AppDefinition>)l.Item2)));

    [Fact]
    public void User_override_shadows_exactly_one_entry()
    {
        var bundled = Chrome("chrome.yml",
            ("Tabs", new[]
            {
                Entry("Ctrl+T", "New tab", recommended: true),
                Entry("Ctrl+W", "Close tab"),
                Entry("Ctrl+Shift+T", "Reopen closed tab"),
            }));
        var user = Chrome("my-chrome.yml",
            ("Tabs", new[] { Entry("Ctrl+W", "Close tab (careful!)") }));

        var merged = Merge((LibraryLayer.Bundled, new[] { bundled }), (LibraryLayer.User, new[] { user }));

        var app = Assert.Single(merged);
        var tabs = Assert.Single(app.Sections);
        Assert.Equal(3, tabs.Shortcuts.Count); // nothing orphaned

        ShortcutEntry overridden = tabs.Shortcuts.Single(e => e.KeysText == "Ctrl+W");
        Assert.Equal("Close tab (careful!)", overridden.Description);
        Assert.Equal(LibraryLayer.User, overridden.Layer);
        Assert.True(overridden.OverridesShipped);

        Assert.All(tabs.Shortcuts.Where(e => e.KeysText != "Ctrl+W"), e =>
        {
            Assert.Equal(LibraryLayer.Bundled, e.Layer);
            Assert.False(e.OverridesShipped);
        });
        // position preserved: the override sits where the bundled entry was
        Assert.Equal("Ctrl+W", tabs.Shortcuts[1].KeysText);
    }

    [Fact]
    public void New_user_chords_append_and_new_sections_are_created()
    {
        var bundled = Chrome("chrome.yml", ("Tabs", new[] { Entry("Ctrl+T", "New tab") }));
        var user = Chrome("my-chrome.yml", ("My extras", new[] { Entry("Ctrl+Shift+K", "Do my thing") }));

        var app = Assert.Single(Merge(
            (LibraryLayer.Bundled, new[] { bundled }), (LibraryLayer.User, new[] { user })));

        Assert.Equal(2, app.Sections.Count);
        Assert.Equal("Tabs", app.Sections[0].Name);         // first-seen order
        Assert.Equal("My extras", app.Sections[1].Name);
        Assert.Equal(LibraryLayer.User, app.Sections[1].Shortcuts[0].Layer);
        Assert.False(app.Sections[1].Shortcuts[0].OverridesShipped); // new, not an override
    }

    [Fact]
    public void Layer_order_wins_regardless_of_input_order()
    {
        var bundled = Chrome("chrome.yml", ("Tabs", new[] { Entry("Ctrl+T", "Shipped") }));
        var downloaded = Chrome("chrome.yml", ("Tabs", new[] { Entry("Ctrl+T", "Corrected") }));

        // deliberately fed in the wrong order
        var app = Assert.Single(Merge(
            (LibraryLayer.Downloaded, new[] { downloaded }), (LibraryLayer.Bundled, new[] { bundled })));

        Assert.Equal("Corrected", app.Sections[0].Shortcuts[0].Description);
        Assert.Equal(LibraryLayer.Downloaded, app.Sections[0].Shortcuts[0].Layer);
    }

    [Fact]
    public void Metadata_comes_from_highest_layer_and_process_names_union()
    {
        var bundled = Chrome("chrome.yml", ("Tabs", new[] { Entry("Ctrl+T", "New tab") }))
            with { VerifiedAgainst = "120" };
        var user = (Chrome("my.yml", ("Tabs", new[] { Entry("Ctrl+T", "Mine") }))
            with { VerifiedAgainst = "141", ProcessNames = new[] { "chrome", "chromium" } });

        var app = Assert.Single(Merge(
            (LibraryLayer.Bundled, new[] { bundled }), (LibraryLayer.User, new[] { user })));

        Assert.Equal("141", app.VerifiedAgainst);
        Assert.Equal(new[] { "chrome", "chromium" }, app.ProcessNames.OrderBy(p => p));
    }

    [Fact]
    public void Different_apps_do_not_merge()
    {
        var chrome = Chrome("chrome.yml", ("Tabs", new[] { Entry("Ctrl+T", "New tab") }));
        var figma = chrome with { AppName = "Figma", PackageName = "Figma.Figma", ProcessNames = new[] { "figma" } };

        Assert.Equal(2, Merge((LibraryLayer.Bundled, new[] { chrome, figma })).Count);
    }

    [Fact]
    public void Global_definitions_merge_into_one_system_bucket()
    {
        var shipped = new AppDefinition
        {
            AppName = "Windows",
            IsGlobal = true,
            Sections = new[] { new ShortcutSection("Essentials", new[] { Entry("Win+E", "Open File Explorer") }) },
            SourceFile = "windows.yml",
        };
        var userGlobal = shipped with
        {
            SourceFile = "my-global.yml",
            Sections = new[] { new ShortcutSection("Essentials", new[] { Entry("Win+E", "Explorer (mine)") }) },
        };

        var app = Assert.Single(Merge(
            (LibraryLayer.Bundled, new[] { shipped }), (LibraryLayer.User, new[] { userGlobal })));
        Assert.True(app.IsGlobal);
        Assert.Equal("Explorer (mine)", app.Sections[0].Shortcuts[0].Description);
    }
}
