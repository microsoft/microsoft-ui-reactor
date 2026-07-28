using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace WinUIGalleryReactor;

/// <summary>
/// A theme color palette for a code block. Values are hex color strings.
/// </summary>
internal readonly record struct CodePalette(
    string Background,
    string PlainText,
    string Comment,
    string Keyword,
    string Str,
    string Number,
    string GutterBackground,
    string LineNumber);

/// <summary>
/// A small C# syntax highlighter that reproduces the same coloring rules the
/// official WinUI Gallery uses for C# code — i.e. the <c>ColorCode</c> library's
/// C# grammar and its Default Light / Default Dark themes.
///
/// <para>ColorCode's C# grammar is intentionally minimal: it colors comments,
/// string / char literals, a fixed keyword set, and integer literals. Type names,
/// method calls and member accesses are deliberately left as plain text (the
/// grammar's class-name rule is disabled upstream). All keywords — including
/// control-flow ones — share a single color. This highlighter mirrors those
/// exact rules, palette values, and light/dark split; the line-number gutter is
/// the only local addition.</para>
/// </summary>
internal static class CodeHighlighter
{
    internal const string CodeFontFamily = "Cascadia Code, Cascadia Mono, Consolas";
    internal const double CodeFontSize = 13;
    internal const double CodeLineHeight = 20;

    // ColorCode "Default Dark" (VS dark). Gutter colors are a local addition.
    internal static readonly CodePalette Dark = new(
        Background: "#1E1E1E",       // VSDarkBackground
        PlainText:  "#DADADA",       // VSDarkPlainText
        Comment:    "#57A64A",       // VSDarkComment
        Keyword:    "#569CD6",       // VSDarkKeyword
        Str:        "#D69D85",       // VSDarkString
        Number:     "#B5CEA8",       // VSDarkNumber
        GutterBackground: "#252526",
        LineNumber: "#858585");

    // ColorCode "Default Light" (VS light). ColorCode does not assign the Number
    // scope a color in its light theme, so numbers fall back to plain text.
    internal static readonly CodePalette Light = new(
        Background: "#FFFFFF",        // White
        PlainText:  "#000000",        // Black
        Comment:    "#008000",        // Green
        Keyword:    "#0000FF",        // Blue
        Str:        "#A31515",        // DullRed
        Number:     "#000000",        // (uncolored -> plain text)
        GutterBackground: "#F3F3F3",
        LineNumber: "#9AA0A6");

    enum Kind { Default, Keyword, Str, Comment, Number, Whitespace }

    readonly record struct Tok(string Text, Kind Kind);

    // ColorCode's exact C# keyword list (ColorCode.Core CSharp grammar). All of
    // these map to the single Keyword color — ColorCode does not split out a
    // separate control-flow keyword color for C#.
    static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "ascending", "base", "bool", "break", "by", "byte", "case",
        "catch", "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "descending", "do", "double", "dynamic", "else", "enum", "equals",
        "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "from", "get", "goto", "group", "if", "implicit", "in", "int",
        "into", "interface", "internal", "is", "join", "let", "lock", "long",
        "namespace", "new", "null", "object", "on", "operator", "orderby", "out",
        "override", "params", "partial", "private", "protected", "public", "readonly",
        "ref", "return", "sbyte", "sealed", "select", "set", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "var", "virtual", "void", "volatile", "where", "while", "yield", "async",
        "await", "warning", "disable",
    };

    /// <summary>
    /// Lexes <paramref name="code"/> into one <see cref="RichTextParagraph"/> per
    /// line, colored per <paramref name="palette"/>. <paramref name="lineCount"/>
    /// is the number of lines produced (used to build a matching gutter).
    /// </summary>
    public static RichTextParagraph[] Highlight(string? code, in CodePalette palette, out int lineCount)
    {
        var lines = Tokenize(code ?? string.Empty);
        var paragraphs = new RichTextParagraph[lines.Count];
        for (int li = 0; li < lines.Count; li++)
        {
            var toks = lines[li];
            RichTextInline[] inlines;
            if (toks.Count == 0)
            {
                // Preserve the height of blank lines so the gutter stays aligned.
                inlines = new RichTextInline[] { Run(" ") };
            }
            else
            {
                inlines = new RichTextInline[toks.Count];
                for (int t = 0; t < toks.Count; t++)
                {
                    var tk = toks[t];
                    // Whitespace is invisible, so it inherits the block foreground.
                    inlines[t] = tk.Kind == Kind.Whitespace
                        ? Run(tk.Text)
                        : Run(tk.Text).Foreground(BrushFor(ColorFor(tk.Kind, palette)));
                }
            }
            paragraphs[li] = Paragraph(inlines);
        }

        lineCount = lines.Count;
        return paragraphs;
    }

    /// <summary>
    /// Builds the right-aligned line-number gutter for <paramref name="lineCount"/>
    /// lines. Numbers are left-padded (monospace) so they align on the right.
    /// </summary>
    public static RichTextParagraph[] Gutter(int lineCount, in CodePalette palette)
    {
        int width = lineCount.ToString(global::System.Globalization.CultureInfo.InvariantCulture).Length;
        var paragraphs = new RichTextParagraph[Math.Max(lineCount, 0)];
        for (int i = 0; i < lineCount; i++)
        {
            string num = (i + 1)
                .ToString(global::System.Globalization.CultureInfo.InvariantCulture)
                .PadLeft(width);
            paragraphs[i] = Paragraph(Run(num).Foreground(BrushFor(palette.LineNumber)));
        }
        return paragraphs;
    }

    static string ColorFor(Kind kind, in CodePalette p) => kind switch
    {
        Kind.Keyword => p.Keyword,
        Kind.Str     => p.Str,
        Kind.Comment => p.Comment,
        Kind.Number  => p.Number,
        _            => p.PlainText,
    };

    // Cache one brush per color string. The palette uses a small fixed set of
    // colors, so a highlighted panel reuses ~7 brushes instead of allocating a
    // new SolidColorBrush per token (which .Foreground(string) / BrushHelper.Parse
    // would do). ConcurrentDictionary keeps the shared static cache safe even
    // though it is only expected to be touched on the UI thread during Render.
    static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, Microsoft.UI.Xaml.Media.SolidColorBrush> BrushCache = new();

    static Microsoft.UI.Xaml.Media.SolidColorBrush BrushFor(string hex)
        => BrushCache.GetOrAdd(hex, static h => new Microsoft.UI.Xaml.Media.SolidColorBrush(ParseHexColor(h)));

    static global::Windows.UI.Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 0xFF;
        int o = 0;
        if (hex.Length == 8) { a = Convert.ToByte(hex.Substring(0, 2), 16); o = 2; }
        byte r = Convert.ToByte(hex.Substring(o, 2), 16);
        byte g = Convert.ToByte(hex.Substring(o + 2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(o + 4, 2), 16);
        return global::Windows.UI.Color.FromArgb(a, r, g, b);
    }

    static List<List<Tok>> Tokenize(string src)
    {
        // Normalize line endings only. Tabs are preserved verbatim so the
        // rendered/selectable text matches the original source (and the Copy
        // button); they are treated as whitespace by the lexer below.
        src = src.Replace("\r\n", "\n").Replace("\r", "\n");

        var lines = new List<List<Tok>>();
        var current = new List<Tok>();
        lines.Add(current);

        void Emit(string text, Kind kind) => current.Add(new Tok(text, kind));
        void NewLine()
        {
            current = new List<Tok>();
            lines.Add(current);
        }

        int i = 0;
        int n = src.Length;
        while (i < n)
        {
            char c = src[i];

            if (c == '\n') { i++; NewLine(); continue; }

            if (c == ' ' || c == '\t')
            {
                int s = i;
                while (i < n && (src[i] == ' ' || src[i] == '\t')) i++;
                Emit(src.Substring(s, i - s), Kind.Whitespace);
                continue;
            }

            // Line comment (also covers /// doc comments).
            if (c == '/' && i + 1 < n && src[i + 1] == '/')
            {
                int s = i;
                while (i < n && src[i] != '\n') i++;
                Emit(src.Substring(s, i - s), Kind.Comment);
                continue;
            }

            // Block comment (may span lines).
            if (c == '/' && i + 1 < n && src[i + 1] == '*')
            {
                var sb = new StringBuilder("/*");
                i += 2;
                while (i < n)
                {
                    if (src[i] == '\n') { Emit(sb.ToString(), Kind.Comment); sb.Clear(); NewLine(); i++; continue; }
                    if (src[i] == '*' && i + 1 < n && src[i + 1] == '/') { sb.Append("*/"); i += 2; break; }
                    sb.Append(src[i]); i++;
                }
                if (sb.Length > 0) Emit(sb.ToString(), Kind.Comment);
                continue;
            }

            // Char literal, verbatim string, or regular string. ColorCode has no
            // rule for the '$' of an interpolated string, so it is left plain and
            // only the quoted part is colored (handled by falling through to the
            // '"' case on the next iteration).
            if (c == '\'' || c == '"' || (c == '@' && i + 1 < n && src[i + 1] == '"'))
            {
                ScanString(src, ref i, Emit, NewLine);
                continue;
            }

            // Numbers: ColorCode colors only integer digit runs (\b[0-9]+\b).
            if (char.IsDigit(c))
            {
                int s = i;
                while (i < n && char.IsDigit(src[i])) i++;
                // Require a word boundary on BOTH sides (mirrors \b[0-9]+\b): a
                // letter/digit/underscore on either edge means the digits belong
                // to a larger token (e.g. 0xFF, 200f, 1_000) and are not colored.
                bool leadingBoundary = s == 0 || !(char.IsLetterOrDigit(src[s - 1]) || src[s - 1] == '_');
                bool trailingBoundary = i >= n || !(char.IsLetterOrDigit(src[i]) || src[i] == '_');
                Emit(src.Substring(s, i - s), leadingBoundary && trailingBoundary ? Kind.Number : Kind.Default);
                continue;
            }

            // Identifiers / keywords.
            if (char.IsLetter(c) || c == '_')
            {
                int s = i;
                while (i < n && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                string word = src.Substring(s, i - s);
                Emit(word, Keywords.Contains(word) ? Kind.Keyword : Kind.Default);
                continue;
            }

            // Punctuation / operators (including '$', '.', etc.) — plain text.
            Emit(c.ToString(), Kind.Default);
            i++;
        }

        return lines;
    }

    static void ScanString(string src, ref int i, Action<string, Kind> emit, Action newLine)
    {
        int n = src.Length;
        var sb = new StringBuilder();

        bool verbatim = false;
        if (src[i] == '@') { verbatim = true; sb.Append('@'); i++; }

        char quote = src[i]; // '"' or '\''
        sb.Append(quote);
        i++;

        while (i < n)
        {
            char c = src[i];

            if (c == '\n')
            {
                if (verbatim)
                {
                    // Verbatim strings can span lines.
                    emit(sb.ToString(), Kind.Str);
                    sb.Clear();
                    newLine();
                    i++;
                    continue;
                }
                break; // unterminated on this line — stop before the newline
            }

            if (!verbatim && c == '\\' && i + 1 < n)
            {
                sb.Append(c);
                sb.Append(src[i + 1]);
                i += 2;
                continue;
            }

            if (verbatim && c == quote && i + 1 < n && src[i + 1] == quote)
            {
                sb.Append(quote);
                sb.Append(quote);
                i += 2;
                continue;
            }

            if (c == quote)
            {
                sb.Append(quote);
                i++;
                break;
            }

            sb.Append(c);
            i++;
        }

        emit(sb.ToString(), Kind.Str);
    }
}
