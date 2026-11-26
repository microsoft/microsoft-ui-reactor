---
title: "Factories: Win2D canvas"
description: "Win2D drawing surfaces, from the Microsoft.UI.Reactor.Advanced package."
sidebar:
  label: "Win2D canvas"
---

Each factory method returns an element record. These are on `Microsoft.UI.Reactor.Advanced.Factories` in the `Microsoft.UI.Reactor.Advanced` package, so add `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Learn more:** [Win2D canvas](/microsoft-ui-reactor/main/controls/win2d-canvas)

## `Win2DAnimatedCanvas` [#win2danimatedcanvas]

Creates an animated Win2D canvas whose update and draw callbacks run on the Win2D game thread.

```csharp
Win2DAnimatedCanvasElement Win2DAnimatedCanvas(Action<CanvasAnimatedUpdateEventArgs, object?> onUpdate, Action<CanvasDrawingSession, CanvasAnimatedDrawEventArgs, object?> onDraw, object? drawState = null, bool isPaused = false)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** [`Win2DAnimatedCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2danimatedcanvaselement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `onUpdate` | `Action<CanvasAnimatedUpdateEventArgs, object?>` |  | Per-tick update callback. |
| `onDraw` | `Action<CanvasDrawingSession, CanvasAnimatedDrawEventArgs, object?>` |  | Per-frame draw callback. |
| `drawState` | `object?` | `null` | Optional user state passed to both callbacks. |
| `isPaused` | `bool` | `false` | Whether the game loop starts paused. |

**Remarks**

See [the Win2D canvas threading guide](/microsoft-ui-reactor/main/controls/win2d-canvas#threading) before touching shared state from the callbacks.

## `Win2DCanvas` [#win2dcanvas]

```csharp
Win2DCanvasElement Win2DCanvas(Action<CanvasDrawingSession, CanvasDrawEventArgs> onDraw, object? redrawKey = null)
Win2DCanvasElement Win2DCanvas(Action<CanvasDrawingSession, CanvasDrawEventArgs> onDraw, Func<CanvasControl, Task> onCreateResources, object? redrawKey = null)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

| Overload | Description |
| --- | --- |
| `Win2DCanvas(Action<CanvasDrawingSession, CanvasDrawEventArgs>, object?)` | Creates a manual Win2D canvas that redraws when `redrawKey` changes. |
| `Win2DCanvas(Action<CanvasDrawingSession, CanvasDrawEventArgs>, Func<CanvasControl, Task>, object?)` | Creates a manual Win2D canvas with an asynchronous resource creation callback. |

**Returns:** [`Win2DCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2dcanvaselement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `onDraw` | `Action<CanvasDrawingSession, CanvasDrawEventArgs>` |  | UI-thread draw callback. |
| `redrawKey` | `object?` | `null` | Opaque key used to trigger canvas invalidation. |
| `onCreateResources` | `Func<CanvasControl, Task>` |  | Resource creation callback tracked by Win2D. |

## `Win2DVirtualCanvas` [#win2dvirtualcanvas]

Creates a virtual Win2D canvas with the specified logical content size.

```csharp
Win2DVirtualCanvasElement Win2DVirtualCanvas(Action<CanvasDrawingSession, Rect> onRegionDraw, Size contentSize)
```

Declared in `Factories` (package `Microsoft.UI.Reactor.Advanced`). Call it through `using static Microsoft.UI.Reactor.Advanced.Factories;`.

**Returns:** [`Win2DVirtualCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2dvirtualcanvaselement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `onRegionDraw` | `Action<CanvasDrawingSession, Rect>` | Region draw callback invoked for invalidated tiles. |
| `contentSize` | `Size` | Logical size of the virtual surface, in DIPs. |
