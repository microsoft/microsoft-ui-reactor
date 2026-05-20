using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Docking;

// ════════════════════════════════════════════════════════════════════════
//  Public API surface for spec 045 — Docking Windows
//
//  This file declares the surface committed at Phase 1 exit. Phase 2 swaps
//  the implementation (Reactor-native rewrite) without changing this API.
//  Phase 3 extends it additively (DockHost, DockableWindowRef).
//
//  Cross-reference:
//    docs/specs/045-docking-windows-design.md §4.3 — committed surface
//    docs/specs/tasks/045-docking-windows-implementation.md §1.3
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// A container element that hosts a tree of docked panes inside a Reactor
/// shell. Mounts to a single <c>WinUI.Dock.DockManager</c> XAML control in
/// Phase 1; Phase 2 swaps the implementation to a Reactor-native renderer
/// without changing this public surface.
/// </summary>
/// <remarks>
/// See spec 045 §4.3 for the committed API surface, §4.4 for the wrapper
/// behavior, and §6.4 for the Phase 3 rename to <c>DockHost</c>.
///
/// <para>
/// The element is reconciled by Reactor like any other <see cref="Element"/>:
/// produce a fresh <see cref="DockManager"/> record on every render, and the
/// reconciler will diff the previous tree against the new one and apply the
/// minimum set of mutations to the underlying control.
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
    /// Phase 2 collapses this interface into per-event Action props on a
    /// renamed <c>DockHost</c>; the interface stays as a one-release
    /// <c>[Obsolete]</c> forwarder (spec 045 §5.3.5).
    /// </summary>
    public IDockBehavior? Behavior { get; init; }

    /// <summary>
    /// Stable identifier used to scope persisted layout JSON inside the host
    /// <c>WindowPersistedScope</c>. Required to survive process restarts.
    /// </summary>
    public string? PersistenceId { get; init; }

    /// <summary>
    /// Schema version for persisted layout JSON. Phase 1 emits version 1;
    /// Phase 2 introduces version 2 with migrations registered via
    /// <c>IDockLayoutMigration</c> (spec 045 §5.3.4, §5.4).
    /// </summary>
    public int LayoutSchemaVersion { get; init; } = 1;
}

/// <summary>
/// Sealed algebra of nodes that make up a docking layout. Implementations:
/// <see cref="DockSplit"/>, <see cref="DockTabGroup"/>,
/// <see cref="DockableContent"/>.
/// </summary>
/// <remarks>
/// Spec 045 §4.3. Sealed via the abstract base + sealed concrete records;
/// new node kinds in P3 (<c>DockableWindowRef</c>) extend the algebra
/// additively per §6.4.
/// </remarks>
public abstract record DockNode;

/// <summary>
/// A split container with N children along a single orientation. Children
/// resize via splitters between them; widths/heights drive initial layout
/// and are persisted across re-mounts.
/// </summary>
/// <remarks>Spec 045 §4.3.</remarks>
public sealed record DockSplit(
    Orientation Orientation,
    IReadOnlyList<DockNode> Children,
    double? Width = null,
    double? Height = null,
    double? MinWidth = null,
    double? MinHeight = null,
    double? MaxWidth = null,
    double? MaxHeight = null) : DockNode;

/// <summary>
/// A group of panes presented as tabs. Panes are reordered by drag inside
/// the group; the active tab is reported via <see cref="SelectedIndex"/>.
/// </summary>
/// <remarks>Spec 045 §4.3. The tab strip uses WinUI <c>TabView</c>; spec
/// §11 keeps that decision through P2 for accessibility shape.</remarks>
public sealed record DockTabGroup(
    IReadOnlyList<DockableContent> Documents,
    TabPosition TabPosition = TabPosition.Top,
    bool CompactTabs = false,
    bool ShowWhenEmpty = false,
    int SelectedIndex = -1,
    double? Width = null,
    double? Height = null) : DockNode;

/// <summary>
/// A single dockable pane — the leaf of the dock tree. Carries
/// <see cref="Content"/> (a Reactor element subtree), a stable
/// <see cref="Key"/> for keyed reconciliation across reorderings,
/// and per-pane permission flags.
/// </summary>
/// <remarks>
/// Spec 045 §4.3. Phase 1 collapses Visual Studio's document/tool-window
/// distinction into this single role; Phase 2 introduces
/// <c>Document</c> and <c>ToolWindow</c> subclasses (§5.3.1) — the
/// <see cref="DockableContent"/> base type remains for source compat.
///
/// <para>
/// <see cref="Key"/> identity rules per spec 042 (keyed reconciliation):
/// a stable, equatable value (string, GUID, enum, app domain id) is
/// required for the reconciler to preserve pane state across reorderings
/// and across drag-out / drop-back. <see cref="Key"/> replaces upstream
/// WinUI.Dock's <c>Title</c>-as-key convention (with the <c>##</c>
/// namespace hack) — there is no fallback to title-keying.
/// </para>
/// </remarks>
public sealed record DockableContent(
    string Title,
    Element? Content = null,
    object? Key = null,
    bool CanClose = false,
    bool CanPin = false,
    double? Width = null,
    double? Height = null,
    string? PersistenceState = null) : DockNode;

/// <summary>Where the tab strip is rendered relative to the content.</summary>
/// <remarks>Spec 045 §4.3.</remarks>
public enum TabPosition
{
    /// <summary>Tabs above the active content (Visual Studio default).</summary>
    Top,

    /// <summary>Tabs below the active content (Office tool-window style).</summary>
    Bottom,
}

/// <summary>
/// Where to dock a pane when programmatically issuing
/// <c>DockTo(target, DockTarget)</c>. Split targets land inside the
/// current group's split parent; edge targets land at the manager root.
/// </summary>
/// <remarks>Spec 045 §4.3.</remarks>
public enum DockTarget
{
    /// <summary>Add as a tab in the destination group.</summary>
    Center,

    /// <summary>Split the destination group's parent; new pane on the left.</summary>
    SplitLeft,

    /// <summary>Split the destination group's parent; new pane on top.</summary>
    SplitTop,

    /// <summary>Split the destination group's parent; new pane on the right.</summary>
    SplitRight,

    /// <summary>Split the destination group's parent; new pane on the bottom.</summary>
    SplitBottom,

    /// <summary>Dock at the manager's left edge.</summary>
    DockLeft,

    /// <summary>Dock at the manager's top edge.</summary>
    DockTop,

    /// <summary>Dock at the manager's right edge.</summary>
    DockRight,

    /// <summary>Dock at the manager's bottom edge.</summary>
    DockBottom,
}

/// <summary>
/// Context for an <see cref="IDockAdapter.OnGroupCreated"/> callback. Carries
/// the freshly-created group's identity and the dragged-out source pane
/// (if the group was created by a tear-out, otherwise null).
/// </summary>
/// <remarks>
/// Spec 045 §4.3. Phase 1 is intentionally minimal — Phase 2's
/// <c>DockHostModel</c> introduces a richer mutation handle (§5.3.10).
/// </remarks>
public sealed record DockTabGroupContext(DockableContent? DraggedSource);

/// <summary>
/// App-supplied adapter for two paths the wrapper can't infer from the
/// declarative <see cref="DockManager.Layout"/> alone: content rehydration
/// after layout-JSON restore, and custom floating-window title bar chrome.
/// </summary>
/// <remarks>
/// Spec 045 §4.3. In Phase 2 the adapter surface collapses into per-event
/// Action props on <c>DockHost</c> (§5.3.5); the interface stays as a
/// <c>[Obsolete]</c> forwarder for one release.
/// </remarks>
public interface IDockAdapter
{
    /// <summary>
    /// Called when the wrapper instantiates a pane from persisted layout
    /// JSON. Apps return the Reactor <see cref="Element"/> subtree to mount
    /// as the pane's content. Return null to leave content empty (the pane
    /// will render its <see cref="DockableContent.Title"/> but no body).
    /// </summary>
    /// <param name="content">The reconstituted pane, including its
    /// <see cref="DockableContent.Key"/> and <see cref="DockableContent.PersistenceState"/>.
    /// Match by <c>Key</c>.</param>
    Element? OnContentCreated(DockableContent content);

    /// <summary>
    /// Called when the manager creates a new <c>DocumentGroup</c> at the
    /// tail end of a tear-out drag. Apps may use this to wire group-level
    /// chrome (e.g., a custom tab-strip toolbar).
    /// </summary>
    /// <param name="group">The freshly-created group's context.</param>
    /// <param name="draggedSource">The pane the tear-out originated from,
    /// or null if the group was created from layout JSON.</param>
    void OnGroupCreated(DockTabGroupContext group, DockableContent? draggedSource);

    /// <summary>
    /// Returns an optional Reactor element to render as the title bar of a
    /// freshly-created floating window. Return null to use the default.
    /// </summary>
    /// <param name="draggedSource">The pane whose tear-out spawned the
    /// floating window, or null if the window was restored from layout JSON.</param>
    Element? GetFloatingWindowTitleBar(DockableContent? draggedSource);
}

/// <summary>
/// App-supplied observation hook for dock and float lifecycle events.
/// Phase 1 surface; Phase 2 collapses into Action props (spec 045 §5.3.5).
/// </summary>
/// <remarks>
/// Spec 045 §4.3. Upstream WinUI.Dock's <c>ActivateMainWindow</c> is
/// absorbed by Reactor's window topology and is not exposed here.
/// </remarks>
public interface IDockBehavior
{
    /// <summary>Called after a pane is docked (programmatic or drag-in).</summary>
    /// <param name="src">The pane being docked.</param>
    /// <param name="target">The relative position of the dock landing.</param>
    void OnDocked(DockableContent src, DockTarget target);

    /// <summary>Called after a pane is torn out into a floating window.</summary>
    /// <param name="content">The pane being floated.</param>
    void OnFloating(DockableContent content);
}
