---
title: "Elements: Navigation"
description: "Navigation views, tabs, pivots, breadcrumbs and the title bar."
sidebar:
  label: "Navigation"
---

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Navigation](/microsoft-ui-reactor/main/guides/navigation)

## `BreadcrumbBarElement` [#breadcrumbbarelement]

The element record that describes a WinUI [`BreadcrumbBar`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.breadcrumbbar) control.

```csharp
public record BreadcrumbBarElement(BreadcrumbBarItemData[] Items, Action<BreadcrumbBarItemData>? OnItemClicked = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`BreadcrumbBar`](/microsoft-ui-reactor/main/reference/factories/navigation#breadcrumbbar)

**Modifiers:** [`ItemClicked`](/microsoft-ui-reactor/main/reference/modifiers/navigation#itemclicked), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Items` | [`BreadcrumbBarItemData[]`](/microsoft-ui-reactor/main/reference/controls/navigation-items#breadcrumbbaritemdata) |  |  |
| `OnItemClicked` | `Action<BreadcrumbBarItemData>?` | `null` |  |

## `FrameElement` [#frameelement]

The element record that describes a WinUI [`Frame`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.frame) control.

```csharp
public record FrameElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`Frame`](/microsoft-ui-reactor/main/reference/factories/navigation#frame)

**Modifiers:** [`Navigated`](/microsoft-ui-reactor/main/reference/modifiers/navigation#navigated), [`Navigating`](/microsoft-ui-reactor/main/reference/modifiers/navigation#navigating), [`NavigationFailed`](/microsoft-ui-reactor/main/reference/modifiers/navigation#navigationfailed), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `NavigationParameter` | `object?` |  |
| `OnNavigated` | `Action<Type>?` | Raised after a successful navigation. Receives the new `SourcePageType`. |
| `OnNavigating` | `Action<Type>?` | Raised before navigation begins. Receives the target `SourcePageType`. Cancellation is not supported via this fluent — use `.Set(...)` to wire the raw `Navigating` event for that. |
| `OnNavigationFailed` | `Action<Type, Exception>?` | Raised when a navigation fails. Receives the target `SourcePageType` and the failure exception. Wiring this also marks the failure handled, so a page whose constructor throws degrades into this callback instead of tearing down the application. |
| `SourcePageType` | `Type?` | The [`Page`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.page)-derived type to navigate to on mount. |

**`SourcePageType`**

Must be a `Page` the XAML metadata chain can resolve — in practice one with a `.xaml` file, since that is what makes the XAML compiler emit it. A code-only `Page` is refused and reported through [`FrameElement.OnNavigationFailed`](#frameelement).

## `NavigationHostElement` [#navigationhostelement]

Renders the content for the current route of a [`NavigationHandle<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/NavigationHandle). Created via `NavigationHost<TRoute>(nav, routeMap)` in the DSL. The reconciler uses a Grid container so outgoing/incoming pages can overlap during transitions (Phase 4).

```csharp
public record NavigationHostElement(object NavigationHandle, Func<object, Element> RouteMap) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`NavigationHost`](/microsoft-ui-reactor/main/reference/factories/navigation#navigationhost)

| Property | Type | Description |
| --- | --- | --- |
| `CacheMode` | [`NavigationCacheMode`](/microsoft-ui-reactor/main/reference/navigation/enums#navigationcachemode) |  |
| `CacheSize` | `int` |  |
| `NavigationHandle` | `object` |  |
| `RouteMap` | `Func<object, Element>` |  |
| `Transition` | [`NavigationTransition`](/microsoft-ui-reactor/main/reference/navigation/transitions#navigationtransition) |  |

## `NavigationViewElement` [#navigationviewelement]

The element record that describes a WinUI [`NavigationView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.navigationview) control.

```csharp
public record NavigationViewElement(NavigationViewItemData[] MenuItems, Element? Content = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`NavigationView`](/microsoft-ui-reactor/main/reference/factories/navigation#navigationview)

**Modifiers:** [`AlwaysShowHeader`](/microsoft-ui-reactor/main/reference/modifiers/navigation#alwaysshowheader), [`AutoSuggestBox`](/microsoft-ui-reactor/main/reference/modifiers/navigation#autosuggestbox), [`BackButtonVisible`](/microsoft-ui-reactor/main/reference/modifiers/navigation#backbuttonvisible), [`BackRequested`](/microsoft-ui-reactor/main/reference/modifiers/navigation#backrequested), [`CompactModeThresholdWidth`](/microsoft-ui-reactor/main/reference/modifiers/navigation#compactmodethresholdwidth), [`CompactPaneLength`](/microsoft-ui-reactor/main/reference/modifiers/navigation#compactpanelength), [`ContentOverlay`](/microsoft-ui-reactor/main/reference/modifiers/navigation#contentoverlay), [`DisplayModeChanged`](/microsoft-ui-reactor/main/reference/modifiers/navigation#displaymodechanged), [`ExpandedModeThresholdWidth`](/microsoft-ui-reactor/main/reference/modifiers/navigation#expandedmodethresholdwidth), [`FooterMenuItems`](/microsoft-ui-reactor/main/reference/modifiers/navigation#footermenuitems), [`IsPaneOpen`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#ispaneopen), [`ItemCollapsed`](/microsoft-ui-reactor/main/reference/modifiers/navigation#itemcollapsed), [`ItemExpanding`](/microsoft-ui-reactor/main/reference/modifiers/navigation#itemexpanding), [`ItemInvoked`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#iteminvoked), [`OpenPaneLength`](/microsoft-ui-reactor/main/reference/modifiers/navigation#openpanelength), [`OverflowLabelMode`](/microsoft-ui-reactor/main/reference/modifiers/navigation#overflowlabelmode), [`PaneCustomContent`](/microsoft-ui-reactor/main/reference/modifiers/navigation#panecustomcontent), [`PaneDisplayMode`](/microsoft-ui-reactor/main/reference/modifiers/navigation#panedisplaymode), [`PaneFooter`](/microsoft-ui-reactor/main/reference/modifiers/navigation#panefooter), [`PaneHeader`](/microsoft-ui-reactor/main/reference/modifiers/navigation#paneheader), [`PaneOpenChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#paneopenchanged), [`PaneTitle`](/microsoft-ui-reactor/main/reference/modifiers/navigation#panetitle), [`PaneToggleButtonVisible`](/microsoft-ui-reactor/main/reference/modifiers/navigation#panetogglebuttonvisible), [`PaneVisible`](/microsoft-ui-reactor/main/reference/modifiers/navigation#panevisible), [`SelectedTagChanged`](/microsoft-ui-reactor/main/reference/modifiers/navigation#selectedtagchanged), [`SelectionFollowsFocus`](/microsoft-ui-reactor/main/reference/modifiers/navigation#selectionfollowsfocus), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`SettingsSelected`](/microsoft-ui-reactor/main/reference/modifiers/navigation#settingsselected), [`ShoulderNavigation`](/microsoft-ui-reactor/main/reference/modifiers/navigation#shouldernavigation), [`TitleBarAutoPadding`](/microsoft-ui-reactor/main/reference/modifiers/navigation#titlebarautopadding), [`WithNavigation`](/microsoft-ui-reactor/main/reference/modifiers/navigation#withnavigation)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `AlwaysShowHeader` | `bool` |  | Whether [`NavigationViewElement.Header`](#navigationviewelement) stays visible in Minimal/Compact display modes. |
| `AutoSuggestBox` | [`AutoSuggestBoxElement?`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestboxelement) |  | AutoSuggestBox rendered at the top of the pane. Mirrors `NavigationView.AutoSuggestBox`. |
| `CompactModeThresholdWidth` | `double` |  | Window width below which the pane collapses to compact mode. `NaN` (the default) means Reactor never writes this property, leaving the WinUI default of 640. Sticky: changing an explicit value back to `NaN` skips the write rather than restoring 640. |
| `CompactPaneLength` | `double` |  | Width of the pane when compact. `NaN` (the default) means Reactor never writes this property, leaving the WinUI default of 48. Sticky: changing an explicit value back to `NaN` skips the write rather than restoring 48. |
| `Content` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |
| `ContentOverlay` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  | Element overlaid on top of [`NavigationViewElement.Content`](#navigationviewelement), e.g. a persistent player or status bar. |
| `ExpandedModeThresholdWidth` | `double` |  | Window width at which the pane auto-expands. `NaN` (the default) means Reactor never writes this property, leaving the WinUI default of 1008. Sticky: changing an explicit value back to `NaN` skips the write rather than restoring 1008. |
| `FooterMenuItems` | [`NavigationViewItemData[]`](/microsoft-ui-reactor/main/reference/controls/navigation-items#navigationviewitemdata) |  | Menu items pinned to the bottom of the pane, below [`NavigationViewElement.PaneFooter`](#navigationviewelement)'s divider. Share the [`NavigationViewElement.SelectedTag`](#navigationviewelement) namespace with [`NavigationViewElement.MenuItems`](#navigationviewelement). |
| `Header` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `IsBackButtonVisible` | `NavigationViewBackButtonVisible` |  | Visibility of NavigationView's built-in back button. Mirrors `NavigationView.IsBackButtonVisible`. Set to [`NavigationViewBackButtonVisible.Collapsed`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.navigationviewbackbuttonvisible.collapsed) when a [`TitleBarElement`](#titlebarelement) owns the back button instead. |
| `IsBackEnabled` | `bool` |  |  |
| `IsPaneOpen` | `bool` |  | Whether the pane is open. Defaults to `true`.  **Edge-triggered.** This declares a *transition*, not a mirror: Reactor writes the control only when the declared value *changes*. The control opens and closes its own pane (light dismiss, adaptive display-mode changes on resize) without touching the declared value, so re-rendering the same declared value will not force the pane back. Feed [`NavigationViewElement.OnPaneOpenChanged`](#navigationviewelement) into the state that drives this property (issue #916). |
| `IsPaneToggleButtonVisible` | `bool` |  | Whether NavigationView's built-in pane-toggle ("hamburger") button is shown. Mirrors `NavigationView.IsPaneToggleButtonVisible`. Set to `false` when a [`TitleBarElement`](#titlebarelement) owns the pane-toggle button instead. |
| `IsPaneVisible` | `bool` |  | Whether the pane is shown at all. `false` hides the whole pane, leaving only the content area. |
| `IsSettingsVisible` | `bool` |  |  |
| `IsTitleBarAutoPaddingEnabled` | `bool` |  | Whether NavigationView reserves top padding for the window's title bar. |
| `MenuItems` | [`NavigationViewItemData[]`](/microsoft-ui-reactor/main/reference/controls/navigation-items#navigationviewitemdata) |  |  |
| `OnBackRequested` | `Action?` |  |  |
| `OnDisplayModeChanged` | `Action<NavigationViewDisplayMode>?` |  | Raised when NavigationView switches between Minimal/Compact/Expanded in response to its width. |
| `OnItemCollapsed` | `Action<string?>?` |  | Raised when a hierarchical menu item is collapsed. Carries the item's tag. |
| `OnItemExpanding` | `Action<string?>?` |  | Raised when a hierarchical menu item is expanded. Carries the item's tag. |
| `OnItemInvoked` | `Action<string?>?` |  | Raised when a menu item is invoked (clicked / activated), including re-invoking the already-selected item, which [`NavigationViewElement.OnSelectedTagChanged`](#navigationviewelement) does not report. Carries the item's tag, or [`NavigationViewElement.SettingsTag`](#navigationviewelement) for the settings item. |
| `OnPaneOpenChanged` | `Action<bool>?` |  | Fires whenever the realized control's `IsPaneOpen` changes — including changes the app never requested (light dismiss, adaptive display-mode changes on resize) and the echo of a programmatic [`NavigationViewElement.IsPaneOpen`](#navigationviewelement) write. Feed the value back into the state that drives [`NavigationViewElement.IsPaneOpen`](#navigationviewelement) to keep controlled pane state in sync (issue #916). Same shape as `SplitViewElement.OnPaneOpenChanged`. |
| `OnSelectedTagChanged` | `Action<string?>?` |  |  |
| `OnSettingsSelected` | `Action?` |  | Raised when NavigationView's built-in settings item becomes selected — by the user, or by setting [`NavigationViewElement.SelectedTag`](#navigationviewelement) to [`NavigationViewElement.SettingsTag`](#navigationviewelement). Distinguishes the settings item from "nothing selected" — [`NavigationViewElement.OnSelectedTagChanged`](#navigationviewelement) reports `null` for both. |
| `OpenPaneLength` | `double` |  | Width of the pane when expanded. `NaN` (the default) means Reactor never writes this property, leaving the WinUI default of 320. Sticky: changing an explicit value back to `NaN` skips the write rather than restoring 320. |
| `OverflowLabelMode` | `NavigationViewOverflowLabelMode` |  | Whether the Top pane's overflow button shows a "More" label or only the chevron. |
| `PaneCustomContent` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  | Custom element rendered between the AutoSuggestBox and the menu items. |
| `PaneDisplayMode` | `NavigationViewPaneDisplayMode` |  |  |
| `PaneFooter` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  | Element rendered at the bottom of the pane, below all menu items. |
| `PaneHeader` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  | Element rendered at the top of the pane, above the menu items and below the pane title. |
| `PaneTitle` | `string?` |  |  |
| `SelectedTag` | `string?` |  |  |
| `SelectionFollowsFocus` | `NavigationViewSelectionFollowsFocus` |  | Whether moving keyboard focus between menu items also changes the selection. |
| `ShoulderNavigationEnabled` | `NavigationViewShoulderNavigationEnabled` |  | Whether gamepad shoulder buttons cycle the selection. |

| Member | Description |
| --- | --- |
| `SettingsTag` | Well-known [`NavigationViewElement.SelectedTag`](#navigationviewelement) value that selects NavigationView's built-in settings item — the only menu entry NavigationView creates itself, so it has no [`NavigationViewItemData`](/microsoft-ui-reactor/main/reference/controls/navigation-items#navigationviewitemdata) and cannot be addressed by an author-chosen tag. Also the tag reported to [`NavigationViewElement.OnItemInvoked`](#navigationviewelement) when settings is invoked. An author item carrying this tag still wins: selection searches [`NavigationViewElement.MenuItems`](#navigationviewelement) and [`NavigationViewElement.FooterMenuItems`](#navigationviewelement) before falling back to the settings item, so the sentinel can never shadow real content.  Reserved, so do not use it as an ordinary item tag. Selection would work, but `WithNavigation` skips it when mapping a tag back to a route — it routes the sentinel through its own `settingsRoute` parameter instead — so such an item would select without navigating. |

## `PipsPagerElement` [#pipspagerelement]

The element record that describes a WinUI [`PipsPager`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.pipspager) control.

```csharp
public record PipsPagerElement(int NumberOfPages) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`PipsPager`](/microsoft-ui-reactor/main/reference/factories/navigation#pipspager)

**Modifiers:** [`MaxVisiblePips`](/microsoft-ui-reactor/main/reference/modifiers/navigation#maxvisiblepips), [`NextButtonVisibility`](/microsoft-ui-reactor/main/reference/modifiers/navigation#nextbuttonvisibility), [`PreviousButtonVisibility`](/microsoft-ui-reactor/main/reference/modifiers/navigation#previousbuttonvisibility), [`SelectedPageIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/navigation#selectedpageindexchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`WrapMode`](/microsoft-ui-reactor/main/reference/modifiers/navigation#wrapmode)

| Property | Type | Description |
| --- | --- | --- |
| `MaxVisiblePips` | `int` | Maximum number of visible pips. Defaults to 5 (matches WinUI). |
| `NextButtonVisibility` | `PipsPagerButtonVisibility` | When the next button shows. Defaults to `Collapsed`. |
| `NumberOfPages` | `int` |  |
| `OnSelectedPageIndexChanged` | `Action<int>?` |  |
| `PreviousButtonVisibility` | `PipsPagerButtonVisibility` | When the previous button shows. Defaults to `Collapsed`. |
| `SelectedPageIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | Selected page index. Defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset); use `Optional<int>.Of(-1)` to force no page selection. |
| `WrapMode` | `PipsPagerWrapMode` | Whether the selected index wraps around the ends. Defaults to `None`. |

## `PivotElement` [#pivotelement]

The element record that describes a WinUI [`Pivot`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.pivot) control.

```csharp
public record PivotElement(PivotItemData[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`Pivot`](/microsoft-ui-reactor/main/reference/factories/navigation#pivot)

**Modifiers:** [`SelectedIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectedindexchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | [`PivotItemData[]`](/microsoft-ui-reactor/main/reference/controls/navigation-items#pivotitemdata) |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `SelectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | Selected pivot item index. Defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset); use `Optional<int>.Of(-1)` to force no selection. |
| `Title` | `string?` |  |

## `SelectorBarElement` [#selectorbarelement]

The element record that describes a WinUI [`SelectorBar`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.selectorbar) control.

```csharp
public record SelectorBarElement(SelectorBarItemData[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`SelectorBar`](/microsoft-ui-reactor/main/reference/factories/navigation#selectorbar)

**Modifiers:** [`SelectedIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectedindexchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | [`SelectorBarItemData[]`](/microsoft-ui-reactor/main/reference/controls/navigation-items#selectorbaritemdata) |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `SelectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | Selected item index. Defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset); use `Optional<int>.Of(-1)` to force no selection. |

## `TabViewElement` [#tabviewelement]

The element record that describes a WinUI [`TabView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.tabview) control.

```csharp
public record TabViewElement(TabViewItemData[] Tabs) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`TabView`](/microsoft-ui-reactor/main/reference/factories/navigation#tabview)

**Modifiers:** [`AddTabButtonClick`](/microsoft-ui-reactor/main/reference/modifiers/navigation#addtabbuttonclick), [`AllowDropTabs`](/microsoft-ui-reactor/main/reference/modifiers/navigation#allowdroptabs), [`CanDragTabs`](/microsoft-ui-reactor/main/reference/modifiers/navigation#candragtabs), [`CanReorderTabs`](/microsoft-ui-reactor/main/reference/modifiers/navigation#canreordertabs), [`CloseButtonOverlayMode`](/microsoft-ui-reactor/main/reference/modifiers/navigation#closebuttonoverlaymode), [`FillContentArea`](/microsoft-ui-reactor/main/reference/modifiers/navigation#fillcontentarea), [`IsAddButtonVisible`](/microsoft-ui-reactor/main/reference/modifiers/navigation#isaddbuttonvisible), [`IsAddTabButtonVisible`](/microsoft-ui-reactor/main/reference/modifiers/navigation#isaddtabbuttonvisible), [`SelectedIndexChanged`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#selectedindexchanged), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`ShowAddButton`](/microsoft-ui-reactor/main/reference/modifiers/navigation#showaddbutton), [`TabCloseRequested`](/microsoft-ui-reactor/main/reference/modifiers/navigation#tabcloserequested), [`TabDragCompleted`](/microsoft-ui-reactor/main/reference/modifiers/navigation#tabdragcompleted), [`TabDragStarting`](/microsoft-ui-reactor/main/reference/modifiers/navigation#tabdragstarting), [`TabStripFooter`](/microsoft-ui-reactor/main/reference/modifiers/navigation#tabstripfooter), [`TabStripHeader`](/microsoft-ui-reactor/main/reference/modifiers/navigation#tabstripheader), [`TabWidthMode`](/microsoft-ui-reactor/main/reference/modifiers/navigation#tabwidthmode)

| Property | Type | Description |
| --- | --- | --- |
| `AllowDropTabs` | `bool` | Whether tabs from another TabView can be dropped onto this one. |
| `CanDragTabs` | `bool` | Whether tabs can be dragged out (to a window). |
| `CanReorderTabs` | `bool` | Whether tabs can be reordered within the strip. |
| `CloseButtonOverlayMode` | `TabViewCloseButtonOverlayMode` | Controls when the per-tab close button is visible. Defaults to `Auto`. |
| `FillContentArea` | `bool` | Opt in to having the tab content area fill the space the TabView is given by its parent (issue #914). |
| `IsAddTabButtonVisible` | `bool` |  |
| `OnAddTabButtonClick` | `Action?` |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `OnTabCloseRequested` | `Action<int>?` |  |
| `OnTabDragCompleted` | `Action<int, bool>?` | Fires when a tab drag finishes — either landed on another TabView (`wasOutside == false`) or was dropped outside any TabView (`wasOutside == true`, used by §2.4 to trigger tear-out). |
| `OnTabDragStarting` | `Action<int>?` | Fires when the user starts dragging a tab. `tabIndex` is the index of the dragged tab in [`TabViewElement.Tabs`](#tabviewelement). Used by spec 045 §2.4 docking drag pipeline to start a `DockDragSession`. |
| `SelectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | Selected tab index. Defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset); use `Optional<int>.Of(-1)` to force no selection. |
| `TabStripFooter` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | Element rendered at the trailing edge of the tab strip. |
| `TabStripHeader` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | Element rendered at the leading edge of the tab strip. |
| `TabWidthMode` | `TabViewWidthMode` | How tab widths are sized. Defaults to `Equal` (matches WinUI default). |
| `Tabs` | [`TabViewItemData[]`](/microsoft-ui-reactor/main/reference/controls/navigation-items#tabviewitemdata) |  |

**`FillContentArea`**

WinUI's `DefaultTabViewStyle` ships `<Setter Property="VerticalAlignment" Value="Top" />`, so a TabView is arranged at its *desired* height and the `*` content row in its template never receives any leftover space — tab content is sized to content and the rest of the tab body stays unpainted. Setting this to `true` writes [`VerticalAlignment.Stretch`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.verticalalignment.stretch) on the control, which is what the XAML TabView templates do by hand. An explicit `.VAlign(…)` on this element always wins over the opt-in.

Defaults to `false` so the WinUI style default is preserved.

## `TitleBarElement` [#titlebarelement]

The element record that describes a WinUI [`TitleBar`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.titlebar) control.

```csharp
public record TitleBarElement(string Title) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`TitleBar`](/microsoft-ui-reactor/main/reference/factories/text#titlebar)

**Modifiers:** [`AutoRefreshDragRegions`](/microsoft-ui-reactor/main/reference/modifiers/navigation#autorefreshdragregions), [`BackButtonEnabled`](/microsoft-ui-reactor/main/reference/modifiers/navigation#backbuttonenabled), [`BackButtonVisible`](/microsoft-ui-reactor/main/reference/modifiers/navigation#backbuttonvisible), [`BackRequested`](/microsoft-ui-reactor/main/reference/modifiers/navigation#backrequested), [`Content`](/microsoft-ui-reactor/main/reference/modifiers/shared-controls#content), [`HeightOption`](/microsoft-ui-reactor/main/reference/modifiers/navigation#heightoption), [`Icon`](/microsoft-ui-reactor/main/reference/modifiers/navigation#icon), [`LeftHeader`](/microsoft-ui-reactor/main/reference/modifiers/navigation#leftheader), [`NoIcon`](/microsoft-ui-reactor/main/reference/modifiers/navigation#noicon), [`PaneToggleButtonVisible`](/microsoft-ui-reactor/main/reference/modifiers/navigation#panetogglebuttonvisible), [`PaneToggleRequested`](/microsoft-ui-reactor/main/reference/modifiers/navigation#panetogglerequested), [`PreferredTheme`](/microsoft-ui-reactor/main/reference/modifiers/navigation#preferredtheme), [`RightHeader`](/microsoft-ui-reactor/main/reference/modifiers/navigation#rightheader), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`Subtitle`](/microsoft-ui-reactor/main/reference/modifiers/navigation#subtitle), [`Tall`](/microsoft-ui-reactor/main/reference/modifiers/navigation#tall), [`WithNavigation`](/microsoft-ui-reactor/main/reference/modifiers/navigation#withnavigation)

| Property | Type | Description |
| --- | --- | --- |
| `AutoRefreshDragRegions` | `bool` | When `true`, the WinUI `TitleBar` re-derives its drag regions on every layout pass (WinApp SDK ≥ 2.1.3). Useful when [`TitleBarElement.Content`](#titlebarelement) changes across renders. Default `false` (matches the control default; interactive controls are still auto-excluded from the drag region). See spec 059. |
| `Content` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `HeightOption` | `WindowTitleBarHeight?` | System caption height for the hosting window. `Tall` is the standard layout when the title bar hosts navigation chrome (back button / pane toggle) and is the direct analogue of the XAML NavigationView template's `AppWindow.TitleBar.PreferredHeightOption = Tall` plus its `<RowDefinition Height="48" />`. (issue #917) |
| `Icon` | [`IconData?`](/microsoft-ui-reactor/main/reference/controls/icons#icondata) | Icon shown in the title bar's leading slot. Mirrors WinUI 3 `TitleBar.IconSource`. Pass a [`SymbolIconData`](/microsoft-ui-reactor/main/reference/controls/icons#symbolicondata) / [`FontIconData`](/microsoft-ui-reactor/main/reference/controls/icons#fonticondata) for built-in glyphs, or [`ImageIconData`](/microsoft-ui-reactor/main/reference/controls/icons#imageicondata) / [`BitmapIconData`](/microsoft-ui-reactor/main/reference/controls/icons#bitmapicondata) for a bundled `.ico` / image (e.g. `new ImageIconData(new Uri("ms-appx:///Assets/AppIcon.ico"))`).  Left unset, the title bar inherits the **window's** icon — [`WindowSpec.Icon`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#icon) if declared, otherwise the `Assets\AppIcon.ico` convention — so an app that already has an icon need not restate it here. The WinUI control does not do this itself. An icon that exists only as an executable PE resource is not inherited: that stage yields a raw `HICON` with no path, and a XAML `IconSource` needs an image source. Set [`TitleBarElement.SuppressIcon`](#titlebarelement) via `.NoIcon()` to show none. |
| `IsBackButtonEnabled` | `bool` |  |
| `IsBackButtonVisible` | `bool` |  |
| `IsPaneToggleButtonVisible` | `bool` |  |
| `LeftHeader` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | Custom content in the title bar's leading header slot. |
| `OnBackRequested` | `Action?` |  |
| `OnPaneToggleRequested` | `Action?` |  |
| `PreferredTheme` | `WindowTitleBarTheme?` | Theme of the hosting window's system caption buttons (`AppWindow.TitleBar.PreferredTheme`). `null` (the default) leaves the caption alone. (issue #1297) |
| `RightHeader` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `Subtitle` | `string?` |  |
| `SuppressIcon` | `bool` | Suppresses the icon entirely, including the one Reactor would otherwise inherit from the window (see [`TitleBarElement.Icon`](#titlebarelement)). Set via `.NoIcon()`. |
| `Title` | `string` |  |

**`HeightOption`**

Sets **both** halves: the native caption height and the control's own height (the WinUI `TitleBar` control does not follow the caption, so setting only one leaves the caption and the title bar disagreeing). An explicit `.Height(...)` on this element still wins over the implied 48 DIP. [`WindowSpec.TitleBarHeight`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#titlebarheight), when set, wins over this.

**`PreferredTheme`**

WinUI does not derive the caption theme from the content's `RequestedTheme`, so an app that themes its content opposite to the system declares the matching caption theme here. Removing the declaration (or unmounting the title bar) restores the value the caption had before Reactor first applied one. With several declaring title bars mounted, the most recent declaration wins and the others take over as it goes away. [`WindowSpec.TitleBarTheme`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#titlebartheme), when set, wins over this.

**`SuppressIcon`**

A separate flag rather than a null [`TitleBarElement.Icon`](#titlebarelement), because record `init` properties make "never set an icon" and "explicitly set none" indistinguishable — and those now mean opposite things.
