---
title: "Factories: Navigation"
description: "Navigation views, tabs, pivots, breadcrumbs and the title bar."
sidebar:
  label: "Navigation"
---

Each factory method returns an element record. Most are on `Factories`, so `using static Microsoft.UI.Reactor.Factories;` covers them. A factory declared on another class names the `using static` it needs.

**Learn more:** [Navigation](/microsoft-ui-reactor/main/guides/navigation)

## `Breadcrumb` [#breadcrumb]

```csharp
BreadcrumbBarItemData Breadcrumb(string label, object? tag = null)
```

**Returns:** [`BreadcrumbBarItemData`](/microsoft-ui-reactor/main/reference/controls/navigation-items#breadcrumbbaritemdata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `label` | `string` |  |  |
| `tag` | `object?` | `null` |  |

## `BreadcrumbBar` [#breadcrumbbar]

```csharp
BreadcrumbBarElement BreadcrumbBar(BreadcrumbBarItemData[] items, Action<BreadcrumbBarItemData>? onItemClicked = null)
```

**Returns:** [`BreadcrumbBarElement`](/microsoft-ui-reactor/main/reference/elements/navigation#breadcrumbbarelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | [`BreadcrumbBarItemData[]`](/microsoft-ui-reactor/main/reference/controls/navigation-items#breadcrumbbaritemdata) |  |  |
| `onItemClicked` | `Action<BreadcrumbBarItemData>?` | `null` |  |

## `Frame` [#frame]

A WinUI `Frame` that navigates to `sourcePageType` once on mount.

`Frame` is for interop with pages that already have a `.xaml` file. A `Page` declared only in C# is absent from the XAML metadata the compiler generates, so WinUI cannot resolve it; Reactor refuses such a navigation and reports it through `.NavigationFailed(...)` rather than letting it fault. For navigation inside a Reactor app use `UseNavigation<TRoute>` with `NavigationHost`.

```csharp
FrameElement Frame(Type? sourcePageType = null, object? navigationParameter = null)
```

**Returns:** [`FrameElement`](/microsoft-ui-reactor/main/reference/elements/navigation#frameelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `sourcePageType` | `Type?` | `null` |  |
| `navigationParameter` | `object?` | `null` |  |

## `NavItem` [#navitem]

```csharp
NavigationViewItemData NavItem(string content, string? icon = null, string? tag = null)
```

**Returns:** [`NavigationViewItemData`](/microsoft-ui-reactor/main/reference/controls/navigation-items#navigationviewitemdata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | `string` |  |  |
| `icon` | `string?` | `null` |  |
| `tag` | `string?` | `null` |  |

## `NavItemHeader` [#navitemheader]

```csharp
NavigationViewItemData NavItemHeader(string content)
```

**Returns:** [`NavigationViewItemData`](/microsoft-ui-reactor/main/reference/controls/navigation-items#navigationviewitemdata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |

## `NavigationHost` [#navigationhost]

Creates a navigation host that renders the current route's content. Automatically provides the navigation handle via context so child components can retrieve it with `UseNavigation<TRoute>()`. Use `with { }` to set Transition, CacheMode, and CacheSize.

```csharp
NavigationHostElement NavigationHost<TRoute>(NavigationHandle<TRoute> nav, Func<TRoute, Element> routeMap) where TRoute : notnull
```

**Returns:** [`NavigationHostElement`](/microsoft-ui-reactor/main/reference/elements/navigation#navigationhostelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `nav` | [`NavigationHandle<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/NavigationHandle) |  |
| `routeMap` | `Func<TRoute, Element>` |  |

## `NavigationView` [#navigationview]

```csharp
NavigationViewElement NavigationView(NavigationViewItemData[] menuItems, Element? content = null)
```

**Returns:** [`NavigationViewElement`](/microsoft-ui-reactor/main/reference/elements/navigation#navigationviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `menuItems` | [`NavigationViewItemData[]`](/microsoft-ui-reactor/main/reference/controls/navigation-items#navigationviewitemdata) |  |  |
| `content` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |

## `PipsPager` [#pipspager]

Creates a [`PipsPagerElement`](/microsoft-ui-reactor/main/reference/elements/navigation#pipspagerelement). `selectedPageIndex` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its current page.

```csharp
PipsPagerElement PipsPager(int numberOfPages, Optional<int> selectedPageIndex = default, Action<int>? onSelectedPageIndexChanged = null)
```

**Returns:** [`PipsPagerElement`](/microsoft-ui-reactor/main/reference/elements/navigation#pipspagerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `numberOfPages` | `int` |  |  |
| `selectedPageIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onSelectedPageIndexChanged` | `Action<int>?` | `null` |  |

## `Pivot` [#pivot]

```csharp
PivotElement Pivot(params PivotItemData[] items)
PivotElement Pivot(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params PivotItemData[] items)
PivotElement Pivot(int selectedIndex, Action<int>? onSelectedIndexChanged = null, params PivotItemData[] items)
PivotElement Pivot(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params PivotItemData[] items)
```

| Overload | Description |
| --- | --- |
| `Pivot(PivotItemData[])` |  |
| `Pivot(Optional<int>, Action<int>?, PivotItemData[])` | Creates a pivot whose selected index is controlled only when `selectedIndex` has a value. |
| `Pivot(int, Action<int>?, PivotItemData[])` | Creates a pivot with a controlled selected index. Disambiguates a plain [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) argument, which converts equally well to [`Nullable`](https://learn.microsoft.com/dotnet/api/system.nullable-1) and [`Optional<T>`](/microsoft-ui-reactor/main/reference/components/Optional). Any [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) — including `default`, which is `0` — selects that index; pass `(int?)null` or [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) to leave the selection uncontrolled. |
| `Pivot(int?, Action<int>?, PivotItemData[])` | Creates a pivot whose selected index is unset when `selectedIndex` is `null`. |

**Returns:** [`PivotElement`](/microsoft-ui-reactor/main/reference/elements/navigation#pivotelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | [`PivotItemData[]`](/microsoft-ui-reactor/main/reference/controls/navigation-items#pivotitemdata) |  |  |
| `selectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) or `int` or `int?` |  |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |

## `PivotItem` [#pivotitem]

```csharp
PivotItemData PivotItem(string header, Element content)
```

**Returns:** [`PivotItemData`](/microsoft-ui-reactor/main/reference/controls/navigation-items#pivotitemdata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `header` | `string` |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `SelectorBar` [#selectorbar]

Creates a [`SelectorBarElement`](/microsoft-ui-reactor/main/reference/elements/navigation#selectorbarelement). `selectedIndex` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it to let the control own its selection.

```csharp
SelectorBarElement SelectorBar(SelectorBarItemData[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
```

**Returns:** [`SelectorBarElement`](/microsoft-ui-reactor/main/reference/elements/navigation#selectorbarelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | [`SelectorBarItemData[]`](/microsoft-ui-reactor/main/reference/controls/navigation-items#selectorbaritemdata) |  |  |
| `selectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |

## `SelectorBarItem` [#selectorbaritem]

```csharp
SelectorBarItemData SelectorBarItem(string text, string? icon = null)
```

**Returns:** [`SelectorBarItemData`](/microsoft-ui-reactor/main/reference/controls/navigation-items#selectorbaritemdata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `text` | `string` |  |  |
| `icon` | `string?` | `null` |  |

## `Tab` [#tab]

```csharp
TabViewItemData Tab(string header, Element content)
```

**Returns:** [`TabViewItemData`](/microsoft-ui-reactor/main/reference/controls/navigation-items#tabviewitemdata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `header` | `string` |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `TabView` [#tabview]

```csharp
TabViewElement TabView(params TabViewItemData[] tabs)
TabViewElement TabView(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params TabViewItemData[] tabs)
TabViewElement TabView(int selectedIndex, Action<int>? onSelectedIndexChanged = null, params TabViewItemData[] tabs)
TabViewElement TabView(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params TabViewItemData[] tabs)
```

| Overload | Description |
| --- | --- |
| `TabView(TabViewItemData[])` |  |
| `TabView(Optional<int>, Action<int>?, TabViewItemData[])` | Creates a tab view whose selected index is controlled only when `selectedIndex` has a value. |
| `TabView(int, Action<int>?, TabViewItemData[])` | Creates a tab view with a controlled selected index. Disambiguates a plain [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) argument, which converts equally well to [`Nullable`](https://learn.microsoft.com/dotnet/api/system.nullable-1) and [`Optional<T>`](/microsoft-ui-reactor/main/reference/components/Optional). Any [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) — including `default`, which is `0` — selects that index; pass `(int?)null` or [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) to leave the selection uncontrolled. |
| `TabView(int?, Action<int>?, TabViewItemData[])` | Creates a tab view whose selected index is unset when `selectedIndex` is `null`. |

**Returns:** [`TabViewElement`](/microsoft-ui-reactor/main/reference/elements/navigation#tabviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `tabs` | [`TabViewItemData[]`](/microsoft-ui-reactor/main/reference/controls/navigation-items#tabviewitemdata) |  |  |
| `selectedIndex` | [`Optional<int>`](/microsoft-ui-reactor/main/reference/components/Optional) or `int` or `int?` |  |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |
