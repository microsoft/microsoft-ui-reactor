using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_EVENT_001: Detects an event subscription performed through
/// <c>.Set(c =&gt; c.Event += handler)</c> (or <c>-=</c>). Because <c>.Set(...)</c> setters
/// re-run on every reconcile, each render adds another subscription — old closures are never
/// removed, so the handler multiplies its invocations and leaks. When Reactor exposes a
/// declarative <c>.On*</c> modifier for the event, prefer it; otherwise move the subscription
/// into <c>.OnMountAdd(...)</c> with teardown in <c>.OnUnmountAdd(...)</c>.
/// </summary>
/// <remarks>
/// The event-symbol check is mandatory: <c>.Set(c =&gt; c.Opacity += 0.1)</c> (numeric
/// compound assignment) and <c>+=</c> against a non-event delegate field must not fire.
/// Restricted to Reactor's own <c>.Set</c> DSL setter on receivers deriving from
/// <c>FrameworkElement</c>. This rule reconciles the former <c>REACTOR_LIFECYCLE_001</c>
/// (broad detection + <c>.OnMountAdd</c>/<c>.OnUnmountAdd</c> fix) into the shipped
/// <c>REACTOR_EVENT_001</c> (declarative-modifier fix). See spec 060 §4.6.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SetEventSubscriptionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_EVENT_001";

    /// <summary>
    /// WinUI events that Reactor also surfaces as a declarative <c>.On*</c> modifier. When the
    /// subscribed event is one of these, the code fix can rewrite straight to the modifier;
    /// any other event falls back to the <c>.OnMountAdd</c>/<c>.OnUnmountAdd</c> rewrite.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> EventModifiers =
        new Dictionary<string, string>(StringComparer.Ordinal)
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
        "Subscribe to events declaratively or via .OnMountAdd/.OnUnmountAdd, not .Set";

    private static readonly LocalizableString MessageFormat =
        "Event '{0}' is wired imperatively through '.Set(...)', which re-runs on every render. Use the declarative event modifier where one exists, or subscribe in '.OnMountAdd(...)' and unsubscribe in '.OnUnmountAdd(...)' instead.";

    private static readonly LocalizableString Description =
        "'.Set(...)' setters are re-applied on every reconcile, so wiring an event there is " +
        "wrong in both directions: a '+=' subscription adds a new handler each render (the " +
        "handler multiplies its invocations and old closures leak), and a '-=' repeatedly runs " +
        "teardown. When Reactor already exposes a declarative On* modifier for the event (for " +
        "example '.OnTapped(...)'), prefer that render-safe modifier. Otherwise use " +
        "'.OnMountAdd(c => control.Event += h)' for the one-time subscription and " +
        "'.OnUnmountAdd(c => control.Event -= h)' for teardown (the composing Add variants " +
        "preserve any existing mount/unmount wiring).";

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

        // Syntactic fast path: receiver.Set(x => x.Event += handler) / -= handler.
        if (!SetLambdaHelpers.IsSetInvocation(invocation, out _))
            return;

        var lambdaExpr = invocation.ArgumentList.Arguments[0].Expression;
        var assignment = SetLambdaHelpers.TryGetLambdaAssignment(lambdaExpr);
        if (assignment is null)
            return;
        var kind = assignment.Kind();
        if (kind != SyntaxKind.AddAssignmentExpression && kind != SyntaxKind.SubtractAssignmentExpression)
            return;

        var lambdaParam = SetLambdaHelpers.GetSingleLambdaParameter(lambdaExpr);
        if (lambdaParam is null)
            return;
        var leftAccess = SetLambdaHelpers.GetAssignedMemberAccess(assignment, lambdaParam.Identifier.Text);
        if (leftAccess is null)
            return;

        // Guard against an unrelated user-defined '.Set' helper with the same shape: the
        // declarative-modifier and OnMount/OnUnmount rewrites only compile for Reactor elements.
        if (!SetLambdaHelpers.IsReactorSetInvocation(invocation, context.SemanticModel, context.CancellationToken))
            return;

        // MANDATORY: the assigned member must be an event symbol. Without this the rule
        // false-fires on numeric compound assignment (c.Opacity += 0.1) and on '+=' to a
        // non-event delegate field.
        if (context.SemanticModel.GetSymbolInfo(leftAccess, context.CancellationToken).Symbol is not IEventSymbol)
            return;

        // Restrict to receivers deriving from FrameworkElement (the native control).
        var receiverType = context.SemanticModel
            .GetTypeInfo(leftAccess.Expression, context.CancellationToken).Type;
        if (!SetLambdaHelpers.InheritsFrom(receiverType, "FrameworkElement", "Microsoft.UI.Xaml"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            leftAccess.Name.Identifier.Text));
    }
}
