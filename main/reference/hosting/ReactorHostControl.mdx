---
title: "ReactorHostControl"
description: "A fully self-contained WinUI ContentControl that hosts a Reactor component tree. Drop this into any vanilla WinUI app — no ReactorApp, ReactorApplication, or special bootstrapping needed. Each instance owns its own Reconciler and render loop."
---

Usage in XAML (`xmlns:reactor="using:Microsoft.UI.Reactor.Hosting"`): \<reactor:ReactorHostControl ComponentType="local:StatsCard" /\> — or declare an empty host and mount it from code-behind — \<reactor:ReactorHostControl x:Name="reactorHost" /\>

Usage in code-behind: reactorHost.Mount(new MyComponent()); — or — reactorHost.Mount(ctx =\> VStack(TextBlock("Hello from Reactor!"))); — or — reactorHost.ComponentFactory = () =\> new MyComponent();

Features: - Thread-safe render batching (setState from any thread) - Low-priority re-enqueue so layout/paint/input aren't starved - Render performance stats (FPS, frame timing) - Automatic theme change detection and re-render - Connected animation flushing - Error boundary with fallback UI - Clean lifecycle via Loaded/Unloaded

```csharp
public sealed class ReactorHostControl : ContentControl, IDisposable
```

Namespace: `Microsoft.UI.Reactor.Hosting`. Inherits from [`ContentControl`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.contentcontrol).

**Learn more:** [Coming from XAML](/microsoft-ui-reactor/main/get-started/xaml-developers)

## Constructors [#constructors]

### `ReactorHostControl` [#reactorhostcontrol_1]

#### `ReactorHostControl()` [#reactorhostcontrol_2]

Creates an empty host. This is the constructor XAML uses (`<reactor:ReactorHostControl ... />`); set [`ReactorHostControl.ComponentType`](#componenttype) or [`ReactorHostControl.ComponentFactory`](#componentfactory), or call `Mount`, to give it a root.

```csharp
public ReactorHostControl()
```

#### `ReactorHostControl(Component?, ILogger?)` [#reactorhostcontrolcomponent-ilogger]

```csharp
public ReactorHostControl(Component? component = null, ILogger? logger = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `component` | [`Component?`](/microsoft-ui-reactor/main/reference/components/Component) | `null` |  |
| `logger` | `ILogger?` | `null` |  |

## Properties [#properties]

### `ComponentFactory` [#componentfactory]

Factory to create the root component. Set this or use Mount() for more control. If set, the component is created and mounted when the control is Loaded. Example: ComponentFactory = () =\> new MyComponent();

```csharp
public Func<Component>? ComponentFactory  // get, set
```

### `ComponentType` [#componenttype]

The root component's type, for declaring a host in XAML: `<reactor:ReactorHostControl ComponentType="local:StatsCard" />`. When the control is Loaded and nothing has been mounted, an instance is created and mounted, receiving [`ReactorHostControl.Props`](#props) like a [`ReactorHostControl.ComponentFactory`](#componentfactory) root. [`ReactorHostControl.ComponentFactory`](#componentfactory) wins when both are set.

```csharp
public Type? ComponentType  // get, set
```

**Remarks**

The instance is created through the app's XAML type information (`Application.Current` as [`IXamlMetadataProvider`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.markup.ixamlmetadataprovider)), not reflection: naming a type in markup makes the XAML compiler generate an activator for its public parameterless constructor, so this stays trim- and AOT-safe. A type that is only ever assigned from code has no such entry, and the host shows an error saying so in place of the root; set [`ReactorHostControl.ComponentFactory`](#componentfactory) there instead.

Read once, at Loaded, like [`ReactorHostControl.ComponentFactory`](#componentfactory); changing it after the root has mounted does not remount. Use [`ReactorHostControl.Mount`](#mountcomponent) to swap roots.

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) | The type does not derive from [`Component`](/microsoft-ui-reactor/main/reference/components/Component), or is abstract or an open generic. |

### `EffectiveRenderErrorHandler` [#effectiverendererrorhandler]

The handler this control would use right now: its own [`ReactorHostControl.RenderErrorHandler`](#rendererrorhandler), else [`ReactorApp.DefaultRenderErrorHandler`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#defaultrendererrorhandler), else `null` (built-in fallback).

```csharp
public RenderErrorHandler? EffectiveRenderErrorHandler  // get
```

### `IsIdle` [#isidle]

True when the render loop has no pending or in-flight render and no re-render queued for the next tick — same contract as [`ReactorHost.IsIdle`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost#isidle). Safe to read from any thread. A disposed control reports idle: it will never render again.

```csharp
public bool IsIdle  // get
```

### `OnRenderComplete` [#onrendercomplete]

Optional callback invoked after each render pass with phase timings (ms): treeBuildMs, reconcileMs, effectsMs. Used by perf harnesses to capture the breakdown of a Reactor render cycle.

```csharp
public Action<double, double, double>? OnRenderComplete  // get, set
```

### `Props` [#props]

Optional props to pass to the root component created by ComponentFactory.

```csharp
public object? Props  // get, set
```

### `Reconciler` [#reconciler]

Provides access to the underlying reconciler for RegisterType calls.

```csharp
public Reconciler Reconciler  // get
```

### `RenderErrorHandler` [#rendererrorhandler]

Replaces the built-in render-error fallback for this control only. `null` (the default) falls through to [`ReactorApp.DefaultRenderErrorHandler`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp#defaultrendererrorhandler) at error time. See [`RenderErrorHandler`](/microsoft-ui-reactor/main/reference/components/render-errors#rendererrorhandler). (issue #1291)

```csharp
public RenderErrorHandler? RenderErrorHandler  // get, set
```

### `Stats` [#stats]

Live render performance snapshot, updated every ~1 second. Always available (FPS, frame time). DEBUG builds include per-reconcile element counters.

```csharp
public RenderStats Stats  // get
```

**Remarks**

Returned by value, unlike [`ReactorHost.Stats`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost#stats): the XAML compiler emits type metadata for every public property of a control declared in markup, and a `ref`-returning property becomes `typeof(RenderStats&)` in the app's generated `XamlTypeInfo.g.cs`, which does not compile. The copy is a few dozen bytes, read about once a second.

## Methods [#methods]

### `Dispose` [#dispose]

Releases the resources that this object holds.

```csharp
public void Dispose()
```

### `Mount` [#mount]

#### `Mount(Component)` [#mountcomponent]

Mount a Component instance directly. Starts the render loop immediately.

```csharp
public void Mount(Component component)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `component` | [`Component`](/microsoft-ui-reactor/main/reference/components/Component) |  |

#### `Mount(Func<RenderContext, Element>)` [#mountfuncrendercontext-element]

Mount a function component. Starts the render loop immediately.

```csharp
public void Mount(Func<RenderContext, Element> renderFunc)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `renderFunc` | `Func<RenderContext, Element>` |  |

### `WaitForIdleAsync` [#waitforidleasync]

Completes once the render loop is idle (see [`ReactorHostControl.IsIdle`](#isidle)) — after a `setState`, a [`ReactorHostControl.Mount`](#mountcomponent), or any other change that schedules a render, await this before reading the realized tree back. Yields to the dispatcher at Low priority so Normal-priority renders and Low-priority re-renders all complete first; gives up after `maxYields` yields, or immediately if the dispatcher is shutting down. Same contract as [`ReactorHost.WaitForIdleAsync`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost#waitforidleasync); callable from any thread.

```csharp
public Task WaitForIdleAsync(int maxYields = 50)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `maxYields` | `int` | `50` |  |
