using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 4 (§4.0.3) — full descriptor port of the hand-coded
/// <c>MountTabView</c> / <c>UpdateTabView</c> arms. Closes every gap the
/// Phase-3 carve left open so the descriptor owns the complete TabView
/// behavior and the delegate <c>TabViewHandler</c> + legacy bodies can
/// retire (§4.5).
///
/// <para><b>Coverage:</b>
/// <list type="bullet">
///   <item><c>Tabs</c> via <see cref="TabItemsHost{TElement,TControl,TItem}"/>
///   — each <c>TabViewItemData</c> projected to a <c>WinUI.TabViewItem</c>
///   container with Header + IsClosable + (optional) IconSource set; in-place
///   content reconcile via <c>Reconciler.ReconcileV1Child</c> preserves
///   descendant component state across re-renders.</item>
///   <item><b>§2.2 pinnable headers</b> — <c>CreateContainer</c> builds the
///   header through <c>Reconciler.BuildTabHeader</c> (StackPanel + pin button
///   when <c>IsPinnable</c>); <c>UpdateContainer</c> refreshes it in place via
///   <c>Reconciler.TryUpdatePinHeaderInPlace</c>, mirroring the legacy arm's
///   focus-preserving in-place update.</item>
///   <item><c>SelectedIndex</c> + <c>OnSelectedIndexChanged</c> via
///   <c>.HandCodedControlled</c> (conditional write + echo suppression).</item>
///   <item><c>OnTabCloseRequested</c> + <c>OnAddTabButtonClick</c> +
///   <b>§2.4 docking drag pipeline</b> (<c>OnTabDragStarting</c> /
///   <c>OnTabDragCompleted</c>) via <c>.HandCodedEvent</c>.</item>
///   <item><c>TabStripHeader</c> / <c>TabStripFooter</c> Element slots via
///   <c>.ImperativeBridged</c> structural reconcile.</item>
///   <item><c>IsAddTabButtonVisible</c>, <c>TabWidthMode</c>,
///   <c>CloseButtonOverlayMode</c>, <c>CanDragTabs</c>,
///   <c>CanReorderTabs</c>, <c>AllowDropTabs</c> — plain <c>.OneWay</c>.</item>
/// </list></para>
///
/// <para><b>Still legacy-only:</b> keyed tab reorder — the
/// <see cref="TabItemsHost{TElement,TControl,TItem}"/> pairs by index, matching
/// the index-positional legacy <c>UpdateTabView</c> arm.</para>
///
/// <para><b>One intentional divergence from "byte-identical V1 ON ≡ V1 OFF"
/// (PR #455 nit):</b> a freshly-appended <em>pinnable</em> tab is built through
/// <c>Reconciler.BuildTabHeader</c> in <c>CreateContainer</c>, so its pin
/// button appears <em>immediately</em>. The legacy <c>UpdateTabView</c> arm
/// only surfaced the pin button on the <em>next</em> render. This is a fix in
/// the right direction (the PR already frames TabView as a functional port, not
/// a byte-for-byte relocation); the icon-update path is verified equivalent.</para>
/// </summary>
internal static class TabViewDescriptor
{
    private static readonly WinUI.SelectionChangedEventHandler SelectionChangedTrampoline = (s, _) =>
    {
        var t = (WinUI.TabView)s!;
        if (ChangeEchoSuppressor.ShouldSuppressEcho(t, t.SelectedIndex)) return;
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

    // Spec 045 §2.4 — drag pipeline trampolines. Identical body to the legacy
    // MountTabView arms: resolve the live element off the control tag, seed the
    // WinUI DataPackage so external AllowDrop targets accept the drop, then fire.
    private static readonly TypedEventHandler<WinUI.TabView, WinUI.TabViewTabDragStartingEventArgs>
        TabDragStartingTrampoline = (s, args) =>
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
        TabDragCompletedTrampoline = (s, args) =>
        {
            var t = (WinUI.TabView)s!;
            if (Reconciler.GetElementTag(t) is not TabViewElement el || el.OnTabDragCompleted is null) return;
            var idx = t.TabItems.IndexOf(args.Tab);
            var wasOutside = args.DropResult == global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            el.OnTabDragCompleted(idx, wasOutside);
        };

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
                        Header = Reconciler.BuildTabHeader(item),
                        IsClosable = item.IsClosable,
                        Content = mounted,
                    };
                    if (item.Icon is not null) tvi.IconSource = Reconciler.ResolveIconSource(item.Icon);
                    return tvi;
                },
                UpdateContainer: static (oldItem, newItem, container) =>
                {
                    if (container is not WinUI.TabViewItem tvi) return;

                    // Spec 045 §2.2 — refresh a pinnable header in place when
                    // possible (preserves focus from controls in sibling tabs);
                    // otherwise rebuild, falling back to the plain string header.
                    if (newItem.IsPinnable && oldItem.IsPinnable
                        && tvi.Header is WinUI.StackPanel existingHeader
                        && Reconciler.TryUpdatePinHeaderInPlace(existingHeader, oldItem, newItem))
                    {
                        // In-place succeeded; nothing else to do for the header.
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
                        tvi.IconSource = newItem.Icon is null ? null : Reconciler.ResolveIconSource(newItem.Icon);
                }),
            GetSetters = static e => e.Setters,
        }
        .OneWay(get: static e => e.IsAddTabButtonVisible,  set: static (c, v) => c.IsAddTabButtonVisible = v)
        .OneWay(get: static e => e.TabWidthMode,           set: static (c, v) => c.TabWidthMode = v)
        .OneWay(get: static e => e.CloseButtonOverlayMode, set: static (c, v) => c.CloseButtonOverlayMode = v)
        .OneWay(get: static e => e.CanDragTabs,            set: static (c, v) => c.CanDragTabs = v)
        .OneWay(get: static e => e.CanReorderTabs,         set: static (c, v) => c.CanReorderTabs = v)
        .OneWay(get: static e => e.AllowDropTabs,          set: static (c, v) => c.AllowDropTabs = v)
        // §2.4 docking drag pipeline — fire-only, gated on the element callback.
        .HandCodedEvent<TabViewEventPayload,
            TypedEventHandler<WinUI.TabView, WinUI.TabViewTabDragStartingEventArgs>>(
            subscribe:        static (c, h) => c.TabDragStarting += h,
            callbackPresent:  static e => e.OnTabDragStarting,
            trampoline:       TabDragStartingTrampoline,
            slotIsNull:       static p => p.TabDragStartingTrampoline is null,
            setSlot:          static (p, h) => p.TabDragStartingTrampoline = h)
        .HandCodedEvent<TabViewEventPayload,
            TypedEventHandler<WinUI.TabView, WinUI.TabViewTabDragCompletedEventArgs>>(
            subscribe:        static (c, h) => c.TabDragCompleted += h,
            callbackPresent:  static e => e.OnTabDragCompleted,
            trampoline:       TabDragCompletedTrampoline,
            slotIsNull:       static p => p.TabDragCompletedTrampoline is null,
            setSlot:          static (p, h) => p.TabDragCompletedTrampoline = h)
        // TabStripHeader / TabStripFooter Element slots — structural reconcile
        // so descendant component state survives parent re-renders (mirrors the
        // legacy ReconcileChild calls in UpdateTabView).
        .ImperativeBridged(
            mount: static (ctx, c, e) =>
            {
                if (e.TabStripHeader is not null)
                    c.TabStripHeader = ctx.Reconciler.Mount(e.TabStripHeader, ctx.RequestRerender);
            },
            update: static (ctx, c, o, n) =>
            {
                var next = ctx.Reconciler.ReconcileV1Child(
                    o.TabStripHeader, n.TabStripHeader, c.TabStripHeader as UIElement, ctx.RequestRerender);
                if (!ReferenceEquals(c.TabStripHeader, next)) c.TabStripHeader = next;
            })
        .ImperativeBridged(
            mount: static (ctx, c, e) =>
            {
                if (e.TabStripFooter is not null)
                    c.TabStripFooter = ctx.Reconciler.Mount(e.TabStripFooter, ctx.RequestRerender);
            },
            update: static (ctx, c, o, n) =>
            {
                var next = ctx.Reconciler.ReconcileV1Child(
                    o.TabStripFooter, n.TabStripFooter, c.TabStripFooter as UIElement, ctx.RequestRerender);
                if (!ReferenceEquals(c.TabStripFooter, next)) c.TabStripFooter = next;
            })
        .HandCodedControlled<TabViewEventPayload, int, WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e => e.OnSelectedIndexChanged,
            trampoline:  SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h,
            valueDiffEcho: true)
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
