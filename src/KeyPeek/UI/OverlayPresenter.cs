using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;
using KeyPeek.Core;
using KeyPeek.Hooks;
using KeyPeek.Services;
using static KeyPeek.Interop.NativeMethods;

namespace KeyPeek.UI;

/// <summary>
/// Bridges the hold controller (consumer thread) and the overlay window (UI thread).
/// All UI work is marshalled through the dispatcher; the only cross-thread reads are
/// the panel rect (lock) and the Esc-intercept flag (volatile).
/// </summary>
internal sealed class OverlayPresenter : IOverlayPresenter
{
    private readonly Dispatcher _dispatcher;
    private readonly LibraryService _library;
    private readonly SettingsService _settings;
    private readonly Logger _log;

    private OverlayWindow? _window;
    private bool _visible; // dispatcher thread only
    private bool _pinned;  // dispatcher thread only

    private readonly object _rectLock = new();
    private RECT _panelRect;
    private bool _rectValid;

    // Current content state (dispatcher thread only)
    private ForegroundApp? _app;
    private AppDefinition? _match;
    private AppDefinition? _fallback;
    // Explore-mode selection: the visible zone-card rows flattened in reading order, plus
    // the index each card starts at (for Left/Right card jumps). The Frequent strip is not
    // included — its chips use a different template that can't show the selection.
    private readonly List<EntryVm> _flatEntries = new();
    private ExploreSelection? _selection;
    private PrecomputedContent? _precomputed;
    private IReadOnlyList<ShortcutSection> _systemSections = Array.Empty<ShortcutSection>();
    private string _systemAppName = "Windows";
    private ImageSource? _appIcon;
    private Modifiers _held;
    private string? _query;

    // Geometry state for the current hold: the target monitor, its DPI scale, and how
    // many card columns each zone currently needs (drives the panel width).
    private MONITORINFO _monitorInfo;
    private double _scale = 1.0;
    private bool _monitorValid;
    private int _appCols;
    private int _sysCols;

    /// <summary>Wired to HoldController.RequestPin / RequestExecute by App.</summary>
    /// <summary>Pin the panel. The flag says whether it was pinned to type in the search
    /// box — that pin owns the keyboard, so a modifier press must not undo it.</summary>
    public event Action<bool>? PinRequested;
    public event Action<IReadOnlyList<KeyChord>>? ExecuteRequested;

    private readonly UsageTracker _usage;

    public OverlayPresenter(Dispatcher dispatcher, LibraryService library, SettingsService settings,
        UsageTracker usage, Logger log)
    {
        _dispatcher = dispatcher;
        _library = library;
        _settings = settings;
        _usage = usage;
        _log = log;
        // Zone titles, footer and hints are baked into the warm content at build time, in
        // whatever language was current then.
        L10n.Changed += InvalidatePrecomputed;
    }

    /// <summary>Create AND warm the overlay ahead of time: instantiate the window, build
    /// real content VMs, and force an off-screen measure/arrange pass. Without this the
    /// first hold pays window construction + template instantiation + JIT — measured at
    /// 164–820 ms against a &lt;100 ms budget. After warm-up, shows are ~20–40 ms.</summary>
    public void Preload() =>
        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                _window ??= CreateWindow();
                _window.EnsureCreated();

                // Warm BOTH big system tables (Win is the largest and was the slow one).
                ResolveContent(new ForegroundApp(IntPtr.Zero, "", null, "", false));
                _pinned = false;
                _query = null;
                foreach (Modifiers held in new[] { Modifiers.Win, Modifiers.Ctrl })
                {
                    _held = held;
                    RefreshContent();
                    _window.Measure(new System.Windows.Size(1220, 900));
                    _window.Arrange(new System.Windows.Rect(0, 0, 1220, 900));
                }
                _log.Info("Overlay preloaded (warm)");
            }
            catch (Exception ex)
            {
                _log.Warn($"Overlay preload failed (first show will be slower): {ex.Message}");
            }
        }, DispatcherPriority.Background);

    /// <summary>Build and lay out the panel for an app the user just switched to, so the
    /// hold that follows only has to make an already-measured window visible. Fire-and-
    /// forget at Background priority: if it doesn't finish in time, the show path does the
    /// work itself exactly as before.</summary>
    public void Precompute(ForegroundApp app) =>
        _dispatcher.InvokeAsync(() => PrecomputeCore(app), DispatcherPriority.Background);

    private void PrecomputeCore(ForegroundApp app)
    {
        if (_visible || _pinned)
            return; // never fight a panel that's on screen
        if (HoldController.HoldInProgress)
            return; // a press is already in flight — HideCore re-warms once it's over
        if (!PrecomputedContent.IsWarmable(app.ProcessName, app.IsDesktop, app.IsSelf))
            return;

        // Warm the filter the user is most likely to hold. Warming every trigger would cost
        // a full layout pass each, on every app switch, for tables they may never open.
        Modifiers held = PrimaryTrigger();
        if (_precomputed?.Matches(app.Hwnd, app.ProcessName, app.Title, held) == true)
            return; // already warm for this window

        try
        {
            long t0 = Stopwatch.GetTimestamp();
            _window ??= CreateWindow();
            _window.EnsureCreated();
            _held = held;
            _query = null;
            ResolveContent(app);
            RefreshContent();
            // The expensive part: realizing a row element per shortcut and measuring its
            // text. UpdateLayout forces it to happen now instead of when the window is
            // shown. Sized like Preload; the real show may pick a slightly different width,
            // but re-measuring an existing tree is cheap next to building one.
            _window.WarmLayout(new System.Windows.Size(1220, 900));
            _precomputed = new PrecomputedContent(app.Hwnd, app.ProcessName, app.Title ?? "", held);
            _log.Info($"Warmed {app.ProcessName} in {(Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency:F0} ms");
        }
        catch (Exception ex)
        {
            _precomputed = null;
            _log.Warn($"Warm-up failed for {app.ProcessName}: {ex.Message}");
        }
    }

    /// <summary>Ctrl when it's a trigger (by far the most held), else the first one set.</summary>
    private Modifiers PrimaryTrigger()
    {
        Modifiers mask = _settings.TriggerMask;
        foreach (Modifiers candidate in new[] { Modifiers.Ctrl, Modifiers.Win, Modifiers.Alt, Modifiers.Shift })
            if (mask.HasFlag(candidate))
                return candidate;
        return Modifiers.Ctrl;
    }

    /// <summary>Warm content is only as good as the library and settings it was built from.</summary>
    public void InvalidatePrecomputed() => _dispatcher.InvokeAsync(() => _precomputed = null);

    public void Show(ForegroundApp app, Modifiers held) =>
        _dispatcher.InvokeAsync(() => ShowCore(app, held));

    public void UpdateFilter(Modifiers held) =>
        _dispatcher.InvokeAsync(() =>
        {
            _held = held;
            if (_visible && !_pinned)
            {
                RefreshContent();
                _log.Info($"Overlay filter → {KeyDisplay.ModifierLogText(held)}");
            }
        });

    public void Hide(HideReason reason) =>
        _dispatcher.InvokeAsync(() => HideCore(reason));

    public void UnpinToHold(Modifiers held) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (!_visible || !_pinned)
                return;
            _pinned = false;
            _held = held;
            _query = null;
            _window!.ExitSearchMode();
            // Back under the keyboard: Explore keys are ours again while the panel is up.
            KeyboardMouseHookService.InterceptExplore = _settings.Current.ExploreMode;
            RefreshContent();
            _log.Info($"Overlay unpinned → following {KeyDisplay.ModifierLogText(held)}");
        });

    public void ExploreKey(int vk) =>
        _dispatcher.InvokeAsync(() => ExploreKeyCore(vk));

    public bool IsPointInsideOverlay(int x, int y)
    {
        lock (_rectLock)
        {
            return _rectValid &&
                   x >= _panelRect.Left && x < _panelRect.Right &&
                   y >= _panelRect.Top && y < _panelRect.Bottom;
        }
    }

    // ---- dispatcher thread from here on ----

    private OverlayWindow CreateWindow()
    {
        var window = new OverlayWindow();
        window.FrameReport = message => _log.Info(message);
        window.PanelClicked += OnPanelClicked;
        window.SearchClicked += OnSearchClicked;
        window.SearchTextChanged += OnSearchTextChanged;
        window.EntryClicked += OnEntryClicked;
        window.CreateDefinitionRequested += OnCreateDefinition;
        return window;
    }

    /// <summary>One-click correction loop. With a real library repo configured this
    /// opens a prefilled issue; while the repo URL is still the placeholder (it 404s),
    /// it saves a prefilled report file locally and opens that instead — never a dead link.</summary>

    private readonly Dictionary<string, string?> _versionCache = new(StringComparer.OrdinalIgnoreCase);

    private string? ExeVersion()
    {
        string? path = _app?.ExePath;
        if (path is null)
            return null;
        if (_versionCache.TryGetValue(path, out string? cached))
            return cached;
        string? version;
        try
        {
            // Reads version resources from disk — cached so the show path never repeats it.
            version = FileVersionInfo.GetVersionInfo(path).ProductVersion;
        }
        catch
        {
            version = null;
        }
        _versionCache[path] = version;
        return version;
    }

    /// <summary>Staleness, honestly (never a warning): a low-contrast note when the
    /// running app is meaningfully (major-version) newer than the definition.</summary>
    private string? StalenessNote()
    {
        string? verified = _match?.VerifiedAgainst;
        string? current = ExeVersion();
        if (verified is null || current is null)
            return null;
        int? vMajor = LeadingNumber(verified);
        int? cMajor = LeadingNumber(current);
        if (vMajor is null || cMajor is null || cMajor <= vMajor)
            return null;
        string shortCurrent = System.Text.RegularExpressions.Regex.Match(current, @"^[\d.]+").Value.TrimEnd('.');
        return string.Format(L10n.T("checked against {0} · you're on {1}"), verified, shortCurrent);
    }

    private static int? LeadingNumber(string version)
    {
        var m = System.Text.RegularExpressions.Regex.Match(version, @"^\d+");
        return m.Success && int.TryParse(m.Value, out int n) ? n : null;
    }

    private void ShowCore(ForegroundApp app, Modifiers held)
    {
        _held = held;
        _query = null;
        _pinned = false;

        _window ??= CreateWindow();
        _window.EnsureCreated();
        _window.ExitSearchMode();

        // Resolve the monitor that owns the focused window and its DPI first — the
        // panel is placed there and every size below is scaled to it (Per-Monitor V2).
        IntPtr monitor = app.Hwnd != IntPtr.Zero
            ? MonitorFromWindow(app.Hwnd, MONITOR_DEFAULTTONEAREST)
            : MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY);
        _monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(monitor, ref _monitorInfo))
            _monitorInfo.rcWork = new RECT { Left = 0, Top = 0, Right = 1280, Bottom = 800 };
        _scale = 1.0;
        if (GetDpiForMonitor(monitor, 0 /*MDT_EFFECTIVE_DPI*/, out uint dpiX, out _) == 0 && dpiX > 0)
            _scale = dpiX / 96.0;
        _monitorValid = true;

        // Show-path breakdown, logged so a regression names its own culprit instead of
        // showing up as one opaque "slower than it was" number.
        long t0 = Stopwatch.GetTimestamp();
        // Warm hit: the tree for exactly this window and filter is already built AND laid
        // out, so skip straight to showing it. The match includes the window title because
        // definitions can be chosen by TitleRegex (PrecomputedContent).
        bool warm = _precomputed?.Matches(app.Hwnd, app.ProcessName, app.Title, held) == true;
        if (!warm)
        {
            if (_precomputed is { } p)
                _log.Info($"Warm miss: had [{p.ProcessName} hwnd={p.Hwnd} held={p.Held} title=\"{p.Title}\"] " +
                          $"wanted [{app.ProcessName} hwnd={app.Hwnd} held={held} title=\"{app.Title}\"]");
            ResolveContent(app);
        }
        else
        {
            _app = app; // keep the identity fresh (icons/version text read from it)
        }
        long tResolved = Stopwatch.GetTimestamp();
        // Explore's selection highlight is painted by RefreshContent, and it only paints
        // when interception is live — so the flag has to be set BEFORE the refresh, not
        // after the window is shown. Otherwise the panel opens with an armed but invisible
        // selection: Enter runs a row nobody could see, and Up/Home appear to do nothing.
        KeyboardMouseHookService.InterceptExplore = _settings.Current.ExploreMode;
        if (!warm)
            RefreshContent();
        long tContent = Stopwatch.GetTimestamp();

        _window.Show(); // ShowActivated=false + WS_EX_NOACTIVATE: focus stays put
        PositionWindow();
        long tLaid = Stopwatch.GetTimestamp();
        static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;
        _log.Info($"Show cost: resolve {Ms(t0, tResolved):F0} ms, content {Ms(tResolved, tContent):F0} ms, " +
                  $"layout {Ms(tContent, tLaid):F0} ms");

        _visible = true;
        KeyboardMouseHookService.InterceptEsc = true;
        _window.AnimationsEnabled = _settings.Current.AnimatePanel;
        _window.FadeIn();
        _log.Info($"Overlay shown ({(app.ProcessName.Length > 0 ? app.ProcessName : "windows")}) " +
                  $"in {OverlayTiming.ElapsedMs():F0} ms");
        UpdatePanelRectSoon();
    }

    private void HideCore(HideReason reason)
    {
        if (!_visible)
            return;
        _visible = false;
        KeyboardMouseHookService.InterceptEsc = false;
        KeyboardMouseHookService.InterceptExplore = false;

        // The rendered tree is only "warm" for the state it was built in, and everything
        // that happened while the panel was up — narrowing the filter, searching, pinning,
        // clicking a row — replaced it without touching the warm token. Reusing it on the
        // next hold re-served the PREVIOUS hold's table, hero caps and footer. Drop the
        // token here; the re-warm at the end of this method rebuilds it from scratch.
        _precomputed = null;
        lock (_rectLock)
            _rectValid = false;

        bool wasPinned = _pinned;
        _pinned = false;

        OverlayWindow window = _window!;
        window.ExitSearchMode();
        window.FadeOut(() =>
        {
            if (_visible)
                return; // a new Show ran during the fade — leave the panel alone
            window.Hide();

            // Re-warm for the app the user is still in: the next press is most likely to be
            // here, and this is the quietest moment we will get. Also covers the warm
            // skipped while the key was held.
            //
            // AFTER the fade, not before it. Warming rebuilds the panel's visual tree, and
            // the panel is still on screen for the ~100 ms the fade takes: starting it at
            // the top of Hide let the user watch the content change as it dissolved.
            if (_app is { } current)
                Precompute(current);
        });

        // Pinned mode had real focus; give it back to the app the user was in.
        if (wasPinned && _app?.Hwnd is { } hwnd && hwnd != IntPtr.Zero)
            SetForegroundWindow(hwnd);

        // The managed heap goes in the hide line because "is the overlay leaking?" is
        // otherwise unanswerable from outside: a process's private bytes sawtooth with the
        // GC and drift upwards on a machine with no memory pressure, which reads exactly
        // like a slow leak and is not one. Compare THIS number across a few dozen holds —
        // if it returns to its band, nothing is being retained.
        //
        // The heap goes AFTER the closing bracket on purpose: verify-m3/m4/m5 all match
        // /Overlay hidden \(TriggerReleased\)/, so putting it inside the parentheses failed
        // three suites on a working build. Log lines the harness greps are an interface.
        _log.Info($"Overlay hidden ({reason}) — heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB");
    }

    /// <summary>Clicking the panel keeps it: it stops following the trigger key and stays
    /// until the user clicks outside it or presses Esc. Without this, releasing the key
    /// closed the panel the instant they reached for it with the mouse.</summary>
    private void OnPanelClicked()
    {
        if (!_visible || _pinned)
            return;
        _pinned = true;
        KeyboardMouseHookService.InterceptExplore = false; // pinned: the keyboard is theirs
        PinRequested?.Invoke(false); // not for search: a trigger press takes the panel back
        RefreshContent(); // no held modifier now, so the panel shows everything
        _log.Info("Overlay pinned (clicked)");
    }

    private void OnSearchClicked()
    {
        if (!_visible)
            return;
        // Also reached when the panel is ALREADY pinned by a click: that pin becomes a
        // search pin, so the query box keeps the keyboard from here on.
        _pinned = true;
        // Search mode gives the panel real keyboard focus, so Explore must stop consuming
        // navigation keys — a low-level swallow would block them everywhere, including in
        // our own search box, leaving the caret unmovable.
        KeyboardMouseHookService.InterceptExplore = false;
        PinRequested?.Invoke(true); // for search: the keyboard belongs to the text box
        _window!.EnterSearchMode();
        RefreshContent(); // modifier filter off in search mode
        _log.Info("Overlay pinned (search)");
    }

    private void OnSearchTextChanged(string text)
    {
        _query = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (_visible)
            RefreshContent();
    }

    /// <summary>Explore mode: a navigation key the hook consumed. Enter runs the selected
    /// row through the ordinary click path; everything else moves the selection.</summary>
    private void ExploreKeyCore(int vk)
    {
        if (!_visible || _pinned || _selection is null)
            return;

        if (vk == ExplorePolicy.VkEnter)
        {
            EntryVm? selected = SelectedEntry();
            if (selected is { Executable: true }) // display-only rows aren't sendable
                OnEntryClicked(selected);
            return;
        }

        if (_selection.Apply(vk))
            ApplySelectionToWindow();
    }

    private EntryVm? SelectedEntry() =>
        _selection is { HasSelection: true } s && s.Index < _flatEntries.Count
            ? _flatEntries[s.Index]
            : null;

    /// <summary>Pushes the current selection to the window; <paramref name="show"/> false
    /// clears it (mouse users must not see a stray highlight).</summary>
    private void ApplySelectionToWindow(bool show = true) =>
        _window?.SetSelectedEntry(show ? SelectedEntry() : null);

    /// <summary>Armed while a clicked row's confirmation flash is on screen. Blocks a
    /// second click (double-click, or a click on another row) from queueing a second
    /// execution during those 140 ms.</summary>
    private EntryVm? _executeArmed;

    private void OnEntryClicked(EntryVm vm)
    {
        if (!_visible || _executeArmed is not null)
            return;
        // Panel-interaction data only: what the user clicked HERE, never what they type.
        if (_settings.Current.TrackPanelUsage)
            _usage.Record(UsageKey(), string.Join(" ", vm.ChordData.Select(c => c.ToDisplayString())));

        // Paint the clicked row with the selection accent for a beat BEFORE executing.
        // Execute hides the panel, and without this the click read as "the panel
        // vanished" — no confirmation of which row fired, or that anything fired at all.
        _executeArmed = vm;
        _window!.SetSelectedEntry(vm);
        var flash = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        { Interval = TimeSpan.FromMilliseconds(140) };
        flash.Tick += (_, _) =>
        {
            flash.Stop();
            _executeArmed = null;
            // No _visible check: if the trigger was released during the flash the panel is
            // already hiding, but the click's intent was unambiguous — still execute, as
            // an immediate execute would have.
            ExecuteRequested?.Invoke(vm.ChordData);
        };
        flash.Start();
    }

    private string UsageKey() => _match?.MergeKey ?? _app?.ProcessName ?? "";

    /// <summary>Unknown app: open the shortcut editor for it. This used to write a starter
    /// file containing an "Example — replace me" entry and open it in Notepad — which meant
    /// the app immediately appeared to have a Ctrl+S shortcut it does not have, and left
    /// the user editing YAML. The editor writes the file only when they add something real.</summary>
    private void OnCreateDefinition()
    {
        if (_app is null || _app.ProcessName.Length == 0)
            return;

        var draft = new AppDefinition
        {
            AppName = FriendlyName(_app),
            ProcessNames = new[] { AppMatcher.NormalizeProcessName(_app.ProcessName) },
            Sections = Array.Empty<ShortcutSection>(),
            SourceFile = "",
        };

        try
        {
            // Topmost so it isn't hidden behind the (topmost, no-activate) panel, which
            // stays pinned underneath until the user dismisses it.
            var dialog = new EditShortcutsDialog(draft, _library, _log) { Topmost = true };
            dialog.ShowDialog();
            if (_visible)
            {
                ResolveContent(_app); // pick up whatever they just added
                RefreshContent();
            }
            _log.Info($"Shortcut editor opened for {draft.AppName}");
        }
        catch (Exception ex)
        {
            _log.Error($"Could not open the shortcut editor: {ex.Message}");
        }
    }

    private static string FriendlyName(ForegroundApp app)
    {
        string name = app.ProcessName;
        return name.Length switch
        {
            0 => "App",
            1 => name.ToUpperInvariant(),
            _ => char.ToUpperInvariant(name[0]) + name[1..],
        };
    }

    /// <summary>Resolve everything the panel needs for this hold — every state in the
    /// section-3 table lands in one of these branches.</summary>
    private void ResolveContent(ForegroundApp app)
    {
        _app = app;
        LibraryLoadResult lib = _library.Current;

        _match = app.ProcessName.Length > 0
            ? AppMatcher.FindForProcess(lib.Apps, app.ProcessName, app.Title)
            : null; // no foreground window at all → system-wide only

        // No definition for this app? Ctrl+C / Ctrl+F / Ctrl+S still work there, so show
        // the "common to most apps" set rather than an empty panel.
        _fallback = _match is null && !app.IsDesktop && !app.IsSelf && app.ProcessName.Length > 0
            ? AppMatcher.Fallback(lib.Apps)
            : null;

        IReadOnlyList<AppDefinition> globals = AppMatcher.Globals(lib.Apps);
        _systemSections = globals.SelectMany(g => g.Sections).ToList();
        _systemAppName = globals.Count == 1 ? globals[0].AppName : "Windows";
        _appIcon = IconExtractor.ForFile(app.ExePath);
    }

    private void RefreshContent()
    {
        Modifiers filterMods = _pinned ? Modifiers.None : _held;
        ForegroundApp app = _app!;
        ImageSource? windowsIcon = IconExtractor.WindowsIcon();

        // App zone: the app's own definition, or the common-to-most-apps fallback.
        IReadOnlyList<ShortcutSection> appSections =
            _match?.Sections ?? _fallback?.Sections ?? Array.Empty<ShortcutSection>();
        List<SectionCardVm> appCards = ToCards(appSections, filterMods, _query);
        int appCount = appCards.Sum(c => c.Entries.Count);

        // System zone (search filters both zones, R7)
        List<SectionCardVm> sysCards = ToCards(_systemSections, filterMods, _query);
        int sysCount = sysCards.Sum(c => c.Entries.Count);

        string title = _match?.AppName
            ?? (app.IsDesktop ? L10n.T("Desktop")
                : app.ProcessName.Length > 0 ? FriendlyName(app) : "Windows");
        string? subtitle = app.ProcessName.Length > 0 && !app.IsDesktop
            ? app.ProcessName + ".exe"
            : null;
        if (StalenessNote() is { } stale)
            subtitle = subtitle is null ? stale : $"{subtitle}   ·   {stale}";

        string? hint = null;
        bool showCreate = false;
        if (_match is null && !app.IsDesktop && app.ProcessName.Length > 0)
        {
            hint = _fallback is not null
                ? string.Format(L10n.T("No definition for {0} yet — showing shortcuts that work in most apps."), title)
                : L10n.T("No shortcuts defined for this app yet.");
            showCreate = true;
        }
        if (app.IsElevated)
        {
            hint = (hint is null ? "" : hint + "  ") +
                L10n.T("This app is running as administrator — Windows blocks click-to-run against it.");
        }

        // Frequently used: rank the currently visible entries by the user's own panel
        // usage, then by the curated "recommended" flag. Hidden when there's nothing to
        // suggest (no usage yet and nothing recommended) so it never wastes a row.
        var frequent = new List<EntryVm>();
        if (_settings.Current.ShowFrequentlyUsed && !_pinned)
        {
            string usageKey = UsageKey();
            IEnumerable<ShortcutEntry> pool = appSections
                .Concat(_systemSections)
                .SelectMany(s => ShortcutFilter.Apply(new[] { s }, filterMods, _query))
                .SelectMany(s => s.Shortcuts)
                .Where(e => e.Executable);

            frequent = pool
                .GroupBy(e => e.KeysText, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(e => (Entry: e, Stat: _usage.For(usageKey, e.KeysText)))
                .Where(x => x.Stat is not null || x.Entry.Recommended)
                .OrderByDescending(x => x.Stat?.Count ?? 0)
                .ThenByDescending(x => x.Stat?.LastUsedUtc ?? DateTime.MinValue)
                .ThenByDescending(x => x.Entry.Recommended)
                .Take(5) // 6 chips wrapped to a second row on narrower panels

                .Select(x => KeyDisplay.ToEntryVm(x.Entry, filterMods))
                .ToList();
        }

        // Content drives the size (§4): each zone gets only the card columns it needs,
        // and the window is sized to exactly that, so a short table gives a small panel.
        _appCols = appCount == 0 ? 0 : Math.Clamp(appCards.Count, 1, 2);
        // The rail used to be pinned to one column whenever the app zone was present, so on
        // a wide screen it stayed a narrow strip of truncated text next to a roomy app
        // zone. Give it a second column when it has the cards to fill one and the monitor
        // has the width — the app zone keeps priority, the rail just stops being a sliver.
        // Content is also built during warm-up, before any monitor has been resolved — fall
        // back to the primary work area there, or the warmed layout (which the show then
        // reuses) would always be the narrow one.
        double widthDip = _monitorValid
            ? (_monitorInfo.rcWork.Right - _monitorInfo.rcWork.Left) / _scale
            : System.Windows.SystemParameters.WorkArea.Width;
        bool wideEnough = widthDip >= 1450;
        _sysCols = sysCount == 0 ? 0
            : appCount == 0 ? Math.Clamp(sysCards.Count, 1, 2)
            : sysCards.Count >= 2 && wideEnough && _appCols <= 2 ? 2
            : 1;

        var vm = new OverlayVm(
            HeroCaps: _pinned ? Array.Empty<string>() : KeyDisplay.ModifierLabels(_held),
            Title: title,
            Subtitle: subtitle,
            Icon: _appIcon ?? windowsIcon,
            AppZone: appCount > 0
                ? new ZoneVm(_match is null && _fallback is not null
                    ? L10n.T("COMMON IN MOST APPS")
                    : string.Format(L10n.T("IN {0}"), title.ToUpperInvariant()), appCards, appCount)
                : null,
            SystemZone: sysCount > 0
                ? new ZoneVm(L10n.T("SYSTEM-WIDE"), sysCards, sysCount)
                : null,
            Frequent: frequent,
            AppColumns: _appCols,
            SystemColumns: _sysCols,
            HintText: hint,
            ShowCreateButton: showCreate,
            FooterLeft: _pinned
                ? L10n.T("Pinned — type to search · Esc or a click outside closes")
                : L10n.T("Click a shortcut to run it · hold more modifiers to narrow · Esc closes"),
            FooterRight: string.Format(L10n.T("{0} shortcuts"), appCount + sysCount));

        _window!.Apply(vm);
        RebuildSelection(appCards, sysCards);
        // Deliberately NOT repositioning while visible: resizing the window on every
        // modifier/search change reads as jitter. The size chosen at show time stands
        // for the whole hold; zones collapse/expand within it.
        UpdatePanelRectSoon();
    }

    /// <summary>Rebuilds the Explore-mode selection over the rows now on screen. Content
    /// changes on every filter/search keystroke, so selection restarts at the first row —
    /// Enter then always has a sensible target instead of a stale one.</summary>
    private void RebuildSelection(List<SectionCardVm> appCards, List<SectionCardVm> sysCards)
    {
        _flatEntries.Clear();
        var cardStarts = new List<int>();
        foreach (SectionCardVm card in appCards.Concat(sysCards))
        {
            if (card.Entries.Count == 0)
                continue;
            cardStarts.Add(_flatEntries.Count);
            _flatEntries.AddRange(card.Entries);
        }

        _selection = new ExploreSelection(_flatEntries.Count, cardStarts);
        // Only paint a selection when Explore is actually driving; otherwise a stray
        // highlight would appear for mouse users.
        ApplySelectionToWindow(KeyboardMouseHookService.InterceptExplore && !_pinned);
    }

    /// <summary>Size the window to the content's column count and center it on the
    /// stored monitor. 300-DIP card slots + panel chrome; clamped to the work area.</summary>
    private void PositionWindow()
    {
        if (!_monitorValid)
            return;

        double panelDip = 38                                                    // panel padding+border
            + (_appCols > 0 ? _appCols * 300 + OverlayWindow.ZoneSlack : 0)      // card slots + scrollbar
            + (_appCols > 0 && _sysCols > 0 ? 9 : 0)                             // zone separator
            // The rail needs its own scrollbar allowance too: at 30 a two-column rail was
            // 3 DIP short, so the second column wrapped and half the panel sat empty.
            + (_sysCols > 0 ? _sysCols * 300 + OverlayWindow.ZoneSlack + 12 : 0); // rail padding + slack
        panelDip = Math.Max(panelDip, 580);                          // header needs room

        int workW = _monitorInfo.rcWork.Right - _monitorInfo.rcWork.Left;
        int workH = _monitorInfo.rcWork.Bottom - _monitorInfo.rcWork.Top;
        // On a small or heavily scaled screen the ideal width doesn't fit. Drop the app zone
        // to one column and recompute rather than clamping: the clamp took the whole
        // shortfall out of the rail (its column is star-sized, the app column is fixed),
        // which clipped the rail's cards instead of simply showing fewer app columns.
        if (panelDip * _scale > workW * 0.92 && _appCols > 1)
        {
            _appCols = 1;
            panelDip = 38 + (300 + OverlayWindow.ZoneSlack)
                + (_sysCols > 0 ? 9 + _sysCols * 300 + OverlayWindow.ZoneSlack + 12 : 0);
            panelDip = Math.Max(panelDip, 580);
        }

        int cx = Math.Min((int)(panelDip * _scale), (int)(workW * 0.92));
        int maxCy = Math.Min((int)(workH * 0.85), (int)(920 * _scale));

        // The window stays a fixed slab and the PANEL sizes itself inside it. Sizing the
        // window to the panel was tried to shrink the surface a transparent window repaints
        // each frame: it clipped the last row and the footer (the measured height never
        // quite matched the arranged one) and the frame timings did not improve, so it was
        // not worth the bug. The panel is top-aligned; the rest of the window is
        // click-through glass.
        int cy = maxCy;
        // Reserve for everything that is not the zone area: panel padding, header, the hint
        // bar, the suggestions strip (which can wrap to two rows on a narrow panel) and the
        // footer. At 190 the footer fell outside the window on a wrapped strip — the number
        // has to cover the worst case, because the cost of over-reserving is a little empty
        // space and the cost of under-reserving is a control the user cannot see.
        _window!.SetContentMaxHeight(cy / _scale - 265);
        int x = _monitorInfo.rcWork.Left + (workW - cx) / 2;
        int y = PanelPlacement.TopEdge(_settings.Current.PanelPosition,
            _monitorInfo.rcWork.Top, workH, cy, (int)(24 * _scale));

        SetWindowPos(_window.Handle, HWND_TOPMOST, x, y, cx, cy, SWP_NOACTIVATE);
        _log.Info($"Overlay geometry: cols app={_appCols} sys={_sysCols}, panel {panelDip:F0} dip, window {cx}x{cy} @ {x},{y} (scale {_scale:F2})");
    }

    /// <summary>Give every row in a card the same key-column width — its own widest row,
    /// within sane bounds. A fixed width made cards of single-letter shortcuts waste ~90 px
    /// and truncate their descriptions.</summary>
    private static IReadOnlyList<EntryVm> AlignKeysColumn(IReadOnlyList<EntryVm> entries)
    {
        if (entries.Count == 0)
            return entries;
        double widest = entries.Max(e => ShortcutRowElement.KeysWidthOf(e.Chords));
        double column = Math.Clamp(widest + 10, 70, 190);
        return entries.Select(e => e with { KeysColumn = column }).ToList();
    }

    private static List<SectionCardVm> ToCards(IEnumerable<ShortcutSection> sections,
        Modifiers filterMods, string? query)
    {
        // Rows never repeat what the header already shows: hide the held modifiers.
        // Section names are library data, so they go through the corpus table.
        return ShortcutFilter.Apply(sections, filterMods, query)
            .Select(s => new SectionCardVm(ShortcutL10n.T(s.Name),
                AlignKeysColumn(s.Shortcuts.Select(e => KeyDisplay.ToEntryVm(e, filterMods)).ToList())))
            .ToList();
    }

    /// <summary>Recompute the visible panel's screen rect once layout settles. The rect
    /// is what mouse clicks are tested against (inside = interact, outside = dismiss).</summary>
    private void UpdatePanelRectSoon()
    {
        _dispatcher.InvokeAsync(() =>
        {
            if (!_visible)
                return;
            try
            {
                RECT rect = _window!.GetPanelScreenRect();
                lock (_rectLock)
                {
                    _panelRect = rect;
                    _rectValid = true;
                }
            }
            catch
            {
                // layout not ready; the next refresh will try again
            }
        }, DispatcherPriority.Loaded);
    }
}
