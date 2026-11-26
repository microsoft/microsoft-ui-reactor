---
title: "ChartElement<T>"
sidebar:
  label: "ChartElement"
---

```csharp
public sealed class ChartElement<T>
```

Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Charting](/microsoft-ui-reactor/main/controls/charting)

## Constructors [#constructors]

### `ChartElement` [#chartelement]

```csharp
public ChartElement()
```

## Methods [#methods]

### `AlternateView` [#alternateview]

Enables alternate-view toggle (T / Alt+Shift+F11). When toggled, the chart is hidden from UIA and `view` is shown instead (typically a data table).

```csharp
public ChartElement<T> AlternateView(Element view)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `view` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

### `AnnounceEveryFrame` [#announceeveryframe]

Announces every animation frame via live region. Not recommended — floods assistive technology. Triggers scanner warning A11Y_CHART_007.

```csharp
public ChartElement<T> AnnounceEveryFrame()
```

### `AxisLabel` [#axislabel]

Explicit axis name.

```csharp
public ChartElement<T> AxisLabel(ChartAxisType axis, string label)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `axis` | [`ChartAxisType`](/microsoft-ui-reactor/main/reference/charting/chart-accessibility#chartaxistype) |  |
| `label` | `string` |  |

### `ChartBackground` [#chartbackground]

#### `ChartBackground(D3Color)` [#chartbackgroundd3color]

Declares the representative background color the chart actually renders on. Lets the theme-agnostic accessibility scanner scope A11Y_CHART_011's custom-palette contrast check to this single active background (a `warning`) instead of flagging failure against either fixed light/dark background (an `info`). Omit for charts that may render on any background.

The stored value is normalized to opaque RGB: contrast math ([`ChartPalette.ContrastRatio`](/microsoft-ui-reactor/main/reference/charting/chart-accessibility#contrastratio)) cannot evaluate a semi-transparent background without knowing what is behind it, so any alpha is dropped.

```csharp
public ChartElement<T> ChartBackground(D3Color background)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `background` | [`D3Color`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |

#### `ChartBackground(string)` [#chartbackgroundstring]

Parses a CSS color string (hex, rgb(), hsl(), or named).

```csharp
public ChartElement<T> ChartBackground(string background)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `background` | `string` |  |

#### `ChartBackground(Color)` [#chartbackgroundcolor]

Accepts a [`Color`](https://learn.microsoft.com/uwp/api/windows.ui.color).

```csharp
public ChartElement<T> ChartBackground(Color background)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `background` | `Color` |  |

### `ColorOnly` [#coloronly]

Disables shape/dash double-encoding — color is sole series differentiator. Triggers scanner warning A11Y_CHART_004.

```csharp
public ChartElement<T> ColorOnly()
```

### `DataLabel` [#datalabel]

Per-point label override. Receives the data item and its index.

```csharp
public ChartElement<T> DataLabel(Func<T, int, string> labeller)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `labeller` | `Func<T, int, string>` |  |

### `Description` [#description]

Overrides auto-generated accessible description/summary.

```csharp
public ChartElement<T> Description(string description)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `description` | `string` |  |

### `DisableKeyboard` [#disablekeyboard]

Disables keyboard navigation on an interactive chart. Triggers scanner warning A11Y_CHART_003.

```csharp
public ChartElement<T> DisableKeyboard()
```

### `Fill` [#fill]

```csharp
public ChartElement<T> Fill(string color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `string` |  |

### `FillOpacity` [#fillopacity]

```csharp
public ChartElement<T> FillOpacity(double opacity)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `opacity` | `double` |  |

### `FocusColor` [#focuscolor]

Overrides the default double-ring focus indicator color. Scanner validates contrast (A11Y_CHART_006).

```csharp
public ChartElement<T> FocusColor(Color color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `Color` |  |

### `Height` [#height]

```csharp
public ChartElement<T> Height(double height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `height` | `double` |  |

### `Interactive` [#interactive]

Enables keyboard navigation and virtual focus on the chart.

```csharp
public ChartElement<T> Interactive()
```

### `Margin` [#margin]

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

### `OnBrushChanged` [#onbrushchanged]

Callback invoked when brush selection changes.

```csharp
public ChartElement<T> OnBrushChanged(Action<ChartRange> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<ChartRange>` |  |

### `OnPointInvoke` [#onpointinvoke]

Callback invoked when Enter/Space is pressed on a focused point or a point is clicked.

```csharp
public ChartElement<T> OnPointInvoke(Action<T, int> handler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<T, int>` |  |

### `OnReady` [#onready]

Called after the chart Canvas is mounted. The handle exposes the Canvas for escape-hatch scenarios. Prefer state-driven re-renders for data updates.

```csharp
public ChartElement<T> OnReady(Action<ChartHandle<T>> callback)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callback` | `Action<ChartHandle<T>>` |  |

### `Palette` [#palette]

Sets a curated accessible palette (Tier 1).

```csharp
public ChartElement<T> Palette(ChartPalette palette)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `palette` | [`ChartPalette`](/microsoft-ui-reactor/main/reference/charting/chart-accessibility#chartpalette) |  |

### `RawColors` [#rawcolors]

Sets raw series colors — escape hatch with no validation (Tier 4). Triggers scanner warning A11Y_CHART_012.

```csharp
public ChartElement<T> RawColors(params D3Color[] colors)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `colors` | [`D3Color[]`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |

### `SeriesColors` [#seriescolors]

Sets custom series colors (Tier 3 — scanner-validated).

```csharp
public ChartElement<T> SeriesColors(params D3Color[] colors)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `colors` | [`D3Color[]`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |

### `SeriesDashes` [#seriesdashes]

Explicit dash patterns for series (overrides default cycle).

```csharp
public ChartElement<T> SeriesDashes(params DashStyle[] dashes)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `dashes` | [`DashStyle[]`](/microsoft-ui-reactor/main/reference/charting/chart-accessibility#dashstyle) |  |

### `SeriesName` [#seriesname]

Sets the series name (for single-series charts).

```csharp
public ChartElement<T> SeriesName(string name)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `name` | `string` |  |

### `SeriesNames` [#seriesnames]

Sets names for multiple series.

```csharp
public ChartElement<T> SeriesNames(params string[] names)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `names` | `string[]` |  |

### `SeriesShapes` [#seriesshapes]

Explicit marker shapes for series (overrides default cycle).

```csharp
public ChartElement<T> SeriesShapes(params MarkerShape[] shapes)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `shapes` | [`MarkerShape[]`](/microsoft-ui-reactor/main/reference/charting/chart-accessibility#markershape) |  |

### `ShowAxes` [#showaxes]

```csharp
public ChartElement<T> ShowAxes(bool show)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `show` | `bool` |  |

### `ShowGrid` [#showgrid]

```csharp
public ChartElement<T> ShowGrid(bool show)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `show` | `bool` |  |

### `Stroke` [#stroke]

```csharp
public ChartElement<T> Stroke(string color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `string` |  |

### `StrokeWidth` [#strokewidth]

```csharp
public ChartElement<T> StrokeWidth(double width)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

### `TightHitTest` [#tighthittest]

Uses tight (non-expanded) hit areas for markers. Triggers scanner warning A11Y_CHART_005.

```csharp
public ChartElement<T> TightHitTest()
```

### `Title` [#title]

Sets visible title + accessible name for the chart.

```csharp
public ChartElement<T> Title(string title)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `title` | `string` |  |

### `ToElement` [#toelement]

```csharp
public Element ToElement()
```

### `Units` [#units]

Axis unit annotations (e.g., "months", "USD").

```csharp
public ChartElement<T> Units(string? xUnits = null, string? yUnits = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `xUnits` | `string?` | `null` |  |
| `yUnits` | `string?` | `null` |  |

### `Width` [#width]

```csharp
public ChartElement<T> Width(double width)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

### `XTickLabelView` [#xticklabelview]

Replaces the built-in numeric X-axis tick label with a caller-supplied [`Element`](/microsoft-ui-reactor/main/reference/elements/Element). The element is horizontally centered on the tick mark. By default it is rendered non-interactive and its *entire* realized subtree is hidden from the UIA tree and keyboard tab order (the chart's accessibility data continues to describe axis ticks).

```csharp
public ChartElement<T> XTickLabelView(Func<double, Element> render, Func<double, string>? name = null, bool interactive = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `render` | `Func<double, Element>` |  | Renders the tick element from the tick value. |
| `name` | `Func<double, string>?` | `null` | Optional accessible-name projection applied as the rendered element's `AutomationName`. Only observable when `interactive` is `true` — non-interactive ticks are hidden from UIA, so the chart's own descriptor remains the single source of truth. (Unlike [`PieChartElement.LabelView`](/microsoft-ui-reactor/main/reference/charting/PieChartElement#labelview), an axis tick has no per-tick descriptor for the projection to feed, so it never affects a hidden tick's accessible name.) |
| `interactive` | `bool` | `false` | Opt-in escape hatch. When `true`, the chart does **not** force `AccessibilityView.Raw` / remove the subtree from focus — the caller takes responsibility for the tick element's accessibility. |

### `YTickLabelView` [#yticklabelview]

Replaces the built-in numeric Y-axis tick label with a caller-supplied [`Element`](/microsoft-ui-reactor/main/reference/elements/Element). The element is right-anchored to the axis edge and vertically centered on the tick. By default it is rendered non-interactive and its *entire* realized subtree is hidden from the UIA tree and keyboard tab order.

```csharp
public ChartElement<T> YTickLabelView(Func<double, Element> render, Func<double, string>? name = null, bool interactive = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `render` | `Func<double, Element>` |  | Renders the tick element from the tick value. |
| `name` | `Func<double, string>?` | `null` | Optional accessible-name projection applied as the rendered element's `AutomationName`. Only observable when `interactive` is `true`. |
| `interactive` | `bool` | `false` | Opt-in escape hatch. When `true`, the chart does **not** hide the subtree from UIA / focus — the caller owns the tick element's accessibility. |

## Operators [#operators]

### `op_Implicit` [#op_implicit]

```csharp
public static implicit operator Element(ChartElement<T> chart)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `chart` | [`ChartElement<T>`](/microsoft-ui-reactor/main/reference/charting/ChartElement) |  |
