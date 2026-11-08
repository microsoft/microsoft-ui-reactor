using System.Text;
using ReactorRegedit.Core;
using Xunit;

namespace ReactorRegedit.Tests;

public class ValueFormatTests
{
    [Fact]
    public void Dword_DisplaysHexAndDecimalLikeRegedit()
    {
        Assert.Equal("0x0000002a (42)", ValueFormat.Display(RegType.Dword, BitConverter.GetBytes(42u)));
        Assert.Equal("0xffffffff (4294967295)", ValueFormat.Display(RegType.Dword, BitConverter.GetBytes(uint.MaxValue)));
        Assert.Equal("(invalid DWORD (32-bit) value)", ValueFormat.Display(RegType.Dword, [1, 2]));
    }

    [Fact]
    public void Qword_DisplaysHexAndDecimal()
    {
        Assert.Equal("0x000000000000002a (42)", ValueFormat.Display(RegType.Qword, BitConverter.GetBytes(42UL)));
        Assert.Equal("(invalid QWORD (64-bit) value)", ValueFormat.Display(RegType.Qword, [1, 2, 3, 4]));
    }

    [Fact]
    public void BigEndianDword_IsByteSwapped()
    {
        Assert.Equal("0x0000002a (42)", ValueFormat.Display(RegType.DwordBigEndian, [0, 0, 0, 42]));
    }

    [Theory]
    [InlineData(RegType.Binary)]
    [InlineData(RegType.None)]
    [InlineData(RegType.ResourceList)]
    [InlineData(0x1234u)]
    public void BinaryFamily_IsSpaceSeparatedHex(uint type)
    {
        Assert.Equal("01 ab ff", ValueFormat.Display(type, [0x01, 0xAB, 0xFF]));
        Assert.Equal("(zero-length binary value)", ValueFormat.Display(type, []));
    }

    [Fact]
    public void Binary_IsCappedForDisplay()
    {
        var text = ValueFormat.Display(RegType.Binary, new byte[ValueFormat.MaxDisplayBytes + 10]);
        Assert.EndsWith(" ...", text);
    }

    [Fact]
    public void Strings_StopAtFirstNul_AndExpandSzIsNotExpanded()
    {
        Assert.Equal("abc", ValueFormat.Display(RegType.Sz, Encoding.Unicode.GetBytes("abc\0junk")));
        Assert.Equal("%SystemRoot%\\x", ValueFormat.Display(RegType.ExpandSz, ValueFormat.EncodeString("%SystemRoot%\\x")));
        Assert.Equal("", ValueFormat.Display(RegType.Sz, []));
    }

    [Fact]
    public void EncodeString_IsNulTerminatedUtf16()
    {
        Assert.Equal(new byte[] { (byte)'a', 0, 0, 0 }, ValueFormat.EncodeString("a"));
    }

    [Fact]
    public void MultiString_RoundTripsAndKeepsInnerEmptyStrings()
    {
        var bytes = ValueFormat.EncodeMultiString(["a", "b"]);
        Assert.Equal(Encoding.Unicode.GetBytes("a\0b\0\0"), bytes);
        Assert.Equal(["a", "b"], ValueFormat.DecodeMultiString(bytes));
        Assert.Equal(["a", "", "b"], ValueFormat.DecodeMultiString(Encoding.Unicode.GetBytes("a\0\0b\0\0")));
        Assert.Empty(ValueFormat.DecodeMultiString([]));
        Assert.Equal("one two", ValueFormat.Display(RegType.MultiSz, ValueFormat.EncodeMultiString(["one", "two"])));
    }

    [Fact]
    public void MultiStringEditor_RemovesEmptyLinesAndReportsIt()
    {
        var lines = ValueFormat.ParseMultiStringEditor("a\r\n\r\nb\r\n", out var removed);
        Assert.Equal(["a", "b"], lines);
        Assert.True(removed);

        lines = ValueFormat.ParseMultiStringEditor("a\rb", out removed);
        Assert.Equal(["a", "b"], lines);
        Assert.False(removed);
    }

    [Theory]
    [InlineData("2a", true, 42u)]
    [InlineData("0x2A", true, 42u)]
    [InlineData("42", false, 42u)]
    [InlineData("ffffffff", true, uint.MaxValue)]
    [InlineData("", true, 0u)]
    public void TryParseDword_Accepts(string text, bool hex, uint expected)
    {
        Assert.True(ValueFormat.TryParseDword(text, hex, out var v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData("100000000", true)]
    [InlineData("4294967296", false)]
    [InlineData("-1", false)]
    [InlineData("zz", true)]
    [InlineData("2a", false)]
    public void TryParseDword_RejectsOverflowAndJunk(string text, bool hex)
    {
        Assert.False(ValueFormat.TryParseDword(text, hex, out _));
    }

    [Fact]
    public void TryParseQword_HandlesFullRange()
    {
        Assert.True(ValueFormat.TryParseQword("ffffffffffffffff", true, out var v));
        Assert.Equal(ulong.MaxValue, v);
        Assert.False(ValueFormat.TryParseQword("18446744073709551616", false, out _));
    }

    [Fact]
    public void RegValue_UnsetDefaultShowsValueNotSet()
    {
        var v = RegValue.UnsetDefault();
        Assert.Equal("(Default)", v.DisplayName);
        Assert.Equal("REG_SZ", v.TypeName);
        Assert.Equal("(value not set)", v.DisplayData);
    }

    [Fact]
    public void TypeNames_CoverAllKnownTypesAndUnknown()
    {
        Assert.Equal("REG_RESOURCE_REQUIREMENTS_LIST", RegType.Name(RegType.ResourceRequirementsList));
        Assert.Equal("REG_LINK", RegType.Name(RegType.Link));
        Assert.Equal("0x00001234", RegType.Name(0x1234));
        Assert.True(RegType.IsStringFamily(RegType.MultiSz));
        Assert.False(RegType.IsStringFamily(RegType.Link));
    }
}

public class ValueSortTests
{
    private static readonly RegValue[] Values =
    [
        RegValue.FromString("b", "zzz"),
        RegValue.FromDword("a", 1),
        RegValue.FromString("", "default"),
        RegValue.FromString("C", "aaa"),
    ];

    [Fact]
    public void DefaultStaysFirst_InBothDirections()
    {
        Assert.Equal(["", "a", "b", "C"], ValueSort.Sort(Values, ValueSortColumn.Name, false).Select(v => v.Name));
        Assert.Equal(["", "C", "b", "a"], ValueSort.Sort(Values, ValueSortColumn.Name, true).Select(v => v.Name));
    }

    [Fact]
    public void SortsByTypeAndData()
    {
        Assert.Equal(["", "a", "b", "C"], ValueSort.Sort(Values, ValueSortColumn.Type, false).Select(v => v.Name));
        Assert.Equal(["", "b", "C", "a"], ValueSort.Sort(Values, ValueSortColumn.Data, true).Select(v => v.Name));
    }
}

public class HexDumpTests
{
    [Fact]
    public void FormatsEightBytesPerLine()
    {
        var bytes = Enumerable.Range(0, 10).Select(i => (byte)i).ToArray();
        Assert.Equal("00 01 02 03 04 05 06 07\r08 09", HexDump.FormatBytes(bytes));
        Assert.Equal("0000\r0008", HexDump.Offsets(10));
        Assert.Equal("........\r..", HexDump.Ascii(bytes));
    }

    [Fact]
    public void TypingReflowsIntoPairs_AndCaretTracksDigits()
    {
        var digits = HexDump.ExtractDigits("ab c", out var invalid);
        Assert.False(invalid);
        Assert.Equal("ABC", digits);
        var canonical = HexDump.FormatDigits(digits);
        Assert.Equal("AB C", canonical);
        Assert.Equal(4, HexDump.CaretAfterDigits(canonical, 3));
        Assert.Equal(new byte[] { 0xAB, 0x0C }, HexDump.DigitsToBytes(digits));
        Assert.Equal(2, HexDump.DigitsBefore("ab c", 3));
    }

    [Fact]
    public void NonHexCharactersAreDropped()
    {
        Assert.Equal("0A", HexDump.ExtractDigits("0g a", out var invalid));
        Assert.True(invalid);
    }

    [Fact]
    public void AsciiShowsPrintables()
    {
        Assert.Equal("Hi.", HexDump.Ascii([(byte)'H', (byte)'i', 0x00]));
    }
}
