---
title: "IDockBehavior"
seo:
  description: "App-supplied observation hook for dock and float lifecycle events. Phase 1 surface; Phase 2 collapses into Action props on DockManager (spec 045 §5.3.5)."
---

App-supplied observation hook for dock and float lifecycle events. Phase 1 surface; Phase 2 collapses into Action props on [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) (spec 045 §5.3.5).

```csharp
public interface IDockBehavior
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

> **Obsolete.** IDockBehavior is collapsing into the per-event Action props on DockManager. OnDocked → OnContentDocked, OnFloating → OnContentFloating/OnContentFloated. Slated for removal one release after Phase 2 ships. See spec 045 §5.3.5.

## Remarks [#remarks]

Spec 045 §4.3. Upstream WinUI.Dock's `ActivateMainWindow` is absorbed by Reactor's window topology and is not exposed here.

Phase 2 keeps this interface as a one-release `[Obsolete]` forwarder so apps that bound their P1 behavior class to a [`DockManager.Behavior`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) assignment continue to compile (spec 045 §5.3.5). Migrate to the per-event Action props on [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager): [`DockManager.OnContentDocked`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) replaces [`IDockBehavior.OnDocked`](#ondocked); [`DockManager.OnContentFloating`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) (cancellable) and [`DockManager.OnContentFloated`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) replace [`IDockBehavior.OnFloating`](#onfloating).

## Methods [#methods]

### `OnDocked` [#ondocked]

Called after a pane is docked (programmatic or drag-in).

```csharp
public void OnDocked(DockableContent src, DockTarget target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `src` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) | The pane being docked. |
| `target` | [`DockTarget`](/microsoft-ui-reactor/main/reference/docking/layout-model#docktarget) | The relative position of the dock landing. |

### `OnFloating` [#onfloating]

Called after a pane is torn out into a floating window.

```csharp
public void OnFloating(DockableContent content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) | The pane being floated. |
