using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Bridges WinUI's ItemsRepeater/IElementFactory to Reactor's Reconciler.
/// GetElement calls the view builder then mounts; RecycleElement unmounts.
/// </summary>
/// <remarks>
/// Spec 042 Phase 1: <see cref="_mountedElements"/> is keyed by the
/// stable identity string from <see cref="ReactorRow"/>, not by realized
/// index. Insert-at-0 used to shift every entry's effective index by one
/// — that broke <see cref="RefreshRealizedItems"/>'s lookup contract
/// because the dictionary's int keys no longer matched the repeater's
/// new positions. Keying by string makes the mapping reorder-stable.
/// </remarks>
public sealed partial class ElementFactory<T> : IElementFactory
{
    private IReadOnlyList<T> _items;
    private Func<T, int, Element> _viewBuilder;
    private readonly Reconciler _reconciler;
    private readonly Action _requestRerender;
    private readonly ElementPool? _pool;
    // Optional state used when ItemsSource is the OC<ReactorRow> path
    // (spec 042). Lets GetElement translate an ItemsRepeater realized
    // index → stable key for _mountedElements lookup. Null when running
    // against the legacy Enumerable.Range path.
    private ReactorListState? _listState;

    // Reorder-stable element tracker keyed by ReactorRow.Key. See class doc.
    private readonly Dictionary<string, Element> _mountedElements =
        new(global::System.StringComparer.Ordinal);

    public ElementFactory(
        IReadOnlyList<T> items,
        Func<T, int, Element> viewBuilder,
        Reconciler reconciler,
        Action requestRerender,
        ElementPool? pool = null)
    {
        _items = items;
        _viewBuilder = viewBuilder;
        _reconciler = reconciler;
        _requestRerender = requestRerender;
        _pool = pool;
    }

    /// <summary>
    /// Update items and viewBuilder in place without replacing the factory.
    /// This avoids ItemsRepeater re-realizing all items (which causes
    /// "Cannot run layout in the middle of a collection change" crashes).
    /// Existing realized items stay mounted; they'll render new content
    /// on the next GetElement call (scroll or explicit refresh).
    /// </summary>
    internal void UpdateInPlace(IReadOnlyList<T> items, Func<T, int, Element> viewBuilder)
    {
        _items = items;
        _viewBuilder = viewBuilder;
    }

    /// <summary>
    /// Spec 042 Phase 1: bind this factory to the <see cref="ReactorListState"/>
    /// owned by the parent <see cref="ItemsRepeater"/>'s host so
    /// GetElement can resolve a realized index → ReactorRow.Key for the
    /// reorder-stable <see cref="_mountedElements"/> lookup.
    /// </summary>
    internal void AttachListState(ReactorListState listState) => _listState = listState;

    /// <summary>
    /// After updating the factory in place, reconcile all currently realized
    /// items with the new viewBuilder output. This updates existing WinUI
    /// controls via property changes (no add/remove on the ItemsRepeater's
    /// Children collection).
    /// </summary>
    /// <summary>
    /// When set, RefreshRealizedItems is skipped if the predicate returns true.
    /// Used by DataGrid to suppress reconciliation during active scrolling.
    /// </summary>
    internal Func<bool>? ShouldSkipRefresh;

    internal void RefreshRealizedItems(Microsoft.UI.Xaml.Controls.ItemsRepeater repeater)
    {
        // If scrolling restarted after the render was dispatched, skip reconciliation.
        // The next settle timer will pick it up when scrolling truly stops.
        if (ShouldSkipRefresh?.Invoke() == true)
            return;

        // Snapshot the keys we currently believe are realized. The actual
        // realized set may have changed since the last GetElement, but the
        // ItemsRepeater authoritatively tells us per-key via TryGetElement
        // on the row's current index.
        var keys = _mountedElements.Keys.ToArray();
        foreach (var key in keys)
        {
            // Resolve key → current realized index via the host's list state
            // (or, when running on the legacy int path, treat the key as an
            // integer index for backwards compatibility).
            int currentIndex;
            if (_listState is not null)
            {
                if (!_listState.ByKey.TryGetValue(key, out var row))
                {
                    // Row was removed — drop tracking entry.
                    _mountedElements.Remove(key);
                    continue;
                }
                currentIndex = row.Index;
            }
            else
            {
                // Legacy int-key path: parse if possible, otherwise skip.
                if (!int.TryParse(key, out currentIndex))
                {
                    _mountedElements.Remove(key);
                    continue;
                }
            }

            var child = repeater.TryGetElement(currentIndex);
            if (child is null) { _mountedElements.Remove(key); continue; }

            if (!_mountedElements.TryGetValue(key, out var oldElement)) continue;
            if (currentIndex < 0 || currentIndex >= _items.Count) continue;

            var newElement = _viewBuilder(_items[currentIndex], currentIndex);
            _mountedElements[key] = newElement;

            _reconciler.Reconcile(oldElement, newElement, child, _requestRerender);
        }
    }

    public UIElement GetElement(ElementFactoryGetArgs args)
    {
        // Resolve the realized data → (key, dataIndex). Three paths:
        //   1. Spec 042: args.Data is ReactorRow — read both off the row.
        //   2. Legacy: args.Data is int — index directly, synthetic key.
        //   3. Fallback: unknown shape, treat as index 0.
        string key;
        int index;
        switch (args.Data)
        {
            case ReactorRow row:
                key = row.Key;
                index = row.Index;
                break;
            case int i:
                index = i;
                key = i.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
                break;
            default:
                index = 0;
                key = "0";
                break;
        }

        if (index < 0 || index >= _items.Count)
            return new TextBlock { Text = "" };

        var item = _items[index];
        var element = _viewBuilder(item, index);
        _mountedElements[key] = element;
        var control = _reconciler.Mount(element, _requestRerender);
        return control ?? new TextBlock { Text = "" };
    }

    public void RecycleElement(ElementFactoryRecycleArgs args)
    {
        if (args.Element is null) return;

        // Clean up Reactor state (component contexts, effects).
        _reconciler.UnmountChild(args.Element);

        // Pool interactive leaf controls for reuse. Layout containers (Panel, Border)
        // are NOT pooled here because ItemsRepeater may still reference the root element
        // during its layout pass and modifying children causes COMExceptions. Interactive
        // controls are safe to detach and pool because they are leaves with no children.
        if (_pool is not null)
            PoolInteractiveLeaves(args.Element);
    }

    /// <summary>
    /// Walk the recycled subtree and pool interactive leaf controls (Button, TextBox,
    /// ToggleSwitch). These are the most expensive controls to create and benefit most
    /// from pooling. Detaches each from its parent panel before returning to the pool.
    /// </summary>
    private void PoolInteractiveLeaves(UIElement root)
    {
        if (root is Microsoft.UI.Xaml.Controls.Panel panel)
        {
            // Walk children in reverse so removal doesn't shift indices
            for (int i = panel.Children.Count - 1; i >= 0; i--)
                PoolInteractiveLeaves(panel.Children[i]);
        }
        else if (root is Microsoft.UI.Xaml.Controls.Border border && border.Child is not null)
        {
            PoolInteractiveLeaves(border.Child);
        }
        else if (root is FrameworkElement fe && IsPoolableInteractive(fe))
        {
            _pool!.Return(fe);
        }
    }

    private static bool IsPoolableInteractive(FrameworkElement fe) =>
        fe is Microsoft.UI.Xaml.Controls.Button
        or TextBox
        or Microsoft.UI.Xaml.Controls.ToggleSwitch;
}
