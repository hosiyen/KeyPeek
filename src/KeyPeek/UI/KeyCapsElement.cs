using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace KeyPeek.UI;

/// <summary>
/// The shared key-cap renderer: draws a chord list (caps, "or" prefixes for alternates)
/// directly with a DrawingContext. One element replaces the ~10 nested
/// ItemsControl/Border/TextBlock elements the templated version needed per row — this
/// is what makes first-show of a large table fast (T2b) — and it is the single place
/// chords get drawn (overlay rows, settings, dashboards).
///
/// Colors resolve from the Kp* theme resources at render time, so theme switches
/// re-render correctly (AffectsRender via InvalidateVisual on theme change is not
/// needed — a new show always re-renders, and static text re-renders with the window).
/// </summary>
public sealed class KeyCapsElement : FrameworkElement
{
    public static readonly DependencyProperty ChordsProperty = DependencyProperty.Register(
        nameof(Chords), typeof(IReadOnlyList<ChordVm>), typeof(KeyCapsElement),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<ChordVm>? Chords
    {
        get => (IReadOnlyList<ChordVm>?)GetValue(ChordsProperty);
        set => SetValue(ChordsProperty, value);
    }

    private const double CapFontSize = 11.5;
    private const double PrefixFontSize = 10.5;
    private const double PadX = 5;
    private const double CapHeight = 19;
    private const double MinCapWidth = 22;
    private const double KeyGap = 3;
    private const double ChordGap = 6;
    private const double PrefixGap = 4;
    private const double Corner = 4;

    private static readonly Typeface CapTypeface =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface PrefixTypeface =
        new(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);

    // Label widths repeat massively (Ctrl, Shift, single letters) — cache measurements.
    private static readonly Dictionary<string, double> WidthCache = new();

    private double LabelWidth(string label)
    {
        if (WidthCache.TryGetValue(label, out double w))
            return w;
        var ft = Text(label, CapTypeface, CapFontSize, Brushes.Black);
        w = ft.WidthIncludingTrailingWhitespace;
        WidthCache[label] = w;
        return w;
    }

    private FormattedText Text(string s, Typeface face, double size, Brush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private double CapWidth(string label) => Math.Max(MinCapWidth, LabelWidth(label) + 2 * PadX);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        bool first = true;
        foreach (ChordVm chord in Chords ?? Array.Empty<ChordVm>())
        {
            if (!first)
                width += ChordGap;
            first = false;
            if (chord.Prefix.Length > 0)
                width += Text(chord.Prefix, PrefixTypeface, PrefixFontSize, Brushes.Black)
                             .WidthIncludingTrailingWhitespace + PrefixGap;
            for (int i = 0; i < chord.Keys.Count; i++)
                width += CapWidth(chord.Keys[i]) + (i > 0 ? KeyGap : 0);
        }
        return new Size(width, CapHeight + 2);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var chords = Chords;
        if (chords is null || chords.Count == 0)
            return;

        Brush capBg = TryFindResource("KpCapBg") as Brush ?? Brushes.DimGray;
        Brush capBorder = TryFindResource("KpCapBorder") as Brush ?? Brushes.Gray;
        Brush capText = TryFindResource("KpCapText") as Brush ?? Brushes.White;
        Brush dimText = TryFindResource("KpTextDim") as Brush ?? Brushes.LightGray;
        var pen = new Pen(capBorder, 1);
        var bottomPen = new Pen(capBorder, 2);

        double x = 0;
        bool first = true;
        foreach (ChordVm chord in chords)
        {
            if (!first)
                x += ChordGap;
            first = false;

            if (chord.Prefix.Length > 0)
            {
                FormattedText prefix = Text(chord.Prefix, PrefixTypeface, PrefixFontSize, dimText);
                dc.DrawText(prefix, new Point(x, (CapHeight - prefix.Height) / 2 + 1));
                x += prefix.WidthIncludingTrailingWhitespace + PrefixGap;
            }

            for (int i = 0; i < chord.Keys.Count; i++)
            {
                if (i > 0)
                    x += KeyGap;
                string label = chord.Keys[i];
                double w = CapWidth(label);
                var rect = new Rect(x + 0.5, 1.5, w, CapHeight - 1);
                dc.DrawRoundedRectangle(capBg, pen, rect, Corner, Corner);
                // the "physical key" thicker bottom edge
                dc.DrawLine(bottomPen,
                    new Point(rect.X + Corner, rect.Bottom - 0.5),
                    new Point(rect.Right - Corner, rect.Bottom - 0.5));
                FormattedText text = Text(label, CapTypeface, CapFontSize, capText);
                dc.DrawText(text, new Point(x + (w - text.WidthIncludingTrailingWhitespace) / 2,
                    1 + (CapHeight - text.Height) / 2));
                x += w;
            }
        }
    }
}
