using System.Threading.Channels;
using KeyPeek.Core;
using KeyPeek.Interop;
using KeyPeek.Services;
using static KeyPeek.Interop.NativeMethods;

namespace KeyPeek.Hooks;

/// <summary>
/// Owns the global low-level keyboard and mouse hooks.
///
/// Design constraints (these are the reason the code looks the way it does):
///
///  1. A low-level hook callback runs inside the OS input path for EVERY keystroke and
///     mouse event on the machine. If it's slow, the whole machine feels laggy, and
///     Windows silently evicts hooks that exceed its timeout. So the callbacks below do
///     the absolute minimum — read a few ints, drop a struct into a queue — and return.
///     All real logic happens on a separate consumer thread (HoldController).
///
///  2. KeyPeek is an observer, not a filter. The callbacks always pass events through
///     (CallNextHookEx) with exactly one exception: the Esc key while our overlay is
///     visible. Swallowing that Esc is what keeps Ctrl(held)+Esc from opening the Start
///     menu when the user dismisses the overlay. Nothing else is ever consumed, so
///     normal typing cannot be affected even if our consumer thread deadlocks.
///
///  3. The hooks live on their own dedicated thread with its own message pump, so a busy
///     UI thread can never delay hook processing.
///
///  4. The hooks are uninstalled on every exit path (see App.xaml.cs). If the process
///     dies outright, Windows removes low-level hooks automatically — they are not
///     injected DLLs — so a crash cannot leave the system hooked.
/// </summary>
internal sealed class KeyboardMouseHookService : IDisposable
{
    /// <summary>True while the overlay is visible and not pinned. Written by the UI thread,
    /// read by the hook thread; volatile is sufficient for a bool flag.</summary>
    public static volatile bool InterceptEsc;

    /// <summary>True while the overlay is visible AND the user opted into Explore mode —
    /// then the navigation keys (arrows/Enter/Home/End/PageUp/PageDown) drive the panel
    /// instead of reaching the app. False in the default configuration, which is what keeps
    /// the default input path byte-identical to a build without Explore mode. Cleared when
    /// the panel is pinned: search mode owns the keyboard then.</summary>
    public static volatile bool InterceptExplore;

    private readonly ChannelWriter<InputEvent> _writer;
    private readonly Logger _log;

    // The delegates must be kept in fields: if the GC collected them while the OS still
    // holds a pointer to the thunk, the next keystroke would crash the process.
    private HookProc? _keyboardProc;
    private HookProc? _mouseProc;

    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private Thread? _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new(false);
    private int _disposed;

    // Per-key ownership, hook-thread only. Two arrays because both halves matter:
    //
    //  _weOwn[vk]   — we swallowed this key's press, so its repeats and its release are
    //                 ours to swallow too (an app that sees a release it never saw pressed
    //                 can latch the key down for ever).
    //  _appOwns[vk] — the app already received this key's press, because it was held before
    //                 the panel opened. We must NOT start swallowing mid-press: the app
    //                 would wait for a release that never arrives.
    private readonly bool[] _weOwn = new bool[256];
    private readonly bool[] _appOwns = new bool[256];

    public KeyboardMouseHookService(ChannelWriter<InputEvent> writer, Logger log)
    {
        _writer = writer;
        _log = log;
    }

    public void Install()
    {
        _thread = new Thread(HookThreadProc) { IsBackground = true, Name = "KeyPeek.Hooks" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(5000))
            _log.Error("Hook thread did not start within 5s");
    }

    private void HookThreadProc()
    {
        _threadId = GetCurrentThreadId();
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;

        IntPtr module = GetModuleHandleW(null);
        _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, module, 0);

        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            _log.Error($"Hook install failed (kbd={_keyboardHook}, mouse={_mouseHook})");
        else
            _log.Info("Global keyboard/mouse hooks installed");

        _ready.Set();

        // Pump messages until Dispose() posts WM_QUIT. The hooks are dispatched to this
        // thread by the OS as long as this loop runs.
        while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        Uninstall();
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool isUp = msg is WM_KEYUP or WM_SYSKEYUP;
            if (isDown || isUp)
            {
                int vk;
                unsafe { vk = (int)((KBDLLHOOKSTRUCT*)lParam)->vkCode; }

                _writer.TryWrite(InputEvent.Key(vk, isDown));

                if ((uint)vk < 256)
                {
                    // The two exceptions to "never swallow input": Esc while the panel is
                    // visible (else Ctrl+Esc opens Start as the user dismisses), and the
                    // Explore navigation keys when that opt-in feature is on. ExplorePolicy
                    // is the single source of truth for the second set; with the feature off
                    // InterceptExplore is never true and this costs one bool read.
                    bool wantSwallow = (vk == VK_ESCAPE && InterceptEsc) ||
                                       ExplorePolicy.ShouldSwallow(vk, InterceptExplore, true);

                    if (isDown)
                    {
                        if (_weOwn[vk])
                            return 1; // our key: swallow its auto-repeats too

                        // Never take a key mid-press. If the app already has this key down
                        // (held before the panel opened), its repeats and its release must
                        // reach the app, or the app is left holding a key for ever.
                        if (wantSwallow && !_appOwns[vk])
                        {
                            _weOwn[vk] = true;
                            return 1;
                        }
                        _appOwns[vk] = true;
                    }
                    else // isUp
                    {
                        if (_weOwn[vk])
                        {
                            _weOwn[vk] = false;
                            return 1; // the app never saw the press; it must not see this
                        }
                        _appOwns[vk] = false;
                    }
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            InputEventKind kind;
            switch (msg)
            {
                case WM_LBUTTONDOWN:
                case WM_RBUTTONDOWN:
                case WM_MBUTTONDOWN:
                case WM_XBUTTONDOWN:
                    kind = InputEventKind.MouseButtonDown;
                    break;
                case WM_MOUSEWHEEL:
                case WM_MOUSEHWHEEL:
                    kind = InputEventKind.MouseWheel;
                    break;
                default:
                    // Mouse moves and button-ups are irrelevant to us; skip the queue write.
                    return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            }

            POINT pt;
            unsafe { pt = ((MSLLHOOKSTRUCT*)lParam)->pt; }
            _writer.TryWrite(InputEvent.Mouse(kind, pt.X, pt.Y));
        }
        // Mouse events are NEVER swallowed.
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void Uninstall()
    {
        IntPtr kbd = Interlocked.Exchange(ref _keyboardHook, IntPtr.Zero);
        IntPtr mouse = Interlocked.Exchange(ref _mouseHook, IntPtr.Zero);
        bool removed = false;
        if (kbd != IntPtr.Zero) { UnhookWindowsHookEx(kbd); removed = true; }
        if (mouse != IntPtr.Zero) { UnhookWindowsHookEx(mouse); removed = true; }
        if (removed)
            _log.Info("Global hooks uninstalled");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        if (_threadId != 0)
            PostThreadMessageW(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);

        // Give the hook thread a moment to exit its pump and unhook itself; if it can't
        // (e.g. we're crashing), unhook directly — UnhookWindowsHookEx works cross-thread.
        if (_thread is null || !_thread.Join(1000))
            Uninstall();

        _writer.TryComplete();
    }
}
