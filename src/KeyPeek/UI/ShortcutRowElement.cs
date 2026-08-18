using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using KeyPeek.Core;

namespace KeyPeek.UI;

/// <summary>
/// A whole shortcut row — key caps, ★ and description — drawn in ONE
/// element. The templated version needed ~7 elements per row; a 140-row panel therefore
/// built ~1000 visuals on first show and took ~900 ms. Same pixels, one visual.
///
/// The parent Border still owns hover background and click; this element reports which
/// zone was clicked so the row can tell "run it" from "report it".
/// </summary>
public sealed class ShortcutRowElement : FrameworkElement
{
    public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(
        nameof(Entry), typeof(EntryVm), typeof(ShortcutRowElement),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public EntryVm? Entry
    {
        get => (EntryVm?)GetValue(EntryProperty);
        set => SetValue(EntryProperty, value);
    }

    /// <summary>Explore mode's keyboard selection. Set through a binding that compares this
    /// row's entry with the window's SelectedEntry, so exactly one row paints as selected.</summary>
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(ShortcutRowElement),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender,
            OnIsSelectedChanged));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Keep the keyboard selection on screen. The element asks for itself rather than the
        // presenter hunting for containers — system-zone containers don't exist for a frame
        // after Apply(), so an outside-in lookup would miss them.
        if (e.NewValue is not true || d is not ShortcutRowElement row)
            return;
        row.BringIntoView();

        // Tell assistive technology the selection moved. Without this a Narrator user in
        // Explore mode hears nothing as the highlight travels: the rows are readable, but
        // the fact that a different one is now current is invisible.
        if (AutomationPeer.ListenerExists(AutomationEvents.AutomationFocusChanged) &&
            UIElementAutomationPeer.FromElement(row) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
    }

    /// <summary>How a screen reader (or any automation client) runs this row. The element
    /// draws itself, so it has no clickable child to inherit this from — the template binds
    /// this to the window's row-invoke command.</summary>
    public static readonly DependencyProperty InvokeCommandProperty = DependencyProperty.Register(
        nameof(InvokeCommand), typeof(ICommand), typeof(ShortcutRowElement), new PropertyMetadata(null));

    public ICommand? InvokeCommand
    {
        get => (ICommand?)GetValue(InvokeCommandProperty);
        set => SetValue(InvokeCommandProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ShortcutRowPeer(this);

    /// <summary>Breathing room at the right edge, so a description that fills the row does
    /// not touch the card border. This used to be a 22-DIP strip reserved for a hover flag
    /// that reported a wrong shortcut — removed: with no community library to report to it
    /// wrote a text file into AppData and opened it in Notepad, and it cost a strip of every
    /// row where a click did not run the shortcut.</summary>
    public const double RightPadding = 8;

    private const double RowHeight = 21;
    private const double CapFontSize = 11.5;
    private const double TextFontSize = 12;
    private const double PadX = 5;
    private const double MinCapWidth = 22;
    private const double KeyGap = 3;
    private const double ChordGap = 6;
    private const double PrefixGap = 4;
    private const double KeysColumnWidth = 168;
    private const double Corner = 4;

    private static readonly Typeface CapFace =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface TextFace =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface PrefixFace =
        new(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface SymbolFace =
        new(new FontFamily("Segoe UI Symbol"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static readonly Dictionary<string, double> CapWidthCache = new();

    // Hit-testing across the whole row comes from the transparent rectangle drawn in
    // OnRender — a FrameworkElement with nothing painted is not clickable.

    private FormattedText Text(string s, Typeface face, double size, Brush brush) =>
        FormatText(s, face, size, brush);

    private static FormattedText FormatText(string s, Typeface face, double size, Brush brush) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, 1.0);

    private double CapWidth(string label) => CapWidthOf(label);

    /// <summary>Width of one key cap. Cached by label and shared with the presenter, so the
    /// column width it computes is exactly the width these rows draw.</summary>
    private static double CapWidthOf(string label)
    {
        if (CapWidthCache.TryGetValue(label, out double w))
            return w;
        w = Math.Max(MinCapWidth,
            FormatText(label, CapFace, CapFontSize, Brushes.Black).WidthIncludingTrailingWhitespace + 2 * PadX);
        CapWidthCache[label] = w;
        return w;
    }

    private double KeysWidth(EntryVm entry) => KeysWidthOf(entry.Chords);

    /// <summary>Total width the key caps of one row need. Public so a card can size its keys
    /// column to its own widest row instead of a fixed guess — a card of single-letter
    /// shortcuts was wasting ~90 px and truncating its descriptions.</summary>
    public static double KeysWidthOf(IReadOnlyList<ChordVm> chords)
    {
        double width = 0;
        bool first = true;
        foreach (ChordVm chord in chords)
        {
            if (!first)
                width += ChordGap;
            first = false;
            if (chord.Prefix.Length > 0)
                width += FormatText(chord.Prefix, PrefixFace, 10.5, Brushes.Black)
                             .WidthIncludingTrailingWhitespace + PrefixGap;
            for (int i = 0; i < chord.Keys.Count; i++)
                width += CapWidthOf(chord.Keys[i]) + (i > 0 ? KeyGap : 0);
        }
        return width;
    }

    /// <summary>Where the description starts: the card's shared column when it set one
    /// (rows in a card stay aligned), otherwise this row's own keys.</summary>
    private double TextColumn(EntryVm entry) =>
        entry.KeysColumn > 0
            ? Math.Max(entry.KeysColumn, KeysWidth(entry) + 10)
            : Math.Max(KeysColumnWidth, KeysWidth(entry) + 10);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Entry is null)
            return new Size(0, 0);
        double keys = TextColumn(Entry);
        double desired = keys + 10 + 120 + RightPadding;
        // Measure the height at the width we will actually be GIVEN, not at our minimum:
        // a row inside a fixed-width card is stretched to that card, and measuring the
        // description against the narrower figure made short rows reserve a second line
        // they never draw — visible as uneven gaps down the card.
        double width = double.IsInfinity(availableSize.Width) ? desired : availableSize.Width;

        // A description that doesn't fit gets a second line rather than an ellipsis. Cards
        // are a fixed width, and half the rows in a card like Edge's "Address bar" were
        // being cut mid-sentence — a shortcut list you have to hover to read is not a list.
        return new Size(width, Math.Max(RowHeight, DescriptionHeight(Entry, width) + 4));
    }

    /// <summary>Height the description needs at this width, up to two lines.</summary>
    private double DescriptionHeight(EntryVm entry, double width)
    {
        double available = Math.Max(20, width - TextColumn(entry) - RightPadding);
        FormattedText text = Text(entry.Description, TextFace, TextFontSize, Brushes.Black);
        text.MaxTextWidth = available;
        text.MaxLineCount = MaxDescriptionLines;
        return text.Height;
    }

    private const int MaxDescriptionLines = 2;

    protected override void OnRender(DrawingContext dc)
    {
        EntryVm? entry = Entry;
        if (entry is null)
            return;

        // Transparent fill keeps the row hit-testable across its whole width.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (IsSelected)
        {
            // Accent wash + left bar: readable against both themes without moving the text.
            Brush sel = TryFindResource("KpAccentSubtle") as Brush ?? Brushes.SlateBlue;
            Brush bar = TryFindResource("KpAccent") as Brush ?? Brushes.RoyalBlue;
            dc.DrawRoundedRectangle(sel, null, new Rect(-4, 0, ActualWidth + 8, ActualHeight), 4, 4);
            dc.DrawRoundedRectangle(bar, null, new Rect(-4, 2, 2.5, ActualHeight - 4), 1.5, 1.5);
        }

        Brush capBg = TryFindResource("KpCapBg") as Brush ?? Brushes.DimGray;
        Brush capBorder = TryFindResource("KpCapBorder") as Brush ?? Brushes.Gray;
        Brush capText = TryFindResource("KpCapText") as Brush ?? Brushes.White;
        Brush dim = TryFindResource("KpTextDim") as Brush ?? Brushes.Gray;
        Brush body = TryFindResource("KpTextBody") as Brush ?? Brushes.White;
        Brush star = TryFindResource("KpStar") as Brush ?? Brushes.Violet;
        var pen = new Pen(capBorder, 1);
        var bottomPen = new Pen(capBorder, 2);

        double x = 0;
        bool first = true;
        foreach (ChordVm chord in entry.Chords)
        {
            if (!first)
                x += ChordGap;
            first = false;

            if (chord.Prefix.Length > 0)
            {
                FormattedText prefix = Text(chord.Prefix, PrefixFace, 10.5, dim);
                dc.DrawText(prefix, new Point(x, (RowHeight - prefix.Height) / 2));
                x += prefix.WidthIncludingTrailingWhitespace + PrefixGap;
            }

            for (int i = 0; i < chord.Keys.Count; i++)
            {
                if (i > 0)
                    x += KeyGap;
                string label = chord.Keys[i];
                double w = CapWidth(label);
                var rect = new Rect(x + 0.5, 1.5, w, RowHeight - 4);
                dc.DrawRoundedRectangle(capBg, pen, rect, Corner, Corner);
                dc.DrawLine(bottomPen,
                    new Point(rect.X + Corner, rect.Bottom - 0.5),
                    new Point(rect.Right - Corner, rect.Bottom - 0.5));
                FormattedText t = Text(label, CapFace, CapFontSize, capText);
                dc.DrawText(t, new Point(x + (w - t.WidthIncludingTrailingWhitespace) / 2,
                    1 + (RowHeight - 4 - t.Height) / 2));
                x += w;
            }
        }

        // Descriptions align into a column sized to this card's widest row. The cap only
        // guards against a single very wide chord swallowing the row; at 0.42 it was
        // discarding the card's shared column outright, so rows in one card stopped lining
        // up exactly where the alignment mattered most (pinned mode, where every alternate
        // renders its full modifiers).
        double keysColumn = Math.Min(TextColumn(entry), ActualWidth * 0.58);
        double textX = Math.Max(keysColumn, x + 10);
        if (entry.Recommended)
        {
            FormattedText s = Text("★", SymbolFace, 9.5, star);
            dc.DrawText(s, new Point(textX, (RowHeight - s.Height) / 2));
            textX += s.WidthIncludingTrailingWhitespace + 4;
        }

        double available = Math.Max(20, ActualWidth - textX - RightPadding);
        FormattedText description = Text(entry.Description, TextFace, TextFontSize, body);
        description.MaxTextWidth = available;
        description.MaxLineCount = MaxDescriptionLines;
        description.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(description, new Point(textX, (ActualHeight - description.Height) / 2));

        // Hover affordance: a small accent ▶ at the right edge of an executable row, so
        // "click runs this" is visible at the row itself instead of only in the footer.
        // (This is what the Enter/Leave invalidations at the bottom of this file exist
        // for.) Drawn last and hover-only; on the rare full-width description it covers
        // the final characters while the mouse is over the row, which is the standard
        // hover-overlay trade (Explorer's checkboxes do the same).
        if (IsMouseOver && entry.Executable)
        {
            Brush chipBase = TryFindResource("KpCardBg") as Brush ?? Brushes.DimGray;
            Brush chipWash = TryFindResource("KpAccentSubtle") as Brush ?? Brushes.SlateBlue;
            Brush accent = TryFindResource("KpAccent") as Brush ?? Brushes.RoyalBlue;
            const double chip = 16;
            var badge = new Rect(ActualWidth - chip - 2, (ActualHeight - chip) / 2, chip, chip);
            // Solid base first: the wash is translucent, and over dark hover text alone
            // the glyph was unreadable.
            dc.DrawRoundedRectangle(chipBase, null, badge, 4, 4);
            dc.DrawRoundedRectangle(chipWash, null, badge, 4, 4);
            FormattedText glyph = Text("▶", SymbolFace, 8.5, accent);
            dc.DrawText(glyph, new Point(
                badge.X + (chip - glyph.WidthIncludingTrailingWhitespace) / 2,
                badge.Y + (chip - glyph.Height) / 2));
        }
    }

    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }
}
