using System.Diagnostics;
using System.IO;
using static KeyPeek.Interop.NativeMethods;

namespace KeyPeek.Services;

public sealed record ForegroundApp(
    IntPtr Hwnd,
    string ProcessName,   // lower-case, no ".exe"; "" if nothing could be resolved
    string? ExePath,      // null when inaccessible (e.g. elevated process)
    string Title,
    bool IsFullscreen,
    bool IsDesktop = false,   // the shell desktop/wallpaper is focused
    bool IsElevated = false,  // process could not be opened — likely running as admin
    bool IsSelf = false);     // KeyPeek's own settings/library window

/// <summary>Resolves the current foreground window to a process.</summary>
internal sealed class ForegroundAppService
{
    private readonly Logger _log;

    public ForegroundAppService(Logger log) => _log = log;

    public ForegroundApp Capture()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return new ForegroundApp(IntPtr.Zero, "", null, "", false); // no foreground at all

        // The wallpaper/desktop is technically an Explorer window; report it as its own
        // state so a "Desktop" definition can match (and Explorer's shortcuts don't).
        string className = GetClassName(hwnd);
        if (className is "Progman" or "WorkerW")
            return new ForegroundApp(hwnd, "desktop", null, "Desktop", false, IsDesktop: true);

        GetWindowThreadProcessId(hwnd, out uint pid);

        if (pid == (uint)Environment.ProcessId)
            return new ForegroundApp(hwnd, "keypeek", null, GetTitle(hwnd), false, IsSelf: true);

        string? path = TryGetProcessPath(pid);
        string name = path is not null
            ? Path.GetFileNameWithoutExtension(path)
            : TryGetProcessName(pid) ?? "";

        // UWP/packaged apps are hosted in ApplicationFrameHost.exe; the app's real
        // process owns a child window inside the frame. Resolve through to it.
        if (name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
        {
            uint realPid = FindChildProcessId(hwnd, pid);
            if (realPid != 0)
            {
                string? realPath = TryGetProcessPath(realPid);
                if (realPath is not null)
                {
                    path = realPath;
                    name = Path.GetFileNameWithoutExtension(realPath);
                }
                else
                {
                    name = TryGetProcessName(realPid) ?? name;
                    path = null;
                }
            }
        }

        // OpenProcess with QUERY_LIMITED_INFORMATION failing for a resolvable process
        // almost always means the window is elevated. Reading it is fine; click-to-run
        // against it will be refused by Windows (UIPI), so flag it for the UI.
        bool elevated = path is null && name.Length > 0;

        return new ForegroundApp(hwnd, name.ToLowerInvariant(), path, GetTitle(hwnd),
            IsFullscreenWindow(hwnd), IsElevated: elevated);
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var cls = new char[64];
        int len = GetClassNameW(hwnd, cls, cls.Length);
        return len > 0 ? new string(cls, 0, len) : "";
    }

    private static string? TryGetProcessPath(uint pid)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new char[1024];
            uint size = (uint)buffer.Length;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size)
                ? new string(buffer, 0, (int)size)
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string? TryGetProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch { return null; }
    }

    private static uint FindChildProcessId(IntPtr frameHwnd, uint framePid)
    {
        uint found = 0;
        EnumChildWindows(frameHwnd, (child, _) =>
        {
            GetWindowThreadProcessId(child, out uint childPid);
            if (childPid != framePid)
            {
                found = childPid;
                return false; // stop enumerating
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var buffer = new char[512];
        int len = GetWindowTextW(hwnd, buffer, buffer.Length);
        return len > 0 ? new string(buffer, 0, len) : "";
    }

    /// <summary>Borderless-fullscreen heuristic: the window covers its entire monitor
    /// (including the taskbar area) and is not the desktop itself.</summary>
    private static bool IsFullscreenWindow(IntPtr hwnd)
    {
        var cls = new char[64];
        int len = GetClassNameW(hwnd, cls, cls.Length);
        string className = len > 0 ? new string(cls, 0, len) : "";
        if (className is "Progman" or "WorkerW")
            return false; // the desktop covers the monitor but isn't "fullscreen"

        if (!GetWindowRect(hwnd, out RECT wr))
            return false;

        IntPtr mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(mon, ref mi))
            return false;

        return wr.Left <= mi.rcMonitor.Left && wr.Top <= mi.rcMonitor.Top &&
               wr.Right >= mi.rcMonitor.Right && wr.Bottom >= mi.rcMonitor.Bottom;
    }
}
