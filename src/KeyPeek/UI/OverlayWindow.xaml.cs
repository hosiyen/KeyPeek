using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using static KeyPeek.Interop.NativeMethods;

using KeyPeek.Core;

namespace KeyPeek.UI;

/// <summary>
/// The overlay panel. Never takes focus while in hold mode: ShowActivated=false plus
/// WS_EX_NOACTIVATE means the window under it keeps keyboard focus the entire time
/// (R2/R3), and WS_EX_TOOLWINDOW keeps it out of Alt+Tab. Mouse interaction (scrolling,
/// clicking a shortcut) still works, because mouse input doesn't require activation.
///
/// The one exception is search: clicking the search box deliberately pins the panel and
/// gives it real focus, so typed text goes to the box and nowhere else (R7).
/// </summary>
public partial class OverlayWindow : Window
{
    public IntPtr Handle { get; private set; }

    /// <summary>User clicked the search box (wants to pin + type).</summary>
    public event Action? SearchClicked;
    public event Action<string>? SearchTextChanged;
    /// <summary>User clicked a shortcut row (wants it executed).</summary>
    public event Action<EntryVm>? EntryClicked;
    /// <summary>User clicked "Create definition file" on the unknown-app hint.</summary>
    public event Action? CreateDefinitionRequested;

    public OverlayWindow()
    {
        InitializeComponent();
        RowInvoke = new RelayCommand(vm => EntryClicked?.Invoke((EntryVm)vm!));
        // Static labels ("FREQUENTLY USED", the search placeholder, the empty state).
        // Everything the presenter writes per show goes through L10n.T at build time, and
        // the presenter re-warms on language change, so this window never mixes languages.
        LocalizeUi.Apply(this);
        L10n.Changed += OnLanguageChanged;
        Closed += (_, _) => L10n.Changed -= OnLanguageChanged;
    }

    private void OnLanguageChanged() => Dispatcher.InvokeAsync(() => LocalizeUi.Apply(this));

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLongW(Handle, GWL_EXSTYLE);
        SetWindowLongW(Handle, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    public void EnsureCreated()
    {
        if (Handle == IntPtr.Zero)
            new WindowInteropHelper(this).EnsureHandle();
    }

    /// <summary>Apply a full view state: header, both zones (with adaptive proportions —
    /// content drives the split, and an empty zone hands its space to the other), hints
    /// and footer.</summary>
    /// <summary>Width a zone needs beyond its card slots: scrollbar + layout rounding.</summary>
    public const double ZoneSlack = 26;

    public void Apply(OverlayVm vm)
    {
        HeroCaps.ItemsSource = vm.HeroCaps;
        HeaderTitle.Text = vm.Title;
        HeaderSubtitle.Text = vm.Subtitle ?? "";
        HeaderSubtitle.Visibility = vm.Subtitle is null ? Visibility.Collapsed : Visibility.Visible;
        HeaderIcon.Source = vm.Icon;
        HeaderIcon.Visibility = vm.Icon is null ? Visibility.Collapsed : Visibility.Visible;

        HintBar.Visibility = vm.HintText is null ? Visibility.Collapsed : Visibility.Visible;
        HintText.Text = vm.HintText ?? "";
        CreateDefButton.Visibility = vm.ShowCreateButton ? Visibility.Visible : Visibility.Collapsed;

        FrequentItems.ItemsSource = vm.Frequent;
        FrequentStrip.Visibility = vm.Frequent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool hasApp = vm.AppZone is not null;
        bool hasSys = vm.SystemZone is not null;

        AppZonePanel.Visibility = hasApp ? Visibility.Visible : Visibility.Collapsed;
        SysZonePanel.Visibility = hasSys ? Visibility.Visible : Visibility.Collapsed;
        SepCol.Width = hasApp && hasSys ? GridLength.Auto : new GridLength(0);

        // Columns are fixed 300-DIP card slots; the presenter sizes the whole window to
        // match, so the panel is only ever as wide as its content needs. When only one
        // zone is present it takes the full width; when both are, the rail absorbs any
        // slack left by the minimum panel width.
        if (hasApp && hasSys)
        {
            // Slack matters more than it looks: N cards need exactly N*300, and once the
            // zone scrolls, the scrollbar takes ~17 DIP off the usable width — enough to
            // wrap the last card onto its own row and leave half the zone empty. Reserve
            // the scrollbar plus a rounding pixel or two. Keep in step with PositionWindow.
            AppCol.Width = new GridLength(vm.AppColumns * 300 + ZoneSlack);
            SysCol.Width = new GridLength(1, GridUnitType.Star);
        }
        else if (hasApp || hasSys)
        {
            AppCol.Width = new GridLength(hasApp ? 1 : 0, GridUnitType.Star);
            SysCol.Width = new GridLength(hasSys ? 1 : 0, GridUnitType.Star);
        }
        else
        {
            // Neither zone: leave the first column full-width, or the "nothing matches"
            // message is centred inside a zero-wide column and never seen.
            AppCol.Width = new GridLength(1, GridUnitType.Star);
            SysCol.Width = new GridLength(0);
        }

        AppZoneHeader.Text = vm.AppZone?.Header ?? "";
        AppCards.ItemsSource = vm.AppZone?.Cards;
        SysZoneHeader.Text = vm.SystemZone?.Header ?? L10n.T("SYSTEM-WIDE");

        // The system table (~140 rows) used to be filled a frame late so the panel appeared
        // sooner. That trade is gone: the content is warmed on focus change, so filling now
        // costs almost nothing — and deferring actively broke two things, because the panel
        // is sized to its content and the window to the panel. The rail landing late made
        // the panel jump, and made the window too short for it, clipping the last card and
        // the footer.
        SysCards.ItemsSource = vm.SystemZone?.Cards;

        EmptyState.Visibility = !hasApp && !hasSys ? Visibility.Visible : Visibility.Collapsed;

        // New content, new list: without this the rail keeps the offset from the previous
        // filter and opens scrolled into the middle of a table the user has never seen.
        AppScroll.ScrollToTop();
        SysScroll.ScrollToTop();

        FooterLeft.Text = vm.FooterLeft;
        FooterRight.Text = vm.FooterRight;
    }

    /// <summary>The row Explore mode has selected, or null when the keyboard isn't driving.
    /// Rows bind against this (see IsSelectedConverter) instead of being hunted down in the
    /// visual tree, which would miss the system zone's deferred containers.</summary>
    public static readonly DependencyProperty SelectedEntryProperty = DependencyProperty.Register(
        nameof(SelectedEntry), typeof(EntryVm), typeof(OverlayWindow),
        new PropertyMetadata(null));

    public EntryVm? SelectedEntry
    {
        get => (EntryVm?)GetValue(SelectedEntryProperty);
        set => SetValue(SelectedEntryProperty, value);
    }

    public void SetSelectedEntry(EntryVm? entry) => SelectedEntry = entry;

    /// <summary>
    /// Force the panel's rows to be created and measured now, before the window is shown.
    ///
    /// Measuring the WINDOW does nothing here: a window that has never been shown is
    /// Visibility.Collapsed, and WPF skips layout for collapsed elements entirely. Measuring
    /// the panel element directly bypasses that and is where the cost actually lives —
    /// roughly 150 row elements, each formatting its own text.
    /// </summary>
    public void WarmLayout(Size available)
    {
        PanelBorder.Measure(available);
        PanelBorder.Arrange(new Rect(new Point(0, 0), PanelBorder.DesiredSize));
        PanelBorder.UpdateLayout();

        // Build the fade's bitmap cache now, while nothing is on screen. Creating it at
        // show time cost the first animation frame 30–70 ms — one visible hitch at exactly
        // the moment the user is looking. The cache is dropped when the fade finishes so
        // static text goes back to full ClearType.
        SetPanelCache(true);
    }

    /// <summary>Height (DIP) the panel wants for its current content, 0 before it has been
    /// measured. The window is sized to this: every pixel of a transparent window is copied
    /// to the screen on each animation frame, so a window twice the height of its panel
    /// costs twice as much to fade.</summary>
    public double PanelDesiredHeight => PanelBorder.DesiredSize.Height;

    public void SetContentMaxHeight(double dip) => ZoneGrid.MaxHeight = Math.Max(140, dip);

    // ---- search / pin ----

    /// <summary>Pin mode: temporarily make the window activatable so the search box can
    /// take real keyboard focus. Typed keys go to the box, never to the app underneath.</summary>
    public void EnterSearchMode()
    {
        int ex = GetWindowLongW(Handle, GWL_EXSTYLE);
        SetWindowLongW(Handle, GWL_EXSTYLE, ex & ~WS_EX_NOACTIVATE);
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    /// <summary>Back to pure no-activate observer mode; clears the query.</summary>
    public void ExitSearchMode()
    {
        SearchBox.Text = "";
        SearchPlaceholder.Visibility = Visibility.Visible;
        if (Handle != IntPtr.Zero)
        {
            int ex = GetWindowLongW(Handle, GWL_EXSTYLE);
            SetWindowLongW(Handle, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE);
        }
    }

    private void SearchBorder_MouseDown(object sender, MouseButtonEventArgs e) =>
        SearchClicked?.Invoke();

    /// <summary>A click anywhere on the panel that isn't a row or the search box keeps the
    /// panel open. Before this, letting go of the trigger closed it even if the user had
    /// just clicked into it — so there was no way to hold it open and read it.</summary>
    private void Panel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;
        // Not clicks on the search box. This handler is a PREVIEW, so it runs first, and
        // pinning here made the panel "already pinned" by the time the search box's own
        // handler ran — which then bailed out, and search became unusable.
        if (IsInsideSearch(source))
            return;
        // Not clicks on an executable row either. Pinning drops the modifier filter and
        // re-renders the list while the mouse button is still down, so the RELEASE landed
        // on whatever row the reflow moved under the cursor — clicking "Ctrl+T" could run
        // a different shortcut entirely (worst with a Frequent chip: the strip collapses
        // on pin, and the release hit a random row of the zone below). Remember the
        // pressed row instead; EntryRow_Click runs it only if the release is still on it.
        _pressedEntry = EntryUnder(source);
        if (_pressedEntry is { Executable: true })
            return;
        _pressedEntry = null;
        PanelClicked?.Invoke();
    }

    /// <summary>The row the current mouse press started on (button semantics: a click
    /// only counts when press and release land on the same row).</summary>
    private EntryVm? _pressedEntry;

    private static EntryVm? EntryUnder(DependencyObject source)
    {
        for (DependencyObject? node = source; node is not null;
             node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement { DataContext: EntryVm vm })
                return vm;
        return null;
    }

    private bool IsInsideSearch(DependencyObject source)
    {
        for (DependencyObject? node = source; node is not null;
             node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, SearchBorder))
                return true;
        return false;
    }

    /// <summary>User clicked the panel itself (wants it to stay).</summary>
    public event Action? PanelClicked;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility =
            SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchTextChanged?.Invoke(SearchBox.Text);
    }

    /// <summary>What an automation client (screen reader) invoking a row runs. Bound by the
    /// row template so the drawn element has something to execute — it owns no click logic
    /// itself. Same guard as a mouse click.</summary>
    public ICommand RowInvoke { get; }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public RelayCommand(Action<object?> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => parameter is EntryVm { Executable: true };
        public void Execute(object? parameter)
        {
            if (parameter is EntryVm { Executable: true } vm)
                _execute(vm);
        }
    }

    /// <summary>Row click: run the shortcut. The whole row is the target — it used to
    /// surrender its right-hand strip to a report flag, so a click that landed there did
    /// nothing the user expected. Fires only when the press started on this same row:
    /// a drag that merely ENDS on a row must not run anything.</summary>
    private void EntryRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EntryVm vm })
            return;
        e.Handled = true;
        bool pressedHere = ReferenceEquals(_pressedEntry, vm);
        _pressedEntry = null;
        if (pressedHere && vm.Executable) // display-only rows (prose keys) aren't sendable
            EntryClicked?.Invoke(vm);
    }

    private void CreateDef_Click(object sender, RoutedEventArgs e) =>
        CreateDefinitionRequested?.Invoke();

    // ---- motion: ~200 ms fade with a small upward move (section 4) ----
    //
    // Animations run on the PANEL, not the window, with a temporary BitmapCache: WPF
    // renders AllowsTransparency windows on the CPU and re-uploads the whole surface
    // every animated frame, which stutters on busy machines. Caching the panel as a
    // bitmap makes each frame a cheap transform of a pre-rendered surface; the cache is
    // dropped when the fade completes so static text goes back to full ClearType.

    /// <summary>Counts the frames the fade actually delivers. A 110 ms animation should
    /// produce ~7 frames at 60 Hz; far fewer, or one long gap, is what "it stutters" means
    /// in numbers. Diagnostic — the result goes to the log, not the UI.</summary>
    public Action<string>? FrameReport;

    private void BeginFrameProbe()
    {
        if (FrameReport is null)
            return;
        _probeClock.Restart();
        _probeGaps.Clear();
        _probeLast = 0; _probeWorst = 0; _probeWorstAt = 0;
        if (!_probeAttached)
        {
            _probeAttached = true;
            System.Windows.Media.CompositionTarget.Rendering += OnProbeFrame;
        }
    }

    // The probe used to watch only the first 260 ms, which is how a hitch at +200 ms went
    // un-attributed for days: it reported "a" gap without saying WHEN. It now runs for the
    // whole time the panel is on screen and reports on hide, so a hitch during hover or
    // scrolling is caught by the same instrument as one during the fade.
    private readonly System.Diagnostics.Stopwatch _probeClock = new();
    private readonly List<double> _probeGaps = new();
    private double _probeLast, _probeWorst, _probeWorstAt;
    private bool _probeAttached;

    private void OnProbeFrame(object? sender, EventArgs e)
    {
        double now = _probeClock.Elapsed.TotalMilliseconds;
        if (_probeLast > 0)
        {
            _probeGaps.Add(now - _probeLast);
            if (now - _probeLast > _probeWorst)
            {
                _probeWorst = now - _probeLast;
                _probeWorstAt = _probeLast; // when the freeze STARTED — this places the blame
            }
        }
        _probeLast = now;
    }

    private void EndFrameProbe()
    {
        if (!_probeAttached)
            return;
        _probeAttached = false;
        System.Windows.Media.CompositionTarget.Rendering -= OnProbeFrame;
        // A transparent window costs one frame on first composition (~35 ms here); beyond
        // ~55 ms something else froze and the log should say so before the user has to.
        // The "at" offset names the suspect: <20 ms = first composition; a repeatable
        // offset later = whatever is scheduled there.
        if (FrameReport is not null && _probeGaps.Count > 0 && _probeWorst > 55)
            FrameReport($"Frame stutter: worst gap {_probeWorst:F0} ms at +{_probeWorstAt:F0} ms " +
                        $"over {_probeClock.Elapsed.TotalMilliseconds:F0} ms visible, " +
                        $"median {Median(_probeGaps):F0} ms, {_probeGaps.Count(g => g > 55)} bad of {_probeGaps.Count}");
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    /// <summary>One cache object for the window's whole life, attached and detached rather
    /// than rebuilt. A show/hide attaches a cache twice (fade in, fade out) and a fresh
    /// BitmapCache is a fresh full-panel render surface, which on a 1600×1100 panel is the
    /// largest allocation in the cycle. Measured across 45 hold cycles this moved the net
    /// private-bytes growth from ~28 MB to ~18 MB — within run-to-run noise, so treat it as
    /// "fewer allocations for free", not as the fix for anything.</summary>
    private readonly System.Windows.Media.BitmapCache _panelCache = new() { EnableClearType = false };

    private void SetPanelCache(bool on) =>
        PanelBorder.CacheMode = on ? _panelCache : null;

    // Opacity-only: a slide moves every pixel of a CPU-composited layered window each
    // frame; dropping it halves the per-frame cost and reads calmer, not poorer.
    /// <summary>Whether to animate at all.
    ///
    /// Off in High Contrast (an opacity ramp is a contrast reduction, however brief), and
    /// off when Windows' own "show animations" setting is off — a user who turned that off
    /// has already said what they want, and on a machine where compositing is the thing
    /// that stutters, this is the switch that removes it.</summary>
    /// KeyPeek's own toggle decides. Windows' "show animations" setting is off on plenty of
    /// machines for reasons that have nothing to do with a 110 ms fade on one small panel,
    /// and a user who asks KeyPeek to animate should get it. High Contrast still wins:
    /// there, reduced contrast during a fade is a genuine accessibility problem.
    private bool AnimationsAllowed => AnimationsEnabled && !ThemeManager.HighContrast;

    /// <summary>The user's own setting, pushed in by the presenter (the window has no
    /// access to settings).</summary>
    public bool AnimationsEnabled { get; set; } = true;

    public void FadeIn()
    {
        if (!AnimationsAllowed)
        {
            PanelBorder.BeginAnimation(OpacityProperty, null); // drop any running animation
            PanelBorder.Opacity = 1;
            return;
        }
        SetPanelCache(true); // no-op when the warm-up already built it
        BeginFrameProbe();
        // Start at 35%, not 0. A transparent window is composited on the CPU: the first
        // frame after Show costs 30–70 ms however little else is going on (measured), and
        // starting from invisible turns that into a visible hitch. Starting from mostly
        // visible makes the same delay read as the panel simply arriving.
        PanelBorder.Opacity = 0.35;
        var fade = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(90))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        // The cache used to be dropped here, in Completed, "so static text goes back to
        // full ClearType". The probe finally caught what that cost: Completed fires ~100 ms
        // after the fade visually ends, and the un-cached re-render of the whole panel then
        // froze one frame for 66–92 ms at +193…+235 ms — on nearly every open, which is
        // exactly the stutter the user kept reporting. The cache now stays for the whole
        // visible period and is dropped after Hide, where the re-render costs nobody
        // anything. Text renders through the cache surface the entire time; at panel sizes
        // and DPI here the difference is not visible at arm's length, and a solid frame
        // beats crisper subpixel fringes.
        PanelBorder.BeginAnimation(OpacityProperty, fade);
    }

    public void FadeOut(Action completed)
    {
        EndFrameProbe();
        if (!AnimationsAllowed)
        {
            PanelBorder.BeginAnimation(OpacityProperty, null);
            PanelBorder.Opacity = 0;
            completed();
            return;
        }
        SetPanelCache(true); // no-op now that the cache lives for the whole visible period
        var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(100))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => completed();
        PanelBorder.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>The visible panel's rect in physical screen pixels. The window is bigger
    /// than the panel (transparent glass, click-through); dismiss-on-click-outside must
    /// use the panel, not the window.</summary>
    internal RECT GetPanelScreenRect()
    {
        var topLeft = PanelBorder.PointToScreen(new Point(0, 0));
        var bottomRight = PanelBorder.PointToScreen(new Point(PanelBorder.ActualWidth, PanelBorder.ActualHeight));
        return new RECT
        {
            Left = (int)topLeft.X,
            Top = (int)topLeft.Y,
            Right = (int)bottomRight.X,
            Bottom = (int)bottomRight.Y,
        };
    }
}
