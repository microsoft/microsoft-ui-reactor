---
title: "IDockAdapter"
seo:
  description: "App-supplied adapter for two paths the wrapper can't infer from the declarative DockManager.Layout alone: content rehydration after layout-JSON restore, and custom floating-window title bar chrome."
---

App-supplied adapter for two paths the wrapper can't infer from the declarative [`DockManager.Layout`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) alone: content rehydration after layout-JSON restore, and custom floating-window title bar chrome.

```csharp
public interface IDockAdapter
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

## Remarks [#remarks]

Spec 045 §4.3. In Phase 2 the adapter surface collapses into per-event Action props on `DockManager` (§5.3.5); the interface stays as a `[Obsolete]` forwarder for one release.

## Methods [#methods]

### `GetFloatingWindowTitleBar` [#getfloatingwindowtitlebar]

Returns an optional Reactor element to render as the title bar of a freshly-created floating window. Return null to use the default.

```csharp
public Element? GetFloatingWindowTitleBar(DockableContent? draggedSource)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `draggedSource` | [`DockableContent?`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) | The pane whose tear-out spawned the floating window, or null if the window was restored from layout JSON. |

### `OnContentCreated` [#oncontentcreated]

Called when the wrapper instantiates a pane from persisted layout JSON. Apps return the Reactor [`Element`](/microsoft-ui-reactor/main/reference/elements/Element) subtree to mount as the pane's content. Return null to leave content empty (the pane will render its [`DockableContent.Title`](/microsoft-ui-reactor/main/reference/docking/layout-model#title_1) but no body).

```csharp
public Element? OnContentCreated(DockableContent content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) | The reconstituted pane, including its [`DockableContent.Key`](/microsoft-ui-reactor/main/reference/docking/layout-model#key_1) and [`DockableContent.PersistenceState`](/microsoft-ui-reactor/main/reference/docking/layout-model#persistencestate). Match by `Key`. |

### `OnGroupCreated` [#ongroupcreated]

Called when the manager creates a new `DocumentGroup` at the tail end of a tear-out drag. Apps may use this to wire group-level chrome (e.g., a custom tab-strip toolbar). Read the dragged source (when applicable) from [`DockTabGroupContext.DraggedSource`](/microsoft-ui-reactor/main/reference/docking/layout-model#draggedsource).

```csharp
public void OnGroupCreated(DockTabGroupContext group)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `group` | [`DockTabGroupContext`](/microsoft-ui-reactor/main/reference/docking/layout-model#docktabgroupcontext) | The freshly-created group's context. |
