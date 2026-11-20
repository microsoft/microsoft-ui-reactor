# UseRef

Returns a mutable ref object that persists across renders.

```csharp
public Ref<T> UseRef<T>(T initialValue = null)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Hooks](../../hooks.md)

## Parameters

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `initialValue` | `T` | `null` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected Ref<T> UseRef<T>(T initialValue = null)
```
