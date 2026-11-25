using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

/// <summary>A resolved <c>cref</c>: where it points, and the text to show for it.</summary>
/// <param name="Href">A relative link, or <c>null</c> when the target has no page.</param>
/// <param name="Label">The code text for the link, for example <c>UseState</c>.</param>
internal readonly record struct CrefLink(string? Href, string Label);

/// <summary>
/// Turns one section of an XML doc comment (<c>&lt;summary&gt;</c>,
/// <c>&lt;remarks&gt;</c>, and so on) into Markdown. Inline tags keep their
/// meaning: <c>&lt;see cref&gt;</c> becomes a link (or code text when the target
/// has no page), <c>&lt;c&gt;</c> becomes a code span, <c>&lt;code&gt;</c> a
/// fenced block, and <c>&lt;list&gt;</c> a bullet list.
/// </summary>
internal sealed class DocMarkdown
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex BlankLine = new(@"\r?\n[ \t]*\r?\n\s*", RegexOptions.CultureInvariant);
    private static readonly Regex SentenceEnd = new(@"(?<=[.!?])\s+(?=[A-Z`\[*])", RegexOptions.CultureInvariant);

    private readonly Func<string, CrefLink> _resolve;

    /// <param name="resolve">Maps a cref to a link. Called for every <c>&lt;see cref&gt;</c>,
    /// <c>&lt;seealso cref&gt;</c> and <c>&lt;exception cref&gt;</c>.</param>
    public DocMarkdown(Func<string, CrefLink> resolve) => _resolve = resolve;

    /// <summary>Renders a section as Markdown blocks: paragraphs, lists and code fences,
    /// separated by blank lines. Returns an empty string for a missing or empty section.</summary>
    public string Block(XElement? section)
    {
        if (section is null) return string.Empty;
        var blocks = new List<string>();
        var paragraph = new StringBuilder();
        RenderBlocks(section, blocks, paragraph);
        Flush(blocks, paragraph);
        return string.Join("\n\n", blocks.Where(b => b.Length > 0));
    }

    /// <summary>Renders a section on one line, for table cells and list items.
    /// Paragraphs are joined with a space, and code blocks become code spans.</summary>
    public string Inline(XElement? section)
    {
        if (section is null) return string.Empty;
        var sb = new StringBuilder();
        RenderInlineChildren(section, sb, flatten: true);
        return Tidy(sb.ToString());
    }

    /// <summary>Renders a cref as a Markdown link, or as code text when it has no page.</summary>
    public string Link(string cref)
    {
        var link = _resolve(cref);
        return Format(link, null);
    }

    /// <summary>The first sentence of a Markdown paragraph, for index tables.</summary>
    public static string FirstSentence(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        var firstBlock = markdown.Replace("\r\n", "\n").Split("\n\n")[0];
        if (firstBlock.StartsWith("```", StringComparison.Ordinal) || firstBlock.StartsWith("- ", StringComparison.Ordinal))
            return string.Empty;
        var line = Whitespace.Replace(firstBlock, " ").Trim();
        // Do not split inside a code span or a link.
        var protectedSpans = Regex.Matches(line, @"`[^`]*`|\[[^\]]*\]\([^)]*\)");
        foreach (Match m in SentenceEnd.Matches(line))
        {
            if (protectedSpans.Any(p => m.Index > p.Index && m.Index < p.Index + p.Length)) continue;
            var candidate = line[..m.Index].Trim();
            // "e.g." and "i.e." are not sentence ends.
            if (candidate.EndsWith("e.g.", StringComparison.Ordinal) || candidate.EndsWith("i.e.", StringComparison.Ordinal)) continue;
            return candidate;
        }
        return line;
    }

    /// <summary>Escapes a cell for a Markdown table.</summary>
    public static string Cell(string text) => text.Replace("|", "\\|").Replace("\n", " ");

    // ── Block level ───────────────────────────────────────────────────────

    private void RenderBlocks(XElement container, List<string> blocks, StringBuilder paragraph)
    {
        foreach (var node in container.Nodes())
        {
            // Plain-text docs often separate paragraphs with a blank line instead of
            // <para>. Keep that break; collapsing it runs every paragraph together.
            if (node is XText text && BlankLine.IsMatch(text.Value))
            {
                var parts = BlankLine.Split(text.Value);
                for (var i = 0; i < parts.Length; i++)
                {
                    if (i > 0) Flush(blocks, paragraph);
                    RenderInline(new XText(parts[i]), paragraph, flatten: false);
                }
                continue;
            }
            if (node is XElement el)
            {
                switch (el.Name.LocalName)
                {
                    case "para":
                        Flush(blocks, paragraph);
                        RenderBlocks(el, blocks, paragraph);
                        Flush(blocks, paragraph);
                        continue;
                    case "code":
                        Flush(blocks, paragraph);
                        blocks.Add(Fence(el));
                        continue;
                    case "list":
                        Flush(blocks, paragraph);
                        blocks.Add(List(el));
                        continue;
                }
            }
            RenderInline(node, paragraph, flatten: false);
        }
    }

    private static void Flush(List<string> blocks, StringBuilder paragraph)
    {
        var text = Tidy(paragraph.ToString());
        paragraph.Clear();
        if (text.Length == 0) return;
        blocks.Add(EscapeBlockStart(text));
    }

    /// <summary>
    /// A paragraph that starts with <c>#</c>, <c>-</c>, <c>+</c>, <c>&gt;</c> or
    /// <c>1.</c> would turn into a heading, list or quote. Markdown can only
    /// escape punctuation, so a numbered start keeps its digits and escapes the
    /// dot or parenthesis (<c>1\.</c>); a backslash before a digit would print.
    /// </summary>
    internal static string EscapeBlockStart(string text)
    {
        var numbered = Regex.Match(text, @"^\d+(?=[.)])");
        if (numbered.Success)
            return string.Concat(numbered.Value, "\\", text.AsSpan(numbered.Length));
        return Regex.IsMatch(text, @"^[#>+\-]") ? "\\" + text : text;
    }

    private string List(XElement list)
    {
        var numbered = string.Equals((string?)list.Attribute("type"), "number", StringComparison.Ordinal);
        var lines = new List<string>();
        var n = 0;
        foreach (var item in list.Elements())
        {
            if (item.Name.LocalName is not ("item" or "listheader")) continue;
            if (item.Name.LocalName == "listheader") continue;
            n++;
            var term = item.Element("term");
            var description = item.Element("description");
            string text;
            if (term is not null)
            {
                var t = Inline(term);
                var d = description is null ? string.Empty : Inline(description);
                text = d.Length > 0 ? $"**{t}**: {d}" : t;
            }
            else
            {
                text = Inline(description ?? item);
            }
            if (text.Length == 0) continue;
            lines.Add((numbered ? n + ". " : "- ") + text);
        }
        return string.Join("\n", lines);
    }

    private static string Fence(XElement code)
    {
        var lang = (string?)code.Attribute("lang") ?? (string?)code.Attribute("language") ?? "csharp";
        if (lang is "C#" or "c#" or "cs") lang = "csharp";
        var body = Dedent(code.Value);
        var fence = "```";
        while (body.Contains(fence, StringComparison.Ordinal)) fence += "`";
        return $"{fence}{lang}\n{body}\n{fence}";
    }

    /// <summary>Removes leading and trailing blank lines and the indent every line shares.</summary>
    internal static string Dedent(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\t', ' ').Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) return string.Empty;
        var indent = lines.Where(l => l.Length > 0).Min(l => l.Length - l.TrimStart(' ').Length);
        return string.Join("\n", lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
    }

    // ── Inline level ──────────────────────────────────────────────────────

    private void RenderInlineChildren(XElement el, StringBuilder sb, bool flatten)
    {
        foreach (var node in el.Nodes())
            RenderInline(node, sb, flatten);
    }

    private void RenderInline(XNode node, StringBuilder sb, bool flatten)
    {
        switch (node)
        {
            case XText text:
                sb.Append(EscapeProse(Whitespace.Replace(text.Value, " ")));
                return;
            case XElement el:
                break;
            default:
                return;
        }

        var e = (XElement)node;
        switch (e.Name.LocalName)
        {
            case "c":
                sb.Append(CodeSpan(Whitespace.Replace(e.Value, " ").Trim()));
                return;
            case "code":
                // Only reached in inline mode: a code block inside a table cell or list item.
                sb.Append(' ').Append(CodeSpan(Whitespace.Replace(e.Value, " ").Trim())).Append(' ');
                return;
            case "paramref":
            case "typeparamref":
                sb.Append(CodeSpan((string?)e.Attribute("name") ?? string.Empty));
                return;
            case "see":
            case "seealso":
                RenderSee(e, sb);
                return;
            case "a":
                RenderHref((string?)e.Attribute("href"), e, sb);
                return;
            case "b":
            case "strong":
                Wrap(e, sb, "**", flatten);
                return;
            case "i":
            case "em":
                Wrap(e, sb, "*", flatten);
                return;
            case "br":
                sb.Append(' ');
                return;
            case "para":
            case "list":
                if (e.Name.LocalName == "list" && !flatten)
                {
                    sb.Append(' ').Append(Whitespace.Replace(List(e), " ")).Append(' ');
                    return;
                }
                sb.Append(' ');
                RenderInlineChildren(e, sb, flatten);
                sb.Append(' ');
                return;
            default:
                RenderInlineChildren(e, sb, flatten);
                return;
        }
    }

    private void RenderSee(XElement e, StringBuilder sb)
    {
        var langword = (string?)e.Attribute("langword");
        if (!string.IsNullOrEmpty(langword))
        {
            sb.Append(CodeSpan(langword));
            return;
        }
        var href = (string?)e.Attribute("href");
        if (!string.IsNullOrEmpty(href))
        {
            RenderHref(href, e, sb);
            return;
        }
        var cref = (string?)e.Attribute("cref");
        if (string.IsNullOrEmpty(cref))
        {
            RenderInlineChildren(e, sb, flatten: true);
            return;
        }
        var inner = Tidy(InnerInline(e));
        sb.Append(Format(_resolve(cref), inner.Length > 0 ? inner : null));
    }

    private void RenderHref(string? href, XElement e, StringBuilder sb)
    {
        var inner = Tidy(InnerInline(e));
        if (string.IsNullOrEmpty(href))
        {
            sb.Append(inner);
            return;
        }
        sb.Append('[').Append(inner.Length > 0 ? inner : EscapeProse(href)).Append("](").Append(href).Append(')');
    }

    private string InnerInline(XElement e)
    {
        var sb = new StringBuilder();
        RenderInlineChildren(e, sb, flatten: true);
        return sb.ToString();
    }

    private void Wrap(XElement e, StringBuilder sb, string marker, bool flatten)
    {
        var inner = new StringBuilder();
        RenderInlineChildren(e, inner, flatten);
        var raw = inner.ToString();
        var text = Tidy(raw);
        if (text.Length == 0) return;
        // Emphasis must hug its text, so move outer spaces outside the markers.
        if (raw.Length > 0 && char.IsWhiteSpace(raw[0])) sb.Append(' ');
        sb.Append(marker).Append(text).Append(marker);
        if (raw.Length > 0 && char.IsWhiteSpace(raw[^1])) sb.Append(' ');
    }

    private static string Format(CrefLink link, string? text)
    {
        var shown = text ?? CodeSpan(link.Label);
        return link.Href is null ? shown : $"[{shown}]({link.Href})";
    }

    /// <summary>A Markdown code span that survives backticks inside the code.</summary>
    public static string CodeSpan(string code)
    {
        if (code.Length == 0) return string.Empty;
        var ticks = "`";
        while (code.Contains(ticks, StringComparison.Ordinal)) ticks += "`";
        var pad = code.StartsWith('`') || code.EndsWith('`') ? " " : string.Empty;
        return ticks + pad + code + pad + ticks;
    }

    /// <summary>Escapes the characters that would otherwise start Markdown or HTML syntax.</summary>
    internal static string EscapeProse(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '\\' or '`' or '*' or '<' or '>' or '[' or ']' or '{' or '}')
                sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string Tidy(string text)
    {
        var t = Whitespace.Replace(text, " ").Trim();
        // Prose renders punctuation right after a link or code span; drop the space the
        // XML line break left in front of it.
        return Regex.Replace(t, @" (?=[.,;:!?)](\s|$))", string.Empty);
    }
}
