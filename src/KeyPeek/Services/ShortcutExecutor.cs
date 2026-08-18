using KeyPeek.Core;
using static KeyPeek.Interop.NativeMethods;

namespace KeyPeek.Services;

/// <summary>
/// Sends a shortcut's key chord(s) to the focused app with SendInput. Used ONLY when the
/// user explicitly clicks a shortcut row in the overlay — KeyPeek never injects input on
/// its own initiative.
///
/// The tricky part: at click time the user is often still physically holding the trigger
/// modifiers. Injection reconciles that — modifiers the chord needs but that aren't down
/// are pressed, held modifiers the chord does NOT want are released first, and everything
/// injected is released afterwards.
/// </summary>
internal static class ShortcutExecutor
{
    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D, ["Esc"] = 0x1B, ["Tab"] = 0x09, ["Space"] = 0x20,
        ["Backspace"] = 0x08, ["Delete"] = 0x2E, ["Insert"] = 0x2D,
        ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27,
        ["PrintScreen"] = 0x2C, ["ScrollLock"] = 0x91, ["Pause"] = 0x13,
        ["CapsLock"] = 0x14, ["NumLock"] = 0x90, ["Menu"] = 0x5D,
        ["+"] = 0xBB, ["="] = 0xBB, ["-"] = 0xBD, [","] = 0xBC, ["."] = 0xBE,
        ["/"] = 0xBF, ["\\"] = 0xDC, [";"] = 0xBA, ["'"] = 0xDE, ["`"] = 0xC0,
        ["["] = 0xDB, ["]"] = 0xDD,
    };

    // Keys that need the "extended" flag so apps see the right scan code.
    private static readonly HashSet<ushort> ExtendedKeys = new()
    { 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2C, 0x2D, 0x2E, 0x5B, 0x5C, 0x90 };

    public static void Execute(IReadOnlyList<KeyChord> chords, Logger log)
    {
        try
        {
            foreach (KeyChord chord in chords)
            {
                SendChord(chord);
                if (chords.Count > 1)
                    Thread.Sleep(60); // small gap between the steps of a sequence
            }
            log.Info($"Executed shortcut: {string.Join(" ", chords.Select(c => c.ToDisplayString()))}");
        }
        catch (Exception ex)
        {
            log.Error($"Shortcut injection failed: {ex.Message}");
        }
    }

    private static void SendChord(KeyChord chord)
    {
        Modifiers physical = PhysicallyHeldModifiers();
        Modifiers missing = chord.Mods & ~physical;
        Modifiers excess = physical & ~chord.Mods;

        var stream = new List<(ushort vk, bool down)>();

        // Release held modifiers the chord doesn't want (e.g. user holds Ctrl, clicks Alt+F4).
        foreach (ushort vk in ModifierVksDown(excess))
            stream.Add((vk, false));

        // Press what's missing.
        var pressed = new List<ushort>();
        foreach (Modifiers flag in SplitFlags(missing))
        {
            ushort vk = CanonicalModifierVk(flag);
            stream.Add((vk, true));
            pressed.Add(vk);
        }

        if (chord.Key.Length > 0)
        {
            // A bare letter (Facebook's J/K/L, Gmail's E, YouTube's K…) must NOT go through
            // the keyboard layout: with the Vietnamese Telex IME active, J is the nặng tone
            // key and S/R/F/X are tones too, so an injected VK_J came out transformed — or
            // not at all. Caught live on this machine: injecting "bare" into our own search
            // box produced "bảe". Sending the character AS a character (KEYEVENTF_UNICODE,
            // VK_PACKET) bypasses layout and IME entirely; browsers surface it as the same
            // key event the page listens for. Modified chords keep VK injection — Ctrl+C
            // must stay a real C keystroke, and the IME never eats modified chords anyway.
            if (chord.Mods == Modifiers.None && chord.Key.Length == 1 &&
                chord.Key[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                Inject(stream); // release any still-held modifiers FIRST, or they modify the char
                InjectUnicodeChar(char.ToLowerInvariant(chord.Key[0]));
                return;
            }

            ushort vk = ResolveKeyVk(chord.Key)
                ?? throw new InvalidOperationException($"no virtual key for \"{chord.Key}\"");
            stream.Add((vk, true));
            stream.Add((vk, false));
        }

        // Release what we pressed, in reverse. (Deliberately NOT re-pressing "excess" keys:
        // the OS now considers them up; the user's eventual physical release is a no-op.)
        for (int i = pressed.Count - 1; i >= 0; i--)
            stream.Add((pressed[i], false));

        Inject(stream);
    }

    /// <summary>Send one character as itself (VK_PACKET): no virtual-key, no keyboard
    /// layout, no IME — the receiver gets exactly this char. This is how a bare-letter web
    /// shortcut survives a Vietnamese Telex keyboard being active.</summary>
    private static void InjectUnicodeChar(char c)
    {
        var inputs = new INPUT[2];
        for (int i = 0; i < 2; i++)
            inputs[i] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = KEYEVENTF_UNICODE | (i == 1 ? KEYEVENTF_KEYUP : 0u),
                    },
                },
            };
        SendInput(2, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
    }

    private static void Inject(List<(ushort vk, bool down)> stream)
    {
        var inputs = new INPUT[stream.Count];
        for (int i = 0; i < stream.Count; i++)
        {
            (ushort vk, bool down) = stream[i];
            uint flags = down ? 0u : KEYEVENTF_KEYUP;
            if (ExtendedKeys.Contains(vk))
                flags |= KEYEVENTF_EXTENDEDKEY;
            inputs[i] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } },
            };
        }
        SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
    }

    private static Modifiers PhysicallyHeldModifiers()
    {
        Modifiers held = Modifiers.None;
        if (KeyDown(0xA0) || KeyDown(0xA1)) held |= Modifiers.Shift;
        if (KeyDown(0xA2) || KeyDown(0xA3)) held |= Modifiers.Ctrl;
        if (KeyDown(0xA4) || KeyDown(0xA5)) held |= Modifiers.Alt;
        if (KeyDown(0x5B) || KeyDown(0x5C)) held |= Modifiers.Win;
        return held;

        static bool KeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    private static IEnumerable<ushort> ModifierVksDown(Modifiers flags)
    {
        foreach ((Modifiers flag, ushort left, ushort right) in new[]
        {
            (Modifiers.Shift, (ushort)0xA0, (ushort)0xA1),
            (Modifiers.Ctrl, (ushort)0xA2, (ushort)0xA3),
            (Modifiers.Alt, (ushort)0xA4, (ushort)0xA5),
            (Modifiers.Win, (ushort)0x5B, (ushort)0x5C),
        })
        {
            if (!flags.HasFlag(flag)) continue;
            if ((GetAsyncKeyState(left) & 0x8000) != 0) yield return left;
            if ((GetAsyncKeyState(right) & 0x8000) != 0) yield return right;
        }
    }

    private static IEnumerable<Modifiers> SplitFlags(Modifiers flags)
    {
        if (flags.HasFlag(Modifiers.Ctrl)) yield return Modifiers.Ctrl;
        if (flags.HasFlag(Modifiers.Shift)) yield return Modifiers.Shift;
        if (flags.HasFlag(Modifiers.Alt)) yield return Modifiers.Alt;
        if (flags.HasFlag(Modifiers.Win)) yield return Modifiers.Win;
    }

    private static ushort CanonicalModifierVk(Modifiers flag) => flag switch
    {
        Modifiers.Ctrl => 0xA2,
        Modifiers.Shift => 0xA0,
        Modifiers.Alt => 0xA4,
        _ => 0x5B,
    };

    private static ushort? ResolveKeyVk(string key)
    {
        if (NamedKeys.TryGetValue(key, out ushort vk))
            return vk;
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return (ushort)c;
        }
        if (key.Length is 2 or 3 && key[0] is 'F' or 'f' && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24)
            return (ushort)(0x6F + f); // VK_F1 = 0x70
        return null;
    }
}
