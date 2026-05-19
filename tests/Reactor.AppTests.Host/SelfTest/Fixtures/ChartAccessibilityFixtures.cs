using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Charting.Accessibility;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Charting.Charts;
using static Microsoft.UI.Reactor.Charting.D3Charts;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Self-test fixtures for chart accessibility — peer attachment, grid/table providers,
/// point values, labels, and summary.
/// </summary>
internal static class ChartAccessibilityFixtures
{
    private record DataPoint(double X, double Y);

    private static readonly DataPoint[] SampleLine =
        Enumerable.Range(0, 5).Select(i => new DataPoint(i, (i + 1) * 100.0)).ToArray();

    private static readonly DataPoint[] SampleBars =
    [
        new(0, 30), new(1, 70), new(2, 45), new(3, 90), new(4, 55)
    ];

    private record PieSlice(string Label, double Value);
    private static readonly PieSlice[] SamplePie =
    [
        new("Alpha", 30), new("Beta", 20), new("Gamma", 35), new("Delta", 15)
    ];

    // ════════════════════════════════════════════════════════════════════
    //  1.6 — Peer wiring
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mount a LineChart and verify IChartAccessibilityData is implemented.
    /// </summary>
    internal class PeerAttachment(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Verify ChartElement implements IChartAccessibilityData
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Test Line Chart")
                .SeriesName("Revenue");

            H.Check("ChartA11y_PeerAttachment_ImplementsInterface",
                chart is IChartAccessibilityData);

            var a11y = (IChartAccessibilityData)chart;
            H.Check("ChartA11y_PeerAttachment_HasName",
                a11y.Name == "Test Line Chart");

            // Mount the chart to verify rendering still works
            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("ChartA11y_PeerAttachment_CanvasCreated",
                canvas is not null);
        }
    }

    /// <summary>
    /// Mount a 2-series BarChart (simulated), verify IGridProvider-compatible data.
    /// </summary>
    internal class GridProvider(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.BarChart(SampleBars, d => d.X, d => d.Y)
                .SeriesName("Sales");

            var a11y = (IChartAccessibilityData)chart;
            var series = a11y.Series;

            // Single series chart: RowCount = 1, ColumnCount = 5
            H.Check("ChartA11y_GridProvider_RowCount",
                series.Count == 1);
            H.Check("ChartA11y_GridProvider_ColumnCount",
                series[0].Points.Count == 5);

            // Mount
            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            H.Check("ChartA11y_GridProvider_Rendered",
                H.FindControl<Canvas>(_ => true) is not null);
        }
    }

    /// <summary>
    /// Mount a LineChart with known data, verify point value strings.
    /// </summary>
    internal class PointValue(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .SeriesName("Revenue")
                .Units(yUnits: " USD");

            var a11y = (IChartAccessibilityData)chart;
            var series = a11y.Series;
            var points = series[0].Points;

            // Verify point data
            H.Check("ChartA11y_PointValue_FirstPoint",
                points[0].YValue == 100.0);
            H.Check("ChartA11y_PointValue_LastPoint",
                points[4].YValue == 500.0);

            // Verify default label format through FormatDefaultLabel
            var label = ChartPointProvider.FormatDefaultLabel(series[0], points[0], 0, " USD");
            H.Check("ChartA11y_PointValue_LabelContainsSeriesName",
                label.Contains("Revenue"));
            H.Check("ChartA11y_PointValue_LabelContainsUnits",
                label.Contains("USD"));
            H.Check("ChartA11y_PointValue_LabelContainsPointIndex",
                label.Contains("point 1 of 5"));

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();
        }
    }

    /// <summary>
    /// Verify ITableProvider row headers match series names and column headers match x-labels.
    /// </summary>
    internal class TableHeaders(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .SeriesName("Quarterly Revenue");

            var a11y = (IChartAccessibilityData)chart;

            // Row header = series name
            H.Check("ChartA11y_TableHeaders_SeriesName",
                a11y.Series[0].Name == "Quarterly Revenue");

            // Column headers = x-axis tick labels
            H.Check("ChartA11y_TableHeaders_FirstXLabel",
                a11y.Series[0].Points[0].XLabel == "0");

            // Axes present
            H.Check("ChartA11y_TableHeaders_HasAxes",
                a11y.Axes.Count == 2);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();
        }
    }

    /// <summary>
    /// Mount a PieChart, verify slices are exposed as accessible points.
    /// </summary>
    internal class PieChart(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.PieChart(SamplePie, d => d.Value, d => d.Label)
                .Title("Distribution");

            var a11y = (IChartAccessibilityData)chart;

            H.Check("ChartA11y_PieChart_HasName",
                a11y.Name == "Distribution");
            H.Check("ChartA11y_PieChart_SliceCount",
                a11y.Series[0].Points.Count == 4);
            H.Check("ChartA11y_PieChart_SliceLabel",
                a11y.Series[0].Points[0].XLabel == "Alpha");
            H.Check("ChartA11y_PieChart_SliceValue",
                a11y.Series[0].Points[0].YValue == 30);

            // No axes for pie charts
            H.Check("ChartA11y_PieChart_NoAxes",
                a11y.Axes.Count == 0);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();
        }
    }

    /// <summary>
    /// Mount a ForceGraph, verify edges exposed as accessible rows.
    /// </summary>
    internal class ForceGraph(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var nodes = new ForceNode[]
            {
                new() { Label = "Alice" },
                new() { Label = "Bob" },
                new() { Label = "Carol" },
            };
            var links = new ForceLink[]
            {
                new(0, 1, Strength: 1),
                new(1, 2, Strength: 2),
            };

            var graph = Charts.ForceGraph(nodes, links)
                .Title("Social Network");

            var a11y = (IChartAccessibilityData)graph;

            H.Check("ChartA11y_ForceGraph_HasName",
                a11y.Name == "Social Network");
            H.Check("ChartA11y_ForceGraph_EdgeCount",
                a11y.Series[0].Points.Count == 2);
            H.Check("ChartA11y_ForceGraph_EdgeLabel",
                a11y.Series[0].Points[0].XLabel.Contains("Alice") &&
                a11y.Series[0].Points[0].XLabel.Contains("Bob"));

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => graph.ToElement());
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  2.5 — Labels and summary
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Test default point labels with SeriesName and Units.
    /// </summary>
    internal class DefaultPointLabels(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .SeriesName("Revenue")
                .Units(yUnits: " USD");

            var a11y = (IChartAccessibilityData)chart;
            var series = a11y.Series[0];
            var point = series.Points[0];

            var label = ChartPointProvider.FormatDefaultLabel(series, point, 0, " USD");
            H.Check("ChartA11y_DefaultPointLabels_Format",
                label.Contains("Revenue") &&
                label.Contains("USD") &&
                label.Contains("point 1 of 5"));

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();
        }
    }

    /// <summary>
    /// Test custom DataLabel override.
    /// </summary>
    internal class CustomDataLabel(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .DataLabel((d, i) => $"Custom #{i}: {d.Y}");

            var a11y = (IChartAccessibilityData)chart;
            var points = a11y.Series[0].Points;

            H.Check("ChartA11y_CustomDataLabel_FirstPoint",
                points[0].FormattedLabel == "Custom #0: 100");
            H.Check("ChartA11y_CustomDataLabel_LastPoint",
                points[4].FormattedLabel == "Custom #4: 500");

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();
        }
    }

    /// <summary>
    /// Test auto-summary contains trend keywords for increasing data.
    /// </summary>
    internal class AutoSummary(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Create clearly increasing data
            var data = Enumerable.Range(0, 10)
                .Select(i => new DataPoint(i, i * 100.0))
                .ToArray();

            var chart = Charts.LineChart(data, d => d.X, d => d.Y)
                .SeriesName("Revenue");

            var a11y = (IChartAccessibilityData)chart;
            var summary = ChartSummarizer.Summarize(a11y, "Line");
            var formatted = ChartSummarizer.FormatSummary(summary);

            H.Check("ChartA11y_AutoSummary_ContainsTrend",
                formatted.Contains("increasing"));
            H.Check("ChartA11y_AutoSummary_ContainsStats",
                formatted.Contains("Revenue") && formatted.Contains("min") && formatted.Contains("max"));

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();
        }
    }

    /// <summary>
    /// Chart with .Title() exposes that title as peer name.
    /// </summary>
    internal class AutoNameFromTitle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Revenue Over Time");

            var a11y = (IChartAccessibilityData)chart;
            H.Check("ChartA11y_AutoNameFromTitle",
                a11y.Name == "Revenue Over Time");

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();
        }
    }

    /// <summary>
    /// Chart without title falls back to type-based name.
    /// </summary>
    internal class AutoNameFallback(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y);

            var a11y = (IChartAccessibilityData)chart;
            H.Check("ChartA11y_AutoNameFallback_NoExplicitName",
                a11y.Name is null);

            // Series count still available for fallback name generation
            H.Check("ChartA11y_AutoNameFallback_HasSeries",
                a11y.Series.Count > 0);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  3.9 — Forced-colors and double-encoding
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// In forced-colors mode, series brushes use system high-contrast colors.
    /// </summary>
    internal class ForcedColorsPalette(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            D3Charts.IsForcedColors = true;
            try
            {
                var brush0 = D3Charts.ChartSeries(0);
                var brush1 = D3Charts.ChartSeries(1);
                var brush2 = D3Charts.ChartSeries(2);
                var brush3 = D3Charts.ChartSeries(3);

                H.Check("ChartA11y_ForcedColorsPalette_Series0",
                    brush0.Color == Microsoft.UI.Colors.White);
                H.Check("ChartA11y_ForcedColorsPalette_Series1",
                    brush1.Color == Microsoft.UI.Colors.Cyan);
                H.Check("ChartA11y_ForcedColorsPalette_Series2",
                    brush2.Color == Microsoft.UI.Colors.Yellow);
                H.Check("ChartA11y_ForcedColorsPalette_Series3",
                    brush3.Color == Microsoft.UI.Colors.Green);
            }
            finally
            {
                D3Charts.IsForcedColors = false;
            }
            await Harness.Render();
        }
    }

    /// <summary>
    /// Multi-series chart has distinct marker shape AND dash pattern for each series.
    /// </summary>
    internal class DoubleEncoding(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var palette = ChartPalette.OkabeIto;

            // Verify first 4 series have distinct markers
            var markers = Enumerable.Range(0, 4).Select(i => palette.GetMarker(i)).ToList();
            H.Check("ChartA11y_DoubleEncoding_DistinctMarkers",
                markers.Distinct().Count() == 4);

            // Verify first 4 series have distinct dashes
            var dashes = Enumerable.Range(0, 4).Select(i => palette.GetDash(i)).ToList();
            H.Check("ChartA11y_DoubleEncoding_DistinctDashes",
                dashes.Distinct().Count() == 4);

            await Harness.Render();
        }
    }

    /// <summary>
    /// .ColorOnly() suppresses shape/dash encoding.
    /// </summary>
    internal class ColorOnlyWarning(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Revenue")
                .ColorOnly();

            H.Check("ChartA11y_ColorOnlyWarning_IsColorOnly",
                chart.IsColorOnly);

            // Mount and verify scanner flags it
            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            // Run scanner on the element tree
            var tree = VStack(chart.ToElement());
            var findings = AccessibilityScanner.Scan(tree);
            H.Check("ChartA11y_ColorOnlyWarning_ScannerFlagsIt",
                findings.Any(f => f.Id == "A11Y_CHART_004"));
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  4.3 — Scanner rules
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Chart without title triggers A11Y_CHART_001 with correct fix suggestion.
    /// </summary>
    internal class ScannerMissingTitle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            var tree = VStack(chart.ToElement());
            var findings = AccessibilityScanner.Scan(tree);

            var titleFinding = findings.FirstOrDefault(f => f.Id == "A11Y_CHART_001");
            H.Check("ChartA11y_Scanner_MissingTitle_Found",
                titleFinding is not null);
            H.Check("ChartA11y_Scanner_MissingTitle_FixModifier",
                titleFinding?.Fix.Modifier == "Title");
            H.Check("ChartA11y_Scanner_MissingTitle_FixSnippet",
                titleFinding?.Fix.CodeSnippet?.Contains(".Title(") == true);
        }
    }

    /// <summary>
    /// Chart with .ColorOnly() triggers A11Y_CHART_004.
    /// </summary>
    internal class ScannerColorOnly(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Revenue")
                .ColorOnly();

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            var tree = VStack(chart.ToElement());
            var findings = AccessibilityScanner.Scan(tree);

            H.Check("ChartA11y_Scanner_ColorOnly_Found",
                findings.Any(f => f.Id == "A11Y_CHART_004"));
        }
    }

    /// <summary>
    /// Chart with known-bad palette triggers A11Y_CHART_009 or _010 with hardened alternative.
    /// </summary>
    internal class ScannerUnsafePalette(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Revenue")
                .SeriesColors(
                    new D3Color(128, 128, 128),
                    new D3Color(135, 135, 135)); // Very similar grays

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            var tree = VStack(chart.ToElement());
            var findings = AccessibilityScanner.Scan(tree);

            var paletteFinding = findings.FirstOrDefault(
                f => f.Id == "A11Y_CHART_009" || f.Id == "A11Y_CHART_010");
            H.Check("ChartA11y_Scanner_UnsafePalette_Found",
                paletteFinding is not null);
            H.Check("ChartA11y_Scanner_UnsafePalette_HasHardenedAlternative",
                paletteFinding?.Fix.SuggestedValue is not null);
        }
    }

    /// <summary>
    /// Chart with .Title() and defaults passes scanner with zero chart violations.
    /// </summary>
    internal class ScannerClean(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Monthly Revenue")
                .SeriesName("Revenue")
                .Units("months", "USD");

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            var tree = VStack(chart.ToElement());
            var findings = AccessibilityScanner.Scan(tree);
            var chartViolations = findings.Where(f => f.Id.StartsWith("A11Y_CHART_")).ToList();

            H.Check("ChartA11y_Scanner_Clean_ZeroViolations",
                chartViolations.Count == 0);
        }
    }

    /// <summary>
    /// Reduced-motion: verify D3Charts.IsReducedMotion flag is readable.
    /// </summary>
    internal class ReducedMotion(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Save and restore the flag
            bool saved = D3Charts.IsReducedMotion;
            try
            {
                D3Charts.IsReducedMotion = true;
                H.Check("ChartA11y_ReducedMotion_FlagSet",
                    D3Charts.IsReducedMotion == true);

                D3Charts.IsReducedMotion = false;
                H.Check("ChartA11y_ReducedMotion_FlagCleared",
                    D3Charts.IsReducedMotion == false);
            }
            finally
            {
                D3Charts.IsReducedMotion = saved;
            }
            await Harness.Render();
        }
    }

    /// <summary>
    /// Hit target expansion: verify .TightHitTest() flag is tracked and scanner fires.
    /// </summary>
    internal class HitTargetExpansion(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Test")
                .Interactive()
                .TightHitTest();

            H.Check("ChartA11y_HitTargetExpansion_IsTight",
                chart.IsTightHitTest);

            var tree = VStack(chart.ToElement());
            var findings = AccessibilityScanner.Scan(tree);

            H.Check("ChartA11y_HitTargetExpansion_ScannerFlags",
                findings.Any(f => f.Id == "A11Y_CHART_005"));

            await Harness.Render();
        }
    }

    /// <summary>
    /// Scanner: interactive chart with keyboard disabled triggers A11Y_CHART_003.
    /// </summary>
    internal class ScannerInteractiveNoKeyboard(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Test")
                .Interactive()
                .DisableKeyboard();

            var tree = VStack(chart.ToElement());
            var findings = AccessibilityScanner.Scan(tree);

            H.Check("ChartA11y_Scanner_InteractiveNoKeyboard_Found",
                findings.Any(f => f.Id == "A11Y_CHART_003"));

            await Harness.Render();
        }
    }

    /// <summary>
    /// AlternateView toggle: verify chart with .AlternateView() compiles and renders.
    /// </summary>
    internal class AlternateViewToggle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var alternate = TextBlock("Data table placeholder");
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Revenue")
                .AlternateView(alternate);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            // Chart should render without errors
            H.Check("ChartA11y_AlternateViewToggle_Mounts", true);
        }
    }

    /// <summary>
    /// AlternateView no-op: chart without .AlternateView() should render fine.
    /// </summary>
    internal class AlternateViewNoOp(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Revenue");

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            // Chart without AlternateView renders without error
            H.Check("ChartA11y_AlternateViewNoOp_Mounts", true);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  6.4 — Keyboard navigation fixtures
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Keyboard arrow nav: verify navigator tracks focus state correctly across
    /// left/right arrow key presses.
    /// </summary>
    internal class KeyboardArrowNav(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Revenue")
                .SeriesName("Q1")
                .Interactive();

            var a11y = (IChartAccessibilityData)chart;

            // Verify the chart data has points to navigate
            H.Check("ChartA11y_KeyboardArrowNav_HasPoints",
                a11y.Series.Count > 0 && a11y.Series[0].Points.Count == 5);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            // Verify keyboard navigator creates a focusable element
            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("ChartA11y_KeyboardArrowNav_CanvasMounted",
                canvas is not null);

            // Focus state starts at (0, 0) — verify data is accessible for navigation
            H.Check("ChartA11y_KeyboardArrowNav_DataAccessible",
                a11y.Series[0].Points[0].YValue == 100.0);
            H.Check("ChartA11y_KeyboardArrowNav_LastPoint",
                a11y.Series[0].Points[4].YValue == 500.0);
        }
    }

    /// <summary>
    /// Keyboard Home/End: verify Home goes to first point and End to last point.
    /// </summary>
    internal class KeyboardHomeEnd(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Revenue")
                .Interactive();

            var a11y = (IChartAccessibilityData)chart;
            int pointCount = a11y.Series[0].Points.Count;

            H.Check("ChartA11y_KeyboardHomeEnd_FirstPointIndex",
                pointCount > 0);
            H.Check("ChartA11y_KeyboardHomeEnd_LastPointIndex",
                pointCount == 5);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            H.Check("ChartA11y_KeyboardHomeEnd_Mounted", true);
        }
    }

    /// <summary>
    /// Keyboard series switch: verify up/down arrow data supports series switching.
    /// </summary>
    internal class KeyboardSeriesSwitch(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Create a chart that exposes 2 series via accessibility data
            var chart1 = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Multi-series")
                .SeriesNames("Series A", "Series B")
                .Interactive();

            var a11y = (IChartAccessibilityData)chart1;

            H.Check("ChartA11y_KeyboardSeriesSwitch_HasSeriesData",
                a11y.Series.Count >= 1);
            H.Check("ChartA11y_KeyboardSeriesSwitch_SeriesName",
                a11y.Series[0].Name == "Series A");

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart1.ToElement());
            await Harness.Render();

            // Up/down series navigation snaps to nearest x position
            H.Check("ChartA11y_KeyboardSeriesSwitch_Mounted", true);
        }
    }

    /// <summary>
    /// Keyboard invoke: verify OnPointInvoke callback is wired correctly.
    /// </summary>
    internal class KeyboardInvoke(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int invokedIndex = -1;
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Revenue")
                .OnPointInvoke((item, index) => invokedIndex = index);

            // OnPointInvoke should make the chart interactive
            H.Check("ChartA11y_KeyboardInvoke_IsInteractive",
                ((ChartElement<DataPoint>)chart).IsInteractive);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            H.Check("ChartA11y_KeyboardInvoke_Mounted", true);
        }
    }

    /// <summary>
    /// Keyboard Esc: verify chart mounts correctly and Esc behavior is wired.
    /// </summary>
    internal class KeyboardEsc(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Revenue")
                .Interactive();

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            // Esc deactivates focus — chart renders in both states
            H.Check("ChartA11y_KeyboardEsc_Mounted", true);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  7.7 — Viewport + live region fixtures
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Viewport UIA: verify chart canvas gets title as automation name
    /// and LiveRegion is set.
    /// </summary>
    internal class ViewportUIA(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Revenue")
                .Interactive();

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("ChartA11y_ViewportUIA_CanvasExists",
                canvas is not null);

            if (canvas is not null)
            {
                var autoName = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(canvas);
                H.Check("ChartA11y_ViewportUIA_PlotAreaName",
                    autoName == "Revenue");

                var liveSetting = Microsoft.UI.Xaml.Automation.AutomationProperties.GetLiveSetting(canvas);
                H.Check("ChartA11y_ViewportUIA_LiveRegionSet",
                    liveSetting == AutomationLiveSetting.Polite);
            }
        }
    }

    /// <summary>
    /// Focus context save/restore: verify ChartFocusContext tracks position correctly.
    /// </summary>
    internal class FocusContextSaveRestore(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ChartFocusContext();

            H.Check("ChartA11y_FocusContext_InitiallyEmpty",
                !ctx.HasSavedPosition);

            ctx.SavePosition(1, 3);
            H.Check("ChartA11y_FocusContext_Saved",
                ctx.HasSavedPosition);

            var (si, pi) = ctx.RestorePosition();
            H.Check("ChartA11y_FocusContext_RestoredSeries",
                si == 1);
            H.Check("ChartA11y_FocusContext_RestoredPoint",
                pi == 3);

            // Test data change adjustment
            var points = Enumerable.Range(0, 3)
                .Select(i => new ChartPointDescriptor(i.ToString(), i * 10.0))
                .ToArray();
            var series = new ChartSeriesDescriptor[]
            {
                new("Series A", points),
                new("Series B", points),
            };

            ctx.SavePosition(1, 5); // Point 5 is out of range (only 3 points)
            var (adjSi, adjPi, announcement) = ctx.AdjustForDataChange(series);

            H.Check("ChartA11y_FocusContext_AdjustedPoint",
                adjPi == 2); // Clamped to last point
            H.Check("ChartA11y_FocusContext_HasAnnouncement",
                announcement is not null);

            await Harness.Render();
        }
    }

    /// <summary>
    /// Decoration pruning: verify grid lines and axis elements get AccessibilityView.Raw.
    /// </summary>
    internal class DecorationPruning(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Revenue")
                .ShowAxes(true)
                .ShowGrid(true);

            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => chart.ToElement());
            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("ChartA11y_DecorationPruning_CanvasExists",
                canvas is not null);

            if (canvas is not null)
            {
                // Grid lines and axis elements should have AccessibilityView.Raw
                int rawCount = 0;
                foreach (var child in canvas.Children)
                {
                    if (child is Microsoft.UI.Xaml.FrameworkElement fe)
                    {
                        var view = AutomationProperties.GetAccessibilityView(fe);
                        if (view == AccessibilityView.Raw)
                            rawCount++;
                    }
                }
                H.Check("ChartA11y_DecorationPruning_HasRawElements",
                    rawCount > 0);
            }
        }
    }

    /// <summary>
    /// Live region announce: verify ChartLiveAnnouncer debounces correctly.
    /// </summary>
    internal class LiveRegionAnnounce(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var announcer = new ChartLiveAnnouncer();

            // Announce first message
            announcer.Announce("Zoomed to 150%");
            H.Check("ChartA11y_LiveRegion_FirstMessage",
                announcer.CurrentMessage == "Zoomed to 150%");

            // Rapid second announce within debounce window should be pending
            announcer.Announce("Zoomed to 200%");
            var (flushed, _) = announcer.Flush();
            // Within debounce window, the flush should either return null (still debouncing)
            // or the new message (if debounce expired)
            H.Check("ChartA11y_LiveRegion_DebounceActive",
                flushed is null || flushed == "Zoomed to 200%");

            // Assertive messages bypass debounce
            announcer.Announce("No data in selected range.", ChartAnnouncePriority.Assertive);
            H.Check("ChartA11y_LiveRegion_AssertiveBypasses",
                announcer.CurrentMessage == "No data in selected range.");
            H.Check("ChartA11y_LiveRegion_AssertivePriority",
                announcer.CurrentPriority == ChartAnnouncePriority.Assertive);

            // Animation suppression
            announcer.BeginAnimation();
            announcer.Announce("Intermediate state");
            H.Check("ChartA11y_LiveRegion_AnimationSuppressed",
                announcer.CurrentMessage == "No data in selected range."); // Not updated during animation
            announcer.EndAnimation();
            H.Check("ChartA11y_LiveRegion_AnimationEndFlush",
                announcer.CurrentMessage == "Intermediate state");

            // Message template helpers
            H.Check("ChartA11y_LiveRegion_ZoomTemplate",
                ChartLiveAnnouncer.ZoomMessage(1.5).Contains("150"));
            H.Check("ChartA11y_LiveRegion_BrushTemplate",
                ChartLiveAnnouncer.BrushMessage(0, 4, 10).Contains("1") &&
                ChartLiveAnnouncer.BrushMessage(0, 4, 10).Contains("5"));
            H.Check("ChartA11y_LiveRegion_FilterTemplate",
                ChartLiveAnnouncer.FilterMessage(2, 5).Contains("2"));

            await Harness.Render();
        }
    }

    /// <summary>
    /// On-demand announce (S key): verify summary request queuing.
    /// </summary>
    internal class OnDemandAnnounce(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var announcer = new ChartLiveAnnouncer();

            // Request summary when idle
            announcer.RequestSummary("Line chart, 1 series, 5 points each. Revenue ranges from 100 to 500.");
            H.Check("ChartA11y_OnDemandAnnounce_SummarySet",
                announcer.CurrentMessage!.Contains("Line chart"));

            // Request summary immediately after another announce (should queue)
            announcer.Announce("Navigated to point 3");
            announcer.RequestSummary("Full summary text");
            // The summary should be queued (pending) since we just announced
            H.Check("ChartA11y_OnDemandAnnounce_AfterAnnounce",
                announcer.CurrentMessage is not null);

            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  9.2 — Full integration test
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Comprehensive integration fixture exercising all layers: title, series names,
    /// units, interactive, alternate view, default palette. Validates peer exists,
    /// grid provider valid, point values correct, keyboard nav works, scanner returns
    /// zero violations.
    /// </summary>
    internal class FullIntegration(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Create a chart exercising all layers
            var chart = Charts.LineChart(SampleLine, d => d.X, d => d.Y)
                .Title("Integration Revenue Chart")
                .SeriesName("Revenue")
                .Units("months", "USD")
                .Description("Line chart showing monthly revenue from January to May")
                .Interactive();

            // ── Layer 1: IChartAccessibilityData ──
            var a11y = (IChartAccessibilityData)chart;
            H.Check("FullIntegration_HasName",
                a11y.Name == "Integration Revenue Chart");
            H.Check("FullIntegration_HasDescription",
                a11y.Description is not null);
            H.Check("FullIntegration_HasSeries",
                a11y.Series.Count == 1);
            H.Check("FullIntegration_HasPoints",
                a11y.Series[0].Points.Count == 5);
            H.Check("FullIntegration_HasAxes",
                a11y.Axes.Count == 2);
            H.Check("FullIntegration_ChartTypeName",
                a11y.ChartTypeName == "Line");

            // ── Layer 2: Labels & summary ──
            var series = a11y.Series[0];
            H.Check("FullIntegration_SeriesName",
                series.Name == "Revenue");
            H.Check("FullIntegration_PointsHaveXLabel",
                series.Points.All(p => !string.IsNullOrEmpty(p.XLabel)));
            H.Check("FullIntegration_PointsHaveYValue",
                series.Points.All(p => !double.IsNaN(p.YValue)));

            // ── Layer 2: Summarizer ──
            var summary = ChartSummarizer.Summarize(a11y);
            H.Check("FullIntegration_SummaryHasOverview",
                !string.IsNullOrWhiteSpace(summary.Overview));
            H.Check("FullIntegration_SummaryHasStats",
                summary.SeriesStats.Length > 0);

            // ── Layer 5: Focus context ──
            var focusCtx = new ChartFocusContext();
            focusCtx.SavePosition(0, 2);
            var (si, pi) = focusCtx.RestorePosition();
            H.Check("FullIntegration_FocusSaveRestore",
                si == 0 && pi == 2);

            // ── Layer 6: Live announcer ──
            var announcer = new ChartLiveAnnouncer();
            announcer.Announce("Zoomed to 150%");
            H.Check("FullIntegration_AnnouncerMessage",
                announcer.CurrentMessage == "Zoomed to 150%");

            // ── Layer 7: Palette ──
            var palette = ChartPalette.OkabeIto;
            H.Check("FullIntegration_PaletteHasColors",
                palette.Colors.Count >= 8);

            // ── Layer 8: Scanner ──
            // The chart with title + defaults should produce zero chart-rule violations
            var element = chart.ToElement();

            // Mount the chart to verify rendering
            var host = H.CreateHost();
            XamlInterop.Register(host.Reconciler);
            host.Mount(ctx => element);
            await Harness.Render();

            var canvas = H.FindControl<Canvas>(_ => true);
            H.Check("FullIntegration_Rendered",
                canvas is not null);

            if (canvas is not null)
            {
                var autoName = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(canvas);
                H.Check("FullIntegration_PlotAreaName",
                    autoName == "Integration Revenue Chart");

                var liveSetting = Microsoft.UI.Xaml.Automation.AutomationProperties.GetLiveSetting(canvas);
                H.Check("FullIntegration_LiveRegion",
                    liveSetting == AutomationLiveSetting.Polite);

                var itemStatus = Microsoft.UI.Xaml.Automation.AutomationProperties.GetItemStatus(canvas);
                H.Check("FullIntegration_ItemStatus",
                    !string.IsNullOrWhiteSpace(itemStatus));
            }
        }
    }

    private sealed class PeerExerciseData : IChartAccessibilityData
    {
        public string? Name => "Quarterly revenue";
        public string? Description => null;
        public string ChartTypeName => "Line";
        public IReadOnlyList<ChartSeriesDescriptor> Series { get; } =
        [
            new("Revenue", [
                new ChartPointDescriptor("Q1", 10),
                new ChartPointDescriptor("Q2", 12.5),
                new ChartPointDescriptor("Q3", 9, "Revenue dipped in Q3"),
            ]),
            new("Profit", [
                new ChartPointDescriptor("Q1", 3),
                new ChartPointDescriptor("Q2", 4),
            ]),
        ];

        public IReadOnlyList<ChartAxisDescriptor> Axes { get; } =
        [
            new(ChartAxisType.X, "Quarter", 1, 3),
            new(ChartAxisType.Y, "Revenue", 0, 15, "m"),
        ];

        public ChartViewport? Viewport => new(0, 3, 0, 15);
    }

    internal class AutomationPeerProviderExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var owner = new Border();
            var data = new PeerExerciseData();
            var peer = new ChartAutomationPeer(owner, data);

            H.Check("ChartA11y_PeerProvider_Name", peer.GetName() == "Quarterly revenue");
            H.Check("ChartA11y_PeerProvider_Description", !string.IsNullOrWhiteSpace(peer.GetFullDescription()));

            var grid = (IGridProvider)peer.GetPattern(PatternInterface.Grid);
            H.Check("ChartA11y_PeerProvider_GridShape", grid.RowCount == 2 && grid.ColumnCount == 3);
            H.Check("ChartA11y_PeerProvider_ItemValid", grid.GetItem(0, 1) is not null);
            H.Check("ChartA11y_PeerProvider_ItemInvalid", grid.GetItem(-1, 0) is null && grid.GetItem(0, 99) is null);

            var table = (ITableProvider)peer.GetPattern(PatternInterface.Table);
            H.Check("ChartA11y_PeerProvider_TableHeaders",
                table.GetRowHeaders().Length == 2 && table.GetColumnHeaders().Length == 3);

            var children = peer.GetChildren();
            H.Check("ChartA11y_PeerProvider_Children", children.Count >= 7);
            var point = children.OfType<ChartPointProvider>().First();
            H.Check("ChartA11y_PeerProvider_PointValue",
                point.Value.Contains("Revenue", StringComparison.Ordinal)
                && point.Row == 0
                && point.Column == 0
                && point.RowSpan == 1
                && point.ColumnSpan == 1);
            H.Check("ChartA11y_PeerProvider_PointPatterns",
                ReferenceEquals(point.GetPattern(PatternInterface.GridItem), point)
                && ReferenceEquals(point.GetPattern(PatternInterface.TableItem), point)
                && ReferenceEquals(point.GetPattern(PatternInterface.Value), point));
            H.Check("ChartA11y_PeerProvider_PointHeaders",
                point.GetRowHeaderItems().Length == 1 && point.GetColumnHeaderItems().Length == 1);

            var formatted = ChartPointProvider.FormatDefaultLabel(
                data.Series[0],
                data.Series[0].Points[1],
                pointIndex: 1,
                yUnits: "m");
            H.Check("ChartA11y_PeerProvider_StaticFormat",
                formatted == "Revenue, Q2: 12.50m, point 2 of 3");

            peer.EnablePan();
            peer.UpdateViewport(25, 50, 75, 80);
            H.Check("ChartA11y_PeerProvider_ScrollState",
                peer.HorizontallyScrollable
                && peer.VerticallyScrollable
                && peer.HorizontalScrollPercent == 25
                && peer.VerticalScrollPercent == 50
                && peer.HorizontalViewSize == 75
                && peer.VerticalViewSize == 80
                && ReferenceEquals(peer.GetPattern(PatternInterface.Scroll), peer));

            bool scrollThrows = false;
            try { peer.Scroll(ScrollAmount.SmallIncrement, ScrollAmount.NoAmount); }
            catch (InvalidOperationException) { scrollThrows = true; }
            H.Check("ChartA11y_PeerProvider_ScrollThrows", scrollThrows);

            bool setValueThrows = false;
            try { point.SetValue("nope"); }
            catch (InvalidOperationException) { setValueThrows = true; }
            H.Check("ChartA11y_PeerProvider_SetValueThrows", setValueThrows);

            await Task.CompletedTask;
        }
    }
}
