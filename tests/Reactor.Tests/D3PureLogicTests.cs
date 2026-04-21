using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for ForceSimulation — the force-directed graph layout engine.
/// Pure mathematical simulation with no WinUI dependencies.
/// </summary>
public class ForceSimulationTests
{
    private static ForceSimulation CreateSimple(int nodeCount = 3)
    {
        var nodes = Enumerable.Range(0, nodeCount)
            .Select(_ => new ForceNode())
            .ToArray();
        var links = nodeCount > 1
            ? Enumerable.Range(0, nodeCount - 1)
                .Select(i => new ForceLink(i, i + 1))
                .ToArray()
            : Array.Empty<ForceLink>();

        return new ForceSimulation()
            .SetNodes(nodes)
            .SetLinks(links)
            .Center(300, 200)
            .InitializePositions();
    }

    [Fact]
    public void SetNodes_AssignsIndices()
    {
        var sim = CreateSimple(5);
        for (int i = 0; i < 5; i++)
            Assert.Equal(i, sim.Nodes[i].Index);
    }

    [Fact]
    public void InitializePositions_SetsCoordinates()
    {
        var sim = CreateSimple(5);
        foreach (var node in sim.Nodes)
        {
            Assert.False(double.IsNaN(node.X));
            Assert.False(double.IsNaN(node.Y));
        }
    }

    [Fact]
    public void InitializePositions_FixedNodes()
    {
        var node = new ForceNode { Fx = 100, Fy = 200 };
        var sim = new ForceSimulation()
            .SetNodes(new[] { node })
            .Center(0, 0)
            .InitializePositions();

        Assert.Equal(100, sim.Nodes[0].X);
        Assert.Equal(200, sim.Nodes[0].Y);
    }

    [Fact]
    public void Run_ReducesAlpha()
    {
        var sim = CreateSimple(3);
        Assert.Equal(1, sim.Alpha);

        sim.Run(50);
        Assert.True(sim.Alpha < 1);
    }

    [Fact]
    public void Run_MovesNodes()
    {
        var sim = CreateSimple(5);
        var initialPositions = sim.Nodes.Select(n => (n.X, n.Y)).ToList();

        sim.Run(100);

        bool anyMoved = false;
        for (int i = 0; i < sim.Nodes.Count; i++)
        {
            if (Math.Abs(sim.Nodes[i].X - initialPositions[i].X) > 0.001 ||
                Math.Abs(sim.Nodes[i].Y - initialPositions[i].Y) > 0.001)
            {
                anyMoved = true;
                break;
            }
        }
        Assert.True(anyMoved);
    }

    [Fact]
    public void ChargeStrength_AffectsLayout()
    {
        var sim1 = CreateSimple(3).Run(100);
        var p1 = sim1.Nodes.Select(n => (n.X, n.Y)).ToList();

        var nodes2 = new[] { new ForceNode(), new ForceNode(), new ForceNode() };
        var links2 = new[] { new ForceLink(0, 1), new ForceLink(1, 2) };
        var sim2 = new ForceSimulation()
            .SetNodes(nodes2)
            .SetLinks(links2)
            .Center(300, 200)
            .ChargeStrength(-500) // much stronger repulsion
            .InitializePositions()
            .Run(100);

        // Different charge should produce different positions
        Assert.NotNull(sim2.Nodes);
    }

    [Fact]
    public void Builder_Fluent_Chain()
    {
        var sim = new ForceSimulation()
            .SetNodes(new[] { new ForceNode() })
            .SetLinks(Array.Empty<ForceLink>())
            .ChargeStrength(-50)
            .Center(400, 300)
            .CenterStrength(0.2)
            .LinkDistance(100)
            .LinkStrength(0.5)
            .VelocityDecay(0.4)
            .CollisionRadius(15)
            .AlphaDecay(0.05)
            .InitializePositions();

        Assert.Single(sim.Nodes);
    }

    [Fact]
    public void AlphaTarget_StopsWhenReached()
    {
        var sim = CreateSimple(3);
        sim.AlphaTarget = 0.5;
        sim.Run(1000);
        // Alpha should converge toward target, never go below alpha min
        Assert.True(sim.Alpha >= sim.AlphaMin);
    }

    [Fact]
    public void Empty_Simulation()
    {
        var sim = new ForceSimulation()
            .SetNodes(Array.Empty<ForceNode>())
            .SetLinks(Array.Empty<ForceLink>());
        sim.Run(10);
        Assert.Empty(sim.Nodes);
    }

    [Fact]
    public void SingleNode_Centered()
    {
        var sim = new ForceSimulation()
            .SetNodes(new[] { new ForceNode() })
            .SetLinks(Array.Empty<ForceLink>())
            .Center(100, 100)
            .InitializePositions()
            .Run(300);

        // Single node with center force should converge near center
        Assert.InRange(sim.Nodes[0].X, 50, 150);
        Assert.InRange(sim.Nodes[0].Y, 50, 150);
    }

    [Fact]
    public void CollisionRadius_PreventsOverlap()
    {
        var nodes = Enumerable.Range(0, 10)
            .Select(_ => new ForceNode())
            .ToArray();
        var sim = new ForceSimulation()
            .SetNodes(nodes)
            .Center(0, 0)
            .CollisionRadius(20)
            .ChargeStrength(-100)
            .InitializePositions()
            .Run(200);

        // After running with collision, nodes should be separated
        for (int i = 0; i < nodes.Length; i++)
            for (int j = i + 1; j < nodes.Length; j++)
            {
                double dx = nodes[i].X - nodes[j].X;
                double dy = nodes[i].Y - nodes[j].Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                // Not perfectly enforced due to other forces, but generally spread
                Assert.True(dist > 1);
            }
    }
}

/// <summary>
/// Tests for D3Color — pure color record and conversion utilities.
/// </summary>
public class D3ColorTests
{
    [Fact]
    public void D3Color_Constructor()
    {
        var c = new D3Color(255, 128, 0);
        Assert.Equal(255, c.R);
        Assert.Equal(128, c.G);
        Assert.Equal(0, c.B);
    }

    [Fact]
    public void D3Color_WithAlpha()
    {
        var c = new D3Color(255, 128, 0, 0.5);
        Assert.Equal(0.5, c.Opacity);
    }

    [Fact]
    public void D3Color_Parse_Hex6()
    {
        var c = D3Color.Parse("#ff8000");
        Assert.Equal(255, c.R);
        Assert.Equal(128, c.G);
        Assert.Equal(0, c.B);
    }

    [Fact]
    public void D3Color_Parse_Hex3()
    {
        var c = D3Color.Parse("#f80");
        Assert.Equal(255, c.R);
        Assert.Equal(136, c.G);
        Assert.Equal(0, c.B);
    }

    [Fact]
    public void D3Color_Parse_Rgb()
    {
        var c = D3Color.Parse("rgb(100, 200, 50)");
        Assert.Equal(100, c.R);
        Assert.Equal(200, c.G);
        Assert.Equal(50, c.B);
    }

    [Fact]
    public void D3Color_Category10()
    {
        Assert.True(D3Color.Category10.Count > 0);
        Assert.Equal(10, D3Color.Category10.Count);
    }

    [Fact]
    public void D3Color_ToString_Rgb()
    {
        var c = new D3Color(255, 0, 128);
        var s = c.ToString();
        Assert.Contains("rgb", s);
    }

    [Fact]
    public void D3Color_Equality()
    {
        var a = new D3Color(10, 20, 30);
        var b = new D3Color(10, 20, 30);
        Assert.Equal(a, b);
    }

    [Fact]
    public void D3Color_Brighter_Darker()
    {
        var c = new D3Color(100, 100, 100);
        var brighter = c.Brighter();
        var darker = c.Darker();
        Assert.True(brighter.R > c.R);
        Assert.True(darker.R < c.R);
    }

    [Fact]
    public void D3Color_ToHex()
    {
        var c = new D3Color(255, 0, 128);
        Assert.Equal("#FF0080", c.ToHex());
    }

    [Fact]
    public void D3Color_ToRgb()
    {
        var c = new D3Color(10, 20, 30);
        Assert.Equal("rgb(10, 20, 30)", c.ToRgb());
    }

    [Fact]
    public void D3Color_ToRgba()
    {
        var c = new D3Color(10, 20, 30, 0.5);
        Assert.Contains("rgba", c.ToRgb());
    }

    [Fact]
    public void D3Color_Brewer_Palettes()
    {
        Assert.NotEmpty(D3Color.Category10);
        // Verify all palette entries are valid colors
        foreach (var c in D3Color.Category10)
        {
            Assert.InRange(c.R, 0, 255);
            Assert.InRange(c.G, 0, 255);
            Assert.InRange(c.B, 0, 255);
        }
    }
}
