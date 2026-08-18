using System.IO;
using System.Text.Json.Nodes;
using System.Windows;

using KeyPeek.Core;

namespace KeyPeek.Services;

/// <summary>
/// If PowerToys is installed with its Shortcut Guide enabled, two overlays compete for
/// the same job (both react to a held Win key). Offer — once, dismissibly — to turn the
/// PowerToys module off. Never change anything silently.
/// </summary>
internal static class PowerToysCoexistence
{
    public static void MaybeOffer(SettingsService settings, Logger log)
    {
        if (settings.Current.PowerToysOfferShown)
            return;

        string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "PowerToys", "settings.json");
        if (!File.Exists(settingsPath))
            return; // no PowerToys — nothing to offer, and nothing to remember

        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(settingsPath));
            // The module map is { "enabled": { "Shortcut Guide": true, ... } } — the key
            // really does contain a space.
            bool guideEnabled = root?["enabled"]?["Shortcut Guide"]?.GetValue<bool>() ?? false;

            if (guideEnabled)
            {
                MessageBoxResult answer = MessageBox.Show(
                    L10n.T("PowerToys Shortcut Guide is also enabled on this PC. Two shortcut overlays will compete for the same job (especially the Win key).") +
                    "\n\n" +
                    L10n.T("Turn off PowerToys' Shortcut Guide? (PowerToys may need a restart to notice. You can re-enable it any time in PowerToys Settings.)"),
                    "KeyPeek", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (answer == MessageBoxResult.Yes)
                {
                    root!["enabled"]!["Shortcut Guide"] = false;
                    File.WriteAllText(settingsPath, root.ToJsonString());
                    log.Info("PowerToys Shortcut Guide disabled at the user's request");
                }
                else
                {
                    log.Info("PowerToys Shortcut Guide offer declined — keeping both");
                }
            }
        }
        catch (Exception ex)
        {
            log.Warn($"PowerToys coexistence check failed: {ex.Message}");
        }

        // Asked (or nothing to ask): never bring it up again.
        settings.SaveAndApply(settings.Current with { PowerToysOfferShown = true });
    }
}
