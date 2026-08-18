using System.Reflection;
using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

/// <summary>
/// Guards the shipped data, not the code. A manifest can be perfectly valid YAML and still
/// put nonsense on screen — a key token KeyPeek doesn't understand renders as a cap with the
/// raw text in it ("Office", "Populate start"), which reads like a shortcut the user could
/// press. These tests fail the build when a manifest introduces one.
/// </summary>
public class BundledLibraryTests
{
    /// <summary>Tokens that legitimately render as text rather than a real key. Each one is
    /// here because a human decided it should be: adding to this list is a decision, not a
    /// formality.</summary>
    private static readonly HashSet<string> AllowedDisplayOnlyKeys = new(StringComparer.Ordinal)
    {
        "↑↓←→",        // <Arrow>: "the arrow keys", as a group
        "←→",           // <ArrowLR>
        "1–9",          // <TASKBAR1-9>
        "Underlined letter", // Alt + the underlined letter in a menu
        "Office",       // hardware key, only on some Microsoft keyboards
        "Copilot",      // ditto
        "Shift",        // Adobe uses a bare modifier as "hold Shift while dragging"
        "Alt",          // ditto
    };

    /// <summary>Is this unmapped token deliberate prose rather than a misspelled key?
    /// Adobe's manifests are full of the former — "Pen tool", "Caps Lock", "Numpad 0",
    /// "0 (zero)", "drag" — and they render as display-only rows, which is correct. What
    /// this must keep flagging is the thing that LOOKS like a key name and isn't: a short
    /// all-caps token ("CTRLA") is a typo until proven otherwise.</summary>
    private static bool LooksLikeDeliberateProse(string token) =>
        token.Any(ch => !char.IsLetterOrDigit(ch)) // spaces, slashes, parentheses, dashes…
        || token.Any(char.IsLower)                 // "drag": prose is written like words
        || (token.Length == 2 && token[0] == token[1] && char.IsUpper(token[0]));
        // ^ doubled capitals ("UU", "MM") are After Effects' real double-tap idiom

    private static IReadOnlyList<AppDefinition> Bundled()
    {
        // The manifests are embedded in the app assembly, which the test project does not
        // reference — read them from the repo instead, which is also what a contributor
        // editing a manifest would want checked.
        string dir = RepoLibraryDirectory();
        LibraryLoadResult result = LibraryLoader.LoadDirectory(dir);
        Assert.Empty(result.Errors); // a shipped manifest must load cleanly
        Assert.NotEmpty(result.Apps);
        return result.Apps;
    }

    private static string RepoLibraryDirectory()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "library")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "library");
    }

    [Fact]
    public void No_bundled_manifest_introduces_an_unknown_key_token()
    {
        var offenders = new List<string>();
        foreach (AppDefinition app in Bundled())
        foreach (ShortcutSection section in app.Sections)
        foreach (ShortcutEntry entry in section.Shortcuts)
        {
            if (entry.Executable)
                continue; // real keys: nothing to check
            foreach (KeyChord chord in entry.Chords)
            {
                if (chord.Key.Length == 0)
                    continue;
                // Fine if the key is one KeyPeek understands (it just shares a row with a
                // key it doesn't), or a documented display-only token.
                if (PowerToysKeyMap.Map(chord.Key).Executable ||
                    AllowedDisplayOnlyKeys.Contains(chord.Key) ||
                    LooksLikeDeliberateProse(chord.Key))
                    continue;
                offenders.Add($"{Path.GetFileName(app.SourceFile)}: \"{chord.Key}\" ({entry.Description})");
            }
        }

        Assert.True(offenders.Count == 0,
            "Manifests contain key tokens KeyPeek cannot render as keys. Either map them in " +
            "PowerToysKeyMap or add them to AllowedDisplayOnlyKeys with a reason:\n  " +
            string.Join("\n  ", offenders.Distinct()));
    }

    [Fact]
    public void No_shipped_shortcut_is_a_leftover_placeholder()
    {
        // "Example — replace me" used to be written into starter files and loaded like a
        // real shortcut; anything of that shape is a bug in the data. "example" itself is
        // NOT a smell: real upstream descriptions say "for example, cycle through open
        // compositions", and that word cost After Effects its place in the bundle for a
        // day. The starter-file shape is caught by "replace me".
        string[] smells = { "replace me", "todo", "populate", "lorem" };
        var offenders = Bundled()
            .SelectMany(a => a.Sections.SelectMany(s => s.Shortcuts)
                .Where(e => smells.Any(smell => e.Description.Contains(smell, StringComparison.OrdinalIgnoreCase)))
                .Select(e => $"{Path.GetFileName(a.SourceFile)}: {e.KeysText} — {e.Description}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Placeholder text shipped as a shortcut:\n  " +
                                          string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_shortcut_has_something_to_show()
    {
        var offenders = Bundled()
            .SelectMany(a => a.Sections.SelectMany(s => s.Shortcuts)
                .Where(e => string.IsNullOrWhiteSpace(e.Description) || e.Chords.Count == 0)
                .Select(e => $"{Path.GetFileName(a.SourceFile)}: {e.KeysText}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Shortcuts with no description or no keys:\n  " +
                                          string.Join("\n  ", offenders));
    }

    [Fact]
    public void Section_names_never_leak_the_manifest_templating_syntax()
    {
        // "<TASKBAR1-9>Taskbar Shortcuts" is real upstream data; the loader strips the
        // marker. If one ever survives, it appears as a card title on screen.
        var offenders = Bundled()
            .SelectMany(a => a.Sections.Select(s => (App: Path.GetFileName(a.SourceFile), s.Name)))
            .Where(x => x.Name.Contains('<') || x.Name.Contains('>'))
            .Select(x => $"{x.App}: {x.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, "Section titles still contain markup:\n  " +
                                          string.Join("\n  ", offenders));
    }
}
