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

    // ════════════════════════════════════════════════════════════════
    //  Extended PunctMap coverage — exercise many table regions
    //  Binary search visits different pivot entries depending on
    //  the target codepoint. Spread tests across the full table.
    // ════════════════════════════════════════════════════════════════

    [Theory]
    // Latin-1 supplement symbols
    [InlineData(0x00A2u, true)]   // cent sign
    [InlineData(0x00A5u, true)]   // yen sign
    [InlineData(0x00AEu, true)]   // registered sign
    [InlineData(0x00B4u, true)]   // acute accent (single entry)
    [InlineData(0x00B6u, true)]   // pilcrow sign
    [InlineData(0x02C2u, true)]   // modifier letter left arrowhead (range start)
    [InlineData(0x02C5u, true)]   // modifier letter down arrowhead (range end)
    [InlineData(0x02D2u, true)]   // modifier letter centred right half ring
    [InlineData(0x02EDu, true)]   // modifier letter unaspirated (single)
    [InlineData(0x02EFu, true)]   // modifier letter low down arrowhead (range start)
    // General punctuation
    [InlineData(0x0375u, true)]   // greek lower numeral sign (single)
    [InlineData(0x037Eu, true)]   // greek question mark (single)
    [InlineData(0x0384u, true)]   // greek tonos (range start)
    [InlineData(0x055Au, true)]   // armenian apostrophe (range start)
    [InlineData(0x058Du, true)]   // right-facing armenian eternity sign
    [InlineData(0x0606u, true)]   // arabic-indic cube root
    // Devanagari
    [InlineData(0x0964u, true)]   // devanagari danda
    [InlineData(0x0970u, true)]   // devanagari abbreviation sign
    // Thai
    [InlineData(0x0E3Fu, true)]   // thai currency symbol baht (single)
    // Tibetan
    [InlineData(0x0FD5u, true)]   // right-facing svasti sign
    // Misc symbols
    [InlineData(0x104Au, true)]   // myanmar sign little section
    [InlineData(0x1360u, true)]   // ethiopic section mark
    [InlineData(0x1390u, true)]   // ethiopic tonal mark yizet
    [InlineData(0x166Du, true)]   // canadian syllabics chi sign
    [InlineData(0x169Bu, true)]   // ogham feather mark
    [InlineData(0x17D4u, true)]   // khmer sign khan
    [InlineData(0x1800u, true)]   // mongolian birga
    [InlineData(0x1940u, true)]   // limbu sign loo (single)
    [InlineData(0x19DEu, true)]   // new tai lue sign lae
    [InlineData(0x1B5Au, true)]   // balinese panti
    [InlineData(0x1BFCu, true)]   // batak symbol bindu na metek
    [InlineData(0x1CC0u, true)]   // sundanese punctuation bindu surya
    [InlineData(0x1CD3u, true)]   // vedic sign nihshvasa (single)
    // Arrows and math operators
    [InlineData(0x2194u, true)]   // left right arrow (mid-range)
    [InlineData(0x2200u, true)]   // for all (math operator)
    [InlineData(0x2300u, true)]   // diameter sign
    [InlineData(0x2400u, true)]   // symbol for null
    [InlineData(0x2440u, true)]   // OCR hook
    [InlineData(0x3200u, true)]   // parenthesized hangul kiyeok
    [InlineData(0x2500u, true)]   // box drawings light horizontal
    [InlineData(0x2701u, true)]   // upper blade scissors
    [InlineData(0x2794u, true)]   // heavy wide-headed rightwards arrow
    [InlineData(0x27C0u, true)]   // three dimensional angle
    [InlineData(0x2900u, true)]   // rightwards two-headed arrow with vertical stroke
    [InlineData(0x2B00u, true)]   // north east white arrow
    [InlineData(0x2E00u, true)]   // right angle substitution marker
    // CJK symbols
    [InlineData(0x3001u, true)]   // ideographic comma
    [InlineData(0x3008u, true)]   // left angle bracket
    [InlineData(0x3020u, true)]   // postal mark face
    [InlineData(0x3030u, true)]   // wavy dash
    [InlineData(0xA490u, true)]   // yi radical qot
    [InlineData(0xA700u, true)]   // modifier letter chinese tone yin ping
    [InlineData(0xA720u, true)]   // modifier letter stress and high tone
    [InlineData(0xA828u, true)]   // syloti nagri poetry mark-1
    [InlineData(0xA874u, true)]   // phags-pa single head mark
    [InlineData(0xAB5Bu, true)]   // vai full stop (single)
    // Fullwidth punctuation
    [InlineData(0xFE10u, true)]   // presentation form for vertical comma
    [InlineData(0xFE30u, true)]   // presentation form for vertical two dot leader
    [InlineData(0xFE50u, true)]   // small comma
    [InlineData(0xFF01u, true)]   // fullwidth exclamation mark
    [InlineData(0xFF65u, true)]   // halfwidth katakana middle dot
    [InlineData(0xFFFDu, true)]   // replacement character
    // Supplementary symbols (high ranges)
    [InlineData(0x10100u, true)]  // aegean word separator line
    [InlineData(0x10137u, true)]  // aegean weight base unit
    [InlineData(0x10190u, true)]  // roman sextans sign
    [InlineData(0x1091Fu, true)]  // phoenician word separator
    [InlineData(0x10B39u, true)]  // avestan abbreviation mark
    [InlineData(0x11047u, true)]  // brahmi danda
    [InlineData(0x110BBu, true)]  // kaithi enumeration sign
    [InlineData(0x11140u, true)]  // chakma section mark
    [InlineData(0x11174u, true)]  // mahajani abbreviation sign
    [InlineData(0x111C5u, true)]  // sharada danda
    [InlineData(0x11238u, true)]  // khojki danda
    [InlineData(0x112A9u, true)]  // multani section mark
    [InlineData(0x1144Bu, true)]  // newa danda
    [InlineData(0x1145Bu, true)]  // newa placeholder mark
    [InlineData(0x1173Cu, true)]  // ahom sign small section
    [InlineData(0x11C41u, true)]  // bhaiksuki danda
    [InlineData(0x11C71u, true)]  // marchen mark shad
    [InlineData(0x11EF7u, true)]  // makasar passimbang
    [InlineData(0x16A6Eu, true)]  // mro danda
    [InlineData(0x16B37u, true)]  // pahawh hmong sign xyeem ntxiv
    [InlineData(0x1BC9Cu, true)]  // duployan sign o with cross (single)
    // Emoji and pictographs
    [InlineData(0x1F000u, true)]  // mahjong tile east wind
    [InlineData(0x1F10Du, true)]  // circled zero with slash
    [InlineData(0x1F300u, true)]  // cyclone (emoji)
    [InlineData(0x1F600u, true)]  // grinning face (emoji)
    [InlineData(0x1F900u, true)]  // large blue circle (emoji)
    [InlineData(0x1FA00u, true)]  // chess pawn
    [InlineData(0x1FA60u, true)]  // light blue heart
    [InlineData(0x1FB00u, true)]  // block sextant-1
    // Non-punct controls
    [InlineData(0x0041u, false)]  // 'A'
    [InlineData(0x0061u, false)]  // 'a'
    [InlineData(0x4E00u, false)]  // CJK ideograph
    [InlineData(0x10000u, false)] // linear B syllable B008 A
    public void IsUnicodePunct_Extended(uint codepoint, bool expected) =>
        Assert.Equal(expected, Md4cUnicode.IsUnicodePunct(codepoint));

    // ════════════════════════════════════════════════════════════════
    //  Extended case folding — exercise FoldMap1 across many ranges
    // ════════════════════════════════════════════════════════════════

    [Theory]
    // Basic Latin capitals
    [InlineData(0x0041u, 0x0061u)]  // A → a
    [InlineData(0x005Au, 0x007Au)]  // Z → z
    // Latin Extended-A (even codepoints are uppercase)
    [InlineData(0x0100u, 0x0101u)]  // Ā → ā
    [InlineData(0x012Eu, 0x012Fu)]  // Į → į
    [InlineData(0x0132u, 0x0133u)]  // Ĳ → ĳ
    [InlineData(0x0139u, 0x013Au)]  // Ĺ → ĺ
    [InlineData(0x014Au, 0x014Bu)]  // Ŋ → ŋ
    [InlineData(0x0176u, 0x0177u)]  // Ŷ → ŷ
    // Latin Extended-B
    [InlineData(0x01A0u, 0x01A1u)]  // Ơ → ơ
    [InlineData(0x01DEu, 0x01DFu)]  // Ǟ → ǟ
    // Greek uppercase
    [InlineData(0x0391u, 0x03B1u)]  // Α → α
    [InlineData(0x03A3u, 0x03C3u)]  // Σ → σ
    [InlineData(0x03A9u, 0x03C9u)]  // Ω → ω
    // Cyrillic uppercase
    [InlineData(0x0410u, 0x0430u)]  // А → а
    [InlineData(0x042Fu, 0x044Fu)]  // Я → я
    // Armenian uppercase
    [InlineData(0x0531u, 0x0561u)]  // Ա → ա
    // Georgian uppercase (Mtavruli)
    [InlineData(0x1C90u, 0x10D0u)]  // Ⴀ → ა
    // Fullwidth Latin uppercase
    [InlineData(0xFF21u, 0xFF41u)]  // Ａ → ａ
    // Deseret uppercase
    [InlineData(0x10400u, 0x10428u)] // 𐐀 → 𐐨
    public void GetUnicodeFoldInfo_Extended(uint input, uint expectedFirst)
    {
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(input, ref info);
        Assert.True(info.Count >= 1);
        Assert.Equal(expectedFirst, info.Codepoints[0]);
    }

    // ════════════════════════════════════════════════════════════════
    //  FoldMap2 — two-codepoint folds
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetUnicodeFoldInfo_Eszett_Folds_To_SS()
    {
        // U+00DF (ß) → 0x0073 0x0073 (ss)
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x00DF, ref info);
        Assert.Equal(2, info.Count);
        Assert.Equal(0x0073u, info.Codepoints[0]);
        Assert.Equal(0x0073u, info.Codepoints[1]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_IDot_Folds_To_I_Dot()
    {
        // U+0130 (İ) → 0x0069 0x0307 (i + combining dot above)
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x0130, ref info);
        Assert.Equal(2, info.Count);
        Assert.Equal(0x0069u, info.Codepoints[0]);
        Assert.Equal(0x0307u, info.Codepoints[1]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_Armenian_ECH_YIWN()
    {
        // U+0587 (և) → 0x0565 0x0582
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x0587, ref info);
        Assert.Equal(2, info.Count);
        Assert.Equal(0x0565u, info.Codepoints[0]);
        Assert.Equal(0x0582u, info.Codepoints[1]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_FB00_ff_Ligature()
    {
        // U+FB00 (ﬀ) → 0x0066 0x0066 (ff)
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0xFB00, ref info);
        Assert.Equal(2, info.Count);
        Assert.Equal(0x0066u, info.Codepoints[0]);
        Assert.Equal(0x0066u, info.Codepoints[1]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_FB01_fi_Ligature()
    {
        // U+FB01 (ﬁ) → 0x0066 0x0069 (fi)
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0xFB01, ref info);
        Assert.Equal(2, info.Count);
        Assert.Equal(0x0066u, info.Codepoints[0]);
        Assert.Equal(0x0069u, info.Codepoints[1]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_FB02_fl_Ligature()
    {
        // U+FB02 (ﬂ) → 0x0066 0x006C (fl)
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0xFB02, ref info);
        Assert.Equal(2, info.Count);
        Assert.Equal(0x0066u, info.Codepoints[0]);
        Assert.Equal(0x006Cu, info.Codepoints[1]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_1F80_Range()
    {
        // U+1F80 (ᾀ) → 0x1F00 0x03B9 (2-codepoint fold)
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x1F80, ref info);
        Assert.Equal(2, info.Count);
        Assert.Equal(0x1F00u, info.Codepoints[0]);
        Assert.Equal(0x03B9u, info.Codepoints[1]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_1FBC_AlphaIota()
    {
        // U+1FBC (ᾼ) → 0x03B1 0x03B9
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x1FBC, ref info);
        Assert.Equal(2, info.Count);
        Assert.Equal(0x03B1u, info.Codepoints[0]);
        Assert.Equal(0x03B9u, info.Codepoints[1]);
    }

    // ════════════════════════════════════════════════════════════════
    //  FoldMap3 — three-codepoint folds
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetUnicodeFoldInfo_0390_Iota_Dialytika_Tonos()
    {
        // U+0390 → 0x03B9 0x0308 0x0301 (3-codepoint fold)
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x0390, ref info);
        Assert.Equal(3, info.Count);
        Assert.Equal(0x03B9u, info.Codepoints[0]);
        Assert.Equal(0x0308u, info.Codepoints[1]);
        Assert.Equal(0x0301u, info.Codepoints[2]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_03B0_Upsilon_Dialytika_Tonos()
    {
        // U+03B0 → 0x03C5 0x0308 0x0301 (3-codepoint fold)
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0x03B0, ref info);
        Assert.Equal(3, info.Count);
        Assert.Equal(0x03C5u, info.Codepoints[0]);
        Assert.Equal(0x0308u, info.Codepoints[1]);
        Assert.Equal(0x0301u, info.Codepoints[2]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_FB03_ffi_Ligature()
    {
        // U+FB03 (ﬃ) → 0x0066 0x0066 0x0069 (ffi)
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0xFB03, ref info);
        Assert.Equal(3, info.Count);
        Assert.Equal(0x0066u, info.Codepoints[0]);
        Assert.Equal(0x0066u, info.Codepoints[1]);
        Assert.Equal(0x0069u, info.Codepoints[2]);
    }

    [Fact]
    public void GetUnicodeFoldInfo_FB04_ffl_Ligature()
    {
        // U+FB04 (ﬄ) → 0x0066 0x0066 0x006C (ffl)
        var info = new UnicodeFoldInfo();
        Md4cUnicode.GetUnicodeFoldInfo(0xFB04, ref info);
        Assert.Equal(3, info.Count);
        Assert.Equal(0x0066u, info.Codepoints[0]);
        Assert.Equal(0x0066u, info.Codepoints[1]);
        Assert.Equal(0x006Cu, info.Codepoints[2]);
    }

    // ════════════════════════════════════════════════════════════════
    //  DecodeUnicode / DecodeUnicodeBefore — surrogate pair handling
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DecodeUnicode_BasicAscii()
    {
        var s = "Hello";
        var result = Md4cUnicode.DecodeUnicode(s, 0, s.Length, out var next);
        Assert.Equal((uint)'H', result);
        Assert.Equal(1, next);
    }

    [Fact]
    public void DecodeUnicode_SurrogatePair()
    {
        // U+1F600 = 😀 (grinning face) encoded as surrogate pair
        var s = "\U0001F600";
        var result = Md4cUnicode.DecodeUnicode(s, 0, s.Length, out var next);
        Assert.Equal(0x1F600u, result);
        Assert.Equal(2, next);
    }

    [Fact]
    public void DecodeUnicode_NonBMP()
    {
        // U+10400 (Deseret A) encoded as surrogate pair
        var s = "\U00010400";
        var result = Md4cUnicode.DecodeUnicode(s, 0, s.Length, out var next);
        Assert.Equal(0x10400u, result);
        Assert.Equal(2, next);
    }

    [Fact]
    public void DecodeUnicodeBefore_BasicAscii()
    {
        var s = "AB";
        var result = Md4cUnicode.DecodeUnicodeBefore(s, 1);
        Assert.Equal((uint)'A', result);
    }

    [Fact]
    public void DecodeUnicodeBefore_SurrogatePair()
    {
        var s = "X\U0001F600Y";
        // The emoji occupies indices 1 and 2, so 'Y' is at index 3
        var result = Md4cUnicode.DecodeUnicodeBefore(s, 3);
        Assert.Equal(0x1F600u, result);
    }
}
