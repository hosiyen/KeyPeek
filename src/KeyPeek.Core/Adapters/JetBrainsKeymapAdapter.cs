using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KeyPeek.Core.Adapters;

/// <summary>
/// Reads JetBrains IDE user keymaps: %APPDATA%\JetBrains\&lt;Product&gt;&lt;Version&gt;\keymaps\*.xml
/// (Android Studio lives under %APPDATA%\Google\AndroidStudio*). Format:
///   &lt;keymap version="1" name="Windows copy" parent="$default"&gt;
///     &lt;action id="ReformatCode"&gt;
///       &lt;keyboard-shortcut first-keystroke="ctrl alt L" [second-keystroke="..."] /&gt;
/// Keystrokes use Java Swing syntax: space-separated lowercase modifiers then a VK name
/// ("ctrl shift A", "alt ENTER"). The file holds only the user's deltas from the parent
/// keymap — an adapter supplements a curated definition, it never replaces one.
/// </summary>
public sealed class JetBrainsKeymapAdapter : IDiscoveredAdapter
{
    // Product config-folder prefix → Windows process name (no .exe).
    private static readonly (string Prefix, string Process, string Display)[] Products =
    {
        ("IntelliJIdea", "idea64", "IntelliJ IDEA"),
        ("IdeaIC", "idea64", "IntelliJ IDEA"),
        ("PyCharm", "pycharm64", "PyCharm"),
        ("WebStorm", "webstorm64", "WebStorm"),
        ("PhpStorm", "phpstorm64", "PhpStorm"),
        ("Rider", "rider64", "Rider"),
        ("CLion", "clion64", "CLion"),
        ("GoLand", "goland64", "GoLand"),
        ("RubyMine", "rubymine64", "RubyMine"),
        ("DataGrip", "datagrip64", "DataGrip"),
        ("DataSpell", "dataspell64", "DataSpell"),
        ("RustRover", "rustrover64", "RustRover"),
        ("AndroidStudio", "studio64", "Android Studio"),
    };

    private static readonly Dictionary<string, string> VkNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENTER"] = "Enter", ["ESCAPE"] = "Esc", ["SPACE"] = "Space", ["TAB"] = "Tab",
        ["BACK_SPACE"] = "Backspace", ["DELETE"] = "Delete", ["INSERT"] = "Insert",
        ["HOME"] = "Home", ["END"] = "End", ["PAGE_UP"] = "PageUp", ["PAGE_DOWN"] = "PageDown",
        ["UP"] = "Up", ["DOWN"] = "Down", ["LEFT"] = "Left", ["RIGHT"] = "Right",
        ["PERIOD"] = ".", ["COMMA"] = ",", ["SLASH"] = "/", ["BACK_SLASH"] = "\\",
        ["SEMICOLON"] = ";", ["MINUS"] = "-", ["EQUALS"] = "=", ["QUOTE"] = "'",
        ["BACK_QUOTE"] = "`", ["OPEN_BRACKET"] = "[", ["CLOSE_BRACKET"] = "]",
        ["ADD"] = "+", ["SUBTRACT"] = "-",
    };

    private readonly IReadOnlyList<string> _roots;

    public string Name => "JetBrains keymaps";

    public JetBrainsKeymapAdapter(IReadOnlyList<string>? roots = null)
    {
        if (roots is not null)
        {
            _roots = roots;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _roots = new[]
            {
                Path.Combine(appData, "JetBrains"),
                Path.Combine(appData, "Google"), // AndroidStudio<version>
            };
        }
    }

    public IReadOnlyList<AppDefinition> Read(List<LibraryError> errors)
    {
        var apps = new List<AppDefinition>();
        foreach (string root in _roots.Where(Directory.Exists))
        {
            foreach (string productDir in Directory.EnumerateDirectories(root))
            {
                string folder = Path.GetFileName(productDir);
                var product = Products.FirstOrDefault(p =>
                    folder.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase));
                if (product.Process is null)
                    continue;

                string keymapsDir = Path.Combine(productDir, "keymaps");
                if (!Directory.Exists(keymapsDir))
                    continue;

                foreach (string file in Directory.EnumerateFiles(keymapsDir, "*.xml"))
                {
                    AppDefinition? app = ReadKeymap(file, product.Process, product.Display, errors);
                    if (app is not null)
                        apps.Add(app);
                }
            }
        }
        return apps;
    }

    private static AppDefinition? ReadKeymap(string path, string process, string display,
        List<LibraryError> errors)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(path);
        }
        catch (Exception ex)
        {
            errors.Add(new LibraryError(path, 0, $"could not read keymap: {ex.Message}"));
            return null;
        }

        string keymapName = doc.Root?.Attribute("name")?.Value ?? Path.GetFileNameWithoutExtension(path);
        var entries = new List<ShortcutEntry>();
        foreach (XElement action in doc.Root?.Elements("action") ?? Enumerable.Empty<XElement>())
        {
            string? id = action.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id))
                continue;
            foreach (XElement shortcut in action.Elements("keyboard-shortcut"))
            {
                KeyChord? first = ParseKeystroke(shortcut.Attribute("first-keystroke")?.Value);
                if (first is null)
                    continue;
                var chords = new List<KeyChord> { first };
                KeyChord? second = ParseKeystroke(shortcut.Attribute("second-keystroke")?.Value);
                if (second is not null)
                    chords.Add(second);

                entries.Add(new ShortcutEntry
                {
                    Chords = chords,
                    Description = Humanize(id),
                    Note = $"{id}  (keymap: {keymapName})",
                    RawKeys = string.Join(" ", chords.Select(c => c.ToDisplayString())),
                });
            }
        }
        if (entries.Count == 0)
            return null;

        return new AppDefinition
        {
            AppName = display,
            ProcessNames = new[] { process },
            Sections = new[] { new ShortcutSection($"Your keymap ({keymapName})", entries) },
            SourceFile = path,
        };
    }

    /// <summary>"ctrl shift A" / "alt ENTER" → chord; null when empty or unmappable.</summary>
    public static KeyChord? ParseKeystroke(string? keystroke)
    {
        if (string.IsNullOrWhiteSpace(keystroke))
            return null;

        Modifiers mods = Modifiers.None;
        string? key = null;
        foreach (string token in keystroke.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Modifiers.Ctrl; break;
                case "shift": mods |= Modifiers.Shift; break;
                case "alt": mods |= Modifiers.Alt; break;
                case "meta": mods |= Modifiers.Win; break;
                case "altgraph": return null; // AltGr chords aren't representable
                default:
                    if (key is not null)
                        return null; // two base keys — malformed
                    if (VkNames.TryGetValue(token, out string? named))
                        key = named;
                    else if (token.Length == 1 && char.IsAsciiLetterOrDigit(token[0]))
                        key = token.ToUpperInvariant();
                    else if ((token.Length is 2 or 3) && (token[0] is 'F' or 'f') &&
                             int.TryParse(token[1..], out int f) && f is >= 1 and <= 24)
                        key = "F" + f;
                    else
                        return null; // NUMPAD0, unknown VK names: skip quietly
                    break;
            }
        }
        return key is null && mods == Modifiers.None ? null : new KeyChord(mods, key ?? "");
    }

    /// <summary>"ReformatCode" → "Reformat code".</summary>
    public static string Humanize(string actionId)
    {
        string spaced = Regex.Replace(actionId, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return spaced.Length == 0 ? actionId
            : char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
    }
}
