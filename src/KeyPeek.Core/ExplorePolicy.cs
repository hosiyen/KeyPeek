namespace KeyPeek.Core;

/// <summary>
/// Decides which keys Explore mode consumes.
///
/// This is the ONLY place that widens KeyPeek's swallow set beyond Esc, so it is a pure
/// function with no dependencies: the hook calls it with its two flags, and the test suite
/// calls it with every combination to prove the R2 guarantee — with Explore disabled the
/// answer is false for every key in every state, so the default-configuration input path is
/// byte-identical to the pre-Explore build.
///
/// Interception is scoped to "the panel is visible AND the user opted in". It never applies
/// while the panel is pinned (search mode owns the keyboard then — see OverlayPresenter).
/// </summary>
public static class ExplorePolicy
{
    // Standard Win32 virtual-key codes.
    public const int VkEnter = 0x0D;
    public const int VkPageUp = 0x21;   // VK_PRIOR
    public const int VkPageDown = 0x22; // VK_NEXT
    public const int VkEnd = 0x23;
    public const int VkHome = 0x24;
    public const int VkLeft = 0x25;
    public const int VkUp = 0x26;
    public const int VkRight = 0x27;
    public const int VkDown = 0x28;

    /// <summary>The nine keys Explore mode navigates with. Every one of them has defined
    /// behaviour in ExploreSelection — a swallowed key must never be inert.</summary>
    public static bool IsExploreKey(int vk) => vk is
        VkEnter or VkPageUp or VkPageDown or VkEnd or VkHome or
        VkLeft or VkUp or VkRight or VkDown;

    /// <summary>True when the hook should consume this key instead of passing it on.</summary>
    public static bool ShouldSwallow(int vk, bool overlayVisible, bool exploreEnabled) =>
        overlayVisible && exploreEnabled && IsExploreKey(vk);
}
