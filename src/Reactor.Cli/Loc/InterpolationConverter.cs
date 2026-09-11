using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// Converts C# interpolated string expressions to ICU Message Format strings.
/// Example: $"Hello, {user.Name}" → "Hello, {name}"
/// Example: $"Total: {price:C}" → "Total: {price, number, currency}"
/// </summary>
internal static class InterpolationConverter
{
    // C# format specifiers → ICU formatter types
    private static readonly Dictionary<string, string> FormatSpecifierMap = new(StringComparer.Ordinal)
    {
        ["C"] = "number, currency",
        ["C0"] = "number, currency",
        ["C2"] = "number, currency",
        ["N"] = "number",
        ["N0"] = "number",
        ["N2"] = "number",
        ["P"] = "number, percent",
        ["P0"] = "number, percent",
        ["P1"] = "number, percent",
        ["P2"] = "number, percent",
        ["F"] = "number",
        ["F0"] = "number",
        ["F1"] = "number",
        ["F2"] = "number",
        ["d"] = "date, short",
        ["D"] = "date, long",
        ["f"] = "date, full",
    };

    // Variable names that identify standalone plural candidates.
    private static readonly HashSet<string> QuantityHintNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "count", "total", "length", "size", "amount", "quantity",
    };

    public static (string? icuMessage, Dictionary<string, string>? argumentMap, List<string> warnings)
        Convert(InterpolatedStringExpressionSyntax interpolated)
    {
        var icuParts = new List<string>();
        var argumentMap = new Dictionary<string, string>();
        var warnings = new List<string>();
        var usedNames = new HashSet<string>();
        var emittedHoleNames = new Dictionary<int, string>();

        for (var contentIndex = 0; contentIndex < interpolated.Contents.Count; contentIndex++)
        {
            var content = interpolated.Contents[contentIndex];
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    // Escape ICU-special chars in literal text
                    icuParts.Add(EscapeForIcu(text.TextToken.ValueText));
                    break;

                case InterpolationSyntax hole:
                    var expr = hole.Expression;

                    // Unwrap parentheses: {(x ? "a" : "b")} → {x ? "a" : "b"}
                    while (expr is ParenthesizedExpressionSyntax parens)
                        expr = parens.Expression;

                    // Ternary with string-literal branches → ICU select
                    if (TryConvertPluralSuffix(interpolated, contentIndex, expr, icuParts, emittedHoleNames, warnings, out var suffixPluralIcu, out var suppressStandalonePlural))
                    {
                        icuParts.Add(suffixPluralIcu!);
                        break;
                    }

                    if (!suppressStandalonePlural && TryConvertTernaryPlural(expr, usedNames, argumentMap, out var pluralIcu))
                    {
                        icuParts.Add(pluralIcu!);
                        break;
                    }

                    if (TryConvertTernarySelect(expr, usedNames, argumentMap, out var selectIcu))
                    {
                        icuParts.Add(selectIcu!);
                        break;
                    }

                    var (paramName, exprText, isComplex) = AnalyzeExpression(expr);

                    if (isComplex)
                    {
                        warnings.Add($"Complex expression '{expr}' cannot be extracted; consider extracting to a variable first");
                        // Still include it as a placeholder with a generated name
                        paramName ??= $"arg{usedNames.Count}";
                    }

                    // Ensure unique param names
                    var uniqueName = AddParameter(paramName, exprText, usedNames, argumentMap);

                    // Convert format specifier to ICU formatter
                    var formatClause = hole.FormatClause;
                    if (formatClause != null)
                    {
                        var specifier = formatClause.FormatStringToken.ValueText;
                        // Strip precision digits for lookup (e.g., "C2" → "C")
                        var baseSpec = new string(specifier.TakeWhile(c => !char.IsDigit(c)).ToArray());
                        if (baseSpec.Length == 0) baseSpec = specifier;

                        if (FormatSpecifierMap.TryGetValue(baseSpec, out var icuFormat))
                        {
                            icuParts.Add($"{{{uniqueName}, {icuFormat}}}");
                        }
                        else
                        {
                            // Unknown format specifier — just use plain interpolation
                            warnings.Add($"Unknown format specifier ':{specifier}' on '{exprText}'");
                            icuParts.Add($"{{{uniqueName}}}");
                        }
                    }
                    else
                    {
                        icuParts.Add($"{{{uniqueName}}}");
                    }
                    emittedHoleNames[contentIndex] = uniqueName;
                    break;
            }
        }

        if (icuParts.Count == 0) return (null, null, warnings);

        var icuMessage = string.Join("", icuParts);
        return (icuMessage, argumentMap.Count > 0 ? argumentMap : null, warnings);
    }

    /// <summary>
    /// Checks if a parameter name suggests a quantity.
    /// </summary>
    public static bool IsQuantityName(string name)
    {
        if (QuantityHintNames.Contains(name)) return true;
        // Check prefix patterns like "numItems", "totalCount"
        return name.StartsWith("num", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("total", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("count", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attempts to convert a ternary with string-literal branches into an ICU select.
    /// Example: {(darkMode ? "Yes" : "No")} → {darkMode, select, true {Yes} false {No}}
    /// </summary>
    private static bool TryConvertTernarySelect(
        ExpressionSyntax expr,
        HashSet<string> usedNames,
        Dictionary<string, string> argumentMap,
        out string? icuSelect)
    {
        icuSelect = null;
        if (!(expr is ConditionalExpressionSyntax ternary))
            return false;

        // Both branches must be string literals
        if (!(ternary.WhenTrue is LiteralExpressionSyntax whenTrueLit)
            || !whenTrueLit.IsKind(SyntaxKind.StringLiteralExpression))
            return false;
        if (!(ternary.WhenFalse is LiteralExpressionSyntax whenFalseLit)
            || !whenFalseLit.IsKind(SyntaxKind.StringLiteralExpression))
            return false;

        // Resolve a parameter name from the condition
        var (condName, condExpr, _) = AnalyzeExpression(ternary.Condition);
        var uniqueName = AddParameter(condName, condExpr, usedNames, argumentMap);
        // ICU select keys are strings. Normalize the C# boolean condition rather
        // than passing a bool to the MessageFormat runtime.
        argumentMap[uniqueName] = $"({condExpr}) ? \"true\" : \"false\"";

        var trueText = EscapeForIcu(whenTrueLit.Token.ValueText);
        var falseText = EscapeForIcu(whenFalseLit.Token.ValueText);

        icuSelect = $"{{{uniqueName}, select, true {{{trueText}}} false {{{falseText}}}}}";
        return true;
    }

    private static bool TryConvertPluralSuffix(
        InterpolatedStringExpressionSyntax interpolated,
        int contentIndex,
        ExpressionSyntax expr,
        List<string> icuParts,
        IReadOnlyDictionary<int, string> emittedHoleNames,
        List<string> warnings,
        out string? icuPlural,
        out bool suppressStandalonePlural)
    {
        icuPlural = null;
        suppressStandalonePlural = false;
        if (!TryGetPluralTernary(expr, requireQuantityName: false, out var quantity, out var singularText, out var pluralText))
            return false;

        var previousTextIndex = contentIndex - 1;
        var previousQuantityIndex = contentIndex - 2;
        if (previousQuantityIndex < 0
            || interpolated.Contents[previousTextIndex] is not InterpolatedStringTextSyntax previousText
            || interpolated.Contents[previousQuantityIndex] is not InterpolationSyntax previousQuantityHole)
            return false;

        var previousExpression = previousQuantityHole.Expression;
        while (previousExpression is ParenthesizedExpressionSyntax parens)
            previousExpression = parens.Expression;

        var previousQuantityText = AnalyzeExpression(previousExpression).exprText;
        if (!string.Equals(previousQuantityText, quantity.exprText, StringComparison.Ordinal))
            return false;

        // This ternary belongs to the preceding quantity, even if folding cannot proceed.
        // Do not subsequently emit a second standalone plural for the same quantity.
        suppressStandalonePlural = true;

        var literal = previousText.TextToken.ValueText;
        var trimmedLiteral = literal.TrimEnd();
        var wordStart = trimmedLiteral.Length;
        while (wordStart > 0 && char.IsLetter(trimmedLiteral[wordStart - 1]))
            wordStart--;

        if (wordStart == trimmedLiteral.Length || icuParts.Count < 2)
            return false;

        if (!emittedHoleNames.TryGetValue(previousQuantityIndex, out var uniqueName))
            return false;

        if (previousQuantityHole.FormatClause is not null)
            warnings.Add($"Format specifier on quantity '{previousQuantityText}' is represented by the ICU plural number sign");

        icuParts.RemoveRange(icuParts.Count - 2, 2);
        var singular = $"#{EscapeForIcuPluralBody(literal)}{EscapeForIcuPluralBody(singularText)}";
        var plural = $"#{EscapeForIcuPluralBody(literal)}{EscapeForIcuPluralBody(pluralText)}";
        icuPlural = $"{{{uniqueName}, plural, one {{{singular}}} other {{{plural}}}}}";
        return true;
    }

    private static bool TryConvertTernaryPlural(
        ExpressionSyntax expr,
        HashSet<string> usedNames,
        Dictionary<string, string> argumentMap,
        out string? icuPlural)
    {
        icuPlural = null;
        if (!TryGetPluralTernary(expr, requireQuantityName: true, out var quantity, out var singularText, out var pluralText)
            || (string.IsNullOrEmpty(singularText) && string.IsNullOrEmpty(pluralText)))
            return false;

        // A standalone full-word ternary is only demonstrably plural when the
        // plural arm is the singular arm plus its English plural suffix. Other
        // conditionals (for example Enabled/Disabled) remain selects.
        if (!string.IsNullOrEmpty(singularText) && !string.IsNullOrEmpty(pluralText)
            && !string.Equals(pluralText, singularText + "s", StringComparison.Ordinal))
            return false;

        var uniqueName = AddParameter(quantity, usedNames, argumentMap);
        if (string.IsNullOrEmpty(singularText) || string.IsNullOrEmpty(pluralText))
        {
            var singularSuffix = EscapeForIcuPluralBody(singularText);
            var pluralSuffix = EscapeForIcuPluralBody(pluralText);
            icuPlural = $"{{{uniqueName}, plural, one {{{singularSuffix}}} other {{{pluralSuffix}}}}}";
            return true;
        }

        var singular = $"# {EscapeForIcuPluralBody(singularText)}";
        var plural = $"# {EscapeForIcuPluralBody(pluralText)}";
        icuPlural = $"{{{uniqueName}, plural, one {{{singular}}} other {{{plural}}}}}";
        return true;
    }

    private static bool TryGetPluralTernary(
        ExpressionSyntax expr,
        bool requireQuantityName,
        out (string name, string exprText) quantity,
        out string singularText,
        out string pluralText)
    {
        quantity = default;
        singularText = string.Empty;
        pluralText = string.Empty;

        if (expr is not ConditionalExpressionSyntax ternary
            || !TryGetPluralCondition(ternary.Condition, requireQuantityName, out quantity, out var trueIsSingular))
            return false;

        if (ternary.WhenTrue is not LiteralExpressionSyntax trueLiteral
            || !trueLiteral.IsKind(SyntaxKind.StringLiteralExpression)
            || ternary.WhenFalse is not LiteralExpressionSyntax falseLiteral
            || !falseLiteral.IsKind(SyntaxKind.StringLiteralExpression))
            return false;

        if (trueIsSingular)
        {
            singularText = trueLiteral.Token.ValueText;
            pluralText = falseLiteral.Token.ValueText;
        }
        else
        {
            singularText = falseLiteral.Token.ValueText;
            pluralText = trueLiteral.Token.ValueText;
        }

        return true;
    }

    private static bool TryGetPluralCondition(
        ExpressionSyntax condition,
        bool requireQuantityName,
        out (string name, string exprText) quantity,
        out bool trueIsSingular)
    {
        quantity = default;
        trueIsSingular = false;

        if (condition is not BinaryExpressionSyntax binary
            || (binary.Kind() != SyntaxKind.EqualsExpression && binary.Kind() != SyntaxKind.NotEqualsExpression))
            return false;

        ExpressionSyntax? quantityExpression = null;
        if (IsOneLiteral(binary.Right))
            quantityExpression = binary.Left;
        else if (IsOneLiteral(binary.Left))
            quantityExpression = binary.Right;

        if (quantityExpression is null)
            return false;

        var (name, exprText, isComplex) = AnalyzeExpression(quantityExpression);
        if (name is null || isComplex || (requireQuantityName && !IsQuantityName(name)))
            return false;

        quantity = (name, exprText);
        trueIsSingular = binary.Kind() == SyntaxKind.EqualsExpression;
        return true;
    }

    private static bool IsOneLiteral(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax literal
        && literal.IsKind(SyntaxKind.NumericLiteralExpression)
        && literal.Token.Value is int value
        && value == 1;

    private static string AddParameter(
        (string name, string exprText) parameter,
        HashSet<string> usedNames,
        Dictionary<string, string> argumentMap) =>
        AddParameter(parameter.name, parameter.exprText, usedNames, argumentMap);

    private static string AddParameter(
        string? parameterName,
        string exprText,
        HashSet<string> usedNames,
        Dictionary<string, string> argumentMap)
    {
        var baseName = parameterName ?? $"arg{usedNames.Count}";
        var uniqueName = baseName;
        var suffix = 2;
        while (!usedNames.Add(uniqueName))
            uniqueName = $"{baseName}{suffix++}";

        if (exprText != uniqueName)
            argumentMap[uniqueName] = exprText;

        return uniqueName;
    }

    private static (string? name, string exprText, bool isComplex) AnalyzeExpression(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case ParenthesizedExpressionSyntax parens:
                return AnalyzeExpression(parens.Expression);

            case IdentifierNameSyntax id:
                // Simple variable: {count} → {count}
                return (id.Identifier.Text, id.Identifier.Text, false);

            case MemberAccessExpressionSyntax member:
                // Dotted expression: {user.Name} → {name} with mapping name=user.Name
                var lastSegment = member.Name.Identifier.Text;
                var camelCase = char.ToLowerInvariant(lastSegment[0]) + lastSegment.Substring(1);
                return (camelCase, member.ToString(), false);

            case InvocationExpressionSyntax:
                // Method call: {GetTotal()} → complex, warn
                return (null, expr.ToString(), true);

            case ElementAccessExpressionSyntax:
                // Indexer: {items[0]} → complex
                return (null, expr.ToString(), true);

            case BinaryExpressionSyntax:
                // Binary op: {a + b} → complex
                return (null, expr.ToString(), true);

            case ConditionalExpressionSyntax:
                // Ternary with non-literal branches inside interpolation → complex
                return (null, expr.ToString(), true);

            default:
                return (null, expr.ToString(), true);
        }
    }

    private static string EscapeForIcu(string text)
    {
        // ICU uses single quotes for escaping and { } as syntax
        // We need to escape literal { } that aren't our placeholders
        // and single quotes
        return text.Replace("'", "''").Replace("{", "'{'").Replace("}", "'}'");
    }

    private static string EscapeForIcuPluralBody(string text) =>
        EscapeForIcu(text).Replace("#", "'#'");
}
