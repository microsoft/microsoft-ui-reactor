# UseSystemBackButton

Subscribes to Alt+Left and VirtualKey.GoBack keyboard events on the given window's content to call [`NavigationHandle.GoBack`](../navigation/NavigationHandle.md#goback). Unsubscribes on unmount.

```csharp
public void UseSystemBackButton<TRoute>(NavigationHandle<TRoute> nav, Window window) where TRoute : notnull
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Navigation](../../navigation.md), [Hooks](../../hooks.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `nav` | [`NavigationHandle<TRoute>`](../navigation/NavigationHandle.md) |  |
| `window` | `Window` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected void UseSystemBackButton<TRoute>(NavigationHandle<TRoute> nav, Window window) where TRoute : notnull
```
