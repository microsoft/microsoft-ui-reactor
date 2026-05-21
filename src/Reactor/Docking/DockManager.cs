using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Docking;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 — Docking Windows
//
//  The public API surface committed at Phase 1 exit. Phase 2 swaps the
//  implementation (Reactor-native rewrite) without changing this API.
//  Phase 3 extends it additively (DockHost rename, DockableWindowRef).
//
//  Cross-reference:
//    docs/specs/045-docking-windows-design.md §4.3 — committed surface
//    docs/specs/tasks/045-docking-windows-implementation.md §1.3
//
//  These types live in Reactor.dll (the core) so that the Phase 2 native
//  rewrite — which has no XAML dependency — can extend them in place when
//  the Reactor.Docking.Xaml wrapper assembly is removed at §2.19.
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// A container element that hosts a tree of docked panes inside a Reactor
/// shell. Phase 1 reconciles to a single <c>WinUI.Dock.DockManager</c>
/// XAML control via the Reactor.Docking.Xaml wrapper assembly; Phase 2
/// swaps the implementation to a Reactor-native renderer without changing
/// this public surface.
/// </summary>
/// <remarks>
/// See spec 045 §4.3 for the committed API surface, §4.4 for the wrapper
/// behavior, §5 for the Phase 2 rewrite, and §6.4 for the Phase 3 rename
/// to <c>DockHost</c>.
///
/// <para>
/// The element is reconciled by Reactor like any other <see cref="Element"/>:
/// produce a fresh <see cref="DockManager"/> record on every render, and the
/// reconciler will diff the previous tree against the new one and apply the
/// minimum set of mutations.
/// </para>
///
/// <para>
/// Persistence: when <see cref="PersistenceId"/> is set, the layout JSON is
/// stored under <c>WindowPersistedScope["docking:&lt;PersistenceId&gt;"]</c>
/// (spec 036 §8). On mount, persisted layout is restored as a fallback when
/// the declarative <see cref="Layout"/> is null.
/// </para>
/// </remarks>
public sealed record DockManager : Element
{
    /// <summary>The root of the dock node tree. Null = empty layout.</summary>
    public DockNode? Layout { get; init; }

    /// <summary>Tool windows pinned to the left edge (auto-hide).</summary>
    public IReadOnlyList<DockableContent>? LeftSide { get; init; }

    /// <summary>Tool windows pinned to the top edge (auto-hide).</summary>
    public IReadOnlyList<DockableContent>? TopSide { get; init; }

    /// <summary>Tool windows pinned to the right edge (auto-hide).</summary>
    public IReadOnlyList<DockableContent>? RightSide { get; init; }

    /// <summary>Tool windows pinned to the bottom edge (auto-hide).</summary>
    public IReadOnlyList<DockableContent>? BottomSide { get; init; }

    /// <summary>
    /// The currently-active document. Resolved by <see cref="DockableContent.Key"/>
    /// equality against panes in <see cref="Layout"/>; mismatched keys leave
    /// activation untouched.
    /// </summary>
    public DockableContent? ActiveDocument { get; init; }

    /// <summary>
    /// Optional adapter for app-controlled rehydration of pane content and
    /// floating-window chrome. See spec 045 §4.3 / §4.4 and
    /// <see cref="IDockAdapter"/>.
    /// </summary>
    public IDockAdapter? Adapter { get; init; }

    /// <summary>
    /// Optional behavior hook for app-side observation of dock / float events.
    /// Phase 2 collapses this interface into the per-event Action props
    /// declared below (see <see cref="OnContentDocked"/> et al.); the
    /// interface stays as a one-release <c>[Obsolete]</c> forwarder
    /// (spec 045 §5.3.5).
    /// </summary>
#pragma warning disable CS0618 // IDockBehavior is obsolete during the §2.12 transition.
    [global::System.Obsolete(
        "Behavior is replaced by per-event Action props on DockManager " +
        "(OnContentDocked / OnContentFloating / OnContentFloated). " +
        "Slated for removal one release after Phase 2 ships. See spec 045 §5.3.5.",
        error: false)]
    public IDockBehavior? Behavior { get; init; }
#pragma warning restore CS0618

    /// <summary>
    /// Optional insertion-policy hook applied to programmatic adds. See
    /// <see cref="IDockLayoutStrategy"/> and spec 045 §5.3.6.
    /// </summary>
    /// <remarks>Spec 045 §5.3.6 (Phase 2 addition).</remarks>
    public IDockLayoutStrategy? LayoutStrategy { get; init; }

    /// <summary>
    /// Stable identifier used to scope persisted layout JSON inside the host
    /// <c>WindowPersistedScope</c>. Required to survive process restarts.
    /// </summary>
    public string? PersistenceId { get; init; }

    /// <summary>
    /// Schema version for persisted layout JSON. Phase 1 emits version 1;
    /// Phase 2 introduces version 2 with migrations registered via
    /// <see cref="IDockLayoutMigration"/> (spec 045 §5.3.4, §5.4).
    /// </summary>
    public int LayoutSchemaVersion { get; init; } = 1;

    // ── Phase 2 lifecycle events (spec 045 §5.3.5, tracking §2.12) ─────────
    //
    // Each *ing variant carries a Cancel flag; setting it to true aborts the
    // transition and leaves state unchanged. *ed variants are observation
    // only. No `+=` accumulation: the reconciler holds only the current
    // delegate. Replaces <see cref="IDockBehavior"/> (kept as obsolete
    // forwarder for one release).

    /// <summary>Fired before any structural layout mutation lands.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockLayoutChangingEventArgs>? OnLayoutChanging { get; init; }

    /// <summary>Fired after a structural layout mutation lands.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockLayoutChangedEventArgs>? OnLayoutChanged { get; init; }

    /// <summary>Fired before a Document is closed; Cancel aborts.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockDocumentClosingEventArgs>? OnDocumentClosing { get; init; }

    /// <summary>Fired after a Document is closed.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockDocumentClosedEventArgs>? OnDocumentClosed { get; init; }

    /// <summary>Fired before a ToolWindow auto-hides; Cancel aborts.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockToolWindowHidingEventArgs>? OnToolWindowHiding { get; init; }

    /// <summary>Fired after a ToolWindow auto-hides.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockToolWindowHiddenEventArgs>? OnToolWindowHidden { get; init; }

    /// <summary>Fired before a ToolWindow is closed; Cancel aborts.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockToolWindowClosingEventArgs>? OnToolWindowClosing { get; init; }

    /// <summary>Fired after a ToolWindow is closed.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockToolWindowClosedEventArgs>? OnToolWindowClosed { get; init; }

    /// <summary>Fired before a pane is torn out into a floating window; Cancel aborts.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockContentFloatingEventArgs>? OnContentFloating { get; init; }

    /// <summary>Fired after a pane is torn out into a floating window.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockContentFloatedEventArgs>? OnContentFloated { get; init; }

    /// <summary>Fired before a floating pane is docked back into a host; Cancel aborts.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockContentDockingEventArgs>? OnContentDocking { get; init; }

    /// <summary>Fired after a floating pane is docked back into a host.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockContentDockedEventArgs>? OnContentDocked { get; init; }

    /// <summary>Fired when the active content changes (tab focus, programmatic activation).</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockActiveContentChangedEventArgs>? OnActiveContentChanged { get; init; }

    /// <summary>Fired after a floating window is created.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockFloatingWindowCreatedEventArgs>? OnFloatingWindowCreated { get; init; }

    /// <summary>Fired after a floating window is closed.</summary>
    /// <remarks>Spec 045 §5.3.5.</remarks>
    public Action<DockFloatingWindowClosedEventArgs>? OnFloatingWindowClosed { get; init; }

    /// <summary>
    /// When true, the manager renders the drop-target overlay (9 buttons +
    /// hover preview rectangle) above the docked content. Spec 045 §2.3
    /// builds the overlay primitive; the §2.4 drag pipeline flips this
    /// flag mid-gesture to show the overlay during a tab drag. Apps can
    /// set it directly for testing / keyboard-initiated move (§2.10
    /// <c>Ctrl+Shift+M</c>).
    /// </summary>
    /// <remarks>Spec 045 §2.3.</remarks>
    public bool ShowDropTargets { get; init; }

    /// <summary>
    /// Optional callback fired when the overlay's hovered target changes.
    /// Null while no target is hovered. Fires at pointer-move rate during
    /// a drag; budget per call is ≤ 2 ms (spec §8.1) and the renderer's
    /// hot path is allocation-free aside from the args record.
    /// </summary>
    /// <remarks>Spec 045 §2.3.</remarks>
    public Action<DockTarget?>? OnDropTargetHovered { get; init; }

    /// <summary>
    /// Optional callback fired when the user confirms a drop-target (click,
    /// Enter, drop). The §2.4 drag pipeline subscribes to apply the dock
    /// operation; apps may also subscribe for telemetry / undo bookkeeping.
    /// </summary>
    /// <remarks>Spec 045 §2.3.</remarks>
    public Action<DockTarget>? OnDropTargetConfirmed { get; init; }

    /// <summary>
    /// Optional callback fired when the overlay is dismissed (Esc, drag
    /// cancel). Apps that opened the overlay via <see cref="ShowDropTargets"/>
    /// should reset it to <c>false</c> in response.
    /// </summary>
    /// <remarks>Spec 045 §2.3.</remarks>
    public Action? OnDropTargetsDismissed { get; init; }

    /// <summary>
    /// Fires whenever the host's effective layout mutates as a result
    /// of a drag/drop operation (§2.4). Carries the new
    /// <see cref="DockNode"/> root the renderer is now showing — apps
    /// can sync their own state (e.g. for live JSON inspection or
    /// undo bookkeeping).
    /// </summary>
    /// <remarks>Spec 045 §2.4. The host still also fires the
    /// canonical <see cref="OnContentDocked"/> for the per-pane event;
    /// this companion event carries the whole tree.</remarks>
    public Action<DockNode?>? OnLiveLayoutChanged { get; init; }

    /// <summary>
    /// Fires after a splitter drag completes (and the host's ratio
    /// store has been updated). Apps that own
    /// <see cref="SplitRatios"/> externally can use this to trigger a
    /// re-render — the dictionary is shared, so re-reading it on the
    /// next render surfaces the new ratios.
    /// </summary>
    /// <remarks>Spec 045 §2.1 — companion to <see cref="SplitRatios"/>
    /// for live introspection.</remarks>
    public Action? OnSplitterDragCompleted { get; init; }

    /// <summary>
    /// Optional in-memory operation log. When supplied, the host records
    /// every state-altering operation (drag begin / hover / confirm /
    /// cancel / tear-out, splitter resize, layout change) into this log.
    /// Apps use the log for live debugging + replay scrubbing. Spec 045
    /// keeps this scaffolding through P1–P4 per design discussion.
    /// </summary>
    /// <remarks>Spec 045 diagnostic infrastructure.</remarks>
    public Diagnostics.DockOperationLog? OperationLog { get; init; }

    /// <summary>
    /// Optional externally-owned ratio store for split-pane sizing. When
    /// supplied, the native renderer uses this dictionary instead of its
    /// internal <see cref="DockSplit"/>-ratio cache. Keys are tree-position
    /// paths (e.g. <c>"0"</c>, <c>"0/0"</c>, <c>"0/1"</c>); values are
    /// per-child ratio arrays summing to ~1.0.
    /// </summary>
    /// <remarks>
    /// Spec 045 §2.1 escape hatch. Exposed for app-driven resize (e.g.
    /// slider-driven layout demos) and for tests that need to inspect or
    /// mutate ratios without going through pointer/keyboard events.
    /// </remarks>
    public Dictionary<string, double[]>? SplitRatios { get; init; }

}
