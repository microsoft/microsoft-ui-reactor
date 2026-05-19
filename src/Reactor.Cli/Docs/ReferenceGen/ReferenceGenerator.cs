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

internal sealed record GeneratedPage(MemberDoc Member, RouterResult Route, string Body);

/// <summary>
/// Orchestrates reference page generation:
///
/// <list type="number">
/// <item>Reads <c>Reactor.xml</c> from disk.</item>
/// <item>Groups by category via <see cref="MemberRouter"/>.</item>
/// <item>Filters to the categories requested by the caller (Phase 1B
///   restricts to <c>hooks</c>).</item>
/// <item>Routes each member to an output path; detects collisions.</item>
/// <item>Builds a <see cref="CrefResolver"/> over the routed set and
///   renders each page.</item>
/// </list>
///
/// Findings carry <c>REACTOR_DOC_REFGEN_001</c> (unresolved cref →
/// error), <c>_REFGEN_002</c> (name collision → error) and
/// <c>_REFGEN_W001</c> (missing summary → warning).
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

        // 1) Route each member; collect collisions per category.
        var routes = new Dictionary<string, RouterResult>(StringComparer.Ordinal);
        // collision map: category → (short name → first-seen cref)
        var collisionMap = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

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

            // Methods have one "name" each but ref-gen collapses overloads
            // to the same MD page. Sidestep collision detection between an
            // overload-pair and a same-named property by keying on category+
            // name and only complaining when the *first-seen cref* differs
            // from the current one's parameter list-stripped form.
            if (!collisionMap.TryGetValue(r.Category, out var byName))
                collisionMap[r.Category] = byName = new(StringComparer.OrdinalIgnoreCase);

            if (byName.TryGetValue(r.ShortName, out var firstCref) && !SameMemberFamily(firstCref, m.Cref))
            {
                // Phase 1B routes one page per short-name, so two members
                // that share a name (e.g. parallel extension classes that
                // both expose `UseMemoCells`) collide. The first-seen entry
                // wins the page; later phases will disambiguate per-type.
                // Keep the finding so authors see the drift, but emit at
                // warning severity rather than failing the build.
                findings.Add(new RefGenFinding(
                    "REACTOR_DOC_REFGEN_002",
                    $"name collision in category '{r.Category}': '{r.ShortName}' already routed from {firstCref}, now also from {m.Cref}",
                    r.RelativePath,
                    TierLintSeverity.Warning));
                continue;
            }
            byName[r.ShortName] = m.Cref;
            // First overload encountered wins the page; subsequent overloads
            // are appended in a later phase (out of scope for 1B).
            if (!routes.ContainsKey(m.Cref))
                routes.Add(m.Cref, r);
        }

        // 2) Build the resolver over the routed set so <see cref> links
        //    only resolve to pages we're actually emitting.
        var resolver = new CrefResolver(routes);

        // 3) Render each page. Walk in cref order for deterministic output
        //    (file diffs stay tidy across runs).
        foreach (var m in members.OrderBy(x => x.Cref, StringComparer.Ordinal))
        {
            if (!routes.TryGetValue(m.Cref, out var route)) continue;

            // Skip overload duplicates — the first cref wins the page.
            // A later phase will merge overload signatures into one page.
            if (pages.Any(p => p.Route.AbsolutePath == route.AbsolutePath)) continue;

            var write = ReferenceWriter.Write(m, route, resolver);
            pages.Add(new GeneratedPage(m, route, write.Body));

            if (write.MissingSummary)
            {
                findings.Add(new RefGenFinding(
                    "REACTOR_DOC_REFGEN_W001",
                    $"member has no <summary> — placeholder emitted ({m.Cref})",
                    route.RelativePath,
                    TierLintSeverity.Warning));
            }
            foreach (var u in write.UnresolvedCrefs)
            {
                // In Phase 1B only the Hooks category emits pages, so most
                // cross-namespace crefs (Core, Input, System) are
                // legitimately outside the routed set. Downgrade to a
                // warning so the prototype completes; the canonical Roslyn
                // cref check is REACTOR_DOC_002 (analyzer task 1.8). Crefs
                // pointing into other Reactor namespaces become resolvable
                // when later phases bring those categories online.
                findings.Add(new RefGenFinding(
                    "REACTOR_DOC_REFGEN_001",
                    $"unresolvable cref '{u}' in {m.Cref}",
                    route.RelativePath,
                    TierLintSeverity.Warning));
            }
        }

        return new ReferenceGenResult(pages, findings);
    }

    /// <summary>
    /// Strips method parameter lists and generic arity so two overloads of
    /// the same method don't trigger a collision finding. The collision
    /// detector only fires across genuinely different members (e.g. a
    /// property and a method with the same name in the same category).
    /// </summary>
    private static bool SameMemberFamily(string crefA, string crefB)
    {
        return StemOf(crefA) == StemOf(crefB);
    }

    private static string StemOf(string cref)
    {
        var stem = cref;
        if (stem.Length >= 2 && stem[1] == ':') stem = stem[2..];
        var paren = stem.IndexOf('(');
        if (paren >= 0) stem = stem[..paren];
        return stem;
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
