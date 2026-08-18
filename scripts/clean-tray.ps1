# Removes ghost tray icons left behind by killed processes (Windows only clears them
# when the mouse passes over). Sends synthetic WM_MOUSEMOVE across the tray toolbars —
# the standard, non-destructive refresh technique.

$code = @'
using System;
using System.Runtime.InteropServices;

public static class TraySweeper
{
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string win);
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string win);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    const uint WM_MOUSEMOVE = 0x0200;

    static void Sweep(IntPtr toolbar)
    {
        if (toolbar == IntPtr.Zero) return;
        RECT r;
        if (!GetClientRect(toolbar, out r)) return;
        for (int y = 4; y < r.Bottom; y += 12)
            for (int x = 4; x < r.Right; x += 12)
                SendMessage(toolbar, WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)((y << 16) | (x & 0xFFFF)));
    }

    public static void Run()
    {
        // Visible tray: Shell_TrayWnd > TrayNotifyWnd > SysPager > ToolbarWindow32
        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        IntPtr pager = FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
        Sweep(FindWindowEx(pager == IntPtr.Zero ? notify : pager, IntPtr.Zero, "ToolbarWindow32", null));

        // Overflow flyout: NotifyIconOverflowWindow > ToolbarWindow32
        IntPtr overflow = FindWindow("NotifyIconOverflowWindow", null);
        Sweep(FindWindowEx(overflow, IntPtr.Zero, "ToolbarWindow32", null));
    }
}
'@
Add-Type -TypeDefinition $code
[TraySweeper]::Run()
Write-Output "tray swept"
