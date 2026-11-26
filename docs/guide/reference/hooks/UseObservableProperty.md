# UseObservableProperty

Subscribes to a specific property on an INotifyPropertyChanged source. Re-renders only when that property changes.

```csharp
public TProp UseObservableProperty<T, TProp>(T source, Func<T, TProp> selector, string propertyName) where T : INotifyPropertyChanged
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Reactivity Model](../../reactivity-model.md), [Hooks](../../hooks.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `source` | `T` |  |
| `selector` | `Func<T, TProp>` |  |
| `propertyName` | `string` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected TProp UseObservableProperty<T, TProp>(T source, Func<T, TProp> selector, string propertyName) where T : INotifyPropertyChanged
```
