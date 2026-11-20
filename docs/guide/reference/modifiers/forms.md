# Control modifiers: Forms

Modifiers that only the controls in the Forms area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Forms and Input](../../forms.md)

## `EvaluateAsync`

Evaluates the async validation rule against a ValidationContext.

The previous verdict is kept while the check is in flight and replaced once, at the end. Clearing first would raise [`ValidationContext.Changed`](../validation/ValidationContext.md#changed) twice per evaluation for an already-failing rule, and would briefly report the field as valid in between.

Overlapping evaluations are ordered by a generation token taken before the predicate is awaited. Without it, a slow failing check started first could resolve after a fast passing one and reinstate an error the newer run had already cleared.

```csharp
Task EvaluateAsync(this ValidationRuleElement rule, ValidationContext ctx, CancellationToken cancellationToken = default)
```

Declared in `ValidationRuleDsl`.

**Applies to:** [`ValidationRuleElement`](../elements/forms.md#validationruleelement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `ctx` | [`ValidationContext`](../validation/ValidationContext.md) |  |  |
| `cancellationToken` | `CancellationToken` | `default` |  |

## `RootChanged`

Wires the root-changed handler (fired when an immutable root object is replaced). Passing `null` clears.

```csharp
PropertyGridElement RootChanged(this PropertyGridElement el, Action<object>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`PropertyGridElement`](../elements/forms.md#propertygridelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object>?` |  |
