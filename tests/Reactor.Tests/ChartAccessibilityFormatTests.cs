using Microsoft.UI.Reactor.Charting.Accessibility;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for ChartPointProvider's pure label formatting logic and
/// ChartAxisDescriptor/ChartSeriesDescriptor data structures.
/// These test the accessibility text generation that screen readers use to
/// announce chart data points.
/// </summary>
public class ChartAccessibilityFormatTests
{
    // ════════════════════════════════════════════════════════════════
    //  ChartPointProvider.FormatDefaultLabel — accessibility label generation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void FormatDefaultLabel_Without_PointIndex_Omits_Position()
    {
        var series = new ChartSeriesDescriptor("Revenue", new[]
        {
            new ChartPointDescriptor("Jan", 1000),
            new ChartPointDescriptor("Feb", 2000),
        });
        var point = series.Points[0];

        var label = ChartPointProvider.FormatDefaultLabel(series, point);
        Assert.Equal("Revenue, Jan: 1,000", label);
    }

    [Fact]
    public void FormatDefaultLabel_With_PointIndex_Includes_Position()
    {
        var series = new ChartSeriesDescriptor("Revenue", new[]
        {
            new ChartPointDescriptor("Jan", 1000),
            new ChartPointDescriptor("Feb", 2000),
            new ChartPointDescriptor("Mar", 3000),
        });
        var point = series.Points[1];

        var label = ChartPointProvider.FormatDefaultLabel(series, point, pointIndex: 1);
        Assert.Equal("Revenue, Feb: 2,000, point 2 of 3", label);
    }

    [Fact]
    public void FormatDefaultLabel_With_Units_Appends_Unit_String()
    {
        var series = new ChartSeriesDescriptor("Temperature", new[]
        {
            new ChartPointDescriptor("Mon", 22.5),
        });
        var point = series.Points[0];

        var label = ChartPointProvider.FormatDefaultLabel(series, point, yUnits: "°C");
        Assert.Equal("Temperature, Mon: 22.50°C", label);
    }

    [Fact]
    public void FormatDefaultLabel_Integer_Value_Shows_No_Decimals()
    {
        var series = new ChartSeriesDescriptor("Count", new[]
        {
            new ChartPointDescriptor("Q1", 42.0),
        });
        var point = series.Points[0];

        var label = ChartPointProvider.FormatDefaultLabel(series, point);
        Assert.Equal("Count, Q1: 42", label);
    }

    [Fact]
    public void FormatDefaultLabel_Float_Value_Shows_Two_Decimals()
    {
        var series = new ChartSeriesDescriptor("Price", new[]
        {
            new ChartPointDescriptor("Day1", 3.14159),
        });
        var point = series.Points[0];

        var label = ChartPointProvider.FormatDefaultLabel(series, point);
        Assert.Equal("Price, Day1: 3.14", label);
    }

    [Fact]
    public void FormatDefaultLabel_Large_Number_Uses_Grouping()
    {
        var series = new ChartSeriesDescriptor("Sales", new[]
        {
            new ChartPointDescriptor("2024", 1234567.0),
        });
        var point = series.Points[0];

        var label = ChartPointProvider.FormatDefaultLabel(series, point);
        Assert.Contains("1,234,567", label);
    }

    // ════════════════════════════════════════════════════════════════
    //  ChartPointDescriptor — pre-formatted label override
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ChartPointDescriptor_FormattedLabel_Overrides_Default()
    {
        var point = new ChartPointDescriptor("Jan", 1000, FormattedLabel: "$1,000 revenue in January");
        Assert.Equal("$1,000 revenue in January", point.FormattedLabel);
    }

    [Fact]
    public void ChartPointDescriptor_FormattedLabel_Null_By_Default()
    {
        var point = new ChartPointDescriptor("Jan", 1000);
        Assert.Null(point.FormattedLabel);
    }

    // ════════════════════════════════════════════════════════════════
    //  ChartAxisDescriptor — range and step calculations
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ChartAxisDescriptor_Records_Min_Max()
    {
        var axis = new ChartAxisDescriptor(ChartAxisType.X, "Month", 0, 100);
        Assert.Equal(0, axis.Min);
        Assert.Equal(100, axis.Max);
        Assert.Equal("Month", axis.Label);
        Assert.Equal(ChartAxisType.X, axis.AxisType);
    }

    [Fact]
    public void ChartAxisDescriptor_Units_Null_By_Default()
    {
        var axis = new ChartAxisDescriptor(ChartAxisType.Y, "Value", 0, 50);
        Assert.Null(axis.Units);
    }

    [Fact]
    public void ChartAxisDescriptor_Units_Can_Be_Set()
    {
        var axis = new ChartAxisDescriptor(ChartAxisType.Y, "Temperature", -10, 40, Units: "°C");
        Assert.Equal("°C", axis.Units);
    }

    // ════════════════════════════════════════════════════════════════
    //  ChartSeriesDescriptor — series structure
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ChartSeriesDescriptor_Holds_Points()
    {
        var points = new[]
        {
            new ChartPointDescriptor("A", 1),
            new ChartPointDescriptor("B", 2),
            new ChartPointDescriptor("C", 3),
        };
        var series = new ChartSeriesDescriptor("TestSeries", points);
        Assert.Equal("TestSeries", series.Name);
        Assert.Equal(3, series.Points.Count);
    }

    // ════════════════════════════════════════════════════════════════
    //  ChartViewport — viewport bounds
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ChartViewport_Records_Bounds()
    {
        var vp = new ChartViewport(0, 100, -50, 50);
        Assert.Equal(0, vp.XMin);
        Assert.Equal(100, vp.XMax);
        Assert.Equal(-50, vp.YMin);
        Assert.Equal(50, vp.YMax);
    }

    // ════════════════════════════════════════════════════════════════
    //  ChartAxisType enum
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ChartAxisType_Has_X_And_Y()
    {
        Assert.Equal(0, (int)ChartAxisType.X);
        Assert.Equal(1, (int)ChartAxisType.Y);
    }
}
