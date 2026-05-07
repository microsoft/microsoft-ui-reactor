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

- [ ] `ReactorWindow.Activated` / `Deactivated` — wire via
  `Window.Activated` (Microsoft.UI.Xaml). Filter on
  `WindowActivationState`: `CodeActivated | PointerActivated` →
  `Activated`; `Deactivated` → `Deactivated`.
- [ ] `ReactorWindow.SizeChanged` — wire via
  `Window.SizeChanged`. Compute DIP size from raw `WindowSizeChangedEventArgs
  .Size` and `Dpi`. Pass through the original args via
  `WindowDipSizeChangedEventArgs.Raw` (escape hatch).
- [ ] `ReactorWindow.StateChanged` — wire via `AppWindow.Changed` filtered
  on `DidPresenterChange` and `DidStatusChange` (covers minimize / maximize
  / restore / fullscreen / compactoverlay). Compute the new `WindowState`
  enum value once and fire only on change.
- [ ] `ReactorWindow.Closing` — fires on `Window.Closed`'s
  `WindowEventArgs.Handled` path: subscribe with priority, intercept the
  close, raise `Closing` synchronously, set `args.Handled = true` if
  `Cancel == true`. The reason enum derives from a `_closingReason`
  internal field that `ReactorApp.Exit` and `Owner.Close()` set before
  triggering teardown.

### 3.2 New hooks (spec §7)

- [ ] `RenderContext.UseWindow()` — O(1) field read on
  `ReactorHost.OwningWindow`. No subscription, no re-render trigger.
  Returns null when called outside a `ReactorWindow` (tray flyout). Add
  the docstring example from spec §7.1.
- [ ] `RenderContext.UseWindowState()` — subscribes to `StateChanged`,
  re-renders on change. Returns `WindowState.Normal` when outside a
  window.
- [ ] `RenderContext.UseIsActive()` — subscribes to `Activated` /
  `Deactivated`, re-renders on change. Returns `true` outside a window
  (tray flyout is "active" while shown).
- [ ] `RenderContext.UseClosingGuard(Func<bool> canClose)` — registers a
  guard with the current window's `Closing` event. On unmount, removes the
  guard. Multiple guards stack: any returning `false` cancels. Document
  synchronous-only; for async, return false and re-issue `Close()`.
  No-op outside a window.
- [ ] **Hook ordering**: all new hooks pass through the existing
  `HookOrderException` checks (`Core/HookOrderException.cs`). Add unit
  tests that violating hook order trips the analyzer in DEBUG and the
  runtime check in RELEASE.
- [ ] **Component mirror**: every new `RenderContext.Use*` gets a
  parameterless `Component.Use*` mirror per the existing pattern in
  `Component.cs:57-60`.

### 3.3 Tray-flyout fallbacks (spec §7.1)

- [ ] `UseWindow()` returns null in tray-flyout content. Document on the
  XML doc with the spec §7.1 example.
- [ ] `UseWindowSize()` returns `(0, 0)`, `UseDpi()` returns system
  primary DPI, `UseWindowState()` returns `Normal`, `UseIsActive()`
  returns `true`, `UseClosingGuard()` is a no-op.
- [ ] Add a unit test for each hook in tray-flyout context — Phase 8
  fills in the actual tray fixture; for now use a synthetic
  "no-OwningWindow" host context.

### 3.4 Tests — Phase 3

- [ ] Selftest fixture per hook (`UseWindowFixture.cs`,
  `UseWindowStateFixture.cs`, `UseIsActiveFixture.cs`,
  `UseClosingGuardFixture.cs`).
- [ ] Unit: closing-guard cancellation — guard returning `false`
  prevents `Window.Closed` from firing. Multiple guards: any false
  cancels, all-true allows close, guards are called in subscription
  order, exceptions in a guard are caught and logged (default false-on-
  exception with a `[reactor]` warning — fail-safe), then re-thrown to the
  reconciler error boundary if a feature flag opts in.
- [ ] Unit: stacked guards from sibling components both contribute and
  both cleanup on unmount.
- [ ] Selftest: `StateChanged` fires once per logical state transition
  (no duplicates from `AppWindow.Changed` over-firing). The §35 stress
  fixture pattern is the right shape for this.
- [ ] Perf benchmark: 1000-component tree all calling `UseWindow()` —
  zero allocations after warmup (`UseWindow` must not box, must not LINQ).

---

## Phase 4: Multi-window + `UseOpenWindow` (spec §3.2, §4.3, §6, §13.5)

### 4.1 `UseOpenWindow` hook

- [ ] `RenderContext.UseOpenWindow(WindowKey key, WindowSpec spec,
  Func<Component> factory)`. Identity by `key`; re-renders that pass the
  same key reuse the window. If a window with `key` is already open under
  `ReactorApp.Windows`, return it; otherwise call `ReactorApp.OpenWindow`
  and remember the handle in the hook slot.
- [ ] Cleanup semantics per spec §15.6 (resolved): if the parent
  unmounts while the secondary window is open, **do not** close it
  automatically. Components that want the inverse explicitly call
  `.Close()` from a `UseEffect` cleanup on the returned handle.
- [ ] Document re-render stability: the returned `ReactorWindow` is
  identity-stable across renders so long as `key` is stable. Changing
  `spec` calls `Update(spec)` rather than re-opening.
- [ ] Component-mirror overload.

### 4.2 `ShutdownPolicy` plumbing

- [ ] Define the three policies in `ShutdownPolicy` enum (Phase 1
  added the type, Phase 4 wires the behavior).
- [ ] After every `WindowClosed` and `TrayIconClosed` (Phase 8 will fire
  the latter — for Phase 4, only windows count and `TrayIcons` is empty):
  evaluate the active policy:
  - `OnPrimaryWindowClosed`: if the just-closed window equals
    `PrimaryWindow`, call `ReactorApp.Exit()`.
  - `OnLastSurfaceClosed`: if `Windows.Count == 0 &&
    TrayIcons.Count == 0`, call `Exit()`.
  - `Explicit`: never exit on surface close.
- [ ] If `ShutdownPolicy == OnPrimaryWindowClosed` and the startup callback
  opens zero windows, call `Exit()` after the startup callback returns.
  (Spec §6.2.) Selftest this.

### 4.3 Drop `MainDispatcherQueue` static

- [ ] Remove `ReactorHost.MainDispatcherQueue` (was kept obsolete in
  Phase 1). All internal callers route through `ReactorApp.UIDispatcher`.
- [ ] Search the repo for any remaining references; adjust callers in
  `RenderContext.cs`, `Reconciler*.cs`, and `Hosting/Devtools/*.cs`.

### 4.4 Persistence-scope per window (spec §3.4)

- [ ] `WindowPersistedScope` (`Core/WindowPersistedScope.cs`) currently
  has no host wiring. Wire it: each `ReactorWindow` constructs an
  instance and `RenderContext.UsePersisted` resolves the correct scope
  via `Host.OwningWindow.PersistedScope`. (Closes spec 033 §7.5.)
- [ ] Two windows of the same component class hold independent persisted
  state. Unit-test this.

### 4.5 Tests — Phase 4

- [ ] Selftest `MultiWindowFixture.cs`: open primary + 2 secondary
  windows, close one, verify the other two stay alive and receive
  `WindowClosed` for the closed one.
- [ ] Selftest: close the primary under `OnPrimaryWindowClosed` → app
  exits even with secondary windows open. Use a process-exit assertion
  pattern (the harness exposes one — verify by grepping
  `tests/Reactor.AppTests.Host/`).
- [ ] Selftest: `OnLastSurfaceClosed` policy keeps the app alive while
  any window is open and exits on the last close.
- [ ] Selftest `UseOpenWindowKeyFixture.cs`: parent component renders
  three times, `UseOpenWindow("settings", ...)` yields the same window
  handle each time. Re-rendering with a different key opens a second
  window and the first remains open.
- [ ] AppTest E2E: launch `samples/MultiWindowDemo` (Phase 0 stub now
  fleshed out), assert two top-level windows visible to UIA, screenshot
  each.
- [ ] Unit: per-window `UsePersisted` isolation — two `ReactorWindow`s
  hosting the same component get distinct keyed values.

---

## Phase 5: Persistence + chrome (spec §4.1 chrome fields, §8, §9 owned)

### 5.1 Persistence

- [ ] `IWindowPersistenceStore` interface per spec §8.
- [ ] Default packaged-app store: `PackagedSettingsStore` writes to
  `ApplicationData.Current.LocalSettings`. Match WinUIEx's key namespacing.
- [ ] Default unpackaged store: `JsonFileStore` writes to
  `%LOCALAPPDATA%/<ProcessName>/reactor-windows.json`. **Security:**
  file size cap (1 MB), `FileShare.None`, validate JSON shape before
  applying, never throw to user code on read failure (warn-and-default).
- [ ] `ReactorApp.WindowPersistenceStore` settable static (must be set
  before the first `OpenWindow` — guard with `Interlocked.CompareExchange`
  and throw `InvalidOperationException` on a late set).
- [ ] On `Window.Closed`, serialize `WINDOWPLACEMENT` + monitor-layout
  fingerprint via the active store keyed by `PersistenceId`. Best-effort:
  failures log and don't bubble.
- [ ] On `WM_SHOWWINDOW` first-shown, read back via the store; if the
  monitor fingerprint matches, call `SetWindowPlacement`. Otherwise
  fall back to `WindowSpec.StartPosition`. (Spec §8 — borrows
  fingerprint logic from WinUIEx `WindowManager.LoadPersistence`.)
- [ ] `WindowStartPosition.RestoreFromPersistence` activates this path
  unconditionally; other start positions only restore if a prior session
  saved one.

### 5.2 Chrome — icon, presenter, resizable/minimizable/maximizable, always-on-top

- [ ] `WindowSpec.Icon` → `WindowIcon.Apply(AppWindow)` invoked at apply
  time. Test both `FromPath` and `FromResource`.
- [ ] `WindowSpec.Presenter` (`Overlapped | FullScreen | CompactOverlay`)
  → `AppWindow.SetPresenter(...)`. `Update(spec)` flips presenters.
- [ ] `WindowSpec.IsResizable / IsMinimizable / IsMaximizable` → on
  `OverlappedPresenter` only, set the equivalent properties. On
  `FullScreen` / `CompactOverlay`, these flags have no effect; document
  this.
- [ ] `WindowSpec.IsAlwaysOnTop` → `OverlappedPresenter.IsAlwaysOnTop`.
- [ ] `WindowSpec.IsShownInSwitchers` →
  `AppWindow.IsShownInSwitchers`.
- [ ] `WindowSpec.ExtendsContentIntoTitleBar` → `Window.ExtendsContent
  IntoTitleBar`. (Spec §N5 — only knob added; existing `TitleBar(...)`
  factory owns the rest.)
- [ ] `WindowSpec.Backdrop` (`BackdropChoice?`) → seed the existing
  `BackdropApplier` modifier on the host's root tree before mount. (Spec
  §3.3.) Verify spec 033's `BackdropApplier` API is the right surface
  here.
- [ ] `WindowSpec.ActivateOnOpen` → call `Activate()` after mount and
  persistence restore.

### 5.3 Owned windows (spec §9)

- [ ] `WindowSpec.Owner` → at apply time, call `AppWindow.SetParent` (or
  Win32 `SetWindowLongPtr(GWLP_HWNDPARENT)` fallback for cases where
  AppWindow doesn't expose what we need).
- [ ] Owner-close cascading: when an owner closes, its owned windows
  close first with `WindowClosingEventArgs.Reason = OwnerClosed`. Honor
  guard cancellation; if any owned window cancels, the owner-close is
  also cancelled.
- [ ] Owned windows hide from the taskbar by default
  (`IsShownInSwitchers = false` is the default for owned windows unless
  the spec explicitly overrides).

### 5.4 Tests — Phase 5

- [ ] Unit: `JsonFileStore` round-trip — write → read returns the same
  bytes. Corrupted file (truncated, garbage JSON) returns null and
  emits a warn log without throwing.
- [ ] Unit: monitor-fingerprint mismatch → returns null → fall back to
  default position. Use a fake `IDisplayInfoProvider`.
- [ ] Unit: 1 MB cap — reading a 2 MB persistence file is rejected.
- [ ] Unit: `Owner.Close()` cascades. Test cancellation: an owned-window
  guard returning false cancels the owner's close.
- [ ] Selftest: presenter switch (Overlapped → FullScreen → Overlapped)
  preserves window content tree (no remount).
- [ ] Selftest: persistence — open window, resize, close, reopen,
  assert restored size and position match.
- [ ] Selftest: backdrop seeding via `WindowSpec.Backdrop` matches the
  declarative `Backdrop(...)` modifier path for visual identity.

---

## Phase 6: Devtools / MCP (spec §10)

### 6.1 Devtools `WindowRegistry` integration

- [ ] On every `WindowOpened`, call
  `WindowRegistry.Attach(window, isMain: window == PrimaryWindow)`.
  (Today: `ReactorApp.cs:545` calls Attach inline; move to event handler.)
- [ ] On every `WindowClosed`, call `WindowRegistry.Detach(window)`.
- [ ] Verify `WindowRegistry.cs:21` already supports multiple windows by
  HWND — no change there beyond the call-site move.

### 6.2 New MCP tools

- [ ] `windows.list` returns
  `[{id, key, title, width, height, dpi, state, isMain}]` for every
  window. Title and key may be PII — see §0.5; only emit at `LogTrace`.
- [ ] `windows.activate(id)` — calls `ReactorWindow.Activate()`.
- [ ] `windows.close(id)` — calls `ReactorWindow.Close()`. Honors guards
  (the MCP tool must surface `Cancelled` cleanly, not hang).
- [ ] `windows.open(spec, componentName)` — gated by the existing
  component-allowlist check (`ReactorApp.cs:474-485`). Reject any
  component name not on the allowlist with a structured error.
  **Security**: a unit test exercises a non-allowlisted name and asserts
  rejection.
- [ ] All four tools register with MCP tool discovery using the same
  pattern as existing devtools tools (`Hosting/Devtools/DevtoolsTools.cs`).

### 6.3 Tests — Phase 6

- [ ] Unit: `windows.list` schema round-trip (JSON shape stable).
- [ ] Unit: `windows.open` with allowed component → success; with
  disallowed → returns error code, never instantiates.
- [ ] AppTest E2E: `mur devtools` golden flow with two windows — list
  returns both, activate flips focus, close removes one.
- [ ] Selftest: closing via MCP `windows.close` honors a `UseClosingGuard
  (() => false)` and returns Cancelled.

---

## Phase 7: Shell — taskbar progress, overlay, thumbnail toolbar (spec §11.1, §11.2, §11.5)

### 7.1 `ITaskbarList3` wrapper

- [ ] Create `src/Reactor/Hosting/Shell/TaskbarComInterop.cs` —
  `[ComImport, Guid(...)]` definitions for `ITaskbarList3`. **Trim/AOT-
  safe**: no dynamic invocation. Use `[GeneratedComInterface]` if the
  project's TFM supports it; otherwise classic `[ComImport]`.
- [ ] Per-process singleton initialized lazily on first use. Store
  in a `Lazy<ITaskbarList3>` field on a static helper. Document that
  apps that never touch `Progress` / `Overlay` / `SetThumbnailToolbar`
  pay zero startup cost.

### 7.2 `TaskbarProgress`

- [ ] Type per spec §11.1.
- [ ] `ReactorWindow.Progress` lazily constructs the wrapper on first
  read. The wrapper holds the HWND and forwards property writes to
  `SetProgressValue` / `SetProgressState`.
- [ ] State change marshaling: `Indeterminate` ignores `Value`; `None`
  clears both; explicit `Value` writes implicitly switch to `Normal` if
  state is `None`.
- [ ] Value range: clamp `[0.0, 1.0]`. Out-of-range throws
  `ArgumentOutOfRangeException`.

### 7.3 `TaskbarOverlay`

- [ ] Type per spec §11.2. `Icon = null` clears.
- [ ] `WindowIcon.Apply(taskbarList, hwnd, accessibleDescription)` overload
  for `ITaskbarList3.SetOverlayIcon`. Pass through
  `AccessibleDescription` to `pszDescription`. (Spec §0.6 a11y.)
- [ ] Icon size validation: warn-only if the supplied icon is not
  16 × 16 logical (will be downscaled by the shell with quality loss).

### 7.4 `ThumbnailToolbar`

- [ ] `ThumbnailToolbarButton` record per spec §11.5.
- [ ] `ReactorWindow.SetThumbnailToolbar(IReadOnlyList<...>)` and
  `ClearThumbnailToolbar()`. First call: `ThumbBarAddButtons` with up
  to 7 buttons; further calls diff against previous set and call
  `ThumbBarUpdateButtons` for changed buttons only.
- [ ] **Validation**: > 7 buttons throws `ArgumentException`. Duplicate
  `Id` values throw.
- [ ] Click dispatch: WM_COMMAND from the shell carries the button index.
  Map back to the click delegate; invoke on the UI thread.
- [ ] **Lifetime**: Buttons are released on `ReactorWindow.Dispose`.

### 7.5 Hooks (optional in this phase)

- [ ] Skip purpose-specific hooks (`UseTaskbarProgress`, etc.) — the
  spec only commits to `UseEffect` over `Progress`. Resolved §15.5: wait
  for sample-app evidence before adding wrappers.

### 7.6 Tests — Phase 7

- [ ] Selftest fixture: write `Progress.State = Indeterminate`, then
  `Normal` with `Value = 0.5`, then `Clear()`. Assert no throw.
  Visually verifiable on Windows 10 / 11; selftest just asserts no
  exception (UIA can't read taskbar progress).
- [ ] AppTest E2E: launch `MultiWindowDemo` with progress on, verify
  via UIA inspect that the window's automation peer surfaces a
  `RangeValuePattern` if WinUI propagates it. (May not — skip if so;
  document.)
- [ ] Unit: `Progress.Value = 1.5` throws.
- [ ] Unit: `SetThumbnailToolbar([8 buttons])` throws.
- [ ] Unit: button click delegate fires on the UI thread (assert
  `DispatcherQueue.HasThreadAccess`).
- [ ] Selftest: `Overlay.AccessibleDescription` round-trips through
  `pszDescription` (verify by reading the overlay's UIA property if
  Windows exposes it; otherwise assert the COM call was made with the
  string by recording the interop).

---

## Phase 8: Shell — jump list, tray, activation (spec §11.3, §11.4, §11.6, §13.6)

### 8.1 `JumpList` static

- [ ] `JumpListItem`, `JumpListItemKind`, `JumpList` static per spec §11.3.
- [ ] **Packaged path**: `Windows.UI.StartScreen.JumpList` WinRT API
  (async). Must run on UI thread per WinRT contract.
- [ ] **Unpackaged path**: Win32 `ICustomDestinationList` COM. Add
  interop in `Hosting/Shell/JumpListComInterop.cs`. Detect packaged vs.
  unpackaged at runtime via `Package.Current` (no `#if`). On unpackaged,
  the WinRT API throws — fall back to ICDL.
- [ ] `AppUserModelId` settable; required for unpackaged. Throw on
  `UpdateAsync` if null + unpackaged.
- [ ] **Security**: argument round-trip — `JumpListItem.Arguments` are
  command-line strings reaching the next process invocation. Document
  on `JumpList.UpdateAsync` XML doc that callers must validate any
  inbound arguments via `Reactor.Cli`'s parser before acting on them.
  Reactor itself must not auto-execute arguments.
- [ ] `ShowRecent` / `ShowFrequent` toggle visibility only — content is
  OS-managed.

### 8.2 `LaunchActivation`

- [ ] `LaunchKind`, `LaunchActivation` types per spec §11.6.
- [ ] In `OnLaunched`, parse `Microsoft.UI.Xaml.LaunchActivatedEventArgs`
  and the underlying `IActivatedEventArgs.Kind` (Launch | File | Protocol
  | Toast | …). Map to `LaunchKind`. Extract `Arguments` (CLI args from
  jump-list / tray / thumbnail-toolbar action) and `Files` for File
  activations.
- [ ] Set `ReactorAppContext.LaunchActivation` before invoking the
  startup callback.
- [ ] **Security**: never log `LaunchActivation.Arguments` or `Files` at
  default verbosity (PII / file paths). Trace-only.

### 8.3 `ReactorTrayIcon`

- [ ] Create `src/Reactor/Hosting/Shell/TrayIconComInterop.cs` for
  `Shell_NotifyIcon` (NIM_ADD/NIM_MODIFY/NIM_DELETE) and the message
  constants (NIN_SELECT, WM_CONTEXTMENU, etc.).
- [ ] Create `src/Reactor/Hosting/Shell/TrayHiddenWindow.cs` — the
  hidden message-only window that owns `Shell_NotifyIcon` and routes
  callbacks. **Internal**, never exposed to app code. One per process,
  shared among tray icons.
- [ ] `TrayIconSpec` and `ReactorTrayIcon` types per spec §11.4.
- [ ] Events: `Click`, `DoubleClick`, `RightClick` — fire on UI thread.
- [ ] `ShowFlyout(Element flyoutContent)` — reconciles the element into
  a hidden WinUI popup window (`XamlRoot` from a hidden Microsoft.UI.
  Xaml.Window owned by the tray subsystem). The popup positions near
  the tray icon (`Shell_NotifyIcon` `dwInfoFlags` + `Shell_NotifyIconGetRect`
  for tray rectangle).
- [ ] `HideFlyout()` — closes the popup. Idempotent.
- [ ] `Update(TrayIconSpec)` — diff icon / tooltip / visibility.
- [ ] `Close()` / `Dispose()` — `NIM_DELETE` and remove from
  `ReactorApp.TrayIcons`.

### 8.4 `ReactorApp` tray surface

- [ ] `ReactorApp.OpenTrayIcon(TrayIconSpec)`,
  `ReactorApp.TrayIcons` (snapshot list, COW),
  `ReactorApp.FindTrayIcon(WindowKey)`,
  `ReactorApp.TrayIconOpened` / `TrayIconClosed` events.
- [ ] Mirror methods on `ReactorAppContext`.
- [ ] **Shutdown policy**: `OnLastSurfaceClosed` now considers tray icons.
  `Explicit` is the supported tray-only policy.

### 8.5 `UseTrayIcon` hook

- [ ] `RenderContext.UseTrayIcon(TrayIconSpec)` — opens (or reuses by
  `Key`) a tray icon scoped to the calling component. On unmount, closes
  the icon. The "scope to component" behavior is the only difference from
  `ReactorApp.OpenTrayIcon`. Document the difference clearly so apps
  pick the right one (component-scoped → hook, app-scoped → static).
- [ ] Component mirror.

### 8.6 Tray flyout `RenderContext` shape

- [ ] When the flyout content reconciles, the `RenderContext` it runs
  in does **not** have a `OwningWindow` (per spec §7.1). Verify all hooks
  return their documented fallback values (Phase 3 wired the fallbacks;
  Phase 8 is the first phase where they're exercised in production).

### 8.7 Tests — Phase 8

- [ ] Selftest fixture `TrayOnlyStartupFixture.cs`: startup callback
  opens only a tray icon under `ShutdownPolicy.Explicit`. Assert app
  stays alive with zero windows. Click the tray icon programmatically
  (via the hidden window's message routing); assert `Click` fires.
- [ ] Selftest: tray flyout reconciles a Reactor `Element`; verify the
  flyout's `XamlRoot` content matches the rendered tree.
- [ ] Selftest: `UseTrayIcon` hook unmounts → tray icon disappears.
- [ ] Unit: `TrayIconSpec` non-ASCII tooltip round-trip (spec §0.3
  localization).
- [ ] AppTest E2E: jump list — `JumpList.UpdateAsync([{Title: "Open
  recent"}])`, then via `Reactor.Cli` invoke the entry's arguments and
  verify the app receives the matching `LaunchActivation.Kind ==
  JumpList`.
- [ ] Selftest: tray icon's tooltip is exposed to UIA / Narrator with
  the expected text. (Spec §0.6.)
- [ ] Selftest: closing the main window with tray icon present and
  policy `OnLastSurfaceClosed` does **not** exit; closing the tray icon
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
