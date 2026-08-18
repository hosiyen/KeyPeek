using Microsoft.Win32;

namespace KeyPeek.Services;

/// <summary>
/// Run-at-startup via the current-user Run key (no admin, no scheduled task, no service).
/// </summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyPeek";

    public static void Apply(bool enable, Logger log)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath)!;
            if (enable)
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            log.Info($"Run at startup {(enable ? "enabled" : "disabled")}");
        }
        catch (Exception ex)
        {
            log.Error($"Could not update the startup entry: {ex.Message}");
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
        catch
        {
            return false;
        }
    }
}
