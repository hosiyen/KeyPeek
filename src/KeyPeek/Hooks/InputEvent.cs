namespace KeyPeek.Hooks;

internal enum InputEventKind : byte
{
    Key,
    MouseButtonDown,
    MouseWheel,
    TimerFired,     // hold-delay timer elapsed (Value = generation)
    PinRequested,   // UI asked to pin the overlay open (search mode)
    UnpinRequested,
    ExecuteRequested, // UI asked to run a clicked shortcut (payload via HoldController)
}

/// <summary>
/// One raw input (or internal) event, passed from the hook thread to the consumer thread.
/// A plain struct so the hook callback allocates nothing.
/// </summary>
internal readonly record struct InputEvent(InputEventKind Kind, int Value, bool IsDown, int X, int Y)
{
    public static InputEvent Key(int vk, bool isDown) => new(InputEventKind.Key, vk, isDown, 0, 0);
    public static InputEvent Mouse(InputEventKind kind, int x, int y) => new(kind, 0, false, x, y);
    public static InputEvent Timer(int generation) => new(InputEventKind.TimerFired, generation, false, 0, 0);
    public static InputEvent Pin() => new(InputEventKind.PinRequested, 0, false, 0, 0);
    public static InputEvent Unpin() => new(InputEventKind.UnpinRequested, 0, false, 0, 0);
    public static InputEvent Execute() => new(InputEventKind.ExecuteRequested, 0, false, 0, 0);
}
