using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for REACTOR_ANIM_004: rewrites <c>x.Set(fe =&gt; fe.Rotation = e)</c> to
/// <c>x.Rotation(e)</c>, and the same for <c>Scale</c>, <c>CenterPoint</c>,
/// <c>ScaleTransition</c> and <c>TranslationTransition</c>.
/// <c>fe.OpacityTransition = new ScalarTransition { Duration = d }</c> becomes
/// <c>.OpacityTransition(d)</c>, and the same for <c>RotationTransition</c>.
/// </summary>
/// <remarks>
/// Offered only when the analyzer stamped <see cref="FacadeTransformSetAnalyzer.FixModifierKey"/>:
/// the assignment is the lambda's only statement, the right-hand side maps onto the modifier's
/// parameters, and the rewritten call binds (spec 066 §9.1). The <c>Translation</c> arm never gets
/// a fix, because adding <c>.Translation(...)</c> next to the existing call would be dead code.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FacadeTransformSetCodeFix))]
[Shared]
public sealed class FacadeTransformSetCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(FacadeTransformSetAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(FacadeTransformSetAnalyzer.FixModifierKey, out var modifier)
                || string.IsNullOrEmpty(modifier))
            {
                continue;
            }

            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not AssignmentExpressionSyntax assignment)
                continue;

            var setInvocation = assignment.Ancestors().OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(candidate => SetLambdaHelpers.IsSetInvocation(candidate, out _));
            if (setInvocation is null || setInvocation.Expression is not MemberAccessExpressionSyntax setAccess)
                continue;

            if (!FacadeTransformSetAnalyzer.FacadeMembers.TryGetValue(
                    ((MemberAccessExpressionSyntax)assignment.Left).Name.Identifier.Text, out var info))
            {
                continue;
            }

            var arguments = FacadeTransformSetAnalyzer.BuildModifierArguments(assignment, info.Shape);
            if (arguments is null)
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Use .{modifier}() modifier",
                    ct =>
                    {
                        var rewritten = SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                setAccess.Expression,
                                setAccess.OperatorToken,
                                SyntaxFactory.IdentifierName(modifier!)),
                            arguments);
                        var newRoot = root.ReplaceNode(setInvocation, rewritten.WithTriviaFrom(setInvocation));
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: "ReactorAnim004:" + modifier),
                diagnostic);
        }
    }
}
