# Visual Studio Embedded Preview — Interactive Reactor Component Hosting in VS 2022

## Status

**Draft — design proposal.** No code yet. Builds on the existing
`Reactor.Devtools` subsystem (`PreviewCaptureServer`, `DevtoolsHost`,
`DevtoolsSupervisor`) and the hot-reload pipeline
(`Microsoft.UI.Reactor.Hosting.HotReloadService`). Sibling to the VS Code
preview extension under `src/vscode-reactor/`, which uses a screenshot stream
rather than a real interactive window.

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 Goals & non-goals](#2-goals--non-goals)
- [§3 Architecture overview](#3-architecture-overview)
- [§4 The HWND-hosting strategy](#4-the-hwnd-hosting-strategy)
- [§5 Reactor-side changes](#5-reactor-side-changes)
- [§6 VS extension design](#6-vs-extension-design)
- [§7 Component dropdown & auto-selection](#7-component-dropdown--auto-selection)
- [§8 Force-reload button & hot-reload integration](#8-force-reload-button--hot-reload-integration)
- [§9 Lifecycle, errors, and crash recovery](#9-lifecycle-errors-and-crash-recovery)
- [§10 Security model](#10-security-model)
- [§11 VS licensing & local install](#11-vs-licensing--local-install)
- [§12 Debugging the extension](#12-debugging-the-extension)
- [§13 Automated testing](#13-automated-testing)
- [§14 Build, package, distribute](#14-build-package-distribute)
- [§15 Phasing](#15-phasing)
- [§16 Open questions](#16-open-questions)

---

## §1 Motivation

The VS Code extension under `src/vscode-reactor/` ships today a **streaming
thumbnail** of the live preview: it spawns `dotnet watch run -- --devtools run
--vscode`, waits for the `PreviewCaptureServer` to publish `CAPTURE_PORT=` /
`CAPTURE_TOKEN=`, and polls `/frame` at 10 FPS. The user can pick a component
from a dropdown that round-trips through `POST /preview`, but cannot **click**
the preview — every interaction requires `POST /focus` followed by alt-tabbing
to the standalone WinUI window.

That UX is acceptable for VS Code (no native HWND hosting available, the
extension host is Node), but inside Visual Studio we can do much better. VS is
a WPF host running in `devenv.exe`; it can subclass `HwndHost` and reparent a
foreign HWND. Combined with the existing `--devtools` MCP/preview plumbing,
this lets us deliver a tool window with:

- the **real** WinUI 3 component, running with full input, animations, and
  layout fidelity;
- **hot reload** as a core feature — edit the source, save, the embedded
  control updates in place without losing dock state inside VS;
- a component picker that **auto-tracks the active editor** and a manual
  override dropdown;
- a **force-reload** button when something gets wedged;
- the same `--devtools` MCP server up for AI tooling / `mur devtools`
  workflows.

The only fundamentally hard piece is cross-process HWND hosting of a
WinUI 3 `Window`; the rest is wiring. This spec works through both.

---

## §2 Goals & non-goals

### Goals

1. **Interactive embedded preview** of one named Reactor `Component` inside a
   VS 2022 tool window. Pointer/keyboard/IME route to the live WinUI control.
2. **Component dropdown** in the tool-window chrome, populated from the
   target project's discovered `Component` subclasses. Auto-selects when the
   active editor changes; manual selection sticks until the user picks a new
   one.
3. **Force-reload button** in the chrome. Clicking it tears the WinUI child
   process down and respawns it, recompiling first.
4. **Hot reload as a first-class workflow.** The child process runs under
   `dotnet watch run -- --devtools run --embed`. Saved edits flow through the
   existing `MetadataUpdateHandler` pipeline and the embedded control
   re-renders in place. Type-shape edits that fail in-place migration fall
   through to a clean respawn driven by `dotnet watch`'s own rebuild loop
   (the existing `mur devtools` exit-code-42 supervisor is **not** in this
   path — `dotnet watch` doesn't honor it; see §8.1).
5. **MCP devtools port stays up.** Because the child runs with `--devtools`,
   the user can drive the same preview process from `mur devtools`, Copilot,
   or any other MCP client — no second instance.
6. **Robust crash recovery.** WinUI child crash, build failure, dock state
   change, VS restart, project rebuild — none of these should leave a
   permanently-broken tool window.
7. **No special license tier.** Buildable, debuggable, runnable with VS 2022
   Community on a developer workstation under Microsoft's standard
   individual-use terms (see §11). Internal corporate distribution requires
   the existing Reactor org's standard VS license (Professional or
   Enterprise) — same as the rest of the repo's tooling.

### Non-goals

- **Replacing the VS Code extension.** That stays as the cross-IDE
  thumbnail-stream story for users who don't run VS.
- **Multi-component tiling.** One component per tool window. (You can open
  the tool window twice if you really want two; out of scope to make that
  ergonomic.)
- **Hosting Reactor inside a XAML island via `ContentIsland`.** That's a
  same-process API; we are cross-process by design (see §3).
- **Replacing or bypassing the `PreviewCaptureServer` HTTP API.** Embedded
  mode and screenshot-stream mode coexist; embed mode just skips the
  frame-capture timer.
- **Designer-style live editing (drag handles, property pane).** Out of
  scope for this spec; the MCP devtools surface remains the inspection
  story.
- **Solution Explorer integration, Code Lens, debugger glyph adornments.**
  Possible follow-ups; not in scope.

---

## §3 Architecture overview

Three processes, two IPC channels:

```
┌──────────────────────────────┐                  ┌──────────────────────────┐
│  devenv.exe                  │                  │  dotnet.exe (watch)      │
│  (Visual Studio 2022)        │                  │   supervises:            │
│                              │                  │  ┌─────────────────────┐ │
│  ┌───────────────────────┐   │   HTTP loopback  │  │ dotnet.exe (target) │ │
│  │ Reactor.VS ext (VSIX) │◄──┼──────────────────┼─►│  Reactor.Devtools   │ │
│  │  - ToolWindowPane     │   │   /components    │  │  PreviewCaptureSrv  │ │
│  │  - WPF UserControl    │   │   /preview       │  │  MCP server         │ │
│  │  - HwndHost subclass  │   │   /hwnd  (new)   │  │  MetadataUpdater    │ │
│  │  - Process launcher   │   │   /embed/* (new) │  │                     │ │
│  │  - Editor tracker     │   │                  │  │  ReactorWindow      │ │
│  └──────────┬────────────┘   │                  │  │   ┌──────────────┐  │ │
│             │                │   Win32 SetParent│  │   │ WinUI HWND   │  │ │
│             │   reparents    │ ◄────────────────┼──┼───┤ (embedded)   │  │ │
│             ▼                │                  │  │   └──────────────┘  │ │
│   ┌───────────────────────┐  │                  │  └─────────────────────┘ │
│   │  HwndHost placeholder │  │                  │            ▲             │
│   │  inside tool window   │  │                  │            │ exit 42     │
│   └───────────────────────┘  │                  │            │ → respawn   │
└──────────────────────────────┘                  └──────────────────────────┘
```

- **Process 1 — `devenv.exe`.** Hosts the VSIX. Owns the WPF tool window,
  the `HwndHost` subclass, the dropdown UI, the file-tracker, and the
  process launcher.
- **Process 2 — `dotnet watch`.** Standard hot-reload supervisor. Restarts
  the target process on edits that the runtime can't apply in-place.
- **Process 3 — the target Reactor app.** Same EXE the user would run
  normally, launched with the new `--devtools run --embed` subverb. Owns
  the WinUI `Window` whose HWND we reparent.

There is **no XAML / WinUI bridge across the process boundary.** The VS
extension runs on the WPF text-rendering composition tree owned by
`devenv.exe`. The Reactor app runs on the WinUI 3 visual tree owned by its
own process. They share **only** an HWND parent/child relationship and a
loopback HTTP control channel. This is the same general topology the VS
team uses for the out-of-process WinForms designer in VS 2022.

### Why not `VisualStudio.Extensibility` (the new out-of-proc SDK)?

The 2024+ `VisualStudio.Extensibility` SDK runs extensions out-of-process
and exposes tool windows whose content is rendered via WebView2 or remote-UI
markup. There is **no HwndHost equivalent** in the out-of-proc SDK as of
this spec. We need to host a foreign HWND, so we must use the classic
in-process VSIX model (`Microsoft.VisualStudio.SDK` 17.x +
`Microsoft.VisualStudio.Shell.Framework` + WPF `ToolWindowPane`). This
constrains us to .NET Framework 4.7.2 for the VSIX itself (the VS extension
host's runtime), even though the target Reactor app runs on .NET 10. That's
fine; the IPC is just HTTP + Win32, no shared types.

### Why not `ContentIsland` / lifted XAML hosting?

`Microsoft.UI.Content.ContentIsland` plus `DesktopChildSiteBridge` is
Microsoft's supported, modern way to embed WinUI 3 visual trees in a
foreign HWND. But the bridge requires the parent HWND and the island to
live in **the same process** — the composition surface cannot be projected
across a process boundary. We need cross-process for the hot-reload story
(`dotnet watch` must be free to terminate and respawn the child without
killing VS), so `ContentIsland` is not an option.

---

## §4 The HWND-hosting strategy

This is the technically hardest part of the spec and the **single largest
risk** in shipping Path B. Treat it as load-bearing.

### §4.0 Confidence statement

Cross-process `SetParent` of a WinUI 3 `Window` HWND is **not a documented
hosting model** in the Windows App SDK. There is no stable, supported API
today for projecting a WinUI 3 composition surface into a foreign-process
parent HWND. What works in practice is a combination of:

1. The `WS_CHILD` style flip + `SetParent` Win32 pattern that VS itself
   uses in the out-of-proc WinForms designer (and that community samples
   like `microsoft/microsoft-ui-xaml#9912` document for WinUI 3),
2. Plus per-version workarounds for input routing that have shipped
   organically as Windows App SDK has evolved.

A 2026-06 spike (Appendix C) validated the headline mechanic with
Mitigation A alone (no A′ message-routing tweaks beyond `SetFocus`):
**reparented Reactor WinUI 3 surface receives mouse, keyboard, ComboBox
popups, slider drag, and force-reload across process boundaries on
Windows 11 ARM64 under x64 emulation.** That moves Path B from
"speculative" to "feasible". The remaining unknowns are the rest of the
Phase 0 matrix (IME, UIA, multi-monitor DPI, ARM64-native, real VS tool
window vs WinForms placeholder, stress) — none of which the spike
disproved, all of which are routine to validate in Phase 1 / a final
Phase 0 pass against a real VSIX.

We still describe Mitigation C (owner-window mode) as a defined
fallback, because Phase 0 has only been run against a WinForms
placeholder; differences in VS's WPF/HwndHost dock state machine could
in principle surface issues the spike couldn't see. The fallback path
is cheap to keep and the spec is easier to ship with it documented.

Also investigate during Phase 0/1 whether the **WinAppSDK 2.0+
experimental remote ContentIsland APIs**
(`DesktopChildSiteBridge.AcceptRemoteEndpoint` /
`ContentIsland.ConnectRemoteEndpoint`) are usable from a .NET Framework
4.7.2 in-proc VSIX talking to a .NET 10 WinUI child. If they are, they
are the strictly preferred mechanism and this entire §4.1/§4.2
cascade becomes a footnote. Current expectation: not viable in
Phase 1; revisit annually as WinAppSDK matures.

### §4.1 The plan

In the Reactor process:

1. Construct the `ReactorWindow` as today (top-level WinUI 3 `Window`,
   `WS_OVERLAPPEDWINDOW` style, `AppWindow` configured by `WindowSpec`).
2. **Before** showing it, when `--embed` is set:
   - Hide the AppWindow titlebar via `AppWindow.TitleBar.ExtendsContentIntoTitleBar = false` and the presenter; clear `WS_OVERLAPPED|WS_CAPTION|WS_THICKFRAME|WS_MINIMIZEBOX|WS_MAXIMIZEBOX|WS_SYSMENU`.
   - Add `WS_CHILD`. Add `WS_VISIBLE` only after the embed handshake completes.
   - Skip `AppWindow.Show()`. The host expects the window to be hidden until
     the VS side reparents.
   - Publish the HWND on the existing `PreviewCaptureServer` via a new
     `GET /hwnd` endpoint.
3. Wait (block the Devtools bootstrap) for a `POST /embed/ack` from the
   VS side. The body `{ "parent": "0x1234ABCD" }` carries the VS-side
   placeholder HWND.
4. On `/embed/ack`: call `SetParent(reactorHwnd, parentHwnd)`. Then
   `SetWindowPos(reactorHwnd, HWND_TOP, 0, 0, w, h, SWP_NOZORDER|SWP_FRAMECHANGED|SWP_SHOWWINDOW)` to match the host bounds (initial size sent in the ack body).
5. From then on, the host pushes resize events as `POST /embed/resize`
   `{ w, h }`. Reactor side calls `SetWindowPos` on the dispatcher queue.

In the VS process:

1. Create a `ToolWindowPane` whose `Content` is a `ReactorEmbedControl`
   (WPF `UserControl`).
2. Inside the user control: a chrome strip (dropdown + reload button + status)
   plus a `HwndHostPlaceholder` (a custom `HwndHost` subclass).
3. `HwndHostPlaceholder.BuildWindowCore` creates a **plain Win32 placeholder
   child window** (registered class `ReactorEmbedPlaceholder`, style
   `WS_CHILD|WS_CLIPCHILDREN|WS_VISIBLE`). This window is what `HwndHost`
   returns to WPF — it's owned by the VS process. The placeholder gives us a
   stable HWND that does not vanish if the Reactor child crashes.
4. When the Reactor child publishes `/hwnd`, the extension calls
   `SetParent` from the **child side** (via `POST /embed/ack`). Win32
   permits `SetParent` across process boundaries — the new parent HWND only
   needs to be valid; the child process changes its own window's parent.
5. On `SizeChanged` of the placeholder, push `POST /embed/resize`.
6. On tool window dispose, send `POST /embed/release`. The Reactor child
   calls `SetParent(myHwnd, IntPtr.Zero)` to detach (becomes top-level
   again), then exits.

### §4.2 Known risks and mitigations

#### Risk 1 — Input routing on Windows 11

Was the single largest unknown. WinUI 3 uses an internal
`Microsoft.UI.Content.DesktopChildSiteBridge` that captures pointer input
at the original top-level HWND. Community reports
([microsoft/microsoft-ui-xaml#9912](https://github.com/microsoft/microsoft-ui-xaml/discussions/9912))
had suggested that after reparenting a WinUI 3 `Window` HWND with
`WS_CHILD`, input might stop reaching the XAML tree on Windows 11.

**Status after Phase 0 spike (2026-06): not blocking.** Mitigation A
alone — the basic Win32 hygiene listed below — proved sufficient for
mouse, keyboard, ComboBox popup, and slider drag inside a foreign-
process WinForms placeholder. Full input matrix (IME, tab traversal,
accelerators) still TBD; A′ remains the planned hardening pass.

**Mitigation A (verified, Phase 0):**

- `ShowWindow(SW_HIDE)` before the style flip to avoid a top-level
  flash.
- Clear `WS_OVERLAPPED | WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX |
  WS_MAXIMIZEBOX | WS_SYSMENU | WS_POPUP`; add `WS_CHILD`.
- Clear `WS_EX_APPWINDOW | WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE |
  WS_EX_CLIENTEDGE`.
- `SetParent(reactorHwnd, parentHwnd)`.
- `SetWindowPos(... SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOZORDER)`.
- `SetFocus(reactorHwnd)` immediately afterwards — confirmed necessary
  for the WinUI input bridge to prime.

**Mitigation A′ (low-level corrections to layer onto A in Phase 1):**
several smaller Win32 hygiene steps that should harden input edge cases
the spike didn't cover and that match the documented best practices for
HwndHost-style hosting:

- Implement `HwndHost.TranslateAcceleratorCore`,
  `TranslateCharCore`, and `TabIntoCore` so VS's keyboard dispatch
  forwards properly. Without these, keyboard accelerators (Ctrl+S,
  Tab traversal across the boundary) likely fail.
- Forward `WM_CHANGEUISTATE` / `WM_UPDATEUISTATE` from the placeholder
  to the child so focus-rectangle visibility tracks.
- On `WM_MOUSEACTIVATE`, call `SetFocus(reactorHwnd)`.
- On `WM_MOUSEWHEEL` to the placeholder, forward to the focused
  descendant via `SendMessage`.
- Recompute coordinate translation using `MapWindowPoints` rather than
  trusting the placeholder's rect.

A′ is the **planned hardened path** for Phase 1. It is plain Win32
hygiene, well-documented for HwndHost scenarios, and not WinUI-
specific. Phase 0 validated the baseline; Phase 1 layers A′ on top to
cover the items the spike didn't exercise.

**Mitigation B (experimental, last-resort within child-mode):** subclass
the Reactor window's WndProc to forward `WM_MOUSEMOVE`,
`WM_LBUTTONDOWN`, etc. directly into the XAML island HWND found via
`FindWindowEx(reactorHwnd, NULL, L"Microsoft.UI.Content.DesktopChildSiteBridge", NULL)`.
This is messy, version-fragile (the class name is internal/unstable),
and almost certainly insufficient — it ignores keyboard focus,
`WM_GETDLGCODE`, pointer/touch/pen, capture, coordinate transforms,
IME, accessibility, drag/drop, and accelerator routing. With Phase 0
showing A is sufficient, we now **never plan to ship B** — if A + A′
hit an edge case we can't fix, we jump directly to C.

**Mitigation C (documented escape hatch):** "owned-window" mode. The
Reactor window stays top-level (`WS_OVERLAPPEDWINDOW`), but
`SetWindowLongPtr(reactorHwnd, GWLP_HWNDPARENT, vsToolHwnd)` makes it an
**owned** window of the VS tool. The Reactor window then tracks the
placeholder's screen bounds by listening to a `POST /embed/move` stream
(VS pushes on `LocationChanged`). This is not "inside" VS visually, but it
floats above VS, follows it, minimizes with it, and keeps every WinUI
feature working. We ship this as an off-by-default `--embed-mode owner`
flag that the extension will fall back to if A + A′ fail at runtime
under VS specifically, and that the user can also force on via a
setting.

#### Risk 2 — DPI

WinUI 3 apps and Visual Studio 2022 both run as `PerMonitorV2`. If a
developer launches the embedded Reactor child from a project whose
`<HighDpiMode>` isn't `PerMonitorV2`, the reparented child will render at
the wrong scale. The MSDN `SetParent` documentation explicitly warns
that DPI-awareness mismatches between parent and child force the
**less-aware** of the two to be reset to system DPI awareness, which is
catastrophic for a WinUI 3 visual tree.

**Mitigation:**

1. The `--embed` startup path verifies the **child process** DPI awareness
   context via `GetProcessDpiAwarenessContext()` and refuses to embed if
   it isn't `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2`.
2. Immediately before `SetParent`, the child also compares
   `GetWindowDpiAwarenessContext(parentHwnd)` (passed in the
   `/embed/ack` body — VS publishes its own value) against
   `GetWindowDpiAwarenessContext(reactorHwnd)` using
   `AreDpiAwarenessContextsEqual`. On mismatch, refuse the ack with
   HTTP 412 and an error body; the extension surfaces "DPI awareness
   mismatch — switching to floating preview" and re-launches with
   `--embed-mode owner`.
3. Error surfaces in the VS tool window as "Project must be
   PerMonitorV2 to embed in Visual Studio. Add
   `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` to
   your csproj." for case 1; case 2 is invisible (auto-fallback).

#### Risk 3 — Z-order / airspace

WPF's airspace limitation means WPF popups (tooltips, context menus,
`Popup` controls) cannot render over an `HwndHost`. The VS chrome
(dropdown, reload button) lives in a sibling row, not overlapping the
embedded HWND, so this is fine. The Reactor window's own popups (flyouts,
tooltips, ContentDialog) render on its own composition tree and are
unaffected.

#### Risk 4 — Floating / docking

When the user floats the VS tool window or moves it to another monitor,
the placeholder HWND's parent chain changes. The `HwndHost.OnWindowPositionChanged`
callback fires; we push the new screen rect to the Reactor child for
position-tracking (relevant only for the Mitigation-C owner path), and
re-issue `SetWindowPos(SWP_FRAMECHANGED)` to nudge the input bridge.

#### Risk 5 — Process lifetime

Two failure modes to plan for:

**VS crashes / is force-killed.** The orphaned `dotnet watch` supervisor
+ Reactor child are stranded. Two layers of cleanup:

1. **Primary — Windows Job Object.** The extension creates a job object
   with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` and assigns the
   `dotnet watch` process to it (see §6.5). When `devenv.exe` dies (any
   reason — crash, kill, power loss, OS terminate), Windows closes the
   handle and terminates everything in the job. This is the load-bearing
   cleanup mechanism.
2. **Secondary — `--embed-host-pid` watchdog.** The Reactor child opens a
   process handle for the VS PID at startup and `WaitForSingleObject`s on
   it on a background thread. On signal it calls
   `SetParent(myHwnd, IntPtr.Zero)` and exits. This is belt-and-braces;
   it also covers the (unusual) case where the job object handle is
   leaked or the extension code itself faults before assigning the
   process to the job.

**Reactor child crashes** (build error, unhandled exception). The
`dotnet watch` supervisor stays up and waits for the next file change to
relaunch. The extension's stdout watcher sees the relaunch's
`CAPTURE_TOKEN=` line (or doesn't, if the build is broken) and surfaces
an error overlay accordingly.

**`dotnet watch` itself crashes.** Rare but possible. The extension's
`SupervisorExited` event fires. We treat this like the user clicked Stop
and surface "Preview supervisor exited; click ↻ to restart" in the
overlay.

---

## §5 Reactor-side changes

All changes are additive. The vscode-reactor extension and `mur devtools`
keep working exactly as today.

### §5.1 New CLI flags

`DevtoolsCliOptions` (`src/Reactor/Hosting/Devtools/DevtoolsCliParser.cs`)
grows three optional fields:

```csharp
public sealed record DevtoolsCliOptions(
    DevtoolsSubverb? Subverb,
    string? ComponentName,
    bool VscodeMode,
    bool EmbedMode,                    // NEW — --embed
    EmbedMode EmbedStyle,              // NEW — --embed-mode {child|owner}
    int? EmbedHostPid,                 // NEW — --embed-host-pid <pid>
    int Fps,
    /* … existing fields … */);

public enum EmbedMode { Child, Owner }
```

Recognized argv shape (parsed in `DevtoolsCliParser.Parse`):

```
myapp.exe --devtools run --embed --embed-host-pid 12345 [--embed-mode owner]
```

`--embed` implies `--vscode` (the `PreviewCaptureServer` must be up) and
disables the screenshot timer (we don't need frames). `--embed` without
`--embed-host-pid` is an error: we refuse to embed without a host process
to die-with.

### §5.2 Window construction in embed mode

`ReactorWindow` and `WindowSpec` learn an `EmbedRequest` knob. The
`DevtoolsHost.BootstrapWithDevtools` path constructs the window with that
knob set when `options.EmbedMode` is true. Concretely (pseudocode for the
new path in `DevtoolsHost`):

```csharp
var spec = new WindowSpec
{
    Title = $"Reactor — {componentName}",
    Width = 800, Height = 600,
    Embed = new EmbedRequest(
        EmbedStyle: options.EmbedStyle,
        HostPid: options.EmbedHostPid!.Value,
        InitialVisibility: false /* wait for /embed/ack */)
};
```

`ReactorWindow` honors `Embed.InitialVisibility = false` by not calling
`AppWindow.Show()`. After construction it flips window styles to
`WS_CHILD` (child mode) or keeps `WS_OVERLAPPEDWINDOW` (owner mode), and
posts `READY` to the captured server. The detailed style flip:

```csharp
// child mode
long style = GetWindowLong(hwnd, GWL_STYLE);
style &= ~(WS_OVERLAPPED | WS_CAPTION | WS_THICKFRAME |
           WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU | WS_POPUP);
style |= WS_CHILD;
SetWindowLong(hwnd, GWL_STYLE, style);

long ex = GetWindowLong(hwnd, GWL_EXSTYLE);
ex &= ~(WS_EX_APPWINDOW | WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE);
ex |= WS_EX_NOREDIRECTIONBITMAP; // optional — speeds up composition handoff
SetWindowLong(hwnd, GWL_EXSTYLE, ex);
```

Most of `ReactorWindow`'s `AppWindow` configuration paths (presenter,
backdrop, titlebar extension) are no-ops or unsupported in child mode.
The implementation must gate those calls on
`!Spec.Embed.HasValue || Spec.Embed.Value.Style == Owner`.

### §5.3 New `PreviewCaptureServer` endpoints

| Method | Path             | Auth   | Body                       | Response                            | Description |
|--------|------------------|--------|----------------------------|-------------------------------------|-------------|
| GET    | `/hwnd`          | Bearer | —                          | `{ "hwnd": "0x1234ABCD", "generation": int }` | Returns the HWND of the embedded window and the per-process embed generation (incremented each time the child publishes a new HWND after a respawn). |
| POST   | `/embed/ack`     | Bearer | `{ "parent": "0xPPPP", "w": 800, "h": 600, "generation": int }` | `{ "ok": true }`                    | Tells the Reactor child to `SetParent(reactorHwnd, parent)` and resize. `generation` must match the value just returned by `/hwnd`; mismatched generations are rejected with 409 to guard against respawn races. Idempotent within a generation. |
| POST   | `/embed/resize`  | Bearer | `{ "w": int, "h": int }`   | `{ "ok": true }`                    | Resize the embedded window. Coalesced — the dispatcher only honors the most recent call per layout pass. |
| POST   | `/embed/move`    | Bearer | `{ "x": int, "y": int }`   | `{ "ok": true }`                    | **Owner-mode only.** Position the owned top-level window at screen coords. |
| POST   | `/embed/release` | Bearer | —                          | `{ "ok": true }`                    | Reverses `SetParent`, runs the normal `Window.Close()` shutdown (so telemetry/persistence handlers fire), then exits the process within 1s. Called by the extension on tool-window dispose. |

`/status` gains two fields used by the extension for race-free state
tracking: `protocol` (string, currently `"embed-v1"`; see Q4) and
`generation` (int, mirrors `/hwnd`).

**No `/reload` endpoint.** Force-reload is purely extension-driven (kill
the `dotnet watch` tree + relaunch — see §8). A separate `/reload` HTTP
verb was considered and rejected because the only consumer is the
extension itself, which holds the process handle directly; routing
through HTTP just adds another way to fail.

All new endpoints inherit the existing `PreviewCaptureServer` security
posture (bearer auth, allowlisted Host header, Origin allowlist with
`vscode-webview` removed for the VS extension — see §10).

### §5.4 Module wiring

`DevtoolsHost.RunWithDevtools` already creates the `PreviewCaptureServer`
when `vscodeMode` is true and registers `SwitchComponent`. The embed path
attaches the additional callbacks on the same server instance:

```csharp
server.GetHwnd       = () => (host.WindowHandle, _embedGeneration);
server.AckEmbed      = (parent, w, h, gen) => ApplyEmbedAck(parent, w, h, gen);
server.ResizeEmbed   = (w, h)         => ApplyEmbedResize(w, h);
server.MoveEmbed     = (x, y)         => ApplyEmbedMove(x, y);   // owner only
server.ReleaseEmbed  = ()             => ApplyEmbedRelease();
```

`_embedGeneration` is a monotonically increasing integer initialized to
1 on process start. It is incremented if (and only if) the child performs
an in-process re-handshake — currently no such path exists, so generation
is effectively always 1 within a single child process. The extension uses
the generation field primarily to invalidate in-flight commands across
**respawn boundaries**, which always change PID and port and therefore
trivially change generation from the extension's perspective.

Server-side endpoint handlers are added next to the existing `ServeFrame`
/ `HandleSwitchComponent`. Each new write endpoint:

- requires `POST`
- requires `Content-Type: application/json` (existing CSRF defense)
- rejects bodies > 4 KB (lower than the existing 4 MB cap; payloads are tiny)
- on success, returns 200 with `{ "ok": true }`
- on failure, 4xx with `{ "ok": false, "error": "…" }`

### §5.5 Hot reload behavior

No changes to `HotReloadService`. The supervisor side of `dotnet watch`
already calls `ApplyUpdate` over the runtime's metadata-update channel,
and Reactor's existing handler ends in `ActiveHostInternal?.RequestRender(force: true)`
(see memory: "Reactor hot reload is in-place"). With the WinUI window
reparented as `WS_CHILD`, the in-place render still works because the
visual tree, render thread, and composition surface all live in the
Reactor process. The VS extension does nothing; the embedded surface just
refreshes.

Type-shape edits that the runtime cannot patch (rude edits) — covered by
spec 049 — surface as `MetadataUpdater.IsSupported == false` for that
delta. **`dotnet watch` itself rebuilds and respawns the child process**;
the watch supervisor process stays alive across the respawn. This is the
critical detection problem on the extension side:

> **The extension cannot rely on `Process.Exited` of the `dotnet watch`
> process to detect a respawn — the watch supervisor doesn't exit. The
> respawn is detected by observing a new `CAPTURE_PORT=` / `CAPTURE_TOKEN=`
> pair on the supervisor's stdout, distinct from the previous pair.**

The extension models the active preview as a session keyed by
`(port, token, hwnd, sessionId)` where `sessionId` is an extension-local
counter incremented every time stdout produces a new token. Concretely:

1. New `CAPTURE_TOKEN=` line observed → bump local `sessionId`.
2. Cancel in-flight HTTP requests targeting the previous session
   (`HttpClient.CancelPendingRequests()`).
3. Show "Respawning…" overlay.
4. `GET /components`, `GET /hwnd`, `POST /embed/ack` against the new
   port/token, passing the new `generation` field.
5. Reapply the currently-selected component via `POST /preview`.
6. Hide overlay.

All HTTP responses tagged with a stale `sessionId` (e.g., a `/preview`
post that landed against the now-dead child) are simply discarded; the
extension does not surface them to the user.

By default we also pass dotnet-watch settings that minimize confusing
interactive prompts on rude edits:

```
DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1
DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1
DOTNET_USE_POLLING_FILE_WATCHER=0
```

(`DOTNET_WATCH_RESTART_ON_RUDE_EDIT` was added in .NET 9 SDK; on older
SDKs the prompt is interactive — we parse the prompt string out of
stdout and answer "y" automatically.)

---

## §6 VS extension design

### §6.1 Project layout

Add a new top-level folder mirroring the VS Code extension:

```
src/vs-reactor/
├── Reactor.VsExtension.csproj   ← VSIX, .NET Framework 4.7.2
├── source.extension.vsixmanifest
├── ReactorPackage.cs            ← AsyncPackage
├── ReactorEmbedToolWindow.cs    ← ToolWindowPane
├── UI/
│   ├── ReactorEmbedControl.xaml ← WPF UserControl: chrome + HwndHost
│   ├── ReactorEmbedControl.xaml.cs
│   ├── HwndHostPlaceholder.cs   ← HwndHost subclass
│   └── ComponentDropdownVM.cs
├── Embed/
│   ├── ReactorChildLauncher.cs  ← spawn dotnet watch with --embed
│   ├── EmbedClient.cs           ← HTTP wrapper for PreviewCaptureServer
│   └── ProjectContextResolver.cs ← .csproj + active-doc → context
├── Commands/
│   ├── PreviewActiveFileCommand.cs
│   ├── StopPreviewCommand.cs
│   └── ForceReloadCommand.cs
└── Tests/
    └── … (see §13)
```

The csproj is the legacy SDK style (`Microsoft.NET.Sdk` + `<UseWPF>true</UseWPF>` + `Microsoft.VisualStudio.SDK` PackageReference + `<TargetFramework>net472</TargetFramework>` + `<GeneratePkgDefFile>true</GeneratePkgDefFile>` + VSIX-specific properties).

Key NuGet packages:

- `Microsoft.VisualStudio.SDK` 17.8+
- `Microsoft.VSSDK.BuildTools` 17.8+
- `Newtonsoft.Json` (VS already ships it; pin to the version VS 2022 carries to avoid binding redirect headaches) — *or* `System.Text.Json` via the JSON helpers already in the SDK.

### §6.2 Package + tool window

```csharp
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideToolWindow(typeof(ReactorEmbedToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids80.SolutionExplorer)]
[Guid(PackageGuidString)]
public sealed class ReactorPackage : AsyncPackage
{
    public const string PackageGuidString = "8B7C6A50-8E5B-4B73-9D5C-AA8E1F09E1A1";
    /* … initialize commands … */
}
```

`ReactorEmbedToolWindow` extends `ToolWindowPane`, sets `Caption = "Reactor Preview"`, and `Content = new ReactorEmbedControl()`.

The pane registers a `ToolWindowToolbar` with three commands: `PreviewActiveFile`, `Stop`, `ForceReload`. The component-picker dropdown is implemented as a `Combo` command (`OLECMDF_SUPPORTED|OLECMDF_ENABLED`) bound through a `DropDownCombo` IOleCommandTarget handler, OR as a WPF dropdown directly in the user control's chrome strip — we go with the WPF dropdown because it gives us better data-binding and template control. The chrome lives **above** the HwndHost row inside the user control, so airspace doesn't bite.

**Stability note:** classic in-proc VSIX means every line of this
extension runs inside `devenv.exe`. A null-ref, unhandled async
exception, or P/Invoke misuse can crash Visual Studio. Mitigations the
implementation must follow:

- Keep the in-proc surface minimal. The tool window UI is thin; all
  process spawning, HTTP traffic, and stdout parsing live in helper
  classes designed to be unit-tested headlessly (Tier A in §13).
- No `Microsoft.UI.*` / Windows App SDK references in the VSIX project.
  The extension speaks only Win32 (P/Invoke) and HTTP to the child.
  WinUI 3 itself never loads into `devenv.exe`.
- All async entry points wrap in `JoinableTaskFactory.RunAsync` and
  catch `Exception` at the boundary, logging to the output channel.
- `try/finally` around every native handle: `_jobHandle`,
  `_placeholderHwnd`, `HttpClient` instances.

### §6.3 `HwndHostPlaceholder`

```csharp
internal sealed class HwndHostPlaceholder : HwndHost
{
    private IntPtr _placeholderHwnd;

    public IntPtr PlaceholderHwnd => _placeholderHwnd;
    public event EventHandler<SizeChangedEventArgs>? PlaceholderResized;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _placeholderHwnd = NativeMethods.CreateWindowExW(
            dwExStyle: 0,
            lpClassName: PlaceholderClass.Name,
            lpWindowName: "ReactorEmbedPlaceholder",
            dwStyle: NativeMethods.WS_CHILD | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_VISIBLE,
            X: 0, Y: 0, nWidth: 0, nHeight: 0,
            hWndParent: hwndParent.Handle,
            hMenu: IntPtr.Zero,
            hInstance: NativeMethods.GetModuleHandle(null),
            lpParam: IntPtr.Zero);
        return new HandleRef(this, _placeholderHwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        // We own the placeholder; the embedded WinUI HWND is owned by the child process.
        // The user control calls EmbedClient.Release() BEFORE WPF tears the placeholder down,
        // so by this point the child has SetParent'd back to NULL and exited.
        if (_placeholderHwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_placeholderHwnd);
            _placeholderHwnd = IntPtr.Zero;
        }
    }

    protected override void OnWindowPositionChanged(System.Windows.Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        PlaceholderResized?.Invoke(this, new SizeChangedEventArgs(
            (int)rcBoundingBox.Width, (int)rcBoundingBox.Height));
    }
}
```

The `PlaceholderClass` is a process-global `WNDCLASSEX` registered once
on package load. Its WndProc is `DefWindowProcW` for everything except
`WM_ERASEBKGND` (returns 1 to avoid background flicker before the child
attaches).

### §6.4 `ReactorEmbedControl` (chrome + placeholder)

```xaml
<UserControl xmlns="…">
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>

    <!-- Chrome -->
    <DockPanel Grid.Row="0" LastChildFill="False" Margin="4">
      <ComboBox DockPanel.Dock="Left" Width="240"
                ItemsSource="{Binding Components}"
                SelectedItem="{Binding SelectedComponent}"
                ToolTip="Select component to preview"/>
      <Button DockPanel.Dock="Left" Margin="6,0,0,0" Content="↻"
              ToolTip="Force reload (rebuild + restart)"
              Command="{Binding ForceReloadCommand}"/>
      <TextBlock DockPanel.Dock="Right" VerticalAlignment="Center"
                 Text="{Binding StatusText}"
                 Foreground="{Binding StatusBrush}"/>
    </DockPanel>

    <!-- Embedded preview surface -->
    <Border Grid.Row="1" Background="{DynamicResource VsBrush.ToolWindowBackground}">
      <local:HwndHostPlaceholder x:Name="Placeholder"/>
    </Border>

    <!-- Error overlay (visible only when EmbedState == Error) -->
    <Border Grid.Row="1" Visibility="{Binding ErrorOverlayVisible}"
            Background="{DynamicResource VsBrush.ToolWindowBackground}">
      <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
        <TextBlock Text="{Binding ErrorTitle}" FontSize="14" FontWeight="Bold"/>
        <TextBlock Text="{Binding ErrorDetail}" Margin="0,8,0,0" MaxWidth="500" TextWrapping="Wrap"/>
        <Button Content="Reload" Margin="0,12,0,0" HorizontalAlignment="Center"
                Command="{Binding ForceReloadCommand}"/>
      </StackPanel>
    </Border>

    <!-- Footer status bar (build progress, errors) -->
    <Border Grid.Row="2" Background="{DynamicResource VsBrush.StatusBarBuilding}"
            Visibility="{Binding BuildingVisible}">
      <TextBlock Text="Building…" Margin="6,2"/>
    </Border>
  </Grid>
</UserControl>
```

The code-behind wires `Placeholder.PlaceholderResized` → `EmbedClient.PostResize(w, h)`.
On `Loaded`: if no preview is running, fire `PreviewActiveFileCommand`. On `Unloaded`:
`EmbedClient.Release()` then `Process.Kill(entireProcessTree: true)`.

### §6.5 `ReactorChildLauncher`

Mirrors `launchPreviewProcess` from `src/vscode-reactor/src/extension.ts`,
in C#, with two production-criticial additions:

- **Windows Job Object** with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`
  assigned to the `dotnet watch` process. The job handle is owned by the
  extension's `AsyncPackage`. If `devenv.exe` dies (crash, kill, power
  loss), Windows guarantees the entire watch+child process tree is
  terminated. This is the **primary** orphan-cleanup mechanism;
  `--embed-host-pid` is a secondary child-side belt-and-braces.
- **Session generation tracking.** Each time stdout produces a new
  `CAPTURE_TOKEN=` line, raise a `NewSession(sessionId, port, token)`
  event (the launcher does not raise `Ready` once and forget — it raises
  `NewSession` every respawn).

```csharp
internal sealed class ReactorChildLauncher : IDisposable
{
    private readonly IntPtr _jobHandle;
    private Process? _watch;
    private int _sessionCounter;
    private int? _currentPort;
    private string? _currentToken;

    public event Action<int, int, string>? NewSession;  // (sessionId, port, token)
    public event Action<string>? StdoutLine;
    public event Action<string>? StderrLine;
    public event Action<int>? SupervisorExited;         // dotnet watch died

    public ReactorChildLauncher()
    {
        _jobHandle = JobObject.CreateKillOnCloseJob();
    }

    public async Task LaunchAsync(string csprojPath, int vsPid, CancellationToken ct)
    {
        var dotnetExe = DotnetResolver.Resolve(csprojPath)
            ?? throw new InvalidOperationException("`dotnet` not found outside the workspace.");

        var args = new List<string>
        {
            "watch", "run", "--project", csprojPath, "--",
            "--devtools", "run",
            "--vscode",
            "--embed",
            "--embed-host-pid", vsPid.ToString(CultureInfo.InvariantCulture),
        };

        var psi = new ProcessStartInfo
        {
            FileName = dotnetExe,
            Arguments = string.Join(" ", args.Select(QuoteIfNeeded)),
            WorkingDirectory = Path.GetDirectoryName(csprojPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // for auto-answering rude-edit prompts
        };
        psi.Environment["NoDefaultCurrentDirectoryInExePath"] = "1";
        psi.Environment["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"]  = "1";
        psi.Environment["DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER"] = "1";

        _watch = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _watch.OutputDataReceived += (_, e) => OnStdout(e.Data);
        _watch.ErrorDataReceived  += (_, e) => OnStderr(e.Data);
        _watch.Exited             += (_, _) => SupervisorExited?.Invoke(_watch.ExitCode);

        _watch.Start();
        JobObject.AssignProcessToJob(_jobHandle, _watch.Handle);
        _watch.BeginOutputReadLine();
        _watch.BeginErrorReadLine();
    }

    private void OnStdout(string? line)
    {
        if (line is null) return;
        StdoutLine?.Invoke(line);

        // dotnet watch sometimes pauses on a rude edit prompt; auto-answer.
        if (line.Contains("Do you want to restart your app", StringComparison.OrdinalIgnoreCase))
        {
            try { _watch?.StandardInput.WriteLine("y"); } catch { }
            return;
        }

        var portMatch = CapturePortRegex.Match(line);
        if (portMatch.Success) _currentPort = int.Parse(portMatch.Groups[1].Value);

        var tokenMatch = CaptureTokenRegex.Match(line);
        if (tokenMatch.Success) _currentToken = tokenMatch.Groups[1].Value;

        if (_currentPort is int p && _currentToken is { } t)
        {
            // A fresh PORT+TOKEN pair on stdout means dotnet watch produced a new child.
            // Bump the session counter and raise NewSession so the extension can
            // cancel any in-flight requests against the previous session.
            var newId = Interlocked.Increment(ref _sessionCounter);
            NewSession?.Invoke(newId, p, t);
            _currentPort = null;
            _currentToken = null;
        }
    }

    public void Dispose()
    {
        try { _watch?.Kill(entireProcessTree: true); } catch { }
        _watch?.Dispose();
        // Closing the job handle terminates anything still in the job (belt-and-braces).
        if (_jobHandle != IntPtr.Zero) NativeMethods.CloseHandle(_jobHandle);
    }
}
```

The `DotnetResolver` ports the security-hardened `dotnet` lookup from the
VS Code extension (refuse anything inside the workspace; honor
`NoDefaultCurrentDirectoryInExePath`).

The `JobObject` helper wraps `CreateJobObject` +
`SetInformationJobObject(JobObjectExtendedLimitInformation,
JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE)` + `AssignProcessToJobObject`.

### §6.6 `EmbedClient`

Thin wrapper over `HttpClient` that injects the bearer token, sets `Host:
127.0.0.1:<port>`, posts JSON to the new endpoints. Mirrors `httpGetJson` /
`httpPostJson` from the VS Code extension. **One per session, not one per
preview** — each respawn creates a new `EmbedClient` and disposes the old
one, which cancels in-flight requests via `HttpClient.CancelPendingRequests`.
All requests use loopback only (`127.0.0.1`). The `generation` returned by
`/hwnd` is round-tripped on `/embed/ack`; the server rejects mismatched
generations with HTTP 409 (a belt-and-braces guard against the extension
sending an ack against a stale child).

### §6.7 The embed handshake (sequence)

```
VS ext                              Reactor child (dotnet watch supervisor + target)
─────                              ───────────────────────────────────────────────
LaunchAsync(csproj, vsPid)
                                   … dotnet watch starts target …
                                   CAPTURE_PORT=NNNN    ←─── target stdout
                                   CAPTURE_TOKEN=…      ←─── target stdout
NewSession(sid=1, port, token)
  │
GET  /components                   → { components, current }
GET  /hwnd                         → { hwnd: "0x...", generation: 1 }
                                   (target window is hidden, WS_CHILD, no parent)
POST /embed/ack
   { parent, w, h, generation:1 }  → SetParent(reactorHwnd, parent)
                                     SetWindowPos(...)
                                     ShowWindow(SW_SHOW)
                                   { ok: true }
(steady state — input flows
 to WinUI, hot reload edits
 just refresh the surface)
…
POST /embed/resize {w,h}           (on host resize)
POST /preview {component}          (on dropdown change)

[ User saves a file with a Render-body edit ]
                                   dotnet watch detects file change
                                   ApplyUpdate via MetadataUpdater
                                   RequestRender(force:true) — UI refreshes in place
(no extension involvement, no respawn — placeholder content updates)

[ User saves a file with a rude edit ]
                                   dotnet watch terminates target, rebuilds, relaunches
                                   CAPTURE_PORT=MMMM    ←─── new target stdout
                                   CAPTURE_TOKEN=…      ←─── new target stdout
NewSession(sid=2, port', token')
  │── (cancel in-flight HTTP against sid=1)
  ├── GET /hwnd → { hwnd: "0x...", generation: 1 (new process!) }
  ├── POST /embed/ack { parent, w, h, generation: 1 }
  │       (placeholder HWND unchanged, so VS dock layout is preserved)
  └── POST /preview { currentComponent }

[ User clicks ↻ Force Reload ]
POST /embed/release                → SetParent(myHwnd, NULL), Window.Close(), Exit(0)
launcher.Dispose()                 (Job Object kills any survivors;
                                    also kills the watch supervisor)
LaunchAsync(csproj, vsPid)         (fresh start, sid=1 again on a new launcher)

[ User closes the tool window ]
POST /embed/release
launcher.Dispose()
(placeholder HWND destroyed when WPF tears down the HwndHost)
```

---

## §7 Component dropdown & auto-selection

### §7.1 Population

After the embed handshake, the extension calls `GET /components` (existing
endpoint). The response is the list of `Component` subclass names
discovered via `DevtoolsHost.FindAllComponentNames`
(`Assembly.GetTypes()`-based). Populate the `ComboBox.ItemsSource`.

### §7.2 Auto-selection from the active editor

The extension subscribes to `IVsRunningDocumentTable` events
(`OnAfterFirstDocumentLock` + `IVsTextManager.GetActiveView`), plus
`WindowEvents.WindowActivated` via DTE. On either:

1. Resolve the active document's full path.
2. If the file is `.cs`, parse it (regex, same shape as
   `findAllComponentClasses` in `extension.ts`:
   `/class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*Component(?:<[^>]*>)?\b/g`) for
   classes inheriting `Component`.
3. If at least one component is found AND it appears in the dropdown's
   list AND the user has not manually pinned a component, set
   `SelectedComponent` to the first match. The setter posts
   `POST /preview { component }`. The selection update flows back to the
   model from the HTTP response.
4. If the file belongs to a **different csproj** than the active preview,
   stop the existing preview and relaunch against the new csproj (same
   policy as the VS Code extension).

### §7.3 Manual override pinning

When the user manually picks an item from the dropdown, we set
`_manualPin = SelectedComponentName`. Editor-driven auto-selection is
suppressed until either:

- the user clicks the dropdown's "Auto-track active file" item (top of the
  list, separator below), or
- the active document is one that lexically defines `_manualPin` (i.e.,
  the user is now editing the pinned component, so tracking and pin agree
  — no UI change needed).

This matches developer ergonomics: "I picked this one, leave it alone
until I say otherwise."

### §7.4 Discovery refresh

The dropdown re-fetches `/components` whenever:

- the child process is (re)launched,
- a hot-reload update fires for a type whose name matches the
  `Component` regex (cheap heuristic; just refresh on every reload, the
  endpoint is local and tiny), or
- the user clicks the refresh affordance on the dropdown.

---

## §8 Force-reload button & hot-reload integration

### §8.1 Three layers of "reload"

| Layer | Trigger                                  | Mechanism                                    | What survives                                  |
|-------|------------------------------------------|----------------------------------------------|------------------------------------------------|
| L1    | Save edits to `Render()` body            | Runtime metadata-update → `RequestRender(force:true)` | App state, navigation, hook state (per spec 049) |
| L2    | Save edits the runtime can't patch       | `dotnet watch` rebuilds + respawns the target | Tool window, csproj selection, current component |
| L3    | User clicks ↻ in the chrome              | Extension kills the `dotnet watch` tree (Job Object close) + relaunches | Tool window, csproj selection, current component |

L1 is automatic and invisible to the extension.
L2 is automatic; the extension observes a new `CAPTURE_TOKEN=` on stdout
and rehandshakes (see §5.5).
L3 is the user's escape hatch when L1 or L2 has gone wrong (the build is
stuck, the WinUI window has wedged, or `dotnet watch` itself is hung).

### §8.2 Why no `/reload` endpoint and no exit-42

A `/reload` HTTP endpoint that calls `Environment.Exit(42)` was
considered. **Rejected** because the supervisor is `dotnet watch`, not
`mur devtools`, and `dotnet watch`:

- Treats any non-zero exit code as a crash, not a reload request. It only
  relaunches when a file change triggers a metadata update — it will not
  rebuild + relaunch on a bare child exit.
- Has its own opinions about respawn timing, debouncing, and error
  reporting that fight against an exit-driven restart loop.

Switching the supervisor to `mur devtools` (which does honor exit-42)
would buy us nothing: we want `dotnet watch`'s file-change → metadata-
update pipeline for L1, and that's the entire point of the embedded
preview. So force-reload is **purely extension-side**:

```csharp
async Task OnForceReloadClickedAsync()
{
    _status.Set("Reloading…");
    try { await _embedClient.ReleaseAsync().WithTimeout(TimeSpan.FromSeconds(2)); }
    catch { /* best-effort; the kill below makes it moot */ }

    _launcher.Dispose();   // closes the Job Object → kills dotnet watch + child + grandchildren
    await Task.Delay(200);
    await StartPreviewAsync(_currentCsproj, _currentComponent);
}
```

Total time on a small project: ~3-5 s. The button is the user's escape
hatch; the in-flight build progress is sacrificed for determinism.

### §8.2 What L1 hot reload looks like in the embedded surface

The user saves a file with a `Render` body change. `dotnet watch` calls
`ApplyUpdate` over the runtime metadata-update channel. Reactor's
`HotReloadService.UpdateApplication` sets `_withinUpdatePass` and calls
`ActiveHostInternal?.RequestRender(force: true)`. The visual tree updates
in-place on the embedded WinUI surface. From VS's perspective: zero
events fire — the tool window doesn't know anything happened. The
embedded HWND just shows new pixels.

Build errors during hot reload surface as `dotnet watch`'s stderr output;
we mirror them in the footer status bar (`StatusText = "Build error: …"`)
and surface the first line in the dropdown's tooltip.

### §8.3 What L2 (rude edit → respawn) looks like

The runtime reports the update as unsupported; `dotnet watch` rebuilds and
respawns. The watch supervisor process stays alive; we detect the respawn
by stdout (new `CAPTURE_TOKEN=`), not by `Process.Exited`. See §5.5 for
the detection algorithm. The extension:

1. Shows the error overlay momentarily ("Reloading after build…").
2. Waits for the next `CAPTURE_PORT=` / `CAPTURE_TOKEN=` pair on the
   supervisor stdout (capped at 30 s; after that, surface "Build failed,
   click ↻ to retry" — the actual build error has typically been logged
   to the output channel already from `dotnet watch`'s stderr).
3. Re-handshakes embed via `POST /embed/ack` with the same placeholder
   HWND and current bounds, against the new port/token.
4. Re-applies the current component selection via `POST /preview`.

The placeholder HWND **never changes**, so VS's docking layout is
preserved across respawns. This is the headline UX win over the VS Code
streaming approach.

---

## §9 Lifecycle, errors, and crash recovery

| State                              | Trigger                          | Extension reaction                                   |
|------------------------------------|----------------------------------|------------------------------------------------------|
| **Idle**                           | Initial / after Stop             | Empty placeholder. Dropdown disabled. "Preview" button enabled. |
| **Launching**                      | PreviewActiveFile command        | Footer = "Launching…". `dotnet watch` spawned.       |
| **Waiting for handshake**          | Got CAPTURE_PORT / TOKEN         | Fetched `/components` and `/hwnd`. POST `/embed/ack`. |
| **Embedded**                       | `/embed/ack` returned 200        | Placeholder shows live surface. Dropdown enabled. Status = "Live". |
| **Building**                       | Watch detected file change       | Footer = "Building…". Embedded surface stays. (L1 path.) |
| **Respawning**                     | Child exited unexpectedly        | Error overlay = "Reloading…". Wait up to 30s for new handshake. |
| **Build failed**                   | Respawn timeout / stderr "FAILED" | Error overlay = build error message. ↻ button focused. |
| **Crashed (no respawn)**           | Watch process exited             | Error overlay = "Preview process exited". Auto-restart once; if it crashes again, give up and require ↻. |
| **Project switch**                 | Active doc moved to other csproj | Same as ForceReload but with new csproj. Manual pin cleared. |

DTE-driven events on the VS side (solution close, project unload, VS
shutdown) all collapse to "Idle" through the same path: call
`/embed/release`, kill the `dotnet watch` tree, drop the placeholder
HWND's children. The placeholder itself only dies when the tool window
itself disposes.

A subtle but important property: because the **VS placeholder HWND
persists across child respawns**, VS's "remember tool window layout"
machinery never sees the embed disappear. Float / dock / tab-group it
freely; the embedded preview survives.

---

## §10 Security model

The extension inherits and extends the existing
`PreviewCaptureServer` security envelope (TASK-018 through TASK-029 — see
inline comments in `src/Reactor.Devtools/PreviewCaptureServer.cs`):

- **Loopback-only listener.** `http://127.0.0.1:<port>/`. No external
  binding.
- **Per-launch bearer token.** 256-bit, base64url, printed once on stdout
  via `CAPTURE_TOKEN=…`. The extension is the only consumer; we don't
  pass it to a webview.
- **Host header allowlist.** `127.0.0.1:<port>` only.
- **Origin allowlist.** VS extension requests are loopback-to-loopback
  with no Origin set. The existing `vscode-webview://` and
  `http://localhost*` allowances stay (back-compat for the VS Code
  extension); we add no new Origin entry.
- **CSRF-safe POST.** All write endpoints require
  `Content-Type: application/json`; the existing 4 MB body cap remains;
  new endpoints add a tighter 4 KB cap because payloads are tiny.
- **`dotnet` lookup hardening.** Port the `DotnetResolver` from the VS
  Code extension: refuse any `dotnet[.exe|.cmd|.bat|.com]` resolved from
  inside the workspace; set `NoDefaultCurrentDirectoryInExePath=1` on
  the spawned process; `realpath` check to defeat symlink escapes.
- **No remote code execution surface.** The extension never executes user
  HTML/JS; it does not host a webview. The embedded HWND is the
  developer's own compiled WinUI app — no new trust boundary.
- **Process-tree kill on dispose.** `Process.Kill(entireProcessTree:
  true)` prevents leaks if VS aborts.

New endpoints (`/hwnd`, `/embed/*`) are no more privileged
than the existing `/preview` and `/focus`: they all mutate the local
preview process, and they all sit behind the same bearer-token gate.

The one new attack surface to think about: the VS extension is **inside
`devenv.exe`** and inherits VS's full trust on the developer machine.
A hostile project could try to ship a `dotnet.cmd` that runs malicious
code, exactly the same risk the VS Code extension mitigates. We reuse
that mitigation 1:1.

---

## §11 VS licensing & local install

### §11.1 Building the VSIX

- **VS 2022 Community** is sufficient for individual developers and for
  small organizations (≤5 users in a non-enterprise org per the standard
  Community EULA).
- **VS 2022 Professional or Enterprise** is required for organizations
  that exceed the Community thresholds (the Reactor team itself qualifies
  as enterprise — use Pro/Ent).
- The VSIX SDK templates and the entire build toolchain (`Microsoft.VSSDK.BuildTools`)
  are free and ship with the "Visual Studio extension development"
  workload via the VS Installer.
- **No additional license** is required to build, sign, or distribute a
  VSIX internally. There is no "extension developer license" for VS like
  there used to be for the early UWP Store push.

### §11.2 Installing the VSIX locally

- For dev/test: F5 on the VSIX project launches the **Experimental
  Instance** with the extension auto-installed. No special permission.
- For "real" daily-driver use: double-click the `.vsix` to install it
  into the main VS hive. Self-built VSIXes are not signed by a trusted
  Microsoft authority, so VS warns the user with a yellow "This
  extension is not digitally signed" banner. The user can choose to
  install anyway. No admin rights required (per-user install).
- **For team-wide distribution** (eventually): publish to either the
  public VS Marketplace, the Microsoft-internal extension gallery, or an
  internal NuGet feed configured as an additional gallery in VS
  (`Tools → Options → Environment → Extensions → Add`). All three are
  free; Marketplace requires a publisher account.

### §11.3 Signing

For Phase-1 dev distribution the unsigned VSIX is fine. Before any wider
release we sign with the same Microsoft-internal cert used for the rest
of the Reactor packages (handled by the existing release pipeline in
`.github/workflows/release.yml`; the VSIX is added as a fourth asset
alongside the existing three NuGet/zip outputs).

---

## §12 Debugging the extension

### §12.1 The Experimental Hive

VS extensions debug by launching a second instance of `devenv.exe` with
`/RootSuffix Exp`. That isolated instance has its own registry hive,
settings, and extension cache — it cannot break the developer's main
VS install.

The default VSIX project template wires this up: on F5,
`Microsoft.VSSDK.BuildTools.targets` sets
`<StartProgram>$(DevEnvDir)devenv.exe</StartProgram>` and
`<StartArguments>/rootsuffix Exp</StartArguments>`. No further config
needed for routine debugging.

For multi-extension scenarios or when we want the Exp hive to use a
custom suffix (e.g., `Reactor`), we can override:

```xml
<PropertyGroup>
  <StartAction>Program</StartAction>
  <StartProgram>$(DevEnvDir)devenv.exe</StartProgram>
  <StartArguments>/RootSuffix Reactor /log</StartArguments>
</PropertyGroup>
```

The `/log` switch dumps `ActivityLog.xml` to
`%APPDATA%\Microsoft\VisualStudio\17.0_<hash>Reactor\` — invaluable
when the extension fails to load and you see only a yellow info bar.

### §12.2 Debugging across processes

Inside the Exp hive, the extension is in `devenv.exe`. The Reactor child
is in its own `dotnet.exe`. To debug both:

1. Set a breakpoint in the VSIX (e.g., `EmbedClient.AckEmbed`).
2. F5 to launch the Exp hive.
3. Open a Reactor project. Open the Reactor Preview tool window.
4. The breakpoint hits. Step.
5. To also attach to the child: `Debug → Attach to Process…` in the
   **outer** (developer) VS instance, pick the latest `dotnet.exe`
   running with `--embed`, attach to "Managed (.NET 10.0)".

For headless reproduction of bugs, set `Reactor.VsExtension.DebugBreakOnAttach=1`
as an env var to `devenv.exe`. The extension's `OnPackageLoaded` checks
the env var and calls `Debugger.Launch()` if set.

### §12.3 Cross-process tracing

Both processes emit structured log lines. The extension writes to a
named `OutputChannel` ("Reactor Preview") in VS. The child's `--devtools`
logger writes to its existing log file under
`%LOCALAPPDATA%\Reactor\devtools\` (see `DevtoolsLogger`). Both share a
correlation ID — the `CAPTURE_TOKEN` — so post-hoc joining is easy.

---

## §13 Automated testing

Three test tiers, mirroring the rest of the repo's testing taxonomy
(see `TESTING.md`).

### §13.1 Tier A — Pure unit tests (xUnit, headless)

For the extension's pure logic: `DotnetResolver`,
`ComponentDropdownVM`, `ProjectContextResolver` (regex parser),
`EmbedClient` (against a stubbed `HttpMessageHandler`), the launcher's
stdout-line parser.

Location: `src/vs-reactor/Tests/Reactor.VsExtension.Tests/` (xUnit, net472
or net48, no VS SDK dependency).

Add to `Reactor.slnx` like all other test projects. Run via
`dotnet test src/vs-reactor/Tests/Reactor.VsExtension.Tests/`.

Aim for ~80% line coverage of the pure-logic surface. Specifically:

- `DotnetResolver.ResolveDotnet_RejectsWorkspaceLocal`
- `DotnetResolver.ResolveDotnet_RejectsSymlinkEscape`
- `LaunchOutputParser.PortAndTokenSequence`
- `ComponentRegex_FindsGenericComponent`
- `ComponentRegex_IgnoresUnrelatedClasses`
- `EmbedClient.Ack_SendsCorrectJson`
- `EmbedClient.Ack_AddsBearerHeader`
- `EmbedClient.Ack_SetsHostHeader`
- `EmbedClient.Reload_PostsToReload`

### §13.2 Tier B — In-process VS SDK tests (`Microsoft.VisualStudio.SDK.TestFramework`)

For tests that need a real `AsyncPackage`, `IVsUIShell`, or
`IVsRunningDocumentTable`. The SDK test framework spins up a lightweight
in-proc VS shell (no real `devenv.exe`) and provides the standard
services.

```csharp
[Collection(MockedVS.Collection)]
public sealed class ReactorPackageTests
{
    public ReactorPackageTests(GlobalServiceProvider sp) => sp.Reset();

    [Fact]
    public async Task Package_RegistersToolWindow()
    {
        var pkg = new ReactorPackage();
        await pkg.InitializeAsync(CancellationToken.None, progress: null);
        var found = await pkg.FindToolWindowAsync(typeof(ReactorEmbedToolWindow), id: 0, create: true, default);
        Assert.NotNull(found);
    }
}
```

Add `Microsoft.VisualStudio.SDK.TestFramework.Xunit` PackageReference.
These tests run on `dotnet test` like normal xUnit; CI runs them on the
same `windows-latest` runner.

Cover: package init, tool window creation, command dispatcher wiring,
DTE event subscription/unsubscription on solution close.

### §13.3 Tier C — End-to-end (Exp hive, manual + Apex)

Real launches of `devenv.exe /RootSuffix ReactorE2E` driving a real
sample project. Two sub-tiers:

- **C.1 Manual smoke** (always required, never automated):
  - Launch Exp hive.
  - Open a `samples/apps/...` project.
  - Open Reactor Preview, switch between components, edit a `Render`
    body, save, observe the embedded UI updating, click ↻, observe
    respawn, float the tool window, observe rehosting.
- **C.2 Apex automated UI** (optional, recommended for the `appium`-style
  E2E tier): `Microsoft.VisualStudio.IntegrationTest.Utilities` +
  `Microsoft.Test.Apex.VisualStudio` drive the Exp hive headfully.
  These are slow (~30s per scenario) and brittle, so keep the suite tiny:
  - Smoke: open the tool window → it shows the placeholder + dropdown.
  - Smoke: select a project → preview launches → status reaches "Live".
  - Smoke: click force-reload → status returns to "Live" within 10s.

Apex isn't on NuGet historically; the Reactor team needs an internal
Microsoft Apex package source. If that's not available, two fallbacks
keep us covered:

- **WinAppDriver / Appium** (UIA-based) drives the Exp hive headfully
  without Apex. The repo already has E2E Appium infrastructure for
  `tests/Reactor.AppTests` against Reactor apps; we can reuse the
  fixture base and target VS UIA elements (tool window pane, combo box,
  buttons by `AutomationId`). Slower per test than Apex but more
  portable and uses tooling already in the repo.
- **Manual smoke only**: drop C.2 entirely and rely on Tier A + Tier B
  for CI gating, plus the C.1 release checklist. Acceptable for an
  internal-dev-tool extension where the user base is the Reactor team
  itself.

The matrix:

| Tier | Tools                                          | CI?  | Sample size  |
|------|------------------------------------------------|------|--------------|
| A    | xUnit, no VS                                   | yes  | 40-80 tests  |
| B    | xUnit + `SDK.TestFramework`                    | yes  | 10-20 tests  |
| C.1  | Manual, Exp hive                               | no   | a 10-min checklist before each release |
| C.2  | xUnit + Apex (if available)                    | optional | 3-5 tests |

Add a new CI workflow step in `.github/workflows/ci.yml`:

```yaml
- name: Test VS extension
  run: dotnet test src/vs-reactor/Tests/Reactor.VsExtension.Tests/Reactor.VsExtension.Tests.csproj --no-build --verbosity normal
```

The VSIX itself builds as part of the existing solution build because
it's added to `Reactor.slnx`.

---

## §14 Build, package, distribute

### §14.1 Build

The VSIX project is a regular csproj added to `Reactor.slnx`. It is
.NET Framework 4.7.2 (VS extension host runtime); the rest of the
solution stays on .NET 10 — they don't share project references.
`AssemblyVersion` and `Version` are read from `Directory.Build.props`
just like the other projects in the repo.

### §14.2 Packaging

`Microsoft.VSSDK.BuildTools` produces a `Reactor.VsExtension.vsix` in
`bin/Release/`. The VSIX includes:

- `Reactor.VsExtension.dll`
- `Reactor.VsExtension.pkgdef`
- WPF resource dictionaries
- `extension.vsixmanifest`

It does **not** include any of Reactor core or Reactor.Devtools. The
extension's only runtime contract with Reactor is the HTTP API plus the
stdout key/value lines, both versioned (see Open Q4).

### §14.3 Distribution

For Phase 1, ship the `.vsix` as a release asset on the GitHub release
page alongside the existing NuGet packages and skill-kit zip. Update
`.github/workflows/release.yml` to publish the VSIX as a fourth asset.
Sign the VSIX using the existing release pipeline's signing cert.

Phase 2 (later, gated on demand): publish to the VS Marketplace under the
Reactor team's publisher account.

### §14.4 The `mur` integration

Optionally add a `mur vs install` subverb that copies the `.vsix` from
the local NuGet feed equivalent (or downloads from the latest release)
and shells out to `VsixInstaller.exe`. Out of scope for the spec; nice-
to-have.

---

## §15 Phasing

### Phase 0 — Spike (1-2 days completed; 2-3 days remain)

#### Initial spike (2026-06, COMPLETE)

Built under `C:\Users\andersonch\Code\ReactorDemo\embed-spike\` (outside
the repo, throwaway — see Appendix C). WinForms x64 host with a `Panel`
acting as the placeholder; Reactor + WinUI 3 x64 child reading
`--embed-host-hwnd <hex>` from argv and applying Mitigation A inside
`ReactorApp.WindowOpened`. Reactor 0.0.0-local from the repo's local
NuGet feed. Tested on Windows 11 ARM64 with both processes running
under x64 emulation.

| # | Scenario | Result | Notes |
|---|----------|--------|-------|
| 1 | Mouse click on a `Button` | ✅ | Click counter incremented |
| 2 | Mouse drag (slider thumb) | ✅ | Smooth tracking |
| 4 | Mouse wheel | ✅ (implicit via slider) | |
| 5 | Keyboard focus into `TextBox` and typing | ✅ | "sfasdfas" typed end-to-end |
| 10 | `ComboBox` dropdown popup | ✅ | Popup rendered correctly, including overlapping the WinForms area outside the embedded surface |
| 13 | Force-reload preserves placeholder | ✅ | Child killed + relaunched; panel HWND survived |
| — | Stop + Launch cycle | ✅ | Repeated cleanly |
| — | Heartbeat (background-thread `UseReducer` updater) | ✅ | Counted monotonically while UI was idle, proving dispatcher liveness independent of input |

**Conclusion: Mitigation A is sufficient for the baseline interactive
embed.** The headline risk in §4.2 Risk 1 is retired. We proceed to
Phase 1 planning Mitigation A + A′ as the default, with C as a
documented fallback.

#### Residual matrix (Phase 0b — 2-3 days, to run against a real VSIX)

The initial spike used a WinForms placeholder, not a VS WPF tool
window, and didn't exercise the full Phase 0 checklist. Before
committing to ship Phase 1, validate these against a real VSIX hosting
the same Reactor target:

| # | Scenario | Pass criterion |
|---|----------|----------------|
| 3 | Hover (tooltip on a Button) | Tooltip appears on the embedded surface |
| 6 | Tab traversal between `TextBox`es | Focus moves — likely needs A′'s `TabIntoCore` |
| 7 | Ctrl+A / Ctrl+C / Ctrl+V in `TextBox` | Standard shortcuts work — likely needs A′'s `TranslateAcceleratorCore` |
| 8 | Escape closes a `ContentDialog` | Closes |
| 9 | IME (e.g. Pinyin) typing | Composition window at caret; commits cleanly |
| 11 | `MenuFlyout` from right-click | Appears and dismisses |
| 12 | DPI mixed-monitor move | Float the tool window, drag from 100% monitor to 200% monitor — content rescales without blur |
| 13 | Full VS dock state cycle | Dock → float → re-dock → tab with Output → auto-hide → restore |
| 16 | Force reload with no HWND leaks | Check Spy++ for orphan placeholders after 20× rapid reloads |
| 17 | UIA tree | inspect.exe sees the WinUI tree under the VS tool window root; focus follows |
| 18 | VS crash → Job Object cleanup | Force-kill `devenv.exe`; verify `dotnet watch` + child terminate within 1 s |
| 19 | Elevation mismatch | Document the result either way |
| 20 | ARM64-native | Re-run on native arm64 binaries once Reactor packs an arm64 nupkg variant (current local feed is x64-only) |

A subset of these (#6, #7, #9, #17) materially shape the A′ scope; the
rest are go/no-go gates that are highly likely to pass.

### Phase 1 — MVP

Implement the spec as described:

- Reactor: `--embed` CLI flag, `/hwnd`, `/embed/ack`, `/embed/resize`,
  `/embed/release` endpoints (no `/reload` — see §8.2), child-mode
  window construction.
- VSIX: package, tool window, HwndHostPlaceholder, ReactorEmbedControl
  chrome, EmbedClient (with session-generation guards), ReactorChildLauncher
  (with Job Object + stdout-driven session detection), ProjectContextResolver,
  PreviewActiveFileCommand, ForceReloadCommand, StopPreviewCommand.
- Tier A unit tests + Tier B SDK tests in CI.
- Manual smoke checklist (C.1) checked in under `src/vs-reactor/TESTING.md`.
- README under `src/vs-reactor/README.md`.
- VSIX added as release asset.

Estimated 4-6 weeks for a single engineer, dominated by Mitigation A/A′
robustness work, session-generation race handling, and Apex/WinAppDriver
E2E (if pursued). Add 1-2 weeks if Phase 0 shows we need owner-window
fallback to be production-ready as well.

### Phase 2 — Polish

- DTE-driven auto-preview on solution load (opt-in setting).
- Persisted last-component-per-project (per-user state in
  `%LOCALAPPDATA%\Reactor\vs\<projectHash>.json`).
- "Pin to top" toggle for owner-mode windows.
- Tool window toolbar with `Refresh components`, `Open MCP port`,
  `Show devtools log` affordances.
- Optional: hot-reload deltas highlighted (`SizeChanged` flash on
  the embedded surface synced from a new `/lastReload` endpoint).

### Phase 3 — Stretch

- Component property inspector inside the tool window (replays
  `DevtoolsPropertyTools` MCP surface as a WPF tree).
- Live element tree view (replays `DevtoolsTools.GetComponentTree`).
- "Open in external window" pop-out (detach and become standalone).

Phases 2/3 are out of scope for this spec; listed only to confirm the
Phase-1 design admits them without rework.

---

## §16 Open questions

1. **Q1 — `MITMessageOnlyWindowClass` survival on respawn.**  
   Each child has its own message-only window. When the supervisor
   respawns the target, the old HWND is gone, the new HWND must reattach
   to the same placeholder. The `/embed/ack` re-issue handles that, but
   we should verify VS's HwndHost doesn't cache the original child HWND
   anywhere that breaks across respawns.  
   **Plan to resolve:** part of Phase 0 spike.

2. **Q2 — DPI on multi-monitor moves.**  
   When the VS tool window is floated and moved to a monitor at a
   different scale, both VS and the embedded child should observe
   `WM_DPICHANGED_BEFOREPARENT` / `WM_DPICHANGED`. Does the WinUI 3
   composition tree pick up the new DPI when it arrives via the
   placeholder's parent chain? Or do we need to forward `WM_DPICHANGED`
   manually from the placeholder to the child?  
   **Plan to resolve:** part of Phase 0 spike, on a multi-monitor rig with
   mixed scales.

3. **Q3 — IME / accessibility / UIA tree across the boundary.**  
   The XAML island exposes a UIA tree. Does that UIA tree show up to
   inspect.exe when the window is reparented as a child of a foreign
   process? Does Narrator follow focus across the boundary?  
   **Plan to resolve:** Phase 1 manual smoke (C.1); document any gaps.

4. **Q4 — HTTP API versioning.**  
   The extension and the child are independently versioned. We need to
   reject mismatched protocols cleanly. Proposed: add
   `{ "protocol": "embed-v1" }` to the `/status` endpoint, and have the
   extension refuse to handshake if the field is absent or mismatched —
   in which case it falls back to the streaming-thumbnail UX silently
   (so old Reactor + new extension still gives the user *something*).

5. **Q5 — Project type discovery.**  
   `ProjectContextResolver` walks parents looking for `.csproj`. What
   about `.sln`-only solutions where the active doc isn't in any
   project, or where the project is `.fsproj`? Proposed: scope Phase 1
   to `.csproj`; surface "no Reactor project found" in the tool window
   chrome with actionable text.

6. **Q6 — Multi-targeted projects (`net472;net10.0-windows`).**  
   `dotnet watch run` picks one TFM. If the Reactor app multi-targets
   and `net472` isn't a WinUI 3 head, embed will fail. Need a
   `--framework` pass-through. Proposed: an extension setting
   `Reactor.Preview.Framework` defaulting to `net10.0-windows`.

7. **Q7 — Owner-mode (Mitigation C) UX.**  
   If we have to fall back to owner-mode, the WinUI window is visually
   *floating above* VS, not docked inside. Is that acceptable to ship?
   Proposal: yes, with a clear info-bar in the tool window saying
   "Embedded mode unavailable on this Windows version; preview is
   floating", plus a Settings → "Force floating preview" toggle for
   users who prefer it on machines where embed works.

8. **Q8 — Does VS's tool-window state preservation across VS restart
   re-launch the preview?**  
   The placeholder HWND is lost on VS restart; the embedded child
   process is also gone (because `--embed-host-pid` died). On the next
   VS launch, the tool window re-opens with a fresh placeholder and no
   child. Proposal: the package remembers per-solution "was preview
   running?" and auto-launches against the last component if so. Off by
   default behind a setting.

9. **Q9 — Are the embed endpoints needed in `Reactor.Advanced` too?**  
   `Reactor.Advanced` is the optional package with Win2D and similar
   heavyweight controls. It should not need any embed-specific code
   because everything lives in `Reactor.Devtools` and the core hosting
   layer. To confirm: spike must include a project that pulls in
   `Reactor.Advanced` and verify embed still works.

10. **Q10 — `/embed/release` semantics on Reactor child shutdown.**  
    Today the child exits cleanly when `host.Window.Close()` is called.
    With `WS_CHILD`, `WM_CLOSE` does not propagate the same way (the
    child window can't close itself; it's owned by the parent). We need
    a code path in `/embed/release` that explicitly unhooks the child
    from the parent (`SetParent(myHwnd, IntPtr.Zero)`), then calls
    `Window.Close()` so the rest of Reactor's shutdown (telemetry,
    persistence, etc.) runs.

11. **Q11 — WinAppSDK 2.0 experimental remote ContentIsland APIs.**  
    `DesktopChildSiteBridge.AcceptRemoteEndpoint` and
    `ContentIsland.ConnectRemoteEndpoint` exist as experimental
    surfaces. If they're production-viable from a net472 in-proc VSIX
    talking to a net10 child, they replace the entire §4.1/§4.2
    cascade with a supported API. Strong investigation target for
    Phase 0 (one engineer-day to determine "is this even reachable
    from this toolchain combination?"). Expectation: not viable in
    Phase 1; revisit annually as WinAppSDK matures.

12. **Q12 — Elevation mismatch (VS elevated vs child non-elevated).**  
    If `devenv.exe` is running elevated and the child runs at medium IL
    (or vice versa), Windows blocks the elevated process from receiving
    most window messages from the lower-IL one ("UIPI" — User Interface
    Privilege Isolation). `SetParent` itself succeeds but input is
    silently dropped. Proposal: detect token-elevation mismatch at
    launch (compare `OpenProcessToken` + `TokenElevation` for both
    sides via `IsProcessElevated`) and refuse to embed with a clear
    error directing the user to run VS un-elevated for preview.

13. **Q13 — Component detection beyond regex.**  
    The regex shipped in `findAllComponentClasses` from the VS Code
    extension misses partial classes split across files, generics with
    type-parameter constraints on a separate line, and components
    declared via `record` syntax. For Phase 1 we ship the regex with
    its known limits; for Phase 2 evaluate Roslyn-based discovery via
    the VS workspace API (`Microsoft.VisualStudio.LanguageServices`),
    which would also handle Solution Explorer integration.

14. **Q14 — Keyboard accelerator collisions with VS commands.**  
    The embedded WinUI window may want to handle Ctrl+S, Ctrl+Z, F5,
    etc. — collisions with VS's global commands. Proposal:
    `TranslateAcceleratorCore` lets the embedded window opt out of
    forwarding accelerators that have a meaningful WinUI handler. The
    safest default is: the embedded surface always wins when it has
    focus (matches in-app behavior); when focus is in the chrome,
    VS wins. Document and ship.

15. **Q15 — Supported WinAppSDK and Windows versions.**  
    Embed mode targets Windows 11 23H2+ and WinAppSDK 1.6+. We don't
    test on Windows 10 (out of support by the time this ships) or
    Windows 11 22H2 (input routing fixes for the bridge landed in
    23H2). The extension surfaces "Embedded preview requires Windows 11
    23H2 or later; falling back to floating preview" on older builds.

---

## Appendix A — Reference: existing surfaces this spec relies on

| Surface | File / Path | Purpose |
|---------|-------------|---------|
| `--devtools` CLI parser | `src/Reactor/Hosting/Devtools/DevtoolsCliParser.cs` | We extend with `--embed`, `--embed-host-pid`, `--embed-mode`. |
| `PreviewCaptureServer` | `src/Reactor.Devtools/PreviewCaptureServer.cs` | We add `/hwnd`, `/embed/ack`, `/embed/resize`, `/embed/move`, `/embed/release`. `/status` gains `protocol` + `generation` fields. Security envelope reused 1:1. |
| `DevtoolsHost.RunWithDevtools` | `src/Reactor.Devtools/DevtoolsHost.cs` | We hook embed setup in the same place that wires `server.SwitchComponent`. |
| `ReactorWindow` / `WindowSpec` | `src/Reactor/Hosting/ReactorWindow.cs`, `src/Reactor/Hosting/WindowSpec.cs` | Embed branches gate `AppWindow.Show()`, presenter, titlebar setup. |
| `HotReloadService` | `src/Reactor/Hosting/HotReloadService.cs` | Untouched. Embed mode benefits automatically. |
| `DevtoolsSupervisor` exit-42 | `src/Reactor.Cli/Devtools/DevtoolsSupervisor.cs` | NOT reused — we use `dotnet watch` directly. Mentioned for context. |
| VS Code extension | `src/vscode-reactor/src/extension.ts` | Port the launcher, dotnet resolver, port/token sniffer, component regex 1:1 into C#. |

---

## Appendix B — Why this is worth doing

The current screenshot stream is a respectable demo but it is not a
serious development surface. Once it's open, a developer keeps two
windows visible (the editor and the floating WinUI window) and reads
their preview as a thumbnail. They focus the real window to click
anything. The dropdown drives a remote control, not their actual
workflow.

The embedded preview collapses that to one window. The editor on the
left, the live component on the right, both inside the same VS layout
the developer has tuned over years. Edits flow into the live surface
without a second window stealing focus. Force-reload is one button
away, never an alt-tab. Hot reload becomes the steady state instead of
the special case. The MCP devtools port is still up, so the AI/agent
story is unchanged.

The cost was originally estimated as one Phase-0 spike to settle the
WinUI-3 reparenting question plus a 3-4 week Phase-1 to ship. After the
2026-06 spike (Appendix C) showed Mitigation A is sufficient for the
baseline, the risk has now narrowed to the residual Phase 0b matrix
(IME, UIA, full VS dock cycle, native ARM64) and the Phase 1
implementation. There is no two-week "we're 80% done and stuck" failure
mode in this plan.

---

## Appendix C — Phase 0 spike (2026-06)

**Location:** `C:\Users\andersonch\Code\ReactorDemo\embed-spike\`
(throwaway prototype, **not** in this repository).

**Purpose:** validate that cross-process WinUI 3 HWND reparenting works
end-to-end with interactive input, before committing to Path B in this
spec.

**Components:**

- `EmbedHost/` — WinForms (.NET 10, x64) stand-in for a VS tool window.
  Owns a `Panel` that acts as the placeholder HWND, plus a toolbar with
  Launch / Stop / Force-Reload buttons and a log textbox.
- `EmbedTarget/` — Reactor + WinUI 3 child (`Microsoft.UI.Reactor`
  0.0.0-local + `Microsoft.WindowsAppSDK` 2.0.1, x64). Subscribes to
  `ReactorApp.WindowOpened` before calling `ReactorApp.Run<App>`; the
  handler reads `--embed-host-hwnd <hex>` from argv and applies
  Mitigation A (hide → style flip → `SetParent` →
  `SetWindowPos(SWP_FRAMECHANGED|SWP_SHOWWINDOW)` → `SetFocus`). A
  background watchdog thread waits on `--embed-host-pid` and exits if
  the host dies. The component exercises a Button, TextBox, ComboBox,
  Slider, plus a background-timer heartbeat via `UseReducer`'s
  thread-safe updater.

**Protocol:** stdout-driven, hardcoded. Host spawns target with
`--embed-host-hwnd 0x<hex> --embed-host-pid <int> --embed-w <int>
--embed-h <int>`. Target prints `CHILD_HWND=0x<hex>` once reparented.
Host parses this and runs cross-process `SetWindowPos` on
`Panel.SizeChanged`. No HTTP, no auth, no Job Object — minimum viable
for the input-routing go/no-go question.

**Result:** all baseline scenarios passed (see §15 Phase 0 table). Most
notable: ComboBox dropdown popups render correctly and even extend
*outside* the embedded panel's bounds into the WinForms host area,
which confirms the WinUI composition tree retains its own popup
surfaces independent of the parent window — exactly what we want for
production embed.

**Caveats:**

- Tested under x64 emulation on Windows 11 ARM64. The local Reactor
  NuGet cache contains only x64 binaries. ARM64-native is a residual
  Phase 0b item.
- Tested against a WinForms placeholder, not a real VSIX with WPF
  `HwndHost`. WPF's airspace behavior is different from WinForms' Panel
  and may surface issues the spike couldn't observe.
- Tab traversal, IME, accelerators (Ctrl+S etc.), UIA tree visibility,
  multi-monitor DPI, and full VS dock-cycle stress were not exercised.
  See §15 Phase 0b for the remaining matrix.

**Lifetime:** this spike will not be merged. It is a one-shot artifact
documenting the feasibility check. The eventual VSIX (`src/vs-reactor/`)
and Reactor `--embed` flag will be implemented from scratch per this
spec.
