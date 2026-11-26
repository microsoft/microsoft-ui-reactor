---
title: "Factories: Layout"
description: "Panels, stacks, grids, scrolling and other containers."
sidebar:
  label: "Layout"
---

Each factory method returns an element record. Most are on `Factories`, so `using static Microsoft.UI.Reactor.Factories;` covers them. A factory declared on another class names the `using static` it needs.

**Learn more:** [Layout](/microsoft-ui-reactor/main/guides/layout), [Flex Layout](/microsoft-ui-reactor/main/guides/flex-layout)

## `AnnotatedScrollBar` [#annotatedscrollbar]

```csharp
AnnotatedScrollBarElement AnnotatedScrollBar()
```

**Returns:** [`AnnotatedScrollBarElement`](/microsoft-ui-reactor/main/reference/elements/layout#annotatedscrollbarelement).

## `Border` [#border]

```csharp
BorderElement Border(Element? child)
```

**Returns:** [`BorderElement`](/microsoft-ui-reactor/main/reference/elements/layout#borderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `Canvas` [#canvas]

```csharp
CanvasElement Canvas(params Element?[] children)
```

**Returns:** [`CanvasElement`](/microsoft-ui-reactor/main/reference/elements/layout#canvaselement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `Card` [#card]

Renders `child` inside a card-styled [`BorderElement`](/microsoft-ui-reactor/main/reference/elements/layout#borderelement). Defaults match the canonical WinUI card: 8px corner radius, ~16px padding, theme-aware `CardBackgroundFillColorDefaultBrush` + `CardStrokeColorDefaultBrush` 1px stroke. Re-themes on light / dark / contrast switches because the brushes resolve through [`ThemeRef`](/microsoft-ui-reactor/main/reference/theming/ThemeRef).

```csharp
BorderElement Card(Element child)
```

**Returns:** [`BorderElement`](/microsoft-ui-reactor/main/reference/elements/layout#borderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

**Remarks**

Override any preset by chaining a fluent on the returned border: `Card(child).Padding(24).CornerRadius(16).Background(Theme.SubtleFill)`.

## `Expander` [#expander]

Creates an [`ExpanderElement`](/microsoft-ui-reactor/main/reference/elements/layout#expanderelement). `isExpanded` defaults to [`Optional.Unset`](/microsoft-ui-reactor/main/reference/components/Optional#unset) — omit it and the WinUI control owns its expansion state (uncontrolled — user clicks persist across rerenders). Pass `true`/`false` (implicit `T → Optional<T>`) to control it from a parent state.

```csharp
ExpanderElement Expander(string header, Element content, Optional<bool> isExpanded = default, Action<bool>? onIsExpandedChanged = null)
```

**Returns:** [`ExpanderElement`](/microsoft-ui-reactor/main/reference/elements/layout#expanderelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `header` | `string` |  |  |
| `content` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `isExpanded` | [`Optional<bool>`](/microsoft-ui-reactor/main/reference/components/Optional) | `default` |  |
| `onIsExpandedChanged` | `Action<bool>?` | `null` |  |

## `Flex` [#flex]

```csharp
FlexElement Flex(params Element?[] children)
FlexElement Flex(FlexDirection direction, params Element?[] children)
```

| Overload | Description |
| --- | --- |
| `Flex(Element?[])` | Creates a Yoga-based flexbox container ([`FlexElement`](/microsoft-ui-reactor/main/reference/elements/layout#flexelement)). Default direction is [`FlexDirection.Row`](/microsoft-ui-reactor/main/reference/layout/flex-enums#values_1). |
| `Flex(FlexDirection, Element?[])` | Creates a Yoga flexbox container with an explicit direction. |

**Returns:** [`FlexElement`](/microsoft-ui-reactor/main/reference/elements/layout#flexelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `direction` | [`FlexDirection`](/microsoft-ui-reactor/main/reference/layout/flex-enums#flexdirection) |  |

**Remarks**

Reactor-original — there is no WinUI `Flex` control. This is a custom panel backed by Yoga (Facebook's flexbox engine, see [`FlexPanel`](/microsoft-ui-reactor/main/reference/layout/flex-enums#flexpanel)) for full CSS-flexbox semantics inside a WinUI tree. Prefer [`VStack`](#vstack) / [`HStack`](#hstack) for simple stacks; reach for Flex when you need wrap, justify-content / align-items, or per-child grow/shrink. (spec 039 §0.3)

Reactor-original — see [`Flex`](#flex) for the rationale.

## `FlexColumn` [#flexcolumn]

Creates a Yoga flexbox container with [`FlexDirection.Column`](/microsoft-ui-reactor/main/reference/layout/flex-enums#values_1).

```csharp
FlexElement FlexColumn(params Element?[] children)
```

**Returns:** [`FlexElement`](/microsoft-ui-reactor/main/reference/elements/layout#flexelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

**Remarks**

Reactor-original convenience for the column-direction flex case — see [`Flex`](#flex) for the rationale. (spec 039 §0.3)

## `FlexRow` [#flexrow]

Creates a Yoga flexbox container with [`FlexDirection.Row`](/microsoft-ui-reactor/main/reference/layout/flex-enums#values_1).

```csharp
FlexElement FlexRow(params Element?[] children)
```

**Returns:** [`FlexElement`](/microsoft-ui-reactor/main/reference/elements/layout#flexelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

**Remarks**

Reactor-original convenience for the row-direction flex case — see [`Flex`](#flex) for the rationale. (spec 039 §0.3)

## `Grid` [#grid]

Creates a [`GridElement`](/microsoft-ui-reactor/main/reference/elements/layout#gridelement) with strongly-typed track sizes.

```csharp
GridElement Grid(GridSize[] columns, GridSize[] rows, params Element?[] children)
```

**Returns:** [`GridElement`](/microsoft-ui-reactor/main/reference/elements/layout#gridelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columns` | [`GridSize[]`](/microsoft-ui-reactor/main/reference/layout/GridSize) |  |
| `rows` | [`GridSize[]`](/microsoft-ui-reactor/main/reference/layout/GridSize) |  |
| `children` | [`Element?[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

**Remarks**

Spec 033 §1. Use [`GridSize.Auto`](/microsoft-ui-reactor/main/reference/layout/GridSize#auto) / [`GridSize.Star`](/microsoft-ui-reactor/main/reference/layout/GridSize#star) / [`GridSize.Px`](/microsoft-ui-reactor/main/reference/layout/GridSize#px) instead of the legacy string-form (`"Auto"`/`"*"`/`"200"`) for compile-time validation and IntelliSense over the legal track shapes.

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) | Thrown when `columns` or `rows` is null. |

## `HStack` [#hstack]

```csharp
StackElement HStack(params Element?[] children)
StackElement HStack(double spacing, params Element?[] children)
```

| Overload | Description |
| --- | --- |
| `HStack(Element?[])` | Creates a horizontal [`StackElement`](/microsoft-ui-reactor/main/reference/elements/layout#stackelement) (WinUI `StackPanel` with [`Orientation.Horizontal`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.orientation.horizontal)). Default `Spacing` is 8 — see [`StackElement.Spacing`](/microsoft-ui-reactor/main/reference/elements/layout#stackelement). |
| `HStack(double, Element?[])` | Creates a horizontal [`StackElement`](/microsoft-ui-reactor/main/reference/elements/layout#stackelement) with an explicit `Spacing` override (the first positional argument). |

**Returns:** [`StackElement`](/microsoft-ui-reactor/main/reference/elements/layout#stackelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `spacing` | `double` |  |

**Remarks**

Reactor-original name — see [`VStack`](#vstack) for the naming rationale. (spec 039 §0.3)

Reactor-original convenience overload — see [`VStack`](#vstack) for the naming rationale.

## `InterspersedGrid` [#interspersedgrid]

Creates a grid with items interspersed with separator elements along one axis. Commonly used for split panels where children are separated by splitters.

Each item gets a proportional (\*) size from `proportions`, and separators get a fixed pixel size of `separatorSize`.

Example: InterspersedGrid(Orientation.Horizontal, children, proportions, 6, i =\> MySplitter(i)) produces columns: "0.33\*", "6", "0.33\*", "6", "0.34\*" with children and splitters placed.

```csharp
GridElement InterspersedGrid(Orientation orientation, Element[] items, double[] proportions, double separatorSize, Func<int, Element> separatorFactory)
```

**Returns:** [`GridElement`](/microsoft-ui-reactor/main/reference/elements/layout#gridelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `orientation` | `Orientation` |  |
| `items` | [`Element[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `proportions` | `double[]` |  |
| `separatorSize` | `double` |  |
| `separatorFactory` | `Func<int, Element>` |  |

## `ParallaxView` [#parallaxview]

```csharp
ParallaxViewElement ParallaxView(Element child, double verticalShift = 0, double horizontalShift = 0)
```

**Returns:** [`ParallaxViewElement`](/microsoft-ui-reactor/main/reference/elements/layout#parallaxviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `child` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |  |
| `verticalShift` | `double` | `0` |  |
| `horizontalShift` | `double` | `0` |  |

## `RelativePanel` [#relativepanel]

```csharp
RelativePanelElement RelativePanel(params Element?[] children)
```

**Returns:** [`RelativePanelElement`](/microsoft-ui-reactor/main/reference/elements/layout#relativepanelelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `ScrollView` [#scrollview]

Creates a [`ScrollViewElement`](/microsoft-ui-reactor/main/reference/elements/layout#scrollviewelement) wrapping `child` in the modern [`ScrollView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollview) (InteractionTracker-backed, derives from `FrameworkElement`).

```csharp
ScrollViewElement ScrollView(Element child)
```

**Returns:** [`ScrollViewElement`](/microsoft-ui-reactor/main/reference/elements/layout#scrollviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

**Remarks**

Exposes capabilities the legacy `ScrollViewer` lacks — `ContentOrientation`, `HorizontalAnchorRatio` / `VerticalAnchorRatio`, and the `Scrolling*` enum surface. For the classic control, use [`ScrollViewer`](#scrollviewer). Issue #348.

## `ScrollViewer` [#scrollviewer]

Creates a [`ScrollViewerElement`](/microsoft-ui-reactor/main/reference/elements/layout#scrollviewerelement) wrapping `child` in the classic [`ScrollViewer`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollviewer) container (derives from `Control`; pan + zoom).

```csharp
ScrollViewerElement ScrollViewer(Element child)
```

**Returns:** [`ScrollViewerElement`](/microsoft-ui-reactor/main/reference/elements/layout#scrollviewerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

**Remarks**

For the newer InteractionTracker-backed [`ScrollView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollview) (different enum surface, different events, additive features like `ContentOrientation` and anchor ratios), use [`ScrollView`](#scrollview). Issue #348.

## `SplitView` [#splitview]

```csharp
SplitViewElement SplitView(Element? pane = null, Element? content = null)
```

**Returns:** [`SplitViewElement`](/microsoft-ui-reactor/main/reference/elements/layout#splitviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `pane` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |
| `content` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |

## `Thick` [#thick]

```csharp
Thickness Thick(double uniform)
Thickness Thick(double horizontal, double vertical)
Thickness Thick(double left, double top, double right, double bottom)
```

| Overload | Description |
| --- | --- |
| `Thick(double)` | Creates a uniform Thickness. Shorthand for new Thickness(uniform). |
| `Thick(double, double)` | Creates a Thickness with horizontal and vertical values. Usage: Thick(16, 8) → Thickness(16, 8, 16, 8) |
| `Thick(double, double, double, double)` | Creates a Thickness with all four sides specified. |

**Returns:** [`Thickness`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.thickness).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `uniform` | `double` |  |
| `horizontal` | `double` |  |
| `vertical` | `double` |  |
| `left` | `double` |  |
| `top` | `double` |  |
| `right` | `double` |  |
| `bottom` | `double` |  |

## `UniformGrid` [#uniformgrid]

Creates a uniform grid with equal-sized cells along one axis. Shorthand for a grid where all items share equal proportions with no separators.

```csharp
GridElement UniformGrid(Orientation orientation, params Element?[] items)
```

**Returns:** [`GridElement`](/microsoft-ui-reactor/main/reference/elements/layout#gridelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `orientation` | `Orientation` |  |
| `items` | [`Element?[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `VStack` [#vstack]

```csharp
StackElement VStack(params Element?[] children)
StackElement VStack(double spacing, params Element?[] children)
```

| Overload | Description |
| --- | --- |
| `VStack(Element?[])` | Creates a vertical [`StackElement`](/microsoft-ui-reactor/main/reference/elements/layout#stackelement) (WinUI `StackPanel` with [`Orientation.Vertical`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.orientation.vertical)). Default `Spacing` is 8 — see [`StackElement.Spacing`](/microsoft-ui-reactor/main/reference/elements/layout#stackelement). |
| `VStack(double, Element?[])` | Creates a vertical [`StackElement`](/microsoft-ui-reactor/main/reference/elements/layout#stackelement) with an explicit `Spacing` override (the first positional argument). |

**Returns:** [`StackElement`](/microsoft-ui-reactor/main/reference/elements/layout#stackelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `spacing` | `double` |  |

**Remarks**

Reactor-original name — WinUI exposes one `StackPanel` control keyed by `Orientation`; Reactor splits it into two factories ([`VStack`](#vstack) / [`HStack`](#hstack)) because the orientation is almost always known at the call site and the shorter names reduce DSL noise. The SwiftUI / React Native names are load-bearing for cross-platform agent familiarity. (spec 039 §0.3)

Reactor-original convenience overload — see [`VStack`](#vstack) for the naming rationale.

## `Viewbox` [#viewbox]

```csharp
ViewboxElement Viewbox(Element child)
```

**Returns:** [`ViewboxElement`](/microsoft-ui-reactor/main/reference/elements/layout#viewboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

## `WrapGrid` [#wrapgrid]

```csharp
WrapGridElement WrapGrid(params Element?[] children)
WrapGridElement WrapGrid(int maxRowsOrColumns, params Element?[] children)
```

**Returns:** [`WrapGridElement`](/microsoft-ui-reactor/main/reference/elements/layout#wrapgridelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |
| `maxRowsOrColumns` | `int` |  |
