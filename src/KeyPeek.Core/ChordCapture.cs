namespace KeyPeek.Core;

/// <summary>
/// Turns a real key press into a <see cref="KeyChord"/>, for the shortcut editor's
/// "press the keys you want" box. Kept out of the UI so the awkward cases — modifier-only
/// presses, unmappable keys, canonical spelling — are testable.
/// </summary>
public static class ChordCapture
{
    /// <summary>Modifier virtual-key codes, which are never a chord's base key.</summary>
    public static bool IsModifierKey(int vk) => vk is
        0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C or 0x10 or 0x11 or 0x12;

    /// <summary>
    /// Builds a chord from the base key and the modifiers held with it, or null when the
    /// press cannot be a shortcut: a lone modifier, or a key with no canonical name (media
    /// keys, IME keys, numpad — nothing in the library format spells those).
    /// </summary>
    public static KeyChord? FromKeyPress(int vk, Modifiers modifiers)
    {
        if (IsModifierKey(vk))
            return null;
        string? key = PowerToysKeyMap.FromVirtualKey(vk);
        return key is null ? null : new KeyChord(modifiers, key);
    }

    /// <summary>A shortcut may be a sequence (Ctrl+K then Ctrl+S). The parser accepts at
    /// most three chords, so the editor must refuse a fourth rather than write a file that
    /// only loads by luck.</summary>
    public const int MaxChordsPerShortcut = 3;
}
