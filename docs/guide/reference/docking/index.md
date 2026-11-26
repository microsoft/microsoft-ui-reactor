# Docking

The dock layout model, its events, persistence and the native window interop. From the Microsoft.UI.Reactor.Advanced package.

These types ship in the `Microsoft.UI.Reactor.Advanced` package. The `DockManager` element and its modifiers are in the elements and modifiers categories.

**Learn more:** [Docking Windows](../../docking.md)

## Types

| Type | Description |
| --- | --- |
| [Dock events](events.md) | Event arguments the dock manager raises. |
| [Docking diagnostics](diagnostics.md) | A log of the operations the dock manager performed. |
| [Docking strings](strings.md) | Resource keys and default strings for the docking UI. |
| [Layout model](layout-model.md) | The nodes, panes and targets that make up a dock layout. |
| [Layout persistence](persistence.md) | Save, load and migrate dock layouts. |
| [Native window interop](native.md) | Floating windows, display bounds and the layout snapshots exchanged with native code. |
| [`DockContexts`](DockContexts.md) | Context slots used to surface docking state to function components via the `DockHooks` hooks. |
| [`DockHostModel`](DockHostModel.md) | Internal source-of-truth for a `DockManager` host's runtime layout state. |
| [`DockLayoutOps`](DockLayoutOps.md) | Spec 046 §6.4 — public façade over the layout-tree operations apps need to build, mutate, and inspect a `DockNode` tree programmatically (e.g. an "Open new document" button that should route through role-aware `DockTarget.Center` insert, or a "Close pane" handler that needs the spec-046 reserved-empty rule applied). |
| [`IDockAdapter`](IDockAdapter.md) | App-supplied adapter for two paths the wrapper can't infer from the declarative `DockManager.Layout` alone: content rehydration after layout-JSON restore, and custom floating-window title bar chrome. |
| [`IDockBehavior`](IDockBehavior.md) | App-supplied observation hook for dock and float lifecycle events. |
| [`IDockLayoutMigration`](IDockLayoutMigration.md) | Layout-JSON schema migration step. |
| [`IDockLayoutStrategy`](IDockLayoutStrategy.md) | Insertion-policy hook applied when a pane is added to the layout (programmatic insert, drag-drop landing, JSON restore). |
