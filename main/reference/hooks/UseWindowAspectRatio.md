---
title: "UseWindowAspectRatio"
description: "Applies a lifetime-bound width/height aspect lock to the owning window. Last mounted hook wins; unmounting restores the previous writer."
---

```csharp
public void UseWindowAspectRatio(double? widthOverHeight)
```

Declared on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext).

**Learn more:** [Windows](/microsoft-ui-reactor/main/guides/windows), [Advanced Windowing](/microsoft-ui-reactor/main/guides/windowing-advanced), [Hooks](/microsoft-ui-reactor/main/guides/hooks)

## Parameters [#parameters]

| Name | Type | Description |
| --- | --- | --- |
| `widthOverHeight` | `double?` |  |

## Use from a class component [#use-from-a-class-component]

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected void UseWindowAspectRatio(double? widthOverHeight)
```
