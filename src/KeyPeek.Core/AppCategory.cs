namespace KeyPeek.Core;

/// <summary>What kind of program an app definition describes.</summary>
public enum AppCategory
{
    Browser,
    Editor,
    Terminal,
    Design,
    Vector,
    Video,
    ThreeD,
    Layout,
    Chat,
    Meeting,
    Mail,
    Office,
    Media,
    Files,
    System,
    /// <summary>Nothing matched — the UI falls back to a lettered badge.</summary>
    Generic,
}

/// <summary>
/// Guesses an app's category from its name and process names, so the library list can show a
/// drawn glyph (globe, brackets, pen nib…) instead of a bare letter.
///
/// KeyPeek deliberately ships no third-party logos: redistributing other companies' marks is
/// their call to license, not ours. Real icons still win when the app is installed — see
/// <c>SettingsWindow.IconFor</c> — and this only fills the gap for apps the user does not have.
/// </summary>
public static class AppCategoryClassifier
{
    // Ordered: the first category with a matching token wins, so put the specific tokens
    // ("powerpnt") ahead of the loose ones ("power" would collide with PowerShell).
    private static readonly (AppCategory Category, string[] Tokens)[] Rules =
    {
        (AppCategory.Browser, new[]
        {
            "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "chromium",
            "safari", "librewolf", "waterfox", "tor browser", "arc.exe", "zen browser",
            "iexplore",
        }),
        (AppCategory.Mail, new[] { "outlook", "olk.exe", "thunderbird", "mailbird", "em client", "gmail" }),
        (AppCategory.Meeting, new[] { "teams", "zoom", "webex", "google meet", "skype" }),
        (AppCategory.Chat, new[]
        {
            "discord", "slack", "telegram", "zalo", "whatsapp", "messenger", "signal",
            "viber", "element", "mattermost", "rocket.chat", "wechat", "line.exe",
            "facebook", "threads",
        }),
        // The design tools split four ways on purpose: a column of Adobe apps all wearing the
        // same pencil reads as "no icon", and vector / video / 3D are genuinely different work.
        (AppCategory.Video, new[]
        {
            "premiere", "aftereffects", "after effects", "davinci", "resolve", "capcut",
            "vegas pro", "camtasia", "shotcut", "kdenlive", "final cut", "filmora",
        }),
        (AppCategory.ThreeD, new[]
        {
            "blender", "cinema 4d", "c4d", "sketchup", "3dsmax", "3ds max", "zbrush",
            "houdini", "rhinoceros", "autodesk maya", "fusion 360", "solidworks", "autocad",
        }),
        (AppCategory.Vector, new[]
        {
            "illustrator", "figma", "sketch", "inkscape", "coreldraw", "xd.exe", "penpot",
            "affinity designer", "canva", "framer", "lunacy",
        }),
        (AppCategory.Layout, new[] { "indesign", "publisher", "scribus", "affinity publisher" }),
        (AppCategory.Design, new[]
        {
            "photoshop", "lightroom", "affinity", "krita", "gimp", "paint.net",
            "clip studio", "procreate", "mspaint",
        }),
        (AppCategory.Terminal, new[]
        {
            "windowsterminal", "wt.exe", "cmd.exe", "powershell", "pwsh", "conemu",
            "alacritty", "wezterm", "hyper.exe", "kitty", "putty", "mobaxterm", "console",
        }),
        (AppCategory.Editor, new[]
        {
            "code.exe", "vscode", "visual studio", "devenv", "notepad", "sublime", "atom.exe",
            "vim", "neovim", "emacs", "jetbrains", "intellij", "pycharm", "webstorm", "rider",
            "goland", "clion", "phpstorm", "android studio", "eclipse", "xcode", "zed.exe",
            "cursor", "obsidian", "typora", "notion", "logseq", "onenote", "acode",
        }),
        (AppCategory.Office, new[]
        {
            "winword", "word", "excel", "powerpnt", "powerpoint", "libreoffice", "wps",
            "soffice", "keynote", "numbers", "pages.exe", "acrobat", "acrord32", "sumatra",
            "foxit", "sheets", "docs.exe",
            // Web apps, classified by NAME only (their processes are browsers) — including
            // the localized product names their tabs actually carry on a Vietnamese system.
            "google docs", "google sheets", "google tài liệu", "google trang tính",
        }),
        (AppCategory.Media, new[]
        {
            "spotify", "vlc", "mpc-hc", "potplayer", "musicbee", "foobar", "itunes",
            "audacity", "audition", "obs64", "obs32", "obs.exe", "youtube", "netflix",
            "winamp", "groove", "media player", "mpv.exe",
        }),
        (AppCategory.Files, new[]
        {
            "explorer", "files.exe", "totalcmd", "directory opus", "dopus", "7zfm", "winrar",
            "peazip", "filezilla", "winscp", "everything",
        }),
        (AppCategory.System, new[]
        {
            "windows", "shell", "systemsettings", "taskmgr", "control panel", "regedit",
            "powertoys", "keypeek",
        }),
        // Loose fallbacks, checked only after every specific token above has missed.
        (AppCategory.Browser, new[] { "browser" }),
        (AppCategory.Chat, new[] { "chat", "messag" }),
        (AppCategory.Mail, new[] { "mail" }),
        (AppCategory.Media, new[] { "player", "music", "video", "audio" }),
        (AppCategory.Terminal, new[] { "terminal", "console", "shell" }),
        (AppCategory.Editor, new[] { "editor", "notes", "ide" }),
    };

    /// <summary>Classify an app. Matching is case-insensitive substring matching over the app
    /// name and its process names combined.</summary>
    public static AppCategory Classify(string? appName, IEnumerable<string>? processNames = null)
    {
        var haystack = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(appName))
            haystack.Append(appName).Append(' ');
        if (processNames is not null)
            foreach (string p in processNames)
                if (!string.IsNullOrWhiteSpace(p))
                    haystack.Append(p).Append(' ');

        if (haystack.Length == 0)
            return AppCategory.Generic;

        string text = haystack.ToString().ToLowerInvariant();
        foreach ((AppCategory category, string[] tokens) in Rules)
            foreach (string token in tokens)
                if (text.Contains(token, StringComparison.Ordinal))
                    return category;

        return AppCategory.Generic;
    }
}
