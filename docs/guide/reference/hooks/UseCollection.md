# UseCollection

Subscribes to an ObservableCollection and re-renders on Add/Remove/Reset. Returns the collection as IReadOnlyList.

```csharp
public IReadOnlyList<T> UseCollection<T>(ObservableCollection<T> collection)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Collections](../../collections.md), [Hooks](../../hooks.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `collection` | `ObservableCollection<T>` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected IReadOnlyList<T> UseCollection<T>(ObservableCollection<T> collection)
```
