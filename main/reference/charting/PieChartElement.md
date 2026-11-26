---
title: "PieChartElement<T>"
sidebar:
  label: "PieChartElement"
---

```csharp
public sealed class PieChartElement<T>
```

Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Charting](/microsoft-ui-reactor/main/controls/charting)

## Constructors [#constructors]

### `PieChartElement` [#piechartelement]

```csharp
public PieChartElement()
```

## Methods [#methods]

### `AlternateView` [#alternateview]

Enables alternate-view toggle (T / Alt+Shift+F11).

```csharp
public PieChartElement<T> AlternateView(Element view)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `view` | [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

### `ChartBackground` [#chartbackground]

#### `ChartBackground(D3Color)` [#chartbackgroundd3color]

Declares the representative background color the chart actually renders on. Lets the theme-agnostic accessibility scanner scope A11Y_CHART_011's custom-palette contrast check to this single active background (a `warning`) instead of flagging failure against either fixed light/dark background (an `info`). Omit for charts that may render on any background.

**Note:** the palette contrast-checked against this background is the one the pie actually renders — the `.SetColors(...)` colors — when set. A [`PieChartElement.Palette`](#palette) palette does **not** change what a pie draws (a pie renders the built-in Category10 default unless `.SetColors(...)` overrides it); it is consulted by the scanner only as an advisory fallback when `.SetColors(...)` is unset (issue #645).

The stored value is normalized to opaque RGB: contrast math ([`ChartPalette.ContrastRatio`](/microsoft-ui-reactor/main/reference/charting/chart-accessibility#contrastratio)) cannot evaluate a semi-transparent background without knowing what is behind it, so any alpha is dropped.

```csharp
public PieChartElement<T> ChartBackground(D3Color background)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `background` | [`D3Color`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |

#### `ChartBackground(string)` [#chartbackgroundstring]

Parses a CSS color string (hex, rgb(), hsl(), or named).

```csharp
public PieChartElement<T> ChartBackground(string background)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `background` | `string` |  |

#### `ChartBackground(Color)` [#chartbackgroundcolor]

Accepts a [`Color`](https://learn.microsoft.com/uwp/api/windows.ui.color).

```csharp
public PieChartElement<T> ChartBackground(Color background)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `background` | `Color` |  |

### `ColorOnly` [#coloronly]

Disables shape/dash double-encoding. Triggers scanner warning A11Y_CHART_004.

```csharp
public PieChartElement<T> ColorOnly()
```

### `DataLabel` [#datalabel]

Per-slice label override.

```csharp
public PieChartElement<T> DataLabel(Func<T, int, string> labeller)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `labeller` | `Func<T, int, string>` |  |

### `Description` [#description]

Overrides auto-generated accessible description/summary.

```csharp
public PieChartElement<T> Description(string description)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `description` | `string` |  |

### `Height` [#height]

```csharp
public PieChartElement<T> Height(double height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `height` | `double` |  |

### `InnerRadius` [#innerradius]

```csharp
public PieChartElement<T> InnerRadius(double radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `radius` | `double` |  |

### `LabelRadiusOffset` [#labelradiusoffset]

Shifts auto-positioned labels along the radial axis from the slice centroid. Positive values push labels outward (toward the arc); negative values pull them inward (toward the chart center). Units are pixels. Applies to both the built-in text label and [`PieChartElement.LabelView`](#labelview)-rendered elements.

```csharp
public PieChartElement<T> LabelRadiusOffset(double offset)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `offset` | `double` |  |

### `LabelView` [#labelview]

Replaces the built-in text label for each slice with a caller-supplied [`Element`](/microsoft-ui-reactor/main/reference/elements/Element). The element is positioned centered on the slice's centroid (it does not need a known size), rendered non-interactive by default (no hit-testing), and its *entire* realized subtree is hidden from the UIA tree and keyboard tab order so the chart's `IChartAccessibilityData` remains the single accessible representation of slice data. Set the original string label via the `label` parameter on [`Charts.PieChart`](/microsoft-ui-reactor/main/reference/charting/Charts#piechart), [`PieChartElement.DataLabel`](#datalabel), or the `name` projection to keep accessibility metadata when overriding the visual.

```csharp
public PieChartElement<T> LabelView(Func<T, PieSliceLayout, Element> render, Func<T, string>? name = null, bool interactive = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `render` | `Func<T, PieSliceLayout, Element>` |  | Renders the slice label from the data item and its layout. |
| `name` | `Func<T, string>?` | `null` | Optional accessible-name projection. When neither a string `label` accessor nor [`PieChartElement.DataLabel`](#datalabel) is supplied, this becomes the slice's accessible name in the chart's descriptor (instead of falling back to `"Slice {i+1}"`), so screen-reader users get the same meaningful label the visual shows. Note the deliberate asymmetry with the axis-tick `*TickLabelView(name:)` overloads: a pie slice *has* a per-slice descriptor, so `name` feeds that descriptor even for non-interactive labels; an axis tick has no per-tick descriptor, so there the projection only sets `AutomationName` and is observable only when `interactive: true`. |
| `interactive` | `bool` | `false` | Opt-in escape hatch. When `true`, the chart does **not** force `AccessibilityView.Raw` / remove the subtree from focus — the caller takes responsibility for the label element's accessibility. |

### `OnReady` [#onready]

```csharp
public PieChartElement<T> OnReady(Action<PieChartHandle<T>> callback)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `callback` | `Action<PieChartHandle<T>>` |  |

### `PadAngle` [#padangle]

```csharp
public PieChartElement<T> PadAngle(double angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `double` |  |

### `Palette` [#palette]

Declares a scanner-visible accessible palette for the pie. For a pie this is **advisory-only**: it does **not** change the rendered slice colors (a pie renders the `.SetColors(...)` colors when set, otherwise the built-in Category10 default) and is consulted by the accessibility scanner only as a contrast-check fallback when `.SetColors(...)` is unset (issue #645). Accepts any [`ChartPalette`](/microsoft-ui-reactor/main/reference/charting/chart-accessibility#chartpalette): a curated palette such as `ChartPalette.OkabeIto` (Tier 1) is recommended for guaranteed contrast, but `ChartPalette.FromColors(...)` (Tier 3) and `ChartPalette.FromRaw(...)` (Tier 4) are accepted too. Use [`PieChartElement.SetColors`](#setcolors) to control the colors a pie actually draws.

```csharp
public PieChartElement<T> Palette(ChartPalette palette)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `palette` | [`ChartPalette`](/microsoft-ui-reactor/main/reference/charting/chart-accessibility#chartpalette) |  |

### `SeriesNames` [#seriesnames]

Sets names for pie slices (mapped to series in accessibility).

```csharp
public PieChartElement<T> SeriesNames(params string[] names)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `names` | `string[]` |  |

### `SetColors` [#setcolors]

Override the slice color palette. Colors cycle modulo the palette length when there are more slices than colors. Calling with an empty argument list clears the override and restores the default palette — we deliberately don't store an empty palette because every downstream consumer would have to mod-by-zero guard, and "no colors" isn't a meaningful render state.

**Accessibility:** these are the colors the slices are actually drawn with, so the a11y scanner validates them as a Tier-3 custom palette (A11Y_CHART_009/010/011) — exactly like [`PieChartElement.Palette`](#palette) colors (issue #645). They are the single source of truth for both rendering and the scanner: when both `.SetColors(...)` and `.Palette(...)` are set, the rendered `.SetColors(...)` colors are the ones scanned. Declare [`PieChartElement.ChartBackground`](#chartbackgroundd3color) to scope the contrast check to the background the chart renders on.

```csharp
public PieChartElement<T> SetColors(params D3Color[] colors)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `colors` | [`D3Color[]`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |

### `Title` [#title]

Sets visible title + accessible name for the chart.

```csharp
public PieChartElement<T> Title(string title)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `title` | `string` |  |

### `ToElement` [#toelement]

```csharp
public Element ToElement()
```

### `Width` [#width]

```csharp
public PieChartElement<T> Width(double width)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

## Operators [#operators]

### `op_Implicit` [#op_implicit]

```csharp
public static implicit operator Element(PieChartElement<T> chart)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `chart` | [`PieChartElement<T>`](/microsoft-ui-reactor/main/reference/charting/PieChartElement) |  |
