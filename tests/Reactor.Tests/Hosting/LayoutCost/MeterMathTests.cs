using Microsoft.UI.Reactor.Hosting.LayoutCost;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting.LayoutCost;

public class MeterMathTests
{
    private static ComponentSnapshot Snap(double ms, int authored, int rendered)
        => new(
            new ComponentIdentity(1), "test",
            authored, rendered, Depth: 0,
            EmaLayoutMs: ms,
            LastFrameMs: ms,
            FrameMeasureTicks: 0, FrameArrangeTicks: 0,
            SubtreeX: 0, SubtreeY: 0, SubtreeW: 100, SubtreeH: 100);

    [Fact]
    public void AuthoredEqualsRendered_TailIsZero()
    {
        var s = Snap(ms: 0, authored: 50, rendered: 50);
        var box = new MeterBox(InnerWidth: 30, BarHeight: 5);

        var m = MeterMath.ComputeLayout(s, box);
        Assert.Equal(0f, m.TailBarWidth);
        Assert.True(m.AuthoredBarWidth > 0);
    }

    [Fact]
    public void MsCeiling_ClampsAtOne()
    {
        var s = Snap(ms: MeterMath.MsBarCeilingMs, authored: 1, rendered: 1);
        var box = new MeterBox(InnerWidth: 30, BarHeight: 5);
        var m = MeterMath.ComputeLayout(s, box);
        Assert.Equal(30f, m.MsBarWidth, 3);

        var s2 = Snap(ms: MeterMath.MsBarCeilingMs * 5, authored: 1, rendered: 1);
        var m2 = MeterMath.ComputeLayout(s2, box);
        Assert.Equal(30f, m2.MsBarWidth, 3);
    }

    [Fact]
    public void Rendered10000_FillsNearlyAll()
    {
        var s = Snap(ms: 0, authored: 1, rendered: 10_000);
        var box = new MeterBox(InnerWidth: 30, BarHeight: 5);
        var m = MeterMath.ComputeLayout(s, box);
        Assert.True(m.AuthoredBarWidth + m.TailBarWidth > 29.9f,
            $"expected near-full width, got {m.AuthoredBarWidth + m.TailBarWidth}");
    }

    [Fact]
    public void BarsSum_NeverExceedsInnerWidth()
    {
        var box = new MeterBox(InnerWidth: 30, BarHeight: 5);
        for (int rendered = 0; rendered < 20_000; rendered += 137)
        {
            var s = Snap(ms: 0, authored: rendered, rendered: rendered * 2);
            var m = MeterMath.ComputeLayout(s, box);
            Assert.True(m.AuthoredBarWidth + m.TailBarWidth <= box.InnerWidth + 0.01f,
                $"overflow at rendered={rendered}");
        }
    }

    [Fact]
    public void NegativeOrNaNMs_ClampSafely()
    {
        var box = new MeterBox(InnerWidth: 30, BarHeight: 5);
        var s = Snap(ms: -5, authored: 1, rendered: 1);
        var m = MeterMath.ComputeLayout(s, box);
        Assert.Equal(0f, m.MsBarWidth);

        var s2 = Snap(ms: double.NaN, authored: 1, rendered: 1);
        var m2 = MeterMath.ComputeLayout(s2, box);
        Assert.Equal(0f, m2.MsBarWidth);
    }

    [Fact]
    public void NegativeCounts_ClampToZero()
    {
        var box = new MeterBox(InnerWidth: 30, BarHeight: 5);
        var s = Snap(ms: 0, authored: -5, rendered: -5);
        var m = MeterMath.ComputeLayout(s, box);
        Assert.Equal(0f, m.AuthoredBarWidth);
        Assert.Equal(0f, m.TailBarWidth);
    }
}
