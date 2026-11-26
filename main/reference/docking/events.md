---
title: "Dock events"
description: "Event arguments the dock manager raises."
---

**Learn more:** [Docking Windows](/microsoft-ui-reactor/main/guides/docking)

## `DockActiveContentChangedEventArgs` [#dockactivecontentchangedeventargs]

Args for [`DockManager.OnActiveContentChanged`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockActiveContentChangedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors]

#### `DockActiveContentChangedEventArgs` [#dockactivecontentchangedeventargs_1]

```csharp
public DockActiveContentChangedEventArgs()
```

### Properties [#properties]

#### `ActiveContent` [#activecontent]

The newly-active pane (null if no pane is active).

```csharp
public DockableContent? ActiveContent  // get, init
```

#### `PreviousContent` [#previouscontent]

The previously-active pane (null if none was active).

```csharp
public DockableContent? PreviousContent  // get, init
```

## `DockCancelEventArgs` [#dockcanceleventargs]

Base type for cancellable docking lifecycle event payloads.

```csharp
public abstract class DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks]

Spec 045 §5.3.5.

### Constructors [#constructors_1]

#### `DockCancelEventArgs` [#dockcanceleventargs_1]

```csharp
protected DockCancelEventArgs()
```

### Properties [#properties_1]

#### `Cancel` [#cancel]

Setting to true aborts the in-flight transition.

```csharp
public bool Cancel  // get, set
```

## `DockContentDockedEventArgs` [#dockcontentdockedeventargs]

Args for [`DockManager.OnContentDocked`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockContentDockedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_2]

#### `DockContentDockedEventArgs` [#dockcontentdockedeventargs_1]

```csharp
public DockContentDockedEventArgs()
```

### Properties [#properties_2]

#### `Content` [#content]

The pane that docked.

```csharp
public required DockableContent Content  // get, init
```

#### `Target` [#target]

The dock target it landed at.

```csharp
public required DockTarget Target  // get, init
```

## `DockContentDockingEventArgs` [#dockcontentdockingeventargs]

Args for [`DockManager.OnContentDocking`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockContentDockingEventArgs : DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockCancelEventArgs`](#dockcanceleventargs).

### Constructors [#constructors_3]

#### `DockContentDockingEventArgs` [#dockcontentdockingeventargs_1]

```csharp
public DockContentDockingEventArgs()
```

### Properties [#properties_3]

#### `Content` [#content_1]

The pane being docked.

```csharp
public required DockableContent Content  // get, init
```

#### `Target` [#target_1]

The dock target receiving the pane.

```csharp
public required DockTarget Target  // get, init
```

## `DockContentFloatedEventArgs` [#dockcontentfloatedeventargs]

Args for [`DockManager.OnContentFloated`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockContentFloatedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_4]

#### `DockContentFloatedEventArgs` [#dockcontentfloatedeventargs_1]

```csharp
public DockContentFloatedEventArgs()
```

### Properties [#properties_4]

#### `Content` [#content_2]

The pane that floated.

```csharp
public required DockableContent Content  // get, init
```

## `DockContentFloatingEventArgs` [#dockcontentfloatingeventargs]

Args for [`DockManager.OnContentFloating`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockContentFloatingEventArgs : DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockCancelEventArgs`](#dockcanceleventargs).

### Constructors [#constructors_5]

#### `DockContentFloatingEventArgs` [#dockcontentfloatingeventargs_1]

```csharp
public DockContentFloatingEventArgs()
```

### Properties [#properties_5]

#### `Content` [#content_3]

The pane being torn out.

```csharp
public required DockableContent Content  // get, init
```

## `DockDocumentClosedEventArgs` [#dockdocumentclosedeventargs]

Args for [`DockManager.OnDocumentClosed`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockDocumentClosedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_6]

#### `DockDocumentClosedEventArgs` [#dockdocumentclosedeventargs_1]

```csharp
public DockDocumentClosedEventArgs()
```

### Properties [#properties_6]

#### `Document` [#document]

The document that closed.

```csharp
public required DockableContent Document  // get, init
```

## `DockDocumentClosingEventArgs` [#dockdocumentclosingeventargs]

Args for [`DockManager.OnDocumentClosing`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockDocumentClosingEventArgs : DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockCancelEventArgs`](#dockcanceleventargs).

### Constructors [#constructors_7]

#### `DockDocumentClosingEventArgs` [#dockdocumentclosingeventargs_1]

```csharp
public DockDocumentClosingEventArgs()
```

### Properties [#properties_7]

#### `Document` [#document_1]

The document about to be closed.

```csharp
public required DockableContent Document  // get, init
```

## `DockFloatingCloseReason` [#dockfloatingclosereason]

Why a docking floating window closed. Lets [`DockManager.OnFloatingWindowClosed`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager) handlers tell a genuine user close (release per-document resources) apart from a synthetic close that follows a tab migrating to another surface (the content is still alive — do NOT release anything).

```csharp
public enum DockFloatingCloseReason
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks [#remarks_1]

Spec 045 §5.3.5.

### Values [#values]

| Name | Value | Description |
| --- | --- | --- |
| `ContentClosed` | `0` | The window closed and its content is gone — the OS close button, Alt+F4, an app-driven `Close()`, the last tab being closed, or the owning host unmounting. It is safe to release per-document state tied to the content. This is the zero value, so a handler that ignores [`DockFloatingWindowClosedEventArgs.Reason`](#reason) sees the pre-#417 behaviour. Named for the outcome (content gone), not the initiator — it deliberately covers app-driven and host-unmount closes too, and so is distinct from `WindowCloseReason.UserClosed`. |
| `MigratedToHost` | `1` | The window closed synthetically because its last pane was dock-backed into another host (cross-window dock-back). The content is alive and visible in its new dock position — handlers must NOT release resources tied to it. |
| `MigratedToFloat` | `2` | The window closed synthetically because its last pane was re-torn-out or dropped into a different floating window. The content is alive in the destination float — handlers must NOT release resources tied to it. |

## `DockFloatingWindowClosedEventArgs` [#dockfloatingwindowclosedeventargs]

Args for [`DockManager.OnFloatingWindowClosed`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockFloatingWindowClosedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_8]

#### `DockFloatingWindowClosedEventArgs` [#dockfloatingwindowclosedeventargs_1]

```csharp
public DockFloatingWindowClosedEventArgs()
```

### Properties [#properties_8]

#### `Content` [#content_4]

The pane that was inside the floating window when it closed (best-effort).

```csharp
public DockableContent? Content  // get, init
```

#### `Reason` [#reason]

Why the window closed. [`DockFloatingCloseReason.ContentClosed`](#values) uniquely identifies a true close; the `Migrated*` values mark the synthetic close that pairs a cross-window dock-back / re-tear-out, where [`DockFloatingWindowClosedEventArgs.Content`](#content_4) is still alive elsewhere and must not be released.

```csharp
public required DockFloatingCloseReason Reason  // get, init
```

**Remarks**

Spec 045 §5.3.5. See issue #417.

## `DockFloatingWindowCreatedEventArgs` [#dockfloatingwindowcreatedeventargs]

Args for [`DockManager.OnFloatingWindowCreated`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockFloatingWindowCreatedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_9]

#### `DockFloatingWindowCreatedEventArgs` [#dockfloatingwindowcreatedeventargs_1]

```csharp
public DockFloatingWindowCreatedEventArgs()
```

### Properties [#properties_9]

#### `DraggedSource` [#draggedsource]

The pane that spawned the floating window (null when restored from JSON).

```csharp
public DockableContent? DraggedSource  // get, init
```

## `DockLayoutChangedEventArgs` [#docklayoutchangedeventargs]

Args for [`DockManager.OnLayoutChanged`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockLayoutChangedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_10]

#### `DockLayoutChangedEventArgs` [#docklayoutchangedeventargs_1]

```csharp
public DockLayoutChangedEventArgs()
```

## `DockLayoutChangingEventArgs` [#docklayoutchangingeventargs]

Args for [`DockManager.OnLayoutChanging`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockLayoutChangingEventArgs : DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockCancelEventArgs`](#dockcanceleventargs).

### Constructors [#constructors_11]

#### `DockLayoutChangingEventArgs` [#docklayoutchangingeventargs_1]

```csharp
public DockLayoutChangingEventArgs()
```

## `DockToolWindowClosedEventArgs` [#docktoolwindowclosedeventargs]

Args for [`DockManager.OnToolWindowClosed`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockToolWindowClosedEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_12]

#### `DockToolWindowClosedEventArgs` [#docktoolwindowclosedeventargs_1]

```csharp
public DockToolWindowClosedEventArgs()
```

### Properties [#properties_10]

#### `ToolWindow` [#toolwindow]

The tool window that closed.

```csharp
public required DockableContent ToolWindow  // get, init
```

## `DockToolWindowClosingEventArgs` [#docktoolwindowclosingeventargs]

Args for [`DockManager.OnToolWindowClosing`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockToolWindowClosingEventArgs : DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockCancelEventArgs`](#dockcanceleventargs).

### Constructors [#constructors_13]

#### `DockToolWindowClosingEventArgs` [#docktoolwindowclosingeventargs_1]

```csharp
public DockToolWindowClosingEventArgs()
```

### Properties [#properties_11]

#### `ToolWindow` [#toolwindow_1]

The tool window about to be closed.

```csharp
public required DockableContent ToolWindow  // get, init
```

## `DockToolWindowHiddenEventArgs` [#docktoolwindowhiddeneventargs]

Args for [`DockManager.OnToolWindowHidden`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockToolWindowHiddenEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors [#constructors_14]

#### `DockToolWindowHiddenEventArgs` [#docktoolwindowhiddeneventargs_1]

```csharp
public DockToolWindowHiddenEventArgs()
```

### Properties [#properties_12]

#### `ToolWindow` [#toolwindow_2]

The tool window that auto-hid.

```csharp
public required DockableContent ToolWindow  // get, init
```

## `DockToolWindowHidingEventArgs` [#docktoolwindowhidingeventargs]

Args for [`DockManager.OnToolWindowHiding`](/microsoft-ui-reactor/main/reference/elements/docking#dockmanager).

```csharp
public sealed class DockToolWindowHidingEventArgs : DockCancelEventArgs
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`DockCancelEventArgs`](#dockcanceleventargs).

### Constructors [#constructors_15]

#### `DockToolWindowHidingEventArgs` [#docktoolwindowhidingeventargs_1]

```csharp
public DockToolWindowHidingEventArgs()
```

### Properties [#properties_13]

#### `ToolWindow` [#toolwindow_3]

The tool window about to auto-hide.

```csharp
public required DockableContent ToolWindow  // get, init
```
