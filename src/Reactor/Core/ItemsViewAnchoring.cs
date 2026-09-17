using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Core;

// ItemsView can nominate a recycled bring target even with its content collapsed.
// Reject it after ItemsView's handler, before ScrollPresenter uses the offscreen
// bounds and feeds them back into layout. Only realized rows are eligible.
internal static class ItemsViewAnchoring
{
    private static readonly ConditionalWeakTable<ScrollView, object> RegisteredScrollViews = new();

    internal static void Ensure(ItemContainer container)
    {
        ScrollView? scrollView = null;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(container);
            parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollView scroll)
                scrollView = scroll;
            if (parent is not ItemsView)
                continue;
            if (scrollView is not null && !RegisteredScrollViews.TryGetValue(scrollView, out _))
            {
                scrollView.AnchorRequested += OnAnchorRequested;
                RegisteredScrollViews.Add(scrollView, new object());
            }
            return;
        }
    }

    private static void OnAnchorRequested(ScrollView sender, ScrollingAnchorRequestedEventArgs args)
    {
        if (args.AnchorElement is ItemContainer anchor
            && VisualTreeHelper.GetParent(anchor) is ItemsRepeater repeater
            && repeater.GetElementIndex(anchor) < 0)
            args.AnchorElement = null;
    }
}
