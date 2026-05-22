using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Docking.Native;

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
        return RemoveInner(root, pane);
    }

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
                    if (docs.Count == 1) return ((DockNode?)null, true);
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
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (root is null) return WrapAsGroup(pane);
        // For split / edge targets, the inserted pane needs its own tab
        // strip so the user can drag / close / identify it. Wrapping in
        // a single-document DockTabGroup matches upstream WinUI.Dock
        // behavior: every pane lives inside a tab group, even when it's
        // the only document in that group.
        return target switch
        {
            DockTarget.Center => AddAsTab(root, pane),
            DockTarget.SplitLeft   => new DockSplit(Orientation.Horizontal, new DockNode[] { WrapAsGroup(pane), root }),
            DockTarget.SplitRight  => new DockSplit(Orientation.Horizontal, new DockNode[] { root, WrapAsGroup(pane) }),
            DockTarget.SplitTop    => new DockSplit(Orientation.Vertical,   new DockNode[] { WrapAsGroup(pane), root }),
            DockTarget.SplitBottom => new DockSplit(Orientation.Vertical,   new DockNode[] { root, WrapAsGroup(pane) }),
            // Edge targets: same split semantic at the root for now. §2.4
            // follow-up: edge targets become side-pin (LeftSide etc.)
            // entries when the spec's Dock* edge meaning is finalised.
            DockTarget.DockLeft    => new DockSplit(Orientation.Horizontal, new DockNode[] { WrapAsGroup(pane), root }),
            DockTarget.DockRight   => new DockSplit(Orientation.Horizontal, new DockNode[] { root, WrapAsGroup(pane) }),
            DockTarget.DockTop     => new DockSplit(Orientation.Vertical,   new DockNode[] { WrapAsGroup(pane), root }),
            DockTarget.DockBottom  => new DockSplit(Orientation.Vertical,   new DockNode[] { root, WrapAsGroup(pane) }),
            _ => root,
        };
    }

    private static DockTabGroup WrapAsGroup(DockableContent pane) =>
        new(new[] { pane }, SelectedIndex: 0);

    private static DockNode AddAsTab(DockNode root, DockableContent pane)
    {
        // Folding into the *first* tab group under the root keeps the
        // single-group case (Layout = DockTabGroup) clean. When the root
        // is a split, we collapse the leftmost leaf into a new tab group
        // with the dragged pane. Richer hover-target group resolution
        // arrives once the §2.4 hit-test localizes the target group.
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
                return new DockTabGroup(new[] { leaf, pane }, SelectedIndex: 1);
            case DockSplit s:
            {
                if (s.Children.Count == 0) return pane;
                var newChildren = new DockNode[s.Children.Count];
                for (int i = 0; i < s.Children.Count; i++) newChildren[i] = s.Children[i];
                newChildren[0] = AddAsTab(s.Children[0], pane);
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
    {
        var (afterRemove, found) = RemovePane(root, pane);
        if (!found) return root;
        return InsertPaneAtTarget(afterRemove, pane, target);
    }

    /// <summary>
    /// Spec 045 §2.15. Re-insert <paramref name="pane"/> into the layout
    /// using <see cref="PreviousContainerTracker"/> history when available.
    /// When the remembered container is a <see cref="DockTabGroup"/> still
    /// present in the layout, the pane is folded back as a new tab in that
    /// group (matching VS's "show panel where you left it" behavior). When
    /// no history exists or the previous container has been torn down, the
    /// pane falls back to <paramref name="fallbackTarget"/> at the layout
    /// root via <see cref="InsertPaneAtTarget"/>.
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

        DockNode replacement = target switch
        {
            DockTarget.Center => AddToGroup(resolvedTarget, pane),
            DockTarget.SplitLeft   => new DockSplit(Orientation.Horizontal, new DockNode[] { WrapAsGroup(pane), resolvedTarget }),
            DockTarget.SplitRight  => new DockSplit(Orientation.Horizontal, new DockNode[] { resolvedTarget, WrapAsGroup(pane) }),
            DockTarget.SplitTop    => new DockSplit(Orientation.Vertical,   new DockNode[] { WrapAsGroup(pane), resolvedTarget }),
            DockTarget.SplitBottom => new DockSplit(Orientation.Vertical,   new DockNode[] { resolvedTarget, WrapAsGroup(pane) }),
            // Edge targets aren't surfaced in GroupInner mode; fall back
            // to the root-level handling if the caller ever passes them.
            _ => InsertPaneAtTarget(afterRemove, pane, target),
        };

        if (replacement is DockSplit split && target is DockTarget.Center)
            return split; // Shouldn't happen; defensive.

        return ReplaceNode(afterRemove, resolvedTarget, replacement);
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
    /// to the root-level placement via <see cref="InsertPaneAtTarget"/>.
    /// </summary>
    public static DockNode? InsertPaneRelativeToGroup(
        DockNode? root, DockableContent pane, DockTabGroup targetGroup, DockTarget target)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(targetGroup);
        if (root is null) return WrapAsGroup(pane);

        var resolved = ResolveTargetGroup(root, targetGroup);
        if (resolved is null) return InsertPaneAtTarget(root, pane, target);

        DockNode replacement = target switch
        {
            DockTarget.Center => AddToGroup(resolved, pane),
            DockTarget.SplitLeft   => new DockSplit(Orientation.Horizontal, new DockNode[] { WrapAsGroup(pane), resolved }),
            DockTarget.SplitRight  => new DockSplit(Orientation.Horizontal, new DockNode[] { resolved, WrapAsGroup(pane) }),
            DockTarget.SplitTop    => new DockSplit(Orientation.Vertical,   new DockNode[] { WrapAsGroup(pane), resolved }),
            DockTarget.SplitBottom => new DockSplit(Orientation.Vertical,   new DockNode[] { resolved, WrapAsGroup(pane) }),
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
