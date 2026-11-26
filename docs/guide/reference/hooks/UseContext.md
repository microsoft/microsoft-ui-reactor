# UseContext

Reads the nearest ancestor's provided value for the given context. Returns the context's DefaultValue if no provider exists in the ancestor chain. Follows hook rules — must be called in the same order every render.

```csharp
public T UseContext<T>(Context<T> context)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Context](../../context.md), [Hooks](../../hooks.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `context` | [`Context<T>`](../components/Context.md) |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected T UseContext<T>(Context<T> context)
```
