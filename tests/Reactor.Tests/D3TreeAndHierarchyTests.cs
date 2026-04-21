using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for D3 tree/hierarchy layout, radial shapes, and additional D3 modules
/// that still have coverage gaps.
/// </summary>
public class D3TreeAndHierarchyTests
{
    // ═══════════════════════════════════════════════════════════════
    // TreeLayout + HierarchyNode
    // ═══════════════════════════════════════════════════════════════

    private record TreeData(string Name, TreeData[]? Kids = null);

    private static TreeData SampleTree =>
        new("Root", [
            new("A", [new("A1"), new("A2"), new("A3")]),
            new("B", [new("B1")]),
            new("C"),
        ]);

    [Fact]
    public void TreeLayout_Hierarchy_CreatesHierarchyNode()
    {
        var layout = TreeLayout.Create<TreeData>().Size(600, 400);
        var root = layout.Hierarchy(SampleTree, d => d.Kids);
        Assert.NotNull(root);
        Assert.Equal("Root", root.Data.Name);
    }

    [Fact]
    public void TreeLayout_Layout_AssignsPositions()
    {
        var layout = TreeLayout.Create<TreeData>().Size(600, 400);
        var root = layout.Hierarchy(SampleTree, d => d.Kids);
        layout.Layout(root);
        // Root and descendants should have finite positions
        Assert.True(double.IsFinite(root.X));
        Assert.True(double.IsFinite(root.Y));
    }

    [Fact]
    public void TreeLayout_Descendants_ReturnsAllNodes()
    {
        var layout = TreeLayout.Create<TreeData>().Size(600, 400);
        var root = layout.Hierarchy(SampleTree, d => d.Kids);
        layout.Layout(root);
        var all = root.Descendants().ToList();
        // Root, A, A1, A2, A3, B, B1, C = 8
        Assert.Equal(8, all.Count);
    }

    [Fact]
    public void TreeLayout_Leaves_ReturnsLeafNodes()
    {
        var layout = TreeLayout.Create<TreeData>().Size(600, 400);
        var root = layout.Hierarchy(SampleTree, d => d.Kids);
        layout.Layout(root);
        var leaves = root.Descendants().Where(n => n.Children.Count == 0).ToList();
        // A1, A2, A3, B1, C = 5 leaves
        Assert.Equal(5, leaves.Count);
    }

    [Fact]
    public void TreeLayout_ParentChildRelationships()
    {
        var layout = TreeLayout.Create<TreeData>().Size(600, 400);
        var root = layout.Hierarchy(SampleTree, d => d.Kids);
        layout.Layout(root);
        var all = root.Descendants().ToList();
        var a1 = all.First(n => n.Data.Name == "A1");
        Assert.NotNull(a1.Parent);
        Assert.Equal("A", a1.Parent!.Data.Name);
    }

    [Fact]
    public void TreeLayout_Depth_IsCorrect()
    {
        var layout = TreeLayout.Create<TreeData>().Size(600, 400);
        var root = layout.Hierarchy(SampleTree, d => d.Kids);
        layout.Layout(root);
        var all = root.Descendants().ToList();
        Assert.Equal(0, root.Depth);
        var a = all.First(n => n.Data.Name == "A");
        Assert.Equal(1, a.Depth);
        var a1 = all.First(n => n.Data.Name == "A1");
        Assert.Equal(2, a1.Depth);
    }

    [Fact]
    public void TreeLayout_MaxDepth_IsCorrect()
    {
        var layout = TreeLayout.Create<TreeData>().Size(600, 400);
        var root = layout.Hierarchy(SampleTree, d => d.Kids);
        layout.Layout(root);
        int maxDepth = root.Descendants().Max(n => n.Depth);
        Assert.Equal(2, maxDepth);
    }

    [Fact]
    public void TreeLayout_TopAncestor_ReturnsBranchRoot()
    {
        var layout = TreeLayout.Create<TreeData>().Size(600, 400);
        var root = layout.Hierarchy(SampleTree, d => d.Kids);
        layout.Layout(root);
        var a1 = root.Descendants().First(n => n.Data.Name == "A1");
        var topAnc = a1.TopAncestor;
        Assert.Equal("A", topAnc.Data.Name);
    }

    [Fact]
    public void TreeLayout_LargeTree_HandlesEfficiently()
    {
        // Build a wider tree
        var children = Enumerable.Range(0, 20)
            .Select(i => new TreeData($"Child{i}", 
                Enumerable.Range(0, 5).Select(j => new TreeData($"Grandchild{i}_{j}")).ToArray()))
            .ToArray();
        var bigTree = new TreeData("BigRoot", children);
        
        var layout = TreeLayout.Create<TreeData>().Size(1200, 800);
        var root = layout.Hierarchy(bigTree, d => d.Kids);
        layout.Layout(root);
        var all = root.Descendants().ToList();
        Assert.Equal(1 + 20 + 100, all.Count); // root + 20 children + 100 grandchildren
    }

    [Fact]
    public void TreeLayout_SingleNode_LayoutSucceeds()
    {
        var layout = TreeLayout.Create<TreeData>().Size(600, 400);
        var root = layout.Hierarchy(new TreeData("Solo"), d => d.Kids);
        layout.Layout(root);
        Assert.True(double.IsFinite(root.X));
        Assert.Equal(0, root.Depth);
        Assert.Equal(0, root.Descendants().Max(n => n.Depth));
    }

    // ═══════════════════════════════════════════════════════════════
    // D3 Radial/Curve extra tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LineGenerator_AllCurveTypes()
    {
        var data = new (double x, double y)[] { (0, 0), (50, 50), (100, 25), (150, 75) };
        var curveFactories = new CurveFactory[]
        {
            D3Curve.Linear,
            D3Curve.Step,
            D3Curve.StepBefore,
            D3Curve.StepAfter,
            D3Curve.Basis,
            D3Curve.Cardinal,
            D3Curve.CatmullRom,
            D3Curve.MonotoneX,
            D3Curve.Natural,
        };

        foreach (var cf in curveFactories)
        {
            var gen = LineGenerator.Create();
            gen.SetCurve(cf);
            var path = gen.Generate(data);
            Assert.NotNull(path);
            Assert.StartsWith("M", path);
        }
    }

    [Fact]
    public void LineGenerator_WithDefined_SkipsNulls()
    {
        var data = new (double x, double y)[] { (0, 0), (50, double.NaN), (100, 25) };
        var gen = LineGenerator.Create();
        gen.SetDefined((d, _) => !double.IsNaN(d.y));
        var path = gen.Generate(data);
        Assert.NotNull(path);
    }

    [Fact]
    public void AreaGenerator_WithAccessors_GeneratesPath()
    {
        var gen = AreaGenerator.Create<(double x, double lo, double hi)>(
            d => d.x, d => d.lo, d => d.hi);
        var data = new (double, double, double)[] { (0, 0, 10), (50, 5, 15), (100, 0, 20) };
        var path = gen.Generate(data);
        Assert.NotNull(path);
    }

    [Fact]
    public void AreaGenerator_FromArrays_GeneratesPath()
    {
        var data = new double[][] { [0, 10], [50, 20], [100, 15] };
        var gen = AreaGenerator.FromArrays();
        var path = gen.Generate(data);
        Assert.NotNull(path);
    }

    // ═══════════════════════════════════════════════════════════════
    // D3 Additional Scale tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LinearScale_Nice_AdjustsDomain()
    {
        var scale = new LinearScale();
        scale.Domain = [0.124, 9.89];
        scale.Range = [0, 100];
        scale.Nice();
        Assert.True(scale.Domain[0] <= 0.124);
        Assert.True(scale.Domain[1] >= 9.89);
    }

    [Fact]
    public void LinearScale_Ticks_ReturnsReasonableTickCount()
    {
        var scale = new LinearScale();
        scale.Domain = [0, 100];
        scale.Range = [0, 500];
        var ticks = scale.Ticks(10);
        Assert.True(ticks.Length >= 2);
        Assert.True(ticks.Length <= 20);
    }

    [Fact]
    public void LogScale_WithBase10_MapsCorrectly()
    {
        var scale = new LogScale();
        scale.Domain = [1, 1000];
        scale.Range = [0, 300];
        Assert.Equal(0.0, scale.Map(1), 1);
        Assert.Equal(300.0, scale.Map(1000), 1);
        Assert.Equal(100.0, scale.Map(10), 1);
    }

    [Fact]
    public void LogScale_Invert_RoundTrips()
    {
        var scale = new LogScale();
        scale.Domain = [1, 1000];
        scale.Range = [0, 300];
        double mapped = scale.Map(100);
        double inverted = scale.Invert(mapped);
        Assert.Equal(100.0, inverted, 1);
    }

    [Fact]
    public void LogScale_Ticks_ReturnsLogarithmicTicks()
    {
        var scale = new LogScale();
        scale.Domain = [1, 10000];
        scale.Range = [0, 400];
        var ticks = scale.Ticks();
        Assert.True(ticks.Length >= 2);
    }

    [Fact]
    public void BandScale_PaddingInner_AffectsBandwidth()
    {
        var scale = BandScale.Create();
        scale.Domain = ["A", "B", "C"];
        scale.Range = [0, 300];
        var bw1 = scale.Bandwidth;
        scale.SetPaddingInner(0.5);
        var bw2 = scale.Bandwidth;
        Assert.True(bw2 < bw1);
    }

    [Fact]
    public void BandScale_PaddingOuter_AffectsPositions()
    {
        var scale = BandScale.Create();
        scale.Domain = ["A", "B"];
        scale.Range = [0, 200];
        var pos1 = scale.Map("A");
        scale.SetPaddingOuter(0.5);
        var pos2 = scale.Map("A");
        Assert.NotEqual(pos1, pos2);
    }

    [Fact]
    public void QuantizeScale_MapsToDiscreteRange()
    {
        var scale = new QuantizeScale();
        scale.Domain = [0, 100];
        scale.Range = [1, 2, 3];
        // Values in [0, 33.3) → 1, [33.3, 66.7) → 2, [66.7, 100] → 3
        Assert.Equal(1.0, scale.Map(10));
        Assert.Equal(3.0, scale.Map(90));
    }

    [Fact]
    public void OrdinalScale_UnknownDomainValue_ReturnsDefault()
    {
        var scale = OrdinalScale.Create(
            new[] { "A", "B" },
            new[] { 1.0, 2.0 });
        var result = scale.Map("C");
        Assert.True(double.IsFinite(result));
    }

    // ═══════════════════════════════════════════════════════════════
    // D3 Color - additional coverage
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void D3Color_Brighter_IncreasesLuminance()
    {
        var c = new D3Color(100, 100, 100, 1);
        var bright = c.Brighter();
        Assert.True(bright.R > 100);
    }

    [Fact]
    public void D3Color_Darker_DecreasesLuminance()
    {
        var c = new D3Color(200, 200, 200, 1);
        var dark = c.Darker();
        Assert.True(dark.R < 200);
    }

    [Fact]
    public void D3Color_Interpolate_ReturnsIntermediateColor()
    {
        var a = new D3Color(0, 0, 0, 1);
        var b = new D3Color(255, 255, 255, 1);
        var mid = D3InterpolateColor.Rgb(a, b)(0.5);
        Assert.Equal(128.0, mid.R, 2.0);
        Assert.Equal(128.0, mid.G, 2.0);
        Assert.Equal(128.0, mid.B, 2.0);
    }

    [Fact]
    public void D3Color_Category10_Has10Colors()
    {
        Assert.Equal(10, D3Color.Category10.Count);
    }

    [Fact]
    public void D3Color_ParseRgbFunction()
    {
        var c = D3Color.Parse("rgb(100, 150, 200)");
        Assert.Equal(100.0, c.R, 1.0);
        Assert.Equal(150.0, c.G, 1.0);
        Assert.Equal(200.0, c.B, 1.0);
    }

    [Fact]
    public void D3Color_ParseShortHex()
    {
        var c = D3Color.Parse("#fff");
        Assert.Equal(255.0, c.R, 1.0);
        Assert.Equal(255.0, c.G, 1.0);
        Assert.Equal(255.0, c.B, 1.0);
    }

    [Fact]
    public void D3Color_ParseNamedColor()
    {
        var c = D3Color.Parse("red");
        Assert.Equal(255.0, c.R, 1.0);
        Assert.Equal(0.0, c.G, 1.0);
        Assert.Equal(0.0, c.B, 1.0);
    }

    [Fact]
    public void D3Color_ToRgb_ReturnsString()
    {
        var c = new D3Color(128, 64, 32, 1);
        var rgb = c.ToRgb();
        Assert.Contains("128", rgb);
    }

    [Fact]
    public void D3Color_Constructor_SetsOpacity()
    {
        var c = new D3Color(255, 0, 0, 0.5);
        Assert.Equal(0.5, c.Opacity);
        Assert.Equal(255.0, c.R, 1.0);
    }
}
