using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 finish — Port (10). Descriptor variant of the
/// hand-coded <c>MountTabView</c> / <c>UpdateTabView</c> arms.
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Tabs</c> via <see cref="TabItemsHost{TElement,TControl,TItem}"/>
///   — each <c>TabViewItemData</c> projected to a <c>WinUI.TabViewItem</c>
///   container with Header + IsClosable + (optional) IconSource set.</item>
///   <item><c>SelectedIndex</c> + <c>OnSelectedIndexChanged</c> via
///   <c>.HandCodedControlled</c> with echo suppression.</item>
///   <item><c>OnTabCloseRequested</c> + <c>OnAddTabButtonClick</c> via
///   <c>.HandCodedEvent</c>.</item>
///   <item><c>IsAddTabButtonVisible</c>, <c>TabWidthMode</c>,
///   <c>CloseButtonOverlayMode</c>, <c>CanDragTabs</c>,
///   <c>CanReorderTabs</c>, <c>AllowDropTabs</c> — plain <c>.OneWay</c>.</item>
/// </list></para>
///
/// <para><b>Known gaps vs. hand-coded handler:</b>
/// <list type="bullet">
///   <item><c>TabStripHeader</c> / <c>TabStripFooter</c> Elements stay
///   escape-hatched. Two named slots overlaying the primary
///   <c>TabItemsHost</c> would need the Engine (2) <c>.ImperativeBridged</c>
///   shape; tracked as a follow-up.</item>
///   <item>Spec 045 §2.4 docking drag pipeline (<c>OnTabDragStarting</c> /
///   <c>OnTabDragCompleted</c>) and §2.2 pinnable headers stay on the
///   legacy arm. Common-case TabView ports through; docking-aware tabs
///   stay V1 OFF for now.</item>
/// </list></para>
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class TabViewDescriptor
{
    private static readonly WinUI.SelectionChangedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var t = (WinUI.TabView)s!;
        if (ChangeEchoSuppressor.ShouldSuppress(t)) return;
        (Reconciler.GetElementTag(t) as TabViewElement)?.OnSelectedIndexChanged?.Invoke(t.SelectedIndex);
    };

    private static readonly TypedEventHandler<WinUI.TabView, WinUI.TabViewTabCloseRequestedEventArgs>
        TabCloseRequestedTrampoline = (s, args) =>
        {
            var t = (WinUI.TabView)s!;
            var idx = t.TabItems.IndexOf(args.Tab);
            (Reconciler.GetElementTag(t) as TabViewElement)?.OnTabCloseRequested?.Invoke(idx);
        };

    private static readonly TypedEventHandler<WinUI.TabView, object>
        AddTabButtonClickTrampoline = (s, _) =>
            (Reconciler.GetElementTag((UIElement)s!) as TabViewElement)?.OnAddTabButtonClick?.Invoke();

    public static readonly ControlDescriptor<TabViewElement, WinUI.TabView> Descriptor =
        new ControlDescriptor<TabViewElement, WinUI.TabView>
        {
            Children = new TabItemsHost<TabViewElement, WinUI.TabView, TabViewItemData>(
                GetItems:        static e => e.Tabs,
                GetCollection:   static c => c.TabItems,
                GetContent:      static item => item.Content,
                CreateContainer: static (item, mounted) =>
                {
                    var tvi = new WinUI.TabViewItem
                    {
                        Header = item.Header,
                        IsClosable = item.IsClosable,
                        Content = mounted,
                    };
                    if (item.Icon is not null) tvi.IconSource = Reconciler.ResolveIconSource(item.Icon);
                    return tvi;
                }),
            GetSetters = static e => e.Setters,
        }
        .OneWay(get: static e => e.IsAddTabButtonVisible,  set: static (c, v) => c.IsAddTabButtonVisible = v)
        .OneWay(get: static e => e.TabWidthMode,           set: static (c, v) => c.TabWidthMode = v)
        .OneWay(get: static e => e.CloseButtonOverlayMode, set: static (c, v) => c.CloseButtonOverlayMode = v)
        .OneWay(get: static e => e.CanDragTabs,            set: static (c, v) => c.CanDragTabs = v)
        .OneWay(get: static e => e.CanReorderTabs,         set: static (c, v) => c.CanReorderTabs = v)
        .OneWay(get: static e => e.AllowDropTabs,          set: static (c, v) => c.AllowDropTabs = v)
        .HandCodedControlled<TabViewEventPayload, int, WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e => e.OnSelectedIndexChanged,
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h)
        .HandCodedEvent<TabViewEventPayload,
            TypedEventHandler<WinUI.TabView, WinUI.TabViewTabCloseRequestedEventArgs>>(
            subscribe:        static (c, h) => c.TabCloseRequested += h,
            callbackPresent:  static e => e.OnTabCloseRequested,
            trampoline:       TabCloseRequestedTrampoline,
            slotIsNull:       static p => p.TabCloseRequestedTrampoline is null,
            setSlot:          static (p, h) => p.TabCloseRequestedTrampoline = h)
        .HandCodedEvent<TabViewEventPayload,
            TypedEventHandler<WinUI.TabView, object>>(
            subscribe:        static (c, h) => c.AddTabButtonClick += h,
            callbackPresent:  static e => e.OnAddTabButtonClick,
            trampoline:       AddTabButtonClickTrampoline,
            slotIsNull:       static p => p.AddTabButtonClickTrampoline is null,
            setSlot:          static (p, h) => p.AddTabButtonClickTrampoline = h);
}
