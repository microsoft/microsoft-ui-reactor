using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.SearchIndex;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

public sealed class SearchIndexCoverageTests
{
    readonly record struct CardKey(string Control, string Title, int Occurrence = 1);

    // This manifest is reviewed against the page source. A new omission fails the test even
    // if the generated JSON was refreshed. Remove an entry when its card starts shipping.
    static readonly Dictionary<CardKey, string> Omissions = new()
    {
        [new("auto-suggest-box", "Filtered Results Display")] = "The Select projection is elided.",
        [new("card", "Card with icon, heading, and body")] = "The icon Border is abbreviated.",
        [new("color", "<title>")] = "BrushSection computes the card title and code at runtime.",
        [new("expander", "Basic Expander")] = "Additional content children are elided.",
        [new("flex", "Grow & Shrink")] = "The child Borders have abbreviated content.",
        [new("flip-view", "Data-Driven FlipView")] = "The item template's VStack children are elided.",
        [new("grid", "Basic Grid")] = "Remaining grid cells are elided.",
        [new("list-view", "Basic ListView")] = "Additional list items are elided.",
        [new("list-view", "Animated edit (spec 042)")] = "The mutation signature and item template are elided.",
        [new("rich-text-block", "Inline hyperlinks & embedded UI")] = "The embedded button callback is elided.",
        [new("scroll-view", "Horizontal ScrollView (modern)")] = "The HStack content is elided.",
        [new("scroll-view", "Classic ScrollViewer (legacy)")] = "The VStack content is elided.",
        [new("spacing", "Spacing Scale")] = "The card's snippet is a comment describing the scale.",
        [new("tree-view", "Basic TreeView")] = "The Pictures subtree is elided.",
        [new("typography", "Type Ramp")] = "The code is assembled from the type ramp at runtime.",
    };

    [Fact]
    public void EveryGalleryCard_IsIndexedOrAccountedFor()
    {
        var root = GallerySources.RepoRoot();
        var gallery = GallerySources.GalleryDir();
        using var editorial = JsonDocument.Parse(
            File.ReadAllText(Path.Join(root, "tools", "Reactor.SearchIndex", "editorial.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Join(gallery, "reactor-search-index.json")));
        var problems = CoverageProblems(gallery, editorial.RootElement, index.RootElement, Omissions);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    public void RegeneratedIndex_IgnoresBuildOutputPages(string outputDirectory)
    {
        using var gallery = new MiniGallery(betaRouted: true);
        var editorialPath = gallery.WriteEditorial("""
            { "alpha": { "keywords": ["one", "two", "three"] },
              "beta": { "keywords": ["one", "two", "three"] } }
            """);
        using var editorial = JsonDocument.Parse(File.ReadAllText(editorialPath));
        using var index = JsonDocument.Parse(SearchIndexGenerator.Generate(gallery.GalleryDir, editorialPath).Json);
        var omissions = new Dictionary<CardKey, string>();

        Assert.Empty(CoverageProblems(gallery.GalleryDir, editorial.RootElement, index.RootElement, omissions));

        var generatedDirectory = Path.Join(gallery.GalleryDir, "ControlPages", "Nested", outputDirectory);
        Directory.CreateDirectory(generatedDirectory);
        File.WriteAllText(Path.Join(generatedDirectory, "AlphaPage.g.cs"), """
            class AlphaPage
            {
                object Render() => SampleCard("Generated", null, "Alpha().Generated();");
            }
            """);

        Assert.Empty(CoverageProblems(gallery.GalleryDir, editorial.RootElement, index.RootElement, omissions));
    }

    [Fact]
    public void RegeneratedIndex_ReportsUnaccountedOmission()
    {
        using var gallery = new MiniGallery(betaRouted: true);
        var editorialPath = gallery.WriteEditorial("""
            { "alpha": { "keywords": ["one", "two", "three"] },
              "beta": { "keywords": ["one", "two", "three"] } }
            """);
        using var editorial = JsonDocument.Parse(File.ReadAllText(editorialPath));

        List<string> Check(string code)
        {
            gallery.OverwriteAlphaPage($$"""
                class AlphaPage
                {
                    object Render() => VStack(
                        SampleCard("Before", null, "Alpha();"),
                        SampleCard("Probe", null, {{SyntaxFactory.Literal(code).Text}}),
                        SampleCard("After", null, "Alpha();"));
                }
                """);
            using var index = JsonDocument.Parse(SearchIndexGenerator.Generate(gallery.GalleryDir, editorialPath).Json);
            return CoverageProblems(gallery.GalleryDir, editorial.RootElement, index.RootElement,
                new Dictionary<CardKey, string>());
        }

        // Generate a fresh index in both cases. The coverage check must still see a dropped card.
        Assert.Empty(Check("Alpha(); // see Alpha(...), then continue"));
        Assert.Contains(Check("Alpha(a, ...);"), problem => problem.Contains("Missing: [Probe]", StringComparison.Ordinal));
    }

    [Fact]
    public void RegeneratedIndex_ReportsStaleManifestEntry()
    {
        using var gallery = new MiniGallery(betaRouted: true);
        var editorialPath = gallery.WriteEditorial("""
            { "alpha": { "keywords": ["one", "two", "three"] },
              "beta": { "keywords": ["one", "two", "three"] } }
            """);
        using var editorial = JsonDocument.Parse(File.ReadAllText(editorialPath));
        using var index = JsonDocument.Parse(SearchIndexGenerator.Generate(gallery.GalleryDir, editorialPath).Json);
        var omissions = new Dictionary<CardKey, string>();

        Assert.Empty(CoverageProblems(gallery.GalleryDir, editorial.RootElement, index.RootElement, omissions));

        omissions.Add(new("alpha", "Removed card"), "The card was removed");
        var problem = Assert.Single(CoverageProblems(gallery.GalleryDir, editorial.RootElement, index.RootElement, omissions));
        Assert.Equal("alpha / Removed card (1): stale manifest entry (The card was removed).", problem);
    }

    [Theory]
    [InlineData("""{ "header": "Curated" }""", "Curated")]
    [InlineData("""{ "code": "Alpha().Configured();" }""", "Alpha basic")]
    [InlineData("""{ "header": "Curated", "code": "Alpha().Configured();" }""", "Curated")]
    public void RegeneratedIndex_AppliesEditorialOverride(string sampleOverride, string expectedHeader)
    {
        using var gallery = new MiniGallery(betaRouted: true);
        var editorialPath = gallery.WriteEditorial($$"""
            { "alpha": { "keywords": ["one", "two", "three"], "sampleOverride": {{sampleOverride}} },
              "beta": { "keywords": ["one", "two", "three"] } }
            """);
        using var editorial = JsonDocument.Parse(File.ReadAllText(editorialPath));
        using var index = JsonDocument.Parse(SearchIndexGenerator.Generate(gallery.GalleryDir, editorialPath).Json);
        var samples = index.RootElement.GetProperty("controls")[0].GetProperty("samples");

        Assert.Equal(expectedHeader, Assert.Single(samples.EnumerateArray()).GetProperty("header").GetString());
        Assert.Empty(CoverageProblems(gallery.GalleryDir, editorial.RootElement, index.RootElement,
            new Dictionary<CardKey, string>()));
    }

    [Fact]
    public void RegeneratedIndex_AppliesOverrideAfterSkippedCard()
    {
        using var gallery = new MiniGallery(betaRouted: true);
        gallery.OverwriteAlphaPage("""
            class AlphaPage
            {
                object Render() => VStack(
                    SampleCard("Guidance", null, "// Guidance only"),
                    SampleCard("First", null, "Alpha();"),
                    SampleCard("curated", null, "Alpha().Alternate();"),
                    SampleCard("Later", null, "Alpha().Tuned();"));
            }
            """);
        var editorialPath = gallery.WriteEditorial("""
            { "alpha": { "keywords": ["one", "two", "three"], "sampleOverride": { "header": "Curated" } },
              "beta": { "keywords": ["one", "two", "three"] } }
            """);
        using var editorial = JsonDocument.Parse(File.ReadAllText(editorialPath));
        using var index = JsonDocument.Parse(SearchIndexGenerator.Generate(gallery.GalleryDir, editorialPath).Json);
        var omissions = new Dictionary<CardKey, string>
        {
            [new("alpha", "Guidance")] = "The snippet is comment-only",
        };
        var samples = index.RootElement.GetProperty("controls")[0].GetProperty("samples");

        Assert.Equal(new[] { "Curated", "Later" }, samples.EnumerateArray().Select(s => s.GetProperty("header").GetString()));
        Assert.Empty(CoverageProblems(gallery.GalleryDir, editorial.RootElement, index.RootElement, omissions));
    }

    [Fact]
    public void RegeneratedIndex_UsesStandaloneEditorialOverride()
    {
        using var gallery = new MiniGallery(betaRouted: true);
        gallery.OverwriteAlphaPage("""
            class AlphaPage
            {
                object Render() => SampleCard("Guidance", null, "// Guidance only");
            }
            """);
        var editorialPath = gallery.WriteEditorial("""
            { "alpha": { "keywords": ["one", "two", "three"],
                         "sampleOverride": { "header": "Curated", "code": "Alpha();" } },
              "beta": { "keywords": ["one", "two", "three"] } }
            """);
        using var editorial = JsonDocument.Parse(File.ReadAllText(editorialPath));
        using var index = JsonDocument.Parse(SearchIndexGenerator.Generate(gallery.GalleryDir, editorialPath).Json);
        var omissions = new Dictionary<CardKey, string>
        {
            [new("alpha", "Guidance")] = "The snippet is comment-only",
        };
        var samples = index.RootElement.GetProperty("controls")[0].GetProperty("samples");

        Assert.Equal("Curated", Assert.Single(samples.EnumerateArray()).GetProperty("header").GetString());
        Assert.Empty(CoverageProblems(gallery.GalleryDir, editorial.RootElement, index.RootElement, omissions));
    }

    static List<string> CoverageProblems(string gallery, JsonElement editorial, JsonElement index,
        IReadOnlyDictionary<CardKey, string> omissions)
    {
        var registered = RegistryIds(gallery);
        Assert.NotEmpty(registered);
        var included = registered.Where(id => !IsExcluded(editorial, id)).ToArray();
        var indexed = index.GetProperty("controls").EnumerateArray().ToDictionary(
            c => c.GetProperty("id").GetString()!, c => c.GetProperty("samples"), StringComparer.Ordinal);
        Assert.Equal(included.Order(StringComparer.Ordinal), indexed.Keys.Order(StringComparer.Ordinal));

        var routes = Routes(gallery);
        var pageNames = included.Select(id => routes[id]).ToHashSet(StringComparer.Ordinal);
        var pages = GallerySources.ReadSources(Path.Join(gallery, "ControlPages"))
            .SelectMany(source => source.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(page => pageNames.Contains(page.Identifier.ValueText))
            .ToDictionary(page => page.Identifier.ValueText, page => CardInvocations(page).ToArray(), StringComparer.Ordinal);

        var remainingOmissions = new Dictionary<CardKey, string>(omissions);
        var problems = new List<string>();
        foreach (var id in included)
        {
            var expected = new List<string>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var card in pages[routes[id]])
            {
                var args = card.ArgumentList.Arguments;
                var titleExpression = Argument(args, "title", 0);
                var literalTitle = ReadOptionalLiteral(titleExpression);
                var title = literalTitle?.Trim() ?? $"<{titleExpression}>";
                var count = occurrences.GetValueOrDefault(title) + 1;
                occurrences[title] = count;
                var key = new CardKey(id, title, count);
                var sourceExpression = Argument(args, "sourceCode", 2);
                var sourceCode = ReadOptionalLiteral(sourceExpression);

                if (remainingOmissions.Remove(key)) continue;

                if (literalTitle is null || sourceCode is null)
                {
                    var field = literalTitle is null ? "title" : "sourceCode";
                    problems.Add($"{id} / {title} ({count}): non-literal or absent {field} needs an explicit manifest entry.");
                    continue;
                }

                expected.Add(title);
            }

            var overrideHeader = ReadOverrideHeader(editorial, id);
            if (overrideHeader is not null)
            {
                expected = expected.Skip(1)
                    .Where(header => !string.Equals(header, overrideHeader, StringComparison.OrdinalIgnoreCase))
                    .Prepend(overrideHeader).ToList();
            }

            var actual = indexed[id].EnumerateArray().Select(s => s.GetProperty("header").GetString()!).ToArray();
            if (expected.SequenceEqual(actual)) continue;

            var missing = expected.Except(actual).ToArray();
            var unexpected = actual.Except(expected).ToArray();
            problems.Add($"{id}: expected {expected.Count}, got {actual.Length}. "
                + $"Missing: [{string.Join(", ", missing)}]. "
                + $"Unexpected: [{string.Join(", ", unexpected)}]. "
                + "If both lists are empty, check the order or duplicate count.");
        }

        problems.AddRange(remainingOmissions.Select(entry =>
            $"{entry.Key.Control} / {entry.Key.Title} ({entry.Key.Occurrence}): stale manifest entry ({entry.Value})."));
        return problems;
    }

    static string[] RegistryIds(string gallery)
    {
        var registry = ReadSource(Path.Join(gallery, "ControlRegistry.cs"));
        var all = registry.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(p => p.Identifier.ValueText == "All");
        var controls = all.DescendantNodes().OfType<ArrayCreationExpressionSyntax>().Single().Initializer
            ?? throw new InvalidDataException("Missing ControlRegistry.All initializer.");
        return controls.Expressions.Select(entry => ReadLiteral(entry switch
        {
            ImplicitObjectCreationExpressionSyntax creation => Argument(creation.ArgumentList.Arguments, "Tag", 4),
            ObjectCreationExpressionSyntax { ArgumentList: { } args } => Argument(args.Arguments, "Tag", 4),
            _ => throw new InvalidDataException($"Unknown ControlRegistry entry: {entry}"),
        })).ToArray();
    }

    static Dictionary<string, string> Routes(string gallery)
    {
        return ReadSource(Path.Join(gallery, "PageRouter.cs")).DescendantNodes().OfType<SwitchExpressionArmSyntax>()
            .Where(arm => arm.Pattern is ConstantPatternSyntax)
            .GroupBy(arm => ReadLiteral(((ConstantPatternSyntax)arm.Pattern).Expression), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(arm => arm.Expression.DescendantNodes().OfType<GenericNameSyntax>()
                .Single(name => name.Identifier.ValueText == "Component").TypeArgumentList.Arguments.Single().GetLastToken().ValueText)
                .Distinct(StringComparer.Ordinal).Single(), StringComparer.Ordinal);
    }

    static IEnumerable<InvocationExpressionSyntax> CardInvocations(ClassDeclarationSyntax page) =>
        // Routed pages such as Transitions use nested components for their live cards.
        page.DescendantNodes()
            .OfType<InvocationExpressionSyntax>().Where(call => GallerySources.InvokedName(call) == "SampleCard");

    static ExpressionSyntax? Argument(SeparatedSyntaxList<ArgumentSyntax> args, string name, int position) =>
        args.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == name)?.Expression
        ?? (position < args.Count && args[position].NameColon is null ? args[position].Expression : null);

    static string ReadLiteral(ExpressionSyntax? syntax) => ReadOptionalLiteral(syntax)
        ?? throw new InvalidDataException($"Expected a string literal, got: {syntax}");

    static string? ReadOptionalLiteral(ExpressionSyntax? syntax) =>
        syntax is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal
            ? literal.Token.ValueText : null;

    static bool IsExcluded(JsonElement editorial, string id) =>
        editorial.TryGetProperty(id, out var entry)
            && EditorialProperty(entry, "exclude").ValueKind == JsonValueKind.True;

    static string? ReadOverrideHeader(JsonElement editorial, string id)
    {
        if (!editorial.TryGetProperty(id, out var entry)) return null;
        var sample = EditorialProperty(entry, "sampleOverride");
        return ReadField(sample, "header")?.Trim();
    }

    static string? ReadField(JsonElement element, string field)
    {
        var property = EditorialProperty(element, field);
        return property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString() : null;
    }

    // Editorial field names are case-insensitive, while the control IDs are ordinal.
    static JsonElement EditorialProperty(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        ? element.EnumerateObject().LastOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).Value
        : default;

    static SyntaxNode ReadSource(string file) => CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
}
