using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for D3Statistics — pure mathematical statistics functions
/// ported from d3-array.
/// </summary>
public class D3StatisticsTests
{
    // ═══ Min ═══

    [Fact]
    public void Min_Simple() => Assert.Equal(1, D3Statistics.Min(new[] { 3.0, 1, 4, 1, 5 }));

    [Fact]
    public void Min_WithNaN() => Assert.Equal(2, D3Statistics.Min(new[] { double.NaN, 5, 2, double.NaN }));

    [Fact]
    public void Min_AllNaN() => Assert.True(double.IsNaN(D3Statistics.Min(new[] { double.NaN, double.NaN })));

    [Fact]
    public void Min_Empty() => Assert.True(double.IsNaN(D3Statistics.Min(Array.Empty<double>())));

    [Fact]
    public void Min_Single() => Assert.Equal(42, D3Statistics.Min(new[] { 42.0 }));

    [Fact]
    public void Min_Accessor() => Assert.Equal(1, D3Statistics.Min(new[] { "abc", "a", "ab" }, s => s.Length));

    [Fact]
    public void Min_Accessor_Empty() => Assert.True(double.IsNaN(D3Statistics.Min(Array.Empty<string>(), s => s.Length)));

    [Fact]
    public void Min_Negative() => Assert.Equal(-10, D3Statistics.Min(new[] { -1.0, -10, -5 }));

    // ═══ Max ═══

    [Fact]
    public void Max_Simple() => Assert.Equal(5, D3Statistics.Max(new[] { 3.0, 1, 4, 1, 5 }));

    [Fact]
    public void Max_WithNaN() => Assert.Equal(5, D3Statistics.Max(new[] { double.NaN, 5, 2, double.NaN }));

    [Fact]
    public void Max_AllNaN() => Assert.True(double.IsNaN(D3Statistics.Max(new[] { double.NaN, double.NaN })));

    [Fact]
    public void Max_Empty() => Assert.True(double.IsNaN(D3Statistics.Max(Array.Empty<double>())));

    [Fact]
    public void Max_Accessor() => Assert.Equal(3, D3Statistics.Max(new[] { "abc", "a", "ab" }, s => s.Length));

    // ═══ Sum ═══

    [Fact]
    public void Sum_Simple() => Assert.Equal(15, D3Statistics.Sum(new[] { 1.0, 2, 3, 4, 5 }));

    [Fact]
    public void Sum_WithNaN() => Assert.Equal(7, D3Statistics.Sum(new[] { double.NaN, 3, 4, double.NaN }));

    [Fact]
    public void Sum_Empty() => Assert.Equal(0, D3Statistics.Sum(Array.Empty<double>()));

    [Fact]
    public void Sum_Accessor() => Assert.Equal(6, D3Statistics.Sum(new[] { "abc", "a", "ab" }, s => (double)s.Length));

    // ═══ Mean ═══

    [Fact]
    public void Mean_Simple() => Assert.Equal(3, D3Statistics.Mean(new[] { 1.0, 2, 3, 4, 5 }));

    [Fact]
    public void Mean_WithNaN() => Assert.Equal(3.5, D3Statistics.Mean(new[] { double.NaN, 3, 4, double.NaN }));

    [Fact]
    public void Mean_Empty() => Assert.True(double.IsNaN(D3Statistics.Mean(Array.Empty<double>())));

    [Fact]
    public void Mean_Accessor() => Assert.Equal(2, D3Statistics.Mean(new[] { "abc", "a", "ab" }, s => (double)s.Length));

    // ═══ Median ═══

    [Fact]
    public void Median_Odd() => Assert.Equal(3, D3Statistics.Median(new[] { 1.0, 3, 5 }));

    [Fact]
    public void Median_Even() => Assert.Equal(2.5, D3Statistics.Median(new[] { 1.0, 2, 3, 4 }));

    [Fact]
    public void Median_Single() => Assert.Equal(7, D3Statistics.Median(new[] { 7.0 }));

    [Fact]
    public void Median_Accessor() => Assert.Equal(2, D3Statistics.Median(new[] { "abc", "a", "ab" }, s => (double)s.Length));

    // ═══ Quantile ═══

    [Fact]
    public void Quantile_Median() => Assert.Equal(3, D3Statistics.Quantile(new[] { 1.0, 3, 5 }, 0.5));

    [Fact]
    public void Quantile_P0() => Assert.Equal(1, D3Statistics.QuantileSorted(new[] { 1.0, 3, 5 }, 0));

    [Fact]
    public void Quantile_P1() => Assert.Equal(5, D3Statistics.QuantileSorted(new[] { 1.0, 3, 5 }, 1));

    [Fact]
    public void Quantile_Q1() => Assert.Equal(2, D3Statistics.QuantileSorted(new[] { 1.0, 2, 3, 4, 5 }, 0.25));

    [Fact]
    public void Quantile_Q3()
    {
        var q3 = D3Statistics.QuantileSorted(new[] { 1.0, 2, 3, 4, 5 }, 0.75);
        Assert.Equal(4, q3);
    }

    [Fact]
    public void Quantile_Empty() => Assert.True(double.IsNaN(D3Statistics.QuantileSorted(Array.Empty<double>(), 0.5)));

    [Fact]
    public void Quantile_NaN_P() => Assert.True(double.IsNaN(D3Statistics.QuantileSorted(new[] { 1.0 }, double.NaN)));

    [Fact]
    public void Quantile_Interpolates()
    {
        var result = D3Statistics.QuantileSorted(new[] { 0.0, 10.0 }, 0.3);
        Assert.Equal(3.0, result, 5);
    }

    // ═══ Variance ═══

    [Fact]
    public void Variance_Simple()
    {
        var v = D3Statistics.Variance(new[] { 2.0, 4, 4, 4, 5, 5, 7, 9 });
        Assert.True(v > 0);
    }

    [Fact]
    public void Variance_Single() => Assert.True(double.IsNaN(D3Statistics.Variance(new[] { 5.0 })));

    [Fact]
    public void Variance_Empty() => Assert.True(double.IsNaN(D3Statistics.Variance(Array.Empty<double>())));

    [Fact]
    public void Variance_AllSame() => Assert.Equal(0, D3Statistics.Variance(new[] { 3.0, 3, 3 }));

    // ═══ Deviation ═══

    [Fact]
    public void Deviation_Simple()
    {
        var d = D3Statistics.Deviation(new[] { 2.0, 4, 4, 4, 5, 5, 7, 9 });
        Assert.True(d > 0);
    }

    [Fact]
    public void Deviation_Single() => Assert.True(double.IsNaN(D3Statistics.Deviation(new[] { 5.0 })));

    [Fact]
    public void Deviation_Empty() => Assert.True(double.IsNaN(D3Statistics.Deviation(Array.Empty<double>())));

    // ═══ CumSum ═══

    [Fact]
    public void CumSum_Simple()
    {
        var result = D3Statistics.CumSum(new[] { 1.0, 2, 3, 4, 5 });
        Assert.Equal(new[] { 1.0, 3, 6, 10, 15 }, result);
    }

    [Fact]
    public void CumSum_WithNaN()
    {
        var result = D3Statistics.CumSum(new[] { 1.0, double.NaN, 3 });
        Assert.Equal(new[] { 1.0, 1, 4 }, result);
    }

    [Fact]
    public void CumSum_Empty() => Assert.Empty(D3Statistics.CumSum(Array.Empty<double>()));
}
