using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (3.0.2) — descriptor variant of
/// <see cref="V1Protocol.Handlers.TextBoxHandler"/>. First proof point for
/// the <c>HandCodedControlled</c> / <c>HandCodedEvent</c> escape-hatch
/// builders shipped in 3.0.1.
///
/// <para><b>Why hand-coded:</b> TextBox round-trips one DP (<c>Text</c> via
/// <c>TextChanged</c>) AND raises a second control-intrinsic event
/// (<c>SelectionChanged</c>) with no DP round-trip. The single-event
/// fast-path <c>DescriptorControlledPayload</c> cannot host both — two
/// instances would collide on the same closed-generic per-control payload
/// type. The HandCoded* path reuses
/// <see cref="V1Protocol.TextBoxEventPayload"/> (which already has a slot
/// per event) and lets the descriptor address each slot individually.</para>
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Text</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   with <c>TextChangedEventHandler</c> trampoline; round-trips against
///   <c>OnChanged</c>.</item>
///   <item><c>SelectionChanged</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
///   with <c>RoutedEventHandler</c> trampoline; fire-only against
///   <c>OnSelectionChanged</c>.</item>
///   <item>Remaining props mirror the hand-coded port's classification —
///   <c>PlaceholderText</c> OneWay, <c>Header</c> / <c>IsReadOnly</c> /
///   <c>AcceptsReturn</c> / <c>TextWrapping</c> / <c>IsSpellCheckEnabled</c> /
///   <c>Description</c> OneWayConditional, <c>MaxLength</c> /
///   <c>CharacterCasing</c> / <c>TextAlignment</c> OneWay with predicate
///   gates.</item>
/// </list></para>
///
/// <para><b>Known gap vs. hand-coded handler:</b> the descriptor does not
/// request a rerender from the <c>TextChanged</c> trampoline. The
/// hand-coded handler does this for controlled-mode snap-back (when a user
/// callback returns the same <c>Value</c> after filtering input, the
/// framework would otherwise skip Update and leave the typed text in the
/// box). The descriptor's Update will still enforce the controlled value
/// on element-change OR control-drift when Update DOES run — the gap is
/// only the auto-trigger of Update for snap-back. Acceptable for the
/// Phase 3 proof point and matches the broader §14 thesis that the
/// hand-coded escape hatch retains nuances the declarative path doesn't
/// (yet).</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class TextBoxDescriptor
{
    // Static trampolines — captured-free, allocated once per process.
    // Each reads the live element via Reconciler.GetElementTag on every fire
    // so the same trampoline serves a pool-rented control through any
    // element identity change.

    private static readonly WinUI.TextChangedEventHandler TextChangedTrampoline = (s, _) =>
    {
        var tb = (WinUI.TextBox)s!;
        if (ChangeEchoSuppressor.ShouldSuppress(tb)) return;
        (Reconciler.GetElementTag(tb) as TextBoxElement)?.OnChanged?.Invoke(tb.Text);
    };

    private static readonly RoutedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var tb = (WinUI.TextBox)s!;
        (Reconciler.GetElementTag(tb) as TextBoxElement)
            ?.OnSelectionChanged?.Invoke(tb.SelectedText, tb.SelectionStart, tb.SelectionLength);
    };

    public static readonly ControlDescriptor<TextBoxElement, WinUI.TextBox> Descriptor =
        new ControlDescriptor<TextBoxElement, WinUI.TextBox>
        {
            Children = new None<TextBoxElement, WinUI.TextBox>(),
            GetSetters = static e => e.Setters,
        }
        // AcceptsReturn / TextWrapping BEFORE Text — single-line mode strips
        // embedded \r\n on the Text assignment (matches the hand-coded
        // handler's ordering invariant).
        .OneWayConditional(
            get:         static e => e.AcceptsReturn,
            set:         static (c, v) => c.AcceptsReturn = v!.Value,
            shouldWrite: static e => e.AcceptsReturn.HasValue)
        .OneWayConditional(
            get:         static e => e.TextWrapping,
            set:         static (c, v) => c.TextWrapping = v!.Value,
            shouldWrite: static e => e.TextWrapping.HasValue)
        .HandCodedControlled<TextBoxEventPayload, string, WinUI.TextChangedEventHandler>(
            get:         static e => e.Value,
            set:         static (c, v) => c.Text = v,
            readBack:    static c => c.Text,
            subscribe:   static (c, h) => c.TextChanged += h,
            callback:    static e => e.OnChanged,
            trampoline:  TextChangedTrampoline,
            slotIsNull:  static p => p.TextChangedTrampoline is null,
            setSlot:     static (p, h) => p.TextChangedTrampoline = h)
        .HandCodedEvent<TextBoxEventPayload, RoutedEventHandler>(
            subscribe:        static (c, h) => c.SelectionChanged += h,
            callbackPresent:  static e => e.OnSelectionChanged,
            trampoline:       SelectionChangedTrampoline,
            slotIsNull:       static p => p.SelectionChangedTrampoline is null,
            setSlot:          static (p, h) => p.SelectionChangedTrampoline = h)
        .OneWay(
            get: static e => e.PlaceholderText ?? "",
            set: static (c, v) => c.PlaceholderText = v)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null)
        .OneWayConditional(
            get:         static e => e.IsReadOnly,
            set:         static (c, v) => c.IsReadOnly = v!.Value,
            shouldWrite: static e => e.IsReadOnly == true)
        .OneWayConditional(
            get:         static e => e.SelectionStart,
            set:         static (c, v) => c.SelectionStart = v!.Value,
            shouldWrite: static e => e.SelectionStart.HasValue)
        .OneWayConditional(
            get:         static e => e.SelectionLength,
            set:         static (c, v) => c.SelectionLength = v!.Value,
            shouldWrite: static e => e.SelectionLength.HasValue)
        .OneWayConditional(
            get:         static e => e.MaxLength,
            set:         static (c, v) => c.MaxLength = v,
            shouldWrite: static e => e.MaxLength != 0)
        .OneWayConditional(
            get:         static e => e.IsSpellCheckEnabled,
            set:         static (c, v) => c.IsSpellCheckEnabled = v!.Value,
            shouldWrite: static e => e.IsSpellCheckEnabled.HasValue)
        .OneWayConditional(
            get:         static e => e.CharacterCasing,
            set:         static (c, v) => c.CharacterCasing = v,
            shouldWrite: static e => e.CharacterCasing != WinUI.CharacterCasing.Normal)
        .OneWayConditional(
            get:         static e => e.TextAlignment,
            set:         static (c, v) => c.TextAlignment = v,
            shouldWrite: static e => e.TextAlignment != TextAlignment.Left)
        .OneWayConditional(
            get:         static e => e.Description,
            set:         static (c, v) => c.Description = v,
            shouldWrite: static e => e.Description is not null);
}
