using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Core.Internal;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 completion — descriptor variant of
/// <see cref="ItemsViewElementBase"/>. A single base-derived registration
/// catches every closed <see cref="ItemsViewElement{T}"/> variant.
/// </summary>
internal static class ItemsViewDescriptor
{
    private static readonly TypedEventHandler<WinUI.ItemsView, WinUI.ItemsViewItemInvokedEventArgs>
        ItemInvokedTrampoline = (s, args) =>
        {
            var c = (WinUI.ItemsView)s!;
            if (Reconciler.GetElementTag(c) is not ItemsViewElementBase current) return;
            if (args.InvokedItem is ReactorRow row)
                current.InvokeItemInvoked(row.Index);
        };

    private static readonly TypedEventHandler<WinUI.ItemsView, WinUI.ItemsViewSelectionChangedEventArgs>
        SelectionChangedTrampoline = (s, _) =>
        {
            var c = (WinUI.ItemsView)s!;
            if (Reconciler.GetElementTag(c) is not ItemsViewElementBase current) return;
            var selected = c.SelectedItems;
            if (selected is null || selected.Count == 0)
            {
                current.InvokeSelectionChanged(global::System.Array.Empty<int>());
                return;
            }

            var indices = new global::System.Collections.Generic.List<int>(selected.Count);
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] is ReactorRow row) indices.Add(row.Index);
            }
            current.InvokeSelectionChanged(indices);
        };

    public static readonly ControlDescriptor<ItemsViewElementBase, WinUI.ItemsView> Descriptor =
        new ControlDescriptor<ItemsViewElementBase, WinUI.ItemsView>
        {
            Children = new TemplatedItemsErased<ItemsViewElementBase, WinUI.ItemsView>(
                GetSource: static el => (IKeyedItemSource)el),
            GetSetters = static el => el.Setters,
        }
        .OneWay(get: static el => el.LayoutKind, set: static (ctrl, v) => ctrl.Layout = BuildItemsViewLayout(v))
        .OneWay(get: static el => el.SelectionMode, set: static (ctrl, v) => ctrl.SelectionMode = v)
        .OneWay(get: static el => el.IsItemInvokedEnabled, set: static (ctrl, v) => ctrl.IsItemInvokedEnabled = v)
        .HandCodedEvent<ItemsViewEventPayload,
            TypedEventHandler<WinUI.ItemsView, WinUI.ItemsViewItemInvokedEventArgs>>(
            subscribe:        static (c, h) => c.ItemInvoked += h,
            callbackPresent:  static e => e.HasCallbacks ? (global::System.Action<int>)e.InvokeItemInvoked : null,
            trampoline:       ItemInvokedTrampoline,
            slotIsNull:       static p => p.ItemInvokedTrampoline is null,
            setSlot:          static (p, h) => p.ItemInvokedTrampoline = h)
        .HandCodedEvent<ItemsViewEventPayload,
            TypedEventHandler<WinUI.ItemsView, WinUI.ItemsViewSelectionChangedEventArgs>>(
            subscribe:        static (c, h) => c.SelectionChanged += h,
            callbackPresent:  static e => e.HasCallbacks ? (global::System.Action<global::System.Collections.Generic.IReadOnlyList<int>>)e.InvokeSelectionChanged : null,
            trampoline:       SelectionChangedTrampoline,
            slotIsNull:       static p => p.SelectionChangedTrampoline is null,
            setSlot:          static (p, h) => p.SelectionChangedTrampoline = h);

    private static WinUI.Layout BuildItemsViewLayout(ItemsViewLayoutKind kind) => kind switch
    {
        ItemsViewLayoutKind.LinedFlowLayout => new WinUI.LinedFlowLayout
        {
            LineSpacing = 4,
            MinItemSpacing = 4,
        },
        ItemsViewLayoutKind.UniformGridLayout => new WinUI.UniformGridLayout
        {
            MinRowSpacing = 4,
            MinColumnSpacing = 4,
        },
        _ => new WinUI.StackLayout { Spacing = 4 },
    };

    /// <summary>
    /// Spec 048 §3.4 — closed-generic registration latch. Reading
    /// <see cref="Done"/> from inside the <c>ItemsView&lt;T&gt;</c> factory
    /// runs this cctor exactly once per process, which registers the
    /// descriptor against <see cref="ItemsViewElementBase"/> in the
    /// global <see cref="ControlRegistry"/>. Every closed-T variant
    /// dispatches via the base-derived fallback.
    /// </summary>
    internal static class Registration
    {
        // Explicit (empty) static constructor — disables `beforefieldinit`
        // so Init() runs precisely on the first read of Done (the factory
        // touch), not earlier. Matches the Reg<>/RegDecorator<> shape.
        static Registration() { }

        internal static readonly byte Done = Init();

        private static byte Init()
        {
            ControlRegistry.RegisterForDerivedTypes<ItemsViewElementBase, WinUI.ItemsView>(
                static () => new DescriptorHandler<ItemsViewElementBase, WinUI.ItemsView>(Descriptor));
            return 1;
        }
    }
}
