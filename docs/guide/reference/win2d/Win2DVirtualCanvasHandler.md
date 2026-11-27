# Win2DVirtualCanvasHandler

V1 handler that mounts, updates, and unmounts [`Win2DVirtualCanvasElement`](../elements/win2d.md#win2dvirtualcanvaselement) instances.

```csharp
public sealed class Win2DVirtualCanvasHandler : IElementHandler<Win2DVirtualCanvasElement, CanvasVirtualControl>
```

Namespace: `Microsoft.UI.Reactor.Advanced.Win2D`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Win2D canvas](../../win2d-canvas.md)

## Constructors

### `Win2DVirtualCanvasHandler`

```csharp
public Win2DVirtualCanvasHandler()
```

## Methods

### `Mount`

Mounts a `CanvasVirtualControl` and wires region invalidation events.

```csharp
public CanvasVirtualControl Mount(MountContext ctx, Win2DVirtualCanvasElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](../extensibility/lifecycle-contexts.md#mountcontext) |  |
| `el` | [`Win2DVirtualCanvasElement`](../elements/win2d.md#win2dvirtualcanvaselement) |  |

### `Unmount`

Removes the Win2D control from the visual tree and returns it through the Reactor control path.

```csharp
public void Unmount(UnmountContext ctx, CanvasVirtualControl ctrl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UnmountContext`](../extensibility/lifecycle-contexts.md#unmountcontext) |  |
| `ctrl` | `CanvasVirtualControl` |  |

### `Update`

Updates virtual content size and invalidates newly requested regions.

```csharp
public void Update(UpdateContext ctx, Win2DVirtualCanvasElement oldEl, Win2DVirtualCanvasElement newEl, CanvasVirtualControl ctrl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UpdateContext`](../extensibility/lifecycle-contexts.md#updatecontext) |  |
| `oldEl` | [`Win2DVirtualCanvasElement`](../elements/win2d.md#win2dvirtualcanvaselement) |  |
| `newEl` | [`Win2DVirtualCanvasElement`](../elements/win2d.md#win2dvirtualcanvaselement) |  |
| `ctrl` | `CanvasVirtualControl` |  |
