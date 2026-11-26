---
title: "D3Charts"
description: "Static factory methods for declarative D3 chart drawing. Import with: using static Microsoft.UI.Reactor.Charting.D3Charts;"
---

```csharp
public static class D3Charts
```

Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Charting](/microsoft-ui-reactor/main/controls/charting)

## Properties [#properties]

### `ChartAxis` [#chartaxis]

Axis lines + their tick labels.

```csharp
public static SolidColorBrush ChartAxis  // get
```

### `ChartDisabled` [#chartdisabled]

Disabled element brush — GrayText in forced-colors, gray otherwise.

```csharp
public static SolidColorBrush ChartDisabled  // get
```

### `ChartForeground` [#chartforeground]

Primary text on a chart surface — titles, strong labels.

```csharp
public static SolidColorBrush ChartForeground  // get
```

### `ChartGrid` [#chartgrid]

Subtle horizontal/vertical gridlines behind the plot.

```csharp
public static SolidColorBrush ChartGrid  // get
```

### `ChartMutedForeground` [#chartmutedforeground]

Secondary text — tick labels, subtle annotations, legend labels.

```csharp
public static SolidColorBrush ChartMutedForeground  // get
```

### `ChartSelection` [#chartselection]

Selection highlight brush — Highlight in forced-colors, blue otherwise.

```csharp
public static SolidColorBrush ChartSelection  // get
```

### `ChartSelectionText` [#chartselectiontext]

Selection text — HighlightText in forced-colors, white otherwise.

```csharp
public static SolidColorBrush ChartSelectionText  // get
```

### `ChartSubtleFill` [#chartsubtlefill]

Slightly elevated neutral surface — non-leaf tree fills, alternating rows.

```csharp
public static SolidColorBrush ChartSubtleFill  // get
```

### `ChartSubtleStroke` [#chartsubtlestroke]

Subtle neutral stroke — baselines, separators, non-accented borders.

```csharp
public static SolidColorBrush ChartSubtleStroke  // get
```

### `ChartSurface` [#chartsurface]

Solid fill matching the chart's surrounding card — use for gap strokes between colored slices (pie / sunburst / icicle).

```csharp
public static SolidColorBrush ChartSurface  // get
```

### `ForcedColors` [#forcedcolors]

System high-contrast colors queried from `UISettings.GetColorValue`. Set automatically by the host each render cycle. When null, falls back to safe defaults (white foreground, black background).

```csharp
public static ForcedColorsTheme? ForcedColors  // get, set
```

### `IsDarkTheme` [#isdarktheme]

```csharp
public static bool IsDarkTheme  // get, set
```

### `IsForcedColors` [#isforcedcolors]

When true, chart brushes use system high-contrast colors instead of the normal palette. Set automatically by the host when `AccessibilitySettings.HighContrast` is active.

```csharp
public static bool IsForcedColors  // get, set
```

### `IsReducedMotion` [#isreducedmotion]

When true, chart entrance/exit animations snap to final state, pan inertia is disabled, and force-graph simulation terminates immediately. Set automatically by the host from `UISettings.AnimationsEnabled`.

```csharp
public static bool IsReducedMotion  // get, set
```

## Methods [#methods]

### `Brush` [#brush]

```csharp
public static SolidColorBrush Brush(D3Color c)
public static SolidColorBrush Brush(string color)
public static SolidColorBrush Brush(D3Color c, double opacity)
public static SolidColorBrush Brush(string color, double opacity)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `c` | [`D3Color`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |
| `color` | `string` |  |
| `opacity` | `double` |  |

### `ChartSeries` [#chartseries]

Returns the brush for a chart series. In forced-colors mode, returns system high-contrast colors; otherwise returns the normal palette color.

```csharp
public static SolidColorBrush ChartSeries(int seriesIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `seriesIndex` | `int` |  |

### `ChartSeriesDash` [#chartseriesdash]

Returns the dash pattern for a chart series from the default cycle.

```csharp
public static DashStyle ChartSeriesDash(int seriesIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `seriesIndex` | `int` |  |

### `ChartSeriesMarker` [#chartseriesmarker]

Returns the marker shape for a chart series from the default cycle.

```csharp
public static MarkerShape ChartSeriesMarker(int seriesIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `seriesIndex` | `int` |  |

### `ChartSurfaceAlpha` [#chartsurfacealpha]

Translucent surface — for layered ridge fills or separators that blend with the card.

```csharp
public static SolidColorBrush ChartSurfaceAlpha(byte alpha)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alpha` | `byte` |  |

### `D3ArcPath` [#d3arcpath]

Creates an arc sector path element at (cx, cy), collapsing ArcGenerator + Generate + D3PathTranslated into one expression.

```csharp
public static PathElement D3ArcPath(double startAngle, double endAngle, double cx, double cy, double innerRadius = 0, double outerRadius = 100, double padAngle = 0, Brush? fill = null, Brush? stroke = null, double strokeWidth = 1.5)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `startAngle` | `double` |  |  |
| `endAngle` | `double` |  |  |
| `cx` | `double` |  |  |
| `cy` | `double` |  |  |
| `innerRadius` | `double` | `0` |  |
| `outerRadius` | `double` | `100` |  |
| `padAngle` | `double` | `0` |  |
| `fill` | `Brush?` | `null` |  |
| `stroke` | `Brush?` | `null` |  |
| `strokeWidth` | `double` | `1.5` |  |

### `D3AreaPath` [#d3areapath]

Creates an area path element directly from data, collapsing AreaGenerator + Generate + D3Path into one expression.

```csharp
public static PathElement D3AreaPath<T>(IReadOnlyList<T> data, Func<T, double> x, Func<T, double> y0, Func<T, double> y1, Brush? fill = null, Brush? stroke = null, double strokeWidth = 1.5)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |  |
| `x` | `Func<T, double>` |  |  |
| `y0` | `Func<T, double>` |  |  |
| `y1` | `Func<T, double>` |  |  |
| `fill` | `Brush?` | `null` |  |
| `stroke` | `Brush?` | `null` |  |
| `strokeWidth` | `double` | `1.5` |  |

### `D3Axes` [#d3axes]

Creates X and Y axis lines with tick labels as a flat array of Elements.

```csharp
public static Element[] D3Axes(LinearScale xs, LinearScale ys, double left, double top, double width, double height, int xTicks = 6, int yTicks = 5, Func<double, Element>? xTickLabel = null, Func<double, Element>? yTickLabel = null, Func<double, string>? xTickName = null, Func<double, string>? yTickName = null, bool xTickInteractive = false, bool yTickInteractive = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `xs` | [`LinearScale`](/microsoft-ui-reactor/main/reference/charting/d3-scales#linearscale) |  | Scale that maps domain values to X-axis pixel positions. |
| `ys` | [`LinearScale`](/microsoft-ui-reactor/main/reference/charting/d3-scales#linearscale) |  | Scale that maps domain values to Y-axis pixel positions. |
| `left` | `double` |  | X coordinate of the plot area's left edge. |
| `top` | `double` |  | Y coordinate of the plot area's top edge. |
| `width` | `double` |  | Plot area width. |
| `height` | `double` |  | Plot area height. |
| `xTicks` | `int` | `6` | Approximate number of ticks to generate on the X axis. |
| `yTicks` | `int` | `5` | Approximate number of ticks to generate on the Y axis. |
| `xTickLabel` | `Func<double, Element>?` | `null` | Optional custom renderer for X-axis tick labels. Receives the tick value and returns any [`Element`](/microsoft-ui-reactor/main/reference/elements/Element); the chart anchors it horizontally centered on the tick mark. When null, a built-in numeric `TextBlock` is rendered. |
| `yTickLabel` | `Func<double, Element>?` | `null` | Optional custom renderer for Y-axis tick labels. The returned element is right-anchored to the axis edge and vertically centered on the tick. When null, a built-in numeric `TextBlock` is rendered. |
| `xTickName` | `Func<double, string>?` | `null` | Optional accessible-name projection applied as the X tick element's `AutomationName`. Only observable when `xTickInteractive` is true. |
| `yTickName` | `Func<double, string>?` | `null` | As `xTickName`, for the Y axis. |
| `xTickInteractive` | `bool` | `false` | When true, the custom X tick element keeps its peers/focus — caller owns a11y (issue #162). When false (default), the whole realized subtree is forced to `AccessibilityView.Raw` and removed from the keyboard tab order. |
| `yTickInteractive` | `bool` | `false` | As `xTickInteractive`, for the Y axis. |

### `D3Canvas` [#d3canvas]

Creates a Canvas element with the given dimensions and children.

```csharp
public static CanvasElement D3Canvas(double width, double height, params Element?[] children)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |
| `height` | `double` |  |
| `children` | [`Element?[]`](/microsoft-ui-reactor/main/reference/elements/Element) |  |

### `D3Circle` [#d3circle]

Creates a circle (ellipse) positioned at center (cx, cy) with radius r.

```csharp
public static EllipseElement D3Circle(double cx, double cy, double r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `cx` | `double` |  |
| `cy` | `double` |  |
| `r` | `double` |  |

### `D3Grid` [#d3grid]

Creates horizontal grid lines as a flat array of Elements. Auto-set to AccessibilityView.Raw.

```csharp
public static Element[] D3Grid(LinearScale ys, double left, double width, int ticks = 5)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `ys` | [`LinearScale`](/microsoft-ui-reactor/main/reference/charting/d3-scales#linearscale) |  |  |
| `left` | `double` |  |  |
| `width` | `double` |  |  |
| `ticks` | `int` | `5` |  |

### `D3Legend` [#d3legend]

Creates a legend as rect+text pairs laid out vertically.

```csharp
public static Element[] D3Legend(double x, double y, IEnumerable<(string label, SolidColorBrush color)> items, double fontSize = 11)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `x` | `double` |  |  |
| `y` | `double` |  |  |
| `items` | `IEnumerable<(string label, SolidColorBrush color)>` |  |  |
| `fontSize` | `double` | `11` |  |

### `D3Line` [#d3line]

Creates a line between two points.

```csharp
public static LineElement D3Line(double x1, double y1, double x2, double y2)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x1` | `double` |  |
| `y1` | `double` |  |
| `x2` | `double` |  |
| `y2` | `double` |  |

### `D3LinePath` [#d3linepath]

Creates a line path element directly from data, collapsing LineGenerator + Generate + D3Path into one expression.

```csharp
public static PathElement D3LinePath<T>(IReadOnlyList<T> data, Func<T, double> x, Func<T, double> y, Brush? stroke = null, double strokeWidth = 1.5, CurveFactory? curve = null, Func<T, int, bool>? defined = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |  |
| `x` | `Func<T, double>` |  |  |
| `y` | `Func<T, double>` |  |  |
| `stroke` | `Brush?` | `null` |  |
| `strokeWidth` | `double` | `1.5` |  |
| `curve` | [`CurveFactory?`](/microsoft-ui-reactor/main/reference/charting/d3-shapes#curvefactory) | `null` |  |
| `defined` | `Func<T, int, bool>?` | `null` |  |

### `D3Link` [#d3link]

Creates a vertical bezier tree link path between two points (parent to child).

```csharp
public static PathElement D3Link(double x1, double y1, double x2, double y2, Brush? stroke = null, double strokeWidth = 1.5)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `x1` | `double` |  |  |
| `y1` | `double` |  |  |
| `x2` | `double` |  |  |
| `y2` | `double` |  |  |
| `stroke` | `Brush?` | `null` |  |
| `strokeWidth` | `double` | `1.5` |  |

### `D3Path` [#d3path]

Creates a path from SVG path data string. Accepts null pathData gracefully (renders nothing).

```csharp
public static PathElement D3Path(string? pathData, Brush? stroke = null, Brush? fill = null, double strokeWidth = 1.5)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `pathData` | `string?` |  |  |
| `stroke` | `Brush?` | `null` |  |
| `fill` | `Brush?` | `null` |  |
| `strokeWidth` | `double` | `1.5` |  |

### `D3PathTranslated` [#d3pathtranslated]

Creates a path from SVG path data with a translate transform. Accepts null pathData gracefully (renders nothing).

```csharp
public static PathElement D3PathTranslated(string? pathData, double translateX, double translateY, Brush? stroke = null, Brush? fill = null, double strokeWidth = 1.5)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `pathData` | `string?` |  |  |
| `translateX` | `double` |  |  |
| `translateY` | `double` |  |  |
| `stroke` | `Brush?` | `null` |  |
| `fill` | `Brush?` | `null` |  |
| `strokeWidth` | `double` | `1.5` |  |

### `D3Pie` [#d3pie]

Creates pie/donut slice elements directly from data, collapsing PieGenerator + ArcGenerator + iteration into one expression.

```csharp
public static Element[] D3Pie<T>(IReadOnlyList<T> data, Func<T, double> value, double cx, double cy, double outerRadius = 150, double innerRadius = 0, double padAngle = 0, bool sort = true, Brush? stroke = null, double strokeWidth = 1.5, IReadOnlyList<D3Color>? palette = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  | Source data items, one per slice. |
| `value` | `Func<T, double>` |  | Selector returning the magnitude for each item. |
| `cx` | `double` |  | Center X coordinate (slices are translated into place). |
| `cy` | `double` |  | Center Y coordinate. |
| `outerRadius` | `double` | `150` | Outer radius of the pie/donut. |
| `innerRadius` | `double` | `0` | Inner radius — non-zero produces a donut. |
| `padAngle` | `double` | `0` | Angular padding between slices, in radians. |
| `sort` | `bool` | `true` | When true, slices are sorted by descending value. |
| `stroke` | `Brush?` | `null` | Optional stroke brush for slice borders. |
| `strokeWidth` | `double` | `1.5` | Stroke thickness. |
| `palette` | `IReadOnlyList<D3Color>?` | `null` | Optional color palette. When null or empty, falls back to [`D3Charts.Palette`](#palette) (D3 Category10). Callers that want slice colors to match an external label computation should pass the same palette here that they use for those labels. |

### `D3Rect` [#d3rect]

Creates a positioned rectangle on a Canvas.

```csharp
public static RectangleElement D3Rect(double x, double y, double width, double height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |
| `y` | `double` |  |
| `width` | `double` |  |
| `height` | `double` |  |

### `Fmt` [#fmt]

```csharp
public static string Fmt(double v)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `v` | `double` |  |

### `Gray` [#gray]

```csharp
public static SolidColorBrush Gray(byte v, byte alpha = 255)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `v` | `byte` |  |  |
| `alpha` | `byte` | `255` |  |

### `Text` [#text]

Creates a positioned text label on a Canvas.

```csharp
public static TextBlockElement Text(double x, double y, string text, double fontSize = 10, Brush? foreground = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `x` | `double` |  |  |
| `y` | `double` |  |  |
| `text` | `string` |  |  |
| `fontSize` | `double` | `10` |  |
| `foreground` | `Brush?` | `null` |  |

### `TextCenter` [#textcenter]

Creates a positioned text label with center alignment and explicit width.

```csharp
public static TextBlockElement TextCenter(double x, double y, string text, double width, double fontSize = 10, Brush? foreground = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `x` | `double` |  |  |
| `y` | `double` |  |  |
| `text` | `string` |  |  |
| `width` | `double` |  |  |
| `fontSize` | `double` | `10` |  |
| `foreground` | `Brush?` | `null` |  |

### `TextRight` [#textright]

Creates a positioned text label with right alignment and explicit width (for Y axis labels).

```csharp
public static TextBlockElement TextRight(double x, double y, string text, double width, double fontSize = 10, Brush? foreground = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `x` | `double` |  |  |
| `y` | `double` |  |  |
| `text` | `string` |  |  |
| `width` | `double` |  |  |
| `fontSize` | `double` | `10` |  |
| `foreground` | `Brush?` | `null` |  |

## Fields [#fields]

### `Palette` [#palette]

```csharp
public static readonly IReadOnlyList<D3Color> Palette
```
