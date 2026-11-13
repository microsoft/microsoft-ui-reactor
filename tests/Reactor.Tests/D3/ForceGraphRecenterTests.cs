// Tests for ForceGraphElement.RecenterNodes (FitParent resize re-centre, issue #174)

using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.D3;

public class ForceGraphRecenterTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void FreeNodes_ShiftedByExactlyDxDy()
    {
        var nodes = new[]
        {
            new ForceNode { X = 10, Y = 20 },
            new ForceNode { X = -5, Y = 7.5 },
        };

        ForceGraphElement.RecenterNodes(nodes, 50, -30);

        Assert.Equal(60, nodes[0].X, Tol);
        Assert.Equal(-10, nodes[0].Y, Tol);
        Assert.Equal(45, nodes[1].X, Tol);
        Assert.Equal(-22.5, nodes[1].Y, Tol);
    }

    [Fact]
    public void FixedNodes_Unmoved()
    {
        var node = new ForceNode { X = 100, Y = 200, Fx = 100, Fy = 200 };

        ForceGraphElement.RecenterNodes([node], 50, 40);

        Assert.Equal(100, node.X, Tol);
        Assert.Equal(200, node.Y, Tol);
    }

    [Fact]
    public void FxSet_FyNull_ShiftsYOnly()
    {
        var node = new ForceNode { X = 100, Y = 200, Fx = 100 };

        ForceGraphElement.RecenterNodes([node], 50, 40);

        Assert.Equal(100, node.X, Tol);
        Assert.Equal(240, node.Y, Tol);
    }

    [Fact]
    public void FySet_FxNull_ShiftsXOnly()
    {
        var node = new ForceNode { X = 100, Y = 200, Fy = 200 };

        ForceGraphElement.RecenterNodes([node], 50, 40);

        Assert.Equal(150, node.X, Tol);
        Assert.Equal(200, node.Y, Tol);
    }

    [Fact]
    public void MixedList_ShiftsOnlyFreeAxes()
    {
        var free = new ForceNode { X = 1, Y = 2 };
        var fixedBoth = new ForceNode { X = 3, Y = 4, Fx = 3, Fy = 4 };
        var fixedX = new ForceNode { X = 5, Y = 6, Fx = 5 };

        ForceGraphElement.RecenterNodes([free, fixedBoth, fixedX], -10, 20);

        Assert.Equal(-9, free.X, Tol);
        Assert.Equal(22, free.Y, Tol);
        Assert.Equal(3, fixedBoth.X, Tol);
        Assert.Equal(4, fixedBoth.Y, Tol);
        Assert.Equal(5, fixedX.X, Tol);
        Assert.Equal(26, fixedX.Y, Tol);
    }

    private static List<ForceNode> SixNodes() =>
        Enumerable.Range(0, 6).Select(i => new ForceNode { Label = $"n{i}" }).ToList();

    private static List<ForceLink> Ring() =>
        [new(0, 1), new(1, 2), new(2, 3), new(3, 4), new(4, 5), new(5, 0)];

    [Fact]
    public void StableBuilder_WithoutFitParent_ResizeLeavesNodesAndCentre()
    {
        var chart = Charts.ForceGraph(SixNodes(), Ring()).Width(400).Height(300);
        var sim = chart.EnsureSimulation();
        sim.Center(123, 45); // a centre configured by the caller through the handle
        var before = sim.Nodes.Select(n => (n.X, n.Y)).ToList();

        chart.Width(600).Height(450);
        var after = chart.EnsureSimulation();

        Assert.Same(sim, after);
        Assert.Equal(before, after.Nodes.Select(n => (n.X, n.Y)).ToList());
        Assert.Equal((123.0, 45.0), after.CenterPoint);
    }

    [Fact]
    public void StableBuilder_UnderFitParent_ResizeRecentres()
    {
        var chart = Charts.ForceGraph(SixNodes(), Ring()).Width(400).Height(300);
        _ = chart.FitParent();
        var sim = chart.EnsureSimulation();
        double mx0 = sim.Nodes.Average(n => n.X), my0 = sim.Nodes.Average(n => n.Y);

        chart.Width(600).Height(450);
        var after = chart.EnsureSimulation();

        Assert.Same(sim, after);
        Assert.Equal(100, after.Nodes.Average(n => n.X) - mx0, Tol);
        Assert.Equal(75, after.Nodes.Average(n => n.Y) - my0, Tol);
        Assert.Equal((300.0, 225.0), after.CenterPoint);
    }
}
