---
title: "Win2DVirtualCanvasHandler"
seo:
  description: "V1 handler that mounts, updates, and unmounts Win2DVirtualCanvasElement instances."
---

V1 handler that mounts, updates, and unmounts [`Win2DVirtualCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2dvirtualcanvaselement) instances.

```csharp
public sealed class Win2DVirtualCanvasHandler : IElementHandler<Win2DVirtualCanvasElement, CanvasVirtualControl>
```

Namespace: `Microsoft.UI.Reactor.Advanced.Win2D`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Win2D canvas](/microsoft-ui-reactor/main/controls/win2d-canvas)

## Constructors [#constructors]

### `Win2DVirtualCanvasHandler` [#win2dvirtualcanvashandler_1]

```csharp
public Win2DVirtualCanvasHandler()
```

## Methods [#methods]

### `Mount` [#mount]

Mounts a `CanvasVirtualControl` and wires region invalidation events.

```csharp
public CanvasVirtualControl Mount(MountContext ctx, Win2DVirtualCanvasElement el)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`MountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#mountcontext) |  |
| `el` | [`Win2DVirtualCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2dvirtualcanvaselement) |  |

### `Unmount` [#unmount]

Removes the Win2D control from the visual tree and returns it through the Reactor control path.

```csharp
public void Unmount(UnmountContext ctx, CanvasVirtualControl ctrl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UnmountContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#unmountcontext) |  |
| `ctrl` | `CanvasVirtualControl` |  |

### `Update` [#update]

Updates virtual content size and invalidates newly requested regions.

```csharp
public void Update(UpdateContext ctx, Win2DVirtualCanvasElement oldEl, Win2DVirtualCanvasElement newEl, CanvasVirtualControl ctrl)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ctx` | [`UpdateContext`](/microsoft-ui-reactor/main/reference/extensibility/lifecycle-contexts#updatecontext) |  |
| `oldEl` | [`Win2DVirtualCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2dvirtualcanvaselement) |  |
| `newEl` | [`Win2DVirtualCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2dvirtualcanvaselement) |  |
| `ctrl` | `CanvasVirtualControl` |  |
