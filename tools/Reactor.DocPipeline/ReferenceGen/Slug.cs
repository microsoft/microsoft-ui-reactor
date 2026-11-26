using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

/// <summary>
/// Heading ids as the docs site assigns them. This is a port of
/// <c>slugify</c>, <c>uniqueId</c> and <c>mkdocsSlug</c> in
/// <c>docs/_website/scripts/sync.mjs</c>, which in turn reproduce
/// Python-Markdown's toc extension. A link the generator writes to
/// <c>Page.md#anchor</c> only lands if the anchor is computed the same way the
/// site computes it, so keep the two in step.
/// </summary>
internal static class Slug
{
    // JavaScript's \w is ASCII-only; .NET's is Unicode, so spell it out.
    private const string W = "A-Za-z0-9_";

    private static readonly Regex NonWord = new($"[^{W}\\s-]", RegexOptions.CultureInvariant);
    private static readonly Regex DashSpace = new(@"[-\s]+", RegexOptions.CultureInvariant);
    private static readonly Regex NonAscii = new(@"[^\x00-\x7f]", RegexOptions.CultureInvariant);
    private static readonly Regex CodeSpan = new(@"(`+)([\s\S]*?[^`])\1(?!`)", RegexOptions.CultureInvariant);
    private static readonly Regex Image = new(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.CultureInvariant);
    private static readonly Regex InlineLink = new(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.CultureInvariant);
    private static readonly Regex RefLink = new(@"\[([^\]]*)\]\[[^\]]*\]", RegexOptions.CultureInvariant);
    private static readonly Regex Emphasis = new($@"(^|[^{W}])(\*\*|__|\*|_)(?=\S)(.+?)(?<=\S)\2(?![{W}])", RegexOptions.CultureInvariant);
    private static readonly Regex Escape = new(@"\\([\\`*_{}\[\]()#+\-.!<>])", RegexOptions.CultureInvariant);
    private static readonly Regex InlineHtml = new(@"<(\/?[a-zA-Z][^<>@ ]*( [^<>]*)?|!--[\s\S]*?--)>|[<>]", RegexOptions.CultureInvariant);
    private static readonly Regex Placeholder = new("\u0000(\\d+)\u0000", RegexOptions.CultureInvariant);
    private static readonly Regex Comment = new(@"<!--[\s\S]*?-->", RegexOptions.CultureInvariant);
    private static readonly Regex Entity = new(@"&(#[xX][0-9a-fA-F]+|#[0-9]+|[a-zA-Z]+);", RegexOptions.CultureInvariant);
    private static readonly Regex UniqueSuffix = new(@"^(.*)_([0-9]+)$", RegexOptions.CultureInvariant);

    /// <summary>Python-Markdown's default <c>slugify</c>.</summary>
    public static string Slugify(string text)
    {
        var s = text.Normalize(NormalizationForm.FormKD);
        s = NonAscii.Replace(s, string.Empty);
        s = NonWord.Replace(s, string.Empty);
        s = s.Trim().ToLowerInvariant();
        return DashSpace.Replace(s, "-");
    }

    /// <summary>The id the site gives a heading with this Markdown text, before de-duplication.</summary>
    public static string FromHeading(string headingMarkdown) =>
        Slugify(UnescapeHtml(StripTags(InlineMarkdownAsHtml(headingMarkdown))));

    /// <summary>Python-Markdown's <c>unique</c>: append <c>_1</c>, then count up, until the id is free.</summary>
    public static string Unique(string id, ISet<string> used)
    {
        while (used.Contains(id) || id.Length == 0)
        {
            var m = UniqueSuffix.Match(id);
            id = m.Success
                ? m.Groups[1].Value + "_" + (long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture)
                : id + "_1";
        }
        used.Add(id);
        return id;
    }

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string InlineMarkdownAsHtml(string source)
    {
        var codes = new List<string>();
        var text = CodeSpan.Replace(source, m =>
        {
            codes.Add(EscapeHtml(m.Groups[2].Value.Trim()));
            return "\u0000" + (codes.Count - 1).ToString(CultureInfo.InvariantCulture) + "\u0000";
        });
        text = Image.Replace(text, "<img>");
        text = InlineLink.Replace(text, "$1");
        text = RefLink.Replace(text, "$1");
        text = Emphasis.Replace(text, "$1$3");
        text = Escape.Replace(text, m => EscapeHtml(m.Groups[1].Value));
        text = InlineHtml.Replace(text, m => m.Value == "<" ? "&lt;" : m.Value == ">" ? "&gt;" : m.Value);
        return Placeholder.Replace(text, m => codes[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]);
    }

    private static string StripTags(string html)
    {
        var text = Comment.Replace(html, string.Empty);
        int start;
        while ((start = text.IndexOf('<')) != -1)
        {
            var end = text.IndexOf('>', start);
            if (end == -1) break;
            text = text[..start] + text[(end + 1)..];
        }
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string UnescapeHtml(string text) => Entity.Replace(text, m =>
    {
        var name = m.Groups[1].Value;
        if (name[0] == '#')
        {
            var hex = name.Length > 1 && (name[1] == 'x' || name[1] == 'X');
            var digits = name[(hex ? 2 : 1)..];
            if (!int.TryParse(digits, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out var cp)
                || cp < 0 || cp > 0x10FFFF)
                return m.Value;
            return char.ConvertFromUtf32(cp);
        }
        return name switch
        {
            "amp" => "&",
            "lt" => "<",
            "gt" => ">",
            "quot" => "\"",
            "apos" => "'",
            "nbsp" => "\u00a0",
            _ => m.Value,
        };
    });
}

/// <summary>
/// Hands out heading ids for one page in document order, the way the site does:
/// the H1 claims its id first, and a repeated id gets <c>_1</c>, <c>_2</c>, and so on.
/// </summary>
internal sealed class SlugTracker
{
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    /// <summary>Records a heading and returns the id the site will give it.</summary>
    public string Add(string headingMarkdown) => Slug.Unique(Slug.FromHeading(headingMarkdown), _used);
}
