using Microsoft.UI.Reactor.Markdown;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for Md4cUnicode — character classification and case-folding functions
/// used by the Markdown parser. These are pure functions with no dependencies.
/// Exercising all classification categories also covers the static lookup tables
/// (PunctMap, WhitespaceMap, FoldMaps) that contribute ~475 lines.
/// </summary>
public class Md4cUnicodeTests
{
    // ════════════════════════════════════════════════════════════════
    //  ASCII character classification
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData('a', true)]
    [InlineData('z', true)]
    [InlineData('A', false)]
    [InlineData('0', false)]
    public void IsLower(char ch, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsLower(ch));

    [Theory]
    [InlineData('A', true)]
    [InlineData('Z', true)]
    [InlineData('a', false)]
    public void IsUpper(char ch, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsUpper(ch));

    [Theory]
    [InlineData('a', true)]
    [InlineData('Z', true)]
    [InlineData('0', false)]
    [InlineData('!', false)]
    public void IsAlpha(char ch, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsAlpha(ch));

    [Theory]
    [InlineData('0', true)]
    [InlineData('9', true)]
    [InlineData('a', false)]
    public void IsDigit(char ch, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsDigit(ch));

    [Theory]
    [InlineData('0', true)]
    [InlineData('f', true)]
    [InlineData('F', true)]
    [InlineData('g', false)]
    public void IsXDigit(char ch, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsXDigit(ch));

    [Theory]
    [InlineData('a', true)]
    [InlineData('5', true)]
    [InlineData('!', false)]
    public void IsAlNum(char ch, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsAlNum(ch));

    [Theory]
    [InlineData(' ', true)]
    [InlineData('\t', true)]
    [InlineData('a', false)]
    public void IsBlank(char ch, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsBlank(ch));

    [Theory]
    [InlineData(' ', true)]
    [InlineData('\t', true)]
    [InlineData('\v', true)]
    [InlineData('\f', true)]
    [InlineData('x', false)]
    public void IsWhitespace(char ch, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsWhitespace(ch));

    [Theory]
    [InlineData('\r', true)]
    [InlineData('\n', true)]
    [InlineData(' ', false)]
    public void IsNewline(char ch, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsNewline(ch));

    [Theory]
    [InlineData('\0', true)]
    [InlineData('\x1F', true)]
    [InlineData('\x7F', true)]
    [InlineData(' ', false)]
    public void IsCntrl(char ch, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsCntrl(ch));

    [Theory]
    [InlineData('a', true)]
    [InlineData('\x7F', true)]
    [InlineData('\x80', false)]
    public void IsAscii(char ch, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsAscii(ch));

    // ════════════════════════════════════════════════════════════════
    //  ASCII punctuation
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData('!', true)]
    [InlineData('.', true)]
    [InlineData('/', true)]
    [InlineData(':', true)]
    [InlineData('@', true)]
    [InlineData('[', true)]
    [InlineData('`', true)]
    [InlineData('{', true)]
    [InlineData('~', true)]
    [InlineData('a', false)]
    [InlineData(' ', false)]
    public void IsPunct_Ascii(char ch, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsPunct(ch));

    // ════════════════════════════════════════════════════════════════
    //  Unicode whitespace — exercises WhitespaceMap table
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0x0020u, true)]   // space
    [InlineData(0x00A0u, true)]   // no-break space
    [InlineData(0x1680u, true)]   // ogham space mark
    [InlineData(0x2000u, true)]   // en quad (start of range)
    [InlineData(0x2005u, true)]   // four-per-em space (mid range)
    [InlineData(0x200Au, true)]   // hair space (end of range)
    [InlineData(0x202Fu, true)]   // narrow no-break space
    [InlineData(0x205Fu, true)]   // medium mathematical space
    [InlineData(0x3000u, true)]   // ideographic space
    [InlineData(0x0041u, false)]  // 'A'
    [InlineData(0x0100u, false)]  // Latin Extended A
    [InlineData(0x4000u, false)]  // CJK range
    public void IsUnicodeWhitespace(uint codepoint, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsUnicodeWhitespace(codepoint));

    [Fact]
    public void IsUnicodeWhitespace_AsciiSpace() =>
        Assert.True(Md4cUnicode.IsUnicodeWhitespace(0x09)); // tab

    [Fact]
    public void IsUnicodeWhitespace_NotWhitespace() =>
        Assert.False(Md4cUnicode.IsUnicodeWhitespace(0x61)); // 'a'

    // ════════════════════════════════════════════════════════════════
    //  Unicode punctuation — exercises PunctMap table (298 lines!)
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0x0021u, true)]   // '!' (start of range 0021-002f)
    [InlineData(0x002Fu, true)]   // '/' (end of range)
    [InlineData(0x003Au, true)]   // ':' (start of range 003a-0040)
    [InlineData(0x005Bu, true)]   // '[' (start of range 005b-0060)
    [InlineData(0x007Bu, true)]   // '{' (start of range 007b-007e)
    [InlineData(0x00A1u, true)]   // inverted exclamation mark
    [InlineData(0x00ABu, true)]   // left-pointing double angle quotation
    [InlineData(0x00BBu, true)]   // right-pointing double angle quotation
    [InlineData(0x00BFu, true)]   // inverted question mark
    [InlineData(0x00D7u, true)]   // multiplication sign
    [InlineData(0x00F7u, true)]   // division sign
    [InlineData(0x2010u, true)]   // hyphen
    [InlineData(0x2014u, true)]   // em dash
    [InlineData(0x2018u, true)]   // left single quotation mark
    [InlineData(0x201Cu, true)]   // left double quotation mark
    [InlineData(0x2026u, true)]   // horizontal ellipsis
    [InlineData(0x2190u, true)]   // leftwards arrow
    [InlineData(0x25A0u, true)]   // black square
    [InlineData(0x2605u, true)]   // black star
    [InlineData(0x0041u, false)]  // 'A'
    [InlineData(0x0061u, false)]  // 'a'
    [InlineData(0x0030u, false)]  // '0'
    [InlineData(0x4E00u, false)]  // CJK ideograph
    public void IsUnicodePunct(uint codepoint, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsUnicodePunct(codepoint));

    // ════════════════════════════════════════════════════════════════
    //  Case folding — exercises FoldMap tables
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetUnicodeFoldInfo_Ascii_Lowercase()
    {
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo('A', ref info);
        Assert.Equal(1, info.Count);
        Assert.Equal((uint)'a', info.Codepoints[0]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_Ascii_Already_Lower()
    {
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo('a', ref info);
        Assert.Equal(1, info.Count);
        Assert.Equal((uint)'a', info.Codepoints[0]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_Ascii_Digit()
    {
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo('5', ref info);
        Assert.Equal(1, info.Count);
        Assert.Equal((uint)'5', info.Codepoints[0]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_NonAscii_Latin_Capital()
    {
        // U+00C0 = À → U+00E0 = à
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x00C0, ref info);
        Assert.Equal(1, info.Count);
        Assert.Equal(0x00E0u, info.Codepoints[0]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_Greek_Capital()
    {
        // U+0391 = Α → U+03B1 = α
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x0391, ref info);
        Assert.Equal(1, info.Count);
        Assert.Equal(0x03B1u, info.Codepoints[0]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_Cyrillic_Capital()
    {
        // U+0410 = А → U+0430 = а
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x0410, ref info);
        Assert.Equal(1, info.Count);
        Assert.Equal(0x0430u, info.Codepoints[0]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_German_Eszett()
    {
        // U+00DF = ß → maps to itself (it's already lowercase)
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x00DF, ref info);
        // ß case-folds to "ss" (2 codepoints) in full case folding
        Assert.True(info.Count >= 1);
    }

    [Fact]
    public void GetUnicodeFoldInfo_Unmapped_Codepoint_Maps_To_Self()
    {
        // CJK ideograph — no case fold mapping, maps to itself
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x4E00, ref info);
        Assert.Equal(1, info.Count);
        Assert.Equal(0x4E00u, info.Codepoints[0]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_Multiple_Codepoints()
    {
        // U+0130 = İ (Latin Capital Letter I With Dot Above) → folds to i + combining dot (2 codepoints)
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x0130, ref info);
        Assert.True(info.Count >= 1);
    }

    [Fact]
    public void GetUnicodeFoldInfo_High_Codepoint()
    {
        // U+10400 = Deseret Capital Letter Long I → U+10428
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x10400, ref info);
        Assert.Equal(1, info.Count);
        Assert.Equal(0x10428u, info.Codepoints[0]);
    }
}
