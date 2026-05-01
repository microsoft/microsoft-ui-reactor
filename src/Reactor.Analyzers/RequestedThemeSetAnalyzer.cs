using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_THEME_003: Detects <c>.Set(fe =&gt; fe.RequestedTheme = ...)</c> patterns and
/// suggests using the fluent <c>.RequestedTheme(ElementTheme.X)</c> modifier instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequestedThemeSetAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_THEME_003";

    private static readonly LocalizableString Title =
        "RequestedTheme modifier available";
    private static readonly LocalizableString MessageFormat =
        "Use '.RequestedTheme({0})' instead of '.Set(fe => fe.RequestedTheme = ...)'";
    private static readonly LocalizableString Description =
        "The fluent .RequestedTheme() modifier is more concise and ensures correct ordering with ThemeRef bindings.";

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

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Match: .Set(lambda)
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;
        if (memberAccess.Name.Identifier.Text != "Set")
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 1)
            return;

        // The argument should be a lambda: fe => fe.RequestedTheme = ...
        if (args[0].Expression is not (SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax))
            return;

        var lambda = args[0].Expression;
        ExpressionSyntax? body = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.ExpressionBody,
            ParenthesizedLambdaExpressionSyntax paren => paren.ExpressionBody,
            _ => null,
        };

        if (body is not AssignmentExpressionSyntax assignment)
            return;

        // Check that the left side is *.RequestedTheme
        if (assignment.Left is not MemberAccessExpressionSyntax leftAccess)
            return;
        if (leftAccess.Name.Identifier.Text != "RequestedTheme")
            return;

        // Extract the right-hand value for the message
        var value = assignment.Right.ToString();

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            value));
    }
}
