using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_MOD_001: Detects the same <b>atomic-replace attached-placement</b>
/// modifier (<c>.Grid</c>, <c>.Canvas</c>, <c>.RelativePanel</c>, <c>.Flex</c>)
/// applied two or more times in one linear fluent chain, e.g.
/// <c>.Grid(row: 1).Grid(column: 2)</c>.
/// </summary>
/// <remarks>
/// These modifiers funnel through <c>Element.SetAttached(new XxxAttached(...))</c>,
/// which stores the value keyed by its record type. Each call constructs a
/// <em>fresh</em> record purely from its arguments, so a second call fully
/// <b>replaces</b> the first — it does not merge field-by-field. The intuitive
/// <c>.Grid(row: 1).Grid(column: 2)</c> therefore silently resets <c>row</c> to
/// its default (0); only <c>column</c> survives. The fix merges the calls into
/// one (<c>.Grid(row: 1, column: 2)</c>).
///
/// Modifiers that <em>read</em> the existing attached value and preserve the
/// untouched fields (<c>WrapGridColumnSpan</c>/<c>WrapGridRowSpan</c>) or that
/// accumulate (<c>Validate</c>) are additive, not atomic-replace, and are
/// deliberately excluded.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateAtomicModifierAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_MOD_001";

    /// <summary>
    /// Atomic-replace attached-placement modifier method name → the extension
    /// class that declares it. Each entry must be a modifier that calls
    /// <c>SetAttached(new XxxAttached(...))</c> with a record built purely from
    /// its own parameters (no read-modify-write of the existing attached value).
    /// Keep this in sync with the extension classes when either changes.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> AtomicModifiers =
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "Grid",          "GridExtensions" },
            { "Canvas",        "CanvasExtensions" },
            { "RelativePanel", "RelativePanelExtensions" },
            { "Flex",          "FlexExtensions" },
        };

    private const string ReactorNamespace = "Microsoft.UI.Reactor";

    private static readonly LocalizableString Title =
        "Duplicate atomic-replace modifier in one chain";

    private static readonly LocalizableString MessageFormat =
        "'.{0}(...)' is applied more than once in this chain; attached-placement modifiers are atomic-replace, so the last call wins and the earlier arguments are silently lost. Merge them into a single '.{0}(...)' call.";

    private static readonly LocalizableString Description =
        "Attached-placement modifiers (.Grid/.Canvas/.RelativePanel/.Flex) store " +
        "their value keyed by record type via Element.SetAttached. A second call " +
        "replaces the first outright instead of merging field-by-field, so " +
        "'.Grid(row: 1).Grid(column: 2)' resets row to 0. Combine the calls into " +
        "one so every argument is preserved.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Modifier",
        // Info is deliberate (spec 060 §12 table + §2.5): a nudge-class rule that
        // ships a safe merge fix. It is not promoted to Warning despite describing
        // silent argument loss — the auto-fix makes it low-friction to resolve.
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Syntactic fast path (spec §3): must be `receiver.Name(...)` where Name
        // is one of the atomic-replace placement modifiers.
        var name = GetFluentMethodName(invocation);
        if (name is null || !AtomicModifiers.ContainsKey(name))
            return;

        // Only report from the OUTERMOST occurrence of this name in the chain —
        // if an ancestor fluent link has the same name, let it report instead.
        if (HasAncestorWithSameName(invocation, name))
            return;

        // Walk down the receiver chain collecting every same-name occurrence.
        var occurrences = CollectSameNameOccurrences(invocation, name);
        if (occurrences.Count < 2)
            return;

        // Semantic confirmation: every occurrence must bind to the Reactor
        // extension modifier of that name (not some unrelated `.Grid()`/`.Flex()`).
        foreach (var occ in occurrences)
        {
            if (!IsReactorAtomicModifier(context.SemanticModel, occ, name, context.CancellationToken))
                return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            name));
    }

    /// <summary>Method name of a <c>receiver.Name(...)</c> fluent call, else null.</summary>
    internal static string? GetFluentMethodName(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax
        {
            RawKind: (int)SyntaxKind.SimpleMemberAccessExpression,
        } memberAccess
            ? memberAccess.Name.Identifier.Text
            : null;

    /// <summary>
    /// The invocation immediately below <paramref name="invocation"/> in a fluent
    /// chain — i.e. the receiver of its member access when that receiver is itself
    /// a method call. Returns null when the chain link is broken (identifier,
    /// property access, element access, …).
    /// </summary>
    internal static InvocationExpressionSyntax? GetReceiverInvocation(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax receiver }
            ? receiver
            : null;

    private static bool HasAncestorWithSameName(InvocationExpressionSyntax invocation, string name)
    {
        var current = (ExpressionSyntax)invocation;
        while (current.Parent is MemberAccessExpressionSyntax memberAccess
               && memberAccess.Expression == current
               && memberAccess.Parent is InvocationExpressionSyntax outer)
        {
            if (GetFluentMethodName(outer) == name)
                return true;
            current = outer;
        }
        return false;
    }

    /// <summary>
    /// Every same-name occurrence on the chain, innermost first (later/outer wins
    /// order for the code fix). Shared with <see cref="DuplicateAtomicModifierCodeFix"/>.
    /// </summary>
    internal static List<InvocationExpressionSyntax> CollectSameNameOccurrences(
        InvocationExpressionSyntax outermost, string name)
    {
        var stack = new List<InvocationExpressionSyntax>();
        for (var node = outermost; node is not null; node = GetReceiverInvocation(node))
        {
            if (GetFluentMethodName(node) == name)
                stack.Add(node);
        }
        stack.Reverse();
        return stack;
    }

    private static bool IsReactorAtomicModifier(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        string name,
        System.Threading.CancellationToken ct)
    {
        if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
            return false;

        var original = method.ReducedFrom ?? method.OriginalDefinition;
        var containingType = original.ContainingType;
        if (containingType is null)
            return false;

        return AtomicModifiers.TryGetValue(name, out var expectedClass)
            && containingType.Name == expectedClass
            && containingType.ContainingNamespace?.ToDisplayString() == ReactorNamespace;
    }
}
