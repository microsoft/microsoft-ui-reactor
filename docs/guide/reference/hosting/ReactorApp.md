# ReactorApp

```csharp
public static class ReactorApp
```

Namespace: `Microsoft.UI.Reactor`.

**Learn more:** [Windows](../../windows.md)

## Properties

### `ActiveHost`

Legacy alias for the host of the first window opened in this process. (spec 036 §4.3 / §12.4)

```csharp
public static ReactorHost? ActiveHost  // get
```

> **Obsolete.** Use ReactorApp.PrimaryWindow.Host or ReactorApp.Windows.

### `AppLogger`

```csharp
public static ILogger? AppLogger  // get, set
```

### `DefaultRenderErrorHandler`

App-wide replacement for the built-in render-error fallback, which shows the full exception text (type, message, stack trace). `null` (the default) keeps the built-in fallback. (issue #1291)

```csharp
public static RenderErrorHandler? DefaultRenderErrorHandler  // get, set
```

**Remarks**

Consulted at error time by every [`ReactorHost`](ReactorHost.md) and [`ReactorHostControl`](ReactorHostControl.md) that has no handler of its own — including windows opened after this is set, tray flyouts, and embedded host controls. A host overrides it with [`ReactorHost.RenderErrorHandler`](ReactorHost.md#rendererrorhandler), and a window with [`WindowSpec.RenderErrorHandler`](WindowSpec.md#rendererrorhandler).

Covers every place Reactor catches a render-time exception outside an `ErrorBoundary`: the root render, child component renders, the reconcile pass, effect flushes, and effect cleanups during dispose. See [`RenderErrorHandler`](../components/render-errors.md#rendererrorhandler).

### `DevtoolsEnabled`

```csharp
public static bool DevtoolsEnabled  // get
```

### `PrimaryWindow`

The first window opened during this process's startup callback, or `null` when none has been opened (tray-only / pre-startup).

```csharp
public static ReactorWindow? PrimaryWindow  // get
```

### `ShutdownPolicy`

Process-shutdown policy. Defaults to [`ShutdownPolicy.OnPrimaryWindowClosed`](window-options.md#values_2).

```csharp
public static ShutdownPolicy ShutdownPolicy  // get, set
```

**Remarks**

Settable from any thread and at any point in the process lifetime, and read back immediately. Reactor takes ownership of the event loop at startup (see [`ReactorApp.TakeOwnershipOfDispatcherLifetime`](ReactorApp.md)), so this value is consulted when a decision is due rather than mirrored into platform state — there is no window in which the policy and the platform can disagree.

Two moments consult it. A surface close reaches [`ReactorApp.EvaluateShutdownPolicy`](ReactorApp.md), which reads the value then, so a change made any time beforehand counts. Startup is the exception: [`ReactorApp.ExitIfStartupOpenedNoSurface`](ReactorApp.md) runs the instant the launch path finishes, so a zero-surface startup is judged against whatever the policy holds at that instant. Set it before `Run`, or on the UI thread before the startup callback returns — a write posted from another thread during startup can land too late to be seen.

### `TrayIcons`

Snapshot of every open [`ReactorTrayIcon`](tray-icon.md#reactortrayicon). Copy-on-write; safe to enumerate from any thread. (spec 036 §11.4)

```csharp
public static IReadOnlyList<ReactorTrayIcon> TrayIcons  // get
```

### `UIDispatcher`

The UI [`DispatcherQueue`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.dispatching.dispatcherqueue) captured at `OnLaunched` — null until the first window has been bootstrapped.

```csharp
public static DispatcherQueue? UIDispatcher  // get
```

### `WindowPersistenceStore`

Process-wide store backing [`WindowSpec.PersistenceId`](WindowSpec.md#persistenceid). Defaults are picked lazily on first window open: packaged apps get [`PackagedSettingsStore`](persistence.md#packagedsettingsstore); unpackaged apps get [`JsonFileStore`](persistence.md#jsonfilestore). Setting this property after the first `OpenWindow` throws — windows that already loaded their placement from the previous store would get a half-populated state. (spec 036 §8)

```csharp
public static IWindowPersistenceStore? WindowPersistenceStore  // get, set
```

**Remarks**

Unpackaged apps can opt into [`UnpackagedAppDataStore`](persistence.md#unpackagedappdatastore) by assigning one here before the first `OpenWindow`. It persists under the Windows App SDK's app-data root for an explicit publisher/product pair instead of the entry process's name, so saved layouts survive an executable rename. It is not the auto-detected default because the two stores key their data differently, so switching does not migrate existing saved layouts. (spec 063 §4)

### `Windows`

Snapshot of every open [`ReactorWindow`](ReactorWindow.md). Copy-on-write; safe to enumerate from any thread.

```csharp
public static IReadOnlyList<ReactorWindow> Windows  // get
```

## Methods

### `Exit`

Exit the process. UI-thread only. With `exitCode` set to its default (`0`), routes through [`Application.Exit`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.application.exit) so WinUI gets a clean unwind. With a non-zero exit code, calls [`Application.Exit`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.application.exit) first to release resources, then [`Environment.Exit`](https://learn.microsoft.com/dotnet/api/system.environment.exit) with the requested code so the parent process sees it (assigning [`Environment.ExitCode`](https://learn.microsoft.com/dotnet/api/system.environment.exitcode) would only take effect on a natural managed-entry-point return, which an app under `Application.Start` does not perform).

```csharp
public static void Exit(int exitCode = 0)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `exitCode` | `int` | `0` |  |

### `FindTrayIcon`

Look up an open tray icon by [`WindowKey`](WindowKey.md).

```csharp
public static ReactorTrayIcon? FindTrayIcon(WindowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`WindowKey`](WindowKey.md) |  |

### `FindWindow`

Look up an open window by [`WindowKey`](WindowKey.md).

```csharp
public static ReactorWindow? FindWindow(WindowKey key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | [`WindowKey`](WindowKey.md) |  |

### `OpenTrayIcon`

Open a system-tray icon. UI-thread only. The returned handle stays alive until [`ReactorTrayIcon.Close`](tray-icon.md#close) / [`ReactorTrayIcon.Dispose`](tray-icon.md#dispose) is called or the process exits. (spec 036 §11.4)

```csharp
public static ReactorTrayIcon OpenTrayIcon(TrayIconSpec spec)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `spec` | [`TrayIconSpec`](tray-icon.md#trayiconspec) |  |

### `OpenWindow`

#### `OpenWindow(WindowSpec, Func<Component>, Action<ReactorHost>?)`

Open a window with a [`Component`](../components/Component.md) root. UI-thread only. (spec 036 §4.3)

```csharp
public static ReactorWindow OpenWindow(WindowSpec spec, Func<Component> root, Action<ReactorHost>? configure = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `spec` | [`WindowSpec`](WindowSpec.md) |  | Declarative description of the window's chrome. |
| `root` | `Func<Component>` |  | Factory invoked once to materialize the root component. |
| `configure` | `Action<ReactorHost>?` | `null` | Optional callback invoked on the UI thread **before** the root mounts. Use it for pre-mount setup that needs the live [`ReactorHost`](ReactorHost.md) (logger wiring, custom reconciler registrations, host-level event hooks). Mirror of the `configure` parameter on the legacy [`ReactorApp.Run`](#runtrootstring-double-double-bool-windowicon-actionreactorhost) entry — secondary windows now have the same escape hatch as the primary. |

#### `OpenWindow(WindowSpec, Func<RenderContext, Element>, Action<ReactorHost>?)`

Open a window with a render-function root. UI-thread only. See the [`ReactorApp.OpenWindow`](#openwindowwindowspec-funccomponent-actionreactorhost) overload for `configure` semantics.

```csharp
public static ReactorWindow OpenWindow(WindowSpec spec, Func<RenderContext, Element> render, Action<ReactorHost>? configure = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `spec` | [`WindowSpec`](WindowSpec.md) |  |  |
| `render` | `Func<RenderContext, Element>` |  |  |
| `configure` | `Action<ReactorHost>?` | `null` |  |

### `RegisterAllBuiltIns`

Registers every built-in Reactor control handler with the global [`ControlRegistry`](../extensibility/ControlRegistry.md) in one call.

```csharp
public static void RegisterAllBuiltIns()
```

**Remarks**

**When to call.** Call this once at process startup if your app constructs element records directly (the `new TextBlockElement(…) { … }` hot-loop idiom documented in `docs/guide/advanced.md` "Hot loops") rather than exclusively through factory methods. Factory methods already self-register on first call, so an app that only ever builds UI through the fluent/factory DSL does not need this.

**Trimming.** This method is the deliberate opt-out of spec-048's lazy-registration trim story: calling it roots the entire built-in catalog, so a NativeAOT/trimmed build that calls it keeps every built-in handler and WinUI control. Apps that want a minimal trimmed binary should *not* call this and instead let each factory register only the controls it uses — or register specific controls explicitly: [`ControlRegistry.Register`](../extensibility/ControlRegistry.md#register) for control-backed handlers, or [`ControlRegistry.RegisterDecorator`](../extensibility/ControlRegistry.md#registerdecorator) for decorator-backed elements (overlays, validation, composite wrappers). Because this is an ordinary method (not a `[ModuleInitializer]`), the trimmer removes it — and everything it names — when it is unreachable.

Registration is idempotent and process-wide; calling it more than once is a cheap no-op after the first call.

### `RegisterControlAssembly`

#### `RegisterControlAssembly(IXamlMetadataProvider)`

Registers a XAML metadata provider so its types are visible to the WinUI XAML loader for this process. Required when a third-party control library is referenced from a Reactor app that has no XAML files of its own (and therefore no compiler-generated provider that would auto-chain to the library). Call before [`ReactorApp.Run`](#runtrootstring-double-double-bool-windowicon-actionreactorhost). Idempotent (same instance is added at most once) and thread-safe. See https://github.com/microsoft/microsoft-ui-reactor/issues/142.

```csharp
public static void RegisterControlAssembly(IXamlMetadataProvider provider)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `provider` | `IXamlMetadataProvider` |  |

#### `RegisterControlAssembly(Assembly)`

Convenience overload that locates the XAML-compiler-generated `IXamlMetadataProvider` in `assembly` (the type the XAML compiler emits when the project has at least one XAML file) and registers it. Throws if no such provider is found — pass the [`IXamlMetadataProvider`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.markup.ixamlmetadataprovider) instance directly if your library uses a non-standard provider type. Reflection discovery does not preserve the provider under trimming/NativeAOT; use the instance overload with a statically constructed provider in that case.

```csharp
public static void RegisterControlAssembly(Assembly assembly)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `assembly` | `Assembly` |  |

### `Run`

#### `Run(Action<ReactorAppContext>)`

Multi-window startup entry. The `startup` callback runs on the UI thread after WinUI bootstraps and before any default window is opened. Open windows or tray icons (Phase 8) directly from inside the callback. (spec 036 §4.3 / §6.1)

```csharp
public static void Run(Action<ReactorAppContext> startup)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `startup` | `Action<ReactorAppContext>` |  |

#### `Run<TRoot>(WindowSpec, Action<ReactorHost>?)`

Launches the app with a full [`WindowSpec`](WindowSpec.md) for the primary window, so it gets the same declarative surface as a secondary window opened via [`ReactorApp.OpenWindow`](#openwindowwindowspec-funccomponent-actionreactorhost) — icon, min/max size, backdrop, corner style, start position, and placement persistence.

```csharp
public static void Run<TRoot>(WindowSpec spec, Action<ReactorHost>? configure = null) where TRoot : Component, new()
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `spec` | [`WindowSpec`](WindowSpec.md) |  |  |
| `configure` | `Action<ReactorHost>?` | `null` |  |

**Remarks**

Under `--devtools run` the preview host substitutes its own window; it reads title and size from the spec and ignores the remaining fields, exactly as it does for the flat overloads.

#### `Run(WindowSpec, Func<RenderContext, Element>, Action<ReactorHost>?)`

Launches the app with a render function and a full [`WindowSpec`](WindowSpec.md) for the primary window. See [`ReactorApp.Run`](#runtrootwindowspec-actionreactorhost) for the spec semantics.

```csharp
public static void Run(WindowSpec spec, Func<RenderContext, Element> rootRender, Action<ReactorHost>? configure = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `spec` | [`WindowSpec`](WindowSpec.md) |  |  |
| `rootRender` | `Func<RenderContext, Element>` |  |  |
| `configure` | `Action<ReactorHost>?` | `null` |  |

#### `Run<TRoot>(string, double?, double?, bool, WindowIcon?, Action<ReactorHost>?)`

Launches the app. `width` / `height` are DIPs; leave them `null` (the default) to let the OS pick the initial window size, exactly as a plain XAML `Window` does. With the `Reactor.DevtoolsSupport` runtime host configuration switch on (the Debug default) and the deprecated Microsoft.UI.Reactor.Devtools package referenced, `--devtools run` starts the in-app preview host that the VS Code and Visual Studio previews use (component switching and listing). To inspect a running app, use the WinUI DevTools: `winapp run. --devtools on`.

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
| `icon` | [`WindowIcon?`](WindowIcon.md) | `null` | Optional window icon — the Win32 `HICON` Windows shows in the window's caption and the Alt-Tab switcher. Use an `.ico`: build it with [`WindowIcon.FromPath`](WindowIcon.md#frompath) for a file beside the app, or [`WindowIcon.FromResource`](WindowIcon.md#fromresource) for a packaged `ms-appx:///` asset. When omitted, Reactor falls back to `Assets\AppIcon.ico` and then to the executable's embedded icon. Distinct from `TitleBar(...).Icon(...)`, which sets the app mark drawn inside the window. Note that in a **packaged** app this does not change the taskbar button or Task Manager — the shell draws those from package identity and the manifest's `Square44x44Logo`; see [`WindowSpec.Icon`](WindowSpec.md#icon). |
| `configure` | `Action<ReactorHost>?` | `null` | Optional callback invoked on the UI thread before the root mounts. |

#### `Run(string, Func<RenderContext, Element>, double?, double?, bool, WindowIcon?, Action<ReactorHost>?)`

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
| `icon` | [`WindowIcon?`](WindowIcon.md) | `null` | Optional window icon. See [`ReactorApp.Run`](#runtrootstring-double-double-bool-windowicon-actionreactorhost). |
| `configure` | `Action<ReactorHost>?` | `null` | Optional callback invoked on the UI thread before the root mounts. |

### `TryRegisterControlAssembly`

Tolerant variant of [`ReactorApp.RegisterControlAssembly`](#registercontrolassemblyassembly): registers the assembly's XAML metadata provider if it has one, and silently does nothing (returning `false`) if it does not. A pure-code control library — e.g. code-only panels with no XAML files (and therefore no compiler-generated provider) — has nothing to register and needs nothing. Reflection discovery does not preserve the provider under trimming/NativeAOT. Generated wrappers use the instance overload when a suitable provider is visible at compile time, and this method only as a best-effort fallback.

```csharp
public static bool TryRegisterControlAssembly(Assembly assembly)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `assembly` | `Assembly` |  |

## Events

### `TrayIconClosed`

Fires on the UI thread when a [`ReactorTrayIcon`](tray-icon.md#reactortrayicon) closes.

```csharp
public static event EventHandler<ReactorTrayIcon>? TrayIconClosed
```

### `TrayIconOpened`

Fires on the UI thread when a [`ReactorTrayIcon`](tray-icon.md#reactortrayicon) opens.

```csharp
public static event EventHandler<ReactorTrayIcon>? TrayIconOpened
```

### `WindowClosed`

Fires on the UI thread when a [`ReactorWindow`](ReactorWindow.md) closes.

```csharp
public static event EventHandler<ReactorWindow>? WindowClosed
```

### `WindowOpened`

Fires on the UI thread when a [`ReactorWindow`](ReactorWindow.md) opens.

```csharp
public static event EventHandler<ReactorWindow>? WindowOpened
```
