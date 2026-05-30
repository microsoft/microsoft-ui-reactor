using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

// Spec 047 §14 — typed, data-driven TreeView<T> port (issue #447).
//
// The typed peer of TemplatedListViewElement<T>. Unlike the standard
// descriptor controls, MountTemplatedTreeView/UpdateTemplatedTreeView use a
// hand-coded per-container hosting pipeline: Loaded-time AttachTypedTreeHosting
// with a bounded LayoutUpdated retry, plus ContainerContentChanging on the
// internal TreeViewList, hosting each node's pre-built Element view into its
// realized container's ContentControl.Content. That irregular realization
// pipeline doesn't fit the prop/engine descriptor shapes, so it routes through
// a single Path B decorator on the non-generic base (closed
// TemplatedTreeViewElement<T> resolves to this via the base-derived registry
// walk — same mechanism as TemplatedListHandler / LazyStackHandler).
//
// Mount/Update delegate to the unchanged legacy bodies, so behavior is
// identical to the pre-port switch path. ContinueDefaultTraversal on unmount
// matches the prior unregistered behavior exactly: the hosted node views live
// in ContentControl.Content in the visual tree and are reached by the engine's
// default type-based recursion either way, so their component teardown
// (UseEffect cleanup) still runs. The internal _typedTreeListControls CWT is
// GC-keyed on the TreeView, so no explicit teardown is required.

/// <summary>§14 — typed, data-driven <c>TreeView&lt;T&gt;</c> (issue #447).</summary>
internal sealed class TemplatedTreeViewHandler : IDecoratorElementHandler<TemplatedTreeViewElementBase>
{
    public UIElement Mount(MountContext ctx, TemplatedTreeViewElementBase el)
        => ctx.Reconciler.MountTemplatedTreeView(el, ctx.RequestRerender);

    public UIElement Update(UpdateContext ctx, TemplatedTreeViewElementBase oldEl, TemplatedTreeViewElementBase newEl, UIElement control)
        => ctx.Reconciler.UpdateTemplatedTreeView(oldEl, newEl, (WinUI.TreeView)control, ctx.RequestRerender) ?? control;

    public V1UnmountDisposition Unmount(UnmountContext ctx, TemplatedTreeViewElementBase? element, UIElement control)
        => V1UnmountDisposition.ContinueDefaultTraversal;
}
