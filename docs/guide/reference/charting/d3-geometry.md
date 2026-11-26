# D3: contours, Delaunay and polygons

Contour generators, Delaunay triangulation, Voronoi diagrams and polygon helpers.

**Learn more:** [Charting](../../charting.md)

## `ContourGenerator`

Computes contour polygons using marching squares. Direct port of d3.contours().

```csharp
public sealed class ContourGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `ContourGenerator`

```csharp
public ContourGenerator(int width, int height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `int` |  |
| `height` | `int` |  |

### Methods

#### `Generate`

Computes contour polygons for the given grid of values. Values is a flat array of length width \* height in row-major order.

```csharp
public ContourMultiPolygon[] Generate(double[] values)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `values` | `double[]` |  |

#### `SetThresholdCount`

```csharp
public ContourGenerator SetThresholdCount(int count)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `count` | `int` |  |

#### `SetThresholds`

```csharp
public ContourGenerator SetThresholds(params double[] thresholds)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `thresholds` | `double[]` |  |

## `ContourMultiPolygon`

A multi-polygon contour at a specific threshold value.

```csharp
public sealed class ContourMultiPolygon
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `ContourMultiPolygon`

```csharp
public ContourMultiPolygon()
```

### Properties

#### `Coordinates`

```csharp
public required List<List<(double x, double y)>> Coordinates  // get, init
```

#### `Value`

```csharp
public required double Value  // get, init
```

## `D3Polygon`

Geometric operations on 2D polygons. Direct port of d3-polygon.

```csharp
public static class D3Polygon
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Area`

Returns the signed area of the given polygon. Positive if vertices are in counter-clockwise order. Port of d3.polygonArea().

```csharp
public static double Area(IReadOnlyList<(double x, double y)> polygon)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `polygon` | `IReadOnlyList<(double x, double y)>` |  |

#### `Centroid`

Returns the centroid of the given polygon. Port of d3.polygonCentroid().

```csharp
public static (double x, double y) Centroid(IReadOnlyList<(double x, double y)> polygon)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `polygon` | `IReadOnlyList<(double x, double y)>` |  |

#### `Contains`

Returns true if the given point is inside the polygon. Port of d3.polygonContains().

```csharp
public static bool Contains(IReadOnlyList<(double x, double y)> polygon, (double x, double y) point)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `polygon` | `IReadOnlyList<(double x, double y)>` |  |
| `point` | `(double x, double y)` |  |

#### `Hull`

Returns the convex hull of the given points using Andrew's monotone chain algorithm. Port of d3.polygonHull().

```csharp
public static (double x, double y)[]? Hull(IReadOnlyList<(double x, double y)> points)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `points` | `IReadOnlyList<(double x, double y)>` |  |

#### `Length`

Returns the length (perimeter) of the given polygon. Port of d3.polygonLength().

```csharp
public static double Length(IReadOnlyList<(double x, double y)> polygon)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `polygon` | `IReadOnlyList<(double x, double y)>` |  |

## `Delaunay`

Computes the Delaunay triangulation for a set of 2D points. Direct port of d3.Delaunay.from().

```csharp
public sealed class Delaunay
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Properties

#### `Halfedges`

Half-edge indices. For each half-edge i, halfedges\[i\] is the opposite half-edge, or -1 if on the convex hull.

```csharp
public int[] Halfedges  // get
```

#### `Hull`

Convex hull as an ordered list of point indices.

```csharp
public int[] Hull  // get
```

#### `Points`

The input points.

```csharp
public (double x, double y)[] Points  // get
```

#### `Triangles`

Triangle indices. Every three consecutive values form a triangle. triangles\[i\*3\], triangles\[i\*3+1\], triangles\[i\*3+2\] are point indices for triangle i.

```csharp
public int[] Triangles  // get
```

### Methods

#### `Find`

Returns the index of the point closest to (x, y).

```csharp
public int Find(double x, double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |
| `y` | `double` |  |

#### `From`

```csharp
public static Delaunay From(IReadOnlyList<(double x, double y)> points)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `points` | `IReadOnlyList<(double x, double y)>` |  |

#### `Neighbors`

Returns the neighbors of the given point index.

```csharp
public IEnumerable<int> Neighbors(int index)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |

#### `Voronoi`

Computes the Voronoi diagram for this triangulation within the given bounds.

```csharp
public Voronoi Voronoi(double xmin = 0, double ymin = 0, double xmax = 960, double ymax = 500)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `xmin` | `double` | `0` |  |
| `ymin` | `double` | `0` |  |
| `xmax` | `double` | `960` |  |
| `ymax` | `double` | `500` |  |

### Fields

#### `MaxPointsForDelaunay`

Computes the Delaunay triangulation for the given points. Uses the Bowyer-Watson incremental insertion algorithm.

```csharp
public const int MaxPointsForDelaunay = 5000
```

## `DensityContourGenerator`

Computes density contours for scattered 2D point data. Direct port of d3.contourDensity().

```csharp
public sealed class DensityContourGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `DensityContourGenerator`

```csharp
public DensityContourGenerator()
```

### Methods

#### `Generate`

Computes density contours for the given points.

```csharp
public ContourMultiPolygon[] Generate(IReadOnlyList<(double x, double y)> points)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `points` | `IReadOnlyList<(double x, double y)>` |  |

#### `SetBandwidth`

```csharp
public DensityContourGenerator SetBandwidth(double bandwidth)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `bandwidth` | `double` |  |

#### `SetCellSize`

```csharp
public DensityContourGenerator SetCellSize(int size)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `size` | `int` |  |

#### `SetSize`

```csharp
public DensityContourGenerator SetSize(int width, int height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `int` |  |
| `height` | `int` |  |

#### `SetThresholdCount`

```csharp
public DensityContourGenerator SetThresholdCount(int count)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `count` | `int` |  |

#### `SetThresholds`

```csharp
public DensityContourGenerator SetThresholds(params double[] thresholds)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `thresholds` | `double[]` |  |

## `Voronoi`

Computes the Voronoi diagram from a Delaunay triangulation. Direct port of d3.Delaunay.voronoi().

```csharp
public sealed class Voronoi
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Properties

#### `Circumcenters`

The circumcenters of each Delaunay triangle — these are the Voronoi vertices.

```csharp
public (double x, double y)[] Circumcenters  // get
```

### Methods

#### `CellPolygon`

Returns the Voronoi cell polygon for the given point index, clipped to the bounding box.

```csharp
public (double x, double y)[]? CellPolygon(int index)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |

#### `Contains`

Returns true if the given point is inside the Voronoi cell of point index.

```csharp
public bool Contains(int index, double x, double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |
| `x` | `double` |  |
| `y` | `double` |  |
