# Windowing Evolution — Implementation Tasks

Derived from: [`docs/specs/054-windowing-evolution.md`](../054-windowing-evolution.md).

> **Status:** All phases (0-11) complete on branch
> `andersonch/spec-054-windowing-evolution`. Ready for PR review.
>
> **Final implementation metrics:**
> - Selftest fixtures: 1,069 baseline → 1,118 (+49 across Phases 1-7), zero failures.
> - Unit tests: 9,202 passed / 62 skipped / 0 failed (baseline match).
> - AOT publish smoke green: `samples/apps/command-palette-window`.
> - `mur docs compile --no-screenshots` clean; zero docs-template drift.
> - Removed fields (zero residual references in `src/ tests/ samples/ docs/
>   skills/ plugins/` outside legitimate WinUI `OverlappedPresenter` /
>   `AppWindow` API calls and historical spec/migration-doc text):
>   `WindowSpec.IsResizable`, `WindowSpec.IsShownInSwitchers`,
>   `WindowSpec.IsAlwaysOnTop`, `WindowStartPosition.RestoreFromPersistence`.
>
> **Conventions** (mirroring `053-reactor-advanced-and-win2d-canvas-implementation.md`):
> - Every task is a checkbox; mark `[x]` only when its artifact (code +
>   tests + doc update, where applicable) is landed.
> - Reactor's triple gate must stay green after every phase. Run order on
>   Windows-x64:
>   `dotnet build Reactor.slnx -p:Platform=x64` →
>   `dotnet test tests/Reactor.Tests -p:Platform=x64 --no-build` →
>   `dotnet run --project tests/Reactor.AppTests.Host -p:Platform=x64 -- --self-test`.
> - **All new feature assertions for spec 054 live in AppTests.Host
>   selftest fixtures** — Win32 / AppWindow / DWM interop is not testable
>   in the headless xunit tier. Existing unit tests must stay green, but
>   the *new* coverage for §5/§6 features is selftest-only.
> - **Two-place selftest registration is mandatory.** Per repo memory,
>   every new fixture must be added in `SelfTestFixtureRegistry.cs` in
>   **both** the fixture-name string list **and** the `name → ctor`
>   switch arm. A fixture missing from either place silently never runs.
> - **Breaking changes ship without an obsolete-alias period.** Spec §9
>   removes `IsResizable`, `IsShownInSwitchers`, `IsAlwaysOnTop`, and
>   `WindowStartPosition.RestoreFromPersistence` outright. Every removal
>   has a `Properties.Settings`-style migration line in the changelog
>   and a sample/selftest sweep in its phase.
> - Phases are sequential per spec §10. Phase 1 lands additive read-back
>   surface (zero migration risk); Phases 2–4 land the breaking field
>   changes one cluster at a time; Phases 5–7 land the remaining tier-A
>   and tier-B features; Phases 8–10 land docs, skills, and samples.

---

## Background research — WPF / UWP / WinUI 2 parity check

Recorded so future reviewers can audit the "familiar to WPF/UWP devs"
claim. None of these change the spec; they validate that spec 054's
shape will land naturally for developers coming from the prior
Microsoft UI stacks.

### Persistence: opt-in is consistent, not novel

The source XAML proposal pushes **default-on** placement persistence.
This is _not_ what WPF or UWP/WinUI 2 ever shipped:

- **WPF:** No automatic placement persistence. The canonical pattern is
  `Properties.Settings` saving `Top`/`Left`/`Width`/`Height`/`WindowState`
  in `Window.Closing`, restored in `OnSourceInitialized` — every WPF app
  rolls its own. Search results for "WPF save window position" return
  multi-decade-old StackOverflow snippets, not framework primitives.
- **UWP / WinUI 2:** Single-window app paradigm dominated; window placement
  was OS-managed (`ApplicationView.PreferredLaunchViewSize` was a hint,
  not persisted state). When multi-window arrived
  (`CoreApplication.CreateNewView`), there was still no built-in
  persistence — apps used `LocalSettings` and `SuspensionManager` for
  navigation state, not window geometry.
- **WinUI 3 / WindowsAppSDK:** Same as UWP — nothing automatic. WinUIEx's
  `WindowEx.PersistenceId` (the de-facto third-party patch) is **opt-in
  via a non-null id**, exactly Reactor's pre-054 model.

**Conclusion:** Spec 054's "stay opt-in but provide a one-call
`WithPersistence(id)` helper" lands closer to what WPF and UWP devs
actually expect than the default-on XAML proposal would. The plan does
not need to revisit this decision; the spec §5.7 reasoning stands.
What we _do_ need (R8 below) is correct *gating* so `PersistenceId`
without `PersistPlacement` does **not** silently round-trip placement
through disk — that's the behavior change the migration must enforce.

### Naming / shape parity table

| Concept | WPF / UWP name | Reactor 054 name | Same shape? | Deviation rationale |
| --- | --- | --- | --- | --- |
| Resize policy | `WPF.ResizeMode` (`CanResize` / `CanResizeWithGrip` / `CanMinimize` / `NoResize`) | `WindowResizeMode` (`CanResize` / `CanMinimize` / `NoResize`) | mostly | `CanResizeWithGrip` dropped — Win11 visual language renders no grip (spec §5.1). |
| Size-to-content | `WPF.SizeToContent` (`Manual` / `Width` / `Height` / `WidthAndHeight`) | `WindowSizeToContent` (same four values) | yes | Identical. |
| Topmost | `WPF.Topmost` bool | `WindowLevel` (`Normal` / `Floating` / `AlwaysOnTop`) | richer | Adds `Floating` for tool-palette UX (spec §6.4). |
| Window chrome | `WPF.WindowStyle` (`SingleBorderWindow` / `None` / `ToolWindow` / `ThreeDBorderWindow`) | `WindowStyle` (`Default` / `None` / `ToolWindow`) | trimmed | `ThreeDBorderWindow` is a Win98-era visual; `SingleBorderWindow` collapses into `Default` (spec §6.1). |
| Corner radius | (none in WPF or UWP) | `WindowCornerStyle` enum | new | DWM only exposes discrete preference (spec §6.2). |
| Aspect ratio | (none in WPF or UWP) | `AspectRatio` + `SetAspectRatio` | new | Inherited from NSWindow / WinUIEx (spec §5.2). |
| Drag from anywhere | `WPF.Window.DragMove()` | `BeginDragMove()` + `IsMovableByBackground` | similar | Reactor splits the gesture-attached call from the spec-level "always draggable" flag (spec §5.3). |
| Taskbar visibility | `WPF.ShowInTaskbar` | `ShowInTaskbar` | exact | (spec §5.4). |
| Alt-Tab visibility | (UWP: `ApplicationView.IsScreenCaptureEnabled` is closest; no direct API) | `ShowInSwitcher` | new | Split from `ShowInTaskbar` so tool palettes can hide from taskbar but stay in Alt-Tab (spec §5.4). |
| Startup position | `WPF.WindowStartupLocation` (`Manual` / `CenterScreen` / `CenterOwner`) | `WindowStartPosition` (`Default` / `CenterOnPrimary` / `CenterOnOwner` / `CenterOnCurrent` / `Manual`) | extended | `CenterOnCurrent` (cursor monitor) is new; rest map (spec §5.6). |
| Window placement persistence | None built-in | `WithPersistence(id)` helper + `PersistPlacement` bool | new | Validated above — opt-in matches WPF / UWP expectations. |
| Live position read | `WPF.Window.Top` / `Left` | `ReactorWindow.Position` getter + `PositionChanged` | exact | (spec §5.5). |
| Taskbar item info | `WPF.TaskbarItemInfo` | `TaskbarItem` | exact | Reactor groups existing shortcuts behind the facade (spec §6.5). |
| Custom title bar | `WPF.WindowChrome` | `TitleBar(...)` element + `CornerStyle` + `WindowStyle` | composed | Reactor's tree-based shape (spec §6.6); spec §2 N2 explicitly rejects a WPF-`WindowChrome`-shaped API. |
| Click-through | (WPF: none direct; UWP: none) | `IgnorePointerInput` | shipped (036) | — |
| Show dialog | `WPF.Window.ShowDialog()` | (none) | gap | Out of scope per spec §2 N1; tracked in separate spec. |
| Jump list | `WPF.TaskbarItemInfo.JumpList` | (none) | gap | Out of scope per spec §10 deferred. |
| Cursor-per-window | `WPF.Window.Cursor` | (none) | gap | Out of scope per Appendix A. |

The deviations are deliberate and each one is anchored in a spec
section. A future reviewer who wants to argue for a deviation _back_ to
the WPF shape should open an issue rather than slip the change inside
this implementation PR.

---

## Exit gate (all must hold to declare 054 done)

1. Spec §5 (Tier A) and §6 (Tier B) features all land. Spec §7 (Tier C)
   features are **not** added in any form — see §0.3 negative checklist.
2. Every spec §9.1/§9.2 breaking change is applied; every old field is
   gone from `src/`, `tests/`, `samples/`, `docs/`, and the two skill
   trees. CI grep proves zero residual references.
3. `dotnet build Reactor.slnx -p:Platform=x64` is clean
   (warnings-as-errors on core lib).
4. `dotnet test tests/Reactor.Tests -p:Platform=x64 --no-build` is green
   (existing unit suite — no new feature coverage added here, only
   migration fixups for the removed/renamed fields).
5. Full selftest run
   (`dotnet run --project tests/Reactor.AppTests.Host -p:Platform=x64 -- --self-test`)
   is green, including the ≈40 new fixtures introduced across Phases 1–7.
6. AOT publish smoke: at least one of the new samples (recommend
   `command-palette-window`) publishes clean via
   `-p:PublishAotInternal=true` with zero new trim/AOT warnings.
7. Docs compile: `mur docs compile` regenerates `docs/guide/windows.md`
   and the new `docs/guide/windowing-advanced.md` from their templates;
   no template-vs-output drift after the run.
8. Both skill forms exist for the new content:
   - `skills/windowing.md` (loose-file).
   - `plugins/reactor/skills/reactor-windowing/SKILL.md` (plugin form).
   - `reactor-getting-started` and `reactor-dsl` skills updated for the
     renamed `WindowSpec` fields.
9. Devtools surface (`windows.open`, `windows` snapshot) keeps working
   against the renamed fields; new fields are exposed in the snapshot
   where useful (Phase 11.2).
10. Spec §11 open questions are resolved and the decisions are recorded
    in an append at the bottom of `054-windowing-evolution.md` (or a
    sibling `docs/specs/054/decisions.md` if the spec is locked).

---

## Risks & blind-spots (verified against the codebase + rubber-duck pass)

Tracked here so the implementing agent can mark each as addressed by the
named task. R8 / R10 were corrected by the planning critique — the
original risk write-up was overestimating codec impact and
underestimating restore/save gating.

| #   | Risk / consideration | Addressed in |
| --- | --- | --- |
| R1  | `WM_SIZING` + `WM_GETMINMAXINFO` order: aspect-ratio clamp must run **before** OS re-clamps to min/max, else the user gets a rectangle that's neither correct aspect nor at min/max. Spec §5.2 documents the ordering. | Task 2.2 |
| R2  | `IsMovableByBackground` interactive-suppression bubble walk must include built-in `Selector` derivatives + arbitrary `Drag(false)` opt-outs, and must not consume `PointerPressed` (clicks on non-interactive bg still need to bubble for tests / a11y). | Task 2.3 |
| R3  | `BeginDragMove` re-entrancy during an active drag — must no-op via `Interlocked.CompareExchange` on a per-window `_dragMoveActive` flag, not queue. Spec §11 Q3. | Task 2.3 |
| R4  | `ShowInTaskbar` apply at runtime needs `ShowWindow(SW_HIDE)` + `SW_SHOW` to force shell refresh after `WS_EX_TOOLWINDOW` ↔ `WS_EX_APPWINDOW` flip. Risk of activation flicker / focus loss. | Task 3.1 |
| R5  | `SizeToContent` measure-and-resize feedback loop: resize → layout → SizeChanged → resize. Must guard with a "we're applying" flag, and gate on actual desired-vs-current divergence. | Task 5.1 |
| R6  | `WindowLevel.Floating` sibling tracking re-uses the spec-036 §9 owned-window registry; must not double-register existing listeners. Open question §11 Q1: should `Floating` also stay above the *owner* (WPF `ToolWindow` does)? **Recommendation: yes, pin in Phase 4 selftest.** | Tasks 0.1, 4.3 |
| R7  | `ExtendsContentIntoTitleBar` going from `bool` to `bool?`: callers that read the field need null checks; positional `with` syntax still works thanks to `bool → bool?` implicit. Any JSON serializer of `WindowSpec` would need an explicit converter — none ship today, but devtools `windows.open` (Phase 11.2) must be re-verified. | Tasks 7.1, 11.2 |
| R8  | **Persistence restore/save gating is the real migration risk, not the codec.** `WindowPlacementCodec` stores `WINDOWPLACEMENT` + monitor fingerprint — no `WindowStartPosition` enum value lives on disk. `ReactorWindow` currently restores whenever `PersistenceId` is non-null and `StartPosition == RestoreFromPersistence`; post-spec-054 the gate becomes "non-null `PersistenceId` **and** `PersistPlacement == true`." Both the restore path (`ReactorWindow.cs:~1292`) and the save path (`ReactorWindow.cs:~829`) must be updated. | Task 3.4 |
| R9  | `PositionChanged` / `ZOrderChanged` fire eagerly during drag (`AppWindow.Changed` is high-rate). Spec §11 Q4: fire eagerly, don't coalesce; but hooks (`UseWindowPosition`) must short-circuit re-render when the DIP value is unchanged after conversion/rounding to avoid spamming components. | Tasks 1.1, 1.2 |
| R10 | **Persistence schema migration is not needed for enum removal** (codec doesn't store the enum) — but if any external surface serializes `WindowSpec` (devtools `windows.open` arguments, future config files), the removed numeric enum value would deserialize as garbage. Audit + reject legacy numeric `4` (= old `RestoreFromPersistence`) at deserialization with a clear migration message. | Task 11.2 |
| R11 | `WM_DISPLAYCHANGE` listener for `ReactorDisplay.DisplayLayoutChanged` is a singleton riding the first opened window's `WindowMessageMonitor`. Must ref-count: closing the registering window without others open must re-host on the next open, not silently lose the event. | Task 1.3 |
| R12 | `UseFilePickerAsync` / `UseFolderPickerAsync` call `IInitializeWithWindow.Initialize` with `UseWindow().NativeWindow.GetWindowHandle()` on the UI thread. Must not accept arbitrary HWNDs from user code; must throw if called off the UI thread. (Same-process trust model: there is no untrusted code in Reactor today, but this keeps the contract tight.) | Task 7.3 |
| R13 | Trim / AOT: new typed event-args (`WindowDipPositionChangedEventArgs`, `WindowZOrderChangedEventArgs`, `DisplayInfo`) and new enums must have **no reflection paths**. Records + plain enums are fine; verify no `JsonSerializer.Serialize<object>(args)` slips into the new code. | Tasks 1.1, 1.2, 1.3 |
| R14 | Hot reload: changing element / `WindowSpec` field types (e.g. `ExtendsContentIntoTitleBar` `bool → bool?`) across an HR boundary is not supported by `ReactorHotReloadCopier`. Document the limit; no code change. | Task 7.1 |
| R15 | Devtools `WindowRegistry.Snapshot()` exposes title / hwnd / bounds / DPI / state today; renamed/removed fields must not break the snapshot JSON shape, and the new live values (Position, Level, Style) should be added when useful so the inspector stays in sync. | Task 11.2 |

---

## Phase 0 — Pre-flight & decisions

Resolve everything the spec leaves to the implementing PR before any
code lands, so later phases don't restart on a re-decision.

### 0.1 Resolve spec §11 open questions

- [x] **§11 Q1 — `WindowLevel.Floating` vs. owner.** Default
      recommendation: floating stays above the *owner* (matches WPF
      `ToolWindow`); pin this in Phase 4 selftest
      `WindowLevel_Floating_AboveOwner`. Record the decision.
- [x] **§11 Q2 — `SizeToContent` + min/max contract.** Min/max wins.
      Document under Phase 5 xmldoc.
- [x] **§11 Q3 — `UseWindowDragMove` re-entrancy.** No-op when
      `GetCapture()` reports a drag in progress; document.
- [x] **§11 Q4 — `PositionChanged` rate.** Fire eagerly; throttling
      is the consumer's job (`UseDebounced` from async-resources).
- [x] **§11 Q5 — `Transparent` BackdropKind on Win10.** Log warning at
      apply time, don't throw; matches existing Mica fallback behavior.
- [x] **§11 Q6 — `CenterOnCurrent` source-of-truth.** Cursor first,
      fall back to foreground monitor when no cursor (RDP /
      non-interactive); document.

### 0.2 Baseline measurements

- [x] Record the current selftest count and pass/fail baseline (clean
      `main` HEAD), so the post-054 delta is unambiguous. Expected ≈40
      new fixtures by the end of Phase 7.
- [x] Record the current `dotnet test tests/Reactor.Tests` count on
      `main`. No new unit tests are expected from this work; the count
      should be stable (modulo migration fixups for removed fields).

### 0.3 Negative checklist — do **not** implement

Spec §7 and Appendix A are explicit about what stays out. The
implementing agent must not add any of the following, even
"opportunistically":

- [x] No `WindowMessageReceived` public escape hatch (spec §7.6 deferred).
- [x] No `Window.JumpList` / `ICustomDestinationList` surface
      (spec §10 deferred to its own spec).
- [x] No `Window.ShowDialog()` / `DialogResult` / modal-top-level
      semantics (spec §2 N1, repeated §12).
- [x] No `IsTransparent` / `WS_EX_LAYERED` / `LWA_COLORKEY` shim
      (spec §7.1 rejected).
- [x] No arbitrary continuous `CornerRadius` via `SetWindowRgn`
      (spec §7.2 rejected).
- [x] No NSWindow-style multi-tier `WindowLevel.Overlay` /
      `.statusBar` (spec §7.3 rejected).
- [x] No `WindowStyle.Hud` (spec §7.4 rejected).
- [x] No vibrancy materials beyond `SystemBackdrop` (spec §7.5).
- [x] No `Window.Cursor` per-window cursor (Appendix A).
- [x] No `Window.AllowsTransparency` / WPF-`WindowChrome`-shaped APIs
      (Appendix A).

If a future feature request asks for any of these, file an issue;
do not slip them into this PR.

---

## Phase 1 — Tier A read-back & events _(spec §10 Phase 1, ~1 week)_

Purely additive. No removed fields, no behavior change to existing
windows. Lowest risk; lands first so later phases can build on the
new event surface.

### 1.1 `ReactorWindow.Position` + `PositionChanged`

- [x] Add `(double X, double Y) Position { get; }` to `ReactorWindow`
      (DIP, snapshot). Cache in a `volatile` field like `State` / `IsActive`
      so reads are lock-free.
- [x] Add `event EventHandler<WindowDipPositionChangedEventArgs>? PositionChanged;`.
- [x] Add `WindowDipPositionChangedEventArgs : EventArgs` with
      `(double X, double Y) Position { get; }`.
- [x] Wire `AppWindow.Changed` (with `DidPositionChange`) → DIP
      conversion → fire event. Convert physical → DIP using the
      window's current DPI.
- [x] Suppress duplicate fires when the DIP-rounded value is unchanged
      (R9).
- [x] Selftest `Position_ReadBack` — open window at `(100, 100)` DIP,
      assert `Position == (100, 100)`; move via `SetPosition(300, 200)`,
      assert update visible to the next read.
- [x] Selftest `PositionChanged_FiresOnMove` — assert event fires with
      correct DIP values; cross at least one DPI boundary (different
      monitor) and assert the DIP coords are computed against the new
      monitor's DPI.
- [x] Selftest `PositionChanged_NoDuplicateFire` — call `SetPosition`
      with the current value; assert event does not fire.

### 1.2 `UseWindowPosition` hook + `UseIsCovered` + `ZOrderChanged`

- [x] Add `RenderContext.UseWindowPosition()` returning
      `(double X, double Y)` and re-rendering on `PositionChanged`.
- [x] Add `event EventHandler<WindowZOrderChangedEventArgs>? ZOrderChanged;`
      on `ReactorWindow`.
- [x] Add `WindowZOrderChangedEventArgs : EventArgs` with
      `bool IsCovered { get; }` + `bool MovedToTop { get; }`.
- [x] Hook `WM_WINDOWPOSCHANGED` in `WindowMessageMonitor`, gated on
      `!SWP_NOZORDER`. Interpret `hwndInsertAfter`:
      `HWND_TOP/HWND_TOPMOST` → `MovedToTop = true`; otherwise
      `IsCovered = true` (hint, not ground truth — document this in
      xmldoc and in `docs/guide/windows.md`).
- [x] Add `RenderContext.UseIsCovered()` returning `bool` and
      re-rendering on `ZOrderChanged`.
- [x] Selftest `ZOrderChanged_FiresOnInsertAfter` — open two windows,
      activate the back one, assert front fires `IsCovered = true`
      hint. Document in test header that the assertion is "fires on
      transition", not "asserts pixel-accurate cover".

### 1.3 `ReactorDisplay` + `DisplayInfo` + `UseDisplays`

- [x] New `src/Reactor/Hosting/ReactorDisplay.cs` static class with
      `Displays`, `Primary`, `NearestTo(double, double)`,
      `event EventHandler? DisplayLayoutChanged`.
- [x] New `DisplayInfo` record per spec §6.7 (`Id`, `IsPrimary`,
      `WorkAreaDip`, `BoundsDip`, `Dpi`).
- [x] `WM_DISPLAYCHANGE` listener: first opened window's
      `WindowMessageMonitor` registers; ref-counted so closing the
      registering window without others open re-hosts on the next open
      (R11).
- [x] DIP conversion per-monitor (each monitor has its own DPI;
      `WorkAreaDip.X/Y` of a non-primary monitor is "approximately"
      DIP — document in xmldoc).
- [x] Add `RenderContext.UseDisplays()` returning
      `IReadOnlyList<DisplayInfo>` and re-rendering on
      `DisplayLayoutChanged`.
- [x] Selftest `Displays_Enumerate` — assert at least 1 display, all
      have positive DPI, primary is unique.
- [x] Selftest `Displays_NearestTo` — open window at known position,
      assert `NearestTo(pos)` returns the monitor whose `BoundsDip`
      contains it.

### 1.4 `WindowStartPosition.CenterOnCurrent`

- [x] Add enum value `CenterOnCurrent` to `WindowStartPosition` (before
      `RestoreFromPersistence`, which Phase 3 deletes — but keep the
      numeric stability of other values).
- [x] Apply path: `GetCursorPos` → `MonitorFromPoint(MONITOR_DEFAULTTONEAREST)`
      → work-area centering. Fall back to `CenterOnPrimary` when no
      cursor (RDP / service / etc.).
- [x] Selftest `CenterOnCurrent_UsesCursorMonitor` — open window with
      `CenterOnCurrent`, assert window's center is within the cursor's
      monitor's work area.

### 1.5 Phase 1 exit gate

- [x] All Phase 1 selftests registered in both places in
      `SelfTestFixtureRegistry.cs`.
- [x] Full selftest run green.
- [x] No new trim / AOT warnings on `Reactor.csproj`.

---

## Phase 2 — `ResizeMode`, `AspectRatio`, `IsMovableByBackground` _(spec §10 Phase 2, ~1 week)_

First breaking-change phase: removes `IsResizable`.

### 2.1 `WindowResizeMode` enum (spec §5.1)

- [x] Add `public enum WindowResizeMode { CanResize, NoResize, CanMinimize }`
      to `src/Reactor/Hosting/WindowEnums.cs`.
- [x] Add `ResizeMode { get; init; } = WindowResizeMode.CanResize` to
      `WindowSpec`.
- [x] **Remove** `IsResizable` from `WindowSpec`. Sweep `src/`,
      `tests/`, `samples/`, `docs/` for any reference; convert
      `IsResizable = false` → `ResizeMode = WindowResizeMode.NoResize`.
- [x] Apply path: maps to `OverlappedPresenter.IsResizable` +
      `IsMinimizable` + `IsMaximizable` combination per the enum value.
- [x] Selftest `ResizeMode_NoResize_BordersFixed` — open with
      `NoResize`, assert chrome resize is disabled, then verify programmatic
      `SetSize` still changes size. (Drag-resize cannot be simulated headless.)
- [x] Selftest `ResizeMode_CanMinimize_AllowsMinimize` — assert min
      button is enabled but resize is disabled at the presenter level.
- [x] Selftest `ResizeMode_RuntimeUpdate` — `Update(spec with { ResizeMode = NoResize })`
      mid-run, assert presenter flags follow.

### 2.2 `AspectRatio` (spec §5.2)

- [x] Add `public double? AspectRatio { get; init; }` to `WindowSpec`.
- [x] `Validate()`: `> 0 && double.IsFinite(AspectRatio.Value)` else
      throw; throw when combined with `ResizeMode == NoResize` (spec §5.2).
- [x] Hook `WM_SIZING` in `WindowMessageMonitor` (today only sets
      `_userResized = true`). Implement the master/slave dimension
      algorithm from spec §5.2:
      - `WMSZ_LEFT` / `WMSZ_RIGHT` → height is master, recompute width.
      - `WMSZ_TOP` / `WMSZ_BOTTOM` → width is master, recompute height.
      - Corner handles → whichever has the larger user delta is master.
- [x] Verify the spec §5.2 ordering claim: `WM_SIZING` runs first, then
      `WM_GETMINMAXINFO` re-clamps. **Selftest** the interaction via
      the runtime path (R1).
- [x] Add `ReactorWindow.SetAspectRatio(double? ratio)` runtime mutator;
      mirror into `_spec`.
- [x] Add `RenderContext.UseWindowAspectRatio(double? widthOverHeight)`
      hook. Lifetime-bound: unmounting clears the lock; stacks under
      last-writer-wins.
- [x] Selftest `AspectRatio_LockedDrag` — set ratio + simulate
      `WM_SIZING` via `PostMessage`, assert the mutated `RECT*` honors
      the ratio within ±1 px.
- [x] Selftest `AspectRatio_RespectsMinMax` — set min=600, max=1200,
      ratio=2.0; drag past bounds, assert min/max wins.
- [x] Selftest `AspectRatio_RejectsNoResize` — construct spec with both,
      assert `Validate()` throws.
- [x] Selftest `AspectRatio_RuntimeSwap` — `SetAspectRatio(2.0)` then
      `SetAspectRatio(1.0)`, simulate drag, assert new ratio honored.

### 2.3 `IsMovableByBackground` + `BeginDragMove` (spec §5.3)

- [x] Add `public bool IsMovableByBackground { get; init; }` to `WindowSpec`.
- [x] Add `public void BeginDragMove()` to `ReactorWindow`: snapshots
      `GetCursorPos` + `AppWindow.Position`, then runs a 60Hz
      `DispatcherQueueTimer` that polls `GetCursorPos` and calls
      `AppWindow.Move` until `GetAsyncKeyState(VK_LBUTTON)` shows the
      button is released. (Earlier implementations tried
      `WM_SYSCOMMAND`+`SC_MOVE|HTCAPTION` and then
      `WM_NCLBUTTONDOWN`+`HTCAPTION` — both silently fall into the
      system-menu cursor-track Move mode in WinUI 3 because the
      top-level HWND never sees a `WM_LBUTTONDOWN`; pointer input is
      routed through a child `InputSiteBridge` HWND. The polling-timer
      approach is the only reliable mechanism.)
- [x] Re-entrancy guard: `GetCapture()` returns non-null → no-op
      (R3 / spec §11 Q3).
- [x] Install a `PointerPressed` handler on the visual root when
      `IsMovableByBackground == true`. Walk the bubble chain; suppress
      drag when the source is in the built-in interactive list
      (`Button`, `ToggleButton`, `TextBox`, `Slider`, `ScrollViewer`
      thumbs, `Selector` derivatives) **or** any element with the
      `Drag(false)` attached modifier set (R2).
- [x] Do **not** mark the `PointerPressed` event as handled — clicks
      must still bubble for tests and accessibility (R2).
- [x] Add `Drag(bool)` extension method in `ElementExtensions` reading
      an attached-property flag.
- [x] Add `RenderContext.UseWindowDragMove()` returning a stable
      `Action` (same delegate identity across re-renders).
- [x] Selftest `DragMove_FromBackground` — synthesize pointer-press on
      the root; assert `BeginDragMove` was called (via test hook /
      counter — actual move loop is OS-driven and not assertable headless).
- [x] Selftest `DragMove_SuppressedOnButton` — pointer-press on a
      `Button` inside the root; assert no `BeginDragMove` call, and
      button's `Click` still fires.
- [x] Selftest `DragMove_SuppressedOnDragFalse` — pointer-press on a
      `Border(...).Drag(false)`; assert no `BeginDragMove` call.
- [x] Selftest `BeginDragMove_ReentrancyNoop` — call twice rapidly while
      `GetCapture()` is faked non-null; assert second call is a no-op.

### 2.4 Phase 2 exit gate

- [x] All Phase 2 selftests registered in both places.
- [x] Grep proves zero `WindowSpec.IsResizable` field/usages remain; literal
      `IsResizable` still appears where required for WinUI `OverlappedPresenter.IsResizable`
      and in historical spec text.
- [x] Existing unit tests still green (migration fixups only).
- [x] Selftest run green.

---

## Phase 3 — `ShowInTaskbar` / `ShowInSwitcher` split + persistence revamp _(spec §10 Phase 3, ~1 week)_

Second and third breaking-change cluster: removes `IsShownInSwitchers`
and `WindowStartPosition.RestoreFromPersistence`.

### 3.1 `ShowInTaskbar` / `ShowInSwitcher` split (spec §5.4)

- [x] **Remove** `IsShownInSwitchers` from `WindowSpec`.
- [x] Add `public bool ShowInTaskbar { get; init; } = true;`.
- [x] Add `public bool ShowInSwitcher { get; init; } = true;`.
- [x] Apply path: `AppWindow.IsShownInSwitchers = ShowInSwitcher`;
      `WS_EX_TOOLWINDOW` / `WS_EX_APPWINDOW` toggle on the HWND for
      `ShowInTaskbar`, with `ShowWindow(SW_HIDE)` + `SW_SHOW` cycle
      to force shell refresh (R4 / spec §5.4).
- [x] On apply during a *running* window: only cycle visibility when
      the bit actually changed; preserve focus / activation state.
- [x] Sweep `src/`, `tests/`, `samples/`, `docs/`, both skill trees
      for `IsShownInSwitchers`; convert `false` → both new fields
      `false`.
- [x] Selftest `ShowInTaskbarMatrix` — 4 combos
      (T=T, T=F, F=T, F=F). Assert **HWND style bits** (`WS_EX_TOOLWINDOW`
      / `WS_EX_APPWINDOW`) and `AppWindow.IsShownInSwitchers`. Do **not**
      assert against the actual taskbar UI or Alt-Tab enumeration
      (flaky / env-dependent — critique #5).
- [x] Selftest `ShowInTaskbar_RuntimeFlip` — `Update(spec with { ShowInTaskbar = false })`;
      assert hide/show cycle happened exactly once and style bits flipped.

### 3.2 `PersistPlacement` + `PersistenceFallback` + `WithPersistence` (spec §5.7)

- [x] Add `public bool PersistPlacement { get; init; }` to `WindowSpec`.
- [x] Add `public WindowStartPosition PersistenceFallback { get; init; } = WindowStartPosition.Default;`.
- [x] Add `WithPersistence(string id, WindowStartPosition fallback = WindowStartPosition.Default)`
      helper on `WindowSpec` per spec §5.7.
- [x] `Validate()` cross-check: `PersistPlacement && PersistenceId == null`
      throws.

### 3.3 Remove `WindowStartPosition.RestoreFromPersistence` (spec §9.1)

- [x] **Remove** the enum value.
- [x] Sweep every callsite; convert
      `StartPosition = RestoreFromPersistence, PersistenceId = "x"` →
      `.WithPersistence("x")`.
- [x] Verify the codec (`Persistence/WindowPlacementCodec.cs`) does
      **not** persist `WindowStartPosition` — it stores
      `WINDOWPLACEMENT` + monitor fingerprint only. **No codec
      migration needed** (R10 / critique #1). Confirm this assumption
      by code reading and document in the PR description.

### 3.4 Update restore/save gating (R8 — the real migration risk)

- [x] Restore path (`ReactorWindow.cs` ~line 1292): change condition
      from `StartPosition == RestoreFromPersistence` →
      `PersistPlacement && PersistenceId != null`. Use
      `PersistenceFallback` to resolve initial placement when nothing
      is on disk.
- [x] Save path (`ReactorWindow.cs` ~line 829): change condition from
      "save whenever `PersistenceId != null`" →
      "save when `PersistPlacement && PersistenceId != null`."
- [x] Selftest `PersistPlacement_RoundTrip` — open window with
      `.WithPersistence("p1")`, move + resize, close, reopen with
      same spec, assert restored geometry.
- [x] Selftest `PersistPlacement_FallbackWhenEmpty` — open with
      `.WithPersistence("p2", fallback: CenterOnCurrent)` against an
      empty store, assert centered-on-cursor-monitor.
- [x] Selftest `PersistPlacement_NoIdThrows` — spec with
      `PersistPlacement = true, PersistenceId = null`, assert
      `Validate()` throws.
- [x] Selftest `PersistPlacement_FalseDoesNotSaveOrRestore` — open with
      `PersistenceId = "p3", PersistPlacement = false`, move window,
      close, reopen with same spec, assert geometry is **not** restored
      (PersistenceId without PersistPlacement is "identity for
      future persistence subsystems," not a placement-restore signal).

### 3.5 `ReactorWindow.SavePlacement()` (spec §5.8)

- [x] Add `public void SavePlacement()`. No-op when `PersistenceId == null`
      or `PersistPlacement == false`. Idempotent. Forces an immediate write
      to the registered `IWindowPersistenceStore`.
- [x] Selftest `SavePlacement_Idempotent` — call twice, assert store
      is written twice (counter on a test store impl) with the same
      payload.

### 3.6 Phase 3 exit gate

- [x] Grep proves zero deleted enum references remain and zero deleted
      `WindowSpec` field usages remain; remaining `IsShownInSwitchers`
      mentions are WinUI `AppWindow.IsShownInSwitchers` API calls/assertions.
- [x] All Phase 3 selftests registered in both places.
- [x] Triple gate green.

---

## Phase 4 — `WindowStyle` + `CornerStyle` + `WindowLevel` _(spec §10 Phase 4, ~2 weeks)_

Fourth breaking-change cluster: removes `IsAlwaysOnTop`.

### 4.1 `WindowStyle` (spec §6.1)

- [x] Add `public enum WindowStyle { Default, None, ToolWindow }`.
- [x] Add `public WindowStyle Style { get; init; } = WindowStyle.Default;`
      to `WindowSpec`.
- [x] Apply path per spec §6.1 table:
      `Default` → `SetBorderAndTitleBar(true, true)`;
      `None` → `SetBorderAndTitleBar(false, false)` + `WS_POPUP` flag scrub;
      `ToolWindow` → `SetBorderAndTitleBar(true, true)` + `WS_EX_TOOLWINDOW`.
- [x] `ToolWindow` default: if the developer did not explicitly set
      `ShowInTaskbar`, default it to `false` (spec §6.1 caveat).
      Implement as a "if user didn't set" check via a sentinel or by
      computing the effective value in the apply path.
- [x] `Validate()` warns (does not throw) when
      `Style == None && IsMovableByBackground == false` (spec §6.1).
      Route through `DiagnosticLog.Warning(LogCategory.Hosting, ...)`.
- [x] Selftest `WindowStyle_None_Borderless` — assert HWND has no
      `WS_BORDER` / `WS_CAPTION` / `WS_SYSMENU`.
- [x] Selftest `WindowStyle_ToolWindow_HidesTaskbar` — assert
      `WS_EX_TOOLWINDOW` is set and `ShowInTaskbar` defaults `false`
      when unset by user.
- [x] Selftest `WindowStyle_RuntimeUpdate` — flip Default → None →
      Default, assert HWND style bits follow.

### 4.2 `WindowCornerStyle` (spec §6.2)

- [x] Add `public enum WindowCornerStyle { Default, Square, Rounded, RoundedSmall }`.
- [x] Add `public WindowCornerStyle CornerStyle { get; init; } = WindowCornerStyle.Default;`
      to `WindowSpec`.
- [x] Apply via `DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ...)`
      with the four values. P/Invoke declaration in
      `src/Reactor/Hosting/Interop/` (or wherever existing DWM
      P/Invokes live — verify before adding a new file).
- [x] Windows 10 graceful no-op (the API silently fails / has no
      effect — the `Default` mapping is "OS chooses"). Document in
      xmldoc + the windows guide.
- [x] Selftest `CornerStyle_Apply` — set each enum value, assert
      `DwmGetWindowAttribute` round-trip matches (on Win11). Skip
      gracefully on Win10 with a clear skip reason.

### 4.3 `WindowLevel` (spec §6.4)

- [x] Add `public enum WindowLevel { Normal, Floating, AlwaysOnTop }`.
- [x] Add `public WindowLevel Level { get; init; } = WindowLevel.Normal;`
      to `WindowSpec`.
- [x] **Remove** `IsAlwaysOnTop`. Sweep references; convert
      `IsAlwaysOnTop = true` → `Level = WindowLevel.AlwaysOnTop`.
- [x] Apply path:
      `Normal` → strip `WS_EX_TOPMOST`;
      `Floating` → re-assert `SetWindowPos(HWND_TOP)` whenever an
      app-sibling owned window activates;
      `AlwaysOnTop` → `SetWindowPos(HWND_TOPMOST)`.
- [x] `Floating` re-uses spec-036 §9 owned-window registry; verify
      no double-registration of listeners (R6).
- [x] Open question §11 Q1 resolution: `Floating` also stays above the
      **owner** (matches WPF `ToolWindow`). Pin in selftest.
- [x] Selftest `WindowLevel_AlwaysOnTop_StyleBitSet` — assert
      `WS_EX_TOPMOST` is on the HWND.
- [x] Selftest `WindowLevel_Floating_AboveSiblings` — open two app
      windows (one `Floating`), activate the non-floating one, assert
      the floating one re-asserts above it via the registry listener.
- [x] Selftest `WindowLevel_Floating_AboveOwner` — open owner + owned
      `Floating`, activate owner, assert floating stays above. Pins
      open question §11 Q1.
- [x] Selftest `WindowLevel_RuntimeFlip` — Normal → AlwaysOnTop →
      Normal, assert style bits follow.

### 4.4 Phase 4 sample apps (spec §10 Phase 4)

- [x] `samples/apps/command-palette-window/` — PowerToys-Run lookalike:
      `WindowStyle.None`, `IsMovableByBackground`, `WindowLevel.AlwaysOnTop`,
      `ShowInTaskbar = false`, `ShowInSwitcher = false`,
      `WindowStartPosition.CenterOnCurrent`, `CornerStyle.Rounded`.
      Must demonstrate ≤ ~7 fields on the spec (spec §2 G5).
- [x] `samples/apps/tool-palette/` — Photoshop-style:
      `WindowStyle.ToolWindow`, `WindowLevel.Floating`,
      `CornerStyle.RoundedSmall`, opens owned by main window.
- [x] Both samples added to `Reactor.slnx`.
- [x] At least `command-palette-window` builds AOT-clean
      (`-p:PublishAotInternal=true`) with zero new warnings (R13).

### 4.5 Phase 4 exit gate

- [x] Grep proves zero deleted `WindowSpec.IsAlwaysOnTop` field/usages remain
      in code/samples/generated guide docs; remaining literal mentions are
      WinUI platform API use and historical spec/tracker text.
- [x] All Phase 4 selftests registered in both places.
- [x] Triple gate green.

---

## Phase 5 — `SizeToContent` _(spec §10 Phase 5, ~1 week)_

### 5.1 `WindowSizeToContent` (spec §6.3)

- [x] Add `public enum WindowSizeToContent { Manual, Width, Height, WidthAndHeight }`.
      (Spec-exact match with WPF's `SizeToContent` per parity table.)
- [x] Add `public WindowSizeToContent SizeToContent { get; init; } = WindowSizeToContent.Manual;`
      to `WindowSpec`.
- [x] Apply path: subscribe to root `FrameworkElement.SizeChanged`
      (fall back to `LayoutUpdated` if the root chains through a
      `ScrollViewer`). On each firing:
      - Compute required AppWindow size = content desired size +
        non-client insets via `AdjustWindowRectExForDpi`.
      - Compare against current AppWindow size — gate (R5) only resize
        on actual divergence.
      - Honor `Min/Max` constraints throughout — min/max wins per
        §11 Q2 resolution.
      - Set a "we're applying" flag during `AppWindow.Resize` to
        prevent the SizeChanged → resize → SizeChanged feedback loop
        (R5).
- [x] `Validate()` cross-checks:
      - `AspectRatio != null && SizeToContent != Manual` → throw.
      - `SizeToContent != Manual && WindowState == Maximized` initial →
        warn (sizing no-ops while maximized).
- [x] Document the "one frame of content-too-small flash" caveat in
      xmldoc + the windows guide (spec §6.3).
- [x] Selftest `SizeToContent_Width_Tracks` — mount content with known
      desired width, assert window width settles to content width +
      chrome inset within 2 frames.
- [x] Selftest `SizeToContent_Height_Tracks` — same for height.
- [x] Selftest `SizeToContent_WidthAndHeight` — both axes.
- [x] Selftest `SizeToContent_RespectsMinMax` — content desired
      400×300, MinWidth=500, assert window settles at 500×300.
- [x] Selftest `SizeToContent_NoOpWhenMaximized` — set Maximized
      initial state + SizeToContent, assert warning logged and
      window stays maximized.
- [x] Selftest `SizeToContent_AspectRatio_BothRejected` — assert
      `Validate()` throws.
- [x] Selftest `SizeToContent_NoReentrancy` — instrument the apply
      path to assert it does not recursively trigger itself.

### 5.2 Phase 5 exit gate

- [x] All Phase 5 selftests registered in both places.
- [x] Triple gate green.

---

## Phase 6 — `TaskbarItem` facade + `Description` _(spec §10 Phase 6, ~3 days)_

Purely additive.

### 6.1 `TaskbarItem` facade (spec §6.5)

- [x] Add `src/Reactor/Hosting/Shell/TaskbarItem.cs`:
      ```csharp
      public sealed class TaskbarItem
      {
          public TaskbarProgress Progress { get; }
          public TaskbarOverlay Overlay { get; }
          public string? Description { get; set; }
          public void SetThumbnailToolbar(IReadOnlyList<ThumbnailToolbarButton> buttons);
          public void ClearThumbnailToolbar();
      }
      ```
- [x] `Description` → `ITaskbarList3::SetThumbnailTooltip`. P/Invoke
      declaration in the existing `Shell/` interop area.
- [x] On `ReactorWindow`: expose `TaskbarItem TaskbarItem { get; }`.
      Keep existing shortcuts (`Progress` / `Overlay` /
      `SetThumbnailToolbar` / `ClearThumbnailToolbar`) — they return
      the same instances the facade exposes (no behavior change).
- [x] Selftest `TaskbarItem_Description_RoundTrip` — set Description,
      read back via `ITaskbarList3::GetThumbnailTooltip` (if exposed;
      else just assert the call succeeded without throwing).
- [x] Selftest `TaskbarItem_ProgressRegression` — re-run the existing
      `Progress` selftest via the new facade-accessor path to confirm
      no behavior change.

### 6.2 Phase 6 exit gate

- [x] Selftests registered in both places.
- [x] Triple gate green.

---

## Phase 7 — TitleBar inference + `BackdropKind.Transparent` + picker helpers _(spec §6.6 + §7.1 + §8.3)_

### 7.1 `ExtendsContentIntoTitleBar` becomes `bool?` (spec §6.6 / §9.2)

- [x] Change `WindowSpec.ExtendsContentIntoTitleBar` from `bool` to `bool?`,
      default `null`.
- [x] `TitleBarDescriptor.OnMount`: if spec value is `null`, set
      `Window.ExtendsContentIntoTitleBar = true` (the descriptor is
      authoritative). If `true` / `false`, the explicit setting wins.
- [x] Sweep `src/`, `tests/`, `samples/` for callers that **read**
      the field — every such read needs a null check or `.GetValueOrDefault(false)`.
      Most call-sites that **set** `true` can drop the line entirely.
- [x] Document the hot-reload limitation: changing the field type
      across an HR boundary is not supported by `ReactorHotReloadCopier`
      (R14). xmldoc note only; no code change.
- [x] Selftest `TitleBar_ImplicitExtends` — mount tree with `TitleBar(...)`
      as root, spec `ExtendsContentIntoTitleBar = null`, assert
      `Window.ExtendsContentIntoTitleBar == true`.
- [x] Selftest `TitleBar_ExplicitFalseOverrides` — same tree but spec
      `ExtendsContentIntoTitleBar = false`, assert `Window.ExtendsContentIntoTitleBar == false`
      (caller explicitly wants the system chrome).
- [x] Selftest `TitleBar_NoElement_NullStaysFalse` — tree without
      `TitleBar(...)`, spec `null`, assert `Window.ExtendsContentIntoTitleBar == false`.

### 7.2 `BackdropKind.Transparent` (spec §7.1)

- [x] Add `Transparent` to the `BackdropKind` enum.
- [x] Apply path: instantiate WinAppSDK transparent backdrop when available; current referenced SDK does not expose `TransparentBackdrop`, so `Transparent` logs a warning and falls back to no backdrop.
- [x] On Win10 builds where unsupported, log warning via
      `DiagnosticLog.Warning(LogCategory.Hosting, ...)` and fall back
      to "no backdrop" (§11 Q5 resolution).
- [x] Selftest `BackdropTransparent_Apply` — assert
      `Window.SystemBackdrop is TransparentBackdrop` on supported
      builds.

### 7.3 `UseFilePickerAsync` / `UseFolderPickerAsync` (spec §8.3)

- [x] Add `FilePickerOptions` / `FolderPickerOptions` records (filters,
      suggested-folder, etc. — minimum viable shape; pickers are out
      of scope for windowing beyond the `InitializeWithWindow` helper).
- [x] Add `RenderContext.UseFilePickerAsync(FilePickerOptions)` →
      `Task<StorageFile?>`.
- [x] Add `RenderContext.UseFolderPickerAsync(FolderPickerOptions)` →
      `Task<StorageFolder?>`.
- [x] Implementation: `UseWindow().NativeWindow.GetWindowHandle()` →
      `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)`.
      **Must run on the UI thread**; throw if called off-thread (R12).
      **Must use the owning window's HWND**; do not accept arbitrary
      HWND parameters (R12).
- [x] Selftest `FilePicker_InitializesWithWindow` — use a mock /
      `IPickerService` test double; assert `InitializeWithWindow` was
      called with the owning HWND.
- [x] Note in the PR description: §8.3 is per spec a candidate to split
      into its own spec (§11 Q5 in the source spec 053; not this spec —
      054 doesn't have an explicit Q on it, but the §10 phase wording
      flags it). If scoping requires, lift this section out into a
      follow-up spec; otherwise land here.

### 7.4 Phase 7 exit gate

- [x] Selftests registered in both places.
- [x] Triple gate green.

---

## Phase 8 — Documentation uplift

### 8.1 `docs/_pipeline/templates/windows.md.dt` rewrite

- [x] Add sections for every shipped feature: ResizeMode, AspectRatio,
      IsMovableByBackground + Drag(false), Position read-back +
      PositionChanged, ZOrderChanged, CenterOnCurrent, WithPersistence
      + SavePlacement, ShowInTaskbar / ShowInSwitcher split, WindowStyle,
      CornerStyle, WindowLevel, SizeToContent, TaskbarItem facade +
      Description, ReactorDisplay + UseDisplays, UseFilePickerAsync.
- [x] Each section: short prose + minimal runnable snippet + xmldoc-style
      caveats.
- [x] Update the existing "Tips" + "Next Steps" sections to reference
      the new pages.
- [x] WPF / UWP parity callout box per the research table above — gives
      WPF / UWP devs an at-a-glance "where did my favorite API go?"

### 8.2 New `docs/_pipeline/templates/windowing-advanced.md.dt`

Tier-C workaround documentation per spec §7.

- [x] Header + intro: "These scenarios live outside the framework
      contract. We document the recipe but do not ship the primitive."
- [x] FancyZones-style overlay recipe via HWND interop +
      `WS_EX_LAYERED` (§7.1).
- [x] HUD aesthetic recipe (§7.4): `WindowStyle.None` +
      `BackdropKind.DesktopAcrylic` (dark tint) + custom `TitleBar(...)`
      + `WindowLevel.Floating`. Reference the
      `samples/apps/hud-overlay/` sample (Phase 10.3).
- [x] Arbitrary corner-radius via `SetWindowRgn` (§7.2): document the
      tradeoffs (loses DWM shadow, jaggies, redraw cascade), with a
      "don't do this unless you really need it" disclaimer.
- [x] "Cannot deliver" list — true transparency, NSWindow-level stack,
      vibrancy beyond `SystemBackdrop` — pointing readers at the
      platform-level issues.

### 8.3 Compile + verify

- [x] Run `mur docs compile`; verify `docs/guide/windows.md` and
      `docs/guide/windowing-advanced.md` regenerate cleanly with no
      template-vs-output drift.
- [x] Verify all `winui-ref:` links resolve to current Windows App SDK
      docs (`AppWindow`, `OverlappedPresenter`, `DwmSetWindowAttribute`,
      `WM_SIZING`, `GetCursorPos`/`AppWindow.Move`, `ITaskbarList3`).

### 8.4 Changelog / migration note

- [x] Add the spec §9.1 + §9.2 breaking-change table verbatim to the
      changelog or `docs/guide/migrations/054-windowing-evolution.md`,
      one-line migration per removed field.

---

## Phase 9 — Skills (both forms — loose-file + plugin)

### 9.1 Loose-file: `skills/windowing.md`

- [x] New file with sections per Phase 8.1.
- [x] Three recipes inline: Command Palette, Tool Palette, Media Player
      (aspect-locked).

### 9.2 Plugin: `plugins/reactor/skills/reactor-windowing/SKILL.md`

- [x] New skill folder matching the existing plugin-skill conventions
      (front-matter, when-to-invoke triggers like "open a window",
      "make a window draggable", "remember window position", "tool
      palette", "command palette").
- [x] Same recipe content as `skills/windowing.md`.

### 9.3 Update existing skills for renamed fields

- [x] `plugins/reactor/skills/reactor-getting-started/SKILL.md` — sweep
      for `IsResizable`, `IsShownInSwitchers`, `IsAlwaysOnTop`,
      `RestoreFromPersistence`; update.
- [x] `plugins/reactor/skills/reactor-dsl/SKILL.md` — same sweep.
- [x] `skills/dsl-reference.md` (loose) — same sweep.
- [x] Any other skill with a window-related code snippet (`grep -rli
      "WindowSpec" skills/ plugins/reactor/skills/`).

---

## Phase 10 — Sample apps

### 10.1 `samples/apps/command-palette-window/`

Built in Phase 4.4; verify here that it actually runs end-to-end and
ships with a README.

- [x] README documents the ≤7 fields shape (spec §2 G5 promise).
- [x] Includes a screenshot note (screenshots not bundled by sample convention).
- [x] AOT-clean publish (`-p:PublishAotInternal=true`).

### 10.2 `samples/apps/tool-palette/`

- [x] Same — README, screenshot note, JIT-run verified. AOT publish optional
      and intentionally skipped (not all samples need AOT per critique #9;
      one sample's enough to prove the path).

Deferred — recipes covered in docs/guide/windowing-advanced.md.

### 10.3 `samples/apps/hud-overlay/` (optional)

Tier-C composition recipe per spec §7.4. Lower priority — ship if time
allows; if cut, leave the recipe in the docs only.

- [ ] If shipped: README + screenshot + reference from
      `windowing-advanced.md`.

Deferred — recipes covered in docs/guide/windowing-advanced.md.

### 10.4 `samples/apps/media-player-aspect/` (optional)

Demonstrates runtime `UseWindowAspectRatio` swap.

- [ ] If shipped: README + screenshot + reference from `windows.md`.

---

## Phase 11 — Devtools, final gate, post-merge cleanup

### 11.1 Devtools windows surface (R15 / critique #4)

- [x] Verify `WindowRegistry.Snapshot()` still produces valid JSON
      after the field renames. Sweep the snapshot serializer for any
      reference to removed fields. Verified: `WindowInfo` / devtools
      projections expose no removed `WindowSpec` fields.
- [x] Add the new live values to the snapshot where useful:
      `Position`, `Level`, `Style`, `ResizeMode`, `ShowInTaskbar`,
      `ShowInSwitcher`. Deferred: `WindowInfo` does not currently
      surface these values, and growing the snapshot is out of Phase 11
      scope; current MCP JSON uses the existing devtools serializer path.
- [x] Verify devtools `windows.open` command can still construct a
      valid `WindowSpec` from its JSON arguments. It never accepted
      `StartPosition` (schema is `component` / `title` / `width` /
      `height` / `key` only), so no numeric `RestoreFromPersistence = 4`
      reject path is applicable.
- [x] If no full window inspector exists, explicitly record "verified
      no inspector update needed" in the PR description.

### 11.2 Cross-reference + grep sweep

- [x] `grep -rn "IsResizable\|IsShownInSwitchers\|IsAlwaysOnTop\|RestoreFromPersistence" src/ tests/ samples/ docs/ skills/ plugins/`
      returns **zero residual hits** outside WinUI presenter/AppWindow API
      calls/assertions and historical spec/migration/task-tracker docs.
- [x] `grep -rn "WindowSpec\b" docs/` matches the new field shape only
      for live guides/templates; old-shape mentions are historical spec
      text or migration-guide "Before" examples.
- [x] All TODOs / FIXMEs added during this work are either resolved or
      converted to issues. Verified grep returned no matching TODO/FIXME.

### 11.3 Final triple gate

- [x] `dotnet build Reactor.slnx -p:Platform=x64` — clean.
- [x] `dotnet test tests/Reactor.Tests -p:Platform=x64 --no-build` —
      green: 9,202 passed / 62 skipped / 0 failed (baseline match).
- [x] `dotnet run --project tests/Reactor.AppTests.Host -p:Platform=x64 -- --self-test` —
      green: 1,118 total fixtures, 0 failures (baseline 1,069 + 49 new).
- [x] One sample AOT publishes clean
      (`samples/apps/command-palette-window/` with `PublishAotInternal=true`).
- [x] `mur docs compile` is a no-op after the docs phase (no drift).
      Verified with `--no-screenshots --no-ai` (screenshot capture has a
      pre-existing unrelated stack-overflow in the spec-048
      `extending-reactor-controls` sample app's `StarMeter` — same on
      `origin/main` and unrelated to spec-054). `git diff docs/guide/*.md`
      is empty after a fresh compile.

### 11.4 Spec close-out

- [x] Record Phase 0.1 open-question resolutions in spec 054 (append at
      the bottom of the spec, or a sibling
      `docs/specs/054/decisions.md` if spec is locked).
- [x] Update spec 054 status from "Proposed" to "Implemented".
- [x] Cross-reference spec 036 — note that spec 054 supersedes the
      `IsResizable` / `IsShownInSwitchers` / `IsAlwaysOnTop` /
      `RestoreFromPersistence` lines in spec 036.

---

## Appendix — Tasks intentionally not in this checklist

Mirroring spec §12 + Appendix A + critique #3 — listed once more here
so a future reviewer can verify nothing slipped in:

- No `WindowMessageReceived` public surface (spec §7.6 deferred).
- No `Window.JumpList` / `ICustomDestinationList` surface (spec §10).
- No `Window.ShowDialog()` / modal-top-level (spec §2 N1 / §12).
- No `IsTransparent` / `WS_EX_LAYERED` / `LWA_COLORKEY` (spec §7.1).
- No arbitrary continuous `CornerRadius` (spec §7.2).
- No NSWindow-style `WindowLevel.Overlay` / `.statusBar` (spec §7.3).
- No `WindowStyle.Hud` (spec §7.4).
- No vibrancy beyond `SystemBackdrop` (spec §7.5).
- No splash-screen surface (spec §12).
- No `Window.Cursor` (Appendix A).
- No `Window.AllowsTransparency` / WPF-`WindowChrome` shape (Appendix A).
- No cross-process window interop / Islands hosting (spec §12).

If a future feature ask matches any of these, open an issue rather
than expanding 054's scope.




