using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_DSL_001</c> — when a LINQ <c>Select</c> projects to Reactor
/// elements and the result is materialized into a layout container's children
/// (<c>VStack</c>, <c>HStack</c>, <c>FlexRow</c>, <c>FlexColumn</c>, <c>Grid</c>, ...),
/// every projected element should call <c>.WithKey(...)</c>. Without keys, the
/// reconciler matches positionally and re-mounts every row on insert / reorder
/// — losing focus, animation state, and ElementRef identity.
///
/// Heuristic: a <c>Select(x =&gt; expr)</c> invocation whose lambda body
/// (a) returns a Reactor element-shaped expression, and
/// (b) contains no <c>.WithKey(</c> token anywhere in the lambda body.
///
/// Conservative — fires only on `.Select(...)` whose lambda body's outermost
/// expression is an invocation (the typical "row factory" pattern).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingWithKeyAnalyzer : DiagnosticAnalyzer
{
    public const string Id = "REACTOR_DSL_001";

    private static readonly DiagnosticDescriptor Rule = new(
        Id,
        "Dynamic list item missing .WithKey",
        "Element produced by Select(...) doesn't call .WithKey(...). Without a key, the reconciler matches by position and re-mounts every row on insert/reorder, losing focus, animation, and ElementRef state.",
        "Reactor.Dsl",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Per SKILL.md gotcha #6 — every dynamic list item should carry a stable key via .WithKey(id). The reconciler uses keys to match elements across renders. Without them, inserting at the head of a list re-mounts every row.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    static void Analyze(SyntaxNodeAnalysisContext ctx)
    {
        var inv = (InvocationExpressionSyntax)ctx.Node;

        if (inv.Expression is not MemberAccessExpressionSyntax member) return;
        if (member.Name.Identifier.ValueText != "Select") return;

        // Single lambda argument with an invocation body.
        if (inv.ArgumentList.Arguments.Count != 1) return;
        if (inv.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda) return;

        var body = lambda.Body;
        if (body is BlockSyntax block) body = ExtractReturnExpression(block) ?? body;
        if (body is not InvocationExpressionSyntax) return;

        // Cheap textual probe — analyzers run hot, so avoid full symbol resolution.
        // If the lambda body mentions ".WithKey(" anywhere, assume it's keyed.
        var bodyText = body.ToString();
        if (bodyText.Contains(".WithKey(")) return;

        // Only flag when the result is consumed as children of a layout factory
        // (VStack / HStack / FlexRow / FlexColumn / Grid / ScrollView). This
        // keeps false positives out of generic LINQ that just happens to project
        // to elements (e.g., to a List<Element>).
        if (!IsConsumedAsLayoutChildren(inv)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Rule, inv.GetLocation()));
    }

    static ExpressionSyntax? ExtractReturnExpression(BlockSyntax block)
    {
        // Single-statement `return X;` — walk it. Multi-statement bodies are
        // out of scope for this conservative pass.
        if (block.Statements.Count != 1) return null;
        return block.Statements[0] is ReturnStatementSyntax ret ? ret.Expression : null;
    }

    // Layout factories whose children-array overload is the typical receiver
    // for `Select(...)` row-factories. ScrollView is intentionally excluded —
    // it takes a single child, not Element[]. WrapGrid (not WrapPanel) is the
    // correct factory name in Reactor.
    static readonly System.Collections.Generic.HashSet<string> LayoutFactories = new(System.StringComparer.Ordinal)
    {
        "VStack", "HStack", "FlexRow", "FlexColumn", "Flex", "Grid", "WrapGrid",
    };

    static bool IsConsumedAsLayoutChildren(InvocationExpressionSyntax selectInv)
    {
        // Walk up: Select → optional .ToArray()/.ToList()/.ToArray<Element>() → Argument → Invocation
        SyntaxNode? cur = selectInv;
        while (cur?.Parent is MemberAccessExpressionSyntax m && m.Parent is InvocationExpressionSyntax chain)
        {
            var name = m.Name.Identifier.ValueText;
            if (name is "ToArray" or "ToList") cur = chain;
            else break;
        }

        if (cur?.Parent is not ArgumentSyntax arg) return false;
        if (arg.Parent is not ArgumentListSyntax argList) return false;
        if (argList.Parent is not InvocationExpressionSyntax outer) return false;

        var outerName = outer.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax mae => mae.Name.Identifier.ValueText,
            _ => null,
        };

        return outerName is not null && LayoutFactories.Contains(outerName);
    }
}
