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
/// Code fix for REACTOR_OPT_001. Offers two explicit alternatives to the implicit
/// sentinel force-assert on an <c>Optional&lt;T&gt;</c> selection member:
/// <list type="bullet">
///   <item><c>Optional&lt;T&gt;.Unset</c> — let the control own the selection.</item>
///   <item><c>Optional&lt;T&gt;.Of(sentinel)</c> — keep the explicit force-assert
///   (silences the nudge without changing runtime behaviour).</item>
/// </list>
/// </summary>
/// <remarks>
/// The <c>Optional&lt;T&gt;</c> type is emitted as its minimal name at the fix site
/// (<see cref="ISymbol.ToMinimalDisplayString(SemanticModel, int, SymbolDisplayFormat)"/>),
/// so the rewrite shortens to <c>Optional&lt;T&gt;</c> where the namespace is imported
/// yet stays namespace-qualified (and compiling) where it is not. The analyzer also
/// hands off the fully-qualified type in <see cref="Diagnostic.Properties"/> as a
/// fallback when the member symbol can no longer be re-resolved from the fix document.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OptionalSentinelCodeFix))]
[Shared]
public sealed class OptionalSentinelCodeFix : CodeFixProvider
{
    private const string UnsetEquivalenceKey = OptionalSentinelAnalyzer.DiagnosticId + ":Unset";
    private const string OfEquivalenceKey = OptionalSentinelAnalyzer.DiagnosticId + ":Of";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OptionalSentinelAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var assignment = node as AssignmentExpressionSyntax
                ?? node.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
            if (assignment is null) continue;

            // Re-validate against the current document: only offer the rewrite while the
            // RHS is still the -1/null sentinel the analyzer flagged. Guards the rare case
            // where the source changed between diagnostic report and fix application.
            if (!OptionalSentinelAnalyzer.IsSentinelLiteral(assignment.Right)) continue;

            var optionalType = ResolveOptionalTypeName(semanticModel, assignment, diagnostic);
            if (optionalType is null) continue;

            // Action 1: let the control own the selection — Optional<T>.Unset.
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Let the control own selection (Optional<T>.Unset)",
                    _ => ReplaceRightAsync(
                        context.Document,
                        root,
                        assignment,
                        SyntaxFactory.ParseExpression($"{optionalType}.Unset")),
                    equivalenceKey: UnsetEquivalenceKey),
                diagnostic);

            // Action 2: keep the explicit force-assert — Optional<T>.Of(sentinel).
            var sentinel = assignment.Right.ToString().Trim();
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Keep the explicit force-assert (Optional<T>.Of(...))",
                    _ => ReplaceRightAsync(
                        context.Document,
                        root,
                        assignment,
                        SyntaxFactory.ParseExpression($"{optionalType}.Of({sentinel})")),
                    equivalenceKey: OfEquivalenceKey),
                diagnostic);
        }
    }

    /// <summary>
    /// The <c>Optional&lt;T&gt;</c> type name to emit. Prefers the minimal name that
    /// binds at the assignment site (imported → <c>Optional&lt;T&gt;</c>, otherwise
    /// namespace-qualified); falls back to the analyzer's <c>global::</c>-qualified
    /// handoff when the member symbol can't be re-resolved.
    /// </summary>
    private static string? ResolveOptionalTypeName(
        SemanticModel? semanticModel,
        AssignmentExpressionSyntax assignment,
        Diagnostic diagnostic)
    {
        if (semanticModel is not null
            && semanticModel.GetTypeInfo(assignment.Left).Type is INamedTypeSymbol named
            && OptionalSentinelAnalyzer.IsReactorOptional(named))
        {
            return named.ToMinimalDisplayString(semanticModel, assignment.Right.SpanStart);
        }

        return diagnostic.Properties.TryGetValue(OptionalSentinelAnalyzer.OptionalTypeProperty, out var fqn)
            && !string.IsNullOrEmpty(fqn)
                ? fqn
                : null;
    }

    private static Task<Document> ReplaceRightAsync(
        Document document,
        SyntaxNode root,
        AssignmentExpressionSyntax assignment,
        ExpressionSyntax replacement)
    {
        var newRight = replacement.WithTriviaFrom(assignment.Right);
        var newRoot = root.ReplaceNode(assignment.Right, newRight);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
