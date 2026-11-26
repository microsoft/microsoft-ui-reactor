# ElementFactory\<T\>

Bridges WinUI's ItemsRepeater/IElementFactory to Reactor's Reconciler. GetElement calls the view builder then mounts; RecycleElement unmounts.

```csharp
public sealed class ElementFactory<T> : IElementFactory
```

Namespace: `Microsoft.UI.Reactor.Core`.

## Remarks

Spec 042 Phase 1: [`ElementFactory._mountedElements`](ElementFactory.md) is keyed by the stable identity string from `ReactorRow`, not by realized index. Insert-at-0 used to shift every entry's effective index by one — that broke [`ElementFactory.RefreshRealizedItems`](ElementFactory.md)'s lookup contract because the dictionary's int keys no longer matched the repeater's new positions. Keying by string makes the mapping reorder-stable.

## Constructors

### `ElementFactory`

```csharp
public ElementFactory(IReadOnlyList<T> items, Func<T, int, Element> viewBuilder, Reconciler reconciler, Action requestRerender, ElementPool? pool = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `items` | `IReadOnlyList<T>` |  |  |
| `viewBuilder` | `Func<T, int, Element>` |  |  |
| `reconciler` | [`Reconciler`](../extensibility/Reconciler.md) |  |  |
| `requestRerender` | `Action` |  |  |
| `pool` | [`ElementPool?`](../extensibility/ElementPool.md) | `null` |  |

## Methods

### `GetElement`

```csharp
public UIElement GetElement(ElementFactoryGetArgs args)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `args` | `ElementFactoryGetArgs` |  |

### `RecycleElement`

```csharp
public void RecycleElement(ElementFactoryRecycleArgs args)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `args` | `ElementFactoryRecycleArgs` |  |
