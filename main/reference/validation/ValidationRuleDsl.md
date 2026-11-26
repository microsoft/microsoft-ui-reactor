---
title: "ValidationRuleDsl"
description: "DSL factory methods for ValidationRule elements."
---

```csharp
public static class ValidationRuleDsl
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

**Learn more:** [Forms and Input](/microsoft-ui-reactor/main/controls/forms)

## Methods [#methods]

### `Evaluate` [#evaluate]

Evaluates the validation rule against a ValidationContext. Adds or clears messages based on the predicate result.

The result is applied as one diffed replacement rather than clear-then-add. `Mount/UpdateValidationRule` calls this during reconcile — after the component's render scope has closed — so with clear-then-add a failing rule raised [`ValidationContext.Changed`](/microsoft-ui-reactor/main/reference/validation/ValidationContext#changed) on every pass, each notification drove another render, and the reconciler tripped its re-render re-entrancy limit. Re-evaluating to the same verdict is now silent.

```csharp
public static void Evaluate(this ValidationRuleElement rule, ValidationContext ctx)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`ValidationContext`](/microsoft-ui-reactor/main/reference/validation/ValidationContext) |  |

### `EvaluateAsync` [#evaluateasync]

Evaluates the async validation rule against a ValidationContext.

The previous verdict is kept while the check is in flight and replaced once, at the end. Clearing first would raise [`ValidationContext.Changed`](/microsoft-ui-reactor/main/reference/validation/ValidationContext#changed) twice per evaluation for an already-failing rule, and would briefly report the field as valid in between.

Overlapping evaluations are ordered by a generation token taken before the predicate is awaited. Without it, a slow failing check started first could resolve after a fast passing one and reinstate an error the newer run had already cleared.

```csharp
public static Task EvaluateAsync(this ValidationRuleElement rule, ValidationContext ctx, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `ctx` | [`ValidationContext`](/microsoft-ui-reactor/main/reference/validation/ValidationContext) |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

## Factories [#factories]

- [`ValidationRule`](/microsoft-ui-reactor/main/reference/factories/forms#validationrule)
- [`ValidationRuleAsync`](/microsoft-ui-reactor/main/reference/factories/forms#validationruleasync)
