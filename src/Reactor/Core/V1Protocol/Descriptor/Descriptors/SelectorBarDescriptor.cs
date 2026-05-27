using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 (batch 11) — descriptor variant of the hand-coded
/// <c>MountSelectorBar</c> / <c>UpdateSelectorBar</c> arms in
/// <see cref="Reconciler"/>.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Items</c> — one-way Clear + Add cycle (per-item Text + Icon).
///   Non-keyed; full rebuild on any sequence delta.</item>
///   <item><c>SelectedIndex</c> — <see cref="ControlDescriptor{TElement,TControl}.HandCodedControlled{TPayload,TValue,TDelegate}"/>
///   with a typed <c>SelectionChanged</c> trampoline. SelectorBar exposes
///   <c>SelectedItem</c> as the live property, so set/readBack map
///   <c>SelectedIndex</c> &lt;-&gt; <c>SelectedItem</c> via
///   <c>Items.IndexOf</c>.</item>
/// </list></para>
///
/// <para><b>Known gaps:</b> Items reconciliation is non-keyed (full
/// rebuild on any sequence delta) — the legacy arm does in-place patches
/// for the common prefix. Acceptable for short SelectorBars (typical
/// 2–5 segments).</para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class SelectorBarDescriptor
{
    private static readonly TypedEventHandler<WinUI.SelectorBar, WinUI.SelectorBarSelectionChangedEventArgs>
        SelectionChangedTrampoline = (s, _) =>
        {
            var bar = (WinUI.SelectorBar)s!;
            if (ChangeEchoSuppressor.ShouldSuppress(bar)) return;
            if (Reconciler.GetElementTag(bar) is not SelectorBarElement el) return;
            var idx = bar.Items.IndexOf(bar.SelectedItem);
            el.OnSelectedIndexChanged?.Invoke(idx);
        };

    public static readonly ControlDescriptor<SelectorBarElement, WinUI.SelectorBar> Descriptor =
        new ControlDescriptor<SelectorBarElement, WinUI.SelectorBar>
        {
            Children = new None<SelectorBarElement, WinUI.SelectorBar>(),
            GetSetters = static e => e.Setters,
        }
        // Items BEFORE SelectedIndex so SelectedIndex lands against a
        // populated Items collection. The engine gates this set on the
        // element-pair comparer (SelectorBarItemsComparer below) so we only
        // rebuild when the source array's Text or Icon actually changed —
        // record equality on SelectorBarItemData covers both fields.
        .OneWay<SelectorBarItemData[]>(
            get: static e => e.Items,
            set: static (c, items) =>
            {
                c.Items.Clear();
                foreach (var item in items)
                {
                    var sbi = new WinUI.SelectorBarItem { Text = item.Text };
                    if (item.Icon is not null)
                        sbi.Icon = Reconciler.ResolveIconForDescriptor(new SymbolIconData(item.Icon));
                    c.Items.Add(sbi);
                }
            },
            comparer: SelectorBarItemsComparer.Instance)
        .HandCodedControlled<SelectorBarEventPayload, int,
            TypedEventHandler<WinUI.SelectorBar, WinUI.SelectorBarSelectionChangedEventArgs>>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) =>
            {
                if (v >= 0 && v < c.Items.Count)
                {
                    var desired = c.Items[v];
                    if (!ReferenceEquals(c.SelectedItem, desired))
                        c.SelectedItem = desired;
                }
            },
            readBack:    static c => c.Items.IndexOf(c.SelectedItem),
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e => e.OnSelectedIndexChanged,
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h);

    private sealed class SelectorBarItemsComparer : IEqualityComparer<SelectorBarItemData[]>
    {
        public static readonly SelectorBarItemsComparer Instance = new();
        public bool Equals(SelectorBarItemData[]? a, SelectorBarItemData[]? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!a[i].Equals(b[i])) return false;
            return true;
        }
        public int GetHashCode(SelectorBarItemData[] obj) => obj.Length;
    }
}
