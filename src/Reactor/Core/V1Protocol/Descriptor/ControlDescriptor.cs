using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;

/// <summary>
/// Spec 047 §6 / §14 Phase 2 (Q1 spike) — declarative control description.
///
/// <para>A descriptor is the data-driven alternative to a hand-coded
/// <see cref="IElementHandler{TElement,TControl}"/>. Authors declare
/// property bindings (<see cref="OneWay{TValue}"/>, <see cref="Initial{TValue}"/>,
/// <see cref="Controlled{TValue,TArgs}"/>), an optional children strategy,
/// and an optional factory; the interpreter
/// (<see cref="DescriptorHandler{TElement,TControl}"/>) executes them
/// against the same v1 protocol surface a hand-coded handler would use.</para>
///
/// <para><b>Phase 2 measurement gate (§13 Q1):</b> the descriptor and the
/// hand-coded handler shapes are evaluated head-to-head on the same M1 /
/// M2 / M5 / M7 / M10 micro-benches and the L4 / L9 macros, then the §13 Q1
/// decision matrix determines which surface ships as the primary author
/// path. See <c>docs/specs/047-extensible-control-model.md</c> §13 Q1.</para>
///
/// <para><b>Fluent author pattern:</b>
/// <code>
/// public static readonly ControlDescriptor&lt;ToggleSwitchElement, ToggleSwitch&gt; Descriptor =
///     new ControlDescriptor&lt;ToggleSwitchElement, ToggleSwitch&gt;()
///         .OneWay(e => e.OnContent, (c, v) => c.OnContent = v)
///         .OneWayConditional(e => e.Header, (c, v) => c.Header = v, e => e.Header is not null)
///         .Controlled&lt;bool, RoutedEventArgs&gt;(
///             get:          e => e.IsOn,
///             set:          (c, v) => c.IsOn = v,
///             subscribe:    (fe, h) => ((ToggleSwitch)fe).Toggled += (s, e) => h(s, e),
///             unsubscribe:  (fe, h) => { /* trampoline lives for control lifetime */ },
///             callback:     e => e.OnIsOnChanged,
///             readBack:     c => c.IsOn);
/// </code></para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
public sealed class ControlDescriptor<TElement, TControl>
    where TElement : Element
    where TControl : FrameworkElement, new()
{
    private readonly List<PropEntry<TElement, TControl>> _properties = new();

    /// <summary>Optional factory the engine invokes when the pool is empty
    /// or <see cref="PoolPolicy"/> opts out. Defaults to <c>new TControl()</c>
    /// via the <c>new()</c> constraint.</summary>
    public Func<TControl>? Factory { get; init; }

    /// <summary>Optional pool policy. When null the engine uses the default
    /// (poolable if the control type opts in via <c>PoolableTypes</c>).</summary>
    public PoolPolicy<TControl>? PoolPolicy { get; init; }

    /// <summary>Optional children strategy. Engine dispatches through it via
    /// <see cref="V1HandlerAdapter{TElement,TControl}"/> after each Mount /
    /// Update body returns. Defaults to <c>null</c> (treated as
    /// <see cref="None{TElement,TControl}"/> by the adapter).</summary>
    public ChildrenStrategy<TElement, TControl>? Children { get; init; }

    /// <summary><c>Setters</c> selector — the descriptor needs an opaque way
    /// to pull the element's <c>Action&lt;TControl&gt;[]</c> setters chain
    /// because each element subclass declares its own <c>Setters</c> property
    /// (no common base interface). Authors register
    /// <c>e =&gt; e.Setters</c>; the interpreter passes the result to
    /// <c>Reconciler.ApplySetters</c> after the property entries have run.
    /// Defaults to "no setters" if not configured.</summary>
    public Func<TElement, Action<TControl>[]>? GetSetters { get; init; }

    /// <summary>Read-only view of the property entries declared on this
    /// descriptor (in declaration order).</summary>
    public IReadOnlyList<PropEntry<TElement, TControl>> Properties => _properties;

    // ── Fluent builders ──────────────────────────────────────────────────

    /// <summary>Add a one-way property binding (write on Mount, diff-and-write
    /// on Update). Engine never subscribes to a change event for this prop.
    /// Use for control props the framework writes but the user never
    /// subscribes to (Header text, brushes, layout values).</summary>
    public ControlDescriptor<TElement, TControl> OneWay<TValue>(
        Func<TElement, TValue> get,
        Action<TControl, TValue> set,
        IEqualityComparer<TValue>? comparer = null)
    {
        _properties.Add(new OneWayPropEntry<TElement, TControl, TValue>(get, set, comparer));
        return this;
    }

    /// <summary>Add a one-way property with a "should I write?" predicate.
    /// The write is skipped when the predicate returns false — useful for
    /// nullable props where leaving the control at its default is the
    /// intent (e.g. <c>Border.Background</c> when the element has no
    /// background set).</summary>
    public ControlDescriptor<TElement, TControl> OneWayConditional<TValue>(
        Func<TElement, TValue> get,
        Action<TControl, TValue> set,
        Func<TElement, bool> shouldWrite,
        IEqualityComparer<TValue>? comparer = null)
    {
        _properties.Add(new OneWayConditionalPropEntry<TElement, TControl, TValue>(get, set, shouldWrite, comparer));
        return this;
    }

    /// <summary>Add an initial-only property binding (write on Mount, never
    /// on Update). Use for seed-only props (e.g. <c>TextBox.InitialText</c>).</summary>
    public ControlDescriptor<TElement, TControl> Initial<TValue>(
        Func<TElement, TValue> get,
        Action<TControl, TValue> set)
    {
        _properties.Add(new InitialPropEntry<TElement, TControl, TValue>(get, set));
        return this;
    }

    /// <summary>Add a controlled (two-way) property binding. The engine
    /// writes from element state with echo suppression on Update; the
    /// control raises <paramref name="subscribe"/>'s event on user
    /// interaction; the trampoline invokes the element's
    /// <paramref name="callback"/> with <paramref name="readBack"/>'s value.
    ///
    /// <para><b>Callback gating:</b> if <paramref name="callback"/> returns
    /// <c>null</c> on the element passed at Mount, no subscription happens
    /// at all — mirrors the hand-coded <c>Ensure*Wiring</c> gate.</para>
    /// </summary>
    public ControlDescriptor<TElement, TControl> Controlled<TValue, TArgs>(
        Func<TElement, TValue> get,
        Action<TControl, TValue> set,
        Action<FrameworkElement, EventHandler<TArgs>> subscribe,
        Action<FrameworkElement, EventHandler<TArgs>> unsubscribe,
        Func<TElement, Action<TValue>?> callback,
        Func<TControl, TValue> readBack,
        IEqualityComparer<TValue>? comparer = null)
    {
        _properties.Add(new ControlledPropEntry<TElement, TControl, TValue, TArgs>(
            get, set, subscribe, unsubscribe, callback, readBack, comparer));
        return this;
    }

    /// <summary>§14 Phase 3 (3.0.1) — escape-hatch builder for controlled
    /// (two-way) props on a control that needs a hand-authored trampoline
    /// against a user-supplied per-control event payload
    /// (<typeparamref name="TPayload"/>). Required for multi-event controls
    /// (e.g. <c>TextBox.Text</c> alongside <c>SelectionChanged</c>) where
    /// the single-event fast-path <c>DescriptorControlledPayload</c> would
    /// collide if two controlled entries shared the same closed generic.
    ///
    /// <para>The payload type is typically reused from
    /// <c>ControlEventPayloads.cs</c> (e.g. <c>TextBoxEventPayload</c>)
    /// which already has a slot per event.
    /// <paramref name="slotIsNull"/> + <paramref name="setSlot"/> address
    /// the specific slot on the payload this entry owns;
    /// <paramref name="subscribe"/> wires the user-supplied native
    /// <typeparamref name="TDelegate"/> directly to the control's event
    /// (no <c>EventHandler&lt;TArgs&gt;</c> bridge closure).</para>
    ///
    /// <para>Mount/Update prop-write semantics are identical to
    /// <see cref="Controlled{TValue,TArgs}"/>: bare initial write,
    /// suppressed write on element-change OR control-drift. Subscription is
    /// gated on <paramref name="callback"/> returning non-null on the
    /// mounted element.</para></summary>
    public ControlDescriptor<TElement, TControl> HandCodedControlled<TPayload, TValue, TDelegate>(
        Func<TElement, TValue> get,
        Action<TControl, TValue> set,
        Func<TControl, TValue> readBack,
        Action<TControl, TDelegate> subscribe,
        Func<TElement, Action<TValue>?> callback,
        TDelegate trampoline,
        Func<TPayload, bool> slotIsNull,
        Action<TPayload, TDelegate> setSlot,
        IEqualityComparer<TValue>? comparer = null)
        where TPayload : class, new()
        where TDelegate : Delegate
    {
        _properties.Add(new HandCodedControlledPropEntry<TElement, TControl, TPayload, TValue, TDelegate>(
            get, set, readBack, subscribe, callback, trampoline, slotIsNull, setSlot, comparer));
        return this;
    }

    /// <summary>§14 Phase 3 (3.0.1) — escape-hatch builder for
    /// fire-and-forget control-intrinsic events that do not round-trip a DP
    /// (e.g. <c>TextBox.SelectionChanged</c>, <c>Image.ImageOpened</c>).
    /// Authors supply a per-control payload type and a native trampoline
    /// delegate; the entry wires subscription with the same payload-slot
    /// gating shape as <see cref="HandCodedControlled{TPayload,TValue,TDelegate}"/>
    /// but performs no Mount/Update prop writes.
    ///
    /// <para>Subscription is gated on <paramref name="callbackPresent"/>
    /// returning non-null — the element's callback shape can be anything
    /// (e.g. <c>Action&lt;string, int, int&gt;</c>) so the gate is checked
    /// against the untyped <c>Delegate</c>.</para></summary>
    public ControlDescriptor<TElement, TControl> HandCodedEvent<TPayload, TDelegate>(
        Action<TControl, TDelegate> subscribe,
        Func<TElement, Delegate?> callbackPresent,
        TDelegate trampoline,
        Func<TPayload, bool> slotIsNull,
        Action<TPayload, TDelegate> setSlot)
        where TPayload : class, new()
        where TDelegate : Delegate
    {
        _properties.Add(new HandCodedEventPropEntry<TElement, TControl, TPayload, TDelegate>(
            subscribe, callbackPresent, trampoline, slotIsNull, setSlot));
        return this;
    }

    /// <summary>Add a one-way property whose write may coerce a sibling
    /// controlled prop's value (e.g. <c>Slider.Minimum</c> coercing
    /// <c>Slider.Value</c>). When <paramref name="coercesController"/>
    /// returns true the write is wrapped in
    /// <c>ReactorBinding.WriteSuppressed</c> so the coercion-driven change
    /// event is dropped.
    ///
    /// <para>Declarative replacement for the imperative
    /// <c>if (ctrl.Value &lt; n.Min) WriteSuppressed(...)</c> pattern in the
    /// hand-coded <c>SliderHandler</c>. Encodes the §8 audit's
    /// "coercion-tolerance" treatment for the 8 audited coercion sites
    /// (NumberBox / Slider Min/Max etc.) as a single declarative entry.</para>
    /// </summary>
    public ControlDescriptor<TElement, TControl> CoercingOneWay<TValue>(
        Func<TElement, TValue> get,
        Action<TControl, TValue> set,
        Func<TControl, TValue, bool> coercesController,
        IEqualityComparer<TValue>? comparer = null)
    {
        _properties.Add(new CoercingOneWayPropEntry<TElement, TControl, TValue>(get, set, coercesController, comparer));
        return this;
    }
}
