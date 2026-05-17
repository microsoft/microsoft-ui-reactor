using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_THEME_001: Detects hard-coded color strings in <c>.Background("...")</c>,
/// <c>.Foreground("...")</c>, and <c>.WithBorder("...")</c> calls where a
/// <see cref="ThemeRef"/> overload exists, and suggests using theme tokens instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseThemeRefAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_THEME_001";

    private static readonly LocalizableString Title =
        "Use ThemeRef instead of hard-coded color";
    private static readonly LocalizableString MessageFormat =
        "Use a ThemeRef token (e.g., Theme.{0}) instead of hard-coded color '{1}' for theme-reactive styling";
    private static readonly LocalizableString Description =
        "Hard-coded colors don't adapt when the user switches between Light and Dark themes. Use Theme tokens for theme-reactive styling.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Style",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <summary>Known color-to-theme-token mappings for code fix suggestions.</summary>
    internal static readonly ImmutableDictionary<string, string> ColorToThemeToken =
        ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new[]
        {
            new KeyValuePair<string, string>("#FFFFFF", "PrimaryBackground"),
            new KeyValuePair<string, string>("white", "PrimaryBackground"),
            new KeyValuePair<string, string>("#000000", "PrimaryText"),
            new KeyValuePair<string, string>("black", "PrimaryText"),
            new KeyValuePair<string, string>("#0078D4", "Accent"),
        });

    private static readonly ImmutableHashSet<string> TargetMethods =
        ImmutableHashSet.Create("Background", "Foreground", "WithBorder");

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    // <snippet:theme-ref-rule>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;
        if (!TargetMethods.Contains(methodName))
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0)
            return;

        // Check if the first argument is a string literal
        var firstArg = args[0].Expression;
        if (firstArg is not LiteralExpressionSyntax literal)
            return;
        if (!literal.IsKind(SyntaxKind.StringLiteralExpression))
            return;

        var colorValue = literal.Token.ValueText;
        // </snippet:theme-ref-rule>

        // Suggest a specific theme token if we have a mapping, otherwise generic
        var suggestion = ColorToThemeToken.TryGetValue(colorValue, out var token)
            ? token
            : "Accent or another semantic token";

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            firstArg.GetLocation(),
            suggestion,
            colorValue));
    }
}
