using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class ShortcutL10nTests : IDisposable
{
    public void Dispose() => L10n.Language = UiLanguage.English;

    [Fact]
    public void EnglishModeNeverTranslates()
    {
        L10n.Language = UiLanguage.English;
        Assert.Equal("New tab", ShortcutL10n.T("New tab"));
        Assert.Equal("Tabs and windows", ShortcutL10n.T("Tabs and windows"));
    }

    [Fact]
    public void UnknownStringsPassThroughInVietnamese()
    {
        L10n.Language = UiLanguage.Vietnamese;
        Assert.Equal("Frobnicate the wurble", ShortcutL10n.T("Frobnicate the wurble"));
    }

    [Fact]
    public void TheCorpusTableActuallyShipped()
    {
        // The table is an embedded resource assembled from the translation run; a build
        // that silently drops it (rename, csproj edit) must fail here, not on a user's
        // screen as a mysteriously English panel.
        Assert.True(ShortcutL10n.Count >= 2000,
            $"vi-shortcuts.tsv holds {ShortcutL10n.Count} entries — expected the full corpus (≥2000)");
    }

    [Fact]
    public void TheEverydayRowsAreTranslated()
    {
        L10n.Language = UiLanguage.Vietnamese;
        // Not asserting exact wording (that belongs to the data file), only that the rows
        // a user sees within the first minute stopped being English.
        foreach (string en in new[] { "New tab", "New window", "Copy", "Paste", "Undo" })
            Assert.NotEqual(en, ShortcutL10n.T(en));
    }

    [Fact]
    public void SearchMatchesTheTranslationToo()
    {
        L10n.Language = UiLanguage.Vietnamese;
        var entry = new ShortcutEntry
        {
            Chords = KeyChordParser.Parse("Ctrl+T"),
            Description = "New tab",
            RawKeys = "Ctrl+T",
        };
        string vi = ShortcutL10n.T("New tab");
        Assert.True(ShortcutFilter.MatchesSearch(entry, vi[..Math.Min(4, vi.Length)]),
            "typing the Vietnamese description must find the row");
        Assert.True(ShortcutFilter.MatchesSearch(entry, "new ta"),
            "the English original must keep matching as well");
    }
}
