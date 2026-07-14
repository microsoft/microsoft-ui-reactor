using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_ANIM_002</c> — the <c>.Keyframes(name, trigger, configure)</c>
/// modifier re-runs its animation whenever the <c>trigger</c> value changes
/// between renders (the reconciler compares <c>!Equals(prevTrigger, trigger)</c>
/// in <c>Reconciler.ApplyKeyframeAnimations</c>). Passing a value that is
/// freshly computed every render — <c>DateTime.Now</c>, <c>Guid.NewGuid()</c>,
/// a per-render allocation — restarts the animation on every reconcile, so the
/// element flickers as the keyframes constantly reset.
/// </summary>
/// <remarks>
/// Info-severity nudge, no code-fix (the correct value is intent-specific — a
/// state counter the author increments only when they mean to retrigger).
/// Follows the spec §3 pattern: a cheap syntactic gate (a
/// <c>.Keyframes(name, trigger, configure)</c> invocation whose trigger argument
/// classifies as per-render-varying) runs first, then a single semantic check
/// confirms the invocation binds to Reactor's <c>ElementExtensions.Keyframes</c>
/// (not an unrelated method of the same name). See the terse spec entry
/// (docs/specs/060-analyzer-suite-expansion.md §12) and docs/guide/animation.md
/// "Re-running keyframes on every render".
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KeyframeTriggerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_ANIM_002";

    private static readonly LocalizableString Title =
        "Unstable .Keyframes trigger restarts the animation every render";

    private static readonly LocalizableString MessageFormat =
        "The .Keyframes trigger is {0}, which changes on every render and restarts the animation on each reconcile (visible flicker). Pass a value that changes only when you mean to retrigger — e.g. a UseState/UseReducer counter you increment deliberately.";

    private static readonly LocalizableString Description =
        "The .Keyframes(name, trigger, ...) modifier replays its animation whenever the trigger " +
        "value differs from the previous render (the reconciler compares with !Equals). A value " +
        "recomputed every render — DateTime.Now, Guid.NewGuid(), a freshly-allocated object/array/" +
        "collection — is never equal to the prior one, so the animation restarts on every reconcile " +
        "and the element flickers. Use a stable trigger (a counter you increment only when you mean " +
        "to retrigger).";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Animation",
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

    // <snippet:keyframe-trigger-rule>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Cheap syntactic gate: `<receiver>.Keyframes(name, trigger, configure)`.
        // The extension is always called with instance syntax, so the three
        // declared parameters (name, trigger, configure) map to three
        // arguments — the receiver is the member-access target, not an argument.
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
            return;
        if (member.Name.Identifier.ValueText != "Keyframes")
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 3)
            return;

        var triggerExpr = ResolveTriggerArgument(args);
        if (triggerExpr is null)
            return;

        var kind = ClassifyUnstableTrigger(triggerExpr);
        if (kind is null)
            return;

        // Semantic confirmation (runs only after the cheap gate matched a
        // candidate): the invocation must bind to Reactor's
        // ElementExtensions.Keyframes, not an unrelated method named Keyframes.
        if (!BindsToReactorKeyframes(context.SemanticModel, invocation, context.CancellationToken))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, triggerExpr.GetLocation(), kind));
    }
    // </snippet:keyframe-trigger-rule>

    /// <summary>
    /// Confirms the invocation resolves to Reactor's
    /// <c>Microsoft.UI.Reactor.ElementExtensions.Keyframes</c> extension. Binding
    /// to the resolved symbol (rather than checking the receiver's type) keeps
    /// generic <c>T : Element</c> call sites firing and keeps a same-named
    /// third-party extension from producing a Reactor diagnostic.
    /// </summary>
    private static bool BindsToReactorKeyframes(
        SemanticModel model, InvocationExpressionSyntax invocation, System.Threading.CancellationToken ct)
    {
        if (model.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
            return false;

        // Unwrap the reduced extension form (`el.Keyframes(...)`) to its definition.
        var def = method.ReducedFrom ?? method;
        if (def.Name != "Keyframes")
            return false;

        var containing = def.ContainingType;
        if (containing?.Name != "ElementExtensions")
            return false;

        var ns = containing.ContainingNamespace?.ToDisplayString();
        return ns is not null
            && (ns == "Microsoft.UI.Reactor"
                || ns.StartsWith("Microsoft.UI.Reactor.", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// Resolves the <c>trigger</c> argument. A named <c>trigger:</c> argument
    /// wins (so reordered named args are handled correctly). Otherwise the
    /// trigger is the positional argument at index 1 (<c>name</c>,
    /// <c>trigger</c>, <c>configure</c>) — this still holds when a later
    /// parameter is passed by name (e.g. <c>("x", value, configure: ...)</c>).
    /// If index 1 is itself bound by name to a different parameter, positional
    /// mapping is unreliable, so bail.
    /// </summary>
    private static ExpressionSyntax? ResolveTriggerArgument(SeparatedSyntaxList<ArgumentSyntax> args)
    {
        foreach (var arg in args)
        {
            if (arg.NameColon is { } nc && nc.Name.Identifier.ValueText == "trigger")
                return arg.Expression;
        }

        var second = args[1];
        return second.NameColon is null ? second.Expression : null;
    }

    /// <summary>
    /// Restricted, syntactic "is this recomputed every render?" classifier.
    /// Returns a human-readable kind when the expression is a per-render
    /// allocation or a well-known time/id source; <c>null</c> otherwise.
    /// </summary>
    /// <remarks>
    /// NOTE (consolidation): mirrors the restricted subset of
    /// <c>HookRulesAnalyzer.ClassifyDepExpression</c>. Wave C (spec §3.2) extracts
    /// a shared <c>AllocationAnalysis</c> classifier; when that lands on this
    /// branch, replace the allocation arm here with the shared helper.
    ///
    /// This rule's re-fire test is <c>!Equals(prevTrigger, trigger)</c>
    /// (<c>Reconciler.ApplyKeyframeAnimations</c>), so only shapes that yield a
    /// DIFFERENT value/identity each render are flagged. Object / array /
    /// collection creations default to reference equality → a fresh instance each
    /// render → re-fire. <b>Tuples and anonymous objects are excluded</b>: both
    /// have structural (value) equality, so a stable-valued literal does NOT
    /// re-fire, and flagging it would be a false "changes every render". (This
    /// diverges from spec §3.2's allocation-focused subset, which lists anonymous
    /// objects — correct there because HOOKS_013 is about allocation cost, not
    /// <c>Equals</c> identity. Verified against source for ANIM_002.)
    /// </remarks>
    private static string? ClassifyUnstableTrigger(ExpressionSyntax expr)
    {
        expr = UnwrapCasts(expr);

        switch (expr)
        {
            case ObjectCreationExpressionSyntax:
            case ImplicitObjectCreationExpressionSyntax:
                return "a freshly-allocated object";
            case ArrayCreationExpressionSyntax:
            case ImplicitArrayCreationExpressionSyntax:
                return "a freshly-allocated array";
            case CollectionExpressionSyntax:
                return "a freshly-allocated collection";
        }

        // Well-known per-render-varying time / identity sources.
        // `X.Now` / `X.UtcNow` / `X.TickCount(64)` member reads.
        if (expr is MemberAccessExpressionSyntax ma)
        {
            var receiver = RightmostName(ma.Expression);
            var name = ma.Name.Identifier.ValueText;
            switch (receiver, name)
            {
                case ("DateTime", "Now"):
                case ("DateTimeOffset", "Now"):
                    return $"{receiver}.Now";
                case ("DateTime", "UtcNow"):
                case ("DateTimeOffset", "UtcNow"):
                    return $"{receiver}.UtcNow";
                case ("Environment", "TickCount"):
                    return "Environment.TickCount";
                case ("Environment", "TickCount64"):
                    return "Environment.TickCount64";
            }
        }

        // `Guid.NewGuid()` invocation.
        if (expr is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax call }
            && RightmostName(call.Expression) == "Guid"
            && call.Name.Identifier.ValueText == "NewGuid")
        {
            return "Guid.NewGuid()";
        }

        return null;
    }

    /// <summary>
    /// Returns the rightmost identifier of a (possibly qualified) receiver:
    /// <c>DateTime</c> for both <c>DateTime</c> and <c>System.DateTime</c>.
    /// </summary>
    private static string? RightmostName(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
        _ => null,
    };

    private static ExpressionSyntax UnwrapCasts(ExpressionSyntax expr)
    {
        while (true)
        {
            switch (expr)
            {
                case CastExpressionSyntax cast: expr = cast.Expression; continue;
                case ParenthesizedExpressionSyntax paren: expr = paren.Expression; continue;
                default: return expr;
            }
        }
    }
}
