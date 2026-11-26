# RenderStats

Snapshot of render loop performance, updated every ~1 second by [`ReactorHost`](ReactorHost.md). Always available: FPS, frame timing, render count. DEBUG builds add per-reconcile element counters from the last render pass.

Threading contract: this struct is written field-by-field on the UI thread and exposed via `ref readonly`. Readers on other threads may observe torn values. Always read on the UI thread or copy the entire struct atomically via assignment.

```csharp
public readonly struct RenderStats
```

Namespace: `Microsoft.UI.Reactor.Hosting`.

**Learn more:** [Coming from XAML](../../xaml-developers.md)

## Properties

### `AvgEffectsMs`

Average effects flush time (ms) over the last window.

```csharp
public double AvgEffectsMs  // get, init
```

### `AvgReconcileMs`

Average reconcile time (ms) over the last window.

```csharp
public double AvgReconcileMs  // get, init
```

### `AvgTotalMs`

Average total frame time (ms) over the last window (tree + reconcile + effects).

```csharp
public double AvgTotalMs  // get, init
```

### `AvgTreeBuildMs`

Average tree-build time (ms) over the last window.

```csharp
public double AvgTreeBuildMs  // get, init
```

### `Fps`

Renders per second over the last measurement window.

```csharp
public double Fps  // get, init
```

### `LastCreated`

New UIElements created (mounted) in the last reconcile pass.

```csharp
public int LastCreated  // get, init
```

### `LastDiffed`

Elements diffed in the last reconcile pass.

```csharp
public int LastDiffed  // get, init
```

### `LastModified`

UIElements modified (property updates) in the last reconcile pass.

```csharp
public int LastModified  // get, init
```

### `LastSkipped`

Elements skipped (memo/ShallowEquals) in the last reconcile pass.

```csharp
public int LastSkipped  // get, init
```

### `RendersInWindow`

Number of renders in the last measurement window (~1 second).

```csharp
public int RendersInWindow  // get, init
```

### `TotalRenders`

Total renders since the ReactorHost was created.

```csharp
public long TotalRenders  // get, init
```
