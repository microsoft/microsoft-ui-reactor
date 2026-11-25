using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// The source of the <c>Reconciler</c> partial class (<c>src/Reactor/Core/Reconciler.cs</c> and
/// every <c>Reconciler.*.cs</c>), parsed once per test run. Gates that read a reconciler method's
/// body look it up across all the partial files, so moving a method between them doesn't break
/// the gate.
/// </summary>
internal static class ReconcilerSource
{
    private static readonly Lazy<IReadOnlyList<SyntaxNode>> s_roots = new(() =>
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var dir = Path.Join(root!, "src", "Reactor", "Core");
        var files = Directory.EnumerateFiles(dir, "*.cs")
            .Where(file => Path.GetFileName(file) is var name
                && (name == "Reconciler.cs" || name.StartsWith("Reconciler.", StringComparison.Ordinal)))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();
        Assert.True(files.Any(file => Path.GetFileName(file) == "Reconciler.cs"), $"Reconciler.cs not found in {dir}");
        return files.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot()).ToList();
    });

    /// <summary>Every method named <paramref name="name"/> declared in the reconciler's partial files.</summary>
    public static List<MethodDeclarationSyntax> Methods(string name) =>
        s_roots.Value
            .SelectMany(root => root.DescendantNodes())
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.Text == name)
            .ToList();

    /// <summary>Every method whose name is in <paramref name="names"/>.</summary>
    public static List<MethodDeclarationSyntax> Methods(ISet<string> names) =>
        s_roots.Value
            .SelectMany(root => root.DescendantNodes())
            .OfType<MethodDeclarationSyntax>()
            .Where(method => names.Contains(method.Identifier.Text))
            .ToList();
}
