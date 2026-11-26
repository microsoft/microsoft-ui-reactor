# Hosting and windows

Start the app, open windows and integrate with the Windows shell.

`ReactorApp` starts the app and opens its first window. The other types here describe windows, the tray icon, the taskbar and the ways to host Reactor inside an existing WinUI app.

**Learn more:** [Windows](../../windows.md), [Advanced Windowing](../../windowing-advanced.md)

## Types

| Type | Description |
| --- | --- |
| [Displays](displays.md) | Information about the connected displays. |
| [File pickers](file-pickers.md) | Options for the open file, save file and folder pickers. |
| [Launch and activation](launch.md) | How the app was started, and requests to embed it. |
| [Persistence](persistence.md) | Stores and scopes that save window placement and persisted state. |
| [Taskbar and jump lists](taskbar.md) | Types for the taskbar button, its progress, overlay, thumbnail buttons and jump list. |
| [Tray icon](tray-icon.md) | Types for an icon in the notification area. |
| [Window events](window-events.md) | Event arguments raised by a Reactor window. |
| [Window options](window-options.md) | Enums that describe the style, size and position of a window. |
| [XAML interop](xaml-interop.md) | Helpers for hosting Reactor inside an existing XAML app. |
| [`ReactorApp`](ReactorApp.md) |  |
| [`ReactorAppContext`](ReactorAppContext.md) | The argument to `ReactorApp.Run`'s startup callback. |
| [`ReactorApplication`](ReactorApplication.md) | Application subclass that implements IXamlMetadataProvider so the native XAML schema context can resolve managed types from XBF theme resources. |
| [`ReactorHost`](ReactorHost.md) | Hosts a Reactor component tree inside a WinUI Window. |
| [`ReactorHostControl`](ReactorHostControl.md) | A fully self-contained WinUI ContentControl that hosts a Reactor component tree. |
| [`ReactorWindow`](ReactorWindow.md) | Owns one OS top-level Window and one `ReactorHost`. |
| [`RenderStats`](RenderStats.md) | Snapshot of render loop performance, updated every ~1 second by `ReactorHost`. |
| [`UIThreadOnlyAttribute`](UIThreadOnlyAttribute.md) | Marks a method or property setter that must be invoked on the UI thread — the annotated method (or the property's setter) calls `ThreadAffinity.ThrowIfNotOnUIThread`, which throws `InvalidOperationException` when reached from a background thread once the UI dispatcher has been captured. (The guard is a no-op before the first window bootstraps, while `ReactorApp.UIDispatcher` is still null.) Property getters are not guarded, so the REACTOR_THREAD_001 analyzer flags writes to an annotated property, not reads. |
| [`WindowIcon`](WindowIcon.md) | Abstraction over `AppWindow.SetIcon` and `AppWindow.SetIcon`. |
| [`WindowKey`](WindowKey.md) | Stable identity for a top-level surface within a process. |
| [`WindowSpec`](WindowSpec.md) | Immutable (init-only) declarative description of a top-level Reactor window. |
