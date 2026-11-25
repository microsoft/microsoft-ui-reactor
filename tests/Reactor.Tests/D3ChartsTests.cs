using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Charting.Accessibility;
using Microsoft.UI.Reactor.Core;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for D3Charts's pure functions: number formatting (Fmt), palette cycling
/// (ChartSeriesDash, ChartSeriesMarker), and state flag management.
/// Note: Brush-creating methods (Gray, ChartForeground, etc.) require WinUI
/// thread context and are tested in selftest fixtures.
/// </summary>
public class D3ChartsTests
{
    [Theory]
    [InlineData(12.5, 37.25, 12.5, 37.25)]
    [InlineData(-2, 8, 0, 8)]
    [InlineData(8, -2, 8, 0)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(double.NaN, 8, double.NaN, 8)]
    [InlineData(8, double.NaN, 8, double.NaN)]
    [InlineData(double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity, 0)]
    public void D3Rect_Preserves_Dimensions_Position_And_Defaults(
        double width, double height, double expectedWidth, double expectedHeight)
    {
        var actual = D3Charts.D3Rect(-7.5, 13.25, width, height);
        var expected = Rectangle().Width(expectedWidth).Height(expectedHeight);

        Assert.Equal(expected, actual with { Attached = null });
        Assert.Single(actual.Attached!);
        Assert.Equal(new CanvasAttached(-7.5, 13.25), actual.GetAttached<CanvasAttached>());
    }

    [Theory]
    [InlineData(3.5)]
    [InlineData(0)]
    [InlineData(-3.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void D3Circle_Preserves_Diameter_Center_And_Defaults(double radius)
    {
        var actual = D3Charts.D3Circle(-7.5, 13.25, radius);
        var expected = Ellipse().Width(radius * 2).Height(radius * 2);

        Assert.Equal(expected, actual with { Attached = null });
        Assert.Single(actual.Attached!);
        Assert.Equal(new CanvasAttached(-7.5 - radius, 13.25 - radius), actual.GetAttached<CanvasAttached>());
    }

    // ════════════════════════════════════════════════════════════════
    //  Fmt — number formatting for axis labels and data annotations
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1_234_567, "1.2M")]
    [InlineData(5_000_000, "5M")]
    [InlineData(10_500_000, "10.5M")]
    [InlineData(-2_300_000, "-2.3M")]
    public void Fmt_Formats_Millions(double value, string expected)
    {
        Assert.Equal(expected, D3Charts.Fmt(value));
    }

    [Theory]
    [InlineData(1234, "1.2k")]
    [InlineData(5000, "5k")]
    [InlineData(99_999, "100k")]
    [InlineData(-4500, "-4.5k")]
    public void Fmt_Formats_Thousands(double value, string expected)
    {
        Assert.Equal(expected, D3Charts.Fmt(value));
    }

    [Theory]
    [InlineData(42, "42")]
    [InlineData(0, "0")]
    [InlineData(-7, "-7")]
    [InlineData(100, "100")]
    public void Fmt_Formats_Integers_Without_Decimals(double value, string expected)
    {
        Assert.Equal(expected, D3Charts.Fmt(value));
    }

    [Theory]
    [InlineData(3.14159, "3.142")]
    public void Fmt_Formats_Decimals_With_Precision(double value, string expected)
    {
        Assert.Equal(expected, D3Charts.Fmt(value));
    }

    [Fact]
    public void Fmt_Small_Decimal_Uses_G4()
    {
        // 0.5 is a decimal that should use G4 format
        var result = D3Charts.Fmt(0.5);
        Assert.Equal("0.5", result);
    }

    [Fact]
    public void Fmt_Zero_Returns_Zero()
    {
        Assert.Equal("0", D3Charts.Fmt(0));
    }

    [Fact]
    public void Fmt_Boundary_At_1000()
    {
        // Exactly 1000 should format as "1k"
        Assert.Equal("1k", D3Charts.Fmt(1000));
    }

    [Fact]
    public void Fmt_Boundary_At_1_000_000()
    {
        Assert.Equal("1M", D3Charts.Fmt(1_000_000));
    }

    [Fact]
    public void Fmt_Negative_Below_Thousand()
    {
        Assert.Equal("-42", D3Charts.Fmt(-42));
    }

    // ════════════════════════════════════════════════════════════════
    //  Palette cycling — ChartSeriesDash, ChartSeriesMarker
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ChartSeriesDash_Cycles_Through_Dash_Styles()
    {
        var cycleLen = ChartPalette.DefaultDashCycle.Length;
        for (int i = 0; i < cycleLen * 2; i++)
        {
            var dash = D3Charts.ChartSeriesDash(i);
            Assert.Equal(ChartPalette.DefaultDashCycle[i % cycleLen], dash);
        }
    }

    [Fact]
    public void ChartSeriesMarker_Cycles_Through_Marker_Shapes()
    {
        var cycleLen = ChartPalette.DefaultMarkerCycle.Length;
        for (int i = 0; i < cycleLen * 2; i++)
        {
            var marker = D3Charts.ChartSeriesMarker(i);
            Assert.Equal(ChartPalette.DefaultMarkerCycle[i % cycleLen], marker);
        }
    }

    [Fact]
    public void ChartSeriesDash_Handles_Negative_Index()
    {
        var dash = D3Charts.ChartSeriesDash(-1);
        Assert.True(Enum.IsDefined(dash));
    }

    [Fact]
    public void ChartSeriesMarker_Handles_Negative_Index()
    {
        var marker = D3Charts.ChartSeriesMarker(-1);
        Assert.True(Enum.IsDefined(marker));
    }

    [Fact]
    public void ChartSeriesDash_Large_Index_Wraps()
    {
        var cycleLen = ChartPalette.DefaultDashCycle.Length;
        var d1 = D3Charts.ChartSeriesDash(0);
        var d2 = D3Charts.ChartSeriesDash(cycleLen);
        Assert.Equal(d1, d2);
    }

    [Fact]
    public void ChartSeriesMarker_Large_Index_Wraps()
    {
        var cycleLen = ChartPalette.DefaultMarkerCycle.Length;
        var m1 = D3Charts.ChartSeriesMarker(0);
        var m2 = D3Charts.ChartSeriesMarker(cycleLen);
        Assert.Equal(m1, m2);
    }

    // ════════════════════════════════════════════════════════════════
    //  Thread-static state flags
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void IsDarkTheme_Can_Be_Toggled()
    {
        var prev = D3Charts.IsDarkTheme;
        try
        {
            D3Charts.IsDarkTheme = true;
            Assert.True(D3Charts.IsDarkTheme);

            D3Charts.IsDarkTheme = false;
            Assert.False(D3Charts.IsDarkTheme);
        }
        finally { D3Charts.IsDarkTheme = prev; }
    }

    [Fact]
    public void IsForcedColors_Can_Be_Toggled()
    {
        var prev = D3Charts.IsForcedColors;
        try
        {
            D3Charts.IsForcedColors = true;
            Assert.True(D3Charts.IsForcedColors);

            D3Charts.IsForcedColors = false;
            Assert.False(D3Charts.IsForcedColors);
        }
        finally { D3Charts.IsForcedColors = prev; }
    }

    [Fact]
    public void IsReducedMotion_Can_Be_Toggled()
    {
        var prev = D3Charts.IsReducedMotion;
        try
        {
            D3Charts.IsReducedMotion = true;
            Assert.True(D3Charts.IsReducedMotion);

            D3Charts.IsReducedMotion = false;
            Assert.False(D3Charts.IsReducedMotion);
        }
        finally { D3Charts.IsReducedMotion = prev; }
    }

    [Fact]
    public void ForcedColors_Nullable_Property()
    {
        var prev = D3Charts.ForcedColors;
        try
        {
            D3Charts.ForcedColors = null;
            Assert.Null(D3Charts.ForcedColors);

            D3Charts.ForcedColors = ForcedColorsTheme.Default;
            Assert.NotNull(D3Charts.ForcedColors);
        }
        finally { D3Charts.ForcedColors = prev; }
    }

    // ════════════════════════════════════════════════════════════════
    //  Palette static property
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Palette_Is_Category10_And_Has_10_Colors()
    {
        Assert.Equal(10, D3Charts.Palette.Count);
    }

    [Fact]
    public void Palette_Colors_Are_Distinct()
    {
        var set = new HashSet<(byte, byte, byte)>();
        foreach (var c in D3Charts.Palette)
            set.Add((c.R, c.G, c.B));
        Assert.Equal(D3Charts.Palette.Count, set.Count);
    }
}
