---
title: "D3: shapes"
description: "Line, area, arc, pie, stack, link and symbol generators, curves and the path builder."
---

**Learn more:** [Charting](/microsoft-ui-reactor/main/controls/charting)

## `ArcGenerator` [#arcgenerator]

Generates SVG path data for circular or annular arcs. Direct port of d3.arc(). Works with PieArc data from PieGenerator.

```csharp
public sealed class ArcGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors]

#### `ArcGenerator` [#arcgenerator_1]

```csharp
public ArcGenerator()
```

### Methods [#methods]

#### `Centroid` [#centroid]

##### `Centroid(double, double, double, double)` [#centroiddouble-double-double-double]

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

##### `Centroid(double, double, double?, double?)` [#centroiddouble-double-double-double_1]

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

#### `Generate` [#generate]

##### `Generate<T>(PieArc<T>)` [#generatetpiearct]

Generates the SVG path string for the given arc descriptor.

```csharp
public string? Generate<T>(PieArc<T> arc)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `arc` | [`PieArc<T>`](#piearct) |  |

##### `Generate(double, double, double, double?, double?)` [#generatedouble-double-double-double-double]

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

#### `SetCornerRadius` [#setcornerradius]

```csharp
public ArcGenerator SetCornerRadius(double r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `r` | `double` |  |

#### `SetDigits` [#setdigits]

```csharp
public ArcGenerator SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetInnerRadius` [#setinnerradius]

```csharp
public ArcGenerator SetInnerRadius(double r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `r` | `double` |  |

#### `SetOuterRadius` [#setouterradius]

```csharp
public ArcGenerator SetOuterRadius(double r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `r` | `double` |  |

## `AreaGenerator` [#areagenerator]

```csharp
public static class AreaGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_1]

#### `Create` [#create]

##### `Create<T>(Func<T, double>, Func<T, double>)` [#createtfunct-double-funct-double]

Creates an area generator with baseline at 0.

```csharp
public static AreaGenerator<T> Create<T>(Func<T, double> x, Func<T, double> y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<T, double>` |  |
| `y` | `Func<T, double>` |  |

##### `Create<T>(Func<T, double>, Func<T, double>, Func<T, double>)` [#createtfunct-double-funct-double-funct-double]

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

#### `FromArrays` [#fromarrays]

Creates an area generator for double\[\] data where \[0\]=x, \[1\]=y.

```csharp
public static AreaGenerator<double[]> FromArrays()
```

## `AreaGenerator<T>` [#areageneratort]

Generates SVG path data for an area shape from an array of data points. Direct port of d3.area().

```csharp
public sealed class AreaGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_1]

#### `AreaGenerator` [#areagenerator_1]

```csharp
public AreaGenerator(Func<T, int, double> x0, Func<T, int, double> y0, Func<T, int, double> y1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x0` | `Func<T, int, double>` |  |
| `y0` | `Func<T, int, double>` |  |
| `y1` | `Func<T, int, double>` |  |

### Methods [#methods_2]

#### `Generate` [#generate_1]

Generates the SVG path string for the given data.

```csharp
public string? Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetDefined` [#setdefined]

```csharp
public AreaGenerator<T> SetDefined(Func<T, int, bool> defined)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `defined` | `Func<T, int, bool>` |  |

#### `SetDigits` [#setdigits_1]

```csharp
public AreaGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetX` [#setx]

```csharp
public AreaGenerator<T> SetX(Func<T, int, double> x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<T, int, double>` |  |

#### `SetX0` [#setx0]

```csharp
public AreaGenerator<T> SetX0(Func<T, int, double> x0)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x0` | `Func<T, int, double>` |  |

#### `SetX1` [#setx1]

```csharp
public AreaGenerator<T> SetX1(Func<T, int, double>? x1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x1` | `Func<T, int, double>?` |  |

#### `SetY0` [#sety0]

```csharp
public AreaGenerator<T> SetY0(Func<T, int, double> y0)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `y0` | `Func<T, int, double>` |  |

#### `SetY1` [#sety1]

```csharp
public AreaGenerator<T> SetY1(Func<T, int, double> y1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `y1` | `Func<T, int, double>` |  |

## `CurveFactory` [#curvefactory]

Curve factory that creates curve instances bound to a PathBuilder.

```csharp
public delegate ICurve CurveFactory(PathBuilder path)
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

## `D3Curve` [#d3curve]

Built-in curve implementations matching d3-shape curves.

```csharp
public static class D3Curve
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_3]

#### `CardinalWithTension` [#cardinalwithtension]

Creates a cardinal spline with the given tension \[0, 1\].

```csharp
public static CurveFactory CardinalWithTension(double tension)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `tension` | `double` |  |

#### `CatmullRomWithAlpha` [#catmullromwithalpha]

Creates a Catmull-Rom spline with the given alpha \[0, 1\].

```csharp
public static CurveFactory CatmullRomWithAlpha(double alpha)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `alpha` | `double` |  |

### Fields [#fields]

#### `Basis` [#basis]

Uniform B-spline. Port of d3.curveBasis.

```csharp
public static readonly CurveFactory Basis
```

#### `BasisClosed` [#basisclosed]

Closed uniform B-spline. Port of d3.curveBasisClosed.

```csharp
public static readonly CurveFactory BasisClosed
```

#### `Cardinal` [#cardinal]

Cardinal spline with default tension. Port of d3.curveCardinal.

```csharp
public static readonly CurveFactory Cardinal
```

#### `CatmullRom` [#catmullrom]

Catmull-Rom spline with default alpha. Port of d3.curveCatmullRom.

```csharp
public static readonly CurveFactory CatmullRom
```

#### `Linear` [#linear]

Straight line segments between points. Port of d3.curveLinear.

```csharp
public static readonly CurveFactory Linear
```

#### `MonotoneX` [#monotonex]

Monotone cubic interpolation in x. Port of d3.curveMonotoneX.

```csharp
public static readonly CurveFactory MonotoneX
```

#### `Natural` [#natural]

Natural cubic spline. Port of d3.curveNatural.

```csharp
public static readonly CurveFactory Natural
```

#### `Step` [#step]

Step function (midpoint). Port of d3.curveStep.

```csharp
public static readonly CurveFactory Step
```

#### `StepAfter` [#stepafter]

Step function (change at end). Port of d3.curveStepAfter.

```csharp
public static readonly CurveFactory StepAfter
```

#### `StepBefore` [#stepbefore]

Step function (change at start). Port of d3.curveStepBefore.

```csharp
public static readonly CurveFactory StepBefore
```

## `D3Symbol` [#d3symbol]

Built-in symbol types matching d3-shape.

```csharp
public static class D3Symbol
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Fields [#fields_1]

#### `All` [#all]

All built-in symbol types.

```csharp
public static readonly ISymbolType[] All
```

#### `Circle` [#circle]

```csharp
public static readonly ISymbolType Circle
```

#### `Cross` [#cross]

```csharp
public static readonly ISymbolType Cross
```

#### `Diamond` [#diamond]

```csharp
public static readonly ISymbolType Diamond
```

#### `Square` [#square]

```csharp
public static readonly ISymbolType Square
```

#### `Star` [#star]

```csharp
public static readonly ISymbolType Star
```

#### `Triangle` [#triangle]

```csharp
public static readonly ISymbolType Triangle
```

#### `Wye` [#wye]

```csharp
public static readonly ISymbolType Wye
```

## `ICurve` [#icurve]

Curves transform a sequence of points into a path. This is the interface all curve implementations share.

```csharp
public interface ICurve
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_4]

#### `LineEnd` [#lineend]

```csharp
public void LineEnd()
```

#### `LineStart` [#linestart]

```csharp
public void LineStart()
```

#### `Point` [#point]

```csharp
public void Point(double x, double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |
| `y` | `double` |  |

## `ISymbolType` [#isymboltype]

Defines a symbol shape type (circle, cross, diamond, square, star, triangle, wye).

```csharp
public interface ISymbolType
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_5]

#### `Draw` [#draw]

```csharp
public void Draw(PathBuilder path, double size)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `path` | [`PathBuilder`](#pathbuilder) |  |
| `size` | `double` |  |

## `LineGenerator` [#linegenerator]

Factory for creating line generators with common data types.

```csharp
public static class LineGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_6]

#### `Create` [#create_1]

##### `Create()` [#create_2]

Creates a line generator for (x, y) tuple data.

```csharp
public static LineGenerator<(double x, double y)> Create()
```

##### `Create<T>(Func<T, double>, Func<T, double>)` [#createtfunct-double-funct-double_1]

Creates a line generator with custom accessors.

```csharp
public static LineGenerator<T> Create<T>(Func<T, double> x, Func<T, double> y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<T, double>` |  |
| `y` | `Func<T, double>` |  |

#### `FromArrays` [#fromarrays_1]

Creates a line generator for double\[\] data where \[0\]=x, \[1\]=y.

```csharp
public static LineGenerator<double[]> FromArrays()
```

## `LineGenerator<T>` [#linegeneratort]

Generates SVG path data for a line from an array of data points. Direct port of d3.line().

```csharp
public sealed class LineGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_2]

#### `LineGenerator` [#linegenerator_1]

```csharp
public LineGenerator(Func<T, int, double> x, Func<T, int, double> y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<T, int, double>` |  |
| `y` | `Func<T, int, double>` |  |

### Properties [#properties]

#### `Digits` [#digits]

```csharp
public int? Digits  // get
```

### Methods [#methods_7]

#### `Generate` [#generate_2]

Generates the SVG path string for the given data.

```csharp
public string? Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetCurve` [#setcurve]

```csharp
public LineGenerator<T> SetCurve(CurveFactory? curve)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `curve` | [`CurveFactory?`](#curvefactory) |  |

#### `SetDefined` [#setdefined_1]

```csharp
public LineGenerator<T> SetDefined(Func<T, int, bool> defined)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `defined` | `Func<T, int, bool>` |  |

#### `SetDigits` [#setdigits_2]

```csharp
public LineGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetX` [#setx_1]

```csharp
public LineGenerator<T> SetX(Func<T, int, double> x)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<T, int, double>` |  |

#### `SetY` [#sety]

```csharp
public LineGenerator<T> SetY(Func<T, int, double> y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `y` | `Func<T, int, double>` |  |

## `LinkGenerator` [#linkgenerator]

```csharp
public static class LinkGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_8]

#### `Horizontal` [#horizontal]

Creates a horizontal link generator (source left, target right).

```csharp
public static LinkGenerator<(TNode source, TNode target)> Horizontal<TNode>(Func<TNode, double> x, Func<TNode, double> y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<TNode, double>` |  |
| `y` | `Func<TNode, double>` |  |

#### `Vertical` [#vertical]

Creates a vertical link generator (source top, target bottom).

```csharp
public static LinkGenerator<(TNode source, TNode target)> Vertical<TNode>(Func<TNode, double> x, Func<TNode, double> y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `Func<TNode, double>` |  |
| `y` | `Func<TNode, double>` |  |

## `LinkGenerator<T>` [#linkgeneratort]

Generates SVG path data for smooth links/edges between two points. Direct port of d3.link(), d3.linkHorizontal(), d3.linkVertical().

```csharp
public sealed class LinkGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_3]

#### `LinkGenerator` [#linkgenerator_1]

```csharp
public LinkGenerator(Func<T, (double x, double y)> source, Func<T, (double x, double y)> target, LinkOrientation orientation = LinkOrientation.Vertical)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `source` | `Func<T, (double x, double y)>` |  |  |
| `target` | `Func<T, (double x, double y)>` |  |  |
| `orientation` | [`LinkOrientation`](#linkorientation) | `LinkOrientation.Vertical` |  |

### Methods [#methods_9]

#### `Generate` [#generate_3]

Generates the SVG path string for the given link data.

```csharp
public string? Generate(T datum)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `datum` | `T` |  |

#### `SetDigits` [#setdigits_3]

```csharp
public LinkGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetSource` [#setsource]

```csharp
public LinkGenerator<T> SetSource(Func<T, (double x, double y)> source)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `source` | `Func<T, (double x, double y)>` |  |

#### `SetTarget` [#settarget]

```csharp
public LinkGenerator<T> SetTarget(Func<T, (double x, double y)> target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | `Func<T, (double x, double y)>` |  |

## `LinkOrientation` [#linkorientation]

```csharp
public enum LinkOrientation
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Horizontal` | `0` |  |
| `Vertical` | `1` |  |

## `PathBuilder` [#pathbuilder]

Builds SVG path data strings. Direct port of d3-path's Path class. This is the fundamental rendering primitive — all D3 shapes produce path strings through this builder.

```csharp
public sealed class PathBuilder
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_4]

#### `PathBuilder` [#pathbuilder_1]

```csharp
public PathBuilder(int? digits = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `digits` | `int?` | `null` |  |

### Methods [#methods_10]

#### `Arc` [#arc]

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

#### `ArcTo` [#arcto]

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

#### `BezierCurveTo` [#beziercurveto]

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

#### `ClosePath` [#closepath]

```csharp
public PathBuilder ClosePath()
```

#### `Create` [#create_3]

Creates a new PathBuilder with default (no rounding) settings.

```csharp
public static PathBuilder Create()
```

#### `CreateRound` [#createround]

Creates a new PathBuilder that rounds to the given number of digits.

```csharp
public static PathBuilder CreateRound(int digits = 3)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `digits` | `int` | `3` |  |

#### `LineTo` [#lineto]

```csharp
public PathBuilder LineTo(double x, double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |
| `y` | `double` |  |

#### `MoveTo` [#moveto]

```csharp
public PathBuilder MoveTo(double x, double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |
| `y` | `double` |  |

#### `QuadraticCurveTo` [#quadraticcurveto]

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

#### `Rect` [#rect]

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

#### `ToString` [#tostring]

Returns a string that represents this object.

```csharp
public override string ToString()
```

## `PieArc<T>` [#piearct]

Describes a single arc slice computed by PieGenerator.

```csharp
public readonly record struct PieArc<T>(T Data, double Value, int Index, double StartAngle, double EndAngle, double PadAngle)
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_5]

#### `PieArc` [#piearc]

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

### Properties [#properties_1]

#### `Data` [#data]

```csharp
public T Data  // get, init
```

#### `EndAngle` [#endangle]

```csharp
public double EndAngle  // get, init
```

#### `Index` [#index]

```csharp
public int Index  // get, init
```

#### `PadAngle` [#padangle]

```csharp
public double PadAngle  // get, init
```

#### `StartAngle` [#startangle]

```csharp
public double StartAngle  // get, init
```

#### `Value` [#value]

```csharp
public double Value  // get, init
```

## `PieGenerator` [#piegenerator]

```csharp
public static class PieGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_11]

#### `Create` [#create_4]

##### `Create()` [#create_5]

Creates a pie generator for numeric data.

```csharp
public static PieGenerator<double> Create()
```

##### `Create<T>(Func<T, double>)` [#createtfunct-double]

Creates a pie generator with a custom value accessor.

```csharp
public static PieGenerator<T> Create<T>(Func<T, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Func<T, double>` |  |

#### `Generate` [#generate_4]

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

## `PieGenerator<T>` [#piegeneratort]

Computes arc angles from data values, suitable for feeding into an ArcGenerator. Direct port of d3.pie().

```csharp
public sealed class PieGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_6]

#### `PieGenerator` [#piegenerator_1]

```csharp
public PieGenerator(Func<T, int, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Func<T, int, double>` |  |

### Methods [#methods_12]

#### `Generate` [#generate_5]

Computes pie arc descriptors for the given data.

```csharp
public PieArc<T>[] Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetEndAngle` [#setendangle]

```csharp
public PieGenerator<T> SetEndAngle(double angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `double` |  |

#### `SetPadAngle` [#setpadangle]

```csharp
public PieGenerator<T> SetPadAngle(double angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `double` |  |

#### `SetSort` [#setsort]

```csharp
public PieGenerator<T> SetSort(Comparison<T>? sort)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `sort` | `Comparison<T>?` |  |

#### `SetSortValues` [#setsortvalues]

```csharp
public PieGenerator<T> SetSortValues(Comparison<double>? sortValues)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `sortValues` | `Comparison<double>?` |  |

#### `SetStartAngle` [#setstartangle]

```csharp
public PieGenerator<T> SetStartAngle(double angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `double` |  |

#### `SetValue` [#setvalue]

```csharp
public PieGenerator<T> SetValue(Func<T, int, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Func<T, int, double>` |  |

## `RadialAreaGenerator` [#radialareagenerator]

```csharp
public static class RadialAreaGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_13]

#### `Create` [#create_6]

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

## `RadialAreaGenerator<T>` [#radialareageneratort]

Generates SVG path data for a radial area (polar coordinates). Direct port of d3.areaRadial(). Maps angle → x, innerRadius/outerRadius → y.

```csharp
public sealed class RadialAreaGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_7]

#### `RadialAreaGenerator` [#radialareagenerator_1]

```csharp
public RadialAreaGenerator(Func<T, int, double> angle, Func<T, int, double> innerRadius, Func<T, int, double> outerRadius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, int, double>` |  |
| `innerRadius` | `Func<T, int, double>` |  |
| `outerRadius` | `Func<T, int, double>` |  |

### Methods [#methods_14]

#### `Generate` [#generate_6]

Generates the SVG path string for the given data in polar coordinates.

```csharp
public string? Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetAngle` [#setangle]

```csharp
public RadialAreaGenerator<T> SetAngle(Func<T, int, double> angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, int, double>` |  |

#### `SetDefined` [#setdefined_2]

```csharp
public RadialAreaGenerator<T> SetDefined(Func<T, int, bool> defined)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `defined` | `Func<T, int, bool>` |  |

#### `SetDigits` [#setdigits_4]

```csharp
public RadialAreaGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetEndAngle` [#setendangle_1]

```csharp
public RadialAreaGenerator<T> SetEndAngle(Func<T, int, double>? angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, int, double>?` |  |

#### `SetInnerRadius` [#setinnerradius_1]

```csharp
public RadialAreaGenerator<T> SetInnerRadius(Func<T, int, double> r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `r` | `Func<T, int, double>` |  |

#### `SetOuterRadius` [#setouterradius_1]

```csharp
public RadialAreaGenerator<T> SetOuterRadius(Func<T, int, double> r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `r` | `Func<T, int, double>` |  |

#### `SetStartAngle` [#setstartangle_1]

```csharp
public RadialAreaGenerator<T> SetStartAngle(Func<T, int, double> angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, int, double>` |  |

## `RadialLineGenerator` [#radiallinegenerator]

```csharp
public static class RadialLineGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_15]

#### `Create` [#create_7]

##### `Create()` [#create_8]

Creates a radial line generator for (angle, radius) tuple data.

```csharp
public static RadialLineGenerator<(double angle, double radius)> Create()
```

##### `Create<T>(Func<T, double>, Func<T, double>)` [#createtfunct-double-funct-double_2]

Creates a radial line generator with custom accessors.

```csharp
public static RadialLineGenerator<T> Create<T>(Func<T, double> angle, Func<T, double> radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, double>` |  |
| `radius` | `Func<T, double>` |  |

## `RadialLineGenerator<T>` [#radiallinegeneratort]

Generates SVG path data for a radial line (polar coordinates). Direct port of d3.lineRadial(). Maps angle → x, radius → y.

```csharp
public sealed class RadialLineGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_8]

#### `RadialLineGenerator` [#radiallinegenerator_1]

```csharp
public RadialLineGenerator(Func<T, int, double> angle, Func<T, int, double> radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, int, double>` |  |
| `radius` | `Func<T, int, double>` |  |

### Methods [#methods_16]

#### `Generate` [#generate_7]

Generates the SVG path string for the given data in polar coordinates.

```csharp
public string? Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetAngle` [#setangle_1]

```csharp
public RadialLineGenerator<T> SetAngle(Func<T, int, double> angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<T, int, double>` |  |

#### `SetCurve` [#setcurve_1]

```csharp
public RadialLineGenerator<T> SetCurve(CurveFactory? curve)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `curve` | [`CurveFactory?`](#curvefactory) |  |

#### `SetDefined` [#setdefined_3]

```csharp
public RadialLineGenerator<T> SetDefined(Func<T, int, bool> defined)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `defined` | `Func<T, int, bool>` |  |

#### `SetDigits` [#setdigits_5]

```csharp
public RadialLineGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetRadius` [#setradius]

```csharp
public RadialLineGenerator<T> SetRadius(Func<T, int, double> radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `radius` | `Func<T, int, double>` |  |

## `RadialLinkGenerator` [#radiallinkgenerator]

```csharp
public static class RadialLinkGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_17]

#### `Create` [#create_9]

Creates a radial link generator with custom accessors.

```csharp
public static RadialLinkGenerator<(TNode source, TNode target)> Create<TNode>(Func<TNode, double> angle, Func<TNode, double> radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `Func<TNode, double>` |  |
| `radius` | `Func<TNode, double>` |  |

## `RadialLinkGenerator<T>` [#radiallinkgeneratort]

Generates SVG path data for a radial link (polar coordinates). Direct port of d3.linkRadial(). Useful for radial tree/dendrogram edges.

```csharp
public sealed class RadialLinkGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_9]

#### `RadialLinkGenerator` [#radiallinkgenerator_1]

```csharp
public RadialLinkGenerator(Func<T, (double angle, double radius)> source, Func<T, (double angle, double radius)> target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `source` | `Func<T, (double angle, double radius)>` |  |
| `target` | `Func<T, (double angle, double radius)>` |  |

### Methods [#methods_18]

#### `Generate` [#generate_8]

Generates the SVG path string for the given link data.

```csharp
public string? Generate(T datum)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `datum` | `T` |  |

#### `SetDigits` [#setdigits_6]

```csharp
public RadialLinkGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetSource` [#setsource_1]

```csharp
public RadialLinkGenerator<T> SetSource(Func<T, (double angle, double radius)> source)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `source` | `Func<T, (double angle, double radius)>` |  |

#### `SetTarget` [#settarget_1]

```csharp
public RadialLinkGenerator<T> SetTarget(Func<T, (double angle, double radius)> target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `target` | `Func<T, (double angle, double radius)>` |  |

## `StackGenerator` [#stackgenerator]

```csharp
public static class StackGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_19]

#### `Create` [#create_10]

```csharp
public static StackGenerator<T> Create<T>()
```

## `StackGenerator<T>` [#stackgeneratort]

Computes a stacked layout from tabular data. Direct port of d3.stack().

```csharp
public sealed class StackGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_10]

#### `StackGenerator` [#stackgenerator_1]

```csharp
public StackGenerator()
```

### Methods [#methods_20]

#### `Generate` [#generate_9]

Computes the stacked series from the given data.

```csharp
public StackSeries[] Generate(IReadOnlyList<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IReadOnlyList<T>` |  |

#### `SetKeys` [#setkeys]

```csharp
public StackGenerator<T> SetKeys(params string[] keys)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `keys` | `string[]` |  |

#### `SetValue` [#setvalue_1]

```csharp
public StackGenerator<T> SetValue(Func<T, string, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `value` | `Func<T, string, double>` |  |

## `StackPoint` [#stackpoint]

```csharp
public record struct StackPoint(double Y0, double Y1)
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_11]

#### `StackPoint` [#stackpoint_1]

```csharp
public StackPoint(double Y0, double Y1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Y0` | `double` |  |
| `Y1` | `double` |  |

### Properties [#properties_2]

#### `Y0` [#y0]

```csharp
public double Y0  // get, set
```

#### `Y1` [#y1]

```csharp
public double Y1  // get, set
```

## `StackSeries` [#stackseries]

```csharp
public sealed class StackSeries
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_12]

#### `StackSeries` [#stackseries_1]

```csharp
public StackSeries()
```

### Properties [#properties_3]

#### `Key` [#key]

```csharp
public required string Key  // get, init
```

#### `Points` [#points]

```csharp
public required StackPoint[] Points  // get, set
```

## `SymbolGenerator` [#symbolgenerator]

```csharp
public static class SymbolGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_21]

#### `Create` [#create_11]

##### `Create()` [#create_12]

Creates a symbol generator with default circle type and size 64.

```csharp
public static SymbolGenerator<object?> Create()
```

##### `Create<T>(ISymbolType, double)` [#createtisymboltype-double]

Creates a symbol generator with the given type and size.

```csharp
public static SymbolGenerator<T> Create<T>(ISymbolType type, double size = 64)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `type` | [`ISymbolType`](#isymboltype) |  |  |
| `size` | `double` | `64` |  |

## `SymbolGenerator<T>` [#symbolgeneratort]

Generates SVG path data for symbols (markers) at data points. Direct port of d3.symbol().

```csharp
public sealed class SymbolGenerator<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_13]

#### `SymbolGenerator` [#symbolgenerator_1]

```csharp
public SymbolGenerator(Func<T, int, ISymbolType> type, Func<T, int, double> size)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `type` | `Func<T, int, ISymbolType>` |  |
| `size` | `Func<T, int, double>` |  |

### Methods [#methods_22]

#### `Generate` [#generate_10]

Generates the SVG path string for a symbol.

```csharp
public string? Generate(T datum, int index = 0)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `datum` | `T` |  |  |
| `index` | `int` | `0` |  |

#### `SetDigits` [#setdigits_7]

```csharp
public SymbolGenerator<T> SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetSize` [#setsize]

```csharp
public SymbolGenerator<T> SetSize(double size)
public SymbolGenerator<T> SetSize(Func<T, int, double> size)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `size` | `double` or `Func<T, int, double>` |  |

#### `SetType` [#settype]

```csharp
public SymbolGenerator<T> SetType(ISymbolType type)
public SymbolGenerator<T> SetType(Func<T, int, ISymbolType> type)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `type` | [`ISymbolType`](#isymboltype) or `Func<T, int, ISymbolType>` |  |
