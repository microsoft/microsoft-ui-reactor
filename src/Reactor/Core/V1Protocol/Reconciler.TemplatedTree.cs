using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

// Spec 047 §14 Phase 3 — typed, data-driven TreeView<T> mount/update bodies,
// relocated verbatim out of the shared Reconciler.Mount.cs / Reconciler.Update.cs
// partials to declutter the reconciler core. These remain Reconciler instance
// members (this is a pure same-class move, not a lifecycle extraction) because
// the subsystem's teardown uses core helpers: TemplatedTreeViewHandler clears
// the `_typedTreeListControls` CWT subscription marker, and
// FindTypedTreeListControl/FindDescendantListView live in Reconciler.cs.
// TemplatedTreeViewHandler delegates here via
// ctx.Reconciler.MountTemplatedTreeView / UpdateTemplatedTreeView.
public sealed partial class Reconciler
{
    // ── Typed, data-driven TreeView<T> ───────────────────────────────────

    /// <summary>
    /// Mounts a typed <see cref="TemplatedTreeViewElementBase"/>. Builds a WinUI
    /// node-mode <c>TreeView</c> whose <c>ItemTemplate</c> is an empty
    /// <c>ContentControl</c> shell; each node's view is mounted imperatively
    /// into the realized container on demand (see
    /// <see cref="OnTypedTreeContainerContentChanging"/>) — the same
    /// realize/recycle hosting as the typed <c>ListView</c>, which keeps
    /// expand/collapse robust under container recycling. <c>node.Content</c>
    /// holds the developer's data item.
    /// </summary>
    internal WinUI.TreeView MountTemplatedTreeView(TemplatedTreeViewElementBase el, Action requestRerender)
    {
        var treeView = new WinUI.TreeView
        {
            SelectionMode = el.GetSelectionMode(),
            CanDragItems = el.GetCanDragItems(),
            AllowDrop = el.GetAllowDrop(),
            CanReorderItems = el.GetCanReorderItems(),
            ItemTemplate = SharedContentControlTemplate.Value,
        };

        foreach (var root in el.GetRoots())
            treeView.RootNodes.Add(BuildTemplatedTreeNode(el, root));

        SetElementTag(treeView, el);

        // Trampolines resolve the live element + the data item (node.Content)
        // on dispatch, so they're wired unconditionally and never need
        // re-subscribing on Update (a no-op when the user supplied no callback).
        treeView.ItemInvoked += TemplatedTreeItemInvoked;
        treeView.Expanding += TemplatedTreeExpanding;

        // Hook the internal TreeViewList ("ListControl") ContainerContentChanging
        // so node views are mounted into their realized containers. The
        // ListControl only exists once the template applies (after the control
        // loads in-tree), so we subscribe on Loaded and populate the
        // already-realized initial containers there. Loaded also re-attaches
        // after an Unloaded/Loaded cycle; the attach is idempotent.
        treeView.Loaded += (s, _) => AttachTypedTreeHosting((WinUI.TreeView)s!, requestRerender);

        el.ApplyControlSetters(treeView);
        return treeView;
    }

    /// <summary>
    /// Recursively materializes a <see cref="WinUI.TreeViewNode"/> for
    /// <paramref name="item"/>. <c>node.Content</c> holds the data item; the
    /// view itself is mounted lazily per realized container, so nothing is
    /// mounted here.
    /// </summary>
    private static WinUI.TreeViewNode BuildTemplatedTreeNode(TemplatedTreeViewElementBase el, object item)
    {
        var node = new WinUI.TreeViewNode { Content = item, IsExpanded = el.GetIsExpanded(item) };
        var children = el.GetChildren(item);
        if (children is not null)
            foreach (var child in children)
                node.Children.Add(BuildTemplatedTreeNode(el, child));
        return node;
    }

    /// <summary>
    /// Subscribes the typed TreeView's internal list to ContainerContentChanging
    /// (once) and populates any containers that realized before the subscription.
    /// Idempotent — presence in <see cref="_typedTreeListControls"/> marks
    /// "already subscribed", so it's safe to call on every Loaded.
    /// </summary>
    private void AttachTypedTreeHosting(WinUI.TreeView treeView, Action requestRerender)
    {
        if (_typedTreeListControls.TryGetValue(treeView, out _)) return; // already subscribed

        var list = FindDescendantListView(treeView);
        if (list is null) return; // template not applied yet — a later Loaded will retry

        _typedTreeListControls.Add(treeView, list); // mark subscribed + cache for Update
        list.ContainerContentChanging += (_, args) =>
            OnTypedTreeContainerContentChanging(treeView, args, requestRerender);

        // Host the containers that realized before we subscribed — their
        // ContainerContentChanging already fired and won't fire again. They may
        // not be ready yet (their ContentTemplateRoot is generated a layout pass
        // later — observed under NativeAOT, where the realized container is even
        // still the base ListViewItem at Loaded time), so re-attempt on
        // LayoutUpdated until every realized container is hosted, then detach.
        // Everything realized AFTER this point flows through CCC. The pass count
        // is bounded so the handler always detaches (no dangling subscription),
        // and CCC still covers anything not hosted by then.
        if (!PopulateRealizedTreeContainers(treeView, list, requestRerender))
        {
            int passesLeft = 8;
            EventHandler<object>? onLayout = null;
            onLayout = (_, _) =>
            {
                if (PopulateRealizedTreeContainers(treeView, list, requestRerender) || --passesLeft <= 0)
                    list.LayoutUpdated -= onLayout;
            };
            list.LayoutUpdated += onLayout;
        }
    }

    /// <summary>
    /// Hosts every currently-realized container that's ready and not yet hosted.
    /// Returns true when no realized container remains unhosted (so the caller
    /// can stop re-attempting). Virtualized-out indices (null container) are not
    /// counted — ContainerContentChanging hosts them when they realize.
    /// </summary>
    private bool PopulateRealizedTreeContainers(WinUI.TreeView treeView, WinUI.ListView list, Action requestRerender)
    {
        bool complete = true;
        for (int i = 0; i < list.Items.Count; i++)
        {
            // The container is a ListViewItem/TreeViewItem (both ContentControl).
            // Don't filter on TreeViewItem — under AOT it can still be the base
            // ListViewItem when first realized.
            if (list.ContainerFromIndex(i) is not ContentControl container) continue;
            if (container.ContentTemplateRoot is null) { complete = false; continue; } // not ready yet
            PopulateTypedTreeContainer(treeView, container, list.Items[i], requestRerender);
        }
        return complete;
    }

    /// <summary>
    /// Realize/recycle handler for the typed TreeView's containers. On realize,
    /// mounts the node's view into the container's ContentControl; on recycle,
    /// unmounts it. Does <b>not</b> set <c>args.Handled</c> — the internal
    /// TreeViewList runs its own ContainerContentChanging handler (indentation /
    /// selection visuals) and must keep doing so.
    /// </summary>
    private void OnTypedTreeContainerContentChanging(
        WinUI.TreeView treeView, ContainerContentChangingEventArgs args, Action requestRerender)
    {
        if (args.ItemContainer?.ContentTemplateRoot is not ContentControl cc) return;

        if (args.InRecycleQueue)
        {
            if (cc.Content is UIElement old) UnmountChild(old);
            cc.Content = null;
            ClearElementTag(cc);
            return;
        }

        PopulateTypedTreeContainer(treeView, args.ItemContainer, args.Item, requestRerender);
    }

    /// <summary>
    /// Mounts the node's view into a realized container's ContentControl, unless
    /// it's already populated. The developer's data item is <c>node.Content</c>.
    /// </summary>
    private void PopulateTypedTreeContainer(
        WinUI.TreeView treeView, WinUI.Control container, object? item, Action requestRerender)
    {
        if (GetElementTag(treeView) is not TemplatedTreeViewElementBase el) return;
        if ((container as ContentControl)?.ContentTemplateRoot is not ContentControl cc) return;
        if (cc.Content is not null) return; // already hosted for this realization
        if (item is not WinUI.TreeViewNode node || node.Content is not { } data) return;

        var view = el.BuildView(data);
        cc.Content = Mount(view, requestRerender);
        SetElementTag(cc, view);
    }

    private static void TemplatedTreeItemInvoked(WinUI.TreeView sender, WinUI.TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is WinUI.TreeViewNode node
            && node.Content is { } item
            && GetElementTag(sender) is TemplatedTreeViewElementBase el)
            el.InvokeItemInvoked(item);
    }

    private static void TemplatedTreeExpanding(WinUI.TreeView sender, WinUI.TreeViewExpandingEventArgs args)
    {
        if (args.Node.Content is { } item
            && GetElementTag(sender) is TemplatedTreeViewElementBase el)
            el.InvokeExpanding(item);
    }

    /// <summary>
    /// Updates a typed <see cref="TemplatedTreeViewElementBase"/> in place:
    /// keyed-diffs the node hierarchy and writes back the parent-control props.
    /// The ItemInvoked / Expanding trampolines resolve the live element via
    /// <c>GetElementTag</c>, so refreshing the element tag is all that's needed
    /// to pick up new callbacks — no re-subscription.
    /// </summary>
    internal UIElement? UpdateTemplatedTreeView(TemplatedTreeViewElementBase o, TemplatedTreeViewElementBase n, WinUI.TreeView tv, Action requestRerender)
    {
        // Diff the node hierarchy (structure + each node's data item). The view
        // reconcile is a separate flat pass over the realized containers below —
        // keeping the two concerns decoupled, and using the index-based container
        // lookup that works under NativeAOT (ContainerFromItem does not resolve
        // there, and a freshly-realized container can still be the base
        // ListViewItem rather than TreeViewItem).
        DiffTemplatedTreeNodes(tv.RootNodes, o, o.GetRoots(), n, n.GetRoots());

        tv.SelectionMode = n.GetSelectionMode();
        tv.CanDragItems = n.GetCanDragItems();
        tv.AllowDrop = n.GetAllowDrop();
        tv.CanReorderItems = n.GetCanReorderItems();

        SetElementTag(tv, n);
        n.ApplyControlSetters(tv);

        // Reconcile the view of every currently-realized container against its
        // node's (now-updated) data. Unrealized nodes need no work — their view
        // is (re)built fresh from node.Content when they next realize via CCC.
        RefreshRealizedTreeContainers(tv, FindTypedTreeListControl(tv), n, requestRerender);
        return null;
    }

    /// <summary>
    /// Reconciles the hosted view of every realized container against its node's
    /// current <c>node.Content</c> data. Iterates the flattened
    /// <c>ListView.Items</c> via index (the AOT-robust lookup), so
    /// it covers visible nodes at every depth in one pass.
    /// </summary>
    private void RefreshRealizedTreeContainers(WinUI.TreeView tv, WinUI.ListView? list, TemplatedTreeViewElementBase n, Action requestRerender)
    {
        if (list is null) return;
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.ContainerFromIndex(i) is not ContentControl container) continue;
            if (container.ContentTemplateRoot is not ContentControl cc) continue;
            if (list.Items[i] is not WinUI.TreeViewNode node || node.Content is not { } data) continue;

            var newView = n.BuildView(data);
            if (cc.Content is UIElement existing && GetElementTag(cc) is Element oldView && CanUpdate(oldView, newView))
            {
                var replacement = Update(oldView, newView, existing, requestRerender);
                if (replacement is not null && !ReferenceEquals(cc.Content, replacement))
                    cc.Content = replacement;
            }
            else
            {
                if (cc.Content is UIElement old) UnmountChild(old);
                cc.Content = Mount(newView, requestRerender);
            }
            SetElementTag(cc, newView);
        }
    }

    private static readonly object[] s_emptyTreeItems = [];

    /// <summary>
    /// Keyed, in-place hierarchical reconcile of a typed TreeView node list.
    /// Keys on the element's <c>KeySelector</c>. Matched nodes are reused (their
    /// data item and any realized container view reconciled in place); the live
    /// <see cref="WinUI.TreeViewNode"/> collection is then reordered with minimal
    /// insert/move/remove ops so that <b>unchanged-order updates touch the
    /// collection not at all</b> — avoiding the container churn that a
    /// clear-and-rebuild would force.
    /// </summary>
    private void DiffTemplatedTreeNodes(
        IList<WinUI.TreeViewNode> liveNodes,
        TemplatedTreeViewElementBase oldEl, IReadOnlyList<object> oldItems,
        TemplatedTreeViewElementBase newEl, IReadOnlyList<object> newItems)
    {
        // Snapshot: map old key → (live node, old item). Live nodes correspond
        // 1:1 to oldItems in order.
        var oldByKey = new Dictionary<string, (WinUI.TreeViewNode Node, object OldItem)>(oldItems.Count);
        for (int i = 0; i < oldItems.Count && i < liveNodes.Count; i++)
            oldByKey.TryAdd(oldEl.GetKey(oldItems[i]), (liveNodes[i], oldItems[i]));

        // Resolve the target node sequence: reuse-and-reconcile matched nodes,
        // build fresh ones for new keys.
        var target = new List<WinUI.TreeViewNode>(newItems.Count);
        for (int i = 0; i < newItems.Count; i++)
        {
            var newItem = newItems[i];
            if (oldByKey.Remove(newEl.GetKey(newItem), out var match))
            {
                var node = match.Node;
                // node.Content is the data item; refresh it so the trampolines
                // hand back the current T (value-type T is boxed fresh).
                node.Content = newItem;

                bool expanded = newEl.GetIsExpanded(newItem);
                if (node.IsExpanded != expanded) node.IsExpanded = expanded;

                DiffTemplatedTreeNodes(
                    node.Children,
                    oldEl, oldEl.GetChildren(match.OldItem) ?? s_emptyTreeItems,
                    newEl, newEl.GetChildren(newItem) ?? s_emptyTreeItems);

                target.Add(node);
            }
            else
            {
                target.Add(BuildTemplatedTreeNode(newEl, newItem));
            }
        }

        // Removed nodes (unmatched old keys): drop them. Their realized
        // containers recycle → the ContainerContentChanging recycle path tears
        // their views down.
        foreach (var leftover in oldByKey.Values)
            liveNodes.Remove(leftover.Node);

        // Reorder/insert so liveNodes matches `target`. Reused nodes already in
        // the right slot mean zero collection mutations (the common case).
        for (int j = 0; j < target.Count; j++)
        {
            if (j < liveNodes.Count && ReferenceEquals(liveNodes[j], target[j])) continue;

            int current = IndexOfNode(liveNodes, target[j], j);
            if (current >= 0) liveNodes.RemoveAt(current);
            liveNodes.Insert(j, target[j]);
        }
        // Trim any stragglers beyond the target length (defensive — removals
        // above should already have handled these).
        while (liveNodes.Count > target.Count)
            liveNodes.RemoveAt(liveNodes.Count - 1);
    }

    private static int IndexOfNode(IList<WinUI.TreeViewNode> nodes, WinUI.TreeViewNode target, int startAt)
    {
        for (int i = startAt; i < nodes.Count; i++)
            if (ReferenceEquals(nodes[i], target)) return i;
        return -1;
    }
}
