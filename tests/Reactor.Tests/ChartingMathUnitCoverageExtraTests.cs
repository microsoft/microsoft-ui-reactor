// Unit coverage for pure-managed D3 math: partition hierarchy helpers, radial
// generator setters/undefined arm, and the Delaunay over-cap guard + cell clipping.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.D3;

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
    public void Partition_RelayoutOverload_RoundsAndPreservesInstance()
    {
        var layout = PartitionLayout.Create<FileItem>().Size(120, 90).SetPadding(1).SetRound(true);

        var root = layout.Layout(SampleTree(), n => n.Children, n => n.Size);

        // Summed leaf values bubble up to the root.
        Assert.Equal(60, root.Value);

        // SetRound(true) snapped every bound to an integer.
        Assert.Equal(Math.Round(root.X0), root.X0);
        Assert.Equal(Math.Round(root.X1), root.X1);
        Assert.Equal(Math.Round(root.Y1), root.Y1);

        // maxDepth = 2 -> depthHeight = 90 / 3 = 30; root spans one band.
        Assert.Equal(30, root.Height);

        // The compute-only overload re-lays out the same instance in place.
        var again = layout.Layout(root);
        Assert.Same(root, again);
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
