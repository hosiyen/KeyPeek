namespace KeyPeek.Core;

/// <summary>
/// Normalizes the wildly inconsistent key spellings found in PowerToys Shortcut Guide
/// manifests into KeyPeek's canonical key names. Observed in the real corpus:
///  - bare or quoted letters/digits: N, "D", "1"
///  - angle-bracket digits and specials: "&lt;0&gt;", "&lt;Left&gt;", "&lt;Page Up&gt;", "&lt;PrtScr&gt;"
///  - bare named keys: Tab, Space, Plus, Minus, PageUp, LeftBracket, Esc
///  - "" (empty string) = a bare-modifier chord (the lone Win key)
///  - prose/display tokens that are NOT sendable keys: "&lt;Arrow&gt;", "&lt;Underlined letter&gt;",
///    "&lt;Office&gt;", "&lt;TASKBAR1-9&gt;" — kept for display, marked non-executable.
/// </summary>
public static class PowerToysKeyMap
{
    private static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Up"] = "Up", ["Down"] = "Down", ["Left"] = "Left", ["Right"] = "Right",
        ["Up arrow"] = "Up", ["Down arrow"] = "Down", ["Left arrow"] = "Left", ["Right arrow"] = "Right",
        ["Enter"] = "Enter", ["Return"] = "Enter",
        ["Esc"] = "Esc", ["Escape"] = "Esc",
        ["Tab"] = "Tab", ["Space"] = "Space", ["Spacebar"] = "Space",
        ["Backspace"] = "Backspace", ["Back space"] = "Backspace",
        ["Delete"] = "Delete", ["Del"] = "Delete",
        ["Insert"] = "Insert", ["Ins"] = "Insert",
        ["Home"] = "Home", ["End"] = "End",
        ["PageUp"] = "PageUp", ["Page Up"] = "PageUp", ["PgUp"] = "PageUp",
        ["PageDown"] = "PageDown", ["Page Down"] = "PageDown", ["PgDn"] = "PageDown",
        ["PrtScr"] = "PrintScreen", ["PrtScn"] = "PrintScreen", ["PrintScreen"] = "PrintScreen", ["Print Screen"] = "PrintScreen",
        ["Pause"] = "Pause", ["Break"] = "Pause",
        ["CapsLock"] = "CapsLock", ["NumLock"] = "NumLock", ["ScrollLock"] = "ScrollLock",
        ["Menu"] = "Menu", ["Apps"] = "Menu",
        ["Plus"] = "+", ["Minus"] = "-", ["Equals"] = "=", ["Equal"] = "=",
        ["Comma"] = ",", ["Period"] = ".", ["Dot"] = ".",
        ["Slash"] = "/", ["Backslash"] = "\\",
        ["Semicolon"] = ";", ["Apostrophe"] = "'", ["Quote"] = "'",
        ["Grave"] = "`", ["Backtick"] = "`", ["Tilde"] = "~",
        ["LeftBracket"] = "[", ["RightBracket"] = "]",
        ["OpenBracket"] = "[", ["CloseBracket"] = "]",
    };

    /// <summary>Map one manifest key token. Returns the canonical (or display) text and
    /// whether it is a real key that click-to-run can send.</summary>
    public static (string Key, bool Executable) Map(string token)
    {
        token = token.Trim();

        if (token.Length == 0)
            return ("", true); // bare-modifier chord (lone Win, Ctrl+Shift, …)

        // "<...>" display convention: digits, specials, or prose.
        if (token.Length > 2 && token[0] == '<' && token[^1] == '>')
        {
            string inner = token[1..^1].Trim();
            if (inner.Length == 1 && char.IsAsciiDigit(inner[0]))
                return (inner, true);
            if (Named.TryGetValue(inner, out string? mapped))
                return (mapped, true);
            return Placeholder(inner) ?? (inner, false); // "Underlined letter", "Office"…
        }

        if (Named.TryGetValue(token, out string? canonical))
            return (canonical, true);

        // Placeholders also appear WITHOUT brackets in some files ("ArrowLR" bare).
        if (Placeholder(token) is { } placeholder)
            return placeholder;

        if (token.Length == 1)
        {
            char c = token[0];
            if (char.IsAsciiLetter(c))
                return (char.ToUpperInvariant(c).ToString(), true);
            if (char.IsAsciiDigit(c))
                return (token, true);
            if (!char.IsControl(c) && char.IsAscii(c))
                return (token, true); // punctuation: , . / ; ` [ ] = etc.
        }

        if (token.Length is 2 or 3 && (token[0] is 'F' or 'f') &&
            int.TryParse(token[1..], out int f) && f is >= 1 and <= 24)
            return ("F" + f, true);

        // Some manifests (PowerToys' own hotkey file in the WinGet folder) write keys as
        // raw virtual-key codes: "84" = T, "187" = the + key. Decode multi-digit numerics.
        if (token.Length is 2 or 3 && token.All(char.IsAsciiDigit) &&
            int.TryParse(token, out int vk) && vk is >= 8 and <= 255)
        {
            string? named = FromVirtualKey(vk);
            if (named is not null)
                return (named, true);
        }

        // Scraped prose ("Dodge tool/Burn tool", "CTRL X", "⇧ P"): keep for display only.
        return (token, false);
    }

    /// <summary>Display-only placeholder tokens, with and without angle brackets.</summary>
    private static (string Key, bool Executable)? Placeholder(string s) => s.ToUpperInvariant() switch
    {
        "ARROW" => ("↑↓←→", false),
        "ARROWLR" => ("←→", false),
        // "any of the number keys" appears with three different spellings across the corpus.
        "TASKBAR1-9" => ("1–9", false),
        "NUMBER (1-9)" => ("1–9", false),
        "NUMBER" => ("1–9", false),
        // Hardware keys on some Microsoft keyboards. Named so the row reads as a key the
        // user either has or doesn't, rather than as a chord they should be able to press.
        "OFFICE" => ("Office", false),
        "COPILOT" => ("Copilot", false),
        _ => null,
    };

    /// <summary>Virtual-key code → the canonical key name used everywhere else. Public
    /// because the shortcut editor captures real key presses and must spell the result the
    /// same way manifests do: merge/override matching compares rendered chord text, so
    /// "Plus" vs "+" would create a duplicate entry instead of an override.</summary>
    public static string? FromVirtualKey(int vk) => vk switch
    {
        >= 65 and <= 90 => ((char)vk).ToString(),   // A–Z
        >= 48 and <= 57 => ((char)vk).ToString(),   // 0–9
        >= 112 and <= 135 => "F" + (vk - 111),      // F1–F24
        0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x13 => "Pause",
        0x14 => "CapsLock", 0x1B => "Esc", 0x20 => "Space",
        0x21 => "PageUp", 0x22 => "PageDown", 0x23 => "End", 0x24 => "Home",
        0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
        0x2C => "PrintScreen", 0x2D => "Insert", 0x2E => "Delete",
        0x5D => "Menu", 0x90 => "NumLock", 0x91 => "ScrollLock",
        0xBA => ";", 0xBB => "+", 0xBC => ",", 0xBD => "-", 0xBE => ".", 0xBF => "/",
        0xC0 => "`", 0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
        _ => null,
    };
}
