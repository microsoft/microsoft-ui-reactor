using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;
using Path = global::System.IO.Path;

namespace Microsoft.UI.Reactor.Tests;

public class FixtureRegistryConsistencyTests
{
    [Theory]
    [InlineData("SelfTest/SelfTestFixtureRegistry.cs", 1000)]
    [InlineData("FixtureRegistry.cs", 50)]
    public void RegisteredFixtureNamesAreUnique(string registryFile, int minimumCount)
    {
        var repoRoot = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(repoRoot);

        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Join(repoRoot, "tests", "Reactor.AppTests.Host", registryFile);
        var tree = ParseFile(path, cancellationToken);
        Assert.DoesNotContain(tree.GetDiagnostics(cancellationToken), d => d.Severity == DiagnosticSeverity.Error);

        var root = tree.GetRoot(cancellationToken);
        var registry = Assert.Single(root.DescendantNodes().OfType<ClassDeclarationSyntax>(),
            type => type.Identifier.ValueText == Path.GetFileNameWithoutExtension(path));
        var registrations = Assert.Single(registry.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables), field => field.Identifier.ValueText == "Registrations");
        var initializer = Assert.IsType<CollectionExpressionSyntax>(registrations.Initializer?.Value);

        SemanticModel? model = null;
        string ReadName(ExpressionSyntax expression)
        {
            if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                return literal.Token.ValueText;

            // Resolve const string names from fixture declarations through Roslyn.
            model ??= CreateNameModel(tree, cancellationToken);
            var constant = model.GetConstantValue(expression, cancellationToken);
            Assert.True(constant.HasValue && constant.Value is string,
                $"{registryFile} fixture name `{expression}` must resolve to a constant string.");
            return (string)constant.Value!;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in initializer.Elements)
        {
            var expression = Assert.IsType<ExpressionElementSyntax>(item).Expression;
            var entry = Assert.IsType<TupleExpressionSyntax>(expression);
            var name = ReadName(entry.Arguments[0].Expression);
            Assert.True(names.Add(name), $"{registryFile} registers fixture '{name}' more than once.");
        }

        Assert.True(names.Count >= minimumCount,
            $"Expected at least {minimumCount} fixture names in {registryFile}, but found {names.Count}.");
    }

    private static SyntaxTree ParseFile(string path, CancellationToken cancellationToken) =>
        CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path, cancellationToken: cancellationToken);

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "Reactor.Tests runs from a regular build. Roslyn reads the core library metadata from disk.")]
    private static SemanticModel CreateNameModel(SyntaxTree registry, CancellationToken cancellationToken)
    {
        var fixtureDirectory = Path.Join(Path.GetDirectoryName(registry.FilePath), "Fixtures");
        var fixtures = Directory.EnumerateFiles(fixtureDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(path => ParseFile(path, cancellationToken));
        // Only constant values are needed. The compilation is never emitted or executed.
        return CSharpCompilation.Create("FixtureRegistryNames", fixtures.Prepend(registry),
            [MetadataReference.CreateFromFile(typeof(string).Assembly.Location)])
            .GetSemanticModel(registry);
    }
}
