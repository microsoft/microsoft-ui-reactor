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

        // Build index list for ItemsRepeater's item source
        var indices = UseMemo(() =>
            Enumerable.Range(0, el.ItemCount).ToList() as IReadOnlyList<int>,
            el.ItemCount);

        // Key selector: use provided GetItemKey or default to index string
        var keySelector = el.GetItemKey ?? (i => i.ToString());

        // View builder: wrap the RenderItem callback, applying fixed height when set
        var fixedHeight = el.ItemHeight;

        // Issue #327 — opt-in cross-recycle row memoization. The cache lives in a
        // UseMemo cell so its lifetime is pinned to (ItemCount, RenderItem identity,
        // CacheRowsBy identity, fixedHeight, capacity): any change there could alter
        // what a row renders to, and a fresh RenderItem closure may close over new
        // state we can't see through, so we conservatively drop the whole cache. The
        // hook is called unconditionally (null cache when not opted in) to keep hook
        // order stable across renders. Scroll-driven realize/recycle does NOT re-run
        // this Render, so the cache persists across every ItemsRepeater container
        // cycle between component renders — exactly the hot path #327 targets.
        var cacheRowsBy = el.CacheRowsBy;
        var rowCache = UseMemo<RowMemoCache?>(
            () => cacheRowsBy is null ? null : new RowMemoCache(el.RowCacheCapacity),
            el.ItemCount, el.RenderItem, (object)cacheRowsBy!, fixedHeight ?? double.NaN, el.RowCacheCapacity);

        Func<int, int, Element> viewBuilder = (index, _) =>
        {
            if (index < 0 || index >= el.ItemCount)
                return Empty();

            // Default path (no opt-in): byte-for-byte the original behavior.
            if (rowCache is null || cacheRowsBy is null)
            {
                var plain = el.RenderItem(index);
                if (fixedHeight.HasValue)
                    plain = plain.Height(fixedHeight.Value);
                return plain;
            }

            var rowKey = cacheRowsBy(index);
            if (rowCache.TryGet(rowKey, out var cached))
                return cached; // hit → same instance → reconciler ReferenceEquals-skips

            var built = el.RenderItem(index);
            // Apply fixedHeight BEFORE caching. `.Height(...)` returns a NEW element
            // (modifiers are immutable), so the post-Height instance is the one the
            // reconciler later compares by reference — caching it (not the pre-Height
            // element) is what keeps cache hits reference-stable.
            if (fixedHeight.HasValue)
                built = built.Height(fixedHeight.Value);
            rowCache.Set(rowKey, built);
            return built;
        };

        // Configure the LazyVStack with appropriate settings
        var estimatedSize = el.ItemHeight ?? el.EstimatedItemHeight;
        var lazyStack = LazyVStack(indices, i => keySelector(i), viewBuilder) with
        {
            Spacing = el.Spacing,
            EstimatedItemSize = estimatedSize,
        };

        // Wire up Ref and OnVisibleRangeChanged via ScrollViewer setters.
        // These setters run at mount AND on every update, so we use a flag ref
        // to ensure event handlers are only attached once.
        var wiredRef = UseRef(false);
        var elRef = el.Ref;
        var elOnRange = el.OnVisibleRangeChanged;
        var elHeight = el.ItemHeight;
        var elEstHeight = el.EstimatedItemHeight;
        var elSpacing = el.Spacing;

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

                    // Only attach event handler once
                    if (!wiredRef.Current && elOnRange is not null)
                    {
                        wiredRef.Current = true;
                        sv.ViewChanged += (_, _) =>
                        {
                            var (first, last) = GetVisibleRange(sv, elHeight, elEstHeight, elSpacing);
                            elOnRange(first, last);
                        };
                    }
                }],
            };
        }

        return lazyStack;
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
