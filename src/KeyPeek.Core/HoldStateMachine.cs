namespace KeyPeek.Core;

public enum HoldState
{
    /// <summary>No trigger modifier held; waiting.</summary>
    Idle,
    /// <summary>A trigger modifier is down; the hold-delay timer is running. No overlay yet.</summary>
    Arming,
    /// <summary>Trigger modifier(s) still down but the hold was cancelled (a real shortcut
    /// or a mouse action happened). We stay here, doing nothing, until every trigger
    /// modifier is released — this guarantees the overlay never pops up mid-chord.</summary>
    Cancelled,
    /// <summary>Overlay is on screen because trigger modifier(s) are held.</summary>
    Showing,
    /// <summary>Overlay is pinned open (search mode); it survives key release.</summary>
    Pinned,
}

public enum HideReason { TriggerReleased, OtherKeyPressed, MouseActivity, EscPressed, Unpinned, Executed }

/// <summary>
/// Side effects requested by the state machine. Implemented by the app; a fake
/// implementation is used in unit tests.
/// </summary>
public interface IHoldActions
{
    /// <summary>Arrange for <see cref="HoldStateMachine.OnTimerFired"/> to be called after
    /// <paramref name="delayMs"/> with the same generation number. Restarting with a new
    /// generation invalidates older timers.</summary>
    void StartHoldTimer(int generation, int delayMs);

    /// <summary>Capture the foreground app and show the overlay, pre-filtered to the held
    /// modifiers. Called only when a clean, uninterrupted hold completed.</summary>
    void ShowOverlay(Modifiers held);

    void HideOverlay(HideReason reason);

    /// <summary>Held modifiers changed while the overlay is visible.</summary>
    void UpdateFilter(Modifiers held);

    /// <summary>A trigger went down while the panel was pinned by a click: it goes back to
    /// following the keyboard, filtered by <paramref name="held"/>, and will close when the
    /// keys are released.</summary>
    void UnpinToHold(Modifiers held);
}

/// <summary>
/// The hold-to-reveal state machine. Pure logic: no timers, no Win32, no threads —
/// the caller feeds it events and it calls back through <see cref="IHoldActions"/>.
/// All calls must come from a single thread.
///
/// Any modifier in the trigger mask (default Ctrl|Win|Alt, configurable) starts a hold
/// when pressed from Idle; the overlay opens filtered to whatever modifiers are held.
///
/// Cancellation rules (requirement R2 — never interfere with normal typing):
///  - Any non-modifier key, mouse button, or wheel during Arming cancels the hold.
///  - Additional modifier keys do NOT cancel — they refine the filter.
///  - Once cancelled, nothing shows until every trigger modifier is released.
///  - A hold that starts while a mouse button is already down (Ctrl+drag) never arms.
/// </summary>
public sealed class HoldStateMachine
{
    private readonly IHoldActions _actions;
    private int _delayMs;
    private IReadOnlyDictionary<Modifiers, int>? _delayOverrides;
    private int _generation;
    private Modifiers _triggerMask;

    public HoldState State { get; private set; } = HoldState.Idle;
    public Modifiers HeldModifiers { get; private set; }

    public HoldStateMachine(IHoldActions actions, int delayMs, Modifiers triggerMask)
    {
        _actions = actions;
        _delayMs = delayMs;
        _triggerMask = triggerMask;
    }

    public void SetDelay(int delayMs) => _delayMs = delayMs;
    /// <summary>Optional per-modifier hold durations (e.g. a longer delay for Alt).</summary>
    public void SetDelayOverrides(IReadOnlyDictionary<Modifiers, int>? overrides) => _delayOverrides = overrides;
    public void SetTriggerMask(Modifiers mask) => _triggerMask = mask;

    private int DelayFor(Modifiers mod) =>
        _delayOverrides is not null && _delayOverrides.TryGetValue(mod, out int ms) ? ms : _delayMs;

    private bool AnyTriggerHeld => (HeldModifiers & _triggerMask) != Modifiers.None;

    /// <summary>A modifier key became physically held.</summary>
    public void OnModifierDown(Modifiers mod, bool mouseButtonsDown)
    {
        HeldModifiers |= mod;
        switch (State)
        {
            case HoldState.Idle when (mod & _triggerMask) != Modifiers.None:
                if (mouseButtonsDown)
                {
                    // Ctrl pressed mid-drag (Ctrl+drag copy, Ctrl+click-select). Not a hold.
                    State = HoldState.Cancelled;
                    break;
                }
                State = HoldState.Arming;
                _actions.StartHoldTimer(++_generation, DelayFor(mod));
                break;

            case HoldState.Showing:
                _actions.UpdateFilter(HeldModifiers);
                break;

            // Pinned by a click, then a trigger goes down again: hand the panel back to the
            // keyboard. It filters as more modifiers join and closes when they are released,
            // exactly as if it had just been opened — otherwise a pinned panel is frozen and
            // the only way out is the mouse or Esc.
            case HoldState.Pinned when !PinnedForSearch && (mod & _triggerMask) != Modifiers.None:
                State = HoldState.Showing;
                _actions.UnpinToHold(HeldModifiers);
                break;

            // Arming: the extra modifier joins the pending filter; the timer keeps running.
            // Idle + non-trigger modifier (e.g. bare Shift while typing): nothing.
            // Pinned for search: modifiers belong to the search box (Ctrl+A, Shift+arrows).
        }
    }

    public void OnModifierUp(Modifiers mod)
    {
        HeldModifiers &= ~mod;
        switch (State)
        {
            case HoldState.Arming when !AnyTriggerHeld:
                _generation++; // invalidate the pending timer
                State = HoldState.Idle;
                break;

            case HoldState.Cancelled when !AnyTriggerHeld:
                State = HoldState.Idle;
                break;

            case HoldState.Showing:
                if (!AnyTriggerHeld)
                {
                    _actions.HideOverlay(HideReason.TriggerReleased);
                    State = HoldState.Idle;
                }
                else
                {
                    _actions.UpdateFilter(HeldModifiers); // widen the list
                }
                break;

            // Pinned: overlay deliberately survives key release.
        }
    }

    /// <summary>Any key that is neither a modifier nor Esc.</summary>
    public void OnOtherKeyDown()
    {
        switch (State)
        {
            case HoldState.Arming:
                _generation++;
                State = HoldState.Cancelled; // user typed a real chord (Ctrl+C) — stay silent
                break;
            case HoldState.Showing:
                // User executed a real shortcut while reading the overlay. The key was NOT
                // swallowed — the app receives it normally; we just get out of the way.
                _actions.HideOverlay(HideReason.OtherKeyPressed);
                State = HoldState.Cancelled;
                break;
            // Pinned: user is typing in the search box; ignore.
        }
    }

    /// <summary>Mouse button down or wheel outside the overlay.</summary>
    public void OnMouseCancel()
    {
        switch (State)
        {
            case HoldState.Arming:
                _generation++;
                State = HoldState.Cancelled; // Ctrl+click / Ctrl+wheel-zoom — not a hold
                break;
            case HoldState.Showing:
                _actions.HideOverlay(HideReason.MouseActivity);
                State = HoldState.Cancelled;
                break;
            case HoldState.Pinned:
                _actions.HideOverlay(HideReason.MouseActivity); // click outside closes pinned overlay
                State = HoldState.Idle;
                break;
        }
    }

    /// <summary>Esc pressed while the overlay is visible (the hook swallowed it).</summary>
    public void OnEscDown()
    {
        switch (State)
        {
            case HoldState.Showing:
                _actions.HideOverlay(HideReason.EscPressed);
                State = HoldState.Cancelled;
                break;
            case HoldState.Pinned:
                _actions.HideOverlay(HideReason.EscPressed);
                State = HoldState.Idle;
                break;
        }
    }

    public void OnTimerFired(int generation)
    {
        if (State == HoldState.Arming && generation == _generation)
        {
            State = HoldState.Showing;
            _actions.ShowOverlay(HeldModifiers);
        }
    }

    /// <summary>User clicked the search box: keep the overlay open past key release.</summary>
    /// <summary>True when the pin came from clicking the search box: the panel then owns the
    /// keyboard for typing, so a modifier press must not steal it back.</summary>
    public bool PinnedForSearch { get; private set; }

    public void OnPinRequested(bool forSearch)
    {
        PinnedForSearch = forSearch;
        OnPinRequested();
    }

    public void OnPinRequested()
    {
        if (State == HoldState.Showing)
            State = HoldState.Pinned;
    }

    public void OnUnpinRequested()
    {
        if (State == HoldState.Pinned)
        {
            _actions.HideOverlay(HideReason.Unpinned);
            State = HoldState.Idle;
        }
    }

    /// <summary>User clicked a shortcut row: hide first, the caller injects the chord after.</summary>
    public void OnExecuteRequested()
    {
        switch (State)
        {
            case HoldState.Showing:
                _actions.HideOverlay(HideReason.Executed);
                State = HoldState.Cancelled; // trigger keys may still be physically held
                break;
            case HoldState.Pinned:
                _actions.HideOverlay(HideReason.Executed);
                State = HoldState.Idle;
                break;
        }
    }
}
