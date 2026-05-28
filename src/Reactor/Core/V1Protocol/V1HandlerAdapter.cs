using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Spec 047 §14 Phase 1 (1.6) — adapter that bridges
/// <see cref="IElementHandler{TElement,TControl}"/> to the type-erased
/// <see cref="IV1HandlerEntry"/> dispatch table on
/// <see cref="V1HandlerRegistry"/>. Closes over a single
/// <c>IElementHandler&lt;TElement,TControl&gt;</c> instance; downcasts at
/// the dispatch boundary so the hot path is dictionary lookup + interface
/// call + cast (the cast is JIT-folded for monomorphic call sites).
/// </summary>
internal sealed class V1HandlerAdapter<TElement, TControl> : IV1HandlerEntry
    where TElement : Element
    where TControl : UIElement
{
    private readonly IElementHandler<TElement, TControl> _handler;

    public V1HandlerAdapter(IElementHandler<TElement, TControl> handler)
    {
        _handler = handler;
    }

    public bool HasUnmount => true; // the default-body call is cheap; no point branching.

    public UIElement Mount(Element element, Action requestRerender, Reconciler reconciler)
    {
        var typedEl = (TElement)element;
        var ctx = new MountContext(reconciler, requestRerender);
        var control = _handler.Mount(ctx, typedEl);

        // Anchor element identity on the control via the attached state DP so
        // event trampolines can re-fetch the live element on each fire.
        if (control is FrameworkElement fe)
            Reconciler.SetElementTag(fe, typedEl);

        // Strategy dispatch — only when the handler declares a non-None Children strategy.
        var strategy = _handler.Children;
        if (strategy is not null)
            DispatchChildrenMount(strategy, ctx, typedEl, control);

        return control;
    }

    public UIElement Update(Element oldEl, Element newEl, UIElement control, Action requestRerender, Reconciler reconciler)
    {
        var typedOld = (TElement)oldEl;
        var typedNew = (TElement)newEl;
        var typedControl = (TControl)control;
        var ctx = new UpdateContext(reconciler, requestRerender);
        _handler.Update(ctx, typedOld, typedNew, typedControl);

        if (control is FrameworkElement fe)
            Reconciler.SetElementTag(fe, typedNew);

        var strategy = _handler.Children;
        if (strategy is not null)
            DispatchChildrenUpdate(strategy, reconciler, requestRerender, typedOld, typedNew, typedControl);

        // §13 Q12 — standard handlers never substitute the control on
        // update; identity is preserved across updates.
        return control;
    }

    public V1UnmountDisposition Unmount(UIElement control, Reconciler reconciler)
    {
        var typedControl = (TControl)control;
        var ctx = new UnmountContext(reconciler);
        _handler.Unmount(ctx, typedControl);
        // Standard handlers always opt into pool collection — matches
        // the pre-Phase-3-completion behavior.
        return V1UnmountDisposition.CollectSelf;
    }

    // ── Strategy dispatch ────────────────────────────────────────────

    private static void DispatchChildrenMount(
        ChildrenStrategy<TElement, TControl> strategy,
        MountContext ctx, TElement element, TControl control)
    {
        // §14 Phase 3 finish — consolidated dispatch arm. Every items-
        // binder strategy variant (templated/erased today; tree/tab/pivot
        // when they arrive) implements IItemsBinderStrategy, so the cost
        // here is one is-check + one interface call. Reconciler.BindKeyed*
        // owns the realization plumbing (ReactorListState + KeyedListDiff
        // + per-control realization channel).
        if (strategy is IItemsBinderStrategy binder && control is FrameworkElement feBinder)
        {
            binder.Bind(feBinder, oldElement: null, element, ctx.Reconciler, ctx.RequestRerender, isMount: true);
            return;
        }
        switch (strategy)
        {
            case None<TElement, TControl>:
                return;

            case SingleContent<TElement, TControl> single:
            {
                var child = single.GetChild(element);
                var mounted = child is null ? null : ctx.MountChild(child);
                single.SetChild(control, mounted);
                return;
            }

            case Panel<TElement, TControl> panel:
            {
                var collection = panel.GetCollection(control);
                var children = panel.GetChildren(element);
                var attached = panel.PerChildAttached;
                var afterAll = panel.PerChildAttachedAfterAll;
                var pairs = afterAll is null
                    ? null
                    : new List<(UIElement, Element)>(children.Count);
                // Phase 1: append-only (no keyed reconcile). Phase 3 integrates with spec-042.
                for (int i = 0; i < children.Count; i++)
                {
                    var childEl = children[i];
                    var mounted = ctx.MountChild(childEl);
                    if (mounted is not null)
                    {
                        collection.Add(mounted);
                        attached?.Invoke(control, mounted, childEl);
                        pairs?.Add((mounted, childEl));
                    }
                }
                // pairs is non-null exactly when afterAll is non-null (see
                // the conditional allocation above), so the afterAll guard
                // alone is sufficient.
                if (afterAll is not null)
                    afterAll(control, pairs!);
                return;
            }

            case NamedSlots<TElement, TControl> ns:
            {
                for (int i = 0; i < ns.Slots.Count; i++)
                {
                    var slot = ns.Slots[i];
                    var childEl = slot.GetChild(element);
                    var mounted = childEl is null ? null : ctx.MountChild(childEl);
                    slot.SetChild(control, mounted);
                }
                return;
            }

            case Imperative<TElement, TControl> imp:
                imp.Reconcile(ctx, element, element, control);
                return;

            case ItemsHost<TElement, TControl> ih:
            {
                // §14 Phase 3-final: ItemsHost dispatches a flat items
                // collection rebuild for descriptor authors of items-host
                // controls (ListBox / ComboBox.Items / RadioButtons items).
                // Typed templated lists (ListView<T>, GridView<T>, etc.) keep
                // their own delegate-body handlers in Batch G2 with spec-042
                // keyed reconciliation; ItemsHost is for the flat case only.
                //
                // Note: descriptors hit this only via hand-coded handlers —
                // DescriptorHandler interleaves ItemsHost dispatch between
                // RentControl and the prop loop so initial writes like
                // SelectedIndex see a populated collection.
                var newItems = ih.GetItems(element);
                var collection = ih.GetCollection(control);
                if (collection.Count > 0) collection.Clear();
                for (int i = 0; i < newItems.Count; i++)
                {
                    var item = newItems[i];
                    if (item is Element childEl)
                    {
                        var mounted = ctx.MountChild(childEl);
                        if (mounted is not null) collection.Add(mounted);
                    }
                    else collection.Add(item);
                }
                return;
            }
        }
    }

    private static void DispatchChildrenUpdate(
        ChildrenStrategy<TElement, TControl> strategy,
        Reconciler reconciler, Action requestRerender,
        TElement oldEl, TElement newEl, TControl control)
    {
        // §14 Phase 3 finish — consolidated dispatch arm (see Mount path).
        // isMount: false so the bind runs the keyed-diff branch.
        if (strategy is IItemsBinderStrategy binder && control is FrameworkElement feBinder)
        {
            binder.Bind(feBinder, oldEl, newEl, reconciler, requestRerender, isMount: false);
            return;
        }
        switch (strategy)
        {
            case None<TElement, TControl>:
                return;

            case SingleContent<TElement, TControl> single:
            {
                // Structural reconcile against the existing slot — keeps
                // descendant component state across re-renders. When the
                // strategy doesn't expose GetCurrentChild we fall back to
                // remount (the original Phase 1 behavior), which is correct
                // but discards descendant state.
                var oldChild = single.GetChild(oldEl);
                var newChild = single.GetChild(newEl);
                if (single.GetCurrentChild is { } getCur)
                {
                    var existing = getCur(control);
                    var next = reconciler.ReconcileV1Child(oldChild, newChild, existing, requestRerender);
                    if (!ReferenceEquals(existing, next))
                        single.SetChild(control, next);
                }
                else if (!ReferenceEquals(oldChild, newChild))
                {
                    var mounted = newChild is null ? null : reconciler.Mount(newChild, requestRerender);
                    single.SetChild(control, mounted);
                }
                return;
            }

            case Panel<TElement, TControl> panel:
            {
                // Phase 1 limitation: structural diff per slot, but no
                // keyed-list reconciliation. Phase 3 integrates with
                // spec-042's ChildReconciler. Until then we walk by index
                // and reuse-or-replace at each slot — preserves descendant
                // state across reorderings that happen by index but not
                // across keyed moves.
                var collection = panel.GetCollection(control);
                var newChildren = panel.GetChildren(newEl);
                var oldChildren = panel.GetChildren(oldEl);
                var attached = panel.PerChildAttached;
                var afterAll = panel.PerChildAttachedAfterAll;
                var pairs = afterAll is null
                    ? null
                    : new List<(UIElement, Element)>(newChildren.Count);
                int oldCount = oldChildren.Count;
                int newCount = newChildren.Count;
                int common = global::System.Math.Min(oldCount, newCount);
                int slot = 0;
                for (int i = 0; i < common; i++)
                {
                    var existing = slot < collection.Count ? collection[slot] : null;
                    var next = reconciler.ReconcileV1Child(oldChildren[i], newChildren[i], existing, requestRerender);
                    if (next is null)
                    {
                        if (existing is not null) collection.RemoveAt(slot);
                    }
                    else if (existing is null)
                    {
                        collection.Insert(slot, next);
                        attached?.Invoke(control, next, newChildren[i]);
                        pairs?.Add((next, newChildren[i]));
                        slot++;
                    }
                    else if (!ReferenceEquals(existing, next))
                    {
                        collection[slot] = next;
                        attached?.Invoke(control, next, newChildren[i]);
                        pairs?.Add((next, newChildren[i]));
                        slot++;
                    }
                    else
                    {
                        // Same UIElement instance survived — re-apply attached
                        // props in case the child element's hints changed
                        // (e.g. Grid.Row swapped between two existing rows).
                        attached?.Invoke(control, next, newChildren[i]);
                        pairs?.Add((next, newChildren[i]));
                        slot++;
                    }
                }
                // Trailing removals — reconcile against null newChild to
                // route through the same Unmount path used by SingleContent.
                for (int i = common; i < oldCount; i++)
                {
                    var existing = slot < collection.Count ? collection[slot] : null;
                    reconciler.ReconcileV1Child(oldChildren[i], null, existing, requestRerender);
                    if (slot < collection.Count) collection.RemoveAt(slot);
                }
                // Trailing additions.
                for (int i = common; i < newCount; i++)
                {
                    var mounted = reconciler.Mount(newChildren[i], requestRerender);
                    if (mounted is not null)
                    {
                        collection.Add(mounted);
                        attached?.Invoke(control, mounted, newChildren[i]);
                        pairs?.Add((mounted, newChildren[i]));
                    }
                }
                // pairs is non-null exactly when afterAll is non-null (see
                // the conditional allocation above), so the afterAll guard
                // alone is sufficient.
                if (afterAll is not null)
                    afterAll(control, pairs!);
                return;
            }

            case NamedSlots<TElement, TControl> ns:
            {
                for (int i = 0; i < ns.Slots.Count; i++)
                {
                    var slot = ns.Slots[i];
                    var oldChild = slot.GetChild(oldEl);
                    var newChild = slot.GetChild(newEl);
                    if (slot.GetCurrentChild is { } getCur)
                    {
                        var existing = getCur(control);
                        var next = reconciler.ReconcileV1Child(oldChild, newChild, existing, requestRerender);
                        if (!ReferenceEquals(existing, next))
                            slot.SetChild(control, next);
                    }
                    else if (!ReferenceEquals(oldChild, newChild))
                    {
                        var mounted = newChild is null ? null : reconciler.Mount(newChild, requestRerender);
                        slot.SetChild(control, mounted);
                    }
                }
                return;
            }

            case Imperative<TElement, TControl> imp:
                imp.Reconcile(new MountContext(reconciler, requestRerender), oldEl, newEl, control);
                return;

            case ItemsHost<TElement, TControl> ih:
            {
                // §14 Phase 3-final: positional rebuild gated by ItemEquals.
                var oldItems = ih.GetItems(oldEl);
                var newItems = ih.GetItems(newEl);
                var equals = ih.ItemEquals ?? object.Equals;
                if (ReferenceEquals(oldItems, newItems)) return;
                if (oldItems.Count == newItems.Count)
                {
                    bool same = true;
                    for (int i = 0; i < newItems.Count; i++)
                    {
                        if (!equals(oldItems[i], newItems[i])) { same = false; break; }
                    }
                    if (same) return;
                }
                // Structural change — rebuild. Element items are unmounted via
                // the existing UnmountChild path (ReconcileV1Child(old, null, ...))
                // walks them; strings are dropped directly.
                var collection = ih.GetCollection(control);
                var ctx = new MountContext(reconciler, requestRerender);
                for (int i = 0; i < oldItems.Count; i++)
                {
                    if (oldItems[i] is Element oldChild)
                        reconciler.ReconcileV1Child(oldChild, null, null, requestRerender);
                }
                if (collection.Count > 0) collection.Clear();
                for (int i = 0; i < newItems.Count; i++)
                {
                    var item = newItems[i];
                    if (item is Element childEl)
                    {
                        var mounted = ctx.MountChild(childEl);
                        if (mounted is not null) collection.Add(mounted);
                    }
                    else collection.Add(item);
                }
                return;
            }
        }
    }
}
