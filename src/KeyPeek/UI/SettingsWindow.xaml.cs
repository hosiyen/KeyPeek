using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KeyPeek.Core;
using KeyPeek.Services;
using static KeyPeek.Interop.NativeMethods;

namespace KeyPeek.UI;

/// <summary>
/// The KeyPeek window: Home dashboard, Settings, Shortcut library and Conflicts behind a
/// left nav (Windows 11 Settings idiom). Every setting applies instantly — there is no
/// Save button and no unsaved state.
/// </summary>
public partial class SettingsWindow : Window
{
    // ---- view models ----
    internal sealed record LibRow(
        IReadOnlyList<ChordVm> Chords, string Description, Brush DotBrush,
        Visibility DotVisibility, string DotTooltip, Visibility StarVisibility,
        Visibility EditVisibility, string RowTooltip, ShortcutEntry Entry, string AppKey);

    internal sealed record DetailSectionVm(string Name, List<LibRow> Rows);
    internal sealed record ConflictVm(IReadOnlyList<ChordVm> Chords, string Kind,
        string Line1, string Line2, string Winner);
    internal sealed record SearchAppVm(string AppName, string AppKey, List<LibRow> Rows);

    private readonly SettingsService _settings;
    private readonly LibraryService _library;
    private readonly LibraryDownloader _downloader;
    private readonly UsageTracker _usage;
    private readonly Logger _log;

    private readonly AppIconStore _icons;
    private readonly Action _onIconsArrived;
    private bool _closed;

    private bool _loadingUi = true;
    private int _lastPage;
    private readonly DispatcherTimer _delayTestTimer = new();
    private readonly DispatcherTimer _iconRefresh = new();
    private Dictionary<string, string>? _exePathCache;

    private static readonly (string Label, int Page)[] SettingsIndex =
    {
        ("Trigger keys", 1), ("Hold delay", 1), ("Theme", 1), ("Opacity / transparency", 1),
        ("Explore mode / keyboard navigation", 1), ("Panel position", 1),
        ("Start at sign-in", 1), ("Show over fullscreen apps", 1),
        ("Library updates", 1), ("Excluded apps", 1), ("Conflicts", 3), ("Shortcut library", 2),
        ("Language", 1), ("Download app logos", 1),
    };

    internal SettingsWindow(SettingsService settings, LibraryService library,
        LibraryDownloader downloader, UsageTracker usage, Logger log)
    {
        _settings = settings;
        _library = library;
        _downloader = downloader;
        _usage = usage;
        _log = log;
        _icons = new AppIconStore(settings, log);
        InitializeComponent();

        // Logos land one at a time over a second or two; coalesce them into a single pass
        // instead of twenty.
        _iconRefresh.Interval = TimeSpan.FromMilliseconds(400);
        _iconRefresh.Tick += (_, _) =>
        {
            _iconRefresh.Stop();
            SwapInArrivedIcons();
        };
        // A download can land after the window is gone (they're fire-and-forget and the
        // store outlives nothing else). Closed unsubscribes; the _closed guard covers the
        // race where the event was already queued on the dispatcher.
        _onIconsArrived = () => Dispatcher.InvokeAsync(() =>
        {
            if (_closed)
                return;
            _iconRefresh.Stop();
            _iconRefresh.Start();
        });
        _icons.IconsArrived += _onIconsArrived;

        RestorePlacement();
        LocalizeUi.Apply(this);
        LoadSettingsIntoUi();
        RefreshLibraryList();
        RefreshConflicts();
        RefreshHome();
        Nav.SelectedIndex = 0;
        _loadingUi = false;

        _delayTestTimer.Tick += DelayTest_Elapsed;
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewKeyUp += Window_PreviewKeyUp;

        // Keyboard navigation: Ctrl+1..4 switch pages, Ctrl+F focuses search.
        for (int i = 0; i < 4; i++)
        {
            int page = i;
            InputBindings.Add(new KeyBinding(
                new RelayCommand(() => Nav.SelectedIndex = page),
                Key.D1 + i, ModifierKeys.Control));
        }
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() => GlobalSearch.Focus()), Key.F, ModifierKeys.Control));

        _library.Reloaded += OnLibraryReloaded;
        _settings.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            _library.Reloaded -= OnLibraryReloaded;
            _settings.Changed -= OnSettingsChanged;
            _icons.IconsArrived -= _onIconsArrived;
            _delayTestTimer.Stop();
            _iconRefresh.Stop();
            SavePlacement();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyTitleBarTheme();
    }

    /// <summary>Match the title bar to the theme (Win10 1809+ / Win11).</summary>
    private void ApplyTitleBarTheme() => TitleBar.ApplyTheme(this);

    private void OnSettingsChanged() => Dispatcher.InvokeAsync(() =>
    {
        ApplyTitleBarTheme();
        RefreshHome();
    });

    private void OnLibraryReloaded(LibraryLoadResult _) => Dispatcher.InvokeAsync(() =>
    {
        RefreshLibraryList();
        RefreshConflicts();
        RefreshHome();
    });

    // ================= window placement =================

    private void RestorePlacement()
    {
        // Fit the default size to the screen first: 1020x680 DIP overflows a 1536x864
        // display at 125% scaling.
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);

        string[] parts = _settings.Current.WindowBounds.Split(',');
        if (parts.Length != 4 ||
            !double.TryParse(parts[0], out double x) || !double.TryParse(parts[1], out double y) ||
            !double.TryParse(parts[2], out double w) || !double.TryParse(parts[3], out double h))
            return;
        // Only restore if the saved position still lands on a screen.
        if (w < MinWidth || h < MinHeight ||
            x + w < SystemParameters.VirtualScreenLeft + 80 ||
            y + h < SystemParameters.VirtualScreenTop + 80 ||
            x > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 ||
            y > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80)
            return;
        // Clamp to the work area: a size saved on a bigger screen (or before this clamp
        // existed) must not restore half off the right edge.
        Rect work = SystemParameters.WorkArea;
        w = Math.Min(w, work.Width - 20);
        h = Math.Min(h, work.Height - 20);
        x = Math.Min(Math.Max(x, work.Left), work.Right - w);
        y = Math.Min(Math.Max(y, work.Top), work.Bottom - h);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = x; Top = y; Width = w; Height = h;
    }

    private void SavePlacement()
    {
        if (WindowState != WindowState.Normal)
            return;
        _settings.SaveAndApply(_settings.Current with
        {
            WindowBounds = $"{Left:F0},{Top:F0},{Width:F0},{Height:F0}",
        });
    }

    // ================= navigation =================

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedIndex < 0)
            return;
        ShowPage(Nav.SelectedIndex);
    }

    private void ShowPage(int index)
    {
        _lastPage = index;
        PageHome.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageLibrary.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        PageConflicts.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        PageSearch.Visibility = Visibility.Collapsed;
        if (index == 0)
            RefreshHome();

        // Subtle fade on page change — enough to feel connected, not enough to wait for.
        UIElement? page = index switch
        {
            0 => PageHome, 1 => PageSettings, 2 => PageLibrary, _ => PageConflicts,
        };
        page?.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0.6, 1.0,
                TimeSpan.FromMilliseconds(110)));
    }

    private void GoSettings_Click(object sender, RoutedEventArgs e) => Nav.SelectedIndex = 1;
    private void GoLibrary_Click(object sender, RoutedEventArgs e) => Nav.SelectedIndex = 2;
    private void GoConflicts_Click(object sender, RoutedEventArgs e) => Nav.SelectedIndex = 3;

    // ================= home =================

    private void RefreshHome()
    {
        LibraryLoadResult lib = _library.Current;
        int conflicts = ConflictDetector.Detect(lib.Apps).Count;

        HomeConflictIcon.Text = conflicts == 0 ? "" : "";
        HomeConflictIcon.Foreground = (Brush)(conflicts == 0
            ? FindResource("KpGood") : FindResource("KpWarn"));
        HomeConflictValue.Text = conflicts == 0 ? L10n.T("None") : conflicts.ToString();
        HomeConflictText.Text = conflicts == 0
            ? L10n.T("Every chord resolves to one shortcut.")
            : L10n.T("Chords Windows takes before the app ever sees them.");

        NavConflictBadge.Visibility = conflicts > 0 ? Visibility.Visible : Visibility.Collapsed;
        NavConflictCount.Text = conflicts.ToString();

        HomeLibraryValue.Text = _downloader.LastCheckUtc is { } last ? Relative(last) : L10n.T("Never");
        HomeLibraryText.Text = _downloader.LastCheckUtc is not null
            ? L10n.T("Definitions are fetched over HTTPS and carry no data about you.")
            : L10n.T("KeyPeek has not looked for updated definitions yet.");

        HomeCoverageValue.Text = $"{lib.Apps.Count}";
        HomeCoverageText.Text = string.Format(
            L10n.T("{0} shortcuts, including the ones you added."), $"{lib.TotalShortcuts:N0}");

        HomeTriggerCaps.Chords = new[]
        {
            new ChordVm(KeyDisplay.ModifierLabels(_settings.TriggerMask)),
        };
        HomeTriggerText.Text = string.Format(
            L10n.T("Hold any of them for {0} ms."), _settings.Current.HoldDelayMs);

        string version = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "0.9.0";
        HomeVersion.Text = string.Format(L10n.T("KeyPeek {0} · MIT licensed"), version);
        HomeGreeting.Text = conflicts == 0
            ? L10n.T("Everything looks healthy.")
            : L10n.T("One thing needs a look.");
    }

    private static string Relative(DateTime utc)
    {
        TimeSpan age = DateTime.UtcNow - utc;
        if (age < TimeSpan.FromMinutes(2)) return L10n.T("just now");
        if (age < TimeSpan.FromHours(1)) return string.Format(L10n.T("{0} minutes ago"), (int)age.TotalMinutes);
        if (age < TimeSpan.FromHours(48)) return string.Format(L10n.T("{0} hours ago"), (int)age.TotalHours);
        return string.Format(L10n.T("{0} days ago"), (int)age.TotalDays);
    }

    // ================= settings (instant apply) =================

    private void LoadSettingsIntoUi()
    {
        KeyPeekSettings s = _settings.Current;
        Modifiers mask = _settings.TriggerMask;
        TriggerCtrl.IsChecked = mask.HasFlag(Modifiers.Ctrl);
        TriggerWin.IsChecked = mask.HasFlag(Modifiers.Win);
        TriggerAlt.IsChecked = mask.HasFlag(Modifiers.Alt);
        TriggerShift.IsChecked = mask.HasFlag(Modifiers.Shift);

        DelaySlider.Value = Math.Clamp(s.HoldDelayMs, 200, 800);
        DelayValue.Text = $"{s.HoldDelayMs} ms";

        SyncAccentButtons(s.Accent);
        AnimateToggle.IsChecked = s.AnimatePanel;
        // This used to read "Windows has animations turned off, so the panel appears
        // instantly whatever this says" whenever SystemParameters.ClientAreaAnimation was
        // false. That stopped being true when the overlay was changed to obey its own
        // toggle: the text sat there on this machine telling the user their setting did
        // nothing while the panel faded in front of them. High Contrast is the one case
        // that still overrides, and it overrides for a reason worth stating.
        if (ThemeManager.HighContrast)
            MotionHint.Text = L10n.T("High Contrast is on, so the panel appears instantly — a fade is a brief drop in contrast, which is the thing that mode exists to prevent.");

        SyncPositionButtons(s.PanelPosition);
        SyncLanguageButtons(s.Language);
        ExploreToggle.IsChecked = s.ExploreMode;
        ShowFrequent.IsChecked = s.ShowFrequentlyUsed;
        TrackUsage.IsChecked = s.TrackPanelUsage;
        RefreshUsageSummary();

        SyncThemeButtons(s.Theme);
        // The slider speaks "transparency" (0 = solid); the setting stores opacity.
        int transparency = 100 - Math.Clamp(s.OverlayOpacityPercent, 60, 100);
        OpacitySlider.Value = transparency;
        OpacityValue.Text = $"{transparency}%";

        RunAtStartup.IsChecked = StartupManager.IsEnabled();
        ShowOverFullscreen.IsChecked = s.ShowOverFullscreen;

        UpdatesEnabled.IsChecked = s.LibraryUpdate.Enabled;
        LogosEnabled.IsChecked = s.DownloadAppLogos;
        UpdateInterval.Text = s.LibraryUpdate.IntervalDays.ToString();
        UpdateUrl.Text = s.LibraryUpdate.IndexUrl;
        UpdateStatus.Text = _downloader.LastCheckUtc is { } last
            ? string.Format(L10n.T("Last checked {0}"), Relative(last))
            : L10n.T("Never checked yet");

        RefreshExclusionChips();
    }

    private void SyncThemeButtons(string theme)
    {
        string t = theme.Trim().ToLowerInvariant();
        ThemeDark.IsChecked = t == "dark";
        ThemeLight.IsChecked = t == "light";
        ThemeSystem.IsChecked = t is not ("dark" or "light");
    }

    private void SyncLanguageButtons(string language)
    {
        string l = language.Trim().ToLowerInvariant();
        LangVi.IsChecked = l == "vi";
        LangEn.IsChecked = l == "en";
        LangSystem.IsChecked = l is not ("vi" or "en");
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        string language = ReferenceEquals(sender, LangVi) ? "vi"
            : ReferenceEquals(sender, LangEn) ? "en" : "system";
        SyncLanguageButtons(language);
        // Persist raises settings.Changed, which App turns into L10n.Language; by the time
        // this returns, T() already answers in the new language.
        Persist(s => s with { Language = language });

        // Re-render this window in place: static XAML through the tree walk, dynamic text
        // by running the same builders that produced it. No restart, no flicker.
        LocalizeUi.Apply(this);
        bool wasLoading = _loadingUi;
        _loadingUi = true;
        try
        {
            LoadSettingsIntoUi();
        }
        finally
        {
            _loadingUi = wasLoading;
        }
        RefreshHome();
        RefreshLibraryList();
        RefreshConflicts();
    }

    /// <summary>Single write path — every control calls this; nothing is deferred.</summary>
    private void Persist(Func<KeyPeekSettings, KeyPeekSettings> change)
    {
        if (_loadingUi)
            return;
        _settings.SaveAndApply(change(_settings.Current));
    }

    private void Trigger_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingUi)
            return;
        var triggers = new List<string>();
        if (TriggerCtrl.IsChecked == true) triggers.Add("Ctrl");
        if (TriggerWin.IsChecked == true) triggers.Add("Win");
        if (TriggerAlt.IsChecked == true) triggers.Add("Alt");
        if (TriggerShift.IsChecked == true) triggers.Add("Shift");
        if (triggers.Count == 0)
        {
            // Never leave the app with no way to open the panel.
            ((ToggleButton)sender).IsChecked = true;
            return;
        }
        Persist(s => s with { TriggerKeys = triggers });
        RefreshHome();
    }

    private void DelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DelayValue is null)
            return;
        DelayValue.Text = $"{(int)DelaySlider.Value} ms";
        Persist(s => s with { HoldDelayMs = (int)DelaySlider.Value });
        RefreshHome();
    }

    /// <summary>Accent the search field while it has focus — the one place in the window
    /// where the user types, so it should say so.</summary>
    private void Search_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) =>
        SearchBox.BorderBrush = (Brush)FindResource(
            GlobalSearch.IsKeyboardFocusWithin ? "KpAccent" : "KpLine");

    private void Accent_Click(object sender, RoutedEventArgs e)
    {
        string accent =
            ReferenceEquals(sender, AccentIndigo) ? "indigo" :
            ReferenceEquals(sender, AccentViolet) ? "violet" :
            ReferenceEquals(sender, AccentTeal) ? "teal" :
            ReferenceEquals(sender, AccentAmber) ? "amber" : "system";
        SyncAccentButtons(accent);
        Persist(s => s with { Accent = accent });
        RefreshLibraryList(); // the initial badges are tinted per theme
    }

    private void SyncAccentButtons(string accent)
    {
        string a = accent.Trim().ToLowerInvariant();
        AccentSystem.IsChecked = a is not ("indigo" or "violet" or "teal" or "amber");
        AccentIndigo.IsChecked = a == "indigo";
        AccentViolet.IsChecked = a == "violet";
        AccentTeal.IsChecked = a == "teal";
        AccentAmber.IsChecked = a == "amber";
    }

    private void Motion_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingUi)
            return;
        Persist(s => s with { AnimatePanel = AnimateToggle.IsChecked == true });
    }

    private void Position_Click(object sender, RoutedEventArgs e)
    {
        string position =
            ReferenceEquals(sender, PosTop) ? PanelPlacement.Top :
            ReferenceEquals(sender, PosBottom) ? PanelPlacement.Bottom :
            PanelPlacement.Center;
        SyncPositionButtons(position);
        Persist(s => s with { PanelPosition = position });
    }

    private void SyncPositionButtons(string position)
    {
        string p = PanelPlacement.Normalize(position);
        PosTop.IsChecked = p == PanelPlacement.Top;
        PosCenter.IsChecked = p == PanelPlacement.Center;
        PosBottom.IsChecked = p == PanelPlacement.Bottom;
    }

    private void Explore_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingUi)
            return;
        Persist(s => s with { ExploreMode = ExploreToggle.IsChecked == true });
    }

    private void Suggestions_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingUi)
            return;
        Persist(s => s with
        {
            ShowFrequentlyUsed = ShowFrequent.IsChecked == true,
            TrackPanelUsage = TrackUsage.IsChecked == true,
        });
    }

    private void ClearUsage_Click(object sender, RoutedEventArgs e)
    {
        _usage.Clear();
        RefreshUsageSummary();
    }

    private void RefreshUsageSummary()
    {
        int total = _usage.TotalRecorded;
        UsageSummary.Text = total switch
        {
            0 => L10n.T("Nothing recorded yet."),
            1 => L10n.T("1 panel click recorded in usage.json."),
            _ => string.Format(L10n.T("{0} panel clicks recorded in usage.json."), total),
        };
    }

    private void CommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is UIElement el)
        {
            el.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (_loadingUi)
            return;
        string theme = ReferenceEquals(sender, ThemeDark) ? "dark"
            : ReferenceEquals(sender, ThemeLight) ? "light" : "system";
        SyncThemeButtons(theme);
        Persist(s => s with { Theme = theme });
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue is null)
            return;
        int transparency = (int)OpacitySlider.Value;
        OpacityValue.Text = $"{transparency}%";
        Persist(s => s with { OverlayOpacityPercent = 100 - transparency });
    }

    private void Behavior_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingUi)
            return;
        bool startup = RunAtStartup.IsChecked == true;
        Persist(s => s with
        {
            RunAtStartup = startup,
            ShowOverFullscreen = ShowOverFullscreen.IsChecked == true,
        });
        StartupManager.Apply(startup, _log);
    }

    private void Updates_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingUi)
            return;
        Persist(s => s with
        {
            LibraryUpdate = s.LibraryUpdate with
            {
                Enabled = UpdatesEnabled.IsChecked == true,
                IntervalDays = int.TryParse(UpdateInterval.Text.Trim(), out int d) && d is >= 1 and <= 90
                    ? d : 7,
            },
        });
        UpdateInterval.Text = _settings.Current.LibraryUpdate.IntervalDays.ToString();
    }

    private void Logos_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingUi)
            return;
        Persist(s => s with { DownloadAppLogos = LogosEnabled.IsChecked == true });
        // Turning it on should fill the list in without a restart; turning it off leaves
        // already-cached logos alone (they cost nothing and deleting them would surprise).
        RefreshLibraryList();
    }

    private void UpdateUrl_Committed(object sender, RoutedEventArgs e)
    {
        if (_loadingUi)
            return;
        string url = UpdateUrl.Text.Trim();
        bool valid = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
                     (uri!.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        UpdateUrlError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (valid)
            Persist(s => s with { LibraryUpdate = s.LibraryUpdate with { IndexUrl = url } });
    }

    private void ResetUrl_Click(object sender, RoutedEventArgs e)
    {
        UpdateUrl.Text = new LibraryUpdateSettings().IndexUrl;
        UpdateUrlError.Visibility = Visibility.Collapsed;
        Persist(s => s with { LibraryUpdate = s.LibraryUpdate with { IndexUrl = UpdateUrl.Text } });
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        CheckNowButton.IsEnabled = false;
        HomeCheckNow.IsEnabled = false;
        UpdateStatus.Text = L10n.T("Checking…");
        HomeLibraryText.Text = L10n.T("Checking…");
        string message = await _downloader.CheckNowAsync();
        UpdateStatus.Text = message;
        CheckNowButton.IsEnabled = true;
        HomeCheckNow.IsEnabled = true;
        RefreshHome();
    }

    // ---- excluded apps ----

    private void RefreshExclusionChips()
    {
        var list = _settings.Current.ExcludedProcesses;
        ExclusionChips.ItemsSource = list.ToList();
        ExclusionEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddExclusion(string name)
    {
        name = AppMatcher.NormalizeProcessName(name);
        if (name.Length == 0)
            return;
        var list = _settings.Current.ExcludedProcesses.ToList();
        if (list.Any(p => AppMatcher.NormalizeProcessName(p) == name))
            return;
        list.Add(name);
        _settings.SaveAndApply(_settings.Current with { ExcludedProcesses = list });
        RefreshExclusionChips();
    }

    private void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        AddExclusion(ExclusionInput.Text);
        ExclusionInput.Text = "";
    }

    private void ExclusionInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        AddExclusion(ExclusionInput.Text);
        ExclusionInput.Text = "";
        e.Handled = true;
    }

    private void RemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string name })
            return;
        var list = _settings.Current.ExcludedProcesses
            .Where(p => !string.Equals(p, name, StringComparison.OrdinalIgnoreCase)).ToList();
        _settings.SaveAndApply(_settings.Current with { ExcludedProcesses = list });
        RefreshExclusionChips();
    }

    private void PickExclusion_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddAppDialog(processOnly: true) { Owner = this };
        if (dialog.ShowDialog() == true)
            AddExclusion(dialog.ProcessName);
    }

    // ---- hold-delay test strip ----

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.LeftCtrl or Key.RightCtrl) || e.IsRepeat || PageSettings.Visibility != Visibility.Visible)
            return;
        _delayTestTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, DelaySlider.Value));
        _delayTestTimer.Start();
        DelayTestText.Text = L10n.T("Holding…");
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.LeftCtrl or Key.RightCtrl))
            return;
        _delayTestTimer.Stop();
        DelayTestStrip.Background = (Brush)FindResource("KpSurface2");
        DelayTestText.Text = L10n.T("Hold Ctrl with this window focused to feel the timing.");
    }

    private void DelayTest_Elapsed(object? sender, EventArgs e)
    {
        _delayTestTimer.Stop();
        DelayTestStrip.Background = (Brush)FindResource("KpAccentSubtle");
        DelayTestText.Text = string.Format(L10n.T("That's {0} ms — the panel would appear now."), (int)DelaySlider.Value);
    }

    // ================= library =================

    private AppDefinition? SelectedApp => (AppList.SelectedItem as ListBoxItem)?.Tag as AppDefinition;

    /// <summary>process name → exe path for running processes (icons in the sidebar).</summary>
    /// <summary>
    /// Where to find an executable for each app, so the list can show its real icon.
    ///
    /// Three sources, all of them the user's OWN installed software — KeyPeek ships no
    /// third-party logos, because redistributing other companies' marks is their call to
    /// license, not ours. Apps the user does not have fall back to a lettered badge.
    /// </summary>
    private Dictionary<string, string> ExePaths()
    {
        if (_exePathCache is not null)
            return _exePathCache;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddRegisteredApps(map);
        AddStartMenuApps(map);
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (map.ContainsKey(p.ProcessName))
                    continue;
                IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)p.Id);
                if (handle == IntPtr.Zero)
                    continue;
                try
                {
                    var buffer = new char[1024];
                    uint size = (uint)buffer.Length;
                    if (QueryFullProcessImageNameW(handle, 0, buffer, ref size))
                        map[p.ProcessName] = new string(buffer, 0, (int)size);
                }
                finally { CloseHandle(handle); }
            }
            catch { /* processes come and go; icons are best-effort */ }
            finally { p.Dispose(); }
        }
        _exePathCache = map;
        return map;
    }

    private const double IconSize = 18;

    /// <summary>
    /// The tile in front of an app row, best source first:
    ///
    ///  1. the icon of the app as installed here — the real thing, no network, exact;
    ///  2. the official logo fetched once from that app's own vendor and cached locally
    ///     (see <see cref="AppIconStore"/>: KeyPeek's package contains no third-party marks);
    ///  3. a category glyph we drew, for apps whose vendor publishes no stable icon file.
    ///
    /// A blank gap made the list look broken; the glyph floor means a row is never empty even
    /// offline.
    /// </summary>
    private FrameworkElement AppIcon(AppDefinition app)
    {
        // Web apps (titleRegex) skip the vendor fetch too: their process list would match
        // a BROWSER's row in the sources table, and Gmail wearing the Chrome logo is worse
        // than a drawn envelope. Their glyph classifies by name only, for the same reason.
        if (app.TitleRegex is not null)
            return CategoryBadge.Create(app.AppName, null, IsLightTheme(), IconSize);

        ImageSource? source = IconFor(app)
            ?? (app.IsGlobal || app.IsFallback
                ? null
                : _icons.Get(app.PackageName, app.ProcessNames, app.AppName));
        if (source is null)
            return CategoryBadge.Create(app.AppName, app.ProcessNames, IsLightTheme(), IconSize,
                app.IsFallback);

        var image = new Image
        {
            Width = IconSize,
            Height = IconSize,
            Source = source,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // The extractor asks for 48 px; without this the downscale to 18 is a nearest-neighbour
        // mess on exactly the icons users recognise fastest.
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private bool IsLightTheme() => (TryFindResource("KpBg") as SolidColorBrush)?.Color.R > 128;

    /// <summary>"App Paths" is where installers register an executable so `start winword`
    /// works — a reliable map from process name to full path for anything properly
    /// installed, running or not.</summary>
    private static void AddRegisteredApps(Dictionary<string, string> map)
    {
        const string subKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
        foreach (Microsoft.Win32.RegistryKey root in new[]
                 { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        {
            try
            {
                using Microsoft.Win32.RegistryKey? apps = root.OpenSubKey(subKey);
                if (apps is null)
                    continue;
                foreach (string name in apps.GetSubKeyNames())
                {
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string process = Path.GetFileNameWithoutExtension(name);
                    if (map.ContainsKey(process))
                        continue;
                    using Microsoft.Win32.RegistryKey? entry = apps.OpenSubKey(name);
                    if (entry?.GetValue(null) is string path && path.Length > 0)
                        map[process] = path.Trim('"');
                }
            }
            catch (Exception) { /* icons are best-effort */ }
        }
    }

    /// <summary>Start-Menu shortcuts catch what App Paths misses (Discord, Slack, Figma and
    /// most per-user installs). Shortcut targets are read through the shell, one level of
    /// folders deep, and the whole thing is cached for the window's lifetime.</summary>
    private static void AddStartMenuApps(Dictionary<string, string> map)
    {
        string[] roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };

        object? shell = null;
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return;
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return;

            foreach (string root in roots.Where(Directory.Exists))
            foreach (string link in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).Take(400))
            {
                try
                {
                    object? shortcut = shellType.InvokeMember("CreateShortcut",
                        System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { link });
                    if (shortcut?.GetType().InvokeMember("TargetPath",
                            System.Reflection.BindingFlags.GetProperty, null, shortcut, null) is not string target)
                        continue;
                    if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
                        continue;
                    string process = Path.GetFileNameWithoutExtension(target);
                    if (!map.ContainsKey(process))
                        map[process] = target;
                }
                catch (Exception) { /* one bad shortcut must not cost the rest */ }
            }
        }
        catch (Exception) { /* no shell automation: fall back to lettered badges */ }
        finally
        {
            if (shell is not null && System.Runtime.InteropServices.Marshal.IsComObject(shell))
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    private ImageSource? IconFor(AppDefinition app)
    {
        if (app.IsGlobal)
            return IconExtractor.WindowsIcon();
        // A titleRegex definition is a WEB app (Gmail, YouTube…): its process list names
        // browsers, and the browser's icon on a Gmail row says the wrong thing twice —
        // it is not Gmail's mark, and it makes the row look like a duplicate of Edge.
        if (app.TitleRegex is not null)
            return null;
        var paths = ExePaths();
        foreach (string process in app.ProcessNames)
            if (paths.TryGetValue(AppMatcher.NormalizeProcessName(process), out string? path))
                return IconExtractor.ForFile(path);
        return null;
    }

    private void RefreshLibraryList()
    {
        LibraryLoadResult lib = _library.Current;
        string? selectedKey = SelectedApp?.MergeKey;

        var items = new List<ListBoxItem>();
        void AddGroupLabel(string text)
        {
            items.Add(new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = text,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("KpSectionLabel"),
                    Margin = new Thickness(2, 6, 0, 2),
                },
                IsEnabled = false,
                Focusable = false,
            });
        }

        var globals = lib.Apps.Where(a => a.IsGlobal)
            .OrderBy(a => a.AppName, StringComparer.OrdinalIgnoreCase).ToList();
        var rest = lib.Apps.Where(a => !a.IsGlobal)
            .OrderBy(a => a.AppName, StringComparer.OrdinalIgnoreCase).ToList();

        if (globals.Count > 0)
        {
            AddGroupLabel(L10n.T("SYSTEM"));
            foreach (AppDefinition app in globals)
                items.Add(AppRow(app));
        }
        if (rest.Count > 0)
        {
            AddGroupLabel(L10n.T("APPS"));
            foreach (AppDefinition app in rest)
                items.Add(AppRow(app));
        }

        AppList.ItemsSource = items;
        LibraryTotals.Text = string.Format(L10n.T("{0} apps · {1} shortcuts"), lib.Apps.Count, $"{lib.TotalShortcuts:N0}");

        // selectedKey must be non-null here: group-label rows carry a null Tag, and
        // "null == null" would match a label and open the pane empty.
        int restore = selectedKey is null
            ? -1
            : items.FindIndex(i => (i.Tag as AppDefinition)?.MergeKey == selectedKey);
        int target = restore >= 0 ? restore : items.FindIndex(i => i.Tag is AppDefinition);
        // After container generation, otherwise the selection silently doesn't stick and
        // the detail pane opens empty.
        Dispatcher.InvokeAsync(() =>
        {
            if (target >= 0 && AppList.SelectedIndex != target)
                AppList.SelectedIndex = target;
        }, DispatcherPriority.Loaded);
    }

    private ListBoxItem AppRow(AppDefinition app)
    {
        var panel = new DockPanel();
        var count = new TextBlock
        {
            Text = app.ShortcutCount.ToString(),
            FontSize = 11.5,
            Foreground = (Brush)FindResource("KpTextFaint"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
        };
        DockPanel.SetDock(count, Dock.Right);
        panel.Children.Add(count);

        FrameworkElement icon = AppIcon(app);
        DockPanel.SetDock(icon, Dock.Left);
        panel.Children.Add(icon);

        panel.Children.Add(new TextBlock
        {
            Text = app.AppName,
            FontSize = 13,
            Foreground = (Brush)FindResource("KpTextBody"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        return new ListBoxItem { Content = panel, Tag = app };
    }

    /// <summary>
    /// A logo has finished downloading: put it into the rows that are already on screen.
    ///
    /// Rebuilding the list instead would be two lines shorter and would throw away the
    /// user's scroll position a second or two after they started scrolling — the logos
    /// arrive exactly while someone is looking through the list. Swapping the icon child
    /// leaves selection, scroll offset and focus untouched.
    /// </summary>
    private void SwapInArrivedIcons()
    {
        foreach (ListBoxItem row in AppList.Items.OfType<ListBoxItem>())
        {
            if (row.Tag is not AppDefinition app || row.Content is not DockPanel panel)
                continue;
            int at = panel.Children.IndexOf(panel.Children.OfType<FrameworkElement>()
                .FirstOrDefault(c => DockPanel.GetDock(c) == Dock.Left));
            if (at < 0)
                continue;
            FrameworkElement fresh = AppIcon(app);
            if (fresh is not Image && panel.Children[at] is not Image)
                continue; // still a glyph — nothing arrived for this row
            DockPanel.SetDock(fresh, Dock.Left);
            panel.Children.RemoveAt(at);
            panel.Children.Insert(at, fresh);
        }
    }

    private void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AppDefinition? app = SelectedApp;
        bool hasOverrides = app is not null && app.Sections.SelectMany(s => s.Shortcuts)
            .Any(x => x.Layer == LibraryLayer.User && x.OverridesShipped);

        EditFileButton.IsEnabled = app is not null;
        ResetOverridesButton.IsEnabled = hasOverrides;
        ResetOverridesButton.ToolTip = hasOverrides
            ? L10n.T("Remove your entries that shadow shipped ones for this app")
            : L10n.T("You haven't overridden anything for this app");

        if (app is null)
        {
            DetailName.Text = L10n.T("Select an app");
            DetailSummary.Text = "";
            DetailSections.ItemsSource = null;
            DetailIcon.Source = null;
            DetailBadge.Visibility = Visibility.Collapsed;
            return;
        }

        DetailIcon.Source = IconFor(app);
        DetailName.Text = app.AppName;
        DetailBadge.Visibility = app.IsGlobal ? Visibility.Visible : Visibility.Collapsed;

        var sections = app.DisplaySections();
        var entries = sections.SelectMany(s => s.Shortcuts).ToList();
        int discovered = entries.Count(x => x.Layer == LibraryLayer.Discovered);
        int downloaded = entries.Count(x => x.Layer == LibraryLayer.Downloaded);
        int overridden = entries.Count(x => x.Layer == LibraryLayer.User);
        var summary = new List<string> { $"{entries.Count} shortcuts" };
        var sources = new List<string>();
        if (entries.Any(x => x.Layer == LibraryLayer.Bundled)) sources.Add("bundled");
        if (downloaded > 0) sources.Add($"{downloaded} downloaded");
        if (discovered > 0) sources.Add($"{discovered} discovered");
        if (overridden > 0) sources.Add($"{overridden} overridden");
        summary.Add(string.Join(", ", sources));
        if (!app.IsGlobal)
            summary.Add(string.Format(L10n.T("matches {0}"), string.Join(", ", app.ProcessNames)));
        if (app.VerifiedAgainst is { } verified)
            summary.Add(string.Format(L10n.T("checked against {0}"), verified));
        DetailSummary.Text = string.Join("  ·  ", summary);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var vms = sections
            .Select(s => new DetailSectionVm(ShortcutL10n.T(s.Name),
                s.Shortcuts.Select(x => ToRow(x, app)).ToList()))
            .ToList();
        DetailSections.ItemsSource = vms;
        if (clock.ElapsedMilliseconds > 120)
            _log.Info($"Library detail for {app.AppName} ({entries.Count} rows) built in {clock.ElapsedMilliseconds} ms");
        DetailEmpty.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailEmpty.Text = L10n.T("This definition has no shortcuts yet. Use “My shortcuts” to add some.");

        ErrorList.ItemsSource = _library.Current.Errors
            .Where(err => string.Equals(Path.GetFileName(err.File), Path.GetFileName(app.SourceFile),
                StringComparison.OrdinalIgnoreCase))
            .Select(err => $"⚠ {err}")
            .ToList();
    }

    private LibRow ToRow(ShortcutEntry entry, AppDefinition app)
    {
        EntryVm vm = KeyDisplay.ToEntryVm(entry, Modifiers.None);
        bool differs = entry.Layer != LibraryLayer.Bundled;
        Brush dot = entry.Layer switch
        {
            LibraryLayer.User => (Brush)FindResource("KpDotUser"),
            LibraryLayer.Downloaded => (Brush)FindResource("KpDotDownloaded"),
            _ => (Brush)FindResource("KpDotDiscovered"),
        };
        string dotTip = entry.Layer switch
        {
            LibraryLayer.User => string.Format(L10n.T("Your override · {0}"), entry.Origin),
            LibraryLayer.Downloaded => string.Format(L10n.T("From the community library · {0}"), entry.Origin),
            LibraryLayer.Discovered => string.Format(L10n.T("Discovered from the app's own config · {0}"), entry.Origin),
            _ => L10n.T("Shipped with KeyPeek"),
        };
        string description = ShortcutL10n.T(entry.Description);
        string tooltip = entry.Note is null ? description
            : description + Environment.NewLine + entry.Note;

        return new LibRow(vm.Chords, description, dot,
            differs ? Visibility.Visible : Visibility.Hidden, dotTip,
            entry.Recommended ? Visibility.Visible : Visibility.Collapsed,
            Visibility.Visible, tooltip, entry, app.MergeKey);
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        _exePathCache = null;
        _library.Reload();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        // Create it first. The folder is made at startup, but a user can delete it while
        // the app runs — and then this button handed them a Windows error box about a
        // location being unavailable instead of just opening the folder.
        try { Directory.CreateDirectory(_library.LibraryDirectory); }
        catch (Exception ex) { _log.Warn($"Could not create {_library.LibraryDirectory}: {ex.Message}"); }
        OpenPath(_library.LibraryDirectory);
    }
    private void OpenLog_Click(object sender, RoutedEventArgs e) => OpenPath(_log.LogPath);

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp is { } app)
            OpenPath(app.SourceFile);
    }

    /// <summary>Pencil on a row: copy the entry into the user layer (creating the file if
    /// needed) and open it, so the edit lands where updates can never overwrite it.</summary>
    private void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LibRow row } || SelectedApp is not { } app)
            return;
        try
        {
            // UserManifest owns the naming rule, including the case this used to crash on:
            // a definition with no process names at all (the "Common shortcuts" fallback).
            string path = Path.Combine(_library.LibraryDirectory, UserManifest.FileNameFor(app));

            AppDefinition target;
            var scratch = new List<LibraryError>();
            AppDefinition? existing = File.Exists(path)
                ? PowerToysManifestLoader.LoadFile(path, scratch) : null;
            if (existing is null)
            {
                target = app with
                {
                    SourceFile = path,
                    Sections = new[] { new ShortcutSection("My shortcuts", new[] { row.Entry }) },
                };
            }
            else if (existing.Sections.SelectMany(s => s.Shortcuts)
                     .Any(x => x.KeysText == row.Entry.KeysText))
            {
                target = existing; // already overridden — just open it
            }
            else
            {
                var sections = existing.Sections.ToList();
                sections[0] = new ShortcutSection(sections[0].Name,
                    sections[0].Shortcuts.Append(row.Entry).ToList(), sections[0].Table);
                target = existing with { Sections = sections };
            }

            File.WriteAllText(path, PowerToysManifestLoader.Serialize(target));
            _library.Reload();
            OpenPath(path);
            _log.Info($"Row copied to the user layer: {row.Entry.KeysText} → {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            _log.Error($"Could not create the user override: {ex.Message}");
        }
    }

    /// <summary>Open the form-based editor for the selected app — the path that doesn't
    /// require knowing what YAML is.</summary>
    private void MyShortcuts_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp is not { } app)
            return;
        new EditShortcutsDialog(app, _library, _log) { Owner = this }.ShowDialog();
        RefreshLibraryList();
    }

    private void ResetOverrides_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp is not { } app)
            return;
        var overrides = app.Sections.SelectMany(s => s.Shortcuts)
            .Where(x => x.Layer == LibraryLayer.User && x.OverridesShipped)
            .ToList();
        if (overrides.Count == 0)
            return;

        foreach (var fileGroup in overrides.GroupBy(x => x.Origin))
        {
            string path = Path.Combine(_library.LibraryDirectory, fileGroup.Key);
            if (!File.Exists(path))
                continue;
            var scratch = new List<LibraryError>();
            bool isJson = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            AppDefinition? def = isJson
                ? LibraryLoader.LoadFile(path, scratch)
                : PowerToysManifestLoader.LoadFile(path, scratch);
            if (def is null)
                continue;

            var removeKeys = fileGroup.Select(x => x.KeysText)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var kept = def.Sections
                .Select(s => new ShortcutSection(s.Name,
                    s.Shortcuts.Where(x => !removeKeys.Contains(x.KeysText)).ToList(), s.Table))
                .Where(s => s.Shortcuts.Count > 0)
                .ToList();

            if (kept.Count == 0)
                File.Delete(path);
            else
                File.WriteAllText(path, isJson
                    ? LibraryLoader.Serialize(def with { Sections = kept })
                    : PowerToysManifestLoader.Serialize(def with { Sections = kept }));
            _log.Info($"Reset {removeKeys.Count} override(s) in {fileGroup.Key}");
        }
        _library.Reload();
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddAppDialog(processOnly: false) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            string process = AppMatcher.NormalizeProcessName(dialog.ProcessName);
            string display = string.IsNullOrWhiteSpace(dialog.DisplayName)
                ? char.ToUpperInvariant(process[0]) + process[1..] : dialog.DisplayName.Trim();
            string path = Path.Combine(_library.LibraryDirectory, process + ".yml");
            if (!File.Exists(path))
                File.WriteAllText(path, PowerToysManifestLoader.StarterFile(display, process));
            _library.Reload();
            OpenPath(path);
        }
        catch (Exception ex)
        {
            _log.Error($"Could not create the definition: {ex.Message}");
        }
    }

    // ================= conflicts =================

    private void RefreshConflicts()
    {
        var conflicts = ConflictDetector.Detect(_library.Current.Apps);
        ConflictList.ItemsSource = conflicts.Select(c =>
        {
            // c.Chord is a display string (can hold placeholder glyphs) — never re-parse it.
            var chords = c.Chords.Select(k => KeyDisplay.ToChordVm(k, Modifiers.None)).ToList();
            string kind = c.Kind == ConflictKind.AppVsGlobal
                ? L10n.T("App vs system-wide") : L10n.T("Two definitions match the same app");
            return new ConflictVm(chords, kind,
                $"{c.AppA}: {c.DescriptionA}",
                $"{c.AppB}: {c.DescriptionB}",
                c.Kind == ConflictKind.AppVsGlobal
                    ? string.Format(L10n.T("The app's own shortcut usually wins while {0} is focused."), c.AppA)
                    : L10n.T("Whichever definition loads first wins — give one a titleRegex to separate them."));
        }).ToList();
        ConflictEmpty.Visibility = conflicts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================= search =================

    private void GlobalSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = GlobalSearch.Text.Trim();
        GlobalSearchPlaceholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (query.Length == 0)
        {
            ShowPage(_lastPage);
            return;
        }

        SearchTitle.Text = string.Format(L10n.T("Results for “{0}”"), query);
        // Match the English label AND its translation, display the translation: a search
        // for "ngôn ngữ" must find the Language card on a Vietnamese UI.
        var settingsHits = SettingsIndex
            .Where(i => i.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        L10n.T(i.Label).Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(i => L10n.T(i.Label)).ToList();
        SearchSettingsResults.ItemsSource = settingsHits;

        var appHits = new List<SearchAppVm>();
        foreach (AppDefinition app in _library.Current.Apps)
        {
            var rows = ShortcutFilter.Apply(app.DisplaySections(), Modifiers.None, query)
                .SelectMany(s => s.Shortcuts)
                .Take(8)
                .Select(x => ToRow(x, app))
                .ToList();
            if (rows.Count > 0)
                appHits.Add(new SearchAppVm(app.AppName, app.MergeKey, rows));
        }
        SearchLibraryResults.ItemsSource = appHits.Take(12).ToList();
        SearchEmpty.Visibility = settingsHits.Count == 0 && appHits.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        PageHome.Visibility = PageSettings.Visibility = PageLibrary.Visibility =
            PageConflicts.Visibility = Visibility.Collapsed;
        PageSearch.Visibility = Visibility.Visible;
    }

    private void SearchSetting_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string label })
        {
            int page = SettingsIndex.FirstOrDefault(i =>
                i.Label == label || L10n.T(i.Label) == label).Page;
            GlobalSearch.Text = "";
            Nav.SelectedIndex = page;
        }
    }

    private void SearchApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string key })
            return;
        GlobalSearch.Text = "";
        Nav.SelectedIndex = 2;
        var items = (List<ListBoxItem>?)AppList.ItemsSource;
        int index = items?.FindIndex(i => (i.Tag as AppDefinition)?.MergeKey == key) ?? -1;
        if (index >= 0)
            AppList.SelectedIndex = index;
    }

    // ================= shared =================

    private void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not open {path}: {ex.Message}");
        }
    }

    /// <summary>Minimal ICommand for the window's keyboard shortcuts.</summary>
    private sealed class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _run;
        public RelayCommand(Action run) => _run = run;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _run();
    }
}
