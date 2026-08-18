namespace KeyPeek.Core;

/// <summary>
/// Whether to show the first-run welcome. Small enough to inline, kept separate because
/// getting it wrong is invisible in testing and obnoxious in use: a welcome window that
/// reappears, or one that pops up when the user only asked to open Settings.
/// </summary>
public static class OnboardingPolicy
{
    /// <param name="onboardingShown">Persisted flag — false on a fresh install, and also on
    /// installs that predate the flag (they see the welcome once, by design).</param>
    /// <param name="launchedWithSettings">Started with --settings: the user asked for a
    /// specific window, so don't greet them.</param>
    public static bool ShouldShowWelcome(bool onboardingShown, bool launchedWithSettings) =>
        !onboardingShown && !launchedWithSettings;
}
