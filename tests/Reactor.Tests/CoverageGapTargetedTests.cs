using Microsoft.UI.Reactor.Charting.Accessibility;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Xaml.Automation.Peers;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Targeted tests for remaining coverage gaps:
/// - Sankey cycle detection and collision resolution
/// - AccessibilityScanner untested rules (A11Y_005, A11Y_007, CHART_003/005/006/007/012)
/// - QuantileScale/ThresholdScale (0% covered)
/// - Delaunay/Voronoi edge cases
/// - ValidationContext additional paths
/// - D3 Contour/Polygon/Interpolate/Curve additional paths
/// </summary>
public class CoverageGapTargetedTests
{
    // ═══════════════════════════════════════════════════════════════
    // Sankey - cycle detection and collision resolution
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Sankey_CyclicGraph_HandlesGracefully()
    {
        var graph = new SankeyGraph();
        graph.Nodes.AddRange([
            new SankeyNode { Id = "A" },
            new SankeyNode { Id = "B" },
            new SankeyNode { Id = "C" },
        ]);
        graph.Links.Add(new SankeyLink { SourceId = "A", TargetId = "B", Value = 10 });
        graph.Links.Add(new SankeyLink { SourceId = "B", TargetId = "C", Value = 10 });
        graph.Links.Add(new SankeyLink { SourceId = "C", TargetId = "A", Value = 10 }); // cycle

        new SankeyLayout().Size(400, 300).Layout(graph);
        Assert.True(graph.Nodes.All(n => double.IsFinite(n.X0)));
    }

    [Fact]
    public void Sankey_ManyNodes_CollisionResolution()
    {
        var graph = new SankeyGraph();
        for (int i = 0; i < 20; i++)
            graph.Nodes.Add(new SankeyNode { Id = $"N{i}" });
        for (int i = 0; i < 19; i++)
            graph.Links.Add(new SankeyLink { SourceId = $"N{i}", TargetId = $"N{i + 1}", Value = 100 });

        new SankeyLayout().Size(100, 100).Layout(graph); // small size forces collisions
        Assert.True(graph.Nodes.All(n => double.IsFinite(n.Y0)));
    }

    [Fact]
    public void Sankey_FanOut_OverflowResolution()
    {
        var graph = new SankeyGraph();
        graph.Nodes.Add(new SankeyNode { Id = "src" });
        for (int i = 0; i < 10; i++)
        {
            graph.Nodes.Add(new SankeyNode { Id = $"dst{i}" });
            graph.Links.Add(new SankeyLink { SourceId = "src", TargetId = $"dst{i}", Value = 100 });
        }
        new SankeyLayout().Size(200, 50).Layout(graph); // very small height
        // Layout should complete without throwing; nodes get positions
        Assert.True(graph.Nodes.All(n => double.IsFinite(n.Y0)));
    }

    [Fact]
    public void Sankey_EmptyGraph_NoError()
    {
        var graph = new SankeyGraph();
        new SankeyLayout().Size(400, 300).Layout(graph);
        Assert.Empty(graph.Nodes);
    }

    [Fact]
    public void Sankey_SingleNode_LayoutWorks()
    {
        var graph = new SankeyGraph();
        graph.Nodes.Add(new SankeyNode { Id = "solo" });
        new SankeyLayout().Size(400, 300).Layout(graph);
        Assert.True(double.IsFinite(graph.Nodes[0].X0));
    }

    [Fact]
    public void Sankey_MultipleIterations_Converges()
    {
        var graph = new SankeyGraph();
        graph.Nodes.AddRange([
            new SankeyNode { Id = "A" }, new SankeyNode { Id = "B" },
            new SankeyNode { Id = "C" }, new SankeyNode { Id = "D" },
        ]);
        graph.Links.Add(new SankeyLink { SourceId = "A", TargetId = "B", Value = 10 });
        graph.Links.Add(new SankeyLink { SourceId = "A", TargetId = "C", Value = 20 });
        graph.Links.Add(new SankeyLink { SourceId = "B", TargetId = "D", Value = 10 });
        graph.Links.Add(new SankeyLink { SourceId = "C", TargetId = "D", Value = 20 });

        new SankeyLayout().Size(400, 300).SetIterations(32).Layout(graph);
        Assert.True(graph.Links.All(l => double.IsFinite(l.Width)));
    }

    // ═══════════════════════════════════════════════════════════════
    // QuantileScale and ThresholdScale
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void QuantileScale_MapsToQuantiles()
    {
        var scale = new QuantileScale();
        scale.Domain = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        scale.Range = [10, 20, 30, 40];
        Assert.Equal(10.0, scale.Map(1));
        Assert.Equal(40.0, scale.Map(10));
    }

    [Fact]
    public void QuantileScale_NaN_ReturnsNaN()
    {
        var scale = new QuantileScale().SetDomain(1, 2, 3).SetRange(10, 20);
        Assert.True(double.IsNaN(scale.Map(double.NaN)));
    }

    [Fact]
    public void QuantileScale_InvertExtent_ReturnsDomainRange()
    {
        var scale = new QuantileScale().SetDomain(1, 2, 3, 4, 5, 6, 7, 8).SetRange(10, 20, 30, 40);
        var (x0, x1) = scale.InvertExtent(10);
        Assert.True(double.IsFinite(x0));
        Assert.True(double.IsFinite(x1));
    }

    [Fact]
    public void QuantileScale_InvertExtent_NotInRange_ReturnsNaN()
    {
        var scale = new QuantileScale().SetDomain(1, 2, 3).SetRange(10, 20);
        var (x0, x1) = scale.InvertExtent(999);
        Assert.True(double.IsNaN(x0));
        Assert.True(double.IsNaN(x1));
    }

    [Fact]
    public void QuantileScale_Quantiles_ReturnsThresholds()
    {
        var scale = new QuantileScale().SetDomain(0, 10, 20, 30).SetRange(1, 2);
        var q = scale.Quantiles();
        Assert.NotEmpty(q);
    }

    [Fact]
    public void QuantileScale_Copy_IsIndependent()
    {
        var scale = new QuantileScale().SetDomain(1, 2, 3, 4).SetRange(10, 20);
        var copy = scale.Copy();
        Assert.Equal(scale.Map(2), copy.Map(2));
    }

    [Fact]
    public void ThresholdScale_MapsBasedOnThresholds()
    {
        var scale = new ThresholdScale { Domain = [30, 70], Range = [1, 2, 3] };
        Assert.Equal(1.0, scale.Map(10));
        Assert.Equal(2.0, scale.Map(50));
        Assert.Equal(3.0, scale.Map(90));
    }

    [Fact]
    public void ThresholdScale_NaN_ReturnsNaN()
    {
        var scale = new ThresholdScale();
        Assert.True(double.IsNaN(scale.Map(double.NaN)));
    }

    [Fact]
    public void ThresholdScale_InvertExtent()
    {
        var scale = new ThresholdScale { Domain = [30, 70], Range = [1, 2, 3] };
        var (x0, x1) = scale.InvertExtent(2);
        Assert.Equal(30.0, x0);
        Assert.Equal(70.0, x1);
    }

    [Fact]
    public void ThresholdScale_InvertExtent_FirstBucket()
    {
        var scale = new ThresholdScale { Domain = [50], Range = [0, 1] };
        var (x0, x1) = scale.InvertExtent(0);
        Assert.True(double.IsNegativeInfinity(x0));
        Assert.Equal(50.0, x1);
    }

    [Fact]
    public void ThresholdScale_InvertExtent_NotInRange()
    {
        var scale = new ThresholdScale { Domain = [50], Range = [0, 1] };
        var (x0, x1) = scale.InvertExtent(999);
        Assert.True(double.IsNaN(x0));
    }

    [Fact]
    public void ThresholdScale_Copy_IsIndependent()
    {
        var scale = new ThresholdScale().SetDomain(0.5).SetRange(0, 1);
        var copy = scale.Copy();
        Assert.Equal(scale.Map(0.3), copy.Map(0.3));
    }

    // ═══════════════════════════════════════════════════════════════
    // AccessibilityScanner - untested rules
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void A11Y_007_TabIndexGap_Flagged()
    {
        var tree = VStack(
            new ButtonElement("A", null) { Modifiers = new ElementModifiers { TabIndex = 1 } },
            new ButtonElement("B", null) { Modifiers = new ElementModifiers { TabIndex = 5 } } // gap: 1→5
        );
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_007");
    }

    [Fact]
    public void A11Y_007_SequentialTabIndex_Passes()
    {
        var tree = VStack(
            new ButtonElement("A", null) { Modifiers = new ElementModifiers { TabIndex = 1 } },
            new ButtonElement("B", null) { Modifiers = new ElementModifiers { TabIndex = 2 } }
        );
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_007");
    }

    [Fact]
    public void A11Y_003_NumberBox_Without_Label_Flagged()
    {
        var tree = VStack(
            new NumberBoxElement(0, null, null)
        );
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_003");
    }

    [Fact]
    public void A11Y_003_PasswordBox_Without_Label_Flagged()
    {
        var tree = VStack(
            new PasswordBoxElement("", null)
        );
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_003");
    }

    [Fact]
    public void A11Y_003_AutoSuggestBox_Without_Label_Flagged()
    {
        var tree = VStack(
            new AutoSuggestBoxElement("", null, null, null)
        );
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_003");
    }

    [Fact]
    public void A11Y_004_LargeBoldViaModifiers_Flagged()
    {
        // Test heading detection via Modifiers.FontSize and Modifiers.FontWeight
        var txt = new TextBlockElement("Section Title")
        {
            Modifiers = new ElementModifiers { FontSize = 24, FontWeight = new global::Windows.UI.Text.FontWeight(700) }
        };
        var tree = VStack(txt);
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_004");
    }

    // ═══════════════════════════════════════════════════════════════
    // AccessibilityScanner - chart rules not yet tested
    // ═══════════════════════════════════════════════════════════════

    private sealed class MockChartData : IChartAccessibilityData
    {
        public string? Name { get; init; }
        public string? Description { get; init; }
        public IReadOnlyList<ChartSeriesDescriptor> Series { get; init; } = [];
        public IReadOnlyList<ChartAxisDescriptor> Axes { get; init; } = [];
        public ChartViewport? Viewport { get; init; }
        public string ChartTypeName { get; init; } = "Line";
    }

    private static CanvasElement MakeChartCanvas(
        IChartAccessibilityData? data = null,
        bool isInteractive = false,
        bool isKeyboardDisabled = false,
        bool isTightHitTest = false,
        bool isAnnounceEveryFrame = false,
        bool isRawColors = false,
        global::Windows.UI.Color? customFocusColor = null)
    {
        return new CanvasElement([])
        {
            ChartData = data ?? new MockChartData
            {
                Name = "Test Chart",
                Series = [],
            },
            IsInteractive = isInteractive,
            IsKeyboardDisabled = isKeyboardDisabled,
            IsTightHitTest = isTightHitTest,
            IsAnnounceEveryFrame = isAnnounceEveryFrame,
            IsRawColors = isRawColors,
            CustomFocusColor = customFocusColor,
        };
    }

    [Fact]
    public void A11Y_CHART_003_InteractiveKeyboardDisabled_Flagged()
    {
        var canvas = MakeChartCanvas(isInteractive: true, isKeyboardDisabled: true);
        var tree = VStack(canvas);
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_003");
    }

    [Fact]
    public void A11Y_CHART_003_InteractiveWithKeyboard_Passes()
    {
        var canvas = MakeChartCanvas(isInteractive: true, isKeyboardDisabled: false);
        var tree = VStack(canvas);
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_003");
    }

    [Fact]
    public void A11Y_CHART_005_TightHitTest_Flagged()
    {
        var canvas = MakeChartCanvas(isTightHitTest: true);
        var tree = VStack(canvas);
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_005");
    }

    [Fact]
    public void A11Y_CHART_006_LowContrastFocusColor_Flagged()
    {
        // Very light focus color — fails 3:1 against white background
        var canvas = MakeChartCanvas(customFocusColor: global::Windows.UI.Color.FromArgb(255, 240, 240, 240));
        var tree = VStack(canvas);
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_006");
    }

    [Fact]
    public void A11Y_CHART_006_NoCustomFocusColor_Passes()
    {
        // No custom focus color → no finding
        var canvas = MakeChartCanvas();
        var tree = VStack(canvas);
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.DoesNotContain(findings, f => f.Id == "A11Y_CHART_006");
    }

    [Fact]
    public void A11Y_CHART_007_AnnounceEveryFrame_Flagged()
    {
        var canvas = MakeChartCanvas(isAnnounceEveryFrame: true);
        var tree = VStack(canvas);
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_007");
    }

    [Fact]
    public void A11Y_CHART_012_RawColors_Info()
    {
        var canvas = MakeChartCanvas(isRawColors: true);
        var tree = VStack(canvas);
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_CHART_012");
    }

    // ═══════════════════════════════════════════════════════════════
    // AccessibilityScanner - container child walking
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Scanner_WalksCanvas()
    {
        var tree = VStack(
            new CanvasElement([new ImageElement("test.png")])
        );
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_002"); // image flagged
    }

    [Fact]
    public void Scanner_WalksBorder()
    {
        var tree = VStack(
            new BorderElement(new ImageElement("x.png"))
        );
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_002");
    }

    [Fact]
    public void Scanner_WalksScrollView()
    {
        var tree = VStack(
            new ScrollViewerElement(new ImageElement("y.png"))
        );
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_002");
    }

    [Fact]
    public void Scanner_WalksListView()
    {
        var tree = VStack(
            new ListViewElement([new ImageElement("z.png")])
        );
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_002");
    }

    [Fact]
    public void Scanner_WalksExpander()
    {
        var tree = VStack(
            new ExpanderElement("Header", new ImageElement("w.png"))
        );
        tree = tree with { Modifiers = new ElementModifiers { Accessibility = new AccessibilityModifiers { LandmarkType = AutomationLandmarkType.Main } } };
        var findings = AccessibilityScanner.Scan(tree);
        Assert.Contains(findings, f => f.Id == "A11Y_002");
    }

    // ═══════════════════════════════════════════════════════════════
    // Delaunay edge cases
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Delaunay_CollinearPoints_HandlesGracefully()
    {
        var points = new (double, double)[] { (0, 0), (1, 1), (2, 2), (3, 3) };
        var d = Delaunay.From(points);
        Assert.NotNull(d);
    }

    [Fact]
    public void Delaunay_DuplicatePoints_HandlesGracefully()
    {
        var points = new (double, double)[] { (1, 1), (1, 1), (2, 2), (2, 2) };
        var d = Delaunay.From(points);
        Assert.NotNull(d);
    }

    [Fact]
    public void Delaunay_LargePointSet_Triangulates()
    {
        var rng = new Random(42);
        var points = Enumerable.Range(0, 100)
            .Select(_ => (rng.NextDouble() * 100, rng.NextDouble() * 100))
            .ToArray();
        var d = Delaunay.From(points);
        Assert.NotNull(d);
        Assert.True(d.Triangles.Length > 0);
    }

    [Fact]
    public void Delaunay_FindClosestPoint()
    {
        var points = new (double, double)[] { (0, 0), (100, 0), (50, 50) };
        var d = Delaunay.From(points);
        int closest = d.Find(10, 10);
        Assert.Equal(0, closest);
    }

    [Fact]
    public void Delaunay_Neighbors_ReturnsConnectedPoints()
    {
        var points = new (double, double)[] { (0, 0), (100, 0), (50, 86), (50, 30) };
        var d = Delaunay.From(points);
        var neighbors = d.Neighbors(0).ToList();
        Assert.True(neighbors.Count >= 1);
    }

    [Fact]
    public void Voronoi_Bounds_ClipsCells()
    {
        var points = new (double, double)[] { (25, 25), (75, 75) };
        var d = Delaunay.From(points);
        var v = d.Voronoi(0, 0, 100, 100);
        Assert.NotNull(v);
        Assert.True(v.Contains(0, 10, 10));
    }

    [Fact]
    public void Voronoi_CellPolygon_ReturnsBoundary()
    {
        var points = new (double, double)[] { (25, 25), (75, 25), (50, 75), (25, 75), (75, 75) };
        var d = Delaunay.From(points);
        var v = d.Voronoi(0, 0, 100, 100);
        // At least one cell should produce a polygon
        bool anyCell = false;
        for (int i = 0; i < points.Length; i++)
        {
            var cell = v.CellPolygon(i);
            if (cell is not null && cell.Length >= 3)
            {
                anyCell = true;
                break;
            }
        }
        Assert.True(anyCell);
    }

    // ═══════════════════════════════════════════════════════════════
    // Contour additional paths
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ContourGenerator_MultipleThresholds()
    {
        var values = new double[100];
        for (int i = 0; i < 100; i++)
            values[i] = Math.Sqrt((i % 10 - 5) * (i % 10 - 5) + (i / 10 - 5) * (i / 10 - 5));

        var gen = new ContourGenerator(10, 10);
        gen.SetThresholds([1, 2, 3, 4]);
        var contours = gen.Generate(values);
        Assert.True(contours.Length > 0);
    }

    [Fact]
    public void ContourGenerator_FlatValues()
    {
        var values = Enumerable.Repeat(5.0, 100).ToArray();
        var gen = new ContourGenerator(10, 10);
        gen.SetThresholds([1, 10]);
        var contours = gen.Generate(values);
        Assert.NotNull(contours);
    }

    // ═══════════════════════════════════════════════════════════════
    // D3Polygon additional
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Polygon_Area_NonZero()
    {
        var polygon = new (double, double)[] { (0, 0), (10, 0), (10, 10), (0, 10) };
        Assert.True(Math.Abs(D3Polygon.Area(polygon)) > 0);
    }

    [Fact]
    public void Polygon_Centroid_ReturnsCenter()
    {
        var polygon = new (double, double)[] { (0, 0), (10, 0), (10, 10), (0, 10) };
        var (cx, cy) = D3Polygon.Centroid(polygon);
        Assert.Equal(5.0, cx, 1);
        Assert.Equal(5.0, cy, 1);
    }

    [Fact]
    public void Polygon_Contains_PointInside()
    {
        var polygon = new (double, double)[] { (0, 0), (10, 0), (10, 10), (0, 10) };
        Assert.True(D3Polygon.Contains(polygon, (5, 5)));
        Assert.False(D3Polygon.Contains(polygon, (15, 15)));
    }

    [Fact]
    public void Polygon_Hull_ReturnsConvexHull()
    {
        var points = new (double, double)[] { (0, 0), (10, 0), (10, 10), (0, 10), (5, 5) };
        var hull = D3Polygon.Hull(points);
        Assert.NotNull(hull);
        Assert.True(hull!.Length >= 4);
    }

    [Fact]
    public void Polygon_Length_ReturnsPerimeter()
    {
        var polygon = new (double, double)[] { (0, 0), (10, 0), (10, 10), (0, 10) };
        Assert.Equal(40.0, D3Polygon.Length(polygon), 1);
    }

    // ═══════════════════════════════════════════════════════════════
    // D3 Interpolate additional paths
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void InterpolateNumber_AtBoundaries()
    {
        var fn = D3Interpolate.Number(0, 100);
        Assert.Equal(0.0, fn(0));
        Assert.Equal(100.0, fn(1));
        Assert.Equal(50.0, fn(0.5));
    }

    [Fact]
    public void InterpolateRound_ReturnsIntegers()
    {
        var fn = D3Interpolate.Round(0, 10);
        Assert.Equal(0.0, fn(0));
        Assert.Equal(10.0, fn(1));
        Assert.Equal(5.0, fn(0.5));
    }

    [Fact]
    public void InterpolateNumber_NegativeValues()
    {
        var fn = D3Interpolate.Number(-100, 100);
        Assert.Equal(-100.0, fn(0));
        Assert.Equal(100.0, fn(1));
        Assert.Equal(0.0, fn(0.5));
    }

    // ═══════════════════════════════════════════════════════════════
    // Curve factories - additional closed/parameterized curves
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BasisClosed_GeneratesClosedPath()
    {
        var data = new (double x, double y)[] { (0, 0), (50, 50), (100, 25), (75, 75) };
        var gen = LineGenerator.Create();
        gen.SetCurve(D3Curve.BasisClosed);
        Assert.NotNull(gen.Generate(data));
    }

    [Fact]
    public void CardinalWithTension_GeneratesPath()
    {
        var data = new (double x, double y)[] { (0, 0), (50, 50), (100, 25), (150, 75) };
        var gen = LineGenerator.Create();
        gen.SetCurve(D3Curve.CardinalWithTension(0.5));
        Assert.NotNull(gen.Generate(data));
    }

    [Fact]
    public void CatmullRomWithAlpha_GeneratesPath()
    {
        var data = new (double x, double y)[] { (0, 0), (50, 50), (100, 25), (150, 75) };
        var gen = LineGenerator.Create();
        gen.SetCurve(D3Curve.CatmullRomWithAlpha(1.0));
        Assert.NotNull(gen.Generate(data));
    }

    [Fact]
    public void StepBefore_GeneratesPath()
    {
        var data = new (double x, double y)[] { (0, 0), (50, 50), (100, 0) };
        var gen = LineGenerator.Create();
        gen.SetCurve(D3Curve.StepBefore);
        Assert.NotNull(gen.Generate(data));
    }

    [Fact]
    public void StepAfter_GeneratesPath()
    {
        var data = new (double x, double y)[] { (0, 0), (50, 50), (100, 0) };
        var gen = LineGenerator.Create();
        gen.SetCurve(D3Curve.StepAfter);
        Assert.NotNull(gen.Generate(data));
    }

    // ═══════════════════════════════════════════════════════════════
    // ValidationContext - additional paths (uncovered lines)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ValidationContext_AddExternal_And_ClearExternal()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.AddExternal("email", "Server says invalid");
        Assert.False(ctx.IsValid());
        ctx.ClearExternal("email");
        Assert.True(ctx.IsValid());
    }

    [Fact]
    public void ValidationContext_NotifyValueChanged_ClearsExternalMessages()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.AddExternal("email", "Bad");
        ctx.NotifyValueChanged("email", "new@example.com");
        Assert.Empty(ctx.GetMessages("email"));
    }

    [Fact]
    public void ValidationContext_GetAllMessages_IncludesAllFields()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("name");
        ctx.RegisterField("email");
        ctx.Add("name", "Required");
        ctx.AddExternal("email", "Invalid");
        var all = ctx.GetAllMessages();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void ValidationContext_HasError_ReturnsTrueForErrors()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("name");
        ctx.Add("name", "Required");
        Assert.True(ctx.HasError("name"));
        Assert.False(ctx.HasError("email"));
    }

    [Fact]
    public void ValidationContext_HasMessages_IncludesWarnings()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("name");
        ctx.Add("name", "Short", Severity.Warning);
        Assert.True(ctx.HasMessages("name"));
        Assert.False(ctx.HasMessages("email"));
    }

    [Fact]
    public void ValidationContext_HighestSeverity_ReturnsHighest()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("name");
        ctx.Add("name", "Short", Severity.Warning);
        Assert.Equal(Severity.Warning, ctx.HighestSeverity("name"));
        ctx.Add("name", "Required", Severity.Error);
        Assert.Equal(Severity.Error, ctx.HighestSeverity("name"));
    }

    [Fact]
    public void ValidationContext_InvalidFields_ListsOnlyErrors()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("name");
        ctx.RegisterField("email");
        ctx.Add("name", "Required");
        ctx.Add("email", "Short", Severity.Warning);
        var invalid = ctx.InvalidFields;
        Assert.Contains("name", invalid);
        Assert.DoesNotContain("email", invalid);
    }

    [Fact]
    public void ValidationContext_InvalidFields_IncludesExternalErrors()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.AddExternal("email", "Server error", Severity.Error);
        Assert.Contains("email", ctx.InvalidFields);
    }

    [Fact]
    public void ValidationContext_IsValid_ExternalErrorsMakeInvalid()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.AddExternal("email", "Server error");
        Assert.False(ctx.IsValid());
    }

    [Fact]
    public void ValidationContext_MarkAllTouched()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("name");
        ctx.RegisterField("email");
        ctx.MarkAllTouched();
        Assert.True(ctx.IsTouched("name"));
        Assert.True(ctx.IsTouched("email"));
    }

    [Fact]
    public void ValidationContext_IsDirty_Detects_Changes()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("name");
        ctx.SetInitialValue("name", "Alice");
        Assert.False(ctx.IsDirty("name"));
        Assert.False(ctx.IsDirty());
        ctx.NotifyValueChanged("name", "Bob");
        Assert.True(ctx.IsDirty("name"));
        Assert.True(ctx.IsDirty());
    }

    [Fact]
    public void ValidationContext_Reset_RestoresInitialValue()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("name");
        ctx.SetInitialValue("name", "Alice");
        ctx.MarkTouched("name");
        ctx.Add("name", "Error");
        ctx.NotifyValueChanged("name", "Bob");

        var initial = ctx.Reset("name");
        Assert.Equal("Alice", initial);
        Assert.False(ctx.IsTouched("name"));
        Assert.Empty(ctx.GetMessages("name"));
    }

    [Fact]
    public void ValidationContext_ResetAll_RestoresAllFields()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("name");
        ctx.RegisterField("email");
        ctx.SetInitialValue("name", "Alice");
        ctx.SetInitialValue("email", "a@b.com");
        ctx.MarkAllTouched();
        ctx.Add("name", "Error");

        var result = ctx.ResetAll();
        Assert.Equal("Alice", result["name"]);
        Assert.Equal("a@b.com", result["email"]);
        Assert.False(ctx.IsTouched("name"));
        Assert.Empty(ctx.GetMessages("name"));
    }

    [Fact]
    public void ValidationContext_Clear_RemovesBothInternalAndExternal()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("name");
        ctx.Add("name", "Required");
        ctx.AddExternal("name", "Server error");
        Assert.Equal(2, ctx.GetMessages("name").Count);
        ctx.Clear("name");
        Assert.Empty(ctx.GetMessages("name"));
    }

    [Fact]
    public void ValidationContext_ClearAll_ClearsEverything()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("a");
        ctx.RegisterField("b");
        ctx.Add("a", "err");
        ctx.AddExternal("b", "err");
        ctx.ClearAll();
        Assert.Empty(ctx.GetAllMessages());
    }

    [Fact]
    public void ValidationContext_HighestSeverity_External()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("name");
        ctx.AddExternal("name", "Server warning", Severity.Warning);
        Assert.Equal(Severity.Warning, ctx.HighestSeverity("name"));
    }

    [Fact]
    public void ValidationContext_HasError_ViaExternal()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("name");
        ctx.AddExternal("name", "Server error", Severity.Error);
        Assert.True(ctx.HasError("name"));
    }

    // ═══════════════════════════════════════════════════════════════
    // DataGridState - sort/filter/search operations (paged mode paths)
    // ═══════════════════════════════════════════════════════════════

    private record SortTestItem(int Id, string Name);

    private class SimpleDataSource : IDataSource<SortTestItem>
    {
        private readonly List<SortTestItem> _items;
        public SimpleDataSource(params SortTestItem[] items) => _items = [.. items];

        public async Task<DataPage<SortTestItem>> GetPageAsync(DataRequest req, CancellationToken ct = default)
        {
            await Task.Yield();
            return new DataPage<SortTestItem>(_items, TotalCount: _items.Count);
        }

        public RowKey GetRowKey(SortTestItem item) => new(item.Id.ToString());
        public DataSourceCapabilities Capabilities => DataSourceCapabilities.ServerSort | DataSourceCapabilities.ServerFilter;
    }

    private static readonly FieldDescriptor[] SimpleColumns =
    [
        new FieldDescriptor { Name = "Id", FieldType = typeof(int), GetValue = o => ((SortTestItem)o).Id },
        new FieldDescriptor { Name = "Name", FieldType = typeof(string), GetValue = o => ((SortTestItem)o).Name },
    ];

    [Fact]
    public void DataGrid_ToggleSort_AscDescNone()
    {
        var state = new DataGridState<SortTestItem>(
            new SimpleDataSource(), SimpleColumns, SelectionMode.Single);

        state.ToggleSort("Name");
        Assert.Equal(SortDirection.Ascending, state.GetSortDirection("Name"));

        state.ToggleSort("Name");
        Assert.Equal(SortDirection.Descending, state.GetSortDirection("Name"));

        state.ToggleSort("Name");
        Assert.Null(state.GetSortDirection("Name"));
    }

    [Fact]
    public void DataGrid_ToggleSort_Additive()
    {
        var state = new DataGridState<SortTestItem>(
            new SimpleDataSource(), SimpleColumns, SelectionMode.Single);

        state.ToggleSort("Name");
        state.ToggleSort("Id", additive: true);
        Assert.NotNull(state.GetSortDirection("Name"));
        Assert.NotNull(state.GetSortDirection("Id"));
    }

    [Fact]
    public void DataGrid_ToggleSort_Additive_Cycle()
    {
        var state = new DataGridState<SortTestItem>(
            new SimpleDataSource(), SimpleColumns, SelectionMode.Single);

        state.ToggleSort("Name", additive: true);
        Assert.Equal(SortDirection.Ascending, state.GetSortDirection("Name"));

        state.ToggleSort("Name", additive: true);
        Assert.Equal(SortDirection.Descending, state.GetSortDirection("Name"));

        state.ToggleSort("Name", additive: true);
        Assert.Null(state.GetSortDirection("Name"));
    }

    [Fact]
    public void DataGrid_SetFilter_GetFilter_ClearFilter()
    {
        var state = new DataGridState<SortTestItem>(
            new SimpleDataSource(), SimpleColumns, SelectionMode.Single);

        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Contains, "test"));
        var f = state.GetFilter("Name");
        Assert.NotNull(f);
        Assert.Equal("Name", f!.Field);
        Assert.Equal(FilterOperator.Contains, f.Operator);

        state.ClearFilter("Name");
        Assert.Null(state.GetFilter("Name"));
    }

    [Fact]
    public void DataGrid_ClearAllFilters()
    {
        var state = new DataGridState<SortTestItem>(
            new SimpleDataSource(), SimpleColumns, SelectionMode.Single);

        state.SetFilter(new FilterDescriptor("Name", FilterOperator.Contains, "a"));
        state.SetFilter(new FilterDescriptor("Id", FilterOperator.GreaterThan, 5));
        state.ClearAllFilters();
        Assert.Null(state.GetFilter("Name"));
        Assert.Null(state.GetFilter("Id"));
    }

    [Fact]
    public void DataGrid_SearchQuery()
    {
        var state = new DataGridState<SortTestItem>(
            new SimpleDataSource(), SimpleColumns, SelectionMode.Single);

        state.SetSearchQuery("hello");
        Assert.Equal("hello", state.SearchQuery);

        state.SetSearchQuery(null);
        Assert.Null(state.SearchQuery);
    }

    [Fact]
    public void DataGrid_StateChanged_FiresOnSort()
    {
        var state = new DataGridState<SortTestItem>(
            new SimpleDataSource(), SimpleColumns, SelectionMode.Single);

        bool fired = false;
        state.StateChanged += () => fired = true;
        state.ToggleSort("Name");
        Assert.True(fired);
    }
}
