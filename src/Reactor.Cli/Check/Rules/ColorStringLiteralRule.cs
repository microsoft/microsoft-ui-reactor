// Mirror of the REACTOR_DYM_006 analyzer (ColorStringLiteralAnalyzer) — issue #1375.
//
// A compile-time-constant color string that BrushHelper can't parse compiles
// fine and silently renders gray, so unlike every other Tier-3 rule there is
// no compiler error to hang this on. The rule therefore anchors on the
// analyzer's own diagnostic ID: `dotnet build` (which `mur check` wraps) runs
// the shipped Reactor.Analyzers bundle, REACTOR_DYM_006 lands in the parsed
// output, and this rule re-derives the concrete rewrite for the `→ try:`
// suffix. It deliberately has NO Tier-1 HintFor entry in CheckCommand — that
// table wins ties and would mask the suggestion.
//
// Parity with the analyzer is locked by DidYouMeanAnalyzerParityTests (same
// shapes → same suggestion) and ColorStringParityTests (the two classifier
// copies agree with each other and with BrushHelper.TryParseColor).

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal sealed class ColorStringLiteralRule : IRulePattern
{
    internal const string DiagnosticId = "REACTOR_DYM_006";
    const string BrushHelperFqn = "Microsoft.UI.Reactor.BrushHelper";
    const string ResourceBuilderFqn = "Microsoft.UI.Reactor.Elements.ResourceBuilder";
    const string ElementExtensionsFqn = "Microsoft.UI.Reactor.ElementExtensions";
    const string FontFamilyFqn = "Microsoft.UI.Xaml.Media.FontFamily";
    const string ColorParameterName = "color";
    const string KeyParameterName = "key";

    static readonly HashSet<string> ModifierNames = new(StringComparer.Ordinal)
    {
        "Background", "Foreground", "BorderBrush", "WithBorder",
    };

    public string Name => "ColorStringLiteralRule";
    public string Provenance => "analyzer:REACTOR_DYM_006";
    public IReadOnlyList<string> DiagnosticCodes { get; } = new[] { DiagnosticId };
    public IReadOnlyList<string> DeclaredTargets { get; } = new[] { BrushHelperFqn, ResourceBuilderFqn, ElementExtensionsFqn };

    public RuleSuggestion TryMatch(in RuleContext ctx)
    {
        if (ctx.Diagnostic.Id != DiagnosticId) return RuleSuggestion.Silent;

        var brushHelper = ctx.Resolver.ResolveType(BrushHelperFqn);
        var resourceBuilder = ctx.Resolver.ResolveType(ResourceBuilderFqn);
        var extensions = ctx.Resolver.ResolveType(ElementExtensionsFqn);
        if (brushHelper is null && resourceBuilder is null && extensions is null)
            return RuleSuggestion.Silent;

        // PickRelevantNode lands on the ArgumentSyntax (a literal or ?: branch
        // walks up to it) or on the IdentifierName of a const reference.
        var argument = ctx.Node.FirstAncestorOrSelf<ArgumentSyntax>();
        if (argument?.Parent?.Parent is not InvocationExpressionSyntax invocation)
            return RuleSuggestion.Silent;

        if (ctx.SemanticModel.GetOperation(invocation) is not IInvocationOperation operation)
            return RuleSuggestion.Silent;

        var target = operation.TargetMethod;
        var method = (target.ReducedFrom ?? target).OriginalDefinition;
        var str = ctx.Compilation.GetSpecialType(SpecialType.System_String);
        if (!HasStringColorParameter(method, str))
            return RuleSuggestion.Silent;

        var owner = method.ContainingType;
        var isResourceSet = false;
        if (SymbolEqualityComparer.Default.Equals(owner, brushHelper))
        {
            if (method.Name != "Parse") return RuleSuggestion.Silent;
        }
        else if (SymbolEqualityComparer.Default.Equals(owner, resourceBuilder))
        {
            if (method.Name != "Set" || method.Parameters.Length != 2) return RuleSuggestion.Silent;
            isResourceSet = true;
        }
        else if (SymbolEqualityComparer.Default.Equals(owner, extensions))
        {
            if (!method.IsExtensionMethod || !ModifierNames.Contains(method.Name)) return RuleSuggestion.Silent;
        }
        else
        {
            return RuleSuggestion.Silent;
        }

        IArgumentOperation? colorArg = null;
        IArgumentOperation? keyArg = null;
        foreach (var arg in operation.Arguments)
        {
            if (arg.Parameter is null) continue;
            if (arg.Parameter.Name == ColorParameterName) colorArg = arg;
            else if (arg.Parameter.Name == KeyParameterName) keyArg = arg;
        }
        if (colorArg is null || colorArg.ArgumentKind != ArgumentKind.Explicit)
            return RuleSuggestion.Silent;
        // The diagnostic must sit on the color argument, not (say) the key.
        if (!colorArg.Syntax.Span.Contains(ctx.Node.Span) && !ctx.Node.Span.Contains(colorArg.Syntax.Span))
            return RuleSuggestion.Silent;

        string? resourceKey = null;
        var fontFamilyOverload = false;
        if (isResourceSet)
        {
            if (keyArg?.Value.ConstantValue is { HasValue: true, Value: string key })
                resourceKey = key;
            fontFamilyOverload = resourceBuilder!.GetMembers("Set").OfType<IMethodSymbol>()
                .Any(m => m.Parameters.Length == 2 && m.Parameters[1].Type.ToDisplayString() == FontFamilyFqn);
        }

        // Every constant value the argument can take; emit only when the
        // unparseable ones agree on a single concrete rewrite.
        string? text = null;
        ColorStringIssue issue = default;
        foreach (var value in ConstantStrings(colorArg.Value.Syntax as ExpressionSyntax, ctx.SemanticModel))
        {
            if (ColorStringClassifier.Classify(value, resourceKey, fontFamilyOverload) is not { } finding)
                continue;
            if (finding.Replacement is null)
                return RuleSuggestion.Silent;
            var candidate = SuggestionText(finding);
            if (text is not null && !string.Equals(text, candidate, StringComparison.Ordinal))
                return RuleSuggestion.Silent;
            text = candidate;
            issue = finding.Issue;
        }
        if (text is null) return RuleSuggestion.Silent;

        return new RuleSuggestion(
            Text: text,
            Confidence: 0.95,
            Evidence: issue switch
            {
                ColorStringIssue.FontFamilyKey => "*FontFamily key → FontFamily overload",
                ColorStringIssue.HexShorthand => "BrushHelper has no #RGB shorthand",
                ColorStringIssue.MissingHash => "hex color needs a leading '#'",
                _ => "named color BrushHelper doesn't know → hex",
            });
    }

    /// <summary>The <c>→ try:</c> text for a finding that carries a replacement.</summary>
    internal static string SuggestionText(ColorStringFinding finding) =>
        finding.Issue == ColorStringIssue.FontFamilyKey
            ? $"new FontFamily(\"{finding.Replacement}\")"
            : $"\"{finding.Replacement}\"";

    static bool HasStringColorParameter(IMethodSymbol method, ITypeSymbol str)
    {
        foreach (var p in method.Parameters)
            if (p.Name == ColorParameterName && SymbolEqualityComparer.Default.Equals(p.Type, str))
                return true;
        return false;
    }

    // Same walk as ColorStringLiteralAnalyzer.ConstantStrings.
    static IEnumerable<string> ConstantStrings(ExpressionSyntax? expression, SemanticModel model)
    {
        if (expression is null) yield break;

        var constant = model.GetConstantValue(expression);
        if (constant.HasValue)
        {
            if (constant.Value is string s) yield return s;
            yield break;
        }

        IEnumerable<ExpressionSyntax> children = expression switch
        {
            ParenthesizedExpressionSyntax p => [p.Expression],
            ConditionalExpressionSyntax c => [c.WhenTrue, c.WhenFalse],
            BinaryExpressionSyntax b when b.IsKind(SyntaxKind.CoalesceExpression) => [b.Left, b.Right],
            SwitchExpressionSyntax sw => sw.Arms.Select(a => a.Expression),
            _ => [],
        };
        foreach (var child in children)
            foreach (var item in ConstantStrings(child, model))
                yield return item;
    }
}
