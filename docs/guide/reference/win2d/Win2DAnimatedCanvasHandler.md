# Win2DAnimatedCanvasHandler

V1 handler that mounts, updates, and unmounts [`Win2DAnimatedCanvasElement`](../elements/win2d.md#win2danimatedcanvaselement) instances.

```csharp
public sealed class Win2DAnimatedCanvasHandler : IElementHandler<Win2DAnimatedCanvasElement, CanvasAnimatedControl>
```

Namespace: `Microsoft.UI.Reactor.Advanced.Win2D`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Win2D canvas](../../win2d-canvas.md)

## Remarks

Win2D raises `CanvasAnimatedControl.Update` and `CanvasAnimatedControl.Draw` on its game-loop thread. Per spec §8.1, the game-thread callbacks are isolated from Reactor's UI-thread element-tag/state store: they read only a volatile holder updated by [`Win2DAnimatedCanvasHandler.Update`](#update) and must not call `Reconciler.GetElementTag`, `ReactorAttached.StateProperty`, `bind.OnCustomEvent`, or any DependencyProperty API. `CanvasAnimatedControl.CreateResources` remains UI-thread per Win2D docs.

## Constructors

### `Win2DAnimatedCanvasHandler`

```csharp
public Win2DAnimatedCanvasHandler()
```

## Methods

### `Mount`

Mounts a `CanvasAnimatedControl` and wires Win2D events.

```csharp
public CanvasAnimatedControl Mount(MountContext ctx, Win2DAnimatedCanvasElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](../extensibility/lifecycle-contexts.md#mountcontext) |  |
| `el` | [`Win2DAnimatedCanvasElement`](../elements/win2d.md#win2danimatedcanvaselement) |  |

### `Unmount`

Removes the Win2D control from the visual tree and returns it through the Reactor control path.

```csharp
public void Unmount(UnmountContext ctx, CanvasAnimatedControl ctrl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UnmountContext`](../extensibility/lifecycle-contexts.md#unmountcontext) |  |
| `ctrl` | `CanvasAnimatedControl` |  |

### `Update`

Updates animated canvas state while preserving the active Win2D game loop.

```csharp
public void Update(UpdateContext ctx, Win2DAnimatedCanvasElement oldEl, Win2DAnimatedCanvasElement newEl, CanvasAnimatedControl ctrl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UpdateContext`](../extensibility/lifecycle-contexts.md#updatecontext) |  |
| `oldEl` | [`Win2DAnimatedCanvasElement`](../elements/win2d.md#win2danimatedcanvaselement) |  |
| `newEl` | [`Win2DAnimatedCanvasElement`](../elements/win2d.md#win2danimatedcanvaselement) |  |
| `ctrl` | `CanvasAnimatedControl` |  |
