using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for REACTOR_THEME_001 / REACTOR_THEME_004: replaces a hard-coded color string or an
/// inline <c>new SolidColorBrush(Colors.X)</c> with the matching <c>Theme.X</c> token, but only
/// when the color has a known mapping <em>and</em> the token actually resolves on the real
/// <c>Theme</c> — otherwise the diagnostic stands with no auto-fix.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseThemeRefCodeFix))]
[Shared]
public sealed class UseThemeRefCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(UseThemeRefAnalyzer.DiagnosticId, UseThemeRefAnalyzer.BrushDiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        SemanticModel? semanticModel = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            // Resolve which node to replace and which token to replace it with, depending on the rule.
            string? token = null;
            SyntaxNode? target = null;
            ObjectCreationExpressionSyntax? brushCreation = null;

            if (node.FirstAncestorOrSelf<LiteralExpressionSyntax>() is { } literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                // REACTOR_THEME_001 — hard-coded color string.
                if (UseThemeRefAnalyzer.ColorToThemeToken.TryGetValue(literal.Token.ValueText, out var t))
                {
                    token = t;
                    target = literal;
                }
            }
            else if (node.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>() is { } creation)
            {
                // REACTOR_THEME_004 — inline new SolidColorBrush(Colors.X).
                var colorName = UseThemeRefAnalyzer.TryGetColorName(creation);
                if (colorName is not null &&
                    UseThemeRefAnalyzer.ColorToThemeToken.TryGetValue(colorName, out var t))
                {
                    token = t;
                    target = creation;
                    brushCreation = creation;
                }
            }

            if (token is null || target is null)
                continue; // Unmapped color — no key to invent, so the diagnostic stands without a fix.

            // Only offer the fix when the mapped token is sensible for the target modifier: a surface
            // token as a .Foreground (or a text token as a .Background) would flip colors the wrong
            // way across themes, so we withhold rather than auto-apply a misleading rewrite.
            var modifier = (target.FirstAncestorOrSelf<InvocationExpressionSyntax>()?.Expression
                as MemberAccessExpressionSyntax)?.Name.Identifier.Text;
            if (modifier is null || !UseThemeRefAnalyzer.TokenFitsModifier(token, modifier))
                continue;

            semanticModel ??= await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

            // The analyzer's brush match is syntactic; before rewriting, semantically confirm both the
            // brush type (WinUI's SolidColorBrush) and the color source (Microsoft.UI/Windows.UI
            // Colors) so we never rewrite an unrelated same-named type or a non-WinUI palette color.
            if (brushCreation is not null &&
                (!IsWinUiSolidColorBrush(semanticModel, brushCreation, context.CancellationToken)
                 || !IsWinUiColorsArgument(semanticModel, brushCreation, context.CancellationToken)))
                continue;

            var themeAccess = TryBuildThemeReference(semanticModel, target.SpanStart, token, target);
            if (themeAccess is null)
                continue; // Theme.<token> can't be resolved here — never emit non-compiling code.

            var nodeToReplace = target;
            var replacement = themeAccess;

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Replace with Theme.{token}",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(
                        root.ReplaceNode(nodeToReplace, replacement))),
                    equivalenceKey: $"{diagnostic.Id}_{token}"),
                diagnostic);
        }
    }

    /// <summary>
    /// Confirms an inline <c>new SolidColorBrush(...)</c> resolves to WinUI's
    /// <c>Microsoft.UI.Xaml.Media.SolidColorBrush</c> (not an unrelated same-named type), so the
    /// syntactic REACTOR_THEME_004 match is semantically sound before we rewrite it.
    /// </summary>
    private static bool IsWinUiSolidColorBrush(
        SemanticModel? semanticModel, ObjectCreationExpressionSyntax creation, CancellationToken cancellationToken)
    {
        if (semanticModel is null)
            return false;

        var solidColorBrush = semanticModel.Compilation
            .GetTypeByMetadataName("Microsoft.UI.Xaml.Media.SolidColorBrush");
        if (solidColorBrush is null)
            return false;

        var actual = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        return SymbolEqualityComparer.Default.Equals(actual, solidColorBrush);
    }

    /// <summary>
    /// Confirms the brush's constructor argument reads from WinUI's <c>Microsoft.UI.Colors</c> (or
    /// <c>Windows.UI.Colors</c>). <c>TryGetColorName</c> accepts a bare <c>Colors.X</c> syntactically,
    /// so a look-alike <c>Colors</c> type in scope (e.g. via <c>using MyCompany.UI;</c>) could
    /// otherwise be rewritten to a theme token even though the color came from a non-WinUI palette.
    /// </summary>
    private static bool IsWinUiColorsArgument(
        SemanticModel? semanticModel, ObjectCreationExpressionSyntax creation, CancellationToken cancellationToken)
    {
        if (semanticModel is null)
            return false;

        var args = creation.ArgumentList?.Arguments;
        if (args is not { Count: >= 1 } || args.Value[0].Expression is not MemberAccessExpressionSyntax colorAccess)
            return false;

        var colorsType = semanticModel.GetSymbolInfo(colorAccess.Expression, cancellationToken).Symbol as INamedTypeSymbol;
        if (colorsType is null)
            return false;

        var microsoftColors = semanticModel.Compilation.GetTypeByMetadataName("Microsoft.UI.Colors");
        var windowsColors = semanticModel.Compilation.GetTypeByMetadataName("Windows.UI.Colors");
        return SymbolEqualityComparer.Default.Equals(colorsType, microsoftColors)
            || SymbolEqualityComparer.Default.Equals(colorsType, windowsColors);
    }

    /// <summary>
    /// Builds a <c>Theme.&lt;token&gt;</c> expression guaranteed to compile at
    /// <paramref name="position"/>: it confirms the member exists on the real
    /// <c>Microsoft.UI.Reactor.Core.Theme</c> and renders the shortest unambiguous type name via
    /// <see cref="SymbolDisplayExtensions.ToMinimalDisplayString"/>. Returns null when the token
    /// can't be resolved — including when no semantic model is available — so the caller withholds
    /// the fix rather than emit a reference that might not compile.
    /// </summary>
    private static ExpressionSyntax? TryBuildThemeReference(
        SemanticModel? semanticModel, int position, string token, SyntaxNode triviaSource)
    {
        // Without a semantic model we can't confirm Theme.<token> resolves, so withhold rather than
        // emit an unvalidated reference (the diagnostic still stands with no auto-fix).
        if (semanticModel is null)
            return null;

        var themeType = semanticModel.Compilation.GetTypeByMetadataName("Microsoft.UI.Reactor.Core.Theme");
        if (themeType is null)
            return null;
        // The fix emits a static `Theme.<token>` access, so require a matching *static* field/property
        // — an instance member of the same name wouldn't compile through the type name.
        if (!themeType.GetMembers(token).Any(static m => m.IsStatic && m is IPropertySymbol or IFieldSymbol))
            return null;

        var themeName = themeType.ToMinimalDisplayString(semanticModel, position);
        return SyntaxFactory.ParseExpression($"{themeName}.{token}").WithTriviaFrom(triviaSource);
    }
}
