# UseWindowPosition

Subscribes to the host window's `PositionChanged` event and re-renders on change. Returns `(0, 0)` outside a window. (spec 054 §5.5)

```csharp
public (double X, double Y) UseWindowPosition()
```

Declared on [`RenderContext`](../components/RenderContext.md).

**Learn more:** [Windows](../../windows.md), [Advanced Windowing](../../windowing-advanced.md), [Hooks](../../hooks.md)

## Use from a class component

Class components can call the same hook directly. These overloads forward to the ones above:

```csharp
protected (double X, double Y) UseWindowPosition()
```
