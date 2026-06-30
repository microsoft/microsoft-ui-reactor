using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_EVENT_001: Detects <c>.Set(fe =&gt; fe.Event += handler)</c> patterns
/// where Reactor already exposes a declarative <c>.OnEvent(...)</c> modifier.
/// Imperative subscriptions inside <c>.Set(...)</c> can accumulate duplicate
/// handlers as the element rerenders.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SetEventSubscriptionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_EVENT_001";

    public static readonly IReadOnlyDictionary<string, string> EventModifiers =
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "SizeChanged", "OnSizeChanged" },
            { "PointerPressed", "OnPointerPressed" },
            { "PointerMoved", "OnPointerMoved" },
            { "PointerReleased", "OnPointerReleased" },
            { "Tapped", "OnTapped" },
            { "KeyDown", "OnKeyDown" },
            { "PointerEntered", "OnPointerEntered" },
            { "PointerExited", "OnPointerExited" },
            { "PointerCanceled", "OnPointerCanceled" },
            { "PointerCaptureLost", "OnPointerCaptureLost" },
            { "PointerWheelChanged", "OnPointerWheelChanged" },
            { "DoubleTapped", "OnDoubleTapped" },
            { "RightTapped", "OnRightTapped" },
            { "Holding", "OnHolding" },
            { "KeyUp", "OnKeyUp" },
            { "PreviewKeyDown", "OnPreviewKeyDown" },
            { "PreviewKeyUp", "OnPreviewKeyUp" },
            { "CharacterReceived", "OnCharacterReceived" },
            { "AccessKeyDisplayRequested", "OnAccessKeyDisplayRequested" },
            { "GotFocus", "OnGotFocus" },
            { "LostFocus", "OnLostFocus" },
            { "DragEnter", "OnDragEnter" },
            { "DragOver", "OnDragOver" },
            { "DragLeave", "OnDragLeave" },
            { "Drop", "OnDrop" },
        };

    public static readonly IReadOnlyDictionary<string, string> CodeFixableEventModifiers =
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "SizeChanged", "OnSizeChanged" },
            { "PointerPressed", "OnPointerPressed" },
            { "PointerMoved", "OnPointerMoved" },
            { "PointerReleased", "OnPointerReleased" },
            { "Tapped", "OnTapped" },
            { "KeyDown", "OnKeyDown" },
            { "PointerEntered", "OnPointerEntered" },
            { "PointerExited", "OnPointerExited" },
            { "PointerCanceled", "OnPointerCanceled" },
            { "PointerCaptureLost", "OnPointerCaptureLost" },
            { "PointerWheelChanged", "OnPointerWheelChanged" },
            { "DoubleTapped", "OnDoubleTapped" },
            { "RightTapped", "OnRightTapped" },
            { "Holding", "OnHolding" },
            { "KeyUp", "OnKeyUp" },
            { "PreviewKeyDown", "OnPreviewKeyDown" },
            { "PreviewKeyUp", "OnPreviewKeyUp" },
            { "CharacterReceived", "OnCharacterReceived" },
            { "AccessKeyDisplayRequested", "OnAccessKeyDisplayRequested" },
            { "GotFocus", "OnGotFocus" },
            { "LostFocus", "OnLostFocus" },
        };

    private static readonly LocalizableString Title =
        "Use declarative event modifier instead of .Set subscription";

    private static readonly LocalizableString MessageFormat =
        "'{0}' subscribed inside '.Set(...)' may accumulate across renders. Use '.{1}(...)' modifier instead.";

    private static readonly LocalizableString Description =
        "Reactor reruns '.Set(...)' during later updates. Direct event subscriptions " +
        "inside that lambda can accumulate duplicate handlers across renders. When " +
        "Reactor already exposes a declarative On* modifier for the event, prefer " +
        "that render-safe modifier instead.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Events",
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

        if (!TryGetSetInvocationData(invocation, out var setTarget, out var parameterName, out var assignment))
            return;
        if (assignment.Kind() != SyntaxKind.AddAssignmentExpression)
            return;
        if (assignment.Left is not MemberAccessExpressionSyntax leftAccess)
            return;
        if (leftAccess.Expression is not IdentifierNameSyntax leftTarget || leftTarget.Identifier.Text != parameterName)
            return;

        var eventName = leftAccess.Name.Identifier.Text;
        if (!EventModifiers.TryGetValue(eventName, out var modifierName))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            eventName,
            modifierName));
    }

    internal static bool TryGetSetInvocationData(
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax setTarget,
        out string parameterName,
        out AssignmentExpressionSyntax assignment)
    {
        setTarget = null!;
        parameterName = string.Empty;
        assignment = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;
        if (memberAccess.Name.Identifier.Text != "Set")
            return false;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 1)
            return false;

        if (!TryGetLambdaParameterAndAssignment(args[0].Expression, out parameterName, out assignment))
            return false;

        setTarget = memberAccess.Expression;
        return true;
    }

    internal static bool TryGetLambdaParameterAndAssignment(
        ExpressionSyntax lambdaExpr,
        out string parameterName,
        out AssignmentExpressionSyntax assignment)
    {
        parameterName = string.Empty;
        assignment = null!;

        switch (lambdaExpr)
        {
            case SimpleLambdaExpressionSyntax simple:
                parameterName = simple.Parameter.Identifier.Text;
                return TryGetAssignment(simple.ExpressionBody ?? (SyntaxNode?)simple.Block, out assignment);
            case ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count == 1:
                parameterName = paren.ParameterList.Parameters[0].Identifier.Text;
                return TryGetAssignment(paren.ExpressionBody ?? (SyntaxNode?)paren.Block, out assignment);
            default:
                return false;
        }
    }

    private static bool TryGetAssignment(SyntaxNode? exprOrBlock, out AssignmentExpressionSyntax assignment)
    {
        assignment = null!;

        switch (exprOrBlock)
        {
            case AssignmentExpressionSyntax expr:
                assignment = expr;
                return true;
            case BlockSyntax block when block.Statements.Count == 1
                && block.Statements[0] is ExpressionStatementSyntax es
                && es.Expression is AssignmentExpressionSyntax blockAssignment:
                assignment = blockAssignment;
                return true;
            default:
                return false;
        }
    }
}