namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// The "should a descendant Component's badge punch through its parent's
/// rollup?" decision. Pure, so its constants can be tuned without touching
/// the overlay renderer. Thresholds are compile-time for v1 per spec
/// §Surface-through rule.
/// </summary>
/// <remarks>
/// A descendant surfaces when any of these is true (spec §Surface-through):
/// - child.EmaLayoutMs &gt; 50% of parent's
/// - child.FrameRenderedCount &gt; 50% of parent's
/// - child.InflationRatio &gt; 2× parent's
///
/// The 50% / 2× boundaries are <b>inclusive</b> — a child at exactly 50%
/// surfaces. This is an opinionated call that favors revealing edge cases.
/// </remarks>
internal static class SurfaceThrough
{
    public const double MsRatioThreshold = 0.5;
    public const double CountRatioThreshold = 0.5;
    public const double InflationMultipleThreshold = 2.0;

    public static bool ShouldSurface(in ComponentSnapshot parent, in ComponentSnapshot child)
    {
        if (parent.EmaLayoutMs > 0 &&
            child.EmaLayoutMs >= parent.EmaLayoutMs * MsRatioThreshold)
            return true;

        if (parent.RenderedElementCount > 0 &&
            child.RenderedElementCount >= parent.RenderedElementCount * CountRatioThreshold)
            return true;

        if (parent.InflationRatio > 0 &&
            child.InflationRatio >= parent.InflationRatio * InflationMultipleThreshold)
            return true;

        return false;
    }
}
