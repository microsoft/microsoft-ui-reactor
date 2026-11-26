---
title: "Chart accessibility"
description: "Descriptors, palettes and the contrast checks that make charts accessible."
---

**Learn more:** [Charting](/microsoft-ui-reactor/main/controls/charting)

## `ChartAxisDescriptor` [#chartaxisdescriptor]

Describes a chart axis (x or y).

```csharp
public record ChartAxisDescriptor(ChartAxisType AxisType, string? Label, double Min, double Max, string? Units = null)
```

Namespace: `Microsoft.UI.Reactor.Charting.Accessibility`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors]

#### `ChartAxisDescriptor` [#chartaxisdescriptor_1]

Describes a chart axis (x or y).

```csharp
public ChartAxisDescriptor(ChartAxisType AxisType, string? Label, double Min, double Max, string? Units = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `AxisType` | [`ChartAxisType`](#chartaxistype) |  | Which axis this descriptor represents. |
| `Label` | `string?` |  | Human-readable axis label (e.g., "Month"). |
| `Min` | `double` |  | Minimum visible value. |
| `Max` | `double` |  | Maximum visible value. |
| `Units` | `string?` | `null` | Unit annotation (e.g., "USD", "°C"). |

### Properties [#properties]

#### `AxisType` [#axistype]

Which axis this descriptor represents.

```csharp
public ChartAxisType AxisType  // get, init
```

#### `Label` [#label]

Human-readable axis label (e.g., "Month").

```csharp
public string? Label  // get, init
```

#### `Max` [#max]

Maximum visible value.

```csharp
public double Max  // get, init
```

#### `Min` [#min]

Minimum visible value.

```csharp
public double Min  // get, init
```

#### `Units` [#units]

Unit annotation (e.g., "USD", "°C").

```csharp
public string? Units  // get, init
```

## `ChartAxisType` [#chartaxistype]

Identifies which axis a descriptor represents.

```csharp
public enum ChartAxisType
```

Namespace: `Microsoft.UI.Reactor.Charting.Accessibility`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `X` | `0` |  |
| `Y` | `1` |  |

## `ChartPalette` [#chartpalette]

Sealed palette class that ensures chart colors are accessible. Only constructible via curated statics or the [`ChartPalette.Harden`](#harden) utility.

```csharp
public sealed class ChartPalette
```

Namespace: `Microsoft.UI.Reactor.Charting.Accessibility`. Package: `Microsoft.UI.Reactor.Advanced`.

### Properties [#properties_1]

#### `Colors` [#colors]

Gets all colors in this palette.

```csharp
public IReadOnlyList<D3Color> Colors  // get
```

#### `Count` [#count]

Number of colors in this palette.

```csharp
public int Count  // get
```

#### `Item` [#item]

Gets the color at the specified index (wraps for indices ≥ Count).

```csharp
public D3Color this[int]  // get
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |

### Methods [#methods]

#### `ContrastRatio` [#contrastratio]

Computes WCAG 2.x contrast ratio between two colors (1:1 to 21:1).

```csharp
public static double ContrastRatio(D3Color a, D3Color b)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | [`D3Color`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |
| `b` | [`D3Color`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |

#### `DeltaE` [#deltae]

Computes CIE76 ΔE between two colors (Euclidean distance in Lab).

```csharp
public static double DeltaE(D3Color a, D3Color b)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | [`D3Color`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |
| `b` | [`D3Color`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |

#### `FromColors` [#fromcolors]

Creates a palette from user-provided colors, to be scanner-validated (Tier 3).

```csharp
public static ChartPalette FromColors(params D3Color[] colors)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `colors` | [`D3Color[]`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |

#### `FromRaw` [#fromraw]

Creates a palette from raw colors — escape hatch with no validation (Tier 4). Triggers scanner warning A11Y_CHART_012.

```csharp
public static ChartPalette FromRaw(params D3Color[] colors)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `colors` | [`D3Color[]`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |

#### `GetDash` [#getdash]

Gets the dash style for a given series index (wraps).

```csharp
public DashStyle GetDash(int seriesIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `seriesIndex` | `int` |  |

#### `GetDashArray` [#getdasharray]

Gets the dash pattern as a double array for rendering.

```csharp
public static double[] GetDashArray(DashStyle dash)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `dash` | [`DashStyle`](#dashstyle) |  |

#### `GetMarker` [#getmarker]

Gets the marker shape for a given series index (wraps).

```csharp
public MarkerShape GetMarker(int seriesIndex)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `seriesIndex` | `int` |  |

#### `Harden` [#harden]

Checks and adjusts colors for WCAG contrast, colorblind safety, and background contrast. Operates in LCH color space, pushing lightness apart for failing pairs. Max 8 passes by default.

```csharp
public static HardenResult Harden(D3Color[] input, HardenOptions? options = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `input` | [`D3Color[]`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |  |
| `options` | [`HardenOptions?`](#hardenoptions) | `null` |  |

#### `RelativeLuminance` [#relativeluminance]

WCAG 2.x relative luminance of a color.

```csharp
public static double RelativeLuminance(D3Color c)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `c` | [`D3Color`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |

### Fields [#fields]

#### `Cividis` [#cividis]

Cividis 6-color discrete palette — optimized for color vision deficiency.

```csharp
public static readonly ChartPalette Cividis
```

#### `FluentDefault` [#fluentdefault]

Fluent default palette — matches WinUI design language.

```csharp
public static readonly ChartPalette FluentDefault
```

#### `IBM` [#ibm]

IBM Design Language 5-color palette — accessible categorical palette.

```csharp
public static readonly ChartPalette IBM
```

#### `OkabeIto` [#okabeito]

Okabe-Ito 8-color palette — designed for universal color vision. Safe for deuteranopia, protanopia, and tritanopia.

```csharp
public static readonly ChartPalette OkabeIto
```

#### `Viridis` [#viridis]

Viridis-inspired 6-color discrete palette — perceptually uniform.

```csharp
public static readonly ChartPalette Viridis
```

## `ChartPointDescriptor` [#chartpointdescriptor]

Describes a single data point in a chart series for accessibility purposes.

```csharp
public record ChartPointDescriptor(string XLabel, double YValue, string? FormattedLabel = null)
```

Namespace: `Microsoft.UI.Reactor.Charting.Accessibility`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_1]

#### `ChartPointDescriptor` [#chartpointdescriptor_1]

Describes a single data point in a chart series for accessibility purposes.

```csharp
public ChartPointDescriptor(string XLabel, double YValue, string? FormattedLabel = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `XLabel` | `string` |  | Human-readable x-axis label (e.g., "March 14"). |
| `YValue` | `double` |  | Numeric y value. |
| `FormattedLabel` | `string?` | `null` | Pre-formatted label for the point (e.g., "$42,300 on March 14"). When null, the peer generates a default label. |

### Properties [#properties_2]

#### `FormattedLabel` [#formattedlabel]

Pre-formatted label for the point (e.g., "$42,300 on March 14"). When null, the peer generates a default label.

```csharp
public string? FormattedLabel  // get, init
```

#### `XLabel` [#xlabel]

Human-readable x-axis label (e.g., "March 14").

```csharp
public string XLabel  // get, init
```

#### `YValue` [#yvalue]

Numeric y value.

```csharp
public double YValue  // get, init
```

## `ChartSeriesDescriptor` [#chartseriesdescriptor]

Describes a single series in a chart.

```csharp
public record ChartSeriesDescriptor(string Name, IReadOnlyList<ChartPointDescriptor> Points)
```

Namespace: `Microsoft.UI.Reactor.Charting.Accessibility`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_2]

#### `ChartSeriesDescriptor` [#chartseriesdescriptor_1]

Describes a single series in a chart.

```csharp
public ChartSeriesDescriptor(string Name, IReadOnlyList<ChartPointDescriptor> Points)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Name` | `string` | Human-readable series name (e.g., "Revenue"). |
| `Points` | `IReadOnlyList<ChartPointDescriptor>` | Ordered data points in this series. |

### Properties [#properties_3]

#### `Name` [#name]

Human-readable series name (e.g., "Revenue").

```csharp
public string Name  // get, init
```

#### `Points` [#points]

Ordered data points in this series.

```csharp
public IReadOnlyList<ChartPointDescriptor> Points  // get, init
```

## `ChartViewport` [#chartviewport]

Describes the current visible viewport (for pan/zoom scenarios).

```csharp
public record ChartViewport(double XMin, double XMax, double YMin, double YMax)
```

Namespace: `Microsoft.UI.Reactor.Charting.Accessibility`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_3]

#### `ChartViewport` [#chartviewport_1]

Describes the current visible viewport (for pan/zoom scenarios).

```csharp
public ChartViewport(double XMin, double XMax, double YMin, double YMax)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `XMin` | `double` | Left edge of the visible range. |
| `XMax` | `double` | Right edge of the visible range. |
| `YMin` | `double` | Bottom edge of the visible range. |
| `YMax` | `double` | Top edge of the visible range. |

### Properties [#properties_4]

#### `XMax` [#xmax]

Right edge of the visible range.

```csharp
public double XMax  // get, init
```

#### `XMin` [#xmin]

Left edge of the visible range.

```csharp
public double XMin  // get, init
```

#### `YMax` [#ymax]

Top edge of the visible range.

```csharp
public double YMax  // get, init
```

#### `YMin` [#ymin]

Bottom edge of the visible range.

```csharp
public double YMin  // get, init
```

## `DashStyle` [#dashstyle]

Dash pattern for double-encoding chart series. Each series gets a distinct dash pattern in addition to color.

```csharp
public enum DashStyle
```

Namespace: `Microsoft.UI.Reactor.Charting.Accessibility`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values [#values_1]

| Name | Value | Description |
| --- | --- | --- |
| `Solid` | `0` |  |
| `Dash4_2` | `1` |  |
| `Dash2_2` | `2` |  |
| `Dash6_2_2_2` | `3` |  |
| `Dash8_4` | `4` |  |
| `Dash1_1` | `5` |  |

## `HardenDiff` [#hardendiff]

Describes one color adjustment made by [`ChartPalette.Harden`](#harden).

```csharp
public record HardenDiff(int Index, D3Color Original, D3Color Adjusted, string Reason)
```

Namespace: `Microsoft.UI.Reactor.Charting.Accessibility`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_4]

#### `HardenDiff` [#hardendiff_1]

Describes one color adjustment made by [`ChartPalette.Harden`](#harden).

```csharp
public HardenDiff(int Index, D3Color Original, D3Color Adjusted, string Reason)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Index` | `int` |  |
| `Original` | [`D3Color`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |
| `Adjusted` | [`D3Color`](/microsoft-ui-reactor/main/reference/charting/d3-color-and-format#d3color) |  |
| `Reason` | `string` |  |

### Properties [#properties_5]

#### `Adjusted` [#adjusted]

```csharp
public D3Color Adjusted  // get, init
```

#### `Index` [#index]

```csharp
public int Index  // get, init
```

#### `Original` [#original]

```csharp
public D3Color Original  // get, init
```

#### `Reason` [#reason]

```csharp
public string Reason  // get, init
```

## `HardenOptions` [#hardenoptions]

Options for [`ChartPalette.Harden`](#harden).

```csharp
public sealed class HardenOptions
```

Namespace: `Microsoft.UI.Reactor.Charting.Accessibility`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_5]

#### `HardenOptions` [#hardenoptions_1]

```csharp
public HardenOptions()
```

### Properties [#properties_6]

#### `Background` [#background]

Representative background the palette will actually render on. When set, the background-contrast pass scopes its check (and direction-aware nudge) to this single active background instead of hardening against `both` the fixed light and dark backgrounds. Mirrors A11Y_CHART_011's active-background scoping (issue #633). Null keeps the theme-agnostic both-backgrounds behavior.

Opacity is ignored: contrast cannot be evaluated against a semi-transparent background without knowing what is behind it, so any alpha is dropped to opaque RGB inside [`ChartPalette.Harden`](#harden) (matching the `.ChartBackground(...)` DSL modifier).

```csharp
public D3Color? Background  // get, init
```

#### `MaxPasses` [#maxpasses]

Maximum number of adjustment passes. Default 8.

```csharp
public int MaxPasses  // get, init
```

#### `MinBackgroundContrast` [#minbackgroundcontrast]

Minimum contrast ratio of each color against the background. Default 3.0.

```csharp
public double MinBackgroundContrast  // get, init
```

#### `MinColorblindDeltaE` [#mincolorblinddeltae]

Minimum ΔE in simulated colorblind space between any two series. Default 10.

```csharp
public double MinColorblindDeltaE  // get, init
```

#### `MinPairwiseContrast` [#minpairwisecontrast]

Minimum WCAG non-text contrast ratio between any two series colors. Default 3.0.

```csharp
public double MinPairwiseContrast  // get, init
```

## `HardenResult` [#hardenresult]

Result of [`ChartPalette.Harden`](#harden) — contains the adjusted palette plus diagnostic diffs showing what changed.

```csharp
public sealed class HardenResult
```

Namespace: `Microsoft.UI.Reactor.Charting.Accessibility`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_6]

#### `HardenResult` [#hardenresult_1]

```csharp
public HardenResult()
```

### Properties [#properties_7]

#### `Diffs` [#diffs]

Per-color diffs showing what changed from input to output.

```csharp
public required IReadOnlyList<HardenDiff> Diffs  // get, init
```

#### `Palette` [#palette]

The adjusted palette that passes all contrast/colorblind checks.

```csharp
public required ChartPalette Palette  // get, init
```

#### `PassedWithoutChanges` [#passedwithoutchanges]

True if no adjustments were needed — input palette already passed all checks.

```csharp
public required bool PassedWithoutChanges  // get, init
```

## `MarkerShape` [#markershape]

Marker shape for double-encoding chart series. Each series gets a distinct shape in addition to color.

```csharp
public enum MarkerShape
```

Namespace: `Microsoft.UI.Reactor.Charting.Accessibility`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values [#values_2]

| Name | Value | Description |
| --- | --- | --- |
| `Circle` | `0` |  |
| `Square` | `1` |  |
| `Triangle` | `2` |  |
| `Diamond` | `3` |  |
| `Plus` | `4` |  |
| `Cross` | `5` |  |
| `Star` | `6` |  |
| `Hexagon` | `7` |  |
