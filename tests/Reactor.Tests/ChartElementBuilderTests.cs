using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Charting.Accessibility;
using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for TreeChartElement, ForceGraphElement, and PieChartElement builder chains.
/// Exercises the fluent API and accessibility metadata without WinUI rendering.
/// </summary>
public class ChartElementBuilderTests
{
    // ══ TreeChartElement ══════════════════════════════════════

    private record TreeNode(string Name, List<TreeNode>? Children = null);

    private static readonly TreeNode SampleTree = new("Root", new()
    {
        new("A", new() { new("A1"), new("A2") }),
        new("B")
    });

    [Fact]
    public void TreeChart_Factory()
    {
        var tree = ChartDsl.TreeChart(SampleTree, n => n.Children, n => n.Name);
        Assert.NotNull(tree);
    }

    [Fact]
    public void TreeChart_Width_Height()
    {
        var tree = ChartDsl.TreeChart(SampleTree, n => n.Children)
            .Width(800).Height(600);
        Assert.NotNull(tree);
    }

    [Fact]
    public void TreeChart_LinkColor_NodeColor()
    {
        var tree = ChartDsl.TreeChart(SampleTree, n => n.Children)
            .LinkColor("#ff0000")
            .NodeColor("#00ff00");
        Assert.NotNull(tree);
    }

    [Fact]
    public void TreeChart_NodeRadius()
    {
        var tree = ChartDsl.TreeChart(SampleTree, n => n.Children)
            .NodeRadius(10);
        Assert.NotNull(tree);
    }

    [Fact]
    public void TreeChart_Title_Description()
    {
        var tree = ChartDsl.TreeChart(SampleTree, n => n.Children)
            .Title("Org Chart")
            .Description("Company hierarchy");
        Assert.NotNull(tree);
    }

    [Fact]
    public void TreeChart_OnReady()
    {
        var tree = ChartDsl.TreeChart(SampleTree, n => n.Children)
            .OnReady(h => { });
        Assert.NotNull(tree);
    }

    [Fact]
    public void TreeChart_FullChain()
    {
        var tree = ChartDsl.TreeChart(SampleTree, n => n.Children, n => n.Name)
            .Width(1000)
            .Height(800)
            .LinkColor("#ccc")
            .NodeColor("#333")
            .NodeRadius(8)
            .Title("Full Chain Test")
            .Description("Testing all modifiers");
        Assert.NotNull(tree);
    }

    // ══ ForceGraphElement ═════════════════════════════════════

    [Fact]
    public void ForceGraph_Factory()
    {
        var graph = ChartDsl.ForceGraph(
            new[] { new ForceNode { Label = "A" }, new ForceNode { Label = "B" } },
            new[] { new ForceLink(0, 1) });
        Assert.NotNull(graph);
    }

    [Fact]
    public void ForceGraph_Width_Height()
    {
        var graph = ChartDsl.ForceGraph(
            new[] { new ForceNode { Label = "A" } }, Array.Empty<ForceLink>())
            .Width(800).Height(600);
        Assert.NotNull(graph);
    }

    [Fact]
    public void ForceGraph_LinkColor_NodeColor()
    {
        var graph = ChartDsl.ForceGraph(
            new[] { new ForceNode { Label = "A" } }, Array.Empty<ForceLink>())
            .LinkColor("#aaa")
            .NodeColor("#f00");
        Assert.NotNull(graph);
    }

    [Fact]
    public void ForceGraph_Charge_Distance_Iterations()
    {
        var graph = ChartDsl.ForceGraph(
            new[] { new ForceNode { Label = "A" } }, Array.Empty<ForceLink>())
            .Charge(-200)
            .Distance(80)
            .Iterations(500);
        Assert.NotNull(graph);
    }

    [Fact]
    public void ForceGraph_Title_Description()
    {
        var graph = ChartDsl.ForceGraph(
            new[] { new ForceNode { Label = "A" } }, Array.Empty<ForceLink>())
            .Title("Network")
            .Description("Network graph");
        Assert.NotNull(graph);
    }

    [Fact]
    public void ForceGraph_OnReady()
    {
        var graph = ChartDsl.ForceGraph(
            new[] { new ForceNode { Label = "A" } }, Array.Empty<ForceLink>())
            .OnReady(h => { });
        Assert.NotNull(graph);
    }

    // ══ PieChartElement ══════════════════════════════════════

    private record PieItem(string Label, double Value);
    private static readonly List<PieItem> PieData = new()
    {
        new("A", 30), new("B", 50), new("C", 20)
    };

    [Fact]
    public void PieChart_Width_Height()
    {
        var pie = ChartDsl.PieChart(PieData, d => d.Value)
            .Width(400).Height(400);
        Assert.NotNull(pie);
    }

    [Fact]
    public void PieChart_InnerRadius_PadAngle()
    {
        var pie = ChartDsl.PieChart(PieData, d => d.Value)
            .InnerRadius(50)
            .PadAngle(0.05);
        Assert.NotNull(pie);
    }

    [Fact]
    public void PieChart_SetColors()
    {
        var pie = ChartDsl.PieChart(PieData, d => d.Value)
            .SetColors(new D3Color(255, 0, 0), new D3Color(0, 255, 0), new D3Color(0, 0, 255));
        Assert.NotNull(pie);
    }

    [Fact]
    public void PieChart_Title_Description()
    {
        var pie = ChartDsl.PieChart(PieData, d => d.Value)
            .Title("Distribution")
            .Description("Shows category distribution");
        Assert.NotNull(pie);
    }

    [Fact]
    public void PieChart_SeriesNames()
    {
        var pie = ChartDsl.PieChart(PieData, d => d.Value)
            .SeriesNames("Category A", "Category B", "Category C");
        Assert.NotNull(pie);
    }

    [Fact]
    public void PieChart_DataLabel()
    {
        var pie = ChartDsl.PieChart(PieData, d => d.Value)
            .DataLabel((item, idx) => $"{item.Label}: {item.Value}");
        Assert.NotNull(pie);
    }

    [Fact]
    public void PieChart_Palette()
    {
        var pie = ChartDsl.PieChart(PieData, d => d.Value)
            .Palette(ChartPalette.OkabeIto);
        Assert.NotNull(pie);
    }

    [Fact]
    public void PieChart_ColorOnly()
    {
        var pie = ChartDsl.PieChart(PieData, d => d.Value)
            .ColorOnly();
        Assert.True(pie.IsColorOnly);
    }

    [Fact]
    public void PieChart_OnReady()
    {
        var pie = ChartDsl.PieChart(PieData, d => d.Value)
            .OnReady(h => { });
        Assert.NotNull(pie);
    }

    [Fact]
    public void PieChart_FullChain()
    {
        var pie = ChartDsl.PieChart(PieData, d => d.Value, d => d.Label)
            .Width(500)
            .Height(500)
            .InnerRadius(100)
            .PadAngle(0.03)
            .Title("Sales")
            .Description("Quarterly sales")
            .SeriesNames("Q1", "Q2", "Q3")
            .DataLabel((d, i) => $"{d.Label}: {d.Value}")
            .Palette(ChartPalette.IBM);
        Assert.NotNull(pie);
    }

    // ══ ForceNode / ForceLink records ════════════════════════

    [Fact]
    public void ForceNode_Properties()
    {
        var node = new ForceNode { Label = "test", Radius = 12 };
        Assert.Equal("test", node.Label);
        Assert.Equal(12, node.Radius);
    }

    [Fact]
    public void ForceLink_Properties()
    {
        var link = new ForceLink(0, 1, Strength: 2, Distance: 80);
        Assert.Equal(0, link.Source);
        Assert.Equal(1, link.Target);
        Assert.Equal(2, link.Strength);
        Assert.Equal(80, link.Distance);
    }
}
