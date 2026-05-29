using System;
using System.Diagnostics.CodeAnalysis;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 2) — descriptor variant of the hand-coded
/// <c>MountDatePicker</c> / <c>UpdateDatePicker</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Date</c> — controlled (<c>DateChanged</c> event,
///   <c>EventHandler&lt;DatePickerValueChangedEventArgs&gt;</c>,
///   <c>OnDateChanged</c> callback).</item>
///   <item><c>DayVisible</c>, <c>MonthVisible</c>, <c>YearVisible</c>,
///   <c>Orientation</c> — one-way.</item>
///   <item><c>Header</c>, <c>MinYear</c>, <c>MaxYear</c>, <c>DayFormat</c>,
///   <c>MonthFormat</c>, <c>YearFormat</c> — one-way conditional.</item>
/// </list></para>
/// </summary>
internal static class DatePickerDescriptor
{
    public static readonly ControlDescriptor<DatePickerElement, WinUI.DatePicker> Descriptor =
        new ControlDescriptor<DatePickerElement, WinUI.DatePicker>
        {
            Children = new None<DatePickerElement, WinUI.DatePicker>(),
            GetSetters = static e => e.Setters,
        }
        .Controlled<DateTimeOffset, WinUI.DatePickerValueChangedEventArgs>(
            get:         static e => e.Date,
            set:         static (c, v) => c.Date = v,
            // See ToggleSwitchDescriptor for the closure / CWT-gate invariant.
            subscribe:   static (fe, h) => ((WinUI.DatePicker)fe).DateChanged += (s, e) => h(s, e),
            unsubscribe: static (fe, h) => { /* trampoline lives for control lifetime — see CWT gate in PropEntry */ },
            callback:    static e => e.OnDateChanged,
            readBack:    static c => c.Date)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null)
        .OneWayConditional(
            get:         static e => e.MinYear,
            set:         static (c, v) => c.MinYear = v!.Value,
            shouldWrite: static e => e.MinYear.HasValue)
        .OneWayConditional(
            get:         static e => e.MaxYear,
            set:         static (c, v) => c.MaxYear = v!.Value,
            shouldWrite: static e => e.MaxYear.HasValue)
        .OneWay(
            get: static e => e.DayVisible,
            set: static (c, v) => c.DayVisible = v)
        .OneWay(
            get: static e => e.MonthVisible,
            set: static (c, v) => c.MonthVisible = v)
        .OneWay(
            get: static e => e.YearVisible,
            set: static (c, v) => c.YearVisible = v)
        .OneWayConditional(
            get:         static e => e.DayFormat,
            set:         static (c, v) => c.DayFormat = v,
            shouldWrite: static e => e.DayFormat is not null)
        .OneWayConditional(
            get:         static e => e.MonthFormat,
            set:         static (c, v) => c.MonthFormat = v,
            shouldWrite: static e => e.MonthFormat is not null)
        .OneWayConditional(
            get:         static e => e.YearFormat,
            set:         static (c, v) => c.YearFormat = v,
            shouldWrite: static e => e.YearFormat is not null)
        .OneWay(
            get: static e => e.Orientation,
            set: static (c, v) => c.Orientation = v);
}
