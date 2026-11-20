# UseNavigation

Child mode: retrieves an ancestor's [`NavigationHandle<TRoute>`](../navigation/NavigationHandle.md) from context. Throws if no ancestor provides one (i.e., no root `UseNavigation` with a `NavigationHost` exists above this component in the tree).

```csharp
public NavigationHandle<TRoute> UseNavigation<TRoute>()
public NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Navigation](../../navigation.md), [Hooks](../../hooks.md)

## Overloads

| Overload | Description |
| --- | --- |
| [`UseNavigation<TRoute>()`](#usenavigationtroute) | Child mode: retrieves an ancestor's `NavigationHandle<TRoute>` from context. |
| [`UseNavigation<TRoute>(TRoute)`](#usenavigationtroutetroute) | Root mode: creates a navigation stack with the given initial route. |

### `UseNavigation<TRoute>()`

Child mode: retrieves an ancestor's [`NavigationHandle<TRoute>`](../navigation/NavigationHandle.md) from context. Throws if no ancestor provides one (i.e., no root `UseNavigation` with a `NavigationHost` exists above this component in the tree).

```csharp
public NavigationHandle<TRoute> UseNavigation<TRoute>()
```

### `UseNavigation<TRoute>(TRoute)`

Root mode: creates a navigation stack with the given initial route. Returns a stable [`NavigationHandle<TRoute>`](../navigation/NavigationHandle.md) across re-renders. Wire this handle to a `NavigationHost` in the DSL to render route content. The handle is automatically provided to descendants via context so child components can call `UseNavigation<TRoute>()` (parameterless) to access it.

```csharp
public NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `initial` | `TRoute` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected NavigationHandle<TRoute> UseNavigation<TRoute>()
protected NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial)
```
