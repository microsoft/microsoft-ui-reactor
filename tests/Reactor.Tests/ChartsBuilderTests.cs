using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Charting.Accessibility;
using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for Charts factory methods and ChartElement / PieChartElement builder chains.
/// All builders return 'this', enabling fluent API validation without WinUI.
/// </summary>
public class ChartsBuilderTests
{
    private record DataPoint(double X, double Y);
    private static readonly List<DataPoint> SampleData = new()
    {
        new(1, 10), new(2, 20), new(3, 15)
    };

    // ════════════════════════════════════════════════════════════════
    //  Factory methods
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LineChart_CreatesElement()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y);
        Assert.NotNull(chart);
    }

    [Fact]
    public void BarChart_CreatesElement()
    {
        var chart = Charts.BarChart(SampleData, d => d.X, d => d.Y);
        Assert.NotNull(chart);
    }

    [Fact]
    public void AreaChart_CreatesElement()
    {
        var chart = Charts.AreaChart(SampleData, d => d.X, d => d.Y);
        Assert.NotNull(chart);
    }

    [Fact]
    public void PieChart_CreatesElement()
    {
        var chart = Charts.PieChart(SampleData, d => d.Y);
        Assert.NotNull(chart);
    }

    [Fact]
    public void PieChart_WithLabel()
    {
        var chart = Charts.PieChart(SampleData, d => d.Y, d => $"Item {d.X}");
        Assert.NotNull(chart);
    }

    // ════════════════════════════════════════════════════════════════
    //  ChartElement fluent builder (returns this)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ChartElement_Width_Height()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .Width(800)
            .Height(600);
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_Margin()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .Margin(10, 20, 30, 40);
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_StrokeAndFill()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .Stroke("#ff0000")
            .Fill("#00ff00")
            .StrokeWidth(3)
            .FillOpacity(0.5);
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_ShowAxesAndGrid()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .ShowAxes(false)
            .ShowGrid(false);
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_Title_Description()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .Title("Sales Chart")
            .Description("Shows monthly sales data");
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_SeriesName()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .SeriesName("Revenue");
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_SeriesNames()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .SeriesNames("Series A", "Series B");
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_DataLabel()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .DataLabel((item, idx) => $"Point {idx}: {item.Y}");
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_Units()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .Units("months", "USD");
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_AxisLabel()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .AxisLabel(ChartAxisType.X, "Time")
            .AxisLabel(ChartAxisType.Y, "Value");
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_Palette()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .Palette(ChartPalette.OkabeIto);
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_ColorOnly()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .ColorOnly();
        Assert.True(chart.IsColorOnly);
    }

    [Fact]
    public void ChartElement_SeriesShapes()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .SeriesShapes(MarkerShape.Circle, MarkerShape.Square);
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_SeriesDashes()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .SeriesDashes(DashStyle.Solid, DashStyle.Dash4_2);
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_Interactive()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .Interactive();
        Assert.True(chart.IsInteractive);
    }

    [Fact]
    public void ChartElement_DisableKeyboard()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .DisableKeyboard();
        Assert.True(chart.IsKeyboardDisabled);
    }

    [Fact]
    public void ChartElement_TightHitTest()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .TightHitTest();
        Assert.True(chart.IsTightHitTest);
    }

    [Fact]
    public void ChartElement_OnPointInvoke_SetsInteractive()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .OnPointInvoke((item, idx) => { });
        Assert.True(chart.IsInteractive);
    }

    [Fact]
    public void ChartElement_OnBrushChanged_SetsInteractive()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .OnBrushChanged(range => { });
        Assert.True(chart.IsInteractive);
    }

    [Fact]
    public void ChartElement_AnnounceEveryFrame()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .AnnounceEveryFrame();
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_OnReady()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .OnReady(handle => { });
        Assert.NotNull(chart);
    }

    [Fact]
    public void ChartElement_FullChain()
    {
        var chart = Charts.LineChart(SampleData, d => d.X, d => d.Y)
            .Width(600)
            .Height(400)
            .Margin(10, 10, 30, 40)
            .Stroke("#333")
            .Fill("#eee")
            .StrokeWidth(1.5)
            .FillOpacity(0.7)
            .ShowAxes(true)
            .ShowGrid(true)
            .Title("Test Chart")
            .Description("A test chart")
            .SeriesName("Test")
            .DataLabel((d, i) => $"{d.Y}")
            .Units("X", "Y")
            .AxisLabel(ChartAxisType.X, "X Axis")
            .Interactive()
            .AnnounceEveryFrame();
        Assert.NotNull(chart);
        Assert.True(chart.IsInteractive);
    }

    // ════════════════════════════════════════════════════════════════
    //  ChartRange record
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ChartRange_Record()
    {
        var range = new ChartRange(1.0, 5.0);
        Assert.Equal(1.0, range.Start);
        Assert.Equal(5.0, range.End);
    }

    [Fact]
    public void ChartType_Values()
    {
        Assert.NotEqual(ChartType.Line, ChartType.Bar);
        Assert.NotEqual(ChartType.Bar, ChartType.Area);
    }
}
