using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_THEME_002: Detects <c>.Set()</c> callbacks that assign a brush to a property
/// with a known lightweight styling key equivalent, and suggests using
/// <c>.Resources()</c> instead for visual-state-aware overrides.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseLightweightStylingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_THEME_002";

    private static readonly LocalizableString Title =
        "Consider lightweight styling for visual-state overrides";
    private static readonly LocalizableString MessageFormat =
        "'{0}.{1}' has a lightweight styling key '{2}' — consider using .Resources(r => r.Set(\"{2}\", ...)) for hover/pressed state support";
    private static readonly LocalizableString Description =
        "Using .Set() to assign a brush directly overrides only the default state. " +
        "Lightweight styling via .Resources() also handles PointerOver, Pressed, and Disabled states.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Style",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <summary>
    /// Maps (controlTypeSuffix, propertyName) to the lightweight styling resource key.
    /// The control type suffix is checked via <c>EndsWith</c> to handle both
    /// <c>Button</c> and <c>b</c> (lambda parameter names).
    /// </summary>
    internal static readonly ImmutableDictionary<string, string> PropertyToResourceKey =
        ImmutableDictionary.CreateRange(new[]
        {
            new KeyValuePair<string, string>("Background", "ButtonBackground"),
            new KeyValuePair<string, string>("Foreground", "ButtonForeground"),
            new KeyValuePair<string, string>("BorderBrush", "ButtonBorderBrush"),
        });

    /// <summary>Control type names whose properties map to known lightweight styling keys.</summary>
    private static readonly ImmutableHashSet<string> KnownControlTypes =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "Button", "ToggleButton", "RepeatButton", "SplitButton",
            "AppBarButton", "HyperlinkButton");

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;
        if (memberAccess.Name.Identifier.Text != "Set")
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 1)
            return;

        // Expect a lambda: e.g., b => b.Background = someBrush
        var lambda = args[0].Expression;
        ExpressionSyntax? body = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.ExpressionBody,
            ParenthesizedLambdaExpressionSyntax paren => paren.ExpressionBody,
            _ => null,
        };

        if (body is not AssignmentExpressionSyntax assignment)
            return;

        if (assignment.Left is not MemberAccessExpressionSyntax leftAccess)
            return;

        var propertyName = leftAccess.Name.Identifier.Text;
        if (!PropertyToResourceKey.TryGetValue(propertyName, out var resourceKey))
            return;

        // Try to determine the control type from the semantic model
        var receiverType = TryGetReceiverTypeName(context, invocation, memberAccess);
        if (receiverType is not null && !IsKnownControlType(receiverType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            receiverType ?? "Control",
            propertyName,
            resourceKey));
    }

    private static string? TryGetReceiverTypeName(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess.Expression, context.CancellationToken);
        if (symbolInfo.Symbol is ILocalSymbol local)
            return local.Type.Name;
        if (symbolInfo.Symbol is IParameterSymbol param)
            return param.Type.Name;

        // Fallback: try type info on the expression
        var typeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken);
        return typeInfo.Type?.Name;
    }

    private static bool IsKnownControlType(string typeName)
    {
        return KnownControlTypes.Contains(typeName);
    }
}
