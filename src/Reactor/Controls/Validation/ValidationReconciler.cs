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
        var applied = new List<(string Field, string Producer)>(rules.Length);
        for (var i = 0; i < rules.Length; i++)
        {
            var producer = "rules[" + i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + "]";
            ctx.RegisterField(rules[i].Field);
            rules[i].Evaluate(ctx, producer);
            applied.Add((rules[i].Field, producer));
        }

        if (_ruleSets.TryGetValue(ctx, out var previous))
        {
            foreach (var entry in previous.Where(entry => !applied.Contains(entry)))
                ctx.RetireProducer(entry.Field, entry.Producer);
            _ruleSets.Remove(ctx);
        }

        _ruleSets.Add(ctx, applied);
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
    /// Batches against one context are serialized, and a batch that finds a newer one
    /// already requested stands down without writing. Overlapping batches would
    /// otherwise interleave: an older call's <c>rules[0]</c> on field A and a newer
    /// call's <c>rules[0]</c> on field B are tracked under different fields, so the
    /// per-producer generation cannot order them, and whichever finished last would own
    /// the bookkeeping for both.
    /// </para>
    /// </summary>
    public static async Task EvaluateRulesAsync(
        ValidationContext ctx,
        params ValidationRuleElement[] rules)
    {
        var ticket = _ruleSetTickets.GetValue(ctx, static _ => new global::System.Runtime.CompilerServices.StrongBox<int>(0));
        var generation = global::System.Threading.Interlocked.Increment(ref ticket.Value);

        var gate = _ruleSetGates.GetValue(ctx, static _ => new global::System.Threading.SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(true);
        try
        {
            // A newer batch was requested while this one waited; it supersedes this set
            // wholesale, so writing anything here would be stale by definition.
            if (global::System.Threading.Volatile.Read(ref ticket.Value) != generation) return;

            var applied = new List<(string Field, string Producer)>(rules.Length);
            for (var i = 0; i < rules.Length; i++)
            {
                var producer = "rules[" + i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + "]";
                ctx.RegisterField(rules[i].Field);
                await rules[i].EvaluateAsync(ctx, producer);
                applied.Add((rules[i].Field, producer));
            }

            if (_ruleSets.TryGetValue(ctx, out var previous))
            {
                foreach (var entry in previous.Where(entry => !applied.Contains(entry)))
                    ctx.RetireProducer(entry.Field, entry.Producer);
                _ruleSets.Remove(ctx);
            }

            _ruleSets.Add(ctx, applied);
        }
        finally
        {
            gate.Release();
        }
    }

    // Per-context batch ordering: the ticket names the most recently requested batch, the
    // gate keeps two from running at once. Weak on the context so neither holds it alive.
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<ValidationContext, global::System.Runtime.CompilerServices.StrongBox<int>> _ruleSetTickets = new();
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<ValidationContext, global::System.Threading.SemaphoreSlim> _ruleSetGates = new();

    // The rule set most recently evaluated against each context, so the next call can
    // withdraw whatever disappeared. Weak on the context so it holds nothing alive.
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<ValidationContext, List<(string Field, string Producer)>> _ruleSets = new();
}
