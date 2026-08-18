using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using KeyPeek.Core;

namespace KeyPeek.UI;

/// <summary>
/// The tile shown next to an app in the library list when we cannot read the app's real icon —
/// nearly always because the app is not installed on this machine (the library ships
/// definitions for Photoshop, Figma, Blender… regardless).
///
/// The glyphs are drawn here, by us, as a dozen plain strokes on a 24×24 grid. KeyPeek ships no
/// third-party logos: redistributing another company's mark is their call to license, not ours.
/// A globe still says "browser" and a pen nib still says "design tool", which is the part that
/// helps the eye find a row.
/// </summary>
internal static class CategoryBadge
{
    // 24×24, stroke 2, round caps — read them as pen strokes, not filled shapes.
    private static readonly Dictionary<AppCategory, (string Path, double Hue)> Glyphs = new()
    {
        [AppCategory.Browser] = ("M12 3.5a8.5 8.5 0 100 17 8.5 8.5 0 000-17z M3.5 12h17 " +
                                 "M12 3.5c2.2 2.3 3.4 5.3 3.4 8.5s-1.2 6.2-3.4 8.5" +
                                 "c-2.2-2.3-3.4-5.3-3.4-8.5s1.2-6.2 3.4-8.5z", 208),
        [AppCategory.Editor] = ("M9.5 6.5L4 12l5.5 5.5 M14.5 6.5L20 12l-5.5 5.5", 258),
        [AppCategory.Terminal] = ("M3.5 5h17v14h-17z M7 10l2.5 2L7 14 M12 14.5h4.5", 190),
        [AppCategory.Design] = ("M4.5 19.5l1.5-5.5 9-9 4 4-9 9z M13 6.5l4 4", 328),
        [AppCategory.Vector] = ("M4.5 18C7 9.5 13 5.5 19.5 5.5 M3 16.5h3.5V20H3z M17.5 4h3.5v3.5h-3.5z", 300),
        [AppCategory.Video] = ("M3.5 6h17v12h-17z M8 6v12 M16 6v12", 6),
        [AppCategory.ThreeD] = ("M12 3.5l8 4.4v8.2l-8 4.4-8-4.4V7.9z M4 7.9l8 4.4 8-4.4 M12 12.3v8.2", 172),
        [AppCategory.Layout] = ("M4.5 3.5h15v17h-15z M4.5 8.5h15 M12 8.5v12", 314),
        [AppCategory.Chat] = ("M3.5 6h17v9.5h-9L7 19.5V15.5h-3.5z", 150),
        [AppCategory.Meeting] = ("M3.5 7h11v10h-11z M14.5 11l6-3.5v9L14.5 13z", 278),
        [AppCategory.Mail] = ("M3.5 6.5h17v11h-17z M4 7l8 6.5L20 7", 224),
        [AppCategory.Office] = ("M6 3.5h7.5L18 8v12.5H6z M13.5 3.5V8H18 M9 12.5h6 M9 16h6", 32),
        [AppCategory.Media] = ("M8 5l11 7-11 7z", 348),
        [AppCategory.Files] = ("M3.5 19v-13h5.5l2 2.5h9.5V19z", 46),
        [AppCategory.System] = ("M3.5 5h17v14h-17z M3.5 9.5h17", 236),
    };

    /// <summary>The fallback definition is not an app at all — it is the set that stands in
    /// when the focused window has no shortcuts of its own, so it gets a keyboard.</summary>
    private static readonly (string Path, double Hue) FallbackGlyph =
        ("M2.5 6.5h19v11h-19z M6 10h1.5 M10 10h1.5 M14 10h1.5 M18 10h0.5 M8 14h8", 214);

    private static readonly Dictionary<string, Geometry> GeometryCache = new(StringComparer.Ordinal);

    /// <summary>Build the badge for an app. Falls back to a lettered tile for anything the
    /// classifier does not recognise, so a row is never blank.</summary>
    public static FrameworkElement Create(string appName, IEnumerable<string>? processNames,
        bool lightTheme, double size, bool isFallback = false)
    {
        (string Path, double Hue) glyph;
        bool known;
        if (isFallback)
        {
            glyph = FallbackGlyph;
            known = true;
        }
        else
        {
            known = Glyphs.TryGetValue(
                AppCategoryClassifier.Classify(appName, processNames), out glyph);
        }
        // & int.MaxValue, not Math.Abs: one app name in four billion hashes to int.MinValue,
        // whose absolute value does not fit in an int, and Math.Abs throws rather than
        // returning it. A thrown exception here takes the library list down.
        int hash = appName.Aggregate(17, (a, c) => a * 31 + c) & int.MaxValue;

        // Known categories sit near the category's hue, so a column of browsers reads as one
        // family — but each app is nudged up to ±24° off it, so two pencils side by side are
        // not the same pink. Unknown apps take a free hue from the same hash: the same app is
        // always the same colour, and the palette is muted enough to avoid confetti.
        double hue = known ? Wrap(glyph.Hue + (hash % 5 - 2) * 12) : hash % 360;
        Color fill = Hsl(hue, known ? 0.34 : 0.30, lightTheme ? 0.87 : 0.26);
        Color ink = Hsl(hue, known ? 0.62 : 0.55, lightTheme ? 0.32 : 0.82);

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.28),
            Background = new SolidColorBrush(fill),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            Child = known ? Glyph(glyph.Path, ink, size * 0.78) : Letter(appName, ink, size * 0.56),
        };
    }

    private static double Wrap(double hue) => (hue % 360 + 360) % 360;

    private static FrameworkElement Glyph(string data, Color ink, double box)
    {
        if (!GeometryCache.TryGetValue(data, out Geometry? geometry))
        {
            geometry = Geometry.Parse(data);
            geometry.Freeze();
            GeometryCache[data] = geometry;
        }

        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(ink),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        });

        // A Viewbox scales the stroke with the shape, so the 2-unit stroke lands near 1 px at
        // badge size instead of turning into a blob.
        return new Viewbox
        {
            Width = box,
            Height = box,
            Child = canvas,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static FrameworkElement Letter(string appName, Color ink, double fontSize) => new TextBlock
    {
        Text = appName.FirstOrDefault(char.IsLetterOrDigit) is var c && c != '\0'
            ? char.ToUpperInvariant(c).ToString()
            : "?",
        FontSize = fontSize,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(ink),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Color Hsl(double hue, double saturation, double lightness)
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
}
