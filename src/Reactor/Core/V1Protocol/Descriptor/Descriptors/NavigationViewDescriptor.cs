using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;

/// <summary>
/// Spec 047 §14 Phase 3 deferred specialized controls — descriptor variant of
/// the hand-coded NavigationView mount/update arms.
/// </summary>
[Experimental("REACTOR_V1_PREVIEW")]
internal static class NavigationViewDescriptor
{
    private static readonly NamedSlots<NavigationViewElement, WinUI.NavigationView> ChildrenStrategy =
        new NamedSlots<NavigationViewElement, WinUI.NavigationView>(new[]
        {
            new NamedSlot<NavigationViewElement, WinUI.NavigationView>(
                Name: "Header",
                GetChild: static e => e.Header,
                SetChild: static (c, ui) => c.Header = ui)
            {
                GetCurrentChild = static c => c.Header as UIElement,
            },
            new NamedSlot<NavigationViewElement, WinUI.NavigationView>(
                Name: "AutoSuggestBox",
                GetChild: static e => e.AutoSuggestBox,
                SetChild: static (c, ui) =>
                {
                    if (ui is WinUI.AutoSuggestBox box) c.AutoSuggestBox = box;
                    else if (ui is null) c.AutoSuggestBox = null;
                })
            {
                GetCurrentChild = static c => c.AutoSuggestBox,
            },
            new NamedSlot<NavigationViewElement, WinUI.NavigationView>(
                Name: "PaneFooter",
                GetChild: static e => e.PaneFooter,
                SetChild: static (c, ui) => c.PaneFooter = ui)
            {
                GetCurrentChild = static c => c.PaneFooter as UIElement,
            },
            new NamedSlot<NavigationViewElement, WinUI.NavigationView>(
                Name: "PaneCustomContent",
                GetChild: static e => e.PaneCustomContent,
                SetChild: static (c, ui) => c.PaneCustomContent = ui)
            {
                GetCurrentChild = static c => c.PaneCustomContent as UIElement,
            },
            new NamedSlot<NavigationViewElement, WinUI.NavigationView>(
                Name: "Content",
                GetChild: static e => e.Content,
                SetChild: static (c, ui) => c.Content = ui)
            {
                GetCurrentChild = static c => c.Content as UIElement,
            },
        });

    private static readonly TypedEventHandler<WinUI.NavigationView, WinUI.NavigationViewSelectionChangedEventArgs>
        SelectionChangedTrampoline = (s, args) =>
        {
            var tag = args.IsSettingsSelected
                ? null
                : (args.SelectedItem as WinUI.NavigationViewItem)?.Tag as string;
            (Reconciler.GetElementTag(s) as NavigationViewElement)?.OnSelectedTagChanged?.Invoke(tag);
        };

    private static readonly TypedEventHandler<WinUI.NavigationView, WinUI.NavigationViewBackRequestedEventArgs>
        BackRequestedTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as NavigationViewElement)?.OnBackRequested?.Invoke();

    public static readonly ControlDescriptor<NavigationViewElement, WinUI.NavigationView> Descriptor =
        new ControlDescriptor<NavigationViewElement, WinUI.NavigationView>
        {
            Children = ChildrenStrategy,
            GetSetters = static e => e.Setters,
        }
        .OneWay(
            get: static e => e.IsPaneOpen,
            set: static (c, v) => c.IsPaneOpen = v)
        .OneWay(
            get: static e => e.PaneDisplayMode,
            set: static (c, v) => c.PaneDisplayMode = v)
        .OneWay(
            get: static e => e.IsBackEnabled,
            set: static (c, v) => c.IsBackEnabled = v)
        .OneWay(
            get: static e => e.IsSettingsVisible,
            set: static (c, v) => c.IsSettingsVisible = v)
        .OneWayConditional(
            get:         static e => e.PaneTitle,
            set:         static (c, v) => c.PaneTitle = v!,
            shouldWrite: static e => e.PaneTitle is not null)
        .OneWayConditional(
            get:         static e => e.OpenPaneLength,
            set:         static (c, v) => c.OpenPaneLength = v,
            shouldWrite: static e => !double.IsNaN(e.OpenPaneLength))
        .OneWayConditional(
            get:         static e => e.CompactModeThresholdWidth,
            set:         static (c, v) => c.CompactModeThresholdWidth = v,
            shouldWrite: static e => !double.IsNaN(e.CompactModeThresholdWidth))
        .OneWayConditional(
            get:         static e => e.ExpandedModeThresholdWidth,
            set:         static (c, v) => c.ExpandedModeThresholdWidth = v,
            shouldWrite: static e => !double.IsNaN(e.ExpandedModeThresholdWidth))
        .Imperative(
            mount: static (c, e) => ApplyMenuAndSelection(c, oldElement: null, e),
            update: static (c, o, n) => ApplyMenuAndSelection(c, o, n))
        .HandCodedEvent<NavigationViewEventPayload,
            TypedEventHandler<WinUI.NavigationView, WinUI.NavigationViewSelectionChangedEventArgs>>(
            subscribe:        static (c, h) => c.SelectionChanged += h,
            callbackPresent:  static e => e.OnSelectedTagChanged,
            trampoline:       SelectionChangedTrampoline,
            slotIsNull:       static p => p.SelectionChangedTrampoline is null,
            setSlot:          static (p, h) => p.SelectionChangedTrampoline = h)
        .HandCodedEvent<NavigationViewEventPayload,
            TypedEventHandler<WinUI.NavigationView, WinUI.NavigationViewBackRequestedEventArgs>>(
            subscribe:        static (c, h) => c.BackRequested += h,
            callbackPresent:  static e => e.OnBackRequested,
            trampoline:       BackRequestedTrampoline,
            slotIsNull:       static p => p.BackRequestedTrampoline is null,
            setSlot:          static (p, h) => p.BackRequestedTrampoline = h);

    private static void ApplyMenuAndSelection(WinUI.NavigationView control, NavigationViewElement? oldElement, NavigationViewElement element)
    {
        if (oldElement is null || !ReferenceEquals(oldElement.MenuItems, element.MenuItems))
        {
            control.MenuItems.Clear();
            foreach (var item in element.MenuItems)
            {
                control.MenuItems.Add(item.IsHeader
                    ? new WinUI.NavigationViewItemHeader { Content = item.Content }
                    : CreateNavItem(item));
            }
        }

        if (oldElement is null
            || oldElement.SelectedTag != element.SelectedTag
            || !ReferenceEquals(oldElement.MenuItems, element.MenuItems))
        {
            control.SelectedItem = FindItemByTag(control.MenuItems, element.SelectedTag);
        }
    }

    private static WinUI.NavigationViewItem CreateNavItem(NavigationViewItemData data)
    {
        var item = new WinUI.NavigationViewItem { Content = data.Content, Tag = data.Tag ?? data.Content };
        var icon = data.IconElement is not null
            ? Reconciler.ResolveIconForDescriptor(data.IconElement)
            : data.Icon is not null
                ? Reconciler.ResolveIconForDescriptor(new SymbolIconData(data.Icon))
                : null;
        if (icon is not null) item.Icon = icon;
        if (data.Children is not null)
        {
            foreach (var child in data.Children) item.MenuItems.Add(CreateNavItem(child));
        }
        return item;
    }

    private static object? FindItemByTag(global::System.Collections.IEnumerable items, string? selectedTag)
    {
        if (selectedTag is null) return null;
        foreach (var item in items)
        {
            if (item is WinUI.NavigationViewItem nvi)
            {
                if ((nvi.Tag as string) == selectedTag) return nvi;
                var child = FindItemByTag(nvi.MenuItems, selectedTag);
                if (child is not null) return child;
            }
        }
        return null;
    }
}
