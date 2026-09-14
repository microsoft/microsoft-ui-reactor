using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.Core;

// Workaround for https://github.com/microsoft/microsoft-ui-xaml/issues/11865.
// Visibility-valid parked containers can still be retained as bring targets.
// Reject those offscreen anchors after ItemsView's handler, before ScrollPresenter
// uses their bounds. Otherwise anchoring can repeatedly shift the layout.
internal static class ItemsViewAnchorWorkaround
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
