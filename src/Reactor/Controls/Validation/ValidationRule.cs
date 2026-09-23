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
        ctx.ReplaceInternal(rule.Field, BuildMessages(rule, rule.Predicate()));
    }

    /// <summary>
    /// Evaluates the async validation rule against a ValidationContext.
    /// </summary>
    public static async Task EvaluateAsync(this ValidationRuleElement rule, ValidationContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (rule.AsyncPredicate is null)
        {
            rule.Evaluate(ctx);
            return;
        }

        // Drop the previous verdict while the check is in flight, then install the new
        // one — both diffed, so an unchanged outcome stays silent.
        ctx.ReplaceInternal(rule.Field, []);
        var result = await rule.AsyncPredicate();
        cancellationToken.ThrowIfCancellationRequested();

        ctx.ReplaceInternal(rule.Field, BuildMessages(rule, result));
    }

    private static List<ValidationMessage> BuildMessages(ValidationRuleElement rule, bool passed) =>
        passed ? [] : [new ValidationMessage(rule.Field, rule.Message, rule.Severity)];
}
