# UseObservableTree

Observes an object and all nested INotifyPropertyChanged values reachable through its properties. Re-renders when any property at any depth changes. Automatically subscribes/unsubscribes as property values change.

```csharp
public T UseObservableTree<T>(T source) where T : INotifyPropertyChanged
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
protected T UseObservableTree<T>(T source) where T : INotifyPropertyChanged
```
