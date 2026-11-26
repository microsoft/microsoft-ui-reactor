# Dock events

Event arguments the dock manager raises.

**Learn more:** [Docking Windows](../../docking.md)

## `DockActiveContentChangedEventArgs`

Args for [`DockManager.OnActiveContentChanged`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockActiveContentChangedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `DockActiveContentChangedEventArgs`

```csharp
public DockActiveContentChangedEventArgs()
```

### Properties

#### `ActiveContent`

The newly-active pane (null if no pane is active).

```csharp
public DockableContent? ActiveContent  // get, init
```

#### `PreviousContent`

The previously-active pane (null if none was active).

```csharp
public DockableContent? PreviousContent  // get, init
```

## `DockCancelEventArgs`

Base type for cancellable docking lifecycle event payloads.

```csharp
public abstract class DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks

Spec 045 §5.3.5.

### Constructors

#### `DockCancelEventArgs`

```csharp
protected DockCancelEventArgs()
```

### Properties

#### `Cancel`

Setting to true aborts the in-flight transition.

```csharp
public bool Cancel  // get, set
```

## `DockContentDockedEventArgs`

Args for [`DockManager.OnContentDocked`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockContentDockedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `DockContentDockedEventArgs`

```csharp
public DockContentDockedEventArgs()
```

### Properties

#### `Content`

The pane that docked.

```csharp
public required DockableContent Content  // get, init
```

#### `Target`

The dock target it landed at.

```csharp
public required DockTarget Target  // get, init
```

## `DockContentDockingEventArgs`

Args for [`DockManager.OnContentDocking`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockContentDockingEventArgs : DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockCancelEventArgs`](#dockcanceleventargs).

### Constructors

#### `DockContentDockingEventArgs`

```csharp
public DockContentDockingEventArgs()
```

### Properties

#### `Content`

The pane being docked.

```csharp
public required DockableContent Content  // get, init
```

#### `Target`

The dock target receiving the pane.

```csharp
public required DockTarget Target  // get, init
```

## `DockContentFloatedEventArgs`

Args for [`DockManager.OnContentFloated`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockContentFloatedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `DockContentFloatedEventArgs`

```csharp
public DockContentFloatedEventArgs()
```

### Properties

#### `Content`

The pane that floated.

```csharp
public required DockableContent Content  // get, init
```

## `DockContentFloatingEventArgs`

Args for [`DockManager.OnContentFloating`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockContentFloatingEventArgs : DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockCancelEventArgs`](#dockcanceleventargs).

### Constructors

#### `DockContentFloatingEventArgs`

```csharp
public DockContentFloatingEventArgs()
```

### Properties

#### `Content`

The pane being torn out.

```csharp
public required DockableContent Content  // get, init
```

## `DockDocumentClosedEventArgs`

Args for [`DockManager.OnDocumentClosed`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockDocumentClosedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `DockDocumentClosedEventArgs`

```csharp
public DockDocumentClosedEventArgs()
```

### Properties

#### `Document`

The document that closed.

```csharp
public required DockableContent Document  // get, init
```

## `DockDocumentClosingEventArgs`

Args for [`DockManager.OnDocumentClosing`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockDocumentClosingEventArgs : DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockCancelEventArgs`](#dockcanceleventargs).

### Constructors

#### `DockDocumentClosingEventArgs`

```csharp
public DockDocumentClosingEventArgs()
```

### Properties

#### `Document`

The document about to be closed.

```csharp
public required DockableContent Document  // get, init
```

## `DockFloatingCloseReason`

Why a docking floating window closed. Lets [`DockManager.OnFloatingWindowClosed`](../elements/docking.md#dockmanager) handlers tell a genuine user close (release per-document resources) apart from a synthetic close that follows a tab migrating to another surface (the content is still alive — do NOT release anything).

```csharp
public enum DockFloatingCloseReason
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks

Spec 045 §5.3.5.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `ContentClosed` | `0` | The window closed and its content is gone — the OS close button, Alt+F4, an app-driven `Close()`, the last tab being closed, or the owning host unmounting. It is safe to release per-document state tied to the content. This is the zero value, so a handler that ignores [`DockFloatingWindowClosedEventArgs.Reason`](#reason) sees the pre-#417 behaviour. Named for the outcome (content gone), not the initiator — it deliberately covers app-driven and host-unmount closes too, and so is distinct from `WindowCloseReason.UserClosed`. |
| `MigratedToHost` | `1` | The window closed synthetically because its last pane was dock-backed into another host (cross-window dock-back). The content is alive and visible in its new dock position — handlers must NOT release resources tied to it. |
| `MigratedToFloat` | `2` | The window closed synthetically because its last pane was re-torn-out or dropped into a different floating window. The content is alive in the destination float — handlers must NOT release resources tied to it. |

## `DockFloatingWindowClosedEventArgs`

Args for [`DockManager.OnFloatingWindowClosed`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockFloatingWindowClosedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `DockFloatingWindowClosedEventArgs`

```csharp
public DockFloatingWindowClosedEventArgs()
```

### Properties

#### `Content`

The pane that was inside the floating window when it closed (best-effort).

```csharp
public DockableContent? Content  // get, init
```

#### `Reason`

Why the window closed. [`DockFloatingCloseReason.ContentClosed`](#values) uniquely identifies a true close; the `Migrated*` values mark the synthetic close that pairs a cross-window dock-back / re-tear-out, where [`DockFloatingWindowClosedEventArgs.Content`](#content_4) is still alive elsewhere and must not be released.

```csharp
public required DockFloatingCloseReason Reason  // get, init
```

**Remarks**

Spec 045 §5.3.5. See issue #417.

## `DockFloatingWindowCreatedEventArgs`

Args for [`DockManager.OnFloatingWindowCreated`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockFloatingWindowCreatedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `DockFloatingWindowCreatedEventArgs`

```csharp
public DockFloatingWindowCreatedEventArgs()
```

### Properties

#### `DraggedSource`

The pane that spawned the floating window (null when restored from JSON).

```csharp
public DockableContent? DraggedSource  // get, init
```

## `DockLayoutChangedEventArgs`

Args for [`DockManager.OnLayoutChanged`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockLayoutChangedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `DockLayoutChangedEventArgs`

```csharp
public DockLayoutChangedEventArgs()
```

## `DockLayoutChangingEventArgs`

Args for [`DockManager.OnLayoutChanging`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockLayoutChangingEventArgs : DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockCancelEventArgs`](#dockcanceleventargs).

### Constructors

#### `DockLayoutChangingEventArgs`

```csharp
public DockLayoutChangingEventArgs()
```

## `DockToolWindowClosedEventArgs`

Args for [`DockManager.OnToolWindowClosed`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockToolWindowClosedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `DockToolWindowClosedEventArgs`

```csharp
public DockToolWindowClosedEventArgs()
```

### Properties

#### `ToolWindow`

The tool window that closed.

```csharp
public required DockableContent ToolWindow  // get, init
```

## `DockToolWindowClosingEventArgs`

Args for [`DockManager.OnToolWindowClosing`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockToolWindowClosingEventArgs : DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockCancelEventArgs`](#dockcanceleventargs).

### Constructors

#### `DockToolWindowClosingEventArgs`

```csharp
public DockToolWindowClosingEventArgs()
```

### Properties

#### `ToolWindow`

The tool window about to be closed.

```csharp
public required DockableContent ToolWindow  // get, init
```

## `DockToolWindowHiddenEventArgs`

Args for [`DockManager.OnToolWindowHidden`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockToolWindowHiddenEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `DockToolWindowHiddenEventArgs`

```csharp
public DockToolWindowHiddenEventArgs()
```

### Properties

#### `ToolWindow`

The tool window that auto-hid.

```csharp
public required DockableContent ToolWindow  // get, init
```

## `DockToolWindowHidingEventArgs`

Args for [`DockManager.OnToolWindowHiding`](../elements/docking.md#dockmanager).

```csharp
public sealed class DockToolWindowHidingEventArgs : DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockCancelEventArgs`](#dockcanceleventargs).

### Constructors

#### `DockToolWindowHidingEventArgs`

```csharp
public DockToolWindowHidingEventArgs()
```

### Properties

#### `ToolWindow`

The tool window about to auto-hide.

```csharp
public required DockableContent ToolWindow  // get, init
```
