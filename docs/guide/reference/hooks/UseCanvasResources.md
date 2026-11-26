# UseCanvasResources

Creates resources for the current Win2D shared device, recreates them after `CanvasDevice.DeviceLost`, and disposes them on unmount.

```csharp
public static Ref<TResources?> UseCanvasResources<TResources>(this RenderContext ctx, Func<CanvasDevice, ValueTask<TResources>> create, Action<TResources>? dispose = null) where TResources : class
```

An extension method on [`RenderContext`](../components/RenderContext.md), declared in `UseCanvasResourcesHook`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Win2D canvas](../../win2d-canvas.md)

## Parameters

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `create` | `Func<CanvasDevice, ValueTask<TResources>>` |  |  |
| `dispose` | `Action<TResources>?` | `null` |  |

## Remarks

Resources are created on the process-wide shared device (`CanvasDevice.GetSharedDevice`). Win2D resources are device-affine, so any canvas that draws these resources **must** opt into the shared device via `.UseSharedDevice()` (or `UseSharedDevice = true`). A canvas defaults to its own dedicated device; drawing a shared-device resource with that device raises a cross-device error that surfaces as a fatal stowed exception. See [the shared-device guidance](../../win2d-canvas.md#shared-device).

The resource factory can run on a Win2D worker or game thread depending on the host canvas. See [the Win2D canvas threading guide](../../win2d-canvas.md#threading) and [device loss](../../win2d-canvas.md#device-loss).
