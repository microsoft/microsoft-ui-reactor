using System.Linq;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Docking.Native;

/// <summary>
/// Spec 046 §6.3 — payload category used by the role-aware router to
/// decide which <see cref="DockTabGroup.Role"/> values accept a given
/// <see cref="DockableContent"/> insert.
/// </summary>
internal enum DockContentCategory
{
    /// <summary>The pane is a <see cref="Document"/> (or subclass).</summary>
    Document,

    /// <summary>The pane is a <see cref="ToolWindow"/>.</summary>
    ToolWindow,

    /// <summary>Base <see cref="DockableContent"/> with no category subclass.
    /// Spec 045 P1 source-compat shape — accepts every group's role.</summary>
    Untyped,
}

// Spec 046 §6.3 — DockRoutingFallback is the public type
// (Microsoft.UI.Reactor.Docking.DockRoutingFallback) so apps can consume
// the routing-diagnostic surface via DockLayoutOps. The internal mutator
// constructs and returns the same record type. See DockLayoutOps.cs.

/// <summary>
/// Spec 046 §6.6 — drag-drop drop-target filter. Decides whether a given
/// pane <em>payload</em> can land at a given target on a given group.
/// </summary>
internal static class DockDropFilter
{
    /// <summary>
    /// Returns <c>true</c> when the payload may land at the target relative
    /// to the candidate group. Two reject conditions, evaluated in order:
    /// (a) the group's <see cref="DockTabGroup.Role"/> rejects the payload's
    /// category (spec §6.3 matrix); (b) the payload is a <see cref="ToolWindow"/>
    /// whose <see cref="ToolWindow.AllowedSides"/> mask excludes the
    /// <em>logical</em> side of the target (RTL: logical side is the
    /// caller's responsibility per spec 045 §8.8). Untyped panes (P1
    /// back-compat) accept everywhere.
    /// </summary>
    public static bool CanDropInto(DockTabGroup target, DockableContent payload, DockSide? targetSide)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(payload);
        var cat = DockLayoutMutator.CategoryOf(payload);
        if (!DockLayoutMutator.AcceptsCategory(target.Role, cat)) return false;
        if (payload is ToolWindow tw && targetSide is DockSide s
            && !tw.AllowedSides.HasFlag(s.ToFlag())) return false;
        return true;
    }

    /// <summary>
    /// Convenience overload: variant for the layout-root edge targets where
    /// no specific group exists (the drop attaches to the layout's edge,
    /// not a group). Only the <see cref="ToolWindow.AllowedSides"/> mask
    /// matters here — there is no group role to consult.
    /// </summary>
    public static bool CanDockAtEdge(DockableContent payload, DockSide side)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload is ToolWindow tw && !tw.AllowedSides.HasFlag(side.ToFlag()))
            return false;
        return true;
    }

    /// <summary>
    /// Maps a <see cref="DockTarget"/> to its corresponding logical
    /// <see cref="DockSide"/> when the target carries an edge meaning
    /// (Dock* and Split* values), or <c>null</c> for <see cref="DockTarget.Center"/>.
    /// </summary>
    public static DockSide? SideOf(DockTarget target) => target switch
    {
        DockTarget.SplitLeft   => DockSide.Left,
        DockTarget.SplitTop    => DockSide.Top,
        DockTarget.SplitRight  => DockSide.Right,
        DockTarget.SplitBottom => DockSide.Bottom,
        DockTarget.DockLeft    => DockSide.Left,
        DockTarget.DockTop     => DockSide.Top,
        DockTarget.DockRight   => DockSide.Right,
        DockTarget.DockBottom  => DockSide.Bottom,
        _ => null,
    };
}

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.4 — immutable layout mutation for the drag pipeline.
//
//  Given a current root node + a removed pane (the dragged source) + a
//  target descriptor, returns a new root that places the pane at the
//  target slot. The helpers are pure functions over the DockNode algebra;
//  no shared state, no model-side mutation. Higher-level callers
//  (DockHostNativeComponent) feed the result back into their layout state.
//
//  Per spec §2.4 + §8.10: this path runs on the UI thread inside the
//  drag-end handler. There is no concurrency story here — each call
//  consumes a snapshot and yields a snapshot.
// ════════════════════════════════════════════════════════════════════════

internal static class DockLayoutMutator
{
    // ── Shape-only override helpers (spec 045 §2.30) ────────────────────
    //
    // The host's internal `layoutOverride` stores just the SHAPE of the
    // user's drag-modified tree — splits, tab groups, pane Keys — not
    // the full `DockableContent` records (which carry Content, Title,
    // CanClose, etc.). Storing full records would freeze a snapshot of
    // app state at drag-time; subsequent app re-renders couldn't push
    // fresh Content through because the override always wins.
    //
    // The shape tree is a `DockNode` with leaf `DockableContent` records
    // stripped down to just `Key` (every other field defaulted). At
    // render time, the host resolves the effective layout by walking
    // the shape and substituting each leaf with the corresponding
    // pane from `manager.Layout` (matched by Key). This gives apps the
    // idiomatic "declare full tree in Render(), state updates flow
    // naturally" pattern WHILE letting user drags persist as
    // shape-only state inside the host.

    /// <summary>
    /// Strip <see cref="DockableContent"/> records down to just their
    /// <see cref="DockableContent.Key"/> — used by the host before
    /// storing a user-mutated tree in <c>layoutOverride</c>. The
    /// resulting tree is "shape-only": preserves split orientations,
    /// tab-group structure, and pane identity, but carries no
    /// Content / Title / CanClose / other app-owned fields.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A <see cref="DockableContent"/> leaf is encountered with a
    /// <c>null</c> Key. Spec 045 §4.2 requires every docked pane to
    /// carry a non-null Key — without one, the resolve step in
    /// <see cref="ResolveContents(DockNode?, DockNode?)"/> cannot
    /// substitute fresh app state and the override would silently
    /// freeze stale Content / Title.
    /// </exception>
    public static DockNode? StripContent(DockNode? node)
    {
        if (node is null) return null;
        switch (node)
        {
            case DockableContent leaf:
                if (leaf.Key is null)
                    throw new InvalidOperationException(
                        "DockableContent in a docked tree must carry a non-null Key. " +
                        "Spec 045 §4.2 — shape-only overrides resolve by Key. " +
                        $"Offending leaf has Title='{leaf.Title}'.");
                return new DockableContent(string.Empty, Key: leaf.Key);
            case DockTabGroup grp:
            {
                var docs = new DockableContent[grp.Documents.Count];
                for (int i = 0; i < grp.Documents.Count; i++)
                    docs[i] = (DockableContent)StripContent(grp.Documents[i])!;
                return grp with { Documents = docs };
            }
            case DockSplit split:
            {
                var kids = new DockNode[split.Children.Count];
                for (int i = 0; i < split.Children.Count; i++)
                    kids[i] = StripContent(split.Children[i])!;
                return split with { Children = kids };
            }
            default:
                return node;
        }
    }

    /// <summary>
    /// Resolve a shape-only tree against an app-supplied "source" tree
    /// (typically <c>manager.Layout</c>). Walks <paramref name="shape"/>
    /// and substitutes each leaf with the equivalent leaf from
    /// <paramref name="source"/> (matched by
    /// <see cref="DockableContent.Key"/>). Splits and tab groups carry
    /// their orientations / selected-index from the shape — those are
    /// host-owned state. Leaves whose key isn't found in
    /// <paramref name="source"/> remain as the shape's key-only record;
    /// callers can use this to detect orphans (e.g. panes the app
    /// removed while still in the override).
    /// </summary>
    public static DockNode? ResolveContents(DockNode? shape, DockNode? source)
    {
        if (shape is null) return null;
        if (source is null) return shape;
        var index = new Dictionary<object, DockableContent>();
        IndexLeavesInto(source, index);
        return ResolveInner(shape, index);
    }

    /// <summary>
    /// Resolve a shape against a pre-built dictionary of known panes.
    /// Used by the host when it needs to merge multiple sources of
    /// pane records — e.g. <c>manager.Layout</c> (app-supplied) +
    /// model-mutator additions (panes added via
    /// <c>DockHostModel.Dock</c> that don't yet live in the app's
    /// tree). The host populates the dict and passes it in.
    /// </summary>
    public static DockNode? ResolveContents(DockNode? shape, Dictionary<object, DockableContent> index)
    {
        if (shape is null) return null;
        return ResolveInner(shape, index);
    }

    /// <summary>
    /// Walk <paramref name="node"/> and write each leaf into
    /// <paramref name="index"/> by <see cref="DockableContent.Key"/>.
    /// Leaves with null Key are skipped. Public so the host can
    /// maintain a long-lived "known panes" dictionary across renders.
    /// </summary>
    public static void IndexLeavesInto(DockNode? node, Dictionary<object, DockableContent> index)
    {
        if (node is null) return;
        switch (node)
        {
            case DockableContent leaf when leaf.Key is not null:
                index[leaf.Key] = leaf;
                break;
            case DockTabGroup grp:
                foreach (var d in grp.Documents) IndexLeavesInto(d, index);
                break;
            case DockSplit split:
                foreach (var c in split.Children) IndexLeavesInto(c, index);
                break;
        }
    }

    private static DockNode ResolveInner(DockNode shape, Dictionary<object, DockableContent> index)
    {
        switch (shape)
        {
            case DockableContent leaf:
                if (leaf.Key is not null && index.TryGetValue(leaf.Key, out var fresh))
                    return fresh;
                return leaf;
            case DockTabGroup grp:
            {
                var docs = new DockableContent[grp.Documents.Count];
                for (int i = 0; i < grp.Documents.Count; i++)
                    docs[i] = (DockableContent)ResolveInner(grp.Documents[i], index);
                return grp with { Documents = docs };
            }
            case DockSplit split:
            {
                var kids = new DockNode[split.Children.Count];
                for (int i = 0; i < split.Children.Count; i++)
                    kids[i] = ResolveInner(split.Children[i], index);
                return split with { Children = kids };
            }
            default:
                return shape;
        }
    }


    /// <summary>
    /// Remove a pane identified by reference from a layout tree. Returns
    /// (newRoot, found). Collapses empty parent containers (a DockSplit
    /// with zero children → null; a DockTabGroup with zero documents →
    /// null) so the layout doesn't accumulate dead branches.
    /// </summary>
    public static (DockNode? Root, bool Found) RemovePane(DockNode? root, DockableContent pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (root is null) return (null, false);
        var (intermediate, found) = RemoveInner(root, pane);
        if (!found) return (root, false);
        // Spec 046 §6.5 (refined per app feedback): the reserved-empty
        // rule applies to the LAST remaining DocumentArea — extra
        // DocumentArea groups created by split-on-drag cull when they
        // run dry so the layout doesn't accumulate empty wells. Without
        // this pass, dragging a doc out of a split DocumentArea and
        // then closing the remaining docs would leave two empty wells
        // side-by-side instead of one.
        return (PruneRedundantEmptyDocumentAreas(intermediate), true);
    }

    /// <summary>
    /// Spec 046 §6.5 (refined per app feedback): an empty DocumentArea
    /// survives ONLY when it's the only DocumentArea in the tree (the
    /// "reserved well" purpose). When at least one non-empty DocumentArea
    /// exists anywhere in the tree, empty DocumentAreas cull — the
    /// reserved-well role is fulfilled by the non-empty one, so the
    /// split arm that just became empty should collapse and let its
    /// surviving sibling fill the space (Scene J close-doc-in-split repro).
    /// When all DocumentAreas are empty, keep the first (tree-order) so
    /// the original reserved well still appears.
    /// </summary>
    private static DockNode? PruneRedundantEmptyDocumentAreas(DockNode? node)
    {
        if (node is null) return null;
        bool anyNonEmpty = AnyNonEmptyDocumentArea(node);
        bool keptFirstEmpty = false;
        return PruneInner(node, anyNonEmpty, ref keptFirstEmpty);
    }

    private static DockNode? PruneInner(DockNode node, bool anyNonEmptyExists, ref bool keptFirstEmpty)
    {
        switch (node)
        {
            // Spec 046 §6.5: `ShowWhenEmpty = true` opts out of the prune
            // pass — apps that explicitly want every well preserved set
            // the flag and we honor it. The default rule applies only to
            // the implicit DocumentArea-implies-ShowWhenEmpty path.
            case DockTabGroup grp
                when grp.Role == DockGroupRole.DocumentArea
                     && grp.Documents.Count == 0
                     && !grp.ShowWhenEmpty:
                // If any non-empty DocumentArea exists, this empty one
                // collapses so the surrounding split can simplify.
                if (anyNonEmptyExists) return null;
                // All-empty case: keep the first (tree-order), cull the rest.
                if (!keptFirstEmpty) { keptFirstEmpty = true; return grp; }
                return null;
            case DockSplit s:
            {
                var kept = new List<DockNode>(s.Children.Count);
                for (int i = 0; i < s.Children.Count; i++)
                {
                    var child = PruneInner(s.Children[i], anyNonEmptyExists, ref keptFirstEmpty);
                    if (child is not null) kept.Add(child);
                }
                if (kept.Count == 0) return null;
                if (kept.Count == 1) return kept[0];
                return s with { Children = kept.ToArray() };
            }
            default:
                return node;
        }
    }

    private static bool AnyNonEmptyDocumentArea(DockNode node) => node switch
    {
        DockTabGroup g
            when g.Role == DockGroupRole.DocumentArea && g.Documents.Count > 0 => true,
        DockSplit s => s.Children.Any(AnyNonEmptyDocumentArea),
        _ => false,
    };

    /// <summary>
    /// Walks the layout to find the immediate container (a
    /// <see cref="DockTabGroup"/>, or a bare <see cref="DockableContent"/>
    /// if the pane IS the root) holding <paramref name="pane"/>. Returns
    /// null when the pane isn't reachable. Used by the §2.15 PreviousContainer
    /// tracker to record where a pane lived before close / tear-out so a
    /// later show-from-history lands it back in the same group.
    /// </summary>
    public static DockNode? FindContainer(DockNode? root, DockableContent pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (root is null) return null;
        return Inner(root, pane);

        static DockNode? Inner(DockNode node, DockableContent target)
        {
            switch (node)
            {
                case DockableContent leaf:
                    return IsSamePane(leaf, target) ? leaf : null;
                case DockTabGroup grp:
                    foreach (var d in grp.Documents)
                        if (IsSamePane(d, target)) return grp;
                    return null;
                case DockSplit split:
                    foreach (var c in split.Children)
                    {
                        var r = Inner(c, target);
                        if (r is not null) return r;
                    }
                    return null;
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Spec 045 §2.4 — pane-identity comparison used by every layout-
    /// walking helper. <c>ReferenceEquals</c> is the fast path; when
    /// both panes carry a non-null <see cref="DockableContent.Key"/>,
    /// key equality is a fallback so a render-time rebuild that
    /// preserves keys but creates new <see cref="DockableContent"/>
    /// instances doesn't break the drag-session lookup
    /// (`session.Source` captures a ref at drag start; the layout the
    /// confirm fires against may have rebuilt records). Apps that
    /// reuse a Key for two distinct panes have a contract violation
    /// regardless — keys are spec-required to be unique within a
    /// host's tree.
    /// </summary>
    private static bool IsSamePane(DockableContent a, DockableContent b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Key is null || b.Key is null) return false;
        return a.Key.Equals(b.Key);
    }

    private static (DockNode? Node, bool Found) RemoveInner(DockNode node, DockableContent pane)
    {
        switch (node)
        {
            case DockableContent leaf:
                return IsSamePane(leaf, pane)
                    ? ((DockNode?)null, true)
                    : (node, false);

            case DockTabGroup group:
            {
                var docs = group.Documents;
                for (int i = 0; i < docs.Count; i++)
                {
                    if (!IsSamePane(docs[i], pane)) continue;
                    if (docs.Count == 1)
                    {
                        // Spec 046 §6.5 — DocumentArea groups survive empty
                        // (reserved well). The cull is skipped here so the
                        // group remains a visible drop target after the
                        // last document closes. Other roles cull as before.
                        if (group.Role == DockGroupRole.DocumentArea)
                            return (group with
                            {
                                Documents = Array.Empty<DockableContent>(),
                                SelectedIndex = -1,
                            }, true);
                        return ((DockNode?)null, true);
                    }
                    var next = new DockableContent[docs.Count - 1];
                    int j = 0;
                    for (int k = 0; k < docs.Count; k++)
                        if (k != i) next[j++] = docs[k];
                    return (group with
                    {
                        Documents = next,
                        SelectedIndex = group.SelectedIndex >= next.Length
                            ? next.Length - 1
                            : group.SelectedIndex,
                    }, true);
                }
                return (node, false);
            }

            case DockSplit split:
            {
                var children = split.Children;
                var rebuilt = new DockNode[children.Count];
                bool anyFound = false;
                int keep = 0;
                for (int i = 0; i < children.Count; i++)
                {
                    var (child, found) = RemoveInner(children[i], pane);
                    if (found) anyFound = true;
                    if (child is not null) rebuilt[keep++] = child;
                }
                if (!anyFound) return (node, false);
                if (keep == 0) return ((DockNode?)null, true);
                if (keep == 1) return (rebuilt[0], true);
                // Trim the buffer.
                var trimmed = new DockNode[keep];
                Array.Copy(rebuilt, trimmed, keep);
                return (split with { Children = trimmed }, true);
            }

            default:
                return (node, false);
        }
    }

    /// <summary>
    /// Place <paramref name="pane"/> into <paramref name="root"/> according
    /// to the chosen drop target. The split-relative targets (Center,
    /// SplitLeft/Right/Top/Bottom) apply to the layout root since the
    /// §2.3 overlay paints at the manager level; richer per-group split
    /// targets land with §2.4 cross-group hit-test (separate pass).
    /// </summary>
    public static DockNode InsertPaneAtTarget(DockNode? root, DockableContent pane, DockTarget target)
        => InsertPaneAtTarget(root, pane, target, out _);

    /// <summary>
    /// Spec 046 §6.3 — overload that surfaces any role-aware routing
    /// fallback the insert took. <paramref name="fallback"/> is non-null
    /// when a <see cref="DockTarget.Center"/> insert couldn't find a
    /// group whose <see cref="DockTabGroup.Role"/> accepts the payload
    /// category and degraded to today's "leftmost descendant" behavior.
    /// Callers (notably <c>DockHostNativeComponent</c>) route the
    /// fallback into <c>DockOperationLog</c> as a diagnostic.
    /// </summary>
    public static DockNode InsertPaneAtTarget(
        DockNode? root, DockableContent pane, DockTarget target,
        out DockRoutingFallback? fallback)
    {
        ArgumentNullException.ThrowIfNull(pane);
        fallback = null;
        if (root is null) return WrapAsGroup(pane, InferWrapRole(pane));
        // For split / edge targets, the inserted pane needs its own tab
        // strip so the user can drag / close / identify it. Wrapping in
        // a single-document DockTabGroup matches upstream WinUI.Dock
        // behavior: every pane lives inside a tab group, even when it's
        // the only document in that group. Spec 046 §2.3 / §6.3:
        // the new sibling group's role is inferred from the pane's
        // category and the target's edge-vs-split distinction so the VS
        // "documents stay documents, tools stay tools" invariant holds.
        return target switch
        {
            DockTarget.Center => AddAsTab(root, pane, out fallback),
            DockTarget.SplitLeft   => new DockSplit(Orientation.Horizontal, new DockNode[] { WrapAsGroup(pane, RoleForSplitWrap(pane, root)), root }),
            DockTarget.SplitRight  => new DockSplit(Orientation.Horizontal, new DockNode[] { root, WrapAsGroup(pane, RoleForSplitWrap(pane, root)) }),
            DockTarget.SplitTop    => new DockSplit(Orientation.Vertical,   new DockNode[] { WrapAsGroup(pane, RoleForSplitWrap(pane, root)), root }),
            DockTarget.SplitBottom => new DockSplit(Orientation.Vertical,   new DockNode[] { root, WrapAsGroup(pane, RoleForSplitWrap(pane, root)) }),
            // Edge targets: same split semantic at the root for now. §2.4
            // follow-up: edge targets become side-pin (LeftSide etc.)
            // entries when the spec's Dock* edge meaning is finalised.
            // Spec 046 §2.3: ToolWindow + Dock* → ToolWindowStrip (the
            // user dragged the tool to an edge, that IS an edge strip).
            DockTarget.DockLeft    => new DockSplit(Orientation.Horizontal, new DockNode[] { WrapAsGroup(pane, RoleForEdgeWrap(pane)), root }),
            DockTarget.DockRight   => new DockSplit(Orientation.Horizontal, new DockNode[] { root, WrapAsGroup(pane, RoleForEdgeWrap(pane)) }),
            DockTarget.DockTop     => new DockSplit(Orientation.Vertical,   new DockNode[] { WrapAsGroup(pane, RoleForEdgeWrap(pane)), root }),
            DockTarget.DockBottom  => new DockSplit(Orientation.Vertical,   new DockNode[] { root, WrapAsGroup(pane, RoleForEdgeWrap(pane)) }),
            _ => root,
        };
    }

    /// <summary>
    /// Spec 046 §6.3 — payload category dispatch. <see cref="Document"/>
    /// subclasses (including the typed <c>Document&lt;TState&gt;</c>) → <see cref="DockContentCategory.Document"/>;
    /// <see cref="ToolWindow"/> → <see cref="DockContentCategory.ToolWindow"/>;
    /// bare <see cref="DockableContent"/> (P1 source-compat shape) → <see cref="DockContentCategory.Untyped"/>.
    /// </summary>
    internal static DockContentCategory CategoryOf(DockableContent pane) => pane switch
    {
        Document => DockContentCategory.Document,
        ToolWindow => DockContentCategory.ToolWindow,
        _ => DockContentCategory.Untyped,
    };

    /// <summary>
    /// Spec 046 §6.3 — acceptance matrix. Untyped panes accept everywhere
    /// (P1 back-compat). Documents reject <see cref="DockGroupRole.ToolWindowStrip"/>;
    /// ToolWindows reject <see cref="DockGroupRole.DocumentArea"/>.
    /// <see cref="DockGroupRole.General"/> accepts every category.
    /// </summary>
    internal static bool AcceptsCategory(DockGroupRole role, DockContentCategory category) => (role, category) switch
    {
        (_, DockContentCategory.Untyped) => true,
        (DockGroupRole.General, _) => true,
        (DockGroupRole.DocumentArea, DockContentCategory.Document) => true,
        (DockGroupRole.DocumentArea, DockContentCategory.ToolWindow) => false,
        (DockGroupRole.ToolWindowStrip, DockContentCategory.ToolWindow) => true,
        (DockGroupRole.ToolWindowStrip, DockContentCategory.Document) => false,
        _ => false,
    };

    /// <summary>
    /// Spec 046 §6.3 — first-pass preference. <see cref="DockGroupRole.DocumentArea"/>
    /// is preferred for <see cref="DockContentCategory.Document"/>;
    /// <see cref="DockGroupRole.ToolWindowStrip"/> is preferred for
    /// <see cref="DockContentCategory.ToolWindow"/>. Untyped panes have
    /// no preference (any accepting group wins on second pass).
    /// </summary>
    internal static bool PreferredFor(DockGroupRole role, DockContentCategory category) => (role, category) switch
    {
        (DockGroupRole.DocumentArea, DockContentCategory.Document) => true,
        (DockGroupRole.ToolWindowStrip, DockContentCategory.ToolWindow) => true,
        _ => false,
    };

    /// <summary>
    /// Spec 046 §2.2 — wrap-rule. When wrapping a single pane into a new
    /// <see cref="DockTabGroup"/> (empty root, or single-leaf root),
    /// infer the group's role from the pane's category: <see cref="Document"/>
    /// → <see cref="DockGroupRole.DocumentArea"/>; everything else →
    /// <see cref="DockGroupRole.General"/>. We deliberately do NOT auto-promote
    /// a lone <see cref="ToolWindow"/> to <see cref="DockGroupRole.ToolWindowStrip"/>
    /// — strips imply edge attachment, which a free-standing wrap doesn't have.
    /// </summary>
    internal static DockGroupRole InferWrapRole(DockableContent pane) =>
        CategoryOf(pane) == DockContentCategory.Document
            ? DockGroupRole.DocumentArea
            : DockGroupRole.General;

    /// <summary>
    /// Spec 046 §2.3 — role for the new sibling group when a Split* target
    /// wraps the pane next to <paramref name="root"/>. If the root is a
    /// single <see cref="DockTabGroup"/> whose role matches the pane's
    /// category preference, propagate that role to the new sibling (the
    /// "splitting a Document inside a DocumentArea must produce another
    /// DocumentArea" rule). Otherwise fall back to <see cref="InferWrapRole"/>.
    /// </summary>
    private static DockGroupRole RoleForSplitWrap(DockableContent pane, DockNode root)
    {
        var cat = CategoryOf(pane);
        if (root is DockTabGroup g && PreferredFor(g.Role, cat))
            return g.Role;
        return InferWrapRole(pane);
    }

    /// <summary>
    /// Spec 046 §2.3 — role for the new sibling group when a Dock* edge
    /// target attaches the pane to the layout's edge. <see cref="ToolWindow"/>
    /// → <see cref="DockGroupRole.ToolWindowStrip"/> (the user is creating
    /// an edge strip); <see cref="Document"/> → <see cref="DockGroupRole.DocumentArea"/>;
    /// untyped → <see cref="DockGroupRole.General"/>.
    /// </summary>
    private static DockGroupRole RoleForEdgeWrap(DockableContent pane) => CategoryOf(pane) switch
    {
        DockContentCategory.Document => DockGroupRole.DocumentArea,
        DockContentCategory.ToolWindow => DockGroupRole.ToolWindowStrip,
        _ => DockGroupRole.General,
    };

    private static DockTabGroup WrapAsGroup(DockableContent pane) =>
        WrapAsGroup(pane, InferWrapRole(pane));

    private static DockTabGroup WrapAsGroup(DockableContent pane, DockGroupRole role) =>
        new(new[] { pane }, SelectedIndex: 0, Role: role);

    private static DockNode AddAsTab(DockNode root, DockableContent pane, out DockRoutingFallback? fallback)
    {
        // Spec 046 §6.3 — role-aware two-pass insert.
        //   Pass 1: prefer a group whose Role matches the pane's category
        //           (Document ↔ DocumentArea, ToolWindow ↔ ToolWindowStrip).
        //   Pass 2: first group anywhere in the subtree whose Role accepts
        //           the pane's category.
        //   Fallback: today's leftmost-descendant behavior, with a
        //             DockRoutingFallback signalled to the caller so a
        //             DockOperationLog diagnostic can be emitted.
        var category = CategoryOf(pane);
        var inserted = InsertInto(root, pane, category, preferOnly: true)
                    ?? InsertInto(root, pane, category, preferOnly: false);
        if (inserted is not null)
        {
            fallback = null;
            return inserted;
        }

        // No group accepted the pane. Degrade to the pre-spec-046
        // "leftmost descendant" behavior and surface a diagnostic.
        fallback = new DockRoutingFallback(
            $"Dock(Center) for category {category} found no accepting group; " +
            "fell back to leftmost-descendant routing. Spec 046 §6.3.");
        return AddAsTabLeftmost(root, pane);
    }

    /// <summary>
    /// Spec 046 §6.3 — recursive insert. When <paramref name="preferOnly"/>
    /// is true, only inserts into a group whose role is the category's
    /// first-pass preference. When false, inserts into the first group
    /// whose role accepts the category. Returns null when no candidate
    /// group exists in the subtree.
    /// </summary>
    private static DockNode? InsertInto(
        DockNode node, DockableContent pane, DockContentCategory category, bool preferOnly)
    {
        switch (node)
        {
            case DockTabGroup g:
            {
                bool match = preferOnly
                    ? PreferredFor(g.Role, category)
                    : AcceptsCategory(g.Role, category);
                return match ? AddToGroup(g, pane) : null;
            }
            case DockSplit s:
            {
                for (int i = 0; i < s.Children.Count; i++)
                {
                    var result = InsertInto(s.Children[i], pane, category, preferOnly);
                    if (result is null) continue;
                    var next = new DockNode[s.Children.Count];
                    for (int j = 0; j < s.Children.Count; j++) next[j] = s.Children[j];
                    next[i] = result;
                    return s with { Children = next };
                }
                return null;
            }
            case DockableContent leaf:
            {
                // Wrapping a bare leaf is only allowed on the second pass;
                // the first pass needs an existing role-matched group, and
                // an unwrapped leaf has no role yet. The wrap rule (§2.2):
                // the new group's role derives from the leaf's category,
                // NOT from the inserted pane's category, since the leaf is
                // the existing context.
                if (preferOnly) return null;
                var role = InferWrapRole(leaf);
                return new DockTabGroup(new[] { leaf, pane }, SelectedIndex: 1, Role: role);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Pre-spec-046 fallback: the original "leftmost descendant" insert.
    /// Kept as a strict last resort when role-aware routing finds no
    /// acceptor. Diagnostic-logged at the caller.
    /// </summary>
    private static DockNode AddAsTabLeftmost(DockNode root, DockableContent pane)
    {
        switch (root)
        {
            case DockTabGroup g:
            {
                var docs = g.Documents;
                var next = new DockableContent[docs.Count + 1];
                for (int i = 0; i < docs.Count; i++) next[i] = docs[i];
                next[docs.Count] = pane;
                return g with { Documents = next, SelectedIndex = docs.Count };
            }
            case DockableContent leaf:
                return new DockTabGroup(new[] { leaf, pane }, SelectedIndex: 1, Role: InferWrapRole(leaf));
            case DockSplit s:
            {
                if (s.Children.Count == 0) return pane;
                var newChildren = new DockNode[s.Children.Count];
                for (int i = 0; i < s.Children.Count; i++) newChildren[i] = s.Children[i];
                newChildren[0] = AddAsTabLeftmost(s.Children[0], pane);
                return s with { Children = newChildren };
            }
            default:
                return root;
        }
    }

    /// <summary>
    /// Convenience: remove the pane from its current location, then place
    /// it at the target. Returns the new root, or the original if removal
    /// didn't find the pane (no-op safety).
    /// </summary>
    public static DockNode? MovePaneToTarget(DockNode? root, DockableContent pane, DockTarget target)
        => MovePaneToTarget(root, pane, target, out _);

    /// <summary>
    /// Spec 046 §6.3 — overload that surfaces any role-aware routing
    /// fallback the insert took. See
    /// <see cref="InsertPaneAtTarget(DockNode?, DockableContent, DockTarget, out DockRoutingFallback?)"/>.
    /// </summary>
    public static DockNode? MovePaneToTarget(
        DockNode? root, DockableContent pane, DockTarget target,
        out DockRoutingFallback? fallback)
    {
        var (afterRemove, found) = RemovePane(root, pane);
        if (!found)
        {
            fallback = null;
            return root;
        }
        return InsertPaneAtTarget(afterRemove, pane, target, out fallback);
    }

    /// <summary>
    /// Spec 045 §2.15. Re-insert <paramref name="pane"/> into the layout
    /// using <see cref="PreviousContainerTracker"/> history when available.
    /// When the remembered container is a <see cref="DockTabGroup"/> still
    /// present in the layout, the pane is folded back as a new tab in that
    /// group (matching VS's "show panel where you left it" behavior). When
    /// no history exists or the previous container has been torn down, the
    /// pane falls back to <paramref name="fallbackTarget"/> at the layout
    /// root via <see cref="InsertPaneAtTarget(DockNode?, DockableContent, DockTarget)"/>.
    /// </summary>
    public static DockNode ShowFromHistory(
        DockNode? root,
        DockableContent pane,
        DockTarget fallbackTarget = DockTarget.Center)
    {
        ArgumentNullException.ThrowIfNull(pane);

        var remembered = PreviousContainerTracker.GetPrevious(pane);
        if (remembered is DockTabGroup rememberedGroup && root is not null)
        {
            // Walk the current tree looking for the SAME instance — record
            // references decay when the layout is rebuilt. If the
            // remembered group still lives in the tree, fold the pane in;
            // otherwise fall back.
            var patched = FoldIntoGroup(root, rememberedGroup, pane);
            if (patched is not null) return patched;
        }
        return InsertPaneAtTarget(root, pane, fallbackTarget);
    }

    /// <summary>
    /// Spec 045 §2.3 / §2.4 — move <paramref name="pane"/> to a target
    /// position relative to a SPECIFIC tab group, not the whole layout.
    /// Drives the per-tab-group drop overlay: the user drags a tab onto
    /// the Output group's drop targets and the pane folds into Output
    /// (Center) or splits the Output group on the chosen side. Identifies
    /// the target group by reference equality — the caller (the renderer)
    /// captures the group during render and passes it through unchanged.
    /// When the pane is in a different branch from the target group,
    /// RemovePane preserves the target group's reference so ReferenceEquals
    /// continues to match. When the pane was in the target group (drop
    /// onto its own group), the post-remove group has a rebuilt
    /// Documents list — we fall back to matching by content-key
    /// intersection.
    /// </summary>
    public static DockNode? MovePaneToGroupTarget(
        DockNode? root, DockableContent pane, DockTabGroup targetGroup, DockTarget target)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(targetGroup);
        if (root is null) return null;

        var (afterRemove, found) = RemovePane(root, pane);
        if (!found) return root;
        if (afterRemove is null) return WrapAsGroup(pane);

        // For Center inside the same group: just add the pane back as a
        // tab. We need to locate the (possibly-rebuilt) target group in
        // afterRemove. ReferenceEquals first (fast path for cross-group
        // drops); then content-key match (covers the same-group case).
        var resolvedTarget = ResolveTargetGroup(afterRemove, targetGroup);
        if (resolvedTarget is null)
        {
            // Target group has vanished (e.g. it was the source group AND
            // the dragged pane was its only document). Fall back to the
            // root-level mutator so the drop still lands somewhere sane.
            return InsertPaneAtTarget(afterRemove, pane, target);
        }

        // Spec 046 §2.3 — when splitting a Document inside a DocumentArea
        // (or a ToolWindow inside a ToolWindowStrip), the new sibling
        // group inherits the target group's role. Other category/role
        // combinations fall back to the pane-derived inference.
        var siblingRole = RoleForSplitWrapAgainstGroup(pane, resolvedTarget);
        DockNode replacement = target switch
        {
            DockTarget.Center => AddToGroup(resolvedTarget, pane),
            DockTarget.SplitLeft   => new DockSplit(Orientation.Horizontal, new DockNode[] { WrapAsGroup(pane, siblingRole), resolvedTarget }),
            DockTarget.SplitRight  => new DockSplit(Orientation.Horizontal, new DockNode[] { resolvedTarget, WrapAsGroup(pane, siblingRole) }),
            DockTarget.SplitTop    => new DockSplit(Orientation.Vertical,   new DockNode[] { WrapAsGroup(pane, siblingRole), resolvedTarget }),
            DockTarget.SplitBottom => new DockSplit(Orientation.Vertical,   new DockNode[] { resolvedTarget, WrapAsGroup(pane, siblingRole) }),
            // Edge targets aren't surfaced in GroupInner mode; fall back
            // to the root-level handling if the caller ever passes them.
            _ => InsertPaneAtTarget(afterRemove, pane, target),
        };

        if (replacement is DockSplit split && target is DockTarget.Center)
            return split; // Shouldn't happen; defensive.

        return ReplaceNode(afterRemove, resolvedTarget, replacement);
    }

    /// <summary>
    /// Spec 046 §2.3 — when a hit-test driven split lands next to a
    /// specific target group, the new sibling's role inherits from the
    /// target group ONLY when that role is the pane category's preferred
    /// landing (Document ↔ DocumentArea; ToolWindow ↔ ToolWindowStrip).
    /// Otherwise we infer from the pane category alone via
    /// <see cref="InferWrapRole"/>.
    /// </summary>
    private static DockGroupRole RoleForSplitWrapAgainstGroup(DockableContent pane, DockTabGroup target)
    {
        var cat = CategoryOf(pane);
        if (PreferredFor(target.Role, cat)) return target.Role;
        return InferWrapRole(pane);
    }

    /// <summary>
    /// Spec 046 §6.4 — strict group-targeted insert with no root-level
    /// fallback. Returns <c>null</c> when <paramref name="targetGroup"/>
    /// can't be resolved against <paramref name="root"/> (neither by
    /// reference nor by content keys), so the caller can surface a
    /// diagnostic instead of silently re-routing.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="InsertPaneIntoGroup"/> / <see cref="InsertPaneRelativeToGroup"/>,
    /// which fall back to a root-level <c>InsertPaneAtTarget</c> when the
    /// target group is missing — appropriate for drag-drop (the user did
    /// SOMETHING, we want to land it SOMEWHERE) but wrong for the
    /// programmatic <c>DockHostModel.Dock(content, group, target)</c>
    /// overload (which should no-op + log so the caller learns their
    /// group reference is stale).
    /// </remarks>
    public static DockNode? TryInsertPaneAtGroupTarget(
        DockNode? root, DockableContent pane, DockTabGroup targetGroup, DockTarget target)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(targetGroup);
        if (root is null) return null;

        var resolved = ResolveTargetGroup(root, targetGroup);
        if (resolved is null) return null;

        // Spec 046 §2.3 — propagate target group's role to new sibling for
        // category-preferred splits.
        var siblingRole = RoleForSplitWrapAgainstGroup(pane, resolved);
        DockNode replacement = target switch
        {
            DockTarget.Center => AddToGroup(resolved, pane),
            DockTarget.SplitLeft   => new DockSplit(Orientation.Horizontal, new DockNode[] { WrapAsGroup(pane, siblingRole), resolved }),
            DockTarget.SplitRight  => new DockSplit(Orientation.Horizontal, new DockNode[] { resolved, WrapAsGroup(pane, siblingRole) }),
            DockTarget.SplitTop    => new DockSplit(Orientation.Vertical,   new DockNode[] { WrapAsGroup(pane, siblingRole), resolved }),
            DockTarget.SplitBottom => new DockSplit(Orientation.Vertical,   new DockNode[] { resolved, WrapAsGroup(pane, siblingRole) }),
            // Dock-edge targets aren't meaningful against a specific group
            // (they target the layout's edge). Treat as Center; the caller
            // can use Dock(content, target) for edge placement.
            _ => AddToGroup(resolved, pane),
        };
        return ReplaceNode(root, resolved, replacement);
    }

    /// <summary>
    /// Spec 045 §2.4 cross-window insert: fold <paramref name="pane"/>
    /// into <paramref name="targetGroup"/> as a new tab without any
    /// remove step. The pane is assumed to live OUTSIDE
    /// <paramref name="root"/> (e.g. it's in a floating window's
    /// TabView) — the caller is responsible for cleaning up the
    /// source. Returns the new root or the original when the target
    /// group can't be resolved (matches MovePaneToGroupTarget's
    /// fallback). When <paramref name="root"/> is null, returns a
    /// fresh single-tab group containing the pane.
    /// </summary>
    public static DockNode? InsertPaneIntoGroup(
        DockNode? root, DockableContent pane, DockTabGroup targetGroup)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(targetGroup);
        if (root is null) return WrapAsGroup(pane);

        var resolved = ResolveTargetGroup(root, targetGroup);
        if (resolved is null) return InsertPaneAtTarget(root, pane, DockTarget.Center);

        var folded = AddToGroup(resolved, pane);
        return ReplaceNode(root, resolved, folded);
    }

    /// <summary>
    /// Spec 045 §2.4 cross-window insert: place <paramref name="pane"/>
    /// adjacent to <paramref name="targetGroup"/> via a new
    /// <see cref="DockSplit"/>. <paramref name="target"/> must be one
    /// of the Split* values (Center has a dedicated entry —
    /// <see cref="InsertPaneIntoGroup"/>). Dock-edge targets fall back
    /// to the root-level placement via <see cref="InsertPaneAtTarget(DockNode?, DockableContent, DockTarget)"/>.
    /// </summary>
    public static DockNode? InsertPaneRelativeToGroup(
        DockNode? root, DockableContent pane, DockTabGroup targetGroup, DockTarget target)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(targetGroup);
        if (root is null) return WrapAsGroup(pane);

        var resolved = ResolveTargetGroup(root, targetGroup);
        if (resolved is null) return InsertPaneAtTarget(root, pane, target);

        // Spec 046 §2.3 — propagate target group's role to the new sibling
        // when category-preferred (Document ↔ DocumentArea, etc.).
        var siblingRole = RoleForSplitWrapAgainstGroup(pane, resolved);
        DockNode replacement = target switch
        {
            DockTarget.Center => AddToGroup(resolved, pane),
            DockTarget.SplitLeft   => new DockSplit(Orientation.Horizontal, new DockNode[] { WrapAsGroup(pane, siblingRole), resolved }),
            DockTarget.SplitRight  => new DockSplit(Orientation.Horizontal, new DockNode[] { resolved, WrapAsGroup(pane, siblingRole) }),
            DockTarget.SplitTop    => new DockSplit(Orientation.Vertical,   new DockNode[] { WrapAsGroup(pane, siblingRole), resolved }),
            DockTarget.SplitBottom => new DockSplit(Orientation.Vertical,   new DockNode[] { resolved, WrapAsGroup(pane, siblingRole) }),
            _ => InsertPaneAtTarget(root, pane, target),
        };
        return ReplaceNode(root, resolved, replacement);
    }

    private static DockTabGroup AddToGroup(DockTabGroup group, DockableContent pane)
    {
        var docs = group.Documents;
        var next = new DockableContent[docs.Count + 1];
        for (int i = 0; i < docs.Count; i++) next[i] = docs[i];
        next[docs.Count] = pane;
        return group with { Documents = next, SelectedIndex = docs.Count };
    }

    private static DockTabGroup? ResolveTargetGroup(DockNode node, DockTabGroup target)
    {
        // Pass 1: reference equality (typical cross-group drop).
        var byRef = FindGroupByRef(node, target);
        if (byRef is not null) return byRef;
        // Pass 2: content-key intersection (same-group drop where the
        // pane that just got removed was inside the target group).
        return FindGroupByContent(node, target);
    }

    private static DockTabGroup? FindGroupByRef(DockNode node, DockTabGroup target)
    {
        switch (node)
        {
            case DockTabGroup g when ReferenceEquals(g, target): return g;
            case DockSplit s:
                foreach (var c in s.Children)
                {
                    var r = FindGroupByRef(c, target);
                    if (r is not null) return r;
                }
                return null;
            default: return null;
        }
    }

    private static DockTabGroup? FindGroupByContent(DockNode node, DockTabGroup target)
    {
        switch (node)
        {
            case DockTabGroup g:
            {
                foreach (var d in g.Documents)
                {
                    foreach (var t in target.Documents)
                    {
                        if (IsSamePane(d, t)) return g;
                    }
                }
                return null;
            }
            case DockSplit s:
                foreach (var c in s.Children)
                {
                    var r = FindGroupByContent(c, target);
                    if (r is not null) return r;
                }
                return null;
            default: return null;
        }
    }

    private static DockNode ReplaceNode(DockNode root, DockNode oldNode, DockNode newNode)
    {
        if (ReferenceEquals(root, oldNode)) return newNode;
        switch (root)
        {
            case DockSplit split:
            {
                var children = split.Children;
                bool replaced = false;
                var next = new DockNode[children.Count];
                for (int i = 0; i < children.Count; i++)
                {
                    if (!replaced && Contains(children[i], oldNode))
                    {
                        next[i] = ReplaceNode(children[i], oldNode, newNode);
                        replaced = true;
                    }
                    else next[i] = children[i];
                }
                return replaced ? split with { Children = next } : root;
            }
            default:
                return root;
        }
    }

    private static bool Contains(DockNode haystack, DockNode needle)
    {
        if (ReferenceEquals(haystack, needle)) return true;
        if (haystack is DockSplit s)
        {
            foreach (var c in s.Children)
                if (Contains(c, needle)) return true;
        }
        return false;
    }

    private static DockNode? FoldIntoGroup(DockNode node, DockTabGroup target, DockableContent pane)
    {
        switch (node)
        {
            case DockableContent:
                return null;
            case DockTabGroup grp when ReferenceEquals(grp, target):
            {
                var docs = grp.Documents;
                var next = new DockableContent[docs.Count + 1];
                for (int i = 0; i < docs.Count; i++) next[i] = docs[i];
                next[docs.Count] = pane;
                return grp with { Documents = next, SelectedIndex = docs.Count };
            }
            case DockTabGroup:
                return null;
            case DockSplit split:
            {
                var children = split.Children;
                for (int i = 0; i < children.Count; i++)
                {
                    var replaced = FoldIntoGroup(children[i], target, pane);
                    if (replaced is null) continue;
                    var next = new DockNode[children.Count];
                    for (int j = 0; j < children.Count; j++) next[j] = children[j];
                    next[i] = replaced;
                    return split with { Children = next };
                }
                return null;
            }
            default:
                return null;
        }
    }
}
