using System.Threading.Channels;
using KeyPeek.Core;
using KeyPeek.Hooks;
using KeyPeek.UI;
using static KeyPeek.Interop.NativeMethods;

namespace KeyPeek.Services;

/// <summary>
/// Consumes raw input events from the hook thread (via a channel) and drives the
/// <see cref="HoldStateMachine"/>. Everything runs on one long-lived consumer thread,
/// so the state machine needs no locks and the hook callback never waits on us.
/// </summary>
internal sealed class HoldController : IHoldActions
{
    /// <summary>Injected "silent" key (VK 0xFF): pressing it between a Win/Alt down and
    /// its release stops Windows from treating the hold as a lone tap (Start menu /
    /// menu-bar focus). Same masking trick PowerToys and AutoHotkey use.</summary>
    /// <summary>True from the moment a trigger key goes down until every trigger is
    /// released. Written by the consumer thread, read by the UI thread — the panel's
    /// warm-up checks it so a ~300 ms layout burst never lands between the user's press and
    /// the panel they are waiting for.</summary>
    public static volatile bool HoldInProgress;

    private const int MaskVk = 0xFF;

    private readonly Channel<InputEvent> _channel;
    private readonly IOverlayPresenter _presenter;
    private readonly ForegroundAppService _foreground;
    private readonly SettingsService _settings;
    private readonly Logger _log;
    private readonly HoldStateMachine _machine;

    // vk-level bookkeeping. Two physical keys (left/right Ctrl) share one modifier flag,
    // so we count keys per flag and only tell the machine about 0↔1 transitions.
    private readonly HashSet<int> _downVks = new();
    private readonly int[] _modKeyCounts = new int[4]; // Ctrl, Shift, Alt, Win

    private readonly Timer _timer;
    private volatile int _pendingGeneration;

    // Click-to-execute handoff (UI thread writes, consumer thread reads).
    private readonly object _executeLock = new();
    private IReadOnlyList<KeyChord>? _pendingExecute;
    private ForegroundApp? _lastShown; // consumer thread only

    /// <summary>Raised when a clicked shortcut can't be delivered because the target
    /// window is elevated (UIPI). Payload: process name.</summary>
    public event Action<string>? ExecutionBlocked;

    public HoldController(Channel<InputEvent> channel, IOverlayPresenter presenter,
        ForegroundAppService foreground, SettingsService settings, Logger log)
    {
        _channel = channel;
        _presenter = presenter;
        _foreground = foreground;
        _settings = settings;
        _log = log;
        _machine = new HoldStateMachine(this, settings.Current.HoldDelayMs, settings.TriggerMask);
        _timer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
        settings.Changed += () =>
        {
            _machine.SetDelay(settings.Current.HoldDelayMs);
            _machine.SetTriggerMask(settings.TriggerMask);
        };
    }

    public void Start() =>
        Task.Factory.StartNew(RunAsync, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // Called from the UI thread; routed through the channel so all state changes still
    // happen on the consumer thread.
    /// <summary>Written by the UI thread, read by the consumer when the pin event arrives.
    /// A search pin keeps the keyboard; a click pin hands it back on the next trigger.</summary>
    private volatile bool _pinForSearch;

    public void RequestPin(bool forSearch)
    {
        _pinForSearch = forSearch;
        _channel.Writer.TryWrite(InputEvent.Pin());
    }

    public void RequestUnpin() => _channel.Writer.TryWrite(InputEvent.Unpin());

    /// <summary>User clicked a shortcut row. Hide the overlay, then inject the chord into
    /// the app that has focus.</summary>
    public void RequestExecute(IReadOnlyList<KeyChord> chords)
    {
        lock (_executeLock)
            _pendingExecute = chords;
        _channel.Writer.TryWrite(InputEvent.Execute());
    }

    private async Task RunAsync()
    {
        await foreach (InputEvent e in _channel.Reader.ReadAllAsync())
        {
            try
            {
                Handle(e);
            }
            catch (Exception ex)
            {
                _log.Error($"Input event handling failed: {ex}");
            }
        }
    }

    private void Handle(InputEvent e)
    {
        switch (e.Kind)
        {
            case InputEventKind.Key:
                HandleKey(e.Value, e.IsDown);
                break;

            case InputEventKind.MouseButtonDown:
            case InputEventKind.MouseWheel:
                // Clicks/scrolls inside the visible overlay are interaction, not dismissal.
                if (!_presenter.IsPointInsideOverlay(e.X, e.Y))
                    _machine.OnMouseCancel();
                break;

            case InputEventKind.TimerFired:
                _machine.OnTimerFired(e.Value);
                break;

            case InputEventKind.PinRequested:
                _machine.OnPinRequested(_pinForSearch);
                break;

            case InputEventKind.UnpinRequested:
                _machine.OnUnpinRequested();
                break;

            case InputEventKind.ExecuteRequested:
                HandleExecute();
                break;
        }
    }

    private void HandleExecute()
    {
        IReadOnlyList<KeyChord>? chords;
        lock (_executeLock)
        {
            chords = _pendingExecute;
            _pendingExecute = null;
        }
        if (chords is null)
            return;

        ForegroundApp? target = _lastShown;
        bool wasPinned = _machine.State == HoldState.Pinned;
        _machine.OnExecuteRequested(); // hides the overlay (and restores focus if pinned)

        if (target is null)
            return;
        if (target.IsElevated)
        {
            // Windows (UIPI) silently drops synthetic input aimed at an elevated window;
            // tell the user plainly instead of failing silently.
            _log.Warn($"Click-to-run refused: \"{target.ProcessName}\" is elevated");
            ExecutionBlocked?.Invoke(target.ProcessName);
            return;
        }

        int delay = wasPinned ? 250 : 90;
        Task.Delay(delay).ContinueWith(_ =>
        {
            // Send to the window captured at show time, not whatever is foreground now.
            if (target.Hwnd != IntPtr.Zero)
            {
                SetForegroundWindow(target.Hwnd);
                Thread.Sleep(60);
            }
            ShortcutExecutor.Execute(chords, _log);
        });
    }

    private void HandleKey(int vk, bool isDown)
    {
        if (vk == MaskVk)
            return; // our own masking key — never feed it back into the state machine

        // Explore mode: navigation keys drive the panel. This runs BEFORE the auto-repeat
        // dedupe below on purpose — holding an arrow should scroll the selection like any
        // list UI, and the hook consumes every repeat, so the app never sees them either.
        if (KeyboardMouseHookService.InterceptExplore && ExplorePolicy.IsExploreKey(vk))
        {
            if (isDown)
                _presenter.ExploreKey(vk);
            return;
        }

        // Held keys auto-repeat their key-down; the machine wants only real transitions.
        if (isDown && !_downVks.Add(vk))
            return;
        if (!isDown)
            _downVks.Remove(vk);

        if (vk == VK_ESCAPE)
        {
            if (!isDown)
                return;
            // If the hook swallowed this Esc (overlay visible), it dismisses the overlay;
            // otherwise it's just a key like any other and cancels a pending hold.
            if (KeyboardMouseHookService.InterceptEsc)
                _machine.OnEscDown();
            else
                _machine.OnOtherKeyDown();
            return;
        }

        Modifiers mod = VkToModifier(vk);
        if (mod == Modifiers.None)
        {
            if (isDown)
                _machine.OnOtherKeyDown();
            return;
        }

        int idx = ModIndex(mod);
        if (isDown)
        {
            HoldInProgress = true; // set before the machine runs: the timer starts here
            if (++_modKeyCounts[idx] != 1)
                return; // second key of the pair (e.g. right Ctrl while left Ctrl held)
            _machine.OnModifierDown(mod, AnyMouseButtonPhysicallyDown());

            // Win/Alt joining while the overlay is up: mask immediately, so releasing
            // them later can't pop the Start menu / focus the app's menu bar.
            if (_machine.State == HoldState.Showing && (mod & (Modifiers.Win | Modifiers.Alt)) != 0)
                SendMaskKey();
        }
        else
        {
            if (_modKeyCounts[idx] == 0)
                return; // release of a key that was already down before we hooked
            if (--_modKeyCounts[idx] != 0)
                return;
            _machine.OnModifierUp(mod);
            if (_modKeyCounts.All(count => count == 0))
                HoldInProgress = false;
        }
    }

    private static Modifiers VkToModifier(int vk) => vk switch
    {
        0xA0 or 0xA1 => Modifiers.Shift,
        0xA2 or 0xA3 => Modifiers.Ctrl,
        0xA4 or 0xA5 => Modifiers.Alt,
        0x5B or 0x5C => Modifiers.Win,
        _ => Modifiers.None,
    };

    private static int ModIndex(Modifiers mod) => mod switch
    {
        Modifiers.Ctrl => 0,
        Modifiers.Shift => 1,
        Modifiers.Alt => 2,
        _ => 3,
    };

    /// <summary>Physical state straight from the OS — more reliable than counting hook
    /// events we might have missed while starting up.</summary>
    private static bool AnyMouseButtonPhysicallyDown() =>
        (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_XBUTTON1) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_XBUTTON2) & 0x8000) != 0;

    private static void SendMaskKey()
    {
        var inputs = new INPUT[]
        {
            new() { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = MaskVk } } },
            new() { type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = MaskVk, dwFlags = KEYEVENTF_KEYUP } } },
        };
        SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
    }

    // ---- IHoldActions (called by the state machine, on the consumer thread) ----

    public void StartHoldTimer(int generation, int delayMs)
    {
        _pendingGeneration = generation;
        _timer.Change(delayMs, Timeout.Infinite);
    }

    private void OnTimerElapsed(object? _) =>
        _channel.Writer.TryWrite(InputEvent.Timer(_pendingGeneration));

    public void ShowOverlay(Modifiers held)
    {
        // Latency budget starts here (the hold timer just fired). The quality bar is
        // "visible in well under 100 ms"; the presenter logs the measured value.
        OverlayTiming.ShowRequestedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        // Capture the foreground window BEFORE any overlay exists: showing a window can
        // change what "foreground" means, so the order here is load-bearing (R3).
        ForegroundApp app = _foreground.Capture();

        if (app.IsSelf)
            return; // the user is already looking at KeyPeek's own window
        if (_settings.IsExcluded(app.ProcessName))
            return;
        if (app.IsFullscreen && !_settings.Current.ShowOverFullscreen)
            return;

        _lastShown = app;

        // A Win/Alt hold reached the overlay: mask now so the eventual key release
        // doesn't open the Start menu or focus the app's menu bar.
        if ((held & (Modifiers.Win | Modifiers.Alt)) != 0)
            SendMaskKey();

        _log.Info($"Hold detected → {app.ProcessName}{(app.ExePath is null ? "" : $" ({app.ExePath})")}");
        _presenter.Show(app, held);
    }

    public void HideOverlay(HideReason reason) => _presenter.Hide(reason);

    public void UpdateFilter(Modifiers held) => _presenter.UpdateFilter(held);

    public void UnpinToHold(Modifiers held) => _presenter.UnpinToHold(held);
}
