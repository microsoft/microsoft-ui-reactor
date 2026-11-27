# D3: layouts

Hierarchy, treemap, pack, partition, chord, Sankey and force layouts.

**Learn more:** [Charting](../../charting.md)

## `ChordArc`

```csharp
public record struct ChordArc
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Properties

#### `Source`

```csharp
public ChordEnd Source  // get, init
```

#### `Target`

```csharp
public ChordEnd Target  // get, init
```

## `ChordData`

```csharp
public record struct ChordData
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Properties

#### `Chords`

```csharp
public ChordArc[] Chords  // get, init
```

#### `Groups`

```csharp
public ChordGroup[] Groups  // get, init
```

## `ChordEnd`

```csharp
public record struct ChordEnd
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Properties

#### `EndAngle`

```csharp
public double EndAngle  // get, init
```

#### `Index`

```csharp
public int Index  // get, init
```

#### `StartAngle`

```csharp
public double StartAngle  // get, init
```

#### `Value`

```csharp
public double Value  // get, init
```

## `ChordGroup`

```csharp
public record struct ChordGroup
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Properties

#### `EndAngle`

```csharp
public double EndAngle  // get, init
```

#### `Index`

```csharp
public int Index  // get, init
```

#### `StartAngle`

```csharp
public double StartAngle  // get, init
```

#### `Value`

```csharp
public double Value  // get, init
```

## `ChordLayout`

Computes chord layout from a square matrix of flows. Direct port of d3.chord().

```csharp
public sealed class ChordLayout
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `ChordLayout`

```csharp
public ChordLayout()
```

### Methods

#### `Generate`

Computes chord groups and chords from a square flow matrix. matrix\[i\]\[j\] = flow from group i to group j.

```csharp
public ChordData Generate(double[][] matrix)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `matrix` | `double[][]` |  |

#### `SetPadAngle`

```csharp
public ChordLayout SetPadAngle(double angle)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `angle` | `double` |  |

## `ClusterLayout`

```csharp
public static class ClusterLayout
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

```csharp
public static ClusterLayout<T> Create<T>()
```

## `ClusterLayout<T>`

Computes a cluster (dendrogram) layout where all leaves are at the same depth. Unlike tree layout, cluster places all leaves at the bottom, producing dendrograms with uniform leaf positioning. Direct port of d3.cluster().

```csharp
public sealed class ClusterLayout<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `ClusterLayout`

```csharp
public ClusterLayout()
```

### Methods

#### `Hierarchy`

Build a hierarchy from root data and a children accessor. Reuses TreeNode from TreeLayout for compatibility.

```csharp
public TreeNode<T> Hierarchy(T root, Func<T, IEnumerable<T>?> childrenAccessor)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | `T` |  |
| `childrenAccessor` | `Func<T, IEnumerable<T>?>` |  |

#### `Layout`

Compute the cluster layout positions for all nodes. All leaves are placed at the same y depth.

```csharp
public TreeNode<T> Layout(TreeNode<T> root)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | [`TreeNode<T>`](#treenodet) |  |

#### `Separation`

Sets a custom separation function between leaf nodes. The default is 1 for siblings, 2 for non-siblings.

```csharp
public ClusterLayout<T> Separation(Func<TreeNode<T>, TreeNode<T>, double> separation)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `separation` | `Func<TreeNode<T>, TreeNode<T>, double>` |  |

#### `Size`

```csharp
public ClusterLayout<T> Size(double width, double height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |
| `height` | `double` |  |

## `ForceLink`

An edge/link in a force-directed graph.

```csharp
public record struct ForceLink(int Source, int Target, double Strength = 1, double Distance = 50)
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `ForceLink`

An edge/link in a force-directed graph.

```csharp
public ForceLink(int Source, int Target, double Strength = 1, double Distance = 50)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Source` | `int` |  |  |
| `Target` | `int` |  |  |
| `Strength` | `double` | `1` |  |
| `Distance` | `double` | `50` |  |

### Properties

#### `Distance`

```csharp
public double Distance  // get, set
```

#### `Source`

```csharp
public int Source  // get, set
```

#### `Strength`

```csharp
public double Strength  // get, set
```

#### `Target`

```csharp
public int Target  // get, set
```

## `ForceNode`

A node in a force-directed graph with position and velocity.

```csharp
public sealed class ForceNode
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `ForceNode`

```csharp
public ForceNode()
```

### Properties

#### `Data`

```csharp
public object? Data  // get, set
```

#### `Fx`

```csharp
public double? Fx  // get, set
```

#### `Fy`

```csharp
public double? Fy  // get, set
```

#### `Index`

```csharp
public int Index  // get
```

#### `Label`

```csharp
public string? Label  // get, set
```

#### `Radius`

```csharp
public double Radius  // get, set
```

#### `Vx`

```csharp
public double Vx  // get, set
```

#### `Vy`

```csharp
public double Vy  // get, set
```

#### `X`

```csharp
public double X  // get, set
```

#### `Y`

```csharp
public double Y  // get, set
```

## `ForceSimulation`

A force-directed graph layout simulation using velocity Verlet integration. Port of d3-force's simulation with charge (many-body), link, and center forces.

```csharp
public sealed class ForceSimulation
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `ForceSimulation`

```csharp
public ForceSimulation()
```

### Properties

#### `Alpha`

```csharp
public double Alpha  // get, set
```

#### `AlphaMin`

```csharp
public double AlphaMin  // get, set
```

#### `AlphaTarget`

```csharp
public double AlphaTarget  // get, set
```

#### `Links`

```csharp
public IReadOnlyList<ForceLink> Links  // get
```

#### `Nodes`

```csharp
public IReadOnlyList<ForceNode> Nodes  // get
```

### Methods

#### `AlphaDecay`

```csharp
public ForceSimulation AlphaDecay(double decay)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `decay` | `double` |  |

#### `Center`

```csharp
public ForceSimulation Center(double x, double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |
| `y` | `double` |  |

#### `CenterStrength`

```csharp
public ForceSimulation CenterStrength(double strength)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `strength` | `double` |  |

#### `ChargeStrength`

```csharp
public ForceSimulation ChargeStrength(double strength)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `strength` | `double` |  |

#### `CollisionRadius`

```csharp
public ForceSimulation CollisionRadius(double radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `radius` | `double` |  |

#### `InitializePositions`

Initialize node positions in a phyllotaxis pattern (like d3-force).

```csharp
public ForceSimulation InitializePositions()
```

#### `LinkDistance`

```csharp
public ForceSimulation LinkDistance(double distance)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `distance` | `double` |  |

#### `LinkStrength`

```csharp
public ForceSimulation LinkStrength(double strength)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `strength` | `double` |  |

#### `Run`

Run the simulation for the given number of iterations.

```csharp
public ForceSimulation Run(int iterations = 300)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `iterations` | `int` | `300` |  |

#### `SetLinks`

```csharp
public ForceSimulation SetLinks(IEnumerable<ForceLink> links)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `links` | `IEnumerable<ForceLink>` |  |

#### `SetNodes`

```csharp
public ForceSimulation SetNodes(IEnumerable<ForceNode> nodes)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `nodes` | `IEnumerable<ForceNode>` |  |

#### `Tick`

Advance the simulation by one tick.

```csharp
public void Tick()
```

#### `VelocityDecay`

```csharp
public ForceSimulation VelocityDecay(double decay)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `decay` | `double` |  |

## `PackLayout`

```csharp
public static class PackLayout
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

```csharp
public static PackLayout<T> Create<T>()
```

## `PackLayout<T>`

A circle-packing layout. Encodes hierarchical data as nested circles. Direct port of d3.pack().

```csharp
public sealed class PackLayout<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `PackLayout`

```csharp
public PackLayout()
```

### Methods

#### `Layout`

Builds a pack node hierarchy from data with a children accessor, then sums values and computes the layout.

```csharp
public PackNode<T> Layout(T root, Func<T, IEnumerable<T>?> children, Func<T, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | `T` |  |
| `children` | `Func<T, IEnumerable<T>?>` |  |
| `value` | `Func<T, double>` |  |

#### `SetPadding`

```csharp
public PackLayout<T> SetPadding(double padding)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `padding` | `double` |  |

#### `Size`

```csharp
public PackLayout<T> Size(double radius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `radius` | `double` |  |

### Fields

#### `MaxChildrenPerPack`

Cap on per-node child count for the brute-force packer. TASK-087: the implementation is O(n⁴) per node; n ≈ 50 starts to hurt the render thread, n ≈ 1000 freezes it for billions of operations. Until the front-chain port lands (deferred), trim to this cap.

```csharp
public const int MaxChildrenPerPack = 100
```

## `PackNode<T>`

A node in a circle-packing layout.

```csharp
public sealed class PackNode<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `PackNode`

```csharp
public PackNode()
```

### Properties

#### `Children`

```csharp
public List<PackNode<T>> Children  // get
```

#### `Data`

```csharp
public required T Data  // get, init
```

#### `Depth`

```csharp
public int Depth  // get, init
```

#### `Parent`

```csharp
public PackNode<T>? Parent  // get, init
```

#### `R`

```csharp
public double R  // get
```

#### `TopAncestor`

Returns the ancestor that is a direct child of the root (for branch coloring). Returns this node if it has no parent.

```csharp
public PackNode<T> TopAncestor  // get
```

#### `Value`

```csharp
public double Value  // get
```

#### `X`

```csharp
public double X  // get
```

#### `Y`

```csharp
public double Y  // get
```

### Methods

#### `Descendants`

Returns all descendant nodes (including this one) in pre-order.

```csharp
public IEnumerable<PackNode<T>> Descendants()
```

#### `Leaves`

Returns all leaf nodes.

```csharp
public IEnumerable<PackNode<T>> Leaves()
```

## `PartitionLayout`

```csharp
public static class PartitionLayout
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

```csharp
public static PartitionLayout<T> Create<T>()
```

## `PartitionLayout<T>`

A partition layout subdivides a rectangular area by depth. Produces icicle diagrams (Cartesian) or sunburst diagrams (polar). Direct port of d3.partition().

```csharp
public sealed class PartitionLayout<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `PartitionLayout`

```csharp
public PartitionLayout()
```

### Methods

#### `Layout`

##### `Layout(PartitionNode<T>)`

Computes the layout for an already-built hierarchy.

```csharp
public PartitionNode<T> Layout(PartitionNode<T> root)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | [`PartitionNode<T>`](#partitionnodet) |  |

##### `Layout(T, Func<T, IEnumerable<T>?>, Func<T, double>)`

Builds a partition node hierarchy from data, sums values, and computes the layout.

```csharp
public PartitionNode<T> Layout(T root, Func<T, IEnumerable<T>?> children, Func<T, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | `T` |  |
| `children` | `Func<T, IEnumerable<T>?>` |  |
| `value` | `Func<T, double>` |  |

#### `SetPadding`

```csharp
public PartitionLayout<T> SetPadding(double p)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `p` | `double` |  |

#### `SetRound`

```csharp
public PartitionLayout<T> SetRound(bool round)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `round` | `bool` |  |

#### `Size`

```csharp
public PartitionLayout<T> Size(double x1, double y1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x1` | `double` |  |
| `y1` | `double` |  |

## `PartitionNode<T>`

A node in a partition layout with rectangular bounds.

```csharp
public sealed class PartitionNode<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `PartitionNode`

```csharp
public PartitionNode()
```

### Properties

#### `Children`

```csharp
public List<PartitionNode<T>> Children  // get
```

#### `Data`

```csharp
public required T Data  // get, init
```

#### `Depth`

```csharp
public int Depth  // get, init
```

#### `Height`

Height of the rectangle.

```csharp
public double Height  // get
```

#### `Parent`

```csharp
public PartitionNode<T>? Parent  // get, init
```

#### `TopAncestor`

Returns the ancestor that is a direct child of the root (for branch coloring). Returns this node if it has no parent.

```csharp
public PartitionNode<T> TopAncestor  // get
```

#### `Value`

```csharp
public double Value  // get
```

#### `Width`

Width of the rectangle.

```csharp
public double Width  // get
```

#### `X0`

```csharp
public double X0  // get
```

#### `X1`

```csharp
public double X1  // get
```

#### `Y0`

```csharp
public double Y0  // get
```

#### `Y1`

```csharp
public double Y1  // get
```

### Methods

#### `Ancestors`

Returns the ancestors from this node to the root.

```csharp
public IEnumerable<PartitionNode<T>> Ancestors()
```

#### `Descendants`

Returns all descendant nodes (including this one) in pre-order.

```csharp
public IEnumerable<PartitionNode<T>> Descendants()
```

#### `Leaves`

Returns all leaf nodes.

```csharp
public IEnumerable<PartitionNode<T>> Leaves()
```

#### `ToPolar`

Converts rectangular coordinates to polar for sunburst rendering. X0/X1 map to startAngle/endAngle, Y0/Y1 map to innerRadius/outerRadius. Call this after Layout() to get sunburst coordinates.

```csharp
public (double startAngle, double endAngle, double innerRadius, double outerRadius) ToPolar(double totalWidth, double totalHeight, double maxRadius)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `totalWidth` | `double` |  |
| `totalHeight` | `double` |  |
| `maxRadius` | `double` |  |

## `RibbonGenerator`

Generates SVG path data for chord ribbons. Direct port of d3.ribbon().

```csharp
public sealed class RibbonGenerator
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `RibbonGenerator`

```csharp
public RibbonGenerator()
```

### Methods

#### `Generate`

Generates the SVG path for a chord arc.

```csharp
public string? Generate(ChordArc chord)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `chord` | [`ChordArc`](#chordarc) |  |

#### `SetDigits`

```csharp
public RibbonGenerator SetDigits(int? digits)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `digits` | `int?` |  |

#### `SetRadius`

```csharp
public RibbonGenerator SetRadius(double r)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `r` | `double` |  |

## `SankeyGraph`

The complete Sankey graph with nodes and links.

```csharp
public sealed class SankeyGraph
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `SankeyGraph`

```csharp
public SankeyGraph()
```

### Properties

#### `Links`

```csharp
public List<SankeyLink> Links  // get, set
```

#### `Nodes`

```csharp
public List<SankeyNode> Nodes  // get, set
```

## `SankeyLayout`

Computes the layout for a Sankey (flow) diagram. Positions nodes in columns and computes link paths between them. Direct port of d3-sankey.

```csharp
public sealed class SankeyLayout
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `SankeyLayout`

```csharp
public SankeyLayout()
```

### Methods

#### `Layout`

Computes the Sankey layout for the given nodes and links. Modifies nodes and links in place with computed positions.

```csharp
public SankeyGraph Layout(SankeyGraph graph)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `graph` | [`SankeyGraph`](#sankeygraph) |  |

#### `LinkPath`

Generates the SVG path for a Sankey link (curved band).

```csharp
public static string? LinkPath(SankeyLink link, int? digits = 3)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `link` | [`SankeyLink`](#sankeylink) |  |  |
| `digits` | `int?` | `3` |  |

#### `SetAlign`

```csharp
public SankeyLayout SetAlign(SankeyNodeAlign align)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `align` | [`SankeyNodeAlign`](#sankeynodealign) |  |

#### `SetIterations`

```csharp
public SankeyLayout SetIterations(int iterations)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `iterations` | `int` |  |

#### `SetNodePadding`

```csharp
public SankeyLayout SetNodePadding(double padding)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `padding` | `double` |  |

#### `SetNodeWidth`

```csharp
public SankeyLayout SetNodeWidth(double width)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |

#### `Size`

```csharp
public SankeyLayout Size(double x1, double y1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x1` | `double` |  |
| `y1` | `double` |  |

## `SankeyLink`

A link (flow) in the Sankey diagram.

```csharp
public sealed class SankeyLink
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `SankeyLink`

```csharp
public SankeyLink()
```

### Properties

#### `Source`

```csharp
public SankeyNode? Source  // get
```

#### `SourceId`

```csharp
public required string SourceId  // get, init
```

#### `Target`

```csharp
public SankeyNode? Target  // get
```

#### `TargetId`

```csharp
public required string TargetId  // get, init
```

#### `Value`

```csharp
public double Value  // get, set
```

#### `Width`

```csharp
public double Width  // get
```

#### `Y0`

```csharp
public double Y0  // get
```

#### `Y1`

```csharp
public double Y1  // get
```

## `SankeyNode`

A node in the Sankey diagram.

```csharp
public sealed class SankeyNode
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `SankeyNode`

```csharp
public SankeyNode()
```

### Properties

#### `Depth`

```csharp
public int Depth  // get
```

#### `Height`

```csharp
public int Height  // get
```

#### `Id`

```csharp
public required string Id  // get, init
```

#### `Label`

```csharp
public string? Label  // get, set
```

#### `SourceLinks`

```csharp
public List<SankeyLink> SourceLinks  // get
```

#### `TargetLinks`

```csharp
public List<SankeyLink> TargetLinks  // get
```

#### `Value`

```csharp
public double Value  // get
```

#### `X0`

```csharp
public double X0  // get
```

#### `X1`

```csharp
public double X1  // get
```

#### `Y0`

```csharp
public double Y0  // get
```

#### `Y1`

```csharp
public double Y1  // get
```

## `SankeyNodeAlign`

```csharp
public enum SankeyNodeAlign
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Left` | `0` |  |
| `Right` | `1` |  |
| `Center` | `2` |  |
| `Justify` | `3` |  |

## `Stratify`

```csharp
public static class Stratify
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

```csharp
public static Stratify<T> Create<T>()
```

## `Stratify<T>`

Builds a tree hierarchy from tabular (flat) data using id/parentId relationships. Direct port of d3.stratify().

```csharp
public sealed class Stratify<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `Stratify`

```csharp
public Stratify()
```

### Methods

#### `Build`

Builds a TreeNode hierarchy from flat data. The root node is the one whose parentId is null.

```csharp
public TreeNode<T> Build(IEnumerable<T> data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IEnumerable<T>` |  |

#### `BuildPartition`

Builds a PartitionNode hierarchy from flat data (for use with PartitionLayout).

```csharp
public PartitionNode<T> BuildPartition(IEnumerable<T> data, Func<T, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IEnumerable<T>` |  |
| `value` | `Func<T, double>` |  |

#### `BuildTreemap`

Builds a TreemapNode hierarchy from flat data (for use with TreemapLayout).

```csharp
public TreemapNode<T> BuildTreemap(IEnumerable<T> data, Func<T, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `IEnumerable<T>` |  |
| `value` | `Func<T, double>` |  |

#### `SetId`

```csharp
public Stratify<T> SetId(Func<T, string> id)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `id` | `Func<T, string>` |  |

#### `SetParentId`

```csharp
public Stratify<T> SetParentId(Func<T, string?> parentId)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `parentId` | `Func<T, string?>` |  |

## `TreeLayout`

```csharp
public static class TreeLayout
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

```csharp
public static TreeLayout<T> Create<T>()
```

## `TreeLayout<T>`

Computes a tidy tree layout using the Reingold-Tilford algorithm. Port of d3-hierarchy's tree layout.

```csharp
public sealed class TreeLayout<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `TreeLayout`

```csharp
public TreeLayout()
```

### Methods

#### `Hierarchy`

Build a tree from a root data item and a children accessor.

```csharp
public TreeNode<T> Hierarchy(T root, Func<T, IEnumerable<T>?> childrenAccessor)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | `T` |  |
| `childrenAccessor` | `Func<T, IEnumerable<T>?>` |  |

#### `Layout`

Compute the layout positions for all nodes in the tree. After calling this, each node's X and Y are set.

```csharp
public TreeNode<T> Layout(TreeNode<T> root)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | [`TreeNode<T>`](#treenodet) |  |

#### `Separation`

```csharp
public TreeLayout<T> Separation(double sep)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `sep` | `double` |  |

#### `Size`

```csharp
public TreeLayout<T> Size(double width, double height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |
| `height` | `double` |  |

## `TreeNode<T>`

A node in a tree hierarchy. Wraps user data and holds layout results.

```csharp
public sealed class TreeNode<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `TreeNode`

```csharp
public TreeNode(T data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `data` | `T` |  |

### Properties

#### `Children`

```csharp
public List<TreeNode<T>> Children  // get
```

#### `Data`

```csharp
public T Data  // get
```

#### `Depth`

```csharp
public int Depth  // get
```

#### `Parent`

```csharp
public TreeNode<T>? Parent  // get
```

#### `TopAncestor`

Returns the ancestor that is a direct child of the root (for branch coloring). Returns this node if it has no parent.

```csharp
public TreeNode<T> TopAncestor  // get
```

#### `X`

```csharp
public double X  // get
```

#### `Y`

```csharp
public double Y  // get
```

### Methods

#### `Descendants`

Returns all descendant nodes (including this one) in pre-order.

```csharp
public IEnumerable<TreeNode<T>> Descendants()
```

## `TreemapLayout`

```csharp
public static class TreemapLayout
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods

#### `Create`

```csharp
public static TreemapLayout<T> Create<T>()
```

## `TreemapLayout<T>`

A treemap layout that recursively subdivides area into rectangles. Direct port of d3.treemap().

```csharp
public sealed class TreemapLayout<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `TreemapLayout`

```csharp
public TreemapLayout()
```

### Methods

#### `Hierarchy`

Builds a treemap node hierarchy from data with a children accessor, then sums values bottom-up.

```csharp
public TreemapNode<T> Hierarchy(T root, Func<T, IEnumerable<T>?> children, Func<T, double> value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | `T` |  |
| `children` | `Func<T, IEnumerable<T>?>` |  |
| `value` | `Func<T, double>` |  |

#### `Layout`

Computes the treemap layout for the given root node. Each node must have a Value set (call SumValues first).

```csharp
public TreemapNode<T> Layout(TreemapNode<T> root)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | [`TreemapNode<T>`](#treemapnodet) |  |

#### `SetPadding`

```csharp
public TreemapLayout<T> SetPadding(double p)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `p` | `double` |  |

#### `SetPaddingInner`

```csharp
public TreemapLayout<T> SetPaddingInner(double p)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `p` | `double` |  |

#### `SetPaddingOuter`

```csharp
public TreemapLayout<T> SetPaddingOuter(double p)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `p` | `double` |  |

#### `SetTiling`

```csharp
public TreemapLayout<T> SetTiling(TreemapTiling tiling)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `tiling` | [`TreemapTiling`](#treemaptiling) |  |

#### `Size`

```csharp
public TreemapLayout<T> Size(double x1, double y1)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x1` | `double` |  |
| `y1` | `double` |  |

## `TreemapNode<T>`

A node in a treemap hierarchy with layout rectangles.

```csharp
public sealed class TreemapNode<T>
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `TreemapNode`

```csharp
public TreemapNode()
```

### Properties

#### `Children`

```csharp
public List<TreemapNode<T>> Children  // get
```

#### `Data`

```csharp
public required T Data  // get, init
```

#### `Depth`

```csharp
public int Depth  // get, init
```

#### `Height`

Height of the rectangle.

```csharp
public double Height  // get
```

#### `Parent`

```csharp
public TreemapNode<T>? Parent  // get, init
```

#### `TopAncestor`

Returns the ancestor that is a direct child of the root (for branch coloring). Returns this node if it has no parent.

```csharp
public TreemapNode<T> TopAncestor  // get
```

#### `Value`

```csharp
public double Value  // get
```

#### `Width`

Width of the rectangle.

```csharp
public double Width  // get
```

#### `X0`

```csharp
public double X0  // get
```

#### `X1`

```csharp
public double X1  // get
```

#### `Y0`

```csharp
public double Y0  // get
```

#### `Y1`

```csharp
public double Y1  // get
```

### Methods

#### `Descendants`

Returns all descendant nodes (including this one) in pre-order.

```csharp
public IEnumerable<TreemapNode<T>> Descendants()
```

#### `Leaves`

Returns all leaf nodes.

```csharp
public IEnumerable<TreemapNode<T>> Leaves()
```

## `TreemapTiling`

```csharp
public enum TreemapTiling
```

Namespace: `Microsoft.UI.Reactor.Charting.D3`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Squarify` | `0` |  |
| `Slice` | `1` |  |
| `Dice` | `2` |  |
| `SliceDice` | `3` |  |
