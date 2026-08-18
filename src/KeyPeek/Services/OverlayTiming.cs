namespace KeyPeek.Services;

/// <summary>Hold-to-visible latency measurement (quality bar: well under 100 ms).
/// The consumer thread stamps when the hold timer fires; the presenter logs the delta
/// once the fade-in starts. Single writer/reader pair, torn reads impossible in
/// practice (one value per show, hundreds of ms apart).</summary>
internal static class OverlayTiming
{
    public static long ShowRequestedTimestamp;

    public static double ElapsedMs() =>
        (System.Diagnostics.Stopwatch.GetTimestamp() - ShowRequestedTimestamp)
        * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
}
