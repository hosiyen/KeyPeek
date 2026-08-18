using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class LibraryLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "KeyPeekTests", Guid.NewGuid().ToString("N"));

    public LibraryLoaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private string WriteFile(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Valid_file_loads()
    {
        WriteFile("app.json", """
        {
          "app": "Test App",
          "match": { "processName": ["testapp"] },
          "sections": [
            { "name": "General", "shortcuts": [
              { "keys": "Ctrl+P", "description": "Print", "recommended": true }
            ] }
          ]
        }
        """);

        var result = LibraryLoader.LoadDirectory(_dir);
        Assert.Empty(result.Errors);
        var app = Assert.Single(result.Apps);
        Assert.Equal("Test App", app.AppName);
        Assert.False(app.IsGlobal);
        var entry = Assert.Single(app.Sections[0].Shortcuts);
        Assert.True(entry.Recommended);
        Assert.Equal(new KeyChord(Modifiers.Ctrl, "P"), entry.Chords[0]);
    }

    [Fact]
    public void ProcessName_accepts_bare_string()
    {
        WriteFile("app.json", """
        { "app": "A", "match": { "processName": "solo" },
          "sections": [ { "name": "S", "shortcuts": [ { "keys": "F1", "description": "Help" } ] } ] }
        """);
        var result = LibraryLoader.LoadDirectory(_dir);
        Assert.Empty(result.Errors);
        Assert.Equal("solo", Assert.Single(result.Apps).ProcessNames[0]);
    }

    [Fact]
    public void Global_marker_is_honored()
    {
        WriteFile("global.json", """
        { "app": "Windows", "match": { "global": true },
          "sections": [ { "name": "S", "shortcuts": [ { "keys": "Win+E", "description": "Explorer" } ] } ] }
        """);
        var result = LibraryLoader.LoadDirectory(_dir);
        Assert.True(Assert.Single(result.Apps).IsGlobal);
    }

    [Fact]
    public void Malformed_json_reports_file_and_line()
    {
        string path = WriteFile("broken.json", "{\n  \"app\": \"X\",\n  !!!\n}");
        var result = LibraryLoader.LoadDirectory(_dir);
        Assert.Empty(result.Apps);
        var error = Assert.Single(result.Errors);
        Assert.Equal(path, error.File);
        Assert.Equal(3, error.Line);
    }

    [Fact]
    public void Bad_keys_entry_is_reported_with_line_but_other_entries_survive()
    {
        WriteFile("app.json", """
        {
          "app": "Test App",
          "match": { "processName": ["testapp"] },
          "sections": [
            { "name": "General", "shortcuts": [
              { "keys": "Ctrl+P", "description": "Print" },
              { "keys": "Ctrl+Blorp", "description": "Broken" },
              { "keys": "Ctrl+S", "description": "Save" }
            ] }
          ]
        }
        """);

        var result = LibraryLoader.LoadDirectory(_dir);
        var app = Assert.Single(result.Apps);
        Assert.Equal(2, app.ShortcutCount); // the typo didn't swallow its neighbors
        var error = Assert.Single(result.Errors);
        Assert.Contains("Blorp", error.Message);
        Assert.Equal(7, error.Line); // the line of the bad "keys" value
    }

    [Fact]
    public void Missing_description_is_an_error()
    {
        WriteFile("app.json", """
        { "app": "A", "match": { "processName": "a" },
          "sections": [ { "name": "S", "shortcuts": [ { "keys": "F1" } ] } ] }
        """);
        var result = LibraryLoader.LoadDirectory(_dir);
        var error = Assert.Single(result.Errors);
        Assert.Contains("description", error.Message);
    }

    [Fact]
    public void Missing_match_is_an_error()
    {
        WriteFile("app.json", """
        { "app": "A", "sections": [ { "name": "S", "shortcuts": [ { "keys": "F1", "description": "x" } ] } ] }
        """);
        var result = LibraryLoader.LoadDirectory(_dir);
        Assert.Empty(result.Apps);
        Assert.Contains(result.Errors, e => e.Message.Contains("match"));
    }

    [Fact]
    public void Empty_directory_loads_empty()
    {
        var result = LibraryLoader.LoadDirectory(_dir);
        Assert.Empty(result.Apps);
        Assert.Empty(result.Errors);
    }
}
