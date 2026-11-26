# ValidationRuleDsl

DSL factory methods for ValidationRule elements.

```csharp
public static class ValidationRuleDsl
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

**Learn more:** [Forms and Input](../../forms.md)

## Methods

### `Evaluate`

Evaluates the validation rule against a ValidationContext. Adds or clears messages based on the predicate result.

The result is applied as one diffed replacement rather than clear-then-add. `Mount/UpdateValidationRule` calls this during reconcile — after the component's render scope has closed — so with clear-then-add a failing rule raised [`ValidationContext.Changed`](ValidationContext.md#changed) on every pass, each notification drove another render, and the reconciler tripped its re-render re-entrancy limit. Re-evaluating to the same verdict is now silent.

```csharp
public static void Evaluate(this ValidationRuleElement rule, ValidationContext ctx)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`ValidationContext`](ValidationContext.md) |  |

### `EvaluateAsync`

Evaluates the async validation rule against a ValidationContext.

The previous verdict is kept while the check is in flight and replaced once, at the end. Clearing first would raise [`ValidationContext.Changed`](ValidationContext.md#changed) twice per evaluation for an already-failing rule, and would briefly report the field as valid in between.

Overlapping evaluations are ordered by a generation token taken before the predicate is awaited. Without it, a slow failing check started first could resolve after a fast passing one and reinstate an error the newer run had already cleared.

```csharp
public static Task EvaluateAsync(this ValidationRuleElement rule, ValidationContext ctx, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `ctx` | [`ValidationContext`](ValidationContext.md) |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

## Factories

- [`ValidationRule`](../factories/forms.md#validationrule)
- [`ValidationRuleAsync`](../factories/forms.md#validationruleasync)
