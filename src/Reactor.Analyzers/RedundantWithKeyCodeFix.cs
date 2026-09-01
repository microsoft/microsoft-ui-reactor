using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for <c>REACTOR_DSL_004</c> — deletes a <c>.WithKey(...)</c> call
/// that restates the key <c>ForEach</c> already assigns from an
/// <see cref="System.Collections.Generic.IEnumerable{T}"/> of
/// <c>IReactorKeyed</c> items.
///
/// <para>Safe to automate, unlike the <c>REACTOR_DSL_002</c> case: the analyzer
/// only reports the two spellings that are provably the same value the factory
/// supplies, so removing the call cannot change the key. A batch fixer is
/// therefore fine here — each occurrence is independent and needs no per-site
/// judgement.</para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RedundantWithKeyCodeFix))]
[Shared]
public sealed class RedundantWithKeyCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingWithKeyAnalyzer.RedundantKeyId);

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            // The diagnostic is reported on the whole `….WithKey(arg)`
            // invocation; getInnermostNodeForTie mirrors MissingWithKeyCodeFix,
            // where an argument and its invocation can share a span.
            var reported = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (reported is not InvocationExpressionSyntax withKeyInv) continue;

            // `receiver.WithKey(arg)` — dropping the call means replacing the
            // whole invocation with its receiver, keeping the trivia so a
            // multi-line fluent chain doesn't collapse.
            if (withKeyInv.Expression is not MemberAccessExpressionSyntax member) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove redundant .WithKey(...)",
                    ct =>
                    {
                        var newRoot = root.ReplaceNode(
                            withKeyInv,
                            member.Expression.WithTriviaFrom(withKeyInv));
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: $"{MissingWithKeyAnalyzer.RedundantKeyId}_Remove"),
                diagnostic);
        }
    }
}
