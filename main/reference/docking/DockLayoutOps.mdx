---
title: "DockLayoutOps"
seo:
  description: "Spec 046 §6.4 — public façade over the layout-tree operations apps need to build, mutate, and inspect a DockNode tree programmatically (e.g. an \"Open new document\" button that should route through role-aware DockTarget.Center insert, or a \"Close pane\" handler that needs the spec-046 reserved-empty rule applied)."
---

Spec 046 §6.4 — public façade over the layout-tree operations apps need to build, mutate, and inspect a [`DockNode`](/microsoft-ui-reactor/main/reference/docking/layout-model#docknode) tree programmatically (e.g. an "Open new document" button that should route through role-aware [`DockTarget.Center`](/microsoft-ui-reactor/main/reference/docking/layout-model#values_4) insert, or a "Close pane" handler that needs the spec-046 reserved-empty rule applied).

```csharp
public static class DockLayoutOps
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

## Remarks [#remarks]

Pure functional helpers over the public [`DockNode`](/microsoft-ui-reactor/main/reference/docking/layout-model#docknode) records — no host, no model, no UI-thread affinity. Apps typically keep the live layout in state (`UseState<DockNode?>`) and either wire [`DockManager.OnLiveLayoutChanged`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) for drag-driven mutations or call these helpers from app-driven mutations (button clicks, menu commands).

The internal `DockLayoutMutator` class wraps a larger set of drag-drop infrastructure operations (group-targeted inserts, shape- only override, container lookup by reference). Those aren't part of the supported app-facing API surface — [`DockLayoutOps`](/microsoft-ui-reactor/main/reference/docking/DockLayoutOps) is the curated subset apps should depend on.

## Methods [#methods]

### `FindContainer` [#findcontainer]

Walk the tree to find the immediate container (a [`DockTabGroup`](/microsoft-ui-reactor/main/reference/docking/layout-model#docktabgroup), or the pane itself when it's the root leaf) holding `pane`. Returns null when the pane isn't in the tree. Useful for "where does this pane live right now" lookups when the user-driven layout has shifted.

```csharp
public static DockNode? FindContainer(DockNode? root, DockableContent pane)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | [`DockNode?`](/microsoft-ui-reactor/main/reference/docking/layout-model#docknode) |  |
| `pane` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) |  |

### `InsertPaneAtTarget` [#insertpaneattarget]

#### `InsertPaneAtTarget(DockNode?, DockableContent, DockTarget)` [#insertpaneattargetdocknode-dockablecontent-docktarget]

Place `pane` into `root` at the chosen `target`. For [`DockTarget.Center`](/microsoft-ui-reactor/main/reference/docking/layout-model#values_4), this is the spec-046 role-aware insert: the pane lands in the first group whose [`DockTabGroup.Role`](/microsoft-ui-reactor/main/reference/docking/layout-model#role) prefers the payload category ([`Document`](/microsoft-ui-reactor/main/reference/docking/layout-model#document) ↔ [`DockGroupRole.DocumentArea`](/microsoft-ui-reactor/main/reference/docking/layout-model#values); [`ToolWindow`](/microsoft-ui-reactor/main/reference/docking/layout-model#toolwindow) ↔ [`DockGroupRole.ToolWindowStrip`](/microsoft-ui-reactor/main/reference/docking/layout-model#values)), falling back to the first group that accepts the category, falling back finally to the leftmost-descendant heuristic.

```csharp
public static DockNode InsertPaneAtTarget(DockNode? root, DockableContent pane, DockTarget target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | [`DockNode?`](/microsoft-ui-reactor/main/reference/docking/layout-model#docknode) |  |
| `pane` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) |  |
| `target` | [`DockTarget`](/microsoft-ui-reactor/main/reference/docking/layout-model#docktarget) |  |

**Remarks**

For split / edge targets (Split\*, Dock\*), the new sibling group inherits a role inferred from the pane's category and the target's edge meaning (spec 046 §2.3): a [`Document`](/microsoft-ui-reactor/main/reference/docking/layout-model#document) payload's wrapper is [`DockGroupRole.DocumentArea`](/microsoft-ui-reactor/main/reference/docking/layout-model#values); a [`ToolWindow`](/microsoft-ui-reactor/main/reference/docking/layout-model#toolwindow) at a Dock\* edge becomes [`DockGroupRole.ToolWindowStrip`](/microsoft-ui-reactor/main/reference/docking/layout-model#values). When the root is a single [`DockTabGroup`](/microsoft-ui-reactor/main/reference/docking/layout-model#docktabgroup) whose role matches the pane's preference, the role propagates to the new sibling so "Document inside DocumentArea split right" produces two DocumentArea groups, not one DocumentArea + one General.

#### `InsertPaneAtTarget(DockNode?, DockableContent, DockTarget, DockRoutingFallback?)` [#insertpaneattargetdocknode-dockablecontent-docktarget-dockroutingfallback]

Spec 046 §6.3 — overload that surfaces a routing-fallback diagnostic when no group's [`DockTabGroup.Role`](/microsoft-ui-reactor/main/reference/docking/layout-model#role) accepts the payload's category and the insert degraded to the pre-046 "leftmost descendant" heuristic. `fallback` is non-null only in that degraded case; apps can route it into their own diagnostics surface (e.g. a status-bar warning or an `OperationLog` entry).

```csharp
public static DockNode InsertPaneAtTarget(DockNode? root, DockableContent pane, DockTarget target, out DockRoutingFallback? fallback)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | [`DockNode?`](/microsoft-ui-reactor/main/reference/docking/layout-model#docknode) |  |
| `pane` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) |  |
| `target` | [`DockTarget`](/microsoft-ui-reactor/main/reference/docking/layout-model#docktarget) |  |
| `fallback` | [`DockRoutingFallback?`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockroutingfallback) |  |

### `MovePaneToTarget` [#movepanetotarget]

Convenience: remove `pane` from its current location, then re-insert at `target`. Used by "move this doc to a new well" commands. When the pane wasn't found, the original tree is returned unchanged.

```csharp
public static DockNode? MovePaneToTarget(DockNode? root, DockableContent pane, DockTarget target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | [`DockNode?`](/microsoft-ui-reactor/main/reference/docking/layout-model#docknode) |  |
| `pane` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) |  |
| `target` | [`DockTarget`](/microsoft-ui-reactor/main/reference/docking/layout-model#docktarget) |  |

### `RemovePane` [#removepane]

Remove `pane` from `root`. Returns the post-remove tree (or null when the tree collapsed to nothing) and a flag indicating whether the pane was found. Spec 046 §6.5: a [`DockGroupRole.DocumentArea`](/microsoft-ui-reactor/main/reference/docking/layout-model#values) group whose last document is removed survives as an empty reserved well.

```csharp
public static (DockNode? Root, bool Found) RemovePane(DockNode? root, DockableContent pane)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | [`DockNode?`](/microsoft-ui-reactor/main/reference/docking/layout-model#docknode) |  |
| `pane` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) |  |

**Remarks**

Identity follows the spec 045 §2.4 rule: `ReferenceEquals` is the fast path, with key equality as a fallback so app code that holds a reference captured at one render can still match against a later render's rebuilt record.
