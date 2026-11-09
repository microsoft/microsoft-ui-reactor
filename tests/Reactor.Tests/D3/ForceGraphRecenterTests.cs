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
}
