# IDockLayoutStrategy

Insertion-policy hook applied when a pane is added to the layout (programmatic insert, drag-drop landing, JSON restore). Apps register a strategy on [`DockManager.LayoutStrategy`](../elements/docking.md#dockmanager).

```csharp
public interface IDockLayoutStrategy
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Docking Windows](../../docking.md)

## Remarks

Spec 045 §5.3.6 (Phase 2 addition).

[`IDockLayoutStrategy.BeforeInsertDocument`](#beforeinsertdocument) / [`IDockLayoutStrategy.BeforeInsertToolWindow`](#beforeinserttoolwindow) returning `true` short-circuits the default insertion (the strategy performed the placement itself via the model handle); returning `false` lets the manager proceed with its default routing. [`IDockLayoutStrategy.AfterInsertDocument`](#afterinsertdocument) / [`IDockLayoutStrategy.AfterInsertToolWindow`](#afterinserttoolwindow) run after the placement and are the chance to set dimensions, pin to a side, or activate the pane.

Strategies receive a [`DockHostModel`](DockHostModel.md) mutable handle — not the immutable [`DockNode`](layout-model.md#docknode) tree. This is the same model instance the reconciler reads from (spec 045 §5.3.10).

Example: route any tool window whose title starts with "Error" to the bottom side, height 180:

```csharp
class ErrorPaneStrategy : IDockLayoutStrategy {
    public bool BeforeInsertToolWindow(DockHostModel model, ToolWindow tw) {
        if (tw.Title.StartsWith("Error", StringComparison.Ordinal)) {
            model.PinToSide(tw, DockSide.Bottom);
            return true; // we placed it; skip default insertion
        }
        return false;
    }
    // … other members no-op …
}
```

## Methods

### `AfterInsertDocument`

Post-insert hook for documents — runs after default routing.

```csharp
public void AfterInsertDocument(DockHostModel model, Document document)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `model` | [`DockHostModel`](DockHostModel.md) |  |
| `document` | [`Document`](layout-model.md#document) |  |

### `AfterInsertToolWindow`

Post-insert hook for tool windows — runs after default routing.

```csharp
public void AfterInsertToolWindow(DockHostModel model, ToolWindow toolWindow)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `model` | [`DockHostModel`](DockHostModel.md) |  |
| `toolWindow` | [`ToolWindow`](layout-model.md#toolwindow) |  |

### `BeforeInsertDocument`

Pre-insert hook for documents. Return `true` if the strategy performed placement; `false` to let the manager proceed with default insertion.

```csharp
public bool BeforeInsertDocument(DockHostModel model, Document document)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `model` | [`DockHostModel`](DockHostModel.md) |  |
| `document` | [`Document`](layout-model.md#document) |  |

### `BeforeInsertToolWindow`

Pre-insert hook for tool windows. Return `true` if the strategy performed placement; `false` to let the manager proceed with default insertion.

```csharp
public bool BeforeInsertToolWindow(DockHostModel model, ToolWindow toolWindow)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `model` | [`DockHostModel`](DockHostModel.md) |  |
| `toolWindow` | [`ToolWindow`](layout-model.md#toolwindow) |  |
