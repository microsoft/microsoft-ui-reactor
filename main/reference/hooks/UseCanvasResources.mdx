---
title: "UseCanvasResources"
description: "Creates resources for the current Win2D shared device, recreates them after CanvasDevice.DeviceLost, and disposes them on unmount."
---

```csharp
public static Ref<TResources?> UseCanvasResources<TResources>(this RenderContext ctx, Func<CanvasDevice, ValueTask<TResources>> create, Action<TResources>? dispose = null) where TResources : class
```

An extension method on [`RenderContext`](/microsoft-ui-reactor/main/reference/components/RenderContext), declared in `UseCanvasResourcesHook`. Package: `Microsoft.UI.Reactor.Advanced`. Add `using Microsoft.UI.Reactor.Advanced.Win2D;`.

**Learn more:** [Win2D canvas](/microsoft-ui-reactor/main/controls/win2d-canvas)

## Parameters [#parameters]

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `create` | `Func<CanvasDevice, ValueTask<TResources>>` |  |  |
| `dispose` | `Action<TResources>?` | `null` |  |

## Remarks [#remarks]

Resources are created on the process-wide shared device (`CanvasDevice.GetSharedDevice`). Win2D resources are device-affine, so any canvas that draws these resources **must** opt into the shared device via `.UseSharedDevice()` (or `UseSharedDevice = true`). A canvas defaults to its own dedicated device; drawing a shared-device resource with that device raises a cross-device error that surfaces as a fatal stowed exception. See [the shared-device guidance](/microsoft-ui-reactor/main/controls/win2d-canvas#shared-device).

The resource factory can run on a Win2D worker or game thread depending on the host canvas. See [the Win2D canvas threading guide](/microsoft-ui-reactor/main/controls/win2d-canvas#threading) and [device loss](/microsoft-ui-reactor/main/controls/win2d-canvas#device-loss).
