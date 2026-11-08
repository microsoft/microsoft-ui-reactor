using System.Globalization;
using System.Text;

namespace ReactorRegedit.Core;

/// <summary>
/// Writes keys in classic regedit's text format ("Export → Text files" and Print):
/// a "Key Name / Class Name / Last Write Time" header, then one block per value.
/// </summary>
public sealed class RegTextWriter(TextWriter output, CultureInfo? culture = null)
{
    private const string Indent = "                   "; // aligns continuation lines with the data column
    private readonly CultureInfo _culture = culture ?? CultureInfo.CurrentCulture;

    public void WriteKey(string path, DateTime? lastWriteLocal, IReadOnlyList<RegValue> values)
    {
        output.Write("Key Name:          "); output.Write(path); output.Write("\r\n");
        output.Write("Class Name:        <NO CLASS>\r\n");
        if (lastWriteLocal is { } t)
        {
            output.Write("Last Write Time:   ");
            output.Write(t.ToString("d", _culture)); output.Write(" - "); output.Write(t.ToString("t", _culture));
            output.Write("\r\n");
        }
        int index = 0;
        foreach (var v in values)
        {
            if (v.IsUnsetDefault) continue;
            output.Write("Value "); output.Write(index++.ToString(CultureInfo.InvariantCulture)); output.Write("\r\n");
            output.Write("  Name:            "); output.Write(v.Name.Length == 0 ? "<NO NAME>" : v.Name); output.Write("\r\n");
            output.Write("  Type:            "); output.Write(RegType.Name(v.Type)); output.Write("\r\n");
            output.Write("  Data:            "); output.Write(FormatData(v.Type, v.Data)); output.Write("\r\n");
            output.Write("\r\n");
        }
        output.Write("\r\n");
    }

    /// <summary>The "Data:" text; multi-line results use CRLF.</summary>
    public static string FormatData(uint type, byte[] data)
    {
        switch (type)
        {
            case RegType.Sz or RegType.ExpandSz:
                return ValueFormat.DecodeString(data);
            case RegType.MultiSz:
                return string.Join("\r\n" + Indent, ValueFormat.DecodeMultiString(data));
            case RegType.Dword or RegType.DwordBigEndian when data.Length == 4:
            case RegType.Qword when data.Length == 8:
                return "0x" + ValueFormat.ReadNumber(data, type).ToString("x", CultureInfo.InvariantCulture);
            default:
                return data.Length == 0 ? "" : "\r\n" + HexLines(data);
        }
    }

    /// <summary>"00000000  00 01 02 03 04 05 06 07 - 08 09 0a 0b 0c 0d 0e 0f   ................"</summary>
    public static string HexLines(byte[] data)
    {
        var sb = new StringBuilder((data.Length / 16 + 1) * 80);
        for (int off = 0; off < data.Length; off += 16)
        {
            if (off > 0) sb.Append("\r\n");
            sb.Append(off.ToString("x8", CultureInfo.InvariantCulture)).Append("  ");
            int n = Math.Min(16, data.Length - off);
            for (int i = 0; i < 16; i++)
            {
                if (i == 8) sb.Append(n > 8 ? "- " : "  ");
                if (i < n) sb.Append(data[off + i].ToString("x2", CultureInfo.InvariantCulture)).Append(' ');
                else sb.Append("   ");
            }
            sb.Append("  ");
            for (int i = 0; i < n; i++)
            {
                var b = data[off + i];
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
        }
        return sb.ToString();
    }
}
