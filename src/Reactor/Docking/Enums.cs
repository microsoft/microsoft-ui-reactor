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
