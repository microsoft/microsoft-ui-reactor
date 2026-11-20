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
    [InlineData("SelfTest/SelfTestFixtureRegistry.cs", "Create", 1000)]
    [InlineData("FixtureRegistry.cs", "Build", 50)]
    public void AllFixturesMatchesFactory(string registryFile, string factoryName, int minimumCount)
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
        var allFixtures = Assert.Single(registry.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables), field => field.Identifier.ValueText == "AllFixtures");
        var initializer = Assert.IsType<CollectionExpressionSyntax>(allFixtures.Initializer?.Value);
        var factory = Assert.Single(registry.Members.OfType<MethodDeclarationSyntax>(),
            method => method.Identifier.ValueText == factoryName);
        var dispatch = Assert.IsType<SwitchExpressionSyntax>(factory.ExpressionBody?.Expression);

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

        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in initializer.Elements)
        {
            var name = ReadName(Assert.IsType<ExpressionElementSyntax>(item).Expression);
            Assert.True(listed.Add(name), $"{registryFile} lists fixture '{name}' more than once in AllFixtures.");
        }

        Assert.Single(dispatch.Arms, arm => arm.Pattern is DiscardPatternSyntax);
        var registered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arm in dispatch.Arms)
        {
            Assert.True(arm.WhenClause is null,
                $"{registryFile} has a guarded {factoryName}() arm for `{arm.Pattern}`.");
            if (arm.Pattern is DiscardPatternSyntax)
                continue;

            var name = ReadName(Assert.IsType<ConstantPatternSyntax>(arm.Pattern).Expression);
            Assert.True(registered.Add(name), $"{registryFile} registers fixture '{name}' more than once in {factoryName}().");
        }

        Assert.True(listed.Count >= minimumCount && registered.Count >= minimumCount,
            $"Expected at least {minimumCount} fixture names on each side of {registryFile}. " +
            $"AllFixtures has {listed.Count} and {factoryName}() has {registered.Count}.");

        var onlyListed = listed.Except(registered, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var onlyRegistered = registered.Except(listed, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.True(onlyListed.Length == 0 && onlyRegistered.Length == 0,
            $"{registryFile} has inconsistent fixture registrations.\n" +
            $"In AllFixtures but missing from {factoryName}(): {string.Join(", ", onlyListed)}\n" +
            $"In {factoryName}() but missing from AllFixtures: {string.Join(", ", onlyRegistered)}");
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
