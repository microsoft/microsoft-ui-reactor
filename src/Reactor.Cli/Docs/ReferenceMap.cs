using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// One resolution result against the reference-map (spec 041 §10.4.1).
/// </summary>
/// <param name="Category">Semantic group (e.g. <c>hooks</c>, <c>factories</c>,
/// <c>charting</c>). The <see cref="ReferenceGen.MemberRouter"/> uses this to
/// pick the output sub-folder.</param>
/// <param name="GuidePages">Zero-or-more conceptual guide pages this member
/// should cross-link into. Empty when no rule produced a mapping.</param>
/// <param name="MatchedRule">Diagnostic-only — which rule fired (cref-override,
/// prefix-override, or prefix-default).</param>
internal sealed record ReferenceMapMatch(string Category, IReadOnlyList<string> GuidePages, string MatchedRule);

/// <summary>
/// YAML-backed registry resolving a member cref to a category + guide-page
/// set. Implements the most-specific-wins ordering from spec 041 §10.4.1:
/// exact override beats prefix override beats prefix default; among prefix
/// matches the longer prefix wins.
/// </summary>
internal sealed class ReferenceMap
{
    private readonly List<DefaultRule> _defaults;
    private readonly List<OverrideRule> _overrides;

    private ReferenceMap(List<DefaultRule> defaults, List<OverrideRule> overrides)
    {
        // Sort by descending prefix length so the first match in iteration
        // order is the most-specific one. Exact-cref overrides stay separate
        // (handled before any prefix walk).
        _defaults = defaults
            .OrderByDescending(d => PrefixLength(d.Match))
            .ToList();
        _overrides = overrides
            .OrderByDescending(o => o.Cref is null ? PrefixLength(o.Match!) : int.MaxValue)
            .ToList();
    }

    /// <summary>
    /// Resolve a canonical cref (e.g. <c>M:Microsoft.UI.Reactor.Hooks.UseState.SetValue</c>)
    /// to category + guide-pages. Returns <c>null</c> when no rule matches; the
    /// caller should surface a <c>REACTOR_DOC_REGISTRY_W001</c> warning.
    /// </summary>
    public ReferenceMapMatch? Resolve(string cref)
    {
        if (string.IsNullOrEmpty(cref)) return null;
        // Strip the kind prefix (T:/M:/P:/F:/E:) so registry matches operate on
        // the dotted-name portion alone — author-friendly and the spec example
        // form. Exact-cref overrides keep the prefix because the spec's example
        // pins specific members with the full canonical form.
        var stem = StripKindPrefix(cref);

        // 1) Exact cref overrides.
        foreach (var o in _overrides)
        {
            if (o.Cref is not null && string.Equals(o.Cref, cref, StringComparison.Ordinal))
            {
                // An override may pin only guide-pages; if so, inherit the
                // category from the matching default rule (or leave blank).
                var fallback = ResolveDefault(stem);
                var category = fallback?.Category ?? string.Empty;
                return new ReferenceMapMatch(category, o.GuidePages, "cref-override");
            }
        }

        // 2) Prefix overrides (most-specific wins; list is pre-sorted).
        foreach (var o in _overrides)
        {
            if (o.Match is null) continue;
            if (PrefixMatches(stem, o.Match))
            {
                var fallback = ResolveDefault(stem);
                var category = fallback?.Category ?? string.Empty;
                return new ReferenceMapMatch(category, o.GuidePages, "prefix-override");
            }
        }

        // 3) Defaults (most-specific wins).
        return ResolveDefault(stem) is { } d
            ? new ReferenceMapMatch(d.Category, d.GuidePages, "prefix-default")
            : null;
    }

    private (string Category, IReadOnlyList<string> GuidePages)? ResolveDefault(string stem)
    {
        foreach (var d in _defaults)
        {
            if (PrefixMatches(stem, d.Match))
                return (d.Category, d.GuidePages);
        }
        return null;
    }

    // ── Loading ───────────────────────────────────────────────────────────

    /// <summary>
    /// Load and validate <c>reference-map.yaml</c>. Throws
    /// <see cref="DocPipelineException"/> with code <c>REACTOR_DOC_REGISTRY_001</c>
    /// when the file is malformed.
    /// </summary>
    public static ReferenceMap Load(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        return Parse(yaml);
    }

    public static ReferenceMap Parse(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        FileShape? shape;
        try
        {
            shape = deserializer.Deserialize<FileShape>(yaml);
        }
        catch (Exception ex)
        {
            throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", $"reference-map.yaml is malformed: {ex.Message}");
        }
        shape ??= new FileShape();

        var defaults = new List<DefaultRule>();
        foreach (var d in shape.Defaults ?? new())
        {
            if (string.IsNullOrEmpty(d.Match))
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", "default rule is missing `match:` glob");
            if (string.IsNullOrEmpty(d.Category))
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", $"default rule for `{d.Match}` is missing `category:`");
            defaults.Add(new DefaultRule(d.Match!, d.Category!, d.GuidePages ?? new List<string>()));
        }

        var overrides = new List<OverrideRule>();
        foreach (var o in shape.Overrides ?? new())
        {
            if (string.IsNullOrEmpty(o.Cref) && string.IsNullOrEmpty(o.Match))
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", "override rule must declare either `cref:` or `match:`");
            overrides.Add(new OverrideRule(o.Cref, o.Match, o.GuidePages ?? new List<string>()));
        }

        return new ReferenceMap(defaults, overrides);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Glob with zero or more <c>*</c> wildcards. <c>*</c> matches any run of
    /// characters (including dots). Used in registry rules — the supported
    /// shapes are exact (<c>Foo.Bar</c>), trailing-wildcard
    /// (<c>Foo.Bar.*</c>), and prefix-with-infix
    /// (<c>Foo.Bar.*Chart*</c>) — anything composable from literal segments
    /// separated by <c>*</c>.
    /// </summary>
    internal static bool PrefixMatches(string dottedName, string glob)
    {
        if (string.IsNullOrEmpty(glob)) return false;
        if (!glob.Contains('*'))
            return string.Equals(dottedName, glob, StringComparison.Ordinal);

        // Split on `*`; each piece must appear in `dottedName` in order. The
        // first piece must anchor at position 0 unless the glob starts with
        // `*`; likewise the final piece must reach end-of-string unless the
        // glob ends with `*`.
        var parts = glob.Split('*');
        int pos = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0) continue;
            if (i == 0)
            {
                // Anchored start.
                if (!dottedName.AsSpan(pos).StartsWith(part, StringComparison.Ordinal))
                    return false;
                pos += part.Length;
            }
            else if (i == parts.Length - 1 && !glob.EndsWith("*"))
            {
                // Anchored end.
                return dottedName.EndsWith(part, StringComparison.Ordinal)
                    && dottedName.Length - part.Length >= pos;
            }
            else
            {
                var idx = dottedName.IndexOf(part, pos, StringComparison.Ordinal);
                if (idx < 0) return false;
                pos = idx + part.Length;
            }
        }
        return true;
    }

    private static int PrefixLength(string glob)
    {
        // Length used for ordering; substring globs (containing `*` not just
        // at the trailing position) come last because they're least specific.
        // A trailing-wildcard glob's specificity is the literal portion before
        // the first `*`; a glob with interior wildcards is ranked by total
        // literal length so e.g. `Foo.Bar.*Chart*` (8 literal chars after the
        // anchored prefix) beats a bare `Foo.Bar.*` (no interior literal).
        if (string.IsNullOrEmpty(glob)) return 0;
        if (!glob.Contains('*')) return glob.Length + 100; // exact wins outright
        var literal = glob.Replace("*", "").Length;
        var leadingLiteral = glob.IndexOf('*');
        // Lead-anchored globs (e.g. `Foo.Bar.*`) rank above lead-unanchored
        // globs (`*Chart`) of the same total literal length.
        return literal + (leadingLiteral > 0 ? 1 : 0);
    }

    private static string StripKindPrefix(string cref)
    {
        if (cref.Length >= 2 && cref[1] == ':' && "TMPFE".IndexOf(cref[0]) >= 0)
            return cref[2..];
        return cref;
    }

    // ── YAML shape ────────────────────────────────────────────────────────

    private sealed class FileShape
    {
        public List<DefaultEntry>? Defaults { get; set; }
        public List<OverrideEntry>? Overrides { get; set; }
    }

    private sealed class DefaultEntry
    {
        public string? Match { get; set; }
        public string? Category { get; set; }
        public List<string>? GuidePages { get; set; }
    }

    private sealed class OverrideEntry
    {
        public string? Cref { get; set; }
        public string? Match { get; set; }
        public List<string>? GuidePages { get; set; }
    }

    private sealed record DefaultRule(string Match, string Category, IReadOnlyList<string> GuidePages);

    private sealed record OverrideRule(string? Cref, string? Match, IReadOnlyList<string> GuidePages);
}
