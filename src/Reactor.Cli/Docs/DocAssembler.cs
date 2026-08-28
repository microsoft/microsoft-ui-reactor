using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs;

internal record ScreenshotInfo(string Id, string TopicId, string Description, string Format, string Kind = "screenshot");

/// <summary>
/// Replaces <c>snippet=</c> and <c>screenshot://</c> directives in compiled doc output.
/// </summary>
internal static partial class DocAssembler
{
    // ```csharp snippet="topic/id"            or   ```csharp snippet="topic/id" title="Title"
    // ```
    //
    // The language is captured rather than hard-coded. It used to be literal
    // `csharp`, which silently broke every non-C# fence: ExtractSnippetRefs (the
    // discovery side) matches `snippet="..."` in any fence, so an ```xml fence
    // was extracted, resolved, and reported as "✓ resolved" — and then never
    // substituted, because only this regex decides what gets replaced. The three
    // ```xml project-shape fences in packaging.md rendered as *empty* code blocks
    // under prose that promised to show the shape, and the unexpanded
    // `snippet="..."` attribute leaked into the fence info string. Keep discovery
    // and substitution language-agnostic together, or the two disagree silently.
    [GeneratedRegex(@"```([A-Za-z0-9_+#-]+)\s+snippet=""([^""]+)""(?:\s+title=""([^""]+)"")?\s*[\r\n]+```")]
    private static partial Regex SnippetDirective();

    // ![alt text](screenshot://topic/id)
    [GeneratedRegex(@"!\[([^\]]*)\]\(screenshot://([^)]+)\)")]
    private static partial Regex ScreenshotDirective();

    /// <summary>
    /// Compile-time token replaced with the single-source public package version
    /// (<c>&lt;ReactorPublicVersion&gt;</c> in Directory.Build.props, resolved via
    /// <see cref="VersionSource"/>). Authors write this in <c>.md.dt</c> prose /
    /// snippets instead of a hardcoded <c>0.1.0-preview.N</c> literal.
    /// </summary>
    internal const string VersionToken = "{{reactorVersion}}";

    public static string Assemble(
        string body,
        Dictionary<string, SnippetExtractor.Snippet> snippets,
        Dictionary<string, ScreenshotInfo> screenshots,
        out List<string> errors,
        out List<string> warnings,
        string? topicId,
        string? reactorVersion)
    {
        var errs = new List<string>();
        var warns = new List<string>();
        // Topics whose id contains '/' (e.g. "recipes/login") emit to a
        // subdirectory; image refs need "../" * depth to reach docs/guide/images.
        var depth = topicId is null ? 0 : topicId.Count(c => c == '/');
        var imagePrefix = depth == 0 ? "" : string.Concat(Enumerable.Repeat("../", depth));
        var output = body;

        // Replace snippet directives with extracted code
        output = SnippetDirective().Replace(output, match =>
        {
            var language = match.Groups[1].Value;
            var snippetId = match.Groups[2].Value;
            var title = match.Groups[3].Success ? match.Groups[3].Value : null;

            if (!snippets.TryGetValue(snippetId, out var snippet))
            {
                errs.Add($"Missing snippet: {snippetId}");
                return match.Value;
            }

            var sb = new StringBuilder();
            // SECURITY (TASK-043): pick a fence longer than the longest run of
            // backticks in the snippet so embedded ``` cannot break out of the
            // fenced block and inject markdown.
            var fence = ChooseFence(snippet.Code);
            sb.AppendLine(fence + language);
            // Title goes *inside* the fence. It used to be emitted above the
            // opening fence, which put it in markdown body text rather than in
            // the code block: as `// Title` it rendered as a stray literal line
            // of prose, and as an XML/HTML comment it would be parsed as raw
            // markdown HTML and vanish from the page entirely.
            if (title != null)
                sb.AppendLine(TitleComment(language, title));
            sb.AppendLine(snippet.Code);
            sb.Append(fence);
            return sb.ToString();
        });

        // Replace screenshot:// URLs with relative image paths
        output = ScreenshotDirective().Replace(output, match =>
        {
            var altText = match.Groups[1].Value;
            var screenshotId = match.Groups[2].Value;

            if (!screenshots.ContainsKey(screenshotId))
                warns.Add($"Screenshot not captured: {screenshotId}");

            var parts = screenshotId.Split('/');
            var topic = parts[0];
            var id = parts.Length > 1 ? parts[1] : parts[0];
            var format = screenshots.TryGetValue(screenshotId, out var info) ? info.Format : "png";
            // Catalog-thumb captures land at `<id>-thumb.<format>` so the
            // generated URL must match (spec 041 §6.3, §12 Q7).
            var fileBase = ImageProcessor.ThumbAwareFileBase(
                id,
                info != null && string.Equals(info.Kind, "catalog-thumb", StringComparison.OrdinalIgnoreCase));

            return $"![{altText}]({imagePrefix}images/{topic}/{fileBase}.{format})";
        });

        // Substitute the single-source version token LAST — after snippet and
        // screenshot expansion — so a {{reactorVersion}} that appears inside an
        // inserted snippet or screenshot alt-text is resolved too. The value
        // comes from <ReactorPublicVersion> in Directory.Build.props (via
        // VersionSource), so guide prose / PackageReference snippets never need a
        // per-release hand edit. Callers pass null to opt out (e.g. structural
        // tier-lint, which does not render the version token).
        if (reactorVersion is not null)
            output = output.Replace(VersionToken, reactorVersion);

        errors = errs;
        warnings = warns;
        return output;
    }

    /// <summary>
    /// Renders <paramref name="title"/> as a comment valid in
    /// <paramref name="language"/>. A <c>title=</c> on an ```xml fence used to
    /// emit <c>// Title</c>, which is not an XML comment — the header rendered
    /// as malformed markup inside the code block.
    /// </summary>
    internal static string TitleComment(string language, string title) =>
        language.ToLowerInvariant() switch
        {
            "xml" or "html" or "xaml" or "svg" or "csproj" or "props" or "targets"
                => $"<!-- {title} -->",
            _ => $"// {title}",
        };

    /// <summary>
    /// Returns a fence (sequence of backticks) at least one char longer than
    /// any run of backticks present in <paramref name="content"/>. Minimum
    /// length is 3 (the standard CommonMark fence). TASK-043.
    /// </summary>
    internal static string ChooseFence(string content)
    {
        int longest = 0;
        int run = 0;
        foreach (var c in content)
        {
            if (c == '`') { run++; if (run > longest) longest = run; }
            else run = 0;
        }
        return new string('`', Math.Max(3, longest + 1));
    }
}
