# Elements: Docking

The dock manager and its events, from the Microsoft.UI.Reactor.Advanced package.

Element records are immutable descriptions of a control. You usually create them with a factory method and change them with modifiers.

**Learn more:** [Docking Windows](../../docking.md)

## `DockManager`

A container element that hosts a tree of docked panes inside a Reactor shell. Phase 1 reconciles to a single `WinUI.Dock.DockManager` XAML control via the Reactor.Docking.Xaml wrapper assembly; Phase 2 swaps the implementation to a Reactor-native renderer without changing this public surface.

```csharp
public sealed record DockManager : Element
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`. Inherits from [`Element`](Element.md).

**Modifiers:** [`ActiveContentChanged`](../modifiers/docking.md#activecontentchanged), [`ContentDocked`](../modifiers/docking.md#contentdocked), [`ContentDocking`](../modifiers/docking.md#contentdocking), [`ContentFloated`](../modifiers/docking.md#contentfloated), [`ContentFloating`](../modifiers/docking.md#contentfloating), [`DocumentClosed`](../modifiers/docking.md#documentclosed), [`DocumentClosing`](../modifiers/docking.md#documentclosing), [`DropTargetConfirmed`](../modifiers/docking.md#droptargetconfirmed), [`DropTargetHovered`](../modifiers/docking.md#droptargethovered), [`DropTargetsDismissed`](../modifiers/docking.md#droptargetsdismissed), [`FloatingWindowClosed`](../modifiers/docking.md#floatingwindowclosed), [`FloatingWindowCreated`](../modifiers/docking.md#floatingwindowcreated), [`LayoutChanged`](../modifiers/docking.md#layoutchanged), [`LayoutChanging`](../modifiers/docking.md#layoutchanging), [`LiveLayoutChanged`](../modifiers/docking.md#livelayoutchanged), [`SplitterDragCompleted`](../modifiers/docking.md#splitterdragcompleted), [`ToolWindowClosed`](../modifiers/docking.md#toolwindowclosed), [`ToolWindowClosing`](../modifiers/docking.md#toolwindowclosing), [`ToolWindowHidden`](../modifiers/docking.md#toolwindowhidden), [`ToolWindowHiding`](../modifiers/docking.md#toolwindowhiding)

| Property | Type | Description |
| --- | --- | --- |
| `ActiveDocument` | [`DockableContent?`](../docking/layout-model.md#dockablecontent) | The currently-active document. Resolved by [`DockableContent.Key`](../docking/layout-model.md#key_1) equality against panes in [`DockManager.Layout`](#dockmanager); mismatched keys leave activation untouched. |
| `Adapter` | [`IDockAdapter?`](../docking/IDockAdapter.md) | Optional adapter for app-controlled rehydration of pane content and floating-window chrome. See spec 045 §4.3 / §4.4 and [`IDockAdapter`](../docking/IDockAdapter.md). |
| `Behavior` | [`IDockBehavior?`](../docking/IDockBehavior.md) | Optional behavior hook for app-side observation of dock / float events. Phase 2 collapses this interface into the per-event Action props declared below (see [`DockManager.OnContentDocked`](#dockmanager) et al.); the interface stays as a one-release `[Obsolete]` forwarder (spec 045 §5.3.5). |
| `BottomSide` | `IReadOnlyList<DockableContent>?` | Tool windows pinned to the bottom edge (auto-hide). |
| `Layout` | [`DockNode?`](../docking/layout-model.md#docknode) | The root of the dock node tree. Null = empty layout. |
| `LayoutSchemaVersion` | `int` | Schema version for persisted layout JSON. Phase 1 emits version 1; Phase 2 introduces version 2 with migrations registered via [`IDockLayoutMigration`](../docking/IDockLayoutMigration.md) (spec 045 §5.3.4, §5.4). |
| `LayoutStrategy` | [`IDockLayoutStrategy?`](../docking/IDockLayoutStrategy.md) | Optional insertion-policy hook applied to programmatic adds. See [`IDockLayoutStrategy`](../docking/IDockLayoutStrategy.md) and spec 045 §5.3.6. |
| `LeftSide` | `IReadOnlyList<DockableContent>?` | Tool windows pinned to the left edge (auto-hide). |
| `OnActiveContentChanged` | `Action<DockActiveContentChangedEventArgs>?` | Fired when the active content changes (tab focus, programmatic activation). |
| `OnContentDocked` | `Action<DockContentDockedEventArgs>?` | Fired after a floating pane is docked back into a host. |
| `OnContentDocking` | `Action<DockContentDockingEventArgs>?` | Fired before a floating pane is docked back into a host; Cancel aborts. |
| `OnContentFloated` | `Action<DockContentFloatedEventArgs>?` | Fired after a pane is torn out into a floating window. |
| `OnContentFloating` | `Action<DockContentFloatingEventArgs>?` | Fired before a pane is torn out into a floating window; Cancel aborts. |
| `OnDocumentClosed` | `Action<DockDocumentClosedEventArgs>?` | Fired after a Document is closed. |
| `OnDocumentClosing` | `Action<DockDocumentClosingEventArgs>?` | Fired before a Document is closed; Cancel aborts. |
| `OnDropTargetConfirmed` | `Action<DockTarget>?` | Optional callback fired when the user confirms a drop-target (click, Enter, drop). The §2.4 drag pipeline subscribes to apply the dock operation; apps may also subscribe for telemetry / undo bookkeeping. |
| `OnDropTargetHovered` | `Action<DockTarget?>?` | Optional callback fired when the overlay's hovered target changes. Null while no target is hovered. Fires at pointer-move rate during a drag; budget per call is ≤ 2 ms (spec §8.1) and the renderer's hot path is allocation-free aside from the args record. |
| `OnDropTargetsDismissed` | `Action?` | Optional callback fired when the overlay is dismissed (Esc, drag cancel). Apps that opened the overlay via [`DockManager.ShowDropTargets`](#dockmanager) should reset it to `false` in response. |
| `OnFloatingWindowClosed` | `Action<DockFloatingWindowClosedEventArgs>?` | Fired after a floating window is closed. |
| `OnFloatingWindowCreated` | `Action<DockFloatingWindowCreatedEventArgs>?` | Fired after a floating window is created. |
| `OnLayoutChanged` | `Action<DockLayoutChangedEventArgs>?` | Fired after a structural layout mutation lands. |
| `OnLayoutChanging` | `Action<DockLayoutChangingEventArgs>?` | Fired before any structural layout mutation lands. |
| `OnLiveLayoutChanged` | `Action<DockNode?>?` | Fires whenever the host's effective layout mutates as a result of a drag/drop operation (§2.4). Carries the new [`DockNode`](../docking/layout-model.md#docknode) root the renderer is now showing — apps can sync their own state (e.g. for live JSON inspection or undo bookkeeping). |
| `OnSplitterDragCompleted` | `Action?` | Fires after a splitter drag completes (and the host's ratio store has been updated). Apps that own [`DockManager.SplitRatios`](#dockmanager) externally can use this to trigger a re-render — the dictionary is shared, so re-reading it on the next render surfaces the new ratios. |
| `OnToolWindowClosed` | `Action<DockToolWindowClosedEventArgs>?` | Fired after a ToolWindow is closed. |
| `OnToolWindowClosing` | `Action<DockToolWindowClosingEventArgs>?` | Fired before a ToolWindow is closed; Cancel aborts. |
| `OnToolWindowHidden` | `Action<DockToolWindowHiddenEventArgs>?` | Fired after a ToolWindow auto-hides. |
| `OnToolWindowHiding` | `Action<DockToolWindowHidingEventArgs>?` | Fired before a ToolWindow auto-hides; Cancel aborts. |
| `OperationLog` | [`DockOperationLog?`](../docking/diagnostics.md#dockoperationlog) | Optional in-memory operation log. When supplied, the host records every state-altering operation (drag begin / hover / confirm / cancel / tear-out, splitter resize, layout change) into this log. Apps use the log for live debugging + replay scrubbing. Spec 045 keeps this scaffolding through P1–P4 per design discussion. |
| `PersistenceId` | `string?` | Stable identifier used to scope persisted layout JSON inside the host `WindowPersistedScope`. Required to survive process restarts. |
| `RightSide` | `IReadOnlyList<DockableContent>?` | Tool windows pinned to the right edge (auto-hide). |
| `ShowDropTargets` | `bool` | When true, the manager renders the drop-target overlay (9 buttons + hover preview rectangle) above the docked content. Spec 045 §2.3 builds the overlay primitive; the §2.4 drag pipeline flips this flag mid-gesture to show the overlay during a tab drag. Apps can set it directly for testing / keyboard-initiated move (§2.10 `Ctrl+Shift+M`). |
| `SplitRatios` | `Dictionary<string, double[]>?` | Optional externally-owned ratio store for split-pane sizing. When supplied, the native renderer uses this dictionary instead of its internal [`DockSplit`](../docking/layout-model.md#docksplit)-ratio cache. Keys are tree-position paths (e.g. `"0"`, `"0/0"`, `"0/1"`); values are per-child ratio arrays summing to ~1.0. |
| `TopSide` | `IReadOnlyList<DockableContent>?` | Tool windows pinned to the top edge (auto-hide). |

**`LayoutStrategy`**

Spec 045 §5.3.6 (Phase 2 addition).

**`OnActiveContentChanged`**

Spec 045 §5.3.5.

**`OnContentDocked`**

Spec 045 §5.3.5.

**`OnContentDocking`**

Spec 045 §5.3.5.

**`OnContentFloated`**

Spec 045 §5.3.5.

**`OnContentFloating`**

Spec 045 §5.3.5.

**`OnDocumentClosed`**

Spec 045 §5.3.5.

**`OnDocumentClosing`**

Spec 045 §5.3.5.

**`OnDropTargetConfirmed`**

Spec 045 §2.3.

**`OnDropTargetHovered`**

Spec 045 §2.3.

**`OnDropTargetsDismissed`**

Spec 045 §2.3.

**`OnFloatingWindowClosed`**

Spec 045 §5.3.5.

**`OnFloatingWindowCreated`**

Spec 045 §5.3.5.

**`OnLayoutChanged`**

Spec 045 §5.3.5.

**`OnLayoutChanging`**

Spec 045 §5.3.5.

**`OnLiveLayoutChanged`**

Spec 045 §2.4. The host still also fires the canonical [`DockManager.OnContentDocked`](#dockmanager) for the per-pane event; this companion event carries the whole tree.

**`OnSplitterDragCompleted`**

Spec 045 §2.1 — companion to [`DockManager.SplitRatios`](#dockmanager) for live introspection.

**`OnToolWindowClosed`**

Spec 045 §5.3.5.

**`OnToolWindowClosing`**

Spec 045 §5.3.5.

**`OnToolWindowHidden`**

Spec 045 §5.3.5.

**`OnToolWindowHiding`**

Spec 045 §5.3.5.

**`OperationLog`**

Spec 045 diagnostic infrastructure.

**`ShowDropTargets`**

Spec 045 §2.3.

**`SplitRatios`**

Spec 045 §2.1 escape hatch. Exposed for app-driven resize (e.g. slider-driven layout demos) and for tests that need to inspect or mutate ratios without going through pointer/keyboard events.

**Remarks**

See spec 045 §4.3 for the committed API surface, §4.4 for the wrapper behavior, §5 for the Phase 2 rewrite, and §6.4 for the Phase 3 rename to `DockHost`.

The element is reconciled by Reactor like any other [`Element`](Element.md): produce a fresh [`DockManager`](#dockmanager) record on every render, and the reconciler will diff the previous tree against the new one and apply the minimum set of mutations.

Persistence: when [`DockManager.PersistenceId`](#dockmanager) is set, the layout JSON is stored under `WindowPersistedScope["docking:<PersistenceId>"]` (spec 036 §8). On mount, persisted layout is restored as a fallback when the declarative [`DockManager.Layout`](#dockmanager) is null.
