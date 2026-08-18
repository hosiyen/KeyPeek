using KeyPeek.Core;
using Xunit;

namespace KeyPeek.Tests;

public class OfficialIconSourcesTests
{
    [Theory]
    [InlineData("Microsoft.Word")]
    [InlineData("Microsoft.Excel")]
    [InlineData("Microsoft.PowerPoint")]
    [InlineData("Microsoft.Outlook")]
    [InlineData("+Microsoft.OutlookForWindows")]
    [InlineData("Microsoft.Teams")]
    [InlineData("Microsoft.VisualStudioCode")]
    [InlineData("Google.Chrome")]
    [InlineData("Mozilla.Firefox")]
    [InlineData("Figma.Figma")]
    [InlineData("Discord.Discord")]
    [InlineData("SlackTechnologies.Slack")]
    [InlineData("BlenderFoundation.Blender")]
    public void TheAppsWeShipHaveAnOfficialLogoSource(string packageName) =>
        Assert.NotNull(OfficialIconSources.UrlFor(packageName, null));

    [Fact]
    public void PackageNameMatchingIgnoresCase() =>
        Assert.NotNull(OfficialIconSources.UrlFor("figma.figma", null));

    [Fact]
    public void ProcessNamesAreTheFallbackIdentity()
    {
        // Adapter- and user-authored definitions carry no PackageName.
        Assert.NotNull(OfficialIconSources.UrlFor(null, new[] { "code.exe" }));
        Assert.NotNull(OfficialIconSources.UrlFor(null, new[] { "WINWORD.EXE" }));
    }

    [Fact]
    public void AnAppWithNoVendorIconGetsNothingRatherThanAGuess()
    {
        // Adobe publishes no stable raster product icon we can reach, so those apps fall
        // through to the drawn glyph. Returning some other company's art instead would be
        // worse than an honest blank.
        Assert.Null(OfficialIconSources.UrlFor("Adobe.Photoshop", new[] { "photoshop.exe" }));
        Assert.Null(OfficialIconSources.UrlFor("Acme.Nothing", new[] { "acme.exe" }));
        Assert.Null(OfficialIconSources.UrlFor(null, null));
    }

    [Fact]
    public void EveryUrlIsHttpsAndOnTheAllowList()
    {
        foreach (string url in OfficialIconSources.AllUrls)
        {
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out Uri? uri), url);
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
            Assert.Contains(uri.Host, OfficialIconSources.AllowedHosts);
            // No query string: these are plain file fetches, and a query is where an
            // identifier would hide.
            Assert.Equal("", uri.Query);
        }
    }

    [Fact]
    public void NoUrlPointsAtAnSvg()
    {
        // WPF cannot decode SVG, so an .svg entry would fail silently at runtime and the
        // app would look like it "just doesn't show that logo". (Extensionless URLs are
        // fine — a CDN that serves image/png without a suffix is checked at fetch time by
        // the content-type and the decode.)
        foreach (string url in OfficialIconSources.AllUrls)
            Assert.False(url.EndsWith(".svg", StringComparison.OrdinalIgnoreCase), url);
    }

    [Fact]
    public void CacheKeysAreFilenameSafeAndStable()
    {
        string key = OfficialIconSources.CacheKey("JetBrains.IntelliJIDEA.Community", null, "IntelliJ");
        Assert.Equal("jetbrains.intellijidea.community", key);
        Assert.Equal(key, OfficialIconSources.CacheKey("JetBrains.IntelliJIDEA.Community", null, "IntelliJ"));

        foreach (char bad in System.IO.Path.GetInvalidFileNameChars())
            Assert.DoesNotContain(bad,
                OfficialIconSources.CacheKey($"a{bad}b", null, "x"));
    }

    [Fact]
    public void CacheKeyFallsBackToProcessThenName()
    {
        Assert.Equal("chrome", OfficialIconSources.CacheKey(null, new[] { "Chrome.exe" }, "Google Chrome"));
        Assert.Equal("my_app", OfficialIconSources.CacheKey(null, null, "My App"));
    }
}
