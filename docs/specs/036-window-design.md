# Window Model — Design Spec

## Status

**Proposed** — 2026-05-07. No code yet.

This spec proposes a first-class **Window** model for Reactor. Today the
framework hides Window creation behind `ReactorApp.Run`'s `OnLaunched` path
and assumes a single primary window for the lifetime of the process. That
shortcut has aged into a hard ceiling: app authors can't open a second
top-level window, sizes are in physical pixels (so the same numbers produce
a 2× window on a 200 % display), and runtime DPI changes aren't surfaced.
This spec lays out the data model, public API, lifecycle, and migration
path for moving Window out of "internal hosting wiring" and into a proper
Reactor primitive — without breaking the existing single-window `Run<TRoot>`
shape.

---

## Table of contents

- [§1 Motivation](#1-motivation)
- [§2 Goals / non-goals](#2-goals--non-goals)
- [§3 Model](#3-model)
- [§4 Public API](#4-public-api)
- [§5 DPI awareness](#5-dpi-awareness)
- [§6 Lifecycle and shutdown](#6-lifecycle-and-shutdown)
- [§7 Hooks](#7-hooks)
- [§8 Persistence](#8-persistence)
- [§9 Owned and modal windows](#9-owned-and-modal-windows)
- [§10 Devtools and MCP](#10-devtools-and-mcp)
- [§11 Shell integration](#11-shell-integration)
- [§12 Migration](#12-migration)
- [§13 Examples](#13-examples)
- [§14 Implementation plan](#14-implementation-plan)
- [§15 Resolved questions](#15-resolved-questions)
- [§16 Out of scope](#16-out-of-scope)

---

## §1 Motivation

Concrete deficiencies in the status quo (`src/Reactor/Hosting/ReactorApp.cs`,
`ReactorHost.cs`):

1. **Window creation is hidden.** `ReactorApplication.OnLaunched` does
   `var window = new Window { Title = opts.WindowTitle }`. App code never
   sees the `Window`; it can only configure it through `Action<ReactorHost>
   configure` after-the-fact. There is no way to refuse the default window,
   no way to get a window before content mounts, and no way to ask for a
   second one.

2. **Sizes are in physical pixels.** `Run<T>(title, width: 1024, height: 768)`
   ends up at `AppWindow.Resize(new SizeInt32(1024, 768))` — `SizeInt32` is
   pixels. On a 200 % display this is a ~512 × 384 DIP window; on a 4K HiDPI
   laptop it is a postage stamp. WinUI's own `Window.Bounds` is in DIPs, so
   the inconsistency has always been latent.

3. **No multi-window topology.** `ReactorApp.ActiveHost`,
   `ReactorHost.MainDispatcherQueue`, the devtools `WindowRegistry`'s
   "main" pin, and the screenshot subverb all assume one host bound to one
   window. There is no `OpenWindow` API, no per-window component tree
   addressing, no per-window state isolation surface.

4. **No DPI runtime story.** PerMonitorV2 is enabled
   (`SetProcessDpiAwarenessContext`) so the OS hands us correct events,
   but Reactor never reads `GetDpiForWindow`, never surfaces `DpiChanged`,
   and never re-applies size constraints when the window crosses a monitor
   boundary.

5. **Window-level state is awkward.** `WindowPersistedScope` exists in
   `Core/` but has no host wiring (per spec 033 §7.5 explicit follow-up).
   A real Window primitive is the natural owner.

6. **Window-level chrome (icon, min/max, presenter, title bar) requires
   reaching into `host.Window.AppWindow`** from inside `configure: host =>
   …`. The signal that "this is window-level configuration, not
   render-loop configuration" is missing — both arrive through the same
   callback.

The right fix is to promote Window to a typed concept the app interacts
with directly, while keeping the easy "one window, one component, one
line" shape that makes the samples readable.

## §2 Goals / non-goals

### Goals

- **G1.** A `WindowSpec` record describes the complete desktop-window
  surface in DIPs (size, min/max, position, presenter, chrome, backdrop,
  persistence) — no Win32 / no `SizeInt32` in user code.
- **G2.** Multiple top-level windows in one process, each with its own
  `ReactorHost`, sharing the single UI dispatcher. Lifetimes are
  independent.
- **G3.** Idiomatic Reactor authoring: a `UseOpenWindow(...)` hook keyed by
  identity, plus `UseWindow()`, `UseDpi()`, `UseWindowState()`,
  `UseClosingGuard(...)` so secondary windows don't require dropping into
  imperative code.
- **G4.** DPI-aware everything. All public sizes are DIPs. `Window.Dpi` is
  observable. Resize / DPI-changed events fire with DIP-denominated args.
  Min/max constraints survive monitor crossings.
- **G5.** Single-window `ReactorApp.Run<TRoot>` keeps working with the same
  call signature, but DIPs. (Behavior change documented in §11.)
- **G6.** First-class persistence (size / position / state) via a
  `PersistenceId` modeled on WinUIEx but not bound to packaged
  `ApplicationData` — Reactor's primary use case is unpackaged.
- **G7.** Devtools / MCP can address windows by stable id and open / close
  them out-of-band.
- **G8.** Selftests can spin up multi-window scenarios in-process (one new
  fixture surface, no harness fork).

- **G9.** **Expose the full Windows shell surface** that touches a
  top-level window: taskbar progress, taskbar overlay icons, jump
  lists, system tray icon + flyout, thumbnail toolbars, taskbar
  thumbnail clipping. These are not afterthoughts — they are part of
  what "a real Windows app" means and the Window primitive is the
  natural owner. See [§11 Shell integration](#11-shell-integration).

### Non-goals

- **N1.** Windowing on non-Win32 (Reactor is WinUI 3, desktop only).
- **N2.** Cross-window content drag (later spec; needs a content-id
  protocol).
- **N3.** Fully declarative `Window(...)` element returned from
  `Render()` — see [§3 Model](#3-model) for why we picked imperative-
  with-hook over reconciler-as-portal.
- **N4.** Modal dialogs as separate top-level windows — Reactor already
  has `ContentDialog` and modal flyouts that cover that need; modal
  *top-level* windows are a §9 future option.
- **N5.** New title-bar primitives. Reactor already exposes a
  `TitleBar(...)` factory (`src/Reactor/Elements/Dsl.cs`) that owns
  title-bar customization; this spec does not extend or replace that
  model. `WindowSpec.ExtendsContentIntoTitleBar` is the only knob this
  spec adds.

## §3 Model

### 3.1 Why not "Window as a Reactor element"

The most Reactor-native option would be a `Window(...)` element returned
from `Render()`, with the reconciler treating it as a portal that opens
an OS surface. We rejected this for v1:

- The reconciler returns a single `UIElement` per host. A `WindowElement`
  is *not* a UIElement — it is an OS surface that *contains* one. The
  contract change required (each host now produces a forest of UIElements,
  one per portal) would touch every mount/update path.
- Windows have side-effecting lifetimes (HWND creation, `Activate()`)
  that don't compose cleanly with idempotent reconciliation. Reopening a
  window because a parent re-rendered is the wrong default; Reactor would
  need explicit identity / portal keys at every site.
- Per-window state, dispatcher, and devtools registration introduce
  cross-tree coupling that breaks the "render returns a fresh tree"
  invariant.

The hybrid we adopt instead — imperative `OpenWindow` plus a hook that
*looks* declarative inside a component — preserves Reactor's reconciler
contract while keeping authoring ergonomic. The portal model can be
re-evaluated in a future spec without disturbing this one.

### 3.2 Concepts

```
ReactorApp                  process-scoped singleton
  ├── UIDispatcher          captured once at Application.Start
  ├── Windows               ReadOnlyList<ReactorWindow>
  ├── PrimaryWindow         the one passed to the startup callback (or null)
  ├── ShutdownPolicy        OnPrimaryWindowClosed | OnLastSurfaceClosed | Explicit
  └── WindowOpened/Closed   process-level events

ReactorWindow               owns one OS Window + one ReactorHost
  ├── Id                    stable string (e.g. "win-3")
  ├── Key                   optional identity for UseOpenWindow reuse
  ├── NativeWindow          Microsoft.UI.Xaml.Window
  ├── AppWindow             Microsoft.UI.Windowing.AppWindow
  ├── Host                  ReactorHost
  ├── Spec                  last-applied WindowSpec snapshot
  ├── Dpi                   uint, observable
  ├── State                 Normal | Minimized | Maximized | FullScreen | CompactOverlay
  ├── IsVisible / IsActive
  └── events                Activated / Deactivated / SizeChanged / DpiChanged /
                            StateChanged / Closing / Closed

WindowSpec                  immutable record — full surface description
WindowKey                   value type for stable identity (string + props)
WindowIcon                  abstraction over AppWindow.SetIcon paths/IconId
PresenterKind               Overlapped | FullScreen | CompactOverlay
WindowState                 Normal | Minimized | Maximized | FullScreen | CompactOverlay
WindowStartPosition         Default | CenterOnPrimary | CenterOnOwner |
                            RestoreFromPersistence | Manual
ShutdownPolicy              see §6
```

### 3.3 Relationship to existing types

- `ReactorHost` keeps its current 1:1 binding to a `Window`. Construction
  moves from `ReactorApplication.OnLaunched` into `ReactorWindow`. The
  static `MainDispatcherQueue` "first-host-wins" capture (`ReactorHost.cs:31`)
  is replaced by `ReactorApp.UIDispatcher`.
- `ReactorApp.ActiveHost` becomes a deprecated alias for
  `ReactorApp.PrimaryWindow?.Host`. Same shape, broader semantics.
- `BackdropApplier` is unchanged — every host already has its own. The
  new `WindowSpec.Backdrop` simply seeds the modifier so apps can set a
  default without touching the root tree.
- `WindowPersistedScope` becomes the per-window persisted-state scope
  resolved by `RenderContext.UsePersisted` once §3.4 wiring lands. This
  closes spec 033 §7.5.
- `Hosting.Devtools.WindowRegistry` already maps multiple windows by
  HWND; `ReactorWindow` lifecycle hooks into Attach/Detach.

### 3.4 RenderContext ↔ ReactorWindow

Each `ReactorHost` already has access to its `Window`. The new model
adds a back-pointer `ReactorHost.OwningWindow` returning the
`ReactorWindow`. From there, `RenderContext` can resolve:

- the per-window `WindowPersistedScope` (currently process-scoped only);
- the dispatcher for cross-thread `setState`;
- the active `Window` for the parameterless overloads of `UseWindowSize`
  / `UseBreakpoint` (today they require an explicit `Window` argument —
  see `RenderContext.cs:929`).

## §4 Public API

### 4.1 `WindowSpec`

```csharp
namespace Microsoft.UI.Reactor;

public sealed record WindowSpec(
    string Title = "Reactor App",
    double Width = 1024,                                          // DIPs
    double Height = 768,                                          // DIPs
    double? MinWidth = null,
    double? MinHeight = null,
    double? MaxWidth = null,
    double? MaxHeight = null,
    WindowStartPosition StartPosition = WindowStartPosition.Default,
    (double X, double Y)? ManualPosition = null,                  // DIPs, requires StartPosition.Manual
    PresenterKind Presenter = PresenterKind.Overlapped,
    bool IsResizable = true,
    bool IsMinimizable = true,
    bool IsMaximizable = true,
    bool IsAlwaysOnTop = false,
    bool IsShownInSwitchers = true,
    bool ExtendsContentIntoTitleBar = false,
    BackdropChoice? Backdrop = null,
    WindowIcon? Icon = null,
    string? PersistenceId = null,
    WindowKey? Key = null,
    ReactorWindow? Owner = null,
    bool ActivateOnOpen = true);
```

All sizes / positions are DIPs. The record is immutable; `Update(spec)`
on `ReactorWindow` diffs old vs. new and applies only the changed
fields, in the same spirit as the reconciler.

> **On `double` vs `float`.** DIPs are floating-point — the question is
> single vs. double precision. We use `double` to match the rest of the
> WinUI surface: `FrameworkElement.Width` / `Height` / `ActualWidth`,
> `Thickness`, `GridLength`, `WinUIEx.WindowEx.Width`, and Reactor's
> existing `RenderContext.UseWindowSize` overload all return `double`.
> `Windows.Foundation.Rect` is the outlier (`Single`). Mixing the two
> would force callers to cast at every property access; we pay the
> 4-bytes-per-field cost for consistency.

### 4.2 `ReactorWindow`

```csharp
public sealed class ReactorWindow : IDisposable
{
    public string Id { get; }
    public WindowKey? Key { get; }
    public Microsoft.UI.Xaml.Window NativeWindow { get; }
    public Microsoft.UI.Windowing.AppWindow AppWindow { get; }
    public ReactorHost Host { get; }
    public WindowSpec Spec { get; }       // last applied snapshot
    public uint Dpi { get; }
    public double DipScale => Dpi / 96.0;
    public WindowState State { get; }
    public bool IsVisible { get; }
    public bool IsActive { get; }

    public event EventHandler<WindowDipSizeChangedEventArgs>? SizeChanged;
    public event EventHandler<uint>? DpiChanged;
    public event EventHandler<WindowState>? StateChanged;
    public event EventHandler? Activated;
    public event EventHandler? Deactivated;
    public event EventHandler<WindowClosingEventArgs>? Closing;
    public event EventHandler? Closed;

    public void Activate();
    public void Hide();
    public void Show();
    public void Close();
    public void Update(WindowSpec spec);

    public void Mount(Component root);
    public void Mount(Func<RenderContext, Element> render);

    // Convenience — DIP-correct sizing without the caller doing DPI math.
    public void SetSize(double width, double height);
    public void SetPosition(double x, double y);
    public void CenterOnScreen();
}
```

`WindowDipSizeChangedEventArgs` carries `Size` (DIPs) and the raw
WinUI `WindowSizeChangedEventArgs` for escape hatches.
`WindowClosingEventArgs` carries `Cancel` and a `Reason` enum
(UserClosed | AppClosed | OwnerClosed).

### 4.3 `ReactorApp`

```csharp
public static partial class ReactorApp
{
    // Existing single-window entry — preserved, now DIP-denominated.
    // Implemented as: Run(ctx => ctx.OpenWindow(spec, () => new TRoot()));
    public static void Run<TRoot>(
        string title = "Reactor App",
        double width = 1024,
        double height = 768,
        bool fullScreen = false,
        bool devtools = false,
        Action<ReactorHost>? configure = null) where TRoot : Component, new();

    // Multi-window entry. The startup callback runs on the UI thread after
    // ReactorApplication construction, before any window is opened.
    public static void Run(Action<ReactorAppContext> startup);

    public static IReadOnlyList<ReactorWindow> Windows { get; }
    public static ReactorWindow? PrimaryWindow { get; }   // null in tray-only apps
    public static DispatcherQueue UIDispatcher { get; }
    public static ShutdownPolicy ShutdownPolicy { get; set; }
        = ShutdownPolicy.OnPrimaryWindowClosed;

    // Top-level surface operations — usable from anywhere on the UI thread
    // once Run has entered the startup callback. Tray click handlers, menu
    // commands, MCP tools, etc. all call into the same surface. Windows
    // and tray icons are peers; both can be the app's entry point.
    public static ReactorWindow OpenWindow(WindowSpec spec, Func<Component> root);
    public static ReactorWindow OpenWindow(WindowSpec spec, Func<RenderContext, Element> render);
    public static ReactorWindow? FindWindow(WindowKey key);

    public static ReactorTrayIcon OpenTrayIcon(TrayIconSpec spec);
    public static IReadOnlyList<ReactorTrayIcon> TrayIcons { get; }
    public static ReactorTrayIcon? FindTrayIcon(WindowKey key);

    public static event EventHandler<ReactorWindow>? WindowOpened;
    public static event EventHandler<ReactorWindow>? WindowClosed;
    public static event EventHandler<ReactorTrayIcon>? TrayIconOpened;
    public static event EventHandler<ReactorTrayIcon>? TrayIconClosed;

    public static void Exit(int exitCode = 0);

    [Obsolete("Use ReactorApp.PrimaryWindow.Host or ReactorApp.Windows.")]
    public static ReactorHost? ActiveHost { get; }
}

// The startup-callback context is a thin facade over ReactorApp giving access
// to the launch activation. It does not hold per-startup state; calls forward
// to the static ReactorApp surface and remain valid after Run returns control.
public sealed class ReactorAppContext
{
    public LaunchActivation LaunchActivation { get; }
    public ReactorWindow OpenWindow(WindowSpec spec, Func<Component> root);
    public ReactorWindow OpenWindow(WindowSpec spec, Func<RenderContext, Element> render);
    public ReactorWindow? FindWindow(WindowKey key);
    public ReactorTrayIcon OpenTrayIcon(TrayIconSpec spec);
    public ReactorTrayIcon? FindTrayIcon(WindowKey key);
}

public enum ShutdownPolicy
{
    OnPrimaryWindowClosed,
    OnLastSurfaceClosed,
    Explicit,
}
```

`Windows` is a snapshot (copy-on-write list) for thread-safe enumeration.
`Run<TRoot>` preserves source compatibility but its `width`/`height`
parameters now mean DIPs (see §11).

### 4.4 `WindowKey`

```csharp
public readonly record struct WindowKey(string Name)
{
    public static WindowKey Of(string name) => new(name);
    public static implicit operator WindowKey(string name) => new(name);
}
```

Identity is by name within a process. `UseOpenWindow(WindowKey, ...)`
guarantees that re-rendering the same component does not open a second
copy; it returns the existing window if one is already open under that
key.

## §5 DPI awareness

### 5.1 Apply path

For initial sizing:

1. `WindowSpec.Width × Height` arrive in DIPs.
2. Before activation, the window's monitor is unknown. Apply the system
   primary-monitor DPI as an estimate.
3. Subscribe to `AppWindow.Changed` (`DidPositionChange`) and to
   `WM_DPICHANGED` via a minimal `Messaging.WindowMessageMonitor` (port
   from WinUIEx, ~80 LOC). On the first DPI report, *if the user has not
   manually resized*, re-apply `Width × Height` against the actual
   per-window DPI. After that, the window's size belongs to the user;
   we no longer overwrite it.

For runtime updates:

- `ReactorWindow.SetSize(w, h)` always converts DIP → physical at the
  current `Dpi`.
- Min/max constraints flow through `WM_GETMINMAXINFO`. Without that
  hook, dragging a window across a DPI boundary lets the user shrink
  past `MinWidth`. The implementation must include the message hook.

### 5.2 Surface

```csharp
public uint ReactorWindow.Dpi { get; }                    // 96, 120, 144, 192, ...
public event EventHandler<uint> DpiChanged;
public double ReactorWindow.DipScale => Dpi / 96.0;
public uint RenderContext.UseDpi();                       // re-renders on change
```

`Window.Bounds` from WinUI is already DIPs; the existing
`UseWindowSize(Window)` hook (`RenderContext.cs:929`) keeps working and
gets a parameterless overload that resolves the current host's window.

## §6 Lifecycle and shutdown

### 6.1 Startup

```
Process start
  └── ReactorApp.Run(...)
        ├── DPI awareness (SetProcessDpiAwarenessContext, unchanged)
        ├── STA thread (unchanged)
        ├── Application.Start
        │     └── ReactorApplication ctor (loads XamlControlsResources, unchanged)
        └── OnLaunched
              ├── ReactorApp.UIDispatcher = current DispatcherQueue
              ├── startup(ReactorAppContext) callback fires
              │     └── ctx.OpenWindow(spec, root)
              │           ├── new Window
              │           ├── apply chrome from spec
              │           ├── new ReactorHost(window)
              │           ├── host.Mount(root())
              │           ├── apply backdrop / icon / presenter
              │           ├── restore persistence (if PersistenceId set)
              │           ├── window.Activate() (if ActivateOnOpen)
              │           └── PrimaryWindow = first window opened
              └── settle
```

### 6.2 Shutdown policies

"Top-level surfaces" means **windows and tray icons** — both count
toward shutdown decisions.

- **OnPrimaryWindowClosed** *(default)* — closing the primary window
  exits the process, regardless of other windows or tray icons still
  open. Matches today's `Run<TRoot>` semantics. Picking this policy
  with zero initial windows would exit immediately.
- **OnLastSurfaceClosed** — exit when the last window **and** the
  last tray icon have closed. The right default for apps where
  closing all visible surfaces should mean "I'm done with the app."
  Replaces what the previous draft called `OnLastWindowClosed` —
  treating tray as a peer means it has to count.
- **Explicit** — surfaces close, but the process keeps running until
  `ReactorApp.Exit()` is called. The supported policy for
  **tray-only startup** (§13.6), background sync agents, headless
  window respawn, and any other shape where "no surfaces open" is a
  valid running state.

The startup callback is allowed to open zero surfaces. `ReactorApp.Run`
does not require at least one `OpenWindow` or `OpenTrayIcon` call —
only that the selected `ShutdownPolicy` permits the resulting state.

### 6.3 Per-window teardown

- `Window.Closed` → `ReactorWindow.Closed` event
  → `Host.Dispose()` (already wired via `_closedHandler` in
  `ReactorHost.cs:208`)
  → remove from `ReactorApp.Windows`
  → fire `WindowClosed`
  → if shutdown policy is satisfied, `ReactorApp.Exit()`.
- `Closing` (new) fires *before* `Window.Close()` is honored, so
  `UseClosingGuard` can cancel.

## §7 Hooks

```csharp
// Returns the ReactorWindow hosting the current component, or null
// when the component renders outside a window (e.g. tray-icon flyout
// content — see §7.1).
ReactorWindow? RenderContext.UseWindow();

// DIP size of the host window; re-renders on resize.
// Returns (0, 0) when called outside a window (e.g. tray-flyout content).
(double Width, double Height) RenderContext.UseWindowSize();

// Per-monitor DPI; re-renders on DPI change.
// Returns the system primary-monitor DPI when called outside a window.
uint RenderContext.UseDpi();

// Window state; re-renders on minimize/maximize/restore/etc.
// Returns Normal when called outside a window.
WindowState RenderContext.UseWindowState();

// Activation; re-renders on activated/deactivated.
// Returns true when called outside a window (the flyout is "active" while shown).
bool RenderContext.UseIsActive();

// Confirmation gate for Closing — return false to cancel close.
// No-op when called outside a window (no Closing event source).
// The function runs synchronously on the UI thread; for async confirms,
// cancel the close and re-issue programmatically when the user decides.
void RenderContext.UseClosingGuard(Func<bool> canClose);

// Open (or reuse) a secondary window. The returned handle's identity
// is stable across re-renders so long as `key` is stable.
ReactorWindow RenderContext.UseOpenWindow(
    WindowKey key,
    WindowSpec spec,
    Func<Component> factory);
```

The mirror methods on `Component` (currently `UseWindowSize(Window)` /
`UseBreakpoint(Window)` — `Component.cs:57-60`) get parameterless
overloads. The explicit `Window`-typed overloads stay for back compat
and for consumers that hold a reference to a non-Reactor `Window`.

### 7.1 Reaching the host window from a render

`UseWindow()` is the canonical answer to "which window is rendering
me?". It does an O(1) field read on the current `ReactorHost` — no
subscription, no re-render trigger. Use it whenever you need the
window handle (open another window with this one as `Owner`, set
taskbar progress, dispatch a window-level command, etc.). For
behavior that should re-render on changes, use the targeted hooks:
`UseWindowSize`, `UseDpi`, `UseWindowState`, `UseIsActive`.

**Returns `null` for tray-icon flyout content.** A tray icon's
flyout (§11.4) is reconciled into a hidden internal popup window,
not a `ReactorWindow`. Components that may render in either context
should null-check:

```csharp
class StatusBadge : Component
{
    protected override Element Render()
    {
        var window = UseWindow();
        // Same component used inside the main window AND in the tray
        // flyout. The tray-flyout case has no window handle.
        var dpiHint = window is null ? "" : $" @ {window.Dpi}dpi";
        return Text($"Status: connected{dpiHint}");
    }
}
```

For components that only ever render inside a window (the common
case), `UseWindow()!` is fine.

## §8 Persistence

`WindowSpec.PersistenceId` is the opt-in. When set:

1. **On `Window.Closed`**, serialize `WINDOWPLACEMENT` plus the current
   monitor layout to a configurable storage adapter.
2. **On the first `WM_SHOWWINDOW`** after creation, read it back and
   call `SetWindowPlacement` if and only if the monitor layout still
   matches.

Storage abstraction:

```csharp
public interface IWindowPersistenceStore
{
    bool TryRead(string id, out byte[] data);
    void Write(string id, byte[] data);
}
```

Defaults:

- Packaged apps: `ApplicationData.Current.LocalSettings` adapter
  (matches WinUIEx behavior).
- Unpackaged apps: a JSON file under
  `%LOCALAPPDATA%/<ProcessName>/reactor-windows.json`. This is the
  primary case for Reactor.

Apps can register their own store via
`ReactorApp.WindowPersistenceStore = new MyStore();` before the first
`OpenWindow`. The implementation borrows the layout-fingerprint check
from `WinUIEx.WindowManager.LoadPersistence` so a window restored to a
disconnected monitor falls back to default placement.

## §9 Owned and modal windows

`WindowSpec.Owner` declares a parent-child relationship, mapped to
`AppWindow.SetParent` (or the Win32 owner handle) at apply time. Owned
windows:

- Move with their owner when the owner is dragged.
- Minimize with the owner.
- Are not shown separately in the taskbar (unless `IsShownInSwitchers`
  overrides).

Modal top-level windows (`IsModal` flag) are *not* in v1: `AppWindow`
does not expose modal directly, the workaround is to subclass
`overlappedPresenter.IsModal` (currently throwing on lifted WinUI per
WinUIEx `WindowEx.cs:312`). We document this as a known gap and
recommend `ContentDialog` for the in-window modal case.

## §10 Devtools and MCP

The devtools `WindowRegistry`
(`Hosting/Devtools/WindowRegistry.cs:21`) already attaches multiple
windows. `ReactorAppContext.OpenWindow` will:

- emit `Reactor.WindowOpened(window)` so `WindowRegistry.Attach` is
  called from a single place;
- emit `Reactor.WindowClosed(window)` so `WindowRegistry.Detach`
  runs on close;
- pin the **first** opened window as `isMain: true` (matches today's
  behavior at `ReactorApp.cs:545`).

New MCP tools:
- `windows.list` returns id / key / title / size / dpi / state / isMain
- `windows.activate(id)`
- `windows.close(id)`
- `windows.open(spec, componentName)` — gated by the existing
  component-allowlist check (`ReactorApp.cs:474-485`) so loopback
  callers can't spawn arbitrary components.

## §11 Shell integration

The Windows shell exposes a handful of features that attach to a
top-level window: taskbar progress, overlay icons, jump lists, the
notification-area (tray) icon, and thumbnail toolbars. All of these
key off the window's HWND (or, for jump lists, the process AppUserModel
ID). They belong on `ReactorWindow` because their lifetime, identity,
and threading are bound to the window.

We split the surface into five named features. Each is independently
opt-in; apps that don't touch them pay no startup cost.

### 11.1 Taskbar progress

```csharp
public sealed class TaskbarProgress
{
    public TaskbarProgressState State { get; set; }   // None | Indeterminate | Normal | Paused | Error
    public double Value { get; set; }                  // 0.0 – 1.0; ignored when Indeterminate
    public void Clear();                               // shorthand for State = None
}

public enum TaskbarProgressState { None, Indeterminate, Normal, Paused, Error }

// On ReactorWindow:
public TaskbarProgress Progress { get; }
```

Implementation: `ITaskbarList3.SetProgressValue` and `SetProgressState`,
keyed off the window's HWND. The COM object is created lazily on first
property write — apps that never set progress never instantiate it.
DPI-independent.

```csharp
class Downloader : Component
{
    protected override Element Render()
    {
        var (progress, setProgress) = UseState(0.0);
        var window = UseWindow();

        UseEffect(() =>
        {
            window.Progress.State = TaskbarProgressState.Normal;
            window.Progress.Value = progress;
            return () => window.Progress.Clear();
        }, progress);

        return /* … */;
    }
}
```

### 11.2 Taskbar overlay icon and badge

```csharp
public sealed class TaskbarOverlay
{
    public WindowIcon? Icon { get; set; }              // null clears
    public string? AccessibleDescription { get; set; }
}

// On ReactorWindow:
public TaskbarOverlay Overlay { get; }
```

Implementation: `ITaskbarList3.SetOverlayIcon`. Used for unread-count
badges, status indicators, etc. Up to 16 × 16 logical (scaled per DPI).

### 11.3 Jump list

The jump list is *process-scoped* (keyed by the process's
AppUserModel ID), not per-window — but it's most often configured
during window setup, so the API lives next to the window primitive.

```csharp
public sealed record JumpListItem(
    string Title,
    string Arguments,
    JumpListItemKind Kind = JumpListItemKind.Task,
    string? Description = null,
    WindowIcon? Icon = null,
    string? GroupCategory = null)      // for Custom-category items
{
    /// Convenience factory for the "Arguments == deep-link URI" convention
    /// used with <c>LaunchActivation.TryResolve&lt;TRoute&gt;(map)</c>.
    public static JumpListItem ForUri(string title, string uri,
        string? description = null, WindowIcon? icon = null,
        string? groupCategory = null);
}

public enum JumpListItemKind { Task, Custom, Separator }

public static class JumpList
{
    public static string? AppUserModelId { get; set; }

    public static Task UpdateAsync(IEnumerable<JumpListItem> items);
    public static Task ClearAsync();

    // System-controlled categories. SetVisibility(true) shows them,
    // false hides; recent/frequent are managed by the OS, the app
    // only chooses visibility.
    public static bool ShowRecent { get; set; }
    public static bool ShowFrequent { get; set; }
}
```

Implementation: `Windows.UI.StartScreen.JumpList` (WinRT) for the
async update API, with a Win32 `ICustomDestinationList` fallback for
unpackaged apps where the WinRT API throws.

The `JumpListItem.Arguments` round-trip back to the process via the
existing command-line entrypoint; `Reactor.Cli` already exposes a
process-arg parser the app can reuse.

### 11.4 System tray icon

A tray icon is a **peer of `ReactorWindow`**, not a feature attached
to one. Both are top-level OS surfaces with their own lifetime,
their own user-input events, their own reconciled Reactor content
(the window's content tree, the tray's flyout), and either can be
the app's user-facing entry point. The API mirrors `OpenWindow` /
`ReactorWindow` to make that peerage obvious in code:

| Window                                  | Tray icon                                     |
|-----------------------------------------|-----------------------------------------------|
| `WindowSpec`                            | `TrayIconSpec`                                |
| `ReactorApp.OpenWindow(spec, factory)`  | `ReactorApp.OpenTrayIcon(spec)`               |
| `ReactorWindow` handle                  | `ReactorTrayIcon` handle                      |
| `ReactorApp.Windows`                    | `ReactorApp.TrayIcons`                        |
| `ReactorApp.FindWindow(key)`            | `ReactorApp.FindTrayIcon(key)`                |
| `WindowOpened` / `WindowClosed` events  | `TrayIconOpened` / `TrayIconClosed` events    |
| `Update(spec)` / `Close()`              | `Update(spec)` / `Close()`                    |
| Reconciles a content tree continuously  | Reconciles flyout content on demand           |

A tray icon does not have size, position, presenter, DPI awareness,
persistence, owner, or modality — those are window-shaped concepts.
Everything else is the same shape.

```csharp
public sealed record TrayIconSpec(
    WindowIcon Icon,
    string Tooltip,
    WindowKey? Key = null,
    bool IsVisible = true);

public sealed class ReactorTrayIcon : IDisposable
{
    public string Id { get; }
    public WindowKey? Key { get; }
    public TrayIconSpec Spec { get; }   // last applied snapshot

    public WindowIcon Icon { get; set; }
    public string Tooltip { get; set; }
    public bool IsVisible { get; set; }

    public event EventHandler? Click;
    public event EventHandler? DoubleClick;
    public event EventHandler? RightClick;

    public void ShowFlyout(Element flyoutContent);
    public void HideFlyout();
    public void Update(TrayIconSpec spec);
    public void Close();   // == Dispose; removes the icon from the tray
}

// On ReactorApp / ReactorAppContext:
public static ReactorTrayIcon OpenTrayIcon(TrayIconSpec spec);
public static IReadOnlyList<ReactorTrayIcon> TrayIcons { get; }
public static ReactorTrayIcon? FindTrayIcon(WindowKey key);
public static event EventHandler<ReactorTrayIcon>? TrayIconOpened;
public static event EventHandler<ReactorTrayIcon>? TrayIconClosed;
```

The flyout content goes through the reconciler exactly like the rest
of Reactor — the API takes an `Element`, not a WinUI control.
Implementation borrows from `WinUIEx.TrayIcon` (`Shell_NotifyIcon` +
a hidden popup window for the flyout `XamlRoot`); the hidden window
is internal and never exposed to app code.

A common UX pattern is "minimize to tray". This is built on the
public surface, not baked in:

```csharp
class App : Component
{
    protected override Element Render()
    {
        var window = UseWindow();
        var tray = UseTrayIcon(new TrayIconSpec(Icons.AppIcon, "MyApp"));

        UseEffect(() =>
        {
            void onState(object? s, WindowState st)
            {
                if (st == WindowState.Minimized) window.Hide();
            }
            void onClick(object? s, EventArgs e) => window.Show();
            window.StateChanged += onState;
            tray.Click += onClick;
            return () => { window.StateChanged -= onState; tray.Click -= onClick; };
        });

        return /* … */;
    }
}
```

`UseTrayIcon` is a thin hook over `ReactorApp.OpenTrayIcon` that
calls `Close()` on the icon during the calling component's cleanup.
For tray-only apps that have no component tree at startup (§13.6),
call `ReactorApp.OpenTrayIcon` directly from the startup callback —
the same way you'd call `OpenWindow`.

### 11.5 Thumbnail toolbar

```csharp
public sealed record ThumbnailToolbarButton(
    string Id,
    WindowIcon Icon,
    string Tooltip,
    Action OnClick,
    bool IsEnabled = true,
    bool IsVisible = true,
    bool DismissOnClick = false);

// On ReactorWindow:
public void SetThumbnailToolbar(IReadOnlyList<ThumbnailToolbarButton> buttons);
public void ClearThumbnailToolbar();
```

Implementation: `ITaskbarList3.ThumbBarAddButtons` /
`ThumbBarUpdateButtons`. Up to seven buttons; can be added once per
HWND, then updated. The list is diffed against the previous call so
a per-render `SetThumbnailToolbar(buttons)` from a hook re-applies
only the changed buttons.

### 11.6 Activation context

A useful detail: when the app is launched from a jump list, a tray
right-click "Open" entry, or a thumbnail toolbar action, the
activation arrives at `Application.OnLaunched` with arguments. The
existing `ReactorAppContext` already gates the startup callback;
a new `ctx.LaunchActivation` property exposes the parsed
`{Kind, Arguments}` so apps can branch on launch source.

```csharp
public enum LaunchKind { Normal, JumpList, Toast, Protocol, File, Tray }
public sealed record LaunchActivation(LaunchKind Kind, string? Arguments, IReadOnlyList<string> Files)
{
    /// <summary>
    /// Convenience over <see cref="DeepLinkMap{TRoute}.Resolve(string)"/>:
    /// treats <see cref="Arguments"/> as a deep-link URI and resolves it via
    /// the supplied map. Returns <c>false</c> when there is no argument or
    /// no route matches. (spec 036 §11.6)
    /// </summary>
    public bool TryResolve<TRoute>(DeepLinkMap<TRoute> map, out DeepLinkResult<TRoute> result)
        where TRoute : notnull;
}
```

#### Jump list ↔ navigation integration

`JumpListItem.Arguments` is a free-form command-line string by OS
contract, but Reactor's *recommended convention* is that it carries a
deep-link URI string for the app's `DeepLinkMap<TRoute>`. The
`JumpListItem.ForUri(title, uri, ...)` factory makes the convention
discoverable, and the launch handoff is a one-liner in the startup
callback:

```csharp
ReactorApp.Run(ctx =>
{
    var map = new DeepLinkMap<MyRoute>()
        .Map("/", _ => new HomeRoute())
        .Map("/settings/{id}", a => new SettingsRoute(a.Get<string>("id")));

    var window = ctx.OpenWindow(new WindowSpec(...), () => new MainShell(map));

    if (ctx.LaunchActivation.TryResolve(map, out var dl) && dl.Matched)
        window.Host.NavigateTo(dl.Routes);   // however the app's nav root consumes routes
});
```

The same convention applies to tray-icon "Open" / context-menu entries
and thumbnail-toolbar buttons that re-launch the process — every shell
surface that round-trips through the OS arg buffer uses the same
URI-string convention. Apps can still pass arbitrary strings (legacy
launchers, batch flags); `TryResolve` simply returns `false` when the
argument doesn't match a registered pattern. *Implementation-time
addition (Phase 8): the spec originally listed only the raw record;
the `TryResolve` helper plus `JumpListItem.ForUri` factory add the
navigation bridge without expanding the OS-facing surface.*

### 11.7 Threading and lifetime

All shell-integration COM (`ITaskbarList3`, the tray-icon hidden
window) lives on the UI thread. Per-feature objects are owned by the
`ReactorWindow` and disposed when the window closes. The `JumpList`
static is process-scoped and survives window close; it disposes on
`ReactorApp.Exit`.

## §12 Migration

### 12.1 The DIP behavior change

`ReactorApp.Run<TRoot>(title, width, height, ...)` keeps the same
signature. `width` and `height` now mean **DIPs**, not pixels. On a
100 % display this is identical to today; on 200 % the window is twice
as large in physical pixels. This is a **breaking visual change** in
exactly one direction (the new behavior is the historically intended
one — every other framework treats these as DIPs).

We do not add a `legacyPixelSize: true` opt-out. Instead:

- The first call to `Run<TRoot>` from a process that hasn't pinned a
  Reactor version >= the release containing this change emits one
  `[reactor]` info line on stderr describing the new size semantics.
- The release notes flag this prominently. Apps that liked the old
  size can divide by their display scale.

### 12.2 Source-compat path

```
Old (today):                                        New (no source change):
ReactorApp.Run<App>("Title", 1024, 768)             same call site
                                                    width=1024, height=768 are now DIPs
```

### 12.3 Configure callback

`Action<ReactorHost> configure` stays. Apps that reach into
`host.Window.AppWindow` keep working — the underlying types are
unchanged. The new shape is:

```csharp
ReactorApp.Run(ctx =>
{
    var win = ctx.OpenWindow(
        new WindowSpec(Title: "Demo", Width: 1024, Height: 768),
        () => new App());

    win.Closed += (_, _) => Log("primary closed");
});
```

### 12.4 Removal plan

- `ReactorApp.ActiveHost` → keep, marked `[Obsolete]` for two releases,
  delete in the third.
- `ReactorHost.MainDispatcherQueue` → keep, route through
  `ReactorApp.UIDispatcher`, delete in two releases.
- The current `ReactorAppOptions` record is internal — delete in the
  same release that ships the new model. (No source consumer.)

### 12.5 Sample updates

The 9 `samples/**` `Run<TRoot>` call sites need no source changes.
`samples/MultiWindowDemo/` is added in the same PR demonstrating
`OpenWindow` and `UseOpenWindow`.

## §13 Examples

### 13.1 Single-window app — unchanged

```csharp
ReactorApp.Run<TodoApp>("Todos", width: 900, height: 720
#if DEBUG
    , devtools: true
#endif
);
```

Underneath, this is:

```csharp
ReactorApp.Run(ctx =>
{
    ctx.OpenWindow(
        new WindowSpec(Title: "Todos", Width: 900, Height: 720),
        () => new TodoApp());
});
```

### 13.2 Settings window opened from a menu

```csharp
class MainShell : Component
{
    protected override Element Render()
    {
        var openSettings = UseCallback(() =>
        {
            UseOpenWindow(
                "settings",
                new WindowSpec(
                    Title: "Settings",
                    Width: 720, Height: 540,
                    MinWidth: 480, MinHeight: 360,
                    Owner: UseWindow(),
                    PersistenceId: "settings"),
                () => new SettingsPage());
        });

        return VStack(
            MenuBar(
                MenuItem("Settings…", openSettings)),
            // …rest of the shell
        );
    }
}
```

`UseOpenWindow` returns the same `ReactorWindow` if one is already open
under the key `"settings"`, so clicking the menu item twice doesn't
spawn duplicates.

### 13.3 DPI-aware layout decision

```csharp
class ResponsiveShell : Component
{
    protected override Element Render()
    {
        var (w, _) = UseWindowSize();
        var dpi = UseDpi();
        var compact = w < 720;

        return compact ? CompactLayout() : WideLayout();
    }
}
```

### 13.4 Closing guard

```csharp
class Editor : Component
{
    protected override Element Render()
    {
        var (dirty, setDirty) = UseState(false);

        UseClosingGuard(() =>
        {
            if (!dirty) return true;
            // synchronous confirm; for async UX, return false and re-trigger Close()
            return ShowConfirmDialog("Discard unsaved changes?");
        });

        return /* … */;
    }
}
```

### 13.5 Multi-window startup

```csharp
ReactorApp.ShutdownPolicy = ShutdownPolicy.OnLastSurfaceClosed;

ReactorApp.Run(ctx =>
{
    ctx.OpenWindow(
        new WindowSpec(Title: "Main", Width: 1280, Height: 800,
                       PersistenceId: "main"),
        () => new MainShell());

    ctx.OpenWindow(
        new WindowSpec(Title: "Inspector", Width: 480, Height: 800,
                       PersistenceId: "inspector",
                       StartPosition: WindowStartPosition.RestoreFromPersistence),
        () => new InspectorShell());
});
```

### 13.6 Tray-only startup — no initial window

A class of apps (chat clients, sync agents, clipboard managers,
quick-launchers) wants to live in the system tray with no visible
window at startup. The user opens a window on demand via the tray
icon; closing it returns the app to its tray-only state. The app
exits only via an explicit "Quit" command.

The tray icon **is** the entry point — it's the app's primary
top-level surface. The API treats it as a peer of `OpenWindow`:

```csharp
ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;

ReactorApp.Run(ctx =>
{
    var tray = ctx.OpenTrayIcon(new TrayIconSpec(
        Key: "main",
        Icon: WindowIcon.FromResource("Assets/tray.ico"),
        Tooltip: "Sync Agent — idle"));

    // Single-instance window keyed by "main". Opening it twice from
    // a double-click reuses the existing window. Closing it removes
    // the entry from ReactorApp.Windows; the next click reopens.
    void ToggleMainWindow()
    {
        if (ReactorApp.FindWindow("main") is { } existing)
        {
            existing.Activate();
            return;
        }

        ReactorApp.OpenWindow(
            new WindowSpec(
                Key: "main",
                Title: "Sync Agent",
                Width: 720, Height: 520,
                StartPosition: WindowStartPosition.RestoreFromPersistence,
                PersistenceId: "main"),
            () => new SyncAgentShell());
    }

    tray.Click += (_, _) => ToggleMainWindow();
    tray.RightClick += (_, _) => tray.ShowFlyout(BuildContextMenu());

    Element BuildContextMenu() =>
        VStack(
            Button("Open", () => { ToggleMainWindow(); tray.HideFlyout(); }),
            Button("Pause sync", () => SyncService.Pause()),
            Separator(),
            Button("Quit", () => ReactorApp.Exit()));
});
```

Key behaviors this exercises:

- The startup callback opens **only a tray icon, no window** — the
  message loop runs because `ShutdownPolicy.Explicit` doesn't gate
  on surfaces.
- `OpenTrayIcon` and `OpenWindow` share the same shape. A reader
  who knows one knows the other.
- The tray icon's right-click flyout content is a Reactor `Element`
  reconciled into the hidden flyout window.
- Closing the main window does **not** exit the app — the tray icon
  is still open. `ReactorApp.Exit()` is the only path that ends the
  process under this policy.
- A second click of the tray icon while the window is already open
  calls `Activate()` on the existing window rather than spawning a
  new one — `WindowKey` semantics fall out naturally because we
  used `FindWindow("main")` before `OpenWindow`.

The complementary pattern — start with a window visible, fall back
to tray-only when the user closes it — uses
`ShutdownPolicy.Explicit` plus a tray icon as the persistent
surface:

```csharp
ReactorApp.Run(ctx =>
{
    ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;

    var tray = ctx.OpenTrayIcon(new TrayIconSpec(/* … */));
    tray.Click += (_, _) => /* show / hide window */;

    ctx.OpenWindow(
        new WindowSpec(Key: "main", Title: "Chat", Width: 480, Height: 720),
        () => new ChatShell());
});
```

## §14 Implementation plan

The work splits into ~6 phased PRs so each lands behind a clearly tested
seam.

| Phase | Scope | Tests |
|---|---|---|
| **1. WindowSpec + ReactorWindow scaffold** | New types, `ReactorApp.Run(Action<ReactorAppContext>)` overload, `OpenWindow` plumbing. Existing `Run<TRoot>` rewritten on top of the new path. **No** DPI-DIP behavior change yet (still pixels). | Unit tests on spec diffing. Selftest smoke that the existing `Run<TRoot>` path still works. |
| **2. DPI awareness** | DIP-denominated sizing, `Dpi` / `DpiChanged`, `UseDpi`. Port WinUIEx's `GetDpiForWindow` + `SetWindowSize` helpers (no full WinUIEx dep). Behavior change: `Run<T>(width, height)` now treated as DIPs. Stderr info line on first run. | Selftest: open a window with `Width: 800` on a synthetic 200 % monitor → physical 1600. Selftest for `DpiChanged` on monitor crossing (skipped in CI single-monitor). |
| **3. Lifecycle / events / hooks** | `Activated`, `Deactivated`, `StateChanged`, `Closing`, `UseWindow`, `UseWindowSize` parameterless, `UseDpi`, `UseWindowState`, `UseIsActive`, `UseClosingGuard`. | Selftest fixtures for each hook. Unit tests for closing-guard cancellation. |
| **4. Multi-window + UseOpenWindow** | `ReactorAppContext.OpenWindow`, `UseOpenWindow(key, ...)`, `WindowOpened` / `WindowClosed`, `ShutdownPolicy`. Drop `ReactorHost.MainDispatcherQueue` static, route through `ReactorApp.UIDispatcher`. | Selftest: open secondary window, close it, reopen by key, verify host disposal. AppTest E2E: `MultiWindowDemo` smoke. |
| **5. Persistence + chrome** | `PersistenceId`, `IWindowPersistenceStore`, `Icon`, `Presenter`, `IsResizable` / `IsMinimizable` / `IsMaximizable`, min/max via `WM_GETMINMAXINFO` hook. | Unit test for persistence round-trip with monitor-fingerprint mismatch. |
| **6. Devtools / MCP** | `WindowRegistry` integration (open/close events), MCP tools `windows.list` / `windows.activate` / `windows.close` / `windows.open`. | MCP tool tests; `mur devtools` golden flow with two windows. |
| **7. Shell integration — progress + overlay + thumbnail toolbar** | `ITaskbarList3` wrapper (lazy COM init); `ReactorWindow.Progress`, `ReactorWindow.Overlay`, `SetThumbnailToolbar`. | Selftest fixtures verifying property writes don't throw on Windows 10 / Windows 11; AppTest E2E for progress visibility via UIA. |
| **8. Shell integration — jump list + tray + activation** | `JumpList` static, packaged + unpackaged paths, `ReactorTrayIcon` + `ReactorApp.OpenTrayIcon` (peer of `ReactorWindow`), `UseTrayIcon` hook, `LaunchActivation` plumbing. | Selftest for tray flyout reconciliation, tray-only startup with `ShutdownPolicy.Explicit`; E2E for jump list registration round-trip via `Reactor.Cli`. |

Each phase ships independently. Phases 4–6 are gated by 1–3. Phases 7–8
are gated by 1 (they need `ReactorWindow`) but otherwise stand alone
and can ship in parallel with 4–6.

## §15 Resolved questions

These were open at spec-acceptance review (2026-05-07) and have
since been answered. Recording the disposition here so the rationale
stays with the spec.

1. **Modal top-level windows.** *Deferred.* `OverlappedPresenter.IsModal`
   throws on lifted WinUI today; the hand-rolled
   `EnableWindow(parent, false)` + message-routing path is a sizeable
   side quest. We wait for the WinAppSDK fix and revisit in a
   follow-up. `ContentDialog` covers the common in-window modal case
   in the meantime.

2. **Tray icons.** *In scope, modeled as a peer of `ReactorWindow`.*
   `ReactorApp.OpenTrayIcon` is shaped exactly like
   `ReactorApp.OpenWindow`, the returned `ReactorTrayIcon` mirrors
   `ReactorWindow`'s handle shape, and `ReactorApp.TrayIcons` parallels
   `Windows`. A tray icon and a window can both be the app's user-
   facing entry point (see §13.6 for tray-only startup), so they
   share lifecycle and naming conventions. Ships in phase 8 (§14)
   and lives in core Reactor — no separate `Reactor.Tray` package.
   See §11.4 for the full API.

3. **Multi-instance / single-instance app pattern.** *Deferred.*
   Reactor v1 stays neutral on AppInstance redirection. `WindowKey`'s
   shape is process-scoped today and forward-compatible with a
   future cross-instance broadening; no API churn expected when we
   add it.

4. **Title bar customization.** *No expansion.* Reactor already has a
   `TitleBar(...)` factory (`src/Reactor/Elements/Dsl.cs:412`) that
   covers the title-bar customization story. We do not add a second
   primitive. `WindowSpec.ExtendsContentIntoTitleBar` toggles the
   content-extension behavior; everything else flows through the
   existing component.

5. **"Window-level" effects shorthand.** *Deferred.* A
   `UseWindowActivation(...)` wrapper is easy to add later if usage
   patterns demand it; `UseEffect` over the `Activated` / `Deactivated`
   events on `UseWindow()` covers the case today. Wait for sample-app
   evidence before introducing a second hook.

6. **`UseOpenWindow` cleanup semantics.** *Resolved as proposed:* if
   the parent component unmounts while the secondary window is open,
   we **do not** close it automatically. The user opened the window,
   the user (or the app code) closes it. Components that want the
   inverse behavior call `.Close()` from a `UseEffect` cleanup on the
   handle returned by `UseOpenWindow`.

## §16 Out of scope

- Non-Win32 windowing (§N1).
- Cross-window content drag (§N2).
- Reconciler-as-portal `Window(...)` element (§N3).
- Modal top-level windows (§9).
- Custom title-bar layout primitive (§N5).
