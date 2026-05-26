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

    public void Update(Element oldEl, Element newEl, UIElement control, Action requestRerender, Reconciler reconciler)
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
    }

    public void Unmount(UIElement control, Reconciler reconciler)
    {
        var typedControl = (TControl)control;
        var ctx = new UnmountContext(reconciler);
        _handler.Unmount(ctx, typedControl);
    }

    // ── Strategy dispatch ────────────────────────────────────────────

    private static void DispatchChildrenMount(
        ChildrenStrategy<TElement, TControl> strategy,
        MountContext ctx, TElement element, TControl control)
    {
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
                // Phase 1: append-only (no keyed reconcile). Phase 3 integrates with spec-042.
                for (int i = 0; i < children.Count; i++)
                {
                    var mounted = ctx.MountChild(children[i]);
                    if (mounted is not null)
                        collection.Add(mounted);
                }
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

            case ItemsHost<TElement, TControl>:
                // Spec 047 §6: ItemsHost in v1 is a thin shape wrapper. The
                // actual reconciliation goes through ChildReconciler (spec
                // 042); see the ListView port in 1.15. Strategy is for
                // shape-binding only — no-op on mount.
                return;
        }
    }

    private static void DispatchChildrenUpdate(
        ChildrenStrategy<TElement, TControl> strategy,
        Reconciler reconciler, Action requestRerender,
        TElement oldEl, TElement newEl, TControl control)
    {
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
                        slot++;
                    }
                    else if (!ReferenceEquals(existing, next))
                    {
                        collection[slot] = next;
                        slot++;
                    }
                    else slot++;
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
                    if (mounted is not null) collection.Add(mounted);
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

            case ItemsHost<TElement, TControl>:
                // No-op in Phase 1 (see DispatchChildrenMount).
                return;
        }
    }
}
