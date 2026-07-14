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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.SearchIndex;

public static class SearchIndexGenerator
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
    public static SearchIndexResult Generate(string galleryDir, string? editorialPath)
    {
        var registry = ParseRegistry(Path.Join(galleryDir, "ControlRegistry.cs"));
        var routes = ParseRouter(Path.Join(galleryDir, "PageRouter.cs"));
        var samplesByClass = ParseSamples(Path.Join(galleryDir, "ControlPages"));
        var editorial = LoadEditorial(editorialPath);

        var registryIds = new HashSet<string>(registry.Select(r => r.Id), StringComparer.Ordinal);
        var orphanKeys = editorial.Keys.Where(k => !registryIds.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (orphanKeys.Count > 0)
            throw new InvalidOperationException(
                "editorial.json has key(s) that match no control id (typo?): " + string.Join(", ", orphanKeys));

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
            var sample = ResolveSample(extracted, ed?.SampleOverride);
            if (sample is null)
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
                Keywords = keywords,
                RelatedControls = NullIfEmpty(ed?.RelatedControls),
                ApiNamespace = string.IsNullOrWhiteSpace(ed?.ApiNamespace) ? DefaultApiNamespace : ed!.ApiNamespace,
                NugetPackage = string.IsNullOrWhiteSpace(ed?.NugetPackage) ? DefaultNugetPackage : ed!.NugetPackage,
                Usings = NullIfEmpty(ed?.Usings),
                GalleryRoute = reg.Id,
                Samples = new[] { sample },
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

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Relaxed escaping so C# markup in `code` (=> < > & "") stays human-readable;
        // only " \ and control chars are escaped. Output is never embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static string Serialize(IndexRoot root)
    {
        var json = JsonSerializer.Serialize(root, JsonOptions);
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
        var text = System.Text.RegularExpressions.Regex.Replace(node.ToString(), @"\s+", " ").Trim();
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

    static IReadOnlyDictionary<string, ExtractedSample> ParseSamples(string controlPagesDir)
    {
        var map = new Dictionary<string, ExtractedSample>(StringComparer.Ordinal);
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

                var sample = FirstQualifyingSample(cls);
                if (sample is not null)
                    map[name] = sample;
            }
        }

        return map;
    }

    static ExtractedSample? FirstQualifyingSample(ClassDeclarationSyntax cls)
    {
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

            return new ExtractedSample(header.Trim(), normalized);
        }

        return null;
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
    static readonly System.Text.RegularExpressions.Regex PlaceholderPattern = new(
        @",\s*\.\.\.|\.\.\.\s*\)|\(\s*\.\.\.|^\s*\.\.\.\s*$|\.\.\./|<your",
        System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    static bool HasPlaceholder(string code) => PlaceholderPattern.IsMatch(code);

    // Resolves the representative sample: an editorial sampleOverride can replace the header,
    // the code, or both — or supply the whole sample when no page card qualifies. Returns null
    // only when neither the page nor the override yields a header + code.
    static Sample? ResolveSample(ExtractedSample? extracted, EditorialSampleOverride? ov)
    {
        var header = string.IsNullOrWhiteSpace(ov?.Header) ? extracted?.Header : ov!.Header!.Trim();
        var code = string.IsNullOrWhiteSpace(ov?.Code) ? extracted?.Code : NormalizeCode(ov!.Code!);

        if (string.IsNullOrWhiteSpace(header) || string.IsNullOrWhiteSpace(code))
            return null;

        return new Sample { Header = header!, Language = "csharp", Code = code! };
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
        foreach (var k in raw)
        {
            var norm = System.Text.RegularExpressions.Regex.Replace(k.Trim().ToLowerInvariant(), @"\s+", " ");
            if (norm.Length > 0 && seen.Add(norm))
                result.Add(norm);
        }
        return result.Count > 0 ? result : null;
    }

    static IReadOnlyDictionary<string, EditorialEntry> LoadEditorial(string? editorialPath)
    {
        if (string.IsNullOrWhiteSpace(editorialPath) || !File.Exists(editorialPath))
            return new Dictionary<string, EditorialEntry>(StringComparer.Ordinal);

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, EditorialEntry>>(File.ReadAllText(editorialPath), opts);
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

    // ── Internal parse models ──────────────────────────────────────────────

    sealed record RegistryEntry(string Id, string Name, string Description, string Category);

    sealed record ExtractedSample(string Header, string Code);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    sealed class EditorialEntry
    {
        public List<string>? Keywords { get; set; }
        public List<string>? RelatedControls { get; set; }
        public List<string>? Usings { get; set; }
        public string? ApiNamespace { get; set; }
        public string? NugetPackage { get; set; }
        public bool Exclude { get; set; }
        public EditorialSampleOverride? SampleOverride { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    sealed class EditorialSampleOverride
    {
        public string? Header { get; set; }
        public string? Code { get; set; }
    }
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
    public IReadOnlyList<string>? Keywords { get; set; }
    public IReadOnlyList<string>? RelatedControls { get; set; }
    public string? ApiNamespace { get; set; }
    public string? NugetPackage { get; set; }
    public IReadOnlyList<string>? Usings { get; set; }
    public string GalleryRoute { get; set; } = "";
    public IReadOnlyList<Sample> Samples { get; set; } = Array.Empty<Sample>();
}

public sealed class Sample
{
    public string Header { get; set; } = "";
    public string Language { get; set; } = "csharp";
    public string Code { get; set; } = "";
}

public sealed record SearchIndexResult(string Json, int ControlCount, IReadOnlyList<SkippedControl> Skipped);

public sealed record SkippedControl(string Id, string Name, string Reason);
