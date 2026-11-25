# Control modifiers: Navigation

Modifiers that only the controls in the Navigation area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Navigation](../../navigation.md)

## `AddTabButtonClick`

Wires the add-tab-button click handler. Passing `null` clears.

```csharp
TabViewElement AddTabButtonClick(this TabViewElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `AllowDropTabs`

Whether tabs from another TabView can be dropped onto this one.

```csharp
TabViewElement AllowDropTabs(this TabViewElement el, bool allow = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `allow` | `bool` | `true` |  |

## `AlwaysShowHeader`

Sets whether the header stays visible in Minimal/Compact display modes.

```csharp
NavigationViewElement AlwaysShowHeader(this NavigationViewElement el, bool alwaysShow = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `alwaysShow` | `bool` | `true` |  |

## `AutoRefreshDragRegions`

When `true`, the title bar re-derives its drag regions on every layout pass — the right fit for content that changes across renders. WinApp SDK ≥ 2.1.3. Interactive controls are auto-excluded from the drag region either way; use [`IsDragRegion`](input-and-events.md#isdragregion) on a child to override per element.

```csharp
TitleBarElement AutoRefreshDragRegions(this TitleBarElement el, bool value = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `value` | `bool` | `true` |  |

## `AutoSuggestBox`

Sets the AutoSuggestBox rendered at the top of the pane.

```csharp
NavigationViewElement AutoSuggestBox(this NavigationViewElement el, AutoSuggestBoxElement box)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `box` | [`AutoSuggestBoxElement`](../elements/input.md#autosuggestboxelement) |  |

## `BackButtonEnabled`

Enables or disables the back button (when visible).

```csharp
TitleBarElement BackButtonEnabled(this TitleBarElement el, bool enabled)
```

Declared in `ElementExtensions`.

**Applies to:** [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `enabled` | `bool` |  |

## `BackButtonVisible`

```csharp
NavigationViewElement BackButtonVisible(this NavigationViewElement el, NavigationViewBackButtonVisible visible)
NavigationViewElement BackButtonVisible(this NavigationViewElement el, bool visible)
TitleBarElement BackButtonVisible(this TitleBarElement el, bool visible)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `BackButtonVisible(NavigationViewElement, NavigationViewBackButtonVisible)` | Sets the visibility of NavigationView's built-in back button (`NavigationView.IsBackButtonVisible`). |
| `BackButtonVisible(NavigationViewElement, bool)` | Sets the visibility of NavigationView's built-in back button using a bool — `true` maps to [`NavigationViewBackButtonVisible.Visible`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.navigationviewbackbuttonvisible.visible) and `false` to [`NavigationViewBackButtonVisible.Collapsed`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.navigationviewbackbuttonvisible.collapsed). |
| `BackButtonVisible(TitleBarElement, bool)` | Shows or hides the back button on the title bar. |

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement), [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `visible` | `NavigationViewBackButtonVisible` or `bool` |  |

## `BackRequested`

Wires the back-requested handler. Passing `null` clears.

```csharp
NavigationViewElement BackRequested(this NavigationViewElement el, Action? handler)
TitleBarElement BackRequested(this TitleBarElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement), [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `CanDragTabs`

Whether tabs can be dragged out (e.g. to detach into a new window).

```csharp
TabViewElement CanDragTabs(this TabViewElement el, bool canDrag = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `canDrag` | `bool` | `true` |  |

## `CanReorderTabs`

Whether tabs can be reordered within the strip via drag.

```csharp
TabViewElement CanReorderTabs(this TabViewElement el, bool canReorder = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `canReorder` | `bool` | `true` |  |

## `CloseButtonOverlayMode`

Controls when the per-tab close button is visible (Auto, OnPointerOver, Always).

```csharp
TabViewElement CloseButtonOverlayMode(this TabViewElement el, TabViewCloseButtonOverlayMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `TabViewCloseButtonOverlayMode` |  |

## `CompactModeThresholdWidth`

Sets the window width below which the pane collapses to compact mode.

```csharp
NavigationViewElement CompactModeThresholdWidth(this NavigationViewElement el, double width)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

## `CompactPaneLength`

Sets the width of the pane when compact.

```csharp
NavigationViewElement CompactPaneLength(this NavigationViewElement el, double length)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `length` | `double` |  |

## `ContentOverlay`

Sets the element overlaid on top of the content area.

```csharp
NavigationViewElement ContentOverlay(this NavigationViewElement el, Element overlay)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `overlay` | [`Element`](../elements/Element.md) |  |

## `DisplayModeChanged`

Wires the display-mode-changed handler. Passing `null` clears.

```csharp
NavigationViewElement DisplayModeChanged(this NavigationViewElement el, Action<NavigationViewDisplayMode>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<NavigationViewDisplayMode>?` |  |

## `ExpandedModeThresholdWidth`

Sets the window width at which the pane auto-expands.

```csharp
NavigationViewElement ExpandedModeThresholdWidth(this NavigationViewElement el, double width)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

## `FillContentArea`

Makes the tab content area fill the space the parent gives the TabView, instead of collapsing to the content's own height (issue #914).

```csharp
TabViewElement FillContentArea(this TabViewElement el, bool fill = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `fill` | `bool` | `true` |  |

**Remarks**

WinUI's default TabView style sets `VerticalAlignment="Top"` on the control, so by default the tab body is sized to its content and a tab child with its own background paints only a band. An explicit `.VAlign(…)` on the TabView element always wins over this opt-in. See [`TabViewElement.FillContentArea`](../elements/navigation.md#tabviewelement).

## `FooterMenuItems`

Sets the menu items pinned to the bottom of the pane. They share the `SelectedTag` namespace with the primary menu items.

```csharp
NavigationViewElement FooterMenuItems(this NavigationViewElement el, params NavigationViewItemData[] items)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `items` | [`NavigationViewItemData[]`](../controls/navigation-items.md#navigationviewitemdata) |  |

## `HeightOption`

Sets the system caption height for the hosting window. (issue #917)

```csharp
TitleBarElement HeightOption(this TitleBarElement el, WindowTitleBarHeight height)
```

Declared in `ElementExtensions`.

**Applies to:** [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `height` | [`WindowTitleBarHeight`](../hosting/window-options.md#windowtitlebarheight) |  |

**Remarks**

Applies both halves — the native caption (`AppWindow.TitleBar.PreferredHeightOption`) and, for [`WindowTitleBarHeight.Tall`](../hosting/window-options.md#values_12), the control's own 48 DIP height, which the WinUI `TitleBar` control does not derive from the caption. An explicit `.Height(...)` on this element still wins. [`WindowSpec.TitleBarHeight`](../hosting/WindowSpec.md#titlebarheight), when set, wins over this.

## `Icon`

```csharp
TitleBarElement Icon(this TitleBarElement el, IconData icon)
TitleBarElement Icon(this TitleBarElement el, string imageUri)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `Icon(TitleBarElement, IconData)` | Sets the icon shown in the leading slot. Pass a [`SymbolIconData`](../controls/icons.md#symbolicondata), [`FontIconData`](../controls/icons.md#fonticondata), [`ImageIconData`](../controls/icons.md#imageicondata), or [`BitmapIconData`](../controls/icons.md#bitmapicondata). |
| `Icon(TitleBarElement, string)` | Convenience overload — sets the icon to a bundled image / .ico via a Uri string (e.g. `"ms-appx:///Assets/AppIcon.ico"`). |

**Applies to:** [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `icon` | [`IconData`](../controls/icons.md#icondata) |  |
| `imageUri` | `string` |  |

**Remarks**

Only needed to show something *other* than the app's own icon — with no explicit icon the title bar inherits the window's (`WindowSpec.Icon`, else `Assets\AppIcon.ico`). Use [`NoIcon`](#noicon) to show none at all.

## `IsAddButtonVisible`

Controls visibility of the "add tab" button in a TabView.

```csharp
TabViewElement IsAddButtonVisible(this TabViewElement el, bool visible = true)
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsAddTabButtonVisible() — aligns with WinUI naming (see #268).

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `visible` | `bool` | `true` |  |

## `IsAddTabButtonVisible`

Controls visibility of the "add tab" button in a TabView.

```csharp
TabViewElement IsAddTabButtonVisible(this TabViewElement el, bool visible = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `visible` | `bool` | `true` |  |

## `ItemClicked`

Wires the item-clicked handler. Passing `null` clears.

```csharp
BreadcrumbBarElement ItemClicked(this BreadcrumbBarElement el, Action<BreadcrumbBarItemData>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`BreadcrumbBarElement`](../elements/navigation.md#breadcrumbbarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<BreadcrumbBarItemData>?` |  |

## `ItemCollapsed`

Wires the hierarchical item-collapsed handler. Passing `null` clears.

```csharp
NavigationViewElement ItemCollapsed(this NavigationViewElement el, Action<string?>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string?>?` |  |

## `ItemExpanding`

Wires the hierarchical item-expanding handler. Passing `null` clears.

```csharp
NavigationViewElement ItemExpanding(this NavigationViewElement el, Action<string?>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string?>?` |  |

## `LeftHeader`

Sets the leading-edge header slot (typically navigation or contextual controls).

```csharp
TitleBarElement LeftHeader(this TitleBarElement el, Element leftHeader)
```

Declared in `ElementExtensions`.

**Applies to:** [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `leftHeader` | [`Element`](../elements/Element.md) |  |

## `MaxVisiblePips`

Maximum number of visible pips.

```csharp
PipsPagerElement MaxVisiblePips(this PipsPagerElement el, int max)
```

Declared in `ElementExtensions`.

**Applies to:** [`PipsPagerElement`](../elements/navigation.md#pipspagerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `max` | `int` |  |

## `Navigated`

Wires the navigated-completed handler. Receives the new `SourcePageType`. Passing `null` clears.

```csharp
FrameElement Navigated(this FrameElement el, Action<Type>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`FrameElement`](../elements/navigation.md#frameelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<Type>?` |  |

## `Navigating`

Wires the navigating-started handler. Receives the target `SourcePageType`. Passing `null` clears.

```csharp
FrameElement Navigating(this FrameElement el, Action<Type>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`FrameElement`](../elements/navigation.md#frameelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<Type>?` |  |

## `NavigationFailed`

Wires the navigation-failed handler. Receives target type and exception. Passing `null` clears.

```csharp
FrameElement NavigationFailed(this FrameElement el, Action<Type, Exception>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`FrameElement`](../elements/navigation.md#frameelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<Type, Exception>?` |  |

## `NextButtonVisibility`

When the next button shows.

```csharp
PipsPagerElement NextButtonVisibility(this PipsPagerElement el, PipsPagerButtonVisibility visibility)
```

Declared in `ElementExtensions`.

**Applies to:** [`PipsPagerElement`](../elements/navigation.md#pipspagerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `visibility` | `PipsPagerButtonVisibility` |  |

## `NoIcon`

Shows no icon at all, overriding the window icon the title bar would otherwise inherit. The opt-out for a deliberately bare title bar on an app that ships an icon.

```csharp
TitleBarElement NoIcon(this TitleBarElement el)
```

Declared in `ElementExtensions`.

**Applies to:** [`TitleBarElement`](../elements/navigation.md#titlebarelement).

## `OpenPaneLength`

Sets the width of the pane when expanded.

```csharp
NavigationViewElement OpenPaneLength(this NavigationViewElement el, double length)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `length` | `double` |  |

## `OverflowLabelMode`

Sets whether the Top pane's overflow button shows a "More" label or only the chevron.

```csharp
NavigationViewElement OverflowLabelMode(this NavigationViewElement el, NavigationViewOverflowLabelMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `NavigationViewOverflowLabelMode` |  |

## `PaneCustomContent`

Sets the custom element rendered between the AutoSuggestBox and the menu items.

```csharp
NavigationViewElement PaneCustomContent(this NavigationViewElement el, Element content)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | [`Element`](../elements/Element.md) |  |

## `PaneDisplayMode`

```csharp
NavigationViewElement PaneDisplayMode(this NavigationViewElement el, NavigationViewPaneDisplayMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `NavigationViewPaneDisplayMode` |  |

## `PaneFooter`

Sets the element rendered at the bottom of the pane (below menu items).

```csharp
NavigationViewElement PaneFooter(this NavigationViewElement el, Element footer)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `footer` | [`Element`](../elements/Element.md) |  |

## `PaneHeader`

Sets the element rendered at the top of the pane, above the menu items.

```csharp
NavigationViewElement PaneHeader(this NavigationViewElement el, Element header)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `header` | [`Element`](../elements/Element.md) |  |

## `PaneTitle`

```csharp
NavigationViewElement PaneTitle(this NavigationViewElement el, string title)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `title` | `string` |  |

## `PaneToggleButtonVisible`

```csharp
NavigationViewElement PaneToggleButtonVisible(this NavigationViewElement el, bool visible)
TitleBarElement PaneToggleButtonVisible(this TitleBarElement el, bool visible)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `PaneToggleButtonVisible(NavigationViewElement, bool)` | Shows or hides NavigationView's built-in pane-toggle ("hamburger") button (`NavigationView.IsPaneToggleButtonVisible`). |
| `PaneToggleButtonVisible(TitleBarElement, bool)` | Shows or hides the pane toggle (hamburger) button on the title bar. |

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement), [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `visible` | `bool` |  |

## `PaneToggleRequested`

Wires the pane-toggle-requested handler. Passing `null` clears.

```csharp
TitleBarElement PaneToggleRequested(this TitleBarElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `PaneVisible`

Shows or hides the whole pane, leaving only the content area.

```csharp
NavigationViewElement PaneVisible(this NavigationViewElement el, bool visible = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `visible` | `bool` | `true` |  |

## `PreferredTheme`

Sets the theme of the hosting window's system caption buttons (`AppWindow.TitleBar.PreferredTheme`). WinUI does not derive it from the content's `RequestedTheme`, so match it here when the app themes its content opposite to the system. [`WindowSpec.TitleBarTheme`](../hosting/WindowSpec.md#titlebartheme), when set, wins over this. (issue #1297)

```csharp
TitleBarElement PreferredTheme(this TitleBarElement el, WindowTitleBarTheme theme)
```

Declared in `ElementExtensions`.

**Applies to:** [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `theme` | [`WindowTitleBarTheme`](../hosting/window-options.md#windowtitlebartheme) |  |

## `PreviousButtonVisibility`

When the previous button shows.

```csharp
PipsPagerElement PreviousButtonVisibility(this PipsPagerElement el, PipsPagerButtonVisibility visibility)
```

Declared in `ElementExtensions`.

**Applies to:** [`PipsPagerElement`](../elements/navigation.md#pipspagerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `visibility` | `PipsPagerButtonVisibility` |  |

## `RightHeader`

Sets the trailing-edge content slot (typically a profile / settings element).

```csharp
TitleBarElement RightHeader(this TitleBarElement el, Element rightHeader)
```

Declared in `ElementExtensions`.

**Applies to:** [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `rightHeader` | [`Element`](../elements/Element.md) |  |

## `SelectedPageIndexChanged`

Wires the selected-page-index-changed handler. Passing `null` clears.

```csharp
PipsPagerElement SelectedPageIndexChanged(this PipsPagerElement el, Action<int>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`PipsPagerElement`](../elements/navigation.md#pipspagerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<int>?` |  |

## `SelectedTagChanged`

Wires the selected-tag-changed handler. Passing `null` clears.

```csharp
NavigationViewElement SelectedTagChanged(this NavigationViewElement el, Action<string?>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<string?>?` |  |

## `SelectionFollowsFocus`

Sets whether moving keyboard focus between menu items also changes the selection.

```csharp
NavigationViewElement SelectionFollowsFocus(this NavigationViewElement el, bool follows = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `follows` | `bool` | `true` |  |

## `SettingsSelected`

Wires the settings-selected handler, raised when the built-in settings item becomes selected — by the user, or by setting `SelectedTag` to [`NavigationViewElement.SettingsTag`](../elements/navigation.md#navigationviewelement). Passing `null` clears.

```csharp
NavigationViewElement SettingsSelected(this NavigationViewElement el, Action? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `ShoulderNavigation`

Sets whether gamepad shoulder buttons cycle the selection.

```csharp
NavigationViewElement ShoulderNavigation(this NavigationViewElement el, NavigationViewShoulderNavigationEnabled mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `NavigationViewShoulderNavigationEnabled` |  |

## `ShowAddButton`

Controls visibility of the "add tab" button in a TabView.

```csharp
TabViewElement ShowAddButton(this TabViewElement el, bool visible = true)
```

Declared in `ElementExtensions`.

> **Obsolete.** Use IsAddTabButtonVisible() — aligns with WinUI naming (see #268).

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `visible` | `bool` | `true` |  |

## `Subtitle`

```csharp
TitleBarElement Subtitle(this TitleBarElement el, string subtitle)
```

Declared in `ElementExtensions`.

**Applies to:** [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `subtitle` | `string` |  |

## `TabCloseRequested`

Wires the tab-close-requested handler. Passing `null` clears.

```csharp
TabViewElement TabCloseRequested(this TabViewElement el, Action<int>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<int>?` |  |

## `TabDragCompleted`

Wires the tab-drag-completed handler. Receives (sourceTabIndex, wasOutside). Passing `null` clears.

```csharp
TabViewElement TabDragCompleted(this TabViewElement el, Action<int, bool>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<int, bool>?` |  |

## `TabDragStarting`

Wires the tab-drag-starting handler. Receives the source tab index. Passing `null` clears.

```csharp
TabViewElement TabDragStarting(this TabViewElement el, Action<int>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<int>?` |  |

## `TabStripFooter`

Sets the element rendered at the trailing edge of the tab strip.

```csharp
TabViewElement TabStripFooter(this TabViewElement el, Element footer)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `footer` | [`Element`](../elements/Element.md) |  |

## `TabStripHeader`

Sets the element rendered at the leading edge of the tab strip.

```csharp
TabViewElement TabStripHeader(this TabViewElement el, Element header)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `header` | [`Element`](../elements/Element.md) |  |

## `TabWidthMode`

Controls how tab widths are sized (Equal, SizeToContent, Compact).

```csharp
TabViewElement TabWidthMode(this TabViewElement el, TabViewWidthMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `TabViewWidthMode` |  |

## `Tall`

Shorthand for `.HeightOption(WindowTitleBarHeight.Tall)` — the standard layout when the title bar hosts navigation chrome (back button / pane toggle). Pass `false` to force the standard caption. (issue #917)

```csharp
TitleBarElement Tall(this TitleBarElement el, bool tall = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`TitleBarElement`](../elements/navigation.md#titlebarelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `tall` | `bool` | `true` |  |

## `TitleBarAutoPadding`

Sets whether NavigationView reserves top padding for the window's title bar.

```csharp
NavigationViewElement TitleBarAutoPadding(this NavigationViewElement el, bool enabled = true)
```

Declared in `ElementExtensions`.

**Applies to:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `enabled` | `bool` | `true` |  |

## `WithNavigation`

```csharp
TitleBarElement WithNavigation<TRoute>(this TitleBarElement el, NavigationHandle<TRoute> nav)
NavigationViewElement WithNavigation<TRoute>(this NavigationViewElement el, NavigationHandle<TRoute> nav, Func<TRoute, string?> routeToTag, Func<string, TRoute> tagToRoute, Func<TRoute>? settingsRoute = null)
```

Declared in `ElementExtensions`.

| Overload | Description |
| --- | --- |
| `WithNavigation<TRoute>(TitleBarElement, NavigationHandle<TRoute>)` | Auto-syncs this TitleBar's back button with a NavigationHandle: sets `IsBackButtonVisible` and `IsBackButtonEnabled` from `CanGoBack`, and wires `OnBackRequested` to `GoBack`. |
| `WithNavigation<TRoute>(NavigationViewElement, NavigationHandle<TRoute>, Func<TRoute, string?>, Func<string, TRoute>, Func<TRoute>?)` | Auto-syncs this NavigationView with a NavigationHandle: sets `SelectedTag` from the current route, navigates on selection change, wires `OnBackRequested` to `GoBack`, and `IsBackEnabled` to `CanGoBack`. NavigationView's recommended transition is forwarded to the navigation host, so pane navigation uses WinUI's entrance motion and top navigation slides horizontally according to the selected item's position.  Selection is wired through an internal transition-aware callback rather than the public [`NavigationViewElement.OnSelectedTagChanged`](../elements/navigation.md#navigationviewelement), which this deliberately leaves null: setting that property yourself afterwards then takes precedence and suppresses the auto-navigation, so an author can observe selection without this fighting them. The same applies to [`NavigationViewElement.OnSettingsSelected`](../elements/navigation.md#navigationviewelement).  The built-in settings item is opt-in: pass `settingsRoute` to make selecting it navigate. It is a separate parameter rather than a [`NavigationViewElement.SettingsTag`](../elements/navigation.md#navigationviewelement) lookup through `tagToRoute` because that delegate must return a route for every string it is handed — callers typically throw on an unrecognized tag — so there is no way for it to decline. Left null, settings selection does not navigate.  To show the settings item as selected while its route is current, return [`NavigationViewElement.SettingsTag`](../elements/navigation.md#navigationviewelement) from `routeToTag`. |

**Applies to:** [`TitleBarElement`](../elements/navigation.md#titlebarelement), [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `nav` | [`NavigationHandle<TRoute>`](../navigation/NavigationHandle.md) |  | The navigation handle obtained from `UseNavigation`. |
| `routeToTag` | `Func<TRoute, string?>` |  | Maps a route to its NavigationViewItem tag. Return null for routes without a corresponding menu item, or [`NavigationViewElement.SettingsTag`](../elements/navigation.md#navigationviewelement) for the built-in settings item. |
| `tagToRoute` | `Func<string, TRoute>` |  | Maps a NavigationViewItem tag back to a route for `OnSelectedTagChanged`. Never called with [`NavigationViewElement.SettingsTag`](../elements/navigation.md#navigationviewelement) — that value is reserved and routed via `settingsRoute` instead, so an ordinary item must not carry it or it will select without navigating. |
| `settingsRoute` | `Func<TRoute>?` | `null` | Route to navigate to when the built-in settings item is selected. Null (the default) leaves settings selection unrouted. |

## `WrapMode`

Whether the selected index wraps around the ends.

```csharp
PipsPagerElement WrapMode(this PipsPagerElement el, PipsPagerWrapMode mode)
```

Declared in `ElementExtensions`.

**Applies to:** [`PipsPagerElement`](../elements/navigation.md#pipspagerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `mode` | `PipsPagerWrapMode` |  |
