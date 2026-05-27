using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

/// <summary>
/// Spec 047 §6 / §14 Phase 2 (Q1 spike) — base class for descriptor entries
/// declared on a <see cref="ControlDescriptor{TElement,TControl}"/>.
///
/// <para>The interpreter (see
/// <see cref="DescriptorHandler{TElement,TControl}"/>) iterates the entry
/// list during Mount and Update. The property's value type stays inside
/// each concrete entry — the interpreter never sees it, so iterating a
/// heterogeneous list of entries does not box value types (each generic
/// specialization keeps its own monomorphic code path).</para>
///
/// <para><b>Phase ordering inside Mount:</b>
/// <list type="number">
///   <item><see cref="Mount"/> runs once per entry. Controlled entries write
///   their initial value bare — event subscription has not yet been wired,
///   so a synchronous change event has no trampoline to fire (no echo).
///   Mirrors the KD-1b fix on the hand-coded handlers.</item>
///   <item><see cref="EnsureSubscribed"/> runs once per entry. Controlled
///   entries subscribe to their change event here, guarded by
///   <see cref="DescriptorHandler{TElement,TControl}"/>'s per-control
///   subscription gate (so pool-reused controls do not double-subscribe).</item>
/// </list>
/// On Update, <see cref="Update"/> runs and writes through
/// <c>ReactorBinding.WriteSuppressed</c> when the entry is controlled.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
public abstract class PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    /// <summary>Initial write at Mount. Controlled entries write bare here
    /// (no echo suppression — subscription has not yet been wired).</summary>
    public abstract void Mount(TControl ctrl, TElement el);

    /// <summary>Diff old/new element and apply the minimal write. Controlled
    /// entries write through <c>ReactorBinding.WriteSuppressed</c>.</summary>
    public abstract void Update(TControl ctrl, TElement oldEl, TElement newEl);

    /// <summary>Event subscription hook. Default = no-op (one-way / initial
    /// entries do not subscribe to events). Controlled entries override.
    ///
    /// <para>The interpreter calls this after every <see cref="Mount"/> in
    /// the entry list so all bare initial writes complete before any event
    /// trampoline goes live (KD-1b ordering invariant).</para></summary>
    public virtual void EnsureSubscribed(
        ReactorBinding<TElement> binding,
        TControl ctrl,
        TElement el) { }
}

// ════════════════════════════════════════════════════════════════════════
//  Concrete entries — one per §6.1 prop classification.
// ════════════════════════════════════════════════════════════════════════

/// <summary>§6.1 <c>Prop.OneWay</c> — write on Mount and on diff during
/// Update. No event subscription, no echo possible.</summary>
internal sealed class OneWayPropEntry<TElement, TControl, TValue> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    private readonly Func<TElement, TValue> _get;
    private readonly Action<TControl, TValue> _set;
    private readonly IEqualityComparer<TValue> _comparer;

    public OneWayPropEntry(
        Func<TElement, TValue> get,
        Action<TControl, TValue> set,
        IEqualityComparer<TValue>? comparer = null)
    {
        _get = get;
        _set = set;
        _comparer = comparer ?? EqualityComparer<TValue>.Default;
    }

    public override void Mount(TControl ctrl, TElement el) => _set(ctrl, _get(el));

    public override void Update(TControl ctrl, TElement oldEl, TElement newEl)
    {
        var nv = _get(newEl);
        if (!_comparer.Equals(_get(oldEl), nv))
            _set(ctrl, nv);
    }
}

/// <summary>§6.1 <c>Prop.OneWay</c> with a "should I write?" predicate — for
/// nullable / optional values where the host control should be left at its
/// default unless the element actually has a value. Used by Border's
/// nullable Background / BorderBrush props and ToggleSwitch's optional
/// Header.</summary>
internal sealed class OneWayConditionalPropEntry<TElement, TControl, TValue> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    private readonly Func<TElement, TValue> _get;
    private readonly Action<TControl, TValue> _set;
    private readonly Func<TElement, bool> _shouldWrite;
    private readonly IEqualityComparer<TValue> _comparer;

    public OneWayConditionalPropEntry(
        Func<TElement, TValue> get,
        Action<TControl, TValue> set,
        Func<TElement, bool> shouldWrite,
        IEqualityComparer<TValue>? comparer = null)
    {
        _get = get;
        _set = set;
        _shouldWrite = shouldWrite;
        _comparer = comparer ?? EqualityComparer<TValue>.Default;
    }

    public override void Mount(TControl ctrl, TElement el)
    {
        if (_shouldWrite(el)) _set(ctrl, _get(el));
    }

    public override void Update(TControl ctrl, TElement oldEl, TElement newEl)
    {
        if (!_shouldWrite(newEl)) return;
        var nv = _get(newEl);
        // Re-write when the predicate flips from false to true OR the value
        // genuinely changed. Don't try to be clever about the old element.
        if (!_shouldWrite(oldEl) || !_comparer.Equals(_get(oldEl), nv))
            _set(ctrl, nv);
    }
}

/// <summary>§6.1 <c>Prop.Initial</c> — write once at Mount, never on Update.
/// Used for "seed" props where typing / interaction is authoritative after
/// mount (e.g. an "InitialText" on a TextBox).</summary>
internal sealed class InitialPropEntry<TElement, TControl, TValue> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    private readonly Func<TElement, TValue> _get;
    private readonly Action<TControl, TValue> _set;

    public InitialPropEntry(Func<TElement, TValue> get, Action<TControl, TValue> set)
    {
        _get = get;
        _set = set;
    }

    public override void Mount(TControl ctrl, TElement el) => _set(ctrl, _get(el));
    public override void Update(TControl ctrl, TElement oldEl, TElement newEl) { /* never */ }
}

/// <summary>§6.1 <c>Prop.Controlled</c> — two-way bound. The engine writes
/// from element state (suppressed on Update), the control raises the change
/// event on user interaction, a static trampoline invokes the user's
/// callback with the readback value.
///
/// <para><b>Fast-path subscription</b> (matches the hand-coded handlers'
/// §9.2 typed-payload shape): the trampoline is a <c>static</c> field on
/// the closed generic — captures nothing, allocates nothing per mount.
/// Per-entry state (<c>_readBack</c>, <c>_getCallback</c>) lives on a
/// per-control <see cref="DescriptorControlledPayload{TElement,TControl,TValue,TArgs}"/>
/// stashed via <see cref="Reconciler.GetOrCreateControlEventPayload{T}"/>,
/// which survives pool rent/return (KD-3 invariant). Subsequent mounts of
/// the same control hit the null-slot check and skip the subscription
/// entirely — zero allocations on the re-mount fast path, identical shape
/// to <c>EnsureToggledWiring</c> in <see cref="V1Protocol.Handlers.ToggleSwitchHandler"/>.</para>
///
/// <para><b>Callback gate:</b> if the element's callback selector returns
/// <c>null</c> the entry does not subscribe. Mirrors the hand-coded
/// <c>Ensure*Wiring</c> gate (M4/M5 dispatch suites — most ToggleSwitches in
/// a typical app have no callback and should pay zero subscription
/// cost).</para></summary>
internal sealed class ControlledPropEntry<TElement, TControl, TValue, TArgs> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : FrameworkElement
{
    private readonly Func<TElement, TValue> _get;
    private readonly Action<TControl, TValue> _set;
    private readonly Action<FrameworkElement, EventHandler<TArgs>> _subscribe;
    private readonly Action<FrameworkElement, EventHandler<TArgs>> _unsubscribe;
    private readonly Func<TElement, Action<TValue>?> _getCallback;
    private readonly Func<TControl, TValue> _readBack;
    private readonly IEqualityComparer<TValue> _comparer;

    // Static trampoline — closes over the closed generic's TElement /
    // TControl / TValue / TArgs but captures no per-instance state.
    // Reads per-entry lambdas off the payload at fire time.
    private static readonly EventHandler<TArgs> StaticTrampoline = (sender, args) =>
    {
        var fe = (FrameworkElement)sender!;
        if (ChangeEchoSuppressor.ShouldSuppress(fe)) return;
        if (Reconciler.GetElementTag(fe) is not TElement liveEl) return;
        var payload = Reconciler.GetOrCreateControlEventPayload<
            DescriptorControlledPayload<TElement, TControl, TValue, TArgs>>(fe);
        if (payload.ReadBack is not { } rb || payload.GetCallback is not { } gc) return;
        var cb = gc(liveEl);
        cb?.Invoke(rb((TControl)fe));
    };

    public ControlledPropEntry(
        Func<TElement, TValue> get,
        Action<TControl, TValue> set,
        Action<FrameworkElement, EventHandler<TArgs>> subscribe,
        Action<FrameworkElement, EventHandler<TArgs>> unsubscribe,
        Func<TElement, Action<TValue>?> getCallback,
        Func<TControl, TValue> readBack,
        IEqualityComparer<TValue>? comparer = null)
    {
        _get = get;
        _set = set;
        _subscribe = subscribe;
        _unsubscribe = unsubscribe;
        _getCallback = getCallback;
        _readBack = readBack;
        _comparer = comparer ?? EqualityComparer<TValue>.Default;
    }

    public override void Mount(TControl ctrl, TElement el)
    {
        // Bare initial write — subscription has not yet been wired (the
        // interpreter calls EnsureSubscribed after all Mount writes).
        var v = _get(el);
        if (!_comparer.Equals(_readBack(ctrl), v))
            _set(ctrl, v);
    }

    public override void Update(TControl ctrl, TElement oldEl, TElement newEl)
    {
        var nv = _get(newEl);
        var ov = _get(oldEl);
        var current = _readBack(ctrl);
        // Write when the element prop genuinely changed OR the control has
        // drifted from the element's authority (e.g. user-typed text the
        // descriptor is overriding).
        if (!_comparer.Equals(ov, nv) || !_comparer.Equals(current, nv))
            ReactorBinding.WriteSuppressed(ctrl, () => _set(ctrl, nv));
    }

    public override void EnsureSubscribed(
        ReactorBinding<TElement> binding,
        TControl ctrl,
        TElement el)
    {
        // Lazy-wire on callback presence — mirrors the hand-coded handlers'
        // `el.OnXxx is null` early exit so callback-less controls pay zero
        // subscription cost (M4/M5 dispatch suites).
        if (_getCallback(el) is null) return;

        var payload = Reconciler.GetOrCreateControlEventPayload<
            DescriptorControlledPayload<TElement, TControl, TValue, TArgs>>(ctrl);
        if (payload.Trampoline is not null) return; // already wired for this control's lifetime

        // First mount of this control — wire the static trampoline once.
        // ReadBack / GetCallback go on the payload so the static trampoline
        // can read them. Both are entry-level lambdas (declared once when
        // the descriptor is constructed) and are reference-stable across
        // re-renders.
        payload.ReadBack = _readBack;
        payload.GetCallback = _getCallback;
        payload.Trampoline = StaticTrampoline;
        _subscribe(ctrl, StaticTrampoline);
    }
}

/// <summary>§9.2 typed payload for the descriptor model's controlled-prop
/// entries. One closed generic per (<typeparamref name="TElement"/>,
/// <typeparamref name="TControl"/>, <typeparamref name="TValue"/>,
/// <typeparamref name="TArgs"/>) tuple. Survives pool rent/return per
/// KD-3 invariant — the per-control payload is preserved on
/// <c>Reconciler.ReturnControl</c> so re-mounts hit the null-slot
/// check and skip resubscription.
///
/// <para><b>Caveat:</b> two descriptor <c>Controlled</c> entries with the
/// same (<typeparamref name="TElement"/>, <typeparamref name="TControl"/>,
/// <typeparamref name="TValue"/>, <typeparamref name="TArgs"/>) tuple on
/// the same control would share this payload type and collide. None of
/// the Phase 2 spike descriptors hit that case; if a future control needs
/// it, add an entry-index slot marker phantom type to the closed generic.</para></summary>
internal sealed class DescriptorControlledPayload<TElement, TControl, TValue, TArgs>
    where TElement : Element
    where TControl : FrameworkElement
{
    public EventHandler<TArgs>? Trampoline;
    public Func<TControl, TValue>? ReadBack;
    public Func<TElement, Action<TValue>?>? GetCallback;
}

/// <summary>§8 / Slider audit — a <c>Prop.OneWay</c> whose write may coerce
/// a sibling <see cref="ControlledPropEntry{TElement,TControl,TValue,TArgs}"/>
/// on the same control (e.g. <c>Slider.Minimum</c> raising Value).
///
/// <para>When the <c>coercesController</c> predicate returns true, the
/// write is wrapped in <c>ReactorBinding.WriteSuppressed</c> so the
/// coercion-driven change event is dropped. Mirrors the Slider audit's
/// "tolerance" treatment (§8 Phase 4 plan — declarative replacement for the
/// imperative `if (ctrl.Value &lt; n.Min) WriteSuppressed(...)` pattern in
/// the hand-coded SliderHandler).</para></summary>
internal sealed class CoercingOneWayPropEntry<TElement, TControl, TValue> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    private readonly Func<TElement, TValue> _get;
    private readonly Action<TControl, TValue> _set;
    private readonly Func<TControl, TValue, bool> _coercesController;
    private readonly IEqualityComparer<TValue> _comparer;

    public CoercingOneWayPropEntry(
        Func<TElement, TValue> get,
        Action<TControl, TValue> set,
        Func<TControl, TValue, bool> coercesController,
        IEqualityComparer<TValue>? comparer = null)
    {
        _get = get;
        _set = set;
        _coercesController = coercesController;
        _comparer = comparer ?? EqualityComparer<TValue>.Default;
    }

    public override void Mount(TControl ctrl, TElement el)
    {
        // Mount-time writes are bare — sibling subscriptions are not yet
        // live. The interpreter's Mount order (writes-then-subscribe) makes
        // coercion echoes impossible at mount.
        _set(ctrl, _get(el));
    }

    public override void Update(TControl ctrl, TElement oldEl, TElement newEl)
    {
        var nv = _get(newEl);
        if (_comparer.Equals(_get(oldEl), nv)) return;

        if (_coercesController(ctrl, nv))
            ReactorBinding.WriteSuppressed(ctrl, () => _set(ctrl, nv));
        else
            _set(ctrl, nv);
    }
}
