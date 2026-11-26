# Control modifiers: Win2D canvas

Modifiers that only the controls in the Win2D canvas area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Win2D canvas](../../win2d-canvas.md)

## `ClearColor`

```csharp
Win2DAnimatedCanvasElement ClearColor(this Win2DAnimatedCanvasElement el, Color color)
Win2DCanvasElement ClearColor(this Win2DCanvasElement el, Color color)
```

Declared in `Win2DCanvasModifiers` (package `Microsoft.UI.Reactor.Advanced`).

| Overload | Description |
| --- | --- |
| `ClearColor(Win2DAnimatedCanvasElement, Color)` | Sets the clear color for an animated Win2D canvas. |
| `ClearColor(Win2DCanvasElement, Color)` | Sets the clear color for a manual Win2D canvas. |

**Applies to:** [`Win2DAnimatedCanvasElement`](../elements/win2d.md#win2danimatedcanvaselement), [`Win2DCanvasElement`](../elements/win2d.md#win2dcanvaselement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `color` | `Color` |  |

## `Paused`

Sets whether an animated Win2D canvas is paused.

```csharp
Win2DAnimatedCanvasElement Paused(this Win2DAnimatedCanvasElement el, bool paused = true)
```

Declared in `Win2DCanvasModifiers` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`Win2DAnimatedCanvasElement`](../elements/win2d.md#win2danimatedcanvaselement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `paused` | `bool` | `true` |  |

## `TargetFps`

Sets the target frame rate for an animated Win2D canvas.

```csharp
Win2DAnimatedCanvasElement TargetFps(this Win2DAnimatedCanvasElement el, double fps)
```

Declared in `Win2DCanvasModifiers` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`Win2DAnimatedCanvasElement`](../elements/win2d.md#win2danimatedcanvaselement).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `fps` | `double` |  |

## `UseSharedDevice`

```csharp
Win2DAnimatedCanvasElement UseSharedDevice(this Win2DAnimatedCanvasElement el, bool useSharedDevice = true)
Win2DCanvasElement UseSharedDevice(this Win2DCanvasElement el, bool useSharedDevice = true)
Win2DVirtualCanvasElement UseSharedDevice(this Win2DVirtualCanvasElement el, bool useSharedDevice = true)
```

Declared in `Win2DCanvasModifiers` (package `Microsoft.UI.Reactor.Advanced`).

| Overload | Description |
| --- | --- |
| `UseSharedDevice(Win2DAnimatedCanvasElement, bool)` | Opts an animated Win2D canvas into Win2D's process-wide shared device. Required when the canvas draws resources created by the `UseCanvasResources` hook. |
| `UseSharedDevice(Win2DCanvasElement, bool)` | Opts a manual Win2D canvas into Win2D's process-wide shared device. Required when the canvas draws resources created by the `UseCanvasResources` hook. |
| `UseSharedDevice(Win2DVirtualCanvasElement, bool)` | Opts a virtual Win2D canvas into Win2D's process-wide shared device. Required when the canvas draws resources created by the `UseCanvasResources` hook. |

**Applies to:** [`Win2DAnimatedCanvasElement`](../elements/win2d.md#win2danimatedcanvaselement), [`Win2DCanvasElement`](../elements/win2d.md#win2dcanvaselement), [`Win2DVirtualCanvasElement`](../elements/win2d.md#win2dvirtualcanvaselement).

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `useSharedDevice` | `bool` | `true` |  |
