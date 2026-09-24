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
        // The synchronous path registers through ApplyValidation; this one has to do it
        // explicitly, or MarkAllTouched() and the validity summary would skip a field
        // that only ever had async validators (issue #1262 review).
        ctx.RegisterField(fieldName);

        var generation = ctx.BeginAsyncValidation(fieldName);

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
    /// Evaluates a complete set of ValidationRuleElements and pushes results to the
    /// context.
    /// <para>
    /// Each rule is keyed by its position, so re-running the same set replaces each
    /// rule's previous verdict rather than accumulating, and two rules on one field stay
    /// independent even if they carry the same message.
    /// </para>
    /// <para>
    /// The call owns the whole set for that context: any producer from the previous call
    /// that is absent this time — because the list shrank, or a rule moved to another
    /// field — has its contribution withdrawn. Without that, a removed rule's message
    /// would keep the form invalid forever. Mount rules into the element tree instead if
    /// you need several independent rule sets against one context.
    /// </para>
    /// </summary>
    public static void EvaluateRules(
        ValidationContext ctx,
        params ValidationRuleElement[] rules)
    {
        var generation = NextRuleSetGeneration(ctx);

        var applied = new List<(string Field, string Producer)>(rules.Length);
        for (var i = 0; i < rules.Length; i++)
        {
            var producer = RuleProducer(i);
            applied.Add((rules[i].Field, producer));
            CommitRuleVerdict(ctx, rules[i].Field, producer, rules[i].ComputeSync());
        }

        CommitRuleSet(ctx, generation, applied);
    }

    /// <summary>
    /// The asynchronous counterpart to <see cref="EvaluateRules"/>, for rule sets that
    /// contain rules built by <c>ValidationRuleAsync</c>. Synchronous rules in the set
    /// are evaluated normally.
    /// <para>
    /// Rules run in order rather than concurrently, so the resulting message order for a
    /// field is the order the rules were given in — <c>GetMessages</c> exposes that order
    /// and callers read the first message.
    /// </para>
    /// <para>
    /// Ownership works exactly as in the synchronous overload: each rule is keyed by its
    /// position, and a producer from the previous call that is absent this time has its
    /// contribution withdrawn.
    /// </para>
    /// <para>
    /// Every verdict is computed before any of them is installed, and nothing is
    /// installed if another batch — synchronous or asynchronous — has been requested
    /// against this context in the meantime. Both APIs advance one generation counter, so
    /// an overtaken batch stands down with nothing to unwind. No lock is held across a
    /// caller's predicate, so a predicate that never completes cannot block later batches.
    /// </para>
    /// </summary>
    public static async Task EvaluateRulesAsync(
        ValidationContext ctx,
        params ValidationRuleElement[] rules)
    {
        var generation = NextRuleSetGeneration(ctx);

        var verdicts = new List<(string Field, string Producer, List<ValidationMessage> Messages)>(rules.Length);
        for (var i = 0; i < rules.Length; i++)
            verdicts.Add((rules[i].Field, RuleProducer(i), await rules[i].ComputeAsync()));

        if (!IsNewestRuleSet(ctx, generation)) return;

        var applied = new List<(string Field, string Producer)>(verdicts.Count);
        foreach (var verdict in verdicts)
        {
            applied.Add((verdict.Field, verdict.Producer));
            CommitRuleVerdict(ctx, verdict.Field, verdict.Producer, verdict.Messages);
        }

        CommitRuleSet(ctx, generation, applied);
    }

    private static string RuleProducer(int index) =>
        "rules[" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + "]";

    /// <summary>
    /// Installs one rule's verdict.
    /// <para>
    /// No per-producer async generation is involved: a batch computes every verdict
    /// before installing any of them and orders itself by its own generation, so
    /// <c>ComputeAsync</c> deliberately never opens one. That is what keeps a batch
    /// producer that switches from asynchronous to synchronous from leaving a stale
    /// token behind for the next value change to act on.
    /// </para>
    /// </summary>
    private static void CommitRuleVerdict(
        ValidationContext ctx, string field, string producer, List<ValidationMessage> messages)
    {
        ctx.RegisterField(field);
        ctx.ApplyOwned(field, producer, messages);
    }

    private static void CommitRuleSet(
        ValidationContext ctx, int generation, List<(string Field, string Producer)> applied)
    {
        if (!IsNewestRuleSet(ctx, generation)) return;

        if (_ruleSets.TryGetValue(ctx, out var previous))
        {
            foreach (var entry in previous.Where(entry => !applied.Contains(entry)))
                ctx.RetireProducer(entry.Field, entry.Producer);
            _ruleSets.Remove(ctx);
        }

        _ruleSets.Add(ctx, applied);
    }

    private static int NextRuleSetGeneration(ValidationContext ctx)
    {
        var ticket = _ruleSetTickets.GetValue(ctx, static _ => new global::System.Runtime.CompilerServices.StrongBox<int>(0));
        return global::System.Threading.Interlocked.Increment(ref ticket.Value);
    }

    private static bool IsNewestRuleSet(ValidationContext ctx, int generation)
    {
        var ticket = _ruleSetTickets.GetValue(ctx, static _ => new global::System.Runtime.CompilerServices.StrongBox<int>(0));
        return global::System.Threading.Volatile.Read(ref ticket.Value) == generation;
    }

    // Names the most recently requested rule set for a context, so an overtaken batch can
    // stand down. Weak on the context so it holds nothing alive.
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<ValidationContext, global::System.Runtime.CompilerServices.StrongBox<int>> _ruleSetTickets = new();
    // The rule set most recently evaluated against each context, so the next call can
    // withdraw whatever disappeared. Weak on the context so it holds nothing alive.
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<ValidationContext, List<(string Field, string Producer)>> _ruleSets = new();
}
