# Win2DCanvasHandler

V1 handler that mounts, updates, and unmounts [`Win2DCanvasElement`](../elements/win2d.md#win2dcanvaselement) instances.

```csharp
public sealed class Win2DCanvasHandler : IElementHandler<Win2DCanvasElement, CanvasControl>
```

Namespace: `Microsoft.UI.Reactor.Advanced.Win2D`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Win2D canvas](../../win2d-canvas.md)

## Constructors

### `Win2DCanvasHandler`

```csharp
public Win2DCanvasHandler()
```

## Methods

### `Mount`

Mounts a `CanvasControl` and wires Win2D events.

```csharp
public CanvasControl Mount(MountContext ctx, Win2DCanvasElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](../extensibility/lifecycle-contexts.md#mountcontext) |  |
| `el` | [`Win2DCanvasElement`](../elements/win2d.md#win2dcanvaselement) |  |

### `Unmount`

Removes the Win2D control from the visual tree and returns it through the Reactor control path.

```csharp
public void Unmount(UnmountContext ctx, CanvasControl ctrl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UnmountContext`](../extensibility/lifecycle-contexts.md#unmountcontext) |  |
| `ctrl` | `CanvasControl` |  |

### `Update`

Updates manual canvas properties and invalidates when the redraw key changes.

```csharp
public void Update(UpdateContext ctx, Win2DCanvasElement oldEl, Win2DCanvasElement newEl, CanvasControl ctrl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UpdateContext`](../extensibility/lifecycle-contexts.md#updatecontext) |  |
| `oldEl` | [`Win2DCanvasElement`](../elements/win2d.md#win2dcanvaselement) |  |
| `newEl` | [`Win2DCanvasElement`](../elements/win2d.md#win2dcanvaselement) |  |
| `ctrl` | `CanvasControl` |  |
