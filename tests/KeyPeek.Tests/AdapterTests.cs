using KeyPeek.Core;
using KeyPeek.Core.Adapters;
using Xunit;

namespace KeyPeek.Tests;

public class AdapterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "KeyPeekTests", Guid.NewGuid().ToString("N"));

    public AdapterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    // ---- VS Code ----

    private VsCodeKeybindingsAdapter VsCodeWith(string content)
    {
        string userDir = Path.Combine(_dir, "Code", "User");
        Directory.CreateDirectory(userDir);
        File.WriteAllText(Path.Combine(userDir, "keybindings.json"), content);
        return new VsCodeKeybindingsAdapter(_dir);
    }

    [Fact]
    public void VsCode_reads_bindings_with_comments_chords_and_skips_removals()
    {
        var adapter = VsCodeWith("""
            // my custom keybindings
            [
                { "key": "ctrl+shift+y", "command": "workbench.action.output.toggleOutput" },
                { "key": "ctrl+k ctrl+u", "command": "editor.action.transformToUppercase", "when": "editorTextFocus" },
                { "key": "ctrl+shift+k", "command": "-editor.action.deleteLines" },
                { "key": "numpad_add", "command": "editor.action.fontZoomIn" },
            ]
            """);

        var errors = new List<LibraryError>();
        var app = Assert.Single(adapter.Read(errors));
        Assert.Empty(errors);

        Assert.Equal(new[] { "code" }, app.ProcessNames);
        var section = Assert.Single(app.Sections);
        Assert.Equal("Your keybindings", section.Name);
        Assert.Equal(2, section.Shortcuts.Count); // removal and unparsable numpad skipped

        Assert.Equal("Toggle output", section.Shortcuts[0].Description);
        Assert.Equal(new KeyChord(Modifiers.Ctrl | Modifiers.Shift, "Y"), section.Shortcuts[0].Chords[0]);
        Assert.Equal(2, section.Shortcuts[1].Chords.Count); // ctrl+k ctrl+u sequence
        Assert.Contains("when: editorTextFocus", section.Shortcuts[1].Note);
    }

    [Fact]
    public void VsCode_missing_file_yields_nothing()
    {
        var adapter = new VsCodeKeybindingsAdapter(_dir);
        var errors = new List<LibraryError>();
        Assert.Empty(adapter.Read(errors));
        Assert.Empty(errors);
    }

    [Fact]
    public void VsCode_adapter_merges_into_bundled_definition_as_discovered()
    {
        var adapter = VsCodeWith("""
            [ { "key": "ctrl+shift+y", "command": "workbench.action.output.toggleOutput" } ]
            """);
        var bundled = new AppDefinition
        {
            AppName = "Visual Studio Code",
            PackageName = "Microsoft.VisualStudioCode",
            ProcessNames = new[] { "code" },
            Sections = new[]
            {
                new ShortcutSection("General", new[]
                {
                    new ShortcutEntry
                    {
                        Chords = KeyChordParser.Parse("Ctrl+Shift+P"),
                        Description = "Show Command Palette",
                        RawKeys = "Ctrl+Shift+P",
                    },
                }),
            },
            SourceFile = "vscode.yml",
        };

        var errors = new List<LibraryError>();
        var merged = LibraryMerger.Merge(new[]
        {
            (LibraryLayer.Bundled, (IReadOnlyList<AppDefinition>)new[] { bundled }),
            (LibraryLayer.Discovered, adapter.Read(errors)),
        });

        var app = Assert.Single(merged); // ONE VS Code, not two
        Assert.Equal("Visual Studio Code", app.AppName);
        Assert.Equal(2, app.ShortcutCount);
        ShortcutEntry mine = app.Sections.Single(s => s.Name == "Your keybindings").Shortcuts[0];
        Assert.Equal(LibraryLayer.Discovered, mine.Layer);
        Assert.Equal("keybindings.json", mine.Origin);
    }

    // ---- JetBrains ----

    [Fact]
    public void JetBrains_reads_keymap_deltas()
    {
        string keymaps = Path.Combine(_dir, "JetBrains", "IntelliJIdea2025.1", "keymaps");
        Directory.CreateDirectory(keymaps);
        File.WriteAllText(Path.Combine(keymaps, "my.xml"), """
            <keymap version="1" name="Windows copy" parent="Windows">
              <action id="ReformatCode">
                <keyboard-shortcut first-keystroke="ctrl alt L" />
              </action>
              <action id="GotoAction">
                <keyboard-shortcut first-keystroke="ctrl shift A" second-keystroke="ctrl K" />
              </action>
              <action id="Unmappable">
                <keyboard-shortcut first-keystroke="ctrl NUMPAD5" />
              </action>
            </keymap>
            """);

        var adapter = new JetBrainsKeymapAdapter(new[] { Path.Combine(_dir, "JetBrains") });
        var errors = new List<LibraryError>();
        var app = Assert.Single(adapter.Read(errors));
        Assert.Empty(errors);

        Assert.Equal("IntelliJ IDEA", app.AppName);
        Assert.Equal(new[] { "idea64" }, app.ProcessNames);
        var section = Assert.Single(app.Sections);
        Assert.Equal("Your keymap (Windows copy)", section.Name);
        Assert.Equal(2, section.Shortcuts.Count); // NUMPAD5 skipped quietly

        Assert.Equal("Reformat code", section.Shortcuts[0].Description);
        Assert.Equal(new KeyChord(Modifiers.Ctrl | Modifiers.Alt, "L"), section.Shortcuts[0].Chords[0]);
        Assert.Equal(2, section.Shortcuts[1].Chords.Count); // two-keystroke chord
    }

    [Theory]
    [InlineData("ctrl shift A", Modifiers.Ctrl | Modifiers.Shift, "A")]
    [InlineData("alt ENTER", Modifiers.Alt, "Enter")]
    [InlineData("ctrl BACK_QUOTE", Modifiers.Ctrl, "`")]
    [InlineData("F12", Modifiers.None, "F12")]
    public void JetBrains_keystroke_grammar(string keystroke, Modifiers mods, string key)
    {
        KeyChord? chord = JetBrainsKeymapAdapter.ParseKeystroke(keystroke);
        Assert.Equal(new KeyChord(mods, key), chord);
    }
}
