using System.Collections.Generic;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Descriptor variant of the simple <see cref="ListViewElement"/> surface.
/// The production registration keeps the virtualizing hand-coded handler, but
/// the descriptor entry documents and tests the Optional-controlled
/// <c>SelectedIndex</c> contract shared by the collection controls.
/// </summary>
internal static class ListViewDescriptor
{
    private static readonly global::System.Action<int> NoOpSelectedIndexChanged = static _ => { };

    private static readonly WinUI.SelectionChangedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var lv = (WinUI.ListView)s!;
        if (ChangeEchoSuppressor.ShouldSuppress(lv)) return;
        if (Reconciler.GetElementTag(lv) is not ListViewElement el) return;

        el.OnSelectedIndexChanged?.Invoke(lv.SelectedIndex);
        if (el.OnSelectionChanged is { } h)
        {
            var snapshot = new List<int>(lv.SelectedItems.Count);
            for (int i = 0; i < lv.SelectedItems.Count; i++)
            {
                var idx = lv.Items.IndexOf(lv.SelectedItems[i]);
                if (idx >= 0) snapshot.Add(idx);
            }
            h(snapshot);
        }
    };

    private static readonly WinUI.ItemClickEventHandler ItemClickTrampoline = (s, args) =>
    {
        var lv = (WinUI.ListView)s!;
        var idx = lv.Items.IndexOf(args.ClickedItem);
        if (idx >= 0)
            (Reconciler.GetElementTag(lv) as ListViewElement)?.OnItemClick?.Invoke(idx);
    };

    public static readonly ControlDescriptor<ListViewElement, WinUI.ListView> Descriptor =
        new ControlDescriptor<ListViewElement, WinUI.ListView>
        {
            Children = new ItemsHost<ListViewElement, WinUI.ListView>(
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
        .HandCodedControlled<ListViewEventPayload, int, WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            // Spec 050: Optional.Of(-1) is the explicit force-clear sentinel
            // (see ListViewElement.SelectedIndex XML doc + migration guide).
            // WinUI accepts SelectedIndex = -1 as "deselect". Optional<int>.Unset
            // never reaches this lambda (the entry's Optional gate returns
            // early on !HasValue) so the control stays at its current
            // selection in the "no force assert" case.
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e =>
                e.OnSelectedIndexChanged is not null
                    ? e.OnSelectedIndexChanged
                    : (e.OnSelectionChanged is not null ? NoOpSelectedIndexChanged : null),
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h)
        .HandCodedEvent<ListViewEventPayload, WinUI.ItemClickEventHandler>(
            subscribe:        static (c, h) => c.ItemClick += h,
            callbackPresent:  static e => e.OnItemClick,
            trampoline:       ItemClickTrampoline,
            slotIsNull:       static p => p.ItemClickTrampoline is null,
            setSlot:          static (p, h) => p.ItemClickTrampoline = h);
}

internal sealed class ListViewDescriptorHandler()
    : DescriptorHandler<ListViewElement, WinUI.ListView>(ListViewDescriptor.Descriptor);
