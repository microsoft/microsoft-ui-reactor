---
title: "UseIsActive"
description: "Subscribes to the host window's activation events and re-renders on change. Returns true outside a window (the surface is \"active\" while shown). (spec 036 §7)"
---

```csharp
public bool UseIsActive()
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Windows](/microsoft-ui-reactor/main/guides/windows), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected bool UseIsActive()
```
