using System.Threading.Channels;
using System.Windows;
using KeyPeek.Core;
using KeyPeek.Hooks;
using KeyPeek.Services;
using KeyPeek.UI;

namespace KeyPeek;

public partial class App : Application
{
    private const string QuitEventName = "Local\\KeyPeek.Quit";
    private const string ShowSettingsEventName = "Local\\KeyPeek.ShowSettings";
    private const string MutexName = "Local\\KeyPeek.SingleInstance";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _quitEvent;
    private RegisteredWaitHandle? _quitWait;
    private EventWaitHandle? _showSettingsEvent;
    private RegisteredWaitHandle? _showSettingsWait;
    private Logger? _log;
    private SettingsService? _settings;
    private LibraryService? _library;
    private LibraryDownloader? _downloader;
    private UI.ThemeManager? _theme;
    private UsageTracker? _usage;
    private ForegroundWatcher? _foregroundWatcher;
    private KeyboardMouseHookService? _hooks;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // `KeyPeek --quit` asks an already-running instance to exit cleanly (used by
        // scripts and the verify harness; harmless if nothing is running). The target
        // instance creates its quit event late in startup, so retry briefly — otherwise
        // a --quit racing a fresh launch silently does nothing.
        if (e.Args.Contains("--quit"))
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(4);
            bool instanceExists = Mutex.TryOpenExisting(MutexName, out Mutex? other);
            other?.Dispose();
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    EventWaitHandle.OpenExisting(QuitEventName).Set();
                    break;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    if (!instanceExists)
                        break; // nothing is running at all
                    Thread.Sleep(150); // an instance is starting up; wait for its event
                    instanceExists = Mutex.TryOpenExisting(MutexName, out other);
                    other?.Dispose();
                }
            }
            Shutdown();
            return;
        }

        // `KeyPeek --validate [dir]` checks every library file and reports errors to the
        // console, without starting the tray app. Exit code = number of errors.
        int validateIdx = Array.IndexOf(e.Args, "--validate");
        if (validateIdx >= 0)
        {
            string? dir = validateIdx + 1 < e.Args.Length && !e.Args[validateIdx + 1].StartsWith("--")
                ? e.Args[validateIdx + 1] : null;
            Shutdown(ValidateLibrary.Run(dir));
            return;
        }

        // `KeyPeek --convert-yaml <dir>` rewrites legacy JSON definitions as PowerToys
        // YAML manifests (used to convert the curated seeds to the native format).
        int convertIdx = Array.IndexOf(e.Args, "--convert-yaml");
        if (convertIdx >= 0 && convertIdx + 1 < e.Args.Length)
        {
            Shutdown(ValidateLibrary.ConvertToYaml(e.Args[convertIdx + 1]));
            return;
        }

        // `KeyPeek --migrate <dir>` rewrites legacy flat-section files in <dir> into the
        // v2 per-modifier table format (used once to convert the bundled seed data).
        int migrateIdx = Array.IndexOf(e.Args, "--migrate");
        if (migrateIdx >= 0 && migrateIdx + 1 < e.Args.Length)
        {
            Shutdown(ValidateLibrary.Migrate(e.Args[migrateIdx + 1]));
            return;
        }

        // `KeyPeek --settings` opens the window of the running instance (used by the
        // Start-Menu shortcut and the verify scripts).
        // `KeyPeek --harvest <process>`: read a running app's own UI for shortcuts and
        // write a draft to the reports folder. A research tool, not part of normal use.
        int harvestArg = Array.IndexOf(e.Args, "--harvest");
        if (harvestArg >= 0)
        {
            string target = harvestArg + 1 < e.Args.Length ? e.Args[harvestArg + 1] : "";
            Shutdown(target.Length > 0 ? ShortcutHarvester.Run(target) : 2);
            return;
        }

        bool wantsSettings = e.Args.Contains("--settings");

        _instanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            if (wantsSettings)
            {
                try { EventWaitHandle.OpenExisting(ShowSettingsEventName).Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
            }
            else
            {
                MessageBox.Show("KeyPeek is already running — look for its icon in the system tray.",
                    "KeyPeek", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }

        _log = new Logger();
        _log.Info($"KeyPeek starting (pid {Environment.ProcessId})");

        _settings = new SettingsService(_log);
        _settings.Load();

        // Language before any window or the tray exists, so nothing renders in the wrong
        // one. Kept in sync on every settings change — the chips in Settings write the
        // setting, and this is the single place that turns it into a language.
        L10n.Language = L10n.Resolve(_settings.Current.Language,
            System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        _settings.Changed += () => L10n.Language = L10n.Resolve(_settings.Current.Language,
            System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

        // Uninstall the global hooks on every exit path, including crashes. (If the
        // process is killed outright, the OS removes low-level hooks itself.)
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupHooks();
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            _log?.Error($"Unhandled exception: {ex.ExceptionObject}");
            CleanupHooks();
        };
        DispatcherUnhandledException += (_, ex) =>
        {
            _log?.Error($"Unhandled dispatcher exception: {ex.Exception}");
            CleanupHooks();
            // not marking Handled: better to crash visibly than run in a broken state
        };
        Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => Dispatcher.BeginInvoke((Action)Shutdown);

        _theme = new UI.ThemeManager(_settings, _log); // before any window exists
        _library = new LibraryService(_log);

        var channel = Channel.CreateUnbounded<InputEvent>(new UnboundedChannelOptions { SingleReader = true });
        var foreground = new ForegroundAppService(_log);
        _usage = new UsageTracker(_log);
        var presenter = new OverlayPresenter(Dispatcher, _library, _settings, _usage, _log);
        var controller = new HoldController(channel, presenter, foreground, _settings, _log);
        presenter.PinRequested += controller.RequestPin;
        presenter.ExecuteRequested += controller.RequestExecute;
        controller.Start();
        presenter.Preload();

        // Build each app's panel when the user switches to it, not when they ask for it:
        // a cold show spends ~500 ms in WPF layout, which is the whole latency budget.
        _foregroundWatcher = new ForegroundWatcher(foreground, presenter.Precompute, Dispatcher, _log);
        // Warm content is only valid for the library and settings it was built from.
        _library.Reloaded += _ => presenter.InvalidatePrecomputed();
        _settings.Changed += presenter.InvalidatePrecomputed;

        _hooks = new KeyboardMouseHookService(channel.Writer, _log);
        _hooks.Install();

        _tray = new TrayIcon(_settings, _library, _log,
            openSettings: OpenSettingsWindow,
            quit: () => Dispatcher.BeginInvoke((Action)Shutdown));

        controller.ExecutionBlocked += processName => Dispatcher.BeginInvoke(() =>
            _tray?.ShowNotice("KeyPeek", string.Format(
                L10n.T("\"{0}\" is running as administrator — Windows doesn't allow sending keys to it from a normal app. Press the shortcut on the keyboard instead."),
                processName)));

        _downloader = new LibraryDownloader(_settings, _library, _log);

        // One-time dialogs, chained so they can never stack on top of each other, and
        // deferred to idle so startup isn't blocked by a message box.
        Dispatcher.InvokeAsync(() =>
        {
            PowerToysCoexistence.MaybeOffer(_settings!, _log!);
            MaybeShowWelcome(wantsSettings);
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(_quitEvent,
            (_, _) => Dispatcher.BeginInvoke((Action)Shutdown), null, Timeout.Infinite, false);

        _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        _showSettingsWait = ThreadPool.RegisterWaitForSingleObject(_showSettingsEvent,
            (_, _) => Dispatcher.BeginInvoke((Action)OpenSettingsWindow), null, Timeout.Infinite, false);

        if (wantsSettings)
            Dispatcher.BeginInvoke((Action)OpenSettingsWindow);

        _log.Info($"Ready — hold [{_settings.TriggerMask}] for {_settings.Current.HoldDelayMs} ms to trigger");
    }

    /// <summary>First run: say hello once, then never again. Installs that predate the flag
    /// see it once too — better than never explaining the app to an existing user.</summary>
    private void MaybeShowWelcome(bool wantsSettings)
    {
        if (!Core.OnboardingPolicy.ShouldShowWelcome(_settings!.Current.OnboardingShown, wantsSettings))
            return;

        // Persist first: if the window throws or the user kills the app, they still don't
        // get greeted again on every launch.
        _settings.SaveAndApply(_settings.Current with { OnboardingShown = true });
        _log!.Info("First run — showing welcome");

        var welcome = new UI.WelcomeWindow(_settings.TriggerMask);
        welcome.ShowDialog();
        if (welcome.SettingsRequested)
            OpenSettingsWindow();
    }

    private UI.SettingsWindow? _settingsWindow;

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is null || !_settingsWindow.IsLoaded)
            _settingsWindow = new UI.SettingsWindow(_settings!, _library!, _downloader!, _usage!, _log!);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void CleanupHooks()
    {
        try { _hooks?.Dispose(); } catch { /* best effort on the way down */ }
        // Also drop the tray icon on crash paths — otherwise Windows keeps a ghost "K"
        // in the tray until the mouse happens to pass over it.
        try { _tray?.Dispose(); } catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CleanupHooks();
        // Unhooked here rather than in CleanupHooks: UnhookWinEvent only works on the thread
        // that installed it, and CleanupHooks runs from crash handlers on other threads.
        _foregroundWatcher?.Dispose();
        _tray?.Dispose();
        _downloader?.Dispose();
        _theme?.Dispose();
        _library?.Dispose();
        _quitWait?.Unregister(null);
        _quitEvent?.Dispose();
        _showSettingsWait?.Unregister(null);
        _showSettingsEvent?.Dispose();
        _log?.Info("KeyPeek exited cleanly");
        _log?.Dispose();
        if (_instanceMutex is not null)
        {
            _instanceMutex.ReleaseMutex();
            _instanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
