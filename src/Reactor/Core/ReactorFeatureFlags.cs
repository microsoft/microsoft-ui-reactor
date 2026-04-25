namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Process-wide feature flags that gate risky or in-progress behavior. Each flag is
/// a plain mutable boolean so apps can flip it on startup; the defaults are chosen
/// to preserve existing behavior.
/// </summary>
/// <remarks>
/// <para>A flag lives here only while its feature is rolling out. Once a flag's
/// default flips to <c>true</c> and has soaked for a release, the flag is removed
/// along with the legacy code path it was gating — leaving permanent config knobs
/// here would slow future refactors.</para>
/// <para>Flags are read lazily — changes after a feature has already initialized
/// are not guaranteed to take effect. Tests that need to toggle a flag should save
/// and restore the value around the scope that depends on it.</para>
/// </remarks>
public static class ReactorFeatureFlags
{
    /// <summary>
    /// When true, <c>DataGridComponent</c> reads paged data through
    /// <c>UseInfiniteResource</c> / <c>UseDataSource</c> instead of the legacy
    /// <c>DataPageCache</c>. Part of the Phase-3 migration in
    /// <c>docs/specs/020-async-resources-design.md</c> §11.
    /// </summary>
    /// <remarks>
    /// Default: <c>false</c>. The hook-based path is covered by <c>DataGridParityFixtures</c>
    /// but is not yet the default for consumers with custom <c>IDataSource</c> implementations.
    /// </remarks>
    public static bool UseHookBasedPaging { get; set; }

    /// <summary>
    /// When true, stale <c>UseResource</c> entries refetch on window activation / app
    /// resume events. Per-resource opt-out is exposed through
    /// <c>ResourceOptions.RefetchOnWindowFocus</c> (default <c>false</c>).
    /// </summary>
    /// <remarks>
    /// Default: <c>false</c>. This mirrors TanStack Query's opt-in posture — window-focus
    /// revalidation is surprising in desktop apps where Alt-Tab is a common idiom.
    /// </remarks>
    public static bool FocusRevalidation { get; set; }

    /// <summary>
    /// When true, the reconciler records which <c>UIElement</c>s were mounted or
    /// modified during each pass and Reactor hosts (<see cref="Hosting.ReactorHost"/>
    /// and <see cref="Hosting.ReactorHostControl"/>, via the shared
    /// <c>HighlightOverlayWiring</c>) draw semi-transparent overlays
    /// (red = new mount, yellow = property update) so you can visualize reconcile
    /// impact. The overlay is rendered via Composition visuals and is hit-test invisible.
    /// </summary>
    /// <remarks>
    /// Default: <c>false</c>. Intended for interactive debugging sessions. When off,
    /// the reconciler skips all collection work so there is zero overhead.
    /// </remarks>
    public static bool HighlightReconcileChanges { get; set; }

    /// <summary>
    /// When true, Reactor hosts start an in-process ETW session on the
    /// <c>Microsoft-Windows-XAML</c> provider, attribute measure/arrange events to
    /// the owning Reactor <c>Component</c>, and render a per-Component cost-meter
    /// overlay (layout ms + authored vs rendered element count). See
    /// <c>docs/specs/032-layout-cost-overlay-design.md</c>.
    /// </summary>
    /// <remarks>
    /// <para>Default: <c>false</c>. Reactor hosts support toggling this flag
    /// during a session: enabling it lazily builds the ETW pipeline + overlay
    /// wrapper on the next render and starts the layout-cost ETW session;
    /// disabling it stops the session, disposes the layout-cost sub-overlay,
    /// pauses the idle-decay ticker, and (if no other dev overlay flag is on)
    /// tears the wrapper back down. The transitions are edge-triggered, so
    /// every render pass is cheap. Tests that mutate the flag must save and
    /// restore the previous value.</para>
    /// <para>When off, no ETW session is started, no overlay is constructed, and the
    /// reconciler's mount path pays only a single boolean check.</para>
    /// </remarks>
    public static bool ShowLayoutCost { get; set; }
}
