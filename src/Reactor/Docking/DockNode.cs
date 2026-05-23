using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// Sealed algebra of nodes that make up a docking layout. Implementations:
/// <see cref="DockSplit"/>, <see cref="DockTabGroup"/>,
/// <see cref="DockableContent"/> (with Phase 2 subclasses
/// <see cref="Document"/> and <see cref="ToolWindow"/>).
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
    double? Height = null,
    TabChrome TabChrome = TabChrome.Win11) : DockNode;

/// <summary>
/// A single dockable pane — the leaf of the dock tree. Carries
/// <see cref="Content"/> (a Reactor element subtree), a stable
/// <see cref="Key"/> for keyed reconciliation across reorderings,
/// and per-pane permission flags.
/// </summary>
/// <remarks>
/// Spec 045 §4.3. Phase 1 collapses Visual Studio's document/tool-window
/// distinction into this single role. Phase 2 introduces sealed subclasses
/// <see cref="Document"/> and <see cref="ToolWindow"/> (§5.3.1); the
/// <see cref="DockableContent"/> base remains for source compat.
///
/// <para>
/// <see cref="Key"/> identity rules per spec 042 (keyed reconciliation):
/// a stable, equatable value (string, GUID, enum, app domain id) is
/// required for the reconciler to preserve pane state across reorderings
/// and across drag-out / drop-back. <see cref="Key"/> replaces upstream
/// WinUI.Dock's <c>Title</c>-as-key convention (with the <c>##</c>
/// namespace hack) — there is no fallback to title-keying.
/// </para>
///
/// <para>
/// Phase 1 ships the closed-shape positional constructor below for source
/// compat; Phase 2's subclasses use init-only properties so apps can
/// opt-in to the new permission flags additively.
/// </para>
/// </remarks>
public record DockableContent : DockNode
{
    /// <summary>The pane's display title (tab caption / floating-window title).</summary>
    public string Title { get; init; }

    /// <summary>The Reactor element subtree mounted as the pane's body.</summary>
    public Element? Content { get; init; }

    /// <summary>
    /// Stable key for reconciler-grade pane identity. Required for state
    /// preservation across reorderings; replaces upstream's title-as-key
    /// convention with no fallback (spec 045 §4.4, §1.4).
    /// </summary>
    public object? Key { get; init; }

    /// <summary>Whether the close button (X) is enabled on this pane.</summary>
    public bool CanClose { get; init; }

    /// <summary>Whether the pin-to-side affordance is enabled on this pane.</summary>
    public bool CanPin { get; init; }

    /// <summary>
    /// Whether the pane can be torn out into a floating window. Default true
    /// (Phase 2 addition; see spec 045 §5.3.8).
    /// </summary>
    public bool CanFloat { get; init; } = true;

    /// <summary>
    /// Whether the pane can be moved (drag-reorder, drag-to-other-group).
    /// Default true (Phase 2 addition; see spec 045 §5.3.8).
    /// </summary>
    public bool CanMove { get; init; } = true;

    /// <summary>Optional fixed width hint (DIPs).</summary>
    public double? Width { get; init; }

    /// <summary>Optional fixed height hint (DIPs).</summary>
    public double? Height { get; init; }

    /// <summary>
    /// Opaque per-pane state envelope persisted alongside the layout JSON.
    /// Phase 2 introduces <see cref="Document{TState}"/> for typed state;
    /// this string field stays as the untyped escape hatch for P1 source
    /// compat (spec 045 §5.3.2).
    /// </summary>
    public string? PersistenceState { get; init; }

    /// <summary>
    /// Phase-1 closed-shape positional constructor — kept for source compat
    /// with apps that built against the P1 API. Phase 2 adds positional
    /// fields via the <see cref="Document"/> / <see cref="ToolWindow"/>
    /// subclasses instead of extending this constructor.
    /// </summary>
    /// <remarks>
    /// Spec 045 §5.3.1: "Non-breaking deprecation of the closed-shape
    /// <c>DockableContent(...)</c> constructor: warning analyzer points
    /// users to <c>Document(...)</c> / <c>ToolWindow(...)</c>. The base
    /// type still accepts the old shape for P1 source compat."
    /// </remarks>
    public DockableContent(
        string Title,
        Element? Content = null,
        object? Key = null,
        bool CanClose = false,
        bool CanPin = false,
        double? Width = null,
        double? Height = null,
        string? PersistenceState = null)
    {
        this.Title = Title;
        this.Content = Content;
        this.Key = Key;
        this.CanClose = CanClose;
        this.CanPin = CanPin;
        this.Width = Width;
        this.Height = Height;
        this.PersistenceState = PersistenceState;
    }

    /// <summary>Parameterless constructor — for with-expression chaining and Phase-2 subclass init.</summary>
    protected DockableContent() { Title = string.Empty; }
}
