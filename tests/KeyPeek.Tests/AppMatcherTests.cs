using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class AppMatcherTests
{
    private static AppDefinition App(string name, bool global = false, params string[] processes) => new()
    {
        AppName = name,
        IsGlobal = global,
        ProcessNames = processes,
        Sections = new[] { new ShortcutSection("S", new[] { new ShortcutEntry
        {
            Chords = KeyChordParser.Parse("Ctrl+A"),
            Description = "x",
            RawKeys = "Ctrl+A",
        } }) },
        SourceFile = name + ".json",
    };

    private static readonly AppDefinition[] Library =
    {
        App("Windows", global: true),
        App("Visual Studio Code", false, "Code"),
        App("Google Chrome", false, "chrome"),
    };

    [Theory]
    [InlineData("code")]
    [InlineData("CODE")]
    [InlineData("Code.exe")]
    public void Matches_case_insensitively_with_optional_exe(string processName)
    {
        var app = AppMatcher.FindForProcess(Library, processName);
        Assert.Equal("Visual Studio Code", app?.AppName);
    }

    [Fact]
    public void Unknown_process_returns_null()
    {
        Assert.Null(AppMatcher.FindForProcess(Library, "regedit"));
    }

    [Fact]
    public void Empty_process_returns_null()
    {
        Assert.Null(AppMatcher.FindForProcess(Library, ""));
    }

    [Fact]
    public void Global_definitions_never_match_a_process()
    {
        Assert.Null(AppMatcher.FindForProcess(Library, "windows"));
    }

    [Fact]
    public void Globals_returns_only_global_definitions()
    {
        var globals = AppMatcher.Globals(Library);
        var g = Assert.Single(globals);
        Assert.Equal("Windows", g.AppName);
    }
}
