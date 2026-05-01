using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Markdown;

namespace Microsoft.UI.Reactor.Tests.Md4cGenerated;

/// <summary>
/// Normalize HTML for comparison, ported from md4c's test/normalize.py.
/// Uses an HTML-aware approach that collapses whitespace, strips whitespace
/// around block-level tags, and normalizes self-closing tags.
/// </summary>
internal static class Md4cTestHelper
{
    /// <summary>
    /// Spec-compliance helper. The CommonMark spec tests verify the md4c
    /// renderer's output across a 1000+ example corpus that includes raw
    /// HTML blocks and arbitrary URL schemes. Since TASK-045/046 made the
    /// public <c>ToHtml</c> default to safe-mode, spec tests opt back in
    /// via the unsafe flags. This is the parser-correctness API; production
    /// callers should use the safe defaults.
    /// </summary>
    public static string SpecToHtml(string md, MarkdownParserFlags parserFlags) =>
        MarkdownHtml.ToHtml(md, parserFlags,
            MarkdownHtml.HtmlFlags.AllowRawHtml | MarkdownHtml.HtmlFlags.AllowUnsafeUrls);

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "article", "header", "aside", "hgroup", "blockquote",
        "hr", "iframe", "body", "li", "map", "button", "object", "canvas",
        "ol", "caption", "output", "col", "p", "colgroup", "pre", "dd",
        "progress", "div", "section", "dl", "table", "td", "dt",
        "tbody", "embed", "textarea", "fieldset", "tfoot", "figcaption",
        "th", "figure", "thead", "footer", "tr", "form", "ul",
        "h1", "h2", "h3", "h4", "h5", "h6", "video", "script", "style"
    };

    private static readonly Regex WhitespaceRe = new(@"\s+");

    // Matches HTML tags (including comments, CDATA, PI, declarations) or text chunks
    private static readonly Regex HtmlChunkRe = new(@"(<!\[CDATA\[.*?\]\]>|<!--.*?-->|<!\w[^>]*>|<\?[^>]*>|<[^>]*>|[^<]+)", RegexOptions.Singleline);

    // Matches an opening tag: <tagname ...>
    private static readonly Regex OpenTagRe = new(@"^<([a-zA-Z][a-zA-Z0-9]*)\b");

    // Matches a closing tag: </tagname>
    private static readonly Regex CloseTagRe = new(@"^</([a-zA-Z][a-zA-Z0-9]*)\s*>$");

    // Matches a self-closing tag: <tagname ... />
    private static readonly Regex SelfCloseRe = new(@"^<([a-zA-Z][a-zA-Z0-9]*)\b(.*?)\s*/>$", RegexOptions.Singleline);

    public static string NormalizeHtml(string html)
    {
        var output = new StringBuilder();
        bool inPre = false;
        string lastEvent = "starttag"; // "starttag", "endtag", "data", "comment", "ref", "decl", "pi"
        string lastTag = "";

        foreach (Match chunk in HtmlChunkRe.Matches(html))
        {
            string token = chunk.Value;

            if (token.StartsWith("<![CDATA["))
            {
                // Pass CDATA through verbatim
                output.Append(token);
                lastEvent = "data";
                continue;
            }

            if (token.StartsWith("<!--"))
            {
                output.Append(token);
                lastEvent = "comment";
                continue;
            }

            if (token.StartsWith("<?"))
            {
                output.Append(token);
                lastEvent = "pi";
                continue;
            }

            if (token.StartsWith("<!"))
            {
                output.Append(token);
                lastEvent = "decl";
                continue;
            }

            if (token.StartsWith("<"))
            {
                // Self-closing tag: convert to open tag
                var selfCloseMatch = SelfCloseRe.Match(token);
                if (selfCloseMatch.Success)
                {
                    string tag = selfCloseMatch.Groups[1].Value.ToLowerInvariant();
                    string rest = selfCloseMatch.Groups[2].Value;
                    // Reconstruct as open tag
                    if (BlockTags.Contains(tag))
                        RstripOutput(output);
                    output.Append('<').Append(tag);
                    AppendAttributes(output, rest);
                    output.Append('>');
                    lastTag = tag;
                    if (tag == "pre") inPre = true;
                    lastEvent = "endtag"; // self-closing treated as endtag per normalize.py
                    continue;
                }

                // Closing tag
                var closeMatch = CloseTagRe.Match(token);
                if (closeMatch.Success)
                {
                    string tag = closeMatch.Groups[1].Value.ToLowerInvariant();
                    if (tag == "pre") inPre = false;
                    if (BlockTags.Contains(tag))
                        RstripOutput(output);
                    output.Append("</").Append(tag).Append('>');
                    lastTag = tag;
                    lastEvent = "endtag";
                    continue;
                }

                // Opening tag
                var openMatch = OpenTagRe.Match(token);
                if (openMatch.Success)
                {
                    string tag = openMatch.Groups[1].Value.ToLowerInvariant();
                    if (tag == "pre") inPre = true;
                    if (BlockTags.Contains(tag))
                        RstripOutput(output);
                    output.Append('<').Append(tag);
                    // Extract attributes portion (everything after tag name until >)
                    string rest = token.Substring(openMatch.Length);
                    if (rest.EndsWith(">"))
                        rest = rest.Substring(0, rest.Length - 1);
                    AppendAttributes(output, rest);
                    output.Append('>');
                    lastTag = tag;
                    lastEvent = "starttag";
                    continue;
                }

                // Fallback: pass through raw
                output.Append(token);
                continue;
            }

            // Text data
            string data = token;
            bool afterTag = lastEvent == "endtag" || lastEvent == "starttag";
            bool afterBlockTag = afterTag && BlockTags.Contains(lastTag);

            if (afterTag && lastTag == "br")
                data = data.TrimStart('\n');

            if (!inPre)
                data = WhitespaceRe.Replace(data, " ");

            if (afterBlockTag && !inPre)
            {
                if (lastEvent == "starttag")
                    data = data.TrimStart();
                else if (lastEvent == "endtag")
                    data = data.Trim();
            }

            output.Append(data);
            lastEvent = "data";
        }

        return output.ToString();
    }

    private static void RstripOutput(StringBuilder sb)
    {
        while (sb.Length > 0 && char.IsWhiteSpace(sb[sb.Length - 1]))
            sb.Length--;
    }

    private static void AppendAttributes(StringBuilder output, string attrString)
    {
        // Simple attribute parsing: extract key="value" or key pairs, sort them
        attrString = attrString.Trim();
        if (string.IsNullOrEmpty(attrString))
            return;

        var attrs = new List<(string key, string? value)>();
        int i = 0;
        while (i < attrString.Length)
        {
            // Skip whitespace
            while (i < attrString.Length && char.IsWhiteSpace(attrString[i]))
                i++;
            if (i >= attrString.Length) break;

            // Read attribute name
            int nameStart = i;
            while (i < attrString.Length && attrString[i] != '=' && !char.IsWhiteSpace(attrString[i]))
                i++;
            string name = attrString.Substring(nameStart, i - nameStart).ToLowerInvariant();

            // Skip whitespace
            while (i < attrString.Length && char.IsWhiteSpace(attrString[i]))
                i++;

            if (i < attrString.Length && attrString[i] == '=')
            {
                i++; // skip '='
                while (i < attrString.Length && char.IsWhiteSpace(attrString[i]))
                    i++;

                string value;
                if (i < attrString.Length && (attrString[i] == '"' || attrString[i] == '\''))
                {
                    char quote = attrString[i];
                    i++; // skip opening quote
                    int valStart = i;
                    while (i < attrString.Length && attrString[i] != quote)
                        i++;
                    value = attrString.Substring(valStart, i - valStart);
                    if (i < attrString.Length) i++; // skip closing quote
                }
                else
                {
                    int valStart = i;
                    while (i < attrString.Length && !char.IsWhiteSpace(attrString[i]))
                        i++;
                    value = attrString.Substring(valStart, i - valStart);
                }

                attrs.Add((name, WebUtility.HtmlDecode(value) is string decoded
                    ? WebUtility.HtmlEncode(decoded)
                    : value));
            }
            else
            {
                attrs.Add((name, null));
            }
        }

        attrs.Sort((a, b) => string.Compare(a.key, b.key, StringComparison.Ordinal));
        foreach (var (key, value) in attrs)
        {
            output.Append(' ').Append(key);
            if (value != null)
                output.Append("=\"").Append(value).Append('"');
        }
    }
}
