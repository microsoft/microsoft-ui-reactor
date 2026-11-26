# Control modifiers: Forms

Modifiers that only the controls in the Forms area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Forms and Input](../../forms.md)

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
