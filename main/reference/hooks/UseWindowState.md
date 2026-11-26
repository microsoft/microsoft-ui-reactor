---
title: "UseWindowState"
seo:
  description: "Subscribes to the host window's StateChanged event and re-renders on change. Returns WindowState.Normal outside a window. (spec 036 §7)"
---

Subscribes to the host window's `StateChanged` event and re-renders on change. Returns [`WindowState.Normal`](/microsoft-ui-reactor/main/reference/hosting/window-options#values_10) outside a window. (spec 036 §7)

```csharp
public WindowState UseWindowState()
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Windows](/microsoft-ui-reactor/main/guides/windows), [Advanced Windowing](/microsoft-ui-reactor/main/guides/windowing-advanced), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected WindowState UseWindowState()
```
