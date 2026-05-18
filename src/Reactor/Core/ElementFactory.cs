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

    // Reverse lookup: realized WinUI control → key. Lets RecycleElement drop
    // the matching _mountedElements entry in O(1) when ItemsRepeater hands a
    // container back. Without this, entries accumulate one per unique key as
    // the user scrolls (every realize adds; recycle never removes), and on
    // any subsequent re-render RefreshRealizedItems walks stale entries
    // whose row.Index now points at a different logical row's container —
    // running Reconcile against a mismatched UIElement tree.
    private readonly Dictionary<UIElement, string> _keyByControl = new();

    // Recycle pool for proper WinUI ItemsRepeater integration. The framework
    // keeps every realized UIElement parented to the repeater forever and
    // expects the factory to cycle them — see ViewManager.cpp:865-869 in the
    // microsoft-ui-xaml-lift source: on realize, it skips Append if the
    // returned control is already parented to the repeater. So a recycled
    // container must come back out via GetElement to keep the working set
    // bounded; allocating fresh on every realize creates one orphan in
    // Children per call.
    private readonly Stack<UIElement> _recyclePool = new();

    // Last Element bound to a given realized control. On reuse from the
    // recycle pool, this is the oldElement passed to Reconciler.Reconcile so
    // the existing WinUI tree gets diffed-in-place against the new content
    // rather than thrown away and re-mounted.
    private readonly Dictionary<UIElement, Element> _lastElementByControl = new();

    // Test-only accessors for the regression fixture
    // ElementFactoryRecyclingFixtures.Factory_BookkeepingBoundedAcrossCycles.
    // Confirm that the four bookkeeping structures don't grow with the
    // number of realize/recycle cycles. Gated by InternalsVisibleTo on
    // Reactor.AppTests.Host (see Reactor.csproj).
    internal int DebugRecyclePoolCount => _recyclePool.Count;
    internal int DebugLastElementByControlCount => _lastElementByControl.Count;
    internal int DebugMountedElementsCount => _mountedElements.Count;
    internal int DebugKeyByControlCount => _keyByControl.Count;
    internal bool DebugTryGetLastElementByControl(UIElement control, out Element? element)
        => _lastElementByControl.TryGetValue(control, out element!);

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
            // Keep the per-control "last element" tracking in lockstep with
            // _mountedElements. Without this, a later RecycleElement→GetElement
            // round-trip for the same control would feed the pre-refresh
            // Element to Reconcile as oldElement and diff against a stale
            // tree shape. (PR #324 review)
            _lastElementByControl[child] = newElement;

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

        UIElement? control;
        if (_recyclePool.Count > 0)
        {
            // Reuse a previously-recycled container. The framework still has
            // it parented to the ItemsRepeater, so the ViewManager.cpp:866
            // Append-skip kicks in and the visual tree stays stable.
            var reused = _recyclePool.Pop();
            if (_lastElementByControl.TryGetValue(reused, out var oldElement))
            {
                var replacement = _reconciler.Reconcile(oldElement, element, reused, _requestRerender);
                if (replacement is not null && !ReferenceEquals(replacement, reused))
                {
                    // Heterogeneous-row case: Reconcile decided the root
                    // element type changed and built a fresh control.
                    // `reused` is now unmounted but still parented to the
                    // ItemsRepeater — detach so it doesn't sit there as
                    // an orphan (the original leak shape we're fixing).
                    // (PR #324 review)
                    DetachFromParent(reused);
                    _lastElementByControl.Remove(reused);
                    control = replacement;
                }
                else
                {
                    control = reused;
                }
            }
            else
            {
                // Defensive: pool entry without a tracked oldElement should
                // not happen — fall back to re-mounting on top of it.
                control = _reconciler.Mount(element, _requestRerender);
            }
        }
        else
        {
            control = _reconciler.Mount(element, _requestRerender);
        }

        _mountedElements[key] = element;
        if (control is not null)
        {
            _keyByControl[control] = key;
            _lastElementByControl[control] = element;
        }

        return control ?? new TextBlock { Text = "" };
    }

    // Detach a UIElement from whatever container it's parented to. ItemsRepeater
    // is a Panel subclass so the standard Children.Remove path applies; we also
    // handle Border/ScrollViewer/ContentControl so this is safe to call on
    // arbitrary recycled subtrees.
    private static void DetachFromParent(UIElement control)
    {
        if (control is not FrameworkElement fe) return;
        switch (fe.Parent)
        {
            case Microsoft.UI.Xaml.Controls.Panel panel:
                panel.Children.Remove(fe);
                break;
            case Microsoft.UI.Xaml.Controls.Border border when ReferenceEquals(border.Child, fe):
                border.Child = null;
                break;
            case Microsoft.UI.Xaml.Controls.ContentControl cc when ReferenceEquals(cc.Content, fe):
                cc.Content = null;
                break;
        }
    }

    public void RecycleElement(ElementFactoryRecycleArgs args)
    {
        if (args.Element is null) return;

        // Drop the mounted-element tracking for this container so a later
        // RefreshRealizedItems can't run Reconcile against a stale Element
        // paired with a now-foreign realized child.
        if (_keyByControl.Remove(args.Element, out var stashedKey))
            _mountedElements.Remove(stashedKey);

        // DON'T UnmountChild — the WinUI tree stays alive and is reused on
        // the next GetElement call via Reconciler.Reconcile. ItemsRepeater
        // keeps the element parented either way (see ViewManager.cpp), so
        // tearing down Reactor state here would just be discarded work.
        // The _lastElementByControl entry stays valid for the next realize.
        _recyclePool.Push(args.Element);
    }

}
