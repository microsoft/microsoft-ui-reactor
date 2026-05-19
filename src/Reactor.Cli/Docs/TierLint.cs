using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// One lint finding emitted by <see cref="TierLint"/>. Mirrors the
/// <c>&lt;file&gt;:&lt;line&gt; CODE: message</c> compiler-friendly format
/// used by MSBuild diagnostics.
/// </summary>
internal sealed record TierLintFinding(
    string Code,
    string Message,
    string FilePath,
    int Line,
    TierLintSeverity Severity)
{
    public string Format() =>
        $"{FilePath}:{Line} {Code}: {Message}";
}

internal enum TierLintSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Per-tier structural lint per spec 041 §11. Operates on the post-compile
/// Markdown body — the same string that lands in <c>docs/guide/*.md</c> —
/// so heuristics can match the reader's view of the page.
/// </summary>
internal static class TierLint
{
    /// <summary>
    /// Lint a single template + assembled body. The validator inspects the
    /// body *after* snippet/screenshot substitution so it can count resolved
    /// references rather than directive instances.
    /// </summary>
    public static List<TierLintFinding> Lint(
        DocTemplate template,
        string assembledBody,
        int resolvedSnippetCount,
        int resolvedScreenshotCount)
    {
        var findings = new List<TierLintFinding>();
        var file = template.FilePath;
        var tier = template.Tier;

        // Pages without a declared tier are treated as Solid but at info
        // severity only — this lets the validator run cleanly across the
        // current pages without exploding the existing 26-page set.
        var infoOnly = !template.TierDeclared;

        // Stub: title + ≥1 paragraph in body.
        if (string.IsNullOrWhiteSpace(template.Title))
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_001",
                "missing title (front-matter `title:` is empty)",
                file, 1, infoOnly ? TierLintSeverity.Info : TierLintSeverity.Error));

        if (!HasBodyParagraph(assembledBody))
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_002",
                "no body paragraph found",
                file, 1, infoOnly ? TierLintSeverity.Info : TierLintSeverity.Error));

        if (tier == DocTier.Stub) return findings;

        // Solid checks.
        if (resolvedSnippetCount < 3)
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_003",
                $"fewer than 3 resolved snippet= references (found {resolvedSnippetCount})",
                file, 1, Severity(infoOnly)));

        // Spec 041 §11: Solid+ tier requires at least one visual — either a
        // resolved `screenshot://` reference (doc-app capture) or an inline
        // diagram image under `images/<topic>/`. Under-the-hood pages without
        // a doc app satisfy this via Mermaid/SVG diagrams.
        if (resolvedScreenshotCount < 1 && CountDiagramImages(assembledBody) < 1)
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_004",
                "no resolved screenshot:// reference or images/<topic>/ diagram",
                file, 1, Severity(infoOnly)));

        if (!HasReferenceTableInFirstHalf(assembledBody, out var tableLine))
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_005",
                "no reference table found in the first half of the page",
                file, tableLine, Severity(infoOnly)));

        if (!FindHeading(assembledBody, "Tips", out var tipsLine))
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_006",
                "missing `## Tips` heading",
                file, 1, Severity(infoOnly)));

        if (!HasNextStepsWithLinks(assembledBody, out var nextLine, out var linkCount))
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_007",
                $"`## Next Steps` section missing or has fewer than 3 links (found {linkCount})",
                file, nextLine, Severity(infoOnly)));

        if (tier != DocTier.Comprehensive) return findings;

        // Comprehensive checks.
        if (!HasMentalModelLead(assembledBody))
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_008",
                "no mental-model lead paragraph (≥80 words above the first heading)",
                file, 1, Severity(infoOnly)));

        if (template.Caveats.Count == 0)
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_009",
                "no <!-- ai:caveat --> block",
                file, 1, Severity(infoOnly)));

        if (!FindHeading(assembledBody, "Patterns", out var pLine))
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_010",
                "missing `## Patterns` heading",
                file, 1, Severity(infoOnly)));

        if (!FindHeading(assembledBody, "Common Mistakes", out var cmLine))
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_011",
                "missing `## Common Mistakes` heading",
                file, 1, Severity(infoOnly)));

        var xlinkCount = CountCrossLinks(assembledBody);
        if (xlinkCount < 5)
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_012",
                $"fewer than 5 inline cross-links (found {xlinkCount})",
                file, 1, Severity(infoOnly)));

        if (string.IsNullOrEmpty(template.WinUiRef))
            findings.Add(new TierLintFinding(
                "REACTOR_DOC_TIER_W001",
                "winui-ref not declared (non-fatal; only required for transparent-wrapper pages)",
                file, 1, TierLintSeverity.Warning));

        return findings;
    }

    private static TierLintSeverity Severity(bool infoOnly) =>
        infoOnly ? TierLintSeverity.Info : TierLintSeverity.Error;

    private static readonly Regex DiagramImagePattern =
        new(@"!\[[^\]]*\]\(images/[^)]+\)", RegexOptions.Compiled);

    internal static int CountDiagramImages(string body) =>
        DiagramImagePattern.Matches(body).Count;

    // ── Heuristics ────────────────────────────────────────────────────────

    private static bool HasBodyParagraph(string body)
    {
        // A paragraph is any non-empty line that isn't a heading, fenced-code
        // body, list bullet, or blockquote-only line. We must track entry /
        // exit of fenced code blocks so the text inside them isn't counted
        // as prose.
        var inFence = false;
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```"))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;
            if (line.Length == 0) continue;
            if (line.StartsWith("#")) continue;
            if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ ")) continue;
            if (line.StartsWith(">")) continue;
            if (line.StartsWith("|")) continue;
            return true;
        }
        return false;
    }

    internal static bool HasReferenceTableInFirstHalf(string body, out int line)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var half = Math.Max(1, lines.Length / 2);
        for (int i = 0; i < half - 1; i++)
        {
            if (lines[i].TrimStart().StartsWith("|") && IsTableSeparator(lines[i + 1]))
            {
                line = i + 1;
                return true;
            }
        }
        line = 1;
        return false;
    }

    private static bool IsTableSeparator(string line)
    {
        var t = line.Trim();
        if (!t.StartsWith("|")) return false;
        // Each cell separator is some run of dashes (optionally colons for alignment)
        var cells = t.Trim('|').Split('|');
        if (cells.Length < 1) return false;
        foreach (var cell in cells)
        {
            var c = cell.Trim();
            if (c.Length == 0) return false;
            foreach (var ch in c)
                if (ch != '-' && ch != ':' && ch != ' ') return false;
        }
        return true;
    }

    internal static bool FindHeading(string body, string title, out int line)
    {
        var pattern = new Regex($@"^\s*#{{1,6}}\s+{Regex.Escape(title)}\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var m = pattern.Match(body);
        if (m.Success)
        {
            line = body[..m.Index].Count(c => c == '\n') + 1;
            return true;
        }
        line = 1;
        return false;
    }

    internal static bool HasNextStepsWithLinks(string body, out int line, out int linkCount)
    {
        line = 1;
        linkCount = 0;
        var headingPattern = new Regex(@"^\s*#{1,6}\s+Next Steps\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var m = headingPattern.Match(body);
        if (!m.Success) return false;

        line = body[..m.Index].Count(c => c == '\n') + 1;
        // Slice from heading to next heading (or end of body)
        var sectionStart = m.Index + m.Length;
        var nextHeading = Regex.Match(body[sectionStart..], @"^\s*#{1,6}\s+\S",
            RegexOptions.Multiline);
        var section = nextHeading.Success
            ? body[sectionStart..(sectionStart + nextHeading.Index)]
            : body[sectionStart..];

        linkCount = LinkPattern.Matches(section).Count;
        return linkCount >= 3;
    }

    internal static bool HasMentalModelLead(string body)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var words = 0;
        foreach (var raw in lines)
        {
            var line = raw.TrimStart();
            if (line.StartsWith("#")) break;
            // Skip front-matter / callout markers
            if (line.StartsWith(">") || line.StartsWith("```")) continue;
            words += line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        }
        return words >= 80;
    }

    internal static int CountCrossLinks(string body)
    {
        // Count [text](path.md...) links — relative to a guide page.
        return Regex.Matches(body, @"\[[^\]]+\]\([^)]+\.md[^)]*\)").Count;
    }

    private static readonly Regex LinkPattern = new(@"\[[^\]]+\]\([^)]+\)", RegexOptions.Compiled);
}
