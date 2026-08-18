namespace KeyPeek.Core;

/// <summary>Modifier keys as a bit set. Left/right variants of a key map to the same flag.</summary>
[Flags]
public enum Modifiers
{
    None  = 0,
    Ctrl  = 1,
    Shift = 2,
    Alt   = 4,
    Win   = 8,
}
