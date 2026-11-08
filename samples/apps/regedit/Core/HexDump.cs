using System.Globalization;
using System.Text;

namespace ReactorRegedit.Core;

/// <summary>
/// Pure model of the binary editor: the hex column is canonical text — hex digit pairs
/// separated by spaces, <see cref="BytesPerLine"/> per line — derived from the digit
/// sequence alone, so typing/deleting anywhere reflows and the caret is tracked by
/// "digits before caret".
/// </summary>
public static class HexDump
{
    public const int BytesPerLine = 8;

    public static bool IsHexDigit(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    /// <summary>Hex digits of <paramref name="text"/> in order (uppercased); reports other non-space chars.</summary>
    public static string ExtractDigits(string text, out bool hadInvalid)
    {
        hadInvalid = false;
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (IsHexDigit(c)) sb.Append(char.ToUpperInvariant(c));
            else if (!char.IsWhiteSpace(c)) hadInvalid = true;
        }
        return sb.ToString();
    }

    /// <summary>Canonical hex-column text for a digit sequence (a trailing odd digit stays alone).</summary>
    public static string FormatDigits(string digits)
    {
        var sb = new StringBuilder(digits.Length * 2);
        int byteIndex = 0;
        for (int i = 0; i < digits.Length; i += 2, byteIndex++)
        {
            if (byteIndex > 0) sb.Append(byteIndex % BytesPerLine == 0 ? '\r' : ' ');
            sb.Append(digits, i, Math.Min(2, digits.Length - i));
        }
        return sb.ToString();
    }

    public static string FormatBytes(byte[] data) => FormatDigits(Convert.ToHexString(data));

    /// <summary>Bytes from a digit sequence; a trailing lone digit is the low nibble of a final byte.</summary>
    public static byte[] DigitsToBytes(string digits)
    {
        var result = new byte[(digits.Length + 1) / 2];
        for (int i = 0; i < result.Length; i++)
        {
            var pair = digits.Substring(i * 2, Math.Min(2, digits.Length - i * 2));
            result[i] = byte.Parse(pair, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }
        return result;
    }

    /// <summary>Number of hex digits before character index <paramref name="caret"/>.</summary>
    public static int DigitsBefore(string text, int caret)
    {
        int n = 0;
        for (int i = 0; i < Math.Min(caret, text.Length); i++) if (IsHexDigit(text[i])) n++;
        return n;
    }

    /// <summary>Character index in canonical <paramref name="text"/> just after the n-th digit.</summary>
    public static int CaretAfterDigits(string text, int digits)
    {
        if (digits <= 0) return 0;
        int seen = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (IsHexDigit(text[i]) && ++seen == digits) return i + 1;
        }
        return text.Length;
    }

    /// <summary>Offset column: one line per <see cref="BytesPerLine"/> bytes (4 hex digits, 8 past 64 KB).</summary>
    public static string Offsets(int byteCount)
    {
        int lines = Math.Max(1, (byteCount + BytesPerLine - 1) / BytesPerLine);
        var wide = byteCount > 0xFFFF;
        var sb = new StringBuilder(lines * 10);
        for (int i = 0; i < lines; i++)
        {
            if (i > 0) sb.Append('\r');
            var off = i * BytesPerLine;
            sb.Append(off.ToString(wide ? "X8" : "X4", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>ASCII column: printable bytes as-is, others as '.'.</summary>
    public static string Ascii(byte[] data)
    {
        var sb = new StringBuilder(data.Length + data.Length / BytesPerLine);
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0 && i % BytesPerLine == 0) sb.Append('\r');
            var b = data[i];
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }
        return sb.ToString();
    }
}
