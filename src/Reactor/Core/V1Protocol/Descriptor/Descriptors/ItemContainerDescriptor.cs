using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 completion — descriptor variant of
/// <see cref="ItemContainerElement"/>, the single-child wrapper required by
/// <see cref="ItemsViewElementBase"/> item templates.
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
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
