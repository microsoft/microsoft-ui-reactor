using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    /// <summary>
    /// Creates a VirtualList element — a count-based virtualized list.
    /// Items are rendered on demand via the renderItem callback.
    /// </summary>
    public static ComponentElement VirtualList(
        int itemCount,
        Func<int, Element> renderItem,
        Func<int, string>? getItemKey = null,
        double? itemHeight = null,
        double estimatedItemHeight = 40,
        double spacing = 0,
        Action<VirtualListRef>? @ref = null,
        Action<int, int>? onVisibleRangeChanged = null)
    {
        var props = new VirtualListElement
        {
            ItemCount = itemCount,
            RenderItem = renderItem,
            GetItemKey = getItemKey,
            ItemHeight = itemHeight,
            EstimatedItemHeight = estimatedItemHeight,
            Spacing = spacing,
            Ref = @ref,
            OnVisibleRangeChanged = onVisibleRangeChanged,
        };

        return Component<VirtualListComponent, VirtualListElement>(props)
            .WithKey($"vl-{itemCount}");
    }
}
