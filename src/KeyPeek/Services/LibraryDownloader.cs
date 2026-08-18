using System.IO;
using System.Net.Http;
using System.Text.Json;
using KeyPeek.Core;

namespace KeyPeek.Services;

/// <summary>
/// The downloaded library layer: fetches the community library over HTTPS on the user's
/// schedule (default weekly; off switch and "check now" in settings) and swaps the cache
/// wholesale on success. This is how corrections reach existing installs without a build.
///
/// PRIVACY: this is the ONLY network traffic KeyPeek ever produces — a read-only GET of
/// an index and some YAML files. Nothing about the user or their machine is sent.
/// Failures are logged quietly and never interrupt the user.
/// </summary>
internal sealed class LibraryDownloader : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly SettingsService _settings;
    private readonly LibraryService _library;
    private readonly Logger _log;
    private readonly System.Threading.Timer _timer;
    private readonly string _stampPath;
    private int _checking;

    public LibraryDownloader(SettingsService settings, LibraryService library, Logger log)
    {
        _settings = settings;
        _library = library;
        _log = log;
        _stampPath = Path.Combine(library.DownloadedDirectory, ".last-check");

        // A gentle scheduler: first look shortly after startup, then re-evaluate every
        // 6 hours whether the configured interval has elapsed.
        _timer = new System.Threading.Timer(_ => Tick(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromHours(6));
    }

    public DateTime? LastCheckUtc
    {
        get
        {
            try
            {
                return File.Exists(_stampPath)
                    ? DateTime.Parse(File.ReadAllText(_stampPath), null,
                        System.Globalization.DateTimeStyles.RoundtripKind)
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private void Tick()
    {
        LibraryUpdateSettings cfg = _settings.Current.LibraryUpdate;
        if (!cfg.Enabled)
            return;
        DateTime? last = LastCheckUtc;
        if (last is not null && DateTime.UtcNow - last < TimeSpan.FromDays(Math.Max(1, cfg.IntervalDays)))
            return;
        _ = CheckNowAsync();
    }

    /// <summary>Fetch the index and all files; swap the cache only if everything parsed.
    /// Returns a one-line human summary (shown by the settings window's Check now).</summary>
    public async Task<string> CheckNowAsync()
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1)
            return "An update check is already running.";
        try
        {
            string indexUrl = _settings.Current.LibraryUpdate.IndexUrl;
            string baseUrl = indexUrl[..(indexUrl.LastIndexOf('/') + 1)];

            string indexJson = await Http.GetStringAsync(indexUrl);
            var index = JsonSerializer.Deserialize<IndexDto>(indexJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (index?.Files is null || index.Files.Count == 0)
                return Quiet("the index listed no files");

            // Validate names defensively: plain *.yml names only, no paths.
            var files = new List<string>();
            foreach (string f in index.Files)
            {
                if (string.IsNullOrWhiteSpace(f) || f.Contains('/') || f.Contains('\\') ||
                    f.Contains("..") || !f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                    return Quiet($"index contains an invalid file name: \"{f}\"");
                files.Add(f.Trim());
            }

            // Download and parse-validate everything BEFORE touching the cache: a bad
            // publish must never wipe a good cache.
            var contents = new Dictionary<string, string>();
            foreach (string file in files)
            {
                string text = await Http.GetStringAsync(baseUrl + Uri.EscapeDataString(file));
                var scratch = new List<LibraryError>();
                if (PowerToysManifestLoader.LoadText(text, file, scratch) is null)
                    return Quiet($"downloaded {file} does not parse ({scratch.FirstOrDefault()})");
                contents[file] = text;
            }

            foreach (string stale in Directory.EnumerateFiles(_library.DownloadedDirectory, "*.yml"))
                File.Delete(stale);
            foreach ((string file, string text) in contents)
                File.WriteAllText(Path.Combine(_library.DownloadedDirectory, file), text);

            WriteStamp();
            _library.Reload();
            _log.Info($"Community library updated: {contents.Count} definitions");
            return $"Updated {contents.Count} definitions from the community library.";
        }
        catch (Exception ex)
        {
            return Quiet(ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }

        string Quiet(string reason)
        {
            // Quiet by design: no dialogs, no balloons — a failed check must not nag.
            WriteStamp();
            _log.Info($"Library update check: nothing applied ({reason})");
            return $"No update applied: {reason}";
        }
    }

    private void WriteStamp()
    {
        try { File.WriteAllText(_stampPath, DateTime.UtcNow.ToString("o")); } catch { }
    }

    private sealed class IndexDto
    {
        public List<string>? Files { get; set; }
    }

    public void Dispose() => _timer.Dispose();
}
