using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Controls;

/// <summary>
/// Element record for the VirtualList component — a count-based virtualized list
/// backed by WinUI's ItemsRepeater. Unlike LazyVStack which takes a concrete
/// IReadOnlyList&lt;T&gt;, VirtualList works with an item count and a render callback,
/// making it suitable for data-source-driven scenarios where items are fetched on demand.
/// </summary>
public record VirtualListElement : Element
{
    /// <summary>
    /// Default upper bound for <see cref="CacheRowsBy"/>'s LRU cache (issue #327).
    /// 128 rows ≈ a few× a typical realized window (a 500px viewport over ~40px rows
    /// realizes on the order of a couple dozen containers), enough to absorb
    /// scroll-back and brief over-scroll without the cache ever growing unboundedly.
    /// Override per-list via <see cref="RowCacheCapacity"/>.
    /// </summary>
    public const int DefaultRowCacheCapacity = 128;

    /// <summary>Total number of items (drives the virtualizer's extent).</summary>
    public required int ItemCount { get; init; }

    /// <summary>
    /// Render callback: given an index, return the Element for that row.
    /// Called only for visible items (virtualized).
    /// </summary>
    public required Func<int, Element> RenderItem { get; init; }

    /// <summary>
    /// Optional key function for stable identity across re-renders.
    /// When provided, the reconciler uses this to match items across reorderings.
    /// </summary>
    public Func<int, string>? GetItemKey { get; init; }

    /// <summary>
    /// Fixed item height in pixels. When set, enables the fixed-height fast path
    /// with O(1) offset calculation — no per-item measurement needed.
    /// When null, variable-height mode is used with EstimatedItemHeight.
    /// </summary>
    public double? ItemHeight { get; init; }

    /// <summary>
    /// Estimated item height for variable-height mode (default 40px).
    /// Used for initial scroll extent calculation before items are measured.
    /// Ignored when ItemHeight is set.
    /// </summary>
    public double EstimatedItemHeight { get; init; } = 40;

    /// <summary>Spacing between items in pixels (default 0).</summary>
    public double Spacing { get; init; }

    /// <summary>
    /// Opt-in cross-recycle row memoization (issue #327). When set, the list keeps a
    /// bounded LRU cache (see <see cref="RowCacheCapacity"/>) of already-built row
    /// Elements keyed by <c>CacheRowsBy(index)</c>. On an ItemsRepeater container
    /// recycle/realize for a key still in the cache, the list returns the <em>same</em>
    /// Element instance it built before instead of invoking <see cref="RenderItem"/>
    /// again; the reconciler then short-circuits on <c>ReferenceEquals</c>
    /// (<see cref="Core.Element.CanSkipUpdate"/>) and skips the entire per-row diff.
    /// During fast scroll over variable-height rows this is where ~a third of the
    /// scroll wall-clock goes, so collapsing it to ~0 is the win. Leaving this
    /// <see langword="null"/> (the default) preserves today's behavior byte-for-byte.
    ///
    /// <para><b>Purity contract — you are asserting this.</b> By opting in you promise
    /// that <see cref="RenderItem"/> is a pure function of the inputs folded into the
    /// key: for a given key the Element it produces must not depend on anything the key
    /// does not capture. If <see cref="RenderItem"/> closes over mutable state that the
    /// key omits — the current selection, a row-revision counter, an ambient theme it
    /// reads ad-hoc — cached rows can serve <em>stale</em> content, and that is the
    /// author's bug, not the framework's. Fold every such input into the key, e.g.
    /// <c>cacheRowsBy: i =&gt; $"{items[i].Id}:{items[i].Rev}:{(i == selected ? 1 : 0)}"</c>.
    /// The framework still resets the cache automatically whenever <see cref="ItemCount"/>
    /// or the <see cref="RenderItem"/> delegate identity changes (a new closure may
    /// capture new state we cannot see through), so a re-render with fresh data cannot
    /// be served from a stale instance — but <em>within</em> a stable render the key is
    /// the only staleness barrier.</para>
    ///
    /// <para>Keep this a <em>separate, explicit</em> opt-in from <see cref="GetItemKey"/>:
    /// supplying <see cref="GetItemKey"/> for reconciliation identity does not enable
    /// caching, because identity and purity are different promises. It is fine to reuse
    /// the same projection for both when your rows are genuinely pure.</para>
    /// </summary>
    public Func<int, string>? CacheRowsBy { get; init; }

    /// <summary>
    /// Upper bound on the number of row Elements retained by the
    /// <see cref="CacheRowsBy"/> LRU cache. Defaults to
    /// <see cref="DefaultRowCacheCapacity"/> (128). Values below 1 are clamped to 1.
    /// Ignored when <see cref="CacheRowsBy"/> is <see langword="null"/>.
    /// </summary>
    public int RowCacheCapacity { get; init; } = DefaultRowCacheCapacity;

    /// <summary>
    /// Callback ref for scroll operations. When set, receives a VirtualListRef
    /// that exposes ScrollToIndex and scroll position save/restore.
    /// </summary>
    public Action<VirtualListRef>? Ref { get; init; }

    /// <summary>
    /// Callback fired when the visible range changes (viewport tracking).
    /// Receives the first and last visible item indices.
    /// </summary>
    public Action<int, int>? OnVisibleRangeChanged { get; init; }
}

/// <summary>
/// Imperative handle for VirtualList scroll operations.
/// Obtained via the VirtualListElement.Ref callback.
/// </summary>
public sealed class VirtualListRef
{
    private readonly ScrollViewer? _scrollViewer;
    private readonly ItemsRepeater? _repeater;
    private readonly double? _itemHeight;

    internal VirtualListRef(ScrollViewer? scrollViewer, ItemsRepeater? repeater, double? itemHeight)
    {
        _scrollViewer = scrollViewer;
        _repeater = repeater;
        _itemHeight = itemHeight;
    }

    /// <summary>The underlying ItemsRepeater, for advanced scenarios.</summary>
    public ItemsRepeater? Repeater => _repeater;

    /// <summary>
    /// Programmatically scroll to bring the item at the given index into view.
    /// </summary>
    public void ScrollToIndex(int index)
    {
        if (_scrollViewer is null || _repeater is null) return;

        if (_itemHeight.HasValue)
        {
            // Fixed-height fast path: O(1) offset calculation
            var offset = index * (_itemHeight.Value + GetSpacing());
            _scrollViewer.ChangeView(null, offset, null, disableAnimation: false);
        }
        else
        {
            // Variable-height: use ItemsRepeater to get element and bring into view
            var element = _repeater.TryGetElement(index);
            if (element is not null)
            {
                element.StartBringIntoView();
            }
            else
            {
                // Element not realized yet — estimate and scroll, then it'll realize
                var estimated = index * 40; // EstimatedItemHeight default
                _scrollViewer.ChangeView(null, estimated, null, disableAnimation: false);
            }
        }
    }

    /// <summary>Gets the current vertical scroll offset.</summary>
    public double ScrollOffset => _scrollViewer?.VerticalOffset ?? 0;

    /// <summary>Restores a previously saved scroll offset.</summary>
    public void RestoreScrollOffset(double offset)
    {
        _scrollViewer?.ChangeView(null, offset, null, disableAnimation: true);
    }

    private double GetSpacing()
    {
        if (_repeater?.Layout is StackLayout stack) return stack.Spacing;
        return 0;
    }
}
