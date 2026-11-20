# Context\<T\>

A typed, named context that can be provided to a subtree and consumed by any descendant. Define as a static field. Provide via .Provide() modifier. Consume via UseContext() hook.

```csharp
public sealed class Context<T> : ContextBase
```

Namespace: `Microsoft.UI.Reactor.Core`. Inherits from [`ContextBase`](ContextBase.md).

**Learn more:** [Context](../../context.md)

## Constructors

### `Context`

```csharp
public Context(T defaultValue, string? name = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `defaultValue` | `T` |  |  |
| `name` | `string?` | `null` |  |

## Properties

### `DefaultValue`

```csharp
public T DefaultValue  // get
```
