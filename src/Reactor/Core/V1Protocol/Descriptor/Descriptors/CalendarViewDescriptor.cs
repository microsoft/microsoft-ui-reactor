using System;
using System.Diagnostics.CodeAnalysis;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3-final Batch C — descriptor variant of the hand-coded
/// <c>MountCalendarView</c> / <c>UpdateCalendarView</c> arms in
/// <see cref="Reconciler"/>. Proof point for Batch A's new
/// <see cref="ControlDescriptor{TElement,TControl}.CollectionDiffControlled{TPayload,TItem,TKey,TDelegate}"/>
/// entry.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>SelectedDates</c> + <c>SelectedDatesChanged</c> — a single
///   <c>.CollectionDiffControlled</c> entry that mounts the vector bare and
///   diffs subsequent updates inside one <c>BeginSuppress</c> with per-mutation
///   echo gating. Items are keyed by <c>UtcTicks</c> so equivalent dates with
///   different <c>Offset</c>s collapse to one identity (matches the legacy
///   arm's hash-set-of-<c>DateTimeOffset</c> diff for the survivor set).</item>
///   <item><c>SelectionMode</c>, <c>IsGroupLabelVisible</c>,
///   <c>IsOutOfScopeEnabled</c>, <c>NumberOfWeeksInView</c>, <c>DisplayMode</c>
///   — plain <c>.OneWay</c> writes.</item>
///   <item><c>CalendarIdentifier</c>, <c>Language</c> (gated on
///   <c>Windows.Globalization.Language.IsWellFormed</c>), <c>MinDate</c>,
///   <c>MaxDate</c>, <c>FirstDayOfWeek</c> — <c>.OneWayConditional</c>
///   (skip-when-null), matching the legacy arm's null-guard shape.</item>
/// </list></para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item>The legacy <c>UpdateCalendarView</c> arm treats a <c>null</c>
///   <c>SelectedDates</c> on the new element as "uncontrolled — don't
///   reconcile" (so author can wire only <c>OnSelectedDatesChanged</c> and
///   never have their picks wiped by a re-render). The descriptor's
///   <c>.CollectionDiffControlled</c> entry projects <c>null</c> to
///   <see cref="Array.Empty{T}"/> on read, so an Update that flips from
///   non-null to null WILL clear the vector. The expected callsite pattern
///   on the descriptor variant is "always pass a list (even an empty one)
///   when you want controlled selection; never wire a non-null list once
///   and switch back to null".</item>
///   <item>The legacy <c>MountCalendarView</c> subscribes to
///   <c>SelectedDatesChanged</c> unconditionally so a later record-with
///   that attaches a previously-null <c>OnSelectedDatesChanged</c> wires
///   without re-mounting. The descriptor matches the established §14
///   "EnsureXxxWiring null-to-non-null" contract — subscription is gated
///   on the callback being present at Mount.</item>
/// </list></para>
/// </summary>
internal static class CalendarViewDescriptor
{
    // ── Trampoline ───────────────────────────────────────────────────
    // Captured-free. Reads the live element via Reconciler.GetElementTag,
    // gates on ChangeEchoSuppressor.ShouldSuppress, snapshots
    // c.SelectedDates and fires el.OnSelectedDatesChanged. Mirrors the
    // legacy MountCalendarView arm body.
    private static readonly TypedEventHandler<WinUI.CalendarView, WinUI.CalendarViewSelectedDatesChangedEventArgs>
        SelectedDatesChangedTrampoline = static (s, _) =>
        {
            var c = (WinUI.CalendarView)s!;
            if (ChangeEchoSuppressor.ShouldSuppress(c)) return;
            if (Reconciler.GetElementTag(c) is CalendarViewElement el && el.OnSelectedDatesChanged is { } h)
                h(c.SelectedDates.ToArray());
        };

    public static readonly ControlDescriptor<CalendarViewElement, WinUI.CalendarView> Descriptor =
        new ControlDescriptor<CalendarViewElement, WinUI.CalendarView>
        {
            Children = new None<CalendarViewElement, WinUI.CalendarView>(),
            GetSetters = static e => e.Setters,
        }
        // Simple .OneWay props.
        .OneWay(
            get: static e => e.SelectionMode,
            set: static (c, v) => c.SelectionMode = v)
        .OneWay(
            get: static e => e.IsGroupLabelVisible,
            set: static (c, v) => c.IsGroupLabelVisible = v)
        .OneWay(
            get: static e => e.IsOutOfScopeEnabled,
            set: static (c, v) => c.IsOutOfScopeEnabled = v)
        .OneWay(
            get: static e => e.NumberOfWeeksInView,
            set: static (c, v) => c.NumberOfWeeksInView = v)
        .OneWay(
            get: static e => e.DisplayMode,
            set: static (c, v) => c.DisplayMode = v)
        // Conditional (skip-when-null) props.
        .OneWayConditional(
            get:         static e => e.CalendarIdentifier,
            set:         static (c, v) => c.CalendarIdentifier = v!,
            shouldWrite: static e => e.CalendarIdentifier is not null)
        .OneWayConditional(
            get:         static e => e.Language,
            set:         static (c, v) => c.Language = v!,
            shouldWrite: static e => e.Language is not null
                                     && global::Windows.Globalization.Language.IsWellFormed(e.Language))
        .OneWayConditional(
            get:         static e => e.MinDate,
            set:         static (c, v) => c.MinDate = v!.Value,
            shouldWrite: static e => e.MinDate.HasValue)
        .OneWayConditional(
            get:         static e => e.MaxDate,
            set:         static (c, v) => c.MaxDate = v!.Value,
            shouldWrite: static e => e.MaxDate.HasValue)
        .OneWayConditional(
            get:         static e => e.FirstDayOfWeek,
            set:         static (c, v) => c.FirstDayOfWeek = v!.Value,
            shouldWrite: static e => e.FirstDayOfWeek.HasValue)
        // Two-way IObservableVector<DateTimeOffset> selection. Keyed by
        // UtcTicks so equivalent instants with different Offsets collapse
        // to one identity. Mount fills the vector bare; Update emits per-
        // item Add/Remove inside one BeginSuppress.
        .CollectionDiffControlled<
            CalendarViewEventPayload,
            DateTimeOffset,
            long,
            TypedEventHandler<WinUI.CalendarView, WinUI.CalendarViewSelectedDatesChangedEventArgs>>(
            get:             static e => e.SelectedDates ?? Array.Empty<DateTimeOffset>(),
            getVector:       static c => c.SelectedDates,
            key:             static d => d.UtcTicks,
            subscribe:       static (c, h) => c.SelectedDatesChanged += h,
            callbackPresent: static e => e.OnSelectedDatesChanged,
            trampoline:      SelectedDatesChangedTrampoline,
            slotIsNull:      static p => p.SelectedDatesChangedTrampoline is null,
            setSlot:         static (p, h) => p.SelectedDatesChangedTrampoline = h);
}
