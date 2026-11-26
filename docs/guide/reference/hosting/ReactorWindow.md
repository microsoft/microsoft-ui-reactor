# ReactorWindow

Owns one OS top-level Window and one [`ReactorHost`](ReactorHost.md). Created via [`ReactorApp.OpenWindow`](ReactorApp.md#openwindowwindowspec-funccomponent-actionreactorhost). (spec 036 §3.2 / §4.2)

```csharp
public sealed class ReactorWindow : IDisposable
```

Namespace: `Microsoft.UI.Reactor`.

**Learn more:** [Windows](../../windows.md)

## Remarks

Public mutators ([`ReactorWindow.Activate`](#activate), [`ReactorWindow.Hide`](#hide), [`ReactorWindow.Show`](#show), [`ReactorWindow.Close`](#close), [`ReactorWindow.Update`](#update), [`ReactorWindow.SetSize`](#setsize), [`ReactorWindow.SetPosition`](#setposition), [`ReactorWindow.CenterOnScreen`](#centeronscreen), [`ReactorWindow.Mount`](#mountcomponent)) must be called on the UI thread captured by [`ReactorApp.UIDispatcher`](ReactorApp.md#uidispatcher). Read-only properties ([`ReactorWindow.Spec`](#spec), [`ReactorWindow.Dpi`](#dpi), [`ReactorWindow.Position`](#position), [`ReactorWindow.State`](#state), [`ReactorWindow.IsVisible`](#isvisible), [`ReactorWindow.IsActive`](#isactive)) snapshot a `Volatile.Read` field and are safe from any thread.

Disposal is idempotent — a second [`ReactorWindow.Close`](#close) or [`ReactorWindow.Dispose`](#dispose) is a no-op, not an exception.

## Properties

### `AppWindow`

The WinUI [`ReactorWindow.AppWindow`](#appwindow) for this window.

```csharp
public AppWindow AppWindow  // get
```

### `DipScale`

DIP scale factor (Dpi / 96). 1.0 at 100%, 1.5 at 150%, 2.0 at 200%.

```csharp
public double DipScale  // get
```

### `Dpi`

Per-window DPI in raw units (96, 120, 144, 192, ...). Phase 2 makes this observable.

```csharp
public uint Dpi  // get
```

### `Host`

The [`ReactorHost`](ReactorHost.md) driving this window's render loop.

```csharp
public ReactorHost Host  // get
```

### `Id`

Stable id, e.g. `"win-3"`. Allocated monotonically per process.

```csharp
public string Id  // get
```

### `IsActive`

Whether the window currently holds activation.

```csharp
public bool IsActive  // get
```

### `IsVisible`

Whether the window is currently shown (post [`ReactorWindow.Activate`](#activate) / pre [`ReactorWindow.Hide`](#hide)).

```csharp
public bool IsVisible  // get
```

### `Key`

Optional stable identity (from [`WindowSpec.Key`](WindowSpec.md#key)).

```csharp
public WindowKey? Key  // get
```

### `NativeWindow`

The underlying WinUI [`Window`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.window).

```csharp
public Window NativeWindow  // get
```

### `Overlay`

Taskbar overlay icon ("badge"). Lazily allocated. Shortcut kept for compatibility; equivalent to `TaskbarItem.Overlay`. (spec 036 §11.2)

```csharp
public TaskbarOverlay Overlay  // get
```

### `PersistedScope`

Per-window persisted-state scope. Bounded by this window's lifetime — disposed when the window closes. Used by [`UsePersisted`](../hooks/UsePersisted.md#usepersistedtstring-t-persistedscope) when [`PersistedScope.Window`](persistence.md#values) is requested. (spec 036 §3.4 / §4.4)

```csharp
public WindowPersistedScope PersistedScope  // get
```

### `Position`

DIP top-left position of the window, rounded to the nearest DIP.

```csharp
public (double X, double Y) Position  // get
```

### `Progress`

Taskbar progress indicator for this window. Lazily allocated on first read; apps that never touch it pay no shell-COM init cost. Shortcut kept for compatibility; equivalent to `TaskbarItem.Progress`. (spec 036 §11.1)

```csharp
public TaskbarProgress Progress  // get
```

### `Spec`

Last applied [`WindowSpec`](WindowSpec.md) snapshot.

```csharp
public WindowSpec Spec  // get
```

### `State`

Coarse window state.

```csharp
public WindowState State  // get
```

### `TaskbarItem`

Grouped taskbar features for this window. Lazily allocated.

```csharp
public TaskbarItem TaskbarItem  // get
```

## Methods

### `Activate`

Show and focus the window. UI-thread only. No-op after disposal.

```csharp
public void Activate()
```

### `BeginDragMove`

Begin a window drag/move loop as if the title bar had been clicked-and-dragged.

```csharp
public void BeginDragMove()
```

**Remarks**

Call from a left-button `PointerPressed` handler. We snapshot the initial cursor + window screen position, then run a dispatcher timer that polls `GetCursorPos` at ~60Hz and calls `AppWindow.Move` until `GetAsyncKeyState(VK_LBUTTON)` reports the button is no longer pressed. The timer-based approach is necessary because WinUI 3 routes pointer input through a child `InputSiteBridge` HWND, so synthesizing `WM_NCLBUTTONDOWN` (or `WM_SYSCOMMAND` + `SC_MOVE | HTCAPTION`) on the top-level HWND silently falls into keyboard/cursor-track move mode rather than mouse-driven click-drag (DefWindowProc doesn't see a recent WM_LBUTTONDOWN message). The polling approach is simple, reliable, and doesn't depend on the OS's non-client message routing. The trade-off vs. the OS modal drag loop is no Aero Snap during the drag — acceptable for the small floating windows (command palettes, tool palettes) that need IsMovableByBackground.

### `CenterOnScreen`

Center on the window's current monitor. UI-thread only.

```csharp
public void CenterOnScreen()
```

### `ClearThumbnailToolbar`

Hide all thumbnail-toolbar buttons. Shortcut kept for compatibility; equivalent to `TaskbarItem.ClearThumbnailToolbar()`. Idempotent; safe to call before [`ReactorWindow.SetThumbnailToolbar`](#setthumbnailtoolbar) has been called. (spec 036 §11.5)

```csharp
public void ClearThumbnailToolbar()
```

### `Close`

Close the window. UI-thread only. The [`ReactorWindow.Closing`](#closing) event (Phase 3) will run first; if any subscriber sets [`WindowClosingEventArgs.Cancel`](window-events.md#cancel) the close aborts. Idempotent — a second call after disposal is a no-op.

```csharp
public void Close()
```

### `Dispose`

Idempotent dispose. Detaches event handlers and disposes the host. The native window has typically already been closed by this point — this is the cleanup that runs after Window.Closed fires.

```csharp
public void Dispose()
```

### `Hide`

Hide the window without closing. UI-thread only. No-op after disposal.

```csharp
public void Hide()
```

### `Mount`

#### `Mount(Component)`

Mount a new component root. UI-thread only.

```csharp
public void Mount(Component root)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | [`Component`](../components/Component.md) |  |

#### `Mount(Func<RenderContext, Element>)`

Mount a new render-function root. UI-thread only.

```csharp
public void Mount(Func<RenderContext, Element> render)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `render` | `Func<RenderContext, Element>` |  |

### `SavePlacement`

Force-save the current window placement when placement persistence is enabled.

```csharp
public void SavePlacement()
```

### `SetAspectRatio`

Set or clear the width/height aspect lock used during interactive resize.

```csharp
public void SetAspectRatio(double? ratio)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ratio` | `double?` |  |

### `SetIgnorePointerInput`

Toggle the `WS_EX_TRANSPARENT` extended style on the underlying HWND. When set, mouse events pass THROUGH the window to whatever's underneath. The window must already be layered (via [`ReactorWindow.SetOpacity`](#setopacity) with a value \< 1.0) when enabling — the OS only honors transparent on layered windows. UI-thread only. No-op after disposal.

```csharp
public void SetIgnorePointerInput(bool ignore)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `ignore` | `bool` |  |

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) | Thrown when `ignore` is true but the window is not currently layered. Call [`ReactorWindow.SetOpacity`](#setopacity) with a value \< 1.0 first. |

### `SetNoActivate`

Toggle the `WS_EX_NOACTIVATE` extended style on the underlying HWND. When set, the window appears without stealing foreground activation (matches VS tool-window / drag-preview behavior). UI-thread only. No-op after disposal.

```csharp
public void SetNoActivate(bool noActivate)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `noActivate` | `bool` |  |

### `SetOpacity`

Set window-wide alpha in \[0..1\]. 1.0 strips the layered-window extended style; values below 1.0 install it and call `SetLayeredWindowAttributes`. UI-thread only. No-op after disposal.

```csharp
public void SetOpacity(double opacity)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `opacity` | `double` |  |

### `SetPosition`

Move to `x`,`y` DIPs. UI-thread only.

```csharp
public void SetPosition(double x, double y)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `x` | `double` |  |
| `y` | `double` |  |

**Remarks**

The DIP→physical conversion uses the **current window's** DPI. On mixed-DPI multi-monitor setups, moving across to a monitor with a different scale factor can land at a slightly different physical position than a caller expects, because Windows virtual-screen coordinates are physical pixels with no global DIP coordinate space. For predictable cross-monitor placement, prefer [`ReactorWindow.CenterOnScreen`](#centeronscreen) (which resolves against the destination [`DisplayArea`](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.displayarea)) or move in two steps — `SetPosition` onto the target monitor, then adjust within it. (spec 036 §5.2)

### `SetSize`

Resize to `width` x `height` DIPs. UI-thread only.

```csharp
public void SetSize(double width, double height)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `width` | `double` |  |
| `height` | `double` |  |

### `SetThumbnailToolbar`

Replace the thumbnail-toolbar buttons for this window. Shortcut kept for compatibility; equivalent to `TaskbarItem.SetThumbnailToolbar(...)`. Up to seven buttons; duplicate ids throw. The first call adds the button set, later calls diff and update only the changed slots. (spec 036 §11.5)

```csharp
public void SetThumbnailToolbar(IReadOnlyList<ThumbnailToolbarButton> buttons)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `buttons` | `IReadOnlyList<ThumbnailToolbarButton>` |  |

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) | Thrown when more than seven buttons are supplied or when ids are duplicated. |

### `Show`

Show a previously hidden window. UI-thread only. No-op after disposal.

```csharp
public void Show()
```

### `Update`

Diff `next` against the current spec and apply only the fields that changed. UI-thread only.

```csharp
public void Update(WindowSpec next)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `next` | [`WindowSpec`](WindowSpec.md) |  |

## Events

### `Activated`

Fires on the UI thread when the window gains activation.

```csharp
public event EventHandler? Activated
```

### `Closed`

Fires on the UI thread after the window closes and the host disposes.

```csharp
public event EventHandler? Closed
```

### `Closing`

Fires on the UI thread before the window closes. Set [`WindowClosingEventArgs.Cancel`](window-events.md#cancel) to abort. Synchronous — see `UseClosingGuard` for the async pattern. (Phase 3)

```csharp
public event EventHandler<WindowClosingEventArgs>? Closing
```

### `Deactivated`

Fires on the UI thread when the window loses activation.

```csharp
public event EventHandler? Deactivated
```

### `DpiChanged`

Fires on the UI thread when per-window DPI changes. (Phase 2)

```csharp
public event EventHandler<uint>? DpiChanged
```

### `PositionChanged`

Fires on the UI thread when the window's DIP top-left position changes.

```csharp
public event EventHandler<WindowDipPositionChangedEventArgs>? PositionChanged
```

### `SizeChanged`

Fires on the UI thread when the window's DIP size changes. (Phase 3)

```csharp
public event EventHandler<WindowDipSizeChangedEventArgs>? SizeChanged
```

### `StateChanged`

Fires on the UI thread when [`ReactorWindow.State`](#state) changes. (Phase 3)

```csharp
public event EventHandler<WindowState>? StateChanged
```

### `ZOrderChanged`

Fires on the UI thread when Win32 reports a z-order transition. [`WindowZOrderChangedEventArgs.IsCovered`](window-events.md#iscovered) is a covered hint based on HWND insertion order, not a pixel-accurate occlusion guarantee.

```csharp
public event EventHandler<WindowZOrderChangedEventArgs>? ZOrderChanged
```
