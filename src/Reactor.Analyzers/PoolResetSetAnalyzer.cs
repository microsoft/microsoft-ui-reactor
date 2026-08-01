using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_POOL_001: Detects <c>.Set(fe =&gt; fe.PROP = ...)</c> and
/// <c>.Set(fe =&gt; Owner.SetPROP(fe, ...))</c> patterns where <c>PROP</c> is a property
/// <c>ElementPool.CleanElement</c> resets on pool return (or that the reconciler clears
/// between renders), and a Reactor modifier exists that survives the reset. Suggests the
/// fluent modifier.
/// Also reports REACTOR_VIS_001 for the closely-related imperative
/// <c>.Set(c =&gt; c.Visibility = ...)</c> case (see <see cref="VisibilityDiagnosticId"/>).
/// </summary>
/// <remarks>
/// <para>
/// The pool reset is intentional — it's how Reactor guarantees a clean rental.
/// But it makes <c>.Set(...)</c> writes to these properties silently disappear
/// on re-render. The modifier path (stored on <c>Element.Modifiers</c>) is
/// re-applied by the reconciler every render and so survives pool reuse.
/// </para>
/// <para>
/// Two syntactic shapes, one id. An instance-property write is an assignment
/// (<c>fe.Margin = …</c>); an attached-property write is a static call
/// (<c>AutomationProperties.SetName(fe, …)</c>). The failure is identical — the value is
/// cleared on pool return — so they share <see cref="DiagnosticId"/> and differ only in how
/// they are matched and in whether a mechanical rewrite exists
/// (<see cref="AttachedModifierInfo.AutoFix"/>).
/// </para>
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
    /// REACTOR_MOD_002: a fluent modifier exists for this property, but it is not
    /// pool-reset — the value is written correctly, it just costs the element its
    /// structural skip (<c>Element.SettersEqual</c>) and is never unwound when a later
    /// render drops it. A preference rather than a bug, hence Info rather than Warning.
    /// </summary>
    public const string ModifierAvailableDiagnosticId = "REACTOR_MOD_002";

    /// <summary>
    /// Property → modifier name for the pool-reset subset, preserved as a public surface
    /// for callers that only care about that group. The authoritative table, including the
    /// non-pool-reset properties and the receiver gating, is <see cref="ModifierTable"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> TrappedProperties =
        BuildTrappedProperties();

    private static IReadOnlyDictionary<string, string> BuildTrappedProperties()
    {
        var map = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var pair in ModifierTable.Properties.Where(pair => pair.Value.PoolReset))
            map[pair.Key] = pair.Value.Modifier;
        return map;
    }

    /// <summary>
    /// <c>Owner.Property</c> → modifier name for the attached half of the pool-reset set, the
    /// attached counterpart of <see cref="TrappedProperties"/>. Every entry is pool-reset by
    /// construction; the authoritative table, including the setter names and which entries
    /// have a mechanical rewrite, is <see cref="ModifierTable.AttachedProperties"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> TrappedAttachedProperties =
        BuildTrappedAttachedProperties();

    private static IReadOnlyDictionary<string, string> BuildTrappedAttachedProperties()
    {
        var map = new Dictionary<string, string>(
            ModifierTable.AttachedProperties.Count, System.StringComparer.Ordinal);
        foreach (var pair in ModifierTable.AttachedProperties)
            map[pair.Key] = pair.Value.Modifier;
        return map;
    }

    private static readonly LocalizableString Title =
        "Use modifier instead of .Set for pool-reset property";

    private static readonly LocalizableString MessageFormat =
        "'{0}' is reset on pool return; '.Set(...)' writes to it are lost on re-render. Use the '{1}' modifier instead.";

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

    /// <summary>
    /// Diagnostic-property key carrying the comma-separated identifiers of <em>every</em>
    /// write reported on this <c>.Set(...)</c>, so <see cref="PoolResetSetCodeFix"/> knows
    /// exactly which ones passed the gates. An instance-property write is identified by its
    /// bare property name (<c>Margin</c>); an attached one by
    /// <c>Owner.Setter</c> (<c>AutomationProperties.SetName</c>) — the form both sides can
    /// recover syntactically.
    /// <para>
    /// Load-bearing for multi-statement bodies. A block can mix an assignment that was
    /// reported with one the analyzer deliberately skipped (a gated property on the wrong
    /// control type, or one with no modifier at all). Without this the fix would re-derive
    /// candidates from the table alone and could rewrite an assignment that was gated out —
    /// producing exactly the silent no-op the gating exists to prevent.
    /// </para>
    /// <para>
    /// Every diagnostic on the invocation carries the <em>whole</em> set rather than just its
    /// own property, because a code fix provider is not guaranteed to be handed all the
    /// diagnostics sharing a span — Roslyn's <c>CodeFixService</c> groups them, but
    /// <c>Microsoft.CodeAnalysis.Testing</c> invokes the provider once per diagnostic. Making
    /// each diagnostic self-sufficient keeps the fix correct under both.
    /// </para>
    /// </summary>
    internal const string ReportedPropertiesKey = "ReactorReportedProperties";

    /// <summary>
    /// Diagnostic-property key carrying the subset of <see cref="ReportedPropertiesKey"/>
    /// that is safe to rewrite automatically, in the same identifier form.
    /// </summary>
    /// <remarks>
    /// Reported and fixable diverge for the attached shape: several attached properties are
    /// worth diagnosing but have no mechanical rewrite — a different arity
    /// (<c>SetPositionInSet(fe, 2)</c> vs <c>.PositionInSet(position, size)</c>), a different
    /// parameter type, or an N:1 mapping (<c>FlexPanel.*</c> → <c>.Flex(...)</c>). The
    /// decision lives here rather than in the fix because part of it is semantic — whether
    /// <c>ToolTipService.SetToolTip</c>'s <c>object</c> argument really is a <c>string</c> —
    /// and the analyzer is the side that already holds a <c>SemanticModel</c>.
    /// </remarks>
    internal const string FixablePropertiesKey = "ReactorFixableProperties";

    private static readonly LocalizableString ModifierAvailableTitle =
        "Use the Reactor modifier instead of .Set";

    private static readonly LocalizableString ModifierAvailableMessageFormat =
        "A '{1}' modifier exists for '{0}'. Prefer it over '.Set(...)', which re-runs every render, is never unwound, and keeps the element on the reconciler's update path.";

    private static readonly LocalizableString ModifierAvailableDescription =
        "Reactor exposes a fluent modifier for this property. Modifier values are stored on " +
        "Element.Modifiers, structurally diffed, and cleared when removed, whereas '.Set(...)' " +
        "setters are imperative writes the reconciler cannot diff — Element.SettersEqual only " +
        "treats setter arrays as equal when they are the same instance or both empty, so any " +
        "element carrying setters re-runs them on every reconcile. Unlike the pool-reset " +
        "properties this is a preference rather than a correctness bug, so it reports as Info.";

    private static readonly DiagnosticDescriptor ModifierAvailableRule = new(
        ModifierAvailableDiagnosticId,
        ModifierAvailableTitle,
        ModifierAvailableMessageFormat,
        "Reactor.Modifier",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: ModifierAvailableDescription);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule, VisibilityRule, ModifierAvailableRule);

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

        // Both arms require the write's target to be the .Set lambda's own parameter
        // ('fe.X = v' / 'Owner.SetX(fe, v)', not 'captured.X = v') so the modifier rewrite
        // applies to the pooled control the .Set configures rather than some other object.
        var lambdaParam = SetLambdaHelpers.GetSingleLambdaParameter(lambdaExpr);
        if (lambdaParam is null)
            return;
        var paramName = lambdaParam.Identifier.Text;

        // Detection considers every write in the body, not just a lone one: a modifier-backed
        // write is no less wrong for sharing a block with other statements. This is
        // deliberately wider than the fix, which converts a body only when EVERY statement is
        // convertible (SetLambdaHelpers.GetFullyConvertibleLambdaBody) — so a mixed body is
        // reported here and left unfixed rather than partially rewritten.
        var assignments = SetLambdaHelpers.GetLambdaAssignments(lambdaExpr);
        var invocations = SetLambdaHelpers.GetLambdaInvocations(lambdaExpr);
        if (assignments.IsDefaultOrEmpty && invocations.IsDefaultOrEmpty)
            return;

        var isReactorSet = false;
        var reactorSetChecked = false;

        // Guard against an unrelated user-defined '.Set' helper with the same shape: only
        // Reactor's own .Set setters map to the Reactor modifiers these diagnostics/fixes
        // assume. Resolved lazily and once — it is the most expensive check here.
        bool IsReactorSet()
        {
            if (!reactorSetChecked)
            {
                isReactorSet = SetLambdaHelpers.IsReactorSetInvocation(
                    invocation, context.SemanticModel, context.CancellationToken);
                reactorSetChecked = true;
            }
            return isReactorSet;
        }

        // Explicit filter (CodeQL cs/linq/missed-where): only simple assignments are
        // candidates here. '+=' on an event is REACTOR_EVENT_001's job, and a numeric
        // compound assignment has no modifier equivalent.
        var simpleAssignments = assignments
            .Where(assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression));

        // Two passes. The first classifies every write; the second reports, stamping each
        // diagnostic with the complete reported set. The code fix needs the whole set to decide
        // whether a block body is convertible in full, and cannot rely on being handed its
        // siblings (see ReportedPropertiesKey).
        var reportable = new List<(int Position, string Id, string Subject, string Modifier, bool PoolReset)>();

        // Ids the fix must not rewrite. Both bags are keyed by property/setter name, not by
        // occurrence, so one write can disqualify a same-named sibling — and it must:
        // `.Set(fe => { ToolTipService.SetToolTip(fe, "a"); ToolTipService.SetToolTip(fe, obj); })`
        // reports once, and without this the fix would rewrite BOTH occurrences and emit
        // `.ToolTip(obj)`, which does not compile. Same for a write the analyzer deliberately
        // skipped (an explicit null) sharing a name with one it reported: converting it would
        // silently drop the write, since ApplyModifiers ignores a null modifier value.
        //
        // Per-key rather than per-occurrence is exact, not an approximation: the fix is
        // all-or-nothing over the whole body, so a single unconvertible occurrence already
        // declines the entire rewrite.
        var unfixableIds = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var assignment in simpleAssignments)
        {
            var leftAccess = SetLambdaHelpers.GetAssignedMemberAccess(assignment, paramName);
            if (leftAccess is null)
                continue;

            if (!IsReactorSet())
                return;

            var classified = ClassifyAssignment(context, invocation, memberAccess, leftAccess, assignment);
            if (classified is { } hit)
            {
                reportable.Add((
                    assignment.SpanStart, hit.PropName, hit.PropName,
                    "." + hit.Info.Modifier + "(...)",
                    hit.Info.PoolReset && PassesPoolResetGate(context, hit.Info, leftAccess)));
            }
            else
            {
                unfixableIds.Add(leftAccess.Name.Identifier.Text);
            }
        }

        foreach (var attachedCall in invocations)
        {
            if (!SetLambdaHelpers.TryMatchAttachedSetterCall(
                    attachedCall, paramName, out var owner, out var setter, out var value))
            {
                continue;
            }

            var id = owner + "." + setter;
            if (!ModifierTable.AttachedBySetter.TryGetValue(id, out var info))
                continue;

            if (!IsReactorSet())
                return;

            if (!ClassifyAttachedCall(context, attachedCall, info, value, out var fixable))
            {
                unfixableIds.Add(id);
                continue;
            }

            if (!fixable)
                unfixableIds.Add(id);

            reportable.Add((attachedCall.SpanStart, id, info.Key, info.ModifierUsage, PoolReset: true));
        }

        if (reportable.Count == 0)
            return;

        // Source order, so a mixed body reports (and packs) in the order a reader sees.
        reportable.Sort(static (left, right) => left.Position.CompareTo(right.Position));

        var reportedProperties = ImmutableDictionary<string, string?>.Empty
            .Add(ReportedPropertiesKey, string.Join(",", reportable.Select(r => r.Id)))
            .Add(FixablePropertiesKey, string.Join(
                ",",
                reportable.Select(r => r.Id).Where(id => !unfixableIds.Contains(id)).Distinct(System.StringComparer.Ordinal)));

        foreach (var (_, _, subject, modifierUsage, poolReset) in reportable)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                poolReset ? Rule : ModifierAvailableRule,
                invocation.GetLocation(),
                properties: reportedProperties,
                subject,
                modifierUsage));
        }
    }

    /// <summary>
    /// Decide whether an attached-property setter call inside a <c>.Set(...)</c> body should
    /// be reported, and whether the code fix may rewrite it.
    /// </summary>
    private static bool ClassifyAttachedCall(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax attachedCall,
        AttachedModifierInfo info,
        ExpressionSyntax value,
        out bool fixable)
    {
        fixable = false;

        // The simple owner name is not enough — a user type called AutomationProperties with a
        // two-argument SetName would otherwise be rewritten to an unrelated Reactor modifier.
        if (!SetLambdaHelpers.IsAttachedSetterInNamespace(
                attachedCall, info.Owner, info.OwnerNamespace, context.SemanticModel, context.CancellationToken))
        {
            return false;
        }

        // Same reasoning as the assignment arm: ApplyModifiers skips a null modifier value, so
        // suggesting the modifier for an explicit null write would change behaviour.
        if (IsNullOrDefault(value))
            return false;

        fixable = info.AutoFix;
        if (fixable && info.FixValueType is { } requiredType)
        {
            // The setter is typed more loosely than the modifier (SetToolTip takes object,
            // .ToolTip takes string), so the rewrite only compiles for the narrower type.
            var valueInfo = context.SemanticModel.GetTypeInfo(value, context.CancellationToken);
            // A maybe-null value is rejected for the same reason IsNullOrDefault rejects a
            // literal null: ApplyModifiers skips a null modifier value, so the rewrite would
            // silently stop performing the write at runtime (and warn at compile time).
            fixable = MatchesType(valueInfo.Type, requiredType)
                && valueInfo.Nullability.FlowState != NullableFlowState.MaybeNull;
        }

        return true;
    }

    private static bool MatchesType(ITypeSymbol? type, string fullyQualifiedName)
    {
        if (type is null)
            return false;
        var ns = type.ContainingNamespace?.ToDisplayString();
        var name = string.IsNullOrEmpty(ns) ? type.Name : ns + "." + type.Name;
        return string.Equals(name, fullyQualifiedName, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Decide whether one assignment inside a <c>.Set(...)</c> body should be reported as
    /// having a usable modifier. Returns <c>null</c> when it should stay on <c>.Set</c>.
    /// </summary>
    /// <remarks>
    /// REACTOR_VIS_001 is reported inline here and returns <c>null</c>: it has its own
    /// descriptor and its own code fix, so it must not join a REACTOR_POOL_001/MOD_002
    /// modifier chain.
    /// </remarks>
    private static (string PropName, ModifierInfo Info)? ClassifyAssignment(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        MemberAccessExpressionSyntax leftAccess,
        AssignmentExpressionSyntax assignment)
    {
        var propName = leftAccess.Name.Identifier.Text;

        // REACTOR_VIS_001 — imperative Visibility toggling. Handled here as a POOL_001
        // extension: 'Visibility' is intentionally NOT in the modifier table (its modifier,
        // .IsVisible(bool), has a different signature than the enum property), so it gets a
        // distinct descriptor and its own bool-translating code fix. The receiver must derive
        // from UIElement so the '.IsVisible(...)' rewrite is always sound.
        if (propName == "Visibility")
        {
            var visibilityReceiver = context.SemanticModel
                .GetTypeInfo(leftAccess.Expression, context.CancellationToken).Type;
            if (SetLambdaHelpers.InheritsFrom(visibilityReceiver, "UIElement", "Microsoft.UI.Xaml"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    VisibilityRule,
                    invocation.GetLocation()));
            }
            return null;
        }

        if (!ModifierTable.Properties.TryGetValue(propName, out var info))
            return null;

        // A null / default right-hand side is not expressible through the modifier.
        // ApplyModifiers treats a null modifier value as "no modifier supplied" and only
        // clears the property when the PREVIOUS render had one, so `.Background(null)` does
        // not reliably write null the way `.Set(x => x.Background = null)` does. Suggesting
        // the rewrite here would change behaviour — the precise failure this analyzer exists
        // to prevent. Real site: samples/ReactorGallery/ControlPages/Media/ParallaxViewPage.cs.
        if (IsNullOrDefault(assignment.Right))
            return null;

        // Receiver gates. Both are checked against the semantic model rather than inferred:
        // for `.Set(x => …)` the lambda parameter's type IS the runtime WinUI control type
        // (the overload is Action<WinUI.Grid> and friends), and the `.Set` receiver's type
        // is the concrete Reactor element type.
        //
        // The two gates are OR'd when both are present: they are independent routes to a
        // sound rewrite — the generic modifier reaching this control at runtime, or a
        // type-specific overload existing for this element type. Fonts need both.
        var gated = info.ControlGate is not null || info.ElementTypes is not null;
        if (gated && !PassesControlGate(context, info, leftAccess) && !PassesElementGate(context, info, memberAccess))
            return null;

        return (propName, info);
    }

    /// <summary>
    /// True when <c>ElementPool</c> actually resets this property <em>on this receiver</em>, so
    /// <c>REACTOR_POOL_001</c>'s claim that the write is unwound on pool return is true of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A property is pool-reset as a whole, but only the receivers in
    /// <see cref="ModifierInfo.PoolResetGate"/> are recycled. <c>RelativePanel</c> is gated for
    /// <c>Padding</c> and <c>CornerRadius</c> yet is never pooled, so reporting POOL_001 there
    /// asserts something false at Warning severity — a build break for anyone using
    /// <c>TreatWarningsAsErrors</c>. Such a receiver falls to <c>REACTOR_MOD_002</c>, which
    /// describes the hazard it does have.
    /// </para>
    /// <para>
    /// No gate means no receiver restriction — the reset applies wherever the property is
    /// written — so the absence of a list is a pass here, unlike
    /// <see cref="PassesControlGate"/> where absence means "not applicable" and must fail.
    /// The two read alike and mean opposite things, which is why they are separate methods.
    /// </para>
    /// </remarks>
    private static bool PassesPoolResetGate(
        SyntaxNodeAnalysisContext context,
        ModifierInfo info,
        MemberAccessExpressionSyntax leftAccess)
    {
        if (info.PoolResetGate is not { } gate)
            return true;

        var controlType = context.SemanticModel
            .GetTypeInfo(leftAccess.Expression, context.CancellationToken).Type;

        return gate.Any(allowed =>
            SetLambdaHelpers.InheritsFrom(controlType, allowed, "Microsoft.UI.Xaml.Controls"));
    }

    /// <summary>
    /// True when <c>ApplyModifiers</c> would actually write the generic modifier to this
    /// runtime control type. False when no control gate is declared — the caller OR-combines
    /// this with <see cref="PassesElementGate"/>, so "not applicable" must not count as a pass.
    /// </summary>
    private static bool PassesControlGate(
        SyntaxNodeAnalysisContext context,
        ModifierInfo info,
        MemberAccessExpressionSyntax leftAccess)
    {
        if (info.ControlGate is not { } gate)
            return false;

        // ApplyModifiers writes this modifier only to certain control types; on anything
        // else it compiles and silently does nothing, so staying on .Set is correct.
        var controlType = context.SemanticModel
            .GetTypeInfo(leftAccess.Expression, context.CancellationToken).Type;

        return gate.Any(allowed =>
            SetLambdaHelpers.InheritsFrom(controlType, allowed, "Microsoft.UI.Xaml.Controls"));
    }

    /// <summary>
    /// True when the receiver element type declares a type-specific overload of the modifier.
    /// False when no element types are declared, for the same reason as
    /// <see cref="PassesControlGate"/>.
    /// </summary>
    private static bool PassesElementGate(
        SyntaxNodeAnalysisContext context,
        ModifierInfo info,
        MemberAccessExpressionSyntax memberAccess)
    {
        if (info.ElementTypes is not { } elementTypes)
            return false;

        // No generic overload exists (or it does not reach this control), so the rewrite
        // only compiles when the receiver element type declares one.
        var elementType = context.SemanticModel
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (elementType is null)
            return false;

        // Walk the base chain rather than comparing the exact name: element records are not
        // sealed, and an extension declared on TextBlockElement is equally callable on a type
        // derived from it — so exact matching would drop the diagnostic on a receiver where
        // the rewrite compiles fine. InheritsFrom also pins the namespace, so an unrelated
        // type that merely shares a name is still rejected.
        return elementTypes.Any(candidate =>
            SetLambdaHelpers.InheritsFrom(elementType, candidate, "Microsoft.UI.Reactor"));
    }

    /// <summary>
    /// True for a right-hand side that assigns null, seeing through the wrappers that do not
    /// change that: parentheses, casts, and the null-forgiving operator.
    /// </summary>
    /// <remarks>
    /// A bare-literal test is not enough. <c>(Brush)null!</c>, <c>(Brush?)null</c> and
    /// <c>((Brush)null)</c> all assign null while none of them is a
    /// <see cref="SyntaxKind.NullLiteralExpression"/> at the top. Letting one through would
    /// suggest <c>.Background((Brush)null!)</c>, and <c>ApplyModifiers</c> skips a null modifier
    /// value — so the explicit null write silently stops happening, which is exactly the
    /// class of silent behaviour change this gate exists to prevent.
    /// </remarks>
    private static bool IsNullOrDefault(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax suppression
                    when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = suppression.Operand;
                    continue;
                default:
                    return expression.IsKind(SyntaxKind.NullLiteralExpression)
                        || expression.IsKind(SyntaxKind.DefaultLiteralExpression)
                        || expression is DefaultExpressionSyntax;
            }
        }
    }
}
