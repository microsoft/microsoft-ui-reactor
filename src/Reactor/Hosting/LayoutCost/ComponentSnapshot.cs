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
internal readonly record struct ComponentSnapshot(
    ComponentIdentity Id,
    string DisplayName,
    /// <summary>Authored UIElement count (what the Component declared). -1 for the chrome bucket.</summary>
    int AuthoredElementCount,
    /// <summary>Rendered UIElement count this frame (what WinUI actually materialized under the subtree).</summary>
    int RenderedElementCount,
    /// <summary>Depth of this Component in the Component tree — used by spatial attribution for innermost-match.</summary>
    int Depth,
    /// <summary>Exponentially-moving-average of inclusive measure + arrange time in milliseconds.</summary>
    double EmaLayoutMs,
    /// <summary>Most recent frame's raw (un-smoothed) measure+arrange time in ms. Sparkline source.</summary>
    double LastFrameMs,
    /// <summary>Most recent frame's inclusive measure time in ticks (100 ns).</summary>
    long FrameMeasureTicks,
    /// <summary>Most recent frame's inclusive arrange time in ticks (100 ns).</summary>
    long FrameArrangeTicks,
    /// <summary>Root-relative bounding rect of the Component's subtree.</summary>
    float SubtreeX,
    float SubtreeY,
    float SubtreeW,
    float SubtreeH)
{
    /// <summary>Rendered / max(Authored, 1). One indicates no inflation; 10+ is a red flag.</summary>
    public double InflationRatio =>
        AuthoredElementCount <= 0 ? 0 : (double)RenderedElementCount / AuthoredElementCount;
}
