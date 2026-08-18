using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class AppCategoryTests
{
    [Theory]
    [InlineData("Google Chrome", "chrome.exe", AppCategory.Browser)]
    [InlineData("Microsoft Edge", "msedge.exe", AppCategory.Browser)]
    [InlineData("Mozilla Firefox", "firefox.exe", AppCategory.Browser)]
    [InlineData("Visual Studio Code", "code.exe", AppCategory.Editor)]
    [InlineData("Notepad", "notepad.exe", AppCategory.Editor)]
    [InlineData("Windows Terminal", "WindowsTerminal.exe", AppCategory.Terminal)]
    [InlineData("Adobe Photoshop", "photoshop.exe", AppCategory.Design)]
    [InlineData("Adobe Illustrator", "illustrator.exe", AppCategory.Vector)]
    [InlineData("Adobe After Effects", "afterfx.exe", AppCategory.Video)]
    [InlineData("Blender", "blender.exe", AppCategory.ThreeD)]
    [InlineData("Figma", "figma.exe", AppCategory.Vector)]
    [InlineData("Discord", "discord.exe", AppCategory.Chat)]
    [InlineData("Slack", "slack.exe", AppCategory.Chat)]
    [InlineData("Zalo", "zalo.exe", AppCategory.Chat)]
    [InlineData("Teams", "ms-teams.exe", AppCategory.Meeting)]
    [InlineData("Outlook", "outlook.exe", AppCategory.Mail)]
    [InlineData("Outlook (new)", "olk.exe", AppCategory.Mail)]
    [InlineData("Word", "winword.exe", AppCategory.Office)]
    [InlineData("Excel", "excel.exe", AppCategory.Office)]
    [InlineData("PowerPoint", "powerpnt.exe", AppCategory.Office)]
    [InlineData("Spotify", "spotify.exe", AppCategory.Media)]
    [InlineData("File Explorer", "explorer.exe", AppCategory.Files)]
    public void ClassifiesTheAppsWeShip(string name, string process, AppCategory expected) =>
        Assert.Equal(expected, AppCategoryClassifier.Classify(name, new[] { process }));

    [Fact]
    public void PowerShellIsATerminalNotPowerPoint()
    {
        // "powerpnt"/"powerpoint" are spelled out precisely so a bare "power" prefix cannot
        // drag PowerShell into the Office bucket.
        Assert.Equal(AppCategory.Terminal, AppCategoryClassifier.Classify("PowerShell", new[] { "powershell.exe" }));
        Assert.Equal(AppCategory.Terminal, AppCategoryClassifier.Classify("PowerShell 7", new[] { "pwsh.exe" }));
    }

    [Fact]
    public void TheDesignToolsSplitByWhatTheyActuallyDo()
    {
        // A column of Adobe apps wearing one identical pencil reads as "no icon at all", so
        // vector / video / 3D / paint are separate categories with separate glyphs.
        var distinct = new[]
        {
            AppCategoryClassifier.Classify("Adobe Photoshop", new[] { "photoshop" }),
            AppCategoryClassifier.Classify("Adobe Illustrator", new[] { "illustrator" }),
            AppCategoryClassifier.Classify("Adobe InDesign", new[] { "indesign" }),
            AppCategoryClassifier.Classify("Adobe After Effects", new[] { "afterfx" }),
            AppCategoryClassifier.Classify("Blender", new[] { "blender" }),
        };
        Assert.Equal(5, distinct.Distinct().Count());
        Assert.Equal(AppCategory.Media, AppCategoryClassifier.Classify("Adobe Audition", new[] { "audition.exe" }));
    }

    [Fact]
    public void UnknownAppsFallThroughToGeneric()
    {
        Assert.Equal(AppCategory.Generic, AppCategoryClassifier.Classify("Acme Widget Factory", new[] { "acme.exe" }));
        Assert.Equal(AppCategory.Generic, AppCategoryClassifier.Classify(null, null));
        Assert.Equal(AppCategory.Generic, AppCategoryClassifier.Classify("   ", Array.Empty<string>()));
    }

    [Fact]
    public void ProcessNameAloneIsEnough() =>
        Assert.Equal(AppCategory.Browser, AppCategoryClassifier.Classify(null, new[] { "brave.exe" }));

    [Fact]
    public void MatchingIsCaseInsensitive() =>
        Assert.Equal(AppCategory.Chat, AppCategoryClassifier.Classify("TELEGRAM DESKTOP", new[] { "Telegram.exe" }));

    [Fact]
    public void EveryBundledAppGetsAGlyphOrADeliberateLetter()
    {
        // Not an assertion that every app is classified — an unknown app legitimately falls
        // back to a letter. This pins the ones we ship, so a token edit that quietly demotes
        // Chrome to a lettered tile fails here instead of in a screenshot.
        var bundled = new (string Name, string Process, AppCategory Expected)[]
        {
            ("Google Chrome", "chrome", AppCategory.Browser),
            ("Microsoft Edge", "msedge", AppCategory.Browser),
            ("Visual Studio Code", "code", AppCategory.Editor),
            ("Notepad", "Notepad", AppCategory.Editor),
            ("Windows Terminal", "WindowsTerminal", AppCategory.Terminal),
            ("Adobe Photoshop", "Photoshop", AppCategory.Design),
            ("Adobe Illustrator", "Illustrator", AppCategory.Vector),
            ("Adobe InDesign", "InDesign", AppCategory.Layout),
            ("Adobe After Effects", "AfterFX", AppCategory.Video),
            ("Blender", "blender", AppCategory.ThreeD),
            ("Figma", "Figma", AppCategory.Vector),
            ("Discord", "discord", AppCategory.Chat),
            ("Slack", "slack", AppCategory.Chat),
            ("Teams", "ms-teams", AppCategory.Meeting),
            ("Outlook", "outlook", AppCategory.Mail),
            ("Outlook (new)", "olk", AppCategory.Mail),
            ("Word", "winword", AppCategory.Office),
            ("Excel", "excel", AppCategory.Office),
            ("PowerPoint", "powerpnt", AppCategory.Office),
            ("File Explorer", "explorer", AppCategory.Files),
        };
        foreach ((string name, string process, AppCategory expected) in bundled)
            Assert.Equal(expected, AppCategoryClassifier.Classify(name, new[] { process }));
    }
}
