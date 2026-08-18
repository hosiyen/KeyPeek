using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class ExplorePolicyTests
{
    /// <summary>
    /// THE R2 TEST. With Explore mode off, no key in any state may be swallowed — that is
    /// what makes the default configuration's input path identical to a build without the
    /// feature. If this ever fails, KeyPeek is interfering with someone's typing.
    /// </summary>
    [Fact]
    public void With_explore_disabled_nothing_is_ever_swallowed()
    {
        for (int vk = 0; vk < 256; vk++)
        {
            Assert.False(ExplorePolicy.ShouldSwallow(vk, overlayVisible: true, exploreEnabled: false),
                $"vk 0x{vk:X2} swallowed while Explore is off (overlay visible)");
            Assert.False(ExplorePolicy.ShouldSwallow(vk, overlayVisible: false, exploreEnabled: false),
                $"vk 0x{vk:X2} swallowed while Explore is off (overlay hidden)");
        }
    }

    [Fact]
    public void With_explore_enabled_nothing_is_swallowed_unless_the_panel_is_visible()
    {
        for (int vk = 0; vk < 256; vk++)
            Assert.False(ExplorePolicy.ShouldSwallow(vk, overlayVisible: false, exploreEnabled: true),
                $"vk 0x{vk:X2} swallowed with no panel on screen");
    }

    [Fact]
    public void The_swallow_set_is_exactly_the_nine_navigation_keys()
    {
        int[] expected =
        {
            0x0D, // Enter
            0x21, 0x22, // PageUp, PageDown
            0x23, 0x24, // End, Home
            0x25, 0x26, 0x27, 0x28, // Left, Up, Right, Down
        };

        var swallowed = new List<int>();
        for (int vk = 0; vk < 256; vk++)
            if (ExplorePolicy.ShouldSwallow(vk, overlayVisible: true, exploreEnabled: true))
                swallowed.Add(vk);

        Assert.Equal(expected, swallowed);
    }

    [Theory]
    [InlineData(0x41)] // A — typing must always pass through
    [InlineData(0x20)] // Space
    [InlineData(0x08)] // Backspace
    [InlineData(0x09)] // Tab
    [InlineData(0x1B)] // Esc — owned by the separate InterceptEsc path, not by Explore
    [InlineData(0xA2)] // Ctrl
    public void Keys_outside_the_navigation_set_are_never_swallowed(int vk) =>
        Assert.False(ExplorePolicy.ShouldSwallow(vk, overlayVisible: true, exploreEnabled: true));
}

public class ExploreSelectionTests
{
    // Two cards: 3 entries starting at 0, 4 entries starting at 3.
    private static ExploreSelection Sample() => new(7, new[] { 0, 3 });

    [Fact]
    public void Starts_on_the_first_entry()
    {
        ExploreSelection s = Sample();
        Assert.True(s.HasSelection);
        Assert.Equal(0, s.Index);
    }

    [Fact]
    public void An_empty_panel_has_no_selection_and_ignores_every_key()
    {
        var s = new ExploreSelection(0);
        Assert.False(s.HasSelection);
        Assert.False(s.Apply(ExplorePolicy.VkDown));
        Assert.False(s.Apply(ExplorePolicy.VkEnd));
    }

    [Fact]
    public void Down_and_up_step_one_entry_and_clamp_at_the_ends()
    {
        ExploreSelection s = Sample();

        Assert.True(s.Apply(ExplorePolicy.VkDown));
        Assert.Equal(1, s.Index);
        Assert.True(s.Apply(ExplorePolicy.VkUp));
        Assert.Equal(0, s.Index);

        Assert.False(s.Apply(ExplorePolicy.VkUp)); // already at the top: no move
        Assert.Equal(0, s.Index);

        for (int i = 0; i < 20; i++)
            s.Apply(ExplorePolicy.VkDown);
        Assert.Equal(6, s.Index); // clamped at the last entry, no wrap
    }

    [Fact]
    public void Home_and_end_jump_to_the_ends()
    {
        ExploreSelection s = Sample();
        Assert.True(s.Apply(ExplorePolicy.VkEnd));
        Assert.Equal(6, s.Index);
        Assert.True(s.Apply(ExplorePolicy.VkHome));
        Assert.Equal(0, s.Index);
    }

    [Fact]
    public void Page_keys_move_a_screenful_and_clamp()
    {
        ExploreSelection s = Sample();
        Assert.True(s.Apply(ExplorePolicy.VkPageDown));
        Assert.Equal(6, s.Index); // 0 + 10 clamped to the last entry
        Assert.True(s.Apply(ExplorePolicy.VkPageUp));
        Assert.Equal(0, s.Index);
    }

    [Fact]
    public void Right_moves_to_the_next_card_and_wraps()
    {
        ExploreSelection s = Sample();
        Assert.True(s.Apply(ExplorePolicy.VkRight));
        Assert.Equal(3, s.Index); // first entry of card 2
        Assert.True(s.Apply(ExplorePolicy.VkRight));
        Assert.Equal(0, s.Index); // wrapped back to card 1
    }

    [Fact]
    public void Left_snaps_to_the_top_of_the_current_card_before_leaving_it()
    {
        ExploreSelection s = Sample();
        s.Apply(ExplorePolicy.VkEnd); // index 6, inside card 2
        Assert.True(s.Apply(ExplorePolicy.VkLeft));
        Assert.Equal(3, s.Index); // top of card 2
        Assert.True(s.Apply(ExplorePolicy.VkLeft));
        Assert.Equal(0, s.Index); // now card 1
    }

    [Fact]
    public void Enter_is_not_a_movement_key()
    {
        ExploreSelection s = Sample();
        Assert.False(s.Apply(ExplorePolicy.VkEnter));
        Assert.Equal(0, s.Index);
    }

    [Fact]
    public void Card_starts_outside_the_entry_range_are_ignored()
    {
        var s = new ExploreSelection(2, new[] { 0, 5, -1 });
        // Only card 0 survives the filter, so a card jump lands where it already is:
        // no movement, hence no redraw.
        Assert.False(s.Apply(ExplorePolicy.VkRight));
        Assert.Equal(0, s.Index);
    }
}
