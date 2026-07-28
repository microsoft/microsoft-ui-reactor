using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;
using Desc = Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using V1 = Microsoft.UI.Reactor.Core.V1Protocol;

namespace Microsoft.UI.Reactor.Core;

// Spec 058 §15 (P5.25) — TabView's bespoke surface: the TabItemsHost (Tabs → TabViewItem
// containers with pinnable headers/icons), value-diff SelectedIndex, the TabStripHeader/Footer
// Element slots (.ImperativeBridged), and the 4 drag/close/add events. Reproduced verbatim from
// the deleted TabViewDescriptor. The 6 simple props auto-map (in Element.cs).
public partial record TabViewElement
{
    private static readonly WinUI.SelectionChangedEventHandler __SelectionChangedTrampoline = (s, _) =>
    {
        var t = (WinUI.TabView)s!;
        if (!Reconciler.TryGetReactorState(t, out var state)) return;
        if (ChangeEchoSuppressor.ShouldSuppressEcho(state, t.SelectedIndex)) return;
        (state.Element as TabViewElement)?.OnSelectedIndexChanged?.Invoke(t.SelectedIndex);
    };

    private static readonly TypedEventHandler<WinUI.TabView, WinUI.TabViewTabCloseRequestedEventArgs>
        __TabCloseRequestedTrampoline = (s, args) =>
        {
            var t = (WinUI.TabView)s!;
            var idx = t.TabItems.IndexOf(args.Tab);
            (Reconciler.GetElementTag(t) as TabViewElement)?.OnTabCloseRequested?.Invoke(idx);
        };

    private static readonly TypedEventHandler<WinUI.TabView, object>
        __AddTabButtonClickTrampoline = (s, _) =>
            (Reconciler.GetElementTag((UIElement)s!) as TabViewElement)?.OnAddTabButtonClick?.Invoke();

    private static readonly TypedEventHandler<WinUI.TabView, WinUI.TabViewTabDragStartingEventArgs>
        __TabDragStartingTrampoline = (s, args) =>
        {
            var t = (WinUI.TabView)s!;
            if (Reconciler.GetElementTag(t) is not TabViewElement el || el.OnTabDragStarting is null) return;
            var idx = t.TabItems.IndexOf(args.Tab);
            if (idx < 0) return;
            args.Data.RequestedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            args.Data.SetText("reactor-tabview-tab");
            el.OnTabDragStarting(idx);
        };

    private static readonly TypedEventHandler<WinUI.TabView, WinUI.TabViewTabDragCompletedEventArgs>
        __TabDragCompletedTrampoline = (s, args) =>
        {
            var t = (WinUI.TabView)s!;
            if (Reconciler.GetElementTag(t) is not TabViewElement el || el.OnTabDragCompleted is null) return;
            var idx = t.TabItems.IndexOf(args.Tab);
            var wasOutside = args.DropResult == global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            el.OnTabDragCompleted(idx, wasOutside);
        };

    /// <summary>
    /// Issue #914 — WinUI's <c>DefaultTabViewStyle</c> sets
    /// <c>VerticalAlignment="Top"</c> on the TabView itself, so the control is arranged at
    /// its desired height and the <c>*</c> content row of its template never receives the
    /// leftover space: tab content collapses to its own height and the rest of the tab body
    /// stays unpainted. Opting in via <see cref="TabViewElement.FillContentArea"/> writes
    /// <c>Stretch</c> on the control, which is what the XAML TabView templates do by hand.
    ///
    /// <para>An explicit <c>.VAlign(…)</c> always wins, so the opt-in stands down entirely
    /// when one is present. Deferring to <c>ApplyModifiers</c> (which runs after the
    /// descriptor entries) is NOT enough: it only re-writes the alignment when the modifier
    /// *changed*, so an unchanged explicit alignment would be clobbered by this write on
    /// every subsequent re-render.</para>
    ///
    /// <para>Turning the opt-in back off releases the local value so WinUI's style default
    /// (<c>Top</c>) applies again; the control is not pooled, so mount always starts from
    /// the style value and only the on→off transition needs the release.</para>
    /// </summary>
    private static void ApplyFillContentArea(WinUI.TabView control, TabViewElement element, TabViewElement? old)
    {
        if (WritesFill(element)) control.VerticalAlignment = VerticalAlignment.Stretch;
        else if (old is not null && WritesFill(old) && element.Modifiers?.Layout?.VerticalAlignment is null)
            control.ClearValue(FrameworkElement.VerticalAlignmentProperty);
    }

    /// <summary>
    /// True when the opt-in owns the control's vertical alignment: it is on and the author
    /// did not pin the alignment themselves.
    /// </summary>
    private static bool WritesFill(TabViewElement element)
        => element.FillContentArea && element.Modifiers?.Layout?.VerticalAlignment is null;

    private static partial Desc.ControlDescriptor<TabViewElement, WinUI.TabView> Customize(
        Desc.ControlDescriptor<TabViewElement, WinUI.TabView> d)
    {
        d.Children = new V1.TabItemsHost<TabViewElement, WinUI.TabView, TabViewItemData>(
            GetItems:        static e => e.Tabs,
            GetCollection:   static c => c.TabItems,
            GetContent:      static item => item.Content,
            CreateContainer: static (item, mounted) =>
            {
                var tvi = new WinUI.TabViewItem
                {
                    Header = Reconciler.BuildTabHeader(item),
                    IsClosable = item.IsClosable,
                    Content = mounted,
                };
                if (item.Icon is not null) tvi.IconSource = V1.IconResolver.ResolveIconSource(item.Icon);
                return tvi;
            },
            UpdateContainer: static (oldItem, newItem, container) =>
            {
                if (container is not WinUI.TabViewItem tvi) return;

                if (newItem.IsPinnable && oldItem.IsPinnable
                    && tvi.Header is WinUI.StackPanel existingHeader
                    && Reconciler.TryUpdatePinHeaderInPlace(existingHeader, oldItem, newItem))
                {
                    // In-place succeeded.
                }
                else if (newItem.IsPinnable || oldItem.IsPinnable)
                {
                    tvi.Header = Reconciler.BuildTabHeader(newItem);
                }
                else if (tvi.Header as string != newItem.Header)
                {
                    tvi.Header = newItem.Header;
                }

                if (tvi.IsClosable != newItem.IsClosable) tvi.IsClosable = newItem.IsClosable;
                if (!Equals(newItem.Icon, oldItem.Icon))
                    tvi.IconSource = newItem.Icon is null ? null : V1.IconResolver.ResolveIconSource(newItem.Icon);
            });
        // TabStripHeader / TabStripFooter are declared via [WrapElementSlot] — the
        // generator emits their mount/reconcile ImperativeBridged entries. They can't be
        // NamedSlots/SingleContent children: a control has exactly one ChildrenStrategy and
        // TabView's is already the tab ItemsHost. Secondary single-element slots that write a
        // dedicated control property therefore ride the imperative bridge (see
        // docs/guide/extensibility-preview.md — secondary-slot decision).
        return d
            .ImperativeBridged(
                mount:  static (ctx, c, e) => ApplyFillContentArea(c, e, old: null),
                update: static (ctx, c, o, n) => ApplyFillContentArea(c, n, o))
            .HandCodedControlled<V1.TabViewEventPayload, int, WinUI.SelectionChangedEventHandler>(
                get:         static e => e.SelectedIndex,
                set:         static (c, v) => c.SelectedIndex = v,
                readBack:    static c => c.SelectedIndex,
                subscribe:   static (c, h) => c.SelectionChanged += h,
                callback:    static e => e.OnSelectedIndexChanged,
                trampoline:  __SelectionChangedTrampoline,
                slotIsNull:  static p => p.SelectionChangedTrampoline is null,
                setSlot:     static (p, h) => p.SelectionChangedTrampoline = h,
                valueDiffEcho: true)
            .HandCodedEvent<V1.TabViewEventPayload,
                TypedEventHandler<WinUI.TabView, WinUI.TabViewTabCloseRequestedEventArgs>>(
                subscribe:        static (c, h) => c.TabCloseRequested += h,
                callbackPresent:  static e => e.OnTabCloseRequested,
                trampoline:       __TabCloseRequestedTrampoline,
                slotIsNull:       static p => p.TabCloseRequestedTrampoline is null,
                setSlot:          static (p, h) => p.TabCloseRequestedTrampoline = h)
            .HandCodedEvent<V1.TabViewEventPayload,
                TypedEventHandler<WinUI.TabView, object>>(
                subscribe:        static (c, h) => c.AddTabButtonClick += h,
                callbackPresent:  static e => e.OnAddTabButtonClick,
                trampoline:       __AddTabButtonClickTrampoline,
                slotIsNull:       static p => p.AddTabButtonClickTrampoline is null,
                setSlot:          static (p, h) => p.AddTabButtonClickTrampoline = h)
            .HandCodedEvent<V1.TabViewEventPayload,
                TypedEventHandler<WinUI.TabView, WinUI.TabViewTabDragStartingEventArgs>>(
                subscribe:        static (c, h) => c.TabDragStarting += h,
                callbackPresent:  static e => e.OnTabDragStarting,
                trampoline:       __TabDragStartingTrampoline,
                slotIsNull:       static p => p.TabDragStartingTrampoline is null,
                setSlot:          static (p, h) => p.TabDragStartingTrampoline = h)
            .HandCodedEvent<V1.TabViewEventPayload,
                TypedEventHandler<WinUI.TabView, WinUI.TabViewTabDragCompletedEventArgs>>(
                subscribe:        static (c, h) => c.TabDragCompleted += h,
                callbackPresent:  static e => e.OnTabDragCompleted,
                trampoline:       __TabDragCompletedTrampoline,
                slotIsNull:       static p => p.TabDragCompletedTrampoline is null,
                setSlot:          static (p, h) => p.TabDragCompletedTrampoline = h);
    }
}
