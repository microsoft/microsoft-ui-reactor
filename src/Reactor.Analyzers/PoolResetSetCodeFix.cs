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
/// Code fix for REACTOR_POOL_001: rewrites <c>x.Set(fe =&gt; fe.PROP = VALUE)</c>
/// to <c>x.PROP(VALUE)</c> using the corresponding Reactor modifier.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PoolResetSetCodeFix))]
[Shared]
public sealed class PoolResetSetCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(PoolResetSetAnalyzer.DiagnosticId);

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

            var args = invocation.ArgumentList.Arguments;
            if (args.Count != 1) continue;

            ExpressionSyntax? body = args[0].Expression switch
            {
                SimpleLambdaExpressionSyntax simple => simple.ExpressionBody,
                ParenthesizedLambdaExpressionSyntax paren => paren.ExpressionBody,
                _ => null,
            };

            if (body is not AssignmentExpressionSyntax assignment) continue;
            if (assignment.Left is not MemberAccessExpressionSyntax leftAccess) continue;

            var propName = leftAccess.Name.Identifier.Text;
            if (!PoolResetSetAnalyzer.TrappedProperties.TryGetValue(propName, out var modifierName))
                continue;

            var value = assignment.Right;

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Use .{modifierName}() modifier",
                    ct =>
                    {
                        var newInvocation = SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                memberAccess.Expression,
                                SyntaxFactory.IdentifierName(modifierName)),
                            SyntaxFactory.ArgumentList(
                                SyntaxFactory.SingletonSeparatedList(
                                    SyntaxFactory.Argument(value))))
                            .WithTriviaFrom(invocation);

                        var newRoot = root.ReplaceNode(invocation, newInvocation);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: PoolResetSetAnalyzer.DiagnosticId + ":" + modifierName),
                diagnostic);
        }
    }
}
