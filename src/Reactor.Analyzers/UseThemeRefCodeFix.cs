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
/// Code fix for REACTOR_THEME_001: replaces hard-coded color strings with <c>Theme.X</c>
/// tokens where a known mapping exists.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseThemeRefCodeFix))]
[Shared]
public sealed class UseThemeRefCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(UseThemeRefAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var span = diagnostic.Location.SourceSpan;
            var node = root.FindNode(span);

            if (node is not LiteralExpressionSyntax literal)
                continue;
            if (!literal.IsKind(SyntaxKind.StringLiteralExpression))
                continue;

            var colorValue = literal.Token.ValueText;
            if (!UseThemeRefAnalyzer.ColorToThemeToken.TryGetValue(colorValue, out var token))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Replace with Theme.{token}",
                    ct =>
                    {
                        // Build: Theme.Token
                        var themeAccess = SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("Theme"),
                            SyntaxFactory.IdentifierName(token))
                            .WithTriviaFrom(literal);

                        var newRoot = root.ReplaceNode(literal, themeAccess);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: $"{UseThemeRefAnalyzer.DiagnosticId}_{token}"),
                diagnostic);
        }
    }
}
