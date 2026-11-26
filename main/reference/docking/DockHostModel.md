---
title: "DockHostModel"
seo:
  description: "Internal source-of-truth for a DockManager host's runtime layout state. Mutations land here first and re-render the reconciled element tree; the declarative DockManager.Layout prop is the controlled input, the model is the live state, and OnLayoutChanged closes the loop (spec 045 §5.3.10)."
---

Internal source-of-truth for a [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) host's runtime layout state. Mutations land here first and re-render the reconciled element tree; the declarative [`DockManager.Layout`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) prop is the controlled input, the model is the live state, and `OnLayoutChanged` closes the loop (spec 045 §5.3.10).

```csharp
public sealed class DockHostModel
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

## Remarks [#remarks]

Spec 045 §5.3.10 / tracking §2.16 (Phase 2).

**UI-thread-affined.** All mutators must run on the dispatcher that owns the host element. Off-thread access throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) at the boundary; the contract is documented, not enforced with locks (spec 045 §8.10).

**Not a parallel writable surface.** Apps interact via the controlled [`DockManager.Layout`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) prop plus the round-trip events on [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager). The model surface is exposed to [`IDockLayoutStrategy`](/microsoft-ui-reactor/main/reference/docking/IDockLayoutStrategy) and devtools (spec 045 §8.2) — not pane-content components.

## Constructors [#constructors]

### `DockHostModel` [#dockhostmodel_1]

Constructs a host model captured on the current thread.

```csharp
public DockHostModel()
```

## Properties [#properties]

### `ActiveContent` [#activecontent]

The currently-active content, or null if none.

```csharp
public DockableContent? ActiveContent  // get
```

### `BottomSide` [#bottomside]

Tool windows currently pinned to the bottom side strip.

```csharp
public IReadOnlyList<ToolWindow> BottomSide  // get
```

### `Floating` [#floating]

Floating-window state for torn-out panes. Populated by the host renderer from the docking subsystem's per-manager floating-window tracker, so live snapshots include what's currently floating.

```csharp
public IReadOnlyList<FloatingDockWindow> Floating  // get
```

**Remarks**

Each entry carries the pane + best-effort bounds: today the dimensions reflect the spec values captured when the window opened, and `X`/`Y` are `0`. Live position / size tracking is a §2.6 follow-up — callers that depend on current bounds should read off the underlying window rather than trusting this snapshot.

### `LayoutStrategy` [#layoutstrategy]

Insertion-policy hook supplied by the owning [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) (mirrored from [`DockManager.LayoutStrategy`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) each render). The model dispatches into [`IDockLayoutStrategy.BeforeInsertDocument`](/microsoft-ui-reactor/main/reference/docking/IDockLayoutStrategy#beforeinsertdocument) / [`IDockLayoutStrategy.BeforeInsertToolWindow`](/microsoft-ui-reactor/main/reference/docking/IDockLayoutStrategy#beforeinserttoolwindow) on programmatic [`DockHostModel.Dock`](#dockdockablecontent-docktarget); a `true` return short-circuits the default insertion.

```csharp
public IDockLayoutStrategy? LayoutStrategy  // get
```

**Remarks**

Spec 045 §5.3.6 / tracking §2.13.

### `LeftSide` [#leftside]

Tool windows currently pinned to the left side strip.

```csharp
public IReadOnlyList<ToolWindow> LeftSide  // get
```

### `RightSide` [#rightside]

Tool windows currently pinned to the right side strip.

```csharp
public IReadOnlyList<ToolWindow> RightSide  // get
```

### `Root` [#root]

The root of the docked layout tree (excludes side and floating panes).

```csharp
public DockNode? Root  // get
```

### `TopSide` [#topside]

Tool windows currently pinned to the top side strip.

```csharp
public IReadOnlyList<ToolWindow> TopSide  // get
```

## Methods [#methods]

### `Activate` [#activate]

Activates (focuses) `content`.

```csharp
public void Activate(DockableContent content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) |  |

### `AllContent` [#allcontent]

Enumerates every pane in the model (docked, side, floating). Order is unspecified.

```csharp
public IEnumerable<DockableContent> AllContent()
```

### `Close` [#close]

Closes `content`. For ToolWindows with `CanHide`, hides instead.

```csharp
public void Close(DockableContent content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) |  |

### `Descendants` [#descendants]

Walks the [`DockHostModel.Root`](#root) tree depth-first, yielding every [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) leaf.

```csharp
public IEnumerable<DockableContent> Descendants()
```

### `Dock` [#dock]

#### `Dock(DockableContent, DockTarget)` [#dockdockablecontent-docktarget]

Programmatically docks `content` at `target`.

```csharp
public void Dock(DockableContent content, DockTarget target)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) |  |
| `target` | [`DockTarget`](/microsoft-ui-reactor/main/reference/docking/layout-model#docktarget) |  |

**Remarks**

Spec 045 §5.3.10.

When [`DockHostModel.LayoutStrategy`](#layoutstrategy) is supplied (§5.3.6 / tracking §2.13), the strategy's `BeforeInsert*` hook runs first. Returning `true` short-circuits the default insertion — the strategy is responsible for whatever placement it performed via the model surface. Returning `false` queues the default `DockOp` and then fires the `AfterInsert*` hook so apps can adjust dimensions, activate the pane, or pin to a side after the manager's routing.

#### `Dock(DockableContent, DockTabGroup, DockTarget)` [#dockdockablecontent-docktabgroup-docktarget]

Spec 046 §6.4 — group-targeted dock. Inserts `content` into the specific `targetGroup` at `target`. `targetGroup` must come from the current layout snapshot (e.g. captured at layout-build time, or resolved from [`DockHostModel.Root`](#root)); resolution falls back to record equality when reference identity has decayed across a render.

```csharp
public void Dock(DockableContent content, DockTabGroup targetGroup, DockTarget target = DockTarget.Center)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `content` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) |  |  |
| `targetGroup` | [`DockTabGroup`](/microsoft-ui-reactor/main/reference/docking/layout-model#docktabgroup) |  |  |
| `target` | [`DockTarget`](/microsoft-ui-reactor/main/reference/docking/layout-model#docktarget) | `DockTarget.Center` |  |

**Remarks**

This is the explicit-control escape hatch for placements the role/category routing in [`DockHostModel.Dock`](#dockdockablecontent-docktarget) can't express. The overload is *trusted*: it does NOT consult [`DockTabGroup.Role`](/microsoft-ui-reactor/main/reference/docking/layout-model#role) compatibility (per spec §9 Q3) — the caller is taking responsibility for the placement. Pass through [`DockHostModel.LayoutStrategy`](#layoutstrategy) hooks is also skipped — strategies that want to redirect should override `Dock(content, target)` via `BeforeInsert*`, possibly calling this overload from inside.

If `targetGroup` can't be resolved against the current layout (e.g. the caller passed a stale group), the operation is a no-op and a `Note`-kind diagnostic is queued to the `DockOperationLog` — no exception thrown, matching the model's best-effort feel.

### `Float` [#float]

Tears `content` out into a new floating window.

```csharp
public void Float(DockableContent content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) |  |

### `Hide` [#hide]

Auto-hides `toolWindow` to its remembered side strip.

```csharp
public void Hide(ToolWindow toolWindow)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `toolWindow` | [`ToolWindow`](/microsoft-ui-reactor/main/reference/docking/layout-model#toolwindow) |  |

### `PinToSide` [#pintoside]

Pins `toolWindow` to `side`.

```csharp
public void PinToSide(ToolWindow toolWindow, DockSide side)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `toolWindow` | [`ToolWindow`](/microsoft-ui-reactor/main/reference/docking/layout-model#toolwindow) |  |
| `side` | [`DockSide`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockside) |  |

**Remarks**

Spec 045 §5.3.6 — strategies use this to route by category.

Spec 046 §6.6 / §9 Q4 — throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) when `side` is not in `toolWindow`'s [`ToolWindow.AllowedSides`](/microsoft-ui-reactor/main/reference/docking/layout-model#allowedsides) mask. The exception message includes the tool window's title/key and the mask, for diagnostics. Strategies that need to bypass the mask should clear it before calling — `toolWindow with { AllowedSides = DockSides.All }`.

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) | `side` is excluded by `toolWindow`'s [`ToolWindow.AllowedSides`](/microsoft-ui-reactor/main/reference/docking/layout-model#allowedsides) mask. Spec 046 §6.6. |

### `Show` [#show]

Restores `content` from its hidden state into its previous container.

```csharp
public void Show(DockableContent content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `content` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) |  |
