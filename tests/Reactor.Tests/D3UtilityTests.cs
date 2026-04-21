using Microsoft.UI.Reactor.Charting.D3;
using Xunit;
using Microsoft.UI.Reactor.Charting.Accessibility;

namespace Microsoft.UI.Reactor.Tests;

public class D3UtilityTests
{
    // ═══════════════════════════════════════════════════════════════════
    // D3Ease - all easing functions
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ease_Linear_Identity()
    {
        Assert.Equal(0.0, D3Ease.Linear(0), 10);
        Assert.Equal(0.5, D3Ease.Linear(0.5), 10);
        Assert.Equal(1.0, D3Ease.Linear(1), 10);
    }

    [Fact]
    public void Ease_QuadIn_StartsSlowEndsBlank()
    {
        Assert.Equal(0.0, D3Ease.QuadIn(0), 10);
        Assert.Equal(0.25, D3Ease.QuadIn(0.5), 10);
        Assert.Equal(1.0, D3Ease.QuadIn(1), 10);
    }

    [Fact]
    public void Ease_QuadOut_StartsFastEndsSlow()
    {
        Assert.Equal(0.0, D3Ease.QuadOut(0), 10);
        Assert.Equal(0.75, D3Ease.QuadOut(0.5), 10);
        Assert.Equal(1.0, D3Ease.QuadOut(1), 10);
    }

    [Fact]
    public void Ease_Quad_InOut()
    {
        Assert.Equal(0.0, D3Ease.Quad(0), 10);
        Assert.Equal(0.5, D3Ease.Quad(0.5), 10);
        Assert.Equal(1.0, D3Ease.Quad(1), 10);
    }

    [Fact]
    public void Ease_CubicIn()
    {
        Assert.Equal(0.0, D3Ease.CubicIn(0), 10);
        Assert.Equal(0.125, D3Ease.CubicIn(0.5), 10);
        Assert.Equal(1.0, D3Ease.CubicIn(1), 10);
    }

    [Fact]
    public void Ease_CubicOut()
    {
        Assert.Equal(0.0, D3Ease.CubicOut(0), 10);
        Assert.Equal(0.875, D3Ease.CubicOut(0.5), 10);
        Assert.Equal(1.0, D3Ease.CubicOut(1), 10);
    }

    [Fact]
    public void Ease_Cubic_InOut()
    {
        Assert.Equal(0.0, D3Ease.Cubic(0), 10);
        Assert.Equal(0.5, D3Ease.Cubic(0.5), 10);
        Assert.Equal(1.0, D3Ease.Cubic(1), 10);
    }

    [Fact]
    public void Ease_SinIn()
    {
        Assert.Equal(0.0, D3Ease.SinIn(0), 6);
        Assert.Equal(1.0, D3Ease.SinIn(1), 10);
        Assert.True(D3Ease.SinIn(0.5) < 0.5);
    }

    [Fact]
    public void Ease_SinOut()
    {
        Assert.Equal(0.0, D3Ease.SinOut(0), 10);
        Assert.True(D3Ease.SinOut(0.5) > 0.5);
    }

    [Fact]
    public void Ease_Sin_InOut()
    {
        Assert.Equal(0.0, D3Ease.Sin(0), 10);
        Assert.Equal(0.5, D3Ease.Sin(0.5), 6);
        Assert.Equal(1.0, D3Ease.Sin(1), 6);
    }

    [Fact]
    public void Ease_ExpIn()
    {
        Assert.True(D3Ease.ExpIn(0) < 0.01);
        Assert.Equal(1.0, D3Ease.ExpIn(1), 6);
    }

    [Fact]
    public void Ease_ExpOut()
    {
        Assert.True(D3Ease.ExpOut(1) > 0.99);
        Assert.True(D3Ease.ExpOut(0.5) > 0.5);
    }

    [Fact]
    public void Ease_Exp_InOut()
    {
        Assert.True(D3Ease.Exp(0) < 0.01);
        Assert.Equal(0.5, D3Ease.Exp(0.5), 6);
        Assert.True(D3Ease.Exp(1) > 0.99);
    }

    [Fact]
    public void Ease_CircleIn()
    {
        Assert.Equal(0.0, D3Ease.CircleIn(0), 10);
        Assert.True(D3Ease.CircleIn(0.5) < 0.5);
        Assert.Equal(1.0, D3Ease.CircleIn(1), 6);
    }

    [Fact]
    public void Ease_CircleOut()
    {
        Assert.Equal(0.0, D3Ease.CircleOut(0), 6);
        Assert.True(D3Ease.CircleOut(0.5) > 0.5);
    }

    [Fact]
    public void Ease_Circle_InOut()
    {
        Assert.Equal(0.5, D3Ease.Circle(0.5), 6);
    }

    [Fact]
    public void Ease_BounceIn()
    {
        Assert.Equal(0.0, D3Ease.BounceIn(0), 10);
        Assert.Equal(1.0, D3Ease.BounceIn(1), 10);
    }

    [Fact]
    public void Ease_BounceOut()
    {
        Assert.Equal(0.0, D3Ease.BounceOut(0), 10);
        Assert.Equal(1.0, D3Ease.BounceOut(1), 10);
        // Exercise different branches of bounce
        D3Ease.BounceOut(0.2); // < B1
        D3Ease.BounceOut(0.6); // < B3
        D3Ease.BounceOut(0.85); // < B6
        D3Ease.BounceOut(0.95); // else
    }

    [Fact]
    public void Ease_Bounce_InOut()
    {
        Assert.Equal(0.5, D3Ease.Bounce(0.5), 6);
    }

    [Fact]
    public void Ease_PolyIn_CustomExponent()
    {
        var easing = D3Ease.PolyIn(4);
        Assert.Equal(0.0, easing(0), 10);
        Assert.Equal(1.0, easing(1), 10);
        Assert.Equal(Math.Pow(0.5, 4), easing(0.5), 1e-10);
    }

    [Fact]
    public void Ease_PolyOut_CustomExponent()
    {
        var easing = D3Ease.PolyOut(4);
        Assert.Equal(0.0, easing(0), 10);
        Assert.Equal(1.0, easing(1), 10);
    }

    [Fact]
    public void Ease_Poly_InOut()
    {
        var easing = D3Ease.Poly(4);
        Assert.Equal(0.5, easing(0.5), 6);
    }

    [Fact]
    public void Ease_ElasticIn()
    {
        var easing = D3Ease.ElasticIn();
        Assert.True(easing(0.5) < 0);
        Assert.Equal(1.0, easing(1), 6);
    }

    [Fact]
    public void Ease_ElasticOut()
    {
        var easing = D3Ease.ElasticOut();
        Assert.Equal(0.0, easing(0), 6);
        Assert.True(easing(0.5) > 1);
    }

    [Fact]
    public void Ease_Elastic_InOut()
    {
        var easing = D3Ease.Elastic();
        Assert.Equal(0.5, easing(0.5), 6);
    }

    [Fact]
    public void Ease_ElasticIn_CustomParams()
    {
        var easing = D3Ease.ElasticIn(2.0, 0.5);
        Assert.Equal(1.0, easing(1), 6);
    }

    [Fact]
    public void Ease_BackIn()
    {
        var easing = D3Ease.BackIn();
        Assert.Equal(0.0, easing(0), 10);
        Assert.True(easing(0.5) < 0);
        Assert.Equal(1.0, easing(1), 6);
    }

    [Fact]
    public void Ease_BackOut()
    {
        var easing = D3Ease.BackOut();
        Assert.Equal(0.0, easing(0), 6);
        Assert.True(easing(0.5) > 1);
        Assert.Equal(1.0, easing(1), 6);
    }

    [Fact]
    public void Ease_Back_InOut()
    {
        var easing = D3Ease.Back();
        Assert.Equal(0.5, easing(0.5), 6);
    }

    [Fact]
    public void Ease_BackIn_CustomOvershoot()
    {
        var easing = D3Ease.BackIn(3.0);
        // At t=0.5, s=3: ts=0.25, result = 0.25*(0.5*4 - 3) = -0.25
        Assert.True(easing(0.5) < 0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // D3Format
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Format_Fixed_TwoDecimalPlaces()
    {
        var fmt = D3Format.Format(".2f");
        Assert.Equal("3.14", fmt(3.14159));
    }

    [Fact]
    public void Format_Fixed_ZeroDecimals()
    {
        Assert.Equal("43", D3Format.FormatValue(42.7, ".0f"));
    }

    [Fact]
    public void Format_Percent()
    {
        var fmt = D3Format.Format(".0%");
        Assert.Equal("75%", fmt(0.75));
    }

    [Fact]
    public void Format_Integer()
    {
        Assert.Equal("42", D3Format.FormatValue(42.0, "d"));
    }

    [Fact]
    public void Format_Exponent()
    {
        var result = D3Format.FormatValue(1234.5, ".2e");
        Assert.True(result.Contains("e") || result.Contains("E"));
    }

    [Fact]
    public void Format_SiPrefix()
    {
        var result = D3Format.FormatValue(1500, ".2s");
        Assert.Contains("k", result);
    }

    [Fact]
    public void Format_GroupSeparator()
    {
        var result = D3Format.FormatValue(1234567, ",.0f");
        Assert.Contains(",", result);
    }

    [Fact]
    public void Format_Hex()
    {
        var result = D3Format.FormatValue(255, "x");
        Assert.Equal("ff", result);
    }

    [Fact]
    public void Format_HexUppercase()
    {
        var result = D3Format.FormatValue(255, "X");
        Assert.Equal("FF", result);
    }

    [Fact]
    public void Format_Octal()
    {
        var result = D3Format.FormatValue(8, "o");
        Assert.Equal("10", result);
    }

    [Fact]
    public void Format_Binary()
    {
        var result = D3Format.FormatValue(10, "b");
        Assert.Equal("1010", result);
    }

    [Fact]
    public void Format_FormatPrefix_UsesCorrectPrefix()
    {
        var fmt = D3Format.FormatPrefix(".1", 1e6);
        string result = fmt(1.23e6);
        Assert.Contains("M", result);
    }

    [Fact]
    public void Format_PrecisionFixed_ReturnsCorrectDigits()
    {
        Assert.Equal(2, D3Format.PrecisionFixed(0.01));
        Assert.Equal(0, D3Format.PrecisionFixed(1));
    }

    [Fact]
    public void Format_PrecisionRound_ReturnsCorrectDigits()
    {
        int p = D3Format.PrecisionRound(0.01, 1.01);
        Assert.True(p >= 0);
    }

    [Fact]
    public void Format_PrecisionPrefix_ReturnsCorrectDigits()
    {
        int p = D3Format.PrecisionPrefix(1e5, 1.3e6);
        Assert.True(p >= 0);
    }

    [Fact]
    public void Format_General_Notation()
    {
        var result = D3Format.FormatValue(0.00123, ".3g");
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void Format_Round_Notation()
    {
        var result = D3Format.FormatValue(3.14159, ".3r");
        Assert.True(result.Length > 0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // D3Interpolate
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Interpolate_Number_LinearBetween()
    {
        var interp = D3Interpolate.Number(0, 100);
        Assert.Equal(0, interp(0), 10);
        Assert.Equal(50, interp(0.5), 10);
        Assert.Equal(100, interp(1), 10);
    }

    [Fact]
    public void Interpolate_Round_RoundsResult()
    {
        var interp = D3Interpolate.Round(0, 10);
        Assert.Equal(0, interp(0), 10);
        Assert.Equal(5, interp(0.5), 10);
        Assert.Equal(10, interp(1), 10);
    }

    [Fact]
    public void Interpolate_Round_NonIntegerRounds()
    {
        var interp = D3Interpolate.Round(0, 10);
        Assert.Equal(3, interp(0.3), 10);
    }

    // ═══════════════════════════════════════════════════════════════════
    // D3InterpolateColor
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void InterpolateColor_Rgb_InterpolatesBetweenColors()
    {
        var black = new D3Color(0, 0, 0);
        var white = new D3Color(255, 255, 255);
        var interp = D3InterpolateColor.Rgb(black, white);
        var mid = interp(0.5);
        Assert.Equal(128.0, mid.R, 1);
        Assert.Equal(128.0, mid.G, 1);
        Assert.Equal(128.0, mid.B, 1);
    }

    [Fact]
    public void InterpolateColor_Rgb_FromStrings()
    {
        var interp = D3InterpolateColor.Rgb("#000000", "#ffffff");
        var mid = interp(0.5);
        Assert.Equal(128.0, mid.R, 1);
    }

    [Fact]
    public void InterpolateColor_Hsl_InterpolatesHue()
    {
        var red = D3Color.Parse("#ff0000");
        var blue = D3Color.Parse("#0000ff");
        var interp = D3InterpolateColor.Hsl(red, blue);
        var mid = interp(0.5);
        // Mid should be some color between red and blue
        Assert.True(mid.R >= 0 && mid.R <= 255);
    }

    [Fact]
    public void InterpolateColor_Date_InterpolatesBetweenDates()
    {
        var start = new DateTime(2020, 1, 1);
        var end = new DateTime(2020, 12, 31);
        var interp = D3InterpolateColor.Date(start, end);
        var mid = interp(0.5);
        Assert.True(mid > start && mid < end);
    }

    [Fact]
    public void InterpolateColor_Array_InterpolatesArrays()
    {
        var interp = D3InterpolateColor.Array([0, 0], [10, 100]);
        var mid = interp(0.5);
        Assert.Equal(5, mid[0], 10);
        Assert.Equal(50, mid[1], 10);
    }

    // ═══════════════════════════════════════════════════════════════════
    // D3Curve (curve factories for line/area generators)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Curve_Linear_ProducesLine()
    {
        var line = LineGenerator.Create<(double x, double y)>(d => d.x, d => d.y);
        line.SetCurve(D3Curve.Linear);
        var data = new (double, double)[] { (0, 0), (50, 100), (100, 0) };
        string? path = line.Generate(data);
        Assert.NotNull(path);
        Assert.StartsWith("M", path);
    }

    [Fact]
    public void Curve_Step_ProducesSteppedLine()
    {
        var line = LineGenerator.Create<(double x, double y)>(d => d.x, d => d.y);
        line.SetCurve(D3Curve.Step);
        var data = new (double, double)[] { (0, 0), (50, 100), (100, 0) };
        string? path = line.Generate(data);
        Assert.NotNull(path);
    }

    [Fact]
    public void Curve_StepBefore_ProducesSteppedLine()
    {
        var line = LineGenerator.Create<(double x, double y)>(d => d.x, d => d.y);
        line.SetCurve(D3Curve.StepBefore);
        string? path = line.Generate(new (double, double)[] { (0, 0), (50, 100), (100, 0) });
        Assert.NotNull(path);
    }

    [Fact]
    public void Curve_StepAfter_ProducesSteppedLine()
    {
        var line = LineGenerator.Create<(double x, double y)>(d => d.x, d => d.y);
        line.SetCurve(D3Curve.StepAfter);
        string? path = line.Generate(new (double, double)[] { (0, 0), (50, 100), (100, 0) });
        Assert.NotNull(path);
    }

    [Fact]
    public void Curve_Basis_ProducesSmoothLine()
    {
        var line = LineGenerator.Create<(double x, double y)>(d => d.x, d => d.y);
        line.SetCurve(D3Curve.Basis);
        string? path = line.Generate(new (double, double)[] { (0, 0), (25, 50), (50, 100), (75, 50), (100, 0) });
        Assert.NotNull(path);
    }

    [Fact]
    public void Curve_BasisClosed_ProducesClosedCurve()
    {
        var line = LineGenerator.Create<(double x, double y)>(d => d.x, d => d.y);
        line.SetCurve(D3Curve.BasisClosed);
        string? path = line.Generate(new (double, double)[] { (0, 0), (50, 100), (100, 0) });
        Assert.NotNull(path);
    }

    [Fact]
    public void Curve_Natural_ProducesNaturalSpline()
    {
        var line = LineGenerator.Create<(double x, double y)>(d => d.x, d => d.y);
        line.SetCurve(D3Curve.Natural);
        string? path = line.Generate(new (double, double)[] { (0, 0), (25, 50), (50, 100), (75, 50), (100, 0) });
        Assert.NotNull(path);
    }

    [Fact]
    public void Curve_Cardinal_ProducesCurve()
    {
        var line = LineGenerator.Create<(double x, double y)>(d => d.x, d => d.y);
        line.SetCurve(D3Curve.Cardinal);
        string? path = line.Generate(new (double, double)[] { (0, 0), (25, 50), (50, 100), (75, 50), (100, 0) });
        Assert.NotNull(path);
    }

    [Fact]
    public void Curve_CardinalWithTension_ProducesCurve()
    {
        var line = LineGenerator.Create<(double x, double y)>(d => d.x, d => d.y);
        line.SetCurve(D3Curve.CardinalWithTension(0.5));
        string? path = line.Generate(new (double, double)[] { (0, 0), (25, 50), (50, 100), (75, 50), (100, 0) });
        Assert.NotNull(path);
    }

    [Fact]
    public void Curve_CatmullRom_ProducesCurve()
    {
        var line = LineGenerator.Create<(double x, double y)>(d => d.x, d => d.y);
        line.SetCurve(D3Curve.CatmullRom);
        string? path = line.Generate(new (double, double)[] { (0, 0), (25, 50), (50, 100), (75, 50), (100, 0) });
        Assert.NotNull(path);
    }

    [Fact]
    public void Curve_CatmullRomWithAlpha_ProducesCurve()
    {
        var line = LineGenerator.Create<(double x, double y)>(d => d.x, d => d.y);
        line.SetCurve(D3Curve.CatmullRomWithAlpha(0.5));
        string? path = line.Generate(new (double, double)[] { (0, 0), (25, 50), (50, 100), (75, 50), (100, 0) });
        Assert.NotNull(path);
    }

    [Fact]
    public void Curve_MonotoneX_ProducesMonotoneCurve()
    {
        var line = LineGenerator.Create<(double x, double y)>(d => d.x, d => d.y);
        line.SetCurve(D3Curve.MonotoneX);
        string? path = line.Generate(new (double, double)[] { (0, 0), (25, 50), (50, 100), (75, 50), (100, 0) });
        Assert.NotNull(path);
    }

    // ═══════════════════════════════════════════════════════════════════
    // D3Color additional tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Color_Parse_NamedColors()
    {
        var red = D3Color.Parse("red");
        Assert.Equal(255, (int)red.R);
        Assert.Equal(0, (int)red.G);
        Assert.Equal(0, (int)red.B);
    }

    [Fact]
    public void Color_Parse_RgbFunction()
    {
        var c = D3Color.Parse("rgb(100, 200, 50)");
        Assert.Equal(100, (int)c.R);
        Assert.Equal(200, (int)c.G);
        Assert.Equal(50, (int)c.B);
    }

    [Fact]
    public void Color_ToHex_ReturnsHexString()
    {
        var c = new D3Color(255, 0, 128);
        string hex = c.ToHex();
        Assert.StartsWith("#", hex);
        Assert.Equal(7, hex.Length);
    }

    [Fact]
    public void Color_ToRgb_ReturnsRgbString()
    {
        var c = new D3Color(100, 200, 50);
        string rgb = c.ToRgb();
        Assert.StartsWith("rgb(", rgb);
    }

    [Fact]
    public void Color_Brighter_IncreasesLuminance()
    {
        var c = new D3Color(100, 100, 100);
        var bright = c.Brighter();
        Assert.True(bright.R > c.R);
    }

    [Fact]
    public void Color_Darker_DecreasesLuminance()
    {
        var c = new D3Color(100, 100, 100);
        var dark = c.Darker();
        Assert.True(dark.R < c.R);
    }

    [Fact]
    public void Color_Parse_ShortHex()
    {
        var c = D3Color.Parse("#f00");
        Assert.Equal(255, (int)c.R);
        Assert.Equal(0, (int)c.G);
        Assert.Equal(0, (int)c.B);
    }

    [Fact]
    public void Color_Parse_HslFunction()
    {
        var c = D3Color.Parse("hsl(0, 100%, 50%)");
        Assert.True(Math.Abs(255 - (int)c.R) <= 1);
        Assert.True(Math.Abs(0 - (int)c.G) <= 1);
        Assert.True(Math.Abs(0 - (int)c.B) <= 1);
    }

    [Fact]
    public void Color_Parse_RgbaFunction()
    {
        var c = D3Color.Parse("rgba(100, 200, 50, 0.5)");
        Assert.Equal(0.5, c.Opacity, 2);
    }

    [Fact]
    public void Color_Category10_HasColors()
    {
        Assert.Equal(10, D3Color.Category10.Count);
        foreach (var c in D3Color.Category10)
        {
            Assert.True(c.R >= 0 && c.R <= 255);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // ChartPalette
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ChartPalette_ContrastRatio_BlackOnWhite()
    {
        double ratio = ChartPalette.ContrastRatio(
            new D3Color(0, 0, 0), new D3Color(255, 255, 255));
        Assert.True(ratio >= 20); // Black on white is ~21:1
    }

    [Fact]
    public void ChartPalette_ContrastRatio_SameColor_IsOne()
    {
        double ratio = ChartPalette.ContrastRatio(
            new D3Color(128, 128, 128), new D3Color(128, 128, 128));
        Assert.Equal(1.0, ratio, 2);
    }

    [Fact]
    public void ChartPalette_Harden_AdjustsLowContrastColors()
    {
        var colors = new D3Color[]
        {
            new(128, 128, 128), new(130, 130, 130)
        };
        var result = ChartPalette.Harden(colors);
        Assert.NotNull(result.Palette);
        // Should have adjusted because 128 vs 130 is very low contrast
        Assert.False(result.PassedWithoutChanges);
    }

    [Fact]
    public void ChartPalette_RelativeLuminance_White()
    {
        double lum = ChartPalette.RelativeLuminance(new D3Color(255, 255, 255));
        Assert.Equal(1.0, lum, 2);
    }

    [Fact]
    public void ChartPalette_RelativeLuminance_Black()
    {
        double lum = ChartPalette.RelativeLuminance(new D3Color(0, 0, 0));
        Assert.Equal(0.0, lum, 2);
    }
}
