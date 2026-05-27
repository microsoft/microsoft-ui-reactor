using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 6) — descriptor variant of the hand-coded
/// <c>MountComboBox</c> / <c>UpdateComboBox</c> arms in
/// <see cref="Reconciler"/>. Multi-event port that mixes the controlled
/// SelectedIndex round-trip with two fire-only DropDown events.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>SelectedIndex</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   with <c>SelectionChangedEventHandler</c> trampoline. Trampoline
///   gates on <c>ChangeEchoSuppressor.ShouldSuppress</c>; HandCodedControlled
///   wraps the programmatic write in <c>WriteSuppressed</c>.</item>
///   <item><c>DropDownOpened</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedEvent{TPayload,TDelegate}"/>
///   with <c>EventHandler&lt;object&gt;</c> trampoline (ComboBox's
///   DropDownOpened/Closed events are plain <c>EventHandler&lt;object&gt;</c>).</item>
///   <item><c>DropDownClosed</c> — same shape as DropDownOpened.</item>
///   <item><c>Header</c>, <c>PlaceholderText</c>, <c>IsEditable</c>,
///   <c>MaxDropDownHeight</c>, <c>Description</c> — one-way / one-way
///   conditional per the legacy guards.</item>
/// </list></para>
///
/// <para><b>Known gaps vs. hand-coded handler — Items collection
/// escape-hatched:</b> the descriptor does NOT touch <c>cb.Items</c>.
/// ComboBox's items collection requires the legacy arm's full
/// mode-switch logic (string[] vs Element[] keyed reconciliation
/// against <c>requestRerender</c>) — none of which the descriptor
/// builders can yet express. Authors who need ComboBox items must
/// either:
/// <list type="bullet">
///   <item>Run under V1 OFF (legacy arm handles Items + SelectedIndex
///   together).</item>
///   <item>Populate <c>cb.Items</c> via a <c>.Set</c> setter chain (the
///   setter runs after the descriptor's prop writes and can append items
///   directly — this trades reconciliation for an imperative escape).</item>
/// </list>
/// The fixture exercises SelectedIndex / Header / DropDownOpened/Closed
/// only — items are pre-populated via the setter chain to prove the
/// descriptor's SelectedIndex write coordinates with a populated list.</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class ComboBoxDescriptor
{
    private static readonly WinUI.SelectionChangedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var cb = (WinUI.ComboBox)s!;
        if (ChangeEchoSuppressor.ShouldSuppress(cb)) return;
        (Reconciler.GetElementTag(cb) as ComboBoxElement)
            ?.OnSelectedIndexChanged?.Invoke(cb.SelectedIndex);
    };

    private static readonly global::System.EventHandler<object> DropDownOpenedTrampoline = (s, _) =>
        (Reconciler.GetElementTag((WinUI.ComboBox)s!) as ComboBoxElement)?.OnDropDownOpened?.Invoke();

    private static readonly global::System.EventHandler<object> DropDownClosedTrampoline = (s, _) =>
        (Reconciler.GetElementTag((WinUI.ComboBox)s!) as ComboBoxElement)?.OnDropDownClosed?.Invoke();

    public static readonly ControlDescriptor<ComboBoxElement, WinUI.ComboBox> Descriptor =
        new ControlDescriptor<ComboBoxElement, WinUI.ComboBox>
        {
            Children = new None<ComboBoxElement, WinUI.ComboBox>(),
            GetSetters = static e => e.Setters,
        }
        .HandCodedControlled<ComboBoxEventPayload, int, WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e => e.OnSelectedIndexChanged,
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h)
        .HandCodedEvent<ComboBoxEventPayload, global::System.EventHandler<object>>(
            subscribe:        static (c, h) => c.DropDownOpened += h,
            callbackPresent:  static e => e.OnDropDownOpened,
            trampoline:       DropDownOpenedTrampoline,
            slotIsNull:       static p => p.DropDownOpenedTrampoline is null,
            setSlot:          static (p, h) => p.DropDownOpenedTrampoline = h)
        .HandCodedEvent<ComboBoxEventPayload, global::System.EventHandler<object>>(
            subscribe:        static (c, h) => c.DropDownClosed += h,
            callbackPresent:  static e => e.OnDropDownClosed,
            trampoline:       DropDownClosedTrampoline,
            slotIsNull:       static p => p.DropDownClosedTrampoline is null,
            setSlot:          static (p, h) => p.DropDownClosedTrampoline = h)
        .OneWay(
            get: static e => e.PlaceholderText ?? "",
            set: static (c, v) => c.PlaceholderText = v)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null)
        .OneWay(
            get: static e => e.IsEditable,
            set: static (c, v) => c.IsEditable = v)
        .OneWayConditional(
            get:         static e => e.MaxDropDownHeight,
            set:         static (c, v) => c.MaxDropDownHeight = v,
            shouldWrite: static e => !double.IsNaN(e.MaxDropDownHeight))
        .OneWayConditional(
            get:         static e => e.Description,
            set:         static (c, v) => c.Description = v,
            shouldWrite: static e => e.Description is not null);
}
