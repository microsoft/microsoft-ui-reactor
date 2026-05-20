using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// Property hooks for docking state inside a function component. Spec 045
/// §5.3.11; tracking §2.17.
/// </summary>
/// <remarks>
/// Each hook subscribes to a single <see cref="DockContexts"/> slot, so the
/// consumer's re-render scope is the smallest slice that can answer the
/// question:
/// <list type="bullet">
///   <item><see cref="UseDockHost"/> — re-renders only when the *host model
///   instance* changes (rare; mostly mount/unmount).</item>
///   <item><see cref="UseActivePaneKey"/> — re-renders only when the active
///   key changes. Tab focus across the whole layout naturally invalidates
///   one consumer of this hook per pane subtree.</item>
///   <item><see cref="UseIsActivePane"/> — re-renders only on transitions
///   (false→true or true→false).</item>
///   <item><see cref="UsePane"/> — fixed identity for the lifetime of the
///   pane subtree. Throws when called outside any pane content.</item>
///   <item><see cref="UseDockState"/> — re-renders per pane on transitions
///   (Docked → Floating → Hidden → …).</item>
///   <item><see cref="UseDockLayout"/> — wide-net; re-renders on any
///   structural change. Used by devtools; rarely by pane content.</item>
/// </list>
///
/// <para>
/// The hooks are extension methods on <see cref="RenderContext"/> so the
/// call site reads <c>ctx.UseDockHost()</c> — symmetric with the other
/// context hooks like <c>UseColorScheme</c>.
/// </para>
/// </remarks>
public static class DockHooks
{
    /// <summary>
    /// Returns the nearest enclosing <see cref="DockHostModel"/>, or null
    /// when the component is rendered outside any docking host.
    /// </summary>
    /// <remarks>Spec 045 §5.3.11.</remarks>
    public static DockHostModel? UseDockHost(this RenderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ctx.UseContext(DockContexts.Host);
    }

    /// <summary>
    /// Returns the active pane's <see cref="DockableContent.Key"/>, or null
    /// when no pane is active. Re-renders the consumer only when the active
    /// key changes (selector-style scope).
    /// </summary>
    /// <remarks>Spec 045 §5.3.11.</remarks>
    public static object? UseActivePaneKey(this RenderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ctx.UseContext(DockContexts.ActivePaneKey);
    }

    /// <summary>
    /// Returns true when the *enclosing pane* is the host's currently-active
    /// pane. Re-renders the consumer only on the false ↔ true transition.
    /// </summary>
    /// <remarks>
    /// Resolves "the enclosing pane" via <see cref="UsePane"/>; returns
    /// false outside any pane subtree.
    /// </remarks>
    public static bool UseIsActivePane(this RenderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var pane = ctx.UseContext(DockContexts.Pane);
        var activeKey = ctx.UseContext(DockContexts.ActivePaneKey);
        if (pane is null) return false;
        return pane.Value.Key is not null && Equals(pane.Value.Key, activeKey);
    }

    /// <summary>
    /// Returns identity info for the enclosing pane subtree. Throws
    /// <see cref="InvalidOperationException"/> when called from a component
    /// that isn't rendered inside a docked pane.
    /// </summary>
    /// <remarks>Spec 045 §5.3.11.</remarks>
    public static DockPaneInfo UsePane(this RenderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var pane = ctx.UseContext(DockContexts.Pane);
        if (pane is null)
            throw new InvalidOperationException(
                "UsePane() must be called from a component rendered inside a docked pane. " +
                "See spec 045 §5.3.11.");
        return pane.Value;
    }

    /// <summary>
    /// Returns the enclosing pane's current dock state (Docked / Floating /
    /// AutoHidden / AutoHiddenExpanded / Hidden). Re-renders the consumer
    /// only on transitions.
    /// </summary>
    /// <remarks>Spec 045 §5.3.11. Defaults to <see cref="DockPaneState.Docked"/> outside any host.</remarks>
    public static DockPaneState UseDockState(this RenderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ctx.UseContext(DockContexts.PaneState);
    }

    /// <summary>
    /// Returns a wide snapshot of the dock layout — re-renders the consumer
    /// on any structural change. Documented as "used sparingly — devtools,
    /// not pane content."
    /// </summary>
    /// <remarks>Spec 045 §5.3.11. Returns null outside any host.</remarks>
    public static DockLayoutSnapshot? UseDockLayout(this RenderContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return ctx.UseContext(DockContexts.LayoutSnapshot);
    }
}
