using System.Globalization;
using ReactorRegedit.Core;
using Xunit;

namespace ReactorRegedit.Tests;

public class RegTextWriterTests
{
    [Fact]
    public void WriteKey_ClassicTextLayout()
    {
        var sw = new StringWriter();
        new RegTextWriter(sw, CultureInfo.GetCultureInfo("en-US")).WriteKey(@"HKEY_CURRENT_USER\Software\T",
            new DateTime(2026, 11, 8, 14, 3, 0),
            [RegValue.FromString("", "def"), RegValue.FromDword("D", 7), RegValue.FromMultiString("M", ["a", "b"])]);
        Assert.Equal(
            "Key Name:          HKEY_CURRENT_USER\\Software\\T\r\n" +
            "Class Name:        <NO CLASS>\r\n" +
            "Last Write Time:   11/8/2026 - 2:03 PM\r\n" +
            "Value 0\r\n  Name:            <NO NAME>\r\n  Type:            REG_SZ\r\n  Data:            def\r\n\r\n" +
            "Value 1\r\n  Name:            D\r\n  Type:            REG_DWORD\r\n  Data:            0x7\r\n\r\n" +
            "Value 2\r\n  Name:            M\r\n  Type:            REG_MULTI_SZ\r\n  Data:            a\r\n                   b\r\n\r\n" +
            "\r\n",
            sw.ToString());
    }

    [Fact]
    public void WriteKey_SkipsSyntheticDefault()
    {
        var sw = new StringWriter();
        new RegTextWriter(sw).WriteKey("HKEY_USERS", null, [RegValue.UnsetDefault()]);
        Assert.Equal("Key Name:          HKEY_USERS\r\nClass Name:        <NO CLASS>\r\n\r\n", sw.ToString());
    }

    [Fact]
    public void FormatData_BinaryIsHexDumpWithAscii()
    {
        var data = Enumerable.Range(0x41, 18).Select(i => (byte)i).ToArray();
        Assert.Equal(
            "\r\n00000000  41 42 43 44 45 46 47 48 - 49 4a 4b 4c 4d 4e 4f 50   ABCDEFGHIJKLMNOP" +
            "\r\n00000010  51 52" + new string(' ', 47) + "QR",
            RegTextWriter.FormatData(RegType.Binary, data));
    }

    [Fact]
    public void FormatData_NumbersAndOddSizes()
    {
        Assert.Equal("0x2a", RegTextWriter.FormatData(RegType.Qword, BitConverter.GetBytes(42UL)));
        Assert.Equal("\r\n00000000  01 02" + new string(' ', 47) + "..", RegTextWriter.FormatData(RegType.Dword, [1, 2]));
        Assert.Equal("", RegTextWriter.FormatData(RegType.None, []));
    }
}
