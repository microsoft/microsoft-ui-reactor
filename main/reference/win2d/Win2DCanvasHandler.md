---
title: "Win2DCanvasHandler"
seo:
  description: "V1 handler that mounts, updates, and unmounts Win2DCanvasElement instances."
---

V1 handler that mounts, updates, and unmounts [`Win2DCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2dcanvaselement) instances.

```csharp
public sealed class Win2DCanvasHandler : IElementHandler<Win2DCanvasElement, CanvasControl>
```

Namespace: `Microsoft.UI.Reactor.Advanced.Win2D`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Win2D canvas](/microsoft-ui-reactor/main/controls/win2d-canvas)

## Constructors [#constructors]

### `Win2DCanvasHandler` [#win2dcanvashandler_1]

```csharp
public Win2DCanvasHandler()
```

## Methods [#methods]

### `Mount` [#mount]

Mounts a `CanvasControl` and wires Win2D events.

```csharp
public CanvasControl Mount(MountContext ctx, Win2DCanvasElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#mountcontext) |  |
| `el` | [`Win2DCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2dcanvaselement) |  |

### `Unmount` [#unmount]

Removes the Win2D control from the visual tree and returns it through the Reactor control path.

```csharp
public void Unmount(UnmountContext ctx, CanvasControl ctrl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UnmountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#unmountcontext) |  |
| `ctrl` | `CanvasControl` |  |

### `Update` [#update]

Updates manual canvas properties and invalidates when the redraw key changes.

```csharp
public void Update(UpdateContext ctx, Win2DCanvasElement oldEl, Win2DCanvasElement newEl, CanvasControl ctrl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UpdateContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#updatecontext) |  |
| `oldEl` | [`Win2DCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2dcanvaselement) |  |
| `newEl` | [`Win2DCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2dcanvaselement) |  |
| `ctrl` | `CanvasControl` |  |
