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
/// Code fix for <see cref="ThemeBackgroundSuffixAnalyzer"/> (REACTOR_DYM_002): renames the invented
/// <c>Theme.*Background</c> token to the canonical Reactor token, so <c>Theme.AppBackground</c>
/// becomes <c>Theme.SolidBackground</c> (and <c>Theme.LayerBackground</c> → <c>Theme.LayerFill</c>).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ThemeBackgroundSuffixCodeFix))]
[Shared]
public sealed class ThemeBackgroundSuffixCodeFix : CodeFixProvider
{
    private const string EquivalenceKey = "Reactor_ThemeBackgroundSuffix";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ThemeBackgroundSuffixAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var memberAccess = node.FirstAncestorOrSelf<MemberAccessExpressionSyntax>();
            if (memberAccess?.Name is not IdentifierNameSyntax name)
                continue;
            var target = ThemeBackgroundSuffixAnalyzer.ResolveTarget(name.Identifier.Text);
            if (target is null)
                continue;

            var renamed = memberAccess.WithName(
                SyntaxFactory.IdentifierName(target).WithTriviaFrom(name));

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Use 'Theme.{target}'",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(memberAccess, renamed))),
                    equivalenceKey: EquivalenceKey),
                diagnostic);
        }
    }
}
