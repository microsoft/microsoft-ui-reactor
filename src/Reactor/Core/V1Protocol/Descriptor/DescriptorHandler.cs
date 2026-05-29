using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

/// <summary>
/// Spec 047 §6 / §14 Phase 2 (Q1 spike) — interpreter that drives a
/// <see cref="ControlDescriptor{TElement,TControl}"/> against the v1
/// protocol surface.
///
/// <para>Implemented as a regular
/// <see cref="IElementHandler{TElement,TControl}"/> so the
/// <see cref="V1HandlerAdapter{TElement,TControl}"/> dispatch shell is
/// identical between the descriptor and hand-coded handler paths — the only
/// thing that differs between the Q1 A|B variants is the body of Mount /
/// Update. Any measured delta is the interpreter's tax, not a different
/// dispatch shape.</para>
///
/// <para><b>Mount sequence</b> (matches the hand-coded handlers' KD-1b
/// ordering):
/// <list type="number">
///   <item>Rent the control via <see cref="MountContext.RentControl{T}"/>.</item>
///   <item>Iterate <see cref="ControlDescriptor{TElement,TControl}.Properties"/>
///   and invoke <see cref="PropEntry{TElement,TControl}.Mount"/> on each —
///   all bare initial writes happen first.</item>
///   <item>Iterate again and invoke
///   <see cref="PropEntry{TElement,TControl}.EnsureSubscribed"/> — controlled
///   entries wire their change-event trampolines now (no echo on the just-
///   written values because nothing is listening yet).</item>
///   <item>Apply setters.</item>
/// </list></para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed class DescriptorHandler<TElement, TControl> : IElementHandler<TElement, TControl>
    where TElement : Element
    where TControl : FrameworkElement, new()
{
    private readonly ControlDescriptor<TElement, TControl> _descriptor;

    public DescriptorHandler(ControlDescriptor<TElement, TControl> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _descriptor = descriptor;
    }

    /// <summary>The descriptor this handler interprets. Exposed for tests
    /// and bench harnesses — not part of the steady-state author API.</summary>
    public ControlDescriptor<TElement, TControl> Descriptor => _descriptor;

    /// <summary>
    /// Children strategy surfaced to <see cref="V1HandlerAdapter{TElement,TControl}"/>.
    /// Returns the descriptor's strategy except when it's an
    /// <see cref="ItemsHost{TElement,TControl}"/> — that one is dispatched
    /// inline by <see cref="Mount"/> / <see cref="Update"/> before the prop
    /// loop runs so initial writes like <c>SelectedIndex</c> land against a
    /// populated collection (matches legacy mount ordering).
    /// </summary>
    public ChildrenStrategy<TElement, TControl>? Children =>
        _descriptor.Children switch
        {
            ItemsHost<TElement, TControl> => null,
            // §14 Phase 3 finish — every items-binder strategy (templated /
            // erased today; tree / tab / pivot when they arrive) implements
            // IItemsBinderStrategy and needs the same "bind-before-props"
            // ordering as ItemsHost — SelectedIndex initial writes need a
            // populated ItemsSource; otherwise WinUI silently clamps against
            // the empty collection.
            IItemsBinderStrategy => null,
            _ => _descriptor.Children,
        };

    // <snippet:descriptor-mount>
    public TControl Mount(MountContext ctx, TElement el)
    {
        var ctrl = ctx.RentControl(_descriptor.PoolPolicy, _descriptor.Factory);

        // §14 Phase 3-final: when the descriptor declares an ItemsHost,
        // populate the items collection BEFORE the prop loop. Initial writes
        // for selection-tracking props (SelectedIndex/SelectedItem) need the
        // collection populated first — WinUI silently clamps selection
        // against an empty collection.
        if (_descriptor.Children is ItemsHost<TElement, TControl> ih)
            DispatchItemsHostMount(in ctx, el, ctrl, ih);
        // §14 Phase 3 finish — consolidated dispatch arm: every items-
        // binder variant uses the same "bind before prop loop" ordering so
        // SelectedIndex initial writes land against a populated list.
        else if (_descriptor.Children is IItemsBinderStrategy binder && ctrl is FrameworkElement feBinder)
            binder.Bind(feBinder, oldElement: null, el, ctx.Reconciler, ctx.RequestRerender, isMount: true);

        // Phase 1: all bare initial writes (no echo possible — subscriptions
        // not yet live). §14 Phase 3-final: dispatch through the
        // context-carrying overload so OneWayBridged entries can reach the
        // reconciler/rerender helpers; existing entries forward to the
        // parameterless overload via the virtual default on PropEntry.
        var props = _descriptor.Properties;
        for (int i = 0; i < props.Count; i++)
            props[i].Mount(in ctx, ctrl, el);

        // Phase 2: subscribe controlled entries.
        var binding = ctx.BindFor(ctrl, el);
        for (int i = 0; i < props.Count; i++)
            props[i].EnsureSubscribed(binding, ctrl, el);

        var getSetters = _descriptor.GetSetters;
        if (getSetters is not null)
            ctx.ApplySetters(getSetters(el), ctrl);
        return ctrl;
    }
    // </snippet:descriptor-mount>

    /// <summary>§14 Phase 3 prelude (Engine A1) — forwards to the descriptor's
    /// optional <see cref="ControlDescriptor{TElement,TControl}.AfterChildrenMount"/>
    /// callback. The adapter invokes this after every child has mounted.</summary>
    public void AfterChildrenMount(MountContext ctx, TElement element, TControl control)
        => _descriptor.AfterChildrenMount?.Invoke(in ctx, element, control);

    public void Update(UpdateContext ctx, TElement oldEl, TElement newEl, TControl ctrl)
    {
        // §14 Phase 3-final: ItemsHost diff BEFORE prop Update loop, same
        // ordering rationale as Mount — selection-tracking writes need the
        // collection in its post-diff shape first.
        if (_descriptor.Children is ItemsHost<TElement, TControl> ih)
            DispatchItemsHostUpdate(in ctx, oldEl, newEl, ctrl, ih);
        // §14 Phase 3 finish — consolidated dispatch arm.
        else if (_descriptor.Children is IItemsBinderStrategy binder && ctrl is FrameworkElement feBinder)
            binder.Bind(feBinder, oldEl, newEl, ctx.Reconciler, ctx.RequestRerender, isMount: false);

        var props = _descriptor.Properties;
        for (int i = 0; i < props.Count; i++)
            props[i].Update(in ctx, ctrl, oldEl, newEl);

        // Late-wire on null→non-null callback transition — if the element
        // gained a callback since Mount, subscribe now. The per-entry CWT
        // gate makes the no-op case cheap.
        var binding = ctx.BindFor(ctrl, newEl);
        for (int i = 0; i < props.Count; i++)
            props[i].EnsureSubscribed(binding, ctrl, newEl);

        var getSetters = _descriptor.GetSetters;
        if (getSetters is not null)
            ctx.ApplySetters(getSetters(newEl), ctrl);
    }

    private static void DispatchItemsHostMount(
        in MountContext ctx, TElement el, TControl ctrl,
        ItemsHost<TElement, TControl> ih)
    {
        var newItems = ih.GetItems(el);
        var collection = ih.GetCollection(ctrl);
        if (collection.Count > 0) collection.Clear();
        for (int i = 0; i < newItems.Count; i++)
        {
            var item = newItems[i];
            if (item is Element childEl)
            {
                var mounted = ctx.MountChild(childEl);
                if (mounted is not null) collection.Add(mounted);
            }
            else if (item is not null)
                collection.Add(item);
        }
    }

    private static void DispatchItemsHostUpdate(
        in UpdateContext ctx, TElement oldEl, TElement newEl, TControl ctrl,
        ItemsHost<TElement, TControl> ih)
    {
        var oldItems = ih.GetItems(oldEl);
        var newItems = ih.GetItems(newEl);
        if (ReferenceEquals(oldItems, newItems)) return;
        var equals = ih.ItemEquals ?? object.Equals;
        if (oldItems.Count == newItems.Count)
        {
            bool same = true;
            for (int i = 0; i < newItems.Count; i++)
            {
                if (!equals(oldItems[i], newItems[i])) { same = false; break; }
            }
            if (same) return;
        }
        // Structural change — unmount Element items via the reconciler so
        // any descendant component state is torn down, then rebuild flat.
        // (Keyed reconcile lands separately for typed templated lists.)
        var reconciler = ctx.Reconciler;
        var rerender = ctx.RequestRerender;
        for (int i = 0; i < oldItems.Count; i++)
        {
            if (oldItems[i] is Element oldChild)
                reconciler.ReconcileV1Child(oldChild, null, null, rerender);
        }
        var collection = ih.GetCollection(ctrl);
        if (collection.Count > 0) collection.Clear();
        for (int i = 0; i < newItems.Count; i++)
        {
            var item = newItems[i];
            if (item is Element childEl)
            {
                var mounted = ctx.MountChild(childEl);
                if (mounted is not null) collection.Add(mounted);
            }
            else if (item is not null)
                collection.Add(item);
        }
    }
}
