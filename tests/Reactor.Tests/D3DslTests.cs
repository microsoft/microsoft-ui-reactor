using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Charting.Accessibility;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for D3Dsl's pure functions: number formatting (Fmt), palette cycling
/// (ChartSeriesDash, ChartSeriesMarker), and state flag management.
/// Note: Brush-creating methods (Gray, ChartForeground, etc.) require WinUI
/// thread context and are tested in selftest fixtures.
/// </summary>
public class D3DslTests
{
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
        Assert.Equal(expected, D3Dsl.Fmt(value));
    }

    [Theory]
    [InlineData(1234, "1.2k")]
    [InlineData(5000, "5k")]
    [InlineData(99_999, "100k")]
    [InlineData(-4500, "-4.5k")]
    public void Fmt_Formats_Thousands(double value, string expected)
    {
        Assert.Equal(expected, D3Dsl.Fmt(value));
    }

    [Theory]
    [InlineData(42, "42")]
    [InlineData(0, "0")]
    [InlineData(-7, "-7")]
    [InlineData(100, "100")]
    public void Fmt_Formats_Integers_Without_Decimals(double value, string expected)
    {
        Assert.Equal(expected, D3Dsl.Fmt(value));
    }

    [Theory]
    [InlineData(3.14159, "3.142")]
    public void Fmt_Formats_Decimals_With_Precision(double value, string expected)
    {
        Assert.Equal(expected, D3Dsl.Fmt(value));
    }

    [Fact]
    public void Fmt_Small_Decimal_Uses_G4()
    {
        // 0.5 is a decimal that should use G4 format
        var result = D3Dsl.Fmt(0.5);
        Assert.Equal("0.5", result);
    }

    [Fact]
    public void Fmt_Zero_Returns_Zero()
    {
        Assert.Equal("0", D3Dsl.Fmt(0));
    }

    [Fact]
    public void Fmt_Boundary_At_1000()
    {
        // Exactly 1000 should format as "1k"
        Assert.Equal("1k", D3Dsl.Fmt(1000));
    }

    [Fact]
    public void Fmt_Boundary_At_1_000_000()
    {
        Assert.Equal("1M", D3Dsl.Fmt(1_000_000));
    }

    [Fact]
    public void Fmt_Negative_Below_Thousand()
    {
        Assert.Equal("-42", D3Dsl.Fmt(-42));
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
            var dash = D3Dsl.ChartSeriesDash(i);
            Assert.Equal(ChartPalette.DefaultDashCycle[i % cycleLen], dash);
        }
    }

    [Fact]
    public void ChartSeriesMarker_Cycles_Through_Marker_Shapes()
    {
        var cycleLen = ChartPalette.DefaultMarkerCycle.Length;
        for (int i = 0; i < cycleLen * 2; i++)
        {
            var marker = D3Dsl.ChartSeriesMarker(i);
            Assert.Equal(ChartPalette.DefaultMarkerCycle[i % cycleLen], marker);
        }
    }

    [Fact]
    public void ChartSeriesDash_Handles_Negative_Index()
    {
        var dash = D3Dsl.ChartSeriesDash(-1);
        Assert.True(Enum.IsDefined(dash));
    }

    [Fact]
    public void ChartSeriesMarker_Handles_Negative_Index()
    {
        var marker = D3Dsl.ChartSeriesMarker(-1);
        Assert.True(Enum.IsDefined(marker));
    }

    [Fact]
    public void ChartSeriesDash_Large_Index_Wraps()
    {
        var cycleLen = ChartPalette.DefaultDashCycle.Length;
        var d1 = D3Dsl.ChartSeriesDash(0);
        var d2 = D3Dsl.ChartSeriesDash(cycleLen);
        Assert.Equal(d1, d2);
    }

    [Fact]
    public void ChartSeriesMarker_Large_Index_Wraps()
    {
        var cycleLen = ChartPalette.DefaultMarkerCycle.Length;
        var m1 = D3Dsl.ChartSeriesMarker(0);
        var m2 = D3Dsl.ChartSeriesMarker(cycleLen);
        Assert.Equal(m1, m2);
    }

    // ════════════════════════════════════════════════════════════════
    //  Thread-static state flags
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void IsDarkTheme_Can_Be_Toggled()
    {
        var prev = D3Dsl.IsDarkTheme;
        try
        {
            D3Dsl.IsDarkTheme = true;
            Assert.True(D3Dsl.IsDarkTheme);

            D3Dsl.IsDarkTheme = false;
            Assert.False(D3Dsl.IsDarkTheme);
        }
        finally { D3Dsl.IsDarkTheme = prev; }
    }

    [Fact]
    public void IsForcedColors_Can_Be_Toggled()
    {
        var prev = D3Dsl.IsForcedColors;
        try
        {
            D3Dsl.IsForcedColors = true;
            Assert.True(D3Dsl.IsForcedColors);

            D3Dsl.IsForcedColors = false;
            Assert.False(D3Dsl.IsForcedColors);
        }
        finally { D3Dsl.IsForcedColors = prev; }
    }

    [Fact]
    public void IsReducedMotion_Can_Be_Toggled()
    {
        var prev = D3Dsl.IsReducedMotion;
        try
        {
            D3Dsl.IsReducedMotion = true;
            Assert.True(D3Dsl.IsReducedMotion);

            D3Dsl.IsReducedMotion = false;
            Assert.False(D3Dsl.IsReducedMotion);
        }
        finally { D3Dsl.IsReducedMotion = prev; }
    }

    [Fact]
    public void ForcedColors_Nullable_Property()
    {
        var prev = D3Dsl.ForcedColors;
        try
        {
            D3Dsl.ForcedColors = null;
            Assert.Null(D3Dsl.ForcedColors);

            D3Dsl.ForcedColors = ForcedColorsTheme.Default;
            Assert.NotNull(D3Dsl.ForcedColors);
        }
        finally { D3Dsl.ForcedColors = prev; }
    }

    // ════════════════════════════════════════════════════════════════
    //  Palette static property
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Palette_Is_Category10_And_Has_10_Colors()
    {
        Assert.Equal(10, D3Dsl.Palette.Count);
    }

    [Fact]
    public void Palette_Colors_Are_Distinct()
    {
        var set = new HashSet<(byte, byte, byte)>();
        foreach (var c in D3Dsl.Palette)
            set.Add((c.R, c.G, c.B));
        Assert.Equal(D3Dsl.Palette.Count, set.Count);
    }
}
