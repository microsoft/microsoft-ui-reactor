# ReactorHost

Hosts a Reactor component tree inside a WinUI Window. Manages the render loop: when state changes, re-renders the component and reconciles the virtual tree against the real WinUI control tree.

```csharp
public sealed class ReactorHost : IDisposable
```

Namespace: `Microsoft.UI.Reactor.Hosting`.

**Learn more:** [Reactor for XAML Developers](../../xaml-developers.md)

## Constructors

### `ReactorHost`

```csharp
public ReactorHost(Window window, ILogger? logger = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `window` | `Window` |  |  |
| `logger` | `ILogger?` | `null` |  |

## Properties

### `ContentTarget`

Optional: when set, Reactor renders into this Border instead of Window.Content. Useful for embedding Reactor content in a pre-existing layout (e.g., a test harness with a persistent TitleBar).

```csharp
public Border? ContentTarget  // get, set
```

### `EffectiveRenderErrorHandler`

The handler this host would use right now: its own [`ReactorHost.RenderErrorHandler`](#rendererrorhandler), else [`ReactorApp.DefaultRenderErrorHandler`](ReactorApp.md#defaultrendererrorhandler), else `null` (built-in fallback).

```csharp
public RenderErrorHandler? EffectiveRenderErrorHandler  // get
```

### `IsIdle`

True when the render loop has no pending or in-flight render and no re-render queued for the next tick. Mirrors the early-out predicate inside [`ReactorHost.WaitForIdleAsync`](#waitforidleasync); exposed so test harnesses can drive bounded-convergence wait loops without polling the same fields reflectively. The renderPending read uses Volatile.Read so off-thread callers see a stable snapshot of the Interlocked-managed field. A disposed host reports idle (nothing left to render, ever) to match the disposed-arm short-circuit in [`ReactorHost.WaitForIdleAsync`](#waitforidleasync); otherwise convergence loops would spin to their pass cap during/after host teardown and emit misleading non-idle diagnostics.

```csharp
public bool IsIdle  // get
```

### `OnRenderComplete`

Optional callback invoked after each render pass with phase timings (ms): treeBuildMs, reconcileMs, effectsMs. Used by perf harnesses to capture the breakdown of a Reactor render cycle.

```csharp
public Action<double, double, double>? OnRenderComplete  // get, set
```

### `OwningWindow`

The [`ReactorWindow`](ReactorWindow.md) that owns this host, when the host was constructed by Reactor's window primitive. Null for hosts created directly (test harnesses, [`ReactorHostControl`](ReactorHostControl.md) embeds). (spec 036 §3.4)

```csharp
public ReactorWindow? OwningWindow  // get
```

### `Reconciler`

Provides access to the underlying reconciler for RegisterType calls.

```csharp
public Reconciler Reconciler  // get
```

### `RenderErrorHandler`

Replaces the built-in render-error fallback for this host only. `null` (the default) falls through to [`ReactorApp.DefaultRenderErrorHandler`](ReactorApp.md#defaultrendererrorhandler) at error time. A [`ReactorWindow`](ReactorWindow.md) seeds it from [`WindowSpec.RenderErrorHandler`](WindowSpec.md#rendererrorhandler). See [`RenderErrorHandler`](../components/render-errors.md#rendererrorhandler). (issue #1291)

```csharp
public RenderErrorHandler? RenderErrorHandler  // get, set
```

### `Stats`

Live render performance snapshot, updated every ~1 second. Always available (FPS, frame time). DEBUG builds include per-reconcile element counters.

```csharp
public RenderStats Stats  // get
```

### `Window`

The WinUI Window hosting this Reactor tree. Useful for obtaining the HWND (e.g., for file pickers in unpackaged apps).

```csharp
public Window Window  // get
```

## Methods

### `Dispose`

Releases the resources that this object holds.

```csharp
public void Dispose()
```

### `Mount`

```csharp
public void Mount(Component component)
public void Mount(Func<RenderContext, Element> renderFunc)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `component` | [`Component`](../components/Component.md) |  |
| `renderFunc` | `Func<RenderContext, Element>` |  |

### `WaitForIdleAsync`

Awaits until the render loop is idle (no pending or in-flight renders). Yields to the dispatcher at Low priority in a loop so that Normal-priority RenderLoop callbacks and Low-priority re-renders all complete before returning. Used by test harnesses to replace blind Task.Delay waits.

```csharp
public Task WaitForIdleAsync(int maxYields = 50)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `maxYields` | `int` | `50` |  |
