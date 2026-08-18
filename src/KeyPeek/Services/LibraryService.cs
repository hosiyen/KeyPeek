using System.IO;
using System.Reflection;
using KeyPeek.Core;
using KeyPeek.Core.Adapters;

namespace KeyPeek.Services;

/// <summary>
/// Owns the four library layers and their merge (later layers win, per shortcut):
///
///  1. Bundled    — embedded in the exe (PowerToys Shortcut Guide manifests + curated
///                  supplements), read-only, replaced wholesale on app update.
///  2. Downloaded — community library cache (%LOCALAPPDATA%\KeyPeek\downloaded),
///                  refreshed by <see cref="LibraryDownloader"/> on the user's schedule.
///  3. Discovered — read at runtime from apps' own config: the WinGet KeyboardShortcuts
///                  folder if present, plus the VS Code and JetBrains adapters.
///  4. User       — %APPDATA%\KeyPeek\library. Never touched by any update. Wins all.
///
/// A FileSystemWatcher reloads (debounced) when a user file changes; the tray menu also
/// has "Reload library". Loading swaps in an immutable snapshot.
/// </summary>
internal sealed class LibraryService : IDisposable
{
    private readonly Logger _log;
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Threading.Timer _debounce;
    private readonly object _reloadLock = new();
    private volatile LibraryLoadResult _current = LibraryLoadResult.Empty;

    private readonly IDiscoveredAdapter[] _adapters =
    {
        new VsCodeKeybindingsAdapter(),
        new JetBrainsKeymapAdapter(),
    };

    /// <summary>The user layer folder — what "Open library folder" opens.</summary>
    public string LibraryDirectory { get; }
    public string DownloadedDirectory { get; }
    public string WinGetDirectory { get; }

    public LibraryLoadResult Current => _current;
    public event Action<LibraryLoadResult>? Reloaded;

    public LibraryService(Logger log)
    {
        _log = log;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        LibraryDirectory = Path.Combine(appData, "KeyPeek", "library");
        DownloadedDirectory = Path.Combine(localAppData, "KeyPeek", "downloaded");
        WinGetDirectory = Path.Combine(localAppData, "Microsoft", "WinGet", "KeyboardShortcuts");
        Directory.CreateDirectory(LibraryDirectory);
        Directory.CreateDirectory(DownloadedDirectory);

        MigrateUserFolder();
        Reload();

        _debounce = new System.Threading.Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            _watcher = new FileSystemWatcher(LibraryDirectory)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler onChange = (_, _) => _debounce.Change(500, Timeout.Infinite);
            _watcher.Changed += onChange;
            _watcher.Created += onChange;
            _watcher.Deleted += onChange;
            _watcher.Renamed += (_, _) => _debounce.Change(500, Timeout.Infinite);
        }
        catch (Exception ex)
        {
            _log.Warn($"Library watcher unavailable ({ex.Message}) — use the tray's Reload instead");
        }
    }

    /// <summary>Is this error in a file the user themselves can fix?</summary>
    public bool IsUserError(LibraryError error) =>
        error.File.StartsWith(LibraryDirectory, StringComparison.OrdinalIgnoreCase);

    public LibraryLoadResult Reload()
    {
        lock (_reloadLock)
        {
            var errors = new List<LibraryError>();

            IReadOnlyList<AppDefinition> bundled = LoadBundled(errors);
            LibraryLoadResult downloaded = LibraryLoader.LoadDirectory(DownloadedDirectory);
            errors.AddRange(downloaded.Errors);
            IReadOnlyList<AppDefinition> discovered = LoadDiscovered(errors);
            LibraryLoadResult user = LibraryLoader.LoadDirectory(LibraryDirectory);
            errors.AddRange(user.Errors);

            IReadOnlyList<AppDefinition> merged = LibraryMerger.Merge(new[]
            {
                (LibraryLayer.Bundled, bundled),
                (LibraryLayer.Downloaded, downloaded.Apps),
                (LibraryLayer.Discovered, discovered),
                (LibraryLayer.User, user.Apps),
            });

            // Rebuild the by-modifier index over the merged, flat sections.
            var indexed = merged.Select(LibraryMigrator.ToTables).ToList();

            var result = new LibraryLoadResult(indexed, errors);
            _current = result;
            _log.Info($"Library: {indexed.Count} apps, {result.TotalShortcuts} shortcuts " +
                $"(bundled {bundled.Count}, downloaded {downloaded.Apps.Count}, " +
                $"discovered {discovered.Count}, user {user.Apps.Count} files), {errors.Count} error(s)");
            foreach (LibraryError error in errors)
                _log.Warn($"Library: {error}");
            Reloaded?.Invoke(result);
            return result;
        }
    }

    private static IReadOnlyList<AppDefinition> LoadBundled(List<LibraryError> errors)
    {
        var apps = new List<AppDefinition>();
        Assembly asm = Assembly.GetExecutingAssembly();
        foreach (string resource in asm.GetManifestResourceNames()
                     .Where(n => n.StartsWith("bundled/")).OrderBy(n => n))
        {
            using var reader = new StreamReader(asm.GetManifestResourceStream(resource)!);
            AppDefinition? app = PowerToysManifestLoader.LoadText(
                reader.ReadToEnd(), resource, errors);
            if (app is not null)
                apps.Add(app);
        }
        return apps;
    }

    private IReadOnlyList<AppDefinition> LoadDiscovered(List<LibraryError> errors)
    {
        var apps = new List<AppDefinition>();

        // The WinGet/PowerToys curated folder, when present (its index.yml is skipped).
        if (Directory.Exists(WinGetDirectory))
        {
            LibraryLoadResult winget = LibraryLoader.LoadDirectory(WinGetDirectory);
            apps.AddRange(winget.Apps);
            errors.AddRange(winget.Errors);
        }

        foreach (IDiscoveredAdapter adapter in _adapters)
        {
            try
            {
                apps.AddRange(adapter.Read(errors));
            }
            catch (Exception ex)
            {
                _log.Warn($"Adapter {adapter.Name} failed: {ex.Message}");
            }
        }
        return apps;
    }

    /// <summary>One-time cleanup: older builds EXTRACTED the seed files into the user
    /// folder, which would now shadow the bundled layer forever. Remove user files whose
    /// content is identical to the legacy seed they were copied from; anything the user
    /// edited differs and is kept (user edits always win).</summary>
    private void MigrateUserFolder()
    {
        string marker = Path.Combine(Path.GetDirectoryName(LibraryDirectory)!, ".seeds-migrated");
        if (File.Exists(marker))
            return;
        try
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            int removed = 0;
            foreach (string resource in asm.GetManifestResourceNames().Where(n => n.StartsWith("seedlegacy/")))
            {
                string fileName = resource["seedlegacy/".Length..];
                string userFile = Path.Combine(LibraryDirectory, fileName);
                if (!File.Exists(userFile))
                    continue;

                using var reader = new StreamReader(asm.GetManifestResourceStream(resource)!);
                var scratch = new List<LibraryError>();
                AppDefinition? seed = ParseJsonText(reader.ReadToEnd(), scratch);
                AppDefinition? user = LibraryLoader.LoadFile(userFile, scratch);
                if (seed is null || user is null)
                    continue;

                if (LibraryLoader.Serialize(seed) == LibraryLoader.Serialize(user))
                {
                    File.Delete(userFile);
                    removed++;
                }
            }
            if (removed > 0)
                _log.Info($"Migrated user library: removed {removed} unmodified seed copies " +
                          "(they now live in the bundled layer)");
            File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
        }
        catch (Exception ex)
        {
            _log.Warn($"User-folder migration skipped: {ex.Message}");
        }
    }

    private AppDefinition? ParseJsonText(string json, List<LibraryError> errors)
    {
        // LibraryLoader's JSON path reads from disk; round-trip via a temp file.
        string tmp = Path.Combine(Path.GetTempPath(), $"keypeek-seed-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tmp, json);
            return LibraryLoader.LoadFile(tmp, errors);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
    }
}
