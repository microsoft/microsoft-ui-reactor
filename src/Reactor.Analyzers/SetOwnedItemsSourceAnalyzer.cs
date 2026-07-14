using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_ITEMS_001: Detects <c>.Set(lv =&gt; lv.ItemsSource = ...)</c> on a Reactor
/// collection element whose items are owned by keyed reconciliation
/// (<see cref="SetLambdaHelpers.OwnedItemsSourceElements"/>). Reactor populates
/// <c>.Items</c> from the element's <c>items</c> factory argument and diffs it; a manual
/// <c>ItemsSource</c> write throws or fights that diff, corrupting selection and
/// virtualization. <c>AutoSuggestBoxElement</c> is excluded — there, <c>ItemsSource</c>
/// is the documented escape hatch.
/// </summary>
/// <remarks>
/// No code fix: the data belongs in the element's <c>items</c> factory argument, which is
/// not a mechanical rewrite. See spec 060 §4.2.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SetOwnedItemsSourceAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_ITEMS_001";

    private static readonly LocalizableString Title =
        "Do not set ItemsSource on a Reactor-owned collection element";

    private static readonly LocalizableString MessageFormat =
        "'{0}' owns its items via keyed reconciliation; '.Set(x => x.ItemsSource = ...)' fights the diff. Pass the data through the element's 'items' factory argument instead.";

    private static readonly LocalizableString Description =
        "Reactor collection elements (ListView, GridView, TreeView, TabView, Pivot, " +
        "FlipView, SelectorBar) populate the native control's items from their 'items' " +
        "factory argument and reconcile them by key. Assigning ItemsSource imperatively " +
        "through '.Set(...)' bypasses that ownership and either throws or corrupts " +
        "selection/virtualization state. Provide the data through the factory argument.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Collections",
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

        // Syntactic fast path: receiver.Set(x => x.ItemsSource = value).
        if (!SetLambdaHelpers.IsSetInvocation(invocation, out var memberAccess))
            return;

        var lambdaExpr = invocation.ArgumentList.Arguments[0].Expression;
        var assignment = SetLambdaHelpers.TryGetLambdaAssignment(lambdaExpr);
        if (assignment is null || assignment.Kind() != SyntaxKind.SimpleAssignmentExpression)
            return;

        var lambdaParam = SetLambdaHelpers.GetSingleLambdaParameter(lambdaExpr);
        if (lambdaParam is null)
            return;
        var leftAccess = SetLambdaHelpers.GetAssignedMemberAccess(assignment, lambdaParam.Identifier.Text);
        if (leftAccess is null || leftAccess.Name.Identifier.Text != "ItemsSource")
            return;

        // Guard against an unrelated user-defined '.Set' extension on a Reactor element:
        // the "owns its items via keyed reconciliation" rationale is specific to Reactor's
        // own .Set setter.
        if (!SetLambdaHelpers.IsReactorSetInvocation(invocation, context.SemanticModel, context.CancellationToken))
            return;

        // Semantic gate: the receiver's element type must be one of the curated
        // Reactor collection elements (the property name alone is not enough — the
        // curated table is what distinguishes owned collections from the AutoSuggestBox
        // escape hatch and unrelated ItemsSource setters).
        var receiverType = context.SemanticModel
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (!SetLambdaHelpers.IsCuratedReactorElement(receiverType, SetLambdaHelpers.OwnedItemsSourceElements))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            receiverType!.Name));
    }
}
