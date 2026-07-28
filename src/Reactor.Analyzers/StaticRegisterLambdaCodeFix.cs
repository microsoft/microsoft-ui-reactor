using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for <c>REACTOR_DESC_001</c>: inserts the <c>static</c> modifier on a
/// <c>ControlRegistry</c> registration lambda — but <b>only</b> when the lambda captures
/// nothing. A capturing lambda cannot compile with <c>static</c> (CS8820/CS8821), so for
/// those the analyzer's diagnostic stands as a nudge with no auto-fix, leaving the author to
/// refactor the capture out by hand.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StaticRegisterLambdaCodeFix))]
[Shared]
public sealed class StaticRegisterLambdaCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(StaticRegisterLambdaAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        SemanticModel? semanticModel = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            // The diagnostic is reported at the lambda's span, but the enclosing ArgumentSyntax
            // shares that exact span, so FindNode returns the argument, not the lambda. Match the
            // lambda by span among the descendants to land on the right node reliably.
            var span = diagnostic.Location.SourceSpan;
            var node = root.FindNode(span);
            var lambda = node.DescendantNodesAndSelf()
                .OfType<AnonymousFunctionExpressionSyntax>()
                .FirstOrDefault(l => l.Span == span);
            if (lambda is null) continue;
            if (lambda.Modifiers.Any(SyntaxKind.StaticKeyword)) continue;

            semanticModel ??= await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null) return;

            // A static lambda may not capture any enclosing local, parameter, or `this`.
            // If it does, offer no fix — the diagnostic remains as an author nudge.
            if (!CapturesNothing(semanticModel, lambda))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Make lambda static",
                    ct => Task.FromResult(
                        context.Document.WithSyntaxRoot(
                            root.ReplaceNode(lambda, WithStaticModifier(lambda)))),
                    equivalenceKey: StaticRegisterLambdaAnalyzer.DiagnosticId),
                diagnostic);
        }
    }

    private static bool CapturesNothing(
        SemanticModel semanticModel, AnonymousFunctionExpressionSyntax lambda)
    {
        var dataFlow = semanticModel.AnalyzeDataFlow(lambda);
        if (dataFlow is not { Succeeded: true })
            return false;

        // CapturedInside reports every variable closed over anywhere inside the region — which
        // includes locals declared *inside* this factory that its own nested lambdas capture.
        // Those don't stop THIS lambda from being static; only captures of enclosing state
        // (the implicit `this`, or a local/parameter declared in an outer scope) do.
        foreach (var captured in dataFlow.CapturedInside)
        {
            if (IsEnclosingCapture(captured, lambda))
                return false;
        }

        return true;
    }

    private static bool IsEnclosingCapture(ISymbol symbol, AnonymousFunctionExpressionSyntax lambda)
    {
        // The implicit `this` parameter is always an enclosing capture — a static lambda may not
        // reference `this`/`base`.
        if (symbol is IParameterSymbol { IsThis: true })
            return true;

        // A symbol declared inside the lambda (its own parameter/local, or a local of a nested
        // lambda) is not an enclosing capture. Anything declared in an outer scope is.
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            if (lambda.Span.Contains(reference.Span))
                return false;
        }

        return true;
    }

    private static AnonymousFunctionExpressionSyntax WithStaticModifier(AnonymousFunctionExpressionSyntax lambda)
    {
        var staticKeyword = SyntaxFactory.Token(SyntaxKind.StaticKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        // Move the lambda's leading trivia onto the new `static` keyword so indentation and
        // preceding newlines are preserved (e.g. a factory on its own line after the call's
        // open paren stays put).
        return lambda
            .WithoutLeadingTrivia()
            .WithModifiers(lambda.Modifiers.Insert(0, staticKeyword))
            .WithLeadingTrivia(lambda.GetLeadingTrivia());
    }
}
