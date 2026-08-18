using System.Diagnostics;
using System.Drawing;
using System.Windows;
using KeyPeek.Core;
using WinForms = System.Windows.Forms;

namespace KeyPeek.Services;

/// <summary>System tray icon and its context menu (WinForms NotifyIcon; WPF has no native one).</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly Logger _log;

    public TrayIcon(SettingsService settings, LibraryService library, Logger log,
        Action openSettings, Action quit)
    {
        _log = log;

        // A killed or crashed process leaves its tray icon behind until the mouse happens
        // to pass over it, so a few bad exits stack up a row of ghost "K"s (seen on this
        // very machine). Windows offers no API to remove someone else's dead icon; the
        // standard fix is to walk synthetic WM_MOUSEMOVEs across the tray toolbars, which
        // makes the shell re-check each icon's process and drop the dead ones.
        SweepDeadTrayIcons();

        var menu = new WinForms.ContextMenuStrip();
        var open = menu.Items.Add(L10n.T("Open KeyPeek"), null, (_, _) => openSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        var reload = menu.Items.Add(L10n.T("Reload library"), null, (_, _) =>
        {
            var result = library.Reload();
            string text = result.Errors.Count == 0
                ? string.Format(L10n.T("{0} apps, {1} shortcuts."),
                    result.Apps.Count, result.TotalShortcuts)
                : string.Format(L10n.T("{0} apps, {1} shortcuts — {2} error(s), see the log."),
                    result.Apps.Count, result.TotalShortcuts, result.Errors.Count);
            _icon!.ShowBalloonTip(4000, L10n.T("KeyPeek library reloaded"), text,
                result.Errors.Count == 0 ? WinForms.ToolTipIcon.Info : WinForms.ToolTipIcon.Warning);
        });
        var openFolder = menu.Items.Add(L10n.T("Open library folder"), null,
            (_, _) => OpenPath(library.LibraryDirectory));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        var openSettingsFile = menu.Items.Add(L10n.T("Open settings file"), null,
            (_, _) => OpenPath(settings.SettingsPath));
        var openLog = menu.Items.Add(L10n.T("Open log"), null, (_, _) => OpenPath(log.LogPath));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        var exit = menu.Items.Add(L10n.T("Exit"), null, (_, _) => quit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = TooltipFor(settings.TriggerMask),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => openSettings();

        settings.Changed += () => _icon.Text = TooltipFor(settings.TriggerMask);
        // The menu is built once and lives for the whole session — relabel it in place
        // when the user switches language, or the tray stays in the old one until restart.
        L10n.Changed += () =>
        {
            open.Text = L10n.T("Open KeyPeek");
            reload.Text = L10n.T("Reload library");
            openFolder.Text = L10n.T("Open library folder");
            openSettingsFile.Text = L10n.T("Open settings file");
            openLog.Text = L10n.T("Open log");
            exit.Text = L10n.T("Exit");
            _icon.Text = TooltipFor(settings.TriggerMask);
        };

        // Surface library problems without being naggy: one balloon per (re)load, and
        // only for files the USER can fix — noise in discovered/downloaded files is
        // logged, not ballooned.
        library.Reloaded += result =>
        {
            int userErrors = result.Errors.Count(library.IsUserError);
            if (userErrors > 0)
                _icon.ShowBalloonTip(4000, L10n.T("KeyPeek library has errors"),
                    string.Format(
                        L10n.T("{0} problem(s) in your shortcut files — right-click → Open log for details."),
                        userErrors),
                    WinForms.ToolTipIcon.Warning);
        };
    }

    /// <summary>Balloon notification (e.g. click-to-run refused for an elevated app).</summary>
    public void ShowNotice(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.Info);

    private static string TooltipFor(Modifiers mask)
    {
        var keys = new List<string>();
        if (mask.HasFlag(Modifiers.Ctrl)) keys.Add("Ctrl");
        if (mask.HasFlag(Modifiers.Win)) keys.Add("Win");
        if (mask.HasFlag(Modifiers.Alt)) keys.Add("Alt");
        if (mask.HasFlag(Modifiers.Shift)) keys.Add("Shift");
        string text = string.Format(L10n.T("KeyPeek — hold {0} to see shortcuts"),
            string.Join(" / ", keys));
        return text.Length <= 63 ? text : text[..63]; // NotifyIcon tooltip hard limit
    }

    private static void SweepDeadTrayIcons()
    {
        try
        {
            // Visible tray: Shell_TrayWnd > TrayNotifyWnd > SysPager > ToolbarWindow32
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            IntPtr pager = FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
            SweepToolbar(FindWindowEx(pager == IntPtr.Zero ? notify : pager, IntPtr.Zero,
                "ToolbarWindow32", null));

            // Overflow flyout: NotifyIconOverflowWindow > ToolbarWindow32
            IntPtr overflow = FindWindow("NotifyIconOverflowWindow", null);
            SweepToolbar(FindWindowEx(overflow, IntPtr.Zero, "ToolbarWindow32", null));
        }
        catch (Exception)
        {
            // Purely cosmetic: a shell without these windows (or a future Windows that
            // renames them) just skips the sweep.
        }
    }

    private static void SweepToolbar(IntPtr toolbar)
    {
        if (toolbar == IntPtr.Zero)
            return;
        if (!GetClientRect(toolbar, out TRAYRECT r))
            return;
        const uint WM_MOUSEMOVE = 0x0200;
        for (int y = 4; y < r.Bottom; y += 12)
            for (int x = 4; x < r.Right; x += 12)
                SendMessage(toolbar, WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)((y << 16) | (x & 0xFFFF)));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string cls, string? win);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? win);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out TRAYRECT rect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct TRAYRECT { public int Left, Top, Right, Bottom; }

    private static Icon LoadIcon()
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/KeyPeek.ico"))!.Stream;
        using (stream)
            return new Icon(stream);
    }

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

    public void Dispose()
    {
        // Order matters: hiding before disposing is what actually removes the icon from
        // the shell. (A force-killed process can still leave a ghost until the mouse
        // passes over the tray — that is Windows' behavior, not something an app can
        // prevent; scripts/clean-tray.ps1 sweeps those.)
        _icon.Visible = false;
        _icon.Dispose();
    }
}
