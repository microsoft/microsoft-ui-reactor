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
    /// Evaluates all ValidationRuleElements in a list and pushes results to the context.
    /// <para>
    /// Each rule is keyed by its position in the list, so re-running the same set
    /// replaces each rule's previous verdict rather than accumulating — and two rules on
    /// one field stay independent even if they carry the same message.
    /// </para>
    /// </summary>
    public static void EvaluateRules(
        ValidationContext ctx,
        params ValidationRuleElement[] rules)
    {
        for (var i = 0; i < rules.Length; i++)
        {
            rules[i].Evaluate(ctx, "rules[" + i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + "]");
        }
    }
}
