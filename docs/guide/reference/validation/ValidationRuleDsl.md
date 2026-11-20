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

## Factories

- [`ValidationRule`](../factories/forms.md#validationrule)
- [`ValidationRuleAsync`](../factories/forms.md#validationruleasync)

## Modifiers

- [`EvaluateAsync`](../modifiers/forms.md#evaluateasync)
