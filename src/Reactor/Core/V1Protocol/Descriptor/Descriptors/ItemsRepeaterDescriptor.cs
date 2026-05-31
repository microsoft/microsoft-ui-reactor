using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 finish — Port (7). Descriptor variant of the
/// hand-coded <c>MountItemsRepeater</c> / <c>UpdateItemsRepeater</c>
/// arms (themselves added in this branch — there was no legacy arm
/// before this port).
///
/// <para><b>Coverage:</b> the entire control surface flows through
/// <see cref="TemplatedItemsErased{TElement,TControl}"/> targeting
/// <see cref="WinUI.ItemsRepeater"/> — the source object is the element
/// itself (it implements both <see cref="IKeyedItemSource"/> and the
/// internal <see cref="IItemsRepeaterFactorySource"/>). Engine (1)'s
/// ItemsRepeater arm in <see cref="Reconciler.BindErasedKeyedItemsSource"/>
/// reuses the same <c>ReactorListState</c> + <c>KeyedListDiff</c> +
/// <c>IElementFactory</c> realization pipeline the legacy arm uses.
/// Single descriptor registration on the non-generic
/// <see cref="ItemsRepeaterElementBase"/> base catches every closed-T
/// <see cref="ItemsRepeaterElement{T}"/> variant.</para>
///
/// <para><b>Behavior parity vs. legacy:</b> identical — the descriptor
/// drives the same Mount/Update sequence (<c>ConfigureLayout</c> →
/// list state build → factory create/update → diff). No event surface
/// (ItemsRepeater itself doesn't raise selection or item-click events;
/// those are the host control's responsibility), so no
/// <c>.HandCodedControlled</c> / <c>.HandCodedEvent</c> entries.</para>
/// </summary>
internal static class ItemsRepeaterDescriptor
{
    public static readonly ControlDescriptor<ItemsRepeaterElementBase, WinUI.ItemsRepeater> Descriptor =
        new ControlDescriptor<ItemsRepeaterElementBase, WinUI.ItemsRepeater>
        {
            Children = new TemplatedItemsErased<ItemsRepeaterElementBase, WinUI.ItemsRepeater>(
                static el => (IKeyedItemSource)el),
            GetSetters = static e => e.RepeaterSetters,
        };

    /// <summary>
    /// Spec 048 §3.4 — closed-generic registration latch. Reading
    /// <see cref="Done"/> from inside the <c>ItemsRepeater&lt;T&gt;</c> factory
    /// runs this cctor exactly once per process, which registers the
    /// descriptor against <see cref="ItemsRepeaterElementBase"/> in the
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
            ControlRegistry.RegisterForDerivedTypes<ItemsRepeaterElementBase, WinUI.ItemsRepeater>(
                static () => new DescriptorHandler<ItemsRepeaterElementBase, WinUI.ItemsRepeater>(Descriptor));
            return 1;
        }
    }
}
