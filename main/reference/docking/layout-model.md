---
title: "Layout model"
description: "The nodes, panes and targets that make up a dock layout."
---

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

## `DockGroupRole` [#dockgrouprole]

Categorizes a [`DockTabGroup`](#docktabgroup) for routing and reserved-empty behavior. Default [`DockGroupRole.General`](#values) preserves the pre-spec-046 semantics for callers that don't opt in.

```csharp
public enum DockGroupRole
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks]

Spec 046 §6.1. The role tag participates in the routing matrix in the internal `DockLayoutMutator` and in the drag-drop drop-target filter (§6.6). Persisted via `DockLayoutJson` (§6.7); omitted from JSON when at the default [`DockGroupRole.General`](#values).

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `General` | `0` | Untyped group. Accepts every [`DockableContent`](#dockablecontent) category; removed from the layout by the post-close cull pass when emptied. |
| `DocumentArea` | `1` | The document well. Preferred target for [`Document`](#document) inserts via `Dock(Center)`; rejects [`ToolWindow`](#toolwindow) drops by default. Implicitly `ShowWhenEmpty` — survives the post-close cull pass so the well remains a visible drop target after the last document closes. |
| `ToolWindowStrip` | `2` | An edge strip of tool windows. Rejects [`Document`](#document) drops by default; preferred target for [`ToolWindow`](#toolwindow) inserts. Culled normally when emptied (unlike [`DockGroupRole.DocumentArea`](#values)). |

## `DockLayoutSnapshot` [#docklayoutsnapshot]

Immutable snapshot of a dock host's structural state, surfaced via [`UseDockLayout`](/microsoft-ui-reactor/main/reference/hooks/UseDockLayout). Wide-net subscription: consumers re-render on any structural change (use sparingly — meant for devtools, not for pane content).

```csharp
public sealed record DockLayoutSnapshot(DockNode? Root, IReadOnlyList<ToolWindow> LeftSide, IReadOnlyList<ToolWindow> TopSide, IReadOnlyList<ToolWindow> RightSide, IReadOnlyList<ToolWindow> BottomSide, IReadOnlyList<FloatingDockWindow> Floating, DockableContent? ActiveContent)
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_1]

Spec 045 §5.3.11.

### Constructors [#constructors]

#### `DockLayoutSnapshot` [#docklayoutsnapshot_1]

Immutable snapshot of a dock host's structural state, surfaced via [`UseDockLayout`](/microsoft-ui-reactor/main/reference/hooks/UseDockLayout). Wide-net subscription: consumers re-render on any structural change (use sparingly — meant for devtools, not for pane content).

```csharp
public DockLayoutSnapshot(DockNode? Root, IReadOnlyList<ToolWindow> LeftSide, IReadOnlyList<ToolWindow> TopSide, IReadOnlyList<ToolWindow> RightSide, IReadOnlyList<ToolWindow> BottomSide, IReadOnlyList<FloatingDockWindow> Floating, DockableContent? ActiveContent)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Root` | [`DockNode?`](#docknode) |  |
| `LeftSide` | `IReadOnlyList<ToolWindow>` |  |
| `TopSide` | `IReadOnlyList<ToolWindow>` |  |
| `RightSide` | `IReadOnlyList<ToolWindow>` |  |
| `BottomSide` | `IReadOnlyList<ToolWindow>` |  |
| `Floating` | `IReadOnlyList<FloatingDockWindow>` |  |
| `ActiveContent` | [`DockableContent?`](#dockablecontent) |  |

**Remarks**

Spec 045 §5.3.11.

### Properties [#properties]

#### `ActiveContent` [#activecontent]

```csharp
public DockableContent? ActiveContent  // get, init
```

#### `BottomSide` [#bottomside]

```csharp
public IReadOnlyList<ToolWindow> BottomSide  // get, init
```

#### `Floating` [#floating]

```csharp
public IReadOnlyList<FloatingDockWindow> Floating  // get, init
```

#### `LeftSide` [#leftside]

```csharp
public IReadOnlyList<ToolWindow> LeftSide  // get, init
```

#### `RightSide` [#rightside]

```csharp
public IReadOnlyList<ToolWindow> RightSide  // get, init
```

#### `Root` [#root]

```csharp
public DockNode? Root  // get, init
```

#### `TopSide` [#topside]

```csharp
public IReadOnlyList<ToolWindow> TopSide  // get, init
```

## `DockNode` [#docknode]

Sealed algebra of nodes that make up a docking layout. Implementations: [`DockSplit`](#docksplit), [`DockTabGroup`](#docktabgroup), [`DockableContent`](#dockablecontent) (with Phase 2 subclasses [`Document`](#document) and [`ToolWindow`](#toolwindow)).

```csharp
public abstract record DockNode
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_2]

Spec 045 §4.3. Sealed via the abstract base + sealed concrete records; new node kinds in P3 (`DockableWindowRef`) extend the algebra additively per §6.4.

### Constructors [#constructors_1]

#### `DockNode` [#docknode_1]

```csharp
protected DockNode()
```

## `DockPaneInfo` [#dockpaneinfo]

Snapshot of a pane's identity surfaced to pane-content components via the `UsePane()` hook. Apps read this from the pane's own subtree to address themselves (e.g. for context-menu actions).

```csharp
public readonly record struct DockPaneInfo(object? Key, string Title, DockableContent Content)
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_3]

Spec 045 §5.3.11.

### Constructors [#constructors_2]

#### `DockPaneInfo` [#dockpaneinfo_1]

Snapshot of a pane's identity surfaced to pane-content components via the `UsePane()` hook. Apps read this from the pane's own subtree to address themselves (e.g. for context-menu actions).

```csharp
public DockPaneInfo(object? Key, string Title, DockableContent Content)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Key` | `object?` |  |
| `Title` | `string` |  |
| `Content` | [`DockableContent`](#dockablecontent) |  |

**Remarks**

Spec 045 §5.3.11.

### Properties [#properties_1]

#### `Content` [#content]

```csharp
public DockableContent Content  // get, init
```

#### `Key` [#key]

```csharp
public object? Key  // get, init
```

#### `Title` [#title]

```csharp
public string Title  // get, init
```

## `DockPaneState` [#dockpanestate]

Where a pane sits in the dock topology at a given moment. Reported by `UseDockState()` per spec 045 §5.3.11 / §2.17.

```csharp
public enum DockPaneState
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_4]

Spec 045 §5.3.11.

### Values [#values_1]

| Name | Value | Description |
| --- | --- | --- |
| `Docked` | `0` | Pinned inside the host's docking tree (default). |
| `Floating` | `1` | Hosted by a top-level floating window. |
| `AutoHidden` | `2` | ToolWindow collapsed to a side strip; click expands. |
| `AutoHiddenExpanded` | `3` | Auto-hidden ToolWindow whose `SidePopup` is currently expanded. |
| `Hidden` | `4` | Closed-but-remembered; no UI representation until reshown. |

## `DockRoutingFallback` [#dockroutingfallback]

Spec 046 §6.3 — routing diagnostic returned by [`DockLayoutOps.InsertPaneAtTarget`](/microsoft-ui-reactor/main/reference/docking/DockLayoutOps#insertpaneattargetdocknode-dockablecontent-docktarget-dockroutingfallback) when the role-aware insert couldn't find an accepting group and degraded to the pre-046 "leftmost descendant" placement.

```csharp
public sealed record DockRoutingFallback(string Description)
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_5]

The [`DockRoutingFallback.Description`](#description) is a human-readable explanation suitable for diagnostic logs or a status-bar surface. Apps can also detect the fallback condition simply by checking non-null on the out parameter — the description is informational, not parse-targeted.

### Constructors [#constructors_3]

#### `DockRoutingFallback` [#dockroutingfallback_1]

Spec 046 §6.3 — routing diagnostic returned by [`DockLayoutOps.InsertPaneAtTarget`](/microsoft-ui-reactor/main/reference/docking/DockLayoutOps#insertpaneattargetdocknode-dockablecontent-docktarget-dockroutingfallback) when the role-aware insert couldn't find an accepting group and degraded to the pre-046 "leftmost descendant" placement.

```csharp
public DockRoutingFallback(string Description)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Description` | `string` |  |

**Remarks**

The [`DockRoutingFallback.Description`](#description) is a human-readable explanation suitable for diagnostic logs or a status-bar surface. Apps can also detect the fallback condition simply by checking non-null on the out parameter — the description is informational, not parse-targeted.

### Properties [#properties_2]

#### `Description` [#description]

```csharp
public string Description  // get, init
```

## `DockSide` [#dockside]

Which edge of a [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) a [`ToolWindow`](#toolwindow) is pinned to. The model's logical sides; in RTL the visual mapping flips (spec 045 §8.8).

```csharp
public enum DockSide
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_6]

Spec 045 §5.3.10.

### Values [#values_2]

| Name | Value | Description |
| --- | --- | --- |
| `Left` | `0` | Left edge in LTR; right edge visually in RTL. |
| `Top` | `1` | Top edge. |
| `Right` | `2` | Right edge in LTR; left edge visually in RTL. |
| `Bottom` | `3` | Bottom edge. |

## `DockSideExtensions` [#docksideextensions]

Extension helpers for [`DockSide`](#dockside) ↔ [`DockSides`](#docksides).

```csharp
public static class DockSideExtensions
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_7]

Spec 046 §6.2 / §6.6. Single-side enum → flag mask conversion used at the drop-target filter call site.

### Methods [#methods]

#### `ToFlag` [#toflag]

Maps a single [`DockSide`](#dockside) to its corresponding [`DockSides`](#docksides) flag.

```csharp
public static DockSides ToFlag(this DockSide side)
```

## `DockSides` [#docksides]

Flag set describing which edges of a dock host a [`ToolWindow`](#toolwindow) may dock to. Default [`DockSides.All`](#values_3) preserves the pre-spec-046 unconstrained behavior.

```csharp
public enum DockSides
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_8]

Spec 046 §6.2. Drives drag-drop drop-target eligibility (§6.6) and programmatic [`DockHostModel.PinToSide`](/microsoft-ui-reactor/main/reference/docking/DockHostModel#pintoside) validation (§6.6, §9 Q4). RTL-aware: the mask is in *logical* sides (spec 045 §8.8); the drag-drop overlay translates visual edges back to logical before consulting the mask.

### Values [#values_3]

| Name | Value | Description |
| --- | --- | --- |
| `None` | `0` | No edges allowed. Pinning always throws; the tool window is float-only. |
| `Left` | `1` | Logical left edge (visual right in RTL). |
| `Top` | `2` | Top edge. |
| `Right` | `4` | Logical right edge (visual left in RTL). |
| `Bottom` | `8` | Bottom edge. |
| `All` | `15` | All four edges. The default for [`ToolWindow.AllowedSides`](#allowedsides). |

## `DockSplit` [#docksplit]

A split container with N children along a single orientation. Children resize via splitters between them; widths/heights drive initial layout and are persisted across re-mounts.

```csharp
public sealed record DockSplit(Orientation Orientation, IReadOnlyList<DockNode> Children, double? Width = null, double? Height = null, double? MinWidth = null, double? MinHeight = null, double? MaxWidth = null, double? MaxHeight = null) : DockNode
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockNode`](#docknode).

### Remarks [#remarks_9]

Spec 045 §4.3.

### Constructors [#constructors_4]

#### `DockSplit` [#docksplit_1]

A split container with N children along a single orientation. Children resize via splitters between them; widths/heights drive initial layout and are persisted across re-mounts.

```csharp
public DockSplit(Orientation Orientation, IReadOnlyList<DockNode> Children, double? Width = null, double? Height = null, double? MinWidth = null, double? MinHeight = null, double? MaxWidth = null, double? MaxHeight = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Orientation` | `Orientation` |  |  |
| `Children` | `IReadOnlyList<DockNode>` |  |  |
| `Width` | `double?` | `null` |  |
| `Height` | `double?` | `null` |  |
| `MinWidth` | `double?` | `null` |  |
| `MinHeight` | `double?` | `null` |  |
| `MaxWidth` | `double?` | `null` |  |
| `MaxHeight` | `double?` | `null` |  |

**Remarks**

Spec 045 §4.3.

### Properties [#properties_3]

#### `Children` [#children]

```csharp
public IReadOnlyList<DockNode> Children  // get, init
```

#### `Height` [#height]

```csharp
public double? Height  // get, init
```

#### `MaxHeight` [#maxheight]

```csharp
public double? MaxHeight  // get, init
```

#### `MaxWidth` [#maxwidth]

```csharp
public double? MaxWidth  // get, init
```

#### `MinHeight` [#minheight]

```csharp
public double? MinHeight  // get, init
```

#### `MinWidth` [#minwidth]

```csharp
public double? MinWidth  // get, init
```

#### `Orientation` [#orientation]

```csharp
public Orientation Orientation  // get, init
```

#### `Width` [#width]

```csharp
public double? Width  // get, init
```

## `DockTabGroup` [#docktabgroup]

A group of panes presented as tabs. Panes are reordered by drag inside the group; the active tab is reported via [`DockTabGroup.SelectedIndex`](#selectedindex).

```csharp
public sealed record DockTabGroup(IReadOnlyList<DockableContent> Documents, TabPosition TabPosition = TabPosition.Top, bool CompactTabs = false, bool ShowWhenEmpty = false, int SelectedIndex = -1, double? Width = null, double? Height = null, TabChrome TabChrome = TabChrome.Win11, DockGroupRole Role = DockGroupRole.General) : DockNode
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockNode`](#docknode).

### Remarks [#remarks_10]

Spec 045 §4.3. The tab strip uses WinUI `TabView`; spec §11 keeps that decision through P2 for accessibility shape.

Spec 046 §6.1 adds the trailing [`DockTabGroup.Role`](#role) positional parameter. It defaults to [`DockGroupRole.General`](#values) so existing layouts keep their pre-046 routing and cull behavior. Set to [`DockGroupRole.DocumentArea`](#values) to mark a group as the document well (preferred target for `Dock(Center)` + reserved empty) or [`DockGroupRole.ToolWindowStrip`](#values) for an edge strip of tool windows.

[`DockTabGroup.CompactTabs`](#compacttabs) asks for dense tool-pane tabs; a group whose panes are all [`ToolWindow`](#toolwindow)s gets them by default. Each tab is sized to its title instead of stretched to an equal share of the strip, and unselected tabs keep showing their titles.

### Constructors [#constructors_5]

#### `DockTabGroup` [#docktabgroup_1]

A group of panes presented as tabs. Panes are reordered by drag inside the group; the active tab is reported via [`DockTabGroup.SelectedIndex`](#selectedindex).

```csharp
public DockTabGroup(IReadOnlyList<DockableContent> Documents, TabPosition TabPosition = TabPosition.Top, bool CompactTabs = false, bool ShowWhenEmpty = false, int SelectedIndex = -1, double? Width = null, double? Height = null, TabChrome TabChrome = TabChrome.Win11, DockGroupRole Role = DockGroupRole.General)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Documents` | `IReadOnlyList<DockableContent>` |  |  |
| `TabPosition` | [`TabPosition`](#tabposition_1) | `TabPosition.Top` |  |
| `CompactTabs` | `bool` | `false` |  |
| `ShowWhenEmpty` | `bool` | `false` |  |
| `SelectedIndex` | `int` | `-1` |  |
| `Width` | `double?` | `null` |  |
| `Height` | `double?` | `null` |  |
| `TabChrome` | [`TabChrome`](#tabchrome_1) | `TabChrome.Win11` |  |
| `Role` | [`DockGroupRole`](#dockgrouprole) | `DockGroupRole.General` |  |

**Remarks**

Spec 045 §4.3. The tab strip uses WinUI `TabView`; spec §11 keeps that decision through P2 for accessibility shape.

Spec 046 §6.1 adds the trailing [`DockTabGroup.Role`](#role) positional parameter. It defaults to [`DockGroupRole.General`](#values) so existing layouts keep their pre-046 routing and cull behavior. Set to [`DockGroupRole.DocumentArea`](#values) to mark a group as the document well (preferred target for `Dock(Center)` + reserved empty) or [`DockGroupRole.ToolWindowStrip`](#values) for an edge strip of tool windows.

[`DockTabGroup.CompactTabs`](#compacttabs) asks for dense tool-pane tabs; a group whose panes are all [`ToolWindow`](#toolwindow)s gets them by default. Each tab is sized to its title instead of stretched to an equal share of the strip, and unselected tabs keep showing their titles.

### Properties [#properties_4]

#### `CompactTabs` [#compacttabs]

```csharp
public bool CompactTabs  // get, init
```

#### `Documents` [#documents]

```csharp
public IReadOnlyList<DockableContent> Documents  // get, init
```

#### `Height` [#height_1]

```csharp
public double? Height  // get, init
```

#### `Role` [#role]

```csharp
public DockGroupRole Role  // get, init
```

#### `SelectedIndex` [#selectedindex]

```csharp
public int SelectedIndex  // get, init
```

#### `ShowWhenEmpty` [#showwhenempty]

```csharp
public bool ShowWhenEmpty  // get, init
```

#### `TabChrome` [#tabchrome]

```csharp
public TabChrome TabChrome  // get, init
```

#### `TabPosition` [#tabposition]

```csharp
public TabPosition TabPosition  // get, init
```

#### `Width` [#width_1]

```csharp
public double? Width  // get, init
```

## `DockTabGroupContext` [#docktabgroupcontext]

Context for an [`IDockAdapter.OnGroupCreated`](/microsoft-ui-reactor/main/reference/docking/IDockAdapter#ongroupcreated) callback. Carries the dragged-out source pane (if the group was created by a tear-out, otherwise null).

```csharp
public sealed record DockTabGroupContext(DockableContent? DraggedSource)
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_11]

Spec 045 §4.3. Phase 1 is intentionally minimal — Phase 2's `DockHostModel` introduces a richer mutation handle (§5.3.10).

### Constructors [#constructors_6]

#### `DockTabGroupContext` [#docktabgroupcontext_1]

Context for an [`IDockAdapter.OnGroupCreated`](/microsoft-ui-reactor/main/reference/docking/IDockAdapter#ongroupcreated) callback. Carries the dragged-out source pane (if the group was created by a tear-out, otherwise null).

```csharp
public DockTabGroupContext(DockableContent? DraggedSource)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `DraggedSource` | [`DockableContent?`](#dockablecontent) |  |

**Remarks**

Spec 045 §4.3. Phase 1 is intentionally minimal — Phase 2's `DockHostModel` introduces a richer mutation handle (§5.3.10).

### Properties [#properties_5]

#### `DraggedSource` [#draggedsource]

```csharp
public DockableContent? DraggedSource  // get, init
```

## `DockTarget` [#docktarget]

Where to dock a pane when programmatically issuing `DockTo(target, DockTarget)`. Split targets land inside the current group's split parent; edge targets land at the manager root.

```csharp
public enum DockTarget
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_12]

Spec 045 §4.3.

### Values [#values_4]

| Name | Value | Description |
| --- | --- | --- |
| `Center` | `0` | Add as a tab in the destination group. |
| `SplitLeft` | `1` | Split the destination group's parent; new pane on the left. |
| `SplitTop` | `2` | Split the destination group's parent; new pane on top. |
| `SplitRight` | `3` | Split the destination group's parent; new pane on the right. |
| `SplitBottom` | `4` | Split the destination group's parent; new pane on the bottom. |
| `DockLeft` | `5` | Dock at the manager's left edge. |
| `DockTop` | `6` | Dock at the manager's top edge. |
| `DockRight` | `7` | Dock at the manager's right edge. |
| `DockBottom` | `8` | Dock at the manager's bottom edge. |

## `DockableContent` [#dockablecontent]

A single dockable pane — the leaf of the dock tree. Carries [`DockableContent.Content`](#content_1) (a Reactor element subtree), a stable [`DockableContent.Key`](#key_1) for keyed reconciliation across reorderings, and per-pane permission flags.

```csharp
public record DockableContent(string Title, Element? Content = null, object? Key = null, bool CanClose = false, bool CanPin = false, double? Width = null, double? Height = null, string? PersistenceState = null) : DockNode
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockNode`](#docknode).

### Remarks [#remarks_13]

Spec 045 §4.3. Phase 1 collapses Visual Studio's document/tool-window distinction into this single role. Phase 2 introduces sealed subclasses [`Document`](#document) and [`ToolWindow`](#toolwindow) (§5.3.1); the [`DockableContent`](#dockablecontent) base remains for source compat.

[`DockableContent.Key`](#key_1) identity rules per spec 042 (keyed reconciliation): a stable, equatable value (string, GUID, enum, app domain id) is required for the reconciler to preserve pane state across reorderings and across drag-out / drop-back. [`DockableContent.Key`](#key_1) replaces upstream WinUI.Dock's `Title`-as-key convention (with the `##` namespace hack) — there is no fallback to title-keying.

Phase 1 ships the closed-shape positional constructor below for source compat; Phase 2's subclasses use init-only properties so apps can opt-in to the new permission flags additively.

### Constructors [#constructors_7]

#### `DockableContent` [#dockablecontent_1]

##### `DockableContent()` [#dockablecontent_2]

Parameterless constructor — for with-expression chaining and Phase-2 subclass init.

```csharp
protected DockableContent()
```

##### `DockableContent(string, Element?, object?, bool, bool, double?, double?, string?)` [#dockablecontentstring-element-object-bool-bool-double-double-string]

Phase-1 closed-shape positional constructor — kept for source compat with apps that built against the P1 API. Phase 2 adds positional fields via the [`Document`](#document) / [`ToolWindow`](#toolwindow) subclasses instead of extending this constructor.

```csharp
public DockableContent(string Title, Element? Content = null, object? Key = null, bool CanClose = false, bool CanPin = false, double? Width = null, double? Height = null, string? PersistenceState = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `Title` | `string` |  |  |
| `Content` | [`Element?`](/microsoft-ui-reactor/main/reference/elements/Element) | `null` |  |
| `Key` | `object?` | `null` |  |
| `CanClose` | `bool` | `false` |  |
| `CanPin` | `bool` | `false` |  |
| `Width` | `double?` | `null` |  |
| `Height` | `double?` | `null` |  |
| `PersistenceState` | `string?` | `null` |  |

**Remarks**

Spec 045 §5.3.1: "Non-breaking deprecation of the closed-shape `DockableContent(...)` constructor: warning analyzer points users to `Document(...)` / `ToolWindow(...)`. The base type still accepts the old shape for P1 source compat."

### Properties [#properties_6]

#### `CanClose` [#canclose]

Whether the close button (X) is enabled on this pane.

```csharp
public bool CanClose  // get, init
```

#### `CanFloat` [#canfloat]

Whether the pane can be torn out into a floating window. Default true (Phase 2 addition; see spec 045 §5.3.8).

```csharp
public bool CanFloat  // get, init
```

#### `CanMove` [#canmove]

Whether the pane can be moved (drag-reorder, drag-to-other-group). Default true (Phase 2 addition; see spec 045 §5.3.8).

```csharp
public bool CanMove  // get, init
```

#### `CanPin` [#canpin]

Whether the pin-to-side affordance is enabled on this pane.

```csharp
public bool CanPin  // get, init
```

#### `Content` [#content_1]

The Reactor element subtree mounted as the pane's body.

```csharp
public Element? Content  // get, init
```

#### `Height` [#height_2]

Optional fixed height hint (DIPs).

```csharp
public double? Height  // get, init
```

#### `Key` [#key_1]

Stable key for reconciler-grade pane identity. Required for state preservation across reorderings; replaces upstream's title-as-key convention with no fallback (spec 045 §4.4, §1.4).

```csharp
public object? Key  // get, init
```

#### `PersistenceState` [#persistencestate]

Opaque per-pane state envelope persisted alongside the layout JSON. Phase 2 introduces [`Document<TState>`](#documenttstate) for typed state; this string field stays as the untyped escape hatch for P1 source compat (spec 045 §5.3.2).

```csharp
public string? PersistenceState  // get, init
```

#### `Title` [#title_1]

The pane's display title (tab caption / floating-window title).

```csharp
public string Title  // get, init
```

#### `Width` [#width_2]

Optional fixed width hint (DIPs).

```csharp
public double? Width  // get, init
```

## `Document` [#document]

A closable document pane — lives inside a `DocumentPane`; cannot be pinned to a side. Default tab styling: top-position, full.

```csharp
public record Document : DockableContent
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockableContent`](#dockablecontent).

### Remarks [#remarks_14]

Spec 045 §5.3.1 (Phase 2 addition). Documents and [`ToolWindow`](#toolwindow)s distinguish themselves only via their default permission flags and default placement; both reconcile through the same pane pipeline.

Use object-initializer syntax (`new Document { Title = "X", Key = id }`) rather than the positional [`DockableContent`](#dockablecontent) constructor — this matches the rest of Reactor's record style and lets apps opt into the new permission flags additively.

### Constructors [#constructors_8]

#### `Document` [#document_1]

Parameterless ctor — overrides the base permission defaults to Document semantics.

```csharp
public Document()
```

### Properties [#properties_7]

#### `CanDockAsToolWindow` [#candockastoolwindow]

Whether this document can also be docked as a tool window (side-pinnable). Default false (spec 045 §5.3.8).

```csharp
public bool CanDockAsToolWindow  // get, init
```

## `Document<TState>` [#documenttstate]

A document with typed per-pane state. The `TState` is included in the layout JSON that [`DockLayoutSerializer.Save`](/microsoft-ui-reactor/main/reference/docking/persistence#save) writes, so app state survives a restart when the app stores that JSON and loads it back.

```csharp
public sealed record Document<TState> : Document
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Document`](#document).

### Remarks [#remarks_15]

Spec 045 §5.3.2 (Phase 2 addition). Per-pane `TState` schema versioning is the app's responsibility — Reactor stores whatever round-trips through the configured JSON serializer; if the app changes `TState`'s shape it owns the migration (spec 045 §8.11).

Example: a code-editor pane stores its scroll offset and selection range:

```csharp
new Document<EditorState> {
    Title = "MainView.xaml",
    Key = fileId,
    State = new EditorState(scrollOffset: 1024, caret: (line: 42, col: 7)),
    Content = new Editor(...)
}
```

### Type parameters [#type-parameters]

| Name | Description |
| --- | --- |
| `TState` | App-defined per-pane state envelope. Must be JSON-serializable through the app's configured `System.Text.Json.JsonSerializerOptions`. |

### Constructors [#constructors_9]

#### `Document` [#document_2]

```csharp
public Document()
```

### Properties [#properties_8]

#### `State` [#state]

The typed pane state envelope. Round-trips through layout JSON.

```csharp
public TState? State  // get, init
```

## `FloatingDockWindow` [#floatingdockwindow]

Read-only view of a floating-pane window, surfaced through [`DockHostModel.Floating`](/microsoft-ui-reactor/main/reference/docking/DockHostModel#floating).

```csharp
public sealed record FloatingDockWindow
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_16]

Spec 045 §5.3.10 (Phase 2 addition).

### Constructors [#constructors_10]

#### `FloatingDockWindow` [#floatingdockwindow_1]

```csharp
public FloatingDockWindow()
```

### Properties [#properties_9]

#### `Contents` [#contents]

The pane(s) inside the floating window.

```csharp
public required IReadOnlyList<DockableContent> Contents  // get, init
```

#### `Height` [#height_3]

Last-known size in DIPs.

```csharp
public double Height  // get, init
```

#### `Id` [#id]

Stable identifier for the floating window.

```csharp
public required object Id  // get, init
```

#### `Width` [#width_3]

Last-known size in DIPs.

```csharp
public double Width  // get, init
```

#### `X` [#x]

Last-known position in screen coordinates (DIPs).

```csharp
public double X  // get, init
```

#### `Y` [#y]

Last-known position in screen coordinates (DIPs).

```csharp
public double Y  // get, init
```

## `TabChrome` [#tabchrome_1]

Visual chrome preset applied to a [`DockTabGroup`](#docktabgroup)'s tab strip. Maps onto WinUI `TabView` resource-dictionary overrides — the underlying control and accessibility tree are unchanged across presets.

```csharp
public enum TabChrome
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_17]

Spec 045 §4.6 — partial close-out. Selecting a preset doesn't replace the control's template; it scopes a handful of theme-resource overrides to that one `TabView`. New presets land additively; default is [`TabChrome.Win11`](#values_5) so existing layouts re-render unchanged.

### Values [#values_5]

| Name | Value | Description |
| --- | --- | --- |
| `Win11` | `0` | Default Windows 11 TabView look: rounded header corners, theme background. No resource overrides applied. |
| `Flat` | `1` | Sharp, dense IDE chrome: zero corner radius on tab headers, tighter header padding. Modeled after the VS Code / classic-Visual-Studio document-tab look. |
| `TitleBar` | `2` | Tab-strip background uses `TitleBarBackgroundFillBrush` (when available) so the strip blends into the system title bar. Corner radius is unchanged from [`TabChrome.Win11`](#values_5). Spec 045 §4.6. |

## `TabPosition` [#tabposition_1]

Where the tab strip is rendered relative to the content.

```csharp
public enum TabPosition
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_18]

Spec 045 §4.3.

### Values [#values_6]

| Name | Value | Description |
| --- | --- | --- |
| `Top` | `0` | Tabs above the active content (Visual Studio default). |
| `Bottom` | `1` | Tabs below the active content (Office tool-window style). |

## `ToolWindow` [#toolwindow]

A hideable tool window — lives inside a `ToolPane`; pinnable to a side strip; can be auto-hidden. Default tab styling: bottom-position, compact.

```csharp
public sealed record ToolWindow : DockableContent
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockableContent`](#dockablecontent).

### Remarks [#remarks_19]

Spec 045 §5.3.1 (Phase 2 addition). The X button on a ToolWindow *hides* rather than closes (AvalonDock semantic). See spec 045 §5.3.8 for permission defaults.

### Constructors [#constructors_11]

#### `ToolWindow` [#toolwindow_1]

Parameterless ctor — overrides the base permission defaults to ToolWindow semantics.

```csharp
public ToolWindow()
```

### Properties [#properties_10]

#### `AllowedSides` [#allowedsides]

Edges this tool window may dock to. Affects drag-drop drop-target eligibility (filtered targets dim during drag, hit-test ignores them) and programmatic [`DockHostModel.PinToSide`](/microsoft-ui-reactor/main/reference/docking/DockHostModel#pintoside) (which throws [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) on an edge the mask forbids). Default [`DockSides.All`](#values_3) preserves pre-spec-046 unconstrained placement.

```csharp
public DockSides AllowedSides  // get, init
```

**Remarks**

Spec 046 §6.2 / §6.6 / §9 Q4. Strategies that need to bypass the mask (e.g. a custom layout strategy placing a tool window on its own terms) should clear the mask via `tw with { AllowedSides = DockSides.All }` before calling [`DockHostModel.PinToSide`](/microsoft-ui-reactor/main/reference/docking/DockHostModel#pintoside). Setting the mask to [`DockSides.None`](#values_3) is allowed and means the tool window is float-only — every [`DockHostModel.PinToSide`](/microsoft-ui-reactor/main/reference/docking/DockHostModel#pintoside) call throws.

#### `CanAutoHide` [#canautohide]

Auto-hide affordance enabled. Default true.

```csharp
public bool CanAutoHide  // get, init
```

#### `CanDockAsDocument` [#candockasdocument]

Tool window can be docked as a document (promoted into a DocumentPane). Default true.

```csharp
public bool CanDockAsDocument  // get, init
```

#### `CanHide` [#canhide]

X button hides rather than closes. Default true.

```csharp
public bool CanHide  // get, init
```
