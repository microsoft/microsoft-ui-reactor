using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Spec 041 §10.4.1 — post-processes generated reference pages and
/// hand-authored templates so the conceptual guide and the auto-generated
/// reference cross-link in both directions.
///
/// Three responsibilities:
///
/// <list type="number">
/// <item>Inject a <b>Learn more</b> callout near the top of each
///   reference page using the <c>guide-pages</c> mapping from
///   <c>reference-map.yaml</c>.</item>
/// <item>Inject a <b>Featured in</b> list at the bottom of each
///   reference page enumerating any hand-authored guide template that
///   names the member via an <c>&lt;!-- ref:Member --&gt;</c> marker.</item>
/// <item>Expand <c>&lt;!-- ref:Member --&gt;</c> markers in template bodies
///   to relative MD links pointed at the generated reference page.</item>
/// </list>
///
/// Lints:
/// <list type="bullet">
/// <item><c>REACTOR_DOC_REGISTRY_W001</c> — a registry category /
///   member resolves to a category with an empty <c>guide-pages</c>
///   list.</item>
/// <item><c>REACTOR_DOC_REGISTRY_W002</c> — a guide page (declared by
///   any registry rule) has no incoming <c>&lt;!-- ref: --&gt;</c>
///   marker.</item>
/// </list>
/// </summary>
internal static class ReferenceLinkInjector
{
    /// <summary>
    /// Apply post-processing to a single generated reference page. The
    /// caller supplies the in-memory ref-gen result so the injector can
    /// resolve cross-page links without re-parsing the on-disk MD.
    /// </summary>
    public static string Inject(
        GeneratedPage page,
        ReferenceGenResult result,
        IReadOnlyDictionary<string, IReadOnlyList<TemplateReference>> reverseIndex,
        List<RefGenFinding> findings)
    {
        var body = page.Body;
        var route = page.Route;

        // 1) Learn-more callout. Guide pages live under docs/guide/<page>.md;
        //    reference pages live under docs/guide/reference/<category>/.
        //    Each guide-pages entry becomes a relative ../../<page>.md link.
        if (route.GuidePages.Count == 0)
        {
            findings.Add(new RefGenFinding(
                "REACTOR_DOC_REGISTRY_W001",
                $"registry category '{route.Category}' has no guide-pages mapping for {page.Member.Cref}",
                route.RelativePath,
                TierLintSeverity.Warning));
        }
        else
        {
            var links = string.Join(", ", route.GuidePages.Select(p => $"[{ToTitle(p)}](../../{p}.md)"));
            var callout = $"> **Learn more:** {links}\n\n";
            // Insert immediately after the signature block (the `_cref_: ...`
            // line). Locate that line and insert after the trailing blank line.
            body = InsertAfterSignature(body, callout);
        }

        // 2) Dual-link inline <see cref> targets: if the cref resolved to a
        //    page with non-empty guide-pages, append ` ([guide](../../<page>.md))`
        //    next to the existing reference-page link. The existing
        //    `[Name](Name.md)` rendering came from CrefResolver; here we
        //    detect those links and add the guide pointer.
        body = InlineDualLinkPattern.Replace(body, m =>
        {
            var name = m.Groups["name"].Value;
            var relPath = m.Groups["path"].Value;
            // The path is relative (e.g. "Foo.md" sibling, or "../other/Foo.md").
            // Find the corresponding GeneratedPage by short name; if it has
            // guide-pages, append the dual-link suffix.
            var target = result.Pages.FirstOrDefault(p => p.Route.ShortName == name);
            if (target is null || target.Route.GuidePages.Count == 0) return m.Value;
            var guide = target.Route.GuidePages[0];
            return $"{m.Value} ([guide](../../{guide}.md))";
        });

        // 3) Featured-in reverse-index injection.
        if (reverseIndex.TryGetValue(page.Member.Cref, out var refs) ||
            reverseIndex.TryGetValue(page.Route.ShortName, out refs))
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Featured in");
            sb.AppendLine();
            foreach (var r in refs)
            {
                sb.AppendLine($"- [{ToTitle(r.TemplateId)}](../../{r.TemplateId}.md)");
            }
            sb.AppendLine();
            body = body.TrimEnd() + "\n\n" + sb;
        }

        return body;
    }

    /// <summary>
    /// Scan a template body for <c>&lt;!-- ref:Member --&gt;</c> markers and
    /// expand each one to a relative MD link to the routed reference page.
    /// Returns the rewritten body. Unknown members are left as-is and a
    /// finding is added.
    /// </summary>
    public static string ExpandMarkers(
        string templateBody,
        string templateId,
        ReferenceGenResult refResult,
        List<RefGenFinding> findings)
    {
        return RefMarkerPattern.Replace(templateBody, m =>
        {
            var ident = m.Groups["ident"].Value.Trim();
            // Identifier may be a short name (UseState) or a full cref.
            GeneratedPage? target = ident.Contains(':')
                ? refResult.Pages.FirstOrDefault(p => p.Member.Cref == ident)
                : refResult.Pages.FirstOrDefault(p => p.Route.ShortName == ident);
            if (target is null)
            {
                findings.Add(new RefGenFinding(
                    "REACTOR_DOC_REFMARKER_001",
                    $"<!-- ref:{ident} --> in template '{templateId}' does not resolve",
                    templateId,
                    TierLintSeverity.Warning));
                return m.Value;
            }
            // Guide template lives at docs/guide/<topicId>.md; reference page
            // lives at docs/guide/reference/<cat>/<name>.md. Relative path
            // from the guide page to the reference page is `reference/<cat>/<name>.md`.
            var relative = target.Route.RelativePath.Replace('\\', '/');
            return $"[{target.Route.ShortName}]({relative})";
        });
    }

    /// <summary>
    /// Build the reverse index by scanning every template body for
    /// <c>&lt;!-- ref:Member --&gt;</c> markers. Returns
    /// (Member identifier → templates that reference it).
    /// </summary>
    public static Dictionary<string, IReadOnlyList<TemplateReference>> BuildReverseIndex(
        IEnumerable<(string topicId, string body)> templates)
    {
        var index = new Dictionary<string, List<TemplateReference>>(StringComparer.Ordinal);
        foreach (var (topicId, body) in templates)
        {
            foreach (Match m in RefMarkerPattern.Matches(body))
            {
                var ident = m.Groups["ident"].Value.Trim();
                if (!index.TryGetValue(ident, out var list))
                    index[ident] = list = new List<TemplateReference>();
                if (!list.Any(r => r.TemplateId == topicId))
                    list.Add(new TemplateReference(topicId));
            }
        }
        return index.ToDictionary(p => p.Key, p => (IReadOnlyList<TemplateReference>)p.Value);
    }

    /// <summary>
    /// Emit <c>REACTOR_DOC_REGISTRY_W002</c> for any guide page named in
    /// <paramref name="declaredGuidePages"/> that doesn't appear as a
    /// destination of any ref-marker in the reverse index.
    /// </summary>
    public static IEnumerable<RefGenFinding> LintOrphanedGuidePages(
        IEnumerable<string> declaredGuidePages,
        IEnumerable<string> templateIds,
        IReadOnlyDictionary<string, IReadOnlyList<TemplateReference>> reverseIndex)
    {
        var templateSet = templateIds.ToHashSet(StringComparer.Ordinal);
        // A guide page is "featured" if any template that maps to that page
        // contains at least one ref-marker. For Phase 1B the registry maps
        // categories to guide pages; we approximate by checking that every
        // declared guide-page has at least one inbound marker across all
        // templates.
        var pagesWithInboundMarkers = reverseIndex.Values
            .SelectMany(rs => rs.Select(r => r.TemplateId))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var page in declaredGuidePages.Distinct(StringComparer.Ordinal))
        {
            if (!templateSet.Contains(page)) continue; // no template yet — out of scope
            if (!pagesWithInboundMarkers.Contains(page))
            {
                yield return new RefGenFinding(
                    "REACTOR_DOC_REGISTRY_W002",
                    $"guide page '{page}' has no <!-- ref:Member --> markers pointing to it",
                    page + ".md.dt",
                    TierLintSeverity.Warning);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Locate the signature block (the `_cref_: ...` line) and return the
    /// body with <paramref name="content"/> inserted after the next blank
    /// line. Idempotent — calling twice doesn't double the callout because
    /// the second insertion lands after the first callout's blank line.
    /// </summary>
    private static string InsertAfterSignature(string body, string content)
    {
        // Find the first blank line after the `_cref_:` row.
        var lines = body.Replace("\r\n", "\n").Split('\n');
        int crefIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("_cref_:", StringComparison.Ordinal))
            {
                crefIdx = i; break;
            }
        }
        if (crefIdx < 0) return body;
        // Insert after the next blank line.
        int blankIdx = crefIdx + 1;
        while (blankIdx < lines.Length && lines[blankIdx].Length > 0) blankIdx++;
        // Skip the blank to land before the next non-empty line.
        blankIdx++;
        var sb = new StringBuilder();
        for (int i = 0; i < blankIdx; i++) sb.AppendLine(lines[i]);
        sb.Append(content);
        for (int i = blankIdx; i < lines.Length; i++) sb.AppendLine(lines[i]);
        // AppendLine adds \r\n; emit \n to match the reader convention.
        return sb.ToString().Replace("\r\n", "\n");
    }

    private static string ToTitle(string slug) =>
        string.IsNullOrEmpty(slug) ? slug
        : char.ToUpperInvariant(slug[0]) + slug[1..].Replace('-', ' ');

    internal static readonly Regex RefMarkerPattern = new(
        @"<!--\s*ref:\s*(?<ident>[^\s\-][^-]*?)\s*-->",
        RegexOptions.Compiled);

    /// <summary>
    /// Detect inline <c>[Name](path.md)</c> links so the dual-link
    /// post-processor can append a guide pointer.
    /// </summary>
    internal static readonly Regex InlineDualLinkPattern = new(
        @"\[(?<name>[A-Za-z_][A-Za-z0-9_]*)\]\((?<path>(?:[^()\s]+/)?[A-Za-z_][A-Za-z0-9_]*\.md)\)(?!\s*\(\[guide\])",
        RegexOptions.Compiled);
}

/// <summary>
/// One inbound ref-marker entry, keyed by the template id that contains it.
/// </summary>
internal sealed record TemplateReference(string TemplateId);
