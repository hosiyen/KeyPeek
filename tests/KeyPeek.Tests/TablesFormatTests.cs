using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class TablesFormatTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "KeyPeekTests", Guid.NewGuid().ToString("N"));

    public TablesFormatTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private AppDefinition Load(string json)
    {
        string path = Path.Combine(_dir, "app.json");
        File.WriteAllText(path, json);
        var errors = new List<LibraryError>();
        AppDefinition? app = LibraryLoader.LoadFile(path, errors);
        Assert.True(errors.Count == 0, string.Join("; ", errors));
        Assert.NotNull(app);
        return app!;
    }

    [Fact]
    public void V2_tables_load_with_table_assignment()
    {
        var app = Load("""
        { "app": "Chrome", "match": { "processName": ["chrome"] },
          "tables": {
            "ctrl": { "Tabs": [
              { "key": "T", "description": "New tab", "recommended": true },
              { "key": "T", "mods": ["shift"], "description": "Reopen closed tab" } ] },
            "alt": { "Navigation": [ { "key": "Left", "description": "Back" } ] },
            "plain": { "Misc": [ { "key": "F11", "description": "Full screen" } ] }
          } }
        """);

        Assert.Equal(3, app.Sections.Count);

        var tabs = app.Sections.Single(s => s.Name == "Tabs");
        Assert.Equal(Modifiers.Ctrl, tabs.Table);
        Assert.Equal(new KeyChord(Modifiers.Ctrl, "T"), tabs.Shortcuts[0].Chords[0]);
        Assert.Equal(new KeyChord(Modifiers.Ctrl | Modifiers.Shift, "T"), tabs.Shortcuts[1].Chords[0]);

        var nav = app.Sections.Single(s => s.Name == "Navigation");
        Assert.Equal(Modifiers.Alt, nav.Table);
        Assert.Equal(new KeyChord(Modifiers.Alt, "Left"), nav.Shortcuts[0].Chords[0]);

        Assert.Equal(Modifiers.None, app.Sections.Single(s => s.Name == "Misc").Table);
    }

    [Fact]
    public void V2_row_can_use_full_keys_string_for_sequences()
    {
        var app = Load("""
        { "app": "Code", "match": { "processName": "Code" },
          "tables": { "ctrl": { "General": [
            { "keys": "Ctrl+K Ctrl+S", "description": "Keyboard shortcuts" } ] } } }
        """);
        Assert.Equal(2, app.Sections[0].Shortcuts[0].Chords.Count);
    }

    [Fact]
    public void Unknown_table_name_is_a_loud_error()
    {
        string path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, """
        { "app": "X", "match": { "processName": "x" },
          "tables": { "hyper": { "G": [ { "key": "T", "description": "d" } ] },
                      "ctrl": { "G": [ { "key": "T", "description": "d" } ] } } }
        """);
        var errors = new List<LibraryError>();
        AppDefinition? app = LibraryLoader.LoadFile(path, errors);
        Assert.NotNull(app); // the valid table still loads
        var error = Assert.Single(errors);
        Assert.Contains("hyper", error.Message);
    }

    [Fact]
    public void Held_modifier_selects_its_table()
    {
        var app = Load("""
        { "app": "Chrome", "match": { "processName": ["chrome"] },
          "tables": {
            "ctrl": { "Tabs": [ { "key": "T", "description": "New tab" } ] },
            "alt": { "Navigation": [ { "key": "Left", "description": "Back" } ] }
          } }
        """);

        var ctrlView = ShortcutFilter.Apply(app.Sections, Modifiers.Ctrl, null);
        Assert.Equal("Tabs", Assert.Single(ctrlView).Name);

        var altView = ShortcutFilter.Apply(app.Sections, Modifiers.Alt, null);
        Assert.Equal("Navigation", Assert.Single(altView).Name);

        // pinned/search mode: everything
        Assert.Equal(2, ShortcutFilter.Apply(app.Sections, Modifiers.None, null).Count);
    }

    [Fact]
    public void Entry_in_two_tables_dedupes_when_both_selected()
    {
        var app = Load("""
        { "app": "X", "match": { "processName": "x" },
          "tables": {
            "ctrl": { "G": [ { "key": "K", "mods": ["alt"], "description": "Thing" } ] },
            "alt":  { "G": [ { "key": "K", "mods": ["ctrl"], "description": "Thing" } ] }
          } }
        """);
        var view = ShortcutFilter.Apply(app.Sections, Modifiers.Ctrl | Modifiers.Alt, null);
        Assert.Single(Assert.Single(view).Shortcuts);
    }

    [Fact]
    public void Serialize_then_reload_roundtrips()
    {
        var app = Load("""
        { "app": "Chrome", "match": { "processName": ["chrome"], "titleRegex": "YouTube" },
          "tables": {
            "ctrl": { "Tabs": [ { "key": "T", "mods": ["shift"], "description": "Reopen", "recommended": true },
                                 { "keys": "Ctrl+K Ctrl+S", "description": "Sequence" } ] }
          } }
        """);

        string serialized = LibraryLoader.Serialize(app);
        string path = Path.Combine(_dir, "roundtrip.json");
        File.WriteAllText(path, serialized);
        var errors = new List<LibraryError>();
        AppDefinition? reloaded = LibraryLoader.LoadFile(path, errors);

        Assert.Empty(errors);
        Assert.Equal(app.TitleRegex, reloaded!.TitleRegex);
        Assert.Equal(app.Sections.Count, reloaded.Sections.Count);
        Assert.Equal(
            app.Sections[0].Shortcuts.Select(e => e.KeysText),
            reloaded.Sections[0].Shortcuts.Select(e => e.KeysText));
        Assert.True(reloaded.Sections[0].Shortcuts[0].Recommended);
    }

    [Fact]
    public void Migrator_buckets_legacy_entries_by_trigger_modifier()
    {
        var app = Load("""
        { "app": "Legacy", "match": { "processName": "legacy" },
          "sections": [ { "name": "General", "shortcuts": [
            { "keys": "Ctrl+C", "description": "Copy" },
            { "keys": "Alt+F4", "description": "Close" },
            { "keys": "Ctrl+Alt+K", "description": "Both" },
            { "keys": "Shift+Delete", "description": "Hard delete" },
            { "keys": "F2", "description": "Rename" } ] } ] }
        """);

        Assert.True(LibraryMigrator.NeedsMigration(app));
        AppDefinition migrated = LibraryMigrator.ToTables(app);

        var ctrl = migrated.Sections.Single(s => s.Table == Modifiers.Ctrl);
        Assert.Equal(new[] { "Copy", "Both" }, ctrl.Shortcuts.Select(e => e.Description));
        var alt = migrated.Sections.Single(s => s.Table == Modifiers.Alt);
        Assert.Equal(new[] { "Close", "Both" }, alt.Shortcuts.Select(e => e.Description));
        Assert.Equal("Hard delete",
            migrated.Sections.Single(s => s.Table == Modifiers.Shift).Shortcuts.Single().Description);
        Assert.Equal("Rename",
            migrated.Sections.Single(s => s.Table == Modifiers.None).Shortcuts.Single().Description);
    }

    [Fact]
    public void Title_regex_disambiguates_between_definitions()
    {
        var generic = Load("""
        { "app": "Chrome", "match": { "processName": ["chrome"] },
          "tables": { "ctrl": { "G": [ { "key": "T", "description": "New tab" } ] } } }
        """);
        File.Delete(Path.Combine(_dir, "app.json"));
        var youtube = Load("""
        { "app": "YouTube", "match": { "processName": ["chrome"], "titleRegex": "YouTube" },
          "tables": { "ctrl": { "G": [ { "key": "T", "description": "Theater mode" } ] } } }
        """);

        var apps = new[] { generic, youtube };
        Assert.Equal("YouTube", AppMatcher.FindForProcess(apps, "chrome", "Cats — YouTube")?.AppName);
        Assert.Equal("Chrome", AppMatcher.FindForProcess(apps, "chrome", "Some article")?.AppName);
    }
}
