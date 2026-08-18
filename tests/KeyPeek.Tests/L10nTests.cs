using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

// L10n.Language is process-global state; these tests always restore English so the rest
// of the suite is unaffected by ordering.
public class L10nTests : IDisposable
{
    public void Dispose() => L10n.Language = UiLanguage.English;

    [Fact]
    public void EveryEntryHasARealTranslation()
    {
        foreach ((string en, string vi) in L10n.AllPairs)
        {
            Assert.False(string.IsNullOrWhiteSpace(en));
            Assert.False(string.IsNullOrWhiteSpace(vi), $"empty Vietnamese for \"{en}\"");
        }
    }

    [Fact]
    public void FormatPlaceholdersSurviveTranslation()
    {
        // A translation that drops {0} turns string.Format into silent data loss;
        // one that adds {1} turns it into a FormatException at runtime.
        foreach ((string en, string vi) in L10n.AllPairs)
            for (int i = 0; i < 3; i++)
            {
                string slot = "{" + i + "}";
                Assert.Equal(en.Contains(slot), vi.Contains(slot));
            }
    }

    [Fact]
    public void UnknownStringsPassThroughUntouched()
    {
        L10n.Language = UiLanguage.Vietnamese;
        Assert.Equal("Ctrl+Shift+N", L10n.T("Ctrl+Shift+N"));
        Assert.Null(L10n.TryLocalize("Some user typed this"));
    }

    [Fact]
    public void EnglishModeIsTheIdentity()
    {
        L10n.Language = UiLanguage.English;
        foreach (string key in L10n.EnglishKeys)
            Assert.Equal(key, L10n.T(key));
    }

    [Fact]
    public void NoTwoEnglishKeysShareAVietnameseFace()
    {
        // The reverse direction of the bidirectional table: if two keys translate to the
        // same Vietnamese string, switching VI→EN picks one of them arbitrarily and a
        // button quietly changes label. Keep every Vietnamese face unique.
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string en, string vi) in L10n.AllPairs)
        {
            Assert.False(seen.TryGetValue(vi, out string? other),
                $"\"{en}\" and \"{other}\" both translate to \"{vi}\"");
            seen[vi] = en;
        }
    }

    [Fact]
    public void SwitchingBackAndForthIsLossless()
    {
        // The settings window re-walks its rendered tree on a language change, so a string
        // must translate EN→VI and back to exactly where it started.
        foreach (string en in L10n.EnglishKeys)
        {
            L10n.Language = UiLanguage.Vietnamese;
            string vi = L10n.TryLocalize(en) ?? en;
            L10n.Language = UiLanguage.English;
            Assert.Equal(en, L10n.TryLocalize(vi) ?? vi);
        }
    }

    [Theory]
    [InlineData("vi", "en", UiLanguage.Vietnamese)]  // explicit choice beats the OS
    [InlineData("en", "vi", UiLanguage.English)]
    [InlineData("system", "vi", UiLanguage.Vietnamese)]
    [InlineData("system", "en", UiLanguage.English)]
    [InlineData("system", "fr", UiLanguage.English)] // no French table: English, not a crash
    [InlineData(null, "vi", UiLanguage.Vietnamese)]  // pre-upgrade settings files
    [InlineData("", "en", UiLanguage.English)]
    public void SettingResolution(string? setting, string os, UiLanguage expected) =>
        Assert.Equal(expected, L10n.Resolve(setting, os));

    [Fact]
    public void TheOverlaysCoreStringsAreTranslated()
    {
        // Not exhaustive — the table test above covers shape. This pins the handful the
        // user stares at every day, so deleting one from the table fails loudly.
        L10n.Language = UiLanguage.Vietnamese;
        Assert.Equal("TOÀN HỆ THỐNG", L10n.T("SYSTEM-WIDE"));
        Assert.Equal("HAY DÙNG", L10n.T("FREQUENTLY USED"));
        Assert.StartsWith("Bấm một phím tắt để chạy",
            L10n.T("Click a shortcut to run it · hold more modifiers to narrow · Esc closes"));
        Assert.Equal("Thoát", L10n.T("Exit"));
        Assert.Equal("Cài đặt", L10n.T("Settings"));
    }
}
