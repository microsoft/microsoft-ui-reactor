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
///   and invoke <see cref="PropEntry{TElement,TControl}.Mount(TControl, TElement)"/> on each —
///   all bare initial writes happen first.</item>
///   <item>Iterate again and invoke
///   <see cref="PropEntry{TElement,TControl}.EnsureSubscribed"/> — controlled
///   entries wire their change-event trampolines now (no echo on the just-
///   written values because nothing is listening yet).</item>
///   <item>Apply setters.</item>
/// </list></para>
/// </summary>
/// <remarks>
/// Spec 048 §7 — <b>unsealed</b> so the built-in catalog can expose a thin,
/// <c>new()</c>-constructible registration subclass per descriptor (e.g.
/// <c>TextBlockDescriptorHandler() : DescriptorHandler&lt;…&gt;(TextBlockDescriptor.Descriptor)</c>).
/// That subclass is what the <see cref="Reg{TElement,TControl,THandler}"/> shim
/// instantiates via its <c>static () =&gt; new THandler()</c> lambda, so the
/// descriptor path registers through the exact same zero-closure mechanism as a
/// hand-coded handler. The base interpreter has no virtual members the subclass
/// overrides; unsealing carries no dispatch cost (calls already route through the
/// <see cref="IElementHandler{TElement,TControl}"/> interface via the adapter).
/// </remarks>
public class DescriptorHandler<TElement, TControl> : IElementHandler<TElement, TControl>
    where TElement : Element
    where TControl : FrameworkElement, new()
{
    private readonly ControlDescriptor<TElement, TControl> _descriptor;

    // Issue #117 — the per-prop loop ran over IReadOnlyList, paying an
    // interface-indexer dispatch per prop per cell. Snapshot the descriptor's
    // property list into concrete arrays once (the descriptor is a process-wide
    // singleton built before this handler is constructed, so the list is final)
    // and iterate those instead. _updateEntries holds every entry in declared
    // order (drives Mount/Update writes); _subscribeEntries is the small subset
    // (Issue #114) whose EnsureSubscribed override actually wires something —
    // typically empty (e.g. a grid cell's TextBlock) or 1-3 entries.
    private readonly PropEntry<TElement, TControl>[] _updateEntries;
    private readonly PropEntry<TElement, TControl>[] _subscribeEntries;

    public DescriptorHandler(ControlDescriptor<TElement, TControl> descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _descriptor = descriptor;

        var properties = descriptor.Properties;
        var update = new PropEntry<TElement, TControl>[properties.Count];
        int subscribeCount = 0;
        for (int i = 0; i < properties.Count; i++)
        {
            var entry = properties[i];
            update[i] = entry;
            if (entry.Subscribes) subscribeCount++;
        }

        _updateEntries = update;
        if (subscribeCount == 0)
        {
            _subscribeEntries = Array.Empty<PropEntry<TElement, TControl>>();
        }
        else
        {
            var subscribe = new PropEntry<TElement, TControl>[subscribeCount];
            int s = 0;
            for (int i = 0; i < update.Length; i++)
                if (update[i].Subscribes) subscribe[s++] = update[i];
            _subscribeEntries = subscribe;
        }
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

    /// <summary>Issue #375 — on Unmount the bind-before-props ordering
    /// constraint that motivates hiding <see cref="ItemsHost{TElement,TControl}"/>
    /// / <see cref="IItemsBinderStrategy"/> strategies from
    /// <see cref="Children"/> no longer applies, so expose the descriptor's
    /// real strategy here. Lets <c>V1HandlerAdapter</c>'s unmount-side
    /// dispatch walk descendant items (e.g. <c>TabView</c> tabs whose
    /// content holds Components) and fire their <c>UseEffect</c>
    /// cleanups.</summary>
    public ChildrenStrategy<TElement, TControl>? ChildrenForUnmount => _descriptor.Children;

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
        // Issue #117 — iterate the concrete array (no interface-indexer dispatch).
        var entries = _updateEntries;
        for (int i = 0; i < entries.Length; i++)
            entries[i].Mount(in ctx, ctrl, el);

        // Phase 2: subscribe controlled entries. Issue #114 — only the entries
        // that actually wire subscriptions are visited, and when there are none
        // (the dominant grid-cell case) BindFor is skipped entirely. BindFor's
        // only side effect is resetting the reference-slot thread-static, which
        // is consumed solely by reference entries — themselves subscribers — so
        // skipping it when the subset is empty changes no observable behavior.
        var subscribers = _subscribeEntries;
        if (subscribers.Length > 0)
        {
            var binding = ctx.BindFor(ctrl, el);
            for (int i = 0; i < subscribers.Length; i++)
                subscribers[i].EnsureSubscribed(binding, ctrl, el);
        }

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

        // Spec 050: wire trampolines BEFORE the prop Update loop so a
        // controlled write that triggers a DEFERRED change event (TextBox /
        // PasswordBox / NumberBox / AutoSuggest / RichEdit text writes,
        // certain selection writes) has a live subscriber when the engine
        // pumps the dispatcher. Wiring late stranded the WriteSuppressed
        // counter token: the deferred echo had no trampoline to call
        // ShouldSuppress on, so the token sat at +1 and swallowed the user's
        // next real input. The per-entry CWT gate (slot-is-null) makes the
        // steady-state no-op case cheap.
        //
        // Issue #114 — visit only the subscribe subset, and skip BindFor when
        // there are none (e.g. a grid cell of OneWay-only TextBlocks). This is
        // the dominant per-cell-per-frame saving: most entries are no-op
        // EnsureSubscribed overrides that previously paid a vtable dispatch on
        // every Update.
        var subscribers = _subscribeEntries;
        if (subscribers.Length > 0)
        {
            var binding = ctx.BindFor(ctrl, newEl);
            for (int i = 0; i < subscribers.Length; i++)
                subscribers[i].EnsureSubscribed(binding, ctrl, newEl);
        }

        // Issue #117 — iterate the concrete array (no interface-indexer dispatch).
        var entries = _updateEntries;
        for (int i = 0; i < entries.Length; i++)
            entries[i].Update(in ctx, ctrl, oldEl, newEl);

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
