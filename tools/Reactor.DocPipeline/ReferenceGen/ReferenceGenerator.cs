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
/// Where a type or member is documented: a page path relative to
/// <c>docs/guide</c>, and the heading anchor on that page (null for the top
/// of the page).
/// </summary>
internal sealed record RefLocation(string RelativePath, string? Anchor, string Label);

/// <summary>One generated reference page.</summary>
internal sealed record ReferencePage(
    string RelativePath,
    string? CategoryId,
    string Title,
    string Summary,
    string Body,
    PageKind Kind);

/// <summary>Final result of one reference generation run.</summary>
internal sealed record ReferenceGenResult(
    IReadOnlyList<ReferencePage> Pages,
    IReadOnlyList<RefGenFinding> Findings)
{
    /// <summary>Where every documented type and member landed, keyed by doc id.</summary>
    public IReadOnlyDictionary<string, RefLocation> LinksByDocId { get; init; } =
        new Dictionary<string, RefLocation>(StringComparer.Ordinal);

    /// <summary>Short names (hook, type, factory and modifier names) that <c>&lt;!-- ref:X --&gt;</c> markers can use.</summary>
    public IReadOnlyDictionary<string, RefLocation> LinksByName { get; init; } =
        new Dictionary<string, RefLocation>(StringComparer.Ordinal);

    /// <summary>The <c>nav.json</c> groups for the generated pages, as a JSON array.</summary>
    public string NavFragment { get; init; } = "[]\n";

    /// <summary>Public types that no category in <c>reference-map.yaml</c> matched.</summary>
    public IReadOnlyList<string> Uncategorized { get; init; } = Array.Empty<string>();

    /// <summary>Every guide page the map or the generated pages link to.</summary>
    public IReadOnlyList<string> DeclaredGuidePages { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Builds the API reference:
///
/// <list type="number">
/// <item>Reads <c>Microsoft.UI.Reactor.xml</c>, and the public surface from <c>Microsoft.UI.Reactor.dll</c>
///   when it sits next to the XML file (<see cref="ApiModel.FromAssembly"/>).
///   Without the assembly the surface is taken from the XML file alone.</item>
/// <item>Assigns every type and member to a page with
///   <see cref="ReferencePlanner"/>: one page per type, one per hook, and area
///   pages for element records, factory methods and modifiers.</item>
/// <item>Renders the pages with <see cref="ReferenceRenderer"/>, which
///   resolves crefs to page and heading-anchor links.</item>
/// </list>
///
/// Findings: <c>REACTOR_DOC_REFGEN_001</c> (a Reactor cref that does not
/// resolve), <c>_REFGEN_003</c> (no XML file), <c>_REFGEN_004</c> (a public
/// type that no category matches), <c>_REFGEN_W001</c> (missing summary) and
/// <c>_REFGEN_W003</c> (a guide page that has no template).
/// </summary>
internal sealed class ReferenceGenerator
{
    /// <summary>Files that contain this marker are written by hand and never touched.</summary>
    internal const string KeepMarker = "<!-- reference:keep -->";

    public ReferenceGenResult Generate(
        string xmlPath,
        ReferenceMap map,
        string? dllPath = null,
        IReadOnlySet<string>? templateIds = null,
        IReadOnlyDictionary<string, IReadOnlyList<TemplateReference>>? reverseIndex = null,
        Func<string, string?>? guideTitle = null)
    {
        if (!File.Exists(xmlPath))
        {
            return new ReferenceGenResult(Array.Empty<ReferencePage>(), new[]
            {
                new RefGenFinding("REACTOR_DOC_REFGEN_003", $"XML doc file not found: {xmlPath}", xmlPath, TierLintSeverity.Error),
            });
        }

        var docs = XmlDocReader.Read(xmlPath);
        ApiModel? model = null;
        if (dllPath is not null && File.Exists(dllPath))
            model = ApiModel.FromAssembly(dllPath);
        return Generate(docs, map, model, templateIds, reverseIndex, guideTitle);
    }

    public ReferenceGenResult Generate(
        IReadOnlyList<MemberDoc> docs,
        ReferenceMap map,
        ApiModel? model = null,
        IReadOnlySet<string>? templateIds = null,
        IReadOnlyDictionary<string, IReadOnlyList<TemplateReference>>? reverseIndex = null,
        Func<string, string?>? guideTitle = null)
    {
        model ??= ApiModel.FromXml(docs);
        var docsByCref = new Dictionary<string, MemberDoc>(StringComparer.Ordinal);
        foreach (var d in docs) docsByCref.TryAdd(d.Cref, d);

        var plan = ReferencePlanner.Plan(model, map);
        var result = new ReferenceRenderer(plan, docsByCref, templateIds, reverseIndex, guideTitle).Render();

        var findings = result.Findings.ToList();
        foreach (var docId in result.Uncategorized)
        {
            findings.Add(new RefGenFinding(
                "REACTOR_DOC_REFGEN_004",
                $"public type {docId} matches no category in reference-map.yaml, so it has no reference page",
                "docs/_pipeline/reference-map.yaml",
                TierLintSeverity.Warning));
        }
        return result with { Findings = findings };
    }

    /// <summary>
    /// Writes the pages under <paramref name="guideRoot"/> (the
    /// <c>docs/guide</c> folder). The generator owns <c>reference/</c>: a
    /// Markdown file there that this run did not produce is deleted, and so
    /// are folders left empty. Files that contain <see cref="KeepMarker"/>
    /// are never written or deleted. Unchanged files are not rewritten.
    /// </summary>
    /// <returns>The number of files written and deleted.</returns>
    public static (int Written, int Deleted) WriteToDisk(ReferenceGenResult result, string guideRoot)
    {
        var referenceRoot = Path.Combine(guideRoot, "reference");
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int written = 0, deleted = 0;

        foreach (var page in result.Pages.OrderBy(p => p.RelativePath, StringComparer.Ordinal))
        {
            var path = Path.GetFullPath(Path.Combine(guideRoot, page.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            expected.Add(path);
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path);
                if (existing.Contains(KeepMarker, StringComparison.Ordinal)) continue;
                var body = CompileCommand.NormalizeLineEndings(page.Body);
                // On a case-insensitive file system, writing `pending.md` over
                // `Pending.md` keeps the old casing. Delete first so the name on
                // disk is always the planned one.
                var differentCase = !string.Equals(OnDiskName(path), Path.GetFileName(path), StringComparison.Ordinal);
                if (existing == body && !differentCase) continue;
                if (differentCase) File.Delete(path);
                File.WriteAllText(path, body);
                written++;
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, CompileCommand.NormalizeLineEndings(page.Body));
            written++;
        }

        if (!Directory.Exists(referenceRoot)) return (written, deleted);

        foreach (var file in Directory.EnumerateFiles(referenceRoot, "*.md", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var full = Path.GetFullPath(file);
            if (expected.Contains(full)) continue;
            if (File.ReadAllText(full).Contains(KeepMarker, StringComparison.Ordinal)) continue;
            File.Delete(full);
            deleted++;
        }

        foreach (var dir in Directory.EnumerateDirectories(referenceRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length).ThenBy(d => d, StringComparer.Ordinal))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        return (written, deleted);
    }

    /// <summary>The file name as the file system stores it, which may differ in case from <paramref name="path"/>.</summary>
    private static string OnDiskName(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        return Directory.EnumerateFiles(dir, name)
            .Select(Path.GetFileName)
            .FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? name;
    }
}
