using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;

namespace Microsoft.UI.Reactor.Controls.Validation;

/// <summary>
/// Attached validation metadata for an element, stored in Element.Attached.
/// </summary>
public sealed record ValidationAttached(
    string FieldName,
    IValidator[] Validators,
    IAsyncValidator[] AsyncValidators)
{
    /// <summary>
    /// The current field value, used for automatic validation when the element is
    /// mounted inside a FormFieldElement or ValidationVisualizerElement.
    /// </summary>
    public object? Value { get; init; }

    public static readonly ValidationAttached Empty = new("", [], []);
}

/// <summary>
/// Fluent extension methods for attaching validation to Reactor elements.
/// </summary>
public static class ValidateExtensions
{
    /// <summary>
    /// Attaches validators to this element. The element's value will be validated
    /// against the provided validators, with results pushed to the nearest ValidationContext.
    /// </summary>
    /// <param name="el">The element to validate.</param>
    /// <param name="fieldName">Field name for validation messages.</param>
    /// <param name="validators">One or more validators to apply.</param>
    public static T Validate<T>(this T el, string fieldName, params IValidator[] validators) where T : Element
    {
        var existing = el.GetAttached<ValidationAttached>();
        var merged = existing is not null
            ? existing with
            {
                FieldName = fieldName,
                Validators = [.. existing.Validators, .. validators]
            }
            : new ValidationAttached(fieldName, validators, []);
        return (T)el.SetAttached(merged);
    }

    /// <summary>
    /// Attaches validators to this element along with the current field value, and —
    /// when called from inside a component's <c>Render()</c> — runs them immediately
    /// against the enclosing <see cref="ValidationContext"/>.
    /// <para>
    /// Running during render rather than during reconcile is what makes the results
    /// readable by the same <c>Render()</c> that produced them, so
    /// <c>When(ctx.IsTouched(f) &amp;&amp; ctx.HasError(f), …)</c> placed after this call
    /// sees the current verdict instead of the previous pass's.
    /// </para>
    /// <para>
    /// The validators are still attached, so <c>FormField</c> keeps working for
    /// elements built outside a render pass. Re-running them is harmless: results are
    /// applied with a structural diff. The visualizers only *display* what a context
    /// already holds — they never run attached validators — so an element that reaches
    /// neither a render pass nor a <c>FormField</c> contributes no verdict.
    /// </para>
    /// </summary>
    public static T Validate<T>(this T el, string fieldName, object? value, params IValidator[] validators) where T : Element
    {
        var existing = el.GetAttached<ValidationAttached>();
        var merged = existing is not null
            ? existing with
            {
                FieldName = fieldName,
                Value = value,
                Validators = [.. existing.Validators, .. validators]
            }
            : new ValidationAttached(fieldName, validators, []) { Value = value };

        RunDuringRender(merged, value);
        return (T)el.SetAttached(merged);
    }

    /// <summary>
    /// Attaches async validators to this element (in addition to any sync validators).
    /// </summary>
    public static T ValidateAsync<T>(this T el, string fieldName, params IAsyncValidator[] asyncValidators) where T : Element
    {
        var existing = el.GetAttached<ValidationAttached>();
        var merged = existing is not null
            ? existing with
            {
                FieldName = fieldName,
                AsyncValidators = [.. existing.AsyncValidators, .. asyncValidators]
            }
            : new ValidationAttached(fieldName, [], asyncValidators);
        return (T)el.SetAttached(merged);
    }

    /// <summary>
    /// Attaches async validators to this element along with the current field value, and
    /// registers the field so <c>MarkAllTouched()</c> covers it.
    /// <para>
    /// This overload is <b>attach-only</b>: it does not run the validators. Nothing
    /// consumes <see cref="ValidationAttached.AsyncValidators"/> automatically — not the
    /// render scope, which must stay synchronous, and not <c>FormField</c>. Run them
    /// yourself from an effect via
    /// <see cref="ValidationReconciler.ValidateFieldAsync"/>, which carries the
    /// generation guard that discards a result superseded by a newer value.
    /// </para>
    /// <para>
    /// Registration happens here for an element built during a render, and again when
    /// <c>FormField</c> mounts or updates it, which is the only chance an element
    /// assembled outside a render pass gets.
    /// </para>
    /// </summary>
    public static T ValidateAsync<T>(this T el, string fieldName, object? value, params IAsyncValidator[] asyncValidators) where T : Element
    {
        var existing = el.GetAttached<ValidationAttached>();
        var merged = existing is not null
            ? existing with
            {
                FieldName = fieldName,
                Value = value,
                AsyncValidators = [.. existing.AsyncValidators, .. asyncValidators]
            }
            : new ValidationAttached(fieldName, [], asyncValidators) { Value = value };

        // Async validators cannot resolve inside a synchronous render, but the field
        // still has to be registered or MarkAllTouched() would skip it.
        ValidationRenderScope.Current?.RegisterField(fieldName);
        return (T)el.SetAttached(merged);
    }

    /// <summary>
    /// Pushes a freshly-attached field's verdict into the context that is rendering, if
    /// any. Outside a render pass — an element assembled in an event handler, a cached
    /// element, a headless unit test — there is no context to reach and this is a no-op,
    /// leaving <c>.Validate()</c> purely declarative as it has always been.
    /// </summary>
    private static void RunDuringRender(ValidationAttached attached, object? value)
    {
        if (attached.Validators.Length == 0) return;
        var ctx = ValidationRenderScope.Current;
        if (ctx is null) return;
        ValidationReconciler.ValidateAttached(ctx, attached, value);
    }

    /// <summary>
    /// Runs all synchronous validators attached to an element against a value.
    /// Returns the list of validation messages (empty if all pass).
    /// </summary>
    public static IReadOnlyList<ValidationMessage> RunValidators(
        this ValidationAttached attached, object? value)
    {
        var messages = new List<ValidationMessage>();
        foreach (var validator in attached.Validators)
        {
            var result = validator.Validate(value, attached.FieldName);
            if (result is not null)
                messages.Add(result);
        }
        return messages;
    }

    /// <summary>
    /// Runs all async validators attached to an element against a value.
    /// </summary>
    public static async Task<IReadOnlyList<ValidationMessage>> RunAsyncValidators(
        this ValidationAttached attached, object? value, CancellationToken cancellationToken = default)
    {
        var messages = new List<ValidationMessage>();
        foreach (var asyncValidator in attached.AsyncValidators)
        {
            var result = await asyncValidator.ValidateAsync(value, attached.FieldName, cancellationToken);
            if (result is not null)
                messages.Add(result);
        }
        return messages;
    }

    /// <summary>
    /// Gets the ValidationAttached metadata from an element, if any.
    /// </summary>
    public static ValidationAttached? GetValidation<T>(this T el) where T : Element =>
        el.GetAttached<ValidationAttached>();
}
