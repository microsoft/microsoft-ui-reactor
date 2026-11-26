# Control modifiers: Docking

Modifiers that only the controls in the Docking area have.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Docking Windows](../../docking.md)

## `ActiveContentChanged`

Wires the active-content-changed handler. Passing `null` clears.

```csharp
DockManager ActiveContentChanged(this DockManager el, Action<DockActiveContentChangedEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockActiveContentChangedEventArgs>?` |  |

## `ContentDocked`

Wires the content-docked handler. Passing `null` clears.

```csharp
DockManager ContentDocked(this DockManager el, Action<DockContentDockedEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockContentDockedEventArgs>?` |  |

## `ContentDocking`

Wires the cancellable content-docking handler. Passing `null` clears.

```csharp
DockManager ContentDocking(this DockManager el, Action<DockContentDockingEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockContentDockingEventArgs>?` |  |

## `ContentFloated`

Wires the content-floated handler. Passing `null` clears.

```csharp
DockManager ContentFloated(this DockManager el, Action<DockContentFloatedEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockContentFloatedEventArgs>?` |  |

## `ContentFloating`

Wires the cancellable content-floating (about-to-tear-out) handler. Passing `null` clears.

```csharp
DockManager ContentFloating(this DockManager el, Action<DockContentFloatingEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockContentFloatingEventArgs>?` |  |

## `DocumentClosed`

Wires the document-closed handler. Passing `null` clears.

```csharp
DockManager DocumentClosed(this DockManager el, Action<DockDocumentClosedEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockDocumentClosedEventArgs>?` |  |

## `DocumentClosing`

Wires the cancellable document-closing handler. Passing `null` clears.

```csharp
DockManager DocumentClosing(this DockManager el, Action<DockDocumentClosingEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockDocumentClosingEventArgs>?` |  |

## `DropTargetConfirmed`

Wires the drop-target confirm handler. Receives the confirmed target. Passing `null` clears.

```csharp
DockManager DropTargetConfirmed(this DockManager el, Action<DockTarget>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockTarget>?` |  |

## `DropTargetHovered`

Wires the drop-target hover handler. Receives the hovered target (null = none). Passing `null` clears.

```csharp
DockManager DropTargetHovered(this DockManager el, Action<DockTarget?>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockTarget?>?` |  |

## `DropTargetsDismissed`

Wires the drop-targets-dismissed handler (overlay closed without a target). Passing `null` clears.

```csharp
DockManager DropTargetsDismissed(this DockManager el, Action? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `FloatingWindowClosed`

Wires the floating-window-closed handler. Passing `null` clears.

```csharp
DockManager FloatingWindowClosed(this DockManager el, Action<DockFloatingWindowClosedEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockFloatingWindowClosedEventArgs>?` |  |

## `FloatingWindowCreated`

Wires the floating-window-created handler. Passing `null` clears.

```csharp
DockManager FloatingWindowCreated(this DockManager el, Action<DockFloatingWindowCreatedEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockFloatingWindowCreatedEventArgs>?` |  |

## `LayoutChanged`

Wires the post-mutation layout-changed handler. Passing `null` clears.

```csharp
DockManager LayoutChanged(this DockManager el, Action<DockLayoutChangedEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockLayoutChangedEventArgs>?` |  |

## `LayoutChanging`

Wires the cancellable layout-changing handler. Passing `null` clears.

```csharp
DockManager LayoutChanging(this DockManager el, Action<DockLayoutChangingEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockLayoutChangingEventArgs>?` |  |

## `LiveLayoutChanged`

Wires the live (mid-drag) layout-changed handler. Passing `null` clears.

```csharp
DockManager LiveLayoutChanged(this DockManager el, Action<DockNode?>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockNode?>?` |  |

## `SplitterDragCompleted`

Wires the splitter-drag-completed handler. Passing `null` clears.

```csharp
DockManager SplitterDragCompleted(this DockManager el, Action? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action?` |  |

## `ToolWindowClosed`

Wires the tool-window-closed handler. Passing `null` clears.

```csharp
DockManager ToolWindowClosed(this DockManager el, Action<DockToolWindowClosedEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockToolWindowClosedEventArgs>?` |  |

## `ToolWindowClosing`

Wires the cancellable tool-window-closing handler. Passing `null` clears.

```csharp
DockManager ToolWindowClosing(this DockManager el, Action<DockToolWindowClosingEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockToolWindowClosingEventArgs>?` |  |

## `ToolWindowHidden`

Wires the tool-window-hidden handler. Passing `null` clears.

```csharp
DockManager ToolWindowHidden(this DockManager el, Action<DockToolWindowHiddenEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockToolWindowHiddenEventArgs>?` |  |

## `ToolWindowHiding`

Wires the cancellable tool-window-hiding handler. Passing `null` clears.

```csharp
DockManager ToolWindowHiding(this DockManager el, Action<DockToolWindowHidingEventArgs>? handler)
```

Declared in `DockManagerEventExtensions` (package `Microsoft.UI.Reactor.Advanced`).

**Applies to:** [`DockManager`](../elements/docking.md#dockmanager).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `handler` | `Action<DockToolWindowHidingEventArgs>?` |  |
