using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.UI.Reactor.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CombinedSizeModifierCodeFix))]
[Shared]
public sealed class CombinedSizeModifierCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(CombinedSizeModifierAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null) return;
        var text = await context.Document.GetTextAsync(context.CancellationToken).ConfigureAwait(false);

        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax invocation
                || invocation.Expression is not MemberAccessExpressionSyntax
                {
                    Expression: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax innerAccess },
                }
                || CombinedSizeModifierAnalyzer.TryCombine(invocation, model, context.CancellationToken) is not { } replacement
                || replacement.Expression is not MemberAccessExpressionSyntax replacementAccess)
                continue;

            // Change only the suffix, so BatchFixer can also apply a diagnostic
            // inside the untouched receiver without overlapping text edits. Keep
            // argument text intact for nested CallerArgumentExpression calls.
            var suffix = SyntaxFactory.InvocationExpression(
                replacementAccess.Name.WithoutTrivia(), replacement.ArgumentList)
                .WithoutTrailingTrivia().ToFullString();
            var change = new TextChange(TextSpan.FromBounds(innerAccess.Name.SpanStart, invocation.Span.End), suffix);
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Combine dimensions with .Size()",
                    _ => Task.FromResult(context.Document.WithText(text.WithChanges(change))),
                    equivalenceKey: CombinedSizeModifierAnalyzer.DiagnosticId),
                diagnostic);
        }
    }
}
