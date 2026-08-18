using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// KeyPeekDriver — test harness used by the verify scripts. Injects keyboard input with
// SendInput (low-level hooks see injected input, so this exercises the real pipeline),
// reports/sets the foreground window, and takes screenshots.
//
// Usage:  KeyPeekDriver.exe "down ctrl; sleep 700; up ctrl; foreground"
// Commands: down <key> | up <key> | press <key> | sleep <ms> | focus <processName>
//           foreground | shot <path> | click <x> <y> | move <x> <y> | wheel <x> <y> <notches>
//           winrect <processName> | waitidle <idleSec> <timeoutSec>

internal static class Program
{
    private static readonly Dictionary<string, ushort> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0xA2, ["rctrl"] = 0xA3, ["shift"] = 0xA0, ["rshift"] = 0xA1,
        ["alt"] = 0xA4, ["ralt"] = 0xA5, ["win"] = 0x5B,
        ["esc"] = 0x1B, ["tab"] = 0x09, ["space"] = 0x20, ["enter"] = 0x0D,
        ["f13"] = 0x7C, ["f14"] = 0x7D, ["f15"] = 0x7E,
        // Explore-mode navigation keys.
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["home"] = 0x24, ["end"] = 0x23, ["pgup"] = 0x21, ["pgdn"] = 0x22,
    };

    private static int Main(string[] args)
    {
        // Without this the process is DPI-virtualised: PrimaryScreen.Bounds reports the
        // scaled size (1536x864 on this 1920x1080 display at 125%), so every screenshot was
        // silently cropped to the top-left 80% of the screen and UI reviews missed the
        // right edge of the panel.
        SetProcessDPIAware();

        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: KeyPeekDriver \"cmd; cmd; ...\"");
            return 2;
        }

        string script = string.Join(" ", args);
        foreach (string raw in script.Split(';'))
        {
            string[] parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            try
            {
                if (!Execute(parts))
                    return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error in '{raw.Trim()}': {ex.Message}");
                return 1;
            }
        }
        return 0;
    }

    private static bool Execute(string[] parts)
    {
        switch (parts[0].ToLowerInvariant())
        {
            case "down": SendKey(ResolveVk(parts[1]), true); break;
            case "up": SendKey(ResolveVk(parts[1]), false); break;
            case "press":
                SendKey(ResolveVk(parts[1]), true);
                Thread.Sleep(30);
                SendKey(ResolveVk(parts[1]), false);
                break;
            case "sleep": Thread.Sleep(int.Parse(parts[1])); break;
            case "foreground": Console.WriteLine(ForegroundProcessName()); break;
            case "focus": return Focus(parts[1]);
            case "shot": Screenshot(parts[1]); break;
            case "move": SetCursorPos(int.Parse(parts[1]), int.Parse(parts[2])); break;
            case "winrect": return PrintWindowRect(parts[1]);
            case "click": Click(int.Parse(parts[1]), int.Parse(parts[2])); break;
            case "wheel": Wheel(int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3])); break;
            case "waitidle": WaitIdle(int.Parse(parts[1]), int.Parse(parts[2])); break;
            default:
                Console.Error.WriteLine($"unknown command {parts[0]}");
                return false;
        }
        return true;
    }

    private static ushort ResolveVk(string name)
    {
        if (Keys.TryGetValue(name, out ushort vk)) return vk;
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0])) return (ushort)char.ToUpperInvariant(name[0]);
        throw new ArgumentException($"unknown key '{name}'");
    }

    private static void SendKey(ushort vk, bool down)
    {
        var input = new INPUT
        {
            type = 1, // INPUT_KEYBOARD
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = down ? 0u : 2u /*KEYEVENTF_KEYUP*/ } },
        };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) != 1)
            throw new InvalidOperationException($"SendInput failed ({Marshal.GetLastWin32Error()})");
    }

    private static string ForegroundProcessName()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return "(none)";
        GetWindowThreadProcessId(hwnd, out uint pid);
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch { return "(unknown)"; }
    }

    private static bool Focus(string processName)
    {
        foreach (var p in Process.GetProcessesByName(processName))
        {
            if (p.MainWindowHandle == IntPtr.Zero) continue;
            if (TryMakeForeground(p.MainWindowHandle, processName))
            {
                Console.WriteLine("focused");
                return true;
            }
        }
        Console.Error.WriteLine($"could not focus {processName}");
        return false;
    }

    private static bool IsForeground(string processName) =>
        ForegroundProcessName().Equals(processName, StringComparison.OrdinalIgnoreCase);

    // Windows refuses SetForegroundWindow from background processes in many situations;
    // escalate through the classic workarounds until one sticks.
    private static bool TryMakeForeground(IntPtr hwnd, string processName)
    {
        ShowWindow(hwnd, 9 /*SW_RESTORE*/);
        SetForegroundWindow(hwnd);
        Thread.Sleep(250);
        if (IsForeground(processName)) return true;

        // Attaching our input queue to the current foreground thread borrows its
        // right to change the foreground window.
        IntPtr fg = GetForegroundWindow();
        if (fg != IntPtr.Zero)
        {
            uint fgThread = GetWindowThreadProcessId(fg, out _);
            uint myThread = GetCurrentThreadId();
            if (fgThread != 0 && fgThread != myThread)
            {
                AttachThreadInput(myThread, fgThread, true);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                AttachThreadInput(myThread, fgThread, false);
                Thread.Sleep(250);
                if (IsForeground(processName)) return true;
            }
        }

        SwitchToThisWindow(hwnd, true); // last resort: the Alt+Tab switcher API
        Thread.Sleep(250);
        return IsForeground(processName);
    }

    // Wait until the machine has seen no (physical or injected) input for idleSeconds,
    // so test injections never interleave with a person actually using the computer.
    // Gives up after timeoutSeconds and proceeds, to avoid hanging forever.
    private static void WaitIdle(int idleSeconds, int timeoutSeconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < timeoutSeconds)
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info))
                return;
            uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
            if (idleMs >= (uint)(idleSeconds * 1000))
            {
                Console.WriteLine("idle");
                return;
            }
            Thread.Sleep(500);
        }
        Console.WriteLine("not-idle-timeout");
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    /// <summary>Print "x y width height" for a process's main window, so a test can aim a
    /// click at a control by offset instead of hard-coding screen coordinates that change
    /// with every window move.</summary>
    private static bool PrintWindowRect(string processName)
    {
        foreach (Process p in Process.GetProcessesByName(processName))
        {
            if (p.MainWindowHandle == IntPtr.Zero || !GetWindowRect(p.MainWindowHandle, out RECT r))
                continue;
            Console.WriteLine($"{r.Left} {r.Top} {r.Right - r.Left} {r.Bottom - r.Top}");
            return true;
        }
        Console.Error.WriteLine($"no window for {processName}");
        return false;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>Left-click at a screen point (physical pixels — the process is DPI-aware).
    /// Without this the harness could not test anything that needs a mouse: click-to-pin,
    /// the search box, the shortcut editor. Two regressions reached the user through that
    /// gap before it was closed.</summary>
    private static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(60); // let hover/enter handlers run, as a human hand would
        var down = new INPUT { type = INPUT_MOUSE };
        down.U.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
        var up = new INPUT { type = INPUT_MOUSE };
        up.U.mi.dwFlags = MOUSEEVENTF_LEFTUP;
        if (SendInput(1, new[] { down }, Marshal.SizeOf<INPUT>()) == 0 ||
            SendInput(1, new[] { up }, Marshal.SizeOf<INPUT>()) == 0)
            throw new InvalidOperationException("SendInput (mouse) failed");
        Console.WriteLine($"click {x} {y}");
    }

    /// <summary>Scroll the wheel over a point: `wheel x y notches`, negative notches scroll
    /// down. Lists here are virtualised, so a UI review that never scrolls only ever sees the
    /// first screenful — which is exactly how far every icon check had got.</summary>
    private static void Wheel(int x, int y, int notches)
    {
        SetCursorPos(x, y);
        Thread.Sleep(60);
        var input = new INPUT { type = INPUT_MOUSE };
        input.U.mi.dwFlags = MOUSEEVENTF_WHEEL;
        input.U.mi.mouseData = unchecked((uint)(notches * 120)); // WHEEL_DELTA
        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
            throw new InvalidOperationException("SendInput (wheel) failed");
        Console.WriteLine($"wheel {x} {y} {notches}");
    }

    private const int INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    private static void Screenshot(string path)
    {
        Rectangle bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        using var bmp = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        bmp.Save(path, ImageFormat.Png);
        Console.WriteLine($"shot {path}");
    }

    // ---- Win32 ----

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fUnknown);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
