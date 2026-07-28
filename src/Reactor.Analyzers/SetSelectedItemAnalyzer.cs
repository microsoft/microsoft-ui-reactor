using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_CTRL_001: Detects <c>.Set(cb =&gt; cb.SelectedItem = ...)</c> (or
/// <c>SelectedValue</c>) on a Reactor selector element
/// (<see cref="SetLambdaHelpers.SelectedIndexControlledElements"/>) that <b>also</b> sets
/// its controlled <c>SelectedIndex</c>. The imperative <c>SelectedItem</c> write becomes a
/// second selection authority that races and clobbers the controlled index. The fix
/// deletes the redundant <c>.Set(...)</c> call — <c>SelectedIndex</c> is the authority.
/// </summary>
/// <remarks>
/// Only fires when a competing <c>SelectedIndex</c> is actually set on the same element
/// expression (a factory/ctor argument or an object/<c>with</c> initializer). The fix does
/// not auto-convert <c>SelectedItem</c> → <c>SelectedIndex</c> — that is not a mechanical
/// rewrite. See spec 060 §4.2.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SetSelectedItemAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_CTRL_001";

    private static readonly LocalizableString Title =
        "Do not set SelectedItem alongside the controlled SelectedIndex";

    private static readonly LocalizableString MessageFormat =
        "'{0}' selection is controlled through SelectedIndex; '.Set(x => x.{1} = ...)' creates a competing authority that clobbers it. Remove the '.Set(...)' call.";

    private static readonly LocalizableString Description =
        "Reactor selectors expose a controlled 'Optional<int> SelectedIndex'. Assigning the " +
        "native control's SelectedItem/SelectedValue through '.Set(...)' establishes a second " +
        "selection authority that races the controlled index write on every reconcile. Keep " +
        "SelectedIndex as the single source of truth and remove the imperative SelectedItem set.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Controls",
        DiagnosticSeverity.Warning,
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

        // Syntactic fast path: receiver.Set(x => x.SelectedItem/SelectedValue = value).
        if (!SetLambdaHelpers.IsSetInvocation(invocation, out var memberAccess))
            return;

        var lambdaExpr = invocation.ArgumentList.Arguments[0].Expression;
        var assignment = SetLambdaHelpers.TryGetLambdaAssignment(lambdaExpr);
        if (assignment is null || assignment.Kind() != SyntaxKind.SimpleAssignmentExpression)
            return;

        var lambdaParam = SetLambdaHelpers.GetSingleLambdaParameter(lambdaExpr);
        if (lambdaParam is null)
            return;
        var leftAccess = SetLambdaHelpers.GetAssignedMemberAccess(assignment, lambdaParam.Identifier.Text);
        if (leftAccess is null)
            return;
        var member = leftAccess.Name.Identifier.Text;
        if (member != "SelectedItem" && member != "SelectedValue")
            return;

        // Guard against an unrelated user-defined '.Set' extension on a Reactor element:
        // the destructive delete-fix is only sound for Reactor's own .Set setter.
        if (!SetLambdaHelpers.IsReactorSetInvocation(invocation, context.SemanticModel, context.CancellationToken))
            return;

        // Semantic gate 1: receiver is one of the curated SelectedIndex-controlled elements.
        var receiverType = context.SemanticModel
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (!SetLambdaHelpers.IsCuratedReactorElement(receiverType, SetLambdaHelpers.SelectedIndexControlledElements))
            return;

        // Semantic gate 2: the same element expression also sets a competing SelectedIndex.
        if (!ElementAlsoSetsSelectedIndex(memberAccess.Expression, context.SemanticModel, context.CancellationToken))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            receiverType!.Name,
            member));
    }

    /// <summary>
    /// Walks the receiver's fluent-chain / construction "spine" looking for a competing
    /// <c>SelectedIndex</c> being set — a named/positional argument on a factory or record
    /// constructor, or an object/<c>with</c> initializer member. Deliberately does not
    /// descend into argument subtrees (which could belong to nested, unrelated elements);
    /// a <c>SelectedIndex</c> set in a separate statement is out of scope for a syntactic
    /// analyzer.
    /// </summary>
    private static bool ElementAlsoSetsSelectedIndex(
        ExpressionSyntax receiver, SemanticModel model, CancellationToken ct)
    {
        for (ExpressionSyntax? current = receiver; current is not null;)
        {
            switch (current)
            {
                case InvocationExpressionSyntax inv:
                    // Only a Reactor selector factory/modifier argument named 'SelectedIndex'
                    // authoritatively wires the element's controlled index. An arbitrary
                    // helper that merely has a like-named parameter must not trip the
                    // destructive delete-fix.
                    if (IsReactorSelectorFactory(inv, model, ct)
                        && ArgumentsSetSelectedIndex(inv, inv.ArgumentList, model, ct))
                        return true;
                    current = inv.Expression is MemberAccessExpressionSyntax ma ? ma.Expression : null;
                    break;

                case ObjectCreationExpressionSyntax oce:
                    return InitializerSetsSelectedIndex(oce.Initializer)
                        || (oce.ArgumentList is not null && ArgumentsSetSelectedIndex(oce, oce.ArgumentList, model, ct));

                case ImplicitObjectCreationExpressionSyntax ioce:
                    return InitializerSetsSelectedIndex(ioce.Initializer)
                        || ArgumentsSetSelectedIndex(ioce, ioce.ArgumentList, model, ct);

                case WithExpressionSyntax we:
                    if (InitializerSetsSelectedIndex(we.Initializer))
                        return true;
                    current = we.Expression;
                    break;

                case MemberAccessExpressionSyntax ma2:
                    current = ma2.Expression;
                    break;

                case ParenthesizedExpressionSyntax pe:
                    current = pe.Expression;
                    break;

                default:
                    return false;
            }
        }
        return false;
    }

    /// <summary>
    /// True when the invocation is a Reactor DSL factory/modifier — a method under the
    /// <c>Microsoft.UI.Reactor</c> namespace returning one of the curated selector elements.
    /// This scopes the "argument named <c>SelectedIndex</c>" heuristic to the framework's own
    /// selector factories so an unrelated user helper that merely has a like-named parameter
    /// cannot trigger the destructive delete-fix on valid code.
    /// </summary>
    private static bool IsReactorSelectorFactory(
        InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken ct)
    {
        if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
            return false;

        var ns = method.ContainingNamespace?.ToDisplayString();
        if (ns is null ||
            !(ns == "Microsoft.UI.Reactor" || ns.StartsWith("Microsoft.UI.Reactor.", StringComparison.Ordinal)))
            return false;

        return method.ReturnType is INamedTypeSymbol returnType
            && SetLambdaHelpers.SelectedIndexControlledElements.Contains(returnType.Name);
    }

    private static bool InitializerSetsSelectedIndex(InitializerExpressionSyntax? initializer)
    {
        if (initializer is null)
            return false;
        foreach (var expr in initializer.Expressions)
        {
            if (expr is AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.Text: "SelectedIndex" } } a
                && IsRealSelectedIndexValue(a.Right))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ArgumentsSetSelectedIndex(
        ExpressionSyntax callNode, ArgumentListSyntax? argList, SemanticModel model, CancellationToken ct)
    {
        if (argList is null)
            return false;

        // Named argument: selectedIndex: / SelectedIndex: (factory vs. record ctor casing).
        foreach (var arg in argList.Arguments)
        {
            if (arg.NameColon is { } nameColon &&
                string.Equals(nameColon.Name.Identifier.Text, "SelectedIndex", StringComparison.OrdinalIgnoreCase))
            {
                return IsRealSelectedIndexValue(arg.Expression);
            }
        }

        // Positional argument bound to a 'SelectedIndex' parameter. Only maps leading
        // positional arguments (SelectedIndex is never the trailing params items array).
        if (model.GetSymbolInfo(callNode, ct).Symbol is IMethodSymbol method)
        {
            for (int i = 0; i < argList.Arguments.Count; i++)
            {
                var arg = argList.Arguments[i];
                if (arg.NameColon is not null)
                    break; // remaining args are named; positional index mapping no longer valid
                if (i >= method.Parameters.Length)
                    break;
                var parameter = method.Parameters[i];
                if (!parameter.IsParams &&
                    string.Equals(parameter.Name, "SelectedIndex", StringComparison.OrdinalIgnoreCase))
                {
                    return IsRealSelectedIndexValue(arg.Expression);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A <c>SelectedIndex</c> explicitly left unset (<c>default</c> / <c>Optional&lt;int&gt;.Unset</c>)
    /// is not a competing authority, so it doesn't count as "also sets SelectedIndex".
    /// </summary>
    private static bool IsRealSelectedIndexValue(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.DefaultLiteralExpression) => false,
        DefaultExpressionSyntax => false,
        MemberAccessExpressionSyntax ma when ma.Name.Identifier.Text == "Unset" => false,
        _ => true,
    };
}
