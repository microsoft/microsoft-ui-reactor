using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for REACTOR_VIS_001: rewrites
/// <c>x.Set(c =&gt; c.Visibility = Visibility.Collapsed)</c> to
/// <c>x.IsVisible(false)</c> (and <c>Visibility.Visible</c> → <c>.IsVisible(true)</c>).
/// A ternary RHS (<c>cond ? Collapsed : Visible</c>) is rewritten by polarity to
/// <c>.IsVisible(!cond)</c> / <c>.IsVisible(cond)</c>.
/// </summary>
/// <remarks>
/// <c>.IsVisible</c> only round-trips the two boolean-mappable values
/// (<c>Reconciler.cs</c> maps <c>true</c> → <c>Visible</c>, <c>false</c> →
/// <c>Collapsed</c>). Any other RHS — a variable, a call, a ternary whose polarity can't be
/// determined — gets the diagnostic but no fix (the analyzer still flags the trap).
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SetVisibilityCodeFix))]
[Shared]
public sealed class SetVisibilityCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(PoolResetSetAnalyzer.VisibilityDiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            if (node is not InvocationExpressionSyntax invocation)
                continue;
            if (!SetLambdaHelpers.IsSetInvocation(invocation, out var memberAccess))
                continue;

            var assignment = SetLambdaHelpers.TryGetLambdaAssignment(invocation.ArgumentList.Arguments[0].Expression);
            if (assignment is null)
                continue;

            var boolArgument = TryBuildIsVisibleArgument(assignment.Right);
            if (boolArgument is null)
                continue; // Non-mappable RHS: leave the warning unfixed.

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Use .IsVisible(...) modifier",
                    ct =>
                    {
                        var newInvocation = SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                memberAccess.Expression,
                                SyntaxFactory.IdentifierName("IsVisible")),
                            SyntaxFactory.ArgumentList(
                                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(boolArgument))))
                            .WithTriviaFrom(invocation);

                        var newRoot = root.ReplaceNode(invocation, newInvocation);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: PoolResetSetAnalyzer.VisibilityDiagnosticId),
                diagnostic);
        }
    }

    /// <summary>
    /// Translate a <c>Visibility</c> RHS into the boolean argument for <c>.IsVisible(...)</c>,
    /// or <c>null</c> when no sound rewrite exists.
    /// </summary>
    private static ExpressionSyntax? TryBuildIsVisibleArgument(ExpressionSyntax rhs)
    {
        if (TryGetVisibilityBool(rhs, out var isVisible))
            return BoolLiteral(isVisible);

        // Ternary: cond ? <Visibility> : <Visibility> with opposite polarity.
        if (rhs is ConditionalExpressionSyntax ternary &&
            TryGetVisibilityBool(ternary.WhenTrue, out var whenTrue) &&
            TryGetVisibilityBool(ternary.WhenFalse, out var whenFalse) &&
            whenTrue != whenFalse)
        {
            // whenTrue == Visible  → IsVisible tracks the condition.
            // whenTrue == Collapsed → IsVisible is the negation.
            return whenTrue ? ternary.Condition : Negate(ternary.Condition);
        }

        return null;
    }

    /// <summary>
    /// Recognizes <c>Visibility.Visible</c> / <c>Visibility.Collapsed</c> written with the
    /// enum spelled as <c>Visibility</c> — bare (<c>Visibility.Collapsed</c>) or
    /// namespace-qualified (<c>Microsoft.UI.Xaml.Visibility.Collapsed</c>) — and maps it to
    /// the <c>.IsVisible</c> boolean. An enum alias, or a bare member brought in via
    /// <c>using static</c>, is deliberately not recognized and simply yields no fix (the
    /// analyzer still reports the trap).
    /// </summary>
    private static bool TryGetVisibilityBool(ExpressionSyntax expression, out bool isVisible)
    {
        isVisible = false;
        if (expression is not MemberAccessExpressionSyntax member)
            return false;
        if (!IsVisibilityEnumContainer(member.Expression))
            return false;

        switch (member.Name.Identifier.Text)
        {
            case "Visible":
                isVisible = true;
                return true;
            case "Collapsed":
                isVisible = false;
                return true;
            default:
                return false;
        }
    }

    private static bool IsVisibilityEnumContainer(ExpressionSyntax container) => container switch
    {
        IdentifierNameSyntax { Identifier.Text: "Visibility" } => true,
        MemberAccessExpressionSyntax { Name.Identifier.Text: "Visibility" } => true,
        AliasQualifiedNameSyntax { Name.Identifier.Text: "Visibility" } => true,
        _ => false,
    };

    private static LiteralExpressionSyntax BoolLiteral(bool value) =>
        SyntaxFactory.LiteralExpression(
            value ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);

    private static ExpressionSyntax Negate(ExpressionSyntax condition)
    {
        var operand = condition switch
        {
            ParenthesizedExpressionSyntax => condition,
            IdentifierNameSyntax => condition,
            MemberAccessExpressionSyntax => condition,
            InvocationExpressionSyntax => condition,
            ElementAccessExpressionSyntax => condition,
            LiteralExpressionSyntax => condition,
            _ => SyntaxFactory.ParenthesizedExpression(condition),
        };
        return SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, operand);
    }
}
