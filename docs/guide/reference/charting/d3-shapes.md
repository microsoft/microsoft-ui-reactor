# D3: shapes

Line, area, arc, pie, stack, link and symbol generators, curves and the path builder.

**Learn more:** [Charting](../../charting.md)

## `ArcGenerator`

Generates SVG path data for circular or annular arcs. Direct port of d3.arc(). Works with PieArc data from PieGenerator.

```csharp
public sealed class ArcGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `ArcGenerator`

```csharp
public ArcGenerator()
```

### Methods

#### `Centroid`

##### `Centroid(double, double, double, double)`

Static convenience: computes the centroid of an arc without constructing an instance. Returns the midpoint of the inner/outer radii at the midpoint angle.

```csharp
public static (double x, double y) Centroid(double startAngle, double endAngle, double innerRadius = 0, double outerRadius = 100)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `startAngle` | `double` |  |  |
| `endAngle` | `double` |  |  |
| `innerRadius` | `double` | `0` |  |
| `outerRadius` | `double` | `100` |  |

##### `Centroid(double, double, double?, double?)`

Computes the centroid of an arc — the midpoint of the inner and outer radii, and the midpoint of the start and end angles.

```csharp
public (double x, double y) Centroid(double startAngle, double endAngle, double? innerRadius = null, double? outerRadius = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `startAngle` | `double` |  |  |
| `endAngle` | `double` |  |  |
| `innerRadius` | `double?` | `null` |  |
| `outerRadius` | `double?` | `null` |  |

#### `Generate`

##### `Generate<T>(PieArc<T>)`

Generates the SVG path string for the given arc descriptor.

```csharp
public string? Generate<T>(PieArc<T> arc)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `arc` | [`PieArc<T>`](#piearct) |  |

##### `Generate(double, double, double, double?, double?)`

Generates the SVG path string for explicit angle parameters.

```csharp
public string? Generate(double startAngle, double endAngle, double padAngle = 0, double? innerRadius = null, double? outerRadius = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `startAngle` | `double` |  |  |
| `endAngle` | `double` |  |  |
| `padAngle` | `double` | `0` |  |
| `innerRadius` | `double?` | `null` |  |
| `outerRadius` | `double?` | `null` |  |

#### `SetCornerRadius`

```csharp
public ArcGenerator SetCornerRadius(double r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `r` | `double` |  |

#### `SetDigits`

```csharp
public ArcGenerator SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetInnerRadius`

```csharp
public ArcGenerator SetInnerRadius(double r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `r` | `double` |  |

#### `SetOuterRadius`

```csharp
public ArcGenerator SetOuterRadius(double r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `r` | `double` |  |

## `AreaGenerator`

```csharp
public static class AreaGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

##### `Create<T>(Func<T, double>, Func<T, double>)`

Creates an area generator with baseline at 0.

```csharp
public static AreaGenerator<T> Create<T>(Func<T, double> x, Func<T, double> y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<T, double>` |  |
| `y` | `Func<T, double>` |  |

##### `Create<T>(Func<T, double>, Func<T, double>, Func<T, double>)`

Creates an area generator with custom accessors.

```csharp
public static AreaGenerator<T> Create<T>(Func<T, double> x, Func<T, double> y0, Func<T, double> y1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<T, double>` |  |
| `y0` | `Func<T, double>` |  |
| `y1` | `Func<T, double>` |  |

#### `FromArrays`

Creates an area generator for double\[\] data where \[0\]=x, \[1\]=y.

```csharp
public static AreaGenerator<double[]> FromArrays()
```

## `AreaGenerator<T>`

Generates SVG path data for an area shape from an array of data points. Direct port of d3.area().

```csharp
public sealed class AreaGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `AreaGenerator`

```csharp
public AreaGenerator(Func<T, int, double> x0, Func<T, int, double> y0, Func<T, int, double> y1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x0` | `Func<T, int, double>` |  |
| `y0` | `Func<T, int, double>` |  |
| `y1` | `Func<T, int, double>` |  |

### Methods

#### `Generate`

Generates the SVG path string for the given data.

```csharp
public string? Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetDefined`

```csharp
public AreaGenerator<T> SetDefined(Func<T, int, bool> defined)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `defined` | `Func<T, int, bool>` |  |

#### `SetDigits`

```csharp
public AreaGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetX`

```csharp
public AreaGenerator<T> SetX(Func<T, int, double> x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<T, int, double>` |  |

#### `SetX0`

```csharp
public AreaGenerator<T> SetX0(Func<T, int, double> x0)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x0` | `Func<T, int, double>` |  |

#### `SetX1`

```csharp
public AreaGenerator<T> SetX1(Func<T, int, double>? x1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x1` | `Func<T, int, double>?` |  |

#### `SetY0`

```csharp
public AreaGenerator<T> SetY0(Func<T, int, double> y0)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `y0` | `Func<T, int, double>` |  |

#### `SetY1`

```csharp
public AreaGenerator<T> SetY1(Func<T, int, double> y1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `y1` | `Func<T, int, double>` |  |

## `CurveFactory`

Curve factory that creates curve instances bound to a PathBuilder.

```csharp
public delegate ICurve CurveFactory(PathBuilder path)
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

## `D3Curve`

Built-in curve implementations matching d3-shape curves.

```csharp
public static class D3Curve
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `CardinalWithTension`

Creates a cardinal spline with the given tension \[0, 1\].

```csharp
public static CurveFactory CardinalWithTension(double tension)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `tension` | `double` |  |

#### `CatmullRomWithAlpha`

Creates a Catmull-Rom spline with the given alpha \[0, 1\].

```csharp
public static CurveFactory CatmullRomWithAlpha(double alpha)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alpha` | `double` |  |

### Fields

#### `Basis`

Uniform B-spline. Port of d3.curveBasis.

```csharp
public static readonly CurveFactory Basis
```

#### `BasisClosed`

Closed uniform B-spline. Port of d3.curveBasisClosed.

```csharp
public static readonly CurveFactory BasisClosed
```

#### `Cardinal`

Cardinal spline with default tension. Port of d3.curveCardinal.

```csharp
public static readonly CurveFactory Cardinal
```

#### `CatmullRom`

Catmull-Rom spline with default alpha. Port of d3.curveCatmullRom.

```csharp
public static readonly CurveFactory CatmullRom
```

#### `Linear`

Straight line segments between points. Port of d3.curveLinear.

```csharp
public static readonly CurveFactory Linear
```

#### `MonotoneX`

Monotone cubic interpolation in x. Port of d3.curveMonotoneX.

```csharp
public static readonly CurveFactory MonotoneX
```

#### `Natural`

Natural cubic spline. Port of d3.curveNatural.

```csharp
public static readonly CurveFactory Natural
```

#### `Step`

Step function (midpoint). Port of d3.curveStep.

```csharp
public static readonly CurveFactory Step
```

#### `StepAfter`

Step function (change at end). Port of d3.curveStepAfter.

```csharp
public static readonly CurveFactory StepAfter
```

#### `StepBefore`

Step function (change at start). Port of d3.curveStepBefore.

```csharp
public static readonly CurveFactory StepBefore
```

## `D3Symbol`

Built-in symbol types matching d3-shape.

```csharp
public static class D3Symbol
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields

#### `All`

All built-in symbol types.

```csharp
public static readonly ISymbolType[] All
```

#### `Circle`

```csharp
public static readonly ISymbolType Circle
```

#### `Cross`

```csharp
public static readonly ISymbolType Cross
```

#### `Diamond`

```csharp
public static readonly ISymbolType Diamond
```

#### `Square`

```csharp
public static readonly ISymbolType Square
```

#### `Star`

```csharp
public static readonly ISymbolType Star
```

#### `Triangle`

```csharp
public static readonly ISymbolType Triangle
```

#### `Wye`

```csharp
public static readonly ISymbolType Wye
```

## `ICurve`

Curves transform a sequence of points into a path. This is the interface all curve implementations share.

```csharp
public interface ICurve
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `LineEnd`

```csharp
public void LineEnd()
```

#### `LineStart`

```csharp
public void LineStart()
```

#### `Point`

```csharp
public void Point(double x, double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |
| `y` | `double` |  |

## `ISymbolType`

Defines a symbol shape type (circle, cross, diamond, square, star, triangle, wye).

```csharp
public interface ISymbolType
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Draw`

```csharp
public void Draw(PathBuilder path, double size)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `path` | [`PathBuilder`](#pathbuilder) |  |
| `size` | `double` |  |

## `LineGenerator`

Factory for creating line generators with common data types.

```csharp
public static class LineGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

##### `Create()`

Creates a line generator for (x, y) tuple data.

```csharp
public static LineGenerator<(double x, double y)> Create()
```

##### `Create<T>(Func<T, double>, Func<T, double>)`

Creates a line generator with custom accessors.

```csharp
public static LineGenerator<T> Create<T>(Func<T, double> x, Func<T, double> y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<T, double>` |  |
| `y` | `Func<T, double>` |  |

#### `FromArrays`

Creates a line generator for double\[\] data where \[0\]=x, \[1\]=y.

```csharp
public static LineGenerator<double[]> FromArrays()
```

## `LineGenerator<T>`

Generates SVG path data for a line from an array of data points. Direct port of d3.line().

```csharp
public sealed class LineGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `LineGenerator`

```csharp
public LineGenerator(Func<T, int, double> x, Func<T, int, double> y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<T, int, double>` |  |
| `y` | `Func<T, int, double>` |  |

### Properties

#### `Digits`

```csharp
public int? Digits  // get
```

### Methods

#### `Generate`

Generates the SVG path string for the given data.

```csharp
public string? Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetCurve`

```csharp
public LineGenerator<T> SetCurve(CurveFactory? curve)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `curve` | [`CurveFactory?`](#curvefactory) |  |

#### `SetDefined`

```csharp
public LineGenerator<T> SetDefined(Func<T, int, bool> defined)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `defined` | `Func<T, int, bool>` |  |

#### `SetDigits`

```csharp
public LineGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetX`

```csharp
public LineGenerator<T> SetX(Func<T, int, double> x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<T, int, double>` |  |

#### `SetY`

```csharp
public LineGenerator<T> SetY(Func<T, int, double> y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `y` | `Func<T, int, double>` |  |

## `LinkGenerator`

```csharp
public static class LinkGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Horizontal`

Creates a horizontal link generator (source left, target right).

```csharp
public static LinkGenerator<(TNode source, TNode target)> Horizontal<TNode>(Func<TNode, double> x, Func<TNode, double> y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<TNode, double>` |  |
| `y` | `Func<TNode, double>` |  |

#### `Vertical`

Creates a vertical link generator (source top, target bottom).

```csharp
public static LinkGenerator<(TNode source, TNode target)> Vertical<TNode>(Func<TNode, double> x, Func<TNode, double> y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<TNode, double>` |  |
| `y` | `Func<TNode, double>` |  |

## `LinkGenerator<T>`

Generates SVG path data for smooth links/edges between two points. Direct port of d3.link(), d3.linkHorizontal(), d3.linkVertical().

```csharp
public sealed class LinkGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `LinkGenerator`

```csharp
public LinkGenerator(Func<T, (double x, double y)> source, Func<T, (double x, double y)> target, LinkOrientation orientation = LinkOrientation.Vertical)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | `Func<T, (double x, double y)>` |  |  |
| `target` | `Func<T, (double x, double y)>` |  |  |
| `orientation` | [`LinkOrientation`](#linkorientation) | `LinkOrientation.Vertical` |  |

### Methods

#### `Generate`

Generates the SVG path string for the given link data.

```csharp
public string? Generate(T datum)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `datum` | `T` |  |

#### `SetDigits`

```csharp
public LinkGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetSource`

```csharp
public LinkGenerator<T> SetSource(Func<T, (double x, double y)> source)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `source` | `Func<T, (double x, double y)>` |  |

#### `SetTarget`

```csharp
public LinkGenerator<T> SetTarget(Func<T, (double x, double y)> target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | `Func<T, (double x, double y)>` |  |

## `LinkOrientation`

```csharp
public enum LinkOrientation
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Horizontal` | `0` |  |
| `Vertical` | `1` |  |

## `PathBuilder`

Builds SVG path data strings. Direct port of d3-path's Path class. This is the fundamental rendering primitive — all D3 shapes produce path strings through this builder.

```csharp
public sealed class PathBuilder
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `PathBuilder`

```csharp
public PathBuilder(int? digits = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `digits` | `int?` | `null` |  |

### Methods

#### `Arc`

```csharp
public PathBuilder Arc(double x, double y, double r, double a0, double a1, bool ccw = false)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `x` | `double` |  |  |
| `y` | `double` |  |  |
| `r` | `double` |  |  |
| `a0` | `double` |  |  |
| `a1` | `double` |  |  |
| `ccw` | `bool` | `false` |  |

#### `ArcTo`

```csharp
public PathBuilder ArcTo(double x1, double y1, double x2, double y2, double r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x1` | `double` |  |
| `y1` | `double` |  |
| `x2` | `double` |  |
| `y2` | `double` |  |
| `r` | `double` |  |

#### `BezierCurveTo`

```csharp
public PathBuilder BezierCurveTo(double x1, double y1, double x2, double y2, double x, double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x1` | `double` |  |
| `y1` | `double` |  |
| `x2` | `double` |  |
| `y2` | `double` |  |
| `x` | `double` |  |
| `y` | `double` |  |

#### `ClosePath`

```csharp
public PathBuilder ClosePath()
```

#### `Create`

Creates a new PathBuilder with default (no rounding) settings.

```csharp
public static PathBuilder Create()
```

#### `CreateRound`

Creates a new PathBuilder that rounds to the given number of digits.

```csharp
public static PathBuilder CreateRound(int digits = 3)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `digits` | `int` | `3` |  |

#### `LineTo`

```csharp
public PathBuilder LineTo(double x, double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |
| `y` | `double` |  |

#### `MoveTo`

```csharp
public PathBuilder MoveTo(double x, double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |
| `y` | `double` |  |

#### `QuadraticCurveTo`

```csharp
public PathBuilder QuadraticCurveTo(double x1, double y1, double x, double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x1` | `double` |  |
| `y1` | `double` |  |
| `x` | `double` |  |
| `y` | `double` |  |

#### `Rect`

```csharp
public PathBuilder Rect(double x, double y, double w, double h)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |
| `y` | `double` |  |
| `w` | `double` |  |
| `h` | `double` |  |

#### `ToString`

Returns a string that represents this object.

```csharp
public override string ToString()
```

## `PieArc<T>`

Describes a single arc slice computed by PieGenerator.

```csharp
public readonly record struct PieArc<T>(T Data, double Value, int Index, double StartAngle, double EndAngle, double PadAngle)
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `PieArc`

Describes a single arc slice computed by PieGenerator.

```csharp
public PieArc(T Data, double Value, int Index, double StartAngle, double EndAngle, double PadAngle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Data` | `T` |  |
| `Value` | `double` |  |
| `Index` | `int` |  |
| `StartAngle` | `double` |  |
| `EndAngle` | `double` |  |
| `PadAngle` | `double` |  |

### Properties

#### `Data`

```csharp
public T Data  // get, init
```

#### `EndAngle`

```csharp
public double EndAngle  // get, init
```

#### `Index`

```csharp
public int Index  // get, init
```

#### `PadAngle`

```csharp
public double PadAngle  // get, init
```

#### `StartAngle`

```csharp
public double StartAngle  // get, init
```

#### `Value`

```csharp
public double Value  // get, init
```

## `PieGenerator`

```csharp
public static class PieGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

##### `Create()`

Creates a pie generator for numeric data.

```csharp
public static PieGenerator<double> Create()
```

##### `Create<T>(Func<T, double>)`

Creates a pie generator with a custom value accessor.

```csharp
public static PieGenerator<T> Create<T>(Func<T, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Func<T, double>` |  |

#### `Generate`

One-shot convenience: computes pie arcs directly without constructing and configuring a generator instance.

```csharp
public static PieArc<T>[] Generate<T>(IReadOnlyList<T> data, Func<T, double> value, bool sort = true, double padAngle = 0)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |  |
| `value` | `Func<T, double>` |  |  |
| `sort` | `bool` | `true` |  |
| `padAngle` | `double` | `0` |  |

## `PieGenerator<T>`

Computes arc angles from data values, suitable for feeding into an ArcGenerator. Direct port of d3.pie().

```csharp
public sealed class PieGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `PieGenerator`

```csharp
public PieGenerator(Func<T, int, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Func<T, int, double>` |  |

### Methods

#### `Generate`

Computes pie arc descriptors for the given data.

```csharp
public PieArc<T>[] Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetEndAngle`

```csharp
public PieGenerator<T> SetEndAngle(double angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `double` |  |

#### `SetPadAngle`

```csharp
public PieGenerator<T> SetPadAngle(double angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `double` |  |

#### `SetSort`

```csharp
public PieGenerator<T> SetSort(Comparison<T>? sort)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `sort` | `Comparison<T>?` |  |

#### `SetSortValues`

```csharp
public PieGenerator<T> SetSortValues(Comparison<double>? sortValues)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `sortValues` | `Comparison<double>?` |  |

#### `SetStartAngle`

```csharp
public PieGenerator<T> SetStartAngle(double angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `double` |  |

#### `SetValue`

```csharp
public PieGenerator<T> SetValue(Func<T, int, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Func<T, int, double>` |  |

## `RadialAreaGenerator`

```csharp
public static class RadialAreaGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

Creates a radial area generator with custom accessors.

```csharp
public static RadialAreaGenerator<T> Create<T>(Func<T, double> angle, Func<T, double> innerRadius, Func<T, double> outerRadius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, double>` |  |
| `innerRadius` | `Func<T, double>` |  |
| `outerRadius` | `Func<T, double>` |  |

## `RadialAreaGenerator<T>`

Generates SVG path data for a radial area (polar coordinates). Direct port of d3.areaRadial(). Maps angle → x, innerRadius/outerRadius → y.

```csharp
public sealed class RadialAreaGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `RadialAreaGenerator`

```csharp
public RadialAreaGenerator(Func<T, int, double> angle, Func<T, int, double> innerRadius, Func<T, int, double> outerRadius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, int, double>` |  |
| `innerRadius` | `Func<T, int, double>` |  |
| `outerRadius` | `Func<T, int, double>` |  |

### Methods

#### `Generate`

Generates the SVG path string for the given data in polar coordinates.

```csharp
public string? Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetAngle`

```csharp
public RadialAreaGenerator<T> SetAngle(Func<T, int, double> angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, int, double>` |  |

#### `SetDefined`

```csharp
public RadialAreaGenerator<T> SetDefined(Func<T, int, bool> defined)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `defined` | `Func<T, int, bool>` |  |

#### `SetDigits`

```csharp
public RadialAreaGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetEndAngle`

```csharp
public RadialAreaGenerator<T> SetEndAngle(Func<T, int, double>? angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, int, double>?` |  |

#### `SetInnerRadius`

```csharp
public RadialAreaGenerator<T> SetInnerRadius(Func<T, int, double> r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `r` | `Func<T, int, double>` |  |

#### `SetOuterRadius`

```csharp
public RadialAreaGenerator<T> SetOuterRadius(Func<T, int, double> r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `r` | `Func<T, int, double>` |  |

#### `SetStartAngle`

```csharp
public RadialAreaGenerator<T> SetStartAngle(Func<T, int, double> angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, int, double>` |  |

## `RadialLineGenerator`

```csharp
public static class RadialLineGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

##### `Create()`

Creates a radial line generator for (angle, radius) tuple data.

```csharp
public static RadialLineGenerator<(double angle, double radius)> Create()
```

##### `Create<T>(Func<T, double>, Func<T, double>)`

Creates a radial line generator with custom accessors.

```csharp
public static RadialLineGenerator<T> Create<T>(Func<T, double> angle, Func<T, double> radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, double>` |  |
| `radius` | `Func<T, double>` |  |

## `RadialLineGenerator<T>`

Generates SVG path data for a radial line (polar coordinates). Direct port of d3.lineRadial(). Maps angle → x, radius → y.

```csharp
public sealed class RadialLineGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `RadialLineGenerator`

```csharp
public RadialLineGenerator(Func<T, int, double> angle, Func<T, int, double> radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, int, double>` |  |
| `radius` | `Func<T, int, double>` |  |

### Methods

#### `Generate`

Generates the SVG path string for the given data in polar coordinates.

```csharp
public string? Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetAngle`

```csharp
public RadialLineGenerator<T> SetAngle(Func<T, int, double> angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, int, double>` |  |

#### `SetCurve`

```csharp
public RadialLineGenerator<T> SetCurve(CurveFactory? curve)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `curve` | [`CurveFactory?`](#curvefactory) |  |

#### `SetDefined`

```csharp
public RadialLineGenerator<T> SetDefined(Func<T, int, bool> defined)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `defined` | `Func<T, int, bool>` |  |

#### `SetDigits`

```csharp
public RadialLineGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetRadius`

```csharp
public RadialLineGenerator<T> SetRadius(Func<T, int, double> radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `radius` | `Func<T, int, double>` |  |

## `RadialLinkGenerator`

```csharp
public static class RadialLinkGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

Creates a radial link generator with custom accessors.

```csharp
public static RadialLinkGenerator<(TNode source, TNode target)> Create<TNode>(Func<TNode, double> angle, Func<TNode, double> radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<TNode, double>` |  |
| `radius` | `Func<TNode, double>` |  |

## `RadialLinkGenerator<T>`

Generates SVG path data for a radial link (polar coordinates). Direct port of d3.linkRadial(). Useful for radial tree/dendrogram edges.

```csharp
public sealed class RadialLinkGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `RadialLinkGenerator`

```csharp
public RadialLinkGenerator(Func<T, (double angle, double radius)> source, Func<T, (double angle, double radius)> target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `source` | `Func<T, (double angle, double radius)>` |  |
| `target` | `Func<T, (double angle, double radius)>` |  |

### Methods

#### `Generate`

Generates the SVG path string for the given link data.

```csharp
public string? Generate(T datum)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `datum` | `T` |  |

#### `SetDigits`

```csharp
public RadialLinkGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetSource`

```csharp
public RadialLinkGenerator<T> SetSource(Func<T, (double angle, double radius)> source)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `source` | `Func<T, (double angle, double radius)>` |  |

#### `SetTarget`

```csharp
public RadialLinkGenerator<T> SetTarget(Func<T, (double angle, double radius)> target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | `Func<T, (double angle, double radius)>` |  |

## `StackGenerator`

```csharp
public static class StackGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

```csharp
public static StackGenerator<T> Create<T>()
```

## `StackGenerator<T>`

Computes a stacked layout from tabular data. Direct port of d3.stack().

```csharp
public sealed class StackGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `StackGenerator`

```csharp
public StackGenerator()
```

### Methods

#### `Generate`

Computes the stacked series from the given data.

```csharp
public StackSeries[] Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetKeys`

```csharp
public StackGenerator<T> SetKeys(params string[] keys)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `keys` | `string[]` |  |

#### `SetValue`

```csharp
public StackGenerator<T> SetValue(Func<T, string, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Func<T, string, double>` |  |

## `StackPoint`

```csharp
public record struct StackPoint(double Y0, double Y1)
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `StackPoint`

```csharp
public StackPoint(double Y0, double Y1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Y0` | `double` |  |
| `Y1` | `double` |  |

### Properties

#### `Y0`

```csharp
public double Y0  // get, set
```

#### `Y1`

```csharp
public double Y1  // get, set
```

## `StackSeries`

```csharp
public sealed class StackSeries
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `StackSeries`

```csharp
public StackSeries()
```

### Properties

#### `Key`

```csharp
public required string Key  // get, init
```

#### `Points`

```csharp
public required StackPoint[] Points  // get, set
```

## `SymbolGenerator`

```csharp
public static class SymbolGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

##### `Create()`

Creates a symbol generator with default circle type and size 64.

```csharp
public static SymbolGenerator<object?> Create()
```

##### `Create<T>(ISymbolType, double)`

Creates a symbol generator with the given type and size.

```csharp
public static SymbolGenerator<T> Create<T>(ISymbolType type, double size = 64)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `type` | [`ISymbolType`](#isymboltype) |  |  |
| `size` | `double` | `64` |  |

## `SymbolGenerator<T>`

Generates SVG path data for symbols (markers) at data points. Direct port of d3.symbol().

```csharp
public sealed class SymbolGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `SymbolGenerator`

```csharp
public SymbolGenerator(Func<T, int, ISymbolType> type, Func<T, int, double> size)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `type` | `Func<T, int, ISymbolType>` |  |
| `size` | `Func<T, int, double>` |  |

### Methods

#### `Generate`

Generates the SVG path string for a symbol.

```csharp
public string? Generate(T datum, int index = 0)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `datum` | `T` |  |  |
| `index` | `int` | `0` |  |

#### `SetDigits`

```csharp
public SymbolGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetSize`

```csharp
public SymbolGenerator<T> SetSize(double size)
public SymbolGenerator<T> SetSize(Func<T, int, double> size)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `size` | `double` or `Func<T, int, double>` |  |

#### `SetType`

```csharp
public SymbolGenerator<T> SetType(ISymbolType type)
public SymbolGenerator<T> SetType(Func<T, int, ISymbolType> type)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `type` | [`ISymbolType`](#isymboltype) or `Func<T, int, ISymbolType>` |  |
