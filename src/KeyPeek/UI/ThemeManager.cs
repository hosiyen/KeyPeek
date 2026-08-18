using System.Windows;
using System.Windows.Media;
using KeyPeek.Services;
using Microsoft.Win32;

namespace KeyPeek.UI;

/// <summary>
/// Applies the overlay theme (R9: dark / light / follow system) by swapping one merged
/// resource dictionary at the application level. "System" reads Windows' app-theme
/// setting and re-applies live when the user changes it.
/// </summary>
internal sealed class ThemeManager : IDisposable
{
    private readonly SettingsService _settings;
    private readonly Logger _log;
    private ResourceDictionary? _active;

    public ThemeManager(SettingsService settings, Logger log)
    {
        _settings = settings;
        _log = log;
        Apply();
        _settings.Changed += Apply;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Two reasons to re-apply: the user switched Windows' light/dark mode (General, and
        // only while we follow it), or they turned High Contrast on or off — which arrives
        // as Accessibility and must be honoured whatever the theme setting says.
        bool followsSystem = _settings.Current.Theme.Equals("system", StringComparison.OrdinalIgnoreCase);
        if ((e.Category == UserPreferenceCategory.General && followsSystem) ||
            e.Category == UserPreferenceCategory.Accessibility ||
            e.Category == UserPreferenceCategory.Color)
        {
            Application.Current?.Dispatcher.BeginInvoke(Apply);
        }
    }

    /// <summary>True while Windows is in a High Contrast theme. KeyPeek then drops its
    /// transparency and its fade: both reduce contrast, which is the one thing a user in
    /// this mode has told the system they cannot afford.</summary>
    public static bool HighContrast => SystemParameters.HighContrast;

    public void Apply()
    {
        bool highContrast = HighContrast;
        bool light = highContrast
            // In High Contrast, Windows' own colours decide. Judge by perceived brightness,
            // not the red channel alone — a dark red custom theme (#AA0000) has R = 170 and
            // would otherwise be treated as a light background.
            ? (0.299 * SystemColors.WindowColor.R + 0.587 * SystemColors.WindowColor.G +
               0.114 * SystemColors.WindowColor.B) > 128
            : _settings.Current.Theme.Trim().ToLowerInvariant() switch
            {
                "light" => true,
                "dark" => false,
                _ => SystemUsesLightTheme(),
            };

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/UI/Themes/{(light ? "OverlayLight" : "OverlayDark")}.xaml"),
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_active is not null)
            merged.Remove(_active);
        merged.Add(dictionary);
        _active = dictionary;

        if (!highContrast)
            ApplyAccent(light);

        if (highContrast)
        {
            // Nothing after this may touch the palette: the opacity block below re-derives
            // KpPanelBg from the THEME dictionary and would paint the system background
            // back over with KeyPeek's own colour.
            ApplySystemColors();
            _log.Info("Theme applied: high contrast (system colours, opaque, no animation)");
            return;
        }

        // Transparency setting: direct app-level entries win over merged dictionaries,
        // so overriding KpPanelBg here applies the user's alpha on top of either palette.
        if (dictionary["KpPanelBg"] is System.Windows.Media.SolidColorBrush baseBrush)
        {
            int pct = Math.Clamp(_settings.Current.OverlayOpacityPercent, 60, 100);
            var c = baseBrush.Color;
            var brush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb((byte)Math.Round(255 * pct / 100.0), c.R, c.G, c.B));
            brush.Freeze();
            Application.Current.Resources["KpPanelBg"] = brush;
        }

        _log.Info($"Theme applied: {(light ? "light" : "dark")} ({_settings.Current.Theme}), " +
                  $"opacity {_settings.Current.OverlayOpacityPercent}%");
    }

    /// <summary>
    /// Set the one accent hue everything else derives from. The default follows the colour
    /// the user already chose in Windows: KeyPeek is a utility that sits on top of their
    /// desktop, so matching it beats imposing a brand colour. The raw system accent is used
    /// as a HUE, not verbatim — Windows' value can be near-black or near-white, and either
    /// would vanish against one of our two palettes.
    /// </summary>
    private void ApplyAccent(bool light)
    {
        (double hue, double saturation)? choice = _settings.Current.Accent.Trim().ToLowerInvariant() switch
        {
            "indigo" => (231.0, 0.72),
            "violet" => (265.0, 0.62),
            "teal" => (178.0, 0.55),
            "amber" => (36.0, 0.72),
            _ => SystemAccentHue(),
        };
        if (choice is not { } hs)
            return; // no system accent readable: keep the palette's own colour

        var resources = Application.Current.Resources;
        void Set(string key, Color color)
        {
            var brush = new System.Windows.Media.SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        // Legibility comes from the lightness, which is fixed per theme; the hue is the only
        // thing the user's choice moves.
        Set("KpAccent", FromHsl(hs.hue, hs.saturation, light ? 0.42 : 0.68));
        Set("KpAccentSubtle", FromHsl(hs.hue, hs.saturation * 0.75, light ? 0.88 : 0.26));
        Set("KpOnAccent", light ? Colors.White : Color.FromRgb(0x12, 0x12, 0x18));
        Set("KpStar", FromHsl(hs.hue, hs.saturation, light ? 0.48 : 0.72));
    }

    /// <summary>Hue and saturation of the Windows accent colour, or null if unreadable.</summary>
    private static (double Hue, double Saturation)? SystemAccentHue()
    {
        try
        {
            using Microsoft.Win32.RegistryKey? key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("ColorizationColor") is not int argb)
                return null;
            var color = Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            (double h, double s, double _) = ToHsl(color);
            // A grey system accent has no usable hue; fall back to the palette's own.
            return s < 0.08 ? null : (h, Math.Clamp(s, 0.45, 0.8));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        if (d == 0)
            return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0)
                 : max == g ? (b - r) / d + 2
                 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = lightness - c / 2;
        (double r, double g, double b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    /// <summary>Repaint the palette from Windows' High Contrast colours. Only the tokens
    /// that carry meaning are mapped — a themed panel drawn in custom greys inside a High
    /// Contrast desktop is exactly the failure this mode exists to prevent.</summary>
    private static void ApplySystemColors()
    {
        var resources = Application.Current.Resources;
        void Set(string key, System.Windows.Media.Color color)
        {
            var brush = new System.Windows.Media.SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        Set("KpPanelBg", SystemColors.WindowColor);
        Set("KpBg", SystemColors.WindowColor);
        Set("KpSurface", SystemColors.ControlColor);
        Set("KpSurface2", SystemColors.ControlColor);
        Set("KpCardBg", SystemColors.ControlColor);
        Set("KpCardBorder", SystemColors.ActiveBorderColor);
        Set("KpRailBg", SystemColors.ControlColor);
        Set("KpText", SystemColors.WindowTextColor);
        Set("KpTextBody", SystemColors.WindowTextColor);
        Set("KpTextDim", SystemColors.GrayTextColor);
        Set("KpTextFaint", SystemColors.GrayTextColor);
        Set("KpZoneLabel", SystemColors.GrayTextColor);
        Set("KpSectionLabel", SystemColors.GrayTextColor);
        Set("KpLine", SystemColors.ActiveBorderColor);
        Set("KpPanelBorder", SystemColors.ActiveBorderColor);
        Set("KpSeparator", SystemColors.ActiveBorderColor);
        Set("KpCapBg", SystemColors.ControlColor);
        Set("KpCapBorder", SystemColors.WindowTextColor);
        Set("KpCapText", SystemColors.ControlTextColor);
        Set("KpAccent", SystemColors.HighlightColor);
        Set("KpOnAccent", SystemColors.HighlightTextColor);
        Set("KpStar", SystemColors.HighlightColor);
        // NOT the solid highlight colour: a row's description is drawn in WindowText, and
        // filling the row with Highlight underneath it leaves white-on-cyan at roughly
        // 1.35:1 — unreadable, in the one mode whose entire purpose is contrast. Keep the
        // background as-is; the accent bar the selected row draws is what marks it.
        Set("KpHover", SystemColors.WindowColor);
        Set("KpAccentSubtle", SystemColors.WindowColor);
    }

    /// <summary>HKCU AppsUseLightTheme: 1 = light apps. Missing key = dark default.</summary>
    private static bool SystemUsesLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
