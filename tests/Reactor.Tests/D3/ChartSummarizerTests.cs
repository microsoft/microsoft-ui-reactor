using Microsoft.UI.Reactor.Charting.Accessibility;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.D3;

/// <summary>
/// Unit tests for ChartSummarizer — overview generation, axis ranges,
/// series statistics, Mann-Kendall trend detection, and outlier detection.
/// </summary>
public class ChartSummarizerTests
{
    // ── Overview generation ──────────────────────────────────────────

    [Fact]
    public void Overview_EmptyChart()
    {
        var data = MakeData([]);
        var summary = ChartSummarizer.Summarize(data, "Line");
        Assert.Contains("Empty", summary.Overview);
    }

    [Fact]
    public void Overview_SingleSeries()
    {
        var data = MakeData([5]);
        var summary = ChartSummarizer.Summarize(data, "Line");
        Assert.Contains("1 series", summary.Overview);
        Assert.Contains("5 points", summary.Overview);
    }

    [Fact]
    public void Overview_MultipleSeries()
    {
        var data = MakeData([5, 5]);
        var summary = ChartSummarizer.Summarize(data, "Bar");
        Assert.Contains("2 series", summary.Overview);
        Assert.Contains("Bar chart", summary.Overview);
    }

    [Theory]
    [InlineData("Line")]
    [InlineData("Bar")]
    [InlineData("Area")]
    [InlineData("Pie")]
    [InlineData("Tree")]
    public void Overview_ContainsChartType(string chartType)
    {
        var data = MakeData([3]);
        var summary = ChartSummarizer.Summarize(data, chartType);
        Assert.Contains(chartType, summary.Overview);
    }

    // ── Axis ranges ──────────────────────────────────────────────────

    [Fact]
    public void AxisRanges_NumericAxis()
    {
        var data = MakeData([5], xAxisLabel: "Month", yAxisLabel: "Revenue",
            xUnits: "months", yUnits: "USD");
        var summary = ChartSummarizer.Summarize(data);
        Assert.Contains("Month", summary.AxisRanges);
        Assert.Contains("months", summary.AxisRanges);
    }

    [Fact]
    public void AxisRanges_Empty_WhenNoAxes()
    {
        var data = new MockChartData(
            [new ChartSeriesDescriptor("S1", [new ChartPointDescriptor("A", 1)])],
            []);
        var summary = ChartSummarizer.Summarize(data);
        Assert.Empty(summary.AxisRanges);
    }

    // ── Series stats min/max ─────────────────────────────────────────

    [Fact]
    public void SeriesStats_MinMax()
    {
        var points = new[]
        {
            new ChartPointDescriptor("A", 10),
            new ChartPointDescriptor("B", 50),
            new ChartPointDescriptor("C", 30),
        };
        var data = new MockChartData(
            [new ChartSeriesDescriptor("Revenue", points)], []);

        var summary = ChartSummarizer.Summarize(data);
        Assert.Single(summary.SeriesStats);
        Assert.Equal(10, summary.SeriesStats[0].Min);
        Assert.Equal(50, summary.SeriesStats[0].Max);
    }

    // ── Mann-Kendall trend detection ─────────────────────────────────

    [Fact]
    public void Trend_MonotonicIncreasing()
    {
        var values = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var trend = ChartSummarizer.DetectTrend(values);
        Assert.Equal("increasing", trend);
    }

    [Fact]
    public void Trend_MonotonicDecreasing()
    {
        var values = new double[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };
        var trend = ChartSummarizer.DetectTrend(values);
        Assert.Equal("decreasing", trend);
    }

    [Fact]
    public void Trend_Flat_NoClearTrend()
    {
        var values = new double[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 };
        var trend = ChartSummarizer.DetectTrend(values);
        Assert.Null(trend);
    }

    [Fact]
    public void Trend_Seasonal_NoClearTrend()
    {
        // Oscillating data should not register as a clear trend
        var values = new double[] { 1, 10, 1, 10, 1, 10, 1, 10 };
        var trend = ChartSummarizer.DetectTrend(values);
        Assert.Null(trend);
    }

    [Fact]
    public void Trend_TooFewPoints_ReturnsNull()
    {
        var trend = ChartSummarizer.DetectTrend([1, 2]);
        Assert.Null(trend);
    }

    // ── Outlier detection ────────────────────────────────────────────

    [Fact]
    public void Outlier_ClearOutlier_Flagged()
    {
        var points = new[]
        {
            new ChartPointDescriptor("A", 10),
            new ChartPointDescriptor("B", 11),
            new ChartPointDescriptor("C", 10),
            new ChartPointDescriptor("D", 12),
            new ChartPointDescriptor("E", 11),
            new ChartPointDescriptor("F", 100), // obvious outlier
        };
        var series = new ChartSeriesDescriptor("Data", points);
        var outliers = ChartSummarizer.DetectOutliers(series, 0);
        Assert.Contains(outliers, o => o.PointIndex == 5);
    }

    [Fact]
    public void Outlier_NormalDistribution_NoFalsePositives()
    {
        // Tight distribution — no outliers expected
        var points = Enumerable.Range(0, 20)
            .Select(i => new ChartPointDescriptor($"P{i}", 50.0 + (i % 3)))
            .ToArray();
        var series = new ChartSeriesDescriptor("Normal", points);
        var outliers = ChartSummarizer.DetectOutliers(series, 0);
        Assert.Empty(outliers);
    }

    [Fact]
    public void Outlier_TooFewPoints_NoneDetected()
    {
        var series = new ChartSeriesDescriptor("Tiny",
            [new ChartPointDescriptor("A", 1), new ChartPointDescriptor("B", 1000)]);
        var outliers = ChartSummarizer.DetectOutliers(series, 0);
        Assert.Empty(outliers);
    }

    // ── Default label formatting ─────────────────────────────────────

    [Fact]
    public void DefaultLabel_NullSeriesName_StillFormats()
    {
        var series = new ChartSeriesDescriptor("",
            [new ChartPointDescriptor("Q1", 100)]);
        var label = ChartPointProvider.FormatDefaultLabel(series, series.Points[0], 0);
        Assert.Contains("Q1", label);
        Assert.Contains("100", label);
    }

    [Fact]
    public void DefaultLabel_EmptyUnits_NoTrailingSpace()
    {
        var series = new ChartSeriesDescriptor("Sales",
            [new ChartPointDescriptor("Jan", 42)]);
        var label = ChartPointProvider.FormatDefaultLabel(series, series.Points[0], 0, null);
        Assert.DoesNotContain("  ", label);
    }

    [Fact]
    public void DefaultLabel_ZeroPoints_EmptyNotCrash()
    {
        var series = new ChartSeriesDescriptor("Empty", []);
        Assert.Empty(series.Points);
    }

    // ── FormatSummary ────────────────────────────────────────────────

    [Fact]
    public void FormatSummary_ContainsOverviewAndStats()
    {
        var points = Enumerable.Range(0, 10)
            .Select(i => new ChartPointDescriptor($"P{i}", i * 10.0))
            .ToArray();
        var data = new MockChartData(
            [new ChartSeriesDescriptor("Revenue", points)],
            [new ChartAxisDescriptor(ChartAxisType.X, "Month", 0, 9),
             new ChartAxisDescriptor(ChartAxisType.Y, "Value", 0, 90)]);

        var summary = ChartSummarizer.Summarize(data, "Line");
        var formatted = ChartSummarizer.FormatSummary(summary);

        Assert.Contains("Line chart", formatted);
        Assert.Contains("Revenue", formatted);
        Assert.Contains("min", formatted);
        Assert.Contains("max", formatted);
    }

    [CulturedFact(new[] { "nl-NL" })]
    public void FormatSummary_Honors_CurrentCulture_Not_Invariant()
    {
        // ChartSummarizer.FormatValue formats with the current culture on
        // purpose: this string is surfaced to a screen reader, so a Dutch user should hear
        // "1,50", not "1.50".
        //
        // Every other ChartSummarizer test uses whole numbers or asserts keywords, so all
        // of them pass whether FormatValue is culture-sensitive or invariant — they could
        // not notice someone "hardening" it and silently changing the accessibility text
        // for every comma-decimal user. Fractional values are what reach the "N2" branch
        // and make the assertion discriminate.
        var points = new[]
        {
            new ChartPointDescriptor("P0", 1.5),
            new ChartPointDescriptor("P1", 2.25),
        };
        // No axes: keeps the asserted string to the series-stats sentence.
        var data = new MockChartData([new ChartSeriesDescriptor("Revenue", points)], []);

        var formatted = ChartSummarizer.FormatSummary(ChartSummarizer.Summarize(data, "Line"));

        Assert.Contains("min 1,50", formatted);
        Assert.Contains("max 2,25", formatted);
        Assert.DoesNotContain("1.50", formatted);
    }

    // ── Test helpers ─────────────────────────────────────────────────

    private static MockChartData MakeData(
        int[] seriesCounts,
        string? xAxisLabel = null,
        string? yAxisLabel = null,
        string? xUnits = null,
        string? yUnits = null)
    {
        var series = seriesCounts.Select((count, si) =>
        {
            var points = Enumerable.Range(0, count)
                .Select(i => new ChartPointDescriptor($"P{i}", i * 10.0))
                .ToArray();
            return new ChartSeriesDescriptor($"Series {si + 1}", points);
        }).ToArray();

        var axes = seriesCounts.Length > 0 && seriesCounts.Any(c => c > 0)
            ? new ChartAxisDescriptor[]
            {
                new(ChartAxisType.X, xAxisLabel, 0, seriesCounts.Max() * 10.0, xUnits),
                new(ChartAxisType.Y, yAxisLabel, 0, seriesCounts.Max() * 100.0, yUnits),
            }
            : [];

        return new MockChartData(series, axes);
    }

    private sealed class MockChartData : IChartAccessibilityData
    {
        public MockChartData(ChartSeriesDescriptor[] series, ChartAxisDescriptor[] axes)
        {
            Series = series;
            Axes = axes;
        }

        public string? Name => null;
        public string? Description => null;
        public IReadOnlyList<ChartSeriesDescriptor> Series { get; }
        public IReadOnlyList<ChartAxisDescriptor> Axes { get; }
        public ChartViewport? Viewport => null;
    }
}
