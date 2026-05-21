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
                    return ReferenceEquals(leaf, target) ? leaf : null;
                case DockTabGroup grp:
                    foreach (var d in grp.Documents)
                        if (ReferenceEquals(d, target)) return grp;
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

    private static (DockNode? Node, bool Found) RemoveInner(DockNode node, DockableContent pane)
    {
        switch (node)
        {
            case DockableContent leaf:
                return ReferenceEquals(leaf, pane)
                    ? ((DockNode?)null, true)
                    : (node, false);

            case DockTabGroup group:
            {
                var docs = group.Documents;
                for (int i = 0; i < docs.Count; i++)
                {
                    if (!ReferenceEquals(docs[i], pane)) continue;
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
