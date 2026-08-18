namespace KeyPeek.Core;

/// <summary>
/// Where to fetch each app's own official icon, from the vendor's own servers.
///
/// KeyPeek ships no third-party logos. Redistributing another company's mark inside our
/// installer is their call to license, and the usual icon sets are explicit that their own
/// licence does not cover the individual brands (Simple Icons: released under CC0 "though
/// that doesn't mean to imply that all icons within the project are also CC0"). So the marks
/// are never in our package: they are downloaded on the user's machine, from the vendor that
/// owns them, and cached there — the same thing a browser does when it shows a favicon.
///
/// Order of preference in the UI is unchanged: the icon of the app as actually installed
/// wins, this is second, and a drawn category glyph is the floor.
///
/// Every URL below was fetched and the image inspected by eye before being added; entries
/// whose vendor serves no stable raster icon are deliberately absent (Adobe's product-icon
/// paths, Postman and Publisher offer SVG only or nothing, so those apps keep the glyph).
/// </summary>
public static class OfficialIconSources
{
    /// <summary>Keyed by PowerToys/WinGet PackageName — the stable identity for a definition.</summary>
    private static readonly Dictionary<string, string> ByPackage = new(StringComparer.OrdinalIgnoreCase)
    {
        // Microsoft's Fluent product icons, from Microsoft's own CDN.
        ["Microsoft.Word"] = Office("word"),
        ["Microsoft.Excel"] = Office("excel"),
        ["Microsoft.PowerPoint"] = Office("powerpoint"),
        ["Microsoft.Outlook"] = Office("outlook"),
        ["+Microsoft.OutlookForWindows"] = Office("outlook"),
        ["Microsoft.OneNote"] = Office("onenote"),
        ["Microsoft.Teams"] = Office("teams"),
        ["Microsoft.Access"] = Office("access"),
        ["Microsoft.Visio"] = Office("visio"),
        ["Microsoft.Project"] = Office("project"),
        ["Microsoft.VisualStudioCode"] = "https://code.visualstudio.com/apple-touch-icon.png",
        ["Microsoft.PowerToys"] =
            "https://raw.githubusercontent.com/microsoft/PowerToys/main/src/settings-ui/Settings.UI/Assets/Settings/Icons/PowerToys.png",

        ["Google.Chrome"] = "https://www.gstatic.com/images/branding/product/2x/chrome_48dp.png",
        ["Mozilla.Firefox"] = "https://www.firefox.com/favicon.ico",
        ["Figma.Figma"] = "https://static.figma.com/app/icon/1/touch-180.png",
        ["Discord.Discord"] = "https://discord.com/assets/favicon.ico",
        ["SlackTechnologies.Slack"] =
            "https://a.slack-edge.com/80588/marketing/img/meta/slack_hash_256.png",
        ["Telegram.TelegramDesktop"] = "https://telegram.org/img/t_logo.png",
        ["BlenderFoundation.Blender"] = "https://www.blender.org/favicon.ico",
        ["GIMP.GIMP"] = "https://www.gimp.org/images/frontpage/wilber-big.png",
        ["Inkscape.Inkscape"] = "https://media.inkscape.org/favicon.ico",
        ["JetBrains.IntelliJIDEA.Community"] =
            "https://resources.jetbrains.com/storage/products/intellij-idea/img/meta/intellij-idea_logo_300x300.png",
        // Postman serves its site logo as SVG only, which WPF cannot decode; this is the
        // same mark, published as a PNG by Postman itself on its own GitHub organisation.
        ["Postman.Postman"] = "https://avatars.githubusercontent.com/u/10251060",
    };

    /// <summary>Fallback identity for definitions that carry no PackageName (adapters, and
    /// anything the user wrote themselves), keyed by process name without ".exe".</summary>
    private static readonly Dictionary<string, string> ByProcess = new(StringComparer.OrdinalIgnoreCase)
    {
        ["winword"] = Office("word"),
        ["excel"] = Office("excel"),
        ["powerpnt"] = Office("powerpoint"),
        ["outlook"] = Office("outlook"),
        ["olk"] = Office("outlook"),
        ["onenote"] = Office("onenote"),
        ["ms-teams"] = Office("teams"),
        ["msaccess"] = Office("access"),
        ["visio"] = Office("visio"),
        ["winproj"] = Office("project"),
        ["code"] = "https://code.visualstudio.com/apple-touch-icon.png",
        ["chrome"] = "https://www.gstatic.com/images/branding/product/2x/chrome_48dp.png",
        ["firefox"] = "https://www.firefox.com/favicon.ico",
        ["figma"] = "https://static.figma.com/app/icon/1/touch-180.png",
        ["discord"] = "https://discord.com/assets/favicon.ico",
        ["slack"] = "https://a.slack-edge.com/80588/marketing/img/meta/slack_hash_256.png",
        ["telegram"] = "https://telegram.org/img/t_logo.png",
        ["blender"] = "https://www.blender.org/favicon.ico",
        ["gimp-2.10"] = "https://www.gimp.org/images/frontpage/wilber-big.png",
        ["inkscape"] = "https://media.inkscape.org/favicon.ico",
        ["idea64"] = "https://resources.jetbrains.com/storage/products/intellij-idea/img/meta/intellij-idea_logo_300x300.png",
        ["postman"] = "https://avatars.githubusercontent.com/u/10251060",
    };

    private static string Office(string product) =>
        "https://res-1.cdn.office.net/files/fabric-cdn-prod_20230815.002/" +
        $"assets/brand-icons/product/png/{product}_48x1.png";

    /// <summary>The hosts we are willing to talk to. A definition file cannot introduce a
    /// new one — the table above is the whole of it — but the check is cheap insurance that
    /// a typo here can never turn into a request somewhere unexpected.</summary>
    public static readonly IReadOnlyList<string> AllowedHosts = new[]
    {
        "res-1.cdn.office.net", "code.visualstudio.com", "raw.githubusercontent.com",
        "www.gstatic.com", "www.firefox.com", "static.figma.com", "discord.com",
        "a.slack-edge.com", "telegram.org", "www.blender.org", "www.gimp.org",
        "media.inkscape.org", "resources.jetbrains.com", "avatars.githubusercontent.com",
    };

    /// <summary>The official icon URL for an app, or null when its vendor publishes no
    /// stable raster icon (that app keeps the drawn glyph).</summary>
    public static string? UrlFor(string? packageName, IEnumerable<string>? processNames)
    {
        if (packageName is not null && ByPackage.TryGetValue(packageName.Trim(), out string? byPackage))
            return byPackage;
        if (processNames is not null)
            foreach (string process in processNames)
                if (ByProcess.TryGetValue(AppMatcher.NormalizeProcessName(process), out string? byProcess))
                    return byProcess;
        return null;
    }

    /// <summary>A filename-safe cache key for an app.</summary>
    public static string CacheKey(string? packageName, IEnumerable<string>? processNames, string appName)
    {
        string raw = !string.IsNullOrWhiteSpace(packageName)
            ? packageName!
            : processNames?.FirstOrDefault() is { Length: > 0 } p
                ? AppMatcher.NormalizeProcessName(p)
                : appName;
        var safe = new System.Text.StringBuilder(raw.Length);
        foreach (char c in raw)
            safe.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? char.ToLowerInvariant(c) : '_');
        return safe.ToString();
    }

    /// <summary>Every URL this table can ever produce — used by the tests and by nothing else.</summary>
    public static IEnumerable<string> AllUrls => ByPackage.Values.Concat(ByProcess.Values).Distinct();
}
