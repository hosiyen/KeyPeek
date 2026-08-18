using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class ShortcutFilterTests
{
    private static ShortcutEntry Entry(string keys, string description, bool recommended = false) => new()
    {
        Chords = KeyChordParser.Parse(keys),
        Description = description,
        Recommended = recommended,
        RawKeys = keys,
    };

    private static readonly ShortcutSection Section = new("General", new[]
    {
        Entry("Ctrl+C", "Copy"),
        Entry("Ctrl+Shift+P", "Command palette", recommended: true),
        Entry("Ctrl+Shift+Alt+K", "Kitchen sink"),
        Entry("Alt+F4", "Close app"),
        Entry("F2", "Rename"),
        Entry("Ctrl+K Ctrl+S", "Keyboard shortcuts editor"),
    });

    private static IReadOnlyList<ShortcutEntry> Apply(Modifiers held, string? query = null) =>
        ShortcutFilter.Apply(new[] { Section }, held, query)
            .SelectMany(s => s.Shortcuts).ToList();

    [Fact]
    public void No_held_modifiers_shows_everything()
    {
        Assert.Equal(6, Apply(Modifiers.None).Count);
    }

    [Fact]
    public void Held_ctrl_filters_to_ctrl_shortcuts()
    {
        var visible = Apply(Modifiers.Ctrl);
        Assert.Equal(4, visible.Count); // Ctrl+C, Ctrl+Shift+P, Ctrl+Shift+Alt+K, Ctrl+K Ctrl+S
        Assert.All(visible, e => Assert.True(e.Chords[0].Mods.HasFlag(Modifiers.Ctrl)));
    }

    [Fact]
    public void Held_alt_filters_to_alt_shortcuts()
    {
        var visible = Apply(Modifiers.Alt);
        Assert.Equal(2, visible.Count); // Ctrl+Shift+Alt+K and Alt+F4
    }

    [Fact]
    public void Adding_shift_narrows_to_ctrl_shift_supersets()
    {
        var visible = Apply(Modifiers.Ctrl | Modifiers.Shift);
        Assert.Equal(2, visible.Count); // Ctrl+Shift+P and Ctrl+Shift+Alt+K
        Assert.All(visible, e => Assert.True(e.Chords[0].Mods.HasFlag(Modifiers.Shift)));
    }

    [Fact]
    public void Adding_alt_narrows_further()
    {
        var visible = Apply(Modifiers.Ctrl | Modifiers.Shift | Modifiers.Alt);
        var entry = Assert.Single(visible);
        Assert.Equal("Kitchen sink", entry.Description);
    }

    [Fact]
    public void Releasing_a_modifier_widens_again()
    {
        Assert.True(Apply(Modifiers.Ctrl).Count > Apply(Modifiers.Ctrl | Modifiers.Shift).Count);
    }

    [Fact]
    public void Sequences_filter_on_first_chord()
    {
        var visible = Apply(Modifiers.Ctrl | Modifiers.Shift);
        Assert.DoesNotContain(visible, e => e.Description == "Keyboard shortcuts editor");
    }

    [Fact]
    public void Search_matches_description()
    {
        var entry = Assert.Single(Apply(Modifiers.Ctrl, "palette"));
        Assert.Equal("Command palette", entry.Description);
    }

    [Fact]
    public void Search_matches_keys_text()
    {
        Assert.Contains(Apply(Modifiers.None, "F4"), e => e.Description == "Close app");
    }

    [Fact]
    public void Search_is_case_insensitive()
    {
        Assert.Single(Apply(Modifiers.Ctrl, "PALETTE"));
    }

    [Fact]
    public void Search_combines_with_modifier_filter()
    {
        // "K" appears in two entries, but only one survives the Ctrl+Shift filter
        var visible = Apply(Modifiers.Ctrl | Modifiers.Shift, "kitchen");
        Assert.Single(visible);
    }

    [Fact]
    public void Recommended_entries_sort_first_stably()
    {
        var visible = Apply(Modifiers.Ctrl);
        Assert.Equal("Command palette", visible[0].Description);
        Assert.Equal("Copy", visible[1].Description); // original order among non-recommended
    }

    [Fact]
    public void Empty_sections_are_dropped()
    {
        var filtered = ShortcutFilter.Apply(new[] { Section }, Modifiers.Ctrl, "no such thing");
        Assert.Empty(filtered);
    }
}
