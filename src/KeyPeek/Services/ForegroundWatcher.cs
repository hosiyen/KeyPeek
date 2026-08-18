using System.Windows.Threading;
using static KeyPeek.Interop.NativeMethods;

namespace KeyPeek.Services;

/// <summary>
/// Tells us when the user switches apps, so the panel for the new app can be built and laid
/// out while nothing is happening. Measured on this machine, a cold first show spends 3 ms
/// resolving the app, 8 ms building view-models and <b>526 ms in WPF layout</b> — that work
/// has to happen before the user asks, or the first hold of every app feels broken.
///
/// This is a hint generator, not a source of truth: the overlay still captures the
/// foreground app itself when the hold fires (that ordering is load-bearing, see
/// HoldController.ShowOverlay). If the hint is stale or missing, the panel is merely slow.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    // EVENT_SYSTEM_FOREGROUND fires in bursts — Alt-Tab, UWP frame hand-offs, transient
    // menus. Settle first, then look at what the foreground actually is.
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly ForegroundAppService _foreground;
    private readonly Action<ForegroundApp> _onSettled;
    private readonly Logger _log;
    private readonly DispatcherTimer _timer;

    // Kept alive for as long as the hook exists: if the GC collected the thunk while the OS
    // still held a pointer to it, the next window switch would crash the process.
    private WinEventProc? _proc;
    private IntPtr _hook;
    private readonly int _installedThreadId = Environment.CurrentManagedThreadId;
    private int _disposed;

    /// <summary>Must be constructed on the UI thread: WINEVENT_OUTOFCONTEXT delivers the
    /// callback through the installing thread's message loop, which the WPF dispatcher
    /// pumps for us.</summary>
    public ForegroundWatcher(ForegroundAppService foreground, Action<ForegroundApp> onSettled,
        Dispatcher dispatcher, Logger log)
    {
        _foreground = foreground;
        _onSettled = onSettled;
        _log = log;

        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = Debounce };
        _timer.Tick += OnSettled;

        _proc = OnForegroundChanged;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        if (_hook == IntPtr.Zero)
            _log.Warn("Foreground watcher not installed — first shows will be slower");

        // Warm whatever is already focused. The hook only reports CHANGES, so without this
        // the app the user is in when KeyPeek starts is the one app that stays cold — and
        // that is exactly the app they are most likely to press a trigger in.
        _timer.Start();
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        // Restarting the timer collapses a burst into one wake-up.
        _timer.Stop();
        _timer.Start();
    }

    private void OnSettled(object? sender, EventArgs e)
    {
        _timer.Stop();
        try
        {
            // Resolve through the normal path rather than trusting the event's hwnd: a UWP
            // app's first event points at ApplicationFrameHost, not the real process.
            _onSettled(_foreground.Capture());
        }
        catch (Exception ex)
        {
            _log.Warn($"Foreground warm-up failed: {ex.Message}"); // never fatal: it's a hint
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        _timer.Stop();

        // UnhookWinEvent only works from the thread that installed the hook — unlike the
        // low-level hooks, this one cannot be cleaned up from a crash handler on another
        // thread. That is fine: the OS drops WinEvent hooks when the process dies.
        IntPtr hook = Interlocked.Exchange(ref _hook, IntPtr.Zero);
        if (hook != IntPtr.Zero && Environment.CurrentManagedThreadId == _installedThreadId)
            UnhookWinEvent(hook);
        _proc = null;
    }
}
