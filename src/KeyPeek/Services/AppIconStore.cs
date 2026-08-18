using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using KeyPeek.Core;

namespace KeyPeek.Services;

/// <summary>
/// Official app logos, fetched from the vendor that owns them and cached on this machine
/// (%LOCALAPPDATA%\KeyPeek\icons). KeyPeek's installer contains no third-party marks — see
/// <see cref="OfficialIconSources"/> for why — so the library list gets real logos without
/// KeyPeek redistributing anybody's trademark.
///
/// PRIVACY: a plain GET of an image file, no query string, no cookies, no identifiers. Each
/// icon is fetched once, ever. The whole thing is off if "Download app logos" is unchecked,
/// and deleting the cache folder is a complete reset.
/// </summary>
internal sealed class AppIconStore
{
    private const int MaxBytes = 512 * 1024;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly Logger _log;
    private readonly SettingsService _settings;
    private readonly string _dir;
    private readonly Dictionary<string, BitmapSource?> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keys whose download failed this session — offline, proxy, vendor moved the
    /// file. Not retried until the next window, so a dead network is one failed request per
    /// app, not one per list refresh.</summary>
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised (on a worker thread) after one or more icons arrive, so the list can
    /// redraw. Coalesced by the subscriber, not here.</summary>
    public event Action? IconsArrived;

    public AppIconStore(SettingsService settings, Logger log)
    {
        _settings = settings;
        _log = log;
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyPeek", "icons");
    }

    /// <summary>The cached logo for an app, or null. Never blocks: on a miss it starts the
    /// download and returns null, and <see cref="IconsArrived"/> fires when it lands.</summary>
    public BitmapSource? Get(string? packageName, IReadOnlyList<string> processNames, string appName)
    {
        string key = OfficialIconSources.CacheKey(packageName, processNames, appName);
        bool checkedDisk;
        lock (_memory)
        {
            if (_memory.TryGetValue(key, out BitmapSource? cached) && cached is not null)
                return cached;
            checkedDisk = _memory.ContainsKey(key);
        }

        // A remembered miss is NOT final: the setting may have been off, or the vendor
        // unreachable. Fall through to the fetch check every time — Fetch itself is cheap
        // to decline (setting off, already failed this session, already in flight), and
        // this is what makes flipping "Download app logos" ON fill the list in without a
        // restart instead of serving the session's cached nulls forever.
        if (!checkedDisk)
        {
            BitmapSource? fromDisk = LoadFromDisk(key);
            lock (_memory)
                _memory[key] = fromDisk;
            if (fromDisk is not null)
                return fromDisk;
        }

        if (OfficialIconSources.UrlFor(packageName, processNames) is { } url)
            Fetch(key, url);
        return null;
    }

    private BitmapSource? LoadFromDisk(string key)
    {
        try
        {
            string path = Path.Combine(_dir, key + ".img");
            if (!File.Exists(path))
                return null;
            BitmapSource? decoded = Decode(File.ReadAllBytes(path));
            if (decoded is null)
            {
                // A file we cannot render is worse than no file: it is cached as "no logo"
                // for the rest of the session and never retried. Delete it so the next
                // window fetches again.
                _log.Warn($"Discarding an unreadable cached logo: {key}");
                try { File.Delete(path); } catch (Exception) { /* next run tries again */ }
            }
            return decoded;
        }
        catch (Exception ex)
        {
            _log.Warn($"Cached logo for {key} unreadable: {ex.Message}");
            return null;
        }
    }

    private void Fetch(string key, string url)
    {
        if (!_settings.Current.DownloadAppLogos)
            return;
        // The allow-list is belt and braces: the URL always comes from our own table.
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !OfficialIconSources.AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            _log.Warn($"Refusing to fetch a logo from an unexpected address: {url}");
            return;
        }

        lock (_inFlight)
        {
            if (_failed.Contains(key) || !_inFlight.Add(key))
                return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                using HttpResponseMessage response = await Http.GetAsync(uri);
                response.EnsureSuccessStatusCode();
                string type = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"server sent {type}, not an image");
                if (response.Content.Headers.ContentLength > MaxBytes)
                    throw new InvalidDataException("image is larger than 512 KB");

                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length is 0 or > MaxBytes)
                    throw new InvalidDataException($"image is {bytes.Length} bytes");

                // Decode before writing: a cache file we cannot render is worse than none,
                // because it would never be retried.
                BitmapSource? decoded = Decode(bytes);
                if (decoded is null)
                    throw new InvalidDataException("image did not decode");

                // Write-then-rename: two KeyPeek instances did run side by side during
                // development, and a half-written cache file decodes to nothing and then
                // sits there being "no logo" forever.
                Directory.CreateDirectory(_dir);
                string path = Path.Combine(_dir, key + ".img");
                string temp = path + ".part";
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, overwrite: true);
                lock (_memory)
                    _memory[key] = decoded;
                IconsArrived?.Invoke();
            }
            catch (Exception ex)
            {
                // Offline, proxied, or the vendor moved the file: the app keeps its drawn
                // glyph and we try again next time the window opens.
                lock (_inFlight)
                    _failed.Add(key);
                _log.Info($"No official logo for {key} ({ex.Message})");
            }
            finally
            {
                lock (_inFlight)
                    _inFlight.Remove(key);
            }
        });
    }

    /// <summary>Decode PNG/ICO/JPEG/GIF bytes to a frozen, cross-thread-usable bitmap.
    /// Multi-size .ico files decode to the largest frame WPF picked, which is what we want
    /// for a crisp 18 px row.</summary>
    private static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
