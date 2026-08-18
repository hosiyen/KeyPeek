using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class DisplaySectionsTests
{
    private static ShortcutEntry Entry(string keys, string description,
        LibraryLayer layer = LibraryLayer.Bundled) => new()
    {
        Chords = KeyChordParser.Parse(keys),
        Description = description,
        RawKeys = keys,
        Layer = layer,
    };

    [Fact]
    public void Table_expansion_collapses_to_one_section_without_duplicate_rows()
    {
        // What ToTables produces for a section containing Ctrl+Alt+Tab: the SAME
        // section name twice (ctrl table + alt table), the entry in both.
        var app = new AppDefinition
        {
            AppName = "Windows",
            IsGlobal = true,
            Sections = new[]
            {
                new ShortcutSection("Desktop Shortcuts", new[]
                {
                    Entry("Ctrl+Alt+Tab", "View open apps"),
                    Entry("Ctrl+Shift+Esc", "Open Task Manager"),
                }, Modifiers.Ctrl),
                new ShortcutSection("Desktop Shortcuts", new[]
                {
                    Entry("Ctrl+Alt+Tab", "View open apps"),
                    Entry("Alt+Tab", "Switch between apps"),
                }, Modifiers.Alt),
            },
            SourceFile = "shell.yml",
        };

        var display = app.DisplaySections();

        var section = Assert.Single(display); // one section per name
        Assert.Equal("Desktop Shortcuts", section.Name);
        Assert.Equal(3, section.Shortcuts.Count); // Ctrl+Alt+Tab only once
        Assert.Single(section.Shortcuts, e => e.KeysText == "Ctrl+Alt+Tab");
    }

    [Fact]
    public void Identical_entry_from_two_layers_keeps_the_winning_layer()
    {
        var app = new AppDefinition
        {
            AppName = "X",
            ProcessNames = new[] { "x" },
            Sections = new[]
            {
                new ShortcutSection("General", new[]
                {
                    Entry("Ctrl+T", "New tab", LibraryLayer.Bundled),
                }),
                new ShortcutSection("General", new[]
                {
                    Entry("Ctrl+T", "New tab", LibraryLayer.Discovered),
                }),
            },
            SourceFile = "x.yml",
        };

        var section = Assert.Single(app.DisplaySections());
        ShortcutEntry entry = Assert.Single(section.Shortcuts);
        Assert.Equal(LibraryLayer.Discovered, entry.Layer);
    }

    [Fact]
    public void Distinct_descriptions_for_the_same_chord_both_remain()
    {
        var app = new AppDefinition
        {
            AppName = "X",
            ProcessNames = new[] { "x" },
            Sections = new[]
            {
                new ShortcutSection("A", new[] { Entry("Ctrl+L", "Focus address bar") }),
                new ShortcutSection("B", new[] { Entry("Ctrl+L", "Create link") }),
            },
            SourceFile = "x.yml",
        };

        Assert.Equal(2, app.DisplaySections().Sum(s => s.Shortcuts.Count));
    }

    [Fact]
    public void Merger_unifies_same_named_sections_across_layers()
    {
        var bundled = new AppDefinition
        {
            AppName = "X",
            ProcessNames = new[] { "x" },
            Sections = new[] { new ShortcutSection("General", new[] { Entry("Ctrl+A", "All") }) },
            SourceFile = "x.yml",
        };
        var user = bundled with
        {
            SourceFile = "my-x.yml",
            Sections = new[] { new ShortcutSection("General", new[] { Entry("Ctrl+B", "Bold") }) },
        };

        var merged = LibraryMerger.Merge(new[]
        {
            (LibraryLayer.Bundled, (IReadOnlyList<AppDefinition>)new[] { bundled }),
            (LibraryLayer.User, (IReadOnlyList<AppDefinition>)new[] { user }),
        });

        var section = Assert.Single(Assert.Single(merged).Sections); // ONE "General"
        Assert.Equal(2, section.Shortcuts.Count);
    }
}
