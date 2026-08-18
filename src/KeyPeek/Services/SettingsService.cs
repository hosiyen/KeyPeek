using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyPeek.Core;

namespace KeyPeek.Services;

public sealed record KeyPeekSettings
{
    /// <summary>Which held modifier keys reveal the overlay. Any of "Ctrl", "Win", "Alt",
    /// "Shift". Default: Ctrl, Win, Alt. Shift is off by default because holding Shift
    /// while typing capitals is normal typing, not a lookup gesture.</summary>
    public List<string> TriggerKeys { get; init; } = new() { "Ctrl", "Win", "Alt" };

    /// <summary>Legacy single-trigger field (pre-multi-trigger); honored when present.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerKey { get; init; }

    /// <summary>How long a modifier must be held before the overlay appears.</summary>
    public int HoldDelayMs { get; init; } = 400;

    /// <summary>Legacy per-modifier overrides. No longer applied or shown: one hold time
    /// for every trigger key is simpler and what users expect. Kept so old settings
    /// files still load.</summary>
    public Dictionary<string, int>? HoldDelayOverridesMs { get; init; }

    /// <summary>Show the "Frequently used" strip at the top of the panel.</summary>
    /// <summary>Accent colour: "system" (follow the colour Windows is using — the default),
    /// or one of "indigo", "violet", "teal", "amber".</summary>
    public string Accent { get; init; } = "system";

    /// <summary>Fade the panel in. Off = it appears instantly, which is the fix when
    /// compositing stutters on a busy machine.</summary>
    public bool AnimatePanel { get; init; } = true;

    /// <summary>Where the panel appears: "top", "center" (default) or "bottom". Validated
    /// in Load(); unknown values fall back to centre.</summary>
    public string PanelPosition { get; init; } = PanelPlacement.Center;

    /// <summary>Opt-in: while the panel is open, arrow keys/Enter navigate it instead of
    /// reaching the focused app. Off by default — the default configuration never consumes
    /// a key beyond Esc-while-visible (R2).</summary>
    public bool ExploreMode { get; init; } = false;

    public bool ShowFrequentlyUsed { get; init; } = true;

    /// <summary>Count rows clicked inside the panel to rank that strip (app-interaction
    /// data only — never keystrokes).</summary>
    public bool TrackPanelUsage { get; init; } = true;

    public bool RunAtStartup { get; init; }

    /// <summary>Process names (no .exe) for which the overlay must never appear.</summary>
    public List<string> ExcludedProcesses { get; init; } = new();

    /// <summary>Overlays over borderless-fullscreen windows (games, videos) are suppressed
    /// unless this is set.</summary>
    public bool ShowOverFullscreen { get; init; }

    /// <summary>The downloaded community-library layer: schedule and source.</summary>
    public LibraryUpdateSettings LibraryUpdate { get; init; } = new();

    /// <summary>Fetch each app's official logo from that app's own vendor, once, and cache it
    /// in %LOCALAPPDATA%\KeyPeek\icons. KeyPeek ships no third-party marks; this is how the
    /// library list shows real logos for apps that are not installed on this machine. Off
    /// means the list uses drawn category glyphs and KeyPeek makes no such request.</summary>
    public bool DownloadAppLogos { get; init; } = true;

    /// <summary>The one-time PowerToys Shortcut Guide coexistence offer was shown.</summary>
    public bool PowerToysOfferShown { get; init; }

    /// <summary>First-run welcome already shown. Installs that predate this field
    /// deserialize false and see the welcome once — by design.</summary>
    public bool OnboardingShown { get; init; }

    /// <summary>Overlay theme: "dark", "light", or "system" (follow Windows).</summary>
    public string Theme { get; init; } = "system";

    /// <summary>UI language for KeyPeek's own text: "vi", "en", or "system" (follow the
    /// Windows display language). Shortcut descriptions come from the library data and are
    /// not affected.</summary>
    public string Language { get; init; } = "system";

    /// <summary>Overlay panel opacity, 60–100 percent. Presented in the UI as
    /// "transparency" (100 − this), which is how people describe it; the default is a
    /// fully solid panel.</summary>
    public int OverlayOpacityPercent { get; init; } = 100;

    /// <summary>Settings window placement, "x,y,w,h" (empty = center on screen).</summary>
    public string WindowBounds { get; init; } = "";
}

public sealed record LibraryUpdateSettings
{
    public bool Enabled { get; init; } = true;
    public int IntervalDays { get; init; } = 7;
    /// <summary>Index JSON URL: { "files": ["chrome.yml", ...] }; files are fetched from
    /// the same directory. A read-only GET carrying no user data.</summary>
    public string IndexUrl { get; init; } =
        "https://raw.githubusercontent.com/keypeek-app/library/main/index.json";
}

internal sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Logger _log;

    public string SettingsDirectory { get; }
    public string SettingsPath { get; }
    public KeyPeekSettings Current { get; private set; } = new();

    /// <summary>The trigger keys as a modifier bit mask.</summary>
    public Modifiers TriggerMask { get; private set; } = Modifiers.Ctrl | Modifiers.Win | Modifiers.Alt;


    public event Action? Changed;

    public SettingsService(Logger log)
    {
        _log = log;
        SettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyPeek");
        Directory.CreateDirectory(SettingsDirectory);
        SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<KeyPeekSettings>(File.ReadAllText(SettingsPath), JsonOptions);
                if (loaded is not null)
                    Current = loaded;
            }
            else
            {
                Save(Current); // write defaults so the user has a file to edit
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to read settings ({SettingsPath}): {ex.Message} — using defaults");
            Current = new KeyPeekSettings();
        }

        // Legacy migration: a pre-existing single "triggerKey" wins if no list was given.
        IEnumerable<string> keys = Current.TriggerKeys;
        if (Current.TriggerKey is not null && (Current.TriggerKeys.Count == 0))
            keys = new[] { Current.TriggerKey };

        Modifiers mask = Modifiers.None;
        foreach (string key in keys)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "ctrl" or "control": mask |= Modifiers.Ctrl; break;
                case "win" or "windows" or "meta" or "super": mask |= Modifiers.Win; break;
                case "alt" or "option": mask |= Modifiers.Alt; break;
                case "shift": mask |= Modifiers.Shift; break;
                default: _log.Warn($"Unknown trigger key \"{key}\" ignored (use Ctrl, Win, Alt or Shift)"); break;
            }
        }
        if (mask == Modifiers.None)
        {
            _log.Warn("No valid trigger keys configured — falling back to Ctrl+Win+Alt");
            mask = Modifiers.Ctrl | Modifiers.Win | Modifiers.Alt;
        }
        TriggerMask = mask;

        int delay = Current.HoldDelayMs;
        if (delay is < 150 or > 5000)
        {
            _log.Warn($"holdDelayMs {delay} out of range (150–5000) — using 400");
            Current = Current with { HoldDelayMs = 400 };
        }

        string position = PanelPlacement.Normalize(Current.PanelPosition);
        if (!string.Equals(position, Current.PanelPosition, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(Current.PanelPosition))
                _log.Warn($"Unknown panelPosition \"{Current.PanelPosition}\" — using {position}");
            Current = Current with { PanelPosition = position };
        }

        Changed?.Invoke();
    }

    public void Save(KeyPeekSettings settings)
    {
        Current = settings with { TriggerKey = null }; // drop the legacy field on save
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, JsonOptions));
    }

    /// <summary>Save and re-derive the mask/notify listeners (used by the settings UI).</summary>
    public void SaveAndApply(KeyPeekSettings settings)
    {
        Save(settings);
        Load();
    }

    public bool IsExcluded(string processName) =>
        Current.ExcludedProcesses.Any(p =>
            string.Equals(AppMatcher.NormalizeProcessName(p), AppMatcher.NormalizeProcessName(processName),
                StringComparison.OrdinalIgnoreCase));
}
