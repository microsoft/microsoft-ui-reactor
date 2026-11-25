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
/// The source of the <c>Reconciler</c> partial class (every <c>Reconciler.cs</c> /
/// <c>Reconciler.*.cs</c> under <c>src/Reactor/Core</c>, subfolders included) plus the internal
/// static helper classes the reconciler delegates to (<see cref="HelperFiles"/>), parsed once per
/// test run. Gates that read a reconciler method's body look it up across all of these files, so
/// moving a method between them doesn't break the gate.
/// </summary>
internal static class ReconcilerSource
{
    /// <summary>
    /// Static helper classes split out of the reconciler's partials (theme bindings, accessibility
    /// modifiers, modifier events, reference edges, gestures, drag-and-drop). Listed by name so a
    /// rename fails the existence assertion below instead of silently hiding methods from the gates.
    /// </summary>
    private static readonly string[] HelperFiles =
    [
        "AccessibilityApplier.cs",
        "DragDropDispatch.cs",
        "GestureDispatch.cs",
        "ModifierEvents.cs",
        "ReferenceEdgeWiring.cs",
        "ThemeBindingApplier.cs",
    ];

    private static readonly Lazy<IReadOnlyList<SyntaxNode>> s_roots = new(() =>
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var dir = Path.Join(root!, "src", "Reactor", "Core");
        var files = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(file => Path.GetFileName(file) is var name
                && (name == "Reconciler.cs" || name.StartsWith("Reconciler.", StringComparison.Ordinal)
                    || HelperFiles.Contains(name, StringComparer.Ordinal)))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();
        Assert.True(files.Any(file => Path.GetFileName(file) == "Reconciler.cs"), $"Reconciler.cs not found in {dir}");
        foreach (var helper in HelperFiles)
            Assert.True(files.Any(file => Path.GetFileName(file) == helper), $"{helper} not found in {dir}");
        return files.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot()).ToList();
    });

    /// <summary>Every method named <paramref name="name"/> declared in the reconciler's source files.</summary>
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

    /// <summary>
    /// The name gates use for the whole modifier-apply pass: <c>ApplyModifiers</c> plus the
    /// per-family helpers reachable from it (see <see cref="ApplyModifiersFamily"/>).
    /// </summary>
    public const string ApplyModifiersName = "ApplyModifiers";

    /// <summary>
    /// <c>ApplyModifiers</c> and every family helper reachable from it — the methods that,
    /// together, hold the set/unset arm pairs of the common modifiers.
    /// </summary>
    /// <remarks>
    /// <c>ApplyModifiers</c> is an ordered sequence of <c>Apply…Modifier(s)(fe, oldM, m, …)</c>
    /// calls, so a gate that read only its own body would see none of the arms. A helper joins
    /// the family by convention, not by a hand-kept list: it is called from <c>ApplyModifiers</c>
    /// or from another family helper (transitively), its name matches <c>Apply…Modifier</c> or
    /// <c>Apply…Modifiers</c>, and it takes <c>ElementModifiers? oldM</c> and
    /// <c>ElementModifiers m</c> (see <see cref="IsModifierFamilyHelper"/>). A helper that breaks
    /// the convention, or is declared outside the reconciler's partial files, would be missed;
    /// <c>ApplyModifiers_Delegates_Only_To_Scanned_Or_Recorded_Helpers</c> fails on it by name,
    /// driven by <see cref="ApplyModifiersBagHandoffs"/>.
    /// </remarks>
    public static List<MethodDeclarationSyntax> ApplyModifiersFamily()
    {
        var family = Methods(ApplyModifiersName);
        var names = new HashSet<string>(StringComparer.Ordinal) { ApplyModifiersName };
        var candidates = s_roots.Value
            .SelectMany(root => root.DescendantNodes())
            .OfType<MethodDeclarationSyntax>()
            .Where(IsModifierFamilyHelper)
            .ToList();

        // Fixpoint: a helper extracted out of another helper is still part of the family.
        while (true)
        {
            var called = CalledMethodNames(family);
            var added = candidates
                .Where(method => called.Contains(method.Identifier.Text) && !names.Contains(method.Identifier.Text))
                .ToList();
            if (added.Count == 0) return family;

            family.AddRange(added);
            foreach (var method in added) names.Add(method.Identifier.Text);
        }
    }

    /// <summary>
    /// The name of every method the <see cref="ApplyModifiersFamily"/> hands a modifier bag to —
    /// any call that passes the <c>oldM</c> or <c>m</c> identifier as an argument — whether or
    /// not a declaration for it exists in the reconciler's partial files.
    /// </summary>
    /// <remarks>
    /// Driven by call sites rather than by declarations, so a helper that moved out of the
    /// <c>Reconciler.*.cs</c> files (and so no longer resolves) still shows up here.
    /// </remarks>
    public static HashSet<string> ApplyModifiersBagHandoffs() =>
        ApplyModifiersFamily()
            .SelectMany(method => method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(invocation => invocation.ArgumentList.Arguments.Any(argument =>
                argument.Expression is IdentifierNameSyntax { Identifier.Text: "oldM" or "m" }))
            .Select(InvokedName)
            .Where(name => name is not null && name != ApplyModifiersName)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The family-helper convention: an <c>Apply…Modifier(s)</c> method taking
    /// <c>ElementModifiers? oldM</c> and <c>ElementModifiers m</c>.
    /// </summary>
    public static bool IsModifierFamilyHelper(MethodDeclarationSyntax method)
    {
        var name = method.Identifier.Text;
        if (name == ApplyModifiersName
            || !name.StartsWith("Apply", StringComparison.Ordinal)
            || !(name.EndsWith("Modifier", StringComparison.Ordinal) || name.EndsWith("Modifiers", StringComparison.Ordinal)))
            return false;

        var parameters = method.ParameterList.Parameters;
        return parameters.Any(p => p.Identifier.Text == "oldM" && IsElementModifiers(p.Type))
            && parameters.Any(p => p.Identifier.Text == "m" && IsElementModifiers(p.Type));
    }

    private static bool IsElementModifiers(TypeSyntax? type) => type switch
    {
        NullableTypeSyntax nullable => IsElementModifiers(nullable.ElementType),
        _ => type?.ToString() is { } text && text[(text.LastIndexOf('.') + 1)..] == "ElementModifiers",
    };

    private static HashSet<string> CalledMethodNames(IEnumerable<MethodDeclarationSyntax> methods) =>
        methods
            .SelectMany(method => method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Select(InvokedName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

    private static string? InvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        _ => null,
    };
}
