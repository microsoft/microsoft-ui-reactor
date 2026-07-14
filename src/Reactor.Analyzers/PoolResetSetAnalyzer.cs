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
/// Also reports REACTOR_VIS_001 for the closely-related imperative
/// <c>.Set(c =&gt; c.Visibility = ...)</c> case (see <see cref="VisibilityDiagnosticId"/>).
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
    /// REACTOR_VIS_001: an imperative <c>.Set(c =&gt; c.Visibility = Visibility.X)</c>
    /// that should be the declarative <c>.IsVisible(bool)</c> modifier. This is the same
    /// failure mode as POOL_001 — an un-reconciled imperative write lost on re-render /
    /// pool reuse — but <c>Visibility</c> is deliberately kept out of
    /// <see cref="TrappedProperties"/> because its modifier has a different signature
    /// (enum property vs. <c>bool</c> modifier), so it needs its own descriptor and a
    /// dedicated bool-translating code fix (<c>SetVisibilityCodeFix</c>).
    /// </summary>
    public const string VisibilityDiagnosticId = "REACTOR_VIS_001";

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
            { "IsTabStop",           "IsTabStop" },
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

    private static readonly LocalizableString VisibilityTitle =
        "Use .IsVisible(...) modifier instead of imperative .Set(Visibility = ...)";

    private static readonly LocalizableString VisibilityMessageFormat =
        "'.Set(c => c.Visibility = ...)' is imperative and not reconciled; the value is lost on the next render or on pool reuse. Use the '.IsVisible(bool)' modifier instead.";

    private static readonly LocalizableString VisibilityDescription =
        "Setting Visibility through '.Set(...)' bypasses the declarative modifier chain " +
        "the reconciler re-applies each render, so — like the pool-reset properties — the " +
        "value survives the first render but disappears on the next reconcile or when the " +
        "pooled control is reused. Use the '.IsVisible(bool)' modifier (or conditional " +
        "inclusion) so visibility is reconciled every render.";

    private static readonly DiagnosticDescriptor VisibilityRule = new(
        VisibilityDiagnosticId,
        VisibilityTitle,
        VisibilityMessageFormat,
        "Reactor.Layout",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: VisibilityDescription);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule, VisibilityRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (!SetLambdaHelpers.IsSetInvocation(invocation, out var memberAccess))
            return;

        var lambdaExpr = invocation.ArgumentList.Arguments[0].Expression;

        var assignment = SetLambdaHelpers.TryGetLambdaAssignment(lambdaExpr);
        if (assignment is null)
            return;
        if (assignment.Kind() != SyntaxKind.SimpleAssignmentExpression)
            return;

        // Both arms require the assignment target to be the .Set lambda's own parameter
        // ('fe.X = v', not 'captured.X = v') so the modifier rewrite applies to the pooled
        // control the .Set configures rather than some other captured object.
        var lambdaParam = SetLambdaHelpers.GetSingleLambdaParameter(lambdaExpr);
        if (lambdaParam is null)
            return;
        var leftAccess = SetLambdaHelpers.GetAssignedMemberAccess(assignment, lambdaParam.Identifier.Text);
        if (leftAccess is null)
            return;

        // Guard against an unrelated user-defined '.Set' helper with the same shape: only
        // Reactor's own .Set setters map to the Reactor modifiers these diagnostics/fixes assume.
        if (!SetLambdaHelpers.IsReactorSetInvocation(invocation, context.SemanticModel, context.CancellationToken))
            return;

        var propName = leftAccess.Name.Identifier.Text;

        // REACTOR_VIS_001 — imperative Visibility toggling. Handled here as a POOL_001
        // extension: 'Visibility' is intentionally NOT in TrappedProperties (its modifier,
        // .IsVisible(bool), has a different signature than the enum property), so it gets a
        // distinct descriptor and its own bool-translating code fix. The receiver must derive
        // from UIElement so the '.IsVisible(...)' rewrite is always sound.
        if (propName == "Visibility")
        {
            var receiverType = context.SemanticModel
                .GetTypeInfo(leftAccess.Expression, context.CancellationToken).Type;
            if (SetLambdaHelpers.InheritsFrom(receiverType, "UIElement", "Microsoft.UI.Xaml"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    VisibilityRule,
                    invocation.GetLocation()));
            }
            return;
        }

        if (!TrappedProperties.TryGetValue(propName, out var modifierName))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            propName,
            modifierName));
    }
}
