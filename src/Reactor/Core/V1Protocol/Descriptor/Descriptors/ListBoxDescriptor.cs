using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 11) — descriptor variant of the hand-coded
/// <c>MountListBox</c> / <c>UpdateListBox</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Items</c> — one-way Clear + Add cycle gated on a sequence
///   compare (mirrors the legacy <c>StringArrayEquals</c> guard). Same
///   non-keyed shape as <see cref="RadioButtonsDescriptor"/>.</item>
///   <item><c>SelectedIndex</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   with a typed <c>SelectionChanged</c> trampoline. The trampoline fires
///   BOTH <c>OnSelectedIndexChanged</c> and the multi-select snapshot
///   <c>OnSelectionChanged</c> — matches the legacy mount arm's twin-invoke
///   shape.</item>
/// </list></para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b> Items reconciliation is
/// non-keyed (full rebuild on any sequence delta). Acceptable for short
/// ListBoxes (typical 3–15 options).</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class ListBoxDescriptor
{
    private static readonly global::System.Action<int> NoOpSelectedIndexChanged = static _ => { };

    private static readonly WinUI.SelectionChangedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var lb = (WinUI.ListBox)s!;
        if (ChangeEchoSuppressor.ShouldSuppress(lb)) return;
        if (Reconciler.GetElementTag(lb) is not ListBoxElement el) return;
        el.OnSelectedIndexChanged?.Invoke(lb.SelectedIndex);
        if (el.OnSelectionChanged is { } h)
        {
            var snapshot = new global::System.Collections.Generic.List<int>(lb.SelectedItems.Count);
            for (int i = 0; i < lb.SelectedItems.Count; i++)
            {
                var idx = lb.Items.IndexOf(lb.SelectedItems[i]);
                if (idx >= 0) snapshot.Add(idx);
            }
            h(snapshot);
        }
    };

    public static readonly ControlDescriptor<ListBoxElement, WinUI.ListBox> Descriptor =
        new ControlDescriptor<ListBoxElement, WinUI.ListBox>
        {
            Children = new None<ListBoxElement, WinUI.ListBox>(),
            GetSetters = static e => e.Setters,
        }
        // Items BEFORE SelectedIndex so SelectedIndex lands against the
        // populated collection.
        .OneWay<string[]>(
            get: static e => e.Items,
            set: static (c, items) =>
            {
                if (ListBoxItemsEqual(c.Items, items)) return;
                c.Items.Clear();
                foreach (var item in items) c.Items.Add(item);
            })
        .HandCodedControlled<ListBoxEventPayload, int, WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            // Gate is "either callback is present" — match the legacy
            // mount arm's HasCallbacks semantics so a ListBox with only the
            // snapshot subscriber still wires. Cached no-op sentinel avoids
            // a per-Update delegate allocation when only OnSelectionChanged
            // is set (EnsureSubscribed runs every reconcile).
            callback:    static e =>
                e.OnSelectedIndexChanged is not null
                    ? e.OnSelectedIndexChanged
                    : (e.OnSelectionChanged is not null ? NoOpSelectedIndexChanged : null),
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h);

    private static bool ListBoxItemsEqual(
        global::Microsoft.UI.Xaml.Controls.ItemCollection existing, string[] incoming)
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
