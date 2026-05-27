using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 2 (Q1 spike) — descriptor variant of
/// <see cref="V1Protocol.Handlers.SliderHandler"/>.
///
/// <para>The hardest of the three Q1 head-to-head ports — exercises the
/// coercion-tolerance entry shape (§8 audit). <c>Slider.Value</c> is the
/// controlled prop; <c>Minimum</c> and <c>Maximum</c> are one-way writes
/// that may coerce <c>Value</c> when the current control value falls
/// outside the new range, raising <c>ValueChanged</c>. The descriptor
/// declares this via <see cref="ControlDescriptor{TElement,TControl}.CoercingOneWay"/>
/// — the interpreter applies <c>WriteSuppressed</c> only when the predicate
/// says the write would coerce. Mirrors the imperative
/// <c>if (ctrl.Value &lt; n.Min) WriteSuppressed(...)</c> pattern in the
/// hand-coded port.</para>
///
/// <para><b>Order matters:</b> Min before Max before Value before the
/// remaining one-ways. Matches the hand-coded port's invariant — a stale
/// Min/Max would coerce a fresh in-range Value. Property entry list order
/// IS the execution order.</para>
///
/// <para><b>Pool note:</b> WinUI.Slider is not in <c>PoolableTypes</c> by
/// default, so descriptor mounts allocate a fresh control each time
/// (matches the hand-coded port).</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class SliderDescriptor
{
    public static readonly ControlDescriptor<SliderElement, WinUI.Slider> Descriptor =
        new ControlDescriptor<SliderElement, WinUI.Slider>
        {
            Children = new None<SliderElement, WinUI.Slider>(),
            GetSetters = static e => e.Setters,
        }
        // Order matters — Min/Max before Value, Value before the rest, so
        // a stale range can't coerce a fresh Value at Mount.
        .CoercingOneWay(
            get:                static e => e.Min,
            set:                static (c, v) => c.Minimum = v,
            coercesController:  static (c, newMin) => c.Value < newMin)
        .CoercingOneWay(
            get:                static e => e.Max,
            set:                static (c, v) => c.Maximum = v,
            coercesController:  static (c, newMax) => c.Value > newMax)
        .Controlled<double, RangeBaseValueChangedEventArgs>(
            get:         static e => e.Value,
            set:         static (c, v) => c.Value = v,
            // See ToggleSwitchDescriptor for the closure / CWT-gate invariant.
            subscribe:   static (fe, h) => ((WinUI.Slider)fe).ValueChanged += (s, e) => h(s, e),
            unsubscribe: static (fe, h) => { /* trampoline lives for control lifetime — see CWT gate in PropEntry */ },
            callback:    static e => e.OnValueChanged,
            readBack:    static c => c.Value)
        .OneWay(
            get: static e => e.StepFrequency,
            set: static (c, v) => c.StepFrequency = v)
        .OneWay(
            get: static e => e.Orientation,
            set: static (c, v) => c.Orientation = v)
        .OneWay(
            get: static e => e.TickFrequency,
            set: static (c, v) => c.TickFrequency = v)
        .OneWay(
            get: static e => e.TickPlacement,
            set: static (c, v) => c.TickPlacement = v)
        .OneWay(
            get: static e => e.SnapsTo,
            set: static (c, v) => c.SnapsTo = v)
        .OneWay(
            get: static e => e.IsThumbToolTipEnabled,
            set: static (c, v) => c.IsThumbToolTipEnabled = v)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null);
}
