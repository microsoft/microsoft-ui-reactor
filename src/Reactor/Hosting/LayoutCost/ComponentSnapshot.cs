namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// Opaque, process-unique identifier for a mounted Reactor <c>Component</c>
/// as seen by the layout-cost attribution layer. Sentinel value
/// <see cref="ComponentIdentity.Chrome"/> represents the synthetic
/// &lt;chrome&gt; bucket that catches events with no Component owner.
/// </summary>
internal readonly record struct ComponentIdentity(long Value)
{
    public static readonly ComponentIdentity Chrome = new(-1);
    public bool IsChrome => Value == -1;
}

/// <summary>
/// Immutable per-frame snapshot of a Component's layout-cost state.
/// Handed to the overlay renderer and to <see cref="ILayoutCostReporter"/>
/// consumers.
/// </summary>
/// <param name="Id">Identity of the Component.</param>
/// <param name="DisplayName">Display name shown in the overlay and reporters.</param>
/// <param name="AuthoredElementCount">Authored UIElement count (what the Component declared). -1 for the chrome bucket.</param>
/// <param name="RenderedElementCount">Rendered UIElement count this frame (what WinUI actually materialized under the subtree).</param>
/// <param name="Depth">Depth of this Component in the Component tree — used by spatial attribution for innermost-match.</param>
/// <param name="EmaLayoutMs">Exponentially-moving-average of inclusive measure + arrange time in milliseconds.</param>
/// <param name="LastFrameMs">Most recent frame's raw (un-smoothed) measure+arrange time in ms. Sparkline source.</param>
/// <param name="FrameMeasureTicks">Most recent frame's inclusive measure time in ticks (100 ns).</param>
/// <param name="FrameArrangeTicks">Most recent frame's inclusive arrange time in ticks (100 ns).</param>
/// <param name="SubtreeX">Root-relative X of the Component's subtree bounding rect.</param>
/// <param name="SubtreeY">Root-relative Y of the Component's subtree bounding rect.</param>
/// <param name="SubtreeW">Width of the Component's subtree bounding rect.</param>
/// <param name="SubtreeH">Height of the Component's subtree bounding rect.</param>
internal readonly record struct ComponentSnapshot(
    ComponentIdentity Id,
    string DisplayName,
    int AuthoredElementCount,
    int RenderedElementCount,
    int Depth,
    double EmaLayoutMs,
    double LastFrameMs,
    long FrameMeasureTicks,
    long FrameArrangeTicks,
    float SubtreeX,
    float SubtreeY,
    float SubtreeW,
    float SubtreeH)
{
    /// <summary>Rendered / max(Authored, 1). One indicates no inflation; 10+ is a red flag.</summary>
    public double InflationRatio =>
        AuthoredElementCount <= 0 ? 0 : (double)RenderedElementCount / AuthoredElementCount;
}
