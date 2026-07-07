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
/// Code fix for <see cref="NonInvocableMemberParensAnalyzer"/> (REACTOR_DYM_001): removes the
/// stray parentheses so <c>GridSize.Auto()</c> becomes <c>GridSize.Auto</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NonInvocableMemberParensCodeFix))]
[Shared]
public sealed class NonInvocableMemberParensCodeFix : CodeFixProvider
{
    private const string EquivalenceKey = "Reactor_DropParens";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(NonInvocableMemberParensAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation?.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            // If the member name carries explicit type arguments (`GridSize.Auto<int>()`), strip them
            // too: a property/field can't be generic, so keeping them would leave `GridSize.Auto<int>`,
            // which still doesn't compile. Normalize the generic name down to a plain identifier first.
            if (memberAccess.Name is GenericNameSyntax generic)
                memberAccess = memberAccess.WithName(SyntaxFactory.IdentifierName(generic.Identifier));

            // `GridSize.Auto()` → `GridSize.Auto`, preserving the invocation's outer trivia.
            var replacement = memberAccess.WithTriviaFrom(invocation);

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove parentheses",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(invocation, replacement))),
                    equivalenceKey: EquivalenceKey),
                diagnostic);
        }
    }
}
