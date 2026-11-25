# Factories: Layout

Panels, stacks, grids, scrolling and other containers.

Each factory method returns an element record. Call them through `using static Microsoft.UI.Reactor.Factories;`.

**Learn more:** [Layout](../../layout.md), [Flex Layout](../../flex-layout.md)

## `AnnotatedScrollBar`

```csharp
AnnotatedScrollBarElement AnnotatedScrollBar()
```

**Returns:** [`AnnotatedScrollBarElement`](../elements/layout.md#annotatedscrollbarelement).

## `Border`

```csharp
BorderElement Border(Element? child)
```

**Returns:** [`BorderElement`](../elements/layout.md#borderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element?`](../elements/Element.md) |  |

## `Canvas`

```csharp
CanvasElement Canvas(params Element?[] children)
```

**Returns:** [`CanvasElement`](../elements/layout.md#canvaselement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](../elements/Element.md) |  |

## `Card`

Renders `child` inside a card-styled [`BorderElement`](../elements/layout.md#borderelement). Defaults match the canonical WinUI card: 8px corner radius, ~16px padding, theme-aware `CardBackgroundFillColorDefaultBrush` + `CardStrokeColorDefaultBrush` 1px stroke. Re-themes on light / dark / contrast switches because the brushes resolve through [`ThemeRef`](../theming/ThemeRef.md).

```csharp
BorderElement Card(Element child)
```

**Returns:** [`BorderElement`](../elements/layout.md#borderelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element`](../elements/Element.md) |  |

**Remarks**

Override any preset by chaining a fluent on the returned border: `Card(child).Padding(24).CornerRadius(16).Background(Theme.SubtleFill)`.

## `Expander`

Creates an [`ExpanderElement`](../elements/layout.md#expanderelement). `isExpanded` defaults to [`Optional.Unset`](../components/Optional.md#unset) — omit it and the WinUI control owns its expansion state (uncontrolled — user clicks persist across rerenders). Pass `true`/`false` (implicit `T → Optional<T>`) to control it from a parent state.

```csharp
ExpanderElement Expander(string header, Element content, Optional<bool> isExpanded = default, Action<bool>? onIsExpandedChanged = null)
```

**Returns:** [`ExpanderElement`](../elements/layout.md#expanderelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `header` | `string` |  |  |
| `content` | [`Element`](../elements/Element.md) |  |  |
| `isExpanded` | [`Optional<bool>`](../components/Optional.md) | `default` |  |
| `onIsExpandedChanged` | `Action<bool>?` | `null` |  |

## `Flex`

```csharp
FlexElement Flex(params Element?[] children)
FlexElement Flex(FlexDirection direction, params Element?[] children)
```

| Overload | Description |
| --- | --- |
| `Flex(Element?[])` | Creates a Yoga-based flexbox container ([`FlexElement`](../elements/layout.md#flexelement)). Default direction is [`FlexDirection.Row`](../layout/flex-enums.md#values_1). |
| `Flex(FlexDirection, Element?[])` | Creates a Yoga flexbox container with an explicit direction. |

**Returns:** [`FlexElement`](../elements/layout.md#flexelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](../elements/Element.md) |  |
| `direction` | [`FlexDirection`](../layout/flex-enums.md#flexdirection) |  |

**Remarks**

Reactor-original — there is no WinUI `Flex` control. This is a custom panel backed by Yoga (Facebook's flexbox engine, see [`FlexPanel`](../layout/flex-enums.md#flexpanel)) for full CSS-flexbox semantics inside a WinUI tree. Prefer [`VStack`](#vstack) / [`HStack`](#hstack) for simple stacks; reach for Flex when you need wrap, justify-content / align-items, or per-child grow/shrink. (spec 039 §0.3)

Reactor-original — see [`Flex`](#flex) for the rationale.

## `FlexColumn`

Creates a Yoga flexbox container with [`FlexDirection.Column`](../layout/flex-enums.md#values_1).

```csharp
FlexElement FlexColumn(params Element?[] children)
```

**Returns:** [`FlexElement`](../elements/layout.md#flexelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](../elements/Element.md) |  |

**Remarks**

Reactor-original convenience for the column-direction flex case — see [`Flex`](#flex) for the rationale. (spec 039 §0.3)

## `FlexRow`

Creates a Yoga flexbox container with [`FlexDirection.Row`](../layout/flex-enums.md#values_1).

```csharp
FlexElement FlexRow(params Element?[] children)
```

**Returns:** [`FlexElement`](../elements/layout.md#flexelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](../elements/Element.md) |  |

**Remarks**

Reactor-original convenience for the row-direction flex case — see [`Flex`](#flex) for the rationale. (spec 039 §0.3)

## `Grid`

Creates a [`GridElement`](../elements/layout.md#gridelement) with strongly-typed track sizes.

```csharp
GridElement Grid(GridSize[] columns, GridSize[] rows, params Element?[] children)
```

**Returns:** [`GridElement`](../elements/layout.md#gridelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `columns` | [`GridSize[]`](../layout/GridSize.md) |  |
| `rows` | [`GridSize[]`](../layout/GridSize.md) |  |
| `children` | [`Element?[]`](../elements/Element.md) |  |

**Remarks**

Spec 033 §1. Use [`GridSize.Auto`](../layout/GridSize.md#auto) / [`GridSize.Star`](../layout/GridSize.md#star) / [`GridSize.Px`](../layout/GridSize.md#px) instead of the legacy string-form (`"Auto"`/`"*"`/`"200"`) for compile-time validation and IntelliSense over the legal track shapes.

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) | Thrown when `columns` or `rows` is null. |

## `HStack`

```csharp
StackElement HStack(params Element?[] children)
StackElement HStack(double spacing, params Element?[] children)
```

| Overload | Description |
| --- | --- |
| `HStack(Element?[])` | Creates a horizontal [`StackElement`](../elements/layout.md#stackelement) (WinUI `StackPanel` with [`Orientation.Horizontal`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.orientation.horizontal)). Default `Spacing` is 8 — see [`StackElement.Spacing`](../elements/layout.md#stackelement). |
| `HStack(double, Element?[])` | Creates a horizontal [`StackElement`](../elements/layout.md#stackelement) with an explicit `Spacing` override (the first positional argument). |

**Returns:** [`StackElement`](../elements/layout.md#stackelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](../elements/Element.md) |  |
| `spacing` | `double` |  |

**Remarks**

Reactor-original name — see [`VStack`](#vstack) for the naming rationale. (spec 039 §0.3)

Reactor-original convenience overload — see [`VStack`](#vstack) for the naming rationale.

## `InterspersedGrid`

Creates a grid with items interspersed with separator elements along one axis. Commonly used for split panels where children are separated by splitters.

Each item gets a proportional (\*) size from `proportions`, and separators get a fixed pixel size of `separatorSize`.

Example: InterspersedGrid(Orientation.Horizontal, children, proportions, 6, i =\> MySplitter(i)) produces columns: "0.33\*", "6", "0.33\*", "6", "0.34\*" with children and splitters placed.

```csharp
GridElement InterspersedGrid(Orientation orientation, Element[] items, double[] proportions, double separatorSize, Func<int, Element> separatorFactory)
```

**Returns:** [`GridElement`](../elements/layout.md#gridelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `orientation` | `Orientation` |  |
| `items` | [`Element[]`](../elements/Element.md) |  |
| `proportions` | `double[]` |  |
| `separatorSize` | `double` |  |
| `separatorFactory` | `Func<int, Element>` |  |

## `ParallaxView`

```csharp
ParallaxViewElement ParallaxView(Element child, double verticalShift = 0, double horizontalShift = 0)
```

**Returns:** [`ParallaxViewElement`](../elements/layout.md#parallaxviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `child` | [`Element`](../elements/Element.md) |  |  |
| `verticalShift` | `double` | `0` |  |
| `horizontalShift` | `double` | `0` |  |

## `RelativePanel`

```csharp
RelativePanelElement RelativePanel(params Element?[] children)
```

**Returns:** [`RelativePanelElement`](../elements/layout.md#relativepanelelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](../elements/Element.md) |  |

## `ScrollView`

Creates a [`ScrollViewElement`](../elements/layout.md#scrollviewelement) wrapping `child` in the modern [`ScrollView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollview) (InteractionTracker-backed, derives from `FrameworkElement`).

```csharp
ScrollViewElement ScrollView(Element child)
```

**Returns:** [`ScrollViewElement`](../elements/layout.md#scrollviewelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element`](../elements/Element.md) |  |

**Remarks**

Exposes capabilities the legacy `ScrollViewer` lacks — `ContentOrientation`, `HorizontalAnchorRatio` / `VerticalAnchorRatio`, and the `Scrolling*` enum surface. For the classic control, use [`ScrollViewer`](#scrollviewer). Issue #348.

## `ScrollViewer`

Creates a [`ScrollViewerElement`](../elements/layout.md#scrollviewerelement) wrapping `child` in the classic [`ScrollViewer`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollviewer) container (derives from `Control`; pan + zoom).

```csharp
ScrollViewerElement ScrollViewer(Element child)
```

**Returns:** [`ScrollViewerElement`](../elements/layout.md#scrollviewerelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element`](../elements/Element.md) |  |

**Remarks**

For the newer InteractionTracker-backed [`ScrollView`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollview) (different enum surface, different events, additive features like `ContentOrientation` and anchor ratios), use [`ScrollView`](#scrollview). Issue #348.

## `SplitView`

```csharp
SplitViewElement SplitView(Element? pane = null, Element? content = null)
```

**Returns:** [`SplitViewElement`](../elements/layout.md#splitviewelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `pane` | [`Element?`](../elements/Element.md) | `null` |  |
| `content` | [`Element?`](../elements/Element.md) | `null` |  |

## `Thick`

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

## `UniformGrid`

Creates a uniform grid with equal-sized cells along one axis. Shorthand for a grid where all items share equal proportions with no separators.

```csharp
GridElement UniformGrid(Orientation orientation, params Element?[] items)
```

**Returns:** [`GridElement`](../elements/layout.md#gridelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `orientation` | `Orientation` |  |
| `items` | [`Element?[]`](../elements/Element.md) |  |

## `VStack`

```csharp
StackElement VStack(params Element?[] children)
StackElement VStack(double spacing, params Element?[] children)
```

| Overload | Description |
| --- | --- |
| `VStack(Element?[])` | Creates a vertical [`StackElement`](../elements/layout.md#stackelement) (WinUI `StackPanel` with [`Orientation.Vertical`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.orientation.vertical)). Default `Spacing` is 8 — see [`StackElement.Spacing`](../elements/layout.md#stackelement). |
| `VStack(double, Element?[])` | Creates a vertical [`StackElement`](../elements/layout.md#stackelement) with an explicit `Spacing` override (the first positional argument). |

**Returns:** [`StackElement`](../elements/layout.md#stackelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](../elements/Element.md) |  |
| `spacing` | `double` |  |

**Remarks**

Reactor-original name — WinUI exposes one `StackPanel` control keyed by `Orientation`; Reactor splits it into two factories ([`VStack`](#vstack) / [`HStack`](#hstack)) because the orientation is almost always known at the call site and the shorter names reduce DSL noise. The SwiftUI / React Native names are load-bearing for cross-platform agent familiarity. (spec 039 §0.3)

Reactor-original convenience overload — see [`VStack`](#vstack) for the naming rationale.

## `Viewbox`

```csharp
ViewboxElement Viewbox(Element child)
```

**Returns:** [`ViewboxElement`](../elements/layout.md#viewboxelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `child` | [`Element`](../elements/Element.md) |  |

## `WrapGrid`

```csharp
WrapGridElement WrapGrid(params Element?[] children)
WrapGridElement WrapGrid(int maxRowsOrColumns, params Element?[] children)
```

**Returns:** [`WrapGridElement`](../elements/layout.md#wrapgridelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `children` | [`Element?[]`](../elements/Element.md) |  |
| `maxRowsOrColumns` | `int` |  |
