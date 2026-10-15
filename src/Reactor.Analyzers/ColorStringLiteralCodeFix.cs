using System.Collections.Immutable;
using System.Linq;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Simplification;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for <see cref="ColorStringLiteralAnalyzer"/> (REACTOR_DYM_006). Rewrites a string literal
/// to the suggested color (<c>"#888"</c> → <c>"#888888"</c>, <c>"FF0000"</c> → <c>"#FF0000"</c>,
/// <c>"orange"</c> → <c>"#FFA500"</c>), or — for a font name stored under a <c>*FontFamily</c>
/// resource key — rewrites the argument to <c>new FontFamily("…")</c> so it binds to the
/// <c>ResourceBuilder.Set(string, FontFamily)</c> overload. The replacement travels in the
/// diagnostic's property bag; no fix is offered when the analyzer attached none. The literal fixes
/// need a plain string literal (a flagged <c>const</c> reference is fixed at its declaration instead).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ColorStringLiteralCodeFix))]
[Shared]
public sealed class ColorStringLiteralCodeFix : CodeFixProvider
{
    private const string LiteralEquivalenceKey = "Reactor_ColorStringLiteral";
    private const string FontFamilyEquivalenceKey = "Reactor_ColorStringFontFamily";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ColorStringLiteralAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        // Only diagnostics the analyzer attached a rewrite to are fixable.
        foreach (var diagnostic in context.Diagnostics.Where(d =>
            d.Properties.TryGetValue(ColorStringLiteralAnalyzer.ReplacementProperty, out var r) && r is not null
            && d.Properties.ContainsKey(ColorStringLiteralAnalyzer.IssueProperty)))
        {
            var replacement = diagnostic.Properties[ColorStringLiteralAnalyzer.ReplacementProperty]!;
            var issue = diagnostic.Properties[ColorStringLiteralAnalyzer.IssueProperty];

            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (node is ArgumentSyntax argument)
                node = argument.Expression;
            if (node is not ExpressionSyntax expression)
                continue;

            if (issue == nameof(ColorStringIssue.FontFamilyKey))
            {
                // Only when the flagged value *is* the argument — inside a ?: branch the other branch
                // would still be a string, and the expression would no longer type-check. A const
                // reference is fine to wrap as-is.
                if (expression.Parent is not ArgumentSyntax)
                    continue;

                var creation = SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.ParseTypeName("global::" + ColorStringLiteralAnalyzer.FontFamilyMetadataName)
                            .WithAdditionalAnnotations(Simplifier.Annotation))
                    .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(expression.WithoutTrivia()))))
                    .WithTriviaFrom(expression);

                // `color: "Consolas"` must become `<fontFamilyParam>: new FontFamily(...)`, or the call
                // would no longer bind to the FontFamily overload.
                var oldArgument = (ArgumentSyntax)expression.Parent!;
                var newArgument = oldArgument.WithExpression(creation);
                if (oldArgument.NameColon is { } nameColon)
                {
                    if (!diagnostic.Properties.TryGetValue(ColorStringLiteralAnalyzer.FontFamilyParameterProperty, out var parameterName)
                        || string.IsNullOrEmpty(parameterName))
                        continue;
                    newArgument = newArgument.WithNameColon(
                        nameColon.WithName(SyntaxFactory.IdentifierName(parameterName!).WithTriviaFrom(nameColon.Name)));
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Use {ColorStringClassifier.FontFamilySuggestion(replacement, null)}",
                        _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(oldArgument, newArgument))),
                        equivalenceKey: FontFamilyEquivalenceKey),
                    diagnostic);
                continue;
            }

            if (expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                continue;

            var newLiteral = SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(replacement!))
                .WithTriviaFrom(literal);

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Change to \"{replacement}\"",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(literal, newLiteral))),
                    equivalenceKey: LiteralEquivalenceKey),
                diagnostic);
        }
    }
}
