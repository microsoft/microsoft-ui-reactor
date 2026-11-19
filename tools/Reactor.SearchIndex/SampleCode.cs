using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.SearchIndex;

static partial class SampleCode
{
    public static bool IsComplete(string code)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        return root.DescendantTokens().Any(t => !t.IsMissing && !t.IsKind(SyntaxKind.EndOfFileToken))
            && !HasPlaceholder(code, root);
    }

    static bool HasPlaceholder(string code, SyntaxNode root)
    {
        var codeOnly = code.ToCharArray();
        foreach (var trivia in root.DescendantTrivia())
        {
            if (IsOrdinaryComment(trivia))
            {
                var text = CommentText(trivia);
                if (text == "..." || text.StartsWith("omitted:", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (!trivia.IsDirective && trivia.Kind() is not (SyntaxKind.DisabledTextTrivia
                or SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                continue;
            }

            Mask(codeOnly, trivia.SpanStart, trivia.Span.Length);
        }

        // Preserve legacy callback elisions without treating every prose ellipsis as incomplete.
        foreach (var block in root.DescendantNodes().OfType<BlockSyntax>().Where(b => b.Statements.Count == 0))
        {
            if (block.OpenBraceToken.TrailingTrivia.Concat(block.CloseBraceToken.LeadingTrivia)
                .Any(t => IsOrdinaryComment(t) && CommentElision().IsMatch(CommentText(t))))
                return true;
        }

        // Include skipped tokens during parser recovery. Mask only the text of interpolation,
        // leaving the expressions inside its braces visible. A generic type argument is
        // another legitimate use of angle brackets, even if the type name starts with 'your'.
        foreach (var token in root.DescendantTokens(descendIntoTrivia: true))
        {
            if (IsTextToken(token) || (token.IsKind(SyntaxKind.LessThanToken) && token.Parent is TypeArgumentListSyntax))
                Mask(codeOnly, token.SpanStart, token.Span.Length);
        }

        var remaining = new string(codeOnly);
        if (remaining.Contains("...", StringComparison.Ordinal) || ConfigurationPlaceholder().IsMatch(remaining))
            return true;

        return root.DescendantNodes().OfType<ArgumentSyntax>().Any(HasDirectPlaceholder);
    }

    // Keep the existing map-token and URI fill-in policy at these explicit syntax positions.
    // Other argument shapes can use an omission comment without inferring symbols or types.
    static bool HasDirectPlaceholder(ArgumentSyntax argument)
    {
        if (argument.Expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression)
            || argument.Parent is not ArgumentListSyntax list)
            return false;

        var uri = list.Parent is ObjectCreationExpressionSyntax creation
            && creation.Type.ToString() is "Uri" or "System.Uri" or "global::System.Uri";
        var mapToken = list.Parent is InvocationExpressionSyntax call
            && call.Expression.ToString() is "MapControl" or "Factories.MapControl"
            && (argument.NameColon is { } named
                ? named.Name.Identifier.ValueText == "mapServiceToken"
                : list.Arguments.IndexOf(argument) == 0);
        if (!uri && !mapToken) return false;

        var value = literal.Token.ValueText;
        if (ConfigurationPlaceholder().IsMatch(value)) return true;
        var pathEnd = value.AsSpan().IndexOfAny('?', '#');
        return uri && AbbreviatedPath().IsMatch(pathEnd < 0 ? value.AsSpan() : value.AsSpan(0, pathEnd));
    }

    static bool IsTextToken(SyntaxToken token) => token.Kind() is
        SyntaxKind.StringLiteralToken or SyntaxKind.Utf8StringLiteralToken
        or SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken
        or SyntaxKind.Utf8SingleLineRawStringLiteralToken or SyntaxKind.Utf8MultiLineRawStringLiteralToken
        or SyntaxKind.InterpolatedStringTextToken or SyntaxKind.CharacterLiteralToken;

    static bool IsOrdinaryComment(SyntaxTrivia trivia) =>
        trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia;

    static string CommentText(SyntaxTrivia trivia)
    {
        var text = trivia.ToString()[2..];
        if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) && text.EndsWith("*/", StringComparison.Ordinal))
            text = text[..^2];
        return text.Trim();
    }

    static void Mask(char[] code, int start, int length) => Array.Fill(code, ' ', start, length);

    [GeneratedRegex(@"^\.\.\.(?:\s|$)|,\s*\.\.\.$")]
    private static partial Regex CommentElision();

    [GeneratedRegex(@"<your[\w .-]+>")]
    private static partial Regex ConfigurationPlaceholder();

    [GeneratedRegex(@"(?:^|[/\\])(?:\.\.\.|\([ \t]*\.\.\.[ \t]*\))(?=$|[/\\])")]
    private static partial Regex AbbreviatedPath();
}
