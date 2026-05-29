using System;
using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 2) — descriptor variant of the hand-coded
/// <c>MountTimePicker</c> / <c>UpdateTimePicker</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Time</c> — controlled (<c>TimeChanged</c> event,
///   <c>EventHandler&lt;TimePickerValueChangedEventArgs&gt;</c>,
///   <c>OnTimeChanged</c> callback).</item>
///   <item><c>MinuteIncrement</c>, <c>ClockIdentifier</c> — one-way. The
///   legacy Mount arm doesn't set <c>ClockIdentifier</c> directly; the
///   descriptor writes it as a normal one-way so element changes flow
///   through.</item>
///   <item><c>Header</c> — one-way conditional.</item>
/// </list></para>
/// </summary>
internal static class TimePickerDescriptor
{
    public static readonly ControlDescriptor<TimePickerElement, WinUI.TimePicker> Descriptor =
        new ControlDescriptor<TimePickerElement, WinUI.TimePicker>
        {
            Children = new None<TimePickerElement, WinUI.TimePicker>(),
            GetSetters = static e => e.Setters,
        }
        .Controlled<TimeSpan, WinUI.TimePickerValueChangedEventArgs>(
            get:         static e => e.Time,
            set:         static (c, v) => c.Time = v,
            // See ToggleSwitchDescriptor for the closure / CWT-gate invariant.
            subscribe:   static (fe, h) => ((WinUI.TimePicker)fe).TimeChanged += (s, e) => h(s, e),
            unsubscribe: static (fe, h) => { /* trampoline lives for control lifetime — see CWT gate in PropEntry */ },
            callback:    static e => e.OnTimeChanged,
            readBack:    static c => c.Time)
        .OneWay(
            get: static e => e.MinuteIncrement,
            set: static (c, v) => c.MinuteIncrement = v)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null);
}
