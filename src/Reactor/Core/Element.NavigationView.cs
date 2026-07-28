using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;
using Desc = Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using V1 = Microsoft.UI.Reactor.Core.V1Protocol;

namespace Microsoft.UI.Reactor.Core;

// Spec 058 §15 (P5.23) — NavigationView's bespoke surface: 5 NamedSlots, the MenuItems +
// SelectedTag menu reconciler (.Imperative), and the SelectionChanged/BackRequested events.
// Issue #916 added the PaneOpening/PaneClosing pair behind OnPaneOpenChanged.
// IsPaneOpen/PaneDisplayMode/IsBackEnabled/IsSettingsVisible/PaneTitle auto-map (in Element.cs).
// All reproduced verbatim from the deleted NavigationViewDescriptor.
public partial record NavigationViewElement
{
    private static readonly TypedEventHandler<WinUI.NavigationView, WinUI.NavigationViewSelectionChangedEventArgs>
        __SelectionChangedTrampoline = (s, args) =>
        {
            var tag = args.IsSettingsSelected
                ? null
                : (args.SelectedItem as WinUI.NavigationViewItem)?.Tag as string;
            (Reconciler.GetElementTag(s) as NavigationViewElement)?.OnSelectedTagChanged?.Invoke(tag);
        };

    private static readonly TypedEventHandler<WinUI.NavigationView, WinUI.NavigationViewBackRequestedEventArgs>
        __BackRequestedTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as NavigationViewElement)?.OnBackRequested?.Invoke();

    // Issue #916 — the pane can open/close without the app asking (light dismiss, adaptive
    // display-mode changes). Without these, a controlled IsPaneOpen drifts out of sync and the
    // next toggle writes a value the control already holds. PaneOpening/PaneClosing (not the
    // …ed pair) so the app learns the new state immediately: PaneClosed only fires once the
    // close transition finishes, which would leave a toggle pressed mid-animation stale.
    // Mirrors SplitViewElement's twin trampolines.
    private static readonly TypedEventHandler<WinUI.NavigationView, object>
        __PaneOpeningTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as NavigationViewElement)?.OnPaneOpenChanged?.Invoke(true);

    private static readonly TypedEventHandler<WinUI.NavigationView, WinUI.NavigationViewPaneClosingEventArgs>
        __PaneClosingTrampoline = (s, _) =>
            (Reconciler.GetElementTag(s) as NavigationViewElement)?.OnPaneOpenChanged?.Invoke(false);

    private static partial Desc.ControlDescriptor<NavigationViewElement, WinUI.NavigationView> Customize(
        Desc.ControlDescriptor<NavigationViewElement, WinUI.NavigationView> d)
    {
        d.Children = new V1.NamedSlots<NavigationViewElement, WinUI.NavigationView>(new[]
        {
            new V1.NamedSlot<NavigationViewElement, WinUI.NavigationView>(
                Name: "Header",
                GetChild: static e => e.Header,
                SetChild: static (c, ui) => c.Header = ui)
            {
                GetCurrentChild = static c => c.Header as UIElement,
            },
            new V1.NamedSlot<NavigationViewElement, WinUI.NavigationView>(
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
            new V1.NamedSlot<NavigationViewElement, WinUI.NavigationView>(
                Name: "PaneFooter",
                GetChild: static e => e.PaneFooter,
                SetChild: static (c, ui) => c.PaneFooter = ui)
            {
                GetCurrentChild = static c => c.PaneFooter as UIElement,
            },
            new V1.NamedSlot<NavigationViewElement, WinUI.NavigationView>(
                Name: "PaneCustomContent",
                GetChild: static e => e.PaneCustomContent,
                SetChild: static (c, ui) => c.PaneCustomContent = ui)
            {
                GetCurrentChild = static c => c.PaneCustomContent as UIElement,
            },
            new V1.NamedSlot<NavigationViewElement, WinUI.NavigationView>(
                Name: "Content",
                GetChild: static e => e.Content,
                SetChild: static (c, ui) => c.Content = ui)
            {
                GetCurrentChild = static c => c.Content as UIElement,
            },
        });
        return d
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
            .HandCodedEvent<V1.NavigationViewEventPayload,
                TypedEventHandler<WinUI.NavigationView, WinUI.NavigationViewSelectionChangedEventArgs>>(
                subscribe:        static (c, h) => c.SelectionChanged += h,
                callbackPresent:  static e => e.OnSelectedTagChanged,
                trampoline:       __SelectionChangedTrampoline,
                slotIsNull:       static p => p.SelectionChangedTrampoline is null,
                setSlot:          static (p, h) => p.SelectionChangedTrampoline = h)
            .HandCodedEvent<V1.NavigationViewEventPayload,
                TypedEventHandler<WinUI.NavigationView, WinUI.NavigationViewBackRequestedEventArgs>>(
                subscribe:        static (c, h) => c.BackRequested += h,
                callbackPresent:  static e => e.OnBackRequested,
                trampoline:       __BackRequestedTrampoline,
                slotIsNull:       static p => p.BackRequestedTrampoline is null,
                setSlot:          static (p, h) => p.BackRequestedTrampoline = h)
            .HandCodedEvent<V1.NavigationViewEventPayload,
                TypedEventHandler<WinUI.NavigationView, object>>(
                subscribe:        static (c, h) => c.PaneOpening += h,
                callbackPresent:  static e => e.OnPaneOpenChanged,
                trampoline:       __PaneOpeningTrampoline,
                slotIsNull:       static p => p.PaneOpeningTrampoline is null,
                setSlot:          static (p, h) => p.PaneOpeningTrampoline = h)
            .HandCodedEvent<V1.NavigationViewEventPayload,
                TypedEventHandler<WinUI.NavigationView, WinUI.NavigationViewPaneClosingEventArgs>>(
                subscribe:        static (c, h) => c.PaneClosing += h,
                callbackPresent:  static e => e.OnPaneOpenChanged,
                trampoline:       __PaneClosingTrampoline,
                slotIsNull:       static p => p.PaneClosingTrampoline is null,
                setSlot:          static (p, h) => p.PaneClosingTrampoline = h);
    }

    private static void ApplyMenuAndSelection(WinUI.NavigationView control, NavigationViewElement? oldElement, NavigationViewElement element)
    {
        if (oldElement is null)
        {
            control.MenuItems.Clear();
            foreach (var item in element.MenuItems)
            {
                control.MenuItems.Add(item.IsHeader
                    ? new WinUI.NavigationViewItemHeader { Content = item.Content }
                    : CreateNavItem(item));
            }
        }
        else if (!ReferenceEquals(oldElement.MenuItems, element.MenuItems))
        {
            ReconcileMenuItems(control.MenuItems, oldElement.MenuItems, element.MenuItems);
        }

        if (oldElement is null
            || oldElement.SelectedTag != element.SelectedTag
            || !ReferenceEquals(oldElement.MenuItems, element.MenuItems))
        {
            control.SelectedItem = FindItemByTag(control.MenuItems, element.SelectedTag);
        }
    }

    private static void ReconcileMenuItems(
        IList<object> live,
        NavigationViewItemData[]? oldData,
        NavigationViewItemData[] newData)
    {
        if (StructureMatches(live, newData))
        {
            for (int i = 0; i < newData.Length; i++)
            {
                var data = newData[i];
                if (data.IsHeader)
                {
                    if (live[i] is WinUI.NavigationViewItemHeader h && !Equals(h.Content, data.Content))
                        h.Content = data.Content;
                }
                else if (live[i] is WinUI.NavigationViewItem nvi)
                {
                    var oldItem = oldData is not null && i < oldData.Length ? oldData[i] : null;
                    UpdateNavItemInPlace(nvi, oldItem, data);
                }
            }
            return;
        }

        var reusable = new Dictionary<string, WinUI.NavigationViewItem>();
        foreach (var nvi in live.OfType<WinUI.NavigationViewItem>().Where(x => x.Tag is string))
            reusable[(string)nvi.Tag] = nvi;

        var oldByTag = new Dictionary<string, NavigationViewItemData>();
        if (oldData is not null)
            foreach (var dd in oldData.Where(dd => !dd.IsHeader))
                oldByTag[dd.Tag ?? dd.Content] = dd;

        live.Clear();
        foreach (var data in newData)
        {
            if (data.IsHeader)
            {
                live.Add(new WinUI.NavigationViewItemHeader { Content = data.Content });
                continue;
            }

            var key = data.Tag ?? data.Content;
            if (reusable.Remove(key, out var nvi))
                UpdateNavItemInPlace(nvi, oldByTag.GetValueOrDefault(key), data);
            else
                nvi = CreateNavItem(data);
            live.Add(nvi);
        }
    }

    private static bool StructureMatches(IList<object> live, NavigationViewItemData[] newData)
    {
        if (live.Count != newData.Length) return false;
        for (int i = 0; i < newData.Length; i++)
        {
            var data = newData[i];
            if (data.IsHeader)
            {
                if (live[i] is not WinUI.NavigationViewItemHeader) return false;
            }
            else
            {
                if (live[i] is not WinUI.NavigationViewItem nvi) return false;
                if ((nvi.Tag as string) != (data.Tag ?? data.Content)) return false;
            }
        }
        return true;
    }

    private static void UpdateNavItemInPlace(WinUI.NavigationViewItem nvi, NavigationViewItemData? oldData, NavigationViewItemData data)
    {
        if (!Equals(nvi.Content, data.Content)) nvi.Content = data.Content;

        var newTag = data.Tag ?? data.Content;
        if (!Equals(nvi.Tag, newTag)) nvi.Tag = newTag;

        bool iconChanged = oldData is null
            || !Equals(oldData.IconElement, data.IconElement)
            || oldData.Icon != data.Icon;
        if (iconChanged)
        {
            var icon = data.IconElement is not null
                ? V1.IconResolver.ResolveIconForDescriptor(data.IconElement)
                : data.Icon is not null
                    ? V1.IconResolver.ResolveIconForDescriptor(new SymbolIconData(data.Icon))
                    : null;
            if (icon is not null) nvi.Icon = icon;
            else if (nvi.Icon is not null) nvi.Icon = null;
        }

        if (data.Children is { Length: > 0 } children)
            ReconcileMenuItems(nvi.MenuItems, oldData?.Children, children);
        else if (nvi.MenuItems.Count > 0)
            nvi.MenuItems.Clear();
    }

    private static WinUI.NavigationViewItem CreateNavItem(NavigationViewItemData data)
    {
        var item = new WinUI.NavigationViewItem { Content = data.Content, Tag = data.Tag ?? data.Content };
        var icon = data.IconElement is not null
            ? V1.IconResolver.ResolveIconForDescriptor(data.IconElement)
            : data.Icon is not null
                ? V1.IconResolver.ResolveIconForDescriptor(new SymbolIconData(data.Icon))
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
