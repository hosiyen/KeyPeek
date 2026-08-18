using KeyPeek.Core;
using KeyPeek.Services;

namespace KeyPeek.UI;

/// <summary>What the hold controller needs from the overlay.</summary>
internal interface IOverlayPresenter
{
    void Show(ForegroundApp app, Modifiers held);
    void UpdateFilter(Modifiers held);
    void Hide(HideReason reason);

    /// <summary>Return a click-pinned panel to keyboard control (see HoldStateMachine).</summary>
    void UnpinToHold(Modifiers held);

    /// <summary>An Explore-mode navigation key the hook consumed (see ExplorePolicy).
    /// Called on the consumer thread; the presenter marshals to the UI thread.</summary>
    void ExploreKey(int vk);

    /// <summary>Screen point (physical pixels) inside the visible overlay panel? Used to
    /// decide whether a mouse click should dismiss the overlay or interact with it.</summary>
    bool IsPointInsideOverlay(int x, int y);
}
