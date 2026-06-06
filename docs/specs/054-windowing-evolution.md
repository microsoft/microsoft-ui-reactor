# Windowing Evolution — Closing the WinUIEx / NSWindow Gap

## Status

**Implemented** — 2026-06-05. Builds on [spec 036 — Window Model](036-window-design.md), which
established `WindowSpec` / `ReactorWindow` / `ReactorApp.OpenWindow` and the multi-window
hosting story. This spec catches Reactor's windowing surface up to the gaps identified
by the WinUI windowing reform proposal — sizing / state / presenter ergonomics, persistence
defaults, modern title bar, advanced shell windowing — and to the WPF / NSWindow features
that Reactor app authors actually reach for once they outgrow the basics.

The source WinUI proposal is a XAML proposal; this spec is the Reactor translation: same
problem space, but using `WindowSpec` (immutable record) + `ReactorWindow` (imperative
handle) instead of XAML attached properties, and respecting Reactor's DIP-only / one-
source-of-truth conventions.

---

## Table of contents

- [§1 Motivation](#1-motivation)
- [§2 Goals / non-goals](#2-goals--non-goals)
- [§3 Gap matrix — source proposal ↔ Reactor today](#3-gap-matrix--source-proposal--reactor-today)
- [§4 Platform-quality tiering](#4-platform-quality-tiering)
- [§5 Tier A — rock-solid additions](#5-tier-a--rock-solid-additions)
- [§6 Tier B — deliverable with documented constraints](#6-tier-b--deliverable-with-documented-constraints)
- [§7 Tier C — cannot deliver at platform quality](#7-tier-c--cannot-deliver-at-platform-quality)
- [§8 Hook additions](#8-hook-additions)
- [§9 Breaking changes](#9-breaking-changes)
- [§10 Phased implementation plan](#10-phased-implementation-plan)
- [§11 Open questions](#11-open-questions)
- [§12 Out of scope](#12-out-of-scope)

---

## §1 Motivation

Spec 036 closed the most embarrassing gaps in Reactor's windowing story: DIPs
everywhere, multi-window, observable DPI / state, owned windows, persistence,
shell integration (taskbar progress, overlay, tray, thumbnail toolbar). For
the typical line-of-business desktop app, `WindowSpec` covers what a developer
needs.

It does not cover what a _modern shell app_ needs. The reference workloads:

- **PowerToys FancyZones** — transparent, click-through, on-top overlay.
- **PowerToys Run / Spotlight launchers** — borderless, floating, doesn't
  steal focus, drag-from-anywhere.
- **Photoshop-style tool palettes** — floating utility window, no taskbar,
  smaller chrome.
- **Peek-style previews** — borderless rounded window with backdrop.
- **Media player / picture-in-picture** — aspect-ratio-locked resize.
- **Wizards / dialogs that grow with content** — `SizeToContent`.
- **Multi-window apps that remember per-window placement** without the
  developer wiring up a 109-line `WindowStateHelper`.

Today, each of those scenarios bottoms out at one of:

1. **WinUIEx** — the de-facto Windows shell-window library; Reactor avoids
   the dependency but inherits all the same gaps.
2. **HWND interop** — every app rolls its own `WS_EX_LAYERED`,
   `WS_EX_TOOLWINDOW`, `DwmSetWindowAttribute` boilerplate.
3. **"Not possible in WinUI"** — the developer ships a worse app.

Reactor's value prop is "the platform every WinUI dev wishes they had."
Windowing should match.

---

## §2 Goals / non-goals

### Goals

- **G1.** Cover every source-proposal feature that has a clean Win32 /
  AppWindow / DWM implementation — `ResizeMode`, `AspectRatio`,
  `WindowStyle`, `WindowLevel`, `IsMovableByBackground`, `SizeToContent`,
  separated `ShowInTaskbar`, `Window.Left/Top` read-back, `LocationChanged`,
  `ZOrderChanged`, `SavePlacement`, one-call persistence (`WithPersistence`).
- **G2.** Be _honest_ about the features we can't ship at platform quality
  — true `IsTransparent` (XAML composition collides with `WS_EX_LAYERED`),
  arbitrary `CornerRadius` (DWM gives discrete options only), multi-tier
  `WindowLevel` (Win32 has one topmost bit, not NSWindow's level stack),
  vibrancy / HUD materials.
- **G3.** Keep Reactor's contract: immutable `WindowSpec` + diff-on-`Update`,
  DIPs in all sizes, one source of truth, no Win32 in user code.
- **G4.** Embrace breaking changes when they buy a cleaner surface.
  Reactor has no shipped customers yet — every legacy field, every
  alias, every "kept for compat" knob is a future tax on the framework
  for zero current benefit. Where two fields cover the same concept
  (`IsAlwaysOnTop` vs. `WindowLevel`, `IsShownInSwitchers` vs.
  `ShowInTaskbar` + `ShowInSwitcher`, `IsResizable` vs. `ResizeMode`),
  delete the old one outright. Migration notes go in the changelog;
  there is no obsolete-alias period.
- **G5.** Make the "do a 90% scenario in one record" promise true for the
  new scenarios: PowerToys Run = ~7 fields on `WindowSpec`, not a code-
  behind file.

### Non-goals

- **N1.** Modal top-level windows (`ShowDialog`, `DialogResult`, `Owner`-as-
  modal-parent). Reactor has `ContentDialog` and modal flyouts for the
  in-window case; modal top-level is a separate spec — same answer as
  spec 036 §9.
- **N2.** Replicating the WPF `WindowChrome` API verbatim. Reactor already
  has `TitleBar(...)` in the visual tree; the platform-spec analogue is
  layered on top.
- **N3.** Anything that requires shipping a custom WinUI compositor (real
  XAML transparency, vibrancy materials beyond what `SystemBackdrop`
  provides). Those need Windows App SDK platform work, not framework work.
- **N4.** Window-shape regions (non-rectangular windows). `SetWindowRgn`
  is anti-aliased poorly and breaks DWM shadows; we cover the only
  scenario apps actually want (rounded corners) via the DWM corner-
  preference API.

---

## §3 Gap matrix — source proposal ↔ Reactor today

Already covered by spec 036 (✅), evolving in this spec (▲), missing (❌), or designed but deferred (◯).

### Section 1 — Basic windowing

| Source proposal | Reactor today | Action |
| --- | --- | --- |
| `MinWidth` / `MinHeight` / `MaxWidth` / `MaxHeight` | `WindowSpec.Min/MaxWidth/Height` | ✅ |
| `Width` / `Height` initial | `WindowSpec.Width/Height` | ✅ |
| `Left` / `Top` (read-write live) | `SetPosition()` only — no read-back | ▲ §5.5 |
| `WindowState` enum | `WindowState` enum + `State` prop | ✅ |
| `Topmost` | `WindowSpec.IsAlwaysOnTop` | ✅ (subsumed by §6.4 `WindowLevel`) |
| `ResizeMode` (`CanResize`, `CanResizeWithGrip`, `CanMinimize`, `NoResize`) | `IsResizable` bool | ▲ §5.1 |
| `WindowStartupLocation` (`Manual`, `CenterScreen`, `CenterOwner`) | `WindowStartPosition` enum | ✅ (gains §5.6 `CenterOnCurrent`) |
| `ShowInTaskbar` (taskbar visibility) | `IsShownInSwitchers` (covers both Alt-Tab + taskbar) | ▲ §5.4 split |
| `Icon` | `WindowSpec.Icon` | ✅ |
| `ShowActivated` | `WindowSpec.ActivateOnOpen` + `NoActivate` | ✅ |
| `IsActive` (read-only) | `ReactorWindow.IsActive` | ✅ |
| `LocationChanged` / `StateChanged` events | `StateChanged` only | ▲ §5.5 add `PositionChanged` |
| `Activated` / `Deactivated` | `Activated` / `Deactivated` events | ✅ |
| `Activate()` bring-to-front | `Activate()` | ✅ |
| `IsMinimizable` / `IsMaximizable` | same | ✅ |
| `Dpi` + `DpiChanged` | same | ✅ |
| `PresenterKind` | `Presenter` (Overlapped / FullScreen / CompactOverlay) | ✅ |

### Section 2 — Persistence

| Source proposal | Reactor today | Action |
| --- | --- | --- |
| Default-on persistence | Opt-in via `StartPosition.RestoreFromPersistence` | ▲ §5.7 — stays opt-in, but one-call `WithPersistence(id)` helper |
| `PersistenceId` | `WindowSpec.PersistenceId` | ✅ |
| `IsPlacementPersisted` opt-out | implicit via `StartPosition` value | ▲ §5.7 explicit `PersistPlacement` bool |
| `PersistenceFallback` (`SystemDefault`/`CenterScreen`/`Manual`) | folded into `StartPosition` | ▲ §5.7 |
| `SavePlacement()` programmatic | not exposed | ▲ §5.8 |
| Multi-monitor clamp / DPI re-resolve | already implemented in `Persistence/WindowPlacementCodec.cs` | ✅ |

### Section 3 — Title bar

| Source proposal | Reactor today | Action |
| --- | --- | --- |
| `Window.TitleBar` declarative | `TitleBar(...)` element in the visual tree | ✅ (already the Reactor-idiomatic shape — §6.6) |
| Auto-`ExtendsContentIntoTitleBar` when title bar set | manual flag on `WindowSpec` | ▲ §6.6 — implicit when `TitleBar(...)` is the root child |
| Theming follows `RequestedTheme` | covered by `ThemeProvider` | ✅ |

### Section 4 — Custom & advanced windowing

| Source proposal | Reactor today | Action |
| --- | --- | --- |
| `WindowStyle` (`Default`/`None`/`ToolWindow`/`Hud`) | none — only `ExtendsContentIntoTitleBar` | ▲ §6.1 partial (Default / None / ToolWindow; **Hud rejected — see §7.4**) |
| `IsTransparent` | none — only `Opacity` (layered alpha) | ❌ §7.1 — fundamental XAML composition limit, ship _tinted transparency_ via `TransparentBackdrop` instead |
| `WindowLevel` enum (Normal/Floating/AlwaysOnTop/Overlay) | only `IsAlwaysOnTop` bool | ▲ §6.4 partial — 3 useful tiers, not NSWindow's level stack |
| `SizeToContent` | none — manual `SetSize` after measure | ▲ §6.3 |
| `AspectRatio` | none | ▲ §5.2 (rock-solid via `WM_SIZING`) |
| `IsMovableByBackground` / `DragMove()` | none | ▲ §5.3 (`GetCursorPos`-polled `AppWindow.Move` driven from `PointerPressed`; `WM_NCLBUTTONDOWN` synthesis is unreliable under WinUI 3) |
| `CornerRadius` | none on `WindowSpec` | ▲ §6.2 — discrete (`Default`/`Square`/`Rounded`/`Small`) via `DWMWA_WINDOW_CORNER_PREFERENCE`; arbitrary radius rejected (§7.2) |
| `IsHitTestVisible` (click-through) | `WindowSpec.IgnorePointerInput` + opacity prereq | ✅ (already shipped for tear-off; document for general use) |
| `Opacity` 0..1 | `WindowSpec.Opacity` | ✅ |
| `TaskbarItemInfo` (progress / overlay / description / thumb buttons / jump list) | `Progress` / `Overlay` / `ThumbnailToolbar` separately, no description, no jump list | ▲ §6.5 |
| `ZOrderChanged` event | none | ▲ §5.9 |

### Section 5 — Adjacent (tray, message pump)

| Source proposal | Reactor today | Action |
| --- | --- | --- |
| Tray icon | `TrayIconSpec` + `OpenTrayIcon` | ✅ |
| `WindowMessageReceived` escape hatch | internal `WindowMessageMonitor` | ◯ §7.6 — deferred, design preserved |
| Monitor enumeration | `DisplayArea` is reachable via AppWindow but no Reactor wrapper | ▲ §6.7 |
| Splash screen | not in scope of windowing | (separate spec) |
| `InitializeWithWindow` helpers for pickers | partially via `UseWindow()`.NativeWindow | ▲ §8.3 |

---

## §4 Platform-quality tiering

For each feature we ask the same question: **can Windows deliver this at the
quality bar users expect, or only with a known compromise?** That single
question separates "ship and forget" from "ship with footnotes."

- **Tier A — rock solid.** The OS has a stable, well-documented mechanism;
  Reactor adds an idiomatic shape over it. No surprises. **Default
  shipping target.** Examples: `WM_SIZING` for aspect ratio,
  `GetCursorPos`-polled `AppWindow.Move` for drag-from-anywhere,
  `DwmSetWindowAttribute` for corner
  preference, `AppWindow.Position` for read-back.
- **Tier B — works, with documented constraints.** The OS supports it but
  with caveats users should know about (e.g. `SizeToContent` needs a full
  layout pass, `WindowLevel.Overlay` doesn't actually float above other
  topmost windows, `CornerRadius` is discrete not continuous). Ship, but
  document the seam in xmldoc + spec.
- **Tier C — cannot deliver at platform quality.** WinUI XAML and DWM
  block the clean implementation; any shim we ship would be a worse-than-
  third-party experience. Don't ship. Document the gap and point users at
  the official workaround (typically `SystemBackdrop` + tinted brush, or
  HWND interop for FancyZones-style overlays). Examples: true alpha
  per-pixel transparency, NSWindow-style `.statusBar` z-order tier,
  vibrancy materials.

The rest of the spec works through the gaps tier by tier.

---

## §5 Tier A — rock-solid additions

These are straightforward Win32 / AppWindow features with a clean Reactor
shape. They drop into `WindowSpec` or `ReactorWindow` without ceremony.

### 5.1 `ResizeMode` — replace the `IsResizable` bool

The current `IsResizable` bool conflates three things:

1. Can the user drag the borders?
2. Are the minimize / maximize buttons enabled?
3. Is the resize grip visible?

The source proposal splits these into `ResizeMode` (`CanResize`,
`CanResizeWithGrip`, `CanMinimize`, `NoResize`). The grip variant is a WPF
throwback that the Windows 11 visual language doesn't render anyway. We
take the useful three:

```csharp
public enum WindowResizeMode
{
    /// <summary>User can drag borders, min and max buttons enabled (default).</summary>
    CanResize,

    /// <summary>User cannot drag borders; min/max state can still be set programmatically.</summary>
    NoResize,

    /// <summary>User cannot drag borders, but the minimize button is enabled.</summary>
    CanMinimize,
}
```

Surface on `WindowSpec`:

```csharp
public WindowResizeMode ResizeMode { get; init; } = WindowResizeMode.CanResize;
```

**Breaking change.** `IsResizable` is removed. `IsMinimizable` and
`IsMaximizable` stay — they're independent of the resize concept
(a `ResizeMode == NoResize` window can still have an enabled min button,
which is why `CanMinimize` is its own enum value).

**Implementation:** `OverlappedPresenter.IsResizable` + `IsMinimizable` +
`IsMaximizable` flags. Already exposed today; this is purely an API
ergonomics change.

### 5.2 `AspectRatio` — aspect-locked resize

**Platform quality: Tier A.** Win32 sends `WM_SIZING` with a mutable `RECT*`
during the resize drag loop, and the OS smoothly tracks whatever we put
back in it. WPF, every Win32 media player, and Photoshop all use this
exact mechanism. The drag is buttery — there is no flicker because the OS
hasn't committed the new bounds yet when we adjust them.

Reactor already routes `WM_SIZING` through `WindowMessageMonitor` (today
just to set `_userResized = true`). Add real handling.

Surface:

```csharp
public WindowSpec WithAspectRatio(double widthOverHeight) =>
    this with { AspectRatio = widthOverHeight };

// On WindowSpec:
public double? AspectRatio { get; init; }   // width / height; null = unconstrained
public AspectRatioBasis AspectRatioBasis { get; init; } = AspectRatioBasis.Window;

public enum AspectRatioBasis
{
    Window,   // ratio applies to the outer window rect (default; cheap, matches WM_SIZING)
    Client,   // ratio applies to the client (content) area; framework computes chrome inset via AdjustWindowRectExForDpi
}
```

Validation: `> 0` and finite (else throw); rejected when `ResizeMode ==
NoResize` (no drag means no constraint to apply) — that combination is a
spec mistake worth catching at the boundary, not silently ignoring.

**Window vs. client basis.** `AspectRatioBasis.Window` (the default) is
the cheap shape — `WM_SIZING` hands us the outer window rect and we
enforce the ratio on it directly. `AspectRatioBasis.Client` is what
media/game/canvas apps want: a 1:1 *content* area for a square video, a
16:9 viewport for a game. The framework subtracts the chrome inset
(caption + borders, via `AdjustWindowRectExForDpi` at the current
window style + DPI) before applying the ratio, then adds it back. The
ratio stays accurate across DPI changes and `WindowStyle` flips because
the inset is re-computed every message.

**`Client` basis silently falls back to `Window` when
`ExtendsContentIntoTitleBar = true`.** Once the app paints into the
title-bar area, the OS's notion of "client area" (which still excludes
the caption-button inset) no longer matches the developer's notion of
"content area" — the custom title bar lives inside the client area, so
constraining client-rect aspect would size the *title bar + content*
together, not the content alone. The framework can't disambiguate these
without an explicit content-rectangle declaration from the app, so for
now it conservatively treats `Client` as `Window` in that configuration.
A future iteration may add a `Window.SetContentDragRegion(rect)`-style
API for apps that want client-basis aspect ratio together with a custom
title bar.

**Maximize bypasses the lock — by design.** Clicking the maximize
caption button (or pressing <kbd>Win</kbd>+<kbd>↑</kbd>) sends
`WM_SYSCOMMAND` with `SC_MAXIMIZE`, which goes straight to
`ShowWindow(SW_MAXIMIZE)` without firing `WM_SIZING`. The window
expands to fill the work area regardless of `AspectRatio`. This is
intentional — users expect "maximize fills the work area"; an
AspectRatio that refuses to maximize would surprise everyone. Apps
that genuinely want to forbid maximize should set
`ResizeMode = CanMinimize` (disables the maximize button outright).
The lock re-engages on the next interactive drag-resize after restore.

**Edge handling.** The algorithm picks the master dimension from the
`wParam` of `WM_SIZING`:

| Drag handle | Master | Slave |
| --- | --- | --- |
| `WMSZ_LEFT` / `WMSZ_RIGHT` | width | height |
| `WMSZ_TOP` / `WMSZ_BOTTOM` | height | width |
| Corner | whichever has the larger user delta (sticks-to-mouse) |

Min/max constraints still apply through `WM_GETMINMAXINFO`; aspect-ratio
adjustment runs after the user's drag delta but the OS then re-clamps
through the existing min/max path. The two interact correctly because
`WM_SIZING` fires first.

**Runtime mutator:** `ReactorWindow.SetAspectRatio(double? ratio)` for
runtime changes (e.g. swapping aspects when the media player loads a new
file). Mirrors into `_spec` like other live setters.

### 5.3 `IsMovableByBackground` — drag-from-anywhere

**Platform quality: Tier A.** The reliable mechanism in WinUI 3 is a
**`GetCursorPos` polling drag** driven from a `PointerPressed` handler:

1. On `PointerPressed` (left button, root element, no interactive
   suppression): snapshot `GetCursorPos()` and `AppWindow.Position`,
   then start a `DispatcherQueueTimer` (~60Hz).
2. Each tick: if `GetAsyncKeyState(VK_LBUTTON)` shows the button is no
   longer held, stop. Otherwise, `GetCursorPos()` and
   `AppWindow.Move(initialWindowPos + cursorDelta)`.

Why not `WM_NCLBUTTONDOWN` + `HTCAPTION` (the WPF/WinForms `DragMove`
trick)? In WinUI 3 the top-level HWND never sees the `WM_LBUTTONDOWN`
that `DefWindowProc` looks for to enter mouse-track drag mode — pointer
input is routed through a child `InputSiteBridge` HWND. The synthesized
`WM_NCLBUTTONDOWN` silently falls back to the system-menu
cursor-follow Move mode (click does nothing; releasing then moving
the mouse moves the window), which is the wrong UX.
`WM_SYSCOMMAND`+`SC_MOVE|HTCAPTION` has the same problem for the
same reason. `Window.SetTitleBar(root)` works for drag but doesn't
reliably auto-exclude nested interactive controls (`TextBox` text
selection, etc.) — fine for a thin caption strip, wrong here.

The polling-timer approach is simple, reliable, and lets WinUI's normal
input routing handle interactive controls correctly (a `PointerPressed`
marked `Handled` by a `TextBox`/`Button` never reaches the root
handler). The trade-off vs. an OS-modal drag loop is **no Aero Snap
during the drag** — acceptable for the small floating windows
(command palettes, tool palettes) that need `IsMovableByBackground`.

Surface:

```csharp
public bool IsMovableByBackground { get; init; }
```

Plus a method for ad-hoc drag (e.g. from a custom toolbar):

```csharp
public sealed class ReactorWindow
{
    public void BeginDragMove();   // GetCursorPos baseline + 60Hz DispatcherQueueTimer
                                   // polling AppWindow.Move until GetAsyncKeyState(VK_LBUTTON)
                                   // shows the button is released. Returns immediately;
                                   // re-entrant calls while a drag is active no-op.
}
```

**Hit-test filtering.** Implementation registers a `PointerPressed`
handler on the root that calls `BeginDragMove` only if the original
source's bubbling chain contains no interactive controls (`Button`,
`TextBox`, etc.). A `Drag.Disabled()` extension method opts out subtrees.
Detail covered in [§5.3.1](#531-suppressing-drag-on-interactive-content).

#### 5.3.1 Suppressing drag on interactive content

Components that swallow the drag (text fields, scroll thumbs, anything in
a `ToolBar`) need a way to opt out. Two layers:

- Built-in interactives (`Button`, `ToggleButton`, `TextBox`, `Slider`,
  `ScrollViewer` thumbs, all `Selector` derivatives) are recognized by
  the drag dispatcher and never trigger the move.
- Authors mark custom interactive subtrees with `.Drag(false)`:

```csharp
HStack(
    Text("App"),
    Button("Settings").Drag(false))    // never starts a window drag
```

The flag is an attached `bool` modifier read by the dispatcher walking
the bubbling chain.

### 5.4 Split `ShowInTaskbar` from `IsShownInSwitchers`

Today `WindowSpec.IsShownInSwitchers` covers both Alt-Tab and taskbar.
The source proposal separates them — and so does Win32: `WS_EX_APPWINDOW`
forces a taskbar button, `WS_EX_TOOLWINDOW` removes it without affecting
Alt-Tab. Settings can disagree (tool palettes that show in Alt-Tab but
not taskbar are a real shape).

**Breaking change.** `IsShownInSwitchers` is removed and replaced with
two booleans:

```csharp
public bool ShowInTaskbar { get; init; } = true;
public bool ShowInSwitcher { get; init; } = true;
```
```

Apply path: `AppWindow.IsShownInSwitchers = ShowInSwitcher`, plus
`WS_EX_TOOLWINDOW` ↔ `WS_EX_APPWINDOW` toggling on the HWND for the
taskbar half (must `ShowWindow(SW_HIDE)` + `SW_SHOW` after the bit flip
because the shell only refreshes taskbar buttons on visibility change).

### 5.5 `Position` read-back and `PositionChanged` event

Today `ReactorWindow.SetPosition(x, y)` is write-only. `AppWindow.Position`
exposes the current physical position; the missing piece is a DIP
projection + change notification.

```csharp
public sealed class ReactorWindow
{
    public (double X, double Y) Position { get; }     // DIPs, snapshot
    public event EventHandler<WindowDipPositionChangedEventArgs>? PositionChanged;
}

public sealed class WindowDipPositionChangedEventArgs : EventArgs
{
    public (double X, double Y) Position { get; }
}
```

**Implementation.** `AppWindow.Changed` fires with `DidPositionChange`
already — wire it through. Position is cached in a `volatile` field like
`State` / `IsActive` so the property read is lock-free.

**Hook:** `RenderContext.UseWindowPosition()` returns the current DIP
position and re-renders on change. Useful for "snap-to-edge" UIs and
custom multi-window layouts.

### 5.6 `WindowStartPosition.CenterOnCurrent`

The current `WindowStartPosition` enum has `CenterOnPrimary` and
`CenterOnOwner`. The source proposal's `WindowStartupLocation.CenterScreen`
maps to neither — it means "center on the monitor we're about to appear
on" which, without explicit positioning, is usually the active foreground
monitor.

Add:

```csharp
public enum WindowStartPosition
{
    Default,
    CenterOnPrimary,
    CenterOnOwner,
    CenterOnCurrent,            // NEW — center on monitor under cursor
    RestoreFromPersistence,
    Manual,
}
```

Apply path: `GetCursorPos` → `MonitorFromPoint(MONITOR_DEFAULTTONEAREST)` →
work-area centering. Falls back to `CenterOnPrimary` when called before
window activation on a session without a cursor (RDP non-interactive,
service).

### 5.7 Opt-in persistence with a one-call helper

The source proposal argues persistence should be default-on with explicit
opt-out. After weighing it against Reactor's "spec is the truth" model
(same `WindowSpec` → same window, no surprising state pulled from disk),
we stay **opt-in** — but collapse the multi-flag ceremony into a single
helper so the common case is one line.

**Breaking change.** `WindowStartPosition.RestoreFromPersistence` is
removed. Persistence is configured through three independent fields plus
a helper on `WindowSpec`:

```csharp
public sealed record WindowSpec
{
    public string? PersistenceId { get; init; }

    /// <summary>
    /// Restore window position / size / state from the registered store on open
    /// and save on close. Ignored when <see cref="PersistenceId"/> is null.
    /// </summary>
    public bool PersistPlacement { get; init; }    // default: false

    /// <summary>
    /// Where the window opens when <see cref="PersistPlacement"/> is true
    /// but nothing is on disk yet (first run, cleared profile, schema reset).
    /// </summary>
    public WindowStartPosition PersistenceFallback { get; init; }
        = WindowStartPosition.Default;

    /// <summary>
    /// Configure persistence in one call: assigns <see cref="PersistenceId"/>,
    /// sets <see cref="PersistPlacement"/> to true, and (optionally) the fallback.
    /// </summary>
    public WindowSpec WithPersistence(
        string id,
        WindowStartPosition fallback = WindowStartPosition.Default)
        => this with
        {
            PersistenceId = id,
            PersistPlacement = true,
            PersistenceFallback = fallback,
        };
}
```

**Why opt-in over default-on:**

- `WindowSpec` stays pure data — identical specs produce identical
  windows regardless of disk state. Tests, snapshots, and "fresh launch"
  scenarios are predictable.
- `PersistenceId` keeps a single, decoupled meaning ("identity for any
  persistence subsystem"). Future opt-ins like `PersistMaximizedState`,
  `PersistLayout`, etc. follow the same shape without an asymmetry.
- The first-time author writes `PersistPlacement = true` (or calls
  `WithPersistence(...)`) and is therefore aware that windows will
  reposition unpredictably between runs.

**Why the helper:**

- Three fields × N windows would be boilerplate-heavy. The helper makes
  the common case `WindowSpec { Title = "Main" }.WithPersistence("main")`
  — same syntactic weight as the source proposal's default-on.
- The helper is the documented canonical form; raw field assignment
  remains supported for advanced cases (different fallback per window,
  conditional persistence).

**Usage patterns:**

```csharp
// Common case — single line, restore on open, save on close.
new WindowSpec { Title = "Editor" }
    .WithPersistence("editor.main");

// Opt out for a specific window even though it has an ID.
new WindowSpec
{
    Title = "Editor",
    PersistenceId = "editor.main",   // for future PersistLayout
    PersistPlacement = false,        // but don't restore window position
};

// Custom fallback when nothing is on disk.
new WindowSpec { Title = "Tools" }
    .WithPersistence("tools.palette", WindowStartPosition.CenterOnCurrent);
```

**Validation:**

- `PersistPlacement = true && PersistenceId = null` → throws at apply
  time. The two are meaningless apart.
- `PersistenceFallback = RestoreFromPersistence` is impossible (the
  enum value is gone).

### 5.8 `SavePlacement()` — programmatic save

```csharp
public sealed class ReactorWindow
{
    /// <summary>Force-save current placement to the registered IWindowPersistenceStore.</summary>
    /// <remarks>No-op when PersistenceId is null. Idempotent.</remarks>
    public void SavePlacement();
}
```

Useful for apps that want to checkpoint before a non-trivial shutdown
sequence, or that have a "save layout" command.

### 5.9 `ZOrderChanged` event

**Platform quality: Tier A.** Win32 sends `WM_WINDOWPOSCHANGED` with
`WINDOWPOS.hwndInsertAfter`. We can interpret the four possibilities:

- `HWND_TOP` (0) — moved to top
- `HWND_BOTTOM` (1) — moved to bottom
- `HWND_TOPMOST` (-1) — set topmost
- `HWND_NOTOPMOST` (-2) — un-topmost
- any other HWND — inserted after that window

For app authors the interesting question is "did I just get covered?"
The flag in `WINDOWPOS.flags` (`SWP_NOZORDER`) tells us whether the
z-order actually changed.

```csharp
public sealed class ReactorWindow
{
    public event EventHandler<WindowZOrderChangedEventArgs>? ZOrderChanged;
}

public sealed class WindowZOrderChangedEventArgs : EventArgs
{
    /// <summary>True if another window is now in front of this one.</summary>
    public bool IsCovered { get; }
    /// <summary>True if this window was raised to the top of its tier.</summary>
    public bool MovedToTop { get; }
}
```

Determining `IsCovered` accurately would require `GetWindow(GW_HWNDPREV)`
+ a visibility / intersection check on each event — feasible but
non-trivial. v1 ships the simpler shape: raise only on transitions where
`hwndInsertAfter != HWND_TOP/HWND_TOPMOST`. Document as "covered hint, not
ground truth" and add a `Hosting/Z/WindowOcclusionMonitor` follow-up if
overlay apps need pixel-accurate signals.

---

## §6 Tier B — deliverable with documented constraints

These ship but with caveats the developer needs to know. xmldoc and the
relevant guide page (`docs/guide/windowing.md`) carry the constraint.

### 6.1 `WindowStyle` — borderless and tool-window chrome

```csharp
public enum WindowStyle
{
    Default,        // standard chrome
    None,           // no border, no caption, no system menu
    ToolWindow,     // shorter caption, no taskbar by default (WS_EX_TOOLWINDOW)
}

// WindowSpec
public WindowStyle Style { get; init; } = WindowStyle.Default;
```

**Implementation tiers:**

| Style | Mechanism | Platform quality |
| --- | --- | --- |
| `Default` | `OverlappedPresenter.SetBorderAndTitleBar(true, true)` | A |
| `None` | `SetBorderAndTitleBar(false, false)` + `WS_POPUP` flag scrub | A — exactly what FancyZones / PowerToys Run use today |
| `ToolWindow` | `SetBorderAndTitleBar(true, true)` + `WS_EX_TOOLWINDOW` ext style | A — smaller chrome + auto-hidden from taskbar |

**Caveats documented:**

- `WindowStyle.None` removes the system menu (Alt+Space). Apps that need
  Alt+F4 still get it (it's a keyboard accelerator on the HWND, not the
  caption).
- `WindowStyle.None` _without_ `IsMovableByBackground` makes the window
  un-draggable. Validate: warn (not throw) at apply time when this
  combination would strand the user. (Hard throw would prevent legitimate
  uses like fixed-position FancyZones overlays.)
- `WindowStyle.ToolWindow` defaults `ShowInTaskbar = false` if the
  developer didn't set it explicitly.

**Rejected: `WindowStyle.Hud`.** NSWindow's HUD style needs vibrancy and
dark-tinted materials that DWM doesn't ship. See [§7.4](#74-hud-style).

### 6.2 `CornerRadius` — discrete, via DWM corner preference

**Platform quality: Tier B.** Windows 11 has
`DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ...)` with
four values:

- `DWMWCP_DEFAULT` — system decides
- `DWMWCP_DONOTROUND` — square corners
- `DWMWCP_ROUND` — standard rounded
- `DWMWCP_ROUNDSMALL` — slightly less rounded (for tool windows)

There is _no_ API for arbitrary corner radii without using `SetWindowRgn`
(which kills the DWM shadow and anti-aliases poorly).

The source proposal proposes `Window.CornerRadius` as a continuous
property. We ship the discrete enum because that's what the OS gives us:

```csharp
public enum WindowCornerStyle
{
    Default,    // OS chooses (typically Round on Win11, Square on Win10)
    Square,
    Rounded,
    RoundedSmall,
}

public WindowCornerStyle CornerStyle { get; init; } = WindowCornerStyle.Default;
```

**Caveats:**

- Windows 10 ignores this — corners are always square. Document.
- The "small" variant is only meaningful for tool windows; pairs naturally
  with `WindowStyle.ToolWindow`.
- A future spec _may_ add `CornerRadius` as a continuous double via
  `SetWindowRgn` + DWM shadow workaround, but it's intentionally not in
  this one.

### 6.3 `SizeToContent`

**Platform quality: Tier B.** WinUI does not expose a `SizeToContent` API
on `Window`, but the implementation is mechanical: measure the root
content's desired size after layout, call `AppWindow.Resize(...)` with
the result + chrome insets.

```csharp
public enum WindowSizeToContent
{
    Manual,             // default — Width/Height honored as-is
    Width,              // window width tracks content desired width
    Height,             // window height tracks content desired height
    WidthAndHeight,     // both
}

public WindowSizeToContent SizeToContent { get; init; } = WindowSizeToContent.Manual;
```

**Implementation.** Subscribe to the root `FrameworkElement.SizeChanged`
(or `LayoutUpdated` if the root chains through a `ScrollViewer`); on each
firing where the desired size differs from current, recompute the
required AppWindow size (content + non-client insets via
`AdjustWindowRectExForDpi`) and call `AppWindow.Resize`. Honor `Min/Max`
constraints throughout — `SizeToContent` does not override them.

**Caveats documented:**

- One frame of "content-too-small" flash on first paint because we can't
  measure before the OS commits the initial bounds. Mitigation:
  `WindowSpec.Width/Height` should be set to a sane starting point even
  when `SizeToContent` is on; the resize then narrows / grows from there.
- Conflicts with `AspectRatio` — both `SizeToContent` and `AspectRatio`
  set non-`Manual` is a `Validate()` error.
- Conflicts with `WindowState.Maximized` — when maximized, content sizing
  is a no-op until restored.

### 6.4 `WindowLevel` — three useful z-order tiers (not NSWindow's stack)

**Platform quality: Tier B.** Win32 has _one_ topmost bit
(`WS_EX_TOPMOST`), not NSWindow's level integer. We can layer a small
useful surface over it without lying about delivering NSWindow's full
model:

```csharp
public enum WindowLevel
{
    Normal,         // default — sits in the regular z-order
    Floating,       // stays above sibling Normal windows of the same app
    AlwaysOnTop,    // WS_EX_TOPMOST — above all non-topmost windows globally
}
```

Note the absences:

- No `Overlay` tier. NSWindow's `.statusBar` is _above_ even other
  topmost windows; on Windows there is no such tier. An overlay that
  needs to stay above other AlwaysOnTop windows has to actively re-assert
  topmost on `WM_ACTIVATEAPP` — and even then it loses to the start menu,
  task switcher, and lock screen. We don't pretend.
- No `.modalPanel`. Modal top-level is out of scope (§2 N1).

**Implementation:**

- `Normal` → strip `WS_EX_TOPMOST` if set.
- `Floating` → `SetWindowPos(HWND_TOP)` whenever a sibling owned-window
  activates, via a process-wide z-order monitor. Spec 036 §9 already
  tracks owned-window relationships.
- `AlwaysOnTop` → `SetWindowPos(HWND_TOPMOST)`.

**Breaking change.** `WindowSpec.IsAlwaysOnTop` is removed. Authors set
`Level = WindowLevel.AlwaysOnTop` instead. The single-purpose bool was a
strictly weaker version of the new enum.

### 6.5 `TaskbarItem` — group existing shell features

Today the taskbar features hang off `ReactorWindow` as separate
properties (`Progress`, `Overlay`, `SetThumbnailToolbar`). The source
proposal proposes a `TaskbarItemInfo` container for `ProgressState`,
`ProgressValue`, `OverlayIcon`, `Description`, `ThumbButtonInfos`. Two
items are missing in Reactor:

1. **`Description`** — the accessible name spoken when the user hovers
   the taskbar icon. Maps to `ITaskbarList3::SetThumbnailTooltip` (or, as
   a fallback, `WM_SETTEXT` on the taskbar item).
2. **Jump list** — the right-click menu under the taskbar button (recent,
   pinned, tasks). Maps to `ICustomDestinationList` (huge surface).

Plan: ship (1) as a simple property; defer (2) to a separate spec because
jump lists need a full `JumpListItem` / `JumpListTask` model + AppUserModel
ID registration, which is its own design.

```csharp
public sealed class TaskbarItem  // exposed via ReactorWindow.TaskbarItem
{
    public TaskbarProgress Progress { get; }
    public TaskbarOverlay Overlay { get; }
    public string? Description { get; set; }
    public void SetThumbnailToolbar(IReadOnlyList<ThumbnailToolbarButton> buttons);
    public void ClearThumbnailToolbar();
}

// On ReactorWindow:
public TaskbarItem TaskbarItem { get; }                   // grouping facade
// Existing shortcuts stay:
public TaskbarProgress Progress => TaskbarItem.Progress;
public TaskbarOverlay Overlay => TaskbarItem.Overlay;
```

No behavior change for the existing accessors — `Progress` / `Overlay`
continue to return the same COM-init lazy instances they do today;
`TaskbarItem` is a thin facade that holds the same instances.

### 6.6 Declarative `TitleBar` via the visual tree

The source proposal proposes `<Window.TitleBar>` as a typed property on
the XAML `Window`. Reactor's idiomatic equivalent _already exists_:
`TitleBar(...)` is an element you put in the tree, and the
`TitleBarDescriptor` already wires `Window.ExtendsContentIntoTitleBar` +
`Window.SetTitleBar(...)` when it mounts. The XAML spec's "no
boilerplate" goal is already met.

What's missing: the `WindowSpec.ExtendsContentIntoTitleBar` bool can be
inferred (when the root tree contains a `TitleBar(...)` as its first
child, the flag is implicit). Today, developers set both — the flag and
the element.

**Breaking change.** `WindowSpec.ExtendsContentIntoTitleBar` becomes
`bool?` (nullable, defaulting to `null`). When `null`, the
`TitleBarDescriptor.OnMount` path that already sets the flag is
authoritative. When `true` / `false`, the explicit setting wins (apps
that want extension without using `TitleBar(...)` — e.g. fully custom
chrome — still can). Existing code passing `true` still compiles thanks
to the implicit `bool → bool?` conversion; existing code _reading_ the
field needs a null check.

### 6.7 Monitor enumeration

`DisplayArea` is the WinUI primitive; Reactor users have to dig through
`AppWindow.Id` → `DisplayArea.GetFromWindowId` today. Add:

```csharp
public static class ReactorDisplay
{
    public static IReadOnlyList<DisplayInfo> Displays { get; }     // snapshot
    public static DisplayInfo Primary { get; }
    public static DisplayInfo NearestTo(double dipX, double dipY);
    public static event EventHandler? DisplayLayoutChanged;
}

public sealed record DisplayInfo
{
    public string Id { get; init; }                                // stable across enumerations
    public bool IsPrimary { get; init; }
    public (double X, double Y, double Width, double Height) WorkAreaDip { get; init; }
    public (double X, double Y, double Width, double Height) BoundsDip { get; init; }
    public uint Dpi { get; init; }
}
```

`DisplayLayoutChanged` rides on `WM_DISPLAYCHANGE` from any open window's
message monitor (the first window registers a singleton listener; closes
the last one unregisters). Hosting-internal — not a per-window concern.

DIPs in the public surface, converted from the OS's physical-pixel
`RECT`s using each monitor's own DPI. Mixed-DPI virtual-screen quirks
documented in xmldoc — `WorkAreaDip.X/Y` of a non-primary monitor are
"approximately" DIP, because Windows has no global DIP coordinate space.

---

## §7 Tier C — not shipping in v1

Two categories live here. The first (§7.1-§7.5) is what we **cannot
deliver at platform quality** — shipping them would mean shipping a
worse experience than what users get by going to HWND interop
directly. The second (§7.6) is what we **deferred for product reasons**
— we have a design that would work, but the cost / value didn't
clear the bar for v1. Each entry documents the gap honestly and, where
applicable, a workaround for the 5% of apps that need it.

### 7.1 True `IsTransparent` per-pixel alpha

**The block.** WinUI 3's compositor renders XAML into a DComp surface
attached to the window. `WS_EX_LAYERED` + per-pixel alpha
(`UpdateLayeredWindow` / `SetLayeredWindowAttributes` with
`LWA_COLORKEY`) requires a GDI back-buffer and bypasses DComp. The two
are mutually exclusive: enable layered, lose XAML rendering; enable XAML,
lose true transparency. WinUIEx's `IsTransparent` works only because it
uses `LWA_COLORKEY` (chroma-key transparency on a single solid color),
which:

- Aliases badly around anti-aliased edges (the key color shows through).
- Breaks if XAML ever renders that color elsewhere (it punches holes).
- Doesn't compose with backdrops.

That's worse than the WinUI status quo. We don't ship it.

**What we ship instead.** The supported transparency story is
`SystemBackdrop` with `TransparentBackdrop` (WinAppSDK 1.3+). This gives
a fully transparent client area, composes with DWM, doesn't break XAML,
doesn't need `WS_EX_LAYERED`. The `WindowSpec.Backdrop` modifier already
supports custom backdrops via `BackdropChoice.Of(Func<SystemBackdrop>)` —
the only addition is a built-in enum value:

```csharp
public enum BackdropKind
{
    None, Mica, MicaAlt, DesktopAcrylic, AcrylicThin,
    Transparent,    // NEW — WinAppSDK 1.3+ TransparentBackdrop
}
```

Apps that need true GDI/DComp-bypass transparency (FancyZones-style
overlays) must continue to drop to HWND interop. Document this in
[`docs/guide/windowing-advanced.md`](../guide/windowing-advanced.md)
with a working sample — but Reactor is not the layer that solves it.

**Acknowledged gap:** PowerToys FancyZones cannot be built in pure
Reactor today. That's a WinUI platform-spec issue we track but
don't shim.

### 7.2 Arbitrary `CornerRadius`

The DWM API gives four discrete corner styles — see [§6.2](#62-cornerradius--discrete-via-dwm-corner-preference).
Arbitrary continuous radii need `SetWindowRgn`, which:

- Disables the DWM drop shadow on the window.
- Anti-aliases poorly (the region is a binary mask, no alpha blend at
  the edge — the rounded corner has visible jaggies on 100% DPI).
- Breaks in the resize hot-path because the region must be recomputed on
  every `WM_SIZE` and applied via `SetWindowRgn(hwnd, hRgn, FALSE)` to
  avoid a redraw cascade.

Apps that want a continuous `CornerRadius` should render the chrome
themselves inside a `Window` with `WindowStyle.None`. Out of scope.

### 7.3 NSWindow-style `WindowLevel` stack

We ship three useful tiers in [§6.4](#64-windowlevel--three-useful-z-order-tiers-not-nswindows-stack)
— `Normal`, `Floating`, `AlwaysOnTop`. We do not ship NSWindow's level
integer (or its named tiers `.popUpMenu`, `.statusBar`, `.modalPanel`,
`.screenSaver`) because Win32 has _one_ topmost bit. Any "level above
topmost" implementation would be a polling loop that re-asserts
foreground via `SetWindowPos(HWND_TOPMOST)` on a timer — fragile, fights
the start menu, fights other topmost apps, and a clear sign Reactor is
the wrong tool for that scenario.

Apps that genuinely need overlay-on-top-of-overlay (lock screens,
accessibility magnifiers) belong in `UIAccess`-elevated processes with
manifest declarations, not in framework code.

### 7.4 HUD style

NSWindow's `.hudWindow` is dark vibrancy material + light-tinted controls
+ smaller caption. DWM has no vibrancy material with that exact look,
and WinUI's `SystemBackdrop` palette doesn't include a HUD variant.

Reactor apps that want a HUD aesthetic compose:

- `WindowStyle.None`
- `BackdropKind.DesktopAcrylic` with a dark tint via custom factory
- A custom title-bar element using `TitleBar(...)`
- `WindowLevel.Floating`

That's an app-level recipe, not a framework primitive. Document it as a
sample in `samples/HudWindow`. Don't add `WindowStyle.Hud`.

### 7.5 Vibrancy materials beyond `SystemBackdrop`

NSWindow exposes 11+ vibrancy materials (sidebar, menu, popover,
selection, content background, …). Windows exposes 4 system backdrops.
We ship what the platform ships and add custom factories via
`BackdropChoice.Of(Func<SystemBackdrop>)` for apps that build their own.
Nothing new in this spec; documented here so the gap is visible.

### 7.6 Public `WindowMessageReceived` — deferred

**Why deferred (not blocked).** The platform supports it cleanly —
`WindowMessageMonitor` already subclasses the HWND and routes Win32
messages internally, so exposing a curated public surface is a small,
local change. We held off in v1 because:

- **Customer pull is unproven.** No app we've prototyped so far has
  needed a Win32 message that isn't already covered by a typed Reactor
  API (`DpiChanged`, `WindowStateChanged`, `UseDisplays`, theme
  changes via the theme system, etc.).
- **The friendly-API cost is non-trivial.** A useful public surface is
  not raw `WParam` / `LParam` — it's typed event args per message with
  struct marshalling done inside the framework. Five typed events
  (DeviceChanged, PowerStateChanged, SystemSettingChanged,
  SessionEnding, SessionEnded) plus their `EventArgs` records is real
  API surface that has to be designed, tested, and supported.
- **An unsupported escape hatch already exists.** Apps that genuinely
  need raw message access today can call `UseWindow().NativeWindow`
  (WinUIEx) and subclass the HWND themselves. Documented as
  "unsupported, you own the marshalling and the return-value conventions."

**Sketch of the API we'd ship if pulled forward.** Preserved so we can
implement quickly if a customer request lands.

Typed events on `ReactorWindow`, parsed `EventArgs`, no raw
`WParam`/`LParam` in app code:

```csharp
public sealed class ReactorWindow
{
    public event EventHandler<DeviceChangedEventArgs>? DeviceChanged;
    public event EventHandler<PowerStateChangedEventArgs>? PowerStateChanged;
    public event EventHandler<SystemSettingChangedEventArgs>? SystemSettingChanged;
    public event EventHandler<SessionEndingEventArgs>? SessionEnding;   // settable Cancel
    public event EventHandler<SessionEndedEventArgs>? SessionEnded;
}
```

Marshalling lives inside `WindowMessageMonitor`'s subclass proc —
`Marshal.PtrToStructure<DEV_BROADCAST_HDR>` for `WM_DEVICECHANGE`,
endsession flag parsing for `WM_QUERYENDSESSION`, etc. — so app
authors never write `[StructLayout]` or unsafe interop.

Initial allow-list when revived: `WM_DEVICECHANGE`, `WM_POWERBROADCAST`,
`WM_SETTINGCHANGE`, `WM_QUERYENDSESSION`, `WM_ENDSESSION`.
(`WM_DISPLAYCHANGE` and `WM_THEMECHANGED` are already absorbed by
`UseDisplays` and the theme system respectively, so they don't need a
new public surface.)

**Bring-back trigger.** A customer request that names a specific Win32
message and a concrete use case the existing typed APIs can't cover.
Re-open this section, promote to §5.x of a follow-up spec, ship one
typed event per request.

---

## §8 Hook additions

The spec 036 hook surface (`UseWindow`, `UseWindowSize`, `UseDpi`,
`UseWindowState`, `UseIsActive`, `UseClosingGuard`, `UseOpenWindow`)
covers most needs. New hooks added here:

```csharp
// §5.5 — observable DIP position
public (double X, double Y) RenderContext.UseWindowPosition();

// §5.9 — covered/raised signal
public bool RenderContext.UseIsCovered();

// §6.7 — re-render on display layout changes
public IReadOnlyList<DisplayInfo> RenderContext.UseDisplays();
```

### 8.1 `UseWindowAspectRatio` — derived-from-content aspect lock

For media players whose aspect ratio comes from the loaded content:

```csharp
public void RenderContext.UseWindowAspectRatio(double? widthOverHeight);
```

Equivalent to `Window.SetAspectRatio` but lifetime-bound to the component
that called it — re-rendering with a different value updates the lock;
unmounting clears it. Stacks under last-writer-wins.

### 8.2 `UseWindowDragMove` — gesture-attached window drag

For "drag the window by this header element" without making the whole
client area draggable:

```csharp
public Action RenderContext.UseWindowDragMove();
// usage:
var beginDrag = UseWindowDragMove();
return Border(...).PointerPressed(_ => beginDrag());
```

`beginDrag` is stable across re-renders (same delegate identity).

### 8.3 `UseFolderPicker` / `UseFilePicker` — auto-`InitializeWithWindow`

Already-built helpers for the WinRT pickers that need `IInitializeWithWindow`
called with the host HWND. Removes the boilerplate the source proposal
calls out in §5.2.

```csharp
public sealed class FilePickerOptions { /* … */ }
public Task<StorageFile?> RenderContext.UseFilePickerAsync(FilePickerOptions options);
public Task<StorageFolder?> RenderContext.UseFolderPickerAsync(FolderPickerOptions options);
```

Implementation: `UseWindow().NativeWindow.GetWindowHandle()` →
`WinRT.Interop.InitializeWithWindow.Initialize`. The pickers themselves
are out of scope of windowing, but the boilerplate-elimination story
naturally belongs here.

---

## §9 Breaking changes

Reactor has no shipped customers yet. This spec deletes legacy fields
outright rather than maintaining aliases. Every change is captured in
the changelog with a one-line migration. There is no obsolete-attribute
deprecation period.

### 9.1 Removed `WindowSpec` fields

| Removed | Replacement | Migration |
| --- | --- | --- |
| `IsResizable` (bool) | `ResizeMode` (enum) | `IsResizable = false` → `ResizeMode = WindowResizeMode.NoResize`. `IsResizable = true` (the default) is just the new default `ResizeMode = CanResize`. |
| `IsShownInSwitchers` (bool) | `ShowInTaskbar` + `ShowInSwitcher` (two bools) | `IsShownInSwitchers = false` → `ShowInTaskbar = false, ShowInSwitcher = false`. |
| `IsAlwaysOnTop` (bool) | `Level` (`WindowLevel` enum) | `IsAlwaysOnTop = true` → `Level = WindowLevel.AlwaysOnTop`. |
| `StartPosition.RestoreFromPersistence` (enum value) | `WithPersistence(id)` helper, or `PersistPlacement` (bool) + `PersistenceFallback` | Old: `StartPosition = RestoreFromPersistence, PersistenceId = "foo"`. New: `.WithPersistence("foo")`. Or set `PersistenceId = "foo", PersistPlacement = true` manually. |

### 9.2 Changed `WindowSpec` field types

| Field | Old type | New type | Why |
| --- | --- | --- | --- |
| `ExtendsContentIntoTitleBar` | `bool` | `bool?` | `null` defers to the `TitleBar(...)` element (which already calls `Window.ExtendsContentIntoTitleBar = true` from its descriptor). Explicit `true`/`false` overrides. Most callers can delete the line entirely. |

### 9.3 Additive surface (no migration needed)

Everything else in this spec is additive — new fields on `WindowSpec`,
new methods on `ReactorWindow`, new hooks on `RenderContext`,
new top-level types (`WindowLevel`, `WindowStyle`, `WindowResizeMode`,
`WindowCornerStyle`, `WindowSizeToContent`, `TaskbarItem`,
`ReactorDisplay`, `DisplayInfo`).

### 9.4 Validation behavior

The `WindowSpec.Validate()` method gains new cross-field checks:

- `AspectRatio != null && ResizeMode == NoResize` → throws (no drag to constrain).
- `AspectRatio != null && SizeToContent != Manual` → throws (contradictory constraints).
- `SizeToContent != Manual && WindowState == Maximized initial` → warns (sizing no-ops).
- `Style == None && IsMovableByBackground == false` → warns (un-draggable window).

Warnings go through `DiagnosticLog.Warning(LogCategory.Hosting, ...)` —
they don't throw, because each combination has a legitimate use case
(pinned overlay windows that intentionally can't be moved).

---

## §10 Phased implementation plan

Roughly six phases, each independently shippable and testable. Each
phase carries its own selftest fixtures plus unit tests covering
validation / event wiring.

### Phase 1 — Tier A read-back & events _(1 week)_

- `ReactorWindow.Position` + `PositionChanged` + `UseWindowPosition`.
- `ZOrderChanged` event + `UseIsCovered`.
- `ReactorDisplay` + `UseDisplays`.
- `WindowStartPosition.CenterOnCurrent`.

Low risk — purely additive surface, no behavior change to existing
windows. Selftest: open a window, drag it, assert `PositionChanged`
fires with correct DIP values across DPI boundaries.

### Phase 2 — `ResizeMode`, `AspectRatio`, `IsMovableByBackground` _(1 week)_

- `WindowResizeMode` enum + spec field + apply path + `Validate` cross-check.
- `AspectRatio` field + `WM_SIZING` handler + `SetAspectRatio` runtime
  mutator + `UseWindowAspectRatio` hook.
- `IsMovableByBackground` + `BeginDragMove` + `Drag(bool)` modifier +
  `UseWindowDragMove` hook + interactive-control suppression list.

Risk: medium. `WM_SIZING` math has to handle corner-vs-edge drag
correctly and not fight `WM_GETMINMAXINFO`. Selftest fixtures:

- `AspectRatio_LockedDrag` — drag from each handle, assert ratio holds
  within ±1 px.
- `AspectRatio_RespectsMinMax` — set min/max + aspect ratio, drag past
  bounds, assert min wins.
- `DragMove_FromBackground` — pointer-press on root, assert window moves.
- `DragMove_SuppressedOnButton` — pointer-press on a button inside the
  root, assert window does not move and button's `Click` still fires.

### Phase 3 — `ShowInTaskbar` / `ShowInSwitcher` split + persistence revamp _(1 week)_

- `ShowInTaskbar` / `ShowInSwitcher` split (replaces `IsShownInSwitchers`).
- `PersistPlacement` bool + `PersistenceFallback` enum + `WithPersistence()` helper.
- Remove `WindowStartPosition.RestoreFromPersistence`.
- `ReactorWindow.SavePlacement()`.

Risk: low. Migration is mechanical — sweep samples and selftests for
`RestoreFromPersistence` and replace with `WithPersistence(...)` calls.

### Phase 4 — `WindowStyle` + `CornerStyle` + `WindowLevel` _(2 weeks)_

- `WindowStyle` enum + apply path + `WS_EX_TOOLWINDOW` toggling.
- `WindowCornerStyle` enum + `DwmSetWindowAttribute` apply.
- `WindowLevel` enum + topmost / floating apply + sibling-floating monitor.
- Sample: PowerToys Run lookalike (`samples/CommandPaletteWindow`).
- Sample: Tool palette (`samples/ToolPalette`).

Risk: medium. `WindowStyle.None` interacts with `IsMovableByBackground`,
title-bar overlap, system-menu accelerator — full selftest matrix needed.

### Phase 5 — `SizeToContent` _(1 week)_

- `WindowSizeToContent` enum + measure-and-resize apply path.
- `Validate` cross-checks with `AspectRatio`, `Min/Max`, `Maximized`.
- Initial-flash mitigation: document that callers should set sensible
  `Width`/`Height` even when `SizeToContent` is on.

Risk: medium. The one-frame flash is hard to avoid; selftest captures
the desired bounds after the first `SizeChanged` and asserts the window
settles within 2 frames.

### Phase 6 — `TaskbarItem` facade + `Description` _(small, ~3 days)_

- `TaskbarItem` facade grouping existing `Progress`/`Overlay`/`ThumbnailToolbar`.
- `Description` property via `ITaskbarList3::SetThumbnailTooltip`.

No risk — purely additive.

### Phase 7 — Picker boilerplate elimination _(small)_

- `UseFilePickerAsync` / `UseFolderPickerAsync`.

Lives more in the data-system area than windowing; could be split off
into its own spec if scoping requires.

### Skipped / deferred

- **Jump lists.** Need a separate `JumpList` spec covering
  `ICustomDestinationList` + AppUserModel ID registration + system menu
  integration. Out of scope here.
- **Modal `ShowDialog`.** Spec 036 §9 already deferred. Stays deferred.
- **True transparency / vibrancy.** Tier C — out of scope.

---

## §11 Open questions

1. **Owner-floating across multiple apps.** `WindowLevel.Floating` ([§6.4](#64-windowlevel--three-useful-z-order-tiers-not-nswindows-stack))
   keeps a window above sibling owned windows of the same app. Should
   it also stay above the _owner_ when both are non-topmost? WPF's
   `ToolWindow` does. Recommendation: yes, because authors using
   `Floating` expect tool-palette semantics. Pin in Phase 4 selftest.
   **Resolution:** yes — `Floating` stays above the owner as well as sibling Reactor app windows; Phase 4 selftests pin this.

2. **`SizeToContent` + min/max constraints visual contract.** When
   content desires `400×300` but `MinWidth=500`, do we set the window
   to `500×300` (respect min, break aspect) or `500×375` (preserve aspect
   if `AspectRatio` would have allowed it)? Recommendation: respect min,
   document the choice — `AspectRatio` is exclusive with `SizeToContent`
   anyway (§9.1), so the only conflict is min/max vs. content desired,
   and min/max wins.
   **Resolution:** min/max wins; `SizeToContent` clamps to the declared size bounds rather than preserving aspect.

3. **`UseWindowDragMove` reentrancy.** The pattern returns an `Action` —
   calling it during an active drag is a no-op? Or queues a second drag?
   Recommendation: no-op when `GetCapture()` reports a drag in progress;
   document.
   **Resolution:** no-op while `GetCapture()` indicates an active drag; no queued second drag is started.

4. **`PositionChanged` firing rate during user drag.** `AppWindow.Changed`
   fires on every interactive move. Do we coalesce, or fire eagerly?
   Recommendation: fire eagerly. Apps that need throttling have
   `UseDebounced` from the async-resources spec. Throttling at the
   framework level would block snap-to-edge UIs.
   **Resolution:** fire eagerly; hooks short-circuit unchanged DIP values and consumers debounce if needed.

5. **`Transparent` BackdropKind on Windows 10.** `TransparentBackdrop`
   requires WinAppSDK 1.3+ which targets Windows 10 1809+, but the
   experience falls back to "no backdrop" on builds that don't support
   it. Should we throw at `Validate()` time, log a warning at apply, or
   silently no-op? Recommendation: log warning at apply (matches existing
   Mica behavior), don't throw.
   **Resolution:** log a warning at apply time and continue; validation does not reject Windows 10 fallback cases.

6. **Should `CenterOnCurrent` use cursor position or foreground-window
   position?** Cursor is what users expect ("open here"); foreground is
   what WPF's `CenterScreen` actually does. Recommendation: cursor first,
   fall back to foreground monitor if cursor is on a disconnected
   monitor (rare).
   **Resolution:** use the cursor monitor first; fall back to the foreground monitor when no usable cursor monitor is available.

---

## §12 Out of scope

Repeating for clarity — these are explicitly **not** in this spec:

- True per-pixel alpha transparency (§7.1).
- Arbitrary continuous `CornerRadius` (§7.2).
- NSWindow-style multi-tier `WindowLevel` stack (§7.3).
- HUD vibrancy material (§7.4).
- Modal `ShowDialog` / `DialogResult` semantics (spec 036 §9, repeated here).
- Jump lists (§10 — separate spec).
- Splash screens (UWP-era; needs its own spec).
- Non-Windows windowing (Reactor is WinUI 3 desktop).
- Cross-process window interop / Islands hosting (out of scope of Reactor entirely).

---

## Appendix A — feature decision summary

A scannable index of every source-proposal feature and Reactor's
response.

| Source proposal feature | Reactor decision | Section |
| --- | --- | --- |
| `MinWidth` / `MinHeight` / `MaxWidth` / `MaxHeight` | ✅ shipped (spec 036) | — |
| `Width` / `Height` | ✅ shipped (spec 036) | — |
| `Left` / `Top` read/write | ▲ read-back added | §5.5 |
| `WindowState` | ✅ shipped (spec 036) | — |
| `Topmost` | ✅ via `WindowLevel.AlwaysOnTop` | §6.4 |
| `ResizeMode` | ▲ new enum | §5.1 |
| `WindowStartupLocation.CenterScreen` | ▲ `CenterOnCurrent` added | §5.6 |
| `ShowInTaskbar` | ▲ split from switcher | §5.4 |
| `Icon` | ✅ shipped (spec 036) | — |
| `ShowActivated` | ✅ shipped (spec 036) | — |
| `IsActive` | ✅ shipped (spec 036) | — |
| `LocationChanged` event | ▲ `PositionChanged` | §5.5 |
| `Activated` / `Deactivated` | ✅ shipped (spec 036) | — |
| `Activate()` | ✅ shipped (spec 036) | — |
| `IsMinimizable` / `IsMaximizable` | ✅ shipped (spec 036) | — |
| `Dpi` + `DpiChanged` | ✅ shipped (spec 036) | — |
| `PresenterKind` | ✅ shipped (spec 036) | — |
| Default-on persistence | ▲ conditional on `PersistenceId` | §5.7 |
| `IsPlacementPersisted` opt-out | ▲ `PersistPlacement` bool | §5.7 |
| `PersistenceId` | ✅ shipped (spec 036) | — |
| `PersistenceFallback` | ▲ new enum field | §5.7 |
| Multi-monitor clamp / DPI | ✅ shipped (spec 036) | — |
| `SavePlacement()` | ▲ new method | §5.8 |
| `Window.TitleBar` declarative | ✅ via existing `TitleBar(...)` element | §6.6 |
| Auto-`ExtendsContentIntoTitleBar` | ▲ implicit from `TitleBar(...)` | §6.6 |
| `WindowStyle.Default` / `None` / `ToolWindow` | ▲ new enum | §6.1 |
| `WindowStyle.Hud` | ❌ rejected | §7.4 |
| `IsTransparent` | ❌ rejected → use `Backdrop = Transparent` | §7.1 |
| `WindowLevel` (Normal / Floating / AlwaysOnTop) | ▲ new enum | §6.4 |
| `WindowLevel.Overlay` | ❌ rejected (no Win32 tier) | §7.3 |
| `SizeToContent` | ▲ new enum | §6.3 |
| `AspectRatio` | ▲ new field + `WM_SIZING` | §5.2 |
| `IsMovableByBackground` | ▲ new field + `GetCursorPos`-polled `AppWindow.Move` | §5.3 |
| `CornerRadius` (arbitrary) | ❌ rejected | §7.2 |
| `CornerStyle` (discrete) | ▲ new enum (DWM) | §6.2 |
| `IsHitTestVisible` / click-through | ✅ shipped via `IgnorePointerInput` | — |
| `Opacity` | ✅ shipped (spec 036) | — |
| `TaskbarItemInfo` grouping | ▲ `TaskbarItem` facade | §6.5 |
| `TaskbarItemInfo.Description` | ▲ new property | §6.5 |
| `TaskbarItemInfo.ThumbButtonInfos` | ✅ shipped as `ThumbnailToolbar` (spec 036) | — |
| `TaskbarItemInfo` jump list | ❌ deferred to separate spec | §10 |
| `ZOrderChanged` event | ▲ new event | §5.9 |
| Tray icon | ✅ shipped (spec 036) | — |
| `WindowMessageReceived` | ◯ deferred (design preserved) | §7.6 |
| Monitor enumeration | ▲ `ReactorDisplay` | §6.7 |
| Splash screen | ❌ separate spec | — |
| Picker `InitializeWithWindow` helpers | ▲ `UseFilePickerAsync` etc. | §8.3 |
| `Window.Cursor` per-window cursor | ❌ deferred | — |
| `Window.AllowsTransparency` (WPF) | ❌ rejected (see `IsTransparent`) | §7.1 |
| `Window.WindowChrome` (WPF) | ✅ already covered by `TitleBar(...)` + `CornerStyle` + `WindowStyle` | §6.1 / §6.2 / §6.6 |
| `ShowDialog` / `DialogResult` (WPF) | ❌ separate spec | — |
