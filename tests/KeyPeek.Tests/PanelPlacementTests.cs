using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class PanelPlacementTests
{
    // A 1080-tall work area starting at y=0 with a 600-tall panel: 480 px of slack.
    private const int WorkTop = 0, WorkHeight = 1080, PanelHeight = 600, Margin = 24;

    private static int Top(string? position) =>
        PanelPlacement.TopEdge(position, WorkTop, WorkHeight, PanelHeight, Margin);

    [Fact]
    public void Center_keeps_the_placement_KeyPeek_has_always_used()
    {
        Assert.Equal((int)(480 * 0.40), Top(PanelPlacement.Center));
        Assert.Equal(Top(PanelPlacement.Center), Top(null));      // default
        Assert.Equal(Top(PanelPlacement.Center), Top("nonsense")); // unknown value
    }

    [Fact]
    public void Top_and_bottom_sit_one_margin_from_their_edge()
    {
        Assert.Equal(Margin, Top(PanelPlacement.Top));
        Assert.Equal(480 - Margin, Top(PanelPlacement.Bottom));
    }

    [Fact]
    public void Placement_is_relative_to_the_monitors_work_area_not_the_screen()
    {
        // Second monitor above the primary, and a taskbar eating the top of the work area.
        int top = PanelPlacement.TopEdge(PanelPlacement.Top, -1080, WorkHeight, PanelHeight, Margin);
        Assert.Equal(-1080 + Margin, top);
    }

    [Fact]
    public void A_panel_taller_than_the_screen_never_goes_off_the_top()
    {
        foreach (string position in new[] { PanelPlacement.Top, PanelPlacement.Center, PanelPlacement.Bottom })
            Assert.Equal(WorkTop, PanelPlacement.TopEdge(position, WorkTop, 500, 900, Margin));
    }

    [Fact]
    public void Values_are_normalized_case_and_whitespace_insensitively()
    {
        Assert.Equal(PanelPlacement.Top, PanelPlacement.Normalize("  TOP "));
        Assert.Equal(PanelPlacement.Bottom, PanelPlacement.Normalize("Bottom"));
        Assert.Equal(PanelPlacement.Center, PanelPlacement.Normalize(""));
        Assert.Equal(PanelPlacement.Center, PanelPlacement.Normalize(null));
    }
}

public class OnboardingPolicyTests
{
    [Theory]
    [InlineData(false, false, true)]  // fresh install, normal launch → greet
    [InlineData(true, false, false)]  // already greeted → never again
    [InlineData(false, true, false)]  // launched with --settings → they asked for a window
    [InlineData(true, true, false)]
    public void Decision_table(bool shown, bool withSettings, bool expected) =>
        Assert.Equal(expected, OnboardingPolicy.ShouldShowWelcome(shown, withSettings));
}
