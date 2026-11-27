# D3: scales

Scales that map data values to positions, sizes and colors.

**Learn more:** [Charting](../../charting.md)

## `BandScale`

```csharp
public static class BandScale
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

```csharp
public static BandScale<string> Create()
public static BandScale<TDomain> Create<TDomain>(params TDomain[] domain) where TDomain : notnull
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `TDomain[]` |  |

## `BandScale<TDomain>`

A band scale maps discrete domain values to continuous bands within a range. Direct port of d3.scaleBand().

```csharp
public sealed class BandScale<TDomain> where TDomain : notnull
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `BandScale`

```csharp
public BandScale()
```

### Properties

#### `Align`

Gets or sets the alignment \[0, 1\].

```csharp
public double Align  // get, set
```

#### `Bandwidth`

Gets the bandwidth (width of each band).

```csharp
public double Bandwidth  // get
```

#### `Domain`

Gets or sets the domain.

```csharp
public TDomain[] Domain  // get, set
```

#### `Padding`

Sets both inner and outer padding to the same value.

```csharp
public double Padding  // set
```

#### `PaddingInner`

Gets or sets the inner padding \[0, 1\].

```csharp
public double PaddingInner  // get, set
```

#### `PaddingOuter`

Gets or sets the outer padding \[0, 1\].

```csharp
public double PaddingOuter  // get, set
```

#### `Range`

Gets or sets the range \[start, stop\].

```csharp
public double[] Range  // get, set
```

#### `Step`

Gets the step (distance between band starts).

```csharp
public double Step  // get
```

### Methods

#### `Copy`

```csharp
public BandScale<TDomain> Copy()
```

#### `Map`

Maps a domain value to the start of its band.

```csharp
public double Map(TDomain x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `TDomain` |  |

#### `SetAlign`

```csharp
public BandScale<TDomain> SetAlign(double a)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | `double` |  |

#### `SetDomain`

```csharp
public BandScale<TDomain> SetDomain(params TDomain[] domain)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `TDomain[]` |  |

#### `SetPadding`

```csharp
public BandScale<TDomain> SetPadding(double p)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `p` | `double` |  |

#### `SetPaddingInner`

```csharp
public BandScale<TDomain> SetPaddingInner(double p)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `p` | `double` |  |

#### `SetPaddingOuter`

```csharp
public BandScale<TDomain> SetPaddingOuter(double p)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `p` | `double` |  |

#### `SetRange`

```csharp
public BandScale<TDomain> SetRange(params double[] range)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `range` | `double[]` |  |

#### `SetRound`

```csharp
public BandScale<TDomain> SetRound(bool round)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `round` | `bool` |  |

## `LinearScale`

A continuous linear scale that maps a numeric domain to a numeric range. Direct port of d3.scaleLinear().

```csharp
public sealed class LinearScale
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `LinearScale`

```csharp
public LinearScale()
public LinearScale(double[] domain, double[] range)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `double[]` |  |
| `range` | `double[]` |  |

### Properties

#### `Clamped`

Gets or sets whether the scale clamps values to the domain.

```csharp
public bool Clamped  // get, set
```

#### `Domain`

Gets or sets the scale domain.

```csharp
public double[] Domain  // get, set
```

#### `Range`

Gets or sets the scale range.

```csharp
public double[] Range  // get, set
```

#### `Unknown`

Gets or sets the value returned for undefined/NaN inputs.

```csharp
public double? Unknown  // get, set
```

### Methods

#### `Copy`

Creates a copy of this scale.

```csharp
public LinearScale Copy()
```

#### `Invert`

Maps a range value back to a domain value (inverse).

```csharp
public double Invert(double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `y` | `double` |  |

#### `Map`

Maps a domain value to a range value.

```csharp
public double Map(double x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |

#### `Nice`

Extends the domain to nice round values. Port of linearish.nice().

```csharp
public LinearScale Nice(int count = 10)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `count` | `int` | `10` |  |

#### `SetClamp`

```csharp
public LinearScale SetClamp(bool clamp)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `clamp` | `bool` |  |

#### `SetDomain`

```csharp
public LinearScale SetDomain(params double[] domain)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `double[]` |  |

#### `SetRange`

```csharp
public LinearScale SetRange(params double[] range)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `range` | `double[]` |  |

#### `SetUnknown`

```csharp
public LinearScale SetUnknown(double? unknown)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `unknown` | `double?` |  |

#### `Ticks`

Returns approximately `count` representative values from the domain.

```csharp
public double[] Ticks(int count = 10)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `count` | `int` | `10` |  |

## `LogScale`

A continuous logarithmic scale. Maps a domain value to a range value using a log transform. Direct port of d3.scaleLog().

```csharp
public sealed class LogScale
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `LogScale`

```csharp
public LogScale()
public LogScale(double[] domain, double[] range)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `double[]` |  |
| `range` | `double[]` |  |

### Properties

#### `Base`

```csharp
public double Base  // get, set
```

#### `Clamped`

```csharp
public bool Clamped  // get, set
```

#### `Domain`

```csharp
public double[] Domain  // get, set
```

#### `Range`

```csharp
public double[] Range  // get, set
```

### Methods

#### `Copy`

```csharp
public LogScale Copy()
```

#### `Invert`

Maps a range value back to a domain value (inverse).

```csharp
public double Invert(double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `y` | `double` |  |

#### `Map`

Maps a domain value to a range value using log transform.

```csharp
public double Map(double x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |

#### `Nice`

Extends the domain to nice powers of the base.

```csharp
public LogScale Nice()
```

#### `SetBase`

```csharp
public LogScale SetBase(double b)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `b` | `double` |  |

#### `SetClamp`

```csharp
public LogScale SetClamp(bool clamp)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `clamp` | `bool` |  |

#### `SetDomain`

```csharp
public LogScale SetDomain(params double[] domain)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `double[]` |  |

#### `SetRange`

```csharp
public LogScale SetRange(params double[] range)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `range` | `double[]` |  |

#### `Ticks`

Returns approximately count representative values from the domain.

```csharp
public double[] Ticks(int count = 10)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `count` | `int` | `10` |  |

## `OrdinalScale`

```csharp
public static class OrdinalScale
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

```csharp
public static OrdinalScale<string> Create()
public static OrdinalScale<TDomain> Create<TDomain>(IEnumerable<TDomain> domain, double[] range) where TDomain : notnull
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `IEnumerable<TDomain>` |  |
| `range` | `double[]` |  |

## `OrdinalScale<TDomain>`

A discrete ordinal scale that maps a set of discrete domain values to a range. Direct port of d3.scaleOrdinal().

```csharp
public sealed class OrdinalScale<TDomain> where TDomain : notnull
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `OrdinalScale`

```csharp
public OrdinalScale()
public OrdinalScale(IEnumerable<TDomain> domain, double[] range)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `IEnumerable<TDomain>` |  |
| `range` | `double[]` |  |

### Properties

#### `AllowImplicitGrowth`

When false, [`OrdinalScale.Map`](#map_3) returns [`OrdinalScale.Unknown`](#unknown_1) (or NaN if none) for keys that aren't already in the domain instead of growing the domain. TASK-091.

```csharp
public bool AllowImplicitGrowth  // get, set
```

#### `Domain`

Gets or sets the domain.

```csharp
public TDomain[] Domain  // get, set
```

#### `Range`

Gets or sets the range.

```csharp
public double[] Range  // get, set
```

#### `Unknown`

Gets or sets the value returned for unknown domain values.

```csharp
public double Unknown  // get, set
```

### Methods

#### `Copy`

```csharp
public OrdinalScale<TDomain> Copy()
```

#### `Map`

Maps a domain value to a range value. If `x` is not in the domain and [`OrdinalScale.Unknown`](#unknown_1) is NaN, `x` is implicitly added to the domain (matching D3 behavior). Set [`OrdinalScale.Unknown`](#unknown_1) to a finite value to disable implicit domain growth.

```csharp
public double Map(TDomain x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `TDomain` |  |

#### `SetDomain`

```csharp
public OrdinalScale<TDomain> SetDomain(params TDomain[] domain)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `TDomain[]` |  |

#### `SetRange`

```csharp
public OrdinalScale<TDomain> SetRange(params double[] range)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `range` | `double[]` |  |

#### `SetUnknown`

```csharp
public OrdinalScale<TDomain> SetUnknown(double unknown)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `unknown` | `double` |  |

## `PointScale`

```csharp
public static class PointScale
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

```csharp
public static PointScale<string> Create()
public static PointScale<TDomain> Create<TDomain>(params TDomain[] domain) where TDomain : notnull
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `TDomain[]` |  |

## `PointScale<TDomain>`

A point scale is a band scale with zero bandwidth. Direct port of d3.scalePoint().

```csharp
public sealed class PointScale<TDomain> where TDomain : notnull
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `PointScale`

```csharp
public PointScale()
```

### Properties

#### `Align`

```csharp
public double Align  // get, set
```

#### `Domain`

```csharp
public TDomain[] Domain  // get, set
```

#### `Padding`

```csharp
public double Padding  // get, set
```

#### `Range`

```csharp
public double[] Range  // get, set
```

#### `Step`

Gets the step between points.

```csharp
public double Step  // get
```

### Methods

#### `Copy`

```csharp
public PointScale<TDomain> Copy()
```

#### `Map`

Maps a domain value to a point position.

```csharp
public double Map(TDomain x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `TDomain` |  |

#### `SetAlign`

```csharp
public PointScale<TDomain> SetAlign(double a)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `a` | `double` |  |

#### `SetDomain`

```csharp
public PointScale<TDomain> SetDomain(params TDomain[] domain)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `TDomain[]` |  |

#### `SetPadding`

```csharp
public PointScale<TDomain> SetPadding(double p)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `p` | `double` |  |

#### `SetRange`

```csharp
public PointScale<TDomain> SetRange(params double[] range)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `range` | `double[]` |  |

## `PowScale`

A continuous power scale. Maps domain values using a power transform (x^exponent). Direct port of d3.scalePow() and d3.scaleSqrt().

```csharp
public sealed class PowScale
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `PowScale`

```csharp
public PowScale()
public PowScale(double exponent)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `exponent` | `double` |  |

### Properties

#### `Clamped`

```csharp
public bool Clamped  // get, set
```

#### `Domain`

```csharp
public double[] Domain  // get, set
```

#### `Exponent`

```csharp
public double Exponent  // get, set
```

#### `Range`

```csharp
public double[] Range  // get, set
```

### Methods

#### `Copy`

```csharp
public PowScale Copy()
```

#### `Invert`

Maps a range value back to a domain value.

```csharp
public double Invert(double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `y` | `double` |  |

#### `Map`

Maps a domain value to a range value using the power transform.

```csharp
public double Map(double x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |

#### `Nice`

Extends the domain to nice round values.

```csharp
public PowScale Nice(int count = 10)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `count` | `int` | `10` |  |

#### `SetClamp`

```csharp
public PowScale SetClamp(bool clamp)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `clamp` | `bool` |  |

#### `SetDomain`

```csharp
public PowScale SetDomain(params double[] domain)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `double[]` |  |

#### `SetExponent`

```csharp
public PowScale SetExponent(double exp)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `exp` | `double` |  |

#### `SetRange`

```csharp
public PowScale SetRange(params double[] range)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `range` | `double[]` |  |

#### `Sqrt`

Creates a square root scale (exponent = 0.5).

```csharp
public static PowScale Sqrt()
```

#### `Ticks`

Returns approximately count representative values from the domain.

```csharp
public double[] Ticks(int count = 10)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `count` | `int` | `10` |  |

## `QuantileScale`

A quantile scale maps a sampled domain to a discrete range using quantiles. Direct port of d3.scaleQuantile().

```csharp
public sealed class QuantileScale
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `QuantileScale`

```csharp
public QuantileScale()
```

### Properties

#### `Domain`

```csharp
public double[] Domain  // get, set
```

#### `Range`

```csharp
public double[] Range  // get, set
```

### Methods

#### `Copy`

```csharp
public QuantileScale Copy()
```

#### `InvertExtent`

Returns the extent of domain values that map to the given range value.

```csharp
public (double x0, double x1) InvertExtent(double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `y` | `double` |  |

#### `Map`

Maps a domain value to a range value based on quantile thresholds.

```csharp
public double Map(double x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |

#### `Quantiles`

Returns the computed quantile thresholds.

```csharp
public double[] Quantiles()
```

#### `SetDomain`

```csharp
public QuantileScale SetDomain(params double[] domain)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `double[]` |  |

#### `SetRange`

```csharp
public QuantileScale SetRange(params double[] range)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `range` | `double[]` |  |

## `QuantizeScale`

A quantize scale maps a continuous domain to a discrete range by dividing the domain into uniform segments. Direct port of d3.scaleQuantize().

```csharp
public sealed class QuantizeScale
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `QuantizeScale`

```csharp
public QuantizeScale()
```

### Properties

#### `Domain`

```csharp
public double[] Domain  // get, set
```

#### `Range`

```csharp
public double[] Range  // get, set
```

### Methods

#### `Copy`

```csharp
public QuantizeScale Copy()
```

#### `InvertExtent`

Returns the extent of domain values that map to the given range value.

```csharp
public (double x0, double x1) InvertExtent(double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `y` | `double` |  |

#### `Map`

Maps a continuous domain value to a discrete range value.

```csharp
public double Map(double x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |

#### `SetDomain`

```csharp
public QuantizeScale SetDomain(double x0, double x1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x0` | `double` |  |
| `x1` | `double` |  |

#### `SetRange`

```csharp
public QuantizeScale SetRange(params double[] range)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `range` | `double[]` |  |

#### `Thresholds`

Returns the computed thresholds between range segments.

```csharp
public double[] Thresholds()
```

## `ThresholdScale`

A threshold scale maps arbitrary domain thresholds to a discrete range. Direct port of d3.scaleThreshold().

```csharp
public sealed class ThresholdScale
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `ThresholdScale`

```csharp
public ThresholdScale()
```

### Properties

#### `Domain`

```csharp
public double[] Domain  // get, set
```

#### `Range`

```csharp
public double[] Range  // get, set
```

### Methods

#### `Copy`

```csharp
public ThresholdScale Copy()
```

#### `InvertExtent`

Returns the extent of domain values that map to the given range value.

```csharp
public (double x0, double x1) InvertExtent(double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `y` | `double` |  |

#### `Map`

Maps a value to a range value based on which threshold interval it falls in.

```csharp
public double Map(double x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |

#### `SetDomain`

```csharp
public ThresholdScale SetDomain(params double[] domain)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `domain` | `double[]` |  |

#### `SetRange`

```csharp
public ThresholdScale SetRange(params double[] range)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `range` | `double[]` |  |
