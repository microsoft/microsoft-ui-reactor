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
        var generation = NextRuleSetGeneration(ctx, setId);

        // Compute every verdict before installing any: a mid-set notification could
        // otherwise re-enter and take ownership of the set out from under this call.
        var verdicts = new List<(string Field, string Producer, List<ValidationMessage> Messages)>(rules.Length);
        for (var i = 0; i < rules.Length; i++)
            verdicts.Add((rules[i].Field, RuleProducer(setId, i), rules[i].ComputeSync()));

        CommitRuleSet(ctx, setId, generation, verdicts);
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
    /// Every verdict is computed before any is installed, and nothing is installed if
    /// another call has been made for the same <paramref name="setId"/> in the meantime.
    /// Both overloads of the named form advance one generation per set, so an overtaken
    /// call stands down with nothing to unwind. No lock is held across a caller's
    /// predicate, so one that never completes cannot block later evaluations.
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
        var generation = NextRuleSetGeneration(ctx, setId);

        var verdicts = new List<(string Field, string Producer, List<ValidationMessage> Messages)>(rules.Length);
        for (var i = 0; i < rules.Length; i++)
            verdicts.Add((rules[i].Field, RuleProducer(setId, i), await rules[i].ComputeAsync(cancellationToken)));

        CommitRuleSet(ctx, setId, generation, verdicts);
    }

    private static string RuleProducer(string setId, int index) =>
        "ruleset:" + setId + "[" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + "]";

    /// <summary>
    /// Installs a computed rule set, if this call still owns it.
    /// <para>
    /// The generation check, every producer replacement, and the retirement of producers
    /// that have disappeared are one transaction against the context, emitting a single
    /// notification. Installing producer by producer was observable mid-set: a subscriber
    /// woken by the first verdict could start a new evaluation, and this call would carry
    /// on installing the rest of a set it no longer owned.
    /// </para>
    /// <para>
    /// No per-producer async generation is involved: a set computes every verdict before
    /// installing any of them and orders itself by its own generation, so
    /// <c>ComputeAsync</c> deliberately never opens one. That is what keeps a set producer
    /// that switches from asynchronous to synchronous from leaving a stale token behind
    /// for the next value change to act on.
    /// </para>
    /// </summary>
    private static void CommitRuleSet(
        ValidationContext ctx, string setId, int generation,
        List<(string Field, string Producer, List<ValidationMessage> Messages)> verdicts)
    {
        // The generation check, the recorded-set swap and the context transaction are one
        // critical section, and generation advancement takes the same lock — so no
        // concurrent call can overtake this one between its check and its apply. Nothing
        // inside runs caller code, so this cannot be blocked by a predicate.
        lock (RuleSetLock(ctx))
        {
            if (!IsNewestRuleSet(ctx, setId, generation)) return;

            var applied = new List<(string Field, string Producer)>(verdicts.Count);
            foreach (var verdict in verdicts)
                applied.Add((verdict.Field, verdict.Producer));

            var sets = _ruleSets.GetValue(ctx, static _ => new Dictionary<string, List<(string Field, string Producer)>>(StringComparer.Ordinal));

            var retired = new List<(string Field, string Producer)>();
            if (sets.TryGetValue(setId, out var previous))
                retired.AddRange(previous.Where(entry => !applied.Contains(entry)));
            sets[setId] = applied;

            ctx.ApplyRuleSet(verdicts, retired);
        }
    }

    private static int NextRuleSetGeneration(ValidationContext ctx, string setId)
    {
        // Advancing the generation takes the same lock as the commit, so a concurrent
        // call cannot slip an increment between another call's check and its apply —
        // which would let that call commit a verdict it had already been overtaken on.
        // Monitor is reentrant, so a commit-triggered notification that re-enters here
        // on the same thread is fine.
        lock (RuleSetLock(ctx))
        {
            var tickets = _ruleSetTickets.GetValue(ctx, static _ => new Dictionary<string, global::System.Runtime.CompilerServices.StrongBox<int>>(StringComparer.Ordinal));
            if (!tickets.TryGetValue(setId, out var box))
                tickets[setId] = box = new global::System.Runtime.CompilerServices.StrongBox<int>(0);
            return ++box.Value;
        }
    }

    private static bool IsNewestRuleSet(ValidationContext ctx, string setId, int generation)
    {
        lock (RuleSetLock(ctx))
        {
            var tickets = _ruleSetTickets.GetValue(ctx, static _ => new Dictionary<string, global::System.Runtime.CompilerServices.StrongBox<int>>(StringComparer.Ordinal));
            return tickets.TryGetValue(setId, out var box) && box.Value == generation;
        }
    }

    private static object RuleSetLock(ValidationContext ctx) =>
        _ruleSetCommitLocks.GetValue(ctx, static _ => new object());

    // Per-context, per-set bookkeeping: the last recorded membership of each named set,
    // the generation naming its most recent call, and the lock that makes a commit
    // atomic. All weak on the context so none of them holds it alive.
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<ValidationContext, Dictionary<string, List<(string Field, string Producer)>>> _ruleSets = new();
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<ValidationContext, Dictionary<string, global::System.Runtime.CompilerServices.StrongBox<int>>> _ruleSetTickets = new();
    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<ValidationContext, object> _ruleSetCommitLocks = new();}
