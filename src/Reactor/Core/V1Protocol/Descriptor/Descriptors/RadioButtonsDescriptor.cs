using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 5) — descriptor variant of the hand-coded
/// <c>MountRadioButtons</c> / <c>UpdateRadioButtons</c> arms in
/// <see cref="Reconciler"/>. This is the plural <see cref="WinUI.RadioButtons"/>
/// group control; the singular <see cref="WinUI.RadioButton"/> is covered
/// by <see cref="RadioButtonDescriptor"/> (Phase 3 batch 1).
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>SelectedIndex</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   with a <c>SelectionChangedEventHandler</c> trampoline. The trampoline
///   gates on <c>ChangeEchoSuppressor.ShouldSuppress(c)</c> so programmatic
///   index writes don't echo through <c>OnSelectedIndexChanged</c>.</item>
///   <item><c>Header</c> — one-way conditional per the legacy guard.</item>
///   <item><c>Items</c> — declared via the
///   <see cref="ItemsHost{TElement,TControl}"/> child strategy (Phase 3-final
///   batch G1). The engine populates the items collection BEFORE the prop
///   loop runs, so <c>SelectedIndex</c>'s initial write lands against a
///   populated list (WinUI clamps SelectedIndex against an empty Items
///   collection). Reconciliation is positional (clear + add on structural
///   delta) — acceptable for a RadioButtons control whose typical use is
///   3–7 fixed options.</item>
/// </list></para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item>No keyed-list reconciliation for <c>Items</c> — see above.</item>
///   <item>RadioButtonsElement only exposes a <c>string[]</c> for items;
///   nested Element children (e.g. icon-rich radios) would require a
///   child-strategy entry, not in scope this batch.</item>
/// </list></para>
/// </summary>
internal static class RadioButtonsDescriptor
{
    private static readonly WinUI.SelectionChangedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var g = (WinUI.RadioButtons)s!;
        if (ChangeEchoSuppressor.ShouldSuppressEcho(g, g.SelectedIndex)) return;
        (Reconciler.GetElementTag(g) as RadioButtonsElement)
            ?.OnSelectedIndexChanged?.Invoke(g.SelectedIndex);
    };

    public static readonly ControlDescriptor<RadioButtonsElement, WinUI.RadioButtons> Descriptor =
        new ControlDescriptor<RadioButtonsElement, WinUI.RadioButtons>
        {
            // §14 Phase 3-final batch G1: items declared as a child strategy.
            // The engine dispatches ItemsHost BEFORE the prop loop so the
            // initial SelectedIndex write lands against a populated list
            // (WinUI clamps SelectedIndex against Items.Count). string[] ->
            // IReadOnlyList<object> is valid via array reference covariance
            // (reference element types only).
            Children = new ItemsHost<RadioButtonsElement, WinUI.RadioButtons>(
                GetItems:      static e => (global::System.Collections.Generic.IReadOnlyList<object>)e.Items,
                GetCollection: static c => c.Items),
            GetSetters = static e => e.Setters,
        }
        .HandCodedControlled<RadioButtonsEventPayload, int, WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e => e.OnSelectedIndexChanged,
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h,
            valueDiffEcho: true)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null);
}
