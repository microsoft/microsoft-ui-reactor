---
name: reactor-advanced
description: "Reactor.Advanced Win2D canvases — choosing Win2DCanvas, Win2DAnimatedCanvas, or Win2DVirtualCanvas; using UseDrawState, UseCanvasResources, UseDrawCommand; and following threading/device-loss rules."
---

# Reactor Advanced

Use this skill when adding immediate-mode Win2D drawing through `Microsoft.UI.Reactor.Advanced`. The full guide is `docs/guide/win2d-canvas.md`; the performance sample is `samples/apps/particle-storm/`.

## Pick the right canvas

| Workload | Use | Why |
|---|---|---|
| Static or data-driven drawing that changes after state updates | `Win2DCanvas.Of(onDraw, redrawKey)` | Manual invalidation; pass a changing `RedrawKey` for every value the draw callback reads. |
| Game loop, physics, particles, visualizers, steady FPS | `Win2DAnimatedCanvas.Of(onUpdate, onDraw, drawState, isPaused)` | Win2D owns the tick and calls update/draw on the game thread. |
| Huge scrollable or tiled surfaces | `Win2DVirtualCanvas.Of(onRegionDraw, contentSize)` | Draws only invalidated visible regions; update tiles by changing `InvalidateRegions`. |

## Threading cheat sheet

| Callback | Thread | Rule |
|---|---|---|
| `Win2DCanvas.OnDraw` | UI thread | Safe to read Reactor state captured by render. |
| `Win2DAnimatedCanvas.OnUpdate` / `.OnDraw` | Win2D game thread | Do **not** touch WinUI controls. Use thread-safe data or `UseState(threadSafe: true)` for UI handoff. |
| `Win2DVirtualCanvas.OnRegionDraw` | UI thread | Keep work bounded to the invalidated region. |
| `UseCanvasResources` factory | Worker/game thread | Create device resources and return a disposable/recreatable object. |

Treat `Ref.Current` from `UseDrawState` like a volatile field. Re-read it and make the referenced object safe for any cross-thread mutation. Debug builds add a sentinel that appends `docs/guide/win2d-canvas.md#threading` to likely WinUI thread-affinity exceptions from animated callbacks.

## Hooks and recipes

```csharp
var state = ctx.UseDrawState(() => new ParticleField(20_000));

var sprite = ctx.UseCanvasResources<CanvasBitmap>(async device =>
    await CanvasBitmap.LoadAsync(device, "Assets/particle.png"));

var draw = ctx.UseDrawCommand(model, static (session, args, m) =>
    m.Render(session), deps: [model.Version]);
```

`UseCanvasResources` is the device-loss recipe: allocate from the supplied `CanvasDevice`, draw only when the returned ref is non-null, and let the hook dispose/recreate after `CanvasDevice.DeviceLost`.

## Performance proof

The Particle Storm sample (`samples/apps/particle-storm/`) is the canonical pattern: pure Reactor chrome controls parameters; `Win2DAnimatedCanvas` renders the hot particle path; `UseDrawState` holds the particle buffers; `UseCanvasResources` owns sprites and other device resources.
