---
title: "Control modifiers: Forms"
description: "Modifiers that only the controls in the Forms area have."
sidebar:
  label: "Forms"
---

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Forms and Input](/microsoft-ui-reactor/main/controls/forms)

## `RootChanged` [#rootchanged]

Wires the root-changed handler (fired when an immutable root object is replaced). Passing `null` clears.

```csharp
PropertyGridElement RootChanged(this PropertyGridElement el, Action<object>? handler)
```

Declared in `ElementExtensions`.

**Applies to:** [`PropertyGridElement`](/microsoft-ui-reactor/main/reference/elements/forms#propertygridelement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<object>?` |  |
