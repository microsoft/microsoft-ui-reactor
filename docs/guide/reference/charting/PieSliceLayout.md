# PieSliceLayout

Layout metadata for a single pie slice, supplied to [`PieChartElement.LabelView`](PieChartElement.md#labelview) callbacks. Centroid coordinates are absolute within the chart canvas; angles are radians measured clockwise from 12 o'clock (matches d3-shape semantics).

```csharp
public readonly record struct PieSliceLayout(int Index, double Value, double Fraction, double CentroidX, double CentroidY, double StartAngle, double EndAngle, double InnerRadius, double OuterRadius, D3Color Color)
```

Namespace: `Microsoft.UI.Reactor.Charting`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Charting](../../charting.md)

## Constructors

### `PieSliceLayout`

Layout metadata for a single pie slice, supplied to [`PieChartElement.LabelView`](PieChartElement.md#labelview) callbacks. Centroid coordinates are absolute within the chart canvas; angles are radians measured clockwise from 12 o'clock (matches d3-shape semantics).

```csharp
public PieSliceLayout(int Index, double Value, double Fraction, double CentroidX, double CentroidY, double StartAngle, double EndAngle, double InnerRadius, double OuterRadius, D3Color Color)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Index` | `int` |  |
| `Value` | `double` |  |
| `Fraction` | `double` |  |
| `CentroidX` | `double` |  |
| `CentroidY` | `double` |  |
| `StartAngle` | `double` |  |
| `EndAngle` | `double` |  |
| `InnerRadius` | `double` |  |
| `OuterRadius` | `double` |  |
| `Color` | [`D3Color`](d3-color-and-format.md#d3color) |  |

## Properties

### `CentroidX`

```csharp
public double CentroidX  // get, init
```

### `CentroidY`

```csharp
public double CentroidY  // get, init
```

### `Color`

```csharp
public D3Color Color  // get, init
```

### `EndAngle`

```csharp
public double EndAngle  // get, init
```

### `Fraction`

```csharp
public double Fraction  // get, init
```

### `Index`

```csharp
public int Index  // get, init
```

### `InnerRadius`

```csharp
public double InnerRadius  // get, init
```

### `OuterRadius`

```csharp
public double OuterRadius  // get, init
```

### `StartAngle`

```csharp
public double StartAngle  // get, init
```

### `Value`

```csharp
public double Value  // get, init
```
