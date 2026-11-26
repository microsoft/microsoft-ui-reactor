---
title: "ValidationReconciler"
description: "Helpers for running validation within a component's render cycle. Validation runs after state updates and before the returned element tree is reconciled. Results are available to visualizers in the same render pass."
---

```csharp
public static class ValidationReconciler
```

Namespace: `Microsoft.UI.Reactor.Controls.Validation`.

**Learn more:** [Forms and Input](/microsoft-ui-reactor/main/controls/forms)

## Methods [#methods]

### `EvaluateRules` [#evaluaterules]

#### `EvaluateRules(ValidationContext, ValidationRuleElement[])` [#evaluaterulesvalidationcontext-validationruleelement]

Evaluates ValidationRuleElements and pushes their results to the context.

Each rule owns its own slot, identified by its field and the call site of its predicate, so re-running the same rules replaces each verdict rather than accumulating. Rules evaluated here are independent of each other and of any other caller using the same context.

Callers are independent of one another as long as their predicates differ, which they do for the ordinary lambda case — each call site compiles to its own method. Two callers that pass the same \*named method\* as the predicate, for the same field and position, share a slot; give those a `setId` instead.

This overload does not withdraw a rule that disappears from the list — nothing re-evaluates a rule that no longer exists. Use the [`ValidationReconciler.EvaluateRules`](#evaluaterulesvalidationcontext-string-validationruleelement) overload when a set shrinks or empties, or mount the rules in the element tree, where unmount retracts them.

```csharp
public static void EvaluateRules(ValidationContext ctx, params ValidationRuleElement[] rules)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`ValidationContext`](/microsoft-ui-reactor/main/reference/validation/ValidationContext) |  |
| `rules` | [`ValidationRuleElement[]`](/microsoft-ui-reactor/main/reference/elements/forms#validationruleelement) |  |

#### `EvaluateRules(ValidationContext, string, ValidationRuleElement[])` [#evaluaterulesvalidationcontext-string-validationruleelement]

Evaluates a named rule set: the call owns that set for the context, so a producer from the previous call under the same `setId` that is absent this time has its contribution withdrawn.

The identity is explicit because ownership is destructive. Two unrelated callers sharing one context must not silently retract each other's rules, which is what an implicit per-context set would do.

Each rule is keyed by its position within the set, so two rules stay independent even if they carry the same message, and re-running the set replaces each verdict rather than accumulating.

```csharp
public static void EvaluateRules(ValidationContext ctx, string setId, params ValidationRuleElement[] rules)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`ValidationContext`](/microsoft-ui-reactor/main/reference/validation/ValidationContext) |  |
| `setId` | `string` |  |
| `rules` | [`ValidationRuleElement[]`](/microsoft-ui-reactor/main/reference/elements/forms#validationruleelement) |  |

### `EvaluateRulesAsync` [#evaluaterulesasync]

#### `EvaluateRulesAsync(ValidationContext, ValidationRuleElement[])` [#evaluaterulesasyncvalidationcontext-validationruleelement]

The asynchronous counterpart to [`ValidationReconciler.EvaluateRules`](#evaluaterulesvalidationcontext-validationruleelement), for rules built by `ValidationRuleAsync`. Synchronous rules are evaluated normally.

Rules run in order rather than concurrently, so the resulting message order for a field is the order the rules were given in — `GetMessages` exposes that order and callers read the first message.

Each rule is ordered against itself by its own async generation, so two overlapping evaluations of the same rule cannot install out of order. Rules remain independent of each other. No lock is held across a caller's predicate.

```csharp
public static Task EvaluateRulesAsync(ValidationContext ctx, params ValidationRuleElement[] rules)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`ValidationContext`](/microsoft-ui-reactor/main/reference/validation/ValidationContext) |  |
| `rules` | [`ValidationRuleElement[]`](/microsoft-ui-reactor/main/reference/elements/forms#validationruleelement) |  |

#### `EvaluateRulesAsync(ValidationContext, string, ValidationRuleElement[])` [#evaluaterulesasyncvalidationcontext-string-validationruleelement]

The asynchronous counterpart to [`ValidationReconciler.EvaluateRules`](#evaluaterulesvalidationcontext-string-validationruleelement).

Every verdict is computed before any is installed, and nothing is installed if the set has been re-evaluated in the meantime, or if a field's value changed while a predicate was running — each rule opens a per-producer async generation before its predicate runs, and those are verified at the moment of application.

```csharp
public static Task EvaluateRulesAsync(ValidationContext ctx, string setId, params ValidationRuleElement[] rules)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`ValidationContext`](/microsoft-ui-reactor/main/reference/validation/ValidationContext) |  |
| `setId` | `string` |  |
| `rules` | [`ValidationRuleElement[]`](/microsoft-ui-reactor/main/reference/elements/forms#validationruleelement) |  |

#### `EvaluateRulesAsync(ValidationContext, CancellationToken, ValidationRuleElement[])` [#evaluaterulesasyncvalidationcontext-cancellationtoken-validationruleelement]

As [`ValidationReconciler.EvaluateRulesAsync`](#evaluaterulesasyncvalidationcontext-validationruleelement), with cancellation. A rule predicate takes no token of its own, so this is the only way to stop waiting on one that never completes — and with it, to release the evaluation's hold on the context.

```csharp
public static Task EvaluateRulesAsync(ValidationContext ctx, CancellationToken cancellationToken, params ValidationRuleElement[] rules)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`ValidationContext`](/microsoft-ui-reactor/main/reference/validation/ValidationContext) |  |
| `cancellationToken` | `CancellationToken` |  |
| `rules` | [`ValidationRuleElement[]`](/microsoft-ui-reactor/main/reference/elements/forms#validationruleelement) |  |

#### `EvaluateRulesAsync(ValidationContext, string, CancellationToken, ValidationRuleElement[])` [#evaluaterulesasyncvalidationcontext-string-cancellationtoken-validationruleelement]

As [`ValidationReconciler.EvaluateRulesAsync`](#evaluaterulesasyncvalidationcontext-string-validationruleelement), with cancellation. A cancelled call installs nothing: the verdicts are computed before any of them is committed, so there is no partial set to unwind.

```csharp
public static Task EvaluateRulesAsync(ValidationContext ctx, string setId, CancellationToken cancellationToken, params ValidationRuleElement[] rules)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`ValidationContext`](/microsoft-ui-reactor/main/reference/validation/ValidationContext) |  |
| `setId` | `string` |  |
| `cancellationToken` | `CancellationToken` |  |
| `rules` | [`ValidationRuleElement[]`](/microsoft-ui-reactor/main/reference/elements/forms#validationruleelement) |  |

### `ValidateAttached` [#validateattached]

Runs validators from a ValidationAttached record and pushes results to the context.

```csharp
public static void ValidateAttached(ValidationContext ctx, ValidationAttached attached, object? value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`ValidationContext`](/microsoft-ui-reactor/main/reference/validation/ValidationContext) |  |
| `attached` | [`ValidationAttached`](/microsoft-ui-reactor/main/reference/validation/attached#validationattached) |  |
| `value` | `object?` |  |

### `ValidateField` [#validatefield]

Runs all synchronous validators for a field and pushes results to the context. Call this from a component's Render() method after state is finalized.

Results are applied as a single diffed replacement, so calling this repeatedly with an unchanged value is a no-op: no version bump, no change notification, no re-render. That is what lets `.Validate()` run on every render pass.

```csharp
public static void ValidateField(ValidationContext ctx, string fieldName, object? value, params IValidator[] validators)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`ValidationContext`](/microsoft-ui-reactor/main/reference/validation/ValidationContext) |  |
| `fieldName` | `string` |  |
| `value` | `object?` |  |
| `validators` | [`IValidator[]`](/microsoft-ui-reactor/main/reference/validation/IValidator) |  |

### `ValidateFieldAsync` [#validatefieldasync]

Runs all async validators for a field. Typically called from a UseEffect hook.

```csharp
public static Task ValidateFieldAsync(ValidationContext ctx, string fieldName, object? value, IAsyncValidator[] asyncValidators, CancellationToken cancellationToken = default)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `ctx` | [`ValidationContext`](/microsoft-ui-reactor/main/reference/validation/ValidationContext) |  |  |
| `fieldName` | `string` |  |  |
| `value` | `object?` |  |  |
| `asyncValidators` | [`IAsyncValidator[]`](/microsoft-ui-reactor/main/reference/validation/IAsyncValidator) |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |
