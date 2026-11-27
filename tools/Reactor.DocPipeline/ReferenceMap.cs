using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// One resolution result against the reference-map (spec 041 §10.4.1).
/// </summary>
/// <param name="Category">Semantic group (e.g. <c>hooks</c>, <c>factories</c>,
/// <c>charting</c>). The reference planner uses this to
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
internal sealed partial class ReferenceMap
{
    private readonly List<DefaultRule> _defaults;
    private readonly List<OverrideRule> _overrides;

    /// <summary>The reference categories in display order. When the YAML has no
    /// <c>categories:</c> list, one category is synthesized per distinct rule
    /// category id. When it has one, every rule must name a declared category
    /// (or <c>excluded</c>).</summary>
    public IReadOnlyList<RefCategory> Categories { get; private init; } = Array.Empty<RefCategory>();

    /// <summary>Control areas. Element records and factory methods are grouped onto one page per area.</summary>
    public IReadOnlyList<RefArea> Areas { get; private init; } = Array.Empty<RefArea>();

    /// <summary>General modifier areas, matched by method name in list order.</summary>
    public IReadOnlyList<RefModifierArea> ModifierAreas { get; private init; } = Array.Empty<RefModifierArea>();

    /// <summary>The dotted name of the type every element record derives from.</summary>
    public string ElementBase { get; private init; } = "Microsoft.UI.Reactor.Core.Element";

    public RefCategory? FindCategory(string id) =>
        Categories.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

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
    /// reference generator reports such a type as <c>REACTOR_DOC_REFGEN_004</c>.
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
                var category = o.Category ?? fallback?.Category ?? string.Empty;
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
                var category = o.Category ?? fallback?.Category ?? string.Empty;
                // An override that moves a member to another category and names no
                // guide pages keeps none: the default's pages belong to the old category.
                return new ReferenceMapMatch(category, o.GuidePages, "prefix-override");
            }
        }

        // 3) Defaults (most-specific wins).
        return ResolveDefault(stem) is { } d
            ? new ReferenceMapMatch(d.Category, d.GuidePages, "prefix-default")
            : null;
    }

    /// <summary>
    /// The category an override rule pins for <paramref name="cref"/>, or <c>null</c>
    /// when no override names one. The planner checks this before it applies its own
    /// rules (for example "every element record goes to the elements category"), so an
    /// override always wins.
    /// </summary>
    public string? OverrideCategory(string cref)
    {
        if (string.IsNullOrEmpty(cref)) return null;
        foreach (var o in _overrides)
        {
            if (o.Cref is not null && string.Equals(o.Cref, cref, StringComparison.Ordinal))
                return o.Category;
        }
        var stem = StripKindPrefix(cref);
        foreach (var o in _overrides)
        {
            if (o.Match is not null && o.Category is not null && PrefixMatches(stem, o.Match))
                return o.Category;
        }
        return null;
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
    /// when the file is malformed: an unknown key, an id that is not a slug, a
    /// rule that names an undeclared category, and so on.
    /// </summary>
    public static ReferenceMap Load(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        return Parse(yaml);
    }

    public static ReferenceMap Parse(string yaml)
    {
        // No IgnoreUnmatchedProperties: a misspelled key (`modifier_areas:`)
        // would otherwise drop its whole section without a word.
        var deserializer = new StaticDeserializerBuilder(new YamlStaticContext())
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
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
            RequireSlug(d.Category!, $"the category of the default rule for `{d.Match}`");
            defaults.Add(new DefaultRule(d.Match!, d.Category!, d.GuidePages ?? new List<string>()));
        }

        var overrides = new List<OverrideRule>();
        foreach (var o in shape.Overrides ?? new())
        {
            if (string.IsNullOrEmpty(o.Cref) && string.IsNullOrEmpty(o.Match))
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", "override rule must declare either `cref:` or `match:`");
            if (!string.IsNullOrEmpty(o.Category))
                RequireSlug(o.Category!, $"the category of the override for `{o.Cref ?? o.Match}`");
            overrides.Add(new OverrideRule(o.Cref, o.Match, string.IsNullOrEmpty(o.Category) ? null : o.Category, o.GuidePages ?? new List<string>()));
        }

        var categories = new List<RefCategory>();
        var seenCategories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in shape.Categories ?? new())
        {
            if (string.IsNullOrEmpty(c.Id))
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", "category is missing `id:`");
            RequireSlug(c.Id!, "category");
            if (!seenCategories.Add(c.Id!))
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", $"category `{c.Id}` is declared twice");
            if (string.IsNullOrEmpty(c.Title))
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", $"category `{c.Id}` is missing `title:`");
            var layout = string.IsNullOrEmpty(c.Layout) ? "types" : c.Layout!;
            if (Array.IndexOf(Layouts, layout) < 0)
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001",
                    $"category `{c.Id}` has layout `{layout}`; expected one of {string.Join(", ", Layouts)}");
            var groups = new List<RefGroup>();
            var seenGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in c.Groups ?? new())
            {
                if (string.IsNullOrEmpty(g.Id) || string.IsNullOrEmpty(g.Title))
                    throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", $"a group in category `{c.Id}` is missing `id:` or `title:`");
                RequireSlug(g.Id!, $"group (in category `{c.Id}`)");
                if (!seenGroups.Add(g.Id!))
                    throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", $"group `{g.Id}` is declared twice in category `{c.Id}`");
                groups.Add(new RefGroup(g.Id!, g.Title!, g.Summary?.Trim() ?? string.Empty,
                    g.Match ?? new List<string>(), g.Hooks ?? new List<string>()));
            }
            categories.Add(new RefCategory(c.Id!, c.Title!, c.Summary?.Trim() ?? string.Empty,
                c.Intro?.Trim() ?? string.Empty, layout, c.GuidePages ?? new List<string>(), groups));
        }
        var ruleCategories = defaults.Select(d => (Category: (string?)d.Category, Rule: d.Match))
            .Concat(overrides.Select(o => (o.Category, Rule: o.Cref ?? o.Match!)));
        if (categories.Count == 0)
        {
            // A map without `categories:` names its categories only through its rules.
            foreach (var (id, _) in ruleCategories)
            {
                if (id is null || id == ExcludedCategory || !seenCategories.Add(id)) continue;
                categories.Add(new RefCategory(id, char.ToUpperInvariant(id[0]) + id[1..].Replace('-', ' '),
                    string.Empty, string.Empty, id == "hooks" ? "hooks" : "types",
                    Array.Empty<string>(), Array.Empty<RefGroup>()));
            }
        }
        else
        {
            // With a declared list, an unknown id is a typo (`themeing`), not a new
            // top-level folder.
            foreach (var (id, rule) in ruleCategories)
            {
                if (id is null || id == ExcludedCategory || seenCategories.Contains(id)) continue;
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001",
                    $"the rule for `{rule}` names category `{id}`, which `categories:` does not declare");
            }
        }

        var areas = new List<RefArea>();
        foreach (var a in shape.Areas ?? new())
        {
            if (string.IsNullOrEmpty(a.Id) || string.IsNullOrEmpty(a.Title))
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", "an area is missing `id:` or `title:`");
            RequireSlug(a.Id!, "area");
            if (areas.Any(x => x.Id == a.Id))
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", $"area `{a.Id}` is declared twice");
            areas.Add(new RefArea(a.Id!, a.Title!, a.Summary?.Trim() ?? string.Empty,
                a.Elements ?? new List<string>(), a.Factories ?? new List<string>(), a.GuidePages ?? new List<string>()));
        }

        var modifierAreas = new List<RefModifierArea>();
        foreach (var a in shape.ModifierAreas ?? new())
        {
            if (string.IsNullOrEmpty(a.Id) || string.IsNullOrEmpty(a.Title))
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001", "a modifier area is missing `id:` or `title:`");
            RequireSlug(a.Id!, "modifier area");
            if (modifierAreas.Any(x => x.Id == a.Id) || areas.Any(x => x.Id == a.Id))
                throw new DocPipelineException("REACTOR_DOC_REGISTRY_001",
                    $"modifier area `{a.Id}` reuses an id; modifier area ids must differ from each other and from area ids");
            modifierAreas.Add(new RefModifierArea(a.Id!, a.Title!, a.Summary?.Trim() ?? string.Empty,
                a.Match ?? new List<string>(), a.GuidePages ?? new List<string>()));
        }

        return new ReferenceMap(defaults, overrides)
        {
            Categories = categories,
            Areas = areas,
            ModifierAreas = modifierAreas,
            ElementBase = string.IsNullOrEmpty(shape.ElementBase) ? "Microsoft.UI.Reactor.Core.Element" : shape.ElementBase!,
        };
    }

    /// <summary>The category id that keeps a type out of the reference.</summary>
    internal const string ExcludedCategory = "excluded";

    private static readonly string[] Layouts = { "types", "hooks", "elements", "factories", "modifiers" };

    /// <summary>
    /// Map ids become folder and file names under <c>docs/guide/reference</c>, so
    /// they must be slugs: runs of lowercase a–z and digits joined by single hyphens
    /// (<c>win2d</c> is one). Every id in the map has that shape. A dot, a slash or a
    /// capital is rejected here instead of reaching the disk.
    /// </summary>
    [GeneratedRegex(@"\A[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    private static void RequireSlug(string id, string what)
    {
        if (!SlugPattern().IsMatch(id))
            throw new DocPipelineException("REACTOR_DOC_REGISTRY_001",
                $"{what} id `{id}` is not a slug. Ids become file and folder names, so use lowercase letters and digits " +
                "joined by single hyphens, for example `focus-and-keyboard`");
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

    internal sealed class FileShape
    {
        public string? ElementBase { get; set; }
        public List<CategoryEntry>? Categories { get; set; }
        public List<AreaEntry>? Areas { get; set; }
        public List<ModifierAreaEntry>? ModifierAreas { get; set; }
        public List<DefaultEntry>? Defaults { get; set; }
        public List<OverrideEntry>? Overrides { get; set; }
    }

    internal sealed class CategoryEntry
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string? Intro { get; set; }
        public string? Layout { get; set; }
        public List<string>? GuidePages { get; set; }
        public List<GroupEntry>? Groups { get; set; }
    }

    internal sealed class GroupEntry
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public List<string>? Match { get; set; }
        public List<string>? Hooks { get; set; }
    }

    internal sealed class AreaEntry
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public List<string>? Elements { get; set; }
        public List<string>? Factories { get; set; }
        public List<string>? GuidePages { get; set; }
    }

    internal sealed class ModifierAreaEntry
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public List<string>? Match { get; set; }
        public List<string>? GuidePages { get; set; }
    }

    internal sealed class DefaultEntry
    {
        public string? Match { get; set; }
        public string? Category { get; set; }
        public List<string>? GuidePages { get; set; }
    }

    internal sealed class OverrideEntry
    {
        public string? Cref { get; set; }
        public string? Match { get; set; }
        public string? Category { get; set; }
        public List<string>? GuidePages { get; set; }
    }

    private sealed record DefaultRule(string Match, string Category, IReadOnlyList<string> GuidePages);

    private sealed record OverrideRule(string? Cref, string? Match, string? Category, IReadOnlyList<string> GuidePages);
}

/// <summary>One reference category: a folder under <c>docs/guide/reference/</c> with its own index page.</summary>
/// <param name="Layout"><c>types</c> (a page per type), <c>hooks</c> (a page per hook),
/// <c>elements</c>, <c>factories</c>, or <c>modifiers</c> (a page per area).</param>
internal sealed record RefCategory(
    string Id, string Title, string Summary, string Intro, string Layout,
    IReadOnlyList<string> GuidePages, IReadOnlyList<RefGroup> Groups);

/// <summary>Either a page that collects several small types (<paramref name="Match"/>)
/// or a section of the hooks index (<paramref name="Hooks"/>).</summary>
internal sealed record RefGroup(
    string Id, string Title, string Summary, IReadOnlyList<string> Match, IReadOnlyList<string> Hooks);

/// <summary>A control area such as layout or buttons. <paramref name="Elements"/> match an
/// element record name without its <c>Element</c> suffix; <paramref name="Factories"/> match
/// factory method names that do not return an element of the area.</summary>
internal sealed record RefArea(
    string Id, string Title, string Summary, IReadOnlyList<string> Elements,
    IReadOnlyList<string> Factories, IReadOnlyList<string> GuidePages);

/// <summary>A general modifier area, matched by modifier method name.</summary>
internal sealed record RefModifierArea(
    string Id, string Title, string Summary, IReadOnlyList<string> Match, IReadOnlyList<string> GuidePages);
