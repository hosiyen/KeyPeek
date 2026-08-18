namespace KeyPeek.Core;

/// <summary>
/// Identity of the panel content that has already been built and laid out off-screen.
///
/// The window title is part of the identity because app definitions can be selected by
/// TitleRegex: the same window can need different content after the user switches tab or
/// document, and that happens without any foreground-change event. Serving precomputed
/// content in that case would show the wrong app's shortcuts, so the match is deliberately
/// strict — a miss costs time, a false hit costs correctness.
/// </summary>
public readonly record struct PrecomputedContent(
    IntPtr Hwnd, string ProcessName, string Title, Modifiers Held)
{
    public bool Matches(IntPtr hwnd, string processName, string? title, Modifiers held) =>
        Hwnd == hwnd &&
        Held == held &&
        string.Equals(ProcessName, processName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Title, title ?? "", StringComparison.Ordinal);

    /// <summary>Warming makes no sense for these: the desktop and KeyPeek's own window
    /// never show a panel, and an empty process name is the preload placeholder.</summary>
    public static bool IsWarmable(string processName, bool isDesktop, bool isSelf) =>
        processName.Length > 0 && !isDesktop && !isSelf;
}
