using System.Windows;
using KeyPeek.Core;

namespace KeyPeek.UI;

/// <summary>
/// Shown once, on the first run. A tray app that starts silently is indistinguishable from
/// one that failed to start — this is the only moment KeyPeek asks for attention.
/// </summary>
public partial class WelcomeWindow : Window
{
    /// <summary>Set when the user chose "Open settings" instead of dismissing.</summary>
    public bool SettingsRequested { get; private set; }

    internal WelcomeWindow(Modifiers triggerMask)
    {
        InitializeComponent();
        LocalizeUi.Apply(this);
        // Show the user's own trigger key, not a hardcoded Ctrl — they may have changed it.
        string cap = KeyDisplay.ModifierLabels(FirstTrigger(triggerMask)).FirstOrDefault() ?? "Ctrl";
        HoldCap.Chords = new[] { new ChordVm(new[] { cap }) };
    }

    /// <summary>Ctrl if it's a trigger (the one everyone knows), else whatever is.</summary>
    private static Modifiers FirstTrigger(Modifiers mask)
    {
        foreach (Modifiers candidate in new[] { Modifiers.Ctrl, Modifiers.Win, Modifiers.Alt, Modifiers.Shift })
            if (mask.HasFlag(candidate))
                return candidate;
        return Modifiers.Ctrl;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBar.ApplyTheme(this);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
