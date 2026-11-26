---
title: "RenderStats"
seo:
  description: "Snapshot of render loop performance, updated every ~1 second by ReactorHost. Always available: FPS, frame timing, render count. DEBUG builds add per-reconcile element counters from the last render pass."
---

Snapshot of render loop performance, updated every ~1 second by [`ReactorHost`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost). Always available: FPS, frame timing, render count. DEBUG builds add per-reconcile element counters from the last render pass.

Threading contract: this struct is written field-by-field on the UI thread and exposed via `ref readonly`. Readers on other threads may observe torn values. Always read on the UI thread or copy the entire struct atomically via assignment.

```csharp
public readonly struct RenderStats
```

Namespace: `Microsoft.UI.Reactor.Hosting`.

**Learn more:** [Coming from XAML](/microsoft-ui-reactor/main/get-started/xaml-developers)

## Properties [#properties]

### `AvgEffectsMs` [#avgeffectsms]

Average effects flush time (ms) over the last window.

```csharp
public double AvgEffectsMs  // get, init
```

### `AvgReconcileMs` [#avgreconcilems]

Average reconcile time (ms) over the last window.

```csharp
public double AvgReconcileMs  // get, init
```

### `AvgTotalMs` [#avgtotalms]

Average total frame time (ms) over the last window (tree + reconcile + effects).

```csharp
public double AvgTotalMs  // get, init
```

### `AvgTreeBuildMs` [#avgtreebuildms]

Average tree-build time (ms) over the last window.

```csharp
public double AvgTreeBuildMs  // get, init
```

### `Fps` [#fps]

Renders per second over the last measurement window.

```csharp
public double Fps  // get, init
```

### `LastCreated` [#lastcreated]

New UIElements created (mounted) in the last reconcile pass.

```csharp
public int LastCreated  // get, init
```

### `LastDiffed` [#lastdiffed]

Elements diffed in the last reconcile pass.

```csharp
public int LastDiffed  // get, init
```

### `LastModified` [#lastmodified]

UIElements modified (property updates) in the last reconcile pass.

```csharp
public int LastModified  // get, init
```

### `LastSkipped` [#lastskipped]

Elements skipped (memo/ShallowEquals) in the last reconcile pass.

```csharp
public int LastSkipped  // get, init
```

### `RendersInWindow` [#rendersinwindow]

Number of renders in the last measurement window (~1 second).

```csharp
public int RendersInWindow  // get, init
```

### `TotalRenders` [#totalrenders]

Total renders since the ReactorHost was created.

```csharp
public long TotalRenders  // get, init
```
