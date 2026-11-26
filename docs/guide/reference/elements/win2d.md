# Elements: Win2D canvas

Win2D drawing surfaces, from the Microsoft.UI.Reactor.Advanced package.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Win2D canvas](../../win2d-canvas.md)

## `Win2DAnimatedCanvasElement`

Reactor element for Win2D's game-loop `CanvasAnimatedControl`.

```csharp
public sealed record Win2DAnimatedCanvasElement : Element
```

Namespace: `Microsoft.UI.Reactor.Advanced.Win2D`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Element`](Element.md).

**Created by:** [`Win2DAnimatedCanvas`](../factories/win2d.md#win2danimatedcanvas)

**Modifiers:** [`ClearColor`](../modifiers/win2d.md#clearcolor), [`Paused`](../modifiers/win2d.md#paused), [`Set`](../modifiers/native-properties.md#set), [`TargetFps`](../modifiers/win2d.md#targetfps), [`UseSharedDevice`](../modifiers/win2d.md#useshareddevice)

| Property | Type | Description |
| --- | --- | --- |
| `ClearColor` | `Color` | Background clear color applied to the underlying `CanvasAnimatedControl`. |
| `DrawState` | `object?` | Optional user state passed to [`Win2DAnimatedCanvasElement.OnUpdate`](#win2danimatedcanvaselement) and [`Win2DAnimatedCanvasElement.OnDraw`](#win2danimatedcanvaselement). |
| `IsPaused` | `bool` | Whether the canvas simulation is paused without unmounting the control. When `true`, [`Win2DAnimatedCanvasElement.OnUpdate`](#win2danimatedcanvaselement) is skipped (no state advancement) but [`Win2DAnimatedCanvasElement.OnDraw`](#win2danimatedcanvaselement) continues to fire each tick so the last frame stays visible. The underlying Win2D game loop also continues to tick (~16 ms of empty work per pause-second). |
| `OnCreateResources` | `Func<CanvasAnimatedControl, Task>?` | Optional asynchronous resource creation callback tracked before the animated loop starts. |
| `OnDraw` | `Action<CanvasDrawingSession, CanvasAnimatedDrawEventArgs, object?>?` | Per-frame draw callback. Runs on the Win2D game thread; see [the Win2D canvas threading guide](../../win2d-canvas.md#threading). |
| `OnUpdate` | `Action<CanvasAnimatedUpdateEventArgs, object?>?` | Per-tick update callback. Runs on the Win2D game thread; see [the Win2D canvas threading guide](../../win2d-canvas.md#threading). |
| `Setters` | `Action<CanvasAnimatedControl>[]` | Raw Win2D control setters applied after typed properties. |
| `TargetElapsedTime` | `TimeSpan` | Target update interval for the Win2D game loop. |
| `UseSharedDevice` | `bool` | Whether the control draws with Win2D's process-wide shared `CanvasDevice` (`CanvasAnimatedControl.UseSharedDevice`) instead of its own dedicated device. |

## `Win2DCanvasElement`

Reactor element for Win2D's manual-invalidate `CanvasControl`.

```csharp
public sealed record Win2DCanvasElement : Element
```

Namespace: `Microsoft.UI.Reactor.Advanced.Win2D`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Element`](Element.md).

**Created by:** [`Win2DCanvas`](../factories/win2d.md#win2dcanvas)

**Modifiers:** [`ClearColor`](../modifiers/win2d.md#clearcolor), [`Set`](../modifiers/native-properties.md#set), [`UseSharedDevice`](../modifiers/win2d.md#useshareddevice)

| Property | Type | Description |
| --- | --- | --- |
| `ClearColor` | `Color` | Background clear color applied to the underlying `CanvasControl`. |
| `OnCreateResources` | `Func<CanvasControl, Task>?` | Optional asynchronous resource creation callback tracked by Win2D before the first draw. |
| `OnDraw` | `Action<CanvasDrawingSession, CanvasDrawEventArgs>?` | Draw callback invoked on the UI thread when the canvas invalidates. |
| `RedrawKey` | `object?` | Opaque key that triggers `CanvasControl.Invalidate` when the key object changes. |
| `Setters` | `Action<CanvasControl>[]` | Raw Win2D control setters applied after typed properties. |
| `UseSharedDevice` | `bool` | Whether the control draws with Win2D's process-wide shared `CanvasDevice` (`CanvasControl.UseSharedDevice`) instead of its own dedicated device. |

## `Win2DVirtualCanvasElement`

Reactor element for Win2D's tiled `CanvasVirtualControl`.

```csharp
public sealed record Win2DVirtualCanvasElement : Element
```

Namespace: `Microsoft.UI.Reactor.Advanced.Win2D`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Element`](Element.md).

**Created by:** [`Win2DVirtualCanvas`](../factories/win2d.md#win2dvirtualcanvas)

**Modifiers:** [`Set`](../modifiers/native-properties.md#set), [`UseSharedDevice`](../modifiers/win2d.md#useshareddevice)

| Property | Type | Description |
| --- | --- | --- |
| `ContentSize` | `Size` | Logical size of the scrollable virtual surface, in DIPs. |
| `InvalidateRegions` | `IReadOnlyList<Rect>?` | Regions to invalidate when the list instance changes. |
| `OnCreateResources` | `Func<CanvasVirtualControl, Task>?` | Optional asynchronous resource creation callback tracked before region drawing. |
| `OnRegionDraw` | `Action<CanvasDrawingSession, Rect>?` | Callback invoked for each invalidated virtual canvas region. |
| `Setters` | `Action<CanvasVirtualControl>[]` | Raw Win2D control setters applied after typed properties. |
| `UseSharedDevice` | `bool` | Whether the control draws with Win2D's process-wide shared `CanvasDevice` (`CanvasVirtualControl.UseSharedDevice`) instead of its own dedicated device. |
