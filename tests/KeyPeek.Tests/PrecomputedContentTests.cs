using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class PrecomputedContentTests
{
    private static readonly IntPtr Hwnd = new(0x1234);

    private static PrecomputedContent Warm() =>
        new(Hwnd, "notepad", "Untitled - Notepad", Modifiers.Ctrl);

    [Fact]
    public void Matches_the_window_and_filter_it_was_built_for()
    {
        Assert.True(Warm().Matches(Hwnd, "notepad", "Untitled - Notepad", Modifiers.Ctrl));
        Assert.True(Warm().Matches(Hwnd, "NOTEPAD", "Untitled - Notepad", Modifiers.Ctrl)); // process name casing
    }

    [Fact]
    public void A_different_window_of_the_same_app_is_a_miss()
    {
        // Two Notepad windows have different shortcuts only in theory, but the icon and the
        // title in the header come from the captured window — serving the other one's
        // content would show the wrong title.
        Assert.False(Warm().Matches(new IntPtr(0x9999), "notepad", "Untitled - Notepad", Modifiers.Ctrl));
    }

    [Fact]
    public void A_changed_title_is_a_miss_because_definitions_can_be_title_matched()
    {
        // Switching document/tab changes the title without any foreground-change event, and
        // a definition selected by TitleRegex may no longer apply.
        Assert.False(Warm().Matches(Hwnd, "notepad", "Report.txt - Notepad", Modifiers.Ctrl));
        Assert.False(Warm().Matches(Hwnd, "notepad", null, Modifiers.Ctrl));
    }

    [Fact]
    public void A_different_held_modifier_is_a_miss()
    {
        // Each modifier filters the table differently, so the warmed rows are the wrong ones.
        Assert.False(Warm().Matches(Hwnd, "notepad", "Untitled - Notepad", Modifiers.Win));
        Assert.False(Warm().Matches(Hwnd, "notepad", "Untitled - Notepad", Modifiers.Ctrl | Modifiers.Shift));
    }

    [Theory]
    [InlineData("notepad", false, false, true)]
    [InlineData("", false, false, false)]      // the preload placeholder identity
    [InlineData("desktop", true, false, false)] // desktop shows no app zone
    [InlineData("keypeek", false, true, false)] // our own window never gets a panel
    public void Only_real_foreground_apps_are_worth_warming(
        string process, bool isDesktop, bool isSelf, bool expected) =>
        Assert.Equal(expected, PrecomputedContent.IsWarmable(process, isDesktop, isSelf));
}
