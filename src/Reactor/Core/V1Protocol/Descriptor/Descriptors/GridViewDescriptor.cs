using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 completion — descriptor variant of the simple
/// <see cref="GridViewElement"/> arm. The typed templated GridView peer is
/// handled by <see cref="TemplatedGridViewDescriptor"/>.
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
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
