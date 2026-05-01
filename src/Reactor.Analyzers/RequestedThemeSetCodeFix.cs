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
/// Code fix for REACTOR_THEME_003: replaces <c>.Set(fe =&gt; fe.RequestedTheme = ElementTheme.X)</c>
/// with <c>.RequestedTheme(ElementTheme.X)</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RequestedThemeSetCodeFix))]
[Shared]
public sealed class RequestedThemeSetCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(RequestedThemeSetAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var span = diagnostic.Location.SourceSpan;
            var node = root.FindNode(span);
            if (node is not InvocationExpressionSyntax invocation) continue;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;

            // Extract the value from the assignment
            var args = invocation.ArgumentList.Arguments;
            if (args.Count != 1) continue;

            var lambda = args[0].Expression;
            ExpressionSyntax? body = lambda switch
            {
                SimpleLambdaExpressionSyntax simple => simple.ExpressionBody,
                ParenthesizedLambdaExpressionSyntax paren => paren.ExpressionBody,
                _ => null,
            };

            if (body is not AssignmentExpressionSyntax assignment) continue;

            var value = assignment.Right;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Use .RequestedTheme() modifier",
                    ct =>
                    {
                        // Build: .RequestedTheme(value)
                        var newInvocation = SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                memberAccess.Expression,
                                SyntaxFactory.IdentifierName("RequestedTheme")),
                            SyntaxFactory.ArgumentList(
                                SyntaxFactory.SingletonSeparatedList(
                                    SyntaxFactory.Argument(value))))
                            .WithTriviaFrom(invocation);

                        var newRoot = root.ReplaceNode(invocation, newInvocation);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: RequestedThemeSetAnalyzer.DiagnosticId),
                diagnostic);
        }
    }
}
