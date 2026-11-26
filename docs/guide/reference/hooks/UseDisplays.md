# UseDisplays

Returns the current display snapshot and re-renders when the display layout changes.

```csharp
public IReadOnlyList<DisplayInfo> UseDisplays()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Advanced Windowing](../../windowing-advanced.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected IReadOnlyList<DisplayInfo> UseDisplays()
```
