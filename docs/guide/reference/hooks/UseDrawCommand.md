# UseDrawCommand

Memoizes a Win2D draw callback and rebuilds it only when `deps` change.

```csharp
public static Action<CanvasDrawingSession, CanvasDrawEventArgs> UseDrawCommand<TState>(this RenderContext ctx, TState state, Action<CanvasDrawingSession, CanvasDrawEventArgs, TState> draw, object[] deps)
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `UseDrawCommandHook`. Package: `Microsoft.UI.Reactor.Advanced`. Add `using Microsoft.UI.Reactor.Advanced.Win2D;`.

**Learn more:** [Win2D canvas](../../win2d-canvas.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `state` | `TState` |  |
| `draw` | `Action<CanvasDrawingSession, CanvasDrawEventArgs, TState>` |  |
| `deps` | `object[]` |  |

## Remarks

The returned delegate is typically passed to `Win2DCanvas(...)` from [`Factories`](../factories/index.md). If the delegate is consumed by animated callbacks, follow [the Win2D canvas threading guide](../../win2d-canvas.md#threading).
