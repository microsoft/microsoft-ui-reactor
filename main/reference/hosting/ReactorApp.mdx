---
title: "ReactorApp"
---

```csharp
public static class ReactorApp
```

Namespace: `Microsoft.UI.Reactor`.

**Learn more:** [Windows](/microsoft-ui-reactor/main/guides/windows)

## Properties [#properties]

### `ActiveHost` [#activehost]

Legacy alias for the host of the first window opened in this process. (spec 036 §4.3 / §12.4)

```csharp
public static ReactorHost? ActiveHost  // get
```

> **Obsolete.** Use ReactorApp.PrimaryWindow.Host or ReactorApp.Windows.

### `AppLogger` [#applogger]

```csharp
public static ILogger? AppLogger  // get, set
```

### `DefaultRenderErrorHandler` [#defaultrendererrorhandler]

App-wide replacement for the built-in render-error fallback, which shows the full exception text (type, message, stack trace). `null` (the default) keeps the built-in fallback. (issue #1291)

```csharp
public static RenderErrorHandler? DefaultRenderErrorHandler  // get, set
```

**Remarks**

Consulted at error time by every [`ReactorHost`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost) and [`ReactorHostControl`](/microsoft-ui-reactor/main/reference/hosting/ReactorHostControl) that has no handler of its own — including windows opened after this is set, tray flyouts, and embedded host controls. A host overrides it with [`ReactorHost.RenderErrorHandler`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost#rendererrorhandler), and a window with [`WindowSpec.RenderErrorHandler`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#rendererrorhandler).

Covers every place Reactor catches a render-time exception outside an `ErrorBoundary`: the root render, child component renders, the reconcile pass, effect flushes, and effect cleanups during dispose. See [`RenderErrorHandler`](/microsoft-ui-reactor/main/reference/components/render-errors#rendererrorhandler).

### `DevtoolsEnabled` [#devtoolsenabled]

```csharp
public static bool DevtoolsEnabled  // get
```

### `PrimaryWindow` [#primarywindow]

The first window opened during this process's startup callback, or `null` when none has been opened (tray-only / pre-startup).

```csharp
public static ReactorWindow? PrimaryWindow  // get
```

### `ShutdownPolicy` [#shutdownpolicy]

Process-shutdown policy. Defaults to [`ShutdownPolicy.OnPrimaryWindowClosed`](/microsoft-ui-reactor/main/reference/hosting/window-options#values_2).

```csharp
public static ShutdownPolicy ShutdownPolicy  // get, set
```

**Remarks**

Settable from any thread and at any point in the process lifetime, and read back immediately. Reactor takes ownership of the event loop at startup (see [`ReactorApp.TakeOwnershipOfDispatcherLifetime`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp)), so this value is consulted when a decision is due rather than mirrored into platform state — there is no window in which the policy and the platform can disagree.

Two moments consult it. A surface close reaches [`ReactorApp.EvaluateShutdownPolicy`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp), which reads the value then, so a change made any time beforehand counts. Startup is the exception: [`ReactorApp.ExitIfStartupOpenedNoSurface`](/microsoft-ui-reactor/main/reference/hosting/ReactorApp) runs the instant the launch path finishes, so a zero-surface startup is judged against whatever the policy holds at that instant. Set it before `Run`, or on the UI thread before the startup callback returns — a write posted from another thread during startup can land too late to be seen.

### `TrayIcons` [#trayicons]

Snapshot of every open [`ReactorTrayIcon`](/microsoft-ui-reactor/main/reference/hosting/tray-icon#reactortrayicon). Copy-on-write; safe to enumerate from any thread. (spec 036 §11.4)

```csharp
public static IReadOnlyList<ReactorTrayIcon> TrayIcons  // get
```

### `UIDispatcher` [#uidispatcher]

The UI [`DispatcherQueue`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.dispatching.dispatcherqueue) captured at `OnLaunched` — null until the first window has been bootstrapped.

```csharp
public static DispatcherQueue? UIDispatcher  // get
```

### `WindowPersistenceStore` [#windowpersistencestore]

Process-wide store backing [`WindowSpec.PersistenceId`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#persistenceid). Defaults are picked lazily on first window open: packaged apps get [`PackagedSettingsStore`](/microsoft-ui-reactor/main/reference/hosting/persistence#packagedsettingsstore); unpackaged apps get [`JsonFileStore`](/microsoft-ui-reactor/main/reference/hosting/persistence#jsonfilestore). Setting this property after the first `OpenWindow` throws — windows that already loaded their placement from the previous store would get a half-populated state. (spec 036 §8)

```csharp
public static IWindowPersistenceStore? WindowPersistenceStore  // get, set
```

**Remarks**

Unpackaged apps can opt into [`UnpackagedAppDataStore`](/microsoft-ui-reactor/main/reference/hosting/persistence#unpackagedappdatastore) by assigning one here before the first `OpenWindow`. It persists under the Windows App SDK's app-data root for an explicit publisher/product pair instead of the entry process's name, so saved layouts survive an executable rename. It is not the auto-detected default because the two stores key their data differently, so switching does not migrate existing saved layouts. (spec 063 §4)

### `Windows` [#windows]

Snapshot of every open [`ReactorWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow). Copy-on-write; safe to enumerate from any thread.

```csharp
public static IReadOnlyList<ReactorWindow> Windows  // get
```

## Methods [#methods]

### `Exit` [#exit]

Exit the process. UI-thread only. With `exitCode` set to its default (`0`), routes through [`Application.Exit`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.application.exit) so WinUI gets a clean unwind. With a non-zero exit code, calls [`Application.Exit`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.application.exit) first to release resources, then [`Environment.Exit`](https://learn.microsoft.com/dotnet/api/system.environment.exit) with the requested code so the parent process sees it (assigning [`Environment.ExitCode`](https://learn.microsoft.com/dotnet/api/system.environment.exitcode) would only take effect on a natural managed-entry-point return, which an app under `Application.Start` does not perform).

```csharp
public static void Exit(int exitCode = 0)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `exitCode` | `int` | `0` |  |

### `FindTrayIcon` [#findtrayicon]

Look up an open tray icon by [`WindowKey`](/microsoft-ui-reactor/main/reference/hosting/WindowKey).

```csharp
public static ReactorTrayIcon? FindTrayIcon(WindowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`WindowKey`](/microsoft-ui-reactor/main/reference/hosting/WindowKey) |  |

### `FindWindow` [#findwindow]

Look up an open window by [`WindowKey`](/microsoft-ui-reactor/main/reference/hosting/WindowKey).

```csharp
public static ReactorWindow? FindWindow(WindowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`WindowKey`](/microsoft-ui-reactor/main/reference/hosting/WindowKey) |  |

### `OpenTrayIcon` [#opentrayicon]

Open a system-tray icon. UI-thread only. The returned handle stays alive until [`ReactorTrayIcon.Close`](/microsoft-ui-reactor/main/reference/hosting/tray-icon#close) / [`ReactorTrayIcon.Dispose`](/microsoft-ui-reactor/main/reference/hosting/tray-icon#dispose) is called or the process exits. (spec 036 §11.4)

```csharp
public static ReactorTrayIcon OpenTrayIcon(TrayIconSpec spec)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `spec` | [`TrayIconSpec`](/microsoft-ui-reactor/main/reference/hosting/tray-icon#trayiconspec) |  |

### `OpenWindow` [#openwindow]

#### `OpenWindow(WindowSpec, Func<Component>, Action<ReactorHost>?)` [#openwindowwindowspec-funccomponent-actionreactorhost]

Open a window with a [`Component`](/microsoft-ui-reactor/main/reference/components/Component) root. UI-thread only. (spec 036 §4.3)

```csharp
public static ReactorWindow OpenWindow(WindowSpec spec, Func<Component> root, Action<ReactorHost>? configure = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `spec` | [`WindowSpec`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec) |  | Declarative description of the window's chrome. |
| `root` | `Func<Component>` |  | Factory invoked once to materialize the root component. |
| `configure` | `Action<ReactorHost>?` | `null` | Optional callback invoked on the UI thread **before** the root mounts. Use it for pre-mount setup that needs the live [`ReactorHost`](/microsoft-ui-reactor/main/reference/hosting/ReactorHost) (logger wiring, custom reconciler registrations, host-level event hooks). Mirror of the `configure` parameter on the legacy [`ReactorApp.Run`](#runtrootstring-double-double-bool-windowicon-actionreactorhost) entry — secondary windows now have the same escape hatch as the primary. |

#### `OpenWindow(WindowSpec, Func<RenderContext, Element>, Action<ReactorHost>?)` [#openwindowwindowspec-funcrendercontext-element-actionreactorhost]

Open a window with a render-function root. UI-thread only. See the [`ReactorApp.OpenWindow`](#openwindowwindowspec-funccomponent-actionreactorhost) overload for `configure` semantics.

```csharp
public static ReactorWindow OpenWindow(WindowSpec spec, Func<RenderContext, Element> render, Action<ReactorHost>? configure = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `spec` | [`WindowSpec`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec) |  |  |
| `render` | `Func<RenderContext, Element>` |  |  |
| `configure` | `Action<ReactorHost>?` | `null` |  |

### `RegisterAllBuiltIns` [#registerallbuiltins]

Registers every built-in Reactor control handler with the global [`ControlRegistry`](/microsoft-ui-reactor/main/reference/extensibility/ControlRegistry) in one call.

```csharp
public static void RegisterAllBuiltIns()
```

**Remarks**

**When to call.** Call this once at process startup if your app constructs element records directly (the `new TextBlockElement(…) { … }` hot-loop idiom documented in `docs/guide/advanced.md` "Hot loops") rather than exclusively through factory methods. Factory methods already self-register on first call, so an app that only ever builds UI through the fluent/factory DSL does not need this.

**Trimming.** This method is the deliberate opt-out of spec-048's lazy-registration trim story: calling it roots the entire built-in catalog, so a NativeAOT/trimmed build that calls it keeps every built-in handler and WinUI control. Apps that want a minimal trimmed binary should *not* call this and instead let each factory register only the controls it uses — or register specific controls explicitly: [`ControlRegistry.Register`](/microsoft-ui-reactor/main/reference/extensibility/ControlRegistry#register) for control-backed handlers, or [`ControlRegistry.RegisterDecorator`](/microsoft-ui-reactor/main/reference/extensibility/ControlRegistry#registerdecorator) for decorator-backed elements (overlays, validation, composite wrappers). Because this is an ordinary method (not a `[ModuleInitializer]`), the trimmer removes it — and everything it names — when it is unreachable.

Registration is idempotent and process-wide; calling it more than once is a cheap no-op after the first call.

### `RegisterControlAssembly` [#registercontrolassembly]

#### `RegisterControlAssembly(IXamlMetadataProvider)` [#registercontrolassemblyixamlmetadataprovider]

Registers a XAML metadata provider so its types are visible to the WinUI XAML loader for this process. Required when a third-party control library is referenced from a Reactor app that has no XAML files of its own (and therefore no compiler-generated provider that would auto-chain to the library). Call before [`ReactorApp.Run`](#runtrootstring-double-double-bool-windowicon-actionreactorhost). Idempotent (same instance is added at most once) and thread-safe. See https://github.com/microsoft/microsoft-ui-reactor/issues/142.

```csharp
public static void RegisterControlAssembly(IXamlMetadataProvider provider)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `provider` | `IXamlMetadataProvider` |  |

#### `RegisterControlAssembly(Assembly)` [#registercontrolassemblyassembly]

Convenience overload that locates the XAML-compiler-generated `IXamlMetadataProvider` in `assembly` (the type the XAML compiler emits when the project has at least one XAML file) and registers it. Throws if no such provider is found — pass the [`IXamlMetadataProvider`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.markup.ixamlmetadataprovider) instance directly if your library uses a non-standard provider type. Reflection discovery does not preserve the provider under trimming/NativeAOT; use the instance overload with a statically constructed provider in that case.

```csharp
public static void RegisterControlAssembly(Assembly assembly)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `assembly` | `Assembly` |  |

### `Run` [#run]

#### `Run(Action<ReactorAppContext>)` [#runactionreactorappcontext]

Multi-window startup entry. The `startup` callback runs on the UI thread after WinUI bootstraps and before any default window is opened. Open windows or tray icons (Phase 8) directly from inside the callback. (spec 036 §4.3 / §6.1)

```csharp
public static void Run(Action<ReactorAppContext> startup)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `startup` | `Action<ReactorAppContext>` |  |

#### `Run<TRoot>(WindowSpec, Action<ReactorHost>?)` [#runtrootwindowspec-actionreactorhost]

Launches the app with a full [`WindowSpec`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec) for the primary window, so it gets the same declarative surface as a secondary window opened via [`ReactorApp.OpenWindow`](#openwindowwindowspec-funccomponent-actionreactorhost) — icon, min/max size, backdrop, corner style, start position, and placement persistence.

```csharp
public static void Run<TRoot>(WindowSpec spec, Action<ReactorHost>? configure = null) where TRoot : Component, new()
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `spec` | [`WindowSpec`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec) |  |  |
| `configure` | `Action<ReactorHost>?` | `null` |  |

**Remarks**

Under `--devtools run` the preview host substitutes its own window; it reads title and size from the spec and ignores the remaining fields, exactly as it does for the flat overloads.

#### `Run(WindowSpec, Func<RenderContext, Element>, Action<ReactorHost>?)` [#runwindowspec-funcrendercontext-element-actionreactorhost]

Launches the app with a render function and a full [`WindowSpec`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec) for the primary window. See [`ReactorApp.Run`](#runtrootwindowspec-actionreactorhost) for the spec semantics.

```csharp
public static void Run(WindowSpec spec, Func<RenderContext, Element> rootRender, Action<ReactorHost>? configure = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `spec` | [`WindowSpec`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec) |  |  |
| `rootRender` | `Func<RenderContext, Element>` |  |  |
| `configure` | `Action<ReactorHost>?` | `null` |  |

#### `Run<TRoot>(string, double?, double?, bool, WindowIcon?, Action<ReactorHost>?)` [#runtrootstring-double-double-bool-windowicon-actionreactorhost]

Launches the app. `width` / `height` are DIPs; leave them `null` (the default) to let the OS pick the initial window size, exactly as a plain XAML `Window` does. Enable the `Reactor.DevtoolsSupport` runtime host configuration switch in DEBUG builds to allow the `mur devtools` / `--devtools` surface: component switching via VS Code, MCP agent tools, and component listing.

```csharp
public static void Run<TRoot>(string title = "Reactor App", double? width = null, double? height = null, bool fullScreen = false, WindowIcon? icon = null, Action<ReactorHost>? configure = null) where TRoot : Component, new()
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `title` | `string` | `"Reactor App"` | Window caption text. |
| `width` | `double?` | `null` | Initial DIP width; `null` defers to the OS. |
| `height` | `double?` | `null` | Initial DIP height; `null` defers to the OS. |
| `fullScreen` | `bool` | `false` | Open with the full-screen presenter. |
| `icon` | [`WindowIcon?`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon) | `null` | Optional window icon — the Win32 `HICON` Windows shows in the window's caption and the Alt-Tab switcher. Use an `.ico`: build it with [`WindowIcon.FromPath`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#frompath) for a file beside the app, or [`WindowIcon.FromResource`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon#fromresource) for a packaged `ms-appx:///` asset. When omitted, Reactor falls back to `Assets\AppIcon.ico` and then to the executable's embedded icon. Distinct from `TitleBar(...).Icon(...)`, which sets the app mark drawn inside the window. Note that in a **packaged** app this does not change the taskbar button or Task Manager — the shell draws those from package identity and the manifest's `Square44x44Logo`; see [`WindowSpec.Icon`](/microsoft-ui-reactor/main/reference/hosting/WindowSpec#icon). |
| `configure` | `Action<ReactorHost>?` | `null` | Optional callback invoked on the UI thread before the root mounts. |

#### `Run(string, Func<RenderContext, Element>, double?, double?, bool, WindowIcon?, Action<ReactorHost>?)` [#runstring-funcrendercontext-element-double-double-bool-windowicon-actionreactorhost]

Launches the app with a render function instead of a Component subclass. `width` / `height` are DIPs; leave them `null` to let the OS pick the initial window size. See the generic overload for devtools activation semantics.

```csharp
public static void Run(string title, Func<RenderContext, Element> rootRender, double? width = null, double? height = null, bool fullScreen = false, WindowIcon? icon = null, Action<ReactorHost>? configure = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `title` | `string` |  | Window caption text. |
| `rootRender` | `Func<RenderContext, Element>` |  | Render function for the window's root content. |
| `width` | `double?` | `null` | Initial DIP width; `null` defers to the OS. |
| `height` | `double?` | `null` | Initial DIP height; `null` defers to the OS. |
| `fullScreen` | `bool` | `false` | Open with the full-screen presenter. |
| `icon` | [`WindowIcon?`](/microsoft-ui-reactor/main/reference/hosting/WindowIcon) | `null` | Optional window icon. See [`ReactorApp.Run`](#runtrootstring-double-double-bool-windowicon-actionreactorhost). |
| `configure` | `Action<ReactorHost>?` | `null` | Optional callback invoked on the UI thread before the root mounts. |

### `TryRegisterControlAssembly` [#tryregistercontrolassembly]

Tolerant variant of [`ReactorApp.RegisterControlAssembly`](#registercontrolassemblyassembly): registers the assembly's XAML metadata provider if it has one, and silently does nothing (returning `false`) if it does not. A pure-code control library — e.g. code-only panels with no XAML files (and therefore no compiler-generated provider) — has nothing to register and needs nothing. Reflection discovery does not preserve the provider under trimming/NativeAOT. Generated wrappers use the instance overload when a suitable provider is visible at compile time, and this method only as a best-effort fallback.

```csharp
public static bool TryRegisterControlAssembly(Assembly assembly)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `assembly` | `Assembly` |  |

## Events [#events]

### `TrayIconClosed` [#trayiconclosed]

Fires on the UI thread when a [`ReactorTrayIcon`](/microsoft-ui-reactor/main/reference/hosting/tray-icon#reactortrayicon) closes.

```csharp
public static event EventHandler<ReactorTrayIcon>? TrayIconClosed
```

### `TrayIconOpened` [#trayiconopened]

Fires on the UI thread when a [`ReactorTrayIcon`](/microsoft-ui-reactor/main/reference/hosting/tray-icon#reactortrayicon) opens.

```csharp
public static event EventHandler<ReactorTrayIcon>? TrayIconOpened
```

### `WindowClosed` [#windowclosed]

Fires on the UI thread when a [`ReactorWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow) closes.

```csharp
public static event EventHandler<ReactorWindow>? WindowClosed
```

### `WindowOpened` [#windowopened]

Fires on the UI thread when a [`ReactorWindow`](/microsoft-ui-reactor/main/reference/hosting/ReactorWindow) opens.

```csharp
public static event EventHandler<ReactorWindow>? WindowOpened
```
