using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// Component that renders a virtualized list using WinUI's ItemsRepeater.
/// Adapts the count-based VirtualListElement API to the ItemsRepeater infrastructure,
/// supporting both fixed-height (O(1) offset) and variable-height modes.
/// </summary>
public class VirtualListComponent : Component<VirtualListElement>
{
    public override Element Render()
    {
        var el = Props;

        var indices = UseMemo(() => new VirtualListIndices(el.ItemCount), el.ItemCount);

        // Key selector: use provided GetItemKey or default to index string
        var keySelector = el.GetItemKey ?? VirtualListIndices.DefaultKeySelector;

        // View builder: wrap the RenderItem callback, applying fixed height when set
        var fixedHeight = el.ItemHeight;
        Func<int, int, Element> viewBuilder = (index, _) =>
        {
            if (index < 0 || index >= el.ItemCount)
                return Empty();

            var item = el.RenderItem(index);

            // Fixed-height fast path: force each item to the exact height
            // so ItemsRepeater skips per-item measurement (O(1) offset)
            if (fixedHeight.HasValue)
                item = item.Height(fixedHeight.Value);

            return item;
        };

        // Configure the LazyVStack with appropriate settings
        var estimatedSize = el.ItemHeight ?? el.EstimatedItemHeight;
        var lazyStack = LazyVStack(indices, keySelector, viewBuilder) with
        {
            Spacing = el.Spacing,
            EstimatedItemSize = estimatedSize,
        };

        // Refresh even when both callbacks are absent and no viewer setter runs.
        var rangeRef = UseRef<VisibleRangeSubscription?>(null);
        var range = rangeRef.Current ??= new VisibleRangeSubscription();
        range.Current = (el.OnVisibleRangeChanged, el.ItemHeight, el.EstimatedItemHeight, el.Spacing);
        UseEffect(() => range.Dispose, []);
        var elRef = el.Ref;
        var elOnRange = el.OnVisibleRangeChanged;
        var elHeight = el.ItemHeight;

        if (elRef is not null || elOnRange is not null)
        {
            lazyStack = lazyStack with
            {
                ScrollViewerSetters = [sv =>
                {
                    // Always update the Ref (new VirtualListRef pointing at same controls)
                    if (elRef is not null)
                    {
                        var repeater = sv.Content as ItemsRepeater;
                        elRef(new VirtualListRef(sv, repeater, elHeight));
                    }

                    if (elOnRange is not null)
                        range.Attach(sv);
                }],
            };
        }

        return lazyStack;
    }

    private sealed class VisibleRangeSubscription : IDisposable
    {
        private ScrollViewer? _viewer;
        public (Action<int, int>? Callback, double? Height, double Estimate, double Spacing) Current;

        public void Attach(ScrollViewer viewer)
        {
            if (_viewer is not null) return;
            _viewer = viewer;
            viewer.ViewChanged += OnViewChanged;
        }

        private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
        {
            var current = Current;
            if (_viewer is { } viewer && current.Callback is { } callback)
            {
                var (first, last) = GetVisibleRange(viewer, current.Height, current.Estimate, current.Spacing);
                callback(first, last);
            }
        }

        public void Dispose()
        {
            if (_viewer is { } viewer)
                viewer.ViewChanged -= OnViewChanged;
            _viewer = null;
            Current = default;
        }
    }

    /// <summary>
    /// Calculates the first and last visible item indices from the ScrollViewer viewport.
    /// </summary>
    private static (int First, int Last) GetVisibleRange(
        ScrollViewer sv, double? itemHeight, double estimatedHeight, double spacing)
    {
        var offset = sv.VerticalOffset;
        var viewportHeight = sv.ViewportHeight;
        var totalItemSize = (itemHeight ?? estimatedHeight) + spacing;
        if (totalItemSize <= 0) return (0, 0);

        var first = Math.Max(0, (int)(offset / totalItemSize));
        var last = (int)((offset + viewportHeight) / totalItemSize);
        return (first, last);
    }
}
