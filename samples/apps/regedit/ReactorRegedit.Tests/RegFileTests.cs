using System.Text;
using ReactorRegedit.Core;
using Xunit;

namespace ReactorRegedit.Tests;

public class RegFileTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>Rebuilds .reg text from parsed ops with our writer.</summary>
    private static string Rewrite(RegFileParseResult parsed)
    {
        var writer = new RegFileWriter();
        string? key = null;
        var values = new List<RegValue>();
        void Flush()
        {
            if (key is not null) writer.WriteKey(key, values);
            values.Clear();
        }
        foreach (var op in parsed.Ops)
        {
            switch (op)
            {
                case CreateKeyOp c:
                    Flush();
                    key = c.KeyPath;
                    break;
                case SetValueOp s:
                    values.Add(new RegValue(s.Name, s.Type, s.Data));
                    break;
            }
        }
        Flush();
        return writer.Text;
    }

    [Fact]
    public void RegExportFixture_RoundTripsByteForByte()
    {
        // Fixture produced by `reg export` (UTF-16 LE + BOM) covering every common type,
        // escaped names/strings, empty values and line wrapping at different columns.
        var bytes = File.ReadAllBytes(FixturePath("reg-export.reg"));
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);
        var text = RegFileParser.Decode(bytes);

        var parsed = RegFileParser.Parse(text);
        Assert.Empty(parsed.Errors);
        Assert.False(parsed.IsRegedit4);

        Assert.Equal(text, Rewrite(parsed));
    }

    [Fact]
    public void Parser_DecodesTypesFromFixture()
    {
        var parsed = RegFileParser.Parse(RegFileParser.Decode(File.ReadAllBytes(FixturePath("reg-export.reg"))));
        var values = parsed.Ops.OfType<SetValueOp>().ToDictionary(v => v.Name);

        Assert.Equal("default value", ValueFormat.DecodeString(values[""].Data));
        Assert.Equal("C:\\path\\\"quoted\"", ValueFormat.DecodeString(values["Quote\"And\\Back"].Data));
        Assert.Equal(RegType.ExpandSz, values["Exp"].Type);
        Assert.Equal("%SystemRoot%\\system32;%PATH%", ValueFormat.DecodeString(values["Exp"].Data));
        Assert.Equal(3, ValueFormat.DecodeMultiString(values["Multi"].Data).Length);
        Assert.Equal(100, values["Bin"].Data.Length);
        Assert.Empty(values["BinEmpty"].Data);
        Assert.Equal(0xdeadbeefu, BitConverter.ToUInt32(values["Dw"].Data));
        Assert.Equal(0x1122334455667788UL, BitConverter.ToUInt64(values["Qw"].Data));
        Assert.Equal(RegType.None, values["None"].Type);
        Assert.Equal(RegType.DwordBigEndian, values["BE"].Type);
        Assert.Contains(parsed.Ops, o => o is CreateKeyOp { KeyPath: @"HKEY_CURRENT_USER\Software\ReactorRegeditTest\Fixture\Sub Key\Nested" });
    }

    [Fact]
    public void Writer_UsesHexForUnusualTypesAndDirtyStrings()
    {
        Assert.Equal("\"L\"=hex(6):41,00", RegFileWriter.FormatValueLine(new RegValue("L", RegType.Link, [0x41, 0])));
        Assert.Equal("\"U\"=hex(1234):01", RegFileWriter.FormatValueLine(new RegValue("U", 0x1234, [1])));
        // Embedded newline / missing terminator would not survive a quoted string.
        Assert.StartsWith("\"S\"=hex(1):", RegFileWriter.FormatValueLine(new RegValue("S", RegType.Sz, Encoding.Unicode.GetBytes("a\nb\0"))));
        Assert.StartsWith("\"S\"=hex(1):", RegFileWriter.FormatValueLine(new RegValue("S", RegType.Sz, Encoding.Unicode.GetBytes("ab"))));
        // A DWORD with the wrong length can't be written as dword:.
        Assert.Equal("\"D\"=hex(4):01,02", RegFileWriter.FormatValueLine(new RegValue("D", RegType.Dword, [1, 2])));
    }

    [Fact]
    public void Writer_WrappedLinesNeverExceed80Columns()
    {
        var line = RegFileWriter.FormatValueLine(new RegValue("Name", RegType.Binary, new byte[500]));
        foreach (var l in line.Split("\r\n")) Assert.True(l.Length <= 80, $"{l.Length}: {l}");
    }

    [Fact]
    public void Parser_HandlesDeletionsCommentsAndAbbreviations()
    {
        const string text = """
            Windows Registry Editor Version 5.00

            ; a comment
            [-HKCU\Software\Gone]

            [HKCU\Software\Keep]
            "Old"=-
            "New"="v" ; trailing comment
            @=dword:0000002a
            "Wrapped"=hex:01,02,\
              03
            """;
        var parsed = RegFileParser.Parse(text);
        Assert.Empty(parsed.Errors);
        Assert.Collection(parsed.Ops,
            o => Assert.Equal(new DeleteKeyOp(4, @"HKEY_CURRENT_USER\Software\Gone"), o),
            o => Assert.Equal(new CreateKeyOp(6, @"HKEY_CURRENT_USER\Software\Keep"), o),
            o => Assert.Equal(new DeleteValueOp(7, @"HKEY_CURRENT_USER\Software\Keep", "Old"), o),
            o => Assert.Equal("v", ValueFormat.DecodeString(Assert.IsType<SetValueOp>(o).Data)),
            o => Assert.Equal(42u, BitConverter.ToUInt32(Assert.IsType<SetValueOp>(o).Data)),
            o => Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<SetValueOp>(o).Data));
    }

    [Fact]
    public void Parser_Regedit4ConvertsAnsiHexStringsToUnicode()
    {
        const string text = "REGEDIT4\r\n\r\n[HKEY_CURRENT_USER\\X]\r\n\"E\"=hex(2):41,42,00\r\n\"M\"=hex(7):61,00,62,00,00\r\n";
        var parsed = RegFileParser.Parse(text);
        Assert.True(parsed.IsRegedit4);
        var ops = parsed.Ops.OfType<SetValueOp>().ToList();
        Assert.Equal("AB", ValueFormat.DecodeString(ops[0].Data));
        Assert.Equal(["a", "b"], ValueFormat.DecodeMultiString(ops[1].Data));
    }

    [Fact]
    public void Parser_RejectsNonRegistryText_AndReportsBadLines()
    {
        Assert.NotEmpty(RegFileParser.Parse("hello").Errors);
        var parsed = RegFileParser.Parse("Windows Registry Editor Version 5.00\r\n[HKXX\\Bad]\r\n[HKCU\\Ok]\r\n\"v\"=dword:xyz\r\n");
        Assert.Equal(2, parsed.Errors.Count);
        Assert.Single(parsed.Ops);
    }
}
