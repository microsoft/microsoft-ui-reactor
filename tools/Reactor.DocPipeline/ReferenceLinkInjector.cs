using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Spec 041 §10.4.1 — links the hand-written guide and the generated
/// reference in both directions.
///
/// <list type="number">
/// <item>Expands <c>&lt;!-- ref:Member --&gt;</c> markers in template bodies
///   into relative links to the reference page and heading anchor that
///   document the member.</item>
/// <item>Builds the reverse index (member → templates that mention it). The
///   generator uses it to add those templates to each reference page's
///   <b>Learn more</b> line.</item>
/// </list>
///
/// Lint: <c>REACTOR_DOC_REGISTRY_W002</c> — a guide page (declared by any
/// registry rule) has no incoming <c>&lt;!-- ref: --&gt;</c> marker.
/// </summary>
internal static class ReferenceLinkInjector
{
    /// <summary>
    /// Expand every <c>&lt;!-- ref:Member --&gt;</c> marker in
    /// <paramref name="templateBody"/> into a relative link to the reference
    /// page and anchor that document it. The identifier can be a short name
    /// (<c>UseState</c>, <c>Button</c>, <c>Margin</c>) or a full cref
    /// (<c>M:Microsoft.UI.Reactor.Core.RenderContext.UseState``1(``0)</c>).
    ///
    /// An unresolvable marker degrades to inline code — the same treatment
    /// the generator gives an unresolvable cref — and reports
    /// <c>REACTOR_DOC_REFMARKER_001</c>. It must never be left in place: the
    /// marker is an HTML comment, so passing it through deletes the
    /// cross-reference from the rendered page and leaves a hole mid-sentence
    /// with nothing on screen to show something is missing.
    /// </summary>
    public static string ExpandMarkers(
        string templateBody,
        string templateId,
        ReferenceGenResult refResult,
        List<RefGenFinding> findings)
    {
        // The guide page lives at docs/guide/<templateId>.md and links are
        // relative to docs/guide, so a nested template id climbs one folder
        // per segment.
        var prefix = string.Concat(Enumerable.Repeat("../", templateId.Count(c => c == '/')));
        return RefMarkerPattern.Replace(templateBody, m =>
        {
            var ident = m.Groups["ident"].Value.Trim();
            var target = ResolveIdent(ident, refResult.LinksByDocId, refResult.LinksByName);
            if (target is null)
            {
                findings.Add(new RefGenFinding(
                    "REACTOR_DOC_REFMARKER_001",
                    $"<!-- ref:{ident} --> in template '{templateId}' does not resolve — rendered as inline code",
                    templateId,
                    TierLintSeverity.Warning));
                return $"`{MarkerShortName(ident)}`";
            }
            var href = prefix + target.RelativePath + (target.Anchor is null ? string.Empty : "#" + target.Anchor);
            return $"[{MarkerShortName(ident)}]({href})";
        });
    }

    /// <summary>
    /// Finds the reference location for a marker identifier. A full cref
    /// (it contains <c>:</c>) is looked up by doc id; anything else by short
    /// name, so <c>T:Foo.Bar</c> and <c>Bar</c> both work.
    /// </summary>
    internal static RefLocation? ResolveIdent(
        string ident,
        IReadOnlyDictionary<string, RefLocation> byDocId,
        IReadOnlyDictionary<string, RefLocation> byName)
    {
        if (ident.Contains(':'))
            return byDocId.TryGetValue(ident, out var exact) ? exact : null;
        if (byName.TryGetValue(ident, out var named)) return named;
        var shortName = MarkerShortName(ident);
        return byName.TryGetValue(shortName, out var fallback) ? fallback : null;
    }

    /// <summary>
    /// Reader-facing name for a marker: the trailing identifier, whether the
    /// author wrote a short name or a full cref.
    /// </summary>
    internal static string MarkerShortName(string ident)
    {
        var stem = ident;
        if (stem.Length >= 2 && stem[1] == ':') stem = stem[2..];
        var paren = stem.IndexOf('(');
        if (paren >= 0) stem = stem[..paren];
        var dot = stem.LastIndexOf('.');
        if (dot >= 0) stem = stem[(dot + 1)..];
        var tick = stem.IndexOf('`');
        if (tick > 0) stem = stem[..tick];
        return stem;
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

        // One finding for all of them: the map names many guide pages, and a
        // warning per page would bury the rest of the phase output.
        var orphans = declaredGuidePages
            .Distinct(StringComparer.Ordinal)
            .Where(p => templateSet.Contains(p) && !pagesWithInboundMarkers.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        if (orphans.Count == 0) yield break;
        var subject = orphans.Count == 1
            ? $"guide page '{orphans[0]}' has"
            : $"{orphans.Count} guide pages have";
        yield return new RefGenFinding(
            "REACTOR_DOC_REGISTRY_W002",
            $"{subject} no <!-- ref:Member --> markers pointing to the reference: {string.Join(", ", orphans)}",
            orphans.Count == 1 ? orphans[0] + ".md.dt" : "docs/_pipeline/reference-map.yaml",
            TierLintSeverity.Warning);
    }

    /// <summary>Turns a guide slug such as <c>data-system</c> into a title (<c>Data system</c>).</summary>
    internal static string ToTitle(string slug) =>
        string.IsNullOrEmpty(slug) ? slug
        : char.ToUpperInvariant(slug[0]) + slug[1..].Replace('-', ' ');

    internal static readonly Regex RefMarkerPattern = new(
        @"<!--\s*ref:\s*(?<ident>[^\s\-][^-]*?)\s*-->",
        RegexOptions.Compiled);
}

/// <summary>
/// One inbound ref-marker entry, keyed by the template id that contains it.
/// </summary>
internal sealed record TemplateReference(string TemplateId);