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
///   <item><c>Items</c> — escape-hatched via a transparent <c>OneWay</c> entry
///   that runs a Clear + Add cycle when the new array differs by reference
///   OR by content. This matches the legacy <c>StringArrayEquals</c> guard
///   but does NOT do keyed reconciliation — every item-change replaces the
///   full list (acceptable for a RadioButtons control whose typical use is
///   3–7 fixed options).</item>
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
[Experimental("REACTOR_V1_PREVIEW")]
internal static class RadioButtonsDescriptor
{
    private static readonly WinUI.SelectionChangedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var g = (WinUI.RadioButtons)s!;
        if (ChangeEchoSuppressor.ShouldSuppress(g)) return;
        (Reconciler.GetElementTag(g) as RadioButtonsElement)
            ?.OnSelectedIndexChanged?.Invoke(g.SelectedIndex);
    };

    public static readonly ControlDescriptor<RadioButtonsElement, WinUI.RadioButtons> Descriptor =
        new ControlDescriptor<RadioButtonsElement, WinUI.RadioButtons>
        {
            Children = new None<RadioButtonsElement, WinUI.RadioButtons>(),
            GetSetters = static e => e.Setters,
        }
        // Items BEFORE SelectedIndex so the index is honored against the
        // populated list (WinUI clamps SelectedIndex against Items.Count).
        // We pull the string array, populate eagerly each pass, and rely on
        // .HandCodedControlled to gate the index write.
        .OneWay<string[]>(
            get: static e => e.Items,
            set: static (c, items) =>
            {
                // Idempotent in steady state: if the existing items match
                // by sequence we skip the rebuild. This mirrors the legacy
                // StringArrayEquals guard without round-tripping through a
                // separate compare in the descriptor framework.
                if (RadioButtonsItemsEqual(c.Items, items)) return;
                // RadioButtons.Items.Clear / Add raises SelectionChanged
                // (template-driven SelectedIndex coercion) — both the
                // descriptor and the legacy arm let this echo through to
                // OnSelectedIndexChanged. Documented gap; the fixture
                // bounds the count rather than asserting zero.
                c.Items.Clear();
                foreach (var item in items) c.Items.Add(item);
            })
        .HandCodedControlled<RadioButtonsEventPayload, int, WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e => e.OnSelectedIndexChanged,
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null);

    private static bool RadioButtonsItemsEqual(
        global::System.Collections.Generic.IList<object> existing, string[] incoming)
    {
        if (existing.Count != incoming.Length) return false;
        for (int i = 0; i < incoming.Length; i++)
        {
            if (!ReferenceEquals(existing[i], incoming[i])
                && existing[i] as string != incoming[i])
                return false;
        }
        return true;
    }
}
