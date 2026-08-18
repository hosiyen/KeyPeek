using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class PowerToysManifestTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "KeyPeekTests", Guid.NewGuid().ToString("N"));

    public PowerToysManifestTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private AppDefinition Load(string yaml, string name = "app.yml", bool expectErrors = false)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, yaml);
        var errors = new List<LibraryError>();
        AppDefinition? app = PowerToysManifestLoader.LoadFile(path, errors);
        if (!expectErrors)
            Assert.True(errors.Count == 0, string.Join("; ", errors));
        Assert.NotNull(app);
        return app!;
    }

    private const string Notepad = """
        PackageName: +WindowsNT.Notepad
        Name: Notepad
        WindowFilter: "Notepad.exe"
        BackgroundProcess: false
        VerifiedAgainst: "11.2409"
        Updated: 2026-08-01
        Shortcuts:
          - SectionName: File
            Properties:
              - Name: New tab
                Recommended: true
                Shortcut:
                - Win: false
                  Ctrl: true
                  Shift: false
                  Alt: false
                  Keys:
                    - N
              - Name: Reset zoom
                Shortcut:
                - Win: false
                  Ctrl: true
                  Shift: false
                  Alt: false
                  Keys:
                    - "<0>"
        """;

    [Fact]
    public void Modifier_words_inside_a_key_token_fold_into_the_modifier_set()
    {
        // Adobe's After Effects manifest writes the whole chord into the key token —
        // Keys: ["Ctrl Shift W"] with every modifier flag false. Taken literally that is
        // one wide un-sendable cap reading "Ctrl Shift W"; folded, it is a normal chord.
        AppDefinition app = Load("""
            PackageName: Adobe.AfterEffects
            Name: After Effects
            WindowFilter: "AfterFX.exe"
            Shortcuts:
              - SectionName: General
                Properties:
                  - Name: Close panel
                    Shortcut:
                    - Win: false
                      Ctrl: false
                      Shift: false
                      Alt: false
                      Keys:
                        - Ctrl Shift W
                  - Name: Cycle viewers
                    Shortcut:
                    - Win: false
                      Ctrl: false
                      Shift: false
                      Alt: false
                      Keys:
                        - Shift
            """);

        ShortcutEntry close = app.Sections[0].Shortcuts[0];
        KeyChord chord = Assert.Single(close.Chords);
        Assert.Equal(Modifiers.Ctrl | Modifiers.Shift, chord.Mods);
        Assert.Equal("W", chord.Key);
        Assert.True(close.Executable);

        // A token that is nothing but a modifier keeps its last word as a display key —
        // folding it away would leave a bare-modifier chord that click-to-run would send.
        ShortcutEntry cycle = app.Sections[0].Shortcuts[1];
        Assert.Equal("Shift", Assert.Single(cycle.Chords).Key);
    }

    [Fact]
    public void Real_schema_loads_with_extensions()
    {
        var app = Load(Notepad);
        Assert.Equal("Notepad", app.AppName);
        Assert.Equal("+WindowsNT.Notepad", app.PackageName);
        Assert.Equal(new[] { "notepad" }, app.ProcessNames);
        Assert.False(app.IsGlobal);
        Assert.Equal("11.2409", app.VerifiedAgainst);
        Assert.Equal("2026-08-01", app.Updated);

        var section = Assert.Single(app.Sections);
        Assert.Equal(new KeyChord(Modifiers.Ctrl, "N"), section.Shortcuts[0].Chords[0]);
        Assert.True(section.Shortcuts[0].Recommended);
        Assert.Equal(new KeyChord(Modifiers.Ctrl, "0"), section.Shortcuts[1].Chords[0]); // "<0>"
    }

    [Fact]
    public void Shell_style_manifest_is_global_and_bare_win_key_survives()
    {
        var app = Load("""
            PackageName: +WindowsNT.Shell
            WindowFilter: "*"
            BackgroundProcess: true
            Shortcuts:
              - SectionName: Essentials
                Properties:
                  - Name: Open start menu
                    Shortcut:
                    - Win: true
                      Ctrl: false
                      Shift: false
                      Alt: false
                      Keys:
                        - ""
                  - Name: Go back
                    Shortcut:
                    - Win: false
                      Ctrl: false
                      Shift: false
                      Alt: true
                      Keys:
                        - "<Left>"
            """);

        Assert.True(app.IsGlobal);
        Assert.Equal("Windows", app.AppName); // no Name field → sensible default
        Assert.Equal(new KeyChord(Modifiers.Win, ""), app.Sections[0].Shortcuts[0].Chords[0]);
        Assert.Equal(new KeyChord(Modifiers.Alt, "Left"), app.Sections[0].Shortcuts[1].Chords[0]);
    }

    [Fact]
    public void Equal_modifier_chords_are_a_sequence()
    {
        var app = Load("""
            Name: VS Code
            WindowFilter: "code.exe"
            Shortcuts:
              - SectionName: General
                Properties:
                  - Name: Open Keyboard Shortcuts
                    Shortcut:
                    - { Win: false, Ctrl: true, Shift: false, Alt: false, Keys: [ K ] }
                    - { Win: false, Ctrl: true, Shift: false, Alt: false, Keys: [ S ] }
            """);

        ShortcutEntry e = app.Sections[0].Shortcuts[0];
        Assert.False(e.ChordsAreAlternatives);
        Assert.Equal("Ctrl+K Ctrl+S", e.KeysText);
    }

    [Fact]
    public void Differing_modifier_chords_are_alternates_and_match_any_held()
    {
        var app = Load("""
            Name: Edge
            WindowFilter: "msedge.exe"
            Shortcuts:
              - SectionName: Address bar
                Properties:
                  - Name: Select the URL in the address bar
                    Shortcut:
                    - { Win: false, Ctrl: true, Shift: false, Alt: false, Keys: [ L ] }
                    - { Win: false, Ctrl: false, Shift: false, Alt: true, Keys: [ D ] }
                    - { Win: false, Ctrl: false, Shift: false, Alt: false, Keys: [ F4 ] }
            """);

        ShortcutEntry e = app.Sections[0].Shortcuts[0];
        Assert.True(e.ChordsAreAlternatives);
        Assert.True(ShortcutFilter.MatchesModifiers(e, Modifiers.Ctrl));
        Assert.True(ShortcutFilter.MatchesModifiers(e, Modifiers.Alt));
        Assert.False(ShortcutFilter.MatchesModifiers(e, Modifiers.Win));

        // and the by-modifier index puts it in both tables
        var migrated = LibraryMigrator.ToTables(app);
        Assert.Contains(migrated.Sections, s => s.Table == Modifiers.Ctrl);
        Assert.Contains(migrated.Sections, s => s.Table == Modifiers.Alt);
    }

    [Fact]
    public void Numeric_virtual_key_codes_decode()
    {
        var app = Load("""
            Name: PowerToys
            WindowFilter: "PowerToys.exe"
            Shortcuts:
              - SectionName: General
                Properties:
                  - Name: Always On Top
                    Shortcut:
                    - { Win: true, Ctrl: true, Shift: false, Alt: false, Keys: [ 84 ] }
                  - Name: Zoom in
                    Shortcut:
                    - { Win: true, Ctrl: true, Shift: false, Alt: false, Keys: [ 187 ] }
            """);

        Assert.Equal(new KeyChord(Modifiers.Win | Modifiers.Ctrl, "T"), app.Sections[0].Shortcuts[0].Chords[0]);
        Assert.Equal(new KeyChord(Modifiers.Win | Modifiers.Ctrl, "+"), app.Sections[0].Shortcuts[1].Chords[0]);
        Assert.True(app.Sections[0].Shortcuts[0].Executable);
    }

    [Fact]
    public void Prose_tokens_load_display_only()
    {
        var app = Load("""
            Name: Shell
            WindowFilter: "*"
            Shortcuts:
              - SectionName: Misc
                Properties:
                  - Name: Run command for the underlined letter
                    Description: for the underlined letter in the app
                    Shortcut:
                    - { Win: false, Ctrl: false, Shift: false, Alt: true, Keys: [ "<Underlined letter>" ] }
            """);

        ShortcutEntry e = app.Sections[0].Shortcuts[0];
        Assert.False(e.Executable);
        Assert.Equal("Underlined letter", e.Chords[0].Key);
        Assert.Equal("for the underlined letter in the app", e.Note);
    }

    [Fact]
    public void The_Office_key_is_written_out_as_the_modifiers_it_really_sends()
    {
        var app = Load("""
            Name: Shell
            WindowFilter: "*"
            Shortcuts:
              - SectionName: Office
                Properties:
                  - Name: Open Word
                    Shortcut:
                    - { Win: false, Ctrl: false, Shift: false, Alt: false, Keys: [ "<Office>", W ] }
            """);

        // The hardware Office key emits Ctrl+Shift+Alt+Win. A bare "Office" cap said
        // nothing to anyone without the key and could not be clicked to run; the written-
        // out combo does both — the user asked for exactly this ("nên ghi ra") after
        // clicking the row and getting nothing.
        ShortcutEntry e = app.Sections[0].Shortcuts[0];
        KeyChord chord = Assert.Single(e.Chords);
        Assert.Equal(Modifiers.Ctrl | Modifiers.Shift | Modifiers.Alt | Modifiers.Win, chord.Mods);
        Assert.Equal("W", chord.Key);
        Assert.True(e.Executable);
        Assert.Equal(PowerToysManifestLoader.OfficeAliasNote, e.Note);
    }

    [Fact]
    public void Simultaneous_keys_in_one_chord_get_a_cap_each()
    {
        var app = Load("""
            Name: Shell
            WindowFilter: "*"
            Shortcuts:
              - SectionName: Copilot
                Properties:
                  - Name: Something
                    Shortcut:
                    - { Win: false, Ctrl: false, Shift: false, Alt: false, Keys: [ "<Copilot>", W ] }
            """);

        // One cap per key. Joining them ("Copilot+W") drew a single wide key that read
        // like a key nobody has — the shape the user reported as wrong. (Office used to be
        // this case too, until it was rewritten to its real modifiers above.)
        ShortcutEntry e = app.Sections[0].Shortcuts[0];
        Assert.Equal(2, e.Chords.Count);
        Assert.Equal("Copilot", e.Chords[0].Key);
        Assert.Equal("W", e.Chords[1].Key);
        Assert.False(e.ChordsAreAlternatives); // one press, never "Copilot or W"
        Assert.False(e.Executable);            // a hardware key we cannot synthesize
    }

    [Fact]
    public void Modifiers_stay_on_the_first_key_of_a_multi_key_chord()
    {
        var app = Load("""
            Name: Shell
            WindowFilter: "*"
            Shortcuts:
              - SectionName: Copilot
                Properties:
                  - Name: Something
                    Shortcut:
                    - { Win: true, Ctrl: false, Shift: false, Alt: false, Keys: [ "<Copilot>", W ] }
            """);

        ShortcutEntry e = app.Sections[0].Shortcuts[0];
        Assert.Equal(Modifiers.Win, e.Chords[0].Mods);
        Assert.Equal(Modifiers.None, e.Chords[1].Mods); // not repeated on every cap
    }

    [Fact]
    public void Malformed_yaml_reports_file_and_line()
    {
        string path = Path.Combine(_dir, "broken.yml");
        File.WriteAllText(path, "Name: X\nWindowFilter: \"x.exe\"\nShortcuts:\n  - SectionName: A\n   Properties: bad-indent\n");
        var errors = new List<LibraryError>();
        AppDefinition? app = PowerToysManifestLoader.LoadFile(path, errors);
        Assert.Null(app);
        var error = Assert.Single(errors);
        Assert.Equal(path, error.File);
        Assert.True(error.Line >= 4, $"line was {error.Line}");
    }

    [Fact]
    public void Serialize_then_reload_roundtrips()
    {
        var app = Load(Notepad);
        string emitted = PowerToysManifestLoader.Serialize(app);

        var errors = new List<LibraryError>();
        AppDefinition? reloaded = PowerToysManifestLoader.LoadText(emitted, "roundtrip.yml", errors);

        Assert.True(errors.Count == 0, string.Join("; ", errors));
        Assert.Equal(app.AppName, reloaded!.AppName);
        Assert.Equal(app.PackageName, reloaded.PackageName);
        Assert.Equal(app.VerifiedAgainst, reloaded.VerifiedAgainst);
        Assert.Equal(
            app.Sections.SelectMany(s => s.Shortcuts).Select(e => e.KeysText),
            reloaded.Sections.SelectMany(s => s.Shortcuts).Select(e => e.KeysText));
        Assert.Equal(new[] { "notepad" }, reloaded.ProcessNames);
    }

    [Fact]
    public void LoadDirectory_reads_yaml_and_legacy_json_but_skips_index()
    {
        File.WriteAllText(Path.Combine(_dir, "notepad.yml"), Notepad);
        File.WriteAllText(Path.Combine(_dir, "legacy.json"), """
            { "app": "Legacy", "match": { "processName": "legacy" },
              "tables": { "ctrl": { "G": [ { "key": "K", "description": "Old format" } ] } } }
            """);
        File.WriteAllText(Path.Combine(_dir, "index.yml"), "DefaultShellName: +WindowsNT.Shell\nIndex:\n  - WindowFilter: \"*\"\n");

        LibraryLoadResult result = LibraryLoader.LoadDirectory(_dir);
        Assert.True(result.Errors.Count == 0, string.Join("; ", result.Errors));
        Assert.Equal(2, result.Apps.Count); // index.yml ignored
    }
}
