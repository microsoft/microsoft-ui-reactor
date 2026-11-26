# UseObservable

Subscribes to an INotifyPropertyChanged source and re-renders when any property changes. Returns the same source object.

```csharp
public T UseObservable<T>(T source) where T : INotifyPropertyChanged
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Reactivity Model](../../reactivity-model.md), [Hooks](../../hooks.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `source` | `T` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected T UseObservable<T>(T source) where T : INotifyPropertyChanged
```
