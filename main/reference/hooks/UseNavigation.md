---
title: "UseNavigation"
seo:
  description: "Child mode: retrieves an ancestor's NavigationHandle<TRoute> from context. Throws if no ancestor provides one (i.e., no root UseNavigation with a NavigationHost exists above this component in the tree)."
---

Child mode: retrieves an ancestor's [`NavigationHandle<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/NavigationHandle) from context. Throws if no ancestor provides one (i.e., no root `UseNavigation` with a `NavigationHost` exists above this component in the tree).

```csharp
public NavigationHandle<TRoute> UseNavigation<TRoute>() where TRoute : notnull
public NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial) where TRoute : notnull
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Navigation](/microsoft-ui-reactor/main/guides/navigation), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Overloads [#overloads]

| Overload | Description |
| --- | --- |
| [`UseNavigation<TRoute>()`](#usenavigationtroute) | Child mode: retrieves an ancestor's `NavigationHandle<TRoute>` from context. |
| [`UseNavigation<TRoute>(TRoute)`](#usenavigationtroutetroute) | Root mode: creates a navigation stack with the given initial route. |

### `UseNavigation<TRoute>()` [#usenavigationtroute]

Child mode: retrieves an ancestor's [`NavigationHandle<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/NavigationHandle) from context. Throws if no ancestor provides one (i.e., no root `UseNavigation` with a `NavigationHost` exists above this component in the tree).

```csharp
public NavigationHandle<TRoute> UseNavigation<TRoute>() where TRoute : notnull
```

### `UseNavigation<TRoute>(TRoute)` [#usenavigationtroutetroute]

Root mode: creates a navigation stack with the given initial route. Returns a stable [`NavigationHandle<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/NavigationHandle) across re-renders. Wire this handle to a `NavigationHost` in the DSL to render route content. The handle is automatically provided to descendants via context so child components can call `UseNavigation<TRoute>()` (parameterless) to access it.

```csharp
public NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial) where TRoute : notnull
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `initial` | `TRoute` |  |

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected NavigationHandle<TRoute> UseNavigation<TRoute>() where TRoute : notnull
protected NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial) where TRoute : notnull
```
