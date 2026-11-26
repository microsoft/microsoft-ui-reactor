# ChartElement\<T\>

```csharp
public sealed class ChartElement<T>
```

Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Charting](../../charting.md)

## Constructors

### `ChartElement`

```csharp
public ChartElement()
```

## Methods

### `AlternateView`

Enables alternate-view toggle (T / Alt+Shift+F11). When toggled, the chart is hidden from UIA and `view` is shown instead (typically a data table).

```csharp
public ChartElement<T> AlternateView(Element view)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `view` | [`Element`](../elements/Element.md) |  |

### `AnnounceEveryFrame`

Announces every animation frame via live region. Not recommended — floods assistive technology. Triggers scanner warning A11Y_CHART_007.

```csharp
public ChartElement<T> AnnounceEveryFrame()
```

### `AxisLabel`

Explicit axis name.

```csharp
public ChartElement<T> AxisLabel(ChartAxisType axis, string label)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `axis` | [`ChartAxisType`](chart-accessibility.md#chartaxistype) |  |
| `label` | `string` |  |

### `ChartBackground`

#### `ChartBackground(D3Color)`

Declares the representative background color the chart actually renders on. Lets the theme-agnostic accessibility scanner scope A11Y_CHART_011's custom-palette contrast check to this single active background (a `warning`) instead of flagging failure against either fixed light/dark background (an `info`). Omit for charts that may render on any background.

The stored value is normalized to opaque RGB: contrast math ([`ChartPalette.ContrastRatio`](chart-accessibility.md#contrastratio)) cannot evaluate a semi-transparent background without knowing what is behind it, so any alpha is dropped.

```csharp
public ChartElement<T> ChartBackground(D3Color background)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `background` | [`D3Color`](d3-color-and-format.md#d3color) |  |

#### `ChartBackground(string)`

Parses a CSS color string (hex, rgb(), hsl(), or named).

```csharp
public ChartElement<T> ChartBackground(string background)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `background` | `string` |  |

#### `ChartBackground(Color)`

Accepts a [`Color`](https://learn.microsoft.com/uwp/api/windows.ui.color).

```csharp
public ChartElement<T> ChartBackground(Color background)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `background` | `Color` |  |

### `ColorOnly`

Disables shape/dash double-encoding — color is sole series differentiator. Triggers scanner warning A11Y_CHART_004.

```csharp
public ChartElement<T> ColorOnly()
```

### `DataLabel`

Per-point label override. Receives the data item and its index.

```csharp
public ChartElement<T> DataLabel(Func<T, int, string> labeller)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `labeller` | `Func<T, int, string>` |  |

### `Description`

Overrides auto-generated accessible description/summary.

```csharp
public ChartElement<T> Description(string description)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `description` | `string` |  |

### `DisableKeyboard`

Disables keyboard navigation on an interactive chart. Triggers scanner warning A11Y_CHART_003.

```csharp
public ChartElement<T> DisableKeyboard()
```

### `Fill`

```csharp
public ChartElement<T> Fill(string color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `string` |  |

### `FillOpacity`

```csharp
public ChartElement<T> FillOpacity(double opacity)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `opacity` | `double` |  |

### `FocusColor`

Overrides the default double-ring focus indicator color. Scanner validates contrast (A11Y_CHART_006).

```csharp
public ChartElement<T> FocusColor(Color color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `Color` |  |

### `Height`

```csharp
public ChartElement<T> Height(double height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `height` | `double` |  |

### `Interactive`

Enables keyboard navigation and virtual focus on the chart.

```csharp
public ChartElement<T> Interactive()
```

### `Margin`

```csharp
public ChartElement<T> Margin(double left, double top, double right, double bottom)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `left` | `double` |  |
| `top` | `double` |  |
| `right` | `double` |  |
| `bottom` | `double` |  |

### `OnBrushChanged`

Callback invoked when brush selection changes.

```csharp
public ChartElement<T> OnBrushChanged(Action<ChartRange> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<ChartRange>` |  |

### `OnPointInvoke`

Callback invoked when Enter/Space is pressed on a focused point or a point is clicked.

```csharp
public ChartElement<T> OnPointInvoke(Action<T, int> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<T, int>` |  |

### `OnReady`

Called after the chart Canvas is mounted. The handle exposes the Canvas for escape-hatch scenarios. Prefer state-driven re-renders for data updates.

```csharp
public ChartElement<T> OnReady(Action<ChartHandle<T>> callback)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callback` | `Action<ChartHandle<T>>` |  |

### `Palette`

Sets a curated accessible palette (Tier 1).

```csharp
public ChartElement<T> Palette(ChartPalette palette)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `palette` | [`ChartPalette`](chart-accessibility.md#chartpalette) |  |

### `RawColors`

Sets raw series colors — escape hatch with no validation (Tier 4). Triggers scanner warning A11Y_CHART_012.

```csharp
public ChartElement<T> RawColors(params D3Color[] colors)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `colors` | [`D3Color[]`](d3-color-and-format.md#d3color) |  |

### `SeriesColors`

Sets custom series colors (Tier 3 — scanner-validated).

```csharp
public ChartElement<T> SeriesColors(params D3Color[] colors)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `colors` | [`D3Color[]`](d3-color-and-format.md#d3color) |  |

### `SeriesDashes`

Explicit dash patterns for series (overrides default cycle).

```csharp
public ChartElement<T> SeriesDashes(params DashStyle[] dashes)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `dashes` | [`DashStyle[]`](chart-accessibility.md#dashstyle) |  |

### `SeriesName`

Sets the series name (for single-series charts).

```csharp
public ChartElement<T> SeriesName(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |

### `SeriesNames`

Sets names for multiple series.

```csharp
public ChartElement<T> SeriesNames(params string[] names)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `names` | `string[]` |  |

### `SeriesShapes`

Explicit marker shapes for series (overrides default cycle).

```csharp
public ChartElement<T> SeriesShapes(params MarkerShape[] shapes)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `shapes` | [`MarkerShape[]`](chart-accessibility.md#markershape) |  |

### `ShowAxes`

```csharp
public ChartElement<T> ShowAxes(bool show)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `show` | `bool` |  |

### `ShowGrid`

```csharp
public ChartElement<T> ShowGrid(bool show)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `show` | `bool` |  |

### `Stroke`

```csharp
public ChartElement<T> Stroke(string color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `string` |  |

### `StrokeWidth`

```csharp
public ChartElement<T> StrokeWidth(double width)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

### `TightHitTest`

Uses tight (non-expanded) hit areas for markers. Triggers scanner warning A11Y_CHART_005.

```csharp
public ChartElement<T> TightHitTest()
```

### `Title`

Sets visible title + accessible name for the chart.

```csharp
public ChartElement<T> Title(string title)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `title` | `string` |  |

### `ToElement`

```csharp
public Element ToElement()
```

### `Units`

Axis unit annotations (e.g., "months", "USD").

```csharp
public ChartElement<T> Units(string? xUnits = null, string? yUnits = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `xUnits` | `string?` | `null` |  |
| `yUnits` | `string?` | `null` |  |

### `Width`

```csharp
public ChartElement<T> Width(double width)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

### `XTickLabelView`

Replaces the built-in numeric X-axis tick label with a caller-supplied [`Element`](../elements/Element.md). The element is horizontally centered on the tick mark. By default it is rendered non-interactive and its *entire* realized subtree is hidden from the UIA tree and keyboard tab order (the chart's accessibility data continues to describe axis ticks).

```csharp
public ChartElement<T> XTickLabelView(Func<double, Element> render, Func<double, string>? name = null, bool interactive = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `render` | `Func<double, Element>` |  | Renders the tick element from the tick value. |
| `name` | `Func<double, string>?` | `null` | Optional accessible-name projection applied as the rendered element's `AutomationName`. Only observable when `interactive` is `true` — non-interactive ticks are hidden from UIA, so the chart's own descriptor remains the single source of truth. (Unlike [`PieChartElement.LabelView`](PieChartElement.md#labelview), an axis tick has no per-tick descriptor for the projection to feed, so it never affects a hidden tick's accessible name.) |
| `interactive` | `bool` | `false` | Opt-in escape hatch. When `true`, the chart does **not** force `AccessibilityView.Raw` / remove the subtree from focus — the caller takes responsibility for the tick element's accessibility. |

### `YTickLabelView`

Replaces the built-in numeric Y-axis tick label with a caller-supplied [`Element`](../elements/Element.md). The element is right-anchored to the axis edge and vertically centered on the tick. By default it is rendered non-interactive and its *entire* realized subtree is hidden from the UIA tree and keyboard tab order.

```csharp
public ChartElement<T> YTickLabelView(Func<double, Element> render, Func<double, string>? name = null, bool interactive = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `render` | `Func<double, Element>` |  | Renders the tick element from the tick value. |
| `name` | `Func<double, string>?` | `null` | Optional accessible-name projection applied as the rendered element's `AutomationName`. Only observable when `interactive` is `true`. |
| `interactive` | `bool` | `false` | Opt-in escape hatch. When `true`, the chart does **not** hide the subtree from UIA / focus — the caller owns the tick element's accessibility. |

## Operators

### `op_Implicit`

```csharp
public static implicit operator Element(ChartElement<T> chart)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `chart` | [`ChartElement<T>`](ChartElement.md) |  |
