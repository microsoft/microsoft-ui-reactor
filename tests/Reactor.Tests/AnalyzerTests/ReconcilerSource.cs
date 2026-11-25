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
/// <c>Reconciler.*.cs</c> under <c>src/Reactor/Core</c>, subfolders included), parsed once per
/// test run. Gates that read a reconciler method's body look it up across all the partial files,
/// so moving a method between them doesn't break the gate.
/// </summary>
internal static class ReconcilerSource
{
    private static readonly Lazy<IReadOnlyList<SyntaxNode>> s_roots = new(() =>
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var dir = Path.Join(root!, "src", "Reactor", "Core");
        var files = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
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

    /// <summary>
    /// The name gates use for the whole modifier-apply pass: <c>ApplyModifiers</c> plus the
    /// per-family helpers it calls (see <see cref="ApplyModifiersFamily"/>).
    /// </summary>
    public const string ApplyModifiersName = "ApplyModifiers";

    /// <summary>
    /// <c>ApplyModifiers</c> and every family helper it calls directly — the methods that,
    /// together, hold the set/unset arm pairs of the common modifiers.
    /// </summary>
    /// <remarks>
    /// <c>ApplyModifiers</c> is an ordered sequence of <c>Apply…Modifier(s)(fe, oldM, m, …)</c>
    /// calls, so a gate that read only its own body would see none of the arms. A helper joins
    /// the family by convention, not by a hand-kept list: it is invoked from
    /// <c>ApplyModifiers</c>, its name matches <c>Apply…Modifier</c> or <c>Apply…Modifiers</c>,
    /// and it takes <c>ElementModifiers? oldM</c> and <c>ElementModifiers m</c> (see
    /// <see cref="IsModifierFamilyHelper"/>). A newly extracted helper is therefore scanned the
    /// moment it is called. A helper that takes the bags under some other name would be missed
    /// silently, which is what <c>ApplyModifiers_Delegates_Only_To_Scanned_Or_Recorded_Helpers</c>
    /// guards against.
    /// </remarks>
    public static List<MethodDeclarationSyntax> ApplyModifiersFamily()
    {
        var roots = Methods(ApplyModifiersName);
        var called = CalledMethodNames(roots);

        return roots
            .Concat(s_roots.Value
                .SelectMany(root => root.DescendantNodes())
                .OfType<MethodDeclarationSyntax>()
                .Where(method => called.Contains(method.Identifier.Text) && IsModifierFamilyHelper(method)))
            .ToList();
    }

    /// <summary>
    /// Every reconciler method <c>ApplyModifiers</c> calls that takes the modifier bags —
    /// family helpers and delegated subsystems alike.
    /// </summary>
    public static List<MethodDeclarationSyntax> ApplyModifiersBagConsumers()
    {
        var called = CalledMethodNames(Methods(ApplyModifiersName));

        return s_roots.Value
            .SelectMany(root => root.DescendantNodes())
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.Text != ApplyModifiersName
                && called.Contains(method.Identifier.Text)
                && method.ParameterList.Parameters.Any(p => IsElementModifiers(p.Type)))
            .ToList();
    }

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
            .Select(invocation => invocation.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
                _ => null,
            })
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);
}
