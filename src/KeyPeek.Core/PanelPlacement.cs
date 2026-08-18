namespace KeyPeek.Core;

/// <summary>
/// Where the panel sits on the monitor. Pure arithmetic so the placement rules can be
/// tested without a window: the caller supplies the work area and the panel height in
/// physical pixels and gets the top edge back.
/// </summary>
public static class PanelPlacement
{
    public const string Top = "top";
    public const string Center = "center";
    public const string Bottom = "bottom";

    /// <summary>Fraction of the leftover vertical space above the panel in centre mode.
    /// Slightly above the true middle: the eye reads a panel placed dead-centre as low,
    /// and this is the placement KeyPeek has always shipped.</summary>
    private const double CenterBias = 0.40;

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Top => Top,
        Bottom => Bottom,
        _ => Center,
    };

    /// <param name="workTop">Work-area top edge (physical px).</param>
    /// <param name="workHeight">Work-area height (physical px).</param>
    /// <param name="panelHeight">Window height (physical px).</param>
    /// <param name="margin">Gap from the screen edge for top/bottom (physical px).</param>
    public static int TopEdge(string? position, int workTop, int workHeight, int panelHeight, int margin)
    {
        int slack = Math.Max(0, workHeight - panelHeight);
        int offset = Normalize(position) switch
        {
            Top => Math.Min(margin, slack),
            Bottom => Math.Max(0, slack - margin),
            _ => (int)(slack * CenterBias),
        };
        return workTop + offset;
    }
}
