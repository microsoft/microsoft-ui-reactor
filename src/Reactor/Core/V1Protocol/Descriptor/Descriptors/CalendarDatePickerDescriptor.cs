using System;
using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 2) — descriptor variant of the hand-coded
/// <c>MountCalendarDatePicker</c> / <c>UpdateCalendarDatePicker</c> arms
/// in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Date</c> (nullable <c>DateTimeOffset?</c>) — controlled
///   (<c>DateChanged</c> event,
///   <c>TypedEventHandler&lt;CalendarDatePicker,
///   CalendarDatePickerDateChangedEventArgs&gt;</c> bridged to
///   <c>EventHandler&lt;TArgs&gt;</c>, <c>OnDateChanged</c> callback).
///   The legacy arm reads back <c>c.Date</c> rather than args; the
///   descriptor mirrors that read-back.</item>
///   <item><c>PlaceholderText</c> — one-way (defaults to <c>""</c> matching
///   the hand-coded port).</item>
///   <item><c>Header</c>, <c>MinDate</c>, <c>MaxDate</c>, <c>DateFormat</c>
///   — one-way conditional (gated on non-null).</item>
///   <item><c>IsTodayHighlighted</c>, <c>IsGroupLabelVisible</c>,
///   <c>IsCalendarOpen</c> — one-way.</item>
/// </list></para>
///
/// <para><b>Note on Update parity:</b> the legacy <c>UpdateCalendarDatePicker</c>
/// did not re-write <c>Header</c>, <c>PlaceholderText</c>, <c>MinDate</c>,
/// or <c>MaxDate</c> on subsequent renders (effectively initial-only). The
/// descriptor's <c>OneWay</c> / <c>OneWayConditional</c> entries DO re-write
/// these on Update when the element value changes — a behavioral
/// improvement that surfaces value changes from element re-renders.</para>
/// </summary>
internal static class CalendarDatePickerDescriptor
{
    public static readonly ControlDescriptor<CalendarDatePickerElement, WinUI.CalendarDatePicker> Descriptor =
        new ControlDescriptor<CalendarDatePickerElement, WinUI.CalendarDatePicker>
        {
            Children = new None<CalendarDatePickerElement, WinUI.CalendarDatePicker>(),
            GetSetters = static e => e.Setters,
        }
        .Controlled<DateTimeOffset?, WinUI.CalendarDatePickerDateChangedEventArgs>(
            get:         static e => e.Date,
            set:         static (c, v) => c.Date = v,
            // See ToggleSwitchDescriptor for the closure / CWT-gate invariant.
            subscribe:   static (fe, h) => ((WinUI.CalendarDatePicker)fe).DateChanged += (s, e) => h(s, e),
            unsubscribe: static (fe, h) => { /* trampoline lives for control lifetime — see CWT gate in PropEntry */ },
            callback:    static e => e.OnDateChanged,
            readBack:    static c => c.Date)
        .OneWay(
            get: static e => e.PlaceholderText ?? "",
            set: static (c, v) => c.PlaceholderText = v)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null)
        .OneWayConditional(
            get:         static e => e.MinDate,
            set:         static (c, v) => c.MinDate = v!.Value,
            shouldWrite: static e => e.MinDate.HasValue)
        .OneWayConditional(
            get:         static e => e.MaxDate,
            set:         static (c, v) => c.MaxDate = v!.Value,
            shouldWrite: static e => e.MaxDate.HasValue)
        .OneWayConditional(
            get:         static e => e.DateFormat,
            set:         static (c, v) => c.DateFormat = v,
            shouldWrite: static e => e.DateFormat is not null)
        .OneWay(
            get: static e => e.IsTodayHighlighted,
            set: static (c, v) => c.IsTodayHighlighted = v)
        .OneWay(
            get: static e => e.IsGroupLabelVisible,
            set: static (c, v) => c.IsGroupLabelVisible = v)
        .OneWay(
            get: static e => e.IsCalendarOpen,
            set: static (c, v) => c.IsCalendarOpen = v);
}
