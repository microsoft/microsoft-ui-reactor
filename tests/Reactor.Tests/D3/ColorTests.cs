// Port of d3-color behaviors — parsing, brighter/darker, hex/rgb output.
// reactor1's D3Color is a simplified subset of d3-color (no HCL / Lab / Cubehelix).

using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.D3;

public class ColorTests
{
    // ─── Parse hex ───────────────────────────────────────────────────

    [Fact]
    public void Parse_Hex6()
    {
        var c = D3Color.Parse("#ff0000");
        Assert.Equal(255, c.R);
        Assert.Equal(0, c.G);
        Assert.Equal(0, c.B);
        Assert.Equal(1.0, c.Opacity);
    }

    [Fact]
    public void Parse_Hex3_Expands_Channels()
    {
        // #f0a → r=0xFF, g=0x00, b=0xAA (each nibble × 17).
        var c = D3Color.Parse("#f0a");
        Assert.Equal(255, c.R);
        Assert.Equal(0, c.G);
        Assert.Equal(170, c.B);
    }

    [Fact]
    public void Parse_Hex_IsCaseInsensitive()
    {
        var upper = D3Color.Parse("#ABCDEF");
        var lower = D3Color.Parse("#abcdef");
        Assert.Equal(upper.R, lower.R);
        Assert.Equal(upper.G, lower.G);
        Assert.Equal(upper.B, lower.B);
    }

    // ─── Parse rgb / rgba ────────────────────────────────────────────

    [Fact]
    public void Parse_Rgb_Function()
    {
        var c = D3Color.Parse("rgb(128, 64, 32)");
        Assert.Equal(128, c.R);
        Assert.Equal(64, c.G);
        Assert.Equal(32, c.B);
        Assert.Equal(1.0, c.Opacity);
    }

    [Fact]
    public void Parse_Rgba_Function_With_Alpha()
    {
        var c = D3Color.Parse("rgba(255, 255, 255, 0.5)");
        Assert.Equal(255, c.R);
        Assert.Equal(0.5, c.Opacity);
    }

    // ─── Parse hsl ───────────────────────────────────────────────────

    [Fact]
    public void Parse_Hsl_Red()
    {
        // hsl(0, 100%, 50%) = pure red.
        var c = D3Color.Parse("hsl(0, 100%, 50%)");
        Assert.Equal(255, c.R);
        Assert.Equal(0, c.G);
        Assert.Equal(0, c.B);
    }

    [Fact]
    public void Parse_Hsl_Green_120deg()
    {
        var c = D3Color.Parse("hsl(120, 100%, 50%)");
        Assert.Equal(0, c.R);
        Assert.Equal(255, c.G);
        Assert.Equal(0, c.B);
    }

    [Fact]
    public void Parse_Hsl_Blue_240deg()
    {
        var c = D3Color.Parse("hsl(240, 100%, 50%)");
        Assert.Equal(0, c.R);
        Assert.Equal(0, c.G);
        Assert.Equal(255, c.B);
    }

    [Fact]
    public void Parse_Hsl_White_Is_Lightness_100()
    {
        var c = D3Color.Parse("hsl(0, 0%, 100%)");
        Assert.Equal(255, c.R);
        Assert.Equal(255, c.G);
        Assert.Equal(255, c.B);
    }

    // ─── Named colors ────────────────────────────────────────────────

    [Theory]
    [InlineData("red", 255, 0, 0)]
    [InlineData("green", 0, 128, 0)]
    [InlineData("blue", 0, 0, 255)]
    [InlineData("white", 255, 255, 255)]
    [InlineData("black", 0, 0, 0)]
    [InlineData("steelblue", 70, 130, 180)]
    [InlineData("tomato", 255, 99, 71)]
    [InlineData("gold", 255, 215, 0)]
    public void Parse_Named_Color(string name, byte r, byte g, byte b)
    {
        var c = D3Color.Parse(name);
        Assert.Equal(r, c.R);
        Assert.Equal(g, c.G);
        Assert.Equal(b, c.B);
    }

    [Fact]
    public void Parse_Gray_And_Grey_Equivalent()
    {
        Assert.Equal(D3Color.Parse("gray").R, D3Color.Parse("grey").R);
    }

    [Fact]
    public void Parse_Transparent_Has_Zero_Opacity()
    {
        var c = D3Color.Parse("transparent");
        Assert.Equal(0.0, c.Opacity);
    }

    [Fact]
    public void Parse_Unknown_Returns_Black()
    {
        var c = D3Color.Parse("not-a-color");
        Assert.Equal(0, c.R);
        Assert.Equal(0, c.G);
        Assert.Equal(0, c.B);
    }

    // ─── Brighter / Darker ───────────────────────────────────────────

    [Fact]
    public void Brighter_Increases_Channels_Towards_White()
    {
        var c = new D3Color(100, 100, 100);
        var b = c.Brighter();
        Assert.True(b.R > c.R);
        Assert.True(b.G > c.G);
        Assert.True(b.B > c.B);
    }

    [Fact]
    public void Darker_Decreases_Channels_Towards_Black()
    {
        var c = new D3Color(200, 200, 200);
        var d = c.Darker();
        Assert.True(d.R < c.R);
        Assert.True(d.G < c.G);
        Assert.True(d.B < c.B);
    }

    [Fact]
    public void Brighter_Clamps_At_255()
    {
        var c = new D3Color(250, 250, 250);
        // k=10 should clamp, not overflow.
        var b = c.Brighter(10);
        Assert.Equal(255, b.R);
        Assert.Equal(255, b.G);
        Assert.Equal(255, b.B);
    }

    [Fact]
    public void Darker_Clamps_At_0()
    {
        var c = new D3Color(10, 10, 10);
        var d = c.Darker(10);
        Assert.Equal(0, d.R);
    }

    [Fact]
    public void Brighter_Preserves_Opacity()
    {
        var c = new D3Color(100, 100, 100, 0.5);
        Assert.Equal(0.5, c.Brighter().Opacity);
    }

    [Fact]
    public void Brighter_Of_Darker_Approximates_Identity()
    {
        // Brighter(k)(Darker(k)(c)) ≈ c up to rounding.
        var c = new D3Color(128, 128, 128);
        var roundTrip = c.Darker().Brighter();
        Assert.InRange(roundTrip.R, 126, 130);
    }

    // ─── Serialization ───────────────────────────────────────────────

    [Fact]
    public void ToHex_Uppercase_Two_Digits()
    {
        var c = new D3Color(255, 0, 170);
        Assert.Equal("#FF00AA", c.ToHex());
    }

    [Fact]
    public void ToRgb_Default_Opacity_Uses_Rgb()
    {
        var c = new D3Color(128, 64, 32);
        Assert.Equal("rgb(128, 64, 32)", c.ToRgb());
    }

    [Fact]
    public void ToRgb_With_Alpha_Uses_Rgba()
    {
        var c = new D3Color(128, 64, 32, 0.5);
        Assert.Equal("rgba(128, 64, 32, 0.5)", c.ToRgb());
    }

    [CulturedFact(new[] { "nl-NL" })]
    public void ToRgb_With_Alpha_Is_Invariant_Under_Comma_Decimal_Culture()
    {
        // rgba() is machine-readable CSS, so it must not pick up the
        // ambient decimal separator. Interpolating the opacity with the current
        // culture emitted "rgba(128, 64, 32, 0,5)" on nl-NL/de-DE/fr-FR/pt-BR/… —
        // an invalid color for every consumer.
        var c = new D3Color(128, 64, 32, 0.5);
        Assert.Equal("rgba(128, 64, 32, 0.5)", c.ToRgb());
        // ToString() delegates to ToRgb(), so it inherited the same defect.
        Assert.Equal("rgba(128, 64, 32, 0.5)", c.ToString());
    }

    [CulturedFact(new[] { "nl-NL" })]
    public void ToRgb_Round_Trips_Through_Parse_Under_Comma_Decimal_Culture()
    {
        // Parse has always read invariant, so the culture-sensitive ToRgb() broke the
        // round-trip in a specifically nasty way: the extra comma made Parse see five
        // components instead of four, so it read the opacity from "0" — turning a
        // half-transparent color into a fully transparent one rather than throwing.
        var original = new D3Color(128, 64, 32, 0.5);

        var round = D3Color.Parse(original.ToRgb());

        Assert.Equal(original.R, round.R);
        Assert.Equal(original.G, round.G);
        Assert.Equal(original.B, round.B);
        Assert.Equal(0.5, round.Opacity);
    }

    [Fact]
    public void ToString_Matches_ToRgb()
    {
        var c = new D3Color(128, 64, 32);
        Assert.Equal(c.ToRgb(), c.ToString());
    }

    // ─── Predefined palettes ─────────────────────────────────────────

    [Fact]
    public void Category10_Has_10_Colors()
    {
        Assert.Equal(10, D3Color.Category10.Count);
    }

    [Fact]
    public void Tableau10_Has_10_Colors()
    {
        Assert.Equal(10, D3Color.Tableau10.Count);
    }

    [Fact]
    public void Category10_First_Is_SteelblueLike()
    {
        // d3.schemeCategory10[0] === "#1f77b4"
        Assert.Equal("#1F77B4", D3Color.Category10[0].ToHex());
    }

    // ─── Opacity clamping ────────────────────────────────────────────

    [Fact]
    public void Constructor_Clamps_Opacity_Above_1()
    {
        var c = new D3Color(0, 0, 0, 2.0);
        Assert.Equal(1.0, c.Opacity);
    }

    [Fact]
    public void Constructor_Clamps_Opacity_Below_0()
    {
        var c = new D3Color(0, 0, 0, -0.5);
        Assert.Equal(0.0, c.Opacity);
    }
}
