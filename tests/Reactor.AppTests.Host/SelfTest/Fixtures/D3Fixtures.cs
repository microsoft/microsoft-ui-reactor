using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Charting.Charts;
using WinShapes = Microsoft.UI.Xaml.Shapes;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class D3Fixtures
{
    private record DataPoint(double X, double Y);

    private static readonly DataPoint[] SampleLine =
        Enumerable.Range(0, 10).Select(i => new DataPoint(i, Math.Sin(i * 0.5) * 50 + 50)).ToArray();

    private static readonly DataPoint[] SampleBars =
    [
        new(0, 30), new(1, 70), new(2, 45), new(3, 90), new(4, 55)
    ];

    private record PieSlice(string Label, double Value);
    private static readonly PieSlice[] SamplePie =
    [
        new("A", 30), new("B", 20), new("C", 35), new("D", 15)
    ];

    internal class LineChart(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            // D3 charts use XamlHostElement which requires XamlInterop registration
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Line Chart"),
                    Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                        .Width(600).Height(400)
                        .ShowAxes(true)
                        .ShowGrid(true)
                        .ToElement()
                )
            );

            await Harness.Render();

            H.Check("D3_LineChart_TitleVisible",
                H.FindText("Line Chart") is not null);

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3_LineChart_CanvasCreated",
                canvas is not null);

            H.Check("D3_LineChart_HasChildren",
                canvas is not null && canvas.Children.Count > 0);
        }
    }

    internal class BarChart(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Bar Chart"),
                    Charts.BarChart(SampleBars, d => d.X, d => d.Y)
                        .Width(600).Height(400)
                        .ShowAxes(true)
                        .ToElement()
                )
            );

            await Harness.Render();

            H.Check("D3_BarChart_TitleVisible",
                H.FindText("Bar Chart") is not null);

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3_BarChart_CanvasCreated",
                canvas is not null);

            var rects = H.FindAllControls<WinShapes.Rectangle>(_ => true);
            H.Check("D3_BarChart_HasRectangles",
                rects.Count >= SampleBars.Length);
        }
    }

    internal class PieChart(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Pie Chart"),
                    Charts.PieChart(SamplePie, d => d.Value, d => d.Label)
                        .Width(400).Height(400)
                        .ToElement()
                )
            );

            await Harness.Render();

            H.Check("D3_PieChart_TitleVisible",
                H.FindText("Pie Chart") is not null);

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3_PieChart_CanvasCreated",
                canvas is not null);

            var paths = H.FindAllControls<WinShapes.Path>(_ => true);
            H.Check("D3_PieChart_HasArcs",
                paths.Count >= SamplePie.Length);
        }
    }

    internal class AreaChart(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Area Chart"),
                    Charts.AreaChart(SampleLine, d => d.X, d => d.Y)
                        .Width(600).Height(400)
                        .ShowAxes(true)
                        .ShowGrid(true)
                        .ToElement()
                )
            );

            await Harness.Render();

            H.Check("D3_AreaChart_TitleVisible",
                H.FindText("Area Chart") is not null);

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3_AreaChart_CanvasCreated",
                canvas is not null);

            H.Check("D3_AreaChart_HasChildren",
                canvas is not null && canvas.Children.Count > 0);
        }
    }

    private record TreeItem(string Name, TreeItem[]? Kids);

    private static readonly TreeItem TreeData = new("Root",
    [
        new("A", [new("A1", null), new("A2", null)]),
        new("B", [new("B1", null)])
    ]);

    internal class TreeChart(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Tree Chart"),
                    Charts.TreeChart<TreeItem>(TreeData, t => t.Kids, t => t.Name)
                        .Width(600).Height(400)
                        .ToElement()
                )
            );

            await Harness.Render();

            H.Check("D3_TreeChart_TitleVisible",
                H.FindText("Tree Chart") is not null);

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3_TreeChart_CanvasCreated",
                canvas is not null);

            // Tree should have children: lines (paths) for links + ellipses for nodes + text labels
            H.Check("D3_TreeChart_HasChildren",
                canvas is not null && canvas.Children.Count > 0);

            var ellipses = H.FindAllControls<WinShapes.Ellipse>(_ => true);
            H.Check("D3_TreeChart_HasNodes",
                ellipses.Count >= 5); // Root, A, B, A1, A2, B1 = 6 nodes

            var textBlocks = H.FindAllControls<TextBlock>(tb =>
                tb.Text == "Root" || tb.Text == "A" || tb.Text == "B" ||
                tb.Text == "A1" || tb.Text == "A2" || tb.Text == "B1");
            H.Check("D3_TreeChart_HasLabels",
                textBlocks.Count >= 5);
        }
    }

    internal class ForceGraph(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var nodes = Enumerable.Range(0, 5)
                .Select(i => new ForceNode { Label = $"N{i}" }).ToArray();
            var links = new ForceLink[]
            {
                new(0, 1), new(1, 2), new(2, 3), new(3, 4), new(4, 0)
            };

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Force Graph"),
                    Charts.ForceGraph(nodes, links)
                        .Width(600).Height(400)
                        .ToElement()
                )
            );

            await Harness.Render();

            H.Check("D3_ForceGraph_TitleVisible",
                H.FindText("Force Graph") is not null);

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3_ForceGraph_CanvasCreated",
                canvas is not null);

            H.Check("D3_ForceGraph_HasChildren",
                canvas is not null && canvas.Children.Count > 0);

            // Should have lines for links and ellipses for nodes
            var lineControls = H.FindAllControls<WinShapes.Line>(_ => true);
            H.Check("D3_ForceGraph_HasLinks",
                lineControls.Count >= links.Length);

            var ellipses = H.FindAllControls<WinShapes.Ellipse>(_ => true);
            H.Check("D3_ForceGraph_HasNodes",
                ellipses.Count >= nodes.Length);
        }
    }

    internal class ChartCustomization(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Customized Chart"),
                    Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                        .Width(800).Height(500)
                        .Margin(30, 30, 40, 50)
                        .Stroke("#ff0000")
                        .Fill("#00ff00")
                        .StrokeWidth(3)
                        .FillOpacity(0.5)
                        .ShowAxes(true)
                        .ShowGrid(true)
                        .ToElement()
                )
            );

            await Harness.Render();

            H.Check("D3_ChartCustomization_TitleVisible",
                H.FindText("Customized Chart") is not null);

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3_ChartCustomization_CanvasCreated",
                canvas is not null);

            H.Check("D3_ChartCustomization_CorrectWidth",
                canvas is not null && Math.Abs(canvas.Width - 800) < 1);

            H.Check("D3_ChartCustomization_CorrectHeight",
                canvas is not null && Math.Abs(canvas.Height - 500) < 1);

            H.Check("D3_ChartCustomization_HasChildren",
                canvas is not null && canvas.Children.Count > 0);
        }
    }

    internal class PieChartLabels(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx =>
                VStack(
                    TextBlock("Pie Chart Labels"),
                    Charts.PieChart(SamplePie, d => d.Value, d => d.Label)
                        .Width(400).Height(400)
                        .ToElement()
                )
            );

            await Harness.Render();

            H.Check("D3_PieChartLabels_TitleVisible",
                H.FindText("Pie Chart Labels") is not null);

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("D3_PieChartLabels_CanvasCreated",
                canvas is not null);

            // Verify text labels appear for each pie slice
            var labelTexts = H.FindAllControls<TextBlock>(tb =>
                tb.Text == "A" || tb.Text == "B" || tb.Text == "C" || tb.Text == "D");
            H.Check("D3_PieChartLabels_HasSliceLabels",
                labelTexts.Count >= SamplePie.Length);
        }
    }
}
