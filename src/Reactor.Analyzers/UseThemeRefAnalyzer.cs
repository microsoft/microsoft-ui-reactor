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

    /// <summary>
    /// REACTOR_THEME_004: the theme-token-bypassing sibling of THEME_001. THEME_001 only
    /// inspects <em>string</em> literals, so an inline <c>new SolidColorBrush(...)</c> passed to the
    /// same <c>.Background</c>/<c>.Foreground</c>/<c>.WithBorder</c> modifiers sails past it — the
    /// same silent dark-mode regression, just expressed as a <c>Brush</c> object.
    /// </summary>
    public const string BrushDiagnosticId = "REACTOR_THEME_004";

    private static readonly LocalizableString BrushTitle =
        "Use ThemeRef instead of a hard-coded brush";
    private static readonly LocalizableString BrushMessageFormat =
        "Use a ThemeRef token (e.g., Theme.{0}) instead of an inline SolidColorBrush for theme-reactive styling";
    private static readonly LocalizableString BrushDescription =
        "An inline SolidColorBrush is a fixed color that doesn't adapt when the user switches between Light and Dark themes. Use Theme tokens for theme-reactive styling.";

    private static readonly DiagnosticDescriptor BrushRule = new(
        BrushDiagnosticId,
        BrushTitle,
        BrushMessageFormat,
        "Reactor.Style",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: BrushDescription);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule, BrushRule);

    /// <summary>Generic suggestion emitted when a hard-coded color has no known token mapping.</summary>
    internal const string GenericTokenSuggestion = "Accent";

    /// <summary>Known color-to-theme-token mappings for code fix suggestions.</summary>
    internal static readonly ImmutableDictionary<string, string> ColorToThemeToken =
        ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new[]
        {
            new KeyValuePair<string, string>("#FFFFFF", "SolidBackground"),
            new KeyValuePair<string, string>("white", "SolidBackground"),
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
        context.RegisterSyntaxNodeAction(AnalyzeBrushArgument, SyntaxKind.InvocationExpression);
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

        // Suggest a specific theme token when the mapping also fits the target modifier — a surface
        // token is a poor foreground suggestion (and vice versa) — otherwise stay generic.
        var suggestion = ColorToThemeToken.TryGetValue(colorValue, out var token) && TokenFitsModifier(token, methodName)
            ? token
            : GenericTokenSuggestion;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            firstArg.GetLocation(),
            suggestion,
            colorValue));
    }

    // REACTOR_THEME_004 — the Brush-typed escape hatch THEME_001's string-literal gate misses.
    private static void AnalyzeBrushArgument(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Reuse THEME_001's target-modifier gate.
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;
        if (!TargetMethods.Contains(memberAccess.Name.Identifier.Text))
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0)
            return;

        // Only the *inline* `new SolidColorBrush(...)` creation bypasses theming. A brush read from a
        // field/local (identifier or member access) is deliberately left alone — it may legitimately
        // hold an already-resolved token brush, and rewriting it would be unsound.
        if (args[0].Expression is not ObjectCreationExpressionSyntax creation)
            return;
        if (!IsSolidColorBrushType(creation.Type))
            return;

        var modifier = memberAccess.Name.Identifier.Text;
        var colorName = TryGetColorName(creation);
        var suggestion = colorName is not null
            && ColorToThemeToken.TryGetValue(colorName, out var token)
            && TokenFitsModifier(token, modifier)
            ? token
            : GenericTokenSuggestion;

        context.ReportDiagnostic(Diagnostic.Create(
            BrushRule,
            creation.GetLocation(),
            suggestion));
    }

    private static bool IsSolidColorBrushType(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text == "SolidColorBrush",
        // `Microsoft.UI.Xaml.Media.SolidColorBrush` (and its `global::`-qualified form, whose top
        // node is still a QualifiedName with `global::…` buried in the left).
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text == "SolidColorBrush",
        // Degenerate `global::SolidColorBrush` (alias-qualified at the top).
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text == "SolidColorBrush",
        _ => false,
    };

    /// <summary>
    /// Extracts the WinUI named color from an inline <c>new SolidColorBrush(Colors.X)</c> so the
    /// diagnostic can suggest — and the code fix can resolve — the matching theme token. Returns
    /// null for any other constructor shape (empty, a variable, or <c>Color.FromArgb(...)</c>),
    /// which keeps the diagnostic as a nudge but withholds the auto-fix (there is no key to invent).
    /// </summary>
    internal static string? TryGetColorName(ObjectCreationExpressionSyntax creation)
    {
        var ctorArgs = creation.ArgumentList?.Arguments;
        if (ctorArgs is not { Count: >= 1 })
            return null;

        // Restrict to `Colors.X` / `Microsoft.UI.Colors.X` so we never map an unrelated `Foo.White`.
        if (ctorArgs.Value[0].Expression is MemberAccessExpressionSyntax colorAccess &&
            IsColorsReceiver(colorAccess.Expression))
        {
            return colorAccess.Name.Identifier.Text;
        }

        return null;
    }

    // Accept only the WinUI static Colors class: bare `Colors` (with `using Microsoft.UI;`), or the
    // qualified `Microsoft.UI.Colors` / `Windows.UI.Colors` (optionally `global::`-qualified). A
    // look-alike such as `MyCompany.UI.Colors` or an unrelated `Foo.White` is intentionally rejected
    // so the code fix never maps a non-WinUI palette. (The code fix additionally confirms the
    // SolidColorBrush type semantically before rewriting.)
    private static bool IsColorsReceiver(ExpressionSyntax receiver) => receiver switch
    {
        IdentifierNameSyntax { Identifier.Text: "Colors" } => true,
        MemberAccessExpressionSyntax
        {
            Name.Identifier.Text: "Colors",
            Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "UI", Expression: var root }
        } => IsMicrosoftOrWindowsRoot(root),
        _ => false,
    };

    // The `Microsoft` / `Windows` root of `Microsoft.UI.Colors` / `Windows.UI.Colors`, allowing a
    // `global::` alias qualifier (e.g. `global::Microsoft.UI.Colors`, common in generated/qualified
    // code — the framework itself uses it).
    private static bool IsMicrosoftOrWindowsRoot(ExpressionSyntax root) => root switch
    {
        IdentifierNameSyntax { Identifier.Text: "Microsoft" or "Windows" } => true,
        AliasQualifiedNameSyntax { Name.Identifier.Text: "Microsoft" or "Windows" } => true,
        _ => false,
    };

    /// <summary>
    /// Whether a mapped theme <paramref name="token"/> is a sensible suggestion/auto-fix for the
    /// target <paramref name="modifier"/>. The color→token map is keyed by color only, so a surface
    /// token (e.g. <c>SolidBackground</c>) could otherwise be suggested for <c>.Foreground</c> —
    /// which flips colors the wrong way across themes (white foreground text rewritten to a
    /// background brush is invisible in Light). Keep in sync with the values in
    /// <see cref="ColorToThemeToken"/>.
    /// </summary>
    internal static bool TokenFitsModifier(string token, string modifier) => token switch
    {
        // Text token → foreground only.
        "PrimaryText" => modifier == "Foreground",
        // Surface/fill token → background only. `.WithBorder` wants a *stroke* token (e.g.
        // Theme.CardStroke), which this color→token map doesn't carry, so a border falls back to the
        // generic suggestion rather than a misleading fill-as-border auto-fix.
        "SolidBackground" => modifier == "Background",
        _ => true, // Accent / neutral tokens fit any target modifier.
    };
}
