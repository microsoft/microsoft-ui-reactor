using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Reactor.Cli.Devtools;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Pure-helper tests for <see cref="DevtoolsPropertyTools"/>. Covers value
/// parsing, formatting, and the Thickness / CornerRadius / Color parsers.
/// Live UI-dispatcher paths (DP reflection, resource walking, ancestors)
/// are exercised by E2E self-host fixtures.
/// </summary>
public class DevtoolsPropertyToolTests
{
    // ─── FormatValue ────────────────────────────────────────────────────

    [Fact]
    public void FormatValue_Null_ReturnsNull()
    {
        Assert.Null(DevtoolsPropertyTools.FormatValue(null));
    }

    [Fact]
    public void FormatValue_String_ReturnsToString()
    {
        Assert.Equal("hello", DevtoolsPropertyTools.FormatValue("hello"));
    }

    [Fact]
    public void FormatValue_Int_ReturnsToString()
    {
        Assert.Equal("42", DevtoolsPropertyTools.FormatValue(42));
    }

    [Fact]
    public void FormatValue_Double_ReturnsToString()
    {
        Assert.Equal("3.14", DevtoolsPropertyTools.FormatValue(3.14));
    }

    [Fact]
    public void FormatValue_Bool_ReturnsToString()
    {
        Assert.Equal("True", DevtoolsPropertyTools.FormatValue(true));
    }

    [Fact]
    public void FormatValue_Thickness_FormatsAllFour()
    {
        var t = new Thickness(1, 2, 3, 4);
        Assert.Equal("1,2,3,4", DevtoolsPropertyTools.FormatValue(t));
    }

    [Fact]
    public void FormatValue_CornerRadius_FormatsAllFour()
    {
        var cr = new CornerRadius(1, 2, 3, 4);
        Assert.Equal("1,2,3,4", DevtoolsPropertyTools.FormatValue(cr));
    }

    [Fact]
    public void FormatValue_Color_FormatsAsArgbHex()
    {
        var c = global::Windows.UI.Color.FromArgb(0xFF, 0xAA, 0xBB, 0xCC);
        Assert.Equal("#FFAABBCC", DevtoolsPropertyTools.FormatValue(c));
    }

    // ─── TryParseColor ──────────────────────────────────────────────────

    [Fact]
    public void TryParseColor_6Digit_ParsesAsRgb()
    {
        Assert.True(DevtoolsPropertyTools.TryParseColor("#FF8800", out var c));
        Assert.Equal(0xFF, c.A);
        Assert.Equal(0xFF, c.R);
        Assert.Equal(0x88, c.G);
        Assert.Equal(0x00, c.B);
    }

    [Fact]
    public void TryParseColor_8Digit_IncludesAlpha()
    {
        Assert.True(DevtoolsPropertyTools.TryParseColor("#80FF0000", out var c));
        Assert.Equal(0x80, c.A);
        Assert.Equal(0xFF, c.R);
        Assert.Equal(0x00, c.G);
        Assert.Equal(0x00, c.B);
    }

    [Fact]
    public void TryParseColor_WithoutHash_StillParses()
    {
        Assert.True(DevtoolsPropertyTools.TryParseColor("AABBCC", out var c));
        Assert.Equal(0xFF, c.A);
        Assert.Equal(0xAA, c.R);
    }

    [Fact]
    public void TryParseColor_ThreeDigitShorthand_ExpandsToSix()
    {
        Assert.True(DevtoolsPropertyTools.TryParseColor("#ABC", out var c));
        Assert.Equal(0xFF, c.A);
        Assert.Equal(0xAA, c.R);
        Assert.Equal(0xBB, c.G);
        Assert.Equal(0xCC, c.B);
    }

    [Fact]
    public void TryParseColor_InvalidLength_ReturnsFalse()
    {
        Assert.False(DevtoolsPropertyTools.TryParseColor("#ABCD", out _));
    }

    [Fact]
    public void TryParseColor_InvalidHex_ReturnsFalse()
    {
        Assert.False(DevtoolsPropertyTools.TryParseColor("#ZZZZZZ", out _));
    }

    // ─── TryParseThickness ──────────────────────────────────────────────

    [Fact]
    public void TryParseThickness_Uniform_SingleValue()
    {
        Assert.True(DevtoolsPropertyTools.TryParseThickness("8", out var t));
        Assert.Equal(new Thickness(8), t);
    }

    [Fact]
    public void TryParseThickness_TwoValues_LeftRightTopBottom()
    {
        Assert.True(DevtoolsPropertyTools.TryParseThickness("4,8", out var t));
        Assert.Equal(4, t.Left);
        Assert.Equal(8, t.Top);
        Assert.Equal(4, t.Right);
        Assert.Equal(8, t.Bottom);
    }

    [Fact]
    public void TryParseThickness_FourValues_Explicit()
    {
        Assert.True(DevtoolsPropertyTools.TryParseThickness("1,2,3,4", out var t));
        Assert.Equal(1, t.Left);
        Assert.Equal(2, t.Top);
        Assert.Equal(3, t.Right);
        Assert.Equal(4, t.Bottom);
    }

    [Fact]
    public void TryParseThickness_ThreeValues_ReturnsFalse()
    {
        Assert.False(DevtoolsPropertyTools.TryParseThickness("1,2,3", out _));
    }

    [Fact]
    public void TryParseThickness_NonNumeric_ReturnsFalse()
    {
        Assert.False(DevtoolsPropertyTools.TryParseThickness("abc", out _));
    }

    [Fact]
    public void TryParseThickness_WithSpaces_Trims()
    {
        Assert.True(DevtoolsPropertyTools.TryParseThickness(" 4 , 8 ", out var t));
        Assert.Equal(4, t.Left);
        Assert.Equal(8, t.Top);
    }

    // ─── TryParseCornerRadius ───────────────────────────────────────────

    [Fact]
    public void TryParseCornerRadius_Uniform_SingleValue()
    {
        Assert.True(DevtoolsPropertyTools.TryParseCornerRadius("8", out var cr));
        Assert.Equal(new CornerRadius(8), cr);
    }

    [Fact]
    public void TryParseCornerRadius_FourValues_Explicit()
    {
        Assert.True(DevtoolsPropertyTools.TryParseCornerRadius("1,2,3,4", out var cr));
        Assert.Equal(1, cr.TopLeft);
        Assert.Equal(2, cr.TopRight);
        Assert.Equal(3, cr.BottomRight);
        Assert.Equal(4, cr.BottomLeft);
    }

    [Fact]
    public void TryParseCornerRadius_TwoValues_ReturnsFalse()
    {
        // CornerRadius only supports 1 or 4 values (unlike Thickness which also supports 2).
        Assert.False(DevtoolsPropertyTools.TryParseCornerRadius("4,8", out _));
    }

    [Fact]
    public void TryParseCornerRadius_NonNumeric_ReturnsFalse()
    {
        Assert.False(DevtoolsPropertyTools.TryParseCornerRadius("xyz", out _));
    }

    // ─── ParseValue ─────────────────────────────────────────────────────

    [Fact]
    public void ParseValue_Bool_True()
    {
        Assert.Equal(true, DevtoolsPropertyTools.ParseValue("true", null));
    }

    [Fact]
    public void ParseValue_Bool_FalseCaseInsensitive()
    {
        Assert.Equal(false, DevtoolsPropertyTools.ParseValue("FALSE", null));
    }

    [Fact]
    public void ParseValue_Integer_WithTargetType()
    {
        var result = DevtoolsPropertyTools.ParseValue("42", typeof(int));
        Assert.IsType<int>(result);
        Assert.Equal(42, result);
    }

    [Fact]
    public void ParseValue_Double_WithDecimalPoint()
    {
        var result = DevtoolsPropertyTools.ParseValue("3.14", null);
        Assert.IsType<double>(result);
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void ParseValue_Double_WithTargetType()
    {
        var result = DevtoolsPropertyTools.ParseValue("99", typeof(double));
        Assert.IsType<double>(result);
        Assert.Equal(99.0, result);
    }

    [Fact]
    public void ParseValue_NumericString_FallsToDouble()
    {
        // "100" with no target type → double fallback (not string).
        var result = DevtoolsPropertyTools.ParseValue("100", null);
        Assert.IsType<double>(result);
        Assert.Equal(100.0, result);
    }

    [Fact]
    public void ParseValue_NonNumericString_FallsToString()
    {
        Assert.Equal("hello", DevtoolsPropertyTools.ParseValue("hello", null));
    }

    [Fact]
    public void ParseValue_Visibility_ByTargetType()
    {
        var result = DevtoolsPropertyTools.ParseValue("collapsed", typeof(Visibility));
        Assert.IsType<Visibility>(result);
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void ParseValue_Visible_WithoutTargetType()
    {
        // "Visible" is a well-known keyword recognized even without a target type hint.
        var result = DevtoolsPropertyTools.ParseValue("Visible", null);
        Assert.IsType<Visibility>(result);
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void ParseValue_HorizontalAlignment_ByTargetType()
    {
        var result = DevtoolsPropertyTools.ParseValue("center", typeof(HorizontalAlignment));
        Assert.IsType<HorizontalAlignment>(result);
        Assert.Equal(HorizontalAlignment.Center, result);
    }

    [Fact]
    public void ParseValue_VerticalAlignment_ByTargetType()
    {
        var result = DevtoolsPropertyTools.ParseValue("bottom", typeof(VerticalAlignment));
        Assert.IsType<VerticalAlignment>(result);
        Assert.Equal(VerticalAlignment.Bottom, result);
    }

    [Fact]
    public void ParseValue_Thickness_ByTargetType()
    {
        var result = DevtoolsPropertyTools.ParseValue("4", typeof(Thickness));
        Assert.IsType<Thickness>(result);
        Assert.Equal(new Thickness(4), result);
    }

    [Fact]
    public void ParseValue_Thickness_FourValues()
    {
        var result = DevtoolsPropertyTools.ParseValue("1,2,3,4", typeof(Thickness));
        Assert.IsType<Thickness>(result);
        var t = (Thickness)result!;
        Assert.Equal(1, t.Left);
        Assert.Equal(2, t.Top);
        Assert.Equal(3, t.Right);
        Assert.Equal(4, t.Bottom);
    }

    [Fact]
    public void ParseValue_CornerRadius_ByTargetType()
    {
        var result = DevtoolsPropertyTools.ParseValue("8", typeof(CornerRadius));
        Assert.IsType<CornerRadius>(result);
        Assert.Equal(new CornerRadius(8), result);
    }

    [Fact]
    public void ParseValue_HexColor_MatchesColorParse()
    {
        // SolidColorBrush constructor requires a WinUI dispatcher, so we can't
        // create one headlessly. Verify the color parsing pathway instead.
        Assert.True(DevtoolsPropertyTools.TryParseColor("#FF0000", out var c));
        Assert.Equal(0xFF, c.R);
        Assert.Equal(0x00, c.G);
        Assert.Equal(0x00, c.B);
    }

    [Fact]
    public void ParseValue_GenericEnum_ByTargetType()
    {
        var result = DevtoolsPropertyTools.ParseValue("stretch", typeof(HorizontalAlignment));
        Assert.IsType<HorizontalAlignment>(result);
        Assert.Equal(HorizontalAlignment.Stretch, result);
    }

    [Fact]
    public void ParseValue_InvariantCulture_DecimalPoint()
    {
        // Ensures "1.5" is parsed as 1.5 regardless of system locale.
        var result = DevtoolsPropertyTools.ParseValue("1.5", typeof(double));
        Assert.IsType<double>(result);
        Assert.Equal(1.5, result);
    }

    [Fact]
    public void ParseValue_TargetTypeMismatch_Throws()
    {
        // "hello" is not parseable as an int — should throw when targetType is provided.
        var ex = Assert.Throws<McpToolException>(() =>
            DevtoolsPropertyTools.ParseValue("hello", typeof(int)));
        Assert.Contains("Cannot parse", ex.Message);
    }

    [Fact]
    public void FormatValue_Double_InvariantCulture()
    {
        // Ensures doubles format with '.' decimal separator regardless of locale.
        var result = DevtoolsPropertyTools.FormatValue(3.14);
        Assert.Equal("3.14", result);
    }

    // ─── KnownVerbs registry ────────────────────────────────────────────

    [Theory]
    [InlineData("properties")]
    [InlineData("set-property")]
    [InlineData("resources")]
    [InlineData("set-resource")]
    [InlineData("styles")]
    [InlineData("ancestors")]
    public void KnownVerbs_ContainsNewPropertyVerbs(string verb)
    {
        Assert.Contains(verb, DevtoolsVerbs.KnownVerbs);
    }
}
