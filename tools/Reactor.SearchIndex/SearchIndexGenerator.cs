// Reactor.SearchIndex — builds samples/ReactorGallery/reactor-search-index.json by
// parsing the ReactorGallery *source* (ControlRegistry.cs + PageRouter.cs +
// ControlPages/**) with Roslyn and merging a hand-curated editorial sidecar.
//
// The output is a pure, deterministic function of that source + the editorial file:
// controls sorted by id, fixed key order, LF newlines, stable formatting, and NO
// volatile value (sha/timestamp) baked in. tests/Reactor.Tests drives Generate(...)
// in-process and asserts byte-equality with the committed file (the staleness gate).

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.SearchIndex;

public static partial class SearchIndexGenerator
{
    public const int SchemaVersion = 1;
    public const string Source = "reactor";
    public const string GeneratedFrom = "microsoft/microsoft-ui-reactor";
    public const string DefaultApiNamespace = "Microsoft.UI.Reactor";
    public const string DefaultNugetPackage = "Microsoft.UI.Reactor";

    // The only "expected" skip reason: an intentional editorial exclude:true. Any other
    // skip (no route / no clean sample) is a silent drop and fails generation.
    const string ExcludeReason = "editorial-exclude";

    /// <summary>
    /// Produces the deterministic index text (and diagnostics) from the gallery source
    /// directory (the folder holding ControlRegistry.cs / PageRouter.cs / ControlPages)
    /// and the editorial sidecar JSON. A missing/blank editorial path is treated as an empty
    /// sidecar, which then fails generation because every included control requires curated
    /// keywords — a full run needs a real editorial.json.
    /// </summary>
    /// <param name="agentKitRoot">
    /// Directory holding the shipped agent kit (<c>SKILL.md</c>, <c>plugins/</c>, <c>skills/</c>)
    /// — i.e. the repo root. Marked <c>&lt;!-- index:id --&gt;</c> blocks found there become the
    /// matching control's <c>details</c>. Null/missing means "no markers", which the
    /// synthetic-gallery tests rely on.
    /// </param>
    public static SearchIndexResult Generate(string galleryDir, string? editorialPath, string? agentKitRoot = null)
    {
        var registry = ParseRegistry(Path.Join(galleryDir, "ControlRegistry.cs"));
        var routes = ParseRouter(Path.Join(galleryDir, "PageRouter.cs"));
        var samplesByClass = ParseSamples(Path.Join(galleryDir, "ControlPages"));
        var editorial = LoadEditorial(editorialPath);
        var details = ParseAgentKitDetails(agentKitRoot);

        var registryIds = new HashSet<string>(registry.Select(r => r.Id), StringComparer.Ordinal);
        var orphanKeys = editorial.Keys.Where(k => !registryIds.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (orphanKeys.Count > 0)
            throw new InvalidOperationException(
                "editorial.json has key(s) that match no control id (typo?): " + string.Join(", ", orphanKeys));

        // Same no-silent-drops rule for the prose markers: a block whose id does not name a
        // control is prose nobody will ever read, and is almost always a typo'd id.
        var orphanMarkers = details.Keys.Where(k => !registryIds.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (orphanMarkers.Count > 0)
            throw new InvalidOperationException(
                "agent-kit `<!-- index:id -->` marker(s) match no control id (typo?): " + string.Join(", ", orphanMarkers));

        var entries = new List<ControlEntry>();
        var skipped = new List<SkippedControl>();
        var missingKeywords = new List<string>();

        foreach (var reg in registry)
        {
            editorial.TryGetValue(reg.Id, out var ed);

            if (ed?.Exclude == true)
            {
                skipped.Add(new SkippedControl(reg.Id, reg.Name, ExcludeReason));
                continue;
            }

            if (!routes.TryGetValue(reg.Id, out var pageClass))
            {
                skipped.Add(new SkippedControl(reg.Id, reg.Name, "no-route"));
                continue;
            }

            samplesByClass.TryGetValue(pageClass, out var extracted);
            var samples = ResolveSamples(extracted, ed?.SampleOverride);
            if (samples.Count == 0)
            {
                skipped.Add(new SkippedControl(reg.Id, reg.Name, $"no-sample ({pageClass})"));
                continue;
            }

            var keywords = NormalizeKeywords(ed?.Keywords);
            if (keywords is null)
                missingKeywords.Add(reg.Id);

            entries.Add(new ControlEntry
            {
                Id = reg.Id,
                Name = reg.Name,
                Category = reg.Category,
                Description = reg.Description,
                Details = details.GetValueOrDefault(reg.Id),
                Keywords = keywords,
                CuratedKeywords = NormalizeKeywords(ed?.CuratedKeywords),
                RelatedControls = NullIfEmpty(ed?.RelatedControls),
                ApiNamespace = string.IsNullOrWhiteSpace(ed?.ApiNamespace) ? DefaultApiNamespace : ed!.ApiNamespace,
                NugetPackage = string.IsNullOrWhiteSpace(ed?.NugetPackage) ? DefaultNugetPackage : ed!.NugetPackage,
                Usings = NullIfEmpty(ed?.Usings),
                GalleryRoute = reg.Id,
                Docs = ResolveDocs(reg.Id, ed?.Docs),
                Samples = samples,
            });
        }

        // keywords are winui-search's weighted BM25 field — an included control with none
        // has its recall collapse, so require every one to be curated in editorial.json.
        if (missingKeywords.Count > 0)
            throw new InvalidOperationException(
                "these included controls have no editorial keywords (required): " + string.Join(", ", missingKeywords));

        // A control dropped for any reason other than an explicit editorial exclude is a
        // silent coverage loss. Force an intentional decision: add a route/page, an
        // editorial sampleOverride, or exclude:true.
        var unexpectedSkips = skipped.Where(s => s.Reason != ExcludeReason).ToList();
        if (unexpectedSkips.Count > 0)
            throw new InvalidOperationException(
                "controls were dropped (no route or no clean sample). Add a page/route, an editorial " +
                "sampleOverride, or an explicit exclude:true — " +
                string.Join("; ", unexpectedSkips.Select(s => $"{s.Id} ({s.Reason})")));

        entries.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        skipped.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        var root = new IndexRoot
        {
            SchemaVersion = SchemaVersion,
            Source = Source,
            GeneratedFrom = GeneratedFrom,
            Controls = entries,
        };

        return new SearchIndexResult(Serialize(root), entries.Count, skipped);
    }

    // ── Serialization ──────────────────────────────────────────────────────

    // Serialize through the System.Text.Json source generator (SearchIndexJsonContext) so the
    // tool is trim / NativeAOT-safe. The context bakes camelCase / WhenWritingNull / WriteIndented;
    // the encoder (UnsafeRelaxedJsonEscaping — keeps C# markup like => < > & "" readable) can't be
    // set via the attribute, so it is layered on by building the context from copied options.
    static readonly JsonTypeInfo<IndexRoot> IndexRootTypeInfo =
        new SearchIndexJsonContext(new JsonSerializerOptions(SearchIndexJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }).IndexRoot;

    static string Serialize(IndexRoot root)
    {
        var json = JsonSerializer.Serialize(root, IndexRootTypeInfo);
        // Force LF structural newlines regardless of platform/runtime defaults. In-string
        // newlines are already the escaped 2-char "\\n", so this only touches indentation.
        json = json.Replace("\r\n", "\n").Replace("\r", "\n");
        if (!json.EndsWith('\n')) json += "\n";
        return json;
    }

    // ── ControlRegistry.cs → id/name/category/description ───────────────────

    static IReadOnlyList<RegistryEntry> ParseRegistry(string registryPath)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(registryPath)).GetRoot();

        var allProp = root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(p => p.Identifier.Text == "All")
            ?? throw new InvalidOperationException("ControlRegistry.All property not found.");

        // `All` initializer is `new ControlInfo[] { new(...), ... }.OrderBy(...).ToArray()`.
        // Grab the ControlInfo[] array-creation's initializer.
        var arrayInit = allProp.Initializer?.Value
            .DescendantNodesAndSelf().OfType<ArrayCreationExpressionSyntax>()
            .FirstOrDefault()?.Initializer
            ?? throw new InvalidOperationException("ControlRegistry.All array initializer not found.");

        var list = new List<RegistryEntry>();
        foreach (var element in arrayInit.Expressions)
        {
            // Strict: every element of the `new ControlInfo[] { ... }` initializer must be a
            // fully literal `new(title, desc, category, glyph, tag, ...)`. Throwing (rather
            // than skipping) keeps the no-silent-drop guarantee if a future entry uses named
            // args, a constant, or an interpolated string the parser can't read.
            if (ObjectCreationArgs(element) is not { } args || args.Count < 5)
                throw new InvalidOperationException(
                    "ControlRegistry entry is not a `new(title, desc, category, glyph, tag, ...)` with >=5 args: " + Truncate(element));

            var title = TryGetStringLiteral(args[0].Expression);
            var description = TryGetStringLiteral(args[1].Expression);
            var category = TryGetStringLiteral(args[2].Expression);
            var tag = TryGetStringLiteral(args[4].Expression);

            if (title is null || description is null || category is null || string.IsNullOrEmpty(tag))
                throw new InvalidOperationException(
                    "ControlRegistry entry has a non-literal or empty title/description/category/tag: " + Truncate(element));

            list.Add(new RegistryEntry(tag, title, description, category));
        }

        if (list.Count == 0)
            throw new InvalidOperationException("ControlRegistry parse yielded no entries.");

        return list;
    }

    static string Truncate(SyntaxNode node)
    {
        var text = WhitespaceRegex().Replace(node.ToString(), " ").Trim();
        return text.Length <= 120 ? text : text[..117] + "...";
    }

    static SeparatedSyntaxList<ArgumentSyntax>? ObjectCreationArgs(ExpressionSyntax element) => element switch
    {
        ImplicitObjectCreationExpressionSyntax i => i.ArgumentList.Arguments,
        ObjectCreationExpressionSyntax o => o.ArgumentList?.Arguments,
        _ => null,
    };

    // ── PageRouter.cs → tag → page class simple name ────────────────────────

    static IReadOnlyDictionary<string, string> ParseRouter(string routerPath)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(routerPath)).GetRoot();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        var switchExpr = root.DescendantNodes().OfType<SwitchExpressionSyntax>().FirstOrDefault()
            ?? throw new InvalidOperationException("PageRouter switch expression not found.");

        foreach (var arm in switchExpr.Arms)
        {
            if (arm.Pattern is not ConstantPatternSyntax cp) continue; // skips the `_` discard arm
            var tag = TryGetStringLiteral(cp.Expression);
            if (tag is null) continue;

            // Find the `Component<PageType>()` call and take PageType's simple (rightmost) name.
            var generic = arm.Expression.DescendantNodesAndSelf().OfType<GenericNameSyntax>()
                .FirstOrDefault(g => g.Identifier.Text == "Component" && g.TypeArgumentList.Arguments.Count == 1);
            if (generic is null) continue;

            var pageClass = SimpleTypeName(generic.TypeArgumentList.Arguments[0]);
            if (pageClass is not null)
                map[tag] = pageClass;
        }

        return map;
    }

    static string? SimpleTypeName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        QualifiedNameSyntax q => q.Right.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        _ => null,
    };

    // ── ControlPages/**/*.cs → page class → first qualifying SampleCard ─────

    static IReadOnlyDictionary<string, IReadOnlyList<ExtractedSample>> ParseSamples(string controlPagesDir)
    {
        var map = new Dictionary<string, IReadOnlyList<ExtractedSample>>(StringComparer.Ordinal);
        if (!Directory.Exists(controlPagesDir)) return map;

        var files = Directory.EnumerateFiles(controlPagesDir, "*.cs", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal);

        foreach (var file in files)
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var name = cls.Identifier.Text;
                if (map.ContainsKey(name)) continue; // first (sorted-path) wins; names are unique

                var samples = QualifyingSamples(cls);
                if (samples.Count > 0)
                    map[name] = samples;
            }
        }

        return map;
    }

    /// <summary>
    /// Every complete, real-code <c>SampleCard</c> on the page, in source order. The consumer
    /// contract's <c>samples</c> is an array and the CLI iterates it, so a page's whole lesson
    /// travels with the entry rather than only its opening card. Cards that abbreviate with a
    /// placeholder are still passed over — the REAL-CODE-ONLY invariant is per card, so a page
    /// keeps its clean cards and loses only the elided ones. A page with no clean card at all
    /// still fails generation unless an editorial sampleOverride supplies one.
    /// </summary>
    static IReadOnlyList<ExtractedSample> QualifyingSamples(ClassDeclarationSyntax cls)
    {
        var samples = new List<ExtractedSample>();
        var seenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var inv in cls.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (InvokedSimpleName(inv) != "SampleCard") continue;

            var args = inv.ArgumentList.Arguments;
            if (args.Count < 3) continue;

            var header = TryGetStringLiteral(args[0].Expression);
            var code = FindSourceCodeArgument(args);
            if (header is null || code is null) continue;

            var normalized = NormalizeCode(code);
            if (!HasRealCode(normalized) || HasPlaceholder(normalized)) continue;

            // Headers are the 0.8-weighted BM25 field and the human label for a hit, so two
            // cards sharing one header would emit an ambiguous duplicate. Keep the first.
            var trimmedHeader = header.Trim();
            if (!seenHeaders.Add(trimmedHeader)) continue;

            samples.Add(new ExtractedSample(trimmedHeader, normalized));
        }

        return samples;
    }

    static string? FindSourceCodeArgument(SeparatedSyntaxList<ArgumentSyntax> args)
    {
        // Named `sourceCode:` wins wherever it sits...
        foreach (var a in args)
        {
            if (a.NameColon?.Name.Identifier.Text == "sourceCode")
                return TryGetStringLiteral(a.Expression);
        }
        // ...otherwise the 3rd positional argument (title, sample, sourceCode, ...).
        if (args.Count >= 3 && args[2].NameColon is null)
            return TryGetStringLiteral(args[2].Expression);

        return null;
    }

    static string InvokedSimpleName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        _ => string.Empty,
    };

    // ── Shared helpers ─────────────────────────────────────────────────────

    static string? TryGetStringLiteral(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression)
            ? lit.Token.ValueText
            : null;

    static string NormalizeCode(string code) =>
        code.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

    // A snippet qualifies as a real code sample only if it has at least one line that
    // isn't blank or a pure `//` comment — so a guidance card whose "sourceCode" is only
    // a descriptive comment (e.g. SpacingPage's first card) is passed over for the next.
    static bool HasRealCode(string code) =>
        code.Split('\n').Any(line =>
        {
            var t = line.Trim();
            return t.Length > 0 && !t.StartsWith("//", StringComparison.Ordinal);
        });

    // Rejects a snippet that abbreviates required code with a placeholder — an args/element
    // ellipsis ("x, ..." / "(...)" / "...)" / a lone "..." line) or an angle-bracket fill-in
    // like <your-key>. Ellipses inside UI strings ("Type here...") are NOT matched, so those
    // snippets still qualify. Enforces the REAL-CODE-ONLY invariant: the first *complete*
    // card wins, and a control with no complete card needs an editorial sampleOverride.
    [GeneratedRegex(@",\s*\.\.\.|\.\.\.\s*\)|\(\s*\.\.\.|^\s*\.\.\.\s*$|\.\.\./|<your", RegexOptions.Multiline)]
    private static partial Regex PlaceholderRegex();

    static bool HasPlaceholder(string code) => PlaceholderRegex().IsMatch(code);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    // Resolves the representative samples. An editorial sampleOverride still defines the FIRST
    // sample — it can replace that card's header, its code, or both, or stand alone when no
    // page card qualifies — and the page's remaining clean cards follow it. Returns an empty
    // list only when neither the page nor the override yields a header + code.
    static IReadOnlyList<Sample> ResolveSamples(IReadOnlyList<ExtractedSample>? extracted, EditorialSampleOverride? ov)
    {
        var first = ResolveSample(extracted is { Count: > 0 } ? extracted[0] : null, ov);
        if (first is null) return Array.Empty<Sample>();

        var samples = new List<Sample> { first };
        if (extracted is null) return samples;

        // Skip index 0: the override already folded it in (or replaced it outright). A later
        // card that collides with the override's header would read as a duplicate, so drop it.
        // Case-insensitive to match the page-level dedupe in QualifyingSamples — otherwise an
        // override header differing only in case would reintroduce the duplicate it prevents.
        for (var i = 1; i < extracted.Count; i++)
        {
            if (string.Equals(extracted[i].Header, first.Header, StringComparison.OrdinalIgnoreCase)) continue;
            samples.Add(new Sample { Header = extracted[i].Header, Language = "csharp", Code = extracted[i].Code });
        }

        return samples;
    }

    static Sample? ResolveSample(ExtractedSample? extracted, EditorialSampleOverride? ov)
    {
        var header = string.IsNullOrWhiteSpace(ov?.Header) ? extracted?.Header : ov!.Header!.Trim();
        var code = string.IsNullOrWhiteSpace(ov?.Code) ? extracted?.Code : NormalizeCode(ov!.Code!);

        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(code))
            return null;

        return new Sample { Header = header!, Language = "csharp", Code = code! };
    }

    // `docs` is a display-only field in the consumer contract (it does not feed BM25), so the
    // only thing worth enforcing is that a half-filled entry can't ship a blank link.
    static IReadOnlyList<DocLink>? ResolveDocs(string controlId, IReadOnlyList<EditorialDocLink>? raw)
    {
        if (raw is not { Count: > 0 }) return null;

        var links = new List<DocLink>();
        foreach (var link in raw)
        {
            if (string.IsNullOrWhiteSpace(link?.Title) || string.IsNullOrWhiteSpace(link.Uri))
                throw new InvalidOperationException(
                    $"editorial.json '{controlId}' has a docs entry missing title or uri — both are required.");

            links.Add(new DocLink { Title = link.Title!.Trim(), Uri = link.Uri!.Trim() });
        }

        return links;
    }

    static IReadOnlyList<string>? NullIfEmpty(IReadOnlyList<string>? list) =>
        list is { Count: > 0 } ? list : null;

    // keywords feed a token-matched BM25 field: each must be a lowercased single token or
    // short phrase. Trim, lowercase, collapse internal whitespace, and drop empties/dupes so
    // the emitted form is canonical regardless of how editorial.json was hand-typed.
    static IReadOnlyList<string>? NormalizeKeywords(IReadOnlyList<string>? raw)
    {
        if (raw is null) return null;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        // Filter null/blank keywords here (a hand-edited editorial.json can contain
        // "keywords": ["a", null]) so the body never Trims a null.
        foreach (var k in raw.Where(k => !string.IsNullOrWhiteSpace(k)))
        {
            var norm = WhitespaceRegex().Replace(k.Trim().ToLowerInvariant(), " ");
            if (norm.Length > 0 && seen.Add(norm))
                result.Add(norm);
        }
        return result.Count > 0 ? result : null;
    }

    static IReadOnlyDictionary<string, EditorialEntry> LoadEditorial(string? editorialPath)
    {
        if (string.IsNullOrWhiteSpace(editorialPath) || !File.Exists(editorialPath))
            return new Dictionary<string, EditorialEntry>(StringComparer.Ordinal);

        try
        {
            var parsed = JsonSerializer.Deserialize(File.ReadAllText(editorialPath), EditorialJsonContext.Default.DictionaryStringEditorialEntry);
            return parsed is null
                ? new Dictionary<string, EditorialEntry>(StringComparer.Ordinal)
                : new Dictionary<string, EditorialEntry>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            // A misspelled field (e.g. "keyword"/"sampleOveride") trips UnmappedMemberHandling
            // and lands here — surface it as a clear, actionable error instead of a raw crash.
            throw new InvalidOperationException($"editorial.json is invalid ({Path.GetFileName(editorialPath)}): {ex.Message}", ex);
        }
    }

    // ── Agent-kit markdown → control id → `details` prose ───────────────────

    /// <summary>
    /// Lifts the <c>&lt;!-- index:id --&gt; … &lt;!-- /index:id --&gt;</c> blocks out of the shipped
    /// agent kit so the index and the skills carry the same words by construction rather than by
    /// discipline. The marker id IS the control id, so there is no second mapping to keep in sync.
    /// </summary>
    /// <remarks>
    /// A concept legitimately spans sections (pointer events and gestures sit either side of the
    /// keyboard section), so repeating an id is allowed and the blocks concatenate in
    /// (file path, offset) order — deterministic regardless of enumeration order. What is NOT
    /// allowed is a marker that silently produces nothing: unclosed, mismatched, or nested
    /// markers all fail generation, as does an id naming no control (checked by the caller,
    /// which is the side that knows the registry).
    /// </remarks>
    static IReadOnlyDictionary<string, string> ParseAgentKitDetails(string? agentKitRoot)
    {
        var blocks = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(agentKitRoot) || !Directory.Exists(agentKitRoot))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in AgentKitMarkdownFiles(agentKitRoot))
        {
            var markdown = File.ReadAllText(file).Replace("\r\n", "\n").Replace("\r", "\n");
            var where = RepoRelative(file, agentKitRoot);
            foreach (var (id, body) in MarkedBlocks(where, markdown))
            {
                var rewritten = RewriteRelativeLinks(file, agentKitRoot, body);
                if (!blocks.TryGetValue(id, out var list))
                    blocks[id] = list = new List<string>();
                list.Add(rewritten);
            }
        }

        return blocks.ToDictionary(kv => kv.Key, kv => string.Join("\n\n", kv.Value), StringComparer.Ordinal);
    }

    /// <summary>
    /// Repo-relative, forward-slashed path for diagnostics. A marker error that names only the
    /// file name is unactionable — the scanner walks two whole trees, and SKILL.md is not unique.
    /// </summary>
    static string RepoRelative(string file, string agentKitRoot)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(agentKitRoot), Path.GetFullPath(file));
        return relative.Replace('\\', '/');
    }

    /// <summary>Line number (1-based) of <paramref name="offset"/> in already-LF-normalized text.</summary>
    static int LineAt(string text, int offset) =>
        text.AsSpan(0, offset).Count('\n') + 1;

    /// <summary>
    /// The markdown that ships in the NuGet's <c>agentkit/</c>: the root SKILL.md plus the
    /// <c>plugins/</c> and <c>skills/</c> trees. Sorted ordinally so concatenation order is a
    /// pure function of the file names, not of the filesystem.
    /// </summary>
    static IReadOnlyList<string> AgentKitMarkdownFiles(string agentKitRoot)
    {
        var files = new List<string>();

        var rootSkill = Path.Join(agentKitRoot, "SKILL.md");
        if (File.Exists(rootSkill)) files.Add(rootSkill);

        foreach (var dir in new[] { "plugins", "skills" })
        {
            var full = Path.Join(agentKitRoot, dir);
            if (Directory.Exists(full))
                files.AddRange(Directory.EnumerateFiles(full, "*.md", SearchOption.AllDirectories));
        }

        files.Sort((a, b) => string.CompareOrdinal(Normalize(a), Normalize(b)));
        return files;

        static string Normalize(string path) => path.Replace('\\', '/');
    }

    static IEnumerable<(string Id, string Body)> MarkedBlocks(string where, string markdown)
    {
        string? openId = null;
        var openAt = 0;
        var bodyStart = 0;
        var results = new List<(string, string)>();

        foreach (Match m in IndexMarkerRegex().Matches(markdown))
        {
            var isClose = m.Groups[1].Value.Length > 0;
            var id = m.Groups[2].Value;
            var line = LineAt(markdown, m.Index);

            if (!isClose)
            {
                if (openId is not null)
                    throw new InvalidOperationException(
                        $"{where}({line}): `<!-- index:{id} -->` opens while `{openId}` (opened at line {LineAt(markdown, openAt)}) is still open — index markers cannot nest or overlap.");
                openId = id;
                openAt = m.Index;
                bodyStart = m.Index + m.Length;
                continue;
            }

            if (openId is null)
                throw new InvalidOperationException(
                    $"{where}({line}): `<!-- /index:{id} -->` closes a block that was never opened.");
            if (!string.Equals(openId, id, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{where}({line}): `<!-- /index:{id} -->` closes the wrong block — `{openId}` (opened at line {LineAt(markdown, openAt)}) is open.");

            results.Add((id, markdown[bodyStart..m.Index].Trim()));
            openId = null;
        }

        if (openId is not null)
            throw new InvalidOperationException(
                $"{where}({LineAt(markdown, openAt)}): `<!-- index:{openId} -->` is never closed.");

        return results;
    }

    /// <summary>
    /// Rewrites repo-relative markdown links to absolute GitHub URLs. The prose is read far from
    /// the file it was written in — by an agent holding only the index — so a relative link is
    /// dead weight there. A target that does not exist, or that escapes the repo, fails
    /// generation rather than shipping a broken link.
    /// </summary>
    static string RewriteRelativeLinks(string file, string agentKitRoot, string body)
    {
        var fileDir = Path.GetDirectoryName(file)!;
        var rootFull = Path.GetFullPath(agentKitRoot);
        var where = RepoRelative(file, agentKitRoot);

        return MarkdownLinkRegex().Replace(body, m =>
        {
            var target = m.Groups[1].Value;
            if (target.Length == 0 || target[0] == '#' || target.Contains("://", StringComparison.Ordinal)
                || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                return m.Value;
            }

            var hash = target.IndexOf('#');
            var pathPart = hash >= 0 ? target[..hash] : target;
            var anchor = hash >= 0 ? target[hash..] : "";
            if (pathPart.Length == 0) return m.Value;

            var resolved = Path.GetFullPath(Path.Combine(fileDir, pathPart));

            // Boundary-aware containment. A plain StartsWith would accept a sibling that merely
            // shares the root's textual prefix (…/repo vs …/repo-sibling); GetRelativePath is
            // segment-aware, so an escape shows up as a rooted path or a leading "..".
            var relative = Path.GetRelativePath(rootFull, resolved);
            if (Path.IsPathRooted(relative)
                || relative == ".."
                || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith("../", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{where}: index-marked link '{target}' escapes the repo and cannot be made absolute.");
            }
            if (!File.Exists(resolved) && !Directory.Exists(resolved))
                throw new InvalidOperationException(
                    $"{where}: index-marked link '{target}' points at nothing ({relative}).");

            return $"{m.Groups["pre"].Value}(https://github.com/{GeneratedFrom}/blob/main/{relative.Replace('\\', '/')}{anchor})";
        });
    }

    [GeneratedRegex(@"<!--\s*(/?)index:([a-z0-9][a-z0-9-]*)\s*-->")]
    private static partial Regex IndexMarkerRegex();

    // Inline markdown links only — `](target)` with an optional title. Reference-style links and
    // bare autolinks are left alone; neither appears in the marked blocks.
    [GeneratedRegex(@"(?<pre>\])\((?!\s)([^)\s]+)(?:\s+""[^""]*"")?\)")]
    private static partial Regex MarkdownLinkRegex();

    // ── Internal parse models ──────────────────────────────────────────────

    sealed record RegistryEntry(string Id, string Name, string Description, string Category);

    sealed record ExtractedSample(string Header, string Code);
}

// ── JSON output model (property order == emitted key order) ─────────────────

public sealed class IndexRoot
{
    public int SchemaVersion { get; set; }
    public string Source { get; set; } = "";
    public string GeneratedFrom { get; set; } = "";
    public IReadOnlyList<ControlEntry> Controls { get; set; } = Array.Empty<ControlEntry>();
}

public sealed class ControlEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Details { get; set; }
    public IReadOnlyList<string>? Keywords { get; set; }
    public IReadOnlyList<string>? CuratedKeywords { get; set; }
    public IReadOnlyList<string>? RelatedControls { get; set; }
    public string? ApiNamespace { get; set; }
    public string? NugetPackage { get; set; }
    public IReadOnlyList<string>? Usings { get; set; }
    public string GalleryRoute { get; set; } = "";
    public IReadOnlyList<DocLink>? Docs { get; set; }
    public IReadOnlyList<Sample> Samples { get; set; } = Array.Empty<Sample>();
}

/// <summary>
/// A reference link surfaced alongside a search hit. Maps to the consumer contract's
/// control-level <c>docs</c> array (<c>title</c> + <c>uri</c>).
/// </summary>
public sealed class DocLink
{
    public string Title { get; set; } = "";
    public string Uri { get; set; } = "";
}

public sealed class Sample
{
    public string Header { get; set; } = "";
    public string Language { get; set; } = "csharp";
    public string Code { get; set; } = "";
}

public sealed record SearchIndexResult(string Json, int ControlCount, IReadOnlyList<SkippedControl> Skipped);

public sealed record SkippedControl(string Id, string Name, string Reason);

// ── Editorial sidecar model (deserialized from editorial.json; internal to the tool) ────

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class EditorialEntry
{
    public List<string>? Keywords { get; set; }
    public List<string>? CuratedKeywords { get; set; }
    public List<string>? RelatedControls { get; set; }
    public List<string>? Usings { get; set; }
    public List<EditorialDocLink>? Docs { get; set; }
    public string? ApiNamespace { get; set; }
    public string? NugetPackage { get; set; }
    public bool Exclude { get; set; }
    public EditorialSampleOverride? SampleOverride { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class EditorialDocLink
{
    public string? Title { get; set; }
    public string? Uri { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class EditorialSampleOverride
{
    public string? Header { get; set; }
    public string? Code { get; set; }
}

// ── System.Text.Json source-generation contexts (trim / NativeAOT-safe) ─────────────────

// Output: camelCase keys, omit null optionals, indented. The relaxed encoder is layered on at
// the use-site (SearchIndexGenerator.CreateIndexRootTypeInfo) since it has no attribute knob.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IndexRoot))]
internal partial class SearchIndexJsonContext : JsonSerializerContext { }

// Input: tolerate case + `//` comments; unmapped members rejected via the per-type attribute.
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(Dictionary<string, EditorialEntry>))]
internal partial class EditorialJsonContext : JsonSerializerContext { }
