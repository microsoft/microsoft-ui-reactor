using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;

// A third-party managed control may forward this list to a native ItemsRepeater.
// CsWinRT cannot see that indirect call site in the consuming application.
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<object>))]

namespace Microsoft.UI.Reactor.Core.V1Protocol;

internal sealed class ItemsHostState
{
    internal readonly record struct Item(object? Source, object? Value);
    internal List<Item> Items { get; set; } = new();
    internal IReadOnlyList<object>? Source { get; set; }
    internal int ElementCount { get; set; }
    internal bool HasElements => ElementCount != 0;
    internal bool AnyThemeSensitive { get; set; }
}

/// <summary>Shared flat-items lifecycle for descriptors and hand-coded handlers.</summary>
internal static class ItemsHostReconciliation
{
    internal static void Bind<TElement, TControl>(
        ItemsHost<TElement, TControl> strategy, TElement element, TControl control,
        Reconciler reconciler, Action requestRerender, bool isMount)
        where TElement : Element
        where TControl : UIElement
    {
        var state = Reconciler.GetOrCreateReactorState(control);
        IList<object>? collection = null;
        if (isMount)
        {
            // Hand-coded handlers dispatch children after the adapter's initial tag
            // pass, so callback-free hosts must be rooted here too.
            state.Element = element;
            collection = strategy.GetCollection(control);
            if (strategy.SetCollection is { } set && collection is not INotifyCollectionChanged)
            {
                collection = new ObservableCollection<object>();
                set(control, collection);
            }
            else if (collection.Count > 0)
                ClearSuppressed(control, collection);
            state.ItemsHostState = new ItemsHostState();
        }

        var itemsState = state.ItemsHostState
            ?? throw new InvalidOperationException("ItemsHost state is missing for a mounted control.");
        var previous = itemsState.Items;
        var source = strategy.GetItems(element);
        var equals = strategy.ItemEquals ?? object.Equals;
        // Raw values have no descendant state/context. Retain the legacy same-source no-op.
        if (!isMount && !itemsState.HasElements && ReferenceEquals(itemsState.Source, source))
            return;

        // Render inputs are immutable snapshots, as on the ordinary child-skip
        // paths. A retained source has no new closures or item props to refresh.
        // Still visit it for descendant state, context, theme or a forced render.
        if (!isMount && ReferenceEquals(itemsState.Source, source)
            && !itemsState.AnyThemeSensitive
            && !reconciler.ForceFullRenderActive
            && !reconciler.IsOnDirtyAncestorPath(control)
            && !HasChangedItemContext(itemsState, reconciler))
        {
            reconciler.DebugElementsSkipped += itemsState.ElementCount;
            return;
        }

        // String arrays cannot contain Element descendants. Keep their common
        // unchanged path separate so the general raw-edit loop stays small.
        if (!isMount && !itemsState.HasElements && strategy.ItemEquals is null
            && itemsState.Source is string[] oldStrings && source is string[] strings
            && oldStrings.AsSpan().SequenceEqual(strings))
        {
            itemsState.Source = source;
            return;
        }

        if (!isMount && !itemsState.HasElements &&
            TryUpdateRawInPlace(strategy, control, itemsState, source, equals))
            return;

        if (!isMount && MatchesInPlace(previous, source, equals))
        {
            bool anyThemeSensitive = false;
            int target = 0;
            for (int i = 0; i < source.Count; i++)
            {
                var old = previous[i];
                var item = source[i];
                var value = old.Value;
                if (item is Element child)
                {
                    anyThemeSensitive |= ChildDiffHints.IsThemeSensitive(child);
                    // Share the ordinary child-diff skip contract, not a blanket
                    // same-array skip: dirty/context-consuming components must still descend.
                    if (old.Source is Element oldChild && old.Value is UIElement existing
                        && Element.CanSkipUpdate(oldChild, child)
                        && !reconciler.ForceRenderThroughWrapper(child)
                        && !reconciler.IsOnDirtyAncestorPath(existing)
                        && !reconciler.ShouldDeclineSkip(existing))
                    {
                        reconciler.DebugElementsSkipped++;
                        if ((child.HasCallbacks
                                || Reconciler.HasGestureOrDragSlots(oldChild.Modifiers)
                                || Reconciler.HasGestureOrDragSlots(child.Modifiers)
                                || Reconciler.CallSiteChangedOnSkip(oldChild, child))
                            && existing is FrameworkElement fe)
                        {
                            Reconciler.RefreshSkippedChildState(fe, oldChild, child);
                        }
                    }
                    else
                        // ReconcileV1Child owns type replacement and Hot Reload migration.
                        value = reconciler.ReconcileV1Child(old.Source as Element, child,
                            old.Value as UIElement, requestRerender);
                    if (!ReferenceEquals(old.Value, value))
                    {
                        collection ??= strategy.GetCollection(control);
                        if (old.Value is null)
                        {
                            if (value is not null) collection.Insert(target, value);
                        }
                        else if (value is null)
                            collection.RemoveAt(target);
                        else
                            collection[target] = value;
                    }
                }
                // Positionally equal raw-only lists keep their old values, as before this change.
                previous[i] = new(item, value);
                if (value is not null) target++;
            }
            itemsState.Source = source;
            itemsState.AnyThemeSensitive = anyThemeSensitive;
            return;
        }

        collection ??= strategy.GetCollection(control);
        var used = new bool[previous.Count];
        var next = new List<ItemsHostState.Item>(source.Count);
        Dictionary<string, Queue<int>>? keyed = null;
        for (int i = 0; i < previous.Count; i++)
        {
            if (previous[i].Source is Element { Key: { } key })
            {
                keyed ??= new(StringComparer.Ordinal);
                if (!keyed.TryGetValue(key, out var indices))
                    keyed[key] = indices = new Queue<int>();
                indices.Enqueue(i);
            }
        }

        int elementCount = 0;
        bool themeSensitive = false;
        for (int i = 0; i < source.Count; i++)
        {
            var item = source[i];
            int match = -1;
            if (item is Element child)
            {
                elementCount++;
                themeSensitive |= ChildDiffHints.IsThemeSensitive(child);
                if (child.Key is { } key)
                {
                    if (keyed is not null && keyed.TryGetValue(key, out var indices) && indices.Count > 0)
                        match = indices.Dequeue();
                }
                else if (i < previous.Count && !used[i] && previous[i].Source is Element { Key: null })
                    match = i;

                var old = match < 0 ? default : previous[match];
                if (match >= 0) used[match] = true;
                var existing = old.Value as UIElement;
                var mounted = reconciler.ReconcileV1Child(old.Source as Element, child, existing, requestRerender);
                if (!ReferenceEquals(existing, mounted)) RemoveReference(collection, existing);
                next.Add(new(item, mounted));
            }
            else
            {
                if (i < previous.Count && !used[i] && previous[i].Source is not Element
                    && equals(previous[i].Source, item))
                    match = i;
                else
                    for (int j = 0; j < previous.Count; j++)
                        if (!used[j] && previous[j].Source is not Element && equals(previous[j].Source, item))
                        {
                            match = j;
                            break;
                        }
                if (match >= 0)
                {
                    used[match] = true;
                    // Retain equal values (and selection), but refresh changed Id-equal payloads.
                    var retained = previous[match].Value;
                    if (object.Equals(retained, item))
                    {
                        next.Add(new(item, retained));
                        continue;
                    }
                    RemoveReference(collection, retained);
                }
                next.Add(new(item, item));
            }
        }

        for (int i = 0; i < previous.Count; i++)
        {
            if (used[i]) continue;
            var old = previous[i];
            // Teardown must see a parented child (e.g. connected-animation snapshots).
            if (old.Source is Element && old.Value is UIElement child)
                reconciler.UnmountChild(child);
            RemoveReference(collection, old.Value);
        }

        int placement = 0;
        foreach (var item in next)
        {
            if (item.Value is not { } value) continue;
            if (placement < collection.Count && ReferenceEquals(collection[placement], value))
            {
                placement++;
                continue;
            }
            int current = IndexOfReference(collection, value, placement);
            if (current < 0)
                collection.Insert(placement, value);
            else if (collection is ObservableCollection<object> observable)
                observable.Move(current, placement);
            else
            {
                collection.RemoveAt(current);
                collection.Insert(placement, value);
            }
            placement++;
        }
        itemsState.Items = next;
        itemsState.Source = source;
        itemsState.ElementCount = elementCount;
        itemsState.AnyThemeSensitive = themeSensitive;
    }

    private static bool HasChangedItemContext(ItemsHostState state, Reconciler reconciler)
    {
        if (!reconciler.HasActiveContextValues) return false;
        // ItemsControl and third-party hosts need not expose their items as
        // ordinary content children. Query the tracked roots, not the host's
        // visual subtree, so unexpanded/unrealized items are covered as well.
        foreach (var item in state.Items)
            if (item is { Source: Element, Value: UIElement child }
                && reconciler.ShouldDeclineSkip(child))
                return true;
        return false;
    }

    private static bool TryUpdateRawInPlace<TElement, TControl>(
        ItemsHost<TElement, TControl> strategy, TControl control, ItemsHostState state,
        IReadOnlyList<object> source, Func<object?, object?, bool> equals)
        where TElement : Element
        where TControl : UIElement
    {
        int count = source.Count;
        var previous = state.Items;
        if (previous.Count != count) return false;
        bool same = RawItemsEqual(state.Source!, source, equals);
        if (!same)
        {
            for (int i = 0; i < count; i++)
                if (source[i] is Element) return false;
            var collection = strategy.GetCollection(control);
            int target = 0;
            for (int i = 0; i < count; i++)
            {
                var old = previous[i].Value;
                var item = source[i];
                // Immutable equal values need no native write. Id-equal objects whose
                // payload changed still replace the old instance on a changed list.
                var value = object.Equals(old, item) ? old : item;
                if (!ReferenceEquals(old, value))
                {
                    if (old is null)
                    {
                        if (value is not null) collection.Insert(target, value);
                    }
                    else if (value is null)
                        collection.RemoveAt(target);
                    else
                        collection[target] = value;
                }
                previous[i] = new(item, value);
                if (value is not null) target++;
            }
        }
        // Equal raw lists have no descendants, allocations or native collection reads.
        state.Source = source;
        return true;
    }

    private static bool RawItemsEqual(IReadOnlyList<object> oldSource,
        IReadOnlyList<object> source, Func<object?, object?, bool> equals)
    {
        int count = source.Count;
        // Arrays are the generated factory's common input. Avoid covariant
        // IReadOnlyList interface dispatch for each raw value on this hot path.
        if (oldSource is object[] oldArray && source is object[] array)
        {
            for (int i = 0; i < count; i++)
                if (array[i] is Element || !equals(oldArray[i], array[i])) return false;
        }
        else
            for (int i = 0; i < count; i++)
                if (source[i] is Element || !equals(oldSource[i], source[i])) return false;
        return true;
    }

    private static bool MatchesInPlace(List<ItemsHostState.Item> previous, IReadOnlyList<object> source,
        Func<object?, object?, bool> equals)
    {
        if (previous.Count != source.Count) return false;
        for (int i = 0; i < source.Count; i++)
        {
            var old = previous[i].Source;
            if (source[i] is Element child)
            {
                if (old is not Element oldChild || oldChild.Key != child.Key) return false;
            }
            else if (old is Element || !equals(old, source[i]))
                return false;
        }
        return true;
    }

    internal static void Unbind<TElement, TControl>(
        ItemsHost<TElement, TControl> strategy, TControl control, Reconciler reconciler)
        where TElement : Element
        where TControl : UIElement
    {
        var state = Reconciler.GetOrCreateReactorState(control);
        if (state.ItemsHostState is not { } items) return;
        state.ItemsHostState = null;
        for (int i = 0; i < items.Items.Count; i++)
            if (items.Items[i] is { Source: Element, Value: UIElement child })
                reconciler.UnmountChild(child);
        // Children and property-owned slots have already received teardown while
        // parented. Plain generated events read this tag rather than echo state.
        state.Element = null;
        ClearSuppressed(control, strategy.GetCollection(control));
    }

    private static void ClearSuppressed(UIElement control, IList<object> collection)
    {
        // Terminal cleanup is not a user selection. Use the same scoped suppression
        // as ApplySetters, with no one-shot token left to swallow a future event.
        var state = Reconciler.GetOrCreateReactorState(control);
        state.EchoSuppressScopeDepth++;
        try { collection.Clear(); }
        finally { state.EchoSuppressScopeDepth--; }
    }

    private static int IndexOfReference(IList<object> collection, object value, int start)
    {
        for (int i = start; i < collection.Count; i++)
            if (ReferenceEquals(collection[i], value)) return i;
        return -1;
    }

    private static void RemoveReference(IList<object> collection, object? value)
    {
        if (value is null) return;
        int index = IndexOfReference(collection, value, 0);
        if (index >= 0) collection.RemoveAt(index);
    }
}
