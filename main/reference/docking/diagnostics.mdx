---
title: "Docking diagnostics"
description: "A log of the operations the dock manager performed."
---

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

## `DockOperation` [#dockoperation]

A single recorded operation. All fields are snapshots — the log never holds references to mutable state.

```csharp
public sealed record DockOperation
```

Namespace: `Microsoft.UI.Reactor.Docking.Diagnostics`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors]

#### `DockOperation` [#dockoperation_1]

```csharp
public DockOperation()
```

### Properties [#properties]

#### `Description` [#description]

Short human-readable summary, e.g. "drop SplitRight on path '0/0'".

```csharp
public string Description  // get, init
```

#### `Kind` [#kind]

What kind of operation this is.

```csharp
public DockOperationKind Kind  // get, init
```

#### `Layout` [#layout]

Layout snapshot AFTER this operation applied. Replay scrubs to this snapshot.

```csharp
public DockNode? Layout  // get, init
```

#### `LayoutJson` [#layoutjson]

JSON of [`DockOperation.Layout`](#layout) via DockLayoutSerializer; computed lazily / on append.

```csharp
public string? LayoutJson  // get, init
```

#### `PaneKey` [#panekey]

Pane key involved in the operation (drag source, dropped pane, etc.), or null when not applicable.

```csharp
public string? PaneKey  // get, init
```

#### `Ratios` [#ratios]

Ratio snapshot AFTER this operation applied. Keyed by tree-position path (e.g. "0/0"); values per-child ratios.

```csharp
public IReadOnlyDictionary<string, double[]>? Ratios  // get, init
```

#### `Target` [#target]

Drop target chosen on confirm, or null when not applicable (splitter / mount / layout-change events).

```csharp
public DockTarget? Target  // get, init
```

#### `TimestampUtc` [#timestamputc]

When this operation was recorded (UTC).

```csharp
public DateTime TimestampUtc  // get, init
```

## `DockOperationKind` [#dockoperationkind]

Kind of operation recorded by [`DockOperationLog`](#dockoperationlog).

```csharp
public enum DockOperationKind
```

Namespace: `Microsoft.UI.Reactor.Docking.Diagnostics`. Package: `Microsoft.UI.Reactor.Advanced`.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `Mount` | `0` | Initial layout captured at mount. |
| `DragStart` | `1` | Tab drag began — source pane captured into a DockDragSession. |
| `DragHover` | `2` | Drop-target overlay hover changed during a drag. |
| `DragConfirm` | `3` | User confirmed a drop target — layout mutated. |
| `DragCancel` | `4` | User cancelled the drag (Esc or dismiss). |
| `DragTearOut` | `5` | Drag ended outside any tab strip — pane torn out to a floating window. |
| `SplitterResize` | `6` | Splitter drag completed — ratios shifted. |
| `SplitterTrace` | `7` | Splitter intermediate event during a drag (pressed / moved / released snapshots). Carries the math behind the in-flight inline-size mutation so cursor-tracking + jump-back regressions can be traced. |
| `LayoutChange` | `8` | Programmatic / external layout change observed (e.g. app set new Layout). |
| `Note` | `9` | Free-form note appended by app code (debug breadcrumbs). |

## `DockOperationLog` [#dockoperationlog]

Ring-buffer log of docking operations + a cursor-based replay API. Thread-affined to the UI thread — appends from background threads throw, matching the rest of the docking subsystem (spec §8.10).

```csharp
public sealed class DockOperationLog
```

Namespace: `Microsoft.UI.Reactor.Docking.Diagnostics`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks]

Spec 045 — long-lived diagnostic infrastructure.

### Constructors [#constructors_1]

#### `DockOperationLog` [#dockoperationlog_1]

```csharp
public DockOperationLog()
```

### Properties [#properties_1]

#### `Count` [#count]

Current count of recorded operations (≤ [`DockOperationLog.MaxOperations`](#maxoperations)).

```csharp
public int Count  // get
```

#### `Current` [#current]

The operation at the cursor's last-applied position, or null when no ops or cursor at 0.

```csharp
public DockOperation? Current  // get
```

#### `Cursor` [#cursor]

Replay cursor. 0 = before any operation has been applied (the state before the very first recorded op). Count = after the most-recent op. [`DockOperationLog.Rewind`](#rewind) / [`DockOperationLog.StepForward`](#stepforward) move this; [`DockOperationLog.Append`](#append) snaps it to Count.

```csharp
public int Cursor  // get
```

#### `EmitToDebug` [#emittodebug]

When true (default), each [`DockOperationLog.Append`](#append) also writes to [`Debug.WriteLine`](https://learn.microsoft.com/dotnet/api/system.diagnostics.debug.writeline).

```csharp
public bool EmitToDebug  // get, set
```

#### `Operations` [#operations]

All operations currently held, oldest first.

```csharp
public IReadOnlyList<DockOperation> Operations  // get
```

### Methods [#methods]

#### `Append` [#append]

Append a new operation. Ring-buffer trim keeps the most-recent [`DockOperationLog.MaxOperations`](#maxoperations) entries. Cursor snaps to the end so the latest op is the live state.

```csharp
public void Append(DockOperation op)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `op` | [`DockOperation`](#dockoperation) |  |

#### `Record` [#record]

Convenience overload — captures TimestampUtc, computes JSON from `layout` via [`DockLayoutSerializer.Save`](/microsoft-ui-reactor/main/reference/docking/persistence#save), clones `ratios` into an immutable snapshot.

```csharp
public DockOperation Record(DockOperationKind kind, string description, DockNode? layout = null, IDictionary<string, double[]>? ratios = null, string? paneKey = null, DockTarget? target = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `kind` | [`DockOperationKind`](#dockoperationkind) |  |  |
| `description` | `string` |  |  |
| `layout` | [`DockNode?`](/microsoft-ui-reactor/main/reference/docking/layout-model#docknode) | `null` |  |
| `ratios` | `IDictionary<string, double[]>?` | `null` |  |
| `paneKey` | `string?` | `null` |  |
| `target` | `DockTarget?` | `null` |  |

#### `Reset` [#reset]

Clear the log and reset the cursor.

```csharp
public void Reset()
```

#### `Rewind` [#rewind]

Move the cursor one step back. Returns the operation now at the cursor (the one to "show"), or null if already at the beginning.

```csharp
public DockOperation? Rewind()
```

#### `SeekTo` [#seekto]

Jump the cursor to a specific index (clamped).

```csharp
public DockOperation? SeekTo(int index)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `index` | `int` |  |

#### `StepForward` [#stepforward]

Move the cursor one step forward. Returns the operation now at the cursor, or null if already at the end.

```csharp
public DockOperation? StepForward()
```

#### `TruncateAfterCursor` [#truncateaftercursor]

Drop every entry after the cursor — used when re-recording from a replay point.

```csharp
public void TruncateAfterCursor()
```

### Fields [#fields]

#### `MaxOperations` [#maxoperations]

Max entries retained in the ring buffer.

```csharp
public const int MaxOperations = 1000
```
