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

    // ─── Additional edge-case coverage (coverage-uplift-85) ─────────────
    //
    // These tests exercise behaviors of FormatValue / ParseValue /
    // TryParseColor that weren't previously hit, chosen because each
    // assertion ties to a real product contract (a regression in the
    // associated branch would cause the test to fail).

    [Fact]
    public void FormatValue_IFormattable_UsesInvariantCulture()
    {
        // decimal implements IFormattable. The contract is that we format
        // with InvariantCulture so cross-machine round-trips don't depend
        // on the system locale (e.g. "1,5" vs "1.5"). A regression that
        // dropped the IFormattable arm and fell to value.ToString() would
        // pick up the *current* culture and break this assertion on a
        // German-locale machine.
        var formatted = DevtoolsPropertyTools.FormatValue(1.5m);
        Assert.Equal("1.5", formatted);
    }

    // FormatValue's non-SolidColorBrush arm and ParseValue's hex-to-
    // SolidColorBrush construction both need a WinUI activation context
    // (Brush subclass activation fails headlessly). Both are tracked in
    // the worklist as "needs selftest fixture" rather than added here.

    [Fact]
    public void ParseValue_Thickness_TwoValues_ByTargetType()
    {
        // "4,8" with target Thickness must produce Thickness(left=right=4,
        // top=bottom=8). The 2-value form was not previously covered by
        // ParseValue() callers — only by TryParseThickness directly.
        var result = DevtoolsPropertyTools.ParseValue("4,8", typeof(Thickness));
        Assert.IsType<Thickness>(result);
        var t = (Thickness)result!;
        Assert.Equal(4, t.Left);
        Assert.Equal(8, t.Top);
        Assert.Equal(4, t.Right);
        Assert.Equal(8, t.Bottom);
    }

    [Fact]
    public void ParseValue_Thickness_CommaInRaw_ImpliesThicknessEvenWithoutTargetType()
    {
        // When raw contains ',' and TryParseThickness succeeds, ParseValue
        // returns a Thickness even with targetType=null. (See the
        // `raw.Contains(',') && TryParseThickness(...)` guard.) This branch
        // is what lets `setProperty Margin "1,2,3,4"` work without knowing
        // the DP's CLR type up front.
        var result = DevtoolsPropertyTools.ParseValue("1,2,3,4", null);
        Assert.IsType<Thickness>(result);
        var t = (Thickness)result!;
        Assert.Equal(1, t.Left);
        Assert.Equal(4, t.Bottom);
    }

    [Fact]
    public void ParseValue_EnumByGenericTargetType()
    {
        // Generic enum path (targetType.IsEnum), distinct from the
        // well-known Visibility/HorizontalAlignment/VerticalAlignment arms.
        // FlowDirection is a small WinUI enum that exercises only the
        // generic enum path.
        var result = DevtoolsPropertyTools.ParseValue("rightToLeft", typeof(FlowDirection));
        Assert.IsType<FlowDirection>(result);
        Assert.Equal(FlowDirection.RightToLeft, result);
    }

    [Fact]
    public void ParseValue_Bool_Mixed_Case_HasPrecedence_Over_String_Fallback()
    {
        // "True" parses to bool true rather than falling to string. A
        // regression that moved the bool arm below the string fallback
        // would silently return "True" as a string and break setProperty
        // for any IsEnabled-like DP.
        Assert.Equal(true, DevtoolsPropertyTools.ParseValue("True", null));
        Assert.Equal(false, DevtoolsPropertyTools.ParseValue("fAlSe", null));
    }

    [Fact]
    public void TryParseColor_EightDigit_PreservesAlpha()
    {
        // Distinct from the existing 8-digit test which uses A=0x80; here
        // we verify that A=0x00 (fully transparent) is preserved rather
        // than coerced to 0xFF. The current code path: A is read from the
        // first byte without any default-FF fallback.
        Assert.True(DevtoolsPropertyTools.TryParseColor("#00112233", out var c));
        Assert.Equal(0x00, c.A);
        Assert.Equal(0x11, c.R);
        Assert.Equal(0x22, c.G);
        Assert.Equal(0x33, c.B);
    }

    [Fact]
    public void TryParseColor_LowerCaseHex_IsAccepted()
    {
        // byte.Parse(NumberStyles.HexNumber) accepts both cases; the
        // assertion here is that no upper-casing is required at the call
        // site (i.e. nobody added a `.ToUpper()` that broke "tan-style"
        // shorthand).
        Assert.True(DevtoolsPropertyTools.TryParseColor("#abcdef", out var c));
        Assert.Equal(0xAB, c.R);
        Assert.Equal(0xCD, c.G);
        Assert.Equal(0xEF, c.B);
    }

    [Fact]
    public void TryParseColor_FiveDigit_ReturnsFalse()
    {
        // 5 chars is neither #RGB nor #RRGGBB nor #AARRGGBB. The default
        // arm of the length switch must reject it.
        Assert.False(DevtoolsPropertyTools.TryParseColor("#ABCDE", out var c));
        Assert.Equal(default, c);
    }

    [Fact]
    public void TryParseColor_Empty_ReturnsFalse()
    {
        // Hex string of length 0 after TrimStart('#') hits the default
        // arm — no exception, no garbage Color, just false.
        Assert.False(DevtoolsPropertyTools.TryParseColor("#", out var c));
        Assert.Equal(default, c);
        Assert.False(DevtoolsPropertyTools.TryParseColor("", out _));
    }

    [Fact]
    public void TryParseThickness_NegativeValues_AreAccepted()
    {
        // Margin/Padding accept negative values (e.g. negative margin to
        // overlap siblings). A regression that added a `>= 0` guard would
        // break this.
        Assert.True(DevtoolsPropertyTools.TryParseThickness("-4,-8,-12,-16", out var t));
        Assert.Equal(-4, t.Left);
        Assert.Equal(-16, t.Bottom);
    }

    [Fact]
    public void TryParseCornerRadius_Negative_PropagatesArgumentException()
    {
        // Discovery 2026-05-17: WinUI's CornerRadius struct validates in its
        // ctor and throws ArgumentException for negative components.
        // TryParseCornerRadius doesn't catch this — it propagates out of a
        // TryParse-named method, which is surprising. Pin the current
        // behavior so a future contract change (return false on
        // validation failure, matching Try* convention) is intentional.
        Assert.Throws<ArgumentException>(() =>
            DevtoolsPropertyTools.TryParseCornerRadius("-1,-2,-3,-4", out _));
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
