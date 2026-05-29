using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 finish — Port (8). Descriptor variant of the
/// hand-coded <c>MountTreeView</c> / <c>UpdateTreeView</c> arms.
///
/// <para><b>Coverage:</b> <c>Nodes</c> (hierarchical
/// <see cref="TreeViewNodeData"/> tree) via the new
/// <see cref="TreeChildren{TElement,TControl}"/> strategy;
/// <c>SelectionMode</c>, <c>CanDragItems</c>, <c>AllowDrop</c>,
/// <c>CanReorderItems</c> via plain <c>.OneWay</c>;
/// <c>OnItemInvoked</c> / <c>OnExpanding</c> via
/// <c>.HandCodedEvent</c> with typed trampolines.</para>
///
/// <para><b>Behavior parity vs. legacy:</b> positional rebuild on
/// Update — descendant component state inside
/// <c>TreeViewNodeData.ContentElement</c> is lost across renders that
/// touch the tree, matching the legacy <c>UpdateTreeView</c>
/// rebuild path.</para>
/// </summary>
internal static class TreeViewDescriptor
{
    private static readonly TypedEventHandler<WinUI.TreeView, WinUI.TreeViewItemInvokedEventArgs>
        ItemInvokedTrampoline = (s, args) =>
        {
            var t = (WinUI.TreeView)s!;
            if (args.InvokedItem is WinUI.TreeViewNode tvn
                && tvn.Content is TreeViewNodeData nodeData)
                (Reconciler.GetElementTag(t) as TreeViewElement)?.OnItemInvoked?.Invoke(nodeData);
        };

    private static readonly TypedEventHandler<WinUI.TreeView, WinUI.TreeViewExpandingEventArgs>
        ExpandingTrampoline = (s, args) =>
        {
            var t = (WinUI.TreeView)s!;
            if (args.Node.Content is TreeViewNodeData nodeData)
                (Reconciler.GetElementTag(t) as TreeViewElement)?.OnExpanding?.Invoke(nodeData);
        };

    public static readonly ControlDescriptor<TreeViewElement, WinUI.TreeView> Descriptor =
        new ControlDescriptor<TreeViewElement, WinUI.TreeView>
        {
            Children = new TreeChildren<TreeViewElement, WinUI.TreeView>(static e => e.Nodes),
            GetSetters = static e => e.Setters,
        }
        .OneWay(get: static e => e.SelectionMode, set: static (c, v) => c.SelectionMode = v)
        .OneWay(get: static e => e.CanDragItems,  set: static (c, v) => c.CanDragItems = v)
        .OneWay(get: static e => e.AllowDrop,     set: static (c, v) => c.AllowDrop = v)
        .OneWay(get: static e => e.CanReorderItems, set: static (c, v) => c.CanReorderItems = v)
        .HandCodedEvent<TreeViewEventPayload,
            TypedEventHandler<WinUI.TreeView, WinUI.TreeViewItemInvokedEventArgs>>(
            subscribe:        static (c, h) => c.ItemInvoked += h,
            callbackPresent:  static e => e.OnItemInvoked,
            trampoline:       ItemInvokedTrampoline,
            slotIsNull:       static p => p.ItemInvokedTrampoline is null,
            setSlot:          static (p, h) => p.ItemInvokedTrampoline = h)
        .HandCodedEvent<TreeViewEventPayload,
            TypedEventHandler<WinUI.TreeView, WinUI.TreeViewExpandingEventArgs>>(
            subscribe:        static (c, h) => c.Expanding += h,
            callbackPresent:  static e => e.OnExpanding,
            trampoline:       ExpandingTrampoline,
            slotIsNull:       static p => p.ExpandingTrampoline is null,
            setSlot:          static (p, h) => p.ExpandingTrampoline = h);
}
