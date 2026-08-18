using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using KeyPeek.Core;
using KeyPeek.Services;

namespace KeyPeek.UI;

/// <summary>
/// Edit an app's shortcuts without touching YAML: press the keys, say what they do.
/// Everything is written to the user layer, which updates never overwrite.
/// </summary>
public partial class EditShortcutsDialog : Window
{
    private readonly AppDefinition _mergedApp;
    private readonly LibraryService _library;
    private readonly Logger _log;
    private readonly string _path;

    private AppDefinition _userDef;
    private readonly List<KeyChord> _captured = new();

    /// <summary>Chords deleted here but not yet written. A deletion cannot be expressed as
    /// an absence once <see cref="MergeInDiskChanges"/> starts from the file, so it travels
    /// as its own instruction.</summary>
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);

    internal EditShortcutsDialog(AppDefinition mergedApp, LibraryService library, Logger log)
    {
        InitializeComponent();
        LocalizeUi.Apply(this);
        _mergedApp = mergedApp;
        _library = library;
        _log = log;
        _path = Path.Combine(library.LibraryDirectory, UserManifest.FileNameFor(mergedApp));

        var scratch = new List<LibraryError>();
        _userDef = (File.Exists(_path) ? PowerToysManifestLoader.LoadFile(_path, scratch) : null)
                   ?? UserManifest.CreateFor(mergedApp, _path);

        HeaderTitle.Text = string.Format(L10n.T("My shortcuts for {0}"), mergedApp.AppName);
        SectionBox.ItemsSource = mergedApp.DisplaySections().Select(s => s.Name).Distinct().ToList();
        SectionBox.Text = UserManifest.DefaultSection;
        RefreshList();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBar.ApplyTheme(this);
    }

    // ---- capture ------------------------------------------------------------------------

    private void Capture_Click(object sender, MouseButtonEventArgs e) => CaptureBorder.Focus();

    private void Capture_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CaptureBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("KpAccent");
        if (_captured.Count == 0)
            CapturePlaceholder.Text = L10n.T("Press the shortcut…");
    }

    private void Capture_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CaptureBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("KpLine");
        if (_captured.Count == 0)
            CapturePlaceholder.Text = L10n.T("Click here, then press the shortcut");
    }

    private void Capture_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true; // the box owns every key while it has focus

        // Alt-modified keys arrive as Key.System with the real key in SystemKey; without
        // this, Alt+F would be uncapturable.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _captured.Clear();
            UpdateCaptureUi();
            return;
        }
        if (key is Key.Tab) // let the user leave the box
        {
            e.Handled = false;
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        KeyChord? chord = ChordCapture.FromKeyPress(vk, ToModifiers(Keyboard.Modifiers));
        if (chord is null)
            return; // a lone modifier, or a key the library format cannot spell

        if (_captured.Count >= ChordCapture.MaxChordsPerShortcut)
        {
            CaptureHint.Text = string.Format(L10n.T("A shortcut can be at most {0} steps — Esc to start over."), ChordCapture.MaxChordsPerShortcut);
            return;
        }

        _captured.Add(chord);
        UpdateCaptureUi();
    }

    private static Modifiers ToModifiers(ModifierKeys keys)
    {
        Modifiers mods = Modifiers.None;
        if (keys.HasFlag(ModifierKeys.Control)) mods |= Modifiers.Ctrl;
        if (keys.HasFlag(ModifierKeys.Shift)) mods |= Modifiers.Shift;
        if (keys.HasFlag(ModifierKeys.Alt)) mods |= Modifiers.Alt;
        // ModifierKeys.Windows is never reported by WPF — the shell owns the Win key, so it
        // is absent from Keyboard.Modifiers. Ask the keyboard directly, or every Win chord
        // would be captured as if Win had not been pressed and would then override the
        // wrong shortcut (matching is by rendered chord text).
        if (keys.HasFlag(ModifierKeys.Windows) ||
            Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
            mods |= Modifiers.Win;
        return mods;
    }

    private void UpdateCaptureUi()
    {
        CaptureCaps.Chords = _captured
            .Select((c, i) => KeyDisplay.ToChordVm(c, Modifiers.None, i == 0 ? "" : L10n.T("then")))
            .ToList();
        CapturePlaceholder.Visibility = _captured.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CaptureHint.Text = L10n.T("Esc clears · press a second combination to record a sequence (max 3)");
        Form_Changed(this, new RoutedEventArgs());
    }

    // ---- add / remove -------------------------------------------------------------------

    private void Form_Changed(object sender, RoutedEventArgs e)
    {
        if (AddButton is null)
            return; // fires during InitializeComponent
        bool ready = _captured.Count > 0 && DescriptionBox.Text.Trim().Length > 0;
        AddButton.IsEnabled = ready;
        ShowWarningFor(ready ? KeysText() : null);
    }

    private string KeysText() => string.Join(" ", _captured.Select(c => c.ToDisplayString()));

    /// <summary>Warn, never block: the user may well mean to shadow an existing shortcut.</summary>
    private void ShowWarningFor(string? keysText)
    {
        string? message = null;
        if (keysText is not null)
        {
            ShortcutEntry? clash = _mergedApp.DisplaySections()
                .SelectMany(s => s.Shortcuts)
                .FirstOrDefault(s => string.Equals(s.KeysText, keysText, StringComparison.OrdinalIgnoreCase));
            if (clash is not null)
                message = string.Format(L10n.T("“{0}” already does “{1}” in {2}. Adding it will replace that row."), keysText, clash.Description, _mergedApp.AppName);
            else if (_captured.Count == 1 && _captured[0].Mods.HasFlag(Modifiers.Win))
                message = L10n.T("Windows handles most Win+… shortcuts before an app sees them.");
        }
        Warning.Text = message ?? "";
        Warning.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var entry = new ShortcutEntry
        {
            Chords = _captured.ToList(),
            Description = DescriptionBox.Text.Trim(),
            RawKeys = KeysText(),
            Recommended = RecommendedToggle.IsChecked == true,
            ChordsAreAlternatives = false,
        };

        _userDef = UserManifest.WithEntry(_userDef, entry, SectionBox.Text);
        if (!Save())
            return;

        _captured.Clear();
        DescriptionBox.Text = "";
        RecommendedToggle.IsChecked = false;
        UpdateCaptureUi();
        RefreshList();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string keysText })
            return;
        _userDef = UserManifest.WithoutEntry(_userDef, keysText);
        _removed.Add(keysText);
        if (Save())
            RefreshList();
    }

    /// <summary>Re-read the file before writing. This dialog can sit open for a long time,
    /// and its own "Open the file" button invites a text editor alongside it — blind-writing
    /// a constructor-time snapshot would throw away whatever was saved in between.</summary>
    private void MergeInDiskChanges()
    {
        if (!File.Exists(_path))
            return;
        var scratch = new List<LibraryError>();
        AppDefinition? onDisk = PowerToysManifestLoader.LoadFile(_path, scratch);
        if (onDisk is null || scratch.Count > 0)
            return; // unreadable or broken: keep ours rather than lose edits to a bad parse

        _userDef = UserManifest.MergeOverDisk(onDisk, _userDef, _removed);
    }

    private bool Save()
    {
        try
        {
            MergeInDiskChanges();
            Directory.CreateDirectory(_library.LibraryDirectory);
            File.WriteAllText(_path, PowerToysManifestLoader.Serialize(_userDef));
            // The file now says exactly what we say, so the pending deletions have landed.
            // Keeping them would re-delete a shortcut the user later re-added by hand.
            _removed.Clear();
            // The folder watcher would pick this up in half a second; reload now so the
            // list below and the panel agree immediately.
            _library.Reload();
            _log.Info($"User shortcuts saved: {Path.GetFileName(_path)}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Could not save {_path}: {ex.Message}");
            Warning.Text = string.Format(L10n.T("Could not save: {0}"), ex.Message);
            Warning.Visibility = Visibility.Visible;
            return false;
        }
    }

    private void RefreshList()
    {
        var entries = _userDef.Sections.SelectMany(s => s.Shortcuts).ToList();
        EntryList.ItemsSource = entries.Select(entry => new
        {
            entry.Description,
            entry.KeysText,
            Chords = new[] { KeyDisplay.ToChordVm(entry.Chords[0], Modifiers.None) }
                .Concat(entry.Chords.Skip(1).Select(c => KeyDisplay.ToChordVm(c, Modifiers.None, L10n.T("then"))))
                .ToList(),
        }).ToList();
        ListTitle.Text = entries.Count > 0 ? string.Format(L10n.T("Your shortcuts ({0})"), entries.Count) : L10n.T("Your shortcuts");
        EmptyHint.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(_path))
                Save();
            Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not open {_path}: {ex.Message}");
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
