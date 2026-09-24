using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;

namespace Microsoft.UI.Reactor.Controls.Validation;

/// <summary>
/// Helpers for running validation within a component's render cycle.
/// Validation runs after state updates and before the returned element tree is reconciled.
/// Results are available to visualizers in the same render pass.
/// </summary>
public static class ValidationReconciler
{
    /// <summary>
    /// Runs all synchronous validators for a field and pushes results to the context.
    /// Call this from a component's Render() method after state is finalized.
    /// <para>
    /// Results are applied as a single diffed replacement, so calling this repeatedly
    /// with an unchanged value is a no-op: no version bump, no change notification, no
    /// re-render. That is what lets <c>.Validate()</c> run on every render pass.
    /// </para>
    /// </summary>
    public static void ValidateField(
        ValidationContext ctx,
        string fieldName,
        object? value,
        params IValidator[] validators)
    {
        ctx.ApplyValidation(fieldName, value, Run(validators, value, fieldName));
    }

    /// <summary>
    /// Runs validators from a ValidationAttached record and pushes results to the context.
    /// </summary>
    public static void ValidateAttached(
        ValidationContext ctx,
        ValidationAttached attached,
        object? value)
    {
        ctx.ApplyValidation(attached.FieldName, value, Run(attached.Validators, value, attached.FieldName));
    }

    private static List<ValidationMessage> Run(IValidator[] validators, object? value, string fieldName)
    {
        var messages = new List<ValidationMessage>(validators.Length);
        foreach (var validator in validators)
        {
            var result = validator.Validate(value, fieldName);
            if (result is not null)
                messages.Add(result);
        }
        return messages;
    }

    /// <summary>
    /// Runs all async validators for a field. Typically called from a UseEffect hook.
    /// </summary>
    public static async Task ValidateFieldAsync(
        ValidationContext ctx,
        string fieldName,
        object? value,
        IAsyncValidator[] asyncValidators,
        CancellationToken cancellationToken = default)
    {
        // Recording the value and opening the pass happen under one lock: as two calls,
        // concurrent callers could interleave and leave the older value's pass holding
        // the newest token (issue #1262 review). Recording also clears an external
        // verdict about the old value and retires any async pass still out for it, so
        // the helper is self-contained.
        var generation = ctx.BeginAsyncValidation(fieldName, value);

        var messages = new List<ValidationMessage>(asyncValidators.Length);
        foreach (var validator in asyncValidators)
        {
            var result = await validator.ValidateAsync(value, fieldName, cancellationToken);
            if (result is not null)
                messages.Add(result);
        }

        // One atomic install once every validator has resolved: no partial verdict, one
        // notification, and re-running replaces this producer's previous result instead
        // of appending a duplicate. The generation token drops a result that a newer
        // pass has already superseded.
        ctx.ApplyAsyncValidation(fieldName, generation, messages);
    }

    /// <summary>
    /// Evaluates ValidationRuleElements and pushes their results to the context.
    /// <para>
    /// Each rule owns its own slot, identified by its field and the call site of its
    /// predicate, so re-running the same rules replaces each verdict rather than
    /// accumulating. Rules evaluated here are independent of each other and of any other
    /// caller using the same context.
    /// </para>
    /// <para>
    /// Callers are independent of one another as long as their predicates differ, which
    /// they do for the ordinary lambda case — each call site compiles to its own method.
    /// Two callers that pass the same *named method* as the predicate, for the same field
    /// and position, share a slot; give those a <c>setId</c> instead.
    /// </para>
    /// <para>
    /// This overload does not withdraw a rule that disappears from the list — nothing
    /// re-evaluates a rule that no longer exists. Use the
    /// <see cref="EvaluateRules(ValidationContext, string, ValidationRuleElement[])"/>
    /// overload when a set shrinks or empties, or mount the rules in the element tree,
    /// where unmount retracts them.
    /// </para>
    /// </summary>
    public static void EvaluateRules(
        ValidationContext ctx,
        params ValidationRuleElement[] rules)
    {
        for (var i = 0; i < rules.Length; i++)
            rules[i].Evaluate(ctx, ValidationRuleDsl.DirectProducerKey(rules[i], i));
    }

    /// <summary>
    /// Evaluates a named rule set: the call owns that set for the context, so a producer
    /// from the previous call under the same <paramref name="setId"/> that is absent this
    /// time has its contribution withdrawn.
    /// <para>
    /// The identity is explicit because ownership is destructive. Two unrelated callers
    /// sharing one context must not silently retract each other's rules, which is what an
    /// implicit per-context set would do.
    /// </para>
    /// <para>
    /// Each rule is keyed by its position within the set, so two rules stay independent
    /// even if they carry the same message, and re-running the set replaces each verdict
    /// rather than accumulating.
    /// </para>
    /// </summary>
    public static void EvaluateRules(
        ValidationContext ctx,
        string setId,
        params ValidationRuleElement[] rules)
    {
        var generation = ctx.BeginRuleSet(setId);

        // Compute every verdict before installing any: a mid-set notification could
        // otherwise re-enter and take ownership of the set out from under this call.
        var verdicts = new List<(string Field, string Producer, List<ValidationMessage> Messages)>(rules.Length);
        for (var i = 0; i < rules.Length; i++)
        {
            var producer = RuleProducer(setId, i);

            // The set may have been asynchronous last time. A leftover generation would
            // classify this synchronous verdict as async, and the next value change would
            // retract an error that is current.
            ctx.ClearAsyncGeneration(rules[i].Field, producer);

            verdicts.Add((rules[i].Field, producer, rules[i].ComputeSync()));
        }

        ctx.CommitRuleSet(setId, generation, verdicts, asyncTokens: null);
    }

    /// <summary>
    /// The asynchronous counterpart to
    /// <see cref="EvaluateRules(ValidationContext, ValidationRuleElement[])"/>, for rules
    /// built by <c>ValidationRuleAsync</c>. Synchronous rules are evaluated normally.
    /// <para>
    /// Rules run in order rather than concurrently, so the resulting message order for a
    /// field is the order the rules were given in — <c>GetMessages</c> exposes that order
    /// and callers read the first message.
    /// </para>
    /// <para>
    /// Each rule is ordered against itself by its own async generation, so two overlapping
    /// evaluations of the same rule cannot install out of order. Rules remain independent
    /// of each other. No lock is held across a caller's predicate.
    /// </para>
    /// </summary>
    public static Task EvaluateRulesAsync(
        ValidationContext ctx,
        params ValidationRuleElement[] rules)
        => EvaluateRulesAsync(ctx, CancellationToken.None, rules);

    /// <summary>
    /// As <see cref="EvaluateRulesAsync(ValidationContext, ValidationRuleElement[])"/>,
    /// with cancellation. A rule predicate takes no token of its own, so this is the only
    /// way to stop waiting on one that never completes — and with it, to release the
    /// evaluation's hold on the context.
    /// </summary>
    public static async Task EvaluateRulesAsync(
        ValidationContext ctx,
        CancellationToken cancellationToken,
        params ValidationRuleElement[] rules)
    {
        for (var i = 0; i < rules.Length; i++)
            await rules[i].EvaluateAsync(ctx, ValidationRuleDsl.DirectProducerKey(rules[i], i), cancellationToken);
    }

    /// <summary>
    /// The asynchronous counterpart to
    /// <see cref="EvaluateRules(ValidationContext, string, ValidationRuleElement[])"/>.
    /// <para>
    /// Every verdict is computed before any is installed, and nothing is installed if the
    /// set has been re-evaluated in the meantime, or if a field's value changed while a
    /// predicate was running — each rule opens a per-producer async generation before its
    /// predicate runs, and those are verified at the moment of application.
    /// </para>
    /// </summary>
    public static Task EvaluateRulesAsync(
        ValidationContext ctx,
        string setId,
        params ValidationRuleElement[] rules)
        => EvaluateRulesAsync(ctx, setId, CancellationToken.None, rules);

    /// <summary>
    /// As <see cref="EvaluateRulesAsync(ValidationContext, string, ValidationRuleElement[])"/>,
    /// with cancellation. A cancelled call installs nothing: the verdicts are computed
    /// before any of them is committed, so there is no partial set to unwind.
    /// </summary>
    public static async Task EvaluateRulesAsync(
        ValidationContext ctx,
        string setId,
        CancellationToken cancellationToken,
        params ValidationRuleElement[] rules)
    {
        var generation = ctx.BeginRuleSet(setId);

        // Open an async generation for each rule that actually runs asynchronously. A
        // value change retires these, which is how a verdict computed from a superseded
        // value is recognised as stale when the set tries to commit. A synchronous rule
        // in the set is computed at call time and cannot go stale that way, so it gets
        // no token — and any token it carries from a previous async incarnation is
        // cleared, or the next value change would retract a verdict that is current.
        var tokens = new List<(string Field, string Producer, int Token)>(rules.Length);
        var producers = new string[rules.Length];
        for (var i = 0; i < rules.Length; i++)
        {
            var producer = producers[i] = RuleProducer(setId, i);
            ctx.RegisterField(rules[i].Field);

            if (rules[i].AsyncPredicate is null)
                ctx.ClearAsyncGeneration(rules[i].Field, producer);
            else
                tokens.Add((rules[i].Field, producer, ctx.BeginAsyncProducer(rules[i].Field, producer)));
        }

        var verdicts = new List<(string Field, string Producer, List<ValidationMessage> Messages)>(rules.Length);
        for (var i = 0; i < rules.Length; i++)
            verdicts.Add((rules[i].Field, producers[i], await rules[i].ComputeAsync(cancellationToken)));

        ctx.CommitRuleSet(setId, generation, verdicts, tokens);
    }

    private static string RuleProducer(string setId, int index) =>
        "ruleset:" + setId + "[" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + "]";
}