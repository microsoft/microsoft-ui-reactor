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
    private static readonly HashSet<string> IntentionallyExcludedModifiers =
        new(StringComparer.Ordinal)
        {
            // These are drop-target abstractions over DragTargetArgs/DropTargetConfig,
            // not 1:1 replacements for raw WinUI drag event subscriptions.
            "OnDragEnter",
            "OnDragOver",
            "OnDragLeave",
            "OnDrop",
        };

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
            .Where(modifierName =>
                !IntentionallyExcludedModifiers.Contains(modifierName) &&
                !trackedModifiers.Contains(modifierName))
            .OrderBy(name => name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These safe declarative event modifiers exist in ElementExtensions.cs but are not tracked by SetEventSubscriptionAnalyzer.EventModifiers: " +
            $"[{string.Join(", ", missing)}]. Add them to the analyzer map or document why they are intentionally excluded.");
    }

    [Fact]
    public void Intentionally_Excluded_Modifiers_Exist()
    {
        var safeModifiers = ReadSafeEventModifiers();

        foreach (var modifierName in IntentionallyExcludedModifiers)
        {
            Assert.Contains(modifierName, safeModifiers);
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
        return method
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.Left is IdentifierNameSyntax leftIdentifier &&
                leftIdentifier.Identifier.Text == method.Identifier.Text);
    }
}