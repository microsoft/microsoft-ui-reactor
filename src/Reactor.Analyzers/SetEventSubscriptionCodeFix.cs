using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SetEventSubscriptionCodeFix))]
[Shared]
public sealed class SetEventSubscriptionCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(SetEventSubscriptionAnalyzer.DiagnosticId);

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
            if (!SetEventSubscriptionAnalyzer.TryGetSetInvocationData(invocation, out var setTarget, out _, out var assignment))
                continue;
            if (assignment.Kind() != SyntaxKind.AddAssignmentExpression)
                continue;
            if (assignment.Left is not MemberAccessExpressionSyntax leftAccess)
                continue;

            var eventName = leftAccess.Name.Identifier.Text;
            if (!SetEventSubscriptionAnalyzer.CodeFixableEventModifiers.TryGetValue(eventName, out var modifierName))
                continue;

            var newInvocation = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        setTarget,
                        SyntaxFactory.IdentifierName(modifierName)),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(assignment.Right))))
                .WithTriviaFrom(invocation);

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Use .{modifierName}() modifier",
                    ct => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation))),
                    equivalenceKey: SetEventSubscriptionAnalyzer.DiagnosticId + ":" + modifierName),
                diagnostic);
        }
    }
}