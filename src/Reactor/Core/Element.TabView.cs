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
    /// stays unpainted. Opting in via <see cref="TabViewElement.FillContentArea"/> resolves
    /// to <c>Stretch</c>, which is what the XAML TabView templates do by hand.
    ///
    /// <para>An explicit <c>.VAlign(…)</c> always wins, so the opt-in resolves to
    /// <see cref="Optional{T}.Unset"/> when one is present. Deferring to
    /// <c>ApplyModifiers</c> (which runs after the descriptor entries) is NOT enough: it
    /// only re-writes the alignment when the modifier <em>changed</em>. With an unchanged
    /// explicit alignment it is skipped entirely, so an unguarded opt-in would overwrite the
    /// author's value with <c>Stretch</c> as it switched on, and <c>ClearValue</c> it away
    /// to the style's <c>Top</c> as it switched off — with nothing left to restore it.</para>
    ///
    /// <para>Unset resolves to <c>ClearValue</c> on the descriptor's
    /// <see cref="FrameworkElement.VerticalAlignmentProperty"/>, releasing the local value
    /// so WinUI's style default (<c>Top</c>) applies again.</para>
    ///
    /// <para>Ownership is decided from the element's own modifiers. An alignment supplied
    /// through a hand-built <see cref="ModifiedElement"/> wrapper is not visible here (the
    /// reconciler unwraps those into a separate merged bag before dispatch), so pair the
    /// opt-in with the fluent <c>.VAlign(…)</c> on the TabView element itself.</para>
    /// </summary>
    private static Optional<VerticalAlignment> ResolveFillAlignment(TabViewElement element)
        => element.FillContentArea && element.Modifiers?.Layout?.VerticalAlignment is null
            ? VerticalAlignment.Stretch
            : Optional<VerticalAlignment>.Unset;

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
            .OneWay(
                get: static e => ResolveFillAlignment(e),
                set: static (c, v) => c.VerticalAlignment = v,
                dp:  FrameworkElement.VerticalAlignmentProperty)
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
