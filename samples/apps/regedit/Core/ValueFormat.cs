using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace ReactorRegedit.Core;

/// <summary>
/// Pure conversions between raw registry bytes and the text regedit shows / edits.
/// </summary>
public static class ValueFormat
{
    /// <summary>Byte cap for the Data column so huge binaries don't build megabyte strings.</summary>
    public const int MaxDisplayBytes = 2048;

    public static string Display(uint type, byte[] data)
    {
        switch (type)
        {
            case RegType.Sz:
            case RegType.ExpandSz:
                return DecodeString(data);
            case RegType.MultiSz:
                return string.Join(" ", DecodeMultiString(data));
            case RegType.Dword:
                return data.Length == 4
                    ? FormatDword(BinaryPrimitives.ReadUInt32LittleEndian(data))
                    : "(invalid DWORD (32-bit) value)";
            case RegType.DwordBigEndian:
                return data.Length == 4
                    ? FormatDword(BinaryPrimitives.ReadUInt32BigEndian(data))
                    : "(invalid DWORD (32-bit) value)";
            case RegType.Qword:
                return data.Length == 8
                    ? FormatQword(BinaryPrimitives.ReadUInt64LittleEndian(data))
                    : "(invalid QWORD (64-bit) value)";
            default:
                return FormatBinary(data);
        }
    }

    public static string FormatDword(uint value) => $"0x{value:x8} ({value})";

    public static string FormatQword(ulong value) => $"0x{value:x16} ({value})";

    /// <summary>Space-separated lowercase hex bytes, like regedit's Data column.</summary>
    public static string FormatBinary(byte[] data)
    {
        if (data.Length == 0) return "(zero-length binary value)";
        var count = Math.Min(data.Length, MaxDisplayBytes);
        var sb = new StringBuilder(count * 3);
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[i].ToString("x2", CultureInfo.InvariantCulture));
        }
        if (data.Length > count) sb.Append(" ...");
        return sb.ToString();
    }

    /// <summary>UTF-16LE decode up to the first NUL (a trailing odd byte is ignored).</summary>
    public static string DecodeString(byte[] data)
    {
        var s = Encoding.Unicode.GetString(data, 0, data.Length & ~1);
        var nul = s.IndexOf('\0');
        return nul >= 0 ? s[..nul] : s;
    }

    /// <summary>UTF-16LE with a single NUL terminator, as RegSetValueEx expects for REG_SZ.</summary>
    public static byte[] EncodeString(string value) => Encoding.Unicode.GetBytes(value + "\0");

    /// <summary>
    /// Splits REG_MULTI_SZ data. All trailing NULs are terminators; NULs inside are
    /// separators, so embedded empty strings survive ("a\0\0b\0\0" → a, "", b).
    /// </summary>
    public static string[] DecodeMultiString(byte[] data)
    {
        var s = Encoding.Unicode.GetString(data, 0, data.Length & ~1).TrimEnd('\0');
        return s.Length == 0 ? [] : s.Split('\0');
    }

    /// <summary>Each string NUL-terminated plus a final NUL. Empty list encodes as a single NUL.</summary>
    public static byte[] EncodeMultiString(IReadOnlyList<string> values)
    {
        if (values.Count == 0) return [0, 0];
        var sb = new StringBuilder();
        foreach (var v in values) sb.Append(v).Append('\0');
        sb.Append('\0');
        return Encoding.Unicode.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Splits the multi-string editor's text into strings (CRLF/LF separated). Empty
    /// lines are removed, like regedit, and <paramref name="removedEmpty"/> reports it.
    /// </summary>
    public static string[] ParseMultiStringEditor(string text, out bool removedEmpty)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        // A single trailing newline is just the end of the last line, not an empty string.
        var count = lines.Length;
        if (count > 0 && lines[count - 1].Length == 0) count--;
        var result = new List<string>(count);
        removedEmpty = false;
        for (int i = 0; i < count; i++)
        {
            if (lines[i].Length == 0) { removedEmpty = true; continue; }
            result.Add(lines[i]);
        }
        return result.ToArray();
    }

    /// <summary>Parses DWORD editor text in the given base. Rejects overflow and junk.</summary>
    public static bool TryParseDword(string text, bool hex, out uint value)
    {
        text = text.Trim();
        if (hex)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            if (text.Length == 0) { value = 0; return true; }
            return uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }
        if (text.Length == 0) { value = 0; return true; }
        return uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseQword(string text, bool hex, out ulong value)
    {
        text = text.Trim();
        if (hex)
        {
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            if (text.Length == 0) { value = 0; return true; }
            return ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }
        if (text.Length == 0) { value = 0; return true; }
        return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Reads a DWORD/QWORD-ish value from raw bytes (zero-extended, truncated to 8 bytes).</summary>
    public static ulong ReadNumber(byte[] data, uint type)
    {
        if (type == RegType.DwordBigEndian && data.Length == 4) return BinaryPrimitives.ReadUInt32BigEndian(data);
        Span<byte> buf = stackalloc byte[8];
        data.AsSpan(0, Math.Min(8, data.Length)).CopyTo(buf);
        return BinaryPrimitives.ReadUInt64LittleEndian(buf);
    }
}
