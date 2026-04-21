using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Charting.Accessibility;
using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for TreeChartDsl builder methods, ForceGraphElement builders,
/// and IChartAccessibilityData implementations — all pure logic, no WinUI.
/// </summary>
public class TreeChartDslTests
{
    // ═══════════════════════════════════════════════════════════════
    // TreeChartElement builders
    // ═══════════════════════════════════════════════════════════════

    private record TreeNode(string Label, TreeNode[]? Children = null);

    private static TreeNode SampleTree =>
        new("Root", [
            new("A", [new("A1"), new("A2")]),
            new("B", [new("B1")]),
        ]);

    [Fact]
    public void TreeChart_Create_SetsRootAndAccessors()
    {
        var chart = ChartDsl.TreeChart(
            SampleTree,
            n => n.Children,
            n => n.Label);
        Assert.NotNull(chart);
    }

    [Fact]
    public void TreeChart_Width_ReturnsSelf()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children);
        var result = chart.Width(800);
        Assert.Same(chart, result);
    }

    [Fact]
    public void TreeChart_Height_ReturnsSelf()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children);
        var result = chart.Height(500);
        Assert.Same(chart, result);
    }

    [Fact]
    public void TreeChart_LinkColor_ReturnsSelf()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children);
        var result = chart.LinkColor("#ff0000");
        Assert.Same(chart, result);
    }

    [Fact]
    public void TreeChart_NodeColor_ReturnsSelf()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children);
        var result = chart.NodeColor("#00ff00");
        Assert.Same(chart, result);
    }

    [Fact]
    public void TreeChart_NodeRadius_ReturnsSelf()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children);
        var result = chart.NodeRadius(10);
        Assert.Same(chart, result);
    }

    [Fact]
    public void TreeChart_OnReady_ReturnsSelf()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children);
        var result = chart.OnReady(_ => { });
        Assert.Same(chart, result);
    }

    [Fact]
    public void TreeChart_FluentChaining()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children, n => n.Label)
            .Width(1000)
            .Height(600)
            .LinkColor("#aaa")
            .NodeColor("#333")
            .NodeRadius(8)
            .Title("Org Chart")
            .Description("Hierarchy of team members");

        // Should get back the same element
        Assert.NotNull(chart);
    }

    // ═══════════════════════════════════════════════════════════════
    // TreeChartElement IChartAccessibilityData
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TreeChart_AccessibilityName_ReturnsTitle()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children)
            .Title("My Tree");
        var accessData = (IChartAccessibilityData)chart;
        Assert.Equal("My Tree", accessData.Name);
    }

    [Fact]
    public void TreeChart_AccessibilityDescription_ReturnsDescription()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children)
            .Description("A test tree chart");
        var accessData = (IChartAccessibilityData)chart;
        Assert.Equal("A test tree chart", accessData.Description);
    }

    [Fact]
    public void TreeChart_ChartTypeName_IsTree()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children);
        var accessData = (IChartAccessibilityData)chart;
        Assert.Equal("Tree", accessData.ChartTypeName);
    }

    [Fact]
    public void TreeChart_Axes_IsEmpty()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children);
        var accessData = (IChartAccessibilityData)chart;
        Assert.Empty(accessData.Axes);
    }

    [Fact]
    public void TreeChart_Viewport_IsNull()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children);
        var accessData = (IChartAccessibilityData)chart;
        Assert.Null(accessData.Viewport);
    }

    [Fact]
    public void TreeChart_Series_IncludesAllNodes()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children, n => n.Label);
        var accessData = (IChartAccessibilityData)chart;
        var series = accessData.Series;
        Assert.Single(series);
        Assert.Equal("Nodes", series[0].Name);
        // Tree has: Root, A, A1, A2, B, B1 = 6 nodes
        Assert.Equal(6, series[0].Points.Count);
    }

    [Fact]
    public void TreeChart_Series_UsesLabelAccessor()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children, n => n.Label);
        var accessData = (IChartAccessibilityData)chart;
        var points = accessData.Series[0].Points;
        Assert.Contains(points, p => p.XLabel == "Root");
        Assert.Contains(points, p => p.XLabel == "A1");
    }

    [Fact]
    public void TreeChart_Series_FallbackLabel_WhenNoLabelAccessor()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children);
        var accessData = (IChartAccessibilityData)chart;
        var points = accessData.Series[0].Points;
        // Without label accessor, uses "Node {i+1}" format
        Assert.Contains(points, p => p.XLabel == "Node 1");
    }

    [Fact]
    public void TreeChart_Series_ValueIsDepth()
    {
        var chart = ChartDsl.TreeChart(SampleTree, n => n.Children, n => n.Label);
        var accessData = (IChartAccessibilityData)chart;
        var points = accessData.Series[0].Points;
        var root = points.First(p => p.XLabel == "Root");
        var a1 = points.First(p => p.XLabel == "A1");
        Assert.Equal(0, root.YValue); // Root is depth 0
        Assert.Equal(2, a1.YValue);   // A1 is depth 2
    }

    // ═══════════════════════════════════════════════════════════════
    // ForceGraphElement builders
    // ═══════════════════════════════════════════════════════════════

    private static IReadOnlyList<ForceNode> SampleNodes =>
    [
        new() { Label = "A" },
        new() { Label = "B" },
        new() { Label = "C" },
    ];

    private static IReadOnlyList<ForceLink> SampleLinks =>
    [
        new(0, 1),
        new(1, 2),
    ];

    [Fact]
    public void ForceGraph_Create_SetsNodesAndLinks()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        Assert.NotNull(graph);
    }

    [Fact]
    public void ForceGraph_Width_ReturnsSelf()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        var result = graph.Width(800);
        Assert.Same(graph, result);
    }

    [Fact]
    public void ForceGraph_Height_ReturnsSelf()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        var result = graph.Height(500);
        Assert.Same(graph, result);
    }

    [Fact]
    public void ForceGraph_LinkColor_ReturnsSelf()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        var result = graph.LinkColor("#ff0000");
        Assert.Same(graph, result);
    }

    [Fact]
    public void ForceGraph_NodeColor_ReturnsSelf()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        var result = graph.NodeColor("#00ff00");
        Assert.Same(graph, result);
    }

    [Fact]
    public void ForceGraph_Charge_ReturnsSelf()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        var result = graph.Charge(-200);
        Assert.Same(graph, result);
    }

    [Fact]
    public void ForceGraph_Distance_ReturnsSelf()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        var result = graph.Distance(100);
        Assert.Same(graph, result);
    }

    [Fact]
    public void ForceGraph_Iterations_ReturnsSelf()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        var result = graph.Iterations(500);
        Assert.Same(graph, result);
    }

    [Fact]
    public void ForceGraph_FluentChaining()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks)
            .Width(1000)
            .Height(600)
            .LinkColor("#ddd")
            .NodeColor("#444")
            .Charge(-150)
            .Distance(80)
            .Iterations(200)
            .Title("Network Graph")
            .Description("Social network connections");
        Assert.NotNull(graph);
    }

    // ═══════════════════════════════════════════════════════════════
    // ForceGraphElement IChartAccessibilityData
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ForceGraph_AccessibilityName_ReturnsTitle()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks)
            .Title("My Graph");
        var accessData = (IChartAccessibilityData)graph;
        Assert.Equal("My Graph", accessData.Name);
    }

    [Fact]
    public void ForceGraph_AccessibilityDescription_ReturnsDescription()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks)
            .Description("Test graph");
        var accessData = (IChartAccessibilityData)graph;
        Assert.Equal("Test graph", accessData.Description);
    }

    [Fact]
    public void ForceGraph_ChartTypeName_IsForceGraph()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        var accessData = (IChartAccessibilityData)graph;
        Assert.Equal("Force graph", accessData.ChartTypeName);
    }

    [Fact]
    public void ForceGraph_Series_ExposesSingleSeries()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        var accessData = (IChartAccessibilityData)graph;
        var series = accessData.Series;
        Assert.Single(series);
        Assert.Equal("Edges", series[0].Name);
        Assert.Equal(2, series[0].Points.Count); // 2 links
    }

    [Fact]
    public void ForceGraph_Series_UsesNodeLabels()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        var accessData = (IChartAccessibilityData)graph;
        var points = accessData.Series[0].Points;
        Assert.Contains(points, p => p.XLabel.Contains("A") && p.XLabel.Contains("B"));
    }

    [Fact]
    public void ForceGraph_Axes_IsEmpty()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        var accessData = (IChartAccessibilityData)graph;
        Assert.Empty(accessData.Axes);
    }

    [Fact]
    public void ForceGraph_OnReady_ReturnsSelf()
    {
        var graph = ChartDsl.ForceGraph(SampleNodes, SampleLinks);
        var result = graph.OnReady(_ => { });
        Assert.Same(graph, result);
    }
}
