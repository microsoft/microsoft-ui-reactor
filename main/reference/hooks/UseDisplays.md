---
title: "UseDisplays"
description: "Returns the current display snapshot and re-renders when the display layout changes."
---

```csharp
public IReadOnlyList<DisplayInfo> UseDisplays()
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Advanced Windowing](/microsoft-ui-reactor/main/guides/windowing-advanced), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected IReadOnlyList<DisplayInfo> UseDisplays()
```
