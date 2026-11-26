---
title: "Elements: Win2D canvas"
description: "Win2D drawing surfaces, from the Microsoft.UI.Reactor.Advanced package."
sidebar:
  label: "Win2D canvas"
---

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Win2D canvas](/microsoft-ui-reactor/main/controls/win2d-canvas)

## `Win2DAnimatedCanvasElement` [#win2danimatedcanvaselement]

Reactor element for Win2D's game-loop `CanvasAnimatedControl`.

```csharp
public sealed record Win2DAnimatedCanvasElement : Element
```

Namespace: `Microsoft.UI.Reactor.Advanced.Win2D`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`Win2DAnimatedCanvas`](/microsoft-ui-reactor/main/reference/factories/win2d#win2danimatedcanvas)

**Modifiers:** [`ClearColor`](/microsoft-ui-reactor/main/reference/modifiers/win2d#clearcolor), [`Paused`](/microsoft-ui-reactor/main/reference/modifiers/win2d#paused), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`TargetFps`](/microsoft-ui-reactor/main/reference/modifiers/win2d#targetfps), [`UseSharedDevice`](/microsoft-ui-reactor/main/reference/modifiers/win2d#useshareddevice)

| Property | Type | Description |
| --- | --- | --- |
| `ClearColor` | `Color` | Background clear color applied to the underlying `CanvasAnimatedControl`. |
| `DrawState` | `object?` | Optional user state passed to [`Win2DAnimatedCanvasElement.OnUpdate`](#win2danimatedcanvaselement) and [`Win2DAnimatedCanvasElement.OnDraw`](#win2danimatedcanvaselement). |
| `IsPaused` | `bool` | Whether the canvas simulation is paused without unmounting the control. When `true`, [`Win2DAnimatedCanvasElement.OnUpdate`](#win2danimatedcanvaselement) is skipped (no state advancement) but [`Win2DAnimatedCanvasElement.OnDraw`](#win2danimatedcanvaselement) continues to fire each tick so the last frame stays visible. The underlying Win2D game loop also continues to tick (~16 ms of empty work per pause-second). |
| `OnCreateResources` | `Func<CanvasAnimatedControl, Task>?` | Optional asynchronous resource creation callback tracked before the animated loop starts. |
| `OnDraw` | `Action<CanvasDrawingSession, CanvasAnimatedDrawEventArgs, object?>?` | Per-frame draw callback. Runs on the Win2D game thread; see [the Win2D canvas threading guide](/microsoft-ui-reactor/main/controls/win2d-canvas#threading). |
| `OnUpdate` | `Action<CanvasAnimatedUpdateEventArgs, object?>?` | Per-tick update callback. Runs on the Win2D game thread; see [the Win2D canvas threading guide](/microsoft-ui-reactor/main/controls/win2d-canvas#threading). |
| `Setters` | `Action<CanvasAnimatedControl>[]` | Raw Win2D control setters applied after typed properties. |
| `TargetElapsedTime` | `TimeSpan` | Target update interval for the Win2D game loop. |
| `UseSharedDevice` | `bool` | Whether the control draws with Win2D's process-wide shared `CanvasDevice` (`CanvasAnimatedControl.UseSharedDevice`) instead of its own dedicated device. |

**`IsPaused`**

The Reactor handler intentionally does **not** write Win2D's own `CanvasAnimatedControl.Paused` property: under WinUI 3, toggling that property wakes the game thread for exactly one tick and then permanently parks it (known Win2D-on-WinUI-3 limitation), leaving the canvas frozen with no way to resume on the same control instance. Gating [`Win2DAnimatedCanvasElement.OnUpdate`](#win2danimatedcanvaselement) is a reliable declarative "pause simulation". If your app needs to actually suspend the game thread (e.g. for CPU savings on a backgrounded window), use [`Win2DAnimatedCanvasElement.Setters`](#win2danimatedcanvaselement) to mutate `ctrl.Paused` directly and accept the one-way trip.

**`UseSharedDevice`**

This **must** be `true` when the canvas draws resources created by the `UseCanvasResources` hook (or any other resource built from `CanvasDevice.GetSharedDevice`). Win2D resources are device-affine: drawing a shared-device resource with a control that owns a different device raises a cross-device error that surfaces as a fatal stowed exception. See [the shared-device guidance](/microsoft-ui-reactor/main/controls/win2d-canvas#shared-device). This is a device-construction setting evaluated once when the control mounts; to change it, remount the canvas (e.g. via a different key) rather than toggling it on a live control.

## `Win2DCanvasElement` [#win2dcanvaselement]

Reactor element for Win2D's manual-invalidate `CanvasControl`.

```csharp
public sealed record Win2DCanvasElement : Element
```

Namespace: `Microsoft.UI.Reactor.Advanced.Win2D`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`Win2DCanvas`](/microsoft-ui-reactor/main/reference/factories/win2d#win2dcanvas)

**Modifiers:** [`ClearColor`](/microsoft-ui-reactor/main/reference/modifiers/win2d#clearcolor), [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`UseSharedDevice`](/microsoft-ui-reactor/main/reference/modifiers/win2d#useshareddevice)

| Property | Type | Description |
| --- | --- | --- |
| `ClearColor` | `Color` | Background clear color applied to the underlying `CanvasControl`. |
| `OnCreateResources` | `Func<CanvasControl, Task>?` | Optional asynchronous resource creation callback tracked by Win2D before the first draw. |
| `OnDraw` | `Action<CanvasDrawingSession, CanvasDrawEventArgs>?` | Draw callback invoked on the UI thread when the canvas invalidates. |
| `RedrawKey` | `object?` | Opaque key that triggers `CanvasControl.Invalidate` when the key object changes. |
| `Setters` | `Action<CanvasControl>[]` | Raw Win2D control setters applied after typed properties. |
| `UseSharedDevice` | `bool` | Whether the control draws with Win2D's process-wide shared `CanvasDevice` (`CanvasControl.UseSharedDevice`) instead of its own dedicated device. |

**`UseSharedDevice`**

This **must** be `true` when the canvas draws resources created by the `UseCanvasResources` hook (or any other resource built from `CanvasDevice.GetSharedDevice`). Win2D resources are device-affine: drawing a shared-device resource with a control that owns a different device raises a cross-device error that surfaces as a fatal stowed exception. See [the shared-device guidance](/microsoft-ui-reactor/main/controls/win2d-canvas#shared-device). This is a device-construction setting evaluated once when the control mounts; to change it, remount the canvas (e.g. via a different key) rather than toggling it on a live control.

## `Win2DVirtualCanvasElement` [#win2dvirtualcanvaselement]

Reactor element for Win2D's tiled `CanvasVirtualControl`.

```csharp
public sealed record Win2DVirtualCanvasElement : Element
```

Namespace: `Microsoft.UI.Reactor.Advanced.Win2D`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Element`](/microsoft-ui-reactor/main/reference/elements/Element).

**Created by:** [`Win2DVirtualCanvas`](/microsoft-ui-reactor/main/reference/factories/win2d#win2dvirtualcanvas)

**Modifiers:** [`Set`](/microsoft-ui-reactor/main/reference/modifiers/native-properties#set), [`UseSharedDevice`](/microsoft-ui-reactor/main/reference/modifiers/win2d#useshareddevice)

| Property | Type | Description |
| --- | --- | --- |
| `ContentSize` | `Size` | Logical size of the scrollable virtual surface, in DIPs. |
| `InvalidateRegions` | `IReadOnlyList<Rect>?` | Regions to invalidate when the list instance changes. |
| `OnCreateResources` | `Func<CanvasVirtualControl, Task>?` | Optional asynchronous resource creation callback tracked before region drawing. |
| `OnRegionDraw` | `Action<CanvasDrawingSession, Rect>?` | Callback invoked for each invalidated virtual canvas region. |
| `Setters` | `Action<CanvasVirtualControl>[]` | Raw Win2D control setters applied after typed properties. |
| `UseSharedDevice` | `bool` | Whether the control draws with Win2D's process-wide shared `CanvasDevice` (`CanvasVirtualControl.UseSharedDevice`) instead of its own dedicated device. |

**`UseSharedDevice`**

This **must** be `true` when the canvas draws resources created by the `UseCanvasResources` hook (or any other resource built from `CanvasDevice.GetSharedDevice`). Win2D resources are device-affine: drawing a shared-device resource with a control that owns a different device raises a cross-device error that surfaces as a fatal stowed exception. See [the shared-device guidance](/microsoft-ui-reactor/main/controls/win2d-canvas#shared-device). This is a device-construction setting evaluated once when the control mounts; to change it, remount the canvas (e.g. via a different key) rather than toggling it on a live control.
