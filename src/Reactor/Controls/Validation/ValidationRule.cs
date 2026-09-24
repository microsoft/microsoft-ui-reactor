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
    /// <para>
    /// Throws for a rule built by <c>ValidationRuleAsync</c>. Its synchronous predicate
    /// is a constant <c>true</c> placeholder, so evaluating it here would record a
    /// passing verdict without ever running the real check — an invalid field reported
    /// as valid, silently (issue #1262 review).
    /// </para>
    /// <para>
    /// The producer's async generation is retired first. Without that, an
    /// <c>EvaluateAsync</c> still in flight for this same producer would still match its
    /// token when it resolved, and would overwrite the newer synchronous verdict
    /// (issue #1262 review).
    /// </para>
    /// </summary>
    internal static void Evaluate(this ValidationRuleElement rule, ValidationContext ctx, string producer)
    {
        ctx.RegisterField(rule.Field);
        ctx.ClearAsyncGeneration(rule.Field, producer);
        ctx.ApplyOwned(rule.Field, producer, rule.ComputeSync());
    }

    /// <summary>
    /// Evaluates the async validation rule against a ValidationContext.
    /// <para>
    /// The previous verdict is kept while the check is in flight and replaced once,
    /// at the end. Clearing first would raise <see cref="ValidationContext.Changed"/>
    /// twice per evaluation for an already-failing rule, and would briefly report the
    /// field as valid in between.
    /// </para>
    /// <para>
    /// Overlapping evaluations are ordered by a generation token taken before the
    /// predicate is awaited. Without it, a slow failing check started first could
    /// resolve after a fast passing one and reinstate an error the newer run had
    /// already cleared.
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

        ctx.RegisterField(rule.Field);
        var generation = ctx.BeginAsyncProducer(rule.Field, producer);

        // WaitAsync, not a plain await: the predicate takes no token, so awaiting it
        // directly means a hung check keeps this state machine — and through it the rule
        // binding and the ValidationContext — alive forever, with a fresh one added on
        // every re-render. Cancelling now releases us immediately (issue #1262 review).
        var result = await rule.AsyncPredicate().WaitAsync(cancellationToken);

        ctx.ApplyAsyncOwned(rule.Field, producer, generation, BuildMessages(rule, result));
    }

    internal static List<ValidationMessage> ComputeSync(this ValidationRuleElement rule)
    {
        if (rule.AsyncPredicate is not null)
        {
            throw new InvalidOperationException(
                $"The validation rule for field '{rule.Field}' has an async predicate and cannot be " +
                "evaluated synchronously. Use EvaluateAsync or ValidationReconciler.EvaluateRulesAsync, " +
                "or mount the rule in the element tree, which dispatches it asynchronously.");
        }

        return BuildMessages(rule, rule.Predicate());
    }

    internal static async Task<List<ValidationMessage>> ComputeAsync(
        this ValidationRuleElement rule, CancellationToken cancellationToken = default)
    {
        if (rule.AsyncPredicate is null) return BuildMessages(rule, rule.Predicate());

        // WaitAsync, not a plain await: the predicate takes no token, so awaiting it
        // directly means a hung check keeps this state machine — and through it the rule
        // binding and the ValidationContext — alive forever, with a fresh one added on
        // every re-render. Cancelling now releases us immediately (issue #1262 review).
        var result = await rule.AsyncPredicate().WaitAsync(cancellationToken);
        return BuildMessages(rule, result);
    }

    /// <summary>
    /// The identity a single directly-evaluated rule gets, equivalent to position 0 of a
    /// one-rule call. See <see cref="DirectProducerKey"/> for the derivation and its
    /// limits.
    /// </summary>
    internal static string FallbackProducerKey(ValidationRuleElement rule) => DirectProducerKey(rule, 0);

    /// <summary>
    /// Identity for a rule evaluated outside the reconciler, where there is no mounted
    /// instance to key on: the field, the predicate's method — for a lambda, the
    /// compiler-generated method for that call site — and the rule's position in the
    /// call.
    /// <para>
    /// Keying on the message instead (as this once did) orphaned the previous verdict
    /// whenever the text moved, which an interpolated message such as
    /// <c>$"Must be after {start}"</c> does on every change: errors accumulated and a
    /// now-passing rule could not retract the one it replaced.
    /// </para>
    /// <para>
    /// The position disambiguates rules that share a predicate — two
    /// <c>ValidationRule(IsRangeValid, …)</c> on one field would otherwise collapse into
    /// one slot and retract each other.
    /// </para>
    /// <para>
    /// <b>Limit.</b> The predicate's method is the only caller-derived component
    /// available here: C# cannot supply <c>[CallerFilePath]</c> after a <c>params</c>
    /// array, and walking the stack is neither cheap nor trimming-safe. For a lambda
    /// that is enough — each call site compiles to its own method — but two *different*
    /// callers that pass the same **named method** as the predicate, for the same field
    /// and position, share a slot and will retract each other. Give those callers a
    /// <c>setId</c> (<c>EvaluateRules(ctx, "range-rules", …)</c>), which is scoped per
    /// set, or mount the rules in the element tree, where each gets a real per-instance
    /// identity.
    /// </para>
    /// </summary>
    internal static string DirectProducerKey(ValidationRuleElement rule, int position)
    {
        // An async rule's synchronous Predicate is the shared `() => true` created inside
        // ValidationRuleAsync, identical for every such rule — so key off the predicate
        // that actually belongs to this call site.
        var method = rule.AsyncPredicate?.Method ?? rule.Predicate.Method;
        return $"rule:{rule.Field}:{method.DeclaringType?.FullName}.{method.Name}#{position.ToString(global::System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static List<ValidationMessage> BuildMessages(ValidationRuleElement rule, bool passed) =>
        passed ? [] : [new ValidationMessage(rule.Field, rule.Message, rule.Severity)];
}
