using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 deferred specialized controls — descriptor variant of
/// the hand-coded SwipeControl mount/update arms.
/// </summary>
internal static class SwipeControlDescriptor
{
    private static readonly SingleContent<SwipeControlElement, WinUI.SwipeControl> ChildrenStrategy =
        new SingleContent<SwipeControlElement, WinUI.SwipeControl>(
            GetChild: static e => e.Content,
            SetChild: static (c, ui) => c.Content = ui)
        {
            GetCurrentChild = static c => c.Content as UIElement,
        };

    public static readonly ControlDescriptor<SwipeControlElement, WinUI.SwipeControl> Descriptor =
        new ControlDescriptor<SwipeControlElement, WinUI.SwipeControl>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .Imperative(
            mount: static (c, e) => ApplySwipeItems(c, e, force: true),
            update: static (c, o, n) => ApplySwipeItems(c, n,
                force: !ReferenceEquals(o.LeftItems, n.LeftItems)
                    || !ReferenceEquals(o.RightItems, n.RightItems)
                    || o.LeftItemsMode != n.LeftItemsMode
                    || o.RightItemsMode != n.RightItemsMode));

    private static void ApplySwipeItems(WinUI.SwipeControl control, SwipeControlElement element, bool force)
    {
        if (!force) return;
        control.LeftItems = CreateSwipeItems(element.LeftItems, element.LeftItemsMode);
        control.RightItems = CreateSwipeItems(element.RightItems, element.RightItemsMode);
    }

    private static WinUI.SwipeItems? CreateSwipeItems(SwipeItemData[]? data, WinUI.SwipeMode mode)
    {
        if (data is not { Length: > 0 }) return null;
        var items = new WinUI.SwipeItems { Mode = mode };
        foreach (var entry in data) items.Add(CreateSwipeItem(entry));
        return items;
    }

    private static WinUI.SwipeItem CreateSwipeItem(SwipeItemData data)
    {
        var item = new WinUI.SwipeItem
        {
            Text = data.Text,
            BehaviorOnInvoked = data.BehaviorOnInvoked,
        };
        if (data.IconSource is not null) item.IconSource = data.IconSource;
        if (data.Background is not null) item.Background = data.Background;
        if (data.Foreground is not null) item.Foreground = data.Foreground;
        if (data.OnInvoked is not null) item.Invoked += (_, _) => data.OnInvoked();
        return item;
    }
}
