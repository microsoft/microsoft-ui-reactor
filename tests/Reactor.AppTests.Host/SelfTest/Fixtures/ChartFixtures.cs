using Microsoft.UI.Reactor.Advanced.Charts;
using Microsoft.UI.Xaml.Controls.Charts;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;
using WinUIChart = Microsoft.UI.Xaml.Controls.Charts.Chart;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Reactor.Advanced Chart (Microsoft.UI.Xaml.Controls.Charts, Windows App SDK experimental
/// channel) against the live control: series and inferred axes mount, values and props
/// update in place, equal axis records share one native axis per role, and a series type
/// change replaces only that series.
/// </summary>
internal static class ChartFixtures
{
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr"];

    private static WinUIChart? FindChart(Harness h) => h.FindControl<WinUIChart>(_ => true);

    // ItemsSource round-trips through WinRT, which may hand back a copy of an array.
    private static bool SameValues<T>(object? actual, IEnumerable<T> expected) =>
        actual is global::System.Collections.IEnumerable e && e.Cast<object>().SequenceEqual(expected.Cast<object>());

    private static string Describe(object? value) =>
        value is global::System.Collections.IEnumerable e
            ? $"{value.GetType().Name}[{string.Join(",", e.Cast<object>())}]"
            : value?.GetType().Name ?? "null";

    internal class MountsSeriesAndAxes(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            double[] profit = [18, 26, 33, 39];
            double[] target = [20, 25, 40, 40];
            var host = H.CreateHost();
            host.Mount(_ => Chart(
                    LineSeries(Months, profit, "Profit"),
                    BarSeries(Months, target, "Target").Vertical())
                .Legend("Results")
                .Width(480).Height(300));

            H.Check("Chart_Mount_Laidout",
                await Harness.WaitFor(() => FindChart(H) is { ActualHeight: > 0, ActualWidth: > 0 }, maxPasses: 40, perPassMs: 25));

            // The native chart has no control template — it draws through a composition
            // SpriteVisual it attaches as the element's child visual (which RenderTargetBitmap
            // cannot capture) — so prove its renderer is attached and sized to the control.
            var chartForVisual = FindChart(H)!;
            var renderer = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementChildVisual(chartForVisual);
            H.Check("Chart_Mount_RendererAttached",
                renderer is Microsoft.UI.Composition.SpriteVisual sprite
                && sprite.Size.X == (float)chartForVisual.ActualWidth
                && sprite.Size.Y == (float)chartForVisual.ActualHeight,
                $"renderer={renderer?.GetType().Name} size={renderer?.Size}");

            var chart = FindChart(H)!;
            H.Check("Chart_Mount_SeriesCreated",
                chart.Series.Count == 2
                && chart.Series[0] is LineSeries { Title: "Profit" }
                && chart.Series[1] is BarSeries { Title: "Target" },
                $"series={chart.Series.Count}");
            // Both series leave their axes unset: one inferred category X axis and one
            // linear Y axis, shared by both.
            H.Check("Chart_Mount_DefaultAxesInferredAndShared",
                chart.Axes.Count == 2
                && chart.Series[0].XAxis is CategoryAxis
                && chart.Series[0].YAxis is LinearAxis
                && ReferenceEquals(chart.Series[0].XAxis, chart.Series[1].XAxis)
                && ReferenceEquals(chart.Series[0].YAxis, chart.Series[1].YAxis),
                $"axes={chart.Axes.Count}");
            // Both series pass the same X array: one shared Samples, as XAML declares it.
            H.Check("Chart_Mount_SamplesBound",
                chart.Data.Count == 3
                && ReferenceEquals(chart.Series[0].XValues, chart.Series[1].XValues)
                && SameValues(chart.Series[0].XValues.ItemsSource, Months)
                && SameValues(chart.Series[0].YValues.ItemsSource, profit),
                $"data={chart.Data.Count} x={Describe(chart.Series[0].XValues.ItemsSource)} y={Describe(chart.Series[0].YValues.ItemsSource)}");
            H.Check("Chart_Mount_LegendApplied", chart.ShowLegend && chart.LegendTitle == "Results");

            host.Mount(_ => TextBlock("Chart unmounted"));
            await Harness.Render();
        }
    }

    internal class UpdatesInPlace(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            double[] first = [1, 2, 3, 4];
            double[] second = [4, 3, 2, 1];
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                return VStack(8,
                    Button("Next phase", () => setPhase(phase + 1)),
                    Chart(LineSeries(Months, phase == 0 ? first : second, phase == 0 ? "Before" : "After",
                            yAxis: LinearAxis(phase == 0 ? "Units" : "Units (k)", minimum: 0)))
                        .Width(480).Height(300));
            });

            await Harness.WaitFor(() => FindChart(H)?.Series.Count == 1, maxPasses: 40, perPassMs: 25);
            var chart = FindChart(H)!;
            var series = chart.Series[0];
            var yAxis = series.YAxis as LinearAxis;
            var ySamples = series.YValues;

            H.ClickButton("Next phase");
            await Harness.Render();

            H.Check("Chart_Update_SeriesInstancePreserved", chart.Series.Count == 1 && ReferenceEquals(chart.Series[0], series));
            H.Check("Chart_Update_ValuesReassigned",
                ReferenceEquals(series.YValues, ySamples) && SameValues(ySamples.ItemsSource, second),
                $"y={Describe(ySamples.ItemsSource)}");
            H.Check("Chart_Update_TitleWritten", series.Title == "After", $"title={series.Title}");
            H.Check("Chart_Update_AxisUpdatedInPlace",
                ReferenceEquals(series.YAxis, yAxis) && yAxis is { Label: "Units (k)", Minimum: 0 } && chart.Axes.Count == 2,
                $"label={yAxis?.Label} axes={chart.Axes.Count}");

            host.Mount(_ => TextBlock("Chart unmounted"));
            await Harness.Render();
        }
    }

    internal class AxisSharingAndTypeChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            double[] x = [1, 2, 3];
            double[] a = [3, 1, 2];
            double[] b = [2, 2, 2];
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (bars, setBars) = ctx.UseState(false);
                // Separately-constructed but equal axis records share one native axis.
                ChartSeries second = bars
                    ? BarSeries(x, b, "B", xAxis: CategoryAxis("Step"), yAxis: LinearAxis("Units"))
                    : LineSeries(x, b, "B", xAxis: CategoryAxis("Step"), yAxis: LinearAxis("Units"));
                return VStack(8,
                    Button("Bars", () => setBars(true)),
                    Chart(LineSeries(x, a, "A", xAxis: CategoryAxis("Step"), yAxis: LinearAxis("Units")), second)
                        .Width(480).Height(300));
            });

            await Harness.WaitFor(() => FindChart(H)?.Series.Count == 2, maxPasses: 40, perPassMs: 25);
            var chart = FindChart(H)!;
            H.Check("Chart_Axes_EqualRecordsShared",
                chart.Axes.Count == 2
                && chart.Series[0].XAxis is CategoryAxis { Label: "Step" }
                && chart.Series[0].YAxis is LinearAxis { Label: "Units" }
                && ReferenceEquals(chart.Series[0].XAxis, chart.Series[1].XAxis)
                && ReferenceEquals(chart.Series[0].YAxis, chart.Series[1].YAxis),
                $"axes={chart.Axes.Count}");

            var firstSeries = chart.Series[0];
            H.ClickButton("Bars");
            await Harness.Render();
            H.Check("Chart_Series_TypeChangeReplacesOnlyThatSeries",
                chart.Series.Count == 2
                && ReferenceEquals(chart.Series[0], firstSeries)
                && chart.Series[1] is BarSeries { Title: "B" }
                && chart.Data.Count == 3,
                $"series={chart.Series.Count} data={chart.Data.Count}");
            // The bar keeps the control-default horizontal orientation, which the native
            // chart refuses to share an axis with a line: it gets its own pair of axes.
            H.Check("Chart_Axes_HorizontalBarsGetOwnAxes",
                chart.Axes.Count == 4
                && !ReferenceEquals(chart.Series[0].XAxis, chart.Series[1].XAxis)
                && !ReferenceEquals(chart.Series[0].YAxis, chart.Series[1].YAxis),
                $"axes={chart.Axes.Count}");

            host.Mount(_ => TextBlock("Chart unmounted"));
            await Harness.Render();
        }
    }
}
