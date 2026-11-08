using System.Globalization;
using System.Text;

namespace ReactorRegedit.Core;

/// <summary>
/// Writes .reg text exactly like regedit's "Windows Registry Editor Version 5.00"
/// export: CRLF line endings, <c>@</c> for the default value, escaped quoted strings,
/// <c>dword:</c>, <c>hex:</c> / <c>hex(n):</c> with regedit's 80-column wrapping.
/// </summary>
public sealed class RegFileWriter
{
    public const string Header5 = "Windows Registry Editor Version 5.00";
    public const string Header4 = "REGEDIT4";

    /// <summary>regedit wraps hex data once the line reaches this column.</summary>
    private const int HexLineLength = 77;

    private readonly TextWriter _out;

    /// <summary>Writes to <paramref name="output"/>, or to an in-memory buffer read through <see cref="Text"/>.</summary>
    public RegFileWriter(TextWriter? output = null)
    {
        _out = output ?? new StringWriter(CultureInfo.InvariantCulture);
        _out.Write(Header5);
        _out.Write("\r\n\r\n");
    }

    /// <summary>The text written so far (in-memory mode only).</summary>
    public string Text => _out is StringWriter sw ? sw.ToString() : throw new InvalidOperationException("Writer is streaming to an external output.");

    /// <summary>Writes one key section (header line, values, trailing blank line).</summary>
    public void WriteKey(string path, IEnumerable<RegValue> values)
    {
        _out.Write('[');
        _out.Write(path);
        _out.Write("]\r\n");
        foreach (var v in values)
        {
            if (v.IsUnsetDefault) continue;
            _out.Write(FormatValueLine(v));
            _out.Write("\r\n");
        }
        _out.Write("\r\n");
    }

    public static string FormatName(string name) => name.Length == 0 ? "@" : "\"" + Escape(name) + "\"";

    public static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>One value line (may contain wrapped continuation lines).</summary>
    public static string FormatValueLine(RegValue v)
    {
        var prefix = FormatName(v.Name) + "=";
        switch (v.Type)
        {
            case RegType.Sz when IsPlainString(v.Data, out var s):
                return prefix + "\"" + Escape(s) + "\"";
            case RegType.Dword when v.Data.Length == 4:
                return prefix + "dword:" + BitConverter.ToUInt32(v.Data, 0).ToString("x8", CultureInfo.InvariantCulture);
            default:
                var tag = v.Type == RegType.Binary ? "hex:" : $"hex({v.Type.ToString("x", CultureInfo.InvariantCulture)}):";
                return FormatHex(prefix + tag, v.Data);
        }
    }

    /// <summary>
    /// True when REG_SZ data is a clean NUL-terminated string that survives a quoted
    /// round-trip (otherwise it is exported as hex(1) so no byte is lost).
    /// </summary>
    private static bool IsPlainString(byte[] data, out string value)
    {
        value = "";
        if (data.Length == 0) return true;
        if (data.Length % 2 != 0) return false;
        var s = Encoding.Unicode.GetString(data);
        if (!s.EndsWith('\0')) return false;
        s = s[..^1];
        if (s.Contains('\0') || s.Contains('\r') || s.Contains('\n')) return false;
        value = s;
        return true;
    }

    private static string FormatHex(string linePrefix, byte[] data)
    {
        var sb = new StringBuilder(linePrefix.Length + data.Length * 3 + data.Length / 25 * 5);
        sb.Append(linePrefix);
        int column = linePrefix.Length;
        for (int i = 0; i < data.Length; i++)
        {
            sb.Append(data[i].ToString("x2", CultureInfo.InvariantCulture));
            if (i == data.Length - 1) break;
            sb.Append(',');
            column += 3;
            if (column >= HexLineLength)
            {
                sb.Append("\\\r\n  ");
                column = 2;
            }
        }
        return sb.ToString();
    }
}

/// <summary>One change described by a .reg file.</summary>
public abstract record RegFileOp(int Line, string KeyPath);
public sealed record CreateKeyOp(int Line, string KeyPath) : RegFileOp(Line, KeyPath);
public sealed record DeleteKeyOp(int Line, string KeyPath) : RegFileOp(Line, KeyPath);
public sealed record SetValueOp(int Line, string KeyPath, string Name, uint Type, byte[] Data) : RegFileOp(Line, KeyPath);
public sealed record DeleteValueOp(int Line, string KeyPath, string Name) : RegFileOp(Line, KeyPath);

/// <summary>Result of parsing a .reg file: operations plus per-line syntax errors.</summary>
public sealed record RegFileParseResult(IReadOnlyList<RegFileOp> Ops, IReadOnlyList<string> Errors, bool IsRegedit4);

/// <summary>
/// Parses .reg text (REGEDIT4 or Version 5.00): comments, line continuations,
/// <c>[-Key]</c> and <c>"Name"=-</c> deletions, <c>@</c>, escaped strings,
/// <c>dword:</c> and every <c>hex(n):</c>. Hive abbreviations are accepted.
/// </summary>
public static class RegFileParser
{
    /// <summary>Decodes raw .reg file bytes (UTF-16 with BOM, UTF-8 with BOM, else ANSI).</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Latin1OrAnsi(bytes);
    }

    private static string Latin1OrAnsi(byte[] bytes)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    public static RegFileParseResult Parse(string text)
    {
        var ops = new List<RegFileOp>();
        var errors = new List<string>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        int i = 0;
        while (i < lines.Length && lines[i].Trim().Length == 0) i++;
        if (i >= lines.Length)
        {
            errors.Add("The file is empty.");
            return new RegFileParseResult(ops, errors, false);
        }
        var header = lines[i].Trim().TrimStart('\uFEFF');
        bool regedit4;
        if (header == RegFileWriter.Header5) regedit4 = false;
        else if (header == RegFileWriter.Header4) regedit4 = true;
        else
        {
            errors.Add("The specified file is not a registry script. You can only import binary registry files from within the registry editor.");
            return new RegFileParseResult(ops, errors, false);
        }
        i++;

        string? currentKey = null;
        bool keyDeleted = false;
        for (; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            var line = lines[i].TrimStart();
            if (line.Length == 0 || line[0] == ';') continue;

            if (line[0] == '[')
            {
                var end = line.LastIndexOf(']');
                if (end < 0) { errors.Add($"Line {lineNo}: missing ']'."); currentKey = null; continue; }
                var spec = line[1..end];
                var delete = spec.StartsWith('-');
                if (delete) spec = spec[1..];
                var path = RegPath.Normalize(spec);
                if (path is null || path.Length == 0 || RegPath.IsRemote(path))
                {
                    errors.Add($"Line {lineNo}: invalid key name \"{spec}\".");
                    currentKey = null;
                    continue;
                }
                if (delete)
                {
                    ops.Add(new DeleteKeyOp(lineNo, path));
                    currentKey = path;
                    keyDeleted = true;
                }
                else
                {
                    ops.Add(new CreateKeyOp(lineNo, path));
                    currentKey = path;
                    keyDeleted = false;
                }
                continue;
            }

            // Value line; join backslash continuations (hex data only, like regedit).
            var full = line.TrimEnd();
            while (full.EndsWith('\\') && IsHexContinuation(full) && i + 1 < lines.Length)
            {
                full = full[..^1] + lines[++i].Trim();
            }

            if (currentKey is null || keyDeleted)
            {
                if (currentKey is null) errors.Add($"Line {lineNo}: value outside of a key section.");
                continue;
            }

            if (!TryParseValueLine(full, regedit4, out var name, out var op, out var error))
            {
                errors.Add($"Line {lineNo}: {error}");
                continue;
            }
            ops.Add(op switch
            {
                null => new DeleteValueOp(lineNo, currentKey, name),
                var (type, data) => new SetValueOp(lineNo, currentKey, name, type, data),
            });
        }
        return new RegFileParseResult(ops, errors, regedit4);
    }

    private static bool IsHexContinuation(string line)
    {
        var eq = FindValueSeparator(line);
        if (eq < 0) return false;
        var rest = line[(eq + 1)..].TrimStart();
        return rest.StartsWith("hex", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Index of the '=' after the (possibly quoted) value name, or -1.</summary>
    private static int FindValueSeparator(string line)
    {
        if (line.StartsWith('@'))
        {
            var j = 1;
            while (j < line.Length && char.IsWhiteSpace(line[j])) j++;
            return j < line.Length && line[j] == '=' ? j : -1;
        }
        if (!line.StartsWith('"')) return -1;
        for (int k = 1; k < line.Length; k++)
        {
            if (line[k] == '\\') { k++; continue; }
            if (line[k] == '"')
            {
                var j = k + 1;
                while (j < line.Length && char.IsWhiteSpace(line[j])) j++;
                return j < line.Length && line[j] == '=' ? j : -1;
            }
        }
        return -1;
    }

    private static bool TryParseValueLine(string line, bool regedit4, out string name, out (uint Type, byte[] Data)? op, out string error)
    {
        name = "";
        op = null;
        error = "";
        var eq = FindValueSeparator(line);
        if (eq < 0) { error = "expected \"name\"=value."; return false; }
        var nameToken = line[..eq].TrimEnd();
        if (nameToken != "@")
        {
            if (!TryUnquote(nameToken, out name)) { error = "invalid value name."; return false; }
        }
        var data = line[(eq + 1)..].Trim();
        if (data == "-") { op = null; return true; }

        if (data.StartsWith('"'))
        {
            if (!TryUnquote(StripTrailingComment(data), out var s)) { error = "unterminated string."; return false; }
            op = (RegType.Sz, ValueFormat.EncodeString(s));
            return true;
        }
        if (data.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
        {
            var hex = StripTrailingComment(data[6..]).Trim();
            if (hex.Length is 0 or > 8 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var dw))
            { error = "invalid dword value."; return false; }
            op = (RegType.Dword, BitConverter.GetBytes(dw));
            return true;
        }
        if (data.StartsWith("hex", StringComparison.OrdinalIgnoreCase))
        {
            var colon = data.IndexOf(':');
            if (colon < 0) { error = "invalid hex value."; return false; }
            var tag = data[3..colon].Trim();
            uint type = RegType.Binary;
            if (tag.Length > 0)
            {
                if (!(tag.StartsWith('(') && tag.EndsWith(')')) ||
                    !uint.TryParse(tag[1..^1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out type))
                { error = "invalid hex type."; return false; }
            }
            if (!TryParseHexBytes(StripTrailingComment(data[(colon + 1)..]), out var bytes))
            { error = "invalid hex data."; return false; }
            if (regedit4 && type is RegType.ExpandSz or RegType.MultiSz)
                bytes = AnsiToUnicode(bytes);
            op = (type, bytes);
            return true;
        }
        error = "unrecognized value data.";
        return false;
    }

    private static string StripTrailingComment(string s)
    {
        // A ';' after the data starts a comment, but not inside a quoted string.
        bool inQuote = false;
        for (int k = 0; k < s.Length; k++)
        {
            if (s[k] == '\\' && inQuote) { k++; continue; }
            if (s[k] == '"') inQuote = !inQuote;
            else if (s[k] == ';' && !inQuote) return s[..k].TrimEnd();
        }
        return s.TrimEnd();
    }

    private static bool TryUnquote(string token, out string value)
    {
        value = "";
        token = token.Trim();
        if (token.Length < 2 || token[0] != '"' || token[^1] != '"') return false;
        var sb = new StringBuilder(token.Length);
        for (int k = 1; k < token.Length - 1; k++)
        {
            var c = token[k];
            if (c == '\\' && k + 1 < token.Length - 1)
            {
                var n = token[++k];
                sb.Append(n);
            }
            else sb.Append(c);
        }
        value = sb.ToString();
        return true;
    }

    private static bool TryParseHexBytes(string text, out byte[] bytes)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        var list = new List<byte>(parts.Length);
        foreach (var p in parts)
        {
            if (p.Length == 0) continue;
            if (p.Length > 2 || !byte.TryParse(p, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b))
            {
                bytes = [];
                return false;
            }
            list.Add(b);
        }
        bytes = list.ToArray();
        return true;
    }

    /// <summary>REGEDIT4 stores hex(2)/hex(7) as ANSI bytes; registry wants UTF-16.</summary>
    private static byte[] AnsiToUnicode(byte[] ansi) => Encoding.Unicode.GetBytes(Latin1OrAnsi(ansi));
}
