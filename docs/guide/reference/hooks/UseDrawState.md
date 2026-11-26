# UseDrawState

Creates a stable mutable draw-state reference for Win2D callbacks.

```csharp
public static Ref<T> UseDrawState<T>(this RenderContext ctx, Func<T> init) where T : class
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `UseDrawStateHook`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Win2D canvas](../../win2d-canvas.md)

## Parameters

| Name | Type | Description |
| --- | --- | --- |
| `init` | `Func<T>` |  |

## Remarks

The returned [`Ref<T>`](../components/Ref.md) is created once per component instance and survives re-renders, so animated Win2D callbacks see the same object across UI state changes.

**Thread-safety contract.** [`Ref<T>`](../components/Ref.md) is a plain auto-property: it does *not* insert memory barriers and does *not* guarantee that reads from Win2D's game thread observe UI-thread writes promptly, nor that compound mutations of the referenced object are atomic. Authors who share [`Ref.Current`](../components/Ref.md#current) across the UI and Win2D game threads must enforce their own synchronization:

- For replaced [`Ref.Current`](../components/Ref.md#current) values typed as `class` references on a 64-bit runtime, assignment is single-word atomic. Use `System.Threading.Volatile.Read` / `Volatile.Write` on a private field if ordered visibility matters.
- For mutable arrays or collections shared across threads, use `lock`, `System.Collections.Concurrent` primitives, or a producer/ consumer hand-off queue drained by the game thread at the start of each tick.
- For primitive counters mutated from the game thread and read from the UI thread (e.g., FPS samples), use `System.Threading.Interlocked` or `System.Threading.Volatile`.

See [the Win2D canvas threading guide](../../win2d-canvas.md#threading) for the full discussion.
