// Unit coverage for pure-managed D3 math: partition hierarchy helpers, radial
// generator setters/undefined arm, and the Delaunay over-cap guard + cell clipping.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class ChartingMathUnitCoverageExtraTests
{
    private record FileItem(string Name, double Size, FileItem[]? Children = null);

    private static FileItem SampleTree() => new("root", 0,
    [
        new FileItem("dir", 0,
        [
            new FileItem("a1", 10),
            new FileItem("a2", 20),
        ]),
        new FileItem("b", 30),
    ]);

    // ── PartitionLayout / PartitionNode ─────────────────────────────────────

    [Fact]
    public void Partition_RelayoutOverload_SumsValuesAndPreservesInstance()
    {
        var layout = PartitionLayout.Create<FileItem>().Size(120, 90).SetPadding(1);

        var root = layout.Layout(SampleTree(), n => n.Children, n => n.Size);

        // Summed leaf values bubble up to the root (10 + 20 + 30).
        Assert.Equal(60, root.Value);

        // maxDepth = 2 -> depthHeight = 90 / 3 = 30; root spans one band.
        Assert.Equal(30, root.Height);

        // The compute-only overload re-lays out the same instance in place.
        var again = layout.Layout(root);
        Assert.Same(root, again);
    }

    [Fact]
    public void Partition_SetRound_SnapsFractionalBoundsToIntegers()
    {
        // Size(100,90)+padding(1) puts a1's right edge at 47*(1/3)+2 = 17.666...,
        // so rounding has an observable effect (a grid that divides evenly would not).
        var unrounded = PartitionLayout.Create<FileItem>().Size(100, 90).SetPadding(1)
            .Layout(SampleTree(), n => n.Children, n => n.Size);
        var rawLeaf = unrounded.Children[0].Children[0]; // "a1"
        Assert.NotEqual(Math.Round(rawLeaf.X1), rawLeaf.X1); // fractional before rounding

        var rounded = PartitionLayout.Create<FileItem>().Size(100, 90).SetPadding(1).SetRound(true)
            .Layout(SampleTree(), n => n.Children, n => n.Size);

        // RoundAll walked the whole tree: every bound of every node is now integral.
        foreach (var node in rounded.Descendants())
        {
            Assert.Equal(Math.Round(node.X0), node.X0);
            Assert.Equal(Math.Round(node.X1), node.X1);
            Assert.Equal(Math.Round(node.Y0), node.Y0);
            Assert.Equal(Math.Round(node.Y1), node.Y1);
        }

        // The previously-fractional leaf is snapped to exactly its rounded integer.
        var roundedLeaf = rounded.Children[0].Children[0];
        Assert.Equal(Math.Round(rawLeaf.X1), roundedLeaf.X1);
    }

    [Fact]
    public void PartitionNode_TopAncestor_AndAncestors_WalkParentChain()
    {
        var layout = PartitionLayout.Create<FileItem>().Size(100, 100);
        var root = layout.Layout(SampleTree(), n => n.Children, n => n.Size);

        var dir = root.Children[0];   // depth 1
        var a1 = dir.Children[0];     // depth 2 leaf

        // TopAncestor returns the direct child of the root for a deep node,
        // and the node itself when it has no grandparent.
        Assert.Same(dir, a1.TopAncestor);
        Assert.Same(dir, dir.TopAncestor);
        Assert.Same(root, root.TopAncestor);

        // Ancestors walks leaf -> root inclusive.
        var chain = a1.Ancestors().Select(n => n.Data.Name).ToArray();
        Assert.Equal(new[] { "a1", "dir", "root" }, chain);
    }

    // ── Radial generators ───────────────────────────────────────────────────

    [Fact]
    public void RadialLine_Setters_AndUndefinedPoint_ProduceGappedPath()
    {
        var gen = RadialLineGenerator.Create()
            .SetAngle((d, _) => d.angle)
            .SetRadius((d, _) => d.radius)
            .SetDefined((_, i) => i != 1) // index 1 undefined -> the (0,0) placeholder arm
            .SetDigits(2);

        var data = new (double angle, double radius)[] { (0, 100), (1, 80), (2, 120) };
        var path = gen.Generate(data);

        Assert.NotNull(path);
        Assert.StartsWith("M", path);
    }

    [Fact]
    public void RadialArea_Setters_ProduceClosedPath()
    {
        var gen = RadialAreaGenerator.Create<(double angle, double value)>(
                d => d.angle, _ => 10, d => d.value)
            .SetInnerRadius((_, _) => 10)
            .SetOuterRadius((d, _) => d.value)
            .SetStartAngle((d, _) => d.angle)
            .SetEndAngle((d, _) => d.angle)
            .SetDefined((_, _) => true)
            .SetDigits(2)
            .SetAngle((d, _) => d.angle);

        var data = new (double angle, double value)[]
        {
            (0, 50), (Math.PI / 2, 60), (Math.PI, 70),
        };

        var path = gen.Generate(data);

        Assert.NotNull(path);
        Assert.Contains("Z", path);
    }

    [Fact]
    public void RadialLink_Setters_ProduceQuadraticPath()
    {
        var gen = new RadialLinkGenerator<(double a, double r)>(
                d => (d.a, d.r), d => (d.a, d.r))
            .SetSource(d => (d.a, d.r))
            .SetTarget(d => (d.a + Math.PI, d.r * 2))
            .SetDigits(2);

        var path = gen.Generate((1.0, 50.0));

        Assert.NotNull(path);
        Assert.Contains("Q", path);
    }

    // ── Delaunay ────────────────────────────────────────────────────────────

    [Fact]
    public void Delaunay_OverPointCap_ReturnsEmptyTriangulation()
    {
        var pts = new (double x, double y)[Delaunay.MaxPointsForDelaunay + 1];
        for (int i = 0; i < pts.Length; i++) pts[i] = (i % 100, i / 100);

        var d = Delaunay.From(pts);

        // Over-cap inputs bail out with no triangles rather than freezing the UI.
        Assert.Empty(d.Triangles);
        Assert.Empty(d.Halfedges);
        Assert.Equal(pts.Length, d.Hull.Length);
    }

    [Fact]
    public void Voronoi_AsymmetricBounds_ClipCellsWithinBox()
    {
        var pts = new List<(double x, double y)>();
        for (int gx = 0; gx <= 100; gx += 20)
            for (int gy = 0; gy <= 100; gy += 20)
                pts.Add((gx, gy));

        var d = Delaunay.From(pts);
        // Right edge x<=50 cuts through the interior circumcenters, forcing cells
        // near the boundary to be clipped (straddling a single edge).
        var v = d.Voronoi(0, 0, 50, 100);

        const double eps = 1e-6;
        for (int i = 0; i < pts.Count; i++)
        {
            var cell = v.CellPolygon(i);
            if (cell is null) continue;
            foreach (var (x, y) in cell)
            {
                Assert.InRange(x, 0 - eps, 50 + eps);
                Assert.InRange(y, 0 - eps, 100 + eps);
            }
        }
    }
}
