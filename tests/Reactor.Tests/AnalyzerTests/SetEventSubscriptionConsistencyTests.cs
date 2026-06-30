using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Analyzers;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

public class SetEventSubscriptionConsistencyTests
{
    [Fact]
    public void Every_Tracked_Event_Has_A_Matching_Modifier()
    {
        var safeModifiers = ReadSafeEventModifiers();

        foreach (var modifierName in SetEventSubscriptionAnalyzer.EventModifiers.Values)
        {
            Assert.Contains(
                modifierName,
                safeModifiers);
        }
    }

    [Fact]
    public void Every_Safe_Modifier_Is_Tracked()
    {
        var safeModifiers = ReadSafeEventModifiers();
        var trackedModifiers = SetEventSubscriptionAnalyzer.EventModifiers.Values.ToHashSet(StringComparer.Ordinal);

        var missing = safeModifiers
            .Where(modifierName => !trackedModifiers.Contains(modifierName))
            .OrderBy(name => name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These safe declarative event modifiers exist in ElementExtensions.cs but are not tracked by SetEventSubscriptionAnalyzer.EventModifiers: " +
            $"[{string.Join(", ", missing)}]. Add them to the analyzer map or document why they are intentionally excluded.");
    }

    [Fact]
    public void Every_CodeFixable_Event_Is_A_Tracked_Event()
    {
        foreach (var (eventName, modifierName) in SetEventSubscriptionAnalyzer.CodeFixableEventModifiers)
        {
            Assert.True(
                SetEventSubscriptionAnalyzer.EventModifiers.TryGetValue(eventName, out var trackedModifierName) && trackedModifierName == modifierName,
                $"Code-fixable event '{eventName}' -> '{modifierName}' must also exist in EventModifiers.");
        }
    }

    private static HashSet<string> ReadSafeEventModifiers()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Join(root!, "src", "Reactor", "Elements", "ElementExtensions.cs");
        Assert.True(File.Exists(path), $"ElementExtensions.cs not found at {path}");
        var source = File.ReadAllText(path);

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilationUnit = tree.GetCompilationUnitRoot();

        var modifiers = compilationUnit
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.Text.StartsWith("On", StringComparison.Ordinal))
            .Where(method => method.ReturnType is IdentifierNameSyntax { Identifier.Text: "T" })
            .Where(method => MethodBodyContainsSameNamedModifierAssignment(method))
            .Select(method => method.Identifier.Text)
            .ToHashSet(StringComparer.Ordinal);

        return modifiers;
    }

    private static bool MethodBodyContainsSameNamedModifierAssignment(MethodDeclarationSyntax method)
    {
        var marker = method.Identifier.Text + " =";

        if (method.ExpressionBody is not null)
            return method.ExpressionBody.Expression.ToString().Contains(marker, StringComparison.Ordinal);

        if (method.Body is not null)
            return method.Body.ToString().Contains(marker, StringComparison.Ordinal);

        return false;
    }
}