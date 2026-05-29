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
        if (oldElement is null)
        {
            // Mount: build fresh.
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
            // Update: reconcile in place so reused containers keep IsExpanded.
            // Mirrors Reconciler.ReconcileNavMenuItems (legacy arm) — see the note
            // there on why a clear-and-rebuild collapses hierarchical items.
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
        global::System.Collections.Generic.IList<object> live,
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

        var reusable = new global::System.Collections.Generic.Dictionary<string, WinUI.NavigationViewItem>();
        foreach (var nvi in live.OfType<WinUI.NavigationViewItem>().Where(x => x.Tag is string))
            reusable[(string)nvi.Tag] = nvi;

        var oldByTag = new global::System.Collections.Generic.Dictionary<string, NavigationViewItemData>();
        if (oldData is not null)
            foreach (var d in oldData.Where(d => !d.IsHeader))
                oldByTag[d.Tag ?? d.Content] = d;

        live.Clear();
        foreach (var data in newData)
        {
            if (data.IsHeader)
            {
                live.Add(new WinUI.NavigationViewItemHeader { Content = data.Content });
                continue;
            }

            // Consume the reuse entry so duplicate sibling keys fall through to a
            // fresh container rather than adding the same WinUI item to live twice.
            var key = data.Tag ?? data.Content;
            if (reusable.Remove(key, out var nvi))
                UpdateNavItemInPlace(nvi, oldByTag.GetValueOrDefault(key), data);
            else
                nvi = CreateNavItem(data);
            live.Add(nvi);
        }
    }

    private static bool StructureMatches(
        global::System.Collections.Generic.IList<object> live,
        NavigationViewItemData[] newData)
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
                ? Reconciler.ResolveIconForDescriptor(data.IconElement)
                : data.Icon is not null
                    ? Reconciler.ResolveIconForDescriptor(new SymbolIconData(data.Icon))
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
