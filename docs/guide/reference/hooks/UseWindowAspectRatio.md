# UseWindowAspectRatio

Applies a lifetime-bound width/height aspect lock to the owning window. Last mounted hook wins; unmounting restores the previous writer.

```csharp
public void UseWindowAspectRatio(double? widthOverHeight)
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Windows](../../windows.md), [Advanced Windowing](../../windowing-advanced.md), [Hooks](../../hooks.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `widthOverHeight` | `double?` |  |

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected void UseWindowAspectRatio(double? widthOverHeight)
```
