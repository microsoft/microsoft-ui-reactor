# Elements: Navigation

Navigation views, tabs, pivots, breadcrumbs and the title bar.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Navigation](../../navigation.md)

## `BreadcrumbBarElement`

The element record that describes a WinUI [`BreadcrumbBar`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.breadcrumbbar) control.

```csharp
public record BreadcrumbBarElement(BreadcrumbBarItemData[] Items, Action<BreadcrumbBarItemData>? OnItemClicked = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`BreadcrumbBar`](../factories/navigation.md#breadcrumbbar)

**Modifiers:** [`ItemClicked`](../modifiers/navigation.md#itemclicked), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `Items` | [`BreadcrumbBarItemData[]`](../controls/navigation-items.md#breadcrumbbaritemdata) |  |  |
| `OnItemClicked` | `Action<BreadcrumbBarItemData>?` | `null` |  |

## `FrameElement`

The element record that describes a WinUI [`Frame`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.frame) control.

```csharp
public record FrameElement : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Frame`](../factories/navigation.md#frame)

**Modifiers:** [`Navigated`](../modifiers/navigation.md#navigated), [`Navigating`](../modifiers/navigation.md#navigating), [`NavigationFailed`](../modifiers/navigation.md#navigationfailed), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `NavigationParameter` | `object?` |  |
| `OnNavigated` | `Action<Type>?` | Raised after a successful navigation. Receives the new `SourcePageType`. |
| `OnNavigating` | `Action<Type>?` | Raised before navigation begins. Receives the target `SourcePageType`. Cancellation is not supported via this fluent — use `.Set(...)` to wire the raw `Navigating` event for that. |
| `OnNavigationFailed` | `Action<Type, Exception>?` | Raised when a navigation fails. Receives the target `SourcePageType` and the failure exception. Wiring this also marks the failure handled, so a page whose constructor throws degrades into this callback instead of tearing down the application. |
| `SourcePageType` | `Type?` | The [`Page`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.page)-derived type to navigate to on mount. |

**`SourcePageType`**

Must be a `Page` the XAML metadata chain can resolve — in practice one with a `.xaml` file, since that is what makes the XAML compiler emit it. A code-only `Page` is refused and reported through [`FrameElement.OnNavigationFailed`](#frameelement).

## `NavigationHostElement`

Renders the content for the current route of a [`NavigationHandle<TRoute>`](../navigation/NavigationHandle.md). Created via `NavigationHost<TRoute>(nav, routeMap)` in the DSL. The reconciler uses a Grid container so outgoing/incoming pages can overlap during transitions (Phase 4).

```csharp
public record NavigationHostElement(object NavigationHandle, Func<object, Element> RouteMap) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`NavigationHost`](../factories/navigation.md#navigationhost)

| Property | Type | Description |
| --- | --- | --- |
| `CacheMode` | [`NavigationCacheMode`](../navigation/enums.md#navigationcachemode) |  |
| `CacheSize` | `int` |  |
| `NavigationHandle` | `object` |  |
| `RouteMap` | `Func<object, Element>` |  |
| `Transition` | [`NavigationTransition`](../navigation/transitions.md#navigationtransition) |  |

## `NavigationViewElement`

The element record that describes a WinUI [`NavigationView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.navigationview) control.

```csharp
public record NavigationViewElement(NavigationViewItemData[] MenuItems, Element? Content = null) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`NavigationView`](../factories/navigation.md#navigationview)

**Modifiers:** [`AlwaysShowHeader`](../modifiers/navigation.md#alwaysshowheader), [`AutoSuggestBox`](../modifiers/navigation.md#autosuggestbox), [`BackButtonVisible`](../modifiers/navigation.md#backbuttonvisible), [`BackRequested`](../modifiers/navigation.md#backrequested), [`CompactModeThresholdWidth`](../modifiers/navigation.md#compactmodethresholdwidth), [`CompactPaneLength`](../modifiers/navigation.md#compactpanelength), [`ContentOverlay`](../modifiers/navigation.md#contentoverlay), [`DisplayModeChanged`](../modifiers/navigation.md#displaymodechanged), [`ExpandedModeThresholdWidth`](../modifiers/navigation.md#expandedmodethresholdwidth), [`FooterMenuItems`](../modifiers/navigation.md#footermenuitems), [`IsPaneOpen`](../modifiers/shared-controls.md#ispaneopen), [`ItemCollapsed`](../modifiers/navigation.md#itemcollapsed), [`ItemExpanding`](../modifiers/navigation.md#itemexpanding), [`ItemInvoked`](../modifiers/shared-controls.md#iteminvoked), [`OpenPaneLength`](../modifiers/navigation.md#openpanelength), [`OverflowLabelMode`](../modifiers/navigation.md#overflowlabelmode), [`PaneCustomContent`](../modifiers/navigation.md#panecustomcontent), [`PaneDisplayMode`](../modifiers/navigation.md#panedisplaymode), [`PaneFooter`](../modifiers/navigation.md#panefooter), [`PaneHeader`](../modifiers/navigation.md#paneheader), [`PaneOpenChanged`](../modifiers/shared-controls.md#paneopenchanged), [`PaneTitle`](../modifiers/navigation.md#panetitle), [`PaneToggleButtonVisible`](../modifiers/navigation.md#panetogglebuttonvisible), [`PaneVisible`](../modifiers/navigation.md#panevisible), [`SelectedTagChanged`](../modifiers/navigation.md#selectedtagchanged), [`SelectionFollowsFocus`](../modifiers/navigation.md#selectionfollowsfocus), [`Set`](../modifiers/native-properties.md#set), [`SettingsSelected`](../modifiers/navigation.md#settingsselected), [`ShoulderNavigation`](../modifiers/navigation.md#shouldernavigation), [`TitleBarAutoPadding`](../modifiers/navigation.md#titlebarautopadding), [`WithNavigation`](../modifiers/navigation.md#withnavigation)

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `AlwaysShowHeader` | `bool` |  | Whether [`NavigationViewElement.Header`](#navigationviewelement) stays visible in Minimal/Compact display modes. |
| `AutoSuggestBox` | [`AutoSuggestBoxElement?`](input.md#autosuggestboxelement) |  | AutoSuggestBox rendered at the top of the pane. Mirrors `NavigationView.AutoSuggestBox`. |
| `CompactModeThresholdWidth` | `double` |  | Window width below which the pane collapses to compact mode. `NaN` (the default) means Reactor never writes this property, leaving the WinUI default of 640. Sticky: changing an explicit value back to `NaN` skips the write rather than restoring 640. |
| `CompactPaneLength` | `double` |  | Width of the pane when compact. `NaN` (the default) means Reactor never writes this property, leaving the WinUI default of 48. Sticky: changing an explicit value back to `NaN` skips the write rather than restoring 48. |
| `Content` | [`Element?`](Element.md) | `null` |  |
| `ContentOverlay` | [`Element?`](Element.md) |  | Element overlaid on top of [`NavigationViewElement.Content`](#navigationviewelement), e.g. a persistent player or status bar. |
| `ExpandedModeThresholdWidth` | `double` |  | Window width at which the pane auto-expands. `NaN` (the default) means Reactor never writes this property, leaving the WinUI default of 1008. Sticky: changing an explicit value back to `NaN` skips the write rather than restoring 1008. |
| `FooterMenuItems` | [`NavigationViewItemData[]`](../controls/navigation-items.md#navigationviewitemdata) |  | Menu items pinned to the bottom of the pane, below [`NavigationViewElement.PaneFooter`](#navigationviewelement)'s divider. Share the [`NavigationViewElement.SelectedTag`](#navigationviewelement) namespace with [`NavigationViewElement.MenuItems`](#navigationviewelement). |
| `Header` | [`Element?`](Element.md) |  |  |
| `IsBackButtonVisible` | `NavigationViewBackButtonVisible` |  | Visibility of NavigationView's built-in back button. Mirrors `NavigationView.IsBackButtonVisible`. Set to [`NavigationViewBackButtonVisible.Collapsed`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.navigationviewbackbuttonvisible.collapsed) when a [`TitleBarElement`](#titlebarelement) owns the back button instead. |
| `IsBackEnabled` | `bool` |  |  |
| `IsPaneOpen` | `bool` |  | Whether the pane is open. Defaults to `true`.  **Edge-triggered.** This declares a *transition*, not a mirror: Reactor writes the control only when the declared value *changes*. The control opens and closes its own pane (light dismiss, adaptive display-mode changes on resize) without touching the declared value, so re-rendering the same declared value will not force the pane back. Feed [`NavigationViewElement.OnPaneOpenChanged`](#navigationviewelement) into the state that drives this property (issue #916). |
| `IsPaneToggleButtonVisible` | `bool` |  | Whether NavigationView's built-in pane-toggle ("hamburger") button is shown. Mirrors `NavigationView.IsPaneToggleButtonVisible`. Set to `false` when a [`TitleBarElement`](#titlebarelement) owns the pane-toggle button instead. |
| `IsPaneVisible` | `bool` |  | Whether the pane is shown at all. `false` hides the whole pane, leaving only the content area. |
| `IsSettingsVisible` | `bool` |  |  |
| `IsTitleBarAutoPaddingEnabled` | `bool` |  | Whether NavigationView reserves top padding for the window's title bar. |
| `MenuItems` | [`NavigationViewItemData[]`](../controls/navigation-items.md#navigationviewitemdata) |  |  |
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
| `PaneCustomContent` | [`Element?`](Element.md) |  | Custom element rendered between the AutoSuggestBox and the menu items. |
| `PaneDisplayMode` | `NavigationViewPaneDisplayMode` |  |  |
| `PaneFooter` | [`Element?`](Element.md) |  | Element rendered at the bottom of the pane, below all menu items. |
| `PaneHeader` | [`Element?`](Element.md) |  | Element rendered at the top of the pane, above the menu items and below the pane title. |
| `PaneTitle` | `string?` |  |  |
| `SelectedTag` | `string?` |  |  |
| `SelectionFollowsFocus` | `NavigationViewSelectionFollowsFocus` |  | Whether moving keyboard focus between menu items also changes the selection. |
| `ShoulderNavigationEnabled` | `NavigationViewShoulderNavigationEnabled` |  | Whether gamepad shoulder buttons cycle the selection. |

| Member | Description |
| --- | --- |
| `SettingsTag` | Well-known [`NavigationViewElement.SelectedTag`](#navigationviewelement) value that selects NavigationView's built-in settings item — the only menu entry NavigationView creates itself, so it has no [`NavigationViewItemData`](../controls/navigation-items.md#navigationviewitemdata) and cannot be addressed by an author-chosen tag. Also the tag reported to [`NavigationViewElement.OnItemInvoked`](#navigationviewelement) when settings is invoked. An author item carrying this tag still wins: selection searches [`NavigationViewElement.MenuItems`](#navigationviewelement) and [`NavigationViewElement.FooterMenuItems`](#navigationviewelement) before falling back to the settings item, so the sentinel can never shadow real content.  Reserved, so do not use it as an ordinary item tag. Selection would work, but `WithNavigation` skips it when mapping a tag back to a route — it routes the sentinel through its own `settingsRoute` parameter instead — so such an item would select without navigating. |

## `PipsPagerElement`

The element record that describes a WinUI [`PipsPager`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.pipspager) control.

```csharp
public record PipsPagerElement(int NumberOfPages) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`PipsPager`](../factories/navigation.md#pipspager)

**Modifiers:** [`MaxVisiblePips`](../modifiers/navigation.md#maxvisiblepips), [`NextButtonVisibility`](../modifiers/navigation.md#nextbuttonvisibility), [`PreviousButtonVisibility`](../modifiers/navigation.md#previousbuttonvisibility), [`SelectedPageIndexChanged`](../modifiers/navigation.md#selectedpageindexchanged), [`Set`](../modifiers/native-properties.md#set), [`WrapMode`](../modifiers/navigation.md#wrapmode)

| Property | Type | Description |
| --- | --- | --- |
| `MaxVisiblePips` | `int` | Maximum number of visible pips. Defaults to 5 (matches WinUI). |
| `NextButtonVisibility` | `PipsPagerButtonVisibility` | When the next button shows. Defaults to `Collapsed`. |
| `NumberOfPages` | `int` |  |
| `OnSelectedPageIndexChanged` | `Action<int>?` |  |
| `PreviousButtonVisibility` | `PipsPagerButtonVisibility` | When the previous button shows. Defaults to `Collapsed`. |
| `SelectedPageIndex` | [`Optional<int>`](../components/Optional.md) | Selected page index. Defaults to [`Optional.Unset`](../components/Optional.md#unset); use `Optional<int>.Of(-1)` to force no page selection. |
| `WrapMode` | `PipsPagerWrapMode` | Whether the selected index wraps around the ends. Defaults to `None`. |

## `PivotElement`

The element record that describes a WinUI [`Pivot`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.pivot) control.

```csharp
public record PivotElement(PivotItemData[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`Pivot`](../factories/navigation.md#pivot)

**Modifiers:** [`SelectedIndexChanged`](../modifiers/shared-controls.md#selectedindexchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | [`PivotItemData[]`](../controls/navigation-items.md#pivotitemdata) |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `SelectedIndex` | [`Optional<int>`](../components/Optional.md) | Selected pivot item index. Defaults to [`Optional.Unset`](../components/Optional.md#unset); use `Optional<int>.Of(-1)` to force no selection. |
| `Title` | `string?` |  |

## `SelectorBarElement`

The element record that describes a WinUI [`SelectorBar`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.selectorbar) control.

```csharp
public record SelectorBarElement(SelectorBarItemData[] Items) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`SelectorBar`](../factories/navigation.md#selectorbar)

**Modifiers:** [`SelectedIndexChanged`](../modifiers/shared-controls.md#selectedindexchanged), [`Set`](../modifiers/native-properties.md#set)

| Property | Type | Description |
| --- | --- | --- |
| `Items` | [`SelectorBarItemData[]`](../controls/navigation-items.md#selectorbaritemdata) |  |
| `OnSelectedIndexChanged` | `Action<int>?` |  |
| `SelectedIndex` | [`Optional<int>`](../components/Optional.md) | Selected item index. Defaults to [`Optional.Unset`](../components/Optional.md#unset); use `Optional<int>.Of(-1)` to force no selection. |

## `TabViewElement`

The element record that describes a WinUI [`TabView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.tabview) control.

```csharp
public record TabViewElement(TabViewItemData[] Tabs) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`TabView`](../factories/navigation.md#tabview)

**Modifiers:** [`AddTabButtonClick`](../modifiers/navigation.md#addtabbuttonclick), [`AllowDropTabs`](../modifiers/navigation.md#allowdroptabs), [`CanDragTabs`](../modifiers/navigation.md#candragtabs), [`CanReorderTabs`](../modifiers/navigation.md#canreordertabs), [`CloseButtonOverlayMode`](../modifiers/navigation.md#closebuttonoverlaymode), [`FillContentArea`](../modifiers/navigation.md#fillcontentarea), [`IsAddButtonVisible`](../modifiers/navigation.md#isaddbuttonvisible), [`IsAddTabButtonVisible`](../modifiers/navigation.md#isaddtabbuttonvisible), [`SelectedIndexChanged`](../modifiers/shared-controls.md#selectedindexchanged), [`Set`](../modifiers/native-properties.md#set), [`ShowAddButton`](../modifiers/navigation.md#showaddbutton), [`TabCloseRequested`](../modifiers/navigation.md#tabcloserequested), [`TabDragCompleted`](../modifiers/navigation.md#tabdragcompleted), [`TabDragStarting`](../modifiers/navigation.md#tabdragstarting), [`TabStripFooter`](../modifiers/navigation.md#tabstripfooter), [`TabStripHeader`](../modifiers/navigation.md#tabstripheader), [`TabWidthMode`](../modifiers/navigation.md#tabwidthmode)

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
| `SelectedIndex` | [`Optional<int>`](../components/Optional.md) | Selected tab index. Defaults to [`Optional.Unset`](../components/Optional.md#unset); use `Optional<int>.Of(-1)` to force no selection. |
| `TabStripFooter` | [`Element?`](Element.md) | Element rendered at the trailing edge of the tab strip. |
| `TabStripHeader` | [`Element?`](Element.md) | Element rendered at the leading edge of the tab strip. |
| `TabWidthMode` | `TabViewWidthMode` | How tab widths are sized. Defaults to `Equal` (matches WinUI default). |
| `Tabs` | [`TabViewItemData[]`](../controls/navigation-items.md#tabviewitemdata) |  |

**`FillContentArea`**

WinUI's `DefaultTabViewStyle` ships `<Setter Property="VerticalAlignment" Value="Top" />`, so a TabView is arranged at its *desired* height and the `*` content row in its template never receives any leftover space — tab content is sized to content and the rest of the tab body stays unpainted. Setting this to `true` writes [`VerticalAlignment.Stretch`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.verticalalignment.stretch) on the control, which is what the XAML TabView templates do by hand. An explicit `.VAlign(…)` on this element always wins over the opt-in.

Defaults to `false` so the WinUI style default is preserved.

## `TitleBarElement`

The element record that describes a WinUI [`TitleBar`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.titlebar) control.

```csharp
public record TitleBarElement(string Title) : Element
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`Element`](Element.md).

**Created by:** [`TitleBar`](../factories/text.md#titlebar)

**Modifiers:** [`AutoRefreshDragRegions`](../modifiers/navigation.md#autorefreshdragregions), [`BackButtonEnabled`](../modifiers/navigation.md#backbuttonenabled), [`BackButtonVisible`](../modifiers/navigation.md#backbuttonvisible), [`BackRequested`](../modifiers/navigation.md#backrequested), [`Content`](../modifiers/shared-controls.md#content), [`HeightOption`](../modifiers/navigation.md#heightoption), [`Icon`](../modifiers/navigation.md#icon), [`LeftHeader`](../modifiers/navigation.md#leftheader), [`NoIcon`](../modifiers/navigation.md#noicon), [`PaneToggleButtonVisible`](../modifiers/navigation.md#panetogglebuttonvisible), [`PaneToggleRequested`](../modifiers/navigation.md#panetogglerequested), [`PreferredTheme`](../modifiers/navigation.md#preferredtheme), [`RightHeader`](../modifiers/navigation.md#rightheader), [`Set`](../modifiers/native-properties.md#set), [`Subtitle`](../modifiers/navigation.md#subtitle), [`Tall`](../modifiers/navigation.md#tall), [`WithNavigation`](../modifiers/navigation.md#withnavigation)

| Property | Type | Description |
| --- | --- | --- |
| `AutoRefreshDragRegions` | `bool` | When `true`, the WinUI `TitleBar` re-derives its drag regions on every layout pass (WinApp SDK ≥ 2.1.3). Useful when [`TitleBarElement.Content`](#titlebarelement) changes across renders. Default `false` (matches the control default; interactive controls are still auto-excluded from the drag region). See spec 059. |
| `Content` | [`Element?`](Element.md) |  |
| `HeightOption` | `WindowTitleBarHeight?` | System caption height for the hosting window. `Tall` is the standard layout when the title bar hosts navigation chrome (back button / pane toggle) and is the direct analogue of the XAML NavigationView template's `AppWindow.TitleBar.PreferredHeightOption = Tall` plus its `<RowDefinition Height="48" />`. (issue #917) |
| `Icon` | [`IconData?`](../controls/icons.md#icondata) | Icon shown in the title bar's leading slot. Mirrors WinUI 3 `TitleBar.IconSource`. Pass a [`SymbolIconData`](../controls/icons.md#symbolicondata) / [`FontIconData`](../controls/icons.md#fonticondata) for built-in glyphs, or [`ImageIconData`](../controls/icons.md#imageicondata) / [`BitmapIconData`](../controls/icons.md#bitmapicondata) for a bundled `.ico` / image (e.g. `new ImageIconData(new Uri("ms-appx:///Assets/AppIcon.ico"))`).  Left unset, the title bar inherits the **window's** icon — [`WindowSpec.Icon`](../hosting/WindowSpec.md#icon) if declared, otherwise the `Assets\AppIcon.ico` convention — so an app that already has an icon need not restate it here. The WinUI control does not do this itself. An icon that exists only as an executable PE resource is not inherited: that stage yields a raw `HICON` with no path, and a XAML `IconSource` needs an image source. Set [`TitleBarElement.SuppressIcon`](#titlebarelement) via `.NoIcon()` to show none. |
| `IsBackButtonEnabled` | `bool` |  |
| `IsBackButtonVisible` | `bool` |  |
| `IsPaneToggleButtonVisible` | `bool` |  |
| `LeftHeader` | [`Element?`](Element.md) | Custom content in the title bar's leading header slot. |
| `OnBackRequested` | `Action?` |  |
| `OnPaneToggleRequested` | `Action?` |  |
| `PreferredTheme` | `WindowTitleBarTheme?` | Theme of the hosting window's system caption buttons (`AppWindow.TitleBar.PreferredTheme`). `null` (the default) leaves the caption alone. (issue #1297) |
| `RightHeader` | [`Element?`](Element.md) |  |
| `Subtitle` | `string?` |  |
| `SuppressIcon` | `bool` | Suppresses the icon entirely, including the one Reactor would otherwise inherit from the window (see [`TitleBarElement.Icon`](#titlebarelement)). Set via `.NoIcon()`. |
| `Title` | `string` |  |

**`HeightOption`**

Sets **both** halves: the native caption height and the control's own height (the WinUI `TitleBar` control does not follow the caption, so setting only one leaves the caption and the title bar disagreeing). An explicit `.Height(...)` on this element still wins over the implied 48 DIP. [`WindowSpec.TitleBarHeight`](../hosting/WindowSpec.md#titlebarheight), when set, wins over this.

**`PreferredTheme`**

WinUI does not derive the caption theme from the content's `RequestedTheme`, so an app that themes its content opposite to the system declares the matching caption theme here. Removing the declaration (or unmounting the title bar) restores the value the caption had before Reactor first applied one. With several declaring title bars mounted, the most recent declaration wins and the others take over as it goes away. [`WindowSpec.TitleBarTheme`](../hosting/WindowSpec.md#titlebartheme), when set, wins over this.

**`SuppressIcon`**

A separate flag rather than a null [`TitleBarElement.Icon`](#titlebarelement), because record `init` properties make "never set an icon" and "explicitly set none" indistinguishable — and those now mean opposite things.
