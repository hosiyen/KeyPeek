using System.Text.Json;

namespace KeyPeek.Core.Adapters;

/// <summary>
/// A "discovered" library source: an app's own, documented keybinding file, read at
/// runtime so the user's real customisations appear without any work on their part.
/// Adapters SUPPLEMENT a curated definition (the files hold only overrides/additions,
/// never the compiled-in defaults) and their entries are marked as discovered.
/// </summary>
public interface IDiscoveredAdapter
{
    string Name { get; }
    IReadOnlyList<AppDefinition> Read(List<LibraryError> errors);
}

/// <summary>
/// Reads VS Code's user keybindings: %APPDATA%\Code\User\keybindings.json (plus the
/// Insiders and VSCodium variants). Format: JSON-with-comments array of
/// { "key": "ctrl+shift+p", "command": "...", "when": "..." }. Chords are
/// space-separated ("ctrl+k ctrl+s"). Entries whose command starts with "-" REMOVE a
/// default binding — those are skipped (the defaults aren't ours to edit), as is
/// anything whose key string doesn't parse.
/// </summary>
public sealed class VsCodeKeybindingsAdapter : IDiscoveredAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IReadOnlyList<(string Path, string Variant, string Process)> _sources;

    public string Name => "VS Code keybindings";

    public VsCodeKeybindingsAdapter(string? appDataRoot = null)
    {
        string appData = appDataRoot
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _sources = new[]
        {
            (Path.Combine(appData, "Code", "User", "keybindings.json"), "VS Code", "code"),
            (Path.Combine(appData, "Code - Insiders", "User", "keybindings.json"), "VS Code Insiders", "code - insiders"),
            (Path.Combine(appData, "VSCodium", "User", "keybindings.json"), "VSCodium", "vscodium"),
        };
    }

    public IReadOnlyList<AppDefinition> Read(List<LibraryError> errors)
    {
        var apps = new List<AppDefinition>();
        foreach ((string path, string variant, string process) in _sources)
        {
            if (!File.Exists(path))
                continue;
            AppDefinition? app = ReadOne(path, variant, process, errors);
            if (app is not null)
                apps.Add(app);
        }
        return apps;
    }

    private sealed class BindingDto
    {
        public string? Key { get; set; }
        public string? Command { get; set; }
        public string? When { get; set; }
    }

    private AppDefinition? ReadOne(string path, string variant, string process, List<LibraryError> errors)
    {
        List<BindingDto>? bindings;
        try
        {
            bindings = JsonSerializer.Deserialize<List<BindingDto>>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            errors.Add(new LibraryError(path, 0, $"could not read keybindings: {ex.Message}"));
            return null;
        }
        if (bindings is null || bindings.Count == 0)
            return null;

        var entries = new List<ShortcutEntry>();
        foreach (BindingDto b in bindings)
        {
            if (string.IsNullOrWhiteSpace(b.Key) || string.IsNullOrWhiteSpace(b.Command))
                continue;
            if (b.Command.StartsWith('-'))
                continue; // a removal of a default binding — not ours to apply

            IReadOnlyList<KeyChord> chords;
            try
            {
                chords = KeyChordParser.Parse(b.Key);
            }
            catch (FormatException)
            {
                continue; // numpad keys etc. — quietly skip; this is a guest read
            }

            entries.Add(new ShortcutEntry
            {
                Chords = chords,
                Description = Humanize(b.Command),
                Note = b.Command + (string.IsNullOrWhiteSpace(b.When) ? "" : $"  (when: {b.When})"),
                RawKeys = b.Key,
            });
        }
        if (entries.Count == 0)
            return null;

        return new AppDefinition
        {
            AppName = variant,
            ProcessNames = new[] { process },
            Sections = new[] { new ShortcutSection("Your keybindings", entries) },
            SourceFile = path,
        };
    }

    /// <summary>"workbench.action.showCommands" → "Show commands".</summary>
    internal static string Humanize(string command)
    {
        string last = command.Split('.')[^1];
        var words = System.Text.RegularExpressions.Regex
            .Replace(last, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return words.Length == 0 ? command
            : char.ToUpperInvariant(words[0]) + words[1..].ToLowerInvariant();
    }
}
