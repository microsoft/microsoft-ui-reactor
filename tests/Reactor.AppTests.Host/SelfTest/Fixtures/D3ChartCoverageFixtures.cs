using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Charting.D3Charts;
using WinShapes = Microsoft.UI.Xaml.Shapes;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Expanded ReactorCharting chart test fixtures covering chart types not yet exercised
/// by the existing D3Fixtures: scatterplot, histogram, donut, horizontal bar,
/// treemap, sunburst, sankey, chord, stacked area, and low-level D3 primitives.
/// </summary>
internal static class D3ChartCoverageFixtures
{
    // ── Scatterplot ────────────────────────────────────────────────────

    internal class Scatterplot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const double cw = 600, ch = 400;
            const double left = 50, top = 30, right = 20, bottom = 30;
            double pw = cw - left - right, ph = ch - top - bottom;

            var rng = new Random(42);
            var points = Enumerable.Range(0, 30)
                .Select(_ => (x: rng.NextDouble() * 100, y: rng.NextDouble() * 100))
                .ToArray();

            var xs = new LinearScale([0, 100], [left, left + pw]).Nice();
            var ys = new LinearScale([0, 100], [top + ph, top]).Nice();
            var fill = Brush(Palette[0], opacity: 0.6);
            var stroke = Brush(Palette[0]);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Scatterplot"),
                    D3Canvas(cw, ch,
                        [.. D3Grid(ys, left, pw),
                         .. D3Axes(xs, ys, left, top, pw, ph),
                         .. points.Select(p =>
                            (Element)(D3Circle(xs.Map(p.x), ys.Map(p.y), 4)
                                with { Fill = fill, Stroke = stroke }))]
                    )
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_Scatter_CanvasCreated", canvas is not null);

            var ellipses = H.FindAllControls<WinShapes.Ellipse>(_ => true);
            H.Check("D3Cov_Scatter_HasPoints", ellipses.Count >= 30);

            // Axes produce TextBlocks for tick labels
            var labels = H.FindAllControls<TextBlock>(_ => true);
            H.Check("D3Cov_Scatter_HasAxisLabels", labels.Count >= 5);
        }
    }

    // ── Histogram ──────────────────────────────────────────────────────

    internal class Histogram(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const double cw = 600, ch = 400;
            const double left = 50, top = 30, right = 20, bottom = 30;
            double pw = cw - left - right, ph = ch - top - bottom;

            var rng = new Random(99);
            var values = Enumerable.Range(0, 100).Select(_ =>
            {
                double u1 = 1.0 - rng.NextDouble();
                double u2 = rng.NextDouble();
                return 50 + 15 * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            }).ToArray();

            var binner = BinGenerator.Create().SetThresholdCount(15);
            var bins = binner.Generate(values);
            int maxCount = bins.Max(b => b.Count);

            var xs = new LinearScale([bins[0].X0, bins[^1].X1], [left, left + pw]);
            var ys = new LinearScale([0, maxCount], [top + ph, top]).Nice();
            var fill = Brush(Palette[2], opacity: 0.7);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Histogram"),
                    D3Canvas(cw, ch,
                        [.. D3Grid(ys, left, pw),
                         .. bins.Select(bin =>
                         {
                             double bx = xs.Map(bin.X0) + 1;
                             double bw = xs.Map(bin.X1) - xs.Map(bin.X0) - 2;
                             double by = ys.Map(bin.Count);
                             double bh = ys.Map(0) - by;
                             return D3Rect(bx, by, bw, bh) with { Fill = fill };
                         }),
                         .. D3Axes(xs, ys, left, top, pw, ph)]
                    )
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_Histogram_CanvasCreated", canvas is not null);

            var rects = H.FindAllControls<WinShapes.Rectangle>(_ => true);
            H.Check("D3Cov_Histogram_HasBins", rects.Count >= 5);
        }
    }

    // ── Donut Chart ────────────────────────────────────────────────────

    internal class DonutChart(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var data = new[] { 40.0, 25.0, 20.0, 15.0 };
            const double cw = 400, ch = 400;
            double cx = cw / 2, cy = ch / 2;

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Donut Chart"),
                    D3Canvas(cw, ch,
                        D3Pie(data, d => d, cx, cy,
                            outerRadius: 150, innerRadius: 80,
                            padAngle: 0.03, sort: false)
                    )
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_Donut_CanvasCreated", canvas is not null);

            var paths = H.FindAllControls<WinShapes.Path>(_ => true);
            H.Check("D3Cov_Donut_HasArcs", paths.Count >= 4);
        }
    }

    // ── Horizontal Bar Chart ───────────────────────────────────────────

    internal class HorizontalBarChart(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            string[] categories = ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];
            double[] values = [85, 62, 94, 45, 73];

            const double cw = 600, ch = 300;
            const double left = 80, top = 20, right = 20, bottom = 30;
            double pw = cw - left - right, ph = ch - top - bottom;

            var band = BandScale.Create(categories).SetRange(top, top + ph).SetPaddingInner(0.2);
            var xs = new LinearScale([0, 100], [left, left + pw]).Nice();
            var fill = Brush(Palette[1]);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Horizontal Bar Chart"),
                    D3Canvas(cw, ch,
                        [.. categories.Select((cat, i) =>
                            (Element)(D3Rect(left, band.Map(cat), xs.Map(values[i]) - left, band.Bandwidth)
                                with { Fill = fill, RadiusX = 2, RadiusY = 2 })),
                         .. categories.Select((cat, i) =>
                            (Element)TextRight(0, band.Map(cat) + band.Bandwidth / 2 - 7, cat, left - 8, 10, Gray(40)))]
                    )
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_HBar_CanvasCreated", canvas is not null);

            var rects = H.FindAllControls<WinShapes.Rectangle>(_ => true);
            H.Check("D3Cov_HBar_HasBars", rects.Count >= 5);

            var labels = H.FindAllControls<TextBlock>(tb =>
                categories.Contains(tb.Text));
            H.Check("D3Cov_HBar_HasLabels", labels.Count >= 5);
        }
    }

    // ── Treemap ────────────────────────────────────────────────────────

    private record FileNode(string Name, double Size = 0, FileNode[]? Children = null);

    internal class Treemap(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var data = new FileNode("root", 0, [
                new("src", 0, [
                    new("App.cs", 12),
                    new("Main.cs", 8),
                    new("Components", 0, [
                        new("Header.cs", 15),
                        new("Footer.cs", 10),
                        new("Grid.cs", 25),
                    ]),
                ]),
                new("tests", 0, [
                    new("AppTests.cs", 18),
                    new("GridTests.cs", 22),
                ]),
            ]);

            const double cw = 600, ch = 400;
            var treemap = TreemapLayout.Create<FileNode>()
                .Size(cw, ch)
                .SetPadding(2)
                .SetPaddingInner(2);
            var root = treemap.Hierarchy(data, n => n.Children, n => n.Size);
            treemap.Layout(root);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Treemap"),
                    D3Canvas(cw, ch,
                        [.. root.Leaves()
                            .Where(leaf => leaf.Width >= 1 && leaf.Height >= 1)
                            .Select(leaf =>
                            {
                                int ci = root.Children.IndexOf(leaf.TopAncestor);
                                var fill = Brush(Palette[ci % Palette.Count], opacity: 0.75);
                                return (Element)(D3Rect(leaf.X0, leaf.Y0, leaf.Width, leaf.Height)
                                    with { Fill = fill, RadiusX = 2, RadiusY = 2 });
                            })]
                    )
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_Treemap_CanvasCreated", canvas is not null);

            var rects = H.FindAllControls<WinShapes.Rectangle>(_ => true);
            // 7 leaf nodes: App.cs, Main.cs, Header.cs, Footer.cs, Grid.cs, AppTests.cs, GridTests.cs
            H.Check("D3Cov_Treemap_HasLeaves", rects.Count >= 7);
        }
    }

    // ── Sunburst ───────────────────────────────────────────────────────

    private record DiskNode(string Name, double Size = 0, DiskNode[]? Children = null);

    internal class Sunburst(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var data = new DiskNode("root", 0, [
                new("Users", 0, [
                    new("Docs", 450),
                    new("Photos", 1200),
                ]),
                new("Programs", 0, [
                    new("Editor", 400),
                    new("Browser", 300),
                ]),
                new("System", 0, [
                    new("Core", 1800),
                    new("Temp", 400),
                ]),
            ]);

            const double cw = 400, ch = 400;
            double cx = cw / 2, cy = ch / 2;
            double maxRadius = cw / 2 - 10;
            double totalAngleWidth = 1, totalHeightNorm = 1;

            var partition = PartitionLayout.Create<DiskNode>().Size(totalAngleWidth, totalHeightNorm);
            var root = partition.Layout(data, n => n.Children, n => n.Size);
            var allNodes = root.Descendants().ToList();

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Sunburst"),
                    D3Canvas(cw, ch,
                        [.. allNodes
                            .Where(node => node.Parent != null)
                            .Select(node =>
                            {
                                var (startAngle, endAngle, innerRadius, outerRadius) =
                                    node.ToPolar(totalAngleWidth, totalHeightNorm, maxRadius);
                                int ci = root.Children.IndexOf(node.TopAncestor);
                                var fill = Brush(Palette[ci % Palette.Count], opacity: 0.7);
                                return (Element)D3ArcPath(startAngle, endAngle, cx, cy,
                                    innerRadius: innerRadius, outerRadius: outerRadius,
                                    fill: fill, stroke: Gray(255), strokeWidth: 1);
                            })]
                    )
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_Sunburst_CanvasCreated", canvas is not null);

            // 9 non-root nodes (3 groups + 6 leaves)
            var paths = H.FindAllControls<WinShapes.Path>(_ => true);
            H.Check("D3Cov_Sunburst_HasArcs", paths.Count >= 6);
        }
    }

    // ── Sankey Diagram ─────────────────────────────────────────────────

    internal class SankeyDiagram(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const double cw = 600, ch = 400;
            const double pad = 40;
            double plotW = cw - pad * 2, plotH = ch - pad * 2;

            var graph = new SankeyGraph
            {
                Nodes =
                [
                    new SankeyNode { Id = "solar", Label = "Solar" },
                    new SankeyNode { Id = "wind", Label = "Wind" },
                    new SankeyNode { Id = "grid", Label = "Grid" },
                    new SankeyNode { Id = "home", Label = "Home" },
                    new SankeyNode { Id = "factory", Label = "Factory" },
                ],
                Links =
                [
                    new SankeyLink { SourceId = "solar", TargetId = "grid", Value = 80 },
                    new SankeyLink { SourceId = "wind", TargetId = "grid", Value = 60 },
                    new SankeyLink { SourceId = "grid", TargetId = "home", Value = 90 },
                    new SankeyLink { SourceId = "grid", TargetId = "factory", Value = 50 },
                ],
            };

            var layout = new SankeyLayout()
                .Size(plotW, plotH)
                .SetNodeWidth(20)
                .SetNodePadding(14);
            layout.Layout(graph);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Sankey Diagram"),
                    D3Canvas(cw, ch,
                        [.. graph.Links.Select(link =>
                            (Element)D3PathTranslated(SankeyLayout.LinkPath(link), pad, pad,
                                fill: Brush(Palette[0], opacity: 0.35))),
                         .. graph.Nodes.Select(node =>
                         {
                             double nh = node.Y1 - node.Y0;
                             return (Element)(D3Rect(pad + node.X0, pad + node.Y0, node.X1 - node.X0, nh)
                                 with { Fill = Brush(Palette[1]), RadiusX = 2, RadiusY = 2 });
                         })]
                    )
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_Sankey_CanvasCreated", canvas is not null);

            // 4 link paths + 5 node rects
            var paths = H.FindAllControls<WinShapes.Path>(_ => true);
            H.Check("D3Cov_Sankey_HasLinkPaths", paths.Count >= 4);

            var rects = H.FindAllControls<WinShapes.Rectangle>(_ => true);
            H.Check("D3Cov_Sankey_HasNodeRects", rects.Count >= 5);
        }
    }

    // ── Chord Diagram ──────────────────────────────────────────────────

    internal class ChordDiagram(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const double cw = 500, ch = 500;
            double cx = cw / 2, cy = ch / 2;
            double outerR = 180, innerR = 170;

            string[] regions = ["A", "B", "C", "D"];
            double[][] matrix =
            [
                [0,  50, 30, 20],
                [40, 0,  60, 10],
                [25, 55, 0,  35],
                [15, 8,  30, 0 ],
            ];

            var chord = new ChordLayout().SetPadAngle(0.05);
            var data = chord.Generate(matrix);
            var ribbon = new RibbonGenerator().SetRadius(innerR);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Chord Diagram"),
                    D3Canvas(cw, ch,
                        [.. data.Groups.Select(g =>
                        {
                            var fill = Brush(Palette[g.Index % Palette.Count]);
                            var pb = new PathBuilder(3);
                            double a0 = g.StartAngle - Math.PI / 2;
                            double a1 = g.EndAngle - Math.PI / 2;
                            pb.Arc(cx, cy, outerR, a0, a1);
                            pb.Arc(cx, cy, innerR, a1, a0, ccw: true);
                            pb.ClosePath();
                            return (Element)D3Path(pb.ToString(), fill: fill);
                        }),
                         .. data.Chords.Select(c =>
                            (Element)D3PathTranslated(ribbon.Generate(c), cx, cy,
                                fill: Brush(Palette[c.Source.Index % Palette.Count], opacity: 0.5)))]
                    )
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_Chord_CanvasCreated", canvas is not null);

            // 4 group arcs + multiple chord ribbons
            var paths = H.FindAllControls<WinShapes.Path>(_ => true);
            H.Check("D3Cov_Chord_HasGroupArcs", paths.Count >= 4);
            H.Check("D3Cov_Chord_HasRibbons", paths.Count >= 8); // 4 arcs + at least 4 ribbons
        }
    }

    // ── Stacked Area Chart ─────────────────────────────────────────────

    internal class StackedAreaChart(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Three series over 8 time points
            double[] xvals = [0, 1, 2, 3, 4, 5, 6, 7];
            double[][] series =
            [
                [20, 25, 30, 28, 35, 40, 38, 42],
                [15, 18, 12, 20, 22, 17, 25, 28],
                [10, 12, 15, 13, 18, 20, 16, 22],
            ];

            // Compute cumulative baselines for stacking
            double[] baseline0 = new double[8];
            double[][] y0 = new double[3][];
            double[][] y1 = new double[3][];
            for (int s = 0; s < 3; s++)
            {
                y0[s] = (double[])baseline0.Clone();
                y1[s] = new double[8];
                for (int i = 0; i < 8; i++)
                {
                    y1[s][i] = baseline0[i] + series[s][i];
                    baseline0[i] = y1[s][i];
                }
            }

            const double cw = 600, ch = 400;
            const double left = 50, top = 20, right = 20, bottom = 30;
            double pw = cw - left - right, ph = ch - top - bottom;
            double maxY = baseline0.Max();

            var xScale = new LinearScale([0, 7], [left, left + pw]);
            var yScale = new LinearScale([0, maxY], [top + ph, top]).Nice();

            // Build indexed data for AreaGenerator
            var indices = Enumerable.Range(0, 8).ToArray();

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Stacked Area"),
                    D3Canvas(cw, ch,
                        [.. Enumerable.Range(0, 3).Select(s =>
                        {
                            var fill = Brush(Palette[s], opacity: 0.6);
                            return (Element)D3AreaPath(
                                indices,
                                i => xScale.Map(xvals[i]),
                                i => yScale.Map(y0[s][i]),
                                i => yScale.Map(y1[s][i]),
                                fill: fill);
                        }),
                         .. D3Axes(xScale, yScale, left, top, pw, ph)]
                    )
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_StackedArea_CanvasCreated", canvas is not null);

            // 3 area paths
            var paths = H.FindAllControls<WinShapes.Path>(_ => true);
            H.Check("D3Cov_StackedArea_HasAreas", paths.Count >= 3);
        }
    }

    // ── D3 Primitives (low-level DSL exercise) ─────────────────────────

    internal class D3Primitives(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("D3 Primitives"),
                    D3Canvas(400, 300, [
                        D3Rect(10, 10, 80, 60) with { Fill = Brush(Palette[0]), RadiusX = 4, RadiusY = 4 },
                        D3Circle(200, 100, 40) with { Fill = Brush(Palette[1]), Stroke = Brush(Palette[2]) },
                        D3Line(10, 200, 390, 200) with { Stroke = Gray(100), StrokeThickness = 2 },
                        D3Charts.Text(10, 250, "Label", 12, Gray(40)),
                        TextRight(200, 250, "Right", 80, 12, Gray(40)),
                        TextCenter(300, 250, "Center", 80, 12, Gray(40)),
                        D3Link(50, 10, 150, 100, stroke: Gray(80), strokeWidth: 2),
                        .. D3Legend(300, 10, [("Series A", Brush(Palette[0])), ("Series B", Brush(Palette[1]))], 10),
                    ])
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_Prims_CanvasCreated", canvas is not null);
            H.Check("D3Cov_Prims_CanvasSize",
                canvas is not null && Math.Abs(canvas.Width - 400) < 1 && Math.Abs(canvas.Height - 300) < 1);

            var rects = H.FindAllControls<WinShapes.Rectangle>(_ => true);
            H.Check("D3Cov_Prims_HasRect", rects.Count >= 1);

            var ellipses = H.FindAllControls<WinShapes.Ellipse>(_ => true);
            H.Check("D3Cov_Prims_HasCircle", ellipses.Count >= 1);

            var lines = H.FindAllControls<WinShapes.Line>(_ => true);
            H.Check("D3Cov_Prims_HasLine", lines.Count >= 1);

            var paths = H.FindAllControls<WinShapes.Path>(_ => true);
            H.Check("D3Cov_Prims_HasLink", paths.Count >= 1);

            // Legend produces rect+text pairs
            H.Check("D3Cov_Prims_LegendA", H.FindText("Series A") is not null);
            H.Check("D3Cov_Prims_LegendB", H.FindText("Series B") is not null);

            // Text helpers
            H.Check("D3Cov_Prims_TextLabel", H.FindText("Label") is not null);
            H.Check("D3Cov_Prims_TextRight", H.FindText("Right") is not null);
            H.Check("D3Cov_Prims_TextCenter", H.FindText("Center") is not null);
        }
    }

    // ── Multi-line Chart ───────────────────────────────────────────────

    internal class MultiLineChart(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const double cw = 600, ch = 400;
            const double left = 50, top = 20, right = 20, bottom = 30;
            double pw = cw - left - right, ph = ch - top - bottom;

            var rng = new Random(7);
            var series1 = Enumerable.Range(0, 12).Select(i => (x: (double)i, y: 20 + rng.NextDouble() * 60)).ToArray();
            var series2 = Enumerable.Range(0, 12).Select(i => (x: (double)i, y: 30 + rng.NextDouble() * 50)).ToArray();

            var xs = new LinearScale([0, 11], [left, left + pw]);
            var ys = new LinearScale([0, 100], [top + ph, top]).Nice();

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Multi-Line Chart"),
                    D3Canvas(cw, ch,
                        [D3LinePath(series1, d => xs.Map(d.x), d => ys.Map(d.y),
                            stroke: Brush(Palette[0]), strokeWidth: 2),
                         D3LinePath(series2, d => xs.Map(d.x), d => ys.Map(d.y),
                            stroke: Brush(Palette[1]), strokeWidth: 2),
                         .. D3Axes(xs, ys, left, top, pw, ph),
                         .. D3Legend(cw - 120, top, [
                            ("Revenue", Brush(Palette[0])),
                            ("Cost", Brush(Palette[1]))])]
                    )
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_MultiLine_CanvasCreated", canvas is not null);

            var paths = H.FindAllControls<WinShapes.Path>(_ => true);
            H.Check("D3Cov_MultiLine_HasTwoLines", paths.Count >= 2);

            H.Check("D3Cov_MultiLine_HasLegend",
                H.FindText("Revenue") is not null && H.FindText("Cost") is not null);
        }
    }

    // ── Circle Packing ────────────────────────────────────────────────

    private record PackNode(string Name, double Size = 0, PackNode[]? Children = null);

    internal class CirclePacking(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var data = new PackNode("root", 0, [
                new("Group A", 0, [
                    new("A1", 40),
                    new("A2", 25),
                    new("A3", 15),
                ]),
                new("Group B", 0, [
                    new("B1", 50),
                    new("B2", 30),
                ]),
            ]);

            const double cw = 400, ch = 400;
            double cx = cw / 2, cy = ch / 2;
            var pack = PackLayout.Create<PackNode>().Size(180).SetPadding(4);
            var root = pack.Layout(data, n => n.Children, n => n.Size);

            var allNodes = root.Descendants().ToList();

            // Verify the layout computed valid positions
            H.Check("D3Cov_Pack_LayoutComputed", allNodes.Count >= 7);
            H.Check("D3Cov_Pack_RootRadius", root.R > 0);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Circle Packing"),
                    D3Canvas(cw, ch,
                        [.. allNodes
                            .Where(node => node.R > 1)
                            .Select(node =>
                        {
                            double nx = cx + node.X;
                            double ny = cy + node.Y;
                            bool isLeaf = node.Children.Count == 0;
                            int colorIdx = Math.Max(0, root.Children.IndexOf(node.TopAncestor));
                            var fill = isLeaf
                                ? Brush(Palette[colorIdx % Palette.Count], opacity: 0.6)
                                : Gray(200, alpha: 40);
                            var stroke = Brush(Palette[colorIdx % Palette.Count], opacity: 0.8);
                            return (Element)(D3Circle(nx, ny, node.R)
                                with { Fill = fill, Stroke = stroke, StrokeThickness = 1 });
                        })]
                    )
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_Pack_CanvasCreated", canvas is not null);

            var ellipses = H.FindAllControls<WinShapes.Ellipse>(_ => true);
            H.Check("D3Cov_Pack_HasCircles", ellipses.Count >= 5);
        }
    }

    // ── Scales Exercise ────────────────────────────────────────────────

    internal class ScalesExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Exercise multiple scale types to cover ReactorCharting Scale/*

            // LinearScale
            var linear = new LinearScale([0, 100], [0, 500]).Nice();
            double linMapped = linear.Map(50);
            H.Check("D3Cov_Scale_LinearMid", Math.Abs(linMapped - 250) < 10);

            // BandScale
            string[] cats = ["A", "B", "C"];
            var band = BandScale.Create(cats).SetRange(0, 300).SetPaddingInner(0.1);
            double bandA = band.Map("A");
            double bandB = band.Map("B");
            H.Check("D3Cov_Scale_BandOrdered", bandA < bandB);
            H.Check("D3Cov_Scale_BandWidth", band.Bandwidth > 0);

            // LogScale
            var log = new LogScale([1, 1000], [0, 300]);
            double log10 = log.Map(10);
            double log100 = log.Map(100);
            H.Check("D3Cov_Scale_LogOrdered", log10 < log100);
            H.Check("D3Cov_Scale_LogRange", log100 > 0 && log100 < 300);

            // OrdinalScale
            var ordinal = new OrdinalScale<string>(cats, [1.0, 2.0, 3.0]);
            H.Check("D3Cov_Scale_OrdinalA", Math.Abs(ordinal.Map("A") - 1.0) < 0.01);
            H.Check("D3Cov_Scale_OrdinalC", Math.Abs(ordinal.Map("C") - 3.0) < 0.01);

            // Render a simple chart using BandScale to make it visual
            double[] values = [80, 55, 92];
            const double cw = 400, ch = 200;
            var yScale = new LinearScale([0, 100], [ch - 20, 10]).Nice();
            var xBand = BandScale.Create(cats).SetRange(20, cw - 20).SetPaddingInner(0.2);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Scales"),
                    D3Canvas(cw, ch,
                        [.. cats.Select((c, i) =>
                        {
                            double y = yScale.Map(values[i]);
                            double barH = yScale.Map(0) - y;
                            return (Element)(D3Rect(xBand.Map(c), y, xBand.Bandwidth, barH)
                                with { Fill = Brush(Palette[i]) });
                        })]
                    )
                )
            );

            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3Cov_Scale_CanvasCreated", canvas is not null);
        }
    }
}
