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
/// Code fix for <see cref="FuzzyFactoryNameAnalyzer"/> (REACTOR_DYM_003): renames the mistyped
/// identifier to the suggested Reactor factory (e.g. <c>Buton("x")</c> becomes <c>Button("x")</c>).
/// The suggestion is read from the diagnostic's property bag so the fix never re-computes similarity.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FuzzyFactoryNameCodeFix))]
[Shared]
public sealed class FuzzyFactoryNameCodeFix : CodeFixProvider
{
    private const string EquivalenceKey = "Reactor_FuzzyFactoryRename";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(FuzzyFactoryNameAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(FuzzyFactoryNameAnalyzer.SuggestionProperty, out var suggestion)
                || string.IsNullOrEmpty(suggestion))
                continue;

            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var ident = node as IdentifierNameSyntax ?? node.FirstAncestorOrSelf<IdentifierNameSyntax>();
            if (ident is null)
                continue;

            // Preserve the original identifier's trivia (leading/trailing whitespace, comments).
            var replacement = SyntaxFactory.IdentifierName(suggestion!).WithTriviaFrom(ident);

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Change to '{suggestion}'",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(ident, replacement))),
                    equivalenceKey: EquivalenceKey),
                diagnostic);
        }
    }
}
