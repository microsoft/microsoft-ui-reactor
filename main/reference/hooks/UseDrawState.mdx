---
title: "UseDrawState"
description: "Creates a stable mutable draw-state reference for Win2D callbacks."
---

```csharp
public static Ref<T> UseDrawState<T>(this RenderContext ctx, Func<T> init) where T : class
```

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `UseDrawStateHook`. Package: `Microsoft.UI.Reactor.Advanced`. Add `using Microsoft.UI.Reactor.Advanced.Win2D;`.

**Learn more:** [Win2D canvas](/microsoft-ui-reactor/main/controls/win2d-canvas)

## Parameters [#parameters]

| Name | Type | Description |
| --- | --- | --- |
| `init` | `Func<T>` |  |

## Remarks [#remarks]

The returned [`Ref<T>`](/microsoft-ui-reactor/main/reference/components/Ref) is created once per component instance and survives re-renders, so animated Win2D callbacks see the same object across UI state changes.

**Thread-safety contract.** [`Ref<T>`](/microsoft-ui-reactor/main/reference/components/Ref) is a plain auto-property: it does *not* insert memory barriers and does *not* guarantee that reads from Win2D's game thread observe UI-thread writes promptly, nor that compound mutations of the referenced object are atomic. Authors who share [`Ref.Current`](/microsoft-ui-reactor/main/reference/components/Ref#current) across the UI and Win2D game threads must enforce their own synchronization:

- For replaced [`Ref.Current`](/microsoft-ui-reactor/main/reference/components/Ref#current) values typed as `class` references on a 64-bit runtime, assignment is single-word atomic. Use `System.Threading.Volatile.Read` / `Volatile.Write` on a private field if ordered visibility matters.
- For mutable arrays or collections shared across threads, use `lock`, `System.Collections.Concurrent` primitives, or a producer/ consumer hand-off queue drained by the game thread at the start of each tick.
- For primitive counters mutated from the game thread and read from the UI thread (e.g., FPS samples), use `System.Threading.Interlocked` or `System.Threading.Volatile`.

See [the Win2D canvas threading guide](/microsoft-ui-reactor/main/controls/win2d-canvas#threading) for the full discussion.
