using System.IO;
using System.Text.Json;

namespace KeyPeek.Services;

/// <summary>
/// Counts what the user does INSIDE KeyPeek — rows they click in the panel — so the
/// "Frequently used" strip can lead with what they actually reach for.
///
/// PRIVACY: this is app-interaction data, not keyboard data. Nothing is recorded unless
/// the user clicks a row in KeyPeek's own panel; free typing and real chord presses are
/// never counted or seen. The file is plain, readable JSON in %APPDATA%\KeyPeek\usage.json
/// and can be deleted at any time (Settings → Clear usage data).
/// </summary>
internal sealed class UsageTracker
{
    public sealed record Stat(int Count, DateTime LastUsedUtc);

    private readonly object _lock = new();
    private readonly Logger _log;
    private readonly string _path;
    private Dictionary<string, Stat> _stats = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public UsageTracker(Logger log)
    {
        _log = log;
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyPeek", "usage.json");
        Load();
    }

    public string FilePath => _path;

    private static string Key(string appKey, string keysText) => $"{appKey}|{keysText}";

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, Stat>>(
                File.ReadAllText(_path), JsonOptions);
            if (loaded is not null)
                _stats = new Dictionary<string, Stat>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _log.Warn($"Usage data unreadable ({ex.Message}) — starting fresh");
        }
    }

    /// <summary>Record one panel interaction (a row the user clicked to run).</summary>
    public void Record(string appKey, string keysText)
    {
        lock (_lock)
        {
            string key = Key(appKey, keysText);
            Stat current = _stats.TryGetValue(key, out Stat? s) ? s : new Stat(0, DateTime.UtcNow);
            _stats[key] = new Stat(current.Count + 1, DateTime.UtcNow);
            Save();
        }
    }

    public Stat? For(string appKey, string keysText)
    {
        lock (_lock)
            return _stats.TryGetValue(Key(appKey, keysText), out Stat? s) ? s : null;
    }

    public int TotalRecorded
    {
        get { lock (_lock) return _stats.Values.Sum(s => s.Count); }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _stats.Clear();
            try { File.Delete(_path); } catch { /* nothing to clear */ }
            _log.Info("Usage data cleared");
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_stats, JsonOptions));
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not save usage data: {ex.Message}");
        }
    }
}
