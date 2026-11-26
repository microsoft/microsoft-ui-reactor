---
title: "UseSystemBackButton"
seo:
  description: "Subscribes to Alt+Left and VirtualKey.GoBack keyboard events on the given window's content to call NavigationHandle.GoBack. Unsubscribes on unmount."
---

Subscribes to Alt+Left and VirtualKey.GoBack keyboard events on the given window's content to call [`NavigationHandle.GoBack`](/microsoft-ui-reactor/main/reference/navigation/NavigationHandle#goback). Unsubscribes on unmount.

```csharp
public void UseSystemBackButton<TRoute>(NavigationHandle<TRoute> nav, Window window) where TRoute : notnull
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Navigation](/microsoft-ui-reactor/main/guides/navigation), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Parameters [#parameters]

| Name | Type | Description |
| --- | --- | --- |
| `nav` | [`NavigationHandle<TRoute>`](/microsoft-ui-reactor/main/reference/navigation/NavigationHandle) |  |
| `window` | `Window` |  |

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected void UseSystemBackButton<TRoute>(NavigationHandle<TRoute> nav, Window window) where TRoute : notnull
```
