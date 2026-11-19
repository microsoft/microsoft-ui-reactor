using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.SearchIndex;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

public sealed class SearchIndexCoverageTests
{
    enum Action { Skip, Replace }

    readonly record struct CardKey(string Control, string Title, int Occurrence = 1);
    readonly record struct CardChange(Action Action, string Reason);

    // This manifest is reviewed against the page source. A new omission fails the test even
    // if the generated JSON was refreshed. Remove an entry when its card starts shipping.
    static readonly Dictionary<CardKey, CardChange> Changes = new()
    {
        [new("auto-suggest-box", "Filtered Results Display")] = new(Action.Skip, "The Select projection is elided."),
        [new("card", "Card with icon, heading, and body")] = new(Action.Skip, "The icon Border is abbreviated."),
        [new("color", "<title>")] = new(Action.Skip, "BrushSection computes the card title and code at runtime."),
        [new("expander", "Basic Expander")] = new(Action.Skip, "Additional content children are elided."),
        [new("flex", "Grow & Shrink")] = new(Action.Skip, "The child Borders have abbreviated content."),
        [new("flip-view", "Data-Driven FlipView")] = new(Action.Skip, "The item template's VStack children are elided."),
        [new("grid", "Basic Grid")] = new(Action.Skip, "Remaining grid cells are elided."),
        [new("list-view", "Basic ListView")] = new(Action.Skip, "Additional list items are elided."),
        [new("list-view", "Animated edit (spec 042)")] = new(Action.Skip, "The mutation signature and item template are elided."),
        [new("map-control", "Interactive map")] = new(Action.Replace, "Editorial code supplies the complete no-token branch."),
        [new("media-player-element", "Video with transport controls")] = new(Action.Replace, "Editorial code supplies the complete fallback."),
        [new("rich-text-block", "Inline hyperlinks & embedded UI")] = new(Action.Skip, "The embedded button callback is elided."),
        [new("scroll-view", "Horizontal ScrollView (modern)")] = new(Action.Skip, "The HStack content is elided."),
        [new("scroll-view", "Classic ScrollViewer (legacy)")] = new(Action.Skip, "The VStack content is elided."),
        [new("spacing", "Spacing Scale")] = new(Action.Skip, "The card's snippet is a comment describing the scale."),
        [new("split-view", "Basic SplitView")] = new(Action.Replace, "Editorial code supplies the complete pane and content."),
        [new("tree-view", "Basic TreeView")] = new(Action.Skip, "The Pictures subtree is elided."),
        [new("type-ramp", "Factories")] = new(Action.Replace, "Editorial header names the factories; the original code is retained."),
        [new("typography", "Type Ramp")] = new(Action.Skip, "The code is assembled from the type ramp at runtime."),
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
        var problems = CoverageProblems(gallery, editorial.RootElement, index.RootElement, Changes);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
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
                new Dictionary<CardKey, CardChange>());
        }

        // Generate a fresh index in both cases. The coverage check must still see a dropped card.
        Assert.Empty(Check("Alpha(); // see Alpha(...), then continue"));
        Assert.Contains(Check("Alpha(a, ...);"), problem => problem.Contains("Missing: [Probe]", StringComparison.Ordinal));
    }

    static List<string> CoverageProblems(string gallery, JsonElement editorial, JsonElement index,
        IReadOnlyDictionary<CardKey, CardChange> changes)
    {
        var registered = RegistryIds(gallery);
        Assert.NotEmpty(registered);
        var included = registered.Where(id => !IsExcluded(editorial, id)).ToArray();
        var indexed = index.GetProperty("controls").EnumerateArray().ToDictionary(
            c => c.GetProperty("id").GetString()!, c => c.GetProperty("samples"), StringComparer.Ordinal);
        Assert.Equal(included.Order(StringComparer.Ordinal), indexed.Keys.Order(StringComparer.Ordinal));

        var routes = Routes(gallery);
        var pageNames = included.Select(id => routes[id]).ToHashSet(StringComparer.Ordinal);
        var pageFiles = Directory.EnumerateFiles(Path.Join(gallery, "ControlPages"), "*.cs", SearchOption.AllDirectories);
        var pages = pageFiles.SelectMany(file => ReadSource(file).DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(page => pageNames.Contains(page.Identifier.ValueText))
            .ToDictionary(page => page.Identifier.ValueText, page => CardInvocations(page).ToArray(), StringComparer.Ordinal);

        var remainingChanges = new Dictionary<CardKey, CardChange>(changes);
        var problems = new List<string>();
        foreach (var id in included)
        {
            var expected = new List<string>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            var overrideFields = ReadOverride(editorial, id);
            var replaced = false;
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

                if (remainingChanges.Remove(key, out var change))
                {
                    if (change.Action == Action.Skip) continue;

                    Assert.False(replaced, $"{id} has more than one expected override.");
                    Assert.Empty(expected);
                    Assert.True(overrideFields.Header is not null || overrideFields.Code is not null,
                        $"{id} / {title}: expected an editorial override ({change.Reason}).");
                    expected.Add(overrideFields.Header ?? title);
                    replaced = true;
                    continue;
                }

                if (literalTitle is null || sourceCode is null)
                {
                    var field = literalTitle is null ? "title" : "sourceCode";
                    problems.Add($"{id} / {title} ({count}): non-literal or absent {field} needs an explicit manifest entry.");
                    continue;
                }

                expected.Add(title);
            }

            if (!replaced && (overrideFields.Header is not null || overrideFields.Code is not null))
                problems.Add($"{id}: editorial override is missing a manifest entry.");

            var actual = indexed[id].EnumerateArray().Select(s => s.GetProperty("header").GetString()!).ToArray();
            if (expected.SequenceEqual(actual)) continue;

            var missing = expected.Except(actual).ToArray();
            var unexpected = actual.Except(expected).ToArray();
            problems.Add($"{id}: expected {expected.Count}, got {actual.Length}. "
                + $"Missing: [{string.Join(", ", missing)}]. "
                + $"Unexpected: [{string.Join(", ", unexpected)}]. "
                + "If both lists are empty, check the order or duplicate count.");
        }

        problems.AddRange(remainingChanges.Select(entry =>
            $"{entry.Key.Control} / {entry.Key.Title} ({entry.Key.Occurrence}): stale manifest entry ({entry.Value.Reason})."));
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

    static (string? Header, string? Code) ReadOverride(JsonElement editorial, string id)
    {
        if (!editorial.TryGetProperty(id, out var entry)) return default;
        var sample = EditorialProperty(entry, "sampleOverride");
        var code = ReadField(sample, "code");
        return (ReadField(sample, "header")?.Trim(), code);
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
