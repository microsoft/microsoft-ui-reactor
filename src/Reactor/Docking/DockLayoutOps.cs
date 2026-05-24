namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// Spec 046 §6.4 — public façade over the layout-tree operations apps
/// need to build, mutate, and inspect a <see cref="DockNode"/> tree
/// programmatically (e.g. an "Open new document" button that should
/// route through role-aware <see cref="DockTarget.Center"/> insert,
/// or a "Close pane" handler that needs the spec-046 reserved-empty
/// rule applied).
/// </summary>
/// <remarks>
/// Pure functional helpers over the public <see cref="DockNode"/> records
/// — no host, no model, no UI-thread affinity. Apps typically keep the
/// live layout in state (<c>UseState&lt;DockNode?&gt;</c>) and either
/// wire <see cref="DockManager.OnLiveLayoutChanged"/> for drag-driven
/// mutations or call these helpers from app-driven mutations (button
/// clicks, menu commands).
///
/// <para>
/// The internal <c>DockLayoutMutator</c> class wraps a larger set of
/// drag-drop infrastructure operations (group-targeted inserts, shape-
/// only override, container lookup by reference). Those aren't part of
/// the supported app-facing API surface — <see cref="DockLayoutOps"/>
/// is the curated subset apps should depend on.
/// </para>
/// </remarks>
public static class DockLayoutOps
{
    /// <summary>
    /// Place <paramref name="pane"/> into <paramref name="root"/> at
    /// the chosen <paramref name="target"/>. For <see cref="DockTarget.Center"/>,
    /// this is the spec-046 role-aware insert: the pane lands in the
    /// first group whose <see cref="DockTabGroup.Role"/> prefers the
    /// payload category (<see cref="Document"/> ↔
    /// <see cref="DockGroupRole.DocumentArea"/>; <see cref="ToolWindow"/>
    /// ↔ <see cref="DockGroupRole.ToolWindowStrip"/>), falling back to
    /// the first group that accepts the category, falling back finally
    /// to the leftmost-descendant heuristic.
    /// </summary>
    /// <remarks>
    /// For split / edge targets (Split*, Dock*), the new sibling group
    /// inherits a role inferred from the pane's category and the
    /// target's edge meaning (spec 046 §2.3): a <see cref="Document"/>
    /// payload's wrapper is <see cref="DockGroupRole.DocumentArea"/>; a
    /// <see cref="ToolWindow"/> at a Dock* edge becomes
    /// <see cref="DockGroupRole.ToolWindowStrip"/>. When the root is a
    /// single <see cref="DockTabGroup"/> whose role matches the pane's
    /// preference, the role propagates to the new sibling so
    /// "Document inside DocumentArea split right" produces two
    /// DocumentArea groups, not one DocumentArea + one General.
    /// </remarks>
    public static DockNode InsertPaneAtTarget(DockNode? root, DockableContent pane, DockTarget target)
        => Native.DockLayoutMutator.InsertPaneAtTarget(root, pane, target);

    /// <summary>
    /// Spec 046 §6.3 — overload that surfaces a routing-fallback
    /// diagnostic when no group's <see cref="DockTabGroup.Role"/>
    /// accepts the payload's category and the insert degraded to the
    /// pre-046 "leftmost descendant" heuristic. <paramref name="fallback"/>
    /// is non-null only in that degraded case; apps can route it into
    /// their own diagnostics surface (e.g. a status-bar warning or an
    /// <c>OperationLog</c> entry).
    /// </summary>
    public static DockNode InsertPaneAtTarget(
        DockNode? root, DockableContent pane, DockTarget target,
        out DockRoutingFallback? fallback)
        => Native.DockLayoutMutator.InsertPaneAtTarget(root, pane, target, out fallback);

    /// <summary>
    /// Remove <paramref name="pane"/> from <paramref name="root"/>.
    /// Returns the post-remove tree (or null when the tree collapsed to
    /// nothing) and a flag indicating whether the pane was found. Spec
    /// 046 §6.5: a <see cref="DockGroupRole.DocumentArea"/> group whose
    /// last document is removed survives as an empty reserved well.
    /// </summary>
    /// <remarks>
    /// Identity follows the spec 045 §2.4 rule: <c>ReferenceEquals</c>
    /// is the fast path, with key equality as a fallback so app code
    /// that holds a reference captured at one render can still match
    /// against a later render's rebuilt record.
    /// </remarks>
    public static (DockNode? Root, bool Found) RemovePane(DockNode? root, DockableContent pane)
        => Native.DockLayoutMutator.RemovePane(root, pane);

    /// <summary>
    /// Convenience: remove <paramref name="pane"/> from its current
    /// location, then re-insert at <paramref name="target"/>. Used by
    /// "move this doc to a new well" commands. When the pane wasn't
    /// found, the original tree is returned unchanged.
    /// </summary>
    public static DockNode? MovePaneToTarget(DockNode? root, DockableContent pane, DockTarget target)
        => Native.DockLayoutMutator.MovePaneToTarget(root, pane, target);

    /// <summary>
    /// Walk the tree to find the immediate container (a
    /// <see cref="DockTabGroup"/>, or the pane itself when it's the
    /// root leaf) holding <paramref name="pane"/>. Returns null when
    /// the pane isn't in the tree. Useful for "where does this pane
    /// live right now" lookups when the user-driven layout has shifted.
    /// </summary>
    public static DockNode? FindContainer(DockNode? root, DockableContent pane)
        => Native.DockLayoutMutator.FindContainer(root, pane);
}

/// <summary>
/// Spec 046 §6.3 — routing diagnostic returned by
/// <see cref="DockLayoutOps.InsertPaneAtTarget(DockNode?, DockableContent, DockTarget, out DockRoutingFallback?)"/>
/// when the role-aware insert couldn't find an accepting group and
/// degraded to the pre-046 "leftmost descendant" placement.
/// </summary>
/// <remarks>
/// The <see cref="Description"/> is a human-readable explanation suitable
/// for diagnostic logs or a status-bar surface. Apps can also detect the
/// fallback condition simply by checking non-null on the out parameter
/// — the description is informational, not parse-targeted.
/// </remarks>
public sealed record DockRoutingFallback(string Description);
