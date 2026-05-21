using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_POOL_001: Detects <c>.Set(fe =&gt; fe.PROP = ...)</c> patterns where
/// <c>PROP</c> is a FrameworkElement property that <c>ElementPool.CleanElement</c>
/// resets on pool return (or that the reconciler clears between renders), and a
/// Reactor modifier exists that survives the reset. Suggests the fluent modifier.
/// </summary>
/// <remarks>
/// The pool reset is intentional — it's how Reactor guarantees a clean rental.
/// But it makes <c>.Set(...)</c> writes to these properties silently disappear
/// on re-render. The modifier path (stored on <c>Element.Modifiers</c>) is
/// re-applied by the reconciler every render and so survives pool reuse.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PoolResetSetAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_POOL_001";

    /// <summary>
    /// FrameworkElement property → Reactor modifier method name.
    /// Each entry must be:
    ///   - a property reset in <c>src/Reactor/Core/ElementPool.cs CleanElement(...)</c>
    ///     (or otherwise cleared between renders by the reconciler), AND
    ///   - have a corresponding modifier in <c>ElementExtensions.cs</c> that
    ///     stores into <c>ElementModifiers</c> and is re-applied each render.
    /// Keep this list in sync with both files when either changes.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> TrappedProperties =
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "Margin",              "Margin" },
            { "Width",               "Width" },
            { "Height",              "Height" },
            { "MinWidth",            "MinWidth" },
            { "MinHeight",           "MinHeight" },
            { "MaxWidth",            "MaxWidth" },
            { "MaxHeight",           "MaxHeight" },
            { "HorizontalAlignment", "HorizontalAlignment" },
            { "VerticalAlignment",   "VerticalAlignment" },
            { "Opacity",             "Opacity" },
            { "AccessKey",           "AccessKey" },
        };

    private static readonly LocalizableString Title =
        "Use modifier instead of .Set for pool-reset property";

    private static readonly LocalizableString MessageFormat =
        "'{0}' is reset on pool return; '.Set(...)' writes to it are lost on re-render. Use '.{1}(...)' modifier instead.";

    private static readonly LocalizableString Description =
        "The element pool clears these FrameworkElement properties when a control is " +
        "returned for reuse, and the reconciler re-applies the modifier chain on every " +
        "render. Imperative '.Set(...)' assignments to these properties survive the " +
        "first render but disappear on the next reconcile. Use the corresponding " +
        "fluent modifier (stored on Element.Modifiers) so the value survives pool reuse.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Pool",
        DiagnosticSeverity.Warning,
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

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;
        if (memberAccess.Name.Identifier.Text != "Set")
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 1)
            return;

        var assignment = TryGetLambdaAssignment(args[0].Expression);
        if (assignment is null)
            return;
        if (assignment.Kind() != SyntaxKind.SimpleAssignmentExpression)
            return;

        if (assignment.Left is not MemberAccessExpressionSyntax leftAccess)
            return;

        var propName = leftAccess.Name.Identifier.Text;
        if (!TrappedProperties.TryGetValue(propName, out var modifierName))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            propName,
            modifierName));
    }

    /// <summary>
    /// Extract the single assignment expression from a lambda passed to <c>.Set(...)</c>.
    /// Supports both expression-body lambdas (<c>fe =&gt; fe.X = v</c>) and block-body
    /// lambdas with a single assignment statement (<c>fe =&gt; { fe.X = v; }</c>).
    /// Multi-statement blocks return <c>null</c> — the codefix can't safely rewrite them.
    /// </summary>
    internal static AssignmentExpressionSyntax? TryGetLambdaAssignment(ExpressionSyntax lambdaExpr)
    {
        SyntaxNode? exprOrBlock = lambdaExpr switch
        {
            SimpleLambdaExpressionSyntax simple => (SyntaxNode?)simple.ExpressionBody ?? simple.Block,
            ParenthesizedLambdaExpressionSyntax paren => (SyntaxNode?)paren.ExpressionBody ?? paren.Block,
            _ => null,
        };

        return exprOrBlock switch
        {
            AssignmentExpressionSyntax a => a,
            BlockSyntax block when block.Statements.Count == 1
                && block.Statements[0] is ExpressionStatementSyntax es
                && es.Expression is AssignmentExpressionSyntax ba => ba,
            _ => null,
        };
    }
}
