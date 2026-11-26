# IDockBehavior

App-supplied observation hook for dock and float lifecycle events. Phase 1 surface; Phase 2 collapses into Action props on [`DockManager`](../elements/docking.md#dockmanager) (spec 045 §5.3.5).

```csharp
public interface IDockBehavior
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Docking Windows](../../docking.md)

> **Obsolete.** IDockBehavior is collapsing into the per-event Action props on DockManager. OnDocked → OnContentDocked, OnFloating → OnContentFloating/OnContentFloated. Slated for removal one release after Phase 2 ships. See spec 045 §5.3.5.

## Remarks

Spec 045 §4.3. Upstream WinUI.Dock's `ActivateMainWindow` is absorbed by Reactor's window topology and is not exposed here.

Phase 2 keeps this interface as a one-release `[Obsolete]` forwarder so apps that bound their P1 behavior class to a [`DockManager.Behavior`](../elements/docking.md#dockmanager) assignment continue to compile (spec 045 §5.3.5). Migrate to the per-event Action props on [`DockManager`](../elements/docking.md#dockmanager): [`DockManager.OnContentDocked`](../elements/docking.md#dockmanager) replaces [`IDockBehavior.OnDocked`](#ondocked); [`DockManager.OnContentFloating`](../elements/docking.md#dockmanager) (cancellable) and [`DockManager.OnContentFloated`](../elements/docking.md#dockmanager) replace [`IDockBehavior.OnFloating`](#onfloating).

## Methods

### `OnDocked`

Called after a pane is docked (programmatic or drag-in).

```csharp
public void OnDocked(DockableContent src, DockTarget target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `src` | [`DockableContent`](layout-model.md#dockablecontent) | The pane being docked. |
| `target` | [`DockTarget`](layout-model.md#docktarget) | The relative position of the dock landing. |

### `OnFloating`

Called after a pane is torn out into a floating window.

```csharp
public void OnFloating(DockableContent content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | [`DockableContent`](layout-model.md#dockablecontent) | The pane being floated. |
