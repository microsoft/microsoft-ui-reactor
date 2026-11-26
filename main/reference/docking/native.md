---
title: "Native window interop"
description: "Floating windows, display bounds and the layout snapshots exchanged with native code."
---

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

## `DockDisplay` [#dockdisplay]

A display rectangle in screen-coords (DIPs). Mirrors the subset of `DisplayArea` we need for headless tests.

```csharp
public readonly record struct DockDisplay(double X, double Y, double Width, double Height)
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors]

#### `DockDisplay` [#dockdisplay_1]

A display rectangle in screen-coords (DIPs). Mirrors the subset of `DisplayArea` we need for headless tests.

```csharp
public DockDisplay(double X, double Y, double Width, double Height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `X` | `double` |  |
| `Y` | `double` |  |
| `Width` | `double` |  |
| `Height` | `double` |  |

### Properties [#properties]

#### `Bottom` [#bottom]

The bottom edge of the display (Y + Height).

```csharp
public double Bottom  // get
```

#### `Height` [#height]

```csharp
public double Height  // get, init
```

#### `Right` [#right]

The right edge of the display (X + Width).

```csharp
public double Right  // get
```

#### `Width` [#width]

```csharp
public double Width  // get, init
```

#### `X` [#x]

```csharp
public double X  // get, init
```

#### `Y` [#y]

```csharp
public double Y  // get, init
```

## `DockFloatingBounds` [#dockfloatingbounds]

A restored floating-window bounds tuple.

```csharp
public readonly record struct DockFloatingBounds(double X, double Y, double Width, double Height)
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_1]

#### `DockFloatingBounds` [#dockfloatingbounds_1]

A restored floating-window bounds tuple.

```csharp
public DockFloatingBounds(double X, double Y, double Width, double Height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `X` | `double` |  |
| `Y` | `double` |  |
| `Width` | `double` |  |
| `Height` | `double` |  |

### Properties [#properties_1]

#### `Height` [#height_1]

```csharp
public double Height  // get, init
```

#### `Width` [#width_1]

```csharp
public double Width  // get, init
```

#### `X` [#x_1]

```csharp
public double X  // get, init
```

#### `Y` [#y_1]

```csharp
public double Y  // get, init
```

## `DockFloatingClamp` [#dockfloatingclamp]

Pure clamp math for spec §2.6 / §2.25 multi-display restore.

```csharp
public static class DockFloatingClamp
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods]

#### `Clamp` [#clamp]

Clamps saved floating-window bounds against the set of available displays. Returns the original bounds when sufficiently visible; otherwise recenters on the first display in `displays` (the primary by convention) with size preserved or clamped.

```csharp
public static DockFloatingBounds Clamp(DockFloatingBounds saved, IReadOnlyList<DockDisplay> displays)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `saved` | [`DockFloatingBounds`](#dockfloatingbounds) |  |
| `displays` | `IReadOnlyList<DockDisplay>` |  |

**Remarks**

When `displays` is empty (no displays — unusual, e.g. a headless harness with no `DisplayArea`) returns the saved bounds unchanged.

#### `IsSufficientlyVisible` [#issufficientlyvisible]

Returns true when the saved bounds overlap at least one display by the minimum visible area (200 × 100 DIPs).

```csharp
public static bool IsSufficientlyVisible(DockFloatingBounds saved, IReadOnlyList<DockDisplay> displays)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `saved` | [`DockFloatingBounds`](#dockfloatingbounds) |  |
| `displays` | `IReadOnlyList<DockDisplay>` |  |

### Fields [#fields]

#### `MinVisibleHeight` [#minvisibleheight]

Minimum on-screen overlap (DIPs) required to keep saved bounds.

```csharp
public const double MinVisibleHeight = 100
```

#### `MinVisibleWidth` [#minvisiblewidth]

Minimum on-screen overlap (DIPs) required to keep saved bounds.

```csharp
public const double MinVisibleWidth = 200
```

#### `PrimaryClampMargin` [#primaryclampmargin]

Margin (DIPs) used when recentering an oversize window.

```csharp
public const double PrimaryClampMargin = 32
```

## `DockFloatingWindow` [#dockfloatingwindow]

Opens a real Reactor window hosting a single dock pane. The returned [`ReactorWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow) closes naturally on user dismissal; when closed it removes itself from the manager's tracking list (see `DockFloatingTracker`).

```csharp
public static class DockFloatingWindow
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_1]

#### `Open` [#open]

Open a floating window containing the given pane. Must be called on the UI thread.

```csharp
public static ReactorWindow Open(DockableContent pane, string? title = null, double width = 480, double height = 320, ReactorWindow? owner = null, DockFloatingBounds? savedBounds = null, IReadOnlyList<DockDisplay>? displays = null, DockManager? manager = null, double opacity = 1, bool noActivate = false, bool ignorePointerInput = false, (double X, double Y)? initialPosition = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `pane` | [`DockableContent`](/microsoft-ui-reactor/main/reference/docking/layout-model#dockablecontent) |  | The pane to host. Required. |
| `title` | `string?` | `null` | Window title; defaults to `pane.Title` or "Floating Window". |
| `width` | `double` | `480` | Initial width (DIPs). Defaults to 480. |
| `height` | `double` | `320` | Initial height (DIPs). Defaults to 320. |
| `owner` | [`ReactorWindow?`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow) | `null` | Optional owner window for owned-window semantics (spec 036 §9). |
| `savedBounds` | `DockFloatingBounds?` | `null` | Optional restored (x, y, w, h) from layout JSON; clamped against `displays` if provided. |
| `displays` | `IReadOnlyList<DockDisplay>?` | `null` | Optional display rectangles for the clamp. When null, no clamp is applied (current behavior); callers wiring against `DisplayArea.FindAll` pass the enumerated set. |
| `manager` | [`DockManager?`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) | `null` | Optional DockManager that owns this floating window. When supplied, the window is associated with that manager's tracking set; the host unmount handler closes it. When null, the window is tracked only in the global set. |
| `opacity` | `double` | `1` | Window opacity in \[0, 1\]. Defaults to 1.0 (fully opaque). Spec 045 §2.6 tear-off uses 0.5 for the immediate drag preview. |
| `noActivate` | `bool` | `false` | When true, opens the window without stealing focus from the source. Spec 045 §2.6 tear-off uses this so the drag pointer-capture stays on the source TabView. |
| `ignorePointerInput` | `bool` | `false` | When true, the window swallows no pointer input — drop-target overlays underneath remain hit-testable. Spec 045 §2.6 tear-off pairs this with the 0.5 opacity preview. |
| `initialPosition` | `(double X, double Y)?` | `null` | Optional explicit top-left (in DIPs). When provided, suppresses the WindowStartPosition.Default placement and pins the window at the supplied coordinates. |

**Returns:** The opened [`ReactorWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow).

## `DockHostRecord` [#dockhostrecord]

Per-host record in the registry. The `Manager` reference is weak — the registry holds no strong root for the element.

```csharp
public sealed class DockHostRecord
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`.

### Properties [#properties_2]

#### `Id` [#id]

Stable id assigned at registration; format `"dh:{n}"`.

```csharp
public string Id  // get, init
```

#### `Manager` [#manager]

The DockManager element the host rendered last, or null after the element has been GC'd.

```csharp
public DockManager? Manager  // get
```

## `DockHostRegistry` [#dockhostregistry]

Process-wide registry of mounted [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) elements. Used by the §2.26 MCP tools and the corresponding devtools surface to enumerate hosts without holding strong refs.

```csharp
public static class DockHostRegistry
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_2]

#### `Get` [#get]

Resolve a record by its stable [`DockHostRecord.Id`](#id). Returns null when no live host carries that id.

```csharp
public static DockHostRecord? Get(string id)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `id` | `string` |  |

#### `Register` [#register]

Register a freshly-mounted host. Idempotent for the same `manager` reference — returns the existing record. A later element of an already registered native host also returns that host's record, which then points at `manager`.

```csharp
public static DockHostRecord Register(DockManager manager)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `manager` | [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) |  |

#### `Snapshot` [#snapshot]

Snapshot the current set of live hosts. Records whose underlying element ref has been GC'd are filtered out before returning.

```csharp
public static IReadOnlyList<DockHostRecord> Snapshot()
```

#### `Unregister` [#unregister]

Remove the registration for `manager`'s host, if any: the record of the native host that rendered it, or else the record for the element itself.

```csharp
public static void Unregister(DockManager manager)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `manager` | [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) |  |

## `DockSnapshot` [#docksnapshot]

Read-only snapshot of a [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager)'s layout tree + sides. Returned by the §2.26 `docking.snapshot` MCP tool.

```csharp
public sealed record DockSnapshot
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_2]

#### `DockSnapshot` [#docksnapshot_1]

```csharp
public DockSnapshot()
```

### Properties [#properties_3]

#### `ActiveKey` [#activekey]

Stringified active pane key, or null when nothing is active.

```csharp
public string? ActiveKey  // get, init
```

#### `BottomSide` [#bottomside]

Hidden / pinned tool windows on the bottom strip.

```csharp
public IReadOnlyList<DockSnapshotPane> BottomSide  // get, init
```

#### `HostId` [#hostid]

Registry id of the host this snapshot was taken from.

```csharp
public string HostId  // get, init
```

#### `LeftSide` [#leftside]

Hidden / pinned tool windows by side.

```csharp
public IReadOnlyList<DockSnapshotPane> LeftSide  // get, init
```

#### `RightSide` [#rightside]

Hidden / pinned tool windows on the right strip.

```csharp
public IReadOnlyList<DockSnapshotPane> RightSide  // get, init
```

#### `Root` [#root]

Layout root (null = no docked tree mounted).

```csharp
public DockSnapshotNode? Root  // get, init
```

#### `TopSide` [#topside]

Hidden / pinned tool windows on the top strip.

```csharp
public IReadOnlyList<DockSnapshotPane> TopSide  // get, init
```

## `DockSnapshotBuilder` [#docksnapshotbuilder]

Builds [`DockSnapshot`](#docksnapshot) values from a live host. Pure- function transform — no mutation, no side effects.

```csharp
public static class DockSnapshotBuilder
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`.

### Methods [#methods_3]

#### `FromManager` [#frommanager]

Take a snapshot of a manager directly. `HostId` will be empty.

```csharp
public static DockSnapshot FromManager(DockManager manager)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `manager` | [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) |  |

#### `FromRecord` [#fromrecord]

Take a snapshot of a registry record. Returns null when the host is no longer live.

```csharp
public static DockSnapshot? FromRecord(DockHostRecord record)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `record` | [`DockHostRecord`](#dockhostrecord) |  |

## `DockSnapshotLeaf` [#docksnapshotleaf]

```csharp
public sealed record DockSnapshotLeaf(DockSnapshotPane Pane) : DockSnapshotNode
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockSnapshotNode`](#docksnapshotnode).

### Constructors [#constructors_3]

#### `DockSnapshotLeaf` [#docksnapshotleaf_1]

```csharp
public DockSnapshotLeaf(DockSnapshotPane Pane)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Pane` | [`DockSnapshotPane`](#docksnapshotpane) |  |

### Properties [#properties_4]

#### `Kind` [#kind]

```csharp
public override string Kind  // get
```

#### `Pane` [#pane]

```csharp
public DockSnapshotPane Pane  // get, init
```

## `DockSnapshotNode` [#docksnapshotnode]

Node algebra in the snapshot tree.

```csharp
public abstract record DockSnapshotNode
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_4]

#### `DockSnapshotNode` [#docksnapshotnode_1]

```csharp
protected DockSnapshotNode()
```

### Properties [#properties_5]

#### `Kind` [#kind_1]

Kind discriminator surfaced as a JSON field.

```csharp
public abstract string Kind  // get
```

## `DockSnapshotPane` [#docksnapshotpane]

Pane identity surfaced to MCP clients.

```csharp
public sealed record DockSnapshotPane(string? Key, string Title, string Role, bool CanClose, bool CanFloat, bool CanMove)
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_5]

#### `DockSnapshotPane` [#docksnapshotpane_1]

Pane identity surfaced to MCP clients.

```csharp
public DockSnapshotPane(string? Key, string Title, string Role, bool CanClose, bool CanFloat, bool CanMove)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Key` | `string?` |  |
| `Title` | `string` |  |
| `Role` | `string` |  |
| `CanClose` | `bool` |  |
| `CanFloat` | `bool` |  |
| `CanMove` | `bool` |  |

### Properties [#properties_6]

#### `CanClose` [#canclose]

```csharp
public bool CanClose  // get, init
```

#### `CanFloat` [#canfloat]

```csharp
public bool CanFloat  // get, init
```

#### `CanMove` [#canmove]

```csharp
public bool CanMove  // get, init
```

#### `Key` [#key]

```csharp
public string? Key  // get, init
```

#### `Role` [#role]

```csharp
public string Role  // get, init
```

#### `Title` [#title]

```csharp
public string Title  // get, init
```

## `DockSnapshotSplit` [#docksnapshotsplit]

```csharp
public sealed record DockSnapshotSplit(string Orientation, IReadOnlyList<DockSnapshotNode> Children) : DockSnapshotNode
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockSnapshotNode`](#docksnapshotnode).

### Constructors [#constructors_6]

#### `DockSnapshotSplit` [#docksnapshotsplit_1]

```csharp
public DockSnapshotSplit(string Orientation, IReadOnlyList<DockSnapshotNode> Children)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `Orientation` | `string` |  |
| `Children` | `IReadOnlyList<DockSnapshotNode>` |  |

### Properties [#properties_7]

#### `Children` [#children]

```csharp
public IReadOnlyList<DockSnapshotNode> Children  // get, init
```

#### `Kind` [#kind_2]

```csharp
public override string Kind  // get
```

#### `Orientation` [#orientation]

```csharp
public string Orientation  // get, init
```

## `DockSnapshotTabGroup` [#docksnapshottabgroup]

```csharp
public sealed record DockSnapshotTabGroup(int SelectedIndex, IReadOnlyList<DockSnapshotPane> Documents) : DockSnapshotNode
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockSnapshotNode`](#docksnapshotnode).

### Constructors [#constructors_7]

#### `DockSnapshotTabGroup` [#docksnapshottabgroup_1]

```csharp
public DockSnapshotTabGroup(int SelectedIndex, IReadOnlyList<DockSnapshotPane> Documents)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `SelectedIndex` | `int` |  |
| `Documents` | `IReadOnlyList<DockSnapshotPane>` |  |

### Properties [#properties_8]

#### `Documents` [#documents]

```csharp
public IReadOnlyList<DockSnapshotPane> Documents  // get, init
```

#### `Kind` [#kind_3]

```csharp
public override string Kind  // get
```

#### `SelectedIndex` [#selectedindex]

```csharp
public int SelectedIndex  // get, init
```

## `DockingNativeInterop` [#dockingnativeinterop]

Registers the docking element types with a Reactor [`Reconciler`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler) using the Phase-2 native renderer (no WinUI.Dock XAML dependency).

```csharp
public static class DockingNativeInterop
```

Namespace: `Microsoft.UI.Reactor.Docking.Native`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks]

Spec 045 §2.16. Mount creates a [`Border`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.border) whose `Child` is reconciled from a `DockHostNativeComponent` wrapping the [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) element. The component owns ratio state via hooks; reconciler preserves it across updates because the `ComponentElement` type stays the same at the same tree position.

### Methods [#methods_4]

#### `Register` [#register_1]

Registers the [`DockManager`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) element type with the given reconciler using the native renderer. Idempotent — calling twice re-registers the same handler.

```csharp
public static void Register(Reconciler reconciler)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `reconciler` | [`Reconciler`](/microsoft-ui-reactor/main/reference/extensibility/Reconciler) |  |
