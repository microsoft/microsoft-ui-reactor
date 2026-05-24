namespace Microsoft.UI.Reactor.Docking;

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
/// Visual chrome preset applied to a <see cref="DockTabGroup"/>'s tab strip.
/// Maps onto WinUI <c>TabView</c> resource-dictionary overrides — the
/// underlying control and accessibility tree are unchanged across presets.
/// </summary>
/// <remarks>
/// Spec 045 §4.6 — partial close-out. Selecting a preset doesn't replace
/// the control's template; it scopes a handful of theme-resource overrides
/// to that one <c>TabView</c>. New presets land additively; default is
/// <see cref="Win11"/> so existing layouts re-render unchanged.
/// </remarks>
public enum TabChrome
{
    /// <summary>
    /// Default Windows 11 TabView look: rounded header corners, theme
    /// background. No resource overrides applied.
    /// </summary>
    Win11,

    /// <summary>
    /// Sharp, dense IDE chrome: zero corner radius on tab headers, tighter
    /// header padding. Modeled after the VS Code / classic-Visual-Studio
    /// document-tab look.
    /// </summary>
    Flat,

    /// <summary>
    /// Tab-strip background uses <c>TitleBarBackgroundFillBrush</c> (when
    /// available) so the strip blends into the system title bar. Corner
    /// radius is unchanged from <see cref="Win11"/>. Spec 045 §4.6.
    /// </summary>
    TitleBar,
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
/// Categorizes a <see cref="DockTabGroup"/> for routing and reserved-empty
/// behavior. Default <see cref="General"/> preserves the pre-spec-046
/// semantics for callers that don't opt in.
/// </summary>
/// <remarks>
/// Spec 046 §6.1. The role tag participates in the routing matrix in
/// the internal <c>DockLayoutMutator</c> and in the drag-drop drop-target
/// filter (§6.6). Persisted via <c>DockLayoutJson</c> (§6.7); omitted
/// from JSON when at the default <see cref="General"/>.
/// </remarks>
public enum DockGroupRole
{
    /// <summary>
    /// Untyped group. Accepts every <see cref="DockableContent"/> category;
    /// removed from the layout by the post-close cull pass when emptied.
    /// </summary>
    General,

    /// <summary>
    /// The document well. Preferred target for <see cref="Document"/>
    /// inserts via <c>Dock(Center)</c>; rejects <see cref="ToolWindow"/>
    /// drops by default. Implicitly <c>ShowWhenEmpty</c> — survives the
    /// post-close cull pass so the well remains a visible drop target
    /// after the last document closes.
    /// </summary>
    DocumentArea,

    /// <summary>
    /// An edge strip of tool windows. Rejects <see cref="Document"/> drops
    /// by default; preferred target for <see cref="ToolWindow"/> inserts.
    /// Culled normally when emptied (unlike <see cref="DocumentArea"/>).
    /// </summary>
    ToolWindowStrip,
}

/// <summary>
/// Flag set describing which edges of a dock host a <see cref="ToolWindow"/>
/// may dock to. Default <see cref="DockSides.All"/> preserves the
/// pre-spec-046 unconstrained behavior.
/// </summary>
/// <remarks>
/// Spec 046 §6.2. Drives drag-drop drop-target eligibility (§6.6) and
/// programmatic <see cref="DockHostModel.PinToSide"/> validation (§6.6,
/// §9 Q4). RTL-aware: the mask is in <em>logical</em> sides (spec 045
/// §8.8); the drag-drop overlay translates visual edges back to logical
/// before consulting the mask.
/// </remarks>
[Flags]
public enum DockSides
{
    /// <summary>No edges allowed. Pinning always throws; the tool window is float-only.</summary>
    None = 0,

    /// <summary>Logical left edge (visual right in RTL).</summary>
    Left = 1 << 0,

    /// <summary>Top edge.</summary>
    Top = 1 << 1,

    /// <summary>Logical right edge (visual left in RTL).</summary>
    Right = 1 << 2,

    /// <summary>Bottom edge.</summary>
    Bottom = 1 << 3,

    /// <summary>All four edges. The default for <see cref="ToolWindow.AllowedSides"/>.</summary>
    All = Left | Top | Right | Bottom,
}

/// <summary>Extension helpers for <see cref="DockSide"/> ↔ <see cref="DockSides"/>.</summary>
/// <remarks>Spec 046 §6.2 / §6.6. Single-side enum → flag mask conversion
/// used at the drop-target filter call site.</remarks>
public static class DockSideExtensions
{
    /// <summary>Maps a single <see cref="DockSide"/> to its corresponding <see cref="DockSides"/> flag.</summary>
    public static DockSides ToFlag(this DockSide side) => side switch
    {
        DockSide.Left => DockSides.Left,
        DockSide.Top => DockSides.Top,
        DockSide.Right => DockSides.Right,
        DockSide.Bottom => DockSides.Bottom,
        _ => DockSides.None,
    };
}

/// <summary>
/// Where a pane sits in the dock topology at a given moment. Reported by
/// <c>UseDockState()</c> per spec 045 §5.3.11 / §2.17.
/// </summary>
/// <remarks>Spec 045 §5.3.11.</remarks>
public enum DockPaneState
{
    /// <summary>Pinned inside the host's docking tree (default).</summary>
    Docked,

    /// <summary>Hosted by a top-level floating window.</summary>
    Floating,

    /// <summary>ToolWindow collapsed to a side strip; click expands.</summary>
    AutoHidden,

    /// <summary>Auto-hidden ToolWindow whose <c>SidePopup</c> is currently expanded.</summary>
    AutoHiddenExpanded,

    /// <summary>Closed-but-remembered; no UI representation until reshown.</summary>
    Hidden,
}
