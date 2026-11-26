# IDockAdapter

App-supplied adapter for two paths the wrapper can't infer from the declarative [`DockManager.Layout`](../elements/docking.md#dockmanager) alone: content rehydration after layout-JSON restore, and custom floating-window title bar chrome.

```csharp
public interface IDockAdapter
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Docking Windows](../../docking.md)

## Remarks

Spec 045 §4.3. In Phase 2 the adapter surface collapses into per-event Action props on `DockManager` (§5.3.5); the interface stays as a `[Obsolete]` forwarder for one release.

## Methods

### `GetFloatingWindowTitleBar`

Returns an optional Reactor element to render as the title bar of a freshly-created floating window. Return null to use the default.

```csharp
public Element? GetFloatingWindowTitleBar(DockableContent? draggedSource)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `draggedSource` | [`DockableContent?`](layout-model.md#dockablecontent) | The pane whose tear-out spawned the floating window, or null if the window was restored from layout JSON. |

### `OnContentCreated`

Called when the wrapper instantiates a pane from persisted layout JSON. Apps return the Reactor [`Element`](../elements/Element.md) subtree to mount as the pane's content. Return null to leave content empty (the pane will render its [`DockableContent.Title`](layout-model.md#title_1) but no body).

```csharp
public Element? OnContentCreated(DockableContent content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | [`DockableContent`](layout-model.md#dockablecontent) | The reconstituted pane, including its [`DockableContent.Key`](layout-model.md#key_1) and [`DockableContent.PersistenceState`](layout-model.md#persistencestate). Match by `Key`. |

### `OnGroupCreated`

Called when the manager creates a new `DocumentGroup` at the tail end of a tear-out drag. Apps may use this to wire group-level chrome (e.g., a custom tab-strip toolbar). Read the dragged source (when applicable) from [`DockTabGroupContext.DraggedSource`](layout-model.md#draggedsource).

```csharp
public void OnGroupCreated(DockTabGroupContext group)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `group` | [`DockTabGroupContext`](layout-model.md#docktabgroupcontext) | The freshly-created group's context. |
