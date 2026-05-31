using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 completion — descriptor variant of
/// <see cref="ItemContainerElement"/>, the single-child wrapper required by
/// <see cref="ItemsViewElementBase"/> item templates.
/// </summary>
internal static class ItemContainerDescriptor
{
    public static readonly ControlDescriptor<ItemContainerElement, WinUI.ItemContainer> Descriptor =
        new ControlDescriptor<ItemContainerElement, WinUI.ItemContainer>
        {
            Children = new SingleContent<ItemContainerElement, WinUI.ItemContainer>(
                GetChild: static e => e.Child,
                SetChild: static (c, v) => c.Child = v)
            {
                GetCurrentChild = static c => c.Child,
            },
            GetSetters = static e => e.Setters,
        }
        .OneWay(get: static e => e.IsSelected, set: static (c, v) => c.IsSelected = v);
}

/// <summary>
/// Spec 048 §3.3 thin handler — instantiated lazily by
/// <see cref="ControlRegistry"/> when the global path needs the
/// descriptor-backed <see cref="ItemContainerDescriptor"/>.
/// </summary>
internal sealed class ItemContainerDescriptorHandler()
    : DescriptorHandler<ItemContainerElement, WinUI.ItemContainer>(ItemContainerDescriptor.Descriptor);
