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

    /// <summary>Spec 047 §14 Phase 3-final — context-carrying Mount overload.
    /// Default implementation forwards to the parameterless
    /// <see cref="Mount(TControl,TElement)"/>; entry types that need
    /// reconciler/rerender access (e.g.
    /// <see cref="OneWayBridgedPropEntry{TElement,TControl,TValue}"/> for
    /// Flyout descriptor bridging) override this overload instead.</summary>
    public virtual void Mount(in MountContext ctx, TControl ctrl, TElement el)
        => Mount(ctrl, el);

    /// <summary>Spec 047 §14 Phase 3-final — context-carrying Update overload.
    /// Same forwarding contract as
    /// <see cref="Mount(in MountContext, TControl, TElement)"/>.</summary>
    public virtual void Update(in UpdateContext ctx, TControl ctrl, TElement oldEl, TElement newEl)
        => Update(ctrl, oldEl, newEl);

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
        var payload = Reconciler.TryGetControlEventPayload<
            DescriptorControlledPayload<TElement, TControl, TValue, TArgs>>(fe);

        // Counter / setter-scope suppression still wins on this control: an
        // external ReactorBinding.WriteSuppressed token or an ApplySetters
        // `.Set(...)` scope (which carry no expected value) are honored first.
        // But if a value-diff echo is also armed and THIS suppressed event is
        // that echo (readback matches), drain it here — otherwise the pending
        // flag would strand and swallow the user's next real interaction.
        if (ChangeEchoSuppressor.ShouldSuppress(fe))
        {
            if (payload is { HasExpectedEcho: true } ps && ps.ReadBack is { } rbs)
            {
                var cmps = ps.EchoComparer ?? EqualityComparer<TValue>.Default;
                if (cmps.Equals(rbs((TControl)fe), ps.ExpectedEcho!))
                {
                    ps.HasExpectedEcho = false;
                    ps.ExpectedEcho = default;
                    ps.EchoComparer = null;
                }
            }
            return;
        }

        if (Reconciler.GetElementTag(fe) is not TElement liveEl) return;
        if (payload is null || payload.ReadBack is not { } rb || payload.GetCallback is not { } gc) return;

        var current = rb((TControl)fe);

        // §8 value-diff echo suppression (PoC): a programmatic controlled write
        // armed ExpectedEcho. If this event's readback equals it, this IS that
        // echo — consume it once and drop. A mismatch means a real user change
        // superseded the pending write, so clear and fall through to the callback.
        if (payload.HasExpectedEcho)
        {
            var expected = payload.ExpectedEcho;
            var cmp = payload.EchoComparer ?? EqualityComparer<TValue>.Default;
            payload.HasExpectedEcho = false;
            payload.ExpectedEcho = default;
            payload.EchoComparer = null;
            if (cmp.Equals(current, expected!)) return;
        }

        var cb = gc(liveEl);
        cb?.Invoke(current);
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
        // §8 PoC: clear any value-diff arm left on a pooled payload from a prior
        // lifecycle (the payload survives pool rent/return per KD-3) so it can't
        // suppress this lifecycle's first real event.
        var pooled = Reconciler.TryGetControlEventPayload<
            DescriptorControlledPayload<TElement, TControl, TValue, TArgs>>(ctrl);
        if (pooled is not null)
        {
            pooled.HasExpectedEcho = false;
            pooled.ExpectedEcho = default;
            pooled.EchoComparer = null;
        }

        // Bare initial write — subscription has not yet been wired (the
        // interpreter calls EnsureSubscribed after all Mount writes), so this
        // write raises no echo callback and needs no arming.
        var v = _get(el);
        if (!_comparer.Equals(_readBack(ctrl), v))
            _set(ctrl, v);
    }

    public override void Update(TControl ctrl, TElement oldEl, TElement newEl)
    {
        var nv = _get(newEl);
        var current = _readBack(ctrl);
        // Spec 047 §8 echo-suppression contract: write ONLY when the control has
        // drifted from the element's authority. A no-drift write raises no event,
        // so arming would strand the pending flag (the §8 cross-state echo class).
        if (_comparer.Equals(current, nv))
            return;

        // §8 value-diff echo suppression (PoC) for the controlled fast path: arm
        // the per-control "expected echo" instead of bumping the causal counter,
        // then write. The synthesized change event is recognized in the trampoline
        // by readback == expected and dropped once. Only arm when a callback is
        // present (otherwise no echo can fire) and a payload exists (it does iff
        // EnsureSubscribed wired the trampoline for this control's lifetime); if
        // it does not, there is no subscription, hence no echo to suppress.
        if (_getCallback(newEl) is not null)
        {
            var payload = Reconciler.TryGetControlEventPayload<
                DescriptorControlledPayload<TElement, TControl, TValue, TArgs>>(ctrl);
            if (payload is not null)
            {
                payload.ExpectedEcho = nv;
                payload.HasExpectedEcho = true;
                payload.EchoComparer = _comparer;
            }
        }

        _set(ctrl, nv);
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

    // Spec 047 §8 value-diff echo suppression (PoC). A programmatic controlled
    // write arms ExpectedEcho with the value it just wrote; the change-event
    // trampoline drops the single matching echo (readback == ExpectedEcho) once,
    // then clears it. Replaces the causal counter on this fast path only — the
    // counter is retained everywhere else (setter scope, public WriteSuppressed,
    // hand-coded / coercing / collection entries).
    //
    // CAVEAT (accepted PoC tradeoff): this is a SINGLE pending slot and is only
    // correct if the control raises its change event SYNCHRONOUSLY inside the
    // `_set` write (so the trampoline drains the arm before Update can run again).
    // All controls on this path today (RadioButton/ToggleSplitButton/ToggleSwitch
    // .IsChecked-style toggles, RatingControl, the date/time pickers) raise their
    // change event inline. If a control with a deferred/queued change event is
    // ever routed through ControlledPropEntry, a second Update could overwrite the
    // arm before the queued echo arrives, mis-suppressing or leaking one event —
    // at which point this path should migrate back to the causal counter (which
    // is deliberately kept intact for exactly that fallback).
    public TValue? ExpectedEcho;
    public bool HasExpectedEcho;
    public IEqualityComparer<TValue>? EchoComparer;
}

/// <summary>§14 Phase 3 (3.0.1) — <c>Prop.Controlled</c> escape-hatch entry
/// whose change-event trampoline is hand-authored on a user-supplied
/// <typeparamref name="TPayload"/> rather than the fast-path
/// <see cref="DescriptorControlledPayload{TElement,TControl,TValue,TArgs}"/>.
///
/// <para><b>When to use:</b> multi-event controls (e.g. <c>TextBox</c> with
/// both <c>TextChanged</c> and <c>SelectionChanged</c>, <c>NumberBox</c>
/// with <c>ValueChanged</c> + <c>GotFocus/LostFocus</c>) cannot share the
/// single-event fast-path payload — two controlled entries on the same
/// control would collide on the closed-generic
/// <c>DescriptorControlledPayload</c>. The descriptor author reuses an
/// existing per-control payload from <c>ControlEventPayloads.cs</c> (e.g.
/// <c>TextBoxEventPayload</c>) which already has 1+ slots per event, hands
/// the entry a slot-is-null predicate / set-slot setter pair to address
/// the right slot, and passes a native-typed trampoline delegate (matching
/// the control's event signature directly — no
/// <c>EventHandler&lt;TArgs&gt;</c> bridge closure).</para>
///
/// <para><b>Mount / Update writes:</b> identical to
/// <see cref="ControlledPropEntry{TElement,TControl,TValue,TArgs}"/> — bare
/// at Mount, <see cref="ReactorBinding.WriteSuppressed(UIElement,Action)"/>
/// at Update on element-change OR control-drift.</para>
///
/// <para><b>Subscription gate:</b> if the element's callback selector
/// returns null, no subscription happens. On non-null, the entry calls
/// <see cref="Reconciler.GetOrCreateControlEventPayload{T}"/> for the
/// user's payload type, checks the slot-is-null predicate, and (if empty)
/// stuffs the trampoline into the payload slot and subscribes — exactly
/// once per control lifetime. Subsequent rents of the same control hit
/// the non-null-slot fast path with zero allocations.</para></summary>
internal sealed class HandCodedControlledPropEntry<TElement, TControl, TPayload, TValue, TDelegate> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : FrameworkElement
    where TPayload : class, new()
    where TDelegate : Delegate
{
    private readonly Func<TElement, TValue> _get;
    private readonly Action<TControl, TValue> _set;
    private readonly Func<TControl, TValue> _readBack;
    private readonly Action<TControl, TDelegate> _subscribe;
    private readonly Func<TElement, Action<TValue>?> _getCallback;
    private readonly TDelegate _trampoline;
    private readonly Func<TPayload, bool> _slotIsNull;
    private readonly Action<TPayload, TDelegate> _setSlot;
    private readonly IEqualityComparer<TValue> _comparer;
    private readonly bool _valueDiffEcho;

    public HandCodedControlledPropEntry(
        Func<TElement, TValue> get,
        Action<TControl, TValue> set,
        Func<TControl, TValue> readBack,
        Action<TControl, TDelegate> subscribe,
        Func<TElement, Action<TValue>?> callback,
        TDelegate trampoline,
        Func<TPayload, bool> slotIsNull,
        Action<TPayload, TDelegate> setSlot,
        IEqualityComparer<TValue>? comparer = null,
        bool valueDiffEcho = false)
    {
        _get = get;
        _set = set;
        _readBack = readBack;
        _subscribe = subscribe;
        _getCallback = callback;
        _trampoline = trampoline;
        _slotIsNull = slotIsNull;
        _setSlot = setSlot;
        _comparer = comparer ?? EqualityComparer<TValue>.Default;
        _valueDiffEcho = valueDiffEcho;
    }

    public override void Mount(TControl ctrl, TElement el)
    {
        // §8 value-diff: clear any stale arm left on a pooled control so it
        // can't suppress this lifecycle's first real event. Mount writes are
        // bare (subscription not yet wired), so no arming here.
        if (_valueDiffEcho)
            ChangeEchoSuppressor.ClearExpectedEcho(ctrl);

        var v = _get(el);
        if (!_comparer.Equals(_readBack(ctrl), v))
            _set(ctrl, v);
    }

    public override void Update(TControl ctrl, TElement oldEl, TElement newEl)
    {
        var nv = _get(newEl);
        var current = _readBack(ctrl);
        // Spec 047 §8: suppress-write only on real drift (see ControlledPropEntry).
        // The prior `oldEl != newEl` disjunct stranded the suppress token on the
        // standard controlled round-trip and swallowed the next real user event.
        if (_comparer.Equals(current, nv))
            return;

        // §8 value-diff (opt-in via valueDiffEcho): arm the per-control expected
        // echo instead of bumping the causal counter, then write bare. The
        // hand-written trampoline drops the single synthesized echo whose
        // readback matches via ShouldSuppressEcho. Arm only when a callback is
        // present (otherwise nothing is subscribed to echo). The control's event
        // fires synchronously inside _set, so a trampoline that is not yet wired
        // (null→non-null callback transition) simply produces no echo — no strand.
        if (_valueDiffEcho)
        {
            if (_getCallback(newEl) is not null)
            {
                var expected = nv;
                var cmp = _comparer;
                ChangeEchoSuppressor.ArmExpectedEcho(
                    ctrl, rb => cmp.Equals(rb is TValue tv ? tv : default!, expected));
            }
            _set(ctrl, nv);
            // If a guarded/coerced setter (e.g. `if (v >= 0) ...`, bounds checks)
            // dropped the write, the synchronous echo never fired to consume the
            // arm. Clear it so it can't strand and swallow a later real event
            // whose readback happens to equal the never-applied value.
            if (!_comparer.Equals(_readBack(ctrl), nv))
                ChangeEchoSuppressor.ClearExpectedEcho(ctrl);
        }
        else
        {
            ReactorBinding.WriteSuppressed(ctrl, () => _set(ctrl, nv));
        }
    }

    public override void EnsureSubscribed(
        ReactorBinding<TElement> binding,
        TControl ctrl,
        TElement el)
    {
        if (_getCallback(el) is null) return;
        var payload = Reconciler.GetOrCreateControlEventPayload<TPayload>(ctrl);
        if (!_slotIsNull(payload)) return;
        _setSlot(payload, _trampoline);
        _subscribe(ctrl, _trampoline);
    }
}

/// <summary>§14 Phase 3 (3.0.1) — <c>Prop.HandCodedEvent</c> escape-hatch
/// entry for control-intrinsic events that do not have a DP round-trip
/// (e.g. <c>TextBox.SelectionChanged</c>, <c>Image.ImageOpened</c>). No
/// Mount/Update writes — purely event subscription with the same payload-
/// slot gating shape as
/// <see cref="HandCodedControlledPropEntry{TElement,TControl,TPayload,TValue,TDelegate}"/>.
///
/// <para>The element's callback-presence selector drives the subscription
/// gate — null callback ⇒ no subscription cost. Trampoline lives once per
/// control lifetime.</para></summary>
internal sealed class HandCodedEventPropEntry<TElement, TControl, TPayload, TDelegate> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : FrameworkElement
    where TPayload : class, new()
    where TDelegate : Delegate
{
    private readonly Action<TControl, TDelegate> _subscribe;
    private readonly Func<TElement, Delegate?> _callbackPresent;
    private readonly TDelegate _trampoline;
    private readonly Func<TPayload, bool> _slotIsNull;
    private readonly Action<TPayload, TDelegate> _setSlot;

    public HandCodedEventPropEntry(
        Action<TControl, TDelegate> subscribe,
        Func<TElement, Delegate?> callbackPresent,
        TDelegate trampoline,
        Func<TPayload, bool> slotIsNull,
        Action<TPayload, TDelegate> setSlot)
    {
        _subscribe = subscribe;
        _callbackPresent = callbackPresent;
        _trampoline = trampoline;
        _slotIsNull = slotIsNull;
        _setSlot = setSlot;
    }

    public override void Mount(TControl ctrl, TElement el) { /* no DP write */ }

    public override void Update(TControl ctrl, TElement oldEl, TElement newEl) { /* no DP write */ }

    public override void EnsureSubscribed(
        ReactorBinding<TElement> binding,
        TControl ctrl,
        TElement el)
    {
        if (_callbackPresent(el) is null) return;
        var payload = Reconciler.GetOrCreateControlEventPayload<TPayload>(ctrl);
        if (!_slotIsNull(payload)) return;
        _setSlot(payload, _trampoline);
        _subscribe(ctrl, _trampoline);
    }
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

/// <summary>Spec 047 §14 Phase 3 finish — Engine (4). Property-level
/// escape hatch — the entry's Mount/Update lambdas receive the control
/// and TElement directly (Update receives BOTH old and new), letting the
/// descriptor express diffs the per-value get/set shapes can't.
///
/// <para>The classic motivating case is <c>Path.PathDataString</c>: the
/// legacy <c>UpdatePath</c> gates the <c>Data</c> write on a
/// string-vs-string compare of <c>o.PathDataString</c> vs
/// <c>n.PathDataString</c> — the comparer in
/// <see cref="OneWayConditionalPropEntry{TElement,TControl,TValue}"/>
/// only sees two values from the same get lambda, so it can't combine
/// the string compare with the Geometry write.</para>
///
/// <para>No fast-path; the entry's Update lambda runs on every render. If
/// the diff is expressible through the standard entry shapes, prefer
/// those.</para></summary>
internal sealed class ImperativePropEntry<TElement, TControl> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    private readonly Action<TControl, TElement> _mount;
    private readonly Action<TControl, TElement, TElement> _update;

    public ImperativePropEntry(
        Action<TControl, TElement> mount,
        Action<TControl, TElement, TElement> update)
    {
        _mount = mount;
        _update = update;
    }

    public override void Mount(TControl ctrl, TElement el) => _mount(ctrl, el);
    public override void Update(TControl ctrl, TElement oldEl, TElement newEl) => _update(ctrl, oldEl, newEl);
}

/// <summary>Spec 047 §14 Phase 3 finish — Engine (2). Bridged variant
/// of <see cref="ImperativePropEntry{TElement,TControl}"/> — Mount /
/// Update lambdas receive the <see cref="MountContext"/> /
/// <see cref="UpdateContext"/> so they can call engine-internal helpers
/// (<c>Reconciler.ReconcileV1Child</c>, the rerender pump, etc.).
///
/// <para>Canonical use is a secondary Element slot whose write target
/// overlaps with a sibling property — e.g.
/// <c>Expander.HeaderTemplate</c> reconciles into the same
/// <c>Header</c> property the string <c>Header</c> writes to. See
/// <see cref="ControlDescriptor{TElement,TControl}.ImperativeBridged"/>
/// for the composition recipe.</para></summary>
internal sealed class ImperativeBridgedPropEntry<TElement, TControl> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    private readonly Action<MountContext, TControl, TElement> _mount;
    private readonly Action<UpdateContext, TControl, TElement, TElement> _update;

    public ImperativeBridgedPropEntry(
        Action<MountContext, TControl, TElement> mount,
        Action<UpdateContext, TControl, TElement, TElement> update)
    {
        _mount = mount;
        _update = update;
    }

    // Parameterless overloads — unreachable; the bridged entry only goes
    // through the context-carrying overloads. Throw on misuse so a missed
    // dispatch path surfaces immediately (mirrors OneWayBridgedPropEntry).
    public override void Mount(TControl ctrl, TElement el)
        => throw new InvalidOperationException(
            "ImperativeBridged entry requires MountContext — descriptor dispatch must use the context-carrying overload.");
    public override void Update(TControl ctrl, TElement oldEl, TElement newEl)
        => throw new InvalidOperationException(
            "ImperativeBridged entry requires UpdateContext — descriptor dispatch must use the context-carrying overload.");

    public override void Mount(in MountContext ctx, TControl ctrl, TElement el) => _mount(ctx, ctrl, el);
    public override void Update(in UpdateContext ctx, TControl ctrl, TElement oldEl, TElement newEl)
        => _update(ctx, ctrl, oldEl, newEl);
}

/// <summary>Spec 047 §14 Phase 3-final — <c>.OneWayBridged</c> entry. Same
/// diff-and-write contract as
/// <see cref="OneWayConditionalPropEntry{TElement,TControl,TValue}"/>, but
/// the set lambda receives the <see cref="MountContext"/> / <see cref="UpdateContext"/>
/// so it can reach engine-internal helpers — primarily
/// <c>Reconciler.CreateFlyoutForDescriptor</c> for the button-family
/// Flyout port, but reusable for any future bridged transform that needs
/// reconciler / rerender access.
///
/// <para><b>Why a separate entry shape:</b> the parameterless set lambda on
/// <c>.OneWay</c> / <c>.OneWayConditional</c> cannot call
/// <c>ctx.Reconciler.CreateFlyoutForDescriptor(v, ctx.RequestRerender)</c>
/// because it has no context. Threading a thread-static <c>Reconciler.Current</c>
/// would work but makes the bridge contract undiscoverable from the
/// descriptor surface. This entry surfaces it explicitly.</para></summary>
internal sealed class OneWayBridgedPropEntry<TElement, TControl, TValue> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : UIElement
{
    private readonly Func<TElement, TValue> _get;
    private readonly OneWayBridgedSetter<TControl, TValue> _set;
    private readonly Func<TElement, bool> _shouldWrite;
    private readonly IEqualityComparer<TValue> _comparer;

    public OneWayBridgedPropEntry(
        Func<TElement, TValue> get,
        OneWayBridgedSetter<TControl, TValue> set,
        Func<TElement, bool> shouldWrite,
        IEqualityComparer<TValue>? comparer = null)
    {
        _get = get;
        _set = set;
        _shouldWrite = shouldWrite;
        _comparer = comparer ?? EqualityComparer<TValue>.Default;
    }

    // Parameterless overloads — unreachable; the bridged entry only goes
    // through the context-carrying overloads. Throw on misuse rather than
    // silently no-op so a missed dispatch path surfaces immediately.
    public override void Mount(TControl ctrl, TElement el)
        => throw new InvalidOperationException(
            "OneWayBridged entry requires MountContext — descriptor dispatch must use the context-carrying overload.");
    public override void Update(TControl ctrl, TElement oldEl, TElement newEl)
        => throw new InvalidOperationException(
            "OneWayBridged entry requires UpdateContext — descriptor dispatch must use the context-carrying overload.");

    public override void Mount(in MountContext ctx, TControl ctrl, TElement el)
    {
        if (!_shouldWrite(el)) return;
        _set(ctrl, _get(el), ctx.Reconciler, ctx.RequestRerender);
    }

    public override void Update(in UpdateContext ctx, TControl ctrl, TElement oldEl, TElement newEl)
    {
        if (!_shouldWrite(newEl)) return;
        var nv = _get(newEl);
        if (!_shouldWrite(oldEl) || !_comparer.Equals(_get(oldEl), nv))
            _set(ctrl, nv, ctx.Reconciler, ctx.RequestRerender);
    }
}

/// <summary>Spec 047 §14 Phase 3-final — set-lambda signature for
/// <see cref="OneWayBridgedPropEntry{TElement,TControl,TValue}"/>. Named
/// delegate type (not <c>Action&lt;...&gt;</c>) so the
/// <see cref="MountContext"/> / <see cref="UpdateContext"/> doesn't need
/// to be passed — the entry projects the two pieces a bridge typically
/// needs (the <see cref="Reconciler"/> and the rerender callback).</summary>
public delegate void OneWayBridgedSetter<in TControl, in TValue>(
    TControl ctrl, TValue value, Reconciler reconciler, Action requestRerender)
    where TControl : UIElement;

/// <summary>Spec 047 §14 Phase 3-final — <c>.Immediate</c> entry. Pure
/// subscription wiring for the "observed-DP callback + Loaded → inner
/// template-part trampoline" pattern. The control's primary commit-mode
/// DP round-trip stays on a sibling <c>.HandCodedControlled</c> entry —
/// this entry only manages the two extra subscription slots needed for
/// per-keystroke observation.
///
/// <para><b>Why a separate entry shape:</b> the legacy <c>MountNumberBox</c>
/// arm does this manually with <c>RegisterPropertyChangedCallback</c> +
/// <c>Loaded → ApplyTemplate → FindDescendant&lt;TextBox&gt;</c>. The
/// descriptor port reuses this entry by supplying captured-free static
/// trampolines for the property-changed callback and the
/// Loaded-hook-driven inner subscription, matching the
/// <see cref="HandCodedEventPropEntry{TElement,TControl,TPayload,TDelegate}"/>
/// model.</para>
///
/// <para><b>What the author supplies (see ctor params):</b>
/// <list type="bullet">
///   <item><c>observeProperty</c> + <c>observeCallback</c> — the DP and
///   the captured-free property-changed callback (reads the live element
///   via <c>Reconciler.GetElementTag</c>).</item>
///   <item><c>loadedHook</c> — captured-free
///   <see cref="RoutedEventHandler"/> registered against the control's
///   <c>Loaded</c> event. Author's hook walks the visual tree, finds the
///   inner template part, subscribes its event, and flips an idempotency
///   flag on the payload so subsequent Loaded fires skip the walk.</item>
///   <item><c>callbackGate</c> — entry skips registration when the
///   element's callback is null (mirrors the <c>EnsureXxxWiring</c>
///   gate).</item>
/// </list></para>
///
/// <para><b>Mount/Update writes:</b> none. The sibling
/// <c>.HandCodedControlled</c> (or <c>.OneWay</c>) entry on the same
/// descriptor handles the DP write.</para></summary>
internal sealed class ImmediatePropEntry<TElement, TControl, TPayload> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : FrameworkElement
    where TPayload : class, new()
{
    private readonly Func<TElement, Delegate?> _callbackGate;
    private readonly Microsoft.UI.Xaml.DependencyProperty _observeProperty;
    private readonly Microsoft.UI.Xaml.DependencyPropertyChangedCallback _observeCallback;
    private readonly Func<TPayload, bool> _observeSlotIsNull;
    private readonly Action<TPayload, Microsoft.UI.Xaml.DependencyPropertyChangedCallback> _setObserveSlot;
    private readonly Microsoft.UI.Xaml.RoutedEventHandler _loadedHook;

    public ImmediatePropEntry(
        Func<TElement, Delegate?> callbackGate,
        Microsoft.UI.Xaml.DependencyProperty observeProperty,
        Microsoft.UI.Xaml.DependencyPropertyChangedCallback observeCallback,
        Func<TPayload, bool> observeSlotIsNull,
        Action<TPayload, Microsoft.UI.Xaml.DependencyPropertyChangedCallback> setObserveSlot,
        Microsoft.UI.Xaml.RoutedEventHandler loadedHook)
    {
        _callbackGate = callbackGate;
        _observeProperty = observeProperty;
        _observeCallback = observeCallback;
        _observeSlotIsNull = observeSlotIsNull;
        _setObserveSlot = setObserveSlot;
        _loadedHook = loadedHook;
    }

    public override void Mount(TControl ctrl, TElement el) { /* no DP write */ }
    public override void Update(TControl ctrl, TElement oldEl, TElement newEl) { /* no DP write */ }

    public override void EnsureSubscribed(
        ReactorBinding<TElement> binding,
        TControl ctrl,
        TElement el)
    {
        if (_callbackGate(el) is null) return;
        var payload = Reconciler.GetOrCreateControlEventPayload<TPayload>(ctrl);
        if (!_observeSlotIsNull(payload)) return;
        _setObserveSlot(payload, _observeCallback);
        ctrl.RegisterPropertyChangedCallback(_observeProperty, _observeCallback);
        // Loaded fires on each attach; the author's hook is responsible for
        // its own idempotency (typically by flipping a flag on the payload
        // and self-unsubscribing). The entry doesn't track that.
        ctrl.Loaded += _loadedHook;
    }
}

/// <summary>Spec 047 §14 Phase 3-final — <c>.CollectionDiffControlled</c>
/// entry. Two-way bound prop whose value is an
/// <see cref="IReadOnlyList{TItem}"/> on the element side and an
/// <c>IList&lt;TItem&gt;</c> (typically a WinUI
/// <c>IObservableVector&lt;T&gt;</c>) on the control side. Each Update
/// applies a keyed hash-set diff and emits per-element
/// <c>Add</c> / <c>Remove</c> ops inside
/// <see cref="ChangeEchoSuppressor.BeginSuppress"/> so the per-mutation
/// echo doesn't fire back through the user's callback.
///
/// <para><b>Why a separate entry shape:</b>
/// <c>CalendarView.SelectedDates</c> is the canonical case — an
/// <c>IObservableVector&lt;DateTime&gt;</c> that fires
/// <c>SelectedDatesChanged</c> per mutation. The legacy arm clears + re-adds
/// inside one <c>BeginSuppress</c>; this entry preserves item identity
/// across reorder so animations and selection state survive incremental
/// updates. Reusable for any future control with a typed
/// <c>IObservableVector&lt;T&gt;</c> two-way slot.</para></summary>
internal sealed class CollectionDiffControlledPropEntry<TElement, TControl, TPayload, TItem, TKey, TDelegate> : PropEntry<TElement, TControl>
    where TElement : Element
    where TControl : FrameworkElement
    where TPayload : class, new()
    where TKey : notnull
    where TDelegate : Delegate
{
    private readonly Func<TElement, IReadOnlyList<TItem>> _get;
    private readonly Func<TControl, IList<TItem>> _getVector;
    private readonly Func<TItem, TKey> _key;
    private readonly Action<TControl, TDelegate> _subscribe;
    private readonly Func<TElement, Delegate?> _callbackPresent;
    private readonly TDelegate _trampoline;
    private readonly Func<TPayload, bool> _slotIsNull;
    private readonly Action<TPayload, TDelegate> _setSlot;
    private readonly IEqualityComparer<TKey> _keyComparer;

    public CollectionDiffControlledPropEntry(
        Func<TElement, IReadOnlyList<TItem>> get,
        Func<TControl, IList<TItem>> getVector,
        Func<TItem, TKey> key,
        Action<TControl, TDelegate> subscribe,
        Func<TElement, Delegate?> callbackPresent,
        TDelegate trampoline,
        Func<TPayload, bool> slotIsNull,
        Action<TPayload, TDelegate> setSlot,
        IEqualityComparer<TKey>? keyComparer = null)
    {
        _get = get;
        _getVector = getVector;
        _key = key;
        _subscribe = subscribe;
        _callbackPresent = callbackPresent;
        _trampoline = trampoline;
        _slotIsNull = slotIsNull;
        _setSlot = setSlot;
        _keyComparer = keyComparer ?? EqualityComparer<TKey>.Default;
    }

    public override void Mount(TControl ctrl, TElement el)
    {
        var items = _get(el);
        var vec = _getVector(ctrl);
        // Initial fill — sibling subscriptions are not yet live, so the
        // per-mutation echo cannot fire. Skip the suppress block.
        if (vec.Count > 0) vec.Clear();
        for (int i = 0; i < items.Count; i++) vec.Add(items[i]);
    }

    public override void Update(TControl ctrl, TElement oldEl, TElement newEl)
    {
        var oldItems = _get(oldEl);
        var newItems = _get(newEl);

        // Fast path: same items, no work.
        if (ReferenceEquals(oldItems, newItems)) return;

        // Build new key set; track which old indices are still present.
        var newKeys = new HashSet<TKey>(newItems.Count, _keyComparer);
        for (int i = 0; i < newItems.Count; i++) newKeys.Add(_key(newItems[i]));

        var vec = _getVector(ctrl);
        ChangeEchoSuppressor.BeginSuppress(ctrl);

        // Remove items not in newKeys, in descending index order so earlier
        // indices stay stable.
        for (int i = vec.Count - 1; i >= 0; i--)
        {
            if (!newKeys.Contains(_key(vec[i])))
                vec.RemoveAt(i);
        }

        // Add items that aren't already present.
        var presentKeys = new HashSet<TKey>(vec.Count, _keyComparer);
        for (int i = 0; i < vec.Count; i++) presentKeys.Add(_key(vec[i]));
        for (int i = 0; i < newItems.Count; i++)
        {
            var k = _key(newItems[i]);
            if (presentKeys.Add(k)) vec.Add(newItems[i]);
        }
    }

    public override void EnsureSubscribed(
        ReactorBinding<TElement> binding,
        TControl ctrl,
        TElement el)
    {
        if (_callbackPresent(el) is null) return;
        var payload = Reconciler.GetOrCreateControlEventPayload<TPayload>(ctrl);
        if (!_slotIsNull(payload)) return;
        _setSlot(payload, _trampoline);
        _subscribe(ctrl, _trampoline);
    }
}
