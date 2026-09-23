using Microsoft.UI.Reactor.Core;
using V1 = Microsoft.UI.Reactor.Core.V1Protocol;

namespace Microsoft.UI.Reactor.Controls.Validation;

/// <summary>
/// A virtual element that represents a cross-field validation rule.
/// Place anywhere in the element tree; errors bubble to the nearest visualizer.
/// Does not render any UI — it only produces validation messages.
/// </summary>
public sealed record ValidationRuleElement(
    string Field,
    Func<bool> Predicate,
    string Message,
    Severity Severity = Severity.Error) : Element
{
    /// <summary>
    /// Optional async predicate for rules that require I/O.
    /// When set, takes precedence over the sync Predicate.
    /// </summary>
    public Func<Task<bool>>? AsyncPredicate { get; init; }
}

/// <summary>
/// DSL factory methods for ValidationRule elements.
/// </summary>
public static class ValidationRuleDsl
{
    /// <summary>
    /// Creates a cross-field validation rule. When the predicate returns false,
    /// the message is added to the ValidationContext under the given field name.
    /// When the predicate returns true, any previous message for this rule is cleared.
    /// </summary>
    /// <param name="predicate">Returns true when the rule passes (valid).</param>
    /// <param name="message">Error message when the rule fails.</param>
    /// <param name="field">Field name to associate the error with.</param>
    /// <param name="severity">Severity level (default: Error).</param>
    public static ValidationRuleElement ValidationRule(
        Func<bool> predicate,
        string message,
        string field,
        Severity severity = Severity.Error)
    {
        // Spec 048 §3.4 — per-factory registration touch.
        _ = V1.RegDecorator<ValidationRuleElement, V1.Handlers.ValidationRuleHandler>.Done;
        return new(field, predicate, message, severity);
    }

    /// <summary>
    /// Creates an async cross-field validation rule.
    /// </summary>
    public static ValidationRuleElement ValidationRuleAsync(
        Func<Task<bool>> asyncPredicate,
        string message,
        string field,
        Severity severity = Severity.Error)
    {
        _ = V1.RegDecorator<ValidationRuleElement, V1.Handlers.ValidationRuleHandler>.Done;
        return new(field, () => true, message, severity) { AsyncPredicate = asyncPredicate };
    }

    /// <summary>
    /// Evaluates the validation rule against a ValidationContext.
    /// Adds or clears messages based on the predicate result.
    /// <para>
    /// The result is applied as one diffed replacement rather than clear-then-add.
    /// <c>Mount/UpdateValidationRule</c> calls this during reconcile — after the
    /// component's render scope has closed — so with clear-then-add a failing rule
    /// raised <see cref="ValidationContext.Changed"/> on every pass, each notification
    /// drove another render, and the reconciler tripped its re-render re-entrancy limit.
    /// Re-evaluating to the same verdict is now silent.
    /// </para>
    /// </summary>
    public static void Evaluate(this ValidationRuleElement rule, ValidationContext ctx)
    {
        rule.Evaluate(ctx, FallbackProducerKey(rule));
    }

    /// <summary>
    /// Evaluates the rule as a named producer on its field. The reconciler passes an
    /// identity tied to the rule's mounted placeholder, which survives re-renders and
    /// distinguishes two rules that happen to share a message.
    /// </summary>
    internal static void Evaluate(this ValidationRuleElement rule, ValidationContext ctx, string producer)
    {
        ctx.ApplyOwned(rule.Field, producer, BuildMessages(rule, rule.Predicate()));
    }

    /// <summary>
    /// Evaluates the async validation rule against a ValidationContext.
    /// <para>
    /// The previous verdict is kept while the check is in flight and replaced once,
    /// at the end. Clearing first would raise <see cref="ValidationContext.Changed"/>
    /// twice per evaluation for an already-failing rule, and would briefly report the
    /// field as valid in between.
    /// </para>
    /// </summary>
    public static Task EvaluateAsync(this ValidationRuleElement rule, ValidationContext ctx,
        CancellationToken cancellationToken = default)
        => rule.EvaluateAsync(ctx, FallbackProducerKey(rule), cancellationToken);

    internal static async Task EvaluateAsync(this ValidationRuleElement rule, ValidationContext ctx,
        string producer, CancellationToken cancellationToken = default)
    {
        if (rule.AsyncPredicate is null)
        {
            rule.Evaluate(ctx, producer);
            return;
        }

        var result = await rule.AsyncPredicate();
        cancellationToken.ThrowIfCancellationRequested();

        ctx.ApplyOwned(rule.Field, producer, BuildMessages(rule, result));
    }

    /// <summary>
    /// Identity of last resort for a rule evaluated outside the reconciler, where there
    /// is no mounted instance to key on.
    /// <para>
    /// It is derived from the message and severity, which is stable for the overwhelmingly
    /// common case of a fixed message, but not for an interpolated one
    /// (<c>$"Must be after {start}"</c>) — a changed message reads as a different
    /// producer, orphaning the previous one. Rules mounted through the element tree get
    /// a real per-instance identity instead and are unaffected;
    /// <see cref="ValidationReconciler.EvaluateRules"/> keys by position. Prefer either
    /// over calling <c>Evaluate</c> directly in a render loop.
    /// </para>
    /// </summary>
    internal static string FallbackProducerKey(ValidationRuleElement rule) =>
        $"rule:{(int)rule.Severity}:{rule.Message}";

    private static List<ValidationMessage> BuildMessages(ValidationRuleElement rule, bool passed) =>
        passed ? [] : [new ValidationMessage(rule.Field, rule.Message, rule.Severity)];
}
