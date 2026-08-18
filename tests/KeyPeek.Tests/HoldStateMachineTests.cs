using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class HoldStateMachineTests
{
    private sealed class FakeActions : IHoldActions
    {
        public readonly List<string> Log = new();
        public int LastGeneration = -1;

        public void StartHoldTimer(int generation, int delayMs) { LastGeneration = generation; Log.Add($"timer:{delayMs}"); }
        public void ShowOverlay(Modifiers held) => Log.Add($"show:{held}");
        public void HideOverlay(HideReason reason) => Log.Add($"hide:{reason}");
        public void UpdateFilter(Modifiers held) => Log.Add($"filter:{held}");
        public void UnpinToHold(Modifiers held) => Log.Add($"unpin:{held}");
    }

    private const Modifiers Triggers = Modifiers.Ctrl | Modifiers.Win | Modifiers.Alt;
    private readonly FakeActions _actions = new();
    private readonly HoldStateMachine _machine;

    public HoldStateMachineTests()
    {
        _machine = new HoldStateMachine(_actions, 400, Triggers);
    }

    private void HoldAndFire(Modifiers mod)
    {
        _machine.OnModifierDown(mod, mouseButtonsDown: false);
        _machine.OnTimerFired(_actions.LastGeneration);
    }

    [Fact]
    public void A_click_pinned_panel_returns_to_the_keyboard_when_a_trigger_is_held()
    {
        // The real sequence: the panel is up because a trigger is held, the user clicks it,
        // and only then lets go — a release before the click would have closed it.
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnPinRequested(forSearch: false);
        _machine.OnModifierUp(Modifiers.Ctrl);
        Assert.Equal(HoldState.Pinned, _machine.State);

        _machine.OnModifierDown(Modifiers.Alt, mouseButtonsDown: false);

        // Following the keyboard again: filtered by Alt, and it closes on release rather
        // than staying pinned — the pin was a way to keep reading, not a mode to get stuck in.
        Assert.Equal(HoldState.Showing, _machine.State);
        Assert.Contains("unpin:Alt", _actions.Log);

        _machine.OnModifierUp(Modifiers.Alt);
        Assert.Equal(HoldState.Idle, _machine.State);
        Assert.Contains("hide:TriggerReleased", _actions.Log);
    }

    [Fact]
    public void A_search_pinned_panel_keeps_the_keyboard()
    {
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnPinRequested(forSearch: true);
        _machine.OnModifierUp(Modifiers.Ctrl);

        // Ctrl+A in the search box must select its text, not tear the panel out of search.
        _machine.OnModifierDown(Modifiers.Ctrl, mouseButtonsDown: false);

        Assert.Equal(HoldState.Pinned, _machine.State);
        Assert.DoesNotContain(_actions.Log, entry => entry.StartsWith("unpin:"));
    }

    [Theory]
    [InlineData(Modifiers.Ctrl)]
    [InlineData(Modifiers.Win)]
    [InlineData(Modifiers.Alt)]
    public void Any_trigger_modifier_opens_the_overlay(Modifiers trigger)
    {
        HoldAndFire(trigger);
        Assert.Equal(HoldState.Showing, _machine.State);
        Assert.Contains($"show:{trigger}", _actions.Log);
    }

    [Fact]
    public void Non_trigger_modifier_alone_does_nothing()
    {
        _machine.OnModifierDown(Modifiers.Shift, false); // Shift not in the trigger mask
        Assert.Equal(HoldState.Idle, _machine.State);
        Assert.Empty(_actions.Log);
    }

    [Fact]
    public void Release_before_timer_shows_nothing()
    {
        _machine.OnModifierDown(Modifiers.Ctrl, false);
        int gen = _actions.LastGeneration;
        _machine.OnModifierUp(Modifiers.Ctrl);
        _machine.OnTimerFired(gen); // stale timer arrives late
        Assert.Equal(HoldState.Idle, _machine.State);
        Assert.DoesNotContain(_actions.Log, l => l.StartsWith("show"));
    }

    [Fact]
    public void Other_key_during_arming_cancels_until_all_triggers_released()
    {
        _machine.OnModifierDown(Modifiers.Ctrl, false);
        int gen = _actions.LastGeneration;
        _machine.OnOtherKeyDown();                 // user typed Ctrl+C
        Assert.Equal(HoldState.Cancelled, _machine.State);
        _machine.OnTimerFired(gen);                // timer must be ignored
        Assert.Equal(HoldState.Cancelled, _machine.State);
        _machine.OnOtherKeyDown();                 // more typing while held: still nothing
        Assert.DoesNotContain(_actions.Log, l => l.StartsWith("show"));
        _machine.OnModifierUp(Modifiers.Ctrl);
        Assert.Equal(HoldState.Idle, _machine.State);
    }

    [Fact]
    public void Mouse_click_during_arming_cancels()
    {
        _machine.OnModifierDown(Modifiers.Ctrl, false);
        _machine.OnMouseCancel();
        _machine.OnTimerFired(_actions.LastGeneration);
        Assert.Equal(HoldState.Cancelled, _machine.State);
        Assert.DoesNotContain(_actions.Log, l => l.StartsWith("show"));
    }

    [Fact]
    public void Hold_starting_during_drag_never_arms()
    {
        _machine.OnModifierDown(Modifiers.Ctrl, mouseButtonsDown: true); // Ctrl during drag
        Assert.Equal(HoldState.Cancelled, _machine.State);
        Assert.DoesNotContain(_actions.Log, l => l.StartsWith("timer"));
    }

    [Fact]
    public void Extra_modifier_during_arming_joins_the_filter()
    {
        _machine.OnModifierDown(Modifiers.Ctrl, false);
        _machine.OnModifierDown(Modifiers.Shift, false);
        _machine.OnTimerFired(_actions.LastGeneration);
        Assert.Equal(HoldState.Showing, _machine.State);
        Assert.Contains("show:Ctrl, Shift", _actions.Log);
    }

    [Fact]
    public void Modifiers_update_filter_while_showing_in_both_directions()
    {
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnModifierDown(Modifiers.Shift, false);
        Assert.Contains("filter:Ctrl, Shift", _actions.Log);
        _machine.OnModifierUp(Modifiers.Shift);
        Assert.Equal("filter:Ctrl", _actions.Log[^1]);
    }

    [Fact]
    public void Switching_held_trigger_keeps_overlay_open_and_refilters()
    {
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnModifierDown(Modifiers.Win, false);  // now Ctrl+Win
        _machine.OnModifierUp(Modifiers.Ctrl);          // Win remains — still a trigger
        Assert.Equal(HoldState.Showing, _machine.State);
        Assert.Equal("filter:Win", _actions.Log[^1]);
    }

    [Fact]
    public void Releasing_last_trigger_hides_overlay()
    {
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnModifierUp(Modifiers.Ctrl);
        Assert.Equal(HoldState.Idle, _machine.State);
        Assert.Contains($"hide:{HideReason.TriggerReleased}", _actions.Log);
    }

    [Fact]
    public void Non_trigger_modifier_still_held_does_not_keep_overlay_open()
    {
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnModifierDown(Modifiers.Shift, false);
        _machine.OnModifierUp(Modifiers.Ctrl); // only Shift (non-trigger) remains
        Assert.Equal(HoldState.Idle, _machine.State);
        Assert.Contains($"hide:{HideReason.TriggerReleased}", _actions.Log);
    }

    [Fact]
    public void Esc_hides_and_stays_hidden_while_trigger_held()
    {
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnEscDown();
        Assert.Equal(HoldState.Cancelled, _machine.State);
        Assert.Contains($"hide:{HideReason.EscPressed}", _actions.Log);
    }

    [Fact]
    public void Real_shortcut_while_showing_hides_and_cancels()
    {
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnOtherKeyDown(); // e.g. Ctrl+C executed in the app
        Assert.Equal(HoldState.Cancelled, _machine.State);
        Assert.Contains($"hide:{HideReason.OtherKeyPressed}", _actions.Log);
    }

    [Fact]
    public void Stale_timer_generation_is_ignored()
    {
        _machine.OnModifierDown(Modifiers.Ctrl, false);
        int firstGen = _actions.LastGeneration;
        _machine.OnModifierUp(Modifiers.Ctrl);
        _machine.OnModifierDown(Modifiers.Ctrl, false); // second, fresh hold
        _machine.OnTimerFired(firstGen);                // late timer from the first hold
        Assert.Equal(HoldState.Arming, _machine.State);
    }

    [Fact]
    public void Pin_survives_key_release_and_unpin_hides()
    {
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnPinRequested();
        _machine.OnModifierUp(Modifiers.Ctrl);
        Assert.Equal(HoldState.Pinned, _machine.State);
        Assert.DoesNotContain(_actions.Log, l => l.StartsWith("hide"));
        _machine.OnUnpinRequested();
        Assert.Equal(HoldState.Idle, _machine.State);
        Assert.Contains($"hide:{HideReason.Unpinned}", _actions.Log);
    }

    [Fact]
    public void Typing_while_pinned_does_not_hide()
    {
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnPinRequested();
        _machine.OnOtherKeyDown(); // typing into the search box
        Assert.Equal(HoldState.Pinned, _machine.State);
    }

    [Fact]
    public void Click_outside_while_pinned_hides()
    {
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnPinRequested();
        _machine.OnMouseCancel();
        Assert.Equal(HoldState.Idle, _machine.State);
    }

    [Fact]
    public void Execute_while_showing_hides_then_cancels()
    {
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnExecuteRequested();
        Assert.Equal(HoldState.Cancelled, _machine.State);
        Assert.Contains($"hide:{HideReason.Executed}", _actions.Log);
        _machine.OnModifierUp(Modifiers.Ctrl);
        Assert.Equal(HoldState.Idle, _machine.State);
    }

    [Fact]
    public void Execute_while_pinned_hides_to_idle()
    {
        HoldAndFire(Modifiers.Ctrl);
        _machine.OnPinRequested();
        _machine.OnExecuteRequested();
        Assert.Equal(HoldState.Idle, _machine.State);
    }
}
