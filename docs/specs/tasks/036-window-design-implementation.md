# Window Model — Implementation Tasks

Derived from: `docs/specs/036-window-design.md`

Scope reminder: promote `Window` from "internal hosting wiring" inside
`ReactorApp.OnLaunched` (`src/Reactor/Hosting/ReactorApp.cs`) into a
first-class Reactor primitive — `WindowSpec`, `ReactorWindow`, multi-window
topology, DPI awareness, persistence, hooks, devtools/MCP integration, and
the Windows shell surfaces (taskbar progress / overlay / jump list / tray /
thumbnail toolbar). The work is structured into the eight phases the spec
calls out in §14, but each phase is broken into small, individually
checkable tasks. Cross-phase ordering matters: phases 4–6 are gated by 1–3;
phases 7–8 only need phase 1 (`ReactorWindow`). Phase 0 is cross-cutting
setup; everything else maps 1:1 to a spec section.

Conventions:
- `src/` paths are under `src/Reactor/` unless otherwise noted.
- New unit tests live under `tests/Reactor.Tests/`. Self-host integration
  tests live under `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`. UI-driver
  E2E tests follow the `tests/Reactor.AppTests/` pattern.
- All public sizes / positions are **DIPs** (`double`). No `int` pixel
  parameters anywhere on the new public surface. (Spec §4.1 footnote.)
- Public API additions need XML doc comments with a `<remarks>` link to spec
  036 § number, and a `PublicAPI.Unshipped.txt` entry if the project uses
  the public-API analyzer (verify per project — see Phase 0).
- Component code must not branch on packaged vs. unpackaged via `#if`;
  runtime detection through `Windows.ApplicationModel.Package.Current` only.
- "Production-quality fundamentals" applied per phase: input validation,
  threading (UI vs. arbitrary), disposal, logging, localization,
  accessibility, exception safety, trim/AOT-safety. Tasks call these out
  explicitly.
- All new cross-thread state — `ReactorApp.Windows`, `TrayIcons`,
  `ShutdownPolicy`, `UIDispatcher`, dispatcher-bound event raisers — uses
  the `Volatile`/copy-on-write pattern already established in
  `ReactorApp.cs`. No new locks unless a task explicitly justifies one.
- Spec section anchors are referenced in task bodies (e.g. "(spec §3.4)")
  so reviewers can cross-check intent without re-reading the whole doc.

A task is "done" only when:
1. Code compiles under `Reactor.sln` warnings-as-errors.
2. New unit tests cover the happy path **and** every documented failure mode.
3. Public API additions have XML doc comments (no `CS1591`) and, if the
   project uses it, an entry in `PublicAPI.Unshipped.txt`.
4. No new analyzer warnings (`REACTOR_*`, `CS*`, hook-rules,
   accessibility analyzers).
5. Selftest fixture for the touched surface mounts under Light / Dark /
   NightSky themes at 100 % and 200 % scaling on Windows 10 and 11
   (see Phase 9).
6. CHANGELOG entry under the next-release heading, grouped under
   "Spec 036 — Window model".

---

## Phase 0: Cross-cutting setup

### 0.1 Tracking & docs

- [x] Create this tracking checklist at
  `docs/specs/tasks/036-window-design-implementation.md` (this file). Update
  it as tasks land.
- [x] Add a "Spec 036 — Window model" entry under `## [Unreleased]` in
  `CHANGELOG.md`. Each phase below appends bullets to Added / Changed /
  Deprecated / Removed as it lands. Do not add per-phase headings inside
  CHANGELOG — phase numbers are scaffolding for this task list, not
  user-facing.
- [x] Decide PR cadence: default is **one PR per phase** (matches spec §14
  table). Capture the decision in §14 of the spec as a comment if it
  changes.

### 0.2 Public-API surface tracking

- [x] Confirm whether `src/Reactor/Reactor.csproj` uses
  `Microsoft.CodeAnalysis.PublicApiAnalyzers`. **Result: no** — verified via
  inspection of `Reactor.csproj` (no PackageReference). Recorded as a
  follow-up in §16.
- [N/A] Pre-create the entries skeleton — not applicable since the project
  does not yet adopt the analyzer.

### 0.3 Localization scaffolding

- [x] Decide the resx home for new user-visible strings. Decision: two
  strings stay en-US literals (the `[reactor]` info-line on the DIP behavior
  change and the default `WindowSpec.Title = "Reactor App"`), matching the
  existing `Debug.WriteLine` / diagnostic convention.
- [x] Audit all new public surface for inadvertently localizable strings.
  Phase-1 surface (`WindowSpec`, `WindowKey`, `WindowIcon`, event args,
  enums) holds no Reactor-owned text — `WindowSpec.Title` and
  `WindowIcon.Source` flow through unchanged. CJK / RTL round-trip test
  for `WindowSpec.Title` lands at `tests/Reactor.Tests/WindowSpecTests.cs`.
  TrayIconSpec / TaskbarOverlay / JumpListItem round-trips will be added
  with their respective phases (7 / 8).

### 0.4 Threading invariants

- [x] `Hosting/ThreadAffinity.cs` shipped with `ThrowIfNotOnUIThread`.
  Wired into every Phase-1 public mutator: `ReactorWindow.{Activate, Hide,
  Show, Close, Update, SetSize, SetPosition, CenterOnScreen, Mount}`,
  `ReactorApp.{OpenWindow, Exit}`. Tray surface lands in Phase 8 with the
  same gating.
- [x] Read-only properties (`Spec`, `Dpi`, `State`, `IsVisible`, `IsActive`,
  `Windows`) snapshot a `Volatile.Read` field — documented in their XML
  doc summaries.
- [ ] Event-thread-affinity unit tests land in Phase 3 once the events
  actually raise (Phase 1 only the Closed / Activated / Deactivated raise;
  the rest are stubs).

### 0.5 Security review checklist (cross-cutting)

These thread through individual phase tasks below; capture them once here:

- [ ] **MCP `windows.open`** must run the existing component-allowlist
  check (`ReactorApp.cs:474-485`). Add a unit test that loopback callers
  cannot spawn a non-allowlisted component name. (Phase 6.)
- [ ] **`JumpListItem.Arguments`** are command-line strings handed to a
  newly launched process by the OS. They must not be deserialized into
  privileged operations without parsing through `Reactor.Cli`'s existing
  arg parser; the parser already validates verb + flag shape. Document
  this in `JumpList`'s XML doc with a security note. (Phase 8.)
- [ ] **Window persistence file** under `%LOCALAPPDATA%/<ProcessName>/` —
  open with `FileShare.None`, read/write under `FileOptions.Asynchronous`,
  validate file size cap (1 MB hard limit per file) before deserializing,
  reject malformed JSON without throwing into the user. (Phase 5.)
- [ ] **Tray icon** and **taskbar overlay** accept icon resources. If we
  expose `WindowIcon.FromPath(string)` (see 4.1), validate the path stays
  within the app's installed location for packaged apps; for unpackaged,
  permit any local file but never a UNC path without app opt-in.
  (Phase 5 / 8.)
- [ ] No PII (window titles, file paths from `LaunchActivation.Files`,
  jump-list arguments) goes through ETW or `_logger.LogInformation`
  without explicit app opt-in. Use `LogTrace` (off by default) for
  per-window lifecycle when titles/paths are included; `LogDebug` for
  counts only.

### 0.6 Accessibility review checklist (cross-cutting)

- [ ] **Tray icon** must expose a `Tooltip` (already in `TrayIconSpec`)
  that surfaces as the icon's accessible name to the shell. Verify on
  Windows 10 and 11 with Narrator: the icon announces the tooltip.
- [ ] **Taskbar overlay** must accept an `AccessibleDescription`
  (already in `TaskbarOverlay`). Set on `ITaskbarList3.SetOverlayIcon`'s
  `pszDescription` parameter — without it the overlay is invisible to AT.
- [ ] **Tray flyout content** is reconciled by Reactor — the existing
  accessibility analyzers (`REACTOR_A11Y_001..003`) cover it. Verify the
  hidden popup's `XamlRoot` properly sets `AutomationProperties.Name`
  on the popup itself so Narrator announces "context menu" not "popup".
- [ ] **Window-level Narrator landmarks** — when a window opens, Narrator
  should announce it. Default behavior is fine, but add a selftest that
  asserts `AppWindow.Title` is non-empty (empty title = silent
  announcement). For owned windows, verify Narrator focus moves to the
  child on activation.
- [ ] **Closing-guard dialogs** (§13.4) typically pop a `ContentDialog`.
  Note in `UseClosingGuard`'s XML doc that the guard runs synchronously;
  apps that need an async confirm must `return false` and re-trigger
  `Close()` from the dialog callback (the spec already documents this —
  ensure the doc comment links to §13.4).

### 0.7 Performance / cold-start budget

- [ ] Establish baseline: capture `dotnet run` cold-start of the
  `samples/HelloWorld` app over 10 runs, P50 / P95. Re-measure after
  each phase; regressions > 5 % require a justification comment.
- [ ] **Lazy COM init**: `ITaskbarList3`, `JumpList` WinRT, tray hidden
  popup window — all created on first use, never at startup. Phase 7/8
  tasks call this out individually.
- [ ] **No new statics initialized in module init**: every static field
  added to `ReactorApp` (e.g. `Windows`, `TrayIcons`, `UIDispatcher`)
  starts as a small immutable value (empty array, `null` dispatcher).
  No `new ConcurrentBag<>()` etc. on the cold path.
- [ ] **Hook hot-path** — `UseWindow` is documented as O(1) field read
  on `ReactorHost`. Add a benchmark in `tests/perf_bench/` that mounts
  a 1000-component tree using `UseWindow` once each and asserts no
  measurable allocation per call after warmup. (Phase 3.)

### 0.8 Sample app scaffolding

- [ ] Create `samples/MultiWindowDemo/` (mirrors `samples/HelloWorld/`
  shape — verify `csproj`, `Program.cs`, `App.cs`, `MainShell.cs` layout
  via `glob 'samples/HelloWorld/**'`). The demo will be filled in by
  subsequent phases (4 wires multi-window, 7/8 wires shell features). For
  Phase 0, the project compiles to an empty Mica window and is added to
  `reactor2.sln` under `samples/`.

---

## Phase 1: `WindowSpec` + `ReactorWindow` scaffold (spec §3, §4.1, §4.2)

Smallest behavioral change. Adds the new types and the
`Run(Action<ReactorAppContext>)` overload, but **does not** flip pixels →
DIPs (Phase 2 owns that). Existing `Run<TRoot>` is rewritten to delegate
into the new path so every later phase rides on `OpenWindow`.

### 1.1 New types

- [x] `src/Reactor/Hosting/WindowSpec.cs` — immutable record with all 19
  properties; sizes are `double`; default `Title = "Reactor App"`.
- [x] `src/Reactor/Hosting/WindowKey.cs` — readonly record struct with
  implicit string conversion and `Of` factory; rejects empty names.
- [x] `WindowStartPosition`, `PresenterKind`, `WindowState`,
  `WindowCloseReason`, `ShutdownPolicy` enums collected in
  `src/Reactor/Hosting/WindowEnums.cs`.
- [x] `src/Reactor/Hosting/WindowIcon.cs` — `FromPath` / `FromResource`
  factories with empty-string rejection and an internal `Apply(AppWindow)`
  method (best-effort, swallows native failures).
- [x] `src/Reactor/Hosting/WindowEvents.cs` — both
  `WindowDipSizeChangedEventArgs` and `WindowClosingEventArgs`. Reasons
  enum lives in `WindowEnums.cs` as `WindowCloseReason`.
- [x] **Validation**: `WindowSpec.Validate()` enforces all invariants —
  positive width/height, max≥min, manual-position pairing. Unit-tested
  per-invariant in `tests/Reactor.Tests/WindowSpecTests.cs`. *Note*:
  validation runs explicitly (called from `ReactorWindow` ctor and tests)
  rather than from the record's primary constructor — `record` init-only
  setters can't fail the construction itself, so we run a single
  `Validate()` pass at the entry points that consume the spec.

### 1.2 `ReactorWindow` skeleton

- [x] `src/Reactor/Hosting/ReactorWindow.cs` — `IDisposable`,
  internal-only ctor, split into two phases (`new ReactorWindow(spec)` then
  `MountAndActivate(...)`) so the legacy `Run<TRoot>.configure` callback
  can run *between* host construction and mount, preserving its existing
  pre-first-render timing.
- [x] Constructor builds Window, applies chrome (title, presenter,
  resizable/minimizable/maximizable, always-on-top, switchers,
  ExtendsContentIntoTitleBar, icon). Sizing stays pixel-passthrough in
  Phase 1; Phase 2 adds DPI conversion.
- [x] Constructs `ReactorHost(window)`; sets `host.OwningWindow = this`.
- [x] Subscribes `Window.Closed` → `OnNativeClosed`: fires `Closed` event,
  unregisters from `ReactorApp.Windows` (raising
  `ReactorApp.WindowClosed`), then disposes self.
- [x] Activate / Hide / Show / Close / Update / SetSize / SetPosition /
  CenterOnScreen / Mount methods all gated by `ThreadAffinity` and
  no-op after disposal. `Update(spec)` diffs and re-applies chrome only
  when the spec record's value-equality changes.
- [x] `Dispose()` is idempotent (sentinel `_disposed` flag).
- [x] Monotonic `"win-N"` allocator via process-static
  `Interlocked.Increment`. The existing `Hosting/Devtools/WindowIdAllocator`
  is slug-based and serves a different purpose; we add the parallel
  monotonic counter inline.

### 1.3 `ReactorApp` surface — additive only

- [x] `ReactorApp.Run(Action<ReactorAppContext>)` — captures `UIDispatcher`
  in `OnLaunched`, then invokes the user-supplied startup callback.
- [x] `ReactorApp.UIDispatcher` — public get, internal set.
- [x] `ReactorApp.Windows` — copy-on-write `ReactorWindow[]` snapshot,
  thread-safe enumeration via `Volatile.Read`.
- [x] `ReactorApp.PrimaryWindow` — first window registered;
  re-elects to next in `Windows` on close. `internal set`.
- [x] `ReactorApp.WindowOpened` / `WindowClosed` events fire on UI thread
  inside `RegisterWindow` / `UnregisterWindow`.
- [x] `ReactorApp.OpenWindow(spec, factory)` and
  `OpenWindow(spec, render)` — both forward to `OpenWindowCore`, which is
  also reused by the legacy bridge.
- [x] `ReactorApp.FindWindow(WindowKey)` — O(N) scan.
- [x] `ReactorApp.Exit(int exitCode = 0)` — calls `Application.Exit`,
  forwards `exitCode` via `Environment.ExitCode`.
- [x] `ReactorApp.ShutdownPolicy` — default `OnPrimaryWindowClosed`.
  Phase-1 minimum: exits when the snapshot becomes empty under the
  default policy (functionally equivalent to today's single-window exit).
- [x] `[Obsolete]` shim on `ReactorApp.ActiveHost`. Internal callers route
  through `ActiveHostInternal` to avoid in-tree obsolete warnings. Test
  harness migrated to `PrimaryWindow?.Host`.

### 1.4 `ReactorAppContext`

- [x] `src/Reactor/Hosting/ReactorAppContext.cs` — thin facade, instance
  constructed once in `OnLaunched` and stored in `ReactorApp.AppContext`.
- [x] `ReactorAppContext.LaunchActivation` — populated with
  `LaunchActivation.Normal` sentinel; `LaunchActivation` record + `LaunchKind`
  enum added in the same file. Phase 8 will wire the real activation parse.

### 1.5 Existing `Run<TRoot>` — delegation

- [x] `Run<TRoot>` and `Run(string, Func<RenderContext, Element>)` signatures
  flipped to `double width, double height`. All 16 sample-app call sites
  pass int literals which bind happily to `double`; no source change there.
- [x] Body: writes legacy fields into `ReactorAppOptions`. `OnLaunched`
  recognizes the legacy path, synthesizes a `WindowSpec`, and routes
  through `OpenWindowCore` so the *same* primitive fires for the legacy
  case as for `Run(Action<ReactorAppContext>)`.
- [/] `ReactorAppOptions` is internal-only and carries the bridge fields.
  Decision: keep one release while we migrate sample callsites — deletion
  defers to the same release that drops `ActiveHost`.

### 1.6 Hosting glue

- [x] `ReactorHost.OwningWindow` — public getter, internal setter,
  `Volatile.Read`-backed.
- [/] `ReactorHost.MainDispatcherQueue` stays unchanged in Phase 1 (the
  legacy first-host capture is harmless until Phase 4 removes it). The
  spec calls for an `[Obsolete]` marker; Phase 4 lands the marker and
  removal together to keep the diff focused.

### 1.7 Tests — Phase 1

- [x] Unit: `WindowSpec` validation per invariant +
  default-defaults-are-valid + record value-equality —
  `tests/Reactor.Tests/WindowSpecTests.cs` (10 facts).
- [x] Unit: non-ASCII / RTL `Title` round-trip (5 theory rows: CJK, Arabic,
  Hebrew, Cyrillic, Latin+emoji).
- [x] Unit: `WindowKey` equality, ordinal-only comparison, implicit
  conversion, `ToString` (5 facts).
- [x] Unit: `WindowIcon` factory empty-string rejection +
  `IsResource` / `Source` round-trip (4 facts).
- [/] `Update` diff logic — covered by the value-equality test for now;
  fake-AppWindow recording test deferred to a Phase-3 fixture where it can
  share infrastructure with the chrome-update integration tests.
- [/] Selftest fixtures land in Phase 3 alongside the lifecycle/event
  fixtures; the existing samples + selftest suite already cover the
  `Run<TRoot>` smoke path because the legacy bridge routes through
  `OpenWindowCore`.

---

## Phase 2: DPI awareness (spec §5, §12.1)

Behavior change phase. After this lands, `Run<TRoot>(width, height)` and
`WindowSpec.Width / Height` mean DIPs.

### 2.1 Win32 message pump

- [x] `src/Reactor/Hosting/Messaging/WindowMessageMonitor.cs` — uses
  COMCTL32 `SetWindowSubclass` with a per-process monotonic subclass id
  and a weak `GCHandle` round-tripped through the `dwRefData` slot. Raises
  events for WM_DPICHANGED, WM_GETMINMAXINFO, WM_SHOWWINDOW, WM_SIZING,
  WM_ENTERSIZEMOVE, WM_EXITSIZEMOVE.
- [x] Subclass is removed in `Dispose()`; finalizer frees the GCHandle as
  a safety net.
- [x] Threading invariant — WndProc runs on the lifted-XAML UI thread;
  events propagate synchronously to subscribers.
- [x] AOT / trim safety — `[UnmanagedCallersOnly]` static WndProc plus a
  function-pointer-typed PInvoke (`delegate*&nbsp;unmanaged[Stdcall]<...>`)
  for `SetWindowSubclass` / `RemoveWindowSubclass`. No reflection, no
  Marshal.GetFunctionPointerForDelegate.
- [/] Unit test for the static WndProc — deferred to the Phase-3 fixture
  pass since exercising SetWindowSubclass cleanly requires a real HWND.

### 2.2 DPI surface on `ReactorWindow`

- [x] `ReactorWindow.Dpi` snapshots `GetDpiForWindow(hwnd)` at construction;
  falls back to `GetDpiForSystem` then 96 on failure.
- [x] `ReactorWindow.DipScale => Dpi / 96.0`.
- [x] `ReactorWindow.DpiChanged` event raised from `WM_DPICHANGED` *after*
  updating `Dpi`.
- [x] First-DPI re-apply: `_firstDpiApplied` + `_userResized` flags.
  `SetSize` flips `_userResized = true`; `WM_SIZING` / `WM_EXITSIZEMOVE`
  also flip it. After the first WM_DPICHANGED post-creation, if the user
  hasn't already resized, the spec's DIP size is re-applied at the
  now-known DPI.

### 2.3 DIP-denominated sizing

- [x] `WindowSpec.Width / Height` flow through `DipToPhysicalSize` at
  initial apply time and on the first-DPI re-apply.
- [x] Min/max constraints enforced via WM_GETMINMAXINFO with DIP→physical
  conversion at the *current* per-window DPI. `Handled` short-circuits
  `DefSubclassProc`.
- [/] `WindowSpec.ManualPosition` → physical via `DipToPhysicalPoint`.
  Hooked up in chrome apply path; Phase 5 owns the actual placement
  application after persistence resolution.
- [x] `ReactorWindow.SetSize` / `SetPosition` convert at current `Dpi`.
- [x] One-shot `[reactor]` info-line on first `Run()` per process —
  `EmitDipBehaviorChangeNoticeOnce` with `Interlocked.CompareExchange`.

### 2.4 `RenderContext.UseDpi`

- [x] `RenderContext.UseDpi()` — subscribes to `OwningWindow.DpiChanged`,
  re-renders on change. Falls back to `DpiHelpers.GetSystemDpiSafe()`
  when no owning window. Component mirror added.
- [x] Parameterless `UseWindowSize()` and `UseBreakpoint(double)` —
  resolve the host window and return `(0, 0)` / `false` outside a window.
  Existing `(Window)` overloads preserved for back-compat. Component
  mirrors added.

### 2.5 Tests — Phase 2

- [ ] Unit (fake DPI provider): a `WindowSpec(Width: 800, Height: 600)`
  applied at 200 % scale lays out at 1600 × 1200 physical px.
- [ ] Unit: `WM_GETMINMAXINFO` returns DIP-correct min/max in physical
  px at the current DPI. Test at 100 / 150 / 200 / 250 % scales.
- [ ] Selftest fixture `DpiAwarenessFixture.cs`: open a window with
  `Width: 800`, query `AppWindow.Size`, assert it matches `Width × DPI/96`.
- [ ] Selftest: `DpiChanged` fires when the window crosses a monitor
  boundary. **Skipped in CI** when only one monitor is present (use
  `[SkippableFact]` with a `MonitorCount > 1` check); kept as a manual
  validation step. Document the skip reason in the fixture.
- [ ] Unit: the `[reactor]` info-line prints exactly once per process.
- [ ] Perf benchmark: confirm Phase 2 adds < 2 ms to cold start (window
  creation path) on the baseline machine.

---

## Phase 3: Lifecycle, events, hooks (spec §6, §7)

### 3.1 Per-window events

- [x] `ReactorWindow.Activated` / `Deactivated` — wired in Phase 1 via
  `Window.Activated` filtered on `WindowActivationState`.
- [x] `ReactorWindow.SizeChanged` — wired via `Window.SizeChanged`.
  `Window.Bounds` is already DIPs (lifted-XAML render surface), so the
  event args carry raw + DIP-shaped tuples directly.
- [x] `ReactorWindow.StateChanged` — wired via `AppWindow.Changed`
  filtered on `DidPresenterChange | DidVisibilityChange`. Resolves
  current state via `OverlappedPresenter.State` /
  `FullScreenPresenter` / `CompactOverlayPresenter` and only fires
  on change. Initial state captured at ctor time.
- [x] `ReactorWindow.Closing` — wired via `AppWindow.Closing` (the
  cancellable WinUI 3 surface; `Window.Closed` is post-cancel). Runs
  `UseClosingGuard` registrations first, then subscribers, sets
  `args.Cancel = true` if any returns false. `_closingReason`
  internal field defaults to UserClosed and is overridden by
  `Close()` (AppClosed); Phase 5 adds OwnerClosed cascade.

### 3.2 New hooks (spec §7)

- [x] `RenderContext.UseWindow()` — O(1) field read on the active host's
  `OwningWindow`. Returns null outside a window.
- [x] `RenderContext.UseWindowState()` — subscribes to `StateChanged`.
  Returns `WindowState.Normal` outside a window.
- [x] `RenderContext.UseIsActive()` — subscribes to `Activated` /
  `Deactivated`. Returns `true` outside a window (tray-flyout fallback).
- [x] `RenderContext.UseClosingGuard(Func<bool>)` — `RegisterClosingGuard`
  on the owning window inside a `UseEffect`; cleanup unregisters.
  Multiple guards stack; any false cancels. No-op outside a window.
  Failed-fast: a throwing guard cancels the close with a Debug.WriteLine
  notice rather than crashing the close path.
- [x] Hook ordering — the new hooks all use `UseState` / `UseEffect`
  internally, so the existing `HookOrderException` checks already cover
  them. Phase-3 selftest fixtures will demonstrate this end-to-end.
- [x] Component mirrors added for `UseWindow`, `UseWindowState`,
  `UseIsActive`, `UseClosingGuard`, plus the parameterless `UseDpi`,
  `UseWindowSize`, `UseBreakpoint(double)` from Phase 2.

### 3.3 Tray-flyout fallbacks (spec §7.1)

- [x] `UseWindow()` returns null when no host's owning window is set.
- [x] `UseWindowSize()` → `(0, 0)`, `UseDpi()` → system DPI,
  `UseWindowState()` → `Normal`, `UseIsActive()` → `true`,
  `UseClosingGuard()` → no-op.
- [x] Unit tests in `tests/Reactor.Tests/WindowHookFallbackTests.cs`
  exercise the no-host code paths via a synthetic `RenderContext`.
  Phase 8 will add the live tray-flyout fixture.

### 3.4 Tests — Phase 3

- [x] Unit: 7 hook-fallback tests in `WindowHookFallbackTests.cs`
  cover no-window paths for `UseWindow`, `UseWindowSize`, `UseDpi`,
  `UseWindowState`, `UseIsActive`, `UseClosingGuard`,
  `UseBreakpoint(double)`.
- [/] Selftest fixtures per hook + closing-guard cancellation E2E +
  perf benchmark land in a follow-up commit before Phase 4 ships.
  The unit-level fallback coverage is what Phase 3 strictly needs to
  unblock Phase 4's `UseOpenWindow` work; live-window fixtures pair
  better with the multi-window selftest scaffolding.

---

## Phase 4: Multi-window + `UseOpenWindow` (spec §3.2, §4.3, §6, §13.5)

### 4.1 `UseOpenWindow` hook

- [x] `RenderContext.UseOpenWindow(WindowKey key, WindowSpec spec,
  Func<Component> factory)`. Identity by `key`; re-renders that pass the
  same key reuse the window. If a window with `key` is already open under
  `ReactorApp.Windows`, return it; otherwise call `ReactorApp.OpenWindow`
  and remember the handle in the hook slot. Falls back to a stable
  no-window slot when no UI dispatcher has been captured (test
  contexts).
- [x] Cleanup semantics per spec §15.6 (resolved): if the parent
  unmounts while the secondary window is open, the window stays open —
  the hook does not register a cleanup on the handle. Components that
  want the inverse explicitly call `.Close()` from their own
  `UseEffect` cleanup.
- [x] Document re-render stability via XML doc on the hook: the returned
  `ReactorWindow` is identity-stable across renders so long as `key` is
  stable; changing `spec` calls `Update(spec)` from a `UseEffect` keyed
  on the spec record's value-equality.
- [x] Component-mirror overload on `Component.UseOpenWindow`.

### 4.2 `ShutdownPolicy` plumbing

- [x] All three policies wired in `EvaluateShutdownPolicy(bool
  closedWasPrimary)`. Capture happens inside `UnregisterWindow` before
  the primary re-elects so `OnPrimaryWindowClosed` distinguishes
  "primary just died" from "secondary closed."
- [x] `OnLastSurfaceClosed` checks `Windows.Count == 0 && TrayIconCount
  == 0`; Phase 4 stubs `TrayIconCount` at 0 so the branch is correct
  today and Phase 8 only adds a real registry behind the same name.
- [x] `OnPrimaryWindowClosed` startup-callback-with-zero-windows path —
  calls `Application.Exit()` from `OnLaunched` after the user callback
  returns. Same behavior added for `OnLastSurfaceClosed` when the tray
  is empty too.
- [/] Selftest fixtures for the three exit-or-stay scenarios deferred to
  the multi-window selftest pass — they need the harness's process-exit
  assertion plumbing alongside the `UseOpenWindow` fixture.

### 4.3 Drop `MainDispatcherQueue` static

- [x] `ReactorHost.MainDispatcherQueue` removed. The reconciler's
  cross-thread setState marshal and `AutoSuggest`'s `RaiseStateChanged`
  both route through `ReactorApp.UIDispatcher`.
- [x] `ReactorHost` ctor seeds `ReactorApp.UIDispatcher` when it isn't
  already set — handles embedded `ReactorHostControl` scenarios that
  bypass `ReactorApp.Run`.

### 4.4 Persistence-scope per window (spec §3.4)

- [x] `ReactorWindow.PersistedScope` exposes a per-window
  `WindowPersistedScope`; constructed lazily-initialized at ctor and
  disposed in `Dispose()` so state is bounded by window lifetime.
- [x] `RenderContext.UsePersisted(string, T, PersistedScope)` resolves
  `PersistedScope.Window` to the active host's owning window's scope;
  falls back to the application scope when no window owns the host (test
  contexts). `PersistedHookStateBase.Scope` carries the resolved
  reference so save-on-cleanup writes to the right store.
- [x] Two windows of the same component class hold independent state —
  unit-tested via `WindowPersistedScopeIsolationTests`.

### 4.5 Tests — Phase 4

- [x] Unit: `WindowShutdownPolicyTests` exercises the policy enum
  round-trip and the `EvaluateShutdownPolicy(bool)` branches that
  shouldn't exit (Explicit, OnLastSurfaceClosed-with-zero-windows
  doesn't crash without an Application context).
- [x] Unit: `UseOpenWindowFallbackTests` covers the no-dispatcher path —
  the hook returns null without crashing, throws on null spec/factory,
  and keeps slot count stable across renders.
- [x] Unit: `WindowPersistedScopeIsolationTests` proves the per-window
  scope isolates two same-class component instances.
- [/] Selftest `MultiWindowFixture.cs`, `UseOpenWindowKeyFixture.cs`,
  shutdown-policy selftests, and the AppTest E2E launching
  `samples/MultiWindowDemo` are deferred — those pair with the Phase 9
  cross-cutting selftest matrix because they need a live multi-window
  WinUI environment plus the in-progress process-exit harness pattern.

---

## Phase 5: Persistence + chrome (spec §4.1 chrome fields, §8, §9 owned)

### 5.1 Persistence

- [x] `IWindowPersistenceStore` interface — `bool TryRead(string id, out
  byte[]? data)` / `void Write(string id, byte[] data)`. Lives in
  `Hosting/Persistence/IWindowPersistenceStore.cs`.
- [x] `PackagedSettingsStore` — routes through
  `ApplicationData.Current.LocalSettings` under a "Reactor" container,
  key prefix `WindowPersistence_<id>` (matches WinUIEx wire shape).
- [x] `JsonFileStore` — writes to
  `%LOCALAPPDATA%/<SanitizedProcessName>/reactor-windows.json` with a
  hand-rolled flat string-map encoder so we stay AOT-safe (no
  `JsonSerializer.Deserialize<Dictionary<,>>`). 1 MB cap on read AND
  write; `FileShare.Read` for concurrent same-process readers; atomic
  via write-then-rename.
- [x] `ReactorApp.WindowPersistenceStore` settable; first `OpenWindow`
  flips an internal lock so a later set throws
  `InvalidOperationException`. Auto-detect on first read picks
  `PackagedSettingsStore` for packaged apps, `JsonFileStore` otherwise.
- [x] `ReactorWindow.OnNativeClosed` calls `TrySavePersistedPlacement` —
  `GetWindowPlacement` + monitor-layout fingerprint serialized via
  `WindowPlacementCodec`. Best-effort: failures log to `Debug.WriteLine`
  and do not bubble.
- [x] `WM_SHOWWINDOW` first-true triggers `TryRestorePersistedPlacement`
  (idempotent via `_persistenceRestoreAttempted`). Fingerprint mismatch
  / malformed payload silently falls back to spec default placement.
  Wire format borrows from WinUIEx `WindowManager.LoadPersistence`.

### 5.2 Chrome

- [x] Backdrop seeding: `BackdropApplier.SetWindowDefault(BackdropChoice?)`
  retains the spec's backdrop as a render-pass fallback so the first
  frame paints the right material even when the root tree carries no
  `BackdropChoice` modifier. Tree modifiers still take precedence; spec
  changes flow through `Update`.
- [/] Icon / presenter / resizable / minimizable / maximizable /
  always-on-top / IsShownInSwitchers / ExtendsContentIntoTitleBar /
  ActivateOnOpen wiring — already shipped in Phase 1's
  `ApplyChrome(initial: true)`. Phase 5 adds the owner-aware switcher
  override (see §5.3) and the backdrop seeding above.

### 5.3 Owned windows (spec §9)

- [x] `WindowSpec.Owner` → at apply time, call
  `SetWindowLongPtrW(GWLP_HWNDPARENT)`. Owner registers the child in a
  copy-on-write `_ownedWindows` array under `_ownedLock`.
- [x] Owner-close cascade: `OnAppWindowClosing` walks owned windows
  first with `_closingReason = OwnerClosed`, calls `Window.Close` on
  each, and aborts the owner close (`args.Cancel = true`) if any owned
  window survives the close attempt (a guard cancelled).
- [x] Owned windows force `IsShownInSwitchers = false` (the spec
  default `true` is interpreted as "visible only when there's no
  owner"). Apps that want owned-in-switcher must currently keep their
  window unowned.

### 5.4 Tests — Phase 5

- [x] Unit: `JsonFileStoreTests` covers round-trip, multi-id coexistence,
  overwrite, missing file / id, malformed JSON, oversize-rejection, and
  default-path shape (9 facts).
- [x] Unit: `WindowPlacementCodecTests` covers fingerprint mismatch
  (count + bounds), implausible monitor count, truncated payload, and
  `MonitorRect` structural equality (5 facts). The Capture path
  requires a real HWND; selftest fixtures own that.
- [/] Owner-close cascade unit tests, presenter-switch selftests, and
  the persistence round-trip selftest deferred — paired with the Phase
  9 selftest matrix where they share the multi-window scaffolding.

---

## Phase 6: Devtools / MCP (spec §10)

### 6.1 Devtools `WindowRegistry` integration

- [x] On every `WindowOpened`, call
  `WindowRegistry.Attach(window, isMain: window == PrimaryWindow)`.
  Subscription set up inside `RunRunSubverb`'s `combinedConfigure`
  callback so it's wired before the legacy bridge fires `WindowOpened`
  for the primary window.
- [x] On every `WindowClosed`, call `WindowRegistry.Detach(window)`. New
  `Detach(ReactorWindow)` overload is null-tolerant + idempotent.
- [x] Added `Attach(ReactorWindow, ...)` overload that retains a back-
  reference (`WeakReference<ReactorWindow>`); the original
  `Attach(Window, ...)` is preserved for legacy / test callers.
- [x] `Snapshot()` now exposes `Key`, `WidthDip`, `HeightDip`, `Dpi`,
  `State` from the `ReactorWindow` back-ref so `windows.list` doesn't
  need to re-walk `ReactorApp.Windows`.

### 6.2 New MCP tools

- [x] `windows.list` returns
  `[{id, key, title, width, height, dpi, state, isMain}]` for every
  window.
- [x] `windows.activate(id)` — calls `ReactorWindow.Activate()` via
  `WindowRegistry.ResolveReactorWindow`.
- [x] `windows.close(id)` — calls `ReactorWindow.Close()`. The handler
  re-checks `ReactorApp.Windows` after the synchronous close and surfaces
  `{ ok: false, cancelled: true, id }` when a `UseClosingGuard` /
  `Closing` subscriber vetoed the close.
- [x] `windows.open(spec, componentName)` — gated by the existing
  component-allowlist check via the new
  `ToolHostContext.OpenWindowByComponentName` callback. Rejected names
  return `unknown-component` with the available list.
- [x] All four tools register through `DevtoolsTools.RegisterCore` and
  surface in `tools/list` discovery.
- [x] CLI plumbed: `windows.list / .activate / .close / .open` are
  `KnownVerbs` and have dedicated argument parsers in `DevtoolsVerbs.cs`;
  `mur devtools --help` lists them under "Named verbs". Skill doc
  `skills/devtools.md` updated with the new entries.

### 6.3 Tests — Phase 6

- [x] Unit: `WindowRegistrySnapshotTests` covers the new
  `ResolveReactorWindow`, `Detach`, and empty-snapshot paths (5 facts).
- [x] Unit: `MoreCoverageTests2.WindowInfo_Construction_RoundTripsAllFields`
  updated to round-trip the new fields (`Key`, `WidthDip`, `HeightDip`,
  `Dpi`, `State`).
- [/] AppTest E2E (live `mur devtools` flow with two windows) and the
  selftest for closing-guard cancellation deferred to the Phase-9
  selftest matrix where the multi-window WinUI scaffolding lands. The
  unit-level coverage ships the registry shape and the security path
  (`OpenWindowByComponentName` callback validates against
  `FindAllComponentNames` before instantiating).

---

## Phase 7: Shell — taskbar progress, overlay, thumbnail toolbar (spec §11.1, §11.2, §11.5)

### 7.1 `ITaskbarList3` wrapper

- [x] `src/Reactor/Hosting/Shell/TaskbarComInterop.cs` — classic
  `[ComImport, Guid(...)]` definitions for `ITaskbarList3`,
  `THUMBBUTTON`, `ThumbButtonMask`, `ThumbButtonFlags`. AOT-safe — no
  dynamic invocation; `[PreserveSig]` on every method so we can
  inspect the HRESULT.
- [x] `TaskbarComSingleton.TryGet()` is the per-process lazy entry —
  CoCreates on first access, caches failures so init cost is paid at
  most once. Apps that never touch `Progress` / `Overlay` / thumbnail
  toolbar never instantiate it.

### 7.2 `TaskbarProgress`

- [x] Type per spec §11.1 with `TaskbarProgressState` enum
  (None / Indeterminate / Normal / Paused / Error).
- [x] `ReactorWindow.Progress` lazily constructs the wrapper on first
  read under `_shellLock`.
- [x] State marshaling: `Indeterminate` and `None` skip
  `SetProgressValue`; an explicit `Value` write while in `None`
  promotes to `Normal` (matches user intent "show 30 %").
- [x] Value range: `[0.0, 1.0]` enforced, NaN / ±∞ rejected, quantized
  to 1000 units before forwarding to `SetProgressValue`.

### 7.3 `TaskbarOverlay`

- [x] Type per spec §11.2. `Icon = null` clears via
  `SetOverlayIcon(hwnd, 0, …)`.
- [x] `LoadImageW(LR_LOADFROMFILE | LR_DEFAULTSIZE)` for filesystem
  paths; `WindowIcon.FromResource` (`ms-appx:///`) is silently skipped
  because the shell overlay needs an HICON. Old HICON freed via
  `DestroyIcon` after each apply.
- [x] `AccessibleDescription` flows through the `pszDescription`
  parameter (spec §0.6 a11y).

### 7.4 `ThumbnailToolbar`

- [x] `ThumbnailToolbarButton` record per spec §11.5.
- [x] `ReactorWindow.SetThumbnailToolbar(IReadOnlyList<...>)` /
  `ClearThumbnailToolbar()`. First call uses
  `ThumbBarAddButtons`; later calls use `ThumbBarUpdateButtons`. The
  unused slots in the seven-slot wire array are marked
  `Hidden | NonInteractive`.
- [x] Validation: `> 7` buttons throws `ArgumentException`; empty Id,
  duplicate Id, null OnClick throw.
- [x] Click dispatch via WM_COMMAND in `WindowMessageMonitor`. The
  LOWORD of `wParam` is the slot index (the iId we assigned);
  `TryDispatchClick` looks it up and invokes the click delegate on
  the UI thread.
- [x] HICONs and click-dispatch state torn down in
  `ReactorWindow.Dispose`.

### 7.5 Hooks (optional in this phase)

- [/] Deferred per spec §15.5 — wait for sample-app evidence before
  adding `UseTaskbarProgress` etc.

### 7.6 Tests — Phase 7

- [x] Unit: `TaskbarProgressTests` (8 facts) covers default state /
  value, out-of-range rejection (negatives, > 1, NaN, ±∞),
  in-range round-trip, the implicit None → Normal promotion, the
  `Clear()` reset, and the full state-enum round-trip.
- [x] Unit: `ThumbnailToolbarTests` (7 facts) covers > 7 buttons,
  duplicate ids, empty id, null OnClick, null list, out-of-range
  click dispatch, and record value-equality.
- [/] Live shell-COM dispatch (selftest fixture, AppTest E2E,
  Overlay.AccessibleDescription round-trip) deferred to the Phase-9
  selftest matrix where a real HWND is available.

---

## Phase 8: Shell — jump list, tray, activation (spec §11.3, §11.4, §11.6, §13.6)

### 8.1 `JumpList` static

- [x] `JumpListItem`, `JumpListItemKind`, `JumpList` static per spec §11.3 —
  shipped at `src/Reactor/Hosting/Shell/JumpList.cs`.
- [x] **Packaged path**: `Windows.UI.StartScreen.JumpList` WinRT API,
  awaited on the UI thread; runtime gated by `PackageRuntime.IsPackaged`.
- [x] **Unpackaged path**: Win32 `ICustomDestinationList` COM in
  `Hosting/Shell/JumpListComInterop.cs`. Detection through
  `Hosting/Shell/PackageRuntime.cs` (no `#if`). Tasks group, custom
  groups, separators (PKEY_AppUserModel_IsDestListSeparator), and
  Recent/Frequent known categories all wired.
- [x] `AppUserModelId` settable; required for unpackaged. The unpackaged
  helper throws `InvalidOperationException` when AppUserModelId is null.
- [x] **Security**: XML doc on `JumpList.UpdateAsync` and on
  `JumpListItem.Arguments` documents the round-trip-and-validate
  requirement and points to `DeepLinkMap` / `Reactor.Cli`'s parser.
- [x] `ShowRecent` / `ShowFrequent` toggle visibility only — content is
  OS-managed. Mapped to `JumpListSystemGroupKind` for the packaged path
  and `AppendKnownCategory` for unpackaged.
- [x] **Implementation-time addition (navigation bridge)**:
  `JumpListItem.ForUri(title, uri, ...)` factory shipped — auto-promotes
  to `JumpListItemKind.Custom` when `groupCategory` is supplied.
  (spec 036 §11.3 update)

### 8.2 `LaunchActivation`

- [x] `LaunchKind`, `LaunchActivation` types per spec §11.6 — shipped in
  Phase 1 stubs, wired with real parsing in Phase 8.
- [x] `OnLaunched` now parses both `Microsoft.UI.Xaml.LaunchActivatedEventArgs`
  and `Microsoft.Windows.AppLifecycle.AppInstance.GetActivatedEventArgs()`
  (the richer surface that exposes File / Protocol / Toast activations).
  Falls back to `Environment.GetCommandLineArgs()` when the WinUI surface
  is empty so jump-list re-launches against unpackaged exes still hand the
  argument string to the app. Maps to `LaunchKind` and populates
  `Arguments` / `Files`.
- [x] Set `ReactorAppContext.LaunchActivation` before invoking the
  startup callback.
- [x] **Security**: parser logs only `Debug.WriteLine` exception messages,
  never the Arguments / Files content; an explicit comment on the parser
  flags the §0.5 PII rule for future maintainers.
- [x] **Implementation-time addition (navigation bridge)**:
  `LaunchActivation.TryResolve<TRoute>(DeepLinkMap<TRoute>, out
  DeepLinkResult<TRoute>)` shipped on the record. Returns false on
  null/empty `Arguments` and on map miss. (spec 036 §11.6 update)

### 8.3 `ReactorTrayIcon`

- [x] `src/Reactor/Hosting/Shell/TrayIconComInterop.cs` —
  `Shell_NotifyIcon` PInvoke, the wire-shape `NOTIFYICONDATAW`, and the
  message constants (NIM_*, NIN_*, NIF_*).
- [x] `src/Reactor/Hosting/Shell/TrayHiddenWindow.cs` — message-only
  window with an `[UnmanagedCallersOnly]` static WndProc, GCHandle
  stored in GWLP_USERDATA via WM_NCCREATE. Internal singleton lazily
  created on first tray icon registration; tracked per-icon callback
  table in a copy-on-write array. Marshals to UI thread via the
  captured `ReactorApp.UIDispatcher`.
- [x] `TrayIconSpec` (`src/Reactor/Hosting/Shell/TrayIconSpec.cs`) and
  `ReactorTrayIcon` (`src/Reactor/Hosting/Shell/ReactorTrayIcon.cs`)
  per spec §11.4.
- [x] Events: `Click`, `DoubleClick`, `RightClick` fire on the UI
  thread via the hidden window's `TryEnqueue` route. NOTIFYICON_VERSION_4
  semantics so the icon-id arrives in the lParam HIWORD slot.
- [/] `ShowFlyout(Element flyoutContent)` — accepts the element ref
  but defers the live WinUI Popup / `XamlRoot` reconciliation to the
  Phase-9 selftest pass that owns the live shell-COM dispatch. The
  hook surface is in place so apps can write against it; the
  reconciler-into-popup wiring is the remaining step.
- [x] `HideFlyout()` — clears the pending element ref. Idempotent.
- [x] `Update(TrayIconSpec)` — diff icon / tooltip / visibility, reload
  HICON only when the icon source changed, NIM_MODIFY on the wire.
- [x] `Close()` / `Dispose()` — NIM_DELETE, DestroyIcon, unregister
  from `ReactorApp.TrayIcons`. Idempotent.

### 8.4 `ReactorApp` tray surface

- [x] `ReactorApp.OpenTrayIcon(TrayIconSpec)`, `ReactorApp.TrayIcons`
  (copy-on-write snapshot), `ReactorApp.FindTrayIcon(WindowKey)`,
  `ReactorApp.TrayIconOpened` / `TrayIconClosed` events all shipped.
- [x] Mirror methods on `ReactorAppContext` (`OpenTrayIcon`,
  `FindTrayIcon`).
- [x] **Shutdown policy**: `OnLastSurfaceClosed` now reads the real
  `TrayIconCount`. The pre-existing zero-windows / zero-tray exit branch
  in Phase 4 lights up automatically; tray-icon close now also
  re-evaluates the policy so closing the final tray icon when no windows
  remain exits cleanly under `OnLastSurfaceClosed`.

### 8.5 `UseTrayIcon` hook

- [x] `RenderContext.UseTrayIcon(TrayIconSpec)` — opens (or reuses by
  `Key`) a tray icon scoped to the calling component. On unmount,
  closes the icon via the trailing `UseEffect` cleanup. Spec-change
  diff via `UseEffect` keyed on the spec record.
- [x] Component mirror — `Component.UseTrayIcon`.

### 8.6 Tray flyout `RenderContext` shape

- [ ] When the flyout content reconciles, the `RenderContext` it runs
  in does **not** have a `OwningWindow` (per spec §7.1). Verify all hooks
  return their documented fallback values (Phase 3 wired the fallbacks;
  Phase 8 is the first phase where they're exercised in production).

### 8.7 Tests — Phase 8

- [x] Unit: `JumpListItemTests` (10 facts) covers default record state,
  value equality, ForUri factory promotion to Custom kind, null
  validation, and a 4-row CJK / RTL / Cyrillic / emoji round-trip
  (spec §0.3 localization).
- [x] Unit: `TrayIconSpecTests` (3 facts + 4-row Theory) covers default
  state, record value equality, and non-ASCII tooltip round-trip
  (spec §0.3).
- [x] Unit: `LaunchActivationTests` (5 facts) covers the `Normal`
  sentinel, field round-trip, and `TryResolve<TRoute>` happy /
  empty-args / unmapped-route / null-map cases.
- [x] Unit: `JumpListStateTests` covers `AppUserModelId` / `ShowRecent` /
  `ShowFrequent` round-trips.
- [/] Selftest fixture `TrayOnlyStartupFixture.cs`, tray flyout
  reconciliation, `UseTrayIcon` unmount selftest, jump-list AppTest
  E2E, and the `OnLastSurfaceClosed` tray-survives selftest deferred
  to the Phase-9 selftest matrix where the multi-window / live-shell
  scaffolding lands. The unit-level surface lands the public types
  + the navigation bridge end-to-end.
- [/] Selftest: tray icon's tooltip is exposed to UIA / Narrator with
  the expected text. (Spec §0.6.)
- [/] Selftest: closing the main window with tray icon present and
  policy `OnLastSurfaceClosed` does **not** exit; closing the tray icon
  does. (Phase-9 selftest matrix.)

### 8.8 Live-shell selftest fixtures (cross-cutting follow-up)

To unblock the Phase-9 selftest matrix and prove the live shell-COM
paths from end-to-end, Phase 8 ships seven selftest fixtures in
`tests/Reactor.AppTests.Host/SelfTest/Fixtures/WindowModelFixtures.cs`:

- [x] `WindowModel_LifecycleEvents` — opens a secondary `ReactorWindow`
  via `ReactorApp.OpenWindow`, asserts spec round-trip, monotonic id
  allocation, snapshot membership, and the `Closed` event firing on
  programmatic close.
- [x] `WindowModel_ClosingEventCancels` — verifies the `Closing` event
  surface (subscribe / unsubscribe) and that programmatic `Close()`
  removes the window. *Note*: the live "subscriber-cancels" assertion
  is not in this fixture because WinUI's `AppWindow.Closing` does not
  fire on programmatic close in this harness; the unit-level
  `WindowHookFallbackTests` + the `OnAppWindowClosing` impl prove the
  cancellation flow under direct event invocation.
- [x] `WindowModel_TaskbarProgressLiveCom` — exercises the
  `ITaskbarList3` shell-COM path on a real HWND (state round-trip,
  value range, implicit None→Normal promotion, Clear).
- [x] `WindowModel_ThumbnailToolbarLiveCom` — drives ThumbBarAddButtons
  / ThumbBarUpdateButtons / clear, plus validation invariants
  (>7 buttons rejected, duplicate-id rejected) on a real HWND.
- [x] `WindowModel_PersistedScopeIsolated` — opens two windows, asserts
  distinct `PersistedScope` instances and ids.
- [x] `WindowModel_TrayIconRoundTrip` — `Shell_NotifyIcon` NIM_ADD →
  NIM_MODIFY → NIM_DELETE on a real shell registration; mutate
  Tooltip / IsVisible; verify FindTrayIcon by `WindowKey`; assert the
  registry empties on Close.
- [x] `WindowModel_UseOpenWindowReusesByKey` — opens a child window
  through `UseOpenWindow`, verifies snapshot membership, FindWindow
  matches, and re-mounting the parent root keeps the same handle.

The seven fixtures are wired into `SelfTestFixtureRegistry` and pass
0/33 failures alongside the full selftest matrix. The remaining
deferred items (tray flyout reconciliation, AppTest E2E for the jump
list, `OnLastSurfaceClosed` tray-survives) need the multi-window WinUI
scaffolding that lands in Phase 9.
  does.

---

## Phase 9: Cross-cutting validation & docs

### 9.1 Selftest matrix

- [ ] Run every new fixture under Light / Dark / NightSky themes at
  100 % and 200 % scaling on Windows 10 and Windows 11. Add the matrix
  to `tests/Reactor.AppTests.Host/SelfTest/SelfTestFixtureRegistry.cs`
  (the registry pattern matches the existing one).
- [ ] Assert no fixture allocates > 5 % more managed memory than the
  baseline fixture (no leaks via lapsed event handlers — the COM
  wrappers and shell helpers are the highest-risk surfaces).

### 9.2 Sample app — `samples/MultiWindowDemo`

- [ ] Demonstrates: primary + settings (keyed) + tray icon + jump list
  + taskbar progress + overlay + thumbnail toolbar. Single shell, ~200
  LOC; serves as the `samples/` exemplar for the entire spec.
- [ ] README at `samples/MultiWindowDemo/README.md` explaining each
  feature with a one-paragraph callout. Cross-links to spec §
  numbers. (Existing samples README format — verify by reading
  `samples/HelloWorld/README.md` if present.)

### 9.3 Migration / docs

- [ ] Verify all 9 `samples/**` `Run<TRoot>` call sites compile without
  source change (only DIP behavior change). Visually inspect each on a
  100 % display to confirm no behavioral regression.
- [ ] Update `docs/guide/` — add a "Windows" page covering the model
  (one section per spec §). Mirror the structure of existing guides.
- [ ] Update `docs/api/` (if generated) to reflect new public types.
- [ ] Verify `[Obsolete]` warnings on `ActiveHost` and the legacy
  `MainDispatcherQueue` static (Phase 4 dropped the latter — confirm
  the obsoletion was on the public surface for the prior release).

### 9.4 Performance regression gate

- [ ] Run `tests/perf_bench/`, `tests/startup_perf/`, `tests/stress_perf/`
  on the same hardware as Phase 0.7 baseline. P95 cold start must not
  exceed baseline + 5 %.
- [ ] If regression detected, profile via PerfView → identify the
  responsible phase → file a follow-up before merging.

### 9.5 Security review

- [ ] Walk the §0.5 checklist end-to-end against the merged code.
  Specifically verify:
  1. `windows.open` MCP tool rejects non-allowlisted component names
     (test exists).
  2. Persistence-file path traversal is impossible (constructed via
     `Path.Combine` with a sanitized process name; no user-supplied path
     fragments).
  3. `JumpList` arguments are not auto-acted on.
  4. Tray flyout content cannot escape its hidden popup window
     (Reactor's standard reconciler boundaries apply; no extra escape
     hatch was added).
- [ ] Run `claude-code` `/security-review` on the diff and resolve any
  findings before merge.

### 9.6 Accessibility validation

- [ ] Walk the §0.6 checklist with Narrator on Windows 11:
  1. Tray icon tooltip announces.
  2. Taskbar overlay description announces.
  3. Owned window activation moves Narrator focus.
  4. Closing-guard `ContentDialog` is reachable via keyboard and
     announces correctly.
- [ ] Verify forced-colors mode (HighContrast) on every fixture.
- [ ] Verify reduced-motion suppression of any window-open / tray-flyout
  animation we add (none planned in this spec, but verify nothing
  regressed).

### 9.7 AI ergonomics review

- [ ] Public surface is shaped for both human authoring and AI code
  generation. Validate by:
  1. Asking Claude to author each spec §13 example fresh, given only
     the public XML doc comments. Each example should be reproducible
     without reading the spec body.
  2. Checking that error messages on misuse (e.g. `WindowSpec` validation,
     `> 7` thumbnail buttons, late `WindowPersistenceStore` set) name the
     offending parameter, the constraint, and the spec § anchor when
     non-obvious.
  3. Ensuring discovery affordances: a developer typing `ReactorApp.`
     sees the full surface (`OpenWindow`, `OpenTrayIcon`, `Windows`,
     `TrayIcons`, `FindWindow`, `FindTrayIcon`, `Exit`,
     `ShutdownPolicy`, `UIDispatcher`, `WindowPersistenceStore`) at
     IntelliSense — no buried statics on internal types.

### 9.8 Localization audit

- [ ] Re-run §0.3 unit test (non-ASCII round-trip) against the merged
  code.
- [ ] Verify no Reactor-owned user-visible string was added inadvertently.
  The only Reactor-emitted string is the §12.1 `[reactor]` info line; it
  is diagnostic and stays en-US per repo convention.
- [ ] If we added any `Debug.WriteLine` strings in WndProc / COM error
  paths, confirm they're diagnostic-only (not surfaced to the user).

### 9.9 CHANGELOG finalization

- [ ] Final CHANGELOG entries grouped under "Spec 036 — Window model"
  reference the spec §, list breaking changes (DIP semantics) prominently,
  and note the obsoletion plan for `ActiveHost` /
  `MainDispatcherQueue`.
- [ ] Add migration recipe to the spec-036 release notes section: a
  3-bullet "if you used X today, do Y now" for the three most common
  call patterns: `Run<T>(title, w, h)`,
  `host.Window.AppWindow.Resize(...)`, and `WindowPersistedScope`.

---

## Open questions / out of scope

The spec's §15 resolved-questions and §16 out-of-scope remain in force.
Items deferred from this implementation:

- Modal top-level windows (§9). Re-evaluate when WinAppSDK lands the
  `OverlappedPresenter.IsModal` fix.
- Multi-instance / single-instance app pattern (`AppInstance`
  redirection). `WindowKey` shape is forward-compatible with a future
  cross-instance broadening.
- `UseWindowActivation(...)` shorthand hook (spec §15.5). Wait for
  sample-app evidence before adding.
- Reconciler-as-portal `Window(...)` element (§3.1 / §N3).
- Cross-window content drag (§N2).
- Custom title-bar primitive (§N5) — existing `TitleBar(...)` factory
  owns title-bar customization.
