using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// Context slots used to surface docking state to function components via
/// the <see cref="DockHooks"/> hooks. Spec 045 §5.3.11; tracking §2.17.
/// </summary>
/// <remarks>
/// Each slice of dock state is a separate <see cref="Context{T}"/> so a
/// consumer that hooks one slice (e.g. <c>UseActivePaneKey</c>) only
/// re-renders when that specific slice changes — selector-style precision
/// per the spec, achieved by Reactor's existing context-change scope
/// (which only invalidates components subscribed to the changed context).
/// </remarks>
public static class DockContexts
{
    /// <summary>The nearest enclosing host's live <see cref="DockHostModel"/>, or null outside any host.</summary>
    public static readonly Context<DockHostModel?> Host = new(defaultValue: null);

    /// <summary>The active pane key (object equality), or null when no pane is active.</summary>
    public static readonly Context<object?> ActivePaneKey = new(defaultValue: null);

    /// <summary>Identity of the enclosing pane (Title + Key + content reference), or null outside any pane subtree.</summary>
    public static readonly Context<DockPaneInfo?> Pane = new(defaultValue: null);

    /// <summary>Per-pane current dock state (Docked/Floating/AutoHidden/...). Default <see cref="DockPaneState.Docked"/>.</summary>
    public static readonly Context<DockPaneState> PaneState = new(defaultValue: DockPaneState.Docked);

    /// <summary>Wide-net layout snapshot — re-renders consumer on any structural change. Use sparingly.</summary>
    public static readonly Context<DockLayoutSnapshot?> LayoutSnapshot = new(defaultValue: null);
}

/// <summary>
/// Immutable snapshot of a dock host's structural state, surfaced via
/// <see cref="DockHooks.UseDockLayout"/>. Wide-net subscription:
/// consumers re-render on any structural change (use sparingly — meant
/// for devtools, not for pane content).
/// </summary>
/// <remarks>Spec 045 §5.3.11.</remarks>
public sealed record DockLayoutSnapshot(
    DockNode? Root,
    IReadOnlyList<ToolWindow> LeftSide,
    IReadOnlyList<ToolWindow> TopSide,
    IReadOnlyList<ToolWindow> RightSide,
    IReadOnlyList<ToolWindow> BottomSide,
    IReadOnlyList<FloatingDockWindow> Floating,
    DockableContent? ActiveContent);
