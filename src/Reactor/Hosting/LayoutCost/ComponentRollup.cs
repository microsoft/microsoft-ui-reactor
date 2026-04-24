namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// Mutable per-Component cost rollup maintained by
/// <see cref="LayoutCostAttribution"/>. One instance per mounted Component
/// plus one for the synthetic <see cref="ComponentIdentity.Chrome"/> bucket.
/// </summary>
/// <remarks>
/// All reads/writes happen on the UI thread via the drain callback — no
/// cross-thread sync is needed. See spec §Data pipeline.
/// </remarks>
internal sealed class ComponentRollup
{
    /// <summary>EMA smoothing factor from spec §Per-Component rollups &amp; EMAs.</summary>
    public const double EmaAlpha = 0.2;

    public ComponentIdentity Id { get; }
    public string DisplayName { get; set; }
    public int Depth { get; set; }

    /// <summary>
    /// Authored-element count — set externally by the reconciler on mount/unmount
    /// when <see cref="Core.ReactorFeatureFlags.ShowLayoutCost"/> is true. The
    /// reconciler integration lives in spec §2.1 and is audited/completed as part
    /// of host wiring in Phase 3.
    /// </summary>
#pragma warning disable CS0649 // assigned from reconciler mount path (Phase 3 wiring)
    public int AuthoredElementCount;
#pragma warning restore CS0649

    /// <summary>Rect of the Component subtree in root-relative coords.</summary>
    public float SubtreeX;
    public float SubtreeY;
    public float SubtreeW;
    public float SubtreeH;

    // ── Current-frame accumulators ──────────────────────────────────────────
    public long FrameMeasureTicks;
    public long FrameArrangeTicks;
    public int FrameRenderedCount;

    // ── Smoothed outputs ────────────────────────────────────────────────────
    public double EmaLayoutMs;
    public int LastRenderedCount;
    /// <summary>Raw frame ms (measure+arrange) from the last CloseFrame — before EMA smoothing. Used by the sparkline.</summary>
    public double LastFrameMs;

    public ComponentRollup(ComponentIdentity id, string displayName, int depth)
    {
        Id = id;
        DisplayName = displayName;
        Depth = depth;
    }

    /// <summary>Collapse current-frame accumulators into the EMA and reset.</summary>
    public void CloseFrame()
    {
        LastFrameMs = (FrameMeasureTicks + FrameArrangeTicks) / 10_000.0;
        EmaLayoutMs = (EmaAlpha * LastFrameMs) + ((1 - EmaAlpha) * EmaLayoutMs);
        LastRenderedCount = FrameRenderedCount;
        FrameMeasureTicks = 0;
        FrameArrangeTicks = 0;
        FrameRenderedCount = 0;
    }

    public ComponentSnapshot ToSnapshot() => new(
        Id,
        DisplayName,
        Id.IsChrome ? -1 : AuthoredElementCount,
        LastRenderedCount,
        Depth,
        EmaLayoutMs,
        LastFrameMs,
        FrameMeasureTicks,
        FrameArrangeTicks,
        SubtreeX, SubtreeY, SubtreeW, SubtreeH);
}
