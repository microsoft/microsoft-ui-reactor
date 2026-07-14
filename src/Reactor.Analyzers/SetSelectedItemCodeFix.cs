using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for REACTOR_CTRL_001: deletes the redundant
/// <c>.Set(x =&gt; x.SelectedItem = ...)</c> call from the fluent chain, leaving the
/// controlled <c>SelectedIndex</c> as the sole selection authority.
/// </summary>
/// <remarks>
/// This deletes a call — it never inserts a type reference, so there is nothing to
/// qualify. It deliberately does not convert <c>SelectedItem</c> → <c>SelectedIndex</c>:
/// that is not a mechanical rewrite (spec 060 §4.2).
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SetSelectedItemCodeFix))]
[Shared]
public sealed class SetSelectedItemCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(SetSelectedItemAnalyzer.DiagnosticId);

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

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove redundant selection .Set(...) call",
                    ct =>
                    {
                        // Drop the '.Set(...)' invocation, keeping its receiver so any
                        // trailing modifiers in the chain stay attached.
                        var replacement = memberAccess.Expression.WithTriviaFrom(invocation);
                        var newRoot = root.ReplaceNode(invocation, replacement);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: SetSelectedItemAnalyzer.DiagnosticId),
                diagnostic);
        }
    }
}
