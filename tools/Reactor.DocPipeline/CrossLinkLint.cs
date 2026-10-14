using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// One cross-link lint finding (spec 041 §4.5 / §11). Same shape as
/// <see cref="TierLintFinding"/> so the compile loop can print both
/// uniformly.
/// </summary>
internal sealed record CrossLinkFinding(
    string Code,
    string Message,
    string FilePath,
    int Line,
    TierLintSeverity Severity)
{
    public string Format() => $"{FilePath}:{Line} {Code}: {Message}";
}

/// <summary>
/// One concept name that, if mentioned in prose, should link to a guide page.
/// </summary>
/// <param name="Phrase">Exact phrase to search for in prose (case-sensitive
/// outside the title-case fallback — see <see cref="CrossLinkLint"/>).</param>
/// <param name="TargetTopicId">The topic id (file stem under
/// <c>docs/_pipeline/templates</c>) that owns the concept. The analyzer skips
/// findings where the current template's topic id equals
/// <paramref name="TargetTopicId"/>.</param>
/// <param name="TargetHref">The href that a link to this concept should use.
/// Almost always <c>"{TargetTopicId}.md"</c>; spelled out for reference pages
/// that live under <c>reference/&lt;cat&gt;/Name.md</c>.</param>
internal sealed record CrossLinkConcept(string Phrase, string TargetTopicId, string TargetHref);

/// <summary>
/// One template participating in the cross-link sweep — supplies the topic id,
/// the prose body, and the source file for diagnostics.
/// </summary>
internal sealed record CrossLinkTemplate(string TopicId, string FilePath, string Body, string Title, IReadOnlyList<string> ConceptAliases);

/// <summary>
/// Cross-link analyzer (spec 041 §4.5, code <c>REACTOR_DOC_XLINK_001</c>).
/// For every prose mention of a concept that has its own guide page, emit a
/// finding unless the mention is already linked, inside code, or explicitly
/// opted out with a <c>&lt;!-- xlink:skip --&gt;</c> marker.
/// </summary>
internal static class CrossLinkLint
{
    /// <summary>
    /// Default severity for missed cross-links. Warning rather than Error on
    /// first roll-out so the docset doesn't break — elevated to Error after
    /// Phase 4.5 lands clean.
    /// </summary>
    internal const TierLintSeverity DefaultSeverity = TierLintSeverity.Warning;

    /// <summary>
    /// Lint a single template body against the concept registry. The
    /// <paramref name="concepts"/> list comes from <see cref="BuildRegistry"/>
    /// (called once per compile run by <see cref="Run"/>).
    /// </summary>
    public static List<CrossLinkFinding> Lint(
        CrossLinkTemplate template,
        IReadOnlyList<CrossLinkConcept> concepts)
    {
        var findings = new List<CrossLinkFinding>();
        if (string.IsNullOrEmpty(template.Body)) return findings;

        // Pre-filter the concept list: drop any concept whose target is this
        // template itself, so a page mentioning its own title doesn't trip.
        var ownTopic = template.TopicId;
        var ownTitle = template.Title?.Trim() ?? "";
        var ownAliases = new HashSet<string>(template.ConceptAliases ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var applicable = concepts
            .Where(c => !string.Equals(c.TargetTopicId, ownTopic, StringComparison.OrdinalIgnoreCase))
            .Where(c => !string.Equals(c.Phrase, ownTitle, StringComparison.OrdinalIgnoreCase))
            .Where(c => !ownAliases.Contains(c.Phrase))
            .ToList();
        if (applicable.Count == 0) return findings;

        // Compile a single regex that finds any concept phrase as a whole-word
        // match. We then disambiguate the matched concept by exact-phrase
        // lookup on the captured span. Phrases are sorted longest-first so
        // overlapping matches (e.g. "UseFocusTrap" vs "UseFocus") pick the
        // longer one.
        var phraseToConcepts = applicable
            .GroupBy(c => c.Phrase, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CrossLinkConcept>)g.ToList(), StringComparer.Ordinal);
        var sortedPhrases = phraseToConcepts.Keys
            .OrderByDescending(p => p.Length)
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToList();
        if (sortedPhrases.Count == 0) return findings;
        var bigPattern = "(?:" + string.Join("|", sortedPhrases.Select(Regex.Escape)) + ")";
        // Word-ish boundary: the matched phrase must not abut an
        // identifier/character that would make it a fragment of a longer
        // identifier. Markdown / English boundaries (whitespace, punctuation,
        // start/end of line) all qualify.
        var phraseRegex = new Regex(@"(?<![A-Za-z0-9_])" + bigPattern + @"(?![A-Za-z0-9_])",
            RegexOptions.Compiled);

        // Track which concepts have already been linked at least once (first-
        // mention-only rule).
        var alreadyLinked = new HashSet<string>(StringComparer.Ordinal);
        // Track which concepts have been flagged at least once on this page —
        // we only want to fire once per (page, concept) pair.
        var alreadyFlagged = new HashSet<string>(StringComparer.Ordinal);

        // First pass: walk all existing links and mark any concept that
        // appears inside link text as "already linked" (regardless of href —
        // an author may have linked elsewhere).
        foreach (Match link in LinkPattern.Matches(template.Body))
        {
            var linkText = link.Groups[1].Value;
            foreach (Match m in phraseRegex.Matches(linkText))
                alreadyLinked.Add(m.Value);
        }

        // Walk the body line-by-line, tracking state machine for: fenced
        // code blocks, ai:lock blocks, ai:caveat blocks, and per-paragraph
        // xlink:skip markers.
        var lines = template.Body.Replace("\r\n", "\n").Split('\n');
        var inFence = false;
        var inLock = false;
        var inCaveat = false;
        var paragraphSkipAll = false;
        var paragraphSkipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();

            // Blank line resets paragraph-scoped skip state.
            if (trimmed.Length == 0)
            {
                paragraphSkipAll = false;
                paragraphSkipNames.Clear();
                continue;
            }

            // Fenced code block toggle.
            if (trimmed.StartsWith("```"))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;

            // ai:lock / ai:caveat region toggle. We skip everything between
            // the open and close markers — those callouts are styled blocks
            // and the rule applies to body prose only.
            if (trimmed.Contains("<!-- ai:lock -->", StringComparison.OrdinalIgnoreCase))
                inLock = true;
            if (trimmed.Contains("<!-- /ai:lock -->", StringComparison.OrdinalIgnoreCase))
            { inLock = false; continue; }
            if (inLock) continue;

            if (trimmed.Contains("<!-- ai:caveat -->", StringComparison.OrdinalIgnoreCase))
                inCaveat = true;
            if (trimmed.Contains("<!-- /ai:caveat -->", StringComparison.OrdinalIgnoreCase))
            { inCaveat = false; continue; }
            if (inCaveat) continue;

            // Headings, blockquotes, and table rows are not body prose.
            if (trimmed.StartsWith('#')) continue;
            if (trimmed.StartsWith('>')) continue;
            if (trimmed.StartsWith('|')) continue;

            // Honor xlink:skip markers on this line. A bare marker silences
            // the rule for the rest of the paragraph; a quoted-phrase form
            // narrows the silence to a specific concept name.
            foreach (Match sk in XlinkSkipPattern.Matches(raw))
            {
                var arg = sk.Groups[1].Success ? sk.Groups[1].Value.Trim() : "";
                if (string.IsNullOrEmpty(arg)) paragraphSkipAll = true;
                else paragraphSkipNames.Add(arg);
            }
            if (paragraphSkipAll) continue;

            // Mask code spans and link text/href so concept matches don't
            // fire inside them. Replace with same-length filler so column
            // offsets stay stable.
            var masked = MaskCodeAndLinks(raw);

            foreach (Match m in phraseRegex.Matches(masked))
            {
                var phrase = m.Value;
                if (paragraphSkipNames.Contains(phrase)) continue;
                if (alreadyLinked.Contains(phrase)) continue;
                // Each (concept-target, phrase) only fires once per page.
                if (!phraseToConcepts.TryGetValue(phrase, out var matchedConcepts)) continue;
                var concept = matchedConcepts[0];
                var key = phrase + "→" + concept.TargetTopicId;
                if (!alreadyFlagged.Add(key)) continue;

                var msg = $"prose mentions '{phrase}' but does not link to {concept.TargetHref}";
                findings.Add(new CrossLinkFinding(
                    "REACTOR_DOC_XLINK_001",
                    msg,
                    template.FilePath,
                    i + 1,
                    DefaultSeverity));
            }
        }

        return findings;
    }

    /// <summary>
    /// Run the cross-link analyzer over every template in a compile. Builds
    /// the concept registry once from the union of (a) template titles,
    /// (b) <c>concept-aliases:</c> front-matter, and (c) the reference-map
    /// keys; then lints each template against it.
    /// </summary>
    public static List<CrossLinkFinding> Run(
        IReadOnlyList<CrossLinkTemplate> templates,
        IReadOnlyList<CrossLinkConcept> referenceConcepts)
    {
        var concepts = BuildRegistry(templates, referenceConcepts);
        var findings = new List<CrossLinkFinding>();
        foreach (var t in templates)
            findings.AddRange(Lint(t, concepts));
        return findings;
    }

    /// <summary>
    /// Build the concept registry from templates + reference-derived concepts.
    /// Public so tests can construct it manually.
    /// </summary>
    public static List<CrossLinkConcept> BuildRegistry(
        IReadOnlyList<CrossLinkTemplate> templates,
        IReadOnlyList<CrossLinkConcept> referenceConcepts)
    {
        var list = new List<CrossLinkConcept>();
        var seen = new HashSet<(string Phrase, string Topic)>(new PhraseTopicComparer());

        foreach (var t in templates)
        {
            var href = t.TopicId.Contains('/')
                // Subfolder templates (e.g. recipes/login) → relative href
                // matches the topic id structure preserved on disk.
                ? Path.GetFileName(t.TopicId) + ".md"
                : t.TopicId + ".md";
            var title = t.Title?.Trim() ?? "";
            // Single-capitalized-word titles (e.g. "Reactor", "Hooks", "Forms")
            // collide with everyday English usage of the same word and
            // produce a flood of false positives. Require either an
            // identifier-shape signal (internal uppercase, e.g. "DataGrid")
            // or a multi-word phrase before auto-registering the title.
            // Authors can still opt single-word titles in via concept-aliases.
            if (title.Length > 0 && IsConceptShape(title))
                Add(title, t.TopicId, href);
            foreach (var alias in t.ConceptAliases ?? Array.Empty<string>())
            {
                var trimmed = alias.Trim();
                if (trimmed.Length > 0) Add(trimmed, t.TopicId, href);
            }
        }

        // API concepts (from reference-map, with hrefs pre-formed by the
        // caller) come last so template-owned phrases win on collision.
        // Same identifier-shape filter — single-word reference-page names
        // like "Focus", "Error", "Reset" overlap with common English verbs
        // and would dominate the finding list. Multi-camelCase identifiers
        // (UseFocusTrap, DataGrid) are kept.
        foreach (var c in referenceConcepts)
            if (IsConceptShape(c.Phrase))
                Add(c.Phrase, c.TargetTopicId, c.TargetHref);

        return list;

        void Add(string phrase, string topicId, string href)
        {
            if (string.IsNullOrWhiteSpace(phrase)) return;
            if (!seen.Add((phrase, topicId))) return;
            list.Add(new CrossLinkConcept(phrase, topicId, href));
        }
    }

    /// <summary>
    /// Heuristic that decides whether <paramref name="phrase"/> is unambiguously
    /// identifier-shaped enough to auto-register as a cross-link concept.
    /// Multi-word phrases qualify; single words qualify only when they have
    /// an internal uppercase (e.g. <c>UseFocusTrap</c>, <c>DataGrid</c>,
    /// <c>OpenWindow</c>) — that signal is what disambiguates an API name
    /// from an English noun (<c>Focus</c>, <c>Reactor</c>, <c>Hooks</c>).
    /// Authors can still opt single-word concepts in by declaring them as
    /// <c>concept-aliases:</c> entries on the owning template.
    /// </summary>
    internal static bool IsConceptShape(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return false;
        var trimmed = phrase.Trim();
        if (trimmed.Contains(' ') || trimmed.Contains('-')) return true;
        // Single word — require an internal uppercase to count as
        // identifier-shaped. The first character does not count.
        for (int i = 1; i < trimmed.Length; i++)
        {
            if (char.IsUpper(trimmed[i])) return true;
        }
        return false;
    }

    private sealed class PhraseTopicComparer : IEqualityComparer<(string Phrase, string Topic)>
    {
        public bool Equals((string Phrase, string Topic) x, (string Phrase, string Topic) y) =>
            string.Equals(x.Phrase, y.Phrase, StringComparison.Ordinal) &&
            string.Equals(x.Topic, y.Topic, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Phrase, string Topic) obj) =>
            HashCode.Combine(obj.Phrase, obj.Topic.ToLowerInvariant());
    }

    // ── Masking helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Replace inline-code spans (<c>`…`</c>), Markdown links (<c>[text](href)</c>),
    /// and HTML comments with spaces of the same length so concept matches
    /// inside them are suppressed but column positions stay stable.
    /// </summary>
    internal static string MaskCodeAndLinks(string line)
    {
        var chars = line.ToCharArray();

        // HTML comments — drop anything between <!-- and -->. They may span
        // lines in general but here we mask whatever lands on this line.
        MaskRegex(HtmlCommentPattern, chars);
        // Inline code first — the cheapest mask and most common.
        MaskRegex(InlineCodePattern, chars);
        // Markdown links — both the [text] and (href) get masked because the
        // body of the link is rendered output and we don't want to demand
        // the link text re-link itself.
        MaskRegex(LinkPattern, chars);
        // Bare URLs / autolinks <https://…>
        MaskRegex(AutolinkPattern, chars);

        return new string(chars);
    }

    private static void MaskRegex(Regex pattern, char[] chars)
    {
        foreach (Match m in pattern.Matches(new string(chars)))
        {
            for (int i = m.Index; i < m.Index + m.Length && i < chars.Length; i++)
                chars[i] = ' ';
        }
    }

    private static readonly Regex InlineCodePattern = new(@"`[^`\n]+`", RegexOptions.Compiled);
    private static readonly Regex LinkPattern = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex AutolinkPattern = new(@"<https?://[^>\s]+>", RegexOptions.Compiled);
    private static readonly Regex HtmlCommentPattern = new(@"<!--.*?-->", RegexOptions.Compiled);

    // <!-- xlink:skip -->            → silence whole paragraph
    // <!-- xlink:skip "Phrase" -->   → silence just one phrase
    private static readonly Regex XlinkSkipPattern = new(
        @"<!--\s*xlink:skip(?:\s+""([^""]+)"")?\s*-->", RegexOptions.Compiled);
}
