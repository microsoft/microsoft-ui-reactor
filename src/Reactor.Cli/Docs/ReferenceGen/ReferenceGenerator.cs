namespace Microsoft.UI.Reactor.Cli.Docs.ReferenceGen;

/// <summary>
/// One generator finding, mirroring <see cref="TierLintFinding"/>'s
/// shape so the orchestrator can route both to the same stderr formatter.
/// </summary>
internal sealed record RefGenFinding(
    string Code,
    string Message,
    string FilePath,
    TierLintSeverity Severity)
{
    public string Format() => $"{FilePath} {Code}: {Message}";
}

/// <summary>
/// Final result of one ref-gen run.
/// </summary>
internal sealed record ReferenceGenResult(
    IReadOnlyList<GeneratedPage> Pages,
    IReadOnlyList<RefGenFinding> Findings);

internal sealed record GeneratedPage(MemberDoc Member, RouterResult Route, string Body)
{
    /// <summary>
    /// Every member this page documents, in render order. Overloads share a
    /// short name and therefore a page; <see cref="Member"/> is the first of
    /// them and supplies the page's headline cref.
    /// </summary>
    public IReadOnlyList<MemberDoc> Members { get; init; } = new[] { Member };
}

/// <summary>
/// Orchestrates reference page generation:
///
/// <list type="number">
/// <item>Reads <c>Reactor.xml</c> from disk.</item>
/// <item>Groups by category via <see cref="MemberRouter"/>.</item>
/// <item>Filters to the categories requested by the caller (Phase 1B
///   restricts to <c>hooks</c>).</item>
/// <item>Routes each member to an output path; members sharing an output
///   path are merged onto one page, one section each.</item>
/// <item>Builds a <see cref="CrefResolver"/> over the routed set and
///   renders each page.</item>
/// </list>
///
/// Findings carry <c>REACTOR_DOC_REFGEN_001</c> (unresolved cref →
/// warning), <c>_REFGEN_002</c> (a page name claimed by two unrelated
/// declaring scopes → warning) and <c>_REFGEN_W001</c> (missing summary →
/// warning).
/// </summary>
internal sealed class ReferenceGenerator
{
    public ReferenceGenResult Generate(
        string xmlPath,
        ReferenceMap map,
        string referenceRoot,
        IReadOnlySet<string>? categoryAllowList = null)
    {
        var findings = new List<RefGenFinding>();
        var pages = new List<GeneratedPage>();

        if (!File.Exists(xmlPath))
        {
            findings.Add(new RefGenFinding(
                "REACTOR_DOC_REFGEN_003",
                $"XML doc file not found: {xmlPath}",
                xmlPath,
                TierLintSeverity.Error));
            return new ReferenceGenResult(pages, findings);
        }

        var members = XmlDocReader.Read(xmlPath);
        var router = new MemberRouter(map, referenceRoot);

        // Documented <typeparam> names, keyed by type cref, so a method's
        // `N markers can be rendered with the declaring type's own parameter
        // names rather than positional placeholders.
        // <typeparam> tags arrive in authoring order, not declaration order, and
        // this list is then indexed positionally against `0/`1 in FormatType.
        // A type documented <TResult> before <TInput> would therefore rename
        // every parameter into the wrong slot — and the heading it produces
        // feeds the anchor, so the damage outlives the signature. Same rule as
        // CrefSignature applies to method type parameters: a single name has no
        // order to get wrong, so it is used; past that, placeholders. See
        // TypeParamNamesOrPlaceholders.
        var typeParamsByType = members
            .Where(m => m.Kind == MemberKind.Type && m.TypeParams.Count > 0)
            .GroupBy(m => m.Cref, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => TypeParamNamesOrPlaceholders(g.Key, g.First().TypeParams),
                StringComparer.Ordinal);

        // 1) Route each member and bucket by output path. Every member that
        //    lands on a page is kept: collapsing overloads to a single
        //    "winner" silently deleted the rest from the docset.
        var groups = new Dictionary<string, PageGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in members)
        {
            var r = router.Route(m);
            if (r is null) continue;
            if (categoryAllowList is not null && !categoryAllowList.Contains(r.Category)) continue;

            // Constructors collapse to `#ctor` which collides catastrophically
            // across types. Phase 1B treats them as part of the parent type's
            // page (a later phase will emit dedicated overload subsections);
            // skip the standalone routing for now.
            if (r.ShortName.Equals("#ctor", StringComparison.Ordinal) ||
                r.ShortName.Equals("_ctor", StringComparison.Ordinal)) continue;

            if (!groups.TryGetValue(r.AbsolutePath, out var group))
                groups[r.AbsolutePath] = group = new PageGroup(r);
            group.Members.Add(m);
        }

        // 2) Order each group deterministically and assign headings/anchors.
        //    Ordering is derived entirely from the cref, never from the order
        //    members happen to appear in the XML file, so re-running the
        //    generator over an unchanged assembly reproduces byte-identical
        //    output.
        var routes = new Dictionary<string, RouterResult>(StringComparer.Ordinal);
        var anchors = new Dictionary<string, string>(StringComparer.Ordinal);
        var pageMembers = new Dictionary<string, List<PageMember>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups.Values.OrderBy(g => g.Route.AbsolutePath, StringComparer.Ordinal))
        {
            var ordered = group.Members
                .OrderBy(m => m.Kind == MemberKind.Type ? 0 : 1)
                .ThenBy(m => CrefSignature.Parse(m.Cref).DeclaringName, StringComparer.Ordinal)
                .ThenBy(m => CrefSignature.Parse(m.Cref).GenericArity)
                .ThenBy(m => CrefSignature.Parse(m.Cref).ParameterTypes.Count)
                .ThenBy(m => m.Cref, StringComparer.Ordinal)
                .ToList();

            var list = new List<PageMember>(ordered.Count);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in ordered)
            {
                routes[m.Cref] = group.Route;

                if (ordered.Count == 1)
                {
                    // Single-member page — the title is the heading.
                    list.Add(new PageMember(m, string.Empty, string.Empty));
                    continue;
                }

                var declaringCref = CrefSignature.DeclaringTypeCref(m.Cref);
                var declaringParams = declaringCref is not null &&
                                      typeParamsByType.TryGetValue(declaringCref, out var tps)
                    ? tps
                    : null;
                var heading = CrefSignature.Format(m, declaringParams);
                var anchor = Deduplicate(CrefSignature.Anchor(heading), used);
                anchors[m.Cref] = anchor;
                list.Add(new PageMember(m, heading, anchor));
            }
            pageMembers[group.Route.AbsolutePath] = list;

            // 3) REFGEN_002 now means "this page name is claimed by two
            //    unrelated declaring scopes" — a genuine ambiguity an author
            //    must resolve in reference-map.yaml. Overloads of one method
            //    are not ambiguous, they're the normal case, and they are all
            //    rendered. Ambiguous members are rendered too (dropping them
            //    is the bug this replaced) but still reported.
            var scopes = ordered
                .Select(DeclaringScope)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
            if (scopes.Count > 1)
            {
                findings.Add(new RefGenFinding(
                    "REACTOR_DOC_REFGEN_002",
                    $"name collision in category '{group.Route.Category}': '{group.Route.ShortName}' is claimed by " +
                    $"{scopes.Count} unrelated declaring scopes ({string.Join(", ", scopes)}); " +
                    "all members are rendered on one page — pin them to distinct categories in reference-map.yaml to separate them",
                    group.Route.RelativePath,
                    TierLintSeverity.Warning));
            }
        }

        // 4) Build the resolver over the routed set so <see cref> links
        //    only resolve to pages we're actually emitting.
        var resolver = new CrefResolver(routes, anchors);

        // 5) Render each page. Walk in output-path order for deterministic
        //    output (file diffs stay tidy across runs).
        foreach (var group in groups.Values.OrderBy(g => g.Route.AbsolutePath, StringComparer.Ordinal))
        {
            var list = pageMembers[group.Route.AbsolutePath];
            var route = group.Route;

            var write = ReferenceWriter.Write(list, route, resolver);
            pages.Add(new GeneratedPage(list[0].Member, route, write.Body)
            {
                Members = list.Select(pm => pm.Member).ToList(),
            });

            foreach (var cref in write.MissingSummaryCrefs)
            {
                findings.Add(new RefGenFinding(
                    "REACTOR_DOC_REFGEN_W001",
                    $"member has no <summary> — placeholder emitted ({cref})",
                    route.RelativePath,
                    TierLintSeverity.Warning));
            }
            foreach (var u in write.UnresolvedCrefs)
            {
                // In Phase 1B only the Hooks category emits pages, so most
                // cross-namespace crefs (Core, Input, System) are
                // legitimately outside the routed set. Downgrade to a
                // warning so the prototype completes; the canonical cref
                // check is the built-in CS1574. Crefs
                // pointing into other Reactor namespaces become resolvable
                // when later phases bring those categories online.
                findings.Add(new RefGenFinding(
                    "REACTOR_DOC_REFGEN_001",
                    $"unresolvable cref '{u.Cref}' in {u.InMemberCref}",
                    route.RelativePath,
                    TierLintSeverity.Warning));
            }
        }

        return new ReferenceGenResult(pages, findings);
    }

    /// <summary>
    /// The scope that owns a member for collision purposes: the declaring
    /// type for a member, or the type itself for a <c>T:</c> entry. Two
    /// members sharing a scope are overloads of one another; two members
    /// with different scopes that landed on the same page are a genuine
    /// naming ambiguity.
    /// </summary>
    private static string DeclaringScope(MemberDoc member)
    {
        var parts = CrefSignature.Parse(member.Cref);
        if (parts.Kind == "T")
            return string.IsNullOrEmpty(parts.DeclaringName)
                ? parts.Name
                : parts.DeclaringName + "." + parts.Name;
        return string.IsNullOrEmpty(parts.DeclaringName) ? parts.Name : parts.DeclaringName;
    }

    /// <summary>
    /// Positional type-parameter names for a generic type, or placeholders when
    /// the documented order cannot be proven.
    /// </summary>
    /// <remarks>
    /// <c>&lt;typeparam&gt;</c> tags are returned in the order the author wrote
    /// them; nothing in the cref carries declaration ordinals. A single
    /// documented name has no order to get wrong and is used as-is. From two
    /// upward the list is only <i>plausibly</i> ordered, and a wrong name is
    /// worse than a placeholder here because it is silently plausible — it
    /// renames real parameters into each other's slots and then feeds the
    /// heading, and therefore the anchor.
    /// </remarks>
    private static IReadOnlyList<string> TypeParamNamesOrPlaceholders(
        string cref, IReadOnlyList<ParamDoc> documented)
    {
        var arity = CrefSignature.Parse(cref).GenericArity;
        if (arity <= 0) arity = documented.Count;

        if (arity == 1 && documented.Count == 1 && !string.IsNullOrEmpty(documented[0].Name))
            return new[] { documented[0].Name };

        return Enumerable.Range(1, arity)
            .Select(i => "T" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
    }

    /// <summary>
    /// GitHub appends <c>-1</c>, <c>-2</c>, … to repeated heading slugs on a
    /// page; mirror that so generated anchors match what the renderer emits.
    /// </summary>
    private static string Deduplicate(string anchor, HashSet<string> used)
    {
        if (used.Add(anchor)) return anchor;
        for (int i = 1; ; i++)
        {
            var candidate = $"{anchor}-{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            if (used.Add(candidate)) return candidate;
        }
    }

    private sealed class PageGroup(RouterResult route)
    {
        public RouterResult Route { get; } = route;
        public List<MemberDoc> Members { get; } = new();
    }

    /// <summary>
    /// Write the generated pages to disk under <paramref name="outputRoot"/>.
    /// Creates category subdirectories as needed. Returns the list of
    /// absolute paths that were written.
    /// </summary>
    public List<string> WriteToDisk(ReferenceGenResult result, string outputRoot)
    {
        var written = new List<string>();
        foreach (var page in result.Pages)
        {
            var outPath = Path.Combine(outputRoot, "reference", page.Route.Category, Path.GetFileName(page.Route.AbsolutePath));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, CompileCommand.NormalizeLineEndings(page.Body));
            written.Add(outPath);
        }
        return written;
    }
}
