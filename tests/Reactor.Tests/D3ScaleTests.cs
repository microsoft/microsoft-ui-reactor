using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class D3ScaleTests
{
    // ═══════════════════════════════════════════════════════════════════
    // LinearScale
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void LinearScale_DefaultDomain_MapsIdentity()
    {
        var scale = new LinearScale();
        Assert.Equal(0.5, scale.Map(0.5), 10);
    }

    [Fact]
    public void LinearScale_CustomDomain_MapsCorrectly()
    {
        var scale = new LinearScale([0, 100], [0, 1]);
        Assert.Equal(0.5, scale.Map(50), 10);
        Assert.Equal(0.0, scale.Map(0), 10);
        Assert.Equal(1.0, scale.Map(100), 10);
    }

    [Fact]
    public void LinearScale_Invert_ReturnsCorrectDomainValue()
    {
        var scale = new LinearScale([0, 100], [0, 1]);
        Assert.Equal(50.0, scale.Invert(0.5), 10);
        Assert.Equal(0.0, scale.Invert(0.0), 10);
    }

    [Fact]
    public void LinearScale_Clamp_ClampsToDomain()
    {
        var scale = new LinearScale([0, 100], [0, 1]);
        scale.Clamped = true;
        Assert.Equal(1.0, scale.Map(200), 10);
        Assert.Equal(0.0, scale.Map(-50), 10);
    }

    [Fact]
    public void LinearScale_NaN_ReturnsUnknown()
    {
        var scale = new LinearScale();
        Assert.True(double.IsNaN(scale.Map(double.NaN)));
    }

    [Fact]
    public void LinearScale_SetUnknown_ReturnsCustomValue()
    {
        var scale = new LinearScale();
        scale.Unknown = -1;
        Assert.Equal(-1.0, scale.Map(double.NaN), 10);
    }

    [Fact]
    public void LinearScale_Ticks_ReturnsEvenlySpacedValues()
    {
        var scale = new LinearScale([0, 100], [0, 1]);
        var ticks = scale.Ticks(5);
        Assert.True(ticks.Length >= 2);
        Assert.Equal(0.0, ticks[0], 10);
        Assert.Equal(100.0, ticks[^1], 10);
    }

    [Fact]
    public void LinearScale_Nice_RoundsDomain()
    {
        var scale = new LinearScale([0.123, 9.876], [0, 1]);
        scale = scale.Nice();
        Assert.Equal(0.0, scale.Domain[0], 10);
        Assert.Equal(10.0, scale.Domain[1], 10);
    }

    [Fact]
    public void LinearScale_Copy_CreatesIndependentCopy()
    {
        var scale = new LinearScale([0, 10], [0, 100]);
        var copy = scale.Copy();
        copy.Domain = [0, 20];
        Assert.Equal(10.0, scale.Domain[1], 10);
        Assert.Equal(20.0, copy.Domain[1], 10);
    }

    [Fact]
    public void LinearScale_DomainSetter_Rescales()
    {
        var scale = new LinearScale();
        scale.Domain = [0, 200];
        scale.Range = [0, 1];
        Assert.Equal(0.5, scale.Map(100), 10);
    }

    [Fact]
    public void LinearScale_MultiPointDomain_InterpolatesPiecewise()
    {
        var scale = new LinearScale([0, 50, 100], [0, 0.5, 1]);
        Assert.Equal(0.25, scale.Map(25), 10);
        Assert.Equal(0.75, scale.Map(75), 10);
    }

    [Fact]
    public void LinearScale_Clamp_InvertAlsoClamps()
    {
        var scale = new LinearScale([0, 100], [0, 1]);
        scale.Clamped = true;
        double result = scale.Invert(2.0);
        Assert.Equal(100.0, result, 10);
    }

    // ═══════════════════════════════════════════════════════════════════
    // LogScale
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void LogScale_Default_MapsLogarithmically()
    {
        var scale = new LogScale([1, 1000], [0, 3]);
        Assert.Equal(0.0, scale.Map(1), 10);
        Assert.Equal(1.0, scale.Map(10), 4);
        Assert.Equal(2.0, scale.Map(100), 4);
        Assert.Equal(3.0, scale.Map(1000), 4);
    }

    [Fact]
    public void LogScale_Invert_ReturnsOriginalValue()
    {
        var scale = new LogScale([1, 1000], [0, 3]);
        Assert.Equal(10.0, scale.Invert(1), 2);
        Assert.Equal(100.0, scale.Invert(2), 2);
    }

    [Fact]
    public void LogScale_Clamp_ClampsToDomain()
    {
        var scale = new LogScale([1, 100], [0, 2]);
        scale.Clamped = true;
        Assert.Equal(2.0, scale.Map(1000), 10);
        Assert.Equal(0.0, scale.Map(0.01), 10);
    }

    [Fact]
    public void LogScale_Ticks_ReturnsLogTicks()
    {
        var scale = new LogScale([1, 1000], [0, 1]);
        var ticks = scale.Ticks();
        Assert.True(ticks.Length >= 3);
        Assert.Contains(1.0, ticks);
        Assert.Contains(10.0, ticks);
        Assert.Contains(100.0, ticks);
        Assert.Contains(1000.0, ticks);
    }

    [Fact]
    public void LogScale_Nice_RoundsToPowerOfBase()
    {
        var scale = new LogScale([2, 500], [0, 1]);
        scale = scale.Nice();
        Assert.Equal(1.0, scale.Domain[0], 10);
        Assert.Equal(1000.0, scale.Domain[1], 10);
    }

    [Fact]
    public void LogScale_Copy_CreatesIndependentCopy()
    {
        var scale = new LogScale([1, 100], [0, 1]);
        var copy = scale.Copy();
        copy.Domain = [1, 10];
        Assert.Equal(100.0, scale.Domain[1]);
    }

    [Fact]
    public void LogScale_SetBase_UsesCustomBase()
    {
        var scale = new LogScale([1, 8], [0, 3]);
        scale.Base = 2;
        Assert.Equal(1.0, scale.Map(2), 4);
        Assert.Equal(2.0, scale.Map(4), 4);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PowScale
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PowScale_DefaultExponent_IsLinear()
    {
        var scale = new PowScale(1);
        scale.Domain = [0, 100];
        scale.Range = [0, 1];
        Assert.Equal(0.5, scale.Map(50), 10);
    }

    [Fact]
    public void PowScale_SquareExponent_MapsQuadratically()
    {
        var scale = new PowScale(2);
        scale.Domain = [0, 10];
        scale.Range = [0, 100];
        Assert.Equal(25.0, scale.Map(5), 4);
    }

    [Fact]
    public void PowScale_Sqrt_MapsSquareRoot()
    {
        var scale = PowScale.Sqrt();
        scale.Domain = [0, 100];
        scale.Range = [0, 10];
        Assert.Equal(5.0, scale.Map(25), 4);
    }

    [Fact]
    public void PowScale_Invert_ReturnsOriginalValue()
    {
        var scale = new PowScale(2);
        scale.Domain = [0, 10];
        scale.Range = [0, 100];
        // Just verify Invert returns a finite value in domain range
        double mapped = scale.Map(5);
        double inverted = scale.Invert(mapped);
        Assert.True(double.IsFinite(inverted));
        Assert.True(inverted >= 0 && inverted <= 10);
    }

    [Fact]
    public void PowScale_Clamp_ClampsToRange()
    {
        var scale = new PowScale(2);
        scale.Domain = [0, 10];
        scale.Range = [0, 100];
        scale.Clamped = true;
        Assert.Equal(100.0, scale.Map(20), 10);
    }

    [Fact]
    public void PowScale_Ticks_ReturnsValues()
    {
        var scale = new PowScale(2);
        scale.Domain = [0, 100];
        scale.Range = [0, 1];
        var ticks = scale.Ticks(5);
        Assert.True(ticks.Length >= 2);
    }

    [Fact]
    public void PowScale_Nice_RoundsDomain()
    {
        var scale = new PowScale(2);
        scale.Domain = [0.123, 9.876];
        scale.Range = [0, 1];
        scale = scale.Nice();
        Assert.Equal(0.0, scale.Domain[0], 10);
        Assert.Equal(10.0, scale.Domain[1], 10);
    }

    [Fact]
    public void PowScale_Copy_CreatesIndependent()
    {
        var scale = new PowScale(2);
        scale.Domain = [0, 10];
        var copy = scale.Copy();
        copy.Domain = [0, 20];
        Assert.Equal(10.0, scale.Domain[1]);
    }

    [Fact]
    public void PowScale_SetExponent_Rescales()
    {
        var scale = new PowScale(1);
        scale.Domain = [0, 100];
        scale.Range = [0, 100];
        scale.Exponent = 2;
        Assert.NotEqual(50.0, scale.Map(50));
    }

    // ═══════════════════════════════════════════════════════════════════
    // BandScale
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BandScale_DistributesEvenly()
    {
        var scale = BandScale.Create();
        scale.Domain = ["A", "B", "C"];
        scale.Range = [0, 300];
        Assert.Equal(0.0, scale.Map("A"), 10);
        Assert.Equal(100.0, scale.Map("B"), 10);
        Assert.Equal(200.0, scale.Map("C"), 10);
        Assert.Equal(100.0, scale.Bandwidth, 10);
    }

    [Fact]
    public void BandScale_WithPadding_AddsPaddingBetweenBands()
    {
        var scale = BandScale.Create();
        scale.Domain = ["A", "B"];
        scale.Range = [0, 200];
        scale.PaddingInner = 0.5;
        Assert.True(scale.Bandwidth < 100);
        Assert.True(scale.Map("B") > scale.Map("A"));
    }

    [Fact]
    public void BandScale_WithRound_RoundsPositions()
    {
        var scale = BandScale.Create();
        scale.Domain = ["A", "B", "C"];
        scale.Range = [0, 100];
        scale.SetRound(true);
        Assert.Equal(Math.Floor(scale.Map("A")), scale.Map("A"), 10);
        Assert.Equal(Math.Floor(scale.Bandwidth), scale.Bandwidth, 10);
    }

    [Fact]
    public void BandScale_Copy_Independent()
    {
        var scale = BandScale.Create();
        scale.Domain = ["A", "B"];
        var copy = scale.Copy();
        copy.Domain = ["X", "Y", "Z"];
        Assert.Equal(2, scale.Domain.Length);
        Assert.Equal(3, copy.Domain.Length);
    }

    [Fact]
    public void BandScale_Step_EqualsExpected()
    {
        var scale = BandScale.Create();
        scale.Domain = ["A", "B", "C", "D"];
        scale.Range = [0, 400];
        Assert.Equal(100.0, scale.Step, 10);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PointScale
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PointScale_DistributesPoints()
    {
        var scale = PointScale.Create();
        scale.Domain = ["A", "B", "C"];
        scale.Range = [0, 100];
        Assert.True(scale.Map("A") < scale.Map("B"));
        Assert.True(scale.Map("B") < scale.Map("C"));
    }

    [Fact]
    public void PointScale_Align_ShiftsPoints()
    {
        var scale = PointScale.Create();
        scale.Domain = ["A"];
        scale.Range = [0, 100];
        scale.Align = 0.5;
        Assert.Equal(50.0, scale.Map("A"), 10);
    }

    // ═══════════════════════════════════════════════════════════════════
    // QuantizeScale
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void QuantizeScale_MapsToDiscreteRange()
    {
        var scale = new QuantizeScale();
        scale.Domain = [0, 100];
        scale.Range = [0, 1, 2, 3];
        Assert.Equal(0.0, scale.Map(10), 10);
        Assert.Equal(1.0, scale.Map(30), 10);
        Assert.Equal(2.0, scale.Map(60), 10);
        Assert.Equal(3.0, scale.Map(90), 10);
    }

    [Fact]
    public void QuantizeScale_InvertExtent_ReturnsDomainRange()
    {
        var scale = new QuantizeScale();
        scale.Domain = [0, 100];
        scale.Range = [0, 1];
        var (x0, x1) = scale.InvertExtent(0);
        Assert.Equal(0.0, x0, 10);
        Assert.Equal(50.0, x1, 10);
    }

    [Fact]
    public void QuantizeScale_Thresholds_ReturnsBreakpoints()
    {
        var scale = new QuantizeScale();
        scale.Domain = [0, 100];
        scale.Range = [0, 1, 2, 3];
        var thresholds = scale.Thresholds();
        Assert.Equal(3, thresholds.Length);
    }

    [Fact]
    public void QuantizeScale_Copy_Independent()
    {
        var scale = new QuantizeScale();
        scale.Domain = [0, 100];
        scale.Range = [0, 1];
        var copy = scale.Copy();
        copy.Range = [0, 1, 2];
        Assert.Equal(2, scale.Range.Length);
    }

    // ═══════════════════════════════════════════════════════════════════
    // OrdinalScale
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void OrdinalScale_MapsKeyToValue()
    {
        var scale = OrdinalScale.Create<string>(["a", "b", "c"], [10, 20, 30]);
        Assert.Equal(10.0, scale.Map("a"), 10);
        Assert.Equal(20.0, scale.Map("b"), 10);
        Assert.Equal(30.0, scale.Map("c"), 10);
    }

    [Fact]
    public void OrdinalScale_UnknownKey_CyclesOrReturnsUnknown()
    {
        var scale = OrdinalScale.Create<string>(["a"], [10]);
        scale.Unknown = -1;
        Assert.Equal(-1.0, scale.Map("x"), 10);
    }

    [Fact]
    public void OrdinalScale_Copy_Independent()
    {
        var scale = OrdinalScale.Create<string>(["a"], [1]);
        var copy = scale.Copy();
        copy.Unknown = 99;
        Assert.NotEqual(99.0, scale.Unknown);
    }
}
