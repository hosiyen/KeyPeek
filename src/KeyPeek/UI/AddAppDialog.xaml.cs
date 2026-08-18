using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

using KeyPeek.Core;

namespace KeyPeek.UI;

/// <summary>
/// Two jobs, one dialog: create a new app definition (display name + process), or pick a
/// running process (used by the exclusion list). Typing by hand always works; the running
/// list is a convenience.
/// </summary>
public partial class AddAppDialog : Window
{
    public string DisplayName => DisplayNameBox.Text;
    public string ProcessName => ProcessBox.Text;

    public AddAppDialog(bool processOnly)
    {
        InitializeComponent();
        LocalizeUi.Apply(this);

        if (processOnly)
        {
            Title = L10n.T("Pick a running app");
            Intro.Text = L10n.T("Choose an app to exclude. The overlay never appears while it is focused.");
            DisplayNameBlock.Visibility = Visibility.Collapsed;
            OkButton.Content = L10n.T("Select");
        }

        ProcessList.ItemsSource = Process.GetProcesses()
            .Where(p => p.MainWindowHandle != IntPtr.Zero)
            .Select(p => p.ProcessName)
            .Concat(Process.GetProcesses().Select(p => p.ProcessName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessList.SelectedItem is string name)
            ProcessBox.Text = name;
    }

    private void Process_TextChanged(object sender, TextChangedEventArgs e) =>
        OkButton.IsEnabled = ProcessBox.Text.Trim().Length > 0;

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
