# Factories: Navigation

Navigation views, tabs, pivots, breadcrumbs and the title bar.

Each factory method returns an element record. Call them through `using static Microsoft.UI.Reactor.Factories;`.

**Learn more:** [Navigation](../../navigation.md)

## `Breadcrumb`

```csharp
BreadcrumbBarItemData Breadcrumb(string label, object? tag = null)
```

**Returns:** [`BreadcrumbBarItemData`](../controls/navigation-items.md#breadcrumbbaritemdata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `label` | `string` |  |  |
| `tag` | `object?` | `null` |  |

## `BreadcrumbBar`

```csharp
BreadcrumbBarElement BreadcrumbBar(BreadcrumbBarItemData[] items, Action<BreadcrumbBarItemData>? onItemClicked = null)
```

**Returns:** [`BreadcrumbBarElement`](../elements/navigation.md#breadcrumbbarelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | [`BreadcrumbBarItemData[]`](../controls/navigation-items.md#breadcrumbbaritemdata) |  |  |
| `onItemClicked` | `Action<BreadcrumbBarItemData>?` | `null` |  |

## `Frame`

A WinUI `Frame` that navigates to `sourcePageType` once on mount.

`Frame` is for interop with pages that already have a `.xaml` file. A `Page` declared only in C# is absent from the XAML metadata the compiler generates, so WinUI cannot resolve it; Reactor refuses such a navigation and reports it through `.NavigationFailed(...)` rather than letting it fault. For navigation inside a Reactor app use `UseNavigation<TRoute>` with `NavigationHost`.

```csharp
FrameElement Frame(Type? sourcePageType = null, object? navigationParameter = null)
```

**Returns:** [`FrameElement`](../elements/navigation.md#frameelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `sourcePageType` | `Type?` | `null` |  |
| `navigationParameter` | `object?` | `null` |  |

## `NavItem`

```csharp
NavigationViewItemData NavItem(string content, string? icon = null, string? tag = null)
```

**Returns:** [`NavigationViewItemData`](../controls/navigation-items.md#navigationviewitemdata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | `string` |  |  |
| `icon` | `string?` | `null` |  |
| `tag` | `string?` | `null` |  |

## `NavItemHeader`

```csharp
NavigationViewItemData NavItemHeader(string content)
```

**Returns:** [`NavigationViewItemData`](../controls/navigation-items.md#navigationviewitemdata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | `string` |  |

## `NavigationHost`

Creates a navigation host that renders the current route's content. Automatically provides the navigation handle via context so child components can retrieve it with `UseNavigation<TRoute>()`. Use `with { }` to set Transition, CacheMode, and CacheSize.

```csharp
NavigationHostElement NavigationHost<TRoute>(NavigationHandle<TRoute> nav, Func<TRoute, Element> routeMap)
```

**Returns:** [`NavigationHostElement`](../elements/navigation.md#navigationhostelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `nav` | [`NavigationHandle<TRoute>`](../navigation/NavigationHandle.md) |  |
| `routeMap` | `Func<TRoute, Element>` |  |

## `NavigationView`

```csharp
NavigationViewElement NavigationView(NavigationViewItemData[] menuItems, Element? content = null)
```

**Returns:** [`NavigationViewElement`](../elements/navigation.md#navigationviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `menuItems` | [`NavigationViewItemData[]`](../controls/navigation-items.md#navigationviewitemdata) |  |  |
| `content` | [`Element?`](../elements/Element.md) | `null` |  |

## `PipsPager`

Creates a [`PipsPagerElement`](../elements/navigation.md#pipspagerelement). `selectedPageIndex` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its current page.

```csharp
PipsPagerElement PipsPager(int numberOfPages, Optional<int> selectedPageIndex = default, Action<int>? onSelectedPageIndexChanged = null)
```

**Returns:** [`PipsPagerElement`](../elements/navigation.md#pipspagerelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `numberOfPages` | `int` |  |  |
| `selectedPageIndex` | [`Optional<int>`](../components/Optional.md) | `default` |  |
| `onSelectedPageIndexChanged` | `Action<int>?` | `null` |  |

## `Pivot`

Creates a pivot whose selected index is controlled only when `selectedIndex` has a value.

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
| `Pivot(int, Action<int>?, PivotItemData[])` | Creates a pivot with a controlled selected index. Disambiguates a plain [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) argument, which converts equally well to [`Nullable`](https://learn.microsoft.com/dotnet/api/system.nullable-1) and [`Optional<T>`](../components/Optional.md). Any [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) — including `default`, which is `0` — selects that index; pass `(int?)null` or [`Optional.Unset`](../components/Optional.md#unset) to leave the selection uncontrolled. |
| `Pivot(int?, Action<int>?, PivotItemData[])` | Creates a pivot whose selected index is unset when `selectedIndex` is `null`. |

**Returns:** [`PivotElement`](../elements/navigation.md#pivotelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | [`PivotItemData[]`](../controls/navigation-items.md#pivotitemdata) |  |  |
| `selectedIndex` | [`Optional<int>`](../components/Optional.md) or `int` or `int?` |  |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |

## `PivotItem`

```csharp
PivotItemData PivotItem(string header, Element content)
```

**Returns:** [`PivotItemData`](../controls/navigation-items.md#pivotitemdata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `header` | `string` |  |
| `content` | [`Element`](../elements/Element.md) |  |

## `SelectorBar`

Creates a [`SelectorBarElement`](../elements/navigation.md#selectorbarelement). `selectedIndex` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it to let the control own its selection.

```csharp
SelectorBarElement SelectorBar(SelectorBarItemData[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
```

**Returns:** [`SelectorBarElement`](../elements/navigation.md#selectorbarelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | [`SelectorBarItemData[]`](../controls/navigation-items.md#selectorbaritemdata) |  |  |
| `selectedIndex` | [`Optional<int>`](../components/Optional.md) | `default` |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |

## `SelectorBarItem`

```csharp
SelectorBarItemData SelectorBarItem(string text, string? icon = null)
```

**Returns:** [`SelectorBarItemData`](../controls/navigation-items.md#selectorbaritemdata).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `text` | `string` |  |  |
| `icon` | `string?` | `null` |  |

## `Tab`

```csharp
TabViewItemData Tab(string header, Element content)
```

**Returns:** [`TabViewItemData`](../controls/navigation-items.md#tabviewitemdata).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `header` | `string` |  |
| `content` | [`Element`](../elements/Element.md) |  |

## `TabView`

Creates a tab view whose selected index is controlled only when `selectedIndex` has a value.

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
| `TabView(int, Action<int>?, TabViewItemData[])` | Creates a tab view with a controlled selected index. Disambiguates a plain [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) argument, which converts equally well to [`Nullable`](https://learn.microsoft.com/dotnet/api/system.nullable-1) and [`Optional<T>`](../components/Optional.md). Any [`Int32`](https://learn.microsoft.com/dotnet/api/system.int32) — including `default`, which is `0` — selects that index; pass `(int?)null` or [`Optional.Unset`](../components/Optional.md#unset) to leave the selection uncontrolled. |
| `TabView(int?, Action<int>?, TabViewItemData[])` | Creates a tab view whose selected index is unset when `selectedIndex` is `null`. |

**Returns:** [`TabViewElement`](../elements/navigation.md#tabviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `tabs` | [`TabViewItemData[]`](../controls/navigation-items.md#tabviewitemdata) |  |  |
| `selectedIndex` | [`Optional<int>`](../components/Optional.md) or `int` or `int?` |  |  |
| `onSelectedIndexChanged` | `Action<int>?` | `null` |  |
