using System.Text.RegularExpressions;

namespace KeyPeek.Core;

/// <summary>
/// Parses key strings like "Ctrl+Shift+P", "Win+.", "Ctrl+K Ctrl+S" or "Ctrl++" into
/// normalized <see cref="KeyChord"/> sequences at load time. Everything downstream
/// (filtering, rendering) works on the parsed structure, never the raw string.
///
/// Accepted syntax:
///  - parts joined by "+", spaces around "+" allowed ("Ctrl + P")
///  - whitespace (or ", ") separates the chords of a multi-step sequence
///  - "Ctrl++" means Ctrl and the literal "+" key
///  - common aliases: Control/Ctrl, Return/Enter, Win/Meta/Super/Cmd, Option/Alt, Esc/Escape…
///
/// Malformed input throws <see cref="FormatException"/> with a specific message; the
/// library loader turns that into a file+line error so typos are loud, not silent.
/// </summary>
public static partial class KeyChordParser
{
    private static readonly Dictionary<string, Modifiers> ModifierAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = Modifiers.Ctrl, ["control"] = Modifiers.Ctrl, ["ctl"] = Modifiers.Ctrl,
        ["shift"] = Modifiers.Shift,
        ["alt"] = Modifiers.Alt, ["option"] = Modifiers.Alt, ["opt"] = Modifiers.Alt,
        ["win"] = Modifiers.Win, ["windows"] = Modifiers.Win, ["meta"] = Modifiers.Win,
        ["super"] = Modifiers.Win, ["cmd"] = Modifiers.Win, ["command"] = Modifiers.Win,
    };

    private static readonly Dictionary<string, string> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = "Enter", ["return"] = "Enter",
        ["esc"] = "Esc", ["escape"] = "Esc",
        ["tab"] = "Tab",
        ["space"] = "Space", ["spacebar"] = "Space",
        ["backspace"] = "Backspace", ["bksp"] = "Backspace", ["back"] = "Backspace",
        ["delete"] = "Delete", ["del"] = "Delete",
        ["insert"] = "Insert", ["ins"] = "Insert",
        ["home"] = "Home", ["end"] = "End",
        ["pageup"] = "PageUp", ["pgup"] = "PageUp",
        ["pagedown"] = "PageDown", ["pgdn"] = "PageDown", ["pgdown"] = "PageDown",
        ["up"] = "Up", ["uparrow"] = "Up",
        ["down"] = "Down", ["downarrow"] = "Down",
        ["left"] = "Left", ["leftarrow"] = "Left",
        ["right"] = "Right", ["rightarrow"] = "Right",
        ["printscreen"] = "PrintScreen", ["prtsc"] = "PrintScreen", ["prtscn"] = "PrintScreen", ["printscrn"] = "PrintScreen",
        ["scrolllock"] = "ScrollLock",
        ["pause"] = "Pause", ["break"] = "Pause",
        ["capslock"] = "CapsLock", ["numlock"] = "NumLock",
        ["menu"] = "Menu", ["apps"] = "Menu", ["contextmenu"] = "Menu",
        ["plus"] = "+", ["minus"] = "-", ["hyphen"] = "-", ["dash"] = "-",
        ["equals"] = "=", ["equal"] = "=",
        ["comma"] = ",", ["period"] = ".", ["dot"] = ".",
        ["slash"] = "/", ["backslash"] = "\\",
        ["semicolon"] = ";", ["apostrophe"] = "'", ["quote"] = "'",
        ["grave"] = "`", ["backtick"] = "`",
        ["lbracket"] = "[", ["rbracket"] = "]",
    };

    [GeneratedRegex(@"^[fF]([1-9]|1[0-9]|2[0-4])$")]
    private static partial Regex FKeyRegex();

    [GeneratedRegex(@"\s*\+\s*")]
    private static partial Regex PlusSpacingRegex();

    /// <summary>"ctrl"/"win"/… → flag. Used for v2 table names and "mods" arrays.</summary>
    public static bool TryParseModifierName(string name, out Modifiers modifier) =>
        ModifierAliases.TryGetValue(name.Trim(), out modifier);

    /// <summary>Normalize a single base-key name ("Left", "pgup", "]") without modifiers.
    /// Throws FormatException for unknown keys.</summary>
    public static string CanonicalizeBaseKey(string key) => CanonicalizeKey(key.Trim(), key);

    /// <summary>Parse a full "keys" value into a chord sequence. Throws FormatException.</summary>
    public static IReadOnlyList<KeyChord> Parse(string keys)
    {
        if (string.IsNullOrWhiteSpace(keys))
            throw new FormatException("keys value is empty");

        // "Ctrl + P" → "Ctrl+P". A literal plus key is written "Ctrl++" (alias: "Ctrl+Plus").
        string text = PlusSpacingRegex().Replace(keys.Trim(), "+");

        var chords = new List<KeyChord>();
        foreach (string rawToken in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // Tolerate "Ctrl+K, Ctrl+S" sequence commas — but "Ctrl+," means Ctrl+Comma,
            // so only strip a comma that doesn't leave a dangling "+" behind.
            string token = rawToken;
            if (token.EndsWith(',') && token.Length > 1 && token[^2] != '+')
                token = token[..^1];
            if (token.Length == 0)
                continue;
            chords.Add(ParseChord(token));
        }

        if (chords.Count == 0)
            throw new FormatException("keys value is empty");
        if (chords.Count > 3)
            throw new FormatException($"\"{keys}\" has {chords.Count} chords — more than 3 is almost certainly a typo");
        return chords;
    }

    private static KeyChord ParseChord(string token)
    {
        string? baseKey = null;
        string work = token;

        // Trailing "++" = the literal plus key ("+" alone is just the plus key).
        if (work == "+")
            return new KeyChord(Modifiers.None, "+");
        if (work.EndsWith("++", StringComparison.Ordinal))
        {
            baseKey = "+";
            work = work[..^2];
        }

        Modifiers mods = Modifiers.None;
        if (work.Length > 0)
        {
            foreach (string segment in work.Split('+'))
            {
                if (segment.Length == 0)
                    throw new FormatException($"malformed chord \"{token}\" (empty part — a literal plus key is written \"++\")");

                if (ModifierAliases.TryGetValue(segment, out Modifiers mod))
                {
                    if (mods.HasFlag(mod))
                        throw new FormatException($"duplicate modifier \"{segment}\" in \"{token}\"");
                    mods |= mod;
                }
                else
                {
                    string canonical = CanonicalizeKey(segment, token);
                    if (baseKey is not null)
                        throw new FormatException($"chord \"{token}\" has more than one non-modifier key (\"{baseKey}\" and \"{canonical}\")");
                    baseKey = canonical;
                }
            }
        }

        if (baseKey is null && mods == Modifiers.None)
            throw new FormatException($"malformed chord \"{token}\"");

        return new KeyChord(mods, baseKey ?? "");
    }

    private static string CanonicalizeKey(string segment, string token)
    {
        if (KeyAliases.TryGetValue(segment, out string? canonical))
            return canonical;
        if (FKeyRegex().IsMatch(segment))
            return "F" + segment[1..];
        if (segment.Length == 1)
        {
            char c = segment[0];
            if (char.IsLetter(c)) return char.ToUpperInvariant(c).ToString();
            if (!char.IsControl(c)) return segment; // digits and punctuation as-is
        }
        throw new FormatException($"unknown key \"{segment}\" in \"{token}\"");
    }
}
