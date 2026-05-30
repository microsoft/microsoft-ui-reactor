using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 completion — descriptor variant of the simple
/// <see cref="GridViewElement"/> arm. The typed templated GridView peer is
/// handled by <see cref="TemplatedGridViewDescriptor"/>.
///
/// <para><b>Echo suppression — causal counter, not value-diff (PR #455 CR
/// item #1):</b> <c>SelectedIndex</c> stays on <c>ShouldSuppress</c> /
/// <c>WriteSuppressed</c> rather than the §8 value-diff arm. The arm is a
/// single one-shot slot; the reviewer flagged that GridView's
/// <c>SelectionChanged</c> is documented deferred (mount-time container
/// realization). Empirical probing (PR #455) confirmed post-realization
/// programmatic writes echo <em>synchronously</em> (so the arm's precondition
/// actually holds in steady state) AND that the counter and the arm behave
/// identically in every production-reachable path — so we keep these two on
/// the well-understood counter that <c>main</c> shipped, matching the
/// snapshot <c>OnSelectionChanged</c> twin-invoke (the counter gate suppresses
/// the whole trampoline fire, governing the multi-select snapshot too — CR
/// item #3). The value-diff arm remains for the synchronous-by-construction
/// controls (ComboBox, the toggles, pickers).</para>
/// </summary>
internal static class GridViewDescriptor
{
    private static readonly global::System.Action<int> NoOpSelectedIndexChanged = static _ => { };

    private static readonly WinUI.SelectionChangedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var gv = (WinUI.GridView)s!;
        if (ChangeEchoSuppressor.ShouldSuppress(gv)) return;
        if (Reconciler.GetElementTag(gv) is not GridViewElement el) return;

        el.OnSelectedIndexChanged?.Invoke(gv.SelectedIndex);
        if (el.OnSelectionChanged is { } h)
        {
            var snapshot = new List<int>(gv.SelectedItems.Count);
            for (int i = 0; i < gv.SelectedItems.Count; i++)
            {
                var idx = gv.Items.IndexOf(gv.SelectedItems[i]);
                if (idx >= 0) snapshot.Add(idx);
            }
            h(snapshot);
        }
    };

    private static readonly WinUI.ItemClickEventHandler ItemClickTrampoline = (s, args) =>
    {
        var gv = (WinUI.GridView)s!;
        var idx = gv.Items.IndexOf(args.ClickedItem);
        if (idx >= 0)
            (Reconciler.GetElementTag(gv) as GridViewElement)?.OnItemClick?.Invoke(idx);
    };

    public static readonly ControlDescriptor<GridViewElement, WinUI.GridView> Descriptor =
        new ControlDescriptor<GridViewElement, WinUI.GridView>
        {
            Children = new ItemsHost<GridViewElement, WinUI.GridView>(
                GetItems:      static e => (IReadOnlyList<object>)e.Items,
                GetCollection: static c => c.Items),
            GetSetters = static e => e.Setters,
        }
        .OneWay(get: static e => e.SelectionMode, set: static (c, v) => c.SelectionMode = v)
        .OneWay(get: static e => e.OnItemClick is not null, set: static (c, v) => c.IsItemClickEnabled = v)
        .OneWay(get: static e => e.IncrementalLoadingTrigger, set: static (c, v) => c.IncrementalLoadingTrigger = v)
        .OneWayConditional(
            get:         static e => e.Header,
            set:         static (c, v) => c.Header = v,
            shouldWrite: static e => e.Header is not null)
        .OneWayConditional(
            get:         static e => e.ItemContainerStyle,
            set:         static (c, v) => c.ItemContainerStyle = v,
            shouldWrite: static e => e.ItemContainerStyle is not null)
        .HandCodedControlled<GridViewEventPayload, int, WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => { if (v >= 0) c.SelectedIndex = v; },
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e =>
                e.OnSelectedIndexChanged is not null
                    ? e.OnSelectedIndexChanged
                    : (e.OnSelectionChanged is not null ? NoOpSelectedIndexChanged : null),
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h)
        .HandCodedEvent<GridViewEventPayload, WinUI.ItemClickEventHandler>(
            subscribe:        static (c, h) => c.ItemClick += h,
            callbackPresent:  static e => e.OnItemClick,
            trampoline:       ItemClickTrampoline,
            slotIsNull:       static p => p.ItemClickTrampoline is null,
            setSlot:          static (p, h) => p.ItemClickTrampoline = h);
}
