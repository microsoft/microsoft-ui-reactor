using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 11; revised in Phase 3-final batch G1) —
/// descriptor variant of the hand-coded <c>MountListBox</c> /
/// <c>UpdateListBox</c> arms in <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Items</c> — declared via the
///   <see cref="ItemsHost{TElement,TControl}"/> child strategy. The engine
///   populates the items collection BEFORE the prop loop runs (see
///   <see cref="DescriptorHandler{TElement,TControl}"/>), so
///   <c>SelectedIndex</c>'s initial write lands against a populated list.</item>
///   <item><c>SelectedIndex</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   with a typed <c>SelectionChanged</c> trampoline. The trampoline fires
///   BOTH <c>OnSelectedIndexChanged</c> and the multi-select snapshot
///   <c>OnSelectionChanged</c> — matches the legacy mount arm's twin-invoke
///   shape.</item>
/// </list></para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b> Items reconciliation is
/// non-keyed (full rebuild on any structural delta). Acceptable for short
/// ListBoxes (typical 3–15 options).</para>
///
/// <para><b>Echo suppression — causal counter (PR #455 CR item #1):</b>
/// <c>SelectedIndex</c> uses <c>ShouldSuppress</c> / <c>WriteSuppressed</c>,
/// not the §8 value-diff arm, for the same reason as
/// <see cref="GridViewDescriptor"/> — see that type for the full rationale and
/// the empirical findings. The counter gate suppresses the entire trampoline
/// fire, so it governs the multi-select snapshot <c>OnSelectionChanged</c> too
/// (CR item #3).</para>
/// </summary>
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
            // §14 Phase 3-final batch G1: items declared as a child strategy.
            // The engine dispatches ItemsHost BEFORE the prop loop, so
            // SelectedIndex's initial write lands against the populated
            // collection (WinUI clamps SelectedIndex against an empty
            // Items collection).
            //
            // string[] -> IReadOnlyList<object> is valid via array reference
            // covariance (reference element types only) — zero-alloc and the
            // cast never throws at runtime for empty or non-empty arrays.
            Children = new ItemsHost<ListBoxElement, WinUI.ListBox>(
                GetItems:      static e => (global::System.Collections.Generic.IReadOnlyList<object>)e.Items,
                GetCollection: static c => c.Items),
            GetSetters = static e => e.Setters,
        }
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
}
