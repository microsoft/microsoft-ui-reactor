using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3-final Batch B — descriptor variant of the hand-coded
/// <c>MountNumberBox</c> / <c>UpdateNumberBox</c> arms in
/// <see cref="Reconciler"/>. First proof point for the <c>.Immediate</c>
/// entry shape (Batch A).
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Value</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   with a typed <see cref="TypedEventHandler{TSender, TResult}"/>
///   trampoline against <c>ValueChanged</c>. Suppresses programmatic-write
///   echo via <see cref="ChangeEchoSuppressor"/>.</item>
///   <item>Per-keystroke immediate-mode observation —
///   <see cref="ControlDescriptor{TElement,TControl}.Immediate{TPayload}"/>
///   entry against the NumberBox's <c>TextProperty</c> change callback PLUS
///   a <c>Loaded</c> hook that finds the inner template <c>TextBox</c> and
///   subscribes its <c>TextChanged</c>. Both call into the shared
///   <c>Reconciler.HandleNumberBoxImmediateTextChanged</c> helper, which the
///   legacy mount arm also uses.</item>
///   <item><c>Minimum</c>, <c>Maximum</c>, <c>SmallChange</c>,
///   <c>LargeChange</c>, <c>SpinButtonPlacement</c>, <c>AcceptsExpression</c>,
///   <c>ValidationMode</c>, <c>PlaceholderText</c>, <c>NumberFormatter</c>,
///   <c>Description</c>, <c>Header</c> — simple <c>.OneWay</c> /
///   <c>.OneWayConditional</c> matching the legacy guards.</item>
/// </list></para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item>Min/Max coercion suppression — the legacy
///   <c>UpdateNumberBox</c> arm runs <c>ChangeEchoSuppressor.BeginSuppress</c>
///   when the Value-into-new-range coercion is about to fire ValueChanged.
///   The descriptor's plain <c>.OneWay</c> Min/Max entries do not — coercion
///   echoes through <c>OnValueChanged</c>. Acceptable for now (matches the
///   broader §14 thesis that coercion-tolerance is a hand-coded nuance);
///   the <c>.CoercingOneWay</c> entry shape ships separately for callers
///   that need it.</item>
///   <item>Immediate-mode "skip Value write when typed text is non-canonical"
///   — the legacy <c>UpdateNumberBox</c> arm preserves in-progress text like
///   "1." or "2.0". The descriptor's <c>.HandCodedControlled</c> Value entry
///   does not consult <c>nb.Text</c> before writing, so a same-value re-render
///   while the user is mid-typing may reformat. Acceptable — the
///   ImmediateValueAttached attached-prop case stays on V1 OFF for that
///   edge until a future entry shape lands.</item>
/// </list></para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class NumberBoxDescriptor
{
    private static readonly TypedEventHandler<WinUI.NumberBox, WinUI.NumberBoxValueChangedEventArgs>
        ValueChangedTrampoline = (s, _) =>
        {
            var box = (WinUI.NumberBox)s!;
            if (ChangeEchoSuppressor.ShouldSuppress(box)) return;
            (Reconciler.GetElementTag(box) as NumberBoxElement)?.OnValueChanged?.Invoke(box.Value);
        };

    public static readonly ControlDescriptor<NumberBoxElement, WinUI.NumberBox> Descriptor =
        new ControlDescriptor<NumberBoxElement, WinUI.NumberBox>
        {
            Children = new None<NumberBoxElement, WinUI.NumberBox>(),
            GetSetters = static e => e.Setters,
        }
        // Min/Max BEFORE Value so a fresh in-range Value isn't coerced by a
        // stale range. Matches the hand-coded arm's ordering invariant.
        .OneWay(
            get: static e => e.Minimum,
            set: static (c, v) => c.Minimum = v)
        .OneWay(
            get: static e => e.Maximum,
            set: static (c, v) => c.Maximum = v)
        .HandCodedControlled<NumberBoxEventPayload, double,
            TypedEventHandler<WinUI.NumberBox, WinUI.NumberBoxValueChangedEventArgs>>(
            get:         static e => e.Value,
            set:         static (c, v) => c.Value = v,
            readBack:    static c => c.Value,
            subscribe:   static (c, h) => c.ValueChanged += h,
            callback:    static e => e.OnValueChanged,
            trampoline:  ValueChangedTrampoline,
            slotIsNull:  static p => p.ValueChangedTrampoline is null,
            setSlot:     static (p, h) => p.ValueChangedTrampoline = h)
        // Per-keystroke observation against TextProperty + Loaded → inner
        // TextBox. Reuses the legacy mount arm's captured-free trampolines
        // (widened to internal static for descriptor sharing). Gate is the
        // SAME element callback the commit-mode entry above uses — the
        // shared helper additionally gates on the ImmediateValueAttached
        // attached-prop being present.
        .Immediate<NumberBoxEventPayload>(
            callbackGate:       static e => e.OnValueChanged,
            observeProperty:    WinUI.NumberBox.TextProperty,
            observeCallback:    Reconciler.NumberBoxImmediateTextChanged,
            observeSlotIsNull:  static p => p.ImmediateTextChangedCallback is null,
            setObserveSlot:     static (p, h) => p.ImmediateTextChangedCallback = h,
            loadedHook:         Reconciler.NumberBoxLoadedEnsureImmediateTextBox)
        .OneWay(
            get: static e => e.SmallChange,
            set: static (c, v) => c.SmallChange = v)
        .OneWay(
            get: static e => e.LargeChange,
            set: static (c, v) => c.LargeChange = v)
        .OneWay(
            get: static e => e.PlaceholderText ?? "",
            set: static (c, v) => c.PlaceholderText = v)
        .OneWay(
            get: static e => e.SpinButtonPlacement,
            set: static (c, v) => c.SpinButtonPlacementMode = v)
        .OneWay(
            get: static e => e.AcceptsExpression,
            set: static (c, v) => c.AcceptsExpression = v)
        .OneWay(
            get: static e => e.ValidationMode,
            set: static (c, v) => c.ValidationMode = v)
        .OneWayConditional(
            get:         static e => e.NumberFormatter,
            set:         static (c, v) => c.NumberFormatter = v,
            shouldWrite: static e => e.NumberFormatter is not null)
        .OneWayConditional(
            get:         static e => e.Description,
            set:         static (c, v) => c.Description = v,
            shouldWrite: static e => e.Description is not null)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null);
}
