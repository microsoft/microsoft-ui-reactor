// C# port of Martin Mitáš's md4c HTML renderer.
// Ported from md4c/src/md4c-html.h, md4c/src/md4c-html.c

using System.Text;

namespace Microsoft.UI.Reactor.Markdown;

/// <summary>
/// Renders Markdown to HTML using the md4c parser.
/// This is primarily used for testing against the CommonMark spec.
/// </summary>
public static class MarkdownHtml
{
    [Flags]
    public enum HtmlFlags : uint
    {
        None = 0,
        Debug = 0x0001,
        VerbatimEntities = 0x0002,
        SkipUtf8Bom = 0x0004,
        Xhtml = 0x0008,
        /// <summary>
        /// Disable the URL scheme allowlist + raw-HTML stripping. Caller
        /// opts out of the default safe-mode rendering. TASK-045 / TASK-046.
        /// </summary>
        AllowUnsafeUrls = 0x0010,
        AllowRawHtml = 0x0020,
    }

    /// <summary>
    /// URL schemes the safe-mode renderer leaves intact in <c>href</c> /
    /// <c>src</c> attributes. Anything else gets rewritten to
    /// <c>about:blank</c>. TASK-045.
    /// </summary>
    private static readonly HashSet<string> SafeUrlSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto",
    };

    /// <summary>
    /// Render Markdown to HTML. Returns -1 on error, 0 on success.
    /// </summary>
    public static int Render(string input, MarkdownParserFlags parserFlags, HtmlFlags renderFlags, StringBuilder output)
    {
        var r = new HtmlRenderer(output, renderFlags);

        // Skip UTF-8 BOM (in C# this would be the BOM character U+FEFF at start)
        string text = input;
        if ((renderFlags & HtmlFlags.SkipUtf8Bom) != 0 && text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];

        return Md4cParser.Parse(
            text,
            parserFlags,
            r.EnterBlock,
            r.LeaveBlock,
            r.EnterSpan,
            r.LeaveSpan,
            r.TextCallback,
            (renderFlags & HtmlFlags.Debug) != 0 ? (msg => global::System.Diagnostics.Debug.WriteLine($"MD4C: {msg}")) : null);
    }

    /// <summary>
    /// Convenience: render Markdown string to HTML string. Defaults to
    /// safe-mode (raw HTML stripped, unknown URL schemes neutralized).
    /// Pass <see cref="HtmlFlags.AllowRawHtml"/> / <see cref="HtmlFlags.AllowUnsafeUrls"/>
    /// to opt back into the prior behavior. TASK-045 / TASK-046.
    /// </summary>
    public static string ToHtml(string markdown, MarkdownParserFlags parserFlags = MarkdownParserFlags.None, HtmlFlags renderFlags = HtmlFlags.None)
    {
        // SECURITY (TASK-046): default to NoHtml unless the caller has
        // explicitly opted in via AllowRawHtml. The previous default emitted
        // raw <script>/event-handler attributes verbatim into a WebView.
        if ((renderFlags & HtmlFlags.AllowRawHtml) == 0)
            parserFlags |= MarkdownParserFlags.NoHtml;

        var sb = new StringBuilder();
        int ret = Render(markdown, parserFlags, renderFlags, sb);
        if (ret != 0)
            throw new InvalidOperationException($"md4c parse failed with code {ret}");
        return sb.ToString();
    }

    /// <summary>
    /// Returns either <paramref name="url"/> or <c>about:blank</c> after
    /// scheme-allowlist evaluation. TASK-045.
    /// </summary>
    internal static string SanitizeUrl(string url, bool unsafeAllowed)
    {
        if (unsafeAllowed) return url;
        if (string.IsNullOrEmpty(url)) return url;
        // Relative URLs (no scheme) are safe; only absolute schemes need
        // checking. Pull the scheme by hand because Uri.TryCreate is too
        // lenient — it would parse `javascript:alert(1)` as a valid Uri.
        int colon = url.IndexOf(':');
        if (colon <= 0) return url; // no scheme = treat as path-relative
        // Schemes per RFC 3986: ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )
        for (int i = 0; i < colon; i++)
        {
            var c = url[i];
            bool ok = (i == 0)
                ? (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                : (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9') || c == '+' || c == '-' || c == '.';
            if (!ok) return url; // not a real scheme — treat as relative
        }
        var scheme = url[..colon];
        return SafeUrlSchemes.Contains(scheme) ? url : "about:blank";
    }

    private sealed class HtmlRenderer
    {
        private readonly StringBuilder output;
        private readonly HtmlFlags flags;
        private int imageNestingLevel;
        private readonly bool[] escapeMap = new bool[256];

        private const int NEED_HTML_ESC = 1;
        private const int NEED_URL_ESC = 2;
        private readonly byte[] escapeFlags = new byte[256];

        public HtmlRenderer(StringBuilder output, HtmlFlags flags)
        {
            this.output = output;
            this.flags = flags;

            for (int i = 0; i < 256; i++)
            {
                char ch = (char)i;
                if (ch == '"' || ch == '&' || ch == '<' || ch == '>')
                    escapeFlags[i] |= NEED_HTML_ESC;
                if (!IsAlNum(ch) && "~-_.+!*(),%#@?=;:/,+$".IndexOf(ch) < 0)
                    escapeFlags[i] |= NEED_URL_ESC;
            }
        }

        private static bool IsAlNum(char ch) =>
            (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');

        private void Verbatim(string s) => output.Append(s);
        private void Verbatim(ReadOnlySpan<char> s) => output.Append(s);

        private void HtmlEscaped(ReadOnlySpan<char> data)
        {
            int beg = 0;
            for (int i = 0; i < data.Length; i++)
            {
                char ch = data[i];
                if (ch < 256 && (escapeFlags[ch] & NEED_HTML_ESC) != 0)
                {
                    if (i > beg)
                        output.Append(data[beg..i]);
                    switch (ch)
                    {
                        case '&': Verbatim("&amp;"); break;
                        case '<': Verbatim("&lt;"); break;
                        case '>': Verbatim("&gt;"); break;
                        case '"': Verbatim("&quot;"); break;
                    }
                    beg = i + 1;
                }
            }
            if (beg < data.Length)
                output.Append(data[beg..]);
        }

        private void UrlEscaped(ReadOnlySpan<char> data)
        {
            Span<byte> utf8Buf = stackalloc byte[4];
            int beg = 0;
            for (int i = 0; i < data.Length; i++)
            {
                char ch = data[i];
                if (ch >= 128)
                {
                    // Non-ASCII: flush pending, then UTF-8 percent-encode.
                    if (i > beg)
                        output.Append(data[beg..i]);

                    // Handle surrogate pairs.
                    int charCount = 1;
                    if (char.IsHighSurrogate(ch) && i + 1 < data.Length && char.IsLowSurrogate(data[i + 1]))
                        charCount = 2;

                    int bytesWritten = Encoding.UTF8.GetBytes(data.Slice(i, charCount), utf8Buf);
                    for (int b = 0; b < bytesWritten; b++)
                    {
                        output.Append('%');
                        output.Append(HexDigit(utf8Buf[b] >> 4));
                        output.Append(HexDigit(utf8Buf[b] & 0xF));
                    }

                    i += charCount - 1;
                    beg = i + 1;
                }
                else if ((escapeFlags[ch] & NEED_URL_ESC) != 0)
                {
                    if (i > beg)
                        output.Append(data[beg..i]);
                    if (ch == '&')
                        Verbatim("&amp;");
                    else
                    {
                        output.Append('%');
                        output.Append(HexDigit(ch >> 4));
                        output.Append(HexDigit(ch & 0xF));
                    }
                    beg = i + 1;
                }
            }
            if (beg < data.Length)
                output.Append(data[beg..]);
        }

        private static char HexDigit(int value) =>
            (char)(value < 10 ? '0' + value : 'A' + value - 10);

        private static uint HexVal(char ch)
        {
            if (ch >= '0' && ch <= '9') return (uint)(ch - '0');
            if (ch >= 'A' && ch <= 'Z') return (uint)(ch - 'A' + 10);
            return (uint)(ch - 'a' + 10);
        }

        private void AppendUtf8Codepoint(uint codepoint, bool htmlEsc, bool urlEsc = false)
        {
            if (codepoint == 0 || codepoint > 0x10ffff)
                codepoint = 0xFFFD;

            Span<char> chars = stackalloc char[2];
            int charCount;
            if (codepoint <= 0xFFFF)
            {
                chars[0] = (char)codepoint;
                charCount = 1;
            }
            else
            {
                codepoint -= 0x10000;
                chars[0] = (char)(0xD800 + (codepoint >> 10));
                chars[1] = (char)(0xDC00 + (codepoint & 0x3FF));
                charCount = 2;
            }

            ReadOnlySpan<char> span = chars[..charCount];
            if (urlEsc)
                UrlEscaped(span);
            else if (htmlEsc)
                HtmlEscaped(span);
            else
                output.Append(span);
        }

        private void RenderEntity(ReadOnlySpan<char> text, bool htmlEsc, bool urlEsc = false)
        {
            if ((flags & HtmlFlags.VerbatimEntities) != 0)
            {
                Verbatim(text);
                return;
            }

            if (text.Length > 3 && text[1] == '#')
            {
                uint codepoint = 0;
                if (text[2] == 'x' || text[2] == 'X')
                {
                    for (int i = 3; i < text.Length - 1; i++)
                    {
                        codepoint = 16 * codepoint + HexVal(text[i]);
                        if (codepoint > 0x10FFFF) { codepoint = 0xFFFD; break; }
                    }
                }
                else
                {
                    for (int i = 2; i < text.Length - 1; i++)
                    {
                        codepoint = 10 * codepoint + (uint)(text[i] - '0');
                        if (codepoint > 0x10FFFF) { codepoint = 0xFFFD; break; }
                    }
                }
                AppendUtf8Codepoint(codepoint, htmlEsc, urlEsc);
                return;
            }

            // Named entity lookup
            var entity = Md4cEntity.EntityLookup(text);
            if (entity != null)
            {
                AppendUtf8Codepoint(entity.Value.Codepoint0, htmlEsc, urlEsc);
                if (entity.Value.Codepoint1 != 0)
                    AppendUtf8Codepoint(entity.Value.Codepoint1, htmlEsc, urlEsc);
                return;
            }

            // Unknown entity — pass through verbatim.
            if (urlEsc)
                UrlEscaped(text);
            else if (htmlEsc)
                HtmlEscaped(text);
            else
                Verbatim(text);
        }

        /// <summary>
        /// SECURITY (TASK-045): reconstructs the raw URL from the
        /// MarkdownAttribute, runs it through the safe-mode allowlist, then
        /// writes (URL-escaped) the result. <c>javascript:</c>, <c>data:</c>,
        /// <c>vbscript:</c>, custom-scheme URLs become <c>about:blank</c>.
        /// </summary>
        private void RenderUrlSanitized(MarkdownAttribute attr)
        {
            if (attr.Text == null) return;
            // Reassemble the raw URL string. Substr boundaries match the
            // attr.Text source span; entity substrings are rendered later.
            var raw = new StringBuilder(attr.Text.Length);
            for (int i = 0; i < attr.SubstrTypes.Length; i++)
            {
                int off = attr.SubstrOffsets[i];
                int nextOff = attr.SubstrOffsets[i + 1];
                int len = nextOff - off;
                if (off < 0 || len <= 0 || off + len > attr.Text.Length) continue;
                if (attr.SubstrTypes[i] == MarkdownTextType.NullChar)
                {
                    raw.Append('�');
                    continue;
                }
                raw.Append(attr.Text.AsSpan(off, len));
            }
            var sanitized = SanitizeUrl(raw.ToString(), unsafeAllowed: (flags & HtmlFlags.AllowUnsafeUrls) != 0);
            UrlEscaped(sanitized.AsSpan());
        }

        private void RenderAttribute(MarkdownAttribute attr, bool urlEsc)
        {
            if (attr.Text == null) return;

            for (int i = 0; i < attr.SubstrTypes.Length; i++)
            {
                int off = attr.SubstrOffsets[i];
                int nextOff = attr.SubstrOffsets[i + 1];
                int len = nextOff - off;
                if (off < 0 || len <= 0 || off + len > attr.Text.Length) continue;
                var type = attr.SubstrTypes[i];
                var substr = attr.Text.AsSpan(off, len);

                switch (type)
                {
                    case MarkdownTextType.NullChar:
                        AppendUtf8Codepoint(0xFFFD, false);
                        break;
                    case MarkdownTextType.Entity:
                        RenderEntity(substr, !urlEsc, urlEsc);
                        break;
                    default:
                        if (urlEsc)
                            UrlEscaped(substr);
                        else
                            HtmlEscaped(substr);
                        break;
                }
            }
        }

        public int EnterBlock(MarkdownBlockType type, object? detail)
        {
            switch (type)
            {
                case MarkdownBlockType.Doc: break;
                case MarkdownBlockType.Quote: Verbatim("<blockquote>\n"); break;
                case MarkdownBlockType.Ul: Verbatim("<ul>\n"); break;
                case MarkdownBlockType.Ol:
                    var ol = (MarkdownBlockOlDetail)detail!;
                    if (ol.Start == 1)
                        Verbatim("<ol>\n");
                    else
                        Verbatim($"<ol start=\"{ol.Start}\">\n");
                    break;
                case MarkdownBlockType.Li:
                    var li = (MarkdownBlockLiDetail)detail!;
                    if (li.IsTask)
                    {
                        Verbatim("<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled");
                        if (li.TaskMark == 'x' || li.TaskMark == 'X')
                            Verbatim(" checked");
                        Verbatim(">");
                    }
                    else
                        Verbatim("<li>");
                    break;
                case MarkdownBlockType.Hr:
                    Verbatim((flags & HtmlFlags.Xhtml) != 0 ? "<hr />\n" : "<hr>\n");
                    break;
                case MarkdownBlockType.H:
                    var h = (MarkdownBlockHDetail)detail!;
                    Verbatim(h.Level switch { 1 => "<h1>", 2 => "<h2>", 3 => "<h3>", 4 => "<h4>", 5 => "<h5>", _ => "<h6>" });
                    break;
                case MarkdownBlockType.Code:
                    var code = (MarkdownBlockCodeDetail)detail!;
                    Verbatim("<pre><code");
                    if (!string.IsNullOrEmpty(code.Lang.Text))
                    {
                        Verbatim(" class=\"language-");
                        RenderAttribute(code.Lang, false);
                        Verbatim("\"");
                    }
                    Verbatim(">");
                    break;
                case MarkdownBlockType.Html: break;
                case MarkdownBlockType.P: Verbatim("<p>"); break;
                case MarkdownBlockType.Table: Verbatim("<table>\n"); break;
                case MarkdownBlockType.Thead: Verbatim("<thead>\n"); break;
                case MarkdownBlockType.Tbody: Verbatim("<tbody>\n"); break;
                case MarkdownBlockType.Tr: Verbatim("<tr>\n"); break;
                case MarkdownBlockType.Th:
                case MarkdownBlockType.Td:
                    var td = (MarkdownBlockTdDetail)detail!;
                    string cellType = type == MarkdownBlockType.Th ? "th" : "td";
                    string alignAttr = td.Align switch
                    {
                        MarkdownAlign.Left => $"<{cellType} align=\"left\">",
                        MarkdownAlign.Center => $"<{cellType} align=\"center\">",
                        MarkdownAlign.Right => $"<{cellType} align=\"right\">",
                        _ => $"<{cellType}>",
                    };
                    Verbatim(alignAttr);
                    break;
            }
            return 0;
        }

        public int LeaveBlock(MarkdownBlockType type, object? detail)
        {
            switch (type)
            {
                case MarkdownBlockType.Doc: break;
                case MarkdownBlockType.Quote: Verbatim("</blockquote>\n"); break;
                case MarkdownBlockType.Ul: Verbatim("</ul>\n"); break;
                case MarkdownBlockType.Ol: Verbatim("</ol>\n"); break;
                case MarkdownBlockType.Li: Verbatim("</li>\n"); break;
                case MarkdownBlockType.Hr: break;
                case MarkdownBlockType.H:
                    var h = (MarkdownBlockHDetail)detail!;
                    Verbatim(h.Level switch { 1 => "</h1>\n", 2 => "</h2>\n", 3 => "</h3>\n", 4 => "</h4>\n", 5 => "</h5>\n", _ => "</h6>\n" });
                    break;
                case MarkdownBlockType.Code: Verbatim("</code></pre>\n"); break;
                case MarkdownBlockType.Html: break;
                case MarkdownBlockType.P: Verbatim("</p>\n"); break;
                case MarkdownBlockType.Table: Verbatim("</table>\n"); break;
                case MarkdownBlockType.Thead: Verbatim("</thead>\n"); break;
                case MarkdownBlockType.Tbody: Verbatim("</tbody>\n"); break;
                case MarkdownBlockType.Tr: Verbatim("</tr>\n"); break;
                case MarkdownBlockType.Th: Verbatim("</th>\n"); break;
                case MarkdownBlockType.Td: Verbatim("</td>\n"); break;
            }
            return 0;
        }

        public int EnterSpan(MarkdownSpanType type, object? detail)
        {
            bool insideImg = imageNestingLevel > 0;
            if (type == MarkdownSpanType.Img)
                imageNestingLevel++;
            if (insideImg)
                return 0;

            switch (type)
            {
                case MarkdownSpanType.Em: Verbatim("<em>"); break;
                case MarkdownSpanType.Strong: Verbatim("<strong>"); break;
                case MarkdownSpanType.U: Verbatim("<u>"); break;
                case MarkdownSpanType.A:
                    var a = (MarkdownSpanADetail)detail!;
                    Verbatim("<a href=\"");
                    if ((flags & HtmlFlags.AllowUnsafeUrls) != 0)
                        RenderAttribute(a.Href, true);
                    else
                        RenderUrlSanitized(a.Href);
                    if (a.Title.Text != null)
                    {
                        Verbatim("\" title=\"");
                        RenderAttribute(a.Title, false);
                    }
                    Verbatim("\">");
                    break;
                case MarkdownSpanType.Img:
                    var img = (MarkdownSpanImgDetail)detail!;
                    Verbatim("<img src=\"");
                    if ((flags & HtmlFlags.AllowUnsafeUrls) != 0)
                        RenderAttribute(img.Src, true);
                    else
                        RenderUrlSanitized(img.Src);
                    Verbatim("\" alt=\"");
                    break;
                case MarkdownSpanType.Code: Verbatim("<code>"); break;
                case MarkdownSpanType.Del: Verbatim("<del>"); break;
                case MarkdownSpanType.LatexMath: Verbatim("<x-equation>"); break;
                case MarkdownSpanType.LatexMathDisplay: Verbatim("<x-equation type=\"display\">"); break;
                case MarkdownSpanType.WikiLink:
                    var wl = (MarkdownSpanWikiLinkDetail)detail!;
                    Verbatim("<x-wikilink data-target=\"");
                    RenderAttribute(wl.Target, false);
                    Verbatim("\">");
                    break;
            }
            return 0;
        }

        public int LeaveSpan(MarkdownSpanType type, object? detail)
        {
            if (type == MarkdownSpanType.Img)
                imageNestingLevel--;
            if (imageNestingLevel > 0)
                return 0;

            switch (type)
            {
                case MarkdownSpanType.Em: Verbatim("</em>"); break;
                case MarkdownSpanType.Strong: Verbatim("</strong>"); break;
                case MarkdownSpanType.U: Verbatim("</u>"); break;
                case MarkdownSpanType.A: Verbatim("</a>"); break;
                case MarkdownSpanType.Img:
                    var img = (MarkdownSpanImgDetail)detail!;
                    if (img.Title.Text != null)
                    {
                        Verbatim("\" title=\"");
                        RenderAttribute(img.Title, false);
                    }
                    Verbatim((flags & HtmlFlags.Xhtml) != 0 ? "\" />" : "\">");
                    break;
                case MarkdownSpanType.Code: Verbatim("</code>"); break;
                case MarkdownSpanType.Del: Verbatim("</del>"); break;
                case MarkdownSpanType.LatexMath:
                case MarkdownSpanType.LatexMathDisplay: Verbatim("</x-equation>"); break;
                case MarkdownSpanType.WikiLink: Verbatim("</x-wikilink>"); break;
            }
            return 0;
        }

        public int TextCallback(MarkdownTextType type, ReadOnlySpan<char> text)
        {
            switch (type)
            {
                case MarkdownTextType.NullChar:
                    AppendUtf8Codepoint(0xFFFD, false);
                    break;
                case MarkdownTextType.Br:
                    if (imageNestingLevel == 0)
                        Verbatim((flags & HtmlFlags.Xhtml) != 0 ? "<br />\n" : "<br>\n");
                    else
                        Verbatim(" ");
                    break;
                case MarkdownTextType.SoftBr:
                    Verbatim(imageNestingLevel == 0 ? "\n" : " ");
                    break;
                case MarkdownTextType.Html:
                    Verbatim(text);
                    break;
                case MarkdownTextType.Entity:
                    RenderEntity(text, true);
                    break;
                default:
                    HtmlEscaped(text);
                    break;
            }
            return 0;
        }
    }
}
