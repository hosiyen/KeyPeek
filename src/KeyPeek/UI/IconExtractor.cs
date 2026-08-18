using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static KeyPeek.Interop.NativeMethods;

namespace KeyPeek.UI;

/// <summary>
/// Extracts an app's icon from its executable at runtime (R5 — no bundled logo images).
/// Results are frozen ImageSources, cached per path.
/// </summary>
internal static class IconExtractor
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? ForFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        return Cache.GetOrAdd(path, Extract);
    }

    /// <summary>Icon used for the global "Windows" group: Explorer's own.</summary>
    public static ImageSource? WindowsIcon() =>
        ForFile(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));

    private static ImageSource? Extract(string path)
    {
        var icons = new IntPtr[1];
        var ids = new uint[1];
        try
        {
            uint got = PrivateExtractIconsW(path, 0, 48, 48, icons, ids, 1, 0);
            if (got == 0 || icons[0] == IntPtr.Zero)
                return null;

            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); // safe to touch from any thread afterwards
            return source;
        }
        catch
        {
            return null; // no icon is fine; the label still identifies the app
        }
        finally
        {
            if (icons[0] != IntPtr.Zero)
                DestroyIcon(icons[0]);
        }
    }
}
