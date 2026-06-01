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

    // <snippet:adapter-mount>
    public UIElement Mount(Element element, Action requestRerender, Reconciler reconciler)
    {
        var typedEl = (TElement)element;
        var ctx = new MountContext(reconciler, requestRerender);
        var control = _handler.Mount(ctx, typedEl);

        // Anchor element identity on the control via the attached state DP so
        // event trampolines can re-fetch the live element on each fire.
        // Gated: callback-free leaves never dispatch into Reactor code, so we
        // skip the ReactorState allocation for them (§4.4 follow-up).
        if (control is FrameworkElement fe)
            Reconciler.SetElementTagIfNeeded(fe, typedEl);

        // Strategy dispatch — only when the handler declares a non-None Children strategy.
        var strategy = _handler.Children;
        if (strategy is not null)
            DispatchChildrenMount(strategy, ctx, typedEl, control);

        // Post-children mount hook. Fires after every child has mounted
        // (whether via the strategy switch above or an items-binder strategy
        // the handler dispatched inline before returning). Lets handlers
        // subscribe events that must wire after children-add. Default no-op
        // for handlers that don't override it.
        _handler.AfterChildrenMount(ctx, typedEl, control);

        return control;
    }
    // </snippet:adapter-mount>

    public UIElement Update(Element oldEl, Element newEl, UIElement control, Action requestRerender, Reconciler reconciler)
    {
        var typedOld = (TElement)oldEl;
        var typedNew = (TElement)newEl;
        var typedControl = (TControl)control;
        var ctx = new UpdateContext(reconciler, requestRerender);
        _handler.Update(ctx, typedOld, typedNew, typedControl);

        if (control is FrameworkElement fe)
            Reconciler.SetElementTagIfNeeded(fe, typedNew);

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

        // Spec 047 §14 — panel-strategy handlers do NOT own child teardown
        // (their Unmount is a no-op; descriptors never recurse into children).
        // Returning ContinueDefaultTraversal lets the engine's existing
        // `control is WinUI.Panel` recursion run in BOTH unmount paths
        // (UnmountRecursive + UnmountAndCollect), tearing down + pooling each
        // child — byte-identical to the legacy decorator panel handlers, which
        // also return ContinueDefaultTraversal. Without this, CollectSelf would
        // early-return before the panel-child recursion and leak child Component
        // effect cleanups.
        if (_handler.ChildrenForUnmount is Panel<TElement, TControl>)
            return V1UnmountDisposition.ContinueDefaultTraversal;

        // Issue #375 — strategy-driven child teardown on Unmount. Without
        // this, a Component nested under a SingleContent / NamedSlots /
        // ItemsHost / IItemsBinderStrategy parent (e.g. Border, SplitView,
        // ListBox, TabView) leaks its UseEffect cleanups when the parent
        // unmounts: the engine's V1 arm early-returns on CollectSelf before
        // the legacy `border.Child` / `cc.Content` recursion can reach the
        // component wrapper Border that anchors the component-node lookup
        // in _componentNodes. Walk the strategy's live children and run
        // UnmountChild on each — the recursive UnmountRecursive call
        // surfaces every nested component wrapper and fires RunCleanups.
        //
        // We consult ChildrenForUnmount (not Children) because descriptor
        // handlers hide ItemsHost / IItemsBinderStrategy strategies from
        // Children (they dispatch those inline during Mount/Update so the
        // collection is populated before SelectedIndex initial writes
        // land); on Unmount the ordering constraint doesn't apply, so the
        // descriptor exposes the real strategy here for the teardown walk.
        var strategy = _handler.ChildrenForUnmount;
        if (strategy is not null)
            DispatchChildrenUnmount(strategy, reconciler, typedControl);

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
                // Spec 047 §14 — keyed reconcile via spec-042's ChildReconciler,
                // mirroring the legacy hand-coded panel Update* methods exactly
                // (e.g. UpdateFlex: ReconcileChildren then a physical-index
                // post-pass that re-applies attached props). This preserves
                // WinUI control identity across keyed reorder/reverse/swap/
                // remove-middle — the gap the legacy decorator handlers existed
                // to cover.
                var collection = panel.GetCollection(control);
                var newList = panel.GetChildren(newEl);
                var oldList = panel.GetChildren(oldEl);
                // ChildReconciler.Reconcile takes Element[]; avoid a copy when
                // the descriptor already exposes the backing array.
                var newChildren = newList as Element[] ?? global::System.Linq.Enumerable.ToArray(newList);
                var oldChildren = oldList as Element[] ?? global::System.Linq.Enumerable.ToArray(oldList);

                reconciler.ReconcilePanelChildrenInto(oldChildren, newChildren, collection, requestRerender);

                // Re-apply per-child attached props by walking filtered-new
                // children (null / EmptyElement skipped) in lockstep with the
                // live collection — ChildReconciler leaves the collection in
                // filtered-new order. Identical to the legacy Update* post-pass.
                var attached = panel.PerChildAttached;
                var afterAll = panel.PerChildAttachedAfterAll;
                if (attached is not null || afterAll is not null)
                {
                    var pairs = afterAll is null
                        ? null
                        : new List<(UIElement, Element)>(newChildren.Length);
                    int panelIdx = 0;
                    for (int i = 0; i < newChildren.Length && panelIdx < collection.Count; i++)
                    {
                        var childEl = newChildren[i];
                        if (childEl is null or EmptyElement) continue;
                        var live = collection[panelIdx];
                        attached?.Invoke(control, live, childEl);
                        pairs?.Add((live, childEl));
                        panelIdx++;
                    }
                    // pairs is non-null exactly when afterAll is non-null.
                    if (afterAll is not null)
                        afterAll(control, pairs!);
                }
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

    // ── Unmount strategy dispatch (issue #375) ───────────────────────
    //
    // When a parent control is unmounted, walk its children strategy and
    // tear down the live child(ren) so descendant Component UseEffect
    // cleanups run. Before this dispatch existed, only Panel strategy
    // was covered (via ContinueDefaultTraversal + the legacy
    // `control is WinUI.Panel` recursion); SingleContent / NamedSlots /
    // ItemsHost / IItemsBinderStrategy parents silently leaked any
    // nested component cleanups because CollectSelf early-returned in
    // the engine's V1 arm before the legacy `border.Child` / `cc.Content`
    // recursion could reach the inner component wrapper.
    //
    // The teardown delegates to <c>reconciler.UnmountChild</c> (which
    // runs the full <c>UnmountRecursive</c> path on each child), so any
    // depth of nesting is handled — including a component nested deep
    // under multiple Border / SplitView / TabView layers.
    private static void DispatchChildrenUnmount(
        ChildrenStrategy<TElement, TControl> strategy,
        Reconciler reconciler, TControl control)
    {
        // IItemsBinderStrategy covers templated lists (keyed item realization
        // owned by the reconciler-side BindKeyedItemsSource pipeline) plus
        // TabItemsHost / PreMountedItems (positional containers whose
        // realized content lives in TabViewItem / PivotItem / FlipViewItem
        // ContentControl slots). Keyed-templated strategies tear down their
        // realized containers via the reconciler-side pipeline; the
        // positional binder strategies need explicit per-container teardown.
        //
        // Issue #375 — dispatch through the strategy's own Unbind hook
        // rather than iterating ItemsControl.Items. TabView populates
        // TabItems (not Items), so a blanket .Items walk would miss tabs
        // entirely. Each concrete strategy reaches its real collection via
        // GetCollection (TabView.TabItems / FlipView.Items / etc).
        if (strategy is IItemsBinderStrategy binder)
        {
            if (strategy is ITemplatedItemsStrategy or IErasedTemplatedItemsStrategy)
            {
                // Keyed templated strategies — reconciler-side teardown of
                // the realization channel runs out-of-band on container
                // recycle; nothing to do here.
                return;
            }
            if (control is FrameworkElement fe)
                binder.Unbind(fe, reconciler);
            return;
        }

        switch (strategy)
        {
            case None<TElement, TControl>:
                return;

            case SingleContent<TElement, TControl> single:
            {
                if (single.GetCurrentChild is { } getCur
                    && getCur(control) is UIElement child)
                {
                    reconciler.UnmountChild(child);
                }
                return;
            }

            case NamedSlots<TElement, TControl> ns:
            {
                for (int i = 0; i < ns.Slots.Count; i++)
                {
                    var slot = ns.Slots[i];
                    if (slot.GetCurrentChild is { } getCur
                        && getCur(control) is UIElement child)
                    {
                        reconciler.UnmountChild(child);
                    }
                }
                return;
            }

            case ItemsHost<TElement, TControl> ih:
            {
                var collection = ih.GetCollection(control);
                // Walk top-down so we don't surprise an inner unmount with
                // a sibling already collected by the strategy's flat sink.
                for (int i = 0; i < collection.Count; i++)
                {
                    if (collection[i] is UIElement childCtrl)
                        reconciler.UnmountChild(childCtrl);
                }
                return;
            }

            case Imperative<TElement, TControl>:
                // The handler owns reconciliation and child identity for
                // an Imperative strategy; its own Unmount body is
                // responsible for teardown (no generic walk possible
                // because the engine has no view onto the slots).
                return;
        }
    }
}
