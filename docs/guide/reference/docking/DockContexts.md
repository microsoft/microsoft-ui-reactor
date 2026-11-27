# DockContexts

Context slots used to surface docking state to function components via the [`DockHooks`](../hooks/index.md) hooks. Spec 045 §5.3.11; tracking §2.17.

```csharp
public static class DockContexts
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Docking Windows](../../docking.md)

## Remarks

Each slice of dock state is a separate [`Context<T>`](../components/Context.md) so a consumer that hooks one slice (e.g. `UseActivePaneKey`) only re-renders when that specific slice changes — selector-style precision per the spec, achieved by Reactor's existing context-change scope (which only invalidates components subscribed to the changed context).

## Fields

### `ActivePaneKey`

The active pane key (object equality), or null when no pane is active.

```csharp
public static readonly Context<object?> ActivePaneKey
```

### `Host`

The nearest enclosing host's live [`DockHostModel`](DockHostModel.md), or null outside any host.

```csharp
public static readonly Context<DockHostModel?> Host
```

### `LayoutSnapshot`

Wide-net layout snapshot — re-renders consumer on any structural change. Use sparingly.

```csharp
public static readonly Context<DockLayoutSnapshot?> LayoutSnapshot
```

### `Pane`

Identity of the enclosing pane (Title + Key + content reference), or null outside any pane subtree.

```csharp
public static readonly Context<DockPaneInfo?> Pane
```

### `PaneState`

Per-pane current dock state (Docked/Floating/AutoHidden/...). Default [`DockPaneState.Docked`](layout-model.md#values_1).

```csharp
public static readonly Context<DockPaneState> PaneState
```
