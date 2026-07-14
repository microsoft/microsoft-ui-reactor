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
/// Code fix for REACTOR_A11Y_004: appends <c>.IsTabStop(true)</c> to a clickable container's
/// fluent chain so it joins the keyboard tab order.
/// </summary>
/// <remarks>
/// The analyzer reports on the container factory call (<c>Border(x)</c>, <c>Grid(...)</c>, …);
/// the fix walks out to the end of the fluent chain applied to that factory and tacks
/// <c>.IsTabStop(true)</c> onto it, so <c>Border(x).OnTapped(h)</c> becomes
/// <c>Border(x).OnTapped(h).IsTabStop(true)</c>. The author still pairs it with an
/// <c>.OnKeyDown</c> handler for Enter/Space activation — that part is intent-specific and left
/// to the developer.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ClickableContainerKeyboardCodeFix))]
[Shared]
public sealed class ClickableContainerKeyboardCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ClickableContainerKeyboardAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var factory = node as InvocationExpressionSyntax
                ?? node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (factory is null) continue;

            // The appended modifier must land at the end of the whole chain, not immediately on
            // the factory, or it would fire before .OnTapped is wired and the reported squiggle
            // would round-trip wrong.
            var outermost = GetOutermostChainInvocation(factory);

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Add .IsTabStop(true) for keyboard focus",
                    ct =>
                    {
                        // Preserve the receiver's leading and internal trivia; move only the
                        // outermost node's trailing trivia to sit after the appended call so
                        // multi-line chains keep their formatting.
                        var trailing = outermost.GetTrailingTrivia();
                        var newOutermost = SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                outermost.WithoutTrailingTrivia(),
                                SyntaxFactory.IdentifierName("IsTabStop")),
                            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(
                                    SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)))))
                            .WithTrailingTrivia(trailing);

                        var newRoot = root.ReplaceNode(outermost, newOutermost);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: ClickableContainerKeyboardAnalyzer.DiagnosticId),
                diagnostic);
        }
    }

    /// <summary>
    /// Walks from the factory call out to the last invocation of the fluent chain applied to it,
    /// following only genuine method-chain links (<c>factory.M(..).M2(..)</c>).
    /// </summary>
    private static InvocationExpressionSyntax GetOutermostChainInvocation(InvocationExpressionSyntax factory)
    {
        var current = factory;
        while (current.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Expression == current
            && memberAccess.Parent is InvocationExpressionSyntax outer
            && outer.Expression == memberAccess)
        {
            current = outer;
        }
        return current;
    }
}
