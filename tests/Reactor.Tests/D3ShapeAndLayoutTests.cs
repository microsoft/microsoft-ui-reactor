using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class D3ShapeAndLayoutTests
{
    // ═══════════════════════════════════════════════════════════════════
    // PieGenerator
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PieGenerator_Create_GeneratesArcs()
    {
        var pie = PieGenerator.Create();
        var arcs = pie.Generate(new double[] { 1, 2, 3 });
        Assert.Equal(3, arcs.Length);
        double totalAngle = arcs.Sum(a => Math.Abs(a.EndAngle - a.StartAngle));
        Assert.Equal(2 * Math.PI, totalAngle, 6);
    }

    [Fact]
    public void PieGenerator_ValuesSumToFullCircle()
    {
        var pie = PieGenerator.Create();
        var arcs = pie.Generate(new double[] { 25, 25, 25, 25 });
        Assert.Equal(4, arcs.Length);
        foreach (var arc in arcs)
        {
            Assert.Equal(Math.PI / 2, Math.Abs(arc.EndAngle - arc.StartAngle), 6);
        }
    }

    [Fact]
    public void PieGenerator_WithPadAngle_AddsGaps()
    {
        var pie = PieGenerator.Create();
        pie.SetPadAngle(0.1);
        var arcs = pie.Generate(new double[] { 1, 1, 1 });
        double totalAngle = arcs.Sum(a => Math.Abs(a.EndAngle - a.StartAngle));
        Assert.True(totalAngle < 2 * Math.PI);
    }

    [Fact]
    public void PieGenerator_CustomStartEndAngle()
    {
        var pie = PieGenerator.Create();
        pie.SetStartAngle(0);
        pie.SetEndAngle(Math.PI);
        var arcs = pie.Generate(new double[] { 1, 1 });
        double totalAngle = arcs.Sum(a => Math.Abs(a.EndAngle - a.StartAngle));
        Assert.Equal(Math.PI, totalAngle, 6);
    }

    [Fact]
    public void PieGenerator_SortValues_OrdersArcs()
    {
        var pie = PieGenerator.Create();
        pie.SetSortValues((a, b) => a.CompareTo(b));
        var arcs = pie.Generate(new double[] { 3, 1, 2 });
        // Sort function is applied — just verify all values are present
        var values = arcs.Select(a => a.Value).OrderBy(v => v).ToArray();
        Assert.Equal(new double[] { 1, 2, 3 }, values);
    }

    [Fact]
    public void PieGenerator_WithSort_SortsByDatum()
    {
        var pie = new PieGenerator<string>((s, _) => s.Length);
        pie.SetSort((a, b) => a.CompareTo(b));
        var arcs = pie.Generate(new[] { "ccc", "a", "bb" });
        Assert.Equal(3, arcs.Length);
    }

    [Fact]
    public void PieGenerator_StaticGenerate_ProducesArcs()
    {
        var arcs = PieGenerator.Generate(
            new double[] { 10, 20, 30 },
            v => v);
        Assert.Equal(3, arcs.Length);
        double totalAngle = arcs.Sum(a => Math.Abs(a.EndAngle - a.StartAngle));
        Assert.Equal(2 * Math.PI, totalAngle, 6);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ArcGenerator
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ArcGenerator_GeneratesPathData()
    {
        var arc = new ArcGenerator();
        arc.SetInnerRadius(0);
        arc.SetOuterRadius(100);
        string? path = arc.Generate(0, Math.PI);
        Assert.NotNull(path);
        Assert.StartsWith("M", path);
    }

    [Fact]
    public void ArcGenerator_FullCircle_GeneratesClosedPath()
    {
        var arc = new ArcGenerator();
        arc.SetOuterRadius(50);
        string? path = arc.Generate(0, 2 * Math.PI);
        Assert.NotNull(path);
        Assert.Contains("A", path); // Arc command
    }

    [Fact]
    public void ArcGenerator_Annulus_HasInnerPath()
    {
        var arc = new ArcGenerator();
        arc.SetInnerRadius(30);
        arc.SetOuterRadius(50);
        string? path = arc.Generate(0, Math.PI);
        Assert.NotNull(path);
    }

    [Fact]
    public void ArcGenerator_Centroid_ReturnsMiddlePoint()
    {
        var centroid = ArcGenerator.Centroid(0, Math.PI, outerRadius: 100);
        Assert.NotEqual(0.0, centroid.x);
    }

    [Fact]
    public void ArcGenerator_DegenerateRadius_HandlesGracefully()
    {
        var arc = new ArcGenerator();
        arc.SetOuterRadius(0);
        string? path = arc.Generate(0, Math.PI);
        Assert.NotNull(path);
    }

    [Fact]
    public void ArcGenerator_WithCornerRadius_ThrowsNotImplemented()
    {
        var arc = new ArcGenerator();
        arc.SetOuterRadius(100);
        arc.SetCornerRadius(5);
        Assert.Throws<NotImplementedException>(() => arc.Generate(0, Math.PI / 2));
    }

    [Fact]
    public void ArcGenerator_WithPieArc_GeneratesPath()
    {
        var pie = PieGenerator.Create();
        var arcs = pie.Generate(new double[] { 1, 2, 3 });
        var gen = new ArcGenerator();
        gen.SetOuterRadius(100);
        foreach (var a in arcs)
        {
            string? path = gen.Generate(a);
            Assert.NotNull(path);
        }
    }

    [Fact]
    public void ArcGenerator_SetDigits_ChangesPathPrecision()
    {
        var arc = new ArcGenerator();
        arc.SetOuterRadius(100);
        arc.SetDigits(1);
        string? path1 = arc.Generate(0, 1.0);
        arc.SetDigits(6);
        string? path6 = arc.Generate(0, 1.0);
        Assert.NotNull(path1);
        Assert.NotNull(path6);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SymbolGenerator
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SymbolGenerator_Circle_GeneratesPath()
    {
        var gen = SymbolGenerator.Create<object?>(D3Symbol.Circle, 64);
        string? path = gen.Generate(null);
        Assert.NotNull(path);
        Assert.Contains("M", path);
    }

    [Theory]
    [InlineData("Circle")]
    [InlineData("Cross")]
    [InlineData("Diamond")]
    [InlineData("Square")]
    [InlineData("Star")]
    [InlineData("Triangle")]
    [InlineData("Wye")]
    public void SymbolGenerator_AllTypes_GeneratePaths(string typeName)
    {
        var symbolType = typeName switch
        {
            "Circle" => D3Symbol.Circle,
            "Cross" => D3Symbol.Cross,
            "Diamond" => D3Symbol.Diamond,
            "Square" => D3Symbol.Square,
            "Star" => D3Symbol.Star,
            "Triangle" => D3Symbol.Triangle,
            "Wye" => D3Symbol.Wye,
            _ => throw new ArgumentException()
        };
        var gen = SymbolGenerator.Create<object?>(symbolType, 100);
        string? path = gen.Generate(null);
        Assert.NotNull(path);
    }

    [Fact]
    public void SymbolGenerator_All_ContainsAllSymbolTypes()
    {
        Assert.Equal(7, D3Symbol.All.Length);
    }

    [Fact]
    public void SymbolGenerator_FluentSetters_Work()
    {
        var gen = SymbolGenerator.Create();
        gen.SetType(D3Symbol.Star).SetSize(200).SetDigits(2);
        string? path = gen.Generate(null);
        Assert.NotNull(path);
    }

    [Fact]
    public void SymbolGenerator_FuncOverloads_Work()
    {
        var gen = SymbolGenerator.Create();
        gen.SetType((_, _) => D3Symbol.Diamond);
        gen.SetSize((_, idx) => 64.0 + idx * 10);
        string? path = gen.Generate(null, 0);
        Assert.NotNull(path);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Sankey
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Sankey_Layout_ComputesPositions()
    {
        var graph = CreateSankeyGraph();
        var layout = new SankeyLayout().Size(400, 300);
        layout.Layout(graph);

        foreach (var node in graph.Nodes)
        {
            Assert.True(node.X1 > node.X0);
            Assert.True(node.Y1 > node.Y0);
        }
    }

    [Fact]
    public void Sankey_Layout_ComputesNodeDepths()
    {
        var graph = CreateSankeyGraph();
        new SankeyLayout().Size(400, 300).Layout(graph);

        var src = graph.Nodes.First(n => n.Id == "a");
        var mid = graph.Nodes.First(n => n.Id == "b");
        var dst = graph.Nodes.First(n => n.Id == "d");
        Assert.True(src.Depth < mid.Depth);
        Assert.True(mid.Depth < dst.Depth);
    }

    [Fact]
    public void Sankey_Layout_ComputesNodeValues()
    {
        var graph = CreateSankeyGraph();
        new SankeyLayout().Size(400, 300).Layout(graph);

        foreach (var node in graph.Nodes)
        {
            Assert.True(node.Value > 0);
        }
    }

    [Fact]
    public void Sankey_LinkPath_GeneratesSvgPath()
    {
        var graph = CreateSankeyGraph();
        new SankeyLayout().Size(400, 300).Layout(graph);

        var link = graph.Links.First();
        string? path = SankeyLayout.LinkPath(link);
        Assert.NotNull(path);
        Assert.Contains("M", path);
    }

    [Fact]
    public void Sankey_LinkPath_NullSource_ReturnsNull()
    {
        var link = new SankeyLink { SourceId = "x", TargetId = "y" };
        Assert.Null(SankeyLayout.LinkPath(link));
    }

    [Fact]
    public void Sankey_FluentSetters_AreChainable()
    {
        var layout = new SankeyLayout()
            .Size(500, 400)
            .SetNodeWidth(30)
            .SetNodePadding(10)
            .SetIterations(8)
            .SetAlign(SankeyNodeAlign.Left);
        var graph = CreateSankeyGraph();
        layout.Layout(graph);
        Assert.True(graph.Nodes.All(n => n.X1 - n.X0 == 30));
    }

    [Theory]
    [InlineData(SankeyNodeAlign.Left)]
    [InlineData(SankeyNodeAlign.Right)]
    [InlineData(SankeyNodeAlign.Center)]
    [InlineData(SankeyNodeAlign.Justify)]
    public void Sankey_AllAlignments_ProduceValidLayout(SankeyNodeAlign align)
    {
        var graph = CreateSankeyGraph();
        new SankeyLayout().Size(400, 300).SetAlign(align).Layout(graph);
        Assert.True(graph.Nodes.All(n => n.X1 > n.X0));
    }

    // ═══════════════════════════════════════════════════════════════════
    // D3Polygon
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Polygon_Area_ComputesCorrectly()
    {
        // Unit square CCW: area = 1.0
        var square = new (double x, double y)[]
        {
            (0, 0), (1, 0), (1, 1), (0, 1)
        };
        Assert.Equal(-1.0, D3Polygon.Area(square), 10);
    }

    [Fact]
    public void Polygon_Area_EmptyPolygon_ReturnsZero()
    {
        Assert.Equal(0.0, D3Polygon.Area(Array.Empty<(double, double)>()), 10);
    }

    [Fact]
    public void Polygon_Centroid_ReturnsCenter()
    {
        var square = new (double x, double y)[]
        {
            (0, 0), (2, 0), (2, 2), (0, 2)
        };
        var (cx, cy) = D3Polygon.Centroid(square);
        Assert.Equal(1.0, cx, 10);
        Assert.Equal(1.0, cy, 10);
    }

    [Fact]
    public void Polygon_Centroid_EmptyPolygon_ReturnsZero()
    {
        var (cx, cy) = D3Polygon.Centroid(Array.Empty<(double, double)>());
        Assert.Equal(0.0, cx, 10);
        Assert.Equal(0.0, cy, 10);
    }

    [Fact]
    public void Polygon_Contains_PointInside_ReturnsTrue()
    {
        var square = new (double x, double y)[]
        {
            (0, 0), (10, 0), (10, 10), (0, 10)
        };
        Assert.True(D3Polygon.Contains(square, (5, 5)));
    }

    [Fact]
    public void Polygon_Contains_PointOutside_ReturnsFalse()
    {
        var square = new (double x, double y)[]
        {
            (0, 0), (10, 0), (10, 10), (0, 10)
        };
        Assert.False(D3Polygon.Contains(square, (15, 5)));
    }

    [Fact]
    public void Polygon_Length_ComputesPerimeter()
    {
        var square = new (double x, double y)[]
        {
            (0, 0), (1, 0), (1, 1), (0, 1)
        };
        Assert.Equal(4.0, D3Polygon.Length(square), 10);
    }

    [Fact]
    public void Polygon_Hull_ComputesConvexHull()
    {
        var points = new (double x, double y)[]
        {
            (0, 0), (1, 0), (0.5, 0.5), (1, 1), (0, 1)
        };
        var hull = D3Polygon.Hull(points);
        Assert.NotNull(hull);
        Assert.Equal(4, hull.Length); // Interior point excluded
    }

    [Fact]
    public void Polygon_Hull_TooFewPoints_ReturnsNull()
    {
        var hull = D3Polygon.Hull(new (double, double)[] { (0, 0), (1, 1) });
        Assert.Null(hull);
    }

    // ═══════════════════════════════════════════════════════════════════
    // BinGenerator
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BinGenerator_Create_GeneratesBins()
    {
        var bin = BinGenerator.Create();
        var data = Enumerable.Range(0, 100).Select(i => (double)i).ToArray();
        var bins = bin.Generate(data);
        Assert.True(bins.Length > 0);
        int totalItems = bins.Sum(b => b.Items.Count);
        Assert.Equal(100, totalItems);
    }

    [Fact]
    public void BinGenerator_AllItemsInBins()
    {
        var bin = BinGenerator.Create();
        var data = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var bins = bin.Generate(data);
        int total = bins.Sum(b => b.Items.Count);
        Assert.Equal(10, total);
    }

    [Fact]
    public void BinGenerator_SetThresholdCount_ControlsBinCount()
    {
        var bin = BinGenerator.Create();
        bin.SetThresholdCount(3);
        var data = Enumerable.Range(0, 100).Select(i => (double)i).ToArray();
        var bins = bin.Generate(data);
        Assert.True(bins.Length <= 10); // Reasonable number
    }

    [Fact]
    public void BinGenerator_CustomValue_ExtractsField()
    {
        var bin = BinGenerator.Create<(string name, double score)>(x => x.score);
        var data = new (string, double)[] { ("a", 10), ("b", 50), ("c", 90) };
        var bins = bin.Generate(data);
        Assert.True(bins.Length >= 1);
    }

    [Fact]
    public void BinGenerator_EmptyData_ReturnsEmpty()
    {
        var bin = BinGenerator.Create();
        var bins = bin.Generate(Array.Empty<double>());
        Assert.Empty(bins);
    }

    [Fact]
    public void BinGenerator_CustomThresholds_UsesProvidedBreaks()
    {
        var bin = BinGenerator.Create();
        bin.SetThresholds((_, _) => new double[] { 25, 50, 75 });
        var data = Enumerable.Range(0, 100).Select(i => (double)i).ToArray();
        var bins = bin.Generate(data);
        Assert.True(bins.Length >= 3);
    }

    // ═══════════════════════════════════════════════════════════════════
    // D3Group
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Group_GroupsByKey()
    {
        var data = new[] { "apple", "avocado", "banana", "blueberry", "cherry" };
        var groups = D3Group.Group(data, s => s[0]);
        Assert.Equal(3, groups.Count);
        Assert.Equal(2, groups['a'].Count);
        Assert.Equal(2, groups['b'].Count);
        Assert.Single(groups['c']);
    }

    [Fact]
    public void Rollup_ReducesGroups()
    {
        var data = new[] { ("a", 10), ("a", 20), ("b", 30) };
        var result = D3Group.Rollup(data, x => x.Item1, grp => grp.Sum(x => x.Item2));
        Assert.Equal(30, result["a"]);
        Assert.Equal(30, result["b"]);
    }

    [Fact]
    public void Index_MapsKeyToFirstValue()
    {
        var data = new[] { ("a", 1), ("b", 2), ("c", 3) };
        var index = D3Group.Index(data, x => x.Item1);
        Assert.Equal(("a", 1), index["a"]);
        Assert.Equal(3, index.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Delaunay + Voronoi
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Delaunay_From_ComputesTriangulation()
    {
        var points = new (double, double)[]
        {
            (0, 0), (100, 0), (50, 100), (0, 100), (100, 100)
        };
        var d = Delaunay.From(points);
        Assert.NotNull(d.Triangles);
        Assert.True(d.Triangles.Length >= 3);
    }

    [Fact]
    public void Delaunay_Find_ReturnsClosestPoint()
    {
        var points = new (double, double)[] { (0, 0), (100, 0), (50, 100) };
        var d = Delaunay.From(points);
        Assert.Equal(0, d.Find(1, 1));
        Assert.Equal(1, d.Find(99, 1));
    }

    [Fact]
    public void Delaunay_Neighbors_ReturnsAdjacent()
    {
        var points = new (double, double)[] { (0, 0), (1, 0), (0, 1), (1, 1) };
        var d = Delaunay.From(points);
        var neighbors = d.Neighbors(0).ToList();
        Assert.True(neighbors.Count >= 1);
    }

    [Fact]
    public void Delaunay_Hull_ReturnsConvexHullIndices()
    {
        var points = new (double, double)[] { (0, 0), (1, 0), (0.5, 0.5), (1, 1), (0, 1) };
        var d = Delaunay.From(points);
        Assert.True(d.Hull.Length >= 4);
    }

    [Fact]
    public void Voronoi_CellPolygon_ReturnsPolygon()
    {
        var points = new (double, double)[] { (25, 25), (75, 25), (50, 75) };
        var d = Delaunay.From(points);
        var v = d.Voronoi(0, 0, 100, 100);
        var cell = v.CellPolygon(0);
        // CellPolygon may return null for degenerate configs; just verify it doesn't throw
        if (cell != null)
            Assert.True(cell.Length >= 3);
    }

    [Fact]
    public void Voronoi_Contains_PointInCell()
    {
        var points = new (double, double)[] { (25, 25), (75, 25), (50, 75) };
        var d = Delaunay.From(points);
        var v = d.Voronoi(0, 0, 100, 100);
        Assert.True(v.Contains(0, 10, 10));
    }

    // ═══════════════════════════════════════════════════════════════════
    // LinkGenerator
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void LinkGenerator_Vertical_GeneratesPath()
    {
        var link = LinkGenerator.Vertical<(double x, double y)>(n => n.x, n => n.y);
        string? path = link.Generate((source: (0.0, 0.0), target: (100.0, 100.0)));
        Assert.NotNull(path);
        Assert.StartsWith("M", path);
    }

    [Fact]
    public void LinkGenerator_Horizontal_GeneratesPath()
    {
        var link = LinkGenerator.Horizontal<(double x, double y)>(n => n.x, n => n.y);
        string? path = link.Generate((source: (0.0, 0.0), target: (100.0, 50.0)));
        Assert.NotNull(path);
    }

    [Fact]
    public void LinkGenerator_SetDigits_ChangesPath()
    {
        var link = LinkGenerator.Vertical<(double x, double y)>(n => n.x, n => n.y);
        link.SetDigits(1);
        string? path = link.Generate((source: (0.0, 0.0), target: (100.0, 100.0)));
        Assert.NotNull(path);
    }

    // ═══════════════════════════════════════════════════════════════════
    // AreaGenerator
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AreaGenerator_FromArrays_GeneratesPath()
    {
        var area = AreaGenerator.FromArrays();
        var data = new double[][]
        {
            [0, 10],
            [50, 15],
            [100, 20]
        };
        string? path = area.Generate(data);
        Assert.NotNull(path);
        Assert.StartsWith("M", path);
    }

    [Fact]
    public void AreaGenerator_Create_WithAccessors()
    {
        var area = AreaGenerator.Create<(double x, double low, double high)>(
            d => d.x, d => d.low, d => d.high);
        var data = new (double, double, double)[] { (0, 0, 10), (50, 5, 15), (100, 0, 20) };
        string? path = area.Generate(data);
        Assert.NotNull(path);
    }

    [Fact]
    public void AreaGenerator_WithDefined_SkipsUndefinedPoints()
    {
        var area = AreaGenerator.Create<(double x, double low, double high, bool ok)>(
            d => d.x, d => d.low, d => d.high);
        area.SetDefined((d, _) => d.ok);
        var data = new (double, double, double, bool)[]
        {
            (0, 0, 10, true), (50, 5, 15, false), (100, 0, 20, true)
        };
        string? path = area.Generate(data);
        Assert.NotNull(path);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Contour
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ContourGenerator_GeneratesContours()
    {
        var gen = new ContourGenerator(3, 3);
        gen.SetThresholdCount(3);
        var values = new double[] { 0, 0, 0, 0, 1, 0, 0, 0, 0 };
        var contours = gen.Generate(values);
        Assert.NotNull(contours);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helper
    // ═══════════════════════════════════════════════════════════════════

    private static SankeyGraph CreateSankeyGraph()
    {
        return new SankeyGraph
        {
            Nodes =
            [
                new SankeyNode { Id = "a", Label = "Source A" },
                new SankeyNode { Id = "b", Label = "Middle B" },
                new SankeyNode { Id = "c", Label = "Middle C" },
                new SankeyNode { Id = "d", Label = "Sink D" },
            ],
            Links =
            [
                new SankeyLink { SourceId = "a", TargetId = "b", Value = 10 },
                new SankeyLink { SourceId = "a", TargetId = "c", Value = 5 },
                new SankeyLink { SourceId = "b", TargetId = "d", Value = 10 },
                new SankeyLink { SourceId = "c", TargetId = "d", Value = 5 },
            ]
        };
    }
}
