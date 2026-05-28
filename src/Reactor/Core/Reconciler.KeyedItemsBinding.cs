using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

// Spec 047 §14 Phase 3 close-out — descriptor-driven keyed items binding.
//
// `TemplatedItems<TItem, TElement, TControl>` declares the data shape
// (items list, key selector, per-item view builder) the same way
// `Panel<>` declares its child accessor. The dispatch arm in
// `V1HandlerAdapter` routes through this partial so the realization
// machinery is reused 1:1 with the legacy `MountTemplatedListView` path:
//
//   * Mount: build a fresh `ReactorListState` from the user-supplied items
//     + keySelector, attach the shared `ContainerContentChanging` handler,
//     stash a closure-backed `IItemViewSource` so the CCC handler can
//     realize the right Element at the right index, and bind `ItemsSource`
//     to `state.Source` so subsequent OC deltas drive incremental
//     animations.
//   * Update: refresh the stash (the view-builder closure captures the
//     *new* element so realizations done after this point produce the
//     correct content), run `KeyedListDiff.Apply` against the new keys,
//     then walk realized containers to refresh their content via
//     `RefreshRealizedContainers` — exactly the legacy ordering.
//
// MVP supports `WinUI.ListViewBase` (ListView + GridView). The strategy
// shape is open to `WinUI.ItemsRepeater` / virtualized panels later;
// adding a control-type arm is purely additive and doesn't require an
// engine API break.

public sealed partial class Reconciler
{
    /// <summary>
    /// Mount or update the keyed items binding for a descriptor-driven
    /// templated items control. Reached via
    /// <see cref="V1Protocol.V1HandlerAdapter{TElement,TControl}"/> when the
    /// descriptor's Children strategy is
    /// <c>TemplatedItems&lt;TItem,TElement,TControl&gt;</c>.
    /// </summary>
    /// <param name="control">Host control. MVP supports
    /// <see cref="WinUI.ListViewBase"/>. Other control types throw
    /// <see cref="InvalidOperationException"/> so the gap is visible to
    /// descriptor authors at port time rather than silently no-op'ing.</param>
    /// <param name="items">Live items list from
    /// <c>TemplatedItems&lt;&gt;.GetItems</c>. Treated as immutable inside
    /// this binder — never mutated.</param>
    /// <param name="keySelector">Stable identity projection. Must produce
    /// non-null strings; null/duplicate keys trigger the same diff bailout
    /// path as the legacy element-based binder.</param>
    /// <param name="buildItemView">Per-item Element factory captured by
    /// the binder into the stashed <see cref="IItemViewSource"/>. Called
    /// from the CCC handler on container realization and from
    /// <c>RefreshRealizedContainers</c> on update.</param>
    /// <param name="requestRerender">Bubbles into the realization
    /// machinery so descendant components can request re-renders the same
    /// way they do under the legacy path.</param>
    /// <param name="isMount">True on first bind; false on every
    /// subsequent update for the same control.</param>
    internal void BindKeyedItemsSource<TItem>(
        FrameworkElement control,
        IReadOnlyList<TItem> items,
        Func<TItem, int, string> keySelector,
        Func<TItem, int, Element> buildItemView,
        Action requestRerender,
        bool isMount)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(buildItemView);
        ArgumentNullException.ThrowIfNull(requestRerender);

        // Refresh the stashed view source on every call. The closure
        // captures the *current* `items` + `buildItemView` references, so
        // realizations + container refreshes after this point see the
        // updated data. Cheap allocation; happens at most once per render.
        var viewSource = new ClosureItemViewSource<TItem>(items, buildItemView);
        SetItemViewSource(control, viewSource);

        switch (control)
        {
            case WinUI.ListViewBase lvb:
                BindListViewBaseKeyedItems(lvb, items, keySelector, requestRerender, isMount, viewSource);
                return;
            default:
                throw new InvalidOperationException(
                    $"TemplatedItems<> binder does not yet support {control.GetType().FullName}. " +
                    "Supported on Mount/Update: WinUI.ListViewBase (ListView, GridView). " +
                    "Adding ItemsRepeater / Lazy*Stack support is a follow-up on this engine partial.");
        }
    }

    /// <summary>
    /// §14 Phase 3 close-out — erased variant. Same realization plumbing
    /// as <see cref="BindKeyedItemsSource"/> but reads items + keys
    /// through an <see cref="IKeyedItemSource"/> (the element itself, in
    /// practice) so the strategy declaration does not carry TItem. Used
    /// by <see cref="V1Protocol.TemplatedItemsErased{TElement,TControl}"/>
    /// to port the existing typed templated-list peers
    /// (<c>TemplatedListViewElement&lt;T&gt;</c>, <c>TemplatedGridViewElement&lt;T&gt;</c>).
    /// </summary>
    internal void BindErasedKeyedItemsSource(
        FrameworkElement control,
        IKeyedItemSource source,
        Action requestRerender,
        bool isMount)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requestRerender);

        // Same stash contract as BindKeyedItemsSource — the CCC handler
        // pulls the live view source from here on every container realize.
        SetItemViewSource(control, source);

        switch (control)
        {
            case WinUI.ListViewBase lvb:
                BindListViewBaseErasedKeyedItems(lvb, source, requestRerender, isMount);
                return;
            case WinUI.ItemsRepeater ir:
                BindItemsRepeaterErasedKeyedItems(ir, source, requestRerender, isMount);
                return;
            case WinUI.ItemsView iv:
                BindItemsViewErasedKeyedItems(iv, source, requestRerender, isMount);
                return;
            default:
                throw new InvalidOperationException(
                    $"TemplatedItemsErased<> binder does not yet support {control.GetType().FullName}. " +
                    "Supported on Mount/Update: WinUI.ListViewBase (ListView, GridView), " +
                    "WinUI.ItemsRepeater (LazyVStack<T>, LazyHStack<T>, ItemsRepeater<T>), " +
                    "and WinUI.ItemsView. FlipView stays carved.");
        }
    }

    // ── ItemsRepeater arm ─────────────────────────────────────────────────
    //
    // Spec 047 §14 Phase 3 finish — Engine (1).
    //
    // ItemsRepeater realizes through `IElementFactory.GetElement` /
    // `RecycleElement` rather than ContainerContentChanging, so this arm
    // does NOT install the shared CCC trampoline used by the ListViewBase
    // path. Instead it pulls a factory closure off the source object via
    // `IItemsRepeaterFactorySource`, sets `ir.ItemTemplate = factory`,
    // then drives Mount/Update through the same `ReactorListState` +
    // `KeyedListDiff` pipeline used by the lazy-stack path in
    // Reconciler.Update.cs (~3080).
    //
    // The source object MUST implement both IKeyedItemSource (read by the
    // shared `BuildListStateForKeyedSource` + `KeyedSourceKeyAdapter`
    // helpers) and IItemsRepeaterFactorySource (factory + layout knobs).
    // In practice it's the same element instance — `LazyStackElementBase`
    // for the descriptor port, the per-strategy closure adapter for a
    // future `ItemsRepeater<T>` descriptor that erases TItem at the
    // strategy level.
    private void BindItemsRepeaterErasedKeyedItems(
        WinUI.ItemsRepeater ir,
        IKeyedItemSource source,
        Action requestRerender,
        bool isMount)
    {
        if (source is not IItemsRepeaterFactorySource factorySource)
            throw new InvalidOperationException(
                $"TemplatedItemsErased<> on ItemsRepeater requires the source object to also implement {nameof(IItemsRepeaterFactorySource)}. " +
                $"Got: {source.GetType().FullName}. Lazy*Stack descriptor bases implement both interfaces; " +
                "a custom source needs the factory + layout-knob contract too.");

        factorySource.ConfigureLayout(ir);

        if (isMount)
        {
            var state = BuildListStateForKeyedSource(source);
            SetListState(ir, state);
            ir.ItemsSource = state.Source;
            var factory = factorySource.CreateFactory(this, requestRerender, _pool);
            factorySource.AttachListStateToFactory(factory, state);
            ir.ItemTemplate = factory;
            return;
        }

        // Update — match the legacy `Reconciler.Update.cs` lazy-stack
        // ordering: try to update the existing factory in place (avoids
        // re-realizing every visible row), then apply the keyed diff
        // through the host's stable list state, then refresh realized
        // containers against the new viewBuilder closure.
        if (ir.ItemTemplate is IElementFactory existingFactory && factorySource.TryUpdateFactory(existingFactory))
        {
            ApplyErasedKeyedDiffOrFallback(ir, source, factorySource, existingFactory);
            factorySource.RefreshRealizedItems(existingFactory, ir);
        }
        else
        {
            // Factory type mismatch (rare — only when the strategy's
            // erased-TItem changed under us) — full replacement.
            var fresh = BuildListStateForKeyedSource(source);
            SetListState(ir, fresh);
            ir.ItemsSource = fresh.Source;
            var factory = factorySource.CreateFactory(this, requestRerender, _pool);
            factorySource.AttachListStateToFactory(factory, fresh);
            ir.ItemTemplate = factory;
        }
    }

    /// <summary>
    /// Mirrors `ApplyLazyKeyedDiffOrFallback` from Reconciler.Update.cs
    /// but reads keys through the erased `IKeyedItemSource` instead of
    /// `LazyStackElementBase.GetKeyAt`. Bailout / fallback semantics are
    /// identical — `KeyedListDiff.Apply` resets `state.Source` in place
    /// on a duplicate/null-key bailout, so the existing ItemsSource
    /// binding stays valid.
    /// </summary>
    private void ApplyErasedKeyedDiffOrFallback(
        WinUI.ItemsRepeater ir,
        IKeyedItemSource source,
        IItemsRepeaterFactorySource factorySource,
        IElementFactory factory)
    {
        var state = GetListState(ir);
        if (state is null || !ReferenceEquals(ir.ItemsSource, state.Source))
        {
            var fresh = BuildListStateForKeyedSource(source);
            SetListState(ir, fresh);
            ir.ItemsSource = fresh.Source;
            factorySource.AttachListStateToFactory(factory, fresh);
            return;
        }

        var ambient = AnimationAmbient.Current;
        var keyAdapter = new KeyedSourceKeyAdapter(source);
        var stats = KeyedListDiff.Apply(
            state,
            keyAdapter,
            static (k, _) => k,
            _logger,
            ir.GetType().Name,
            ambient,
            controlInstance: ir);

        if (ambient is { HasEffect: true } && stats.MovedRows is { Count: > 0 } movedRows)
            ApplyMoveAnimationsRepeater(ir, movedRows, ambient.Kind);
    }

    private void BindItemsViewErasedKeyedItems(
        WinUI.ItemsView iv,
        IKeyedItemSource source,
        Action requestRerender,
        bool isMount)
    {
        if (source is not ItemsViewElementBase itemsViewSource)
            throw new InvalidOperationException(
                $"TemplatedItemsErased<> on ItemsView requires {nameof(ItemsViewElementBase)} as the source. " +
                $"Got: {source.GetType().FullName}.");

        itemsViewSource.PreflightFirstItem();

        if (isMount)
        {
            var state = BuildListStateForKeyedSource(source);
            SetListState(iv, state);
            iv.ItemsSource = state.Source;
            var factory = itemsViewSource.CreateFactory(this, requestRerender, _pool);
            itemsViewSource.AttachListStateToFactory(factory, state);
            iv.ItemTemplate = factory;
            return;
        }

        var repeater = iv.ScrollView?.Content as WinUI.ItemsRepeater;
        if (repeater is not null
            && repeater.ItemTemplate is IElementFactory existingFactory
            && itemsViewSource.TryUpdateFactory(existingFactory))
        {
            ApplyItemsViewErasedKeyedDiffOrFallback(iv, repeater, source, itemsViewSource, existingFactory);
            itemsViewSource.RefreshRealizedItems(existingFactory, repeater);
            return;
        }

        var fresh = BuildListStateForKeyedSource(source);
        SetListState(iv, fresh);
        iv.ItemsSource = fresh.Source;
        var replacementFactory = itemsViewSource.CreateFactory(this, requestRerender, _pool);
        itemsViewSource.AttachListStateToFactory(replacementFactory, fresh);
        iv.ItemTemplate = replacementFactory;
    }

    private void ApplyItemsViewErasedKeyedDiffOrFallback(
        WinUI.ItemsView iv,
        WinUI.ItemsRepeater repeater,
        IKeyedItemSource source,
        ItemsViewElementBase itemsViewSource,
        IElementFactory factory)
    {
        var state = GetListState(iv);
        if (state is null || !ReferenceEquals(iv.ItemsSource, state.Source))
        {
            var fresh = BuildListStateForKeyedSource(source);
            SetListState(iv, fresh);
            iv.ItemsSource = fresh.Source;
            itemsViewSource.AttachListStateToFactory(factory, fresh);
            return;
        }

        var ambient = AnimationAmbient.Current;
        var stats = KeyedListDiff.Apply(
            state,
            new KeyedSourceKeyAdapter(source),
            static (item, _) => item,
            _logger,
            iv.GetType().Name,
            ambient,
            controlInstance: repeater);

        if (ambient is { HasEffect: true } && stats.MovedRows is { Count: > 0 } movedRows)
            ApplyMoveAnimationsRepeater(repeater, movedRows, ambient.Kind);
    }

    private void BindListViewBaseErasedKeyedItems(
        WinUI.ListViewBase lvb,
        IKeyedItemSource source,
        Action requestRerender,
        bool isMount)
    {
        if (isMount)
        {
            lvb.ItemTemplate = SharedContentControlTemplate.Value;
            lvb.ContainerContentChanging += (sender, args) =>
                HandleTemplatedContainerContentChanging(sender, args, requestRerender);

            // §14 Phase 3 close-out: SelectionChanged + ItemClick wired
            // here once on Mount. Trampolines re-fetch the live element on
            // each fire via GetElementTag, so Update doesn't need to
            // re-subscribe. Mirrors the legacy MountTemplatedListView body
            // verbatim (ReactorRow.Index translation under the OC delta
            // path; int fallback for legacy non-OC consumers).
            lvb.SelectionChanged += (s, _) =>
            {
                var c = (WinUI.ListViewBase)s!;
                if (GetElementTag(c) is not TemplatedListElementBase tel) return;
                tel.InvokeSelectionChanged(c.SelectedIndex);
                if (tel.HasMultiSelectionCallback)
                {
                    var snapshot = new List<int>(c.SelectedItems.Count);
                    foreach (var item in c.SelectedItems)
                    {
                        if (item is ReactorRow row) snapshot.Add(row.Index);
                        else if (item is int i) snapshot.Add(i);
                    }
                    tel.InvokeMultiSelectionChanged(snapshot);
                }
            };
            // ItemClick is on Selector via the per-type events. ListView
            // and GridView expose it as a typed event; cast and subscribe
            // through the typed handler. ListViewBase itself doesn't carry
            // the click event so we split per concrete type.
            switch (lvb)
            {
                case WinUI.ListView lv:
                    lv.ItemClick += (s, args) =>
                    {
                        var l = (WinUI.ListView)s!;
                        int? idx = args.ClickedItem switch
                        {
                            ReactorRow row => row.Index,
                            int i => i,
                            _ => null,
                        };
                        if (idx is int v)
                            (GetElementTag(l) as TemplatedListElementBase)?.InvokeItemClick(v);
                    };
                    break;
                case WinUI.GridView gv:
                    gv.ItemClick += (s, args) =>
                    {
                        var g = (WinUI.GridView)s!;
                        int? idx = args.ClickedItem switch
                        {
                            ReactorRow row => row.Index,
                            int i => i,
                            _ => null,
                        };
                        if (idx is int v)
                            (GetElementTag(g) as TemplatedListElementBase)?.InvokeItemClick(v);
                    };
                    break;
            }

            var state = BuildListStateForKeyedSource(source);
            SetListState(lvb, state);
            lvb.ItemsSource = state.Source;
            return;
        }

        var existing = GetListState(lvb);
        if (existing is null || !ReferenceEquals(lvb.ItemsSource, existing.Source))
        {
            var fresh = BuildListStateForKeyedSource(source);
            SetListState(lvb, fresh);
            lvb.ItemsSource = fresh.Source;
        }
        else
        {
            var ambient = AnimationAmbient.Current;
            var keyAdapter = new KeyedSourceKeyAdapter(source);
            var stats = KeyedListDiff.Apply(
                existing,
                keyAdapter,
                static (k, _) => k,
                _logger,
                lvb.GetType().Name,
                ambient,
                controlInstance: lvb);

            if (ambient is { HasEffect: true } && stats.MovedRows is { Count: > 0 } movedRows)
            {
                for (int i = 0; i < movedRows.Count; i++)
                {
                    var container = lvb.ContainerFromIndex(movedRows[i].Index) as UIElement;
                    if (container is not null)
                        ApplyAmbientEnterAnimation(container, ambient.Kind);
                }
            }
        }

        RefreshRealizedContainers(lvb, source, requestRerender);
    }

    private static ReactorListState BuildListStateForKeyedSource(IKeyedItemSource source)
    {
        var state = new ReactorListState();
        int n = source.ItemCount;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, source.GetKeyAt(i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }

    /// <summary>
    /// Projects <see cref="IKeyedItemSource"/> as an <c>IReadOnlyList&lt;string&gt;</c>
    /// of keys so the keyed diff (which is generic on T) can consume it
    /// without materializing an array.
    /// </summary>
    private readonly struct KeyedSourceKeyAdapter : IReadOnlyList<string>
    {
        private readonly IKeyedItemSource _source;
        public KeyedSourceKeyAdapter(IKeyedItemSource source) => _source = source;
        public string this[int index] => _source.GetKeyAt(index);
        public int Count => _source.ItemCount;
        public IEnumerator<string> GetEnumerator()
        {
            for (int i = 0; i < _source.ItemCount; i++) yield return _source.GetKeyAt(i);
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private void BindListViewBaseKeyedItems<TItem>(
        WinUI.ListViewBase lvb,
        IReadOnlyList<TItem> items,
        Func<TItem, int, string> keySelector,
        Action requestRerender,
        bool isMount,
        IItemViewSource viewSource)
    {
        if (isMount)
        {
            // First mount — set up the realization plumbing. The handler
            // body is shared 1:1 with the legacy path so existing CCC
            // semantics (recycle teardown, attached-tag bookkeeping,
            // ambient-enter animation) come for free.
            lvb.ItemTemplate = SharedContentControlTemplate.Value;
            lvb.ContainerContentChanging += (sender, args) =>
                HandleTemplatedContainerContentChanging(sender, args, requestRerender);

            var state = BuildListStateForItems(items, keySelector);
            SetListState(lvb, state);
            lvb.ItemsSource = state.Source;
            return;
        }

        // Update — run the keyed diff. Mirrors `ApplyKeyedDiffOrFallback`
        // shape, but projects keys directly from the strategy lambdas
        // rather than through a `TemplatedKeyAdapter`.
        var existing = GetListState(lvb);
        if (existing is null || !ReferenceEquals(lvb.ItemsSource, existing.Source))
        {
            var fresh = BuildListStateForItems(items, keySelector);
            SetListState(lvb, fresh);
            lvb.ItemsSource = fresh.Source;
            // Refresh realized containers below so already-bound items pick
            // up new content even without a structural change.
        }
        else
        {
            var ambient = AnimationAmbient.Current;
            var keyAdapter = new ItemsKeyAdapter<TItem>(items, keySelector);
            var stats = KeyedListDiff.Apply(
                existing,
                keyAdapter,
                static (k, _) => k,
                _logger,
                lvb.GetType().Name,
                ambient,
                controlInstance: lvb);

            // Per-container offset animations for moved survivors. Insert
            // / Remove paths attach through realize/recycle so they need
            // no work here.
            if (ambient is { HasEffect: true } && stats.MovedRows is { Count: > 0 } movedRows)
            {
                for (int i = 0; i < movedRows.Count; i++)
                {
                    var container = lvb.ContainerFromIndex(movedRows[i].Index) as UIElement;
                    if (container is not null)
                        ApplyAmbientEnterAnimation(container, ambient.Kind);
                }
            }
        }

        RefreshRealizedContainers(lvb, viewSource, requestRerender);
    }

    private static ReactorListState BuildListStateForItems<TItem>(
        IReadOnlyList<TItem> items,
        Func<TItem, int, string> keySelector)
    {
        var state = new ReactorListState();
        int n = items.Count;
        var seeded = new (int Index, string Key)[n];
        for (int i = 0; i < n; i++)
            seeded[i] = (i, keySelector(items[i], i) ?? $"__null_{i}");
        state.Reset(seeded);
        return state;
    }

    /// <summary>
    /// Adapts a strategy-side <c>(items, keySelector)</c> pair to the
    /// <c>IReadOnlyList&lt;string&gt;</c> shape <see cref="KeyedListDiff.Apply{T}"/>
    /// expects. The adapter projects keys on demand to avoid materializing
    /// a string array up front — the diff's first pass already calls the
    /// selector once per index, so this is allocation-free in steady
    /// state.
    /// </summary>
    private readonly struct ItemsKeyAdapter<TItem> : IReadOnlyList<string>
    {
        private readonly IReadOnlyList<TItem> _items;
        private readonly Func<TItem, int, string> _keySelector;

        public ItemsKeyAdapter(IReadOnlyList<TItem> items, Func<TItem, int, string> keySelector)
        {
            _items = items;
            _keySelector = keySelector;
        }

        public string this[int index] => _keySelector(_items[index], index);
        public int Count => _items.Count;
        public IEnumerator<string> GetEnumerator()
        {
            for (int i = 0; i < _items.Count; i++) yield return this[i];
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Captures a strategy's <c>(items, buildItemView)</c> pair as an
    /// <see cref="IItemViewSource"/> so the shared realization machinery
    /// resolves "item N is this Element" without knowing whether the host
    /// is a legacy <see cref="TemplatedListElementBase"/> or a descriptor.
    /// </summary>
    private sealed class ClosureItemViewSource<TItem> : IItemViewSource
    {
        private readonly IReadOnlyList<TItem> _items;
        private readonly Func<TItem, int, Element> _buildItemView;

        public ClosureItemViewSource(IReadOnlyList<TItem> items, Func<TItem, int, Element> buildItemView)
        {
            _items = items;
            _buildItemView = buildItemView;
        }

        public int ItemCount => _items.Count;
        public Element BuildItemView(int index) => _buildItemView(_items[index], index);
    }
}
