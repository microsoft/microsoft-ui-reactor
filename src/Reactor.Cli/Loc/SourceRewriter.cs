using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// Rewrites source files to replace bare string literals with t.Message(Loc.X.Y) calls.
/// Inserts `var t = UseIntl();` declaration when not already present.
/// </summary>
internal static class SourceRewriter
{
    /// <summary>
    /// Rewrites all source files that have extractions. Returns the number of locations rewritten.
    /// </summary>
    public static int Rewrite(List<KeyedLocString> keyed)
    {
        if (keyed.Count == 0) return 0;

        // Group by file
        var byFile = keyed.GroupBy(k => k.Source.FilePath);
        int totalRewritten = 0;

        foreach (var fileGroup in byFile)
        {
            var filePath = fileGroup.Key;
            var source = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
            var root = tree.GetRoot();

            // Build replacements sorted by position (descending to avoid offset shifts)
            var replacements = fileGroup
                .OrderByDescending(k => k.Source.SpanStart)
                .ToList();

            var sb = new StringBuilder(source);
            var classesNeedingUseIntl = new HashSet<string>();

            foreach (var entry in replacements)
            {
                var replacement = BuildReplacement(entry);
                sb.Remove(entry.Source.SpanStart, entry.Source.SpanLength);
                sb.Insert(entry.Source.SpanStart, replacement);
                totalRewritten++;

                // Track which classes need UseIntl() declaration
                classesNeedingUseIntl.Add(entry.Source.ClassName);
            }

            // Insert UseIntl() declarations where needed
            var updatedSource = sb.ToString();
            updatedSource = InsertUseIntlDeclarations(updatedSource, classesNeedingUseIntl);

            File.WriteAllText(filePath, updatedSource);
        }

        return totalRewritten;
    }

    private static string BuildReplacement(KeyedLocString entry)
    {
        var locPath = $"Loc.{entry.ReswFileName}.{entry.Key}";

        if (entry.Source.IsInterpolation)
        {
            // Collect all ICU parameter names from the pattern (e.g., {count}, {name})
            // using the brace-depth-aware parser from ReswReader. The ArgumentMap
            // only contains entries where the C# expression differs from the ICU
            // param name (e.g., user.Name -> name). Simple variable references like
            // {count} won't be in the map because the name matches.
            var icuParams = ReswReader.ExtractIcuParameters(entry.Source.Value).ToList();
            var argMap = entry.Source.ArgumentMap ?? new Dictionary<string, string>();

            var argParts = new List<string>();
            foreach (var param in icuParams)
            {
                // The ICU param name becomes a string literal in the emitted
                // source, so it has to be C#-escaped. Most ICU params are
                // identifiers, but the spec doesn't forbid `"` or `\` —
                // FormatLiteral keeps the emission valid in those cases.
                var keyLit = SymbolDisplay.FormatLiteral(param, quote: true);
                if (argMap.TryGetValue(param, out var exprText) && param != exprText)
                    argParts.Add($"({keyLit}, {exprText})");
                else
                    argParts.Add($"({keyLit}, {param})");
            }

            if (argParts.Count > 0)
            {
                var args = string.Join(", ", argParts);
                return $"t.Message({locPath}, {args})";
            }
        }

        // Simple string or interpolation with no parameters: t.Message(Loc.X.Y)
        return $"t.Message({locPath})";
    }

    /// <summary>
    /// Inserts `var t = UseIntl();` at the start of Render() methods in classes that need it.
    /// Only inserts if not already present.
    /// </summary>
    private static string InsertUseIntlDeclarations(string source, HashSet<string> classNames)
    {
        if (classNames.Count == 0) return source;

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        // Find Render() methods in the target classes
        var insertions = new List<(int position, string text)>();

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (!classNames.Contains(classDecl.Identifier.Text)) continue;

            foreach (var method in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                if (method.Identifier.Text != "Render") continue;
                if (method.Body == null && method.ExpressionBody == null) continue;

                // Check if UseIntl() is already called
                var hasUseIntl = method.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv =>
                    {
                        var name = inv.Expression switch
                        {
                            IdentifierNameSyntax id => id.Identifier.Text,
                            _ => null,
                        };
                        return name == "UseIntl";
                    });

                if (hasUseIntl) continue;

                // Insert at the start of the method body
                if (method.Body != null)
                {
                    var openBrace = method.Body.OpenBraceToken;
                    var insertPos = openBrace.Span.End;

                    // Detect indentation from the first statement or the method
                    var indent = DetectIndentation(method.Body);
                    insertions.Add((insertPos, $"\n{indent}var t = UseIntl();\n"));
                }
                else if (method.ExpressionBody != null)
                {
                    // Expression-bodied method — more complex rewrite needed
                    // For now, skip and let the user refactor manually
                }
            }
        }

        if (insertions.Count == 0) return source;

        // Apply insertions from end to start
        var sb = new StringBuilder(source);
        foreach (var (position, text) in insertions.OrderByDescending(i => i.position))
        {
            sb.Insert(position, text);
        }

        return sb.ToString();
    }

    private static string DetectIndentation(BlockSyntax block)
    {
        // Use indentation of first statement, or fall back to 8 spaces
        var firstStatement = block.Statements.FirstOrDefault();
        if (firstStatement != null)
        {
            var leadingTrivia = firstStatement.GetLeadingTrivia();
            foreach (var trivia in leadingTrivia)
            {
                if (trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                    return trivia.ToString();
            }
        }

        return "        "; // 8 spaces default
    }
}
