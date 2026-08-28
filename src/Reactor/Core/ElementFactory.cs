using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics.CodeAnalysis;

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
    //
    // Used as a stack (append/remove at the end) but stored as a List so
    // GetElement can SCAN it for a container whose last Element is reusable for
    // the row being realized. Blindly popping the newest entry orphans it
    // whenever the root element type flipped (issue #919): Reconcile mints a
    // different control, and the popped one can never be un-parented from the
    // repeater. Bounded by <see cref="TrimPool"/>, so the scan is short.
    private readonly List<PoolEntry> _recyclePool = new();

    // A parked container plus the value its Visibility DP carried before
    // RecycleElement collapsed it. We store the *value source* — whatever
    // ReadLocalValue returned — rather than the evaluated enum. A row whose
    // Visibility comes from a Style (or is simply at its default) has NO local
    // value, and writing the evaluated enum back on reuse would pin a local
    // value that permanently outranks the style. UnsetValue therefore means
    // "restore by clearing"; a boxed Visibility means "restore by writing";
    // anything else is a BindingExpression, which cannot be re-established from
    // here, so such a container is never parked in the first place.
    private readonly record struct PoolEntry(UIElement Control, object? ParkedVisibility);

    // Retaining a container that cannot serve the current row is what bounds the
    // working set across a root-type flip (issue #919) — but it only ever pays
    // off for a row shape that comes BACK, and for a keyed list it never does:
    // ApplyItemIdentityKey stamps a per-item key on the row root, CanUpdate
    // rejects unequal keys, so scrolling forward through N distinct items would
    // retain N containers and make the scan quadratic. Cap the pool at one
    // realized window per distinct root shape currently pooled (with a floor of
    // two windows, and never fewer than 32 entries) and evict oldest-first
    // beyond that — an evicted container is unmounted, parked collapsed and
    // untracked, which is the pre-#919 outcome and no worse.
    //
    // The per-shape term matters: a list that cycles its rows through three or
    // more root types needs every shape's window retained, or the shape evicted
    // this pass is re-minted next pass and the repeater's children grow without
    // bound (nothing can un-parent them).
    //
    // Because a pool miss is not a cache miss but a permanent native leak, the
    // per-shape window is granted to EVERY recurring shape and an absolute
    // container ceiling caps that term. A clamp on the shape COUNT would quietly
    // reintroduce the leak for any list cycling more shapes than the clamp: the
    // managed pool would stay bounded while the visual tree grew forever.
    //
    // The ceiling bounds the SHAPE term only. TrimPool always grants the
    // two-window floor first, so a viewport realizing more than half this many
    // rows settles above this number — deliberately. A pool smaller than the
    // realized window evicts containers the very next scroll re-realizes, and
    // every such eviction is permanent (nothing can un-parent a repeater child),
    // so clamping there would cost more than it saves. The real bound is
    // max(2 x window, min(ceiling, shapes x window)): bounded by the viewport,
    // which is the term that cannot run away.
    private const int MaxPooledContainers = 512;
    private int _maxRealized;
    private readonly Dictionary<ShapeKey, int> _shapeCounts = new();

    // A pooled container's reuse class. Two containers are interchangeable only
    // if these match, so the census must discriminate on exactly the STRUCTURAL
    // terms of Reconciler.CanUpdate — element type, ComponentElement.ComponentType,
    // XamlHostElement.TypeKey and KeyedMemoElement.MemoKey. Element.Key is
    // deliberately excluded: it is per-item and rotates, so folding it in would
    // make every row of a keyed list its own shape and blow the capacity up
    // instead of bounding it.
    //
    // MemoKey is included even though it can rotate per-item as well, because
    // unlike Element.Key it is a CanUpdate discriminator: two memo containers
    // with different keys can never be reused for one another, so counting them
    // as one shape under-sizes the pool and evicts containers that are still
    // wanted — and every such eviction is a permanently parented native leak.
    // When a MemoKey does rotate per item the census simply degrades to LRU,
    // which the absolute MaxPooledContainers ceiling already bounds.
    private readonly record struct ShapeKey(
        global::System.Type Element,
        global::System.Type? Component,
        string? HostTypeKey,
        object? MemoKey);

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
    internal int DebugViewBuilderCacheCount => _viewBuilderCache.Count;
    internal bool DebugTryGetLastElementByControl(UIElement control, out Element? element)
        => _lastElementByControl.TryGetValue(control, out element!);

    // Issue #327 (Option A) test seam: the keyed-memo LRU's rebuild (Factory invocation)
    // counter and live entry count. The headless effectiveness fixture drives BuildOrCache
    // through N recycle cycles and asserts FactoryInvocations stops climbing once keys are
    // cached. Gated by InternalsVisibleTo on Reactor.Tests / Reactor.AppTests.Host.
    internal long DebugKeyedMemoFactoryInvocations => _keyedMemoCache.FactoryInvocations;
    internal int DebugKeyedMemoCacheCount => _keyedMemoCache.Count;

    // Per-key memoization of the last viewBuilder result. Critical for
    // WinUI ItemsView under <see cref="WinUI.UniformGridLayout"/>: window
    // resize causes the framework to recycle most realized containers
    // and immediately re-realize the same indices with the same item
    // refs. Without memoization, every realize calls the user's
    // viewBuilder afresh, producing a new ItemContainerElement(VStack(...))
    // tree whose Child ref differs from the previously bound Element →
    // <see cref="Element.ShallowEquals"/> returns false → the reconcile
    // fast-path skip never fires → the entire subtree's Update methods
    // walk and write WinUI properties on every resize tick. By returning
    // the same Element instance for the same (key, item ref, index)
    // tuple, Reconcile hits its ReferenceEquals(a, b) shortcut and the
    // Update entry returns null without descending. Net: zero per-row
    // work for resize-driven realize cycles, as long as the user's data
    // follows the standard "new object for new state" pattern (records,
    // immutable updates, etc.).
    private readonly Dictionary<string, ViewBuilderCacheEntry> _viewBuilderCache = new(global::System.StringComparer.Ordinal);
    private readonly struct ViewBuilderCacheEntry
    {
        public readonly T Item;
        public readonly int Index;
        public readonly Element Built;
        public ViewBuilderCacheEntry(T item, int index, Element built)
        { Item = item; Index = index; Built = built; }
    }

    // Issue #327 (Option A) — opt-in keyed memo LRU. When the viewBuilder returns a
    // KeyedMemoElement (author wrote `Memo(key, () => …)`), BuildOrCache resolves it here.
    // Keyed by the author's MemoKey with value equality, so the int-index VirtualList path
    // (where _viewBuilderCache's ReferenceEquals(item) guard never hits because each access
    // re-boxes the index) still serves the SAME inner Element instance across recycles. See
    // KeyedMemoCache for bound/eviction/invalidation.
    private readonly KeyedMemoCache _keyedMemoCache = new();

    // Issue #327 review — for a value-type T (the int-index VirtualList path) the
    // _viewBuilderCache lookup can never hit (its ReferenceEquals(item) guard re-boxes both
    // operands), so populating it is dead weight that also grows UNBOUNDED — one retained entry
    // per distinct key (index) as the user scrolls. JIT-folded per instantiation, so the guard is
    // free. The cross-recycle cache for value-type T is the bounded KeyedMemoCache (opt-in Memo).
    private static readonly bool s_valueTypeItem = typeof(T).IsValueType;

    /// <summary>
    /// Resolve the viewBuilder output for a (key, item, index) tuple,
    /// memoized by reference identity of <paramref name="item"/>. See
    /// <see cref="_viewBuilderCache"/> for the rationale.
    /// <para><c>keyed</c> is true on the spec-042 <see cref="ReactorRow"/>
    /// path, where <paramref name="key"/> is the author's <c>keySelector</c>
    /// projection (a stable per-item identity). When set, the projection is
    /// propagated to the row's top-level <see cref="Element.Key"/> — see
    /// <see cref="ApplyItemIdentityKey"/> (issue #326). It is false on the
    /// legacy int-index path, where the "key" is just the realized index and
    /// propagating it would force a control swap on every scroll.</para>
    /// </summary>
    internal Element BuildOrCache(string key, T item, int index, bool keyed)
    {
        if (_viewBuilderCache.TryGetValue(key, out var cached)
            && ReferenceEquals(cached.Item, item)
            && cached.Index == index)
        {
            return cached.Built;
        }
        var built = _viewBuilder(item, index);
        // Issue #327 (Option A): a KeyedMemoElement asserts its inner Factory is a pure
        // function of MemoKey. Resolve it through the factory-owned bounded LRU so a cache
        // HIT returns the SAME inner Element instance across container recycles → the next
        // Reconcile observes ReferenceEquals via Element.ShallowEquals and skips the per-row
        // reconcile descent; a MISS invokes Factory() exactly once. The identity-key stamp is
        // folded into the resolve (keyed path only) so the cached instance is the final one
        // returned on every subsequent hit (preserving ReferenceEquals).
        //
        // Only a "bare" wrapper is memoized: resolution returns the inner element, so any
        // modifiers / Key / behavioral Extensions applied ON the wrapper itself (the
        // non-idiomatic `Memo(k, …).Margin(8)` shape — modifiers belong inside the factory
        // lambda) would be dropped. A decorated wrapper instead falls through unchanged and is
        // rendered by the reconciler's transparent unwrap path (Mount/Update), which preserves
        // those modifiers.
        //
        // Spec 010: the test is HasBehavioralExtras, not `Extensions is null`. A source-map
        // CallSite stamp materializes the extras bucket but carries no behavior, and the inner
        // element gets its own stamp from its own call site — so dropping the wrapper's stamp is
        // harmless. Testing raw nullness here would disable this cache for every memoized row
        // whenever source mapping is on.
        if (built is KeyedMemoElement km
            && km.Modifiers is null && km.Key is null && !Element.HasBehavioralExtras(km))
            built = _keyedMemoCache.Resolve(km, keyed ? key : null);
        else if (keyed)
            built = ApplyItemIdentityKey(built, key);
        // Skip the never-hitting _viewBuilderCache for value-type T (see s_valueTypeItem): on the
        // int-index path it can only grow unbounded (one pinned row Element per scrolled index)
        // without ever serving a hit. Reference-type T (LazyVStack<record>, ItemsView resize, …)
        // still uses the ReferenceEquals fast-path, so keep populating there. (issue #327 review)
        if (!s_valueTypeItem)
            _viewBuilderCache[key] = new ViewBuilderCacheEntry(item, index, built);
        return built;
    }

    /// <summary>
    /// Issue #326 — propagate the author's per-item <c>keySelector</c>
    /// projection onto the row's top-level <see cref="Element.Key"/> so the
    /// recycle-on-reuse <see cref="Reconciler.Reconcile"/> path (see
    /// <see cref="GetElement"/>) observes a different key when a realized
    /// container is reused for a <em>different</em> logical item. That flips
    /// <see cref="Reconciler.CanUpdate"/> to false → Reactor takes its
    /// keyed-replacement path (unmount + fresh mount) instead of an in-place
    /// property diff, which resets the row's per-item Component
    /// <c>UseState</c> / <c>UseEffect</c> state. Without this, post-#324
    /// recycling reuses the same realized inner <c>Component&lt;T&gt;</c>
    /// across logical items and carries hook state from item A into item B.
    ///
    /// <para>An explicit author-supplied key (<c>row.WithKey(...)</c> inside
    /// the row builder) always wins: it is only applied when the built row's
    /// <see cref="Element.Key"/> is still null. Same-item re-renders
    /// (RefreshRealizedItems) keep the same key on both old and new elements,
    /// so <see cref="Reconciler.CanUpdate"/> stays true and the row diffs in
    /// place — state is preserved exactly when the logical item is unchanged.</para>
    /// </summary>
    internal static Element ApplyItemIdentityKey(Element built, string key)
        => built.Key is null ? built with { Key = key } : built;

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
        // A new viewBuilder closure may capture different external state (UseState
        // cells, Observable subscriptions, theme, etc.) than the one that produced the
        // cached <see cref="ViewBuilderCacheEntry.Built"/> entries. We can't see through
        // delegate captures cheaply, so invalidate conservatively here. Resize-driven
        // recycle/realize cycles still hit the cache because window resize doesn't run
        // the component render path → UpdateInPlace doesn't fire.
        _viewBuilderCache.Clear();
        // Issue #327 (Option A): same invalidation boundary for the keyed memo LRU — a new
        // viewBuilder closure may produce different inner content for the same MemoKey, so a
        // previously-cached inner instance must not be served (mirrors the clear above).
        _keyedMemoCache.Clear();
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
#pragma warning disable CS0649 // Assigned via InternalsVisibleTo by Reactor.Advanced's DataGridComponent (spec 062 §7 B3 — the data grid moved out of core), never inside this assembly.
    internal Func<bool>? ShouldSkipRefresh;
#pragma warning restore CS0649

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
            if (child is null)
            {
                // The framework can return null from TryGetElement during
                // transient layout passes — e.g., the inner ItemsRepeater
                // is mid-relayout when an unrelated re-render (slider
                // scrub, theme change) walks down here. Permanently
                // dropping the key from <see cref="_mountedElements"/> in
                // that case used to strand row 0 (which UniformGridLayout
                // anchors and never recycles): RecycleElement never fires
                // for it, so once dropped it stays invisible to every
                // subsequent refresh and the row's content freezes at
                // whatever state value the user landed on at the moment
                // of the transient null. Skip this iteration but keep
                // the entry so the next refresh pass can pick it back up.
                continue;
            }

            if (!_mountedElements.TryGetValue(key, out var oldElement)) continue;
            if (currentIndex < 0 || currentIndex >= _items.Count) continue;

            var newElement = BuildOrCache(key, _items[currentIndex], currentIndex, keyed: _listState is not null);

            // Issue #919 — when CanUpdate is false, Reconcile unmounts `child` and mints a
            // REPLACEMENT control, but a realized ItemsRepeater child cannot be swapped from
            // managed code: ItemsRepeater is a FrameworkElement, not a Panel, so there is no
            // Children collection to assign into. The only in-place rescue is
            // TryAdoptRealizedReplacement, which requires the realized control to be a
            // component-wrapper Border. For every other shape (e.g. a DataGrid row flipping from
            // a Grid root to a FlexPanel root on expand) the replacement had nowhere to go: the
            // old control was detached, the replacement was never parented, and — because
            // _mountedElements[key] had already been advanced to `newElement` — the NEXT refresh
            // paired the new element with the still-realized old control, so CanUpdate returned
            // true and the handler dispatch hard-cast a Grid to a FlexPanel (InvalidCastException).
            //
            // Detect that case before mounting a doomed replacement: leave `child` and all of its
            // tracking untouched (the control still faithfully hosts `oldElement`) and ask the
            // framework to recycle + re-realize the row, where GetElement's return channel CAN
            // install a different control type.
            if (!_reconciler.CanUpdate(oldElement, newElement) && !CanSafelyAdopt(child, oldElement, newElement))
            {
                ScheduleReRealize(key);
                continue;
            }

            _mountedElements[key] = newElement;

            var replacement = _reconciler.Reconcile(oldElement, newElement, child, _requestRerender);
            if (replacement is not null && !ReferenceEquals(replacement, child))
            {
                // CanUpdate was false (the row's Element.Key changed — e.g. the
                // documented .WithKey($"{id}:{rev}") pattern — or a root type
                // change) → Reconcile unmounted `child` and built a fresh
                // `replacement`. It can ALSO be true: a decorator-style V1
                // handler (spec 048 §14) may substitute a different instance
                // when only its inner target changed shape — FlyoutElement is
                // the live example, and CanUpdate(FlyoutElement, FlyoutElement)
                // is true regardless of what happens to Target. The ItemsRepeater
                // that still parents `child` isn't a Panel, so we can't swap the
                // realized slot the way the GetElement framework return-channel
                // does. Adopt the fresh subtree into the still-parented wrapper
                // only when CanSafelyAdopt allows it — adoption moves the
                // component subtree and nothing else, so a decorator's own
                // wiring (the element tag the flyout's Opened/Closed handlers
                // read back, the attached flyout itself) would be left on the
                // discarded replacement. Otherwise keep the maps consistent so
                // no stale entry survives and re-realize fixes the visual.
                // Without this, the old control was orphaned (stale state still
                // visible) and _lastElementByControl[child] pointed at an
                // element the control no longer hosted. (Issue #326 pr-review H1)
                if (CanSafelyAdopt(child, oldElement, newElement)
                    && _reconciler.TryAdoptRealizedReplacement(child, replacement))
                {
                    // `child` now hosts the fresh component subtree — tracking
                    // stays anchored on the still-realized `child`.
                    _lastElementByControl[child] = newElement;
                    // Adoption moves the component node and Border.Child but not
                    // ReactorAttached.StateProperty, so the still-parented wrapper
                    // would keep pointing at the OLD element. Harmless for
                    // rendering, but ReactorSourceMap.GetSource reads that
                    // back-pointer, so without this the wrapper reports the
                    // previous row's call site (spec 010).
                    if (child is FrameworkElement adoptedFe)
                        Reconciler.SetElementTagIfNeeded(adoptedFe, newElement);
                }
                else
                {
                    // Adoption failed, so `replacement` can never be installed. `child` was
                    // already unmounted inside Reconcile, so drop every tracking entry that
                    // points at it, tear the orphaned replacement down (otherwise its component
                    // effect cleanups leak — it is mounted but unreachable), and route the row
                    // back through the framework's realize channel. `child` cannot be
                    // un-parented from the repeater, so retire it: park it collapsed with its
                    // Reactor state fully detached rather than leaving an unmounted ghost
                    // painted over the row with live trampolines still attached. (Issue #919)
                    RetireAlreadyUnmounted(child);
                    _keyByControl.Remove(child);
                    _mountedElements.Remove(key);
                    _reconciler.UnmountChild(replacement);
                    ScheduleReRealize(key);
                }
            }
            else
            {
                // In-place diff (same key) reused `child`. Keep the per-control
                // "last element" tracking in lockstep with _mountedElements.
                // Without this, a later RecycleElement→GetElement round-trip for
                // the same control would feed the pre-refresh Element to
                // Reconcile as oldElement and diff against a stale tree shape.
                // (PR #324 review)
                _lastElementByControl[child] = newElement;
            }
        }
    }

    // ── Deferred row re-realization (issue #919) ─────────────────────
    //
    // A realized ItemsRepeater container can only be *replaced* by the framework's own
    // realize channel (IElementFactory.GetElement), so when a row's root element type or key
    // changes in a way that cannot be diffed in place, we ask WinUI to recycle and re-realize
    // that row: swap the row's ReactorRow instance inside the internally-owned
    // ObservableCollection<ReactorRow>, which raises a Replace collection change.
    //
    // The swap is deferred onto the dispatcher because RefreshRealizedItems runs inside a
    // reconcile pass (often mid-layout), and mutating the items source there throws
    // "Cannot run layout in the middle of a collection change".
    private HashSet<string>? _pendingReRealize;
    private bool _reRealizeQueued;

    private void ScheduleReRealize(string key)
    {
        // Nothing to drive the swap through on the legacy (unkeyed) path — the row simply
        // keeps its current content until the framework recycles the container on scroll.
        if (_listState is null) return;

        (_pendingReRealize ??= new(global::System.StringComparer.Ordinal)).Add(key);
        if (_reRealizeQueued) return;

        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (queue is null)
        {
            // No dispatcher (headless harnesses): apply immediately. There is no layout pass
            // in flight in that configuration, so the collection change is safe.
            FlushReRealize();
            return;
        }

        _reRealizeQueued = true;
        if (!queue.TryEnqueue(() =>
        {
            _reRealizeQueued = false;
            FlushReRealize();
        }))
        {
            _reRealizeQueued = false;
            FlushReRealize();
        }
    }

    private void FlushReRealize()
    {
        var pending = _pendingReRealize;
        _pendingReRealize = null;
        if (pending is null || pending.Count == 0) return;

        var listState = _listState;
        if (listState is null) return;

        foreach (var key in pending)
        {
            if (!listState.ByKey.TryGetValue(key, out var row)) continue;
            var index = row.Index;
            if (index < 0 || index >= listState.Source.Count) continue;
            // The row may have moved (or been rebuilt) between scheduling and flushing.
            if (!ReferenceEquals(listState.Source[index], row)) continue;

            // A fresh instance is required: INotifyCollectionChanged consumers track items by
            // object identity, so replacing a row with itself is a no-op for the repeater.
            var fresh = new ReactorRow
            {
                Index = row.Index,
                Key = row.Key,
                PendingEnterAnimation = row.PendingEnterAnimation,
            };
            listState.ByKey[key] = fresh;
            listState.Source[index] = fresh;
        }
    }

    // <snippet:factory-shape>
    public UIElement GetElement(ElementFactoryGetArgs args)
    {
        // Resolve the realized data → (key, dataIndex). Three paths:
        //   1. Spec 042: args.Data is ReactorRow — read both off the row.
        //   2. Legacy: args.Data is int — index directly, synthetic key.
        //   3. Fallback: unknown shape, treat as index 0.
        string key;
        int index;
        bool keyed;
        switch (args.Data)
        {
            case ReactorRow row:
                key = row.Key;
                index = row.Index;
                keyed = true;
                break;
            case int i:
                index = i;
                key = i.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
                keyed = false;
                break;
            default:
                index = 0;
                key = "0";
                keyed = false;
                break;
        }

        if (index < 0 || index >= _items.Count)
            return new TextBlock { Text = "" };

        var item = _items[index];
        var element = BuildOrCache(key, item, index, keyed);

        UIElement? control;
        if (TryTakeCompatibleFromPool(element, out var reused, out var oldElement, out var parkedVisibility))
        {
            // Reuse a previously-recycled container. The framework still has
            // it parented to the ItemsRepeater, so the ViewManager.cpp:866
            // Append-skip kicks in and the visual tree stays stable.
            //
            // Undo the parking collapse from RecycleElement BEFORE reconciling.
            // Restore the exact pre-park value source rather than forcing
            // Visible: an in-place diff whose Visibility modifier is unchanged
            // writes nothing, so forcing Visible would silently un-collapse a
            // row the author asked to hide.
            RestoreParkedVisibility(reused, parkedVisibility);
            var replacement = _reconciler.Reconcile(oldElement, element, reused, _requestRerender);
            if (replacement is not null && !ReferenceEquals(replacement, reused))
            {
                // Pass-2 reuse: the row's key changed, so Reconcile unmounted the old
                // component (effect cleanups ran) and built a fresh wrapper. Move that
                // subtree back into the container we already have — returning the
                // replacement instead would strand `reused`, which cannot be un-parented
                // from an ItemsRepeater (see DetachFromParent). (Issues #326, #919.)
                //
                // A pass-1 selection can land here too: CanUpdate was true, but a
                // decorator-style handler substituted a different instance. Re-check
                // CanSafelyAdopt so only wrappers whose entire state lives in the
                // component subtree are adopted; everything else takes the fresh
                // replacement and parks the container.
                if (CanSafelyAdopt(reused, oldElement, element)
                    && _reconciler.TryAdoptRealizedReplacement(reused, replacement))
                {
                    control = reused;
                    // Same as the adopt path above: refresh the back-pointer the
                    // adoption itself does not move, so the reported source
                    // location follows the row that is actually live (spec 010).
                    if (reused is FrameworkElement adoptedFe)
                        Reconciler.SetElementTagIfNeeded(adoptedFe, element);
                }
                else
                {
                    // Nothing can install `replacement` into `reused`. Retire `reused`
                    // — park it collapsed with its Reactor state detached — rather than
                    // leaving a live ghost row painted over the list. Do NOT return it to
                    // the pool: it was unmounted inside Reconcile, so its tracked Element
                    // no longer describes it.
                    RetireAlreadyUnmounted(reused);
                    control = replacement;
                }
            }
            else
            {
                control = reused;
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
            if (_keyByControl.Count > _maxRealized) _maxRealized = _keyByControl.Count;

            // Issue #383: arm the multi-select checkmark flicker guard on the
            // realized container. Idempotent per container instance.
            // Intentionally scoped to ItemContainer (the ItemsView item-root
            // wrapper): LazyVStack/LazyHStack realize into plain panels via
            // ItemsRepeater, not ItemContainer, and the MultiSelectStates.Multiple
            // storyboard the guard collapses only ever runs for multi-select
            // ItemContainers — so widening this to all controls would be inert
            // work everywhere else. Do not "generalize" it.
            if (control is ItemContainer itemContainer)
                ItemContainerSelectionFlickerGuard.Ensure(itemContainer);
        }

        return control ?? new TextBlock { Text = "" };
    }
    // </snippet:factory-shape>

    // Pick a recycled container that can actually be reused for `element`,
    // newest-first (the most recently recycled container is the most likely to
    // be cache-warm and shape-identical). Two passes, in priority order:
    //
    //   1. CanUpdate → a pure in-place diff, the cheapest possible reuse.
    //   2. Two component wrappers of the same shape → CanUpdate is false (the
    //      row's key changed), so Reconcile unmounts the old component — running
    //      its effect cleanups — and mints a fresh wrapper, which
    //      TryAdoptRealizedReplacement then moves back into this still-parented
    //      Border. Same container, fresh per-item state. (Issue #326.)
    //      Adoption transplants only the component subtree — not the wrapper's
    //      own runtime bookkeeping (ElementRef cells, OnMount/OnUpdate/OnUnmount
    //      registrations, which ApplyModifiers installs keyed on the REPLACEMENT
    //      Border) — so this pass is restricted to wrappers that carry no
    //      modifiers and no extensions at all. Equal-by-value modifiers are not
    //      enough: an equal non-null Ref would end up pointing at the discarded
    //      replacement, and an equal OnUnmount would be registered against it.
    //
    // Issue #919: anything else must STAY in the pool. Handing it to Reconcile
    // would mint a different control, and the rejected one can never be removed
    // from an ItemsRepeater (see DetachFromParent) — so every root-type flip
    // used to strand one live, visible, arranged container per realized row,
    // unbounded, painting stale rows over the list. Leaving it pooled instead
    // bounds the working set at one realized window per root shape, which is
    // what WinUI's own RecyclePool does for multiple data templates.
    private bool TryTakeCompatibleFromPool(
        Element element,
        [NotNullWhen(true)] out UIElement? reused,
        [NotNullWhen(true)] out Element? oldElement,
        out object? parkedVisibility)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = _recyclePool.Count - 1; i >= 0; i--)
            {
                var entry = _recyclePool[i];
                if (!_lastElementByControl.TryGetValue(entry.Control, out var candidateElement))
                {
                    // Untracked pool entry (its tracking was dropped elsewhere) can
                    // never be reconciled — evict it so the scan stays short. It
                    // stays parented but collapsed, which is the best available
                    // outcome for a repeater child.
                    //
                    // Parking is sufficient here rather than a shortcut: the only
                    // writer that drops _lastElementByControl is
                    // RetireAlreadyUnmounted, which detaches ReactorState first (and
                    // whose RetireContainer caller unmounts before that). A control
                    // missing from the map has therefore already been torn down, so
                    // unmounting it again here would re-run effect cleanups. Any
                    // future second Remove site must retire the control itself, or
                    // this branch silently starts leaking.
                    _recyclePool.RemoveAt(i);
                    ParkOrphan(entry.Control);
                    continue;
                }

                var usable = pass == 0
                    ? _reconciler.CanUpdate(candidateElement, element)
                    : CanSafelyAdopt(entry.Control, candidateElement, element);
                if (!usable) continue;

                _recyclePool.RemoveAt(i);
                reused = entry.Control;
                oldElement = candidateElement;
                parkedVisibility = entry.ParkedVisibility;
                return true;
            }
        }

        reused = null;
        oldElement = null;
        parkedVisibility = null;
        return false;
    }

    // Park a container we can neither reuse nor un-parent. Collapsing is what
    // keeps it from rendering: an ItemsRepeater child it no longer owns is never
    // re-arranged, so it would otherwise keep painting at its last arranged
    // bounds on top of the live rows.
    private static void ParkOrphan(UIElement control) => control.Visibility = Visibility.Collapsed;

    // The ONLY shape TryAdoptRealizedReplacement can rescue without losing
    // state. Adoption transplants the fresh component subtree plus its
    // _componentNodes entry onto the still-parented wrapper, and nothing else —
    // but ApplyModifiers installs the wrapper's runtime bookkeeping (the
    // ElementRef cell, the OnMount/OnUpdate/OnUnmount registrations) keyed on
    // the REPLACEMENT Border. So an adopted wrapper that carries modifiers ends
    // up with its Ref pointing at the discarded replacement and its OnUnmount
    // registered against it. Value-equal modifiers are NOT sufficient: equality
    // makes the two records interchangeable, not the two controls.
    //
    // Callers that fail this gate must fall back to something that produces a
    // genuinely fresh container — the framework's realize channel
    // (ScheduleReRealize) or a plain mount — never a silent adopt.
    //
    // Spec 010: the extras test is HasBehavioralExtras, not `Extensions is null`.
    // The reason this gate rejects extras is that ApplyModifiers installs runtime
    // bookkeeping keyed on the replacement control; a source-map CallSite stamp
    // installs none, so it must not disqualify adoption. Testing raw nullness
    // here would force re-realization of every stamped row whenever source
    // mapping is on.
    private static bool CanSafelyAdopt(UIElement control, Element oldElement, Element newElement)
        => control is Border
            && oldElement is ComponentElement oldComp
            && newElement is ComponentElement newComp
            && oldComp.ComponentType == newComp.ComponentType
            && oldComp.Modifiers is null && newComp.Modifiers is null
            && !Element.HasBehavioralExtras(oldComp) && !Element.HasBehavioralExtras(newComp);

    // Retire a container for good: it will never be handed back to the
    // repeater, but the repeater keeps it parented regardless, so everything
    // Reactor attached to it has to come off explicitly.
    //
    // Order matters. Unmount first, while the tree is still intact, so
    // component effect cleanups run. Then detach Reactor state across the whole
    // subtree — UnmountChild tears down components but leaves ReactorState's
    // Element pointer and the ModifierEventHandlerState trampolines in place,
    // and a permanently-parented control can still raise size/property events
    // afterwards; DetachReactorState is exactly the "leaves Reactor's ownership
    // but stays alive" primitive for that. Only then collapse it, so the
    // collapse itself can't re-enter a live handler.
    private void RetireContainer(UIElement control)
    {
        _reconciler.UnmountChild(control);
        RetireAlreadyUnmounted(control);
    }

    // The tail of RetireContainer, for containers Reconcile ALREADY unmounted.
    // Both rejected-adoption paths land here: Reconcile minted a replacement,
    // CanSafelyAdopt refused it, and the container it unmounted can never be
    // un-parented from the repeater. Such a container is permanently parented
    // and permanently dead, so it needs the same state teardown as an evicted
    // one — dropping only the map entries would leave its ReactorState Element
    // pointer and modifier trampolines live on a control that still raises
    // size/property events. (Issue #919 pr-review M1.)
    private void RetireAlreadyUnmounted(UIElement control)
    {
        DetachReactorStateRecursive(control);
        DetachFromParent(control);
        ParkOrphan(control);
        _lastElementByControl.Remove(control);
    }

    private static void DetachReactorStateRecursive(DependencyObject node)
    {
        if (node is FrameworkElement fe)
        {
            Reconciler.DetachReactorState(fe);
            // DetachReactorState is the shared "leaves Reactor's ownership"
            // primitive and deliberately keeps the ownership slots that a
            // pooled control re-uses on its next rent. A retired repeater
            // container has no next rent — it is parked forever — so the
            // slots are pure retention: ListState holds the ObservableCollection
            // WinUI bound to, ItemViewSource holds the per-index view source, and
            // ControlEventState holds a payload box whose delegates capture the
            // dead component. Clear them here rather than in DetachReactorState
            // so the pool-return contract (Q18, issue #114) is untouched.
            if (fe.GetValue(Reconciler.ReactorAttached.StateProperty)
                is Reconciler.ReactorState state)
            {
                state.ListState = null;
                state.ItemViewSource = null;
                state.ControlEventState = null;
            }
        }

        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
            DetachReactorStateRecursive(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
    }

    // Collapse a container on its way into the pool, handing back the
    // Visibility value source to restore on reuse. Returns false when the
    // container cannot be parked reversibly: its Visibility is bound, and
    // ReadLocalValue yields a BindingExpression that no public API can
    // reinstall. Such a container must be retired rather than pooled — pooling
    // it un-collapsed would leave a visible, no-longer-arranged repeater child
    // painting at its last bounds, which is the ghost row this whole change
    // exists to prevent.
    private static bool TryParkForPool(UIElement control, out object? restore)
    {
        restore = control.ReadLocalValue(UIElement.VisibilityProperty);
        if (restore is not Visibility && !ReferenceEquals(restore, DependencyProperty.UnsetValue))
        {
            restore = null;
            return false;
        }

        ParkOrphan(control);
        return true;
    }

    // Undo TryParkForPool. Clearing (rather than writing Visible) when there was
    // no local value is what keeps a Style- or default-provided Visibility from
    // being permanently overridden by a local value we invented.
    private static void RestoreParkedVisibility(UIElement control, object? parked)
    {
        if (ReferenceEquals(parked, DependencyProperty.UnsetValue))
            control.ClearValue(UIElement.VisibilityProperty);
        else if (parked is Visibility v)
            control.Visibility = v;
    }

    // Detach a UIElement from whatever container it's parented to.
    //
    // NOTE: this canNOT detach a container realized directly by an ItemsRepeater.
    // Despite deriving from Panel in the C++ implementation, ItemsRepeater does
    // not project IPanel — `repeater is Panel` and even an ABI `.As<Panel>()`
    // both fail — so there is no Children collection to remove from and no public
    // API that un-parents a realized child. Callers must pair this with
    // ParkOrphan (collapse in place) for the repeater case. It is still the right
    // call for nested Panel/Border/ContentControl subtrees, so it's safe to call
    // on arbitrary recycled content.
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
        if (_keyByControl.Remove(args.Element, out var stashedKey) && stashedKey is not null)
            _mountedElements.Remove(stashedKey);

        // DON'T UnmountChild — the WinUI tree stays alive and is reused on
        // the next GetElement call via Reconciler.Reconcile. ItemsRepeater
        // keeps the element parented either way (see ViewManager.cpp), so
        // tearing down Reactor state here would just be discarded work.
        // The _lastElementByControl entry stays valid for the next realize.
        //
        // Collapse while parked, remembering the visibility to restore. The
        // repeater stops arranging a recycled child but keeps it parented, so a
        // still-Visible one paints at its last arranged bounds — a ghost row over
        // the live list whenever the pool isn't drained in the same pass
        // (issue #919). This mirrors WinUI's own RecyclePool.
        if (!TryParkForPool(args.Element, out var restore))
        {
            // Cannot be parked reversibly (bound Visibility). Retire it rather
            // than pool a still-visible ghost or clobber the binding.
            RetireContainer(args.Element);
            return;
        }

        _recyclePool.Add(new PoolEntry(args.Element, restore));

        TrimPool();
    }

    // Evict once the pool exceeds the capacity its realized window and shape mix
    // justify. Eviction retires the container, which is where the lease taken
    // out by RecycleElement (keep it mounted so the next realize can diff it in
    // place) ends.
    //
    // Without a cap, a keyed list — where every row root carries a per-item key,
    // so CanUpdate rejects every cross-item reuse — would retain one container
    // and one tracking entry per item scrolled past, and the reuse scan would
    // grow with it. Without the per-shape terms, a list that cycles its rows
    // through three or more shapes would evict the shape that is about to come
    // back, re-mint it, and grow the repeater's permanently-parented children
    // without bound.
    private void TrimPool()
    {
        // Cheap path first: the shape census below only matters once the pool
        // has outgrown the two-window base, which a single- or dual-shape list
        // never does.
        var capacity = global::System.Math.Max(32, _maxRealized * 2);
        if (_recyclePool.Count <= capacity) return;

        CensusShapes();
        capacity = global::System.Math.Max(
            capacity,
            global::System.Math.Min(MaxPooledContainers, _maxRealized * _shapeCounts.Count));
        if (_recyclePool.Count <= capacity) return;

        var excess = _recyclePool.Count - capacity;
        for (var i = 0; i < excess; i++)
            EvictOldestOfLargestShape();
    }

    // Reuse compatibility — not the native control type — is what makes two
    // pooled containers interchangeable. Three modifier-free Component<A/B/C>
    // roots all mount as Border, so keying the census on Control.GetType()
    // would see one shape where there are really three and under-size the pool.
    // Same for XamlHostElement: CanUpdate discriminates on TypeKey, so hosts
    // with different TypeKeys can never be reused for one another either — and
    // likewise KeyedMemoElement on MemoKey.
    private ShapeKey ShapeKeyOf(UIElement control)
        => _lastElementByControl.TryGetValue(control, out var element)
            ? new ShapeKey(
                element.GetType(),
                (element as ComponentElement)?.ComponentType,
                (element as global::Microsoft.UI.Reactor.Hosting.XamlHostElement)?.TypeKey,
                (element as KeyedMemoElement)?.MemoKey)
            : new ShapeKey(control.GetType(), null, null, null);

    private void CensusShapes()
    {
        _shapeCounts.Clear();
        for (var i = 0; i < _recyclePool.Count; i++)
        {
            var key = ShapeKeyOf(_recyclePool[i].Control);
            _shapeCounts[key] = _shapeCounts.TryGetValue(key, out var count) ? count + 1 : 1;
        }
    }

    // Apply eviction pressure where the surplus actually is. Plain oldest-first
    // lets a churning majority shape evict every last entry of a minority shape
    // that is still being cycled back to.
    private void EvictOldestOfLargestShape()
    {
        ShapeKey worst = default;
        var max = -1;
        foreach (var kv in _shapeCounts)
        {
            if (kv.Value <= max) continue;
            max = kv.Value;
            worst = kv.Key;
        }

        for (var i = 0; i < _recyclePool.Count; i++)
        {
            if (!ShapeKeyOf(_recyclePool[i].Control).Equals(worst)) continue;
            RetireContainer(_recyclePool[i].Control);
            _recyclePool.RemoveAt(i);
            _shapeCounts[worst] = max - 1;
            return;
        }

        // The census disagreed with the pool (it shouldn't). Fall back to plain
        // oldest-first so trimming always makes progress, and drop the phantom
        // bucket so the next call doesn't re-pick a victim that isn't there.
        _shapeCounts.Remove(worst);
        RetireContainer(_recyclePool[0].Control);
        _recyclePool.RemoveAt(0);
    }

}
