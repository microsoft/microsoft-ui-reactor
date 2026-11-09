using Microsoft.UI.Reactor.Layout;
using Xunit;

// Root test namespace on purpose: a `Microsoft.UI.Reactor.Tests.Layout` namespace would shadow
// `Layout.Flex*` references in other test files.
namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Pure size maths behind <c>Factories.FitParent</c> (issue #174): usable-size and jitter
/// guards, the transient scale (content anchored at (0,0) by the Canvas layer), and the
/// debounce clamp.
/// </summary>
public class FitParentMathTests
{
    private const double Eps = 1e-9;

    // ── IsUsableSize ────────────────────────────────────────────────

    [Theory]
    [InlineData(100, 50)]
    [InlineData(0.01, 0.01)]
    [InlineData(1e6, 1e6)]
    public void IsUsableSize_FinitePositive_True(double w, double h) =>
        Assert.True(FitParentMath.IsUsableSize(w, h));

    [Theory]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.NaN)]
    [InlineData(double.PositiveInfinity, 100)]
    [InlineData(100, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 100)]
    [InlineData(100, double.NegativeInfinity)]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    [InlineData(100, -1)]
    [InlineData(0, 0)]
    public void IsUsableSize_NaNInfinityOrNonPositive_False(double w, double h) =>
        Assert.False(FitParentMath.IsUsableSize(w, h));

    // ── IsSignificantChange ─────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    [InlineData(double.NaN, 100)]
    public void IsSignificantChange_NothingCommitted_True(double cw, double ch) =>
        Assert.True(FitParentMath.IsSignificantChange(cw, ch, 100, 100));

    [Fact]
    public void JitterTolerance_IsHalfDip() => Assert.Equal(0.5, FitParentMath.JitterToleranceDip);

    [Theory]
    [InlineData(100.49, 100)]
    [InlineData(99.51, 100)]
    [InlineData(100, 100.49)]
    [InlineData(100, 99.51)]
    [InlineData(100.49, 99.51)]
    [InlineData(100, 100)]
    [InlineData(100.5, 100)] // exactly the tolerance is still jitter
    public void IsSignificantChange_WithinTolerance_False(double nw, double nh) =>
        Assert.False(FitParentMath.IsSignificantChange(100, 100, nw, nh));

    [Theory]
    [InlineData(100.51, 100)]
    [InlineData(99.49, 100)]
    [InlineData(100, 100.51)]
    [InlineData(100, 99.49)]
    [InlineData(200, 100)]
    [InlineData(50, 50)]
    public void IsSignificantChange_BeyondTolerance_True(double nw, double nh) =>
        Assert.True(FitParentMath.IsSignificantChange(100, 100, nw, nh));

    // ── ComputeTransient ────────────────────────────────────────────

    [Theory]
    [InlineData(0, 100, 200, 200)]
    [InlineData(100, 0, 200, 200)]
    [InlineData(double.NaN, 100, 200, 200)]
    [InlineData(100, 100, 0, 200)]
    [InlineData(100, 100, 200, -1)]
    [InlineData(100, 100, double.PositiveInfinity, 200)]
    [InlineData(100, 100, 200, double.NaN)]
    public void ComputeTransient_UnusableSize_Null(double cw, double ch, double nw, double nh) =>
        Assert.Null(FitParentMath.ComputeTransient(cw, ch, nw, nh));

    [Fact]
    public void ComputeTransient_Identity()
    {
        var t = Get(400, 300, 400, 300);
        AssertTransient(t, cx: 200, cy: 150, s: 1, tx: 0, ty: 0);
    }

    [Fact]
    public void ComputeTransient_UniformGrow()
    {
        var t = Get(400, 300, 800, 600);
        AssertTransient(t, cx: 200, cy: 150, s: 2, tx: 200, ty: 150);
    }

    [Fact]
    public void ComputeTransient_UniformShrink()
    {
        var t = Get(400, 300, 200, 150);
        AssertTransient(t, cx: 200, cy: 150, s: 0.5, tx: -100, ty: -75);
    }

    [Fact]
    public void ComputeTransient_Asymmetric_UsesMinScale_AndCentres()
    {
        // Wider (×2) but only slightly taller (×1.2): s = min → 1.2, centred on the new host.
        var t = Get(400, 300, 800, 360);
        AssertTransient(t, cx: 200, cy: 150, s: 1.2, tx: 200, ty: 30);
    }

    [Fact]
    public void ComputeTransient_ShrinkOneAxis_OverflowOnThatAxis()
    {
        // Host narrower than the committed content (content overflows X), taller on Y.
        var t = Get(400, 300, 200, 600);
        AssertTransient(t, cx: 200, cy: 150, s: 0.5, tx: -100, ty: 150);
    }

    [Fact]
    public void ComputeTransient_ShrinkBothAxes_OverflowOnBoth()
    {
        var t = Get(400, 300, 100, 240);
        AssertTransient(t, cx: 200, cy: 150, s: 0.25, tx: -150, ty: -30);
    }

    [Fact]
    public void ComputeTransient_ScaledCentreLandsOnHostCentre()
    {
        // The content centre (cx, cy) is the pivot, so after the transform it sits at
        // (cx + tx, cy + ty), which must be the new host centre.
        var t = Get(321, 123, 777, 99);
        Assert.Equal(777 / 2.0, t.CenterX + t.TranslateX, 9);
        Assert.Equal(99 / 2.0, t.CenterY + t.TranslateY, 9);
        Assert.Equal(global::System.Math.Min(777 / 321.0, 99 / 123.0), t.Scale, 9);
    }

    // ── EffectiveDebounce ───────────────────────────────────────────

    [Fact]
    public void MaxDebounce_IsTenSeconds() =>
        Assert.Equal(global::System.TimeSpan.FromSeconds(10), FitParentMath.MaxDebounce);

    [Fact]
    public void EffectiveDebounce_Negative_IsZero() =>
        Assert.Equal(global::System.TimeSpan.Zero, FitParentMath.EffectiveDebounce(global::System.TimeSpan.FromMilliseconds(-5)));

    [Fact]
    public void EffectiveDebounce_MinValue_IsZero() =>
        Assert.Equal(global::System.TimeSpan.Zero, FitParentMath.EffectiveDebounce(global::System.TimeSpan.MinValue));

    [Fact]
    public void EffectiveDebounce_Zero_IsZero() =>
        Assert.Equal(global::System.TimeSpan.Zero, FitParentMath.EffectiveDebounce(global::System.TimeSpan.Zero));

    [Fact]
    public void EffectiveDebounce_Default150ms_Unchanged() =>
        Assert.Equal(global::System.TimeSpan.FromMilliseconds(150), FitParentMath.EffectiveDebounce(global::System.TimeSpan.FromMilliseconds(150)));

    [Fact]
    public void EffectiveDebounce_ExactlyMax_Unchanged() =>
        Assert.Equal(global::System.TimeSpan.FromSeconds(10), FitParentMath.EffectiveDebounce(global::System.TimeSpan.FromSeconds(10)));

    [Fact]
    public void EffectiveDebounce_MaxValue_ClampedToTenSeconds() =>
        Assert.Equal(global::System.TimeSpan.FromSeconds(10), FitParentMath.EffectiveDebounce(global::System.TimeSpan.MaxValue));

    [Fact]
    public void DefaultOptions_Debounce150ms_ScaleOn()
    {
        Assert.Equal(global::System.TimeSpan.FromMilliseconds(150), FitParentOptions.Default.Debounce);
        Assert.True(FitParentOptions.Default.ScaleDuringResize);
    }

    private static FitParentTransient Get(double cw, double ch, double nw, double nh)
    {
        var t = FitParentMath.ComputeTransient(cw, ch, nw, nh);
        Assert.True(t.HasValue);
        return t!.Value;
    }

    private static void AssertTransient(FitParentTransient t, double cx, double cy, double s, double tx, double ty)
    {
        Assert.Equal(cx, t.CenterX, Eps);
        Assert.Equal(cy, t.CenterY, Eps);
        Assert.Equal(s, t.Scale, Eps);
        Assert.Equal(tx, t.TranslateX, Eps);
        Assert.Equal(ty, t.TranslateY, Eps);
    }
}
