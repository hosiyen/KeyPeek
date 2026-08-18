using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static KeyPeek.Interop.NativeMethods;

namespace KeyPeek.UI;

/// <summary>
/// Dark/light title bar for our own windows (Win10 1809+ / Win11). WPF doesn't follow the
/// app theme here, so a themed window would otherwise wear a white title bar in dark mode.
/// </summary>
internal static class TitleBar
{
    /// <summary>Call from OnSourceInitialized, and again when the theme changes.</summary>
    public static void ApplyTheme(Window window)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        // The palette itself tells us which theme is live — no need to re-read the setting.
        bool light = (window.TryFindResource("KpBg") as SolidColorBrush)?.Color.R > 128;
        int dark = light ? 0 : 1;
        if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_PRE20H1, ref dark, sizeof(int));
    }
}
