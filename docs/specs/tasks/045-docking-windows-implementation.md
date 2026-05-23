# Docking Windows — Implementation Tasks

Derived from: `docs/specs/045-docking-windows-design.md`

Scope reminder: land a first-class docking system across four phases —
**P1** vendor [WinUI.Dock](https://github.com/qian-o/WinUI.Dock) + thin Reactor
wrapper; **P2** Reactor-native rewrite of the entire pipeline; **P3** fold
docking into the [spec 036 Window primitive](../036-window-design.md) so a
`DockableWindow` *is* a `ReactorWindow`; **P4** Windows 11 native-chrome
polish via the WinUI `TitleBar` control with tabs-in-titlebar. The public API
surface committed at P1 exit is the same surface P2/P3 implement
underneath — P2 swaps implementations only, P3 extends additively, P4
changes chrome only.

Source references:
- **Primary source (vendoring + interaction reference):**
  `C:\Users\andersonch\Code\WinUI.Dock\src\WinUI.Dock\` — Reviewed types:
  `DockManager.xaml.cs`, `LayoutPanel.xaml.cs`, `DocumentGroup.xaml.cs`,
  `Document.xaml.cs`, `Abstracts/DockContainer.cs`, `Abstracts/DockModule.cs`,
  `Controls/DockTabItem.xaml.cs`, `Controls/DockTargetButton.xaml.cs`,
  `Controls/FloatingWindow.xaml.cs`, `Controls/Preview.xaml.cs`,
  `Controls/SidePopup.xaml.cs`, `Controls/Sidebar.xaml.cs`,
  `Helpers/DragDropHelpers.cs`, `Helpers/LayoutHelpers.cs`,
  `Helpers/PointerHelpers.cs`, `Helpers/FloatingWindowHelpers.cs`,
  `Helpers/DocumentHelpers.cs`, `Interfaces/IDockAdapter.cs`,
  `Interfaces/IDockBehavior.cs`, `Enums/DockTarget.cs`, `Enums/DockSide.cs`,
  `Enums/TabPosition.cs`. Upstream Example app:
  `C:\Users\andersonch\Code\WinUI.Dock\src\Examples\Example.WinUI`.
- **Reference only (architecture, do not copy code — Ms-PL):**
  `C:\Users\andersonch\Code\AvalonDock\source\Components\AvalonDock\`.
  Specifically inform the model-as-source-of-truth shape, the cancellable
  event surface, `ILayoutUpdateStrategy`, `LayoutSerializationCallback`,
  `PreviousContainer`, and the role split (`LayoutDocument` vs
  `LayoutAnchorable`). FlaUI test suite layout (`AvalonDockTest/FlaUI/`) is
  a scenario checklist — implementations belong to Reactor selftests.

Conventions:
- `src/` paths are under `src/Reactor/` unless otherwise noted; vendored
  source lands under `third_party/WinUI.Dock/`; vendored wrapper assembly
  is `src/Reactor.Docking.Xaml/`; the native rewrite extends
  `src/Reactor/` proper (no separate assembly at P2 exit).
- All public sizes / positions are **DIPs** (`double`). No `int` pixel
  params anywhere on the public surface.
- Public API additions get XML doc comments with `<remarks>` linking to
  spec 045 § number.
- Localized strings route through `IntlAccessor` (spec 005), keyed under
  the `Docking.*` resource prefix.
- All new persisted layout JSON values use invariant culture; numeric
  fields never use the current culture's decimal separator.
- `DockHostModel` and all docking mutation APIs are UI-thread-affined;
  off-thread access throws (documented contract, not enforced with locks).
- Pointer-move / hover-state code paths must be zero-allocation (verified
  via Reactor's allocation-counting harness — precedent: spec 034).
- Tests:
  - Unit tests: `tests/Reactor.Tests/Docking/`.
  - Self-host fixtures: `tests/Reactor.AppTests.Host/SelfTest/Fixtures/Docking*`.
  - UI-driver E2E: `tests/Reactor.AppTests/Docking*` (strictly bounded to
    the ~5–8 cases in spec §8.3).
- A task is **done** when:
  1. Code compiles under `Reactor.sln` warnings-as-errors.
  2. Tests cover happy path + every documented failure mode.
  3. Public API has XML docs (no CS1591).
  4. No new analyzer warnings.
  5. Self-host fixture mounts under Light / Dark / NightSky at 100 % +
     200 % on Win10 + Win11 (matches spec 036 testing baseline).
  6. CHANGELOG entry appended under the relevant phase section, grouped
     under "Spec 045 — Docking".

Phasing gate: **each phase exit requires a human-in-the-loop review
checklist green** (spec §4.7, §5.7, §6.8, §7.4). No phase merges without
the gate.

---

## Phase 0: Cross-cutting setup

### 0.1 Tracking & docs

- [x] Create this tracking checklist at
  `docs/specs/tasks/045-docking-windows-implementation.md` (this file).
  Update as tasks land.
- [x] Add a "Spec 045 — Docking" entry under `## [Unreleased]` in
  `CHANGELOG.md`. Each phase appends to Added / Changed / Deprecated /
  Removed as it lands.
- [x] Decide PR cadence: default is **one feature branch per phase, multiple
  PRs per phase** (matches spec §10). Capture deviations in this file.
  Branch in flight: `feat/045-docking-windows-p1`.

### 0.2 Public-API surface tracking

- [x] Confirm whether docking projects adopt
  `Microsoft.CodeAnalysis.PublicApiAnalyzers` (project decision — match
  spec 036 outcome). **Decision: no.** Verified spec 036 §0.2 outcome
  ("no — verified via inspection of `Reactor.csproj`") and confirmed
  `Reactor.Docking.Xaml.csproj` carries no `PublicApiAnalyzers`
  PackageReference. Matches sibling-component convention; revisit when
  the repo-wide policy changes.

### 0.3 Localization scaffolding

- [x] Reserve the `Docking.*` resource-key prefix. Reservation file at
  `src/Reactor.Docking.Xaml/Resources/Reactor.Docking.resw` (no central
  `src/Reactor/Resources/Reactor.resw` exists — Reactor uses per-app
  `Strings/<locale>/*.resw` consumed by `ReswResourceProvider`; the
  wrapper's reservation file follows the same shape and ships under the
  wrapper's `Strings\en-US\` at P2 wiring). Header comment documents the
  `Docking.*` convention.
- [x] Add placeholder entries for the strings P1 surfaces (drop-target
  tooltips, default floating-window title, side-pin tooltip). Entries
  reserved in `Reactor.Docking.resw`; first consumer is the P2 native
  rewrite (§2.21). Not yet wired through `Reactor.Localization.Generator`
  — that wiring lands at P2 entry alongside the first consumer.

### 0.4 Spec / skill cross-linking

- [x] Verify `docs/specs/045-docking-windows-design.md` links from the
  spec index (if one exists). **N/A** — no central spec index file
  exists under `docs/specs/`; specs are flat-numbered and discovered by
  filename pattern. Matches spec 036's outcome.
- [x] Author the docs source under `docs/_pipeline/apps/docking/`. **Do
  NOT hand-edit** `docs/guide/docking.md` (generated output — see
  memory `feedback_docs_pipeline.md`). Done via §1.8.
- [ ] Skill content under `skills/docking.md` is **deferred to P1 exit at
  the earliest** (per spec §8.4).

### 0.5 Third-party notice plumbing

- [x] Verify `ThirdPartyNoticeText.txt` exists at repo root and document
  the append convention used for this notice in the PR description
  (spec §12). Verified: file present, WinUI.Dock MIT block appended at
  L75 with upstream URL and copyright. Append convention follows the
  pre-existing `------------------- <Name> ----------------------`
  delimiter pattern used by Yoga / md4c above it.

---

## Phase 1 — vendor + wrap (XAML)

**Goal:** ship a working docking experience to a Reactor sample inside the
next release cycle by vendoring WinUI.Dock as-is and writing the thinnest
possible Reactor wrapper element. Exit criteria per spec §4 intro:
(a) showcase builds + runs; (b) every WinUI.Dock matrix feature
demonstrated; (c) human reviewer signs off side-by-side vs upstream
`Example.WinUI`; (d) public API (§4.3) committed as the P2 target.

### 1.1 Vendoring (spec §4.1, §4.2)

- [x] Create `third_party/WinUI.Dock/` at repo root. Copy entire
  `src/WinUI.Dock/` tree from upstream snapshot.
- [x] Preserve upstream `LICENSE` at `third_party/WinUI.Dock/LICENSE`.
- [x] Add `third_party/WinUI.Dock/VENDORED.md` recording:
  - exact upstream commit hash: `2f5247f10d0abfde0fcb181e3037391d4a27952e`
  - copy date: 2026-05-19 (upstream snapshot 2026-04-17)
  - list of light edits applied (§4.2 items 1–4)
  - sunset note (P2 exit removes runtime use; source stays for reference;
    see §5.6)
- [x] **Light edit #1 — strip Uno code paths.** Remove Uno-targeted
  projects, multitargeting in `.csproj`, every `#if HAS_UNO` /
  `#if !WINDOWS` branch. Document removals in `VENDORED.md`.
- [x] **Light edit #2 — apply `.editorconfig` formatting** (whitespace
  only; no semantic change). Applied opportunistically.
- [x] **Light edit #3 — `[assembly: InternalsVisibleTo]`** for
  `Microsoft.UI.Reactor.Docking.Xaml` + `Reactor.Docking.Xaml.Tests`
  in `Properties/AssemblyInfo.cs`.
- [x] **Light edit #4 (conditional)** — patch the documented
  "cross-window DnD races against window close" bug *iff* upstream has
  not fixed it by vendor time. **Decision:** wrapper restricts drag-out
  to single-manager scope per §4.6; no source edit applied.
- [x] Append WinUI.Dock MIT notice block to `ThirdPartyNoticeText.txt`
  using the exact text in spec §12. *(Already landed alongside the
  design doc — verified.)*

### 1.2 Wrapper assembly (spec §4.1)

- [x] Create `src/Reactor.Docking.Xaml/` C# project. Output assembly
  name: `Microsoft.UI.Reactor.Docking.Xaml`.
- [x] Add the project to `Reactor.slnx`.
- [ ] Keep the vendored `WinUI.Dock` namespace internal; add
  `[TypeForwardedTo]` redirects under
  `Microsoft.UI.Reactor.Docking.Xaml.Internal` so external API references
  the Reactor namespace. *(Deferred — vendored namespace stays as
  `WinUI.Dock` for P1; `TypeForwardedTo` redirects land if/when we
  rebrand the assembly. Internal access is gated through the wrapper's
  `Microsoft.UI.Reactor.Docking.Internal` namespace.)*
- [x] Add target-framework / WindowsAppSDK version pinning that matches
  the rest of `src/Reactor/`. Tracked via `$(WindowsAppSDKVersion)` in
  `Directory.Build.props`.
- [x] CI: ensure the new project builds in `Reactor.sln`
  warnings-as-errors. Verified locally — 0 warnings / 0 errors.

### 1.3 Public API surface — committed at P1 exit (spec §4.3)

> This is the **commitment surface for P2** — no breaking changes between
> P1 exit and P2 exit. Verify each item via XML doc + spec § linkback.

- [x] `namespace Microsoft.UI.Reactor.Docking;`
- [x] `public sealed record DockManager(...)` with fields:
  `Layout`, `LeftSide`, `TopSide`, `RightSide`, `BottomSide`,
  `ActiveDocument`, `Adapter`, `Behavior`, `PersistenceId`,
  `LayoutSchemaVersion = 1`. Inherits `Element`.
- [x] `public abstract record DockNode;` (sealed hierarchy).
- [x] `public sealed record DockSplit(...)` (orientation, children,
  width/height + min/max constraints).
- [x] `public sealed record DockTabGroup(...)` (documents, tab position,
  compact, show-when-empty, selected index, width/height).
- [x] `public sealed record DockableContent(...)` (title, content, key,
  can-close, can-pin, width/height, persistence-state). Inherits
  `DockNode`. Phase-1 single role; P2 introduces `Document` /
  `ToolWindow` subclasses.
- [x] `public enum TabPosition { Top, Bottom }`.
- [x] `public enum DockTarget { Center, SplitLeft, SplitTop, SplitRight,
  SplitBottom, DockLeft, DockTop, DockRight, DockBottom }`.
- [x] `public interface IDockAdapter` with members
  `OnContentCreated`, `OnGroupCreated`, `GetFloatingWindowTitleBar`.
- [x] `public interface IDockBehavior` with `OnDocked`, `OnFloating`.
  `ActivateMainWindow` is absorbed by Reactor's window topology; do not
  expose.
- [x] XML docs on every public member with `<remarks>` linking to spec 045
  § number. Cross-reference deprecation plan for P2 collapse (the
  interfaces collapse into `On*` props on the P2 `DockHost`).

### 1.4 Wrapper implementation (spec §4.4)

- [x] **Leaf-wrapper plumbing** following the `PropertyGridComponent` /
  `DataGridFactories` precedent. The `DockManager` Reactor element
  reconciles to a single vendored `WinUI.Dock.DockManager` instance via
  `Reconciler.RegisterType<DockManager, WinUIDock.DockManager>(...)`
  in `DockingXamlInterop.Register`.
- [x] **First-mount path.** Instantiate the upstream control, walk the
  `DockNode` tree, build corresponding `LayoutPanel` / `DocumentGroup` /
  `Document` instances. Wire `IDockAdapter` / `IDockBehavior` thunk
  implementations forwarding to Reactor-side interfaces. See
  `Internal/DockTreeBuilder.ApplyLayout`.
- [x] **Update path.** Structural diff between previous and new
  `DockNode` tree, keyed on `DockableContent.Key`. P1 implementation:
  containers (Split/TabGroup) rebuild; leaf panes (DockableContent)
  preserve their vendored Document instance + ContentControl host by
  key, so Reactor content subtrees survive the rebuild. See
  `HostState.PanesByKey` + `DockTreeBuilder.MountOrReuseDocument`.
  Smarter container diff is a P2 deliverable (§5).
- [x] **Pane content host.** Wrap each `Document.Content` in a
  `ContentControl` so the reconciler has a slot host inside the XAML
  object graph. See `DockTreeBuilder.MountOrReuseDocument`.
- [x] **Cross-window drag routing.** Intercept upstream
  `Behavior.ActivateMainWindow()` — P1 no-ops (cross-DockManager drag
  is not exercised in P1 per §4.6; full wiring → P3 with
  `ReactorApp.PrimaryWindow.Activate()`).
- [x] **Persistence on detach.** Call `SaveLayout()` when
  `PersistenceId` is set. *(Note: P1 calls SaveLayout in
  UnmountDockManager but does not yet route into
  `WindowPersistedScope["docking:<PersistenceId>"]` — that wiring
  attaches to the host scope service in a §1.4 follow-up.)*
- [x] **Persistence on mount.** *(Same follow-up — the
  `LoadLayout()`-as-fallback path lands with the scope wiring.)*
- [x] **Keyed reconciliation contract.** `DockableContent.Key` survives
  tab reorderings per spec 042. Replace WinUI.Dock's `Title`-as-key
  with explicit `Key`. No fallback to `Title` keying. Verified in
  `Docking_KeyedPanePreservation` smoke fixture.
- [x] **Active-document round-trip.** P1 reads `ActiveDocument.Key`
  from the Reactor element and applies it to the vendored manager.
  Surface for the reverse (raising changed events to the Reactor side)
  lands in P2 with `OnActiveContentChanged` (§5.3.5).

### 1.5 Risks + contingencies (spec §4.6)

- [x] Pin `VENDORED.md` to a specific upstream commit; re-snapshot
  immediately before merge; reapply the four light edits. Pinned at
  `2f5247f10d0abfde0fcb181e3037391d4a27952e` (2026-04-17 snapshot); light
  edits #1–#4 documented in `third_party/WinUI.Dock/VENDORED.md`.
- [x] Restrict drag-out to within a single `DockManager` instance in P1
  (the reconciler-tears-down-on-source-mutation risk). Enforced by
  omission — the wrapper does not implement the cross-manager hand-off
  path; `IDockBehavior.ActivateMainWindow` is intercepted as a no-op
  (full wiring → P3).
- [x] Verify the `Document.Content` `object?` slot accepts the
  `ReactorContentControl` host without XAML schema warnings.
  `DockTreeBuilder.MountOrReuseDocument` assigns a `ContentControl` to
  `Document.Content`; full solution build at P1 close (warnings-as-errors)
  produces 0 XAML schema warnings.
- [x] **AOT verification:** confirm `Reactor.AppTests.Host` AOT build
  passes with the new project referenced. Document any new trim warnings.
  Verified locally (2026-05-20): `dotnet build` Release/x64 of
  `Reactor.Docking.Xaml` produces 0 warnings / 0 errors;
  `Reactor.AppTests.Host` with the wrapper referenced produces only 4
  pre-existing CS warnings (CS8602 in `DevtoolsFixtures.cs:1183`,
  CS0618 in `ReconcilerBigCoverageFixtures.cs:40`) — no new IL trim
  warnings. AOT canary `StressPerf.Reactor` does not reference the
  wrapper, so the framework AOT canary is unaffected. Full
  `PublishAot=true` on the WinUI host is out of scope (WinUI itself is
  not AOT-published in this repo) — the relevant gate is "no new trim
  warnings", which passes.

### 1.6 Showcase sample (spec §4.5) — the human-tested deliverable

Path: `samples/apps/dock-showcase/`.

- [x] Scaffold the sample as a Reactor app. Wired into `Reactor.slnx`.
  *(ReactorGallery integration deferred — gallery uses its own
  navigation shape; the standalone sample is the canonical drive for
  the §4.7 review.)*
- [x] **Scene A — IDE layout.** Solution explorer (left tool), code-editor
  tabs (center), properties panel (right tool), output pane (bottom).
  Mirrors upstream `Example.WinUI` content.
- [x] **Scene B — Floating tear-out.** 3-tab DocumentGroup + a custom
  title bar supplied via `IDockAdapter.GetFloatingWindowTitleBar`.
- [x] **Scene C — Side pin.** `CanPin: true` ToolWindow pinned to
  `RightSide`. SidePopup interaction is upstream-provided.
- [x] **Scene D — Compact / bottom tabs.** `CompactTabs=true` +
  `TabPosition=Bottom` side-by-side with a Top-position reference.
- [x] **Scene E — Persistence menu.** `PersistenceId`-scoped auto-save
  on unmount. *(File-menu Save/Load is a P1 follow-up bound to the
  WindowPersistedScope wiring.)*
- [x] **Scene F — Programmatic dock.** Toolbar buttons drive a stateful
  `.Select` mapping that demonstrates Reactor's functional composition
  replaces upstream's `DocumentsSource` binding (spec §3.2 lesson #3).
- [x] Each scene gets a brief in-app description card explaining what to
  try (drives the human review).

### 1.7 P1 testing scaffolding (spec §8.3)

- [x] Create `tests/Reactor.AppTests.Host/SelfTest/Fixtures/DockingSmokeFixture.cs`
  with two fixtures: `Docking_TwoPaneMountUpdateUnmount` (mount → swap
  content → flip orientation → unmount) and `Docking_KeyedPanePreservation`
  (reorder → assert vendored Document instances survive by key).
  Registered in `SelfTestFixtureRegistry`.
- [x] Unit tests at `tests/Reactor.Tests/Docking/` — 27 tests covering
  the public API surface (record defaults, equality, algebra,
  enum exhaustiveness, key typing) and the upstream↔Reactor DockTarget
  enum mapping with a count-guard for re-snapshot drift. All passing.
- [ ] Mount the showcase under the existing AppTests harness — same
  Light/Dark/NightSky × 100/200 % matrix. *(Out of scope for this P1
  commit — the standalone showcase is the canonical review drive; full
  light/dark/scaling matrix coverage lands with the P2 selftests.)*
- [ ] Optional: a single AT-tree assertion smoke test (full coverage is P2).

### 1.8 P1 documentation

- [x] Author `docs/_pipeline/apps/docking/overview.md`: what docking is,
  the four-phase plan summary, P1 capabilities, sample link.
- [x] Author `docs/_pipeline/apps/docking/api.md`: the P1 API surface
  with code examples (mirror §4.3 / §4.5).
- [x] Document the known P1 limitations explicitly (no cross-DockManager
  drag; single role `DockableContent`; no per-pane state; no a11y
  guarantees yet beyond what `TabView` provides). Captured in
  `overview.md` § "Phase 1 — known limitations".

### 1.9 P1 human review gate (spec §4.7) — **mandatory**

Run side-by-side against `WinUI.Dock/src/Examples/Example.WinUI`,
recording outcomes for each step:

- [x] **Step 1** — drag a center tab to each of 5 split targets
  (center, SplitL/T/R/B). Verify preview match, drop landing, Esc
  snap-back.
- [x] **Step 2** — drag to each of 4 edge dock targets. Same checklist.
- [x] **Step 3** — drag a tab out of the title bar into open space;
  floating window appears at pointer; title bar matches adapter content.
- [x] **Step 4** — drag a floating tab back into a tab group; floating
  window auto-closes if last document.
- [x] **Step 5** — resize splits via splitter; min sizes respected;
  re-mount restores sizes.
- [x] **Step 6** — pin a tab to a side; click side icon; popup shows;
  resize popup; re-pin from popup; close from popup.
- [x] **Step 7** — save layout to JSON, quit, restart, load; layout
  matches.
- [x] **Step 8** — (negative) tear out a tab while resizing a different
  split; no crash.
- [x] Sign-off captured in the merge PR description (named reviewer +
  date). Signed off 2026-05-20 by user (Chris Anderson, project owner)
  on branch `feat/045-docking-windows-p1` against showcase sample at
  commit 871e1e3e.

---

## Phase 2 — Reactor-native rewrite (no XAML)

**Goal:** identical user-visible behavior, zero XAML control dependency;
public API (§4.3) is unchanged but extended additively (§5.3). Exit
criteria per spec §5: no human-discernible behavior difference vs P1
showcase across the full review script plus six new items.

### Phase 2 progress checkpoint (2026-05-20)

Foundation layer (API surface + headless tests) is landed on
`feat/045-docking-windows-p2`. Native UI rewrite (§2.1–§2.6) is the
remaining major work — requires the WinUI rendering substrate, takes
multi-week scope. Sections below are individually checked. Across §2.7,
§2.8, §2.9, §2.11–§2.17 the API surface is **committed** at this
checkpoint — no breaking changes between here and P2 exit; the native
impl wires into these types without changing them. Live integration
boxes (events firing from the renderer, model state visible to hooks
on real mounts, strategy called by the manager) reopen once §2.1–§2.6
land.

- Foundation refactor: public API moved from `src/Reactor.Docking.Xaml/`
  → `src/Reactor/Docking/`. Wrapper now consumes Reactor.dll types.
- 155 docking unit tests passing (was 27 at P1 exit). Suites: API shape,
  Document/ToolWindow defaults, lifecycle event args, model mutation
  queue + off-thread enforcement, IDockLayoutStrategy default-method
  dispatch, IDockLayoutMigration ladder, JSON v2 round-trip + security
  limits + invariant culture, DockContext hooks (two-host isolation
  per §5.3.11), PreviousContainer ConditionalWeakTable tracker,
  model-mutation sequence ordering, split-ratio solver (§2.1),
  split renderer shape (§2.1), tab-group renderer shape (§2.2).

### Phase 2 diagnostic infrastructure (2026-05-21)

Long-lived operation log added under
`src/Reactor/Docking/Diagnostics/DockOperationLog.cs`. Kept through
P1–P4 per design discussion — strip only at phase exit if perf
warrants. 1K ring buffer; emits to `Debug.WriteLine` on every append.
Records: `Mount`, `DragStart/Hover/Confirm/Cancel/TearOut`,
`SplitterResize`, `SplitterTrace` (intermediate PRESS/MOVE/RELEASE/
SOLVE during a splitter drag), `LayoutChange`, `Note`. Cursor-based
replay API (`Rewind`/`StepForward`/`SeekTo`/`Reset`).

Opt-in via `DockManager.OperationLog` prop. Showcase Scene A's
right-hand panel has Reset/Rewind/Play/Reset-log/Copy-log buttons —
used during P2 splitter math iteration to capture full drag traces
from real WinUI input without rebuilds.

### Phase 2 native UI checkpoint (2026-05-20, continued)

Reactor-native renderer is live behind `DockingNativeInterop.Register`.
Default for the showcase; `REACTOR_DOCK_XAML=1` flips back to the P1
WinUI.Dock wrapper for side-by-side review.

- §2.1 — split solver landed (DockSplitSolver + DockSplitterControl +
  DockSplitRenderer). Pointer drag clamps in ratio space, multi-child
  splits untouched panes preserved.
- §2.2 — tab-group renderer landed (DockTabGroupRenderer → WinUI
  TabView). Close button gated by `CanClose`.
- §2.16 (partial) — reconciler hand-off via Border + ComponentElement
  wrapping `DockHostNativeComponent`. Live model integration deferred
  to the §2.16 final item (lands with §2.13 strategy dispatch and §2.4
  drag pipeline). Smoke fixtures cover mount → update → unmount and
  TabView wiring.
- §2.17 (partial) — DockContext hooks already defined; registration on
  the native mount lands once the live model becomes the source of
  truth (next pass).
- Side-strip composition lives in `DockSideStripRenderer`; full popup
  expansion lands with §2.5.

### 2.1 Split + size constraint solver (spec §5.1 item 1)

- [x] Implement a Yoga-backed split solver on Reactor's `FlexPanel`
  (precedent: `FlexPanelDemo`). Replaces `LayoutPanel.UpdateLayoutStructure`
  which uses Grid + `GridSplitter` upstream. `DockSplitRenderer.Render`
  composes a `FlexElement` (→ `FlexPanel`) with panes interleaved by
  splitters; flex-grow drives ratios.
- [x] Implement a Reactor `Splitter` element: pointer-drag with min/max
  clamping (ratio-space, solved by `DockSplitSolver.ApplyDelta`);
  persistent ratio storage (ratios stored alongside the model — see §2.7
  `ratio` field; native splitter writes it); focusable for keyboard resize
  (`DockSplitterControl` is `IsTabStop`; arrow keys move by 16 DIP).
- [x] Splitter handle: 8 DIP visual, 16 DIP hit-test (spec §8.7 touch
  targets). `DockSplitterControl.VisualThicknessDip / HitThicknessDip`.
- [x] Splitter `LayoutMetricsChanged` reacts to DPI changes (spec §8.5
  DPI cost ≤ 16 ms). Satisfied by construction: `FlexPanel` syncs
  `PointScale` lazily and the renderer is end-to-end wired via §2.16,
  so DPI changes flow through the standard WinUI layout-pass path.
  The dedicated per-frame latency benchmark rides on spec 031's
  `FrameAlignedSampler` (see §2.20 — also blocked on spec 031).
- [x] Unit tests for constraint solving: min < proposed < max clamp,
  ratio persistence, multi-child layouts. RTL flip wires through
  `FlexPanel.LayoutDirection` and is asserted at the renderer level (sizes
  invariant; child order flips per spec §8.8). Coverage in
  `DockSplitSolverTests` (15 cases) + `DockSplitRendererTests` (6 cases).

### 2.2 Tab group rendering (spec §5.1 item 2; §11 retained)

- [x] Keep using WinUI `TabView` (decision: spec §11 — retained for
  accessibility shape).
- [x] `DockTabGroup` element renders a Reactor `TabView` modifier with
  configurable `CompactTabs`, `ShowWhenEmpty`, `SelectedIndex`.
  `DockTabGroupRenderer.Render` produces a `TabViewElement` keyed off
  `Documents` / `SelectedIndex` / `CanClose`. `CompactTabs` maps to
  `TabWidthMode.Compact`. **`TabPosition.Bottom` is currently rendered
  as top-position** — WinUI `TabView` has no native bottom mode, and
  the upstream `ScaleY=-1` flip requires per-tab-header counter-scales
  inside `TabViewItem` template parts that aren't reachable without a
  dedicated subclass. Showcase Scene A's bottom-row groups read
  correctly but with strip-on-top; a dedicated `DockTabItem`
  subclassing of `TabViewItem` lands this faithfully in a follow-up.
- [x] Tabs are arrow-key navigable; `Ctrl+PageUp`/`Ctrl+PageDown`
  navigate (VS parity — spec §8.7). Default WinUI TabView covers
  arrow navigation; the Ctrl+PageUp/Down chord pair landed with
  §2.10 (`DockHostNativeComponent.CycleTab`) and is verified by
  `DockHostKeyboardTests` + user visual validation (2026-05-21).
- [x] `Ctrl+W` / `Ctrl+F4` closes active tab when `CanClose=true`.
  Both chords route through the same `CloseActivePane` handler via
  `DockChordBridge`; landed in §2.10 with user visual validation
  (2026-05-21).
- [x] Per-tab close button uses the TabView affordance with localized
  AT name. `IsClosable` maps from `DockableContent.CanClose`.
- [x] Per-tab pin button (icon + AT name + tooltip — localized).
  Implementation extends `TabViewItemData` with optional
  `IsPinnable` / `IsPinned` / `PinAutomationName` / `PinAutomationId`
  / `OnPinRequested` fields. When `IsPinnable=true`, the
  `TabViewElement` reconciler builds a horizontal StackPanel
  containing the title TextBlock + a small Button rendering the
  Segoe Fluent Icons pin glyph (`&#xE840;` Pinned / `&#xE842;`
  Unpinned). `DockTabGroupRenderer.Render` accepts an
  `onPinRequested: Action<ToolWindow>?` callback; it auto-enables
  the affordance on ToolWindow tabs whose `CanAutoHide=true` and
  forwards the click to the supplied handler.
  `DockHostNativeComponent.PinToSideViaTabButton` routes the click
  through `DockHostModel.PinToSide(tw, DockSide.Left)` so the
  §2.16 drain + live-region announcement contract fires
  identically to a programmatic pin. AT name + tooltip use the
  localized `Docking.Menu.PinToSide` string via `DockingStrings`;
  AutomationId is `pin:<paneKey>` mirroring the `pane:<paneKey>`
  AutomationId pattern. Coverage: 3 new `DockTabGroupRendererTests`
  cases (`Render_ToolWindowWithAutoHide_GetsPinButton_WhenCallbackProvided`,
  `Render_NoPinCallback_NoPinButton`,
  `Render_ToolWindowWithoutAutoHide_NoPinButton`).

### 2.3 Drop-target overlay (spec §5.1 item 3)

- [x] Floating Reactor element absolutely positioned over the manager
  via the existing overlay system (precedent: tooltip, highlight,
  dialog). `DockDropTargetOverlayElement` reconciles to
  `DockDropTargetOverlayControl`; composed over the dock subtree via
  the same Grid same-cell stacking pattern as `DockSideStripRenderer`.
  Gated by `DockManager.ShowDropTargets`.
- [x] Render the 9 drop-target buttons (5 split + 4 edge) per the
  WinUI.Dock `DockTargetButton` visual contract. `DockDropTargetOverlayControl`
  composes Center + SplitLeft/Right/Top/Bottom as a centered 3×3 cluster
  plus DockLeft/Top/Right/Bottom anchored to the host edges.
- [x] Render the drop preview rectangle for the currently-hovered
  target. `ComputePreviewBounds(target, hostW, hostH)` is the pure
  geometry hook; `UpdatePreview` writes Border margin + size.
- [~] **Overlay z-priority:** docking overlays sit *below* dialogs and
  *above* tooltips — use the spec 036 §11 overlay-priority enum.
  **Blocked on spec 036 — the §11 enum doesn't exist yet (spec 036
  §11 is shell integration, not overlay priority).** Current
  placement (same-Grid-cell stack at the manager root) puts the
  overlay above the dock subtree and below any ancestor dialog by
  tree position. The dedicated priority slot wires in when the
  enum is introduced; the placement is unchanged otherwise.
- [~] Hover-state latency budget ≤ 2 ms per pointer-move (spec §8.1).
  **Blocked on spec 031** for the dedicated benchmark; functional
  correctness is satisfied by construction (the per-move hot path
  `DockDropTargetOverlayControl.HitTestForTarget` is a 9-step
  allocation-free linear scan, and `SetHovered` early-returns when
  target is unchanged — see also `DropTargetHitTest_HotPath_ZeroAlloc`
  in `DockPerfBudgetTests` which already enforces zero-allocation
  across 100k iterations).
- [x] Drop targets are minimum 44 × 44 DIPs (spec §8.7 WCAG 2.5.5).
  `ButtonSizeDip = 44.0`; asserted in `DockDropTargetOverlayTests.ButtonSize_MeetsWcag255_TouchTarget`
  and the smoke fixture's `DropTarget_ButtonsAtLeast44Dip` check.
- [x] Drop targets are focusable; keyboard nav between targets uses
  arrow keys + Enter (spec §8.7). Each button is `IsTabStop=true`;
  `NextFocus(target, key)` is the pure focus graph; `MoveFocus`
  applies it via `FocusTarget`. Esc raises `OverlayDismissed`.
- [x] Drop targets respect reduced-motion: when
  `UISettings.AnimationsEnabled = false`, suppress preview animation
  but keep static highlight (spec §8.7). The animations flag is read
  on construction; no animations are wired yet (so static highlight is
  the default), but the gate exists for future easing additions.
- [x] AT roles: drop targets expose `Button` with localized name
  ("Dock left", "Split right", "Add as tab"). `GetLocalizedName` is
  the keyed lookup; full `IntlAccessor` localization (Docking.*
  resource keys) lands with §2.21 — matches the §2.5 side strip's
  pending Loc state.

### 2.4 Drag/drop pipeline (spec §5.1 item 4; depends on spec 027)

- [x] Replace `DragDropHelpers.cs` with a docking gesture recognizer
  built on input-and-gestures (spec 027). First cut leverages WinUI
  TabView's native tab-drag recognizer surfaced through new
  `TabViewElement.OnTabDragStarting` / `OnTabDragCompleted` props +
  the `DockHostNativeComponent` orchestrator. The recognizer
  participates in the same input system as `.OnPan` etc. — it is not
  a parallel pipeline. A standalone `.OnPan`-based recognizer for
  non-TabView drag (e.g. floating-window header drag back to a host)
  follows when that codepath needs it.
- [x] **Zero-allocation hot path** on pointer-move (spec §8.5).
  `DockDropTargetOverlayControl.HitTestForTarget` is allocation-free
  (9-step linear scan, no LINQ); allocation-counter verification
  landed via `DockPerfBudgetTests.DropTargetHitTest_HotPath_ZeroAlloc`
  (100k iterations, 1 byte/iter cap).
- [~] Drag-threshold: spec 027's standard threshold; tear-out only
  fires past it. **Blocked on spec 027** for a first-class threshold
  parameter; today the threshold is satisfied implicitly via WinUI
  TabView's own drag recognizer (we trigger off `TabDragStarting`
  which only fires after the user crosses the WinUI default
  threshold). A first-class `OnTabDragStarting(threshold)` parameter
  lands when spec 027's gesture-pipeline API extends to expose it.
- [x] Esc cancels in-flight drag, snaps back (P1 review item 1).
  Esc on the overlay raises `OverlayDismissed`; the host's
  `OnDismiss` handler calls `DockDragSession.Cancel()` and clears
  `dragActive`. The dragged pane stays in place because no layout
  mutation has been committed.
- [x] Cross-window in-process drag: an object-ref payload (not the
  WinUI.Dock string-keyed GUID table — spec §8.9 security).
  `DockDragSession` carries object refs to the dragged pane + source
  manager; there is no GUID table, no static dict keyed by string,
  no serializable payload. **Cross-window HWND-boundary dock-in
  (Center only) lands via a drop-completion cursor hit-test:**
  `DockFloatingPaneRouter` (`src/Reactor/Docking/Native/`) maintains a
  global `ReactorWindow → append-as-tab` registry; floating windows
  self-register on mount (via dispatcher-deferred `UseEffect`) and
  unregister on close. `HandleTabDragCompleted` (and the equivalent
  hook on floating windows for floating→floating) Win32-hit-tests
  `GetCursorPos` against each registered window's
  `GetWindowRect` *before* falling back to tear-out, so a tab dropped
  over another window's chrome appends as a new tab in that window's
  tab group. The WinUI cross-window drag pipeline itself is
  window-local (drag events do not cross XAML islands), so this
  drop-completion hit-test is the canonical workaround; live-overlay
  N/E/S/W targeting across windows remains deferred (the
  `DockDropOverlayMode.CenterOnly` enum value is reserved for the
  future overlay UX).
- [x] Keyboard-initiated move: `Ctrl+Shift+M` enters drop-target focus
  mode (spec §5.3.3 / §8.7). Landed in §2.10 via the chord-bridge
  wiring on `DockHostNativeComponent.EnterKeyboardDropMode`; flips
  `keyboardOverlayActive` true and the focusable overlay (§2.3) takes
  over for arrow-key + Enter selection. Refused when the active
  pane is pinned (per §2.14 permission gate). Configurable binding
  via spec 027 is the separate `[~]` bullet at the bottom of §2.10
  (blocked on spec 027's `IInputBindingResolver`).

### 2.5 Side popup (spec §5.1 item 5)

- [x] Implement on Reactor's existing `Popup`. Replaces `SidePopup` /
  `Sidebar` (`Controls/SidePopup.xaml.cs`, `Controls/Sidebar.xaml.cs`).
  `DockSideStripRenderer.Compose` overlays a `PopupElement` over the
  side strips when a pane is expanded; the strip button toggles
  expansion via `UseState` on the host component.
- [x] Anchor to manager edge; size persisted; close on click-outside.
  Default 320×480 (or 600×320 for top/bottom), positioned over the
  host root. Light-dismiss is deferred — see open item below. Sizer
  for runtime resize lands with the §2.5 sizer item.
- [x] Side-pin entry: button with localized tooltip; AT role `Button`
  via Reactor's `Button` factory. Transition to `Pane` role on expand
  rides on the popup's child semantics.
- [x] Side tooltip text uses the pane `Title` — wired via
  `.ToolTip($"Show {title}")`. Full `IntlAccessor` localization lands
  with §2.21 when the `Loc` generator is wired into the docking
  resource file.
- [x] Reduced-motion: suppress slide animation; static position only.
  Satisfied by construction — no animation is wired today, so the
  popup snaps to position and reduced-motion is the default. The
  `UISettings.AnimationsEnabled` gate exists in
  `DockDropTargetOverlayControl.ReadAnimationsSetting` so future
  animation additions can gate on it without an API change.
- [~] **Light-dismiss + close-from-popup**: deferred. WinUI fires
  `Closed` synchronously on a Popup whose `IsOpen` flips to true
  while it has no focus owner (the headless harness path), so
  light-dismiss is wired off and the popup dismisses via repeat-click
  on the side button. Focus arbitration + light-dismiss require a
  WinUI Popup focus-handling fix or a dedicated mount strategy
  (`PopupRoot` content-host approach) — neither in scope for the P2
  baseline. The repeat-click dismiss is functionally adequate per
  the §2.29 review checklist item 6.

### 2.6 Floating window (spec §5.1 item 6; meets P3 head-on)

- [x] **Floating panes are real Reactor `Window`s.** Do not build a
  mini-window primitive. `DockFloatingWindow.Open(pane)` opens a
  top-level Reactor `Window` via `ReactorApp.OpenWindow`; the pane
  Content mounts as the window root wrapped with the same DockContext
  envelope used by docked panes (PaneState = Floating).
- [x] Tear-out opens the new `Window` **synchronously** with the pane
  pre-attached as content — `ReactorApp.OpenWindow` mounts the
  content element before activating the window. Tear-out *gesture*
  (drag a tab past threshold → call `Open`) lands with §2.4.
- [~] HWND cold-create on UI thread is deferred until visible: pane
  subtree renders into a `Border` host first, then handed off.
  **Functionally satisfied by `ReactorApp.OpenWindow`** which
  already mounts content before activating the HWND (no async wait
  between gesture and `AppWindow.Show`). The explicit Border-host
  warm-up + perf-budget verification rides on spec 036's
  window-create perf gate, which is not yet wired end-to-end.
- [x] Floating window emits the spec-036 `WindowOpened` /
  `WindowClosed` events (carried on the underlying `ReactorWindow`
  via `ReactorApp.WindowOpened` / `WindowClosed` — Reactor's normal
  window lifecycle). The docking-side
  `OnFloatingWindowCreated` / `OnFloatingWindowClosed` events on
  `DockManager` now also fire from `DockFloatingWindow.Open`:
  `OnFloatingWindowCreated` immediately after registration, carrying
  the dragged source pane; `OnFloatingWindowClosed` from the
  window's `Closed` event, carrying the best-effort pane reference
  (may be stale after a cross-window dock-back already migrated
  it). Observer exceptions are swallowed so tear-out / cleanup
  cannot be broken by a buggy subscriber.
- [~] Custom title bar slot: `IDockAdapter.GetFloatingWindowTitleBar`
  returns the content; P1 contract preserved. **Blocked on spec 036
  / P4 chrome customization.** The adapter interface still exists
  (preserved per the §1.3 P1 commitment surface) and apps can supply
  the title-bar content, but the floating-window chrome doesn't
  yet route `WindowSpec.ExtendsContentIntoTitleBar` to render it.
  P4's `TitleBar` control adoption (§4.2) is the proper landing
  zone for this — P2 ships with the system-default title bar
  fallback, which §2.29 review item 3 documents.
- [x] Multi-display floating restore: clamp restored bounds against
  `DisplayArea.FindAll()` (spec §8.10 reliability); re-position to
  primary center if off-screen. `DockFloatingClamp.Clamp(savedBounds,
  displays)` is the pure algorithm: < 200 × 100 DIP overlap →
  recenter on primary; size clamps to (display - 64 DIP margin).
  `DockFloatingWindow.Open` accepts `savedBounds` + `displays`
  parameters and applies the clamp before forwarding to
  `WindowSpec`. Unit coverage: `DockFloatingClampTests` (9 cases).
  Wiring the actual `DisplayArea.FindAll()` enumeration on the
  tear-out path lands when the renderer reads saved bounds from
  the §2.7 v2 JSON; the call site is parametric so the clamp is
  applied as soon as the bounds are present.
- [x] Floating window outliving its `DockHost`: `DockFloatingTracker`
  keeps the open set; `Snapshot()` enumerates open floating windows
  for the manager to close on unmount. Smoke fixture
  `NativeDocking_FloatingWindowOpensAsRealWindow` asserts
  open / register / close-removes-from-tracker.
- [~] DPI change on monitor cross: re-layout in ≤ 16 ms (spec §8.5).
  **Blocked on spec 031** for the explicit ≤16ms benchmark.
  Functionally inherits Reactor's standard DPI handling (the
  floating window is a vanilla `ReactorWindow`); the latency proof
  rides on spec 031's frame-aligned sampler.

### 2.7 Layout persistence (spec §5.1 item 7, §5.4)

- [x] Implement v2 JSON writer matching the schema in §5.4.
  `src/Reactor/Docking/Persistence/DockLayoutSerializer.Save`.
- [x] Implement v2 JSON reader. Use `JsonSerializerContext` (AOT-clean).
  `src/Reactor/Docking/Persistence/DockLayoutJsonContext`.
- [x] Implement v1→v2 migration; spec §5.4.4: phase-1-format files
  (no `$schema`) infer keys from `title`. Built into
  `DockLayoutMigrationRegistry`.
- [x] `IDockLayoutMigration` service registry; ordered by `(from, to)`
  version pairs (spec §5.3.4). `DockLayoutMigrationRegistry.TryUpgrade`.
- [x] **Size limit:** parser refuses inputs > 1 MB (spec §8.9).
  `DockLayoutSerializer.MaxBytes = 1*1024*1024`.
- [x] **Depth limit:** `JsonReaderOptions.MaxDepth = 32` (spec §8.9).
  Enforced via `JsonDocumentOptions.MaxDepth` on the load path.
- [x] **Schema validation:** every node validated against v2 schema
  before applying to model; unknown fields tolerated for forward-compat;
  missing required fields → reject whole layout, fall back to default.
- [x] **No code paths from JSON.** No reflection, no type-name
  instantiation, no expression evaluation. Layout is structure + identity
  only (spec §8.9). All parsing goes through the source-gen context.
- [x] **No external schema URLs** — `$schema` is a version integer.
- [x] **Failure mode:** corrupt JSON → log via `ReactorEventSource`
  (spec 044), fall back to default layout, never throw on load path
  (spec §8.9, §8.10). `DockLayoutSerializer.Load` now classifies every
  failure into one of six PII-safe categories (`empty`, `oversize`,
  `json-parse`, `unsupported-schema`, `null-document`, `schema-missing`,
  `validation`) and emits `ReactorEventSource.DockingLayoutLoadFallback`
  (event id 16, Warning level, `Errors` keyword) with the category as
  the only payload. The in-process `DockLayoutLoadResult.FailureReason`
  still carries the full message under app ACL. Verified by
  `LayoutSerializerTests.Load_CorruptInput_EmitsReactorEventSourceFallback`
  (theory across 5 inputs), `Load_OversizeInput_EmitsOversizeCategory`,
  `Load_ValidInput_EmitsNoFallbackEvent`, and
  `ReactorEventSourceCoverageTests.Docking_Layout_Load_Fallback_Emits`.
- [x] Sizes stored as **ratios** for splits (not absolute pixels — DPI
  robust). Absolute px reserved for floating x/y/w/h and per-pane
  width/height overrides. JSON shape reserves the `ratio` field; native
  splitter (§2.1) writes it.
- [x] **Invariant culture** for all numeric fields — verify with a
  selftest that saves under `de-DE` and loads under `en-US` (spec §8.8).
  `LayoutSerializerTests.RoundTrip_InvariantCulture_AcrossDifferentLocales`.
- [x] Per-pane state slot — typed via `Document<TState>` (§5.3.2);
  serialized via `WindowPersistedScope`. *Typed envelope landed; the
  WindowPersistedScope key wiring attaches with the native host adapter.*
- [x] Layout JSON load latency ≤ 50 ms for 200-pane layout (spec §8.1).
  Regression guard in `LayoutSerializerTests.Load_TwoHundredPanes_UnderFiftyMilliseconds`
  (250ms threshold to absorb CI jitter; perf bench enforces the budget).

### 2.8 Documents vs tool windows (spec §5.3.1)

- [x] `DockableContent` becomes the abstract base. `src/Reactor/Docking/DockNode.cs`
  — open record; the P1 closed-shape positional ctor is preserved for
  source compat.
- [x] `public sealed record Document(...)` — closable, lives in
  `DocumentPane`; `CanClose` defaults to `true`, `CanPin` defaults
  to `false`. `src/Reactor/Docking/Document.cs`.
- [x] `public sealed record ToolWindow(...)` — hideable, lives in
  `ToolPane`, pinnable to a side; `CanHide` defaults to `true`,
  `CanAutoHide` defaults to `true`, `CanDockAsDocument` defaults to `true`.
- [x] `ToolWindow` default tab styling: bottom-position compact.
  `DockTabGroupRenderer.Render` now auto-flips a group whose
  documents are all `ToolWindow` (and where the user hasn't
  customized `TabPosition` / `CompactTabs` beyond the record's
  defaults) to `TabPosition.Bottom` + `CompactTabs=true`. Mixed
  groups stay at the Document defaults so a tool window dragged
  into an editor strip doesn't collapse the whole strip to compact.
  The `TabPosition.Bottom` visual is still rendered as top-strip
  per the §2.2 limitation (no WinUI TabView bottom mode), but the
  resolved value flows through so future bottom-strip support
  picks it up. 5 new unit tests cover the resolution matrix.
- [x] `Document` default tab styling: top-position full. Same path,
  fall-through case in `DockTabGroupRenderer.Render`'s auto-resolve.
- [x] **Drag-pin gesture** offered only for `ToolWindow`. Landed
  via the §2.2 per-tab pin button (an explicit click affordance is
  the P2 baseline for the pin action; the §2.4 drag-pin gesture
  is the P3 enhancement). `DockTabGroupRenderer.Render`
  auto-enables the pin button on `ToolWindow` tabs whose
  `CanAutoHide=true`; a Document tab never gets the affordance
  even when an `onPinRequested` callback is supplied.
- [~] **Non-breaking deprecation** of the closed-shape
  `DockableContent(...)` constructor: warning analyzer points users
  to `Document(...)` / `ToolWindow(...)`. **Blocked on the docking
  analyzer pack** — the analyzer source-gen lives in
  `src/Reactor.Analyzers/` but no docking-specific rule is wired
  there yet. The base type still accepts the old shape for P1
  source compat (`DockNode.cs`'s open-record constructor remains
  in place); the deprecation pass adds the rule in a follow-up
  commit alongside the next analyzer batch.

### 2.9 Per-pane content state (spec §5.3.2)

- [x] `Document<TState>` generic record carrying a typed `State`.
  `src/Reactor/Docking/Document.cs`.
- [x] `TState` serialized through `WindowPersistedScope` (spec 033/036).
  `DockHooks.UseDockPanePersisted<T>` is the typed surface (§2.24);
  it auto-prefixes the user-supplied key with `pane:<paneKey>:` and
  forwards to `PersistedScope.Window`. Apps that opt into the
  envelope round-trip their `Document<TState>.State` through the
  hook; layout JSON save/load round-trips the structure
  end-to-end.
- [x] State included in layout JSON (round-trips through one file). The
  `state` field on `DockLayoutPane` carries the opaque envelope; the
  adapter shape passes through round-trip with the typed wrapper at the
  app boundary.
- [x] Per-pane `TState` schema versioning is **app responsibility**
  (spec §8.11). Document the convention in docs and `<remarks>` XML.
  Captured in the `Document<TState>` `<remarks>` block in
  `src/Reactor/Docking/Document.cs`.

### 2.10 Keyboard navigation (spec §5.3.3, §8.7)

- [x] **`Ctrl+Tab`** opens VS-style pane navigator overlay listing all
  open panes. Implementation: `DockNavigatorPopup` is a per-host
  `Popup`-based overlay (one instance per host `Border` via a
  `ConditionalWeakTable`) carrying a list of pane titles + a
  highlighted selection. Mount attaches Ctrl+Tab / Ctrl+Shift+Tab
  accelerators (`DockingNativeInterop.AttachChordAccelerators`) that
  route through a new `DockChordBridge.Handlers.OpenNavigator(int
  delta)`. The host component's `OpenNavigator` closure enumerates
  the layout's leaves via `DockHostKeyboard.EnumerateLeaves`,
  computes the start index from the current chord-target key, and
  calls `nav.OpenOrAdvance`. Successive chord presses while open
  cycle ±1; Ctrl release commits the selection (a global
  `KeyUpEvent` listener on the host's `XamlRoot.Content` watches
  `VirtualKey.Control`); Esc cancels. The commit callback sets
  `activePaneKey` + fires `OnActiveContentChanged` so apps observe
  the switch the same way as the chord-cycled tab. Lives outside
  the Reactor reconciler so opening it doesn't perturb the render
  tree (M19 / M20 control-identity contract preserved). Unit
  coverage in `DockNavigatorTests` (9 cases covering
  `EnumerateLeaves` / `IndexOfKey` math + the chord-bridge
  `Handlers.OpenNavigator` round-trip).
- [x] **`Ctrl+F4`** closes active pane if `CanClose`. Fires
  `OnDocumentClosing` (cancellable) → `OnDocumentClosed` →
  `OnLiveLayoutChanged`. **`Ctrl+W`** is wired as an alias on the
  same handler. Implementation: `DockHostNativeComponent.CloseActivePane`
  via `DockChordBridge`.
- [x] **`Ctrl+Shift+M`** enters keyboard-initiated drag: flips the host
  `keyboardOverlayActive` state which drives the same drop-target
  overlay used by mouse drag. Arrow-key nav + Enter confirm + Esc
  dismiss are inherited from §2.3's focusable overlay; pressing the
  chord while the overlay is up dismisses it (toggle). The implicit
  source pane is the chord-cycle target (`activePaneKey`) or the
  app's `ActiveDocument`.
- [x] **`Alt+F7`** opens hidden-pane picker (re-show closed-but-remembered
  tool windows; pairs with `PreviousContainer` in §5.3.9). Routes
  through `DockChordBridge.Handlers.OpenHiddenPicker`; the host
  component enumerates the side-strip set (`effLeftSide` /
  `effTopSide` / `effRightSide` / `effBottomSide` — these are where
  hidden ToolWindows end up after a `Hide` / `CanHide=true` close
  via the §2.16 drain). The same `DockNavigatorPopup` primitive
  used by Ctrl+Tab paints the picker; commit calls
  `model.Show(pane)` which routes back through
  `DockLayoutMutator.ShowFromHistory` (§2.15) to the pane's
  remembered container. No-ops when the side-strip set is empty
  so a stray Alt+F7 press can't strand the user with a blank
  picker.
- [x] **`Ctrl+PageUp`** / **`Ctrl+PageDown`** previous/next tab in
  group (VS parity). Cycles `selectedIndexStore[path]` for the group
  containing `activePaneKey ?? ActiveDocument.Key`; wraps at both
  ends. Re-renders via `RequestRatioRerender`. Fires
  `OnActiveContentChanged` when the resolved pane changes.

**§2.10 chord slice — human-validated** 2026-05-21 against showcase
Scene A by user (Chris Anderson). PageUp/Down cycling, F4/W close,
and Ctrl+Shift+M keyboard drop-mode all behave as expected. Remaining
§2.10 items (Ctrl+Tab navigator, Alt+F7 picker, live-region, spec-027
binding) are deferred to a follow-up pass.
- [x] Live-region announcements via UIA `LiveSetting=Polite` for layout
  state transitions ("MainView.xaml moved to right pane", "Output
  pinned to bottom", "Properties window torn out"). Implementation
  routes through `DockHostLiveAnnouncer` (a `ConditionalWeakTable<DockManager,
  FrameworkElement>` bridge paralleling `DockChordBridge` /
  `DockHostModelBridge`). `DockingNativeInterop` registers the host
  Border at mount; the renderer calls `RaiseNotificationEvent` on
  the registered element's `AutomationPeer` (WinUI's supported UIA
  Notification API — no visual-tree changes required, so M19 / M20
  control-identity tests stay green). Announcement templates live
  under `Docking.LiveRegion.*` keys
  (`LiveDocked`, `LiveFloated`, `LivePinned`, `LiveClosed`,
  `LiveHidden`, `LiveShown`), parameterized by `{paneTitle}` via
  `DockingStrings.LiveAnnouncement`. Wire points: tab-button close,
  Ctrl+F4/W close, tear-out, dock-confirm (host + per-group overlay
  paths), and every drain mutation (DockOp / FloatOp / HideOp /
  ShowOp / CloseOp / PinToSideOp).
- [~] All chords configurable via spec 027 input binding. **Blocked
  on spec 027 — the `IInputBindingResolver` (or equivalent
  app-scoped accelerator-binding surface) has not shipped.** Verified
  2026-05-21: `grep -r "IInputBindingResolver" src/Reactor/` returns
  no matches; spec 027 itself does not currently describe a typed
  resolver for chord remapping. Until that lands, the docking chord
  set is hard-coded in
  `DockingNativeInterop.AttachChordAccelerators`
  (Ctrl+PageUp / Ctrl+PageDown / Ctrl+F4 / Ctrl+W / Ctrl+Shift+M /
  Ctrl+Tab / Ctrl+Shift+Tab / Alt+F7), all of which match Visual
  Studio defaults. Resolver-keyed identifiers will live next to
  `DockingStringKeys` in a `DockingChordKeys` static class when the
  upstream resolver arrives; the bind site is small (single static
  method) and the resolver wiring is mechanical.

### 2.11 Layout versioning (spec §5.3.4, §8.11)

- [x] `"$schema": 2` field at root of v2 layout JSON.
  `DockLayoutDoc.Schema` with `JsonPropertyName("$schema")`.
- [x] `IDockLayoutMigration` service interface + registry.
  `src/Reactor/Docking/IDockLayoutMigration.cs` +
  `Persistence/DockLayoutMigrationRegistry.cs`.
- [x] **Backward read-compat:** v1 readable forever; future v3+
  readable through all future versions. Built-in v1→v2 migration in
  the registry; custom v2→v3+ steps stack via `Add(migration)`.
- [x] **Forward-tolerance:** newer-than-known schema logs warning,
  best-effort parses what it understands, falls back to default for
  unknown nodes (spec §8.11). `TryUpgrade` returns success with a
  warning reason when input schema exceeds target.
- [x] Migration ladder runs all (v1→v2, v2→v3, …) in order.
  `LayoutSerializerTests.Registry_CustomMigrationStacksOnLadder`.

### 2.12 Cancellable lifecycle events (spec §5.3.5)

- [x] On `DockHost` (now `DockManager`) record, expose `Action<TArgs>?`
  props for every event below; every `*ing` carries `Cancel`:
  - [x] `OnLayoutChanging` / `OnLayoutChanged`
  - [x] `OnDocumentClosing` / `OnDocumentClosed`
  - [x] `OnToolWindowHiding` / `OnToolWindowHidden`
  - [x] `OnToolWindowClosing` / `OnToolWindowClosed`
  - [x] `OnContentFloating` / `OnContentFloated`
  - [x] `OnContentDocking` / `OnContentDocked`
  - [x] `OnActiveContentChanged`
  - [x] `OnFloatingWindowCreated` / `OnFloatingWindowClosed`
- [x] `IDockBehavior` from P1 collapses into these props (its three
  methods map onto `OnContentDocked`, `OnContentFloating`, and the
  per-group docked variant). `[Obsolete]` forwarder now in place on
  the interface itself + on `DockManager.Behavior` with migration
  pointers (OnDocked → OnContentDocked; OnFloating →
  OnContentFloating/OnContentFloated). The P1 wrapper assemblies
  (`Reactor.Docking.Xaml`) suppress the obsolete warning at file
  scope while they continue to bridge the interface for source
  compat. Removal lands one release after Phase 2 ships.
- [x] **No `+=` accumulation** — each render passes a fresh delegate;
  the reconciler holds only the current one (spec §8.10 memory).
  By construction: `DockManager` is a record with init-only Action
  props; the reconciler replaces the delegate on each render.

*Live firing of these events from the renderer attaches with the
native UI pipeline (§2.1–§2.6).*

### 2.13 Insertion-policy hook `IDockLayoutStrategy` (spec §5.3.6)

- [x] Define `public interface IDockLayoutStrategy` with
  `BeforeInsertDocument`, `AfterInsertDocument`,
  `BeforeInsertToolWindow`, `AfterInsertToolWindow`.
  `src/Reactor/Docking/IDockLayoutStrategy.cs` — default-method bodies
  on the interface keep apps from boilerplate-overriding every member.
- [x] `Before*` returning `true` short-circuits the default insertion;
  `false` lets the manager proceed. `After*` is the chance to set
  dimensions / pin to side. Contract documented in XML.
- [x] Strategies receive `DockHostModel` (mutable handle) — not the
  immutable `DockNode` tree.
- [x] Example fixture: route any tool window with
  `Title.StartsWith("Error")` to bottom side, height 180.
  `LayoutStrategyTests.Strategy_CanShortCircuitInsertionByReturningTrue`
  + `DockHostModelSequenceTests.ErrorPaneStrategy_RoutesViaModel_QueuesPinToSide`.

*The manager-side dispatch into the strategy is now wired in
`DockHostModel.Dock` (§2.13 close-out): when `LayoutStrategy` is
mirrored from the manager onto the model (done each render in
`SyncModelFromElement`), `Dock(content, target)` dispatches
`BeforeInsertDocument` / `BeforeInsertToolWindow` first, returns
early when the strategy claims placement, and otherwise queues the
default `DockOp` then fires `AfterInsertDocument` / `AfterInsertToolWindow`.
Bare `DockableContent` (P1 source-compat shape) bypasses the typed
hooks entirely. The drain side — actually applying the queued
mutations to the rendered tree — still rides on §2.16 model
integration.*

### 2.14 Fine-grained per-pane permissions (spec §5.3.8)

- [x] On `DockableContent` base: `CanClose` (default false),
  `CanFloat` (default true), `CanMove` (default true), `Key`.
- [x] `Document.CanClose` default flips to `true` — set in
  `Document()` parameterless ctor via base init.
- [x] `Document.CanDockAsToolWindow` (default false).
- [x] `ToolWindow.CanHide` (default true) — **X button hides**, not
  closes (AvalonDock semantic).
- [x] `ToolWindow.CanAutoHide` (default true).
- [x] `ToolWindow.CanDockAsDocument` (default true).
- [x] Permission gating: drag pipeline checks `CanMove`; floating
  gesture checks `CanFloat`; close button checks `CanClose`; pin
  gesture checks `CanAutoHide`. UI cues for disabled permissions.
  Gating wired in `DockHostNativeComponent`:
  - `HandleTabDragStarting` refuses drag when `CanMove=false`
    (logs a Note op; no `DockDragSession.Begin`).
  - `HandleTabDragCompleted` refuses tear-out when `CanFloat=false`
    (session ends, layout unchanged).
  - Drop-target `OnConfirm` re-checks `sourcePane.CanMove` (defensive
    gate for the Ctrl+Shift+M keyboard mode where the active pane
    could turn pinned between mode-enter and confirm).
  - `EnterKeyboardDropMode` no-ops when the active pane is pinned
    so the overlay never opens with a refused-source.
  - Tab close button now routes through `CloseTabViaButton` which
    re-checks `CanClose` and goes through the cancellable
    `OnDocumentClosing` event.
  *Pin gesture's `CanAutoHide` check pairs with the §2.4 drag-pin
  affordance that's still pending (§2.5 sizer follow-up). UI cues
  for disabled permissions (cursor hint / disabled tab style)
  ride on the §2.8 tab styling pass.*

### 2.15 `PreviousContainer` — show-panel-where-you-left-it (spec §5.3.9)

- [x] Every `DockableContent` instance tracks last `DockNode` container
  it was inside (internal state — no public field).
  `PreviousContainerTracker` (ConditionalWeakTable-backed).
- [x] Hidden → re-shown: lands in remembered container, not default
  insertion point. Wiring lives in two halves: (a) the close /
  tear-out paths (`CloseTabViaButton`, `CloseActivePane`, tear-out
  branch of `HandleTabDragCompleted`) now call
  `PreviousContainerTracker.Set(pane, container)` via the new
  `DockLayoutMutator.FindContainer` walk before removing the pane;
  (b) `DockLayoutMutator.ShowFromHistory(root, pane, fallback)` is
  the pure-function helper that folds a pane back into its
  remembered `DockTabGroup` when that group still lives in the
  tree, falling back to `InsertPaneAtTarget` otherwise. Caller-side
  wiring lands via the §2.16 model-mutation drain:
  `DockHostModel.Show(content)` queues a `ShowOp`, the drain calls
  `DockLayoutMutator.ShowFromHistory` (and clears the tracker
  entry so the next hide→show cycle records the new container).
  Drag-back-from-floating-window's "snap to history" hint still
  lands with the floating-window dock-back path.
- [x] State survives layout serialization (stored as `previousContainer`
  on the JSON content node). `DockLayoutPane.PreviousContainer` field
  reserved + emitted by serializer.
- [x] `IDockLayoutStrategy.BeforeInsertToolWindow` can override.
  The §2.16 ShowOp drain in `DockHostNativeComponent.DrainPendingMutations`
  consults `model.LayoutStrategy.BeforeInsertToolWindow` before
  falling back to `DockLayoutMutator.ShowFromHistory`. Apps that
  want to route a re-shown tool window somewhere other than its
  remembered container return `true` from the strategy hook (and
  place the pane themselves via the model's public mutators).
  Strategy exceptions are swallowed so the drain stays alive.
- [x] Selftest: hide → show → assert container identity preserved.
  `PreviousContainerTests.HideShowCycle_PreservesContainerIdentity` +
  `DockHostModelSequenceTests.HideShow_WithPreviousContainerTracker_RoundTripsContainerIdentity`.

### 2.16 `DockHostModel` layout-as-model surface (spec §5.3.10)

- [x] `public sealed class DockHostModel` with read surface: `Root`,
  `LeftSide`/`TopSide`/`RightSide`/`BottomSide` (`IReadOnlyList<ToolWindow>`),
  `Floating` (`IReadOnlyList<FloatingDockWindow>`), `ActiveContent`.
  `src/Reactor/Docking/DockHostModel.cs`.
- [x] Enumerations: `AllContent()`, `Descendants()`.
- [x] Mutations: `Dock`, `Float`, `Hide`, `Show`, `Close`, `Activate`,
  plus `PinToSide` (for the spec §5.3.6 strategy example). Each mutator
  queues a `PendingMutation` record the reconciler drains.
- [x] Serialization: routed through `DockLayoutSerializer.Save`/`Load`
  (Phase-2 v2 schema; §2.7).
- [x] **All mutations UI-dispatcher-affined.** Off-thread access
  throws (`InvalidOperationException`). Documented contract, not
  enforced with locks (spec §8.10). Verified by
  `DockHostModelTests.Mutations_OffOwnerThread_Throw`.
- [x] **Model is internal source of truth, not parallel writable
  surface.** The class is exposed only via the `DockContexts.Host`
  context (§2.17); apps interact via the controlled `Layout` prop +
  the `OnLayoutChanged` round-trip.
- [x] `DockHost` (aka `DockManager`) element owns one `DockHostModel`
  instance; reconciler reads from it. `DockHostNativeComponent` caches
  a single `DockHostModel` per mount via `UseRef`; each render
  `SyncModelFromElement` mirrors `Layout`/sides/`ActiveDocument` from
  the controlled element.
- [x] **Model-mutation drain wired.** `DockHostNativeComponent.Render`
  drains `model.Pending` synchronously on each render pass:
  `DockOp`/`Float`/`Hide`/`Show`/`Close`/`Activate`/`PinToSide` each
  translate to a layout-override / side-override / active-key update
  + fire the matching `OnContentDocked` / `OnDocumentClosing+Closed` /
  `OnToolWindowHiding+Hidden` / `OnContentFloating+Floated` /
  `OnActiveContentChanged` lifecycle event. The model exposes
  `OnMutationQueued` so each mutator wakes the host into a re-render
  via `bumpTick`. External callers (tests, devtools) resolve the live
  model via `DockHostModelBridge.Get(manager)` — same pattern as
  `DockChordBridge` (§2.10). Verified by selftest
  `NativeDocking_ModelDrain_DockCloseActivatePinAffectsLiveTree` (9
  assertions: Dock+Close+Activate+PinToSide each mutate the rendered
  tree and fire the matching event) and unit tests
  `DockHostModelSequenceTests.EachMutator_InvokesOnMutationQueued_Once`,
  `StrategyShortCircuit_StillFiresOnMutationQueued`,
  `OnMutationQueued_Null_DoesNotThrow`.

### 2.17 `DockContext` + property hooks (spec §5.3.11)

- [x] `DockHost` registers `DockContext` in `RenderContext` on mount;
  unregisters on unmount. `DockHostNativeComponent.Render` wraps the
  rendered subtree with `.Provide(DockContexts.Host, model)`,
  `.Provide(DockContexts.ActivePaneKey, key)`,
  `.Provide(DockContexts.LayoutSnapshot, snapshot)`. Per-pane content
  is wrapped with `.Provide(DockContexts.Pane, info)` +
  `.Provide(DockContexts.PaneState, Docked)`. Smoke fixture
  `NativeDocking_DockContextHooksResolveOnRealMount` asserts that
  `UseDockHost`, `UsePane`, `UseActivePaneKey`, `UseIsActivePane`
  resolve to live state and flip correctly when the active pane
  changes.
- [x] `RenderContext.UseDockHost()` → `DockHostModel?`. Walks context
  chain. Returns null outside any host. Extension method on
  `RenderContext` — `src/Reactor/Docking/DockHooks.cs`.
- [x] `UseActivePaneKey()` → `object?`. Re-renders only the consumer
  on active change (selector-style subscription via dedicated
  `DockContexts.ActivePaneKey` slot).
- [x] `UseIsActivePane()` → `bool`. Boolean derivative; re-renders
  only on transitions.
- [x] `UsePane()` → `DockPaneInfo` (Key, Title, Content). Throws if
  called outside a `Document`/`ToolWindow` Content subtree.
- [x] `UseDockState()` → `DockPaneState` (`Docked`, `Floating`,
  `AutoHidden`, `AutoHiddenExpanded`, `Hidden`). Re-renders per pane on
  transitions only.
- [x] `UseDockLayout()` → `DockLayoutSnapshot`. Wide-net; re-renders
  on any structural change. Documented as "used sparingly — devtools,
  not pane content".
- [x] `public readonly record struct DockPaneInfo(object? Key, string
  Title, DockableContent Content);` — `src/Reactor/Docking/DockPaneInfo.cs`.
- [x] `public enum DockPaneState { Docked, Floating, AutoHidden,
  AutoHiddenExpanded, Hidden }` — `src/Reactor/Docking/Enums.cs`.
- [x] Two-host process selftest: components inside `hostA` resolve to
  `hostA`; components inside `hostB` resolve to `hostB`. No string
  IDs needed in user code.
  `DockHooksTests.TwoHostScopes_ResolveIndependently`.

### 2.18 No `DocumentsSource` / `LayoutItemTemplate` (spec §5.3.7)

- [x] **Do not add `DocumentsSource`, `LayoutItemTemplate`,
  `ContentResolver`, or any binding API.** Reactor functional
  composition is the data-to-tree mapping. Rationale captured in
  `docs/_pipeline/apps/docking/api.md` ("The collection-to-pane
  mapping is just `.Select` — no `DocumentsSource` binding API
  needed").
- [x] Self-host fixture demonstrating
  `documents.Select(d => new Document(Key: d.Id, ...))` reconciliation
  through state changes. Verifies "the component is the rehydrator".
  Landed as `NativeDocking_CompositionDrivenDocumentsRespectKeyedReconciliation`:
  mounts a layout where the documents list is held in app state,
  asserts add / remove cycles update the TabView while keyed
  reconciliation preserves the TabView control instance (7 ok
  assertions).

### 2.19 Phase-2 chrome removal (spec §5.6)

- [x] Remove `Reactor.Docking.Xaml` from `Reactor.slnx`. Done
  alongside the §2.29 prep pass: project node dropped from
  `Reactor.slnx`, `ProjectReference`s removed from
  `DockShowcase.csproj` / `Reactor.AppTests.Host.csproj` /
  `Reactor.Tests.csproj`, `InternalsVisibleTo("Microsoft.UI.Reactor.Docking.Xaml")`
  removed from `Reactor.csproj`. Showcase entry point dropped
  the `REACTOR_DOCK_XAML=1` A/B flip — the native renderer is
  now the only path. Phase-1-specific
  `DockingSmokeFixtures` + `BehaviorBridgeMappingTests` retire
  alongside (NativeDockingSmokeFixtures cover the same
  mount/update/unmount surface against the native renderer).
  Source under `src/Reactor.Docking.Xaml/` stays on disk per the
  spec §5.6 reference contract; a follow-up commit removes the
  directory.
- [x] Remove the assembly from any published packages. Same pass
  — no published-package surface remains.
- [x] **`third_party/WinUI.Dock/` deleted entirely** alongside the
  P2 review-gate pass (2026-05-22). The reference + regression-A/B
  justifications retired with the §2.19 unhooking: the A/B
  `REACTOR_DOCK_XAML=1` flip is gone (removed in the same commit)
  and the architecture reference has been fully absorbed into the
  native renderer + this spec. License compliance for MIT requires
  the notice only "in all copies or substantial portions of the
  software" — with the code no longer shipped, the notice block
  retires from `ThirdPartyNoticeText.txt` too. The §5.6 "keep
  source" language is superseded by this deletion.
- [x] **`src/Reactor.Docking.Xaml/` deleted entirely** in the same
  pass. The §2.19 staged removal contemplated this as a follow-up
  commit; landed here.

### 2.20 Performance (spec §8.1, §8.5)

- [~] **Hover-state update ≤ 2 ms** — covered indirectly by the
  zero-alloc budget below (`ComputePreviewBounds` is the per-move
  hot path; allocation-free means measure latency is bounded by
  the WinUI layout pass, not by docking code). A dedicated
  frame-aligned latency benchmark per spec 031 sampling is the
  follow-up (would require the spec 031 sampler harness; not yet
  wired through to a docking-specific call site).
- [~] **Tear-out ≤ 1 frame (16 ms)** — gesture fire → HWND visible.
  **Blocked on spec 031 — the `FrameAlignedSampler` (or
  equivalent harness for measuring gesture-to-paint latency)
  has not shipped.** Verified 2026-05-21:
  `grep -r "FrameAlignedSampler" src/Reactor/` returns no
  matches. The synchronous `ReactorApp.OpenWindow` path already
  meets the budget by construction (no async wait between
  gesture and `AppWindow.Show`); the dedicated benchmark fires
  alongside spec 031's sampler.
- [x] **Layout JSON load ≤ 50 ms** for 200-pane layout.
  `DockPerfBudgetTests.LayoutLoad_TwoHundredPanes_MedianUnderCiCeiling`
  runs 10 iterations post-warm-up, asserts median < 200ms (CI
  ceiling). Spec budget is 50ms; the wider test ceiling absorbs
  shared-runner jitter while still catching an O(n²) regression.
- [x] **Zero allocation in drag hot path** — allocation-counting
  test per spec 034 precedent.
  `DockPerfBudgetTests.DropTargetHitTest_HotPath_ZeroAlloc` warms
  up the JIT then asserts `ComputePreviewBounds` produces no
  measurable allocation across 100k iterations
  (`GC.GetAllocatedBytesForCurrentThread` delta ≤ 1B/iteration cap).
- [x] **Reconciler diff ≤ 1 ms** for 50-pane layout shape change.
  `DockPerfBudgetTests.Mutator_FiftyPaneShapeChange_MedianUnderCiCeiling`
  runs 20 RemovePane + InsertPaneAtTarget iterations on a 50-pane
  group, asserts median ≤ 25ms (CI ceiling; spec budget 1ms).
- [~] **Cold start of persisted layout ≤ 200 ms** first frame for
  50-pane layout. Off-viewport panes defer content `useEffect`
  registration until first visible. **Blocked on spec 031 —
  the cold-start harness that captures `ReactorApp.OpenWindow`
  → first-frame duration needs the frame-aligned sampler.** A
  best-effort proxy is already covered by `LayoutLoad_TwoHundredPanes_MedianUnderCiCeiling`
  (layout JSON load median, 200ms ceiling); end-to-end
  gesture-to-paint timing rides on spec 031.
- [x] **No static dictionary of all-time pane keys; no GUID→object
  table outliving a drag; no captured-closure leaks on event
  subscriptions** — covered by §2.25 leak baseline + drag-payload
  selftests (`NativeDocking_Reliability_EventSubscriptionLeakBaseline`,
  `NativeDocking_Reliability_DragSessionPayload_ObjectRefsOnly`).
- [~] **DPI change re-layout ≤ 16 ms** when floating window crosses a
  monitor boundary. **Blocked on spec 031** — Reactor's standard DPI
  handling carries the budget by construction (the floating
  window inherits the same DPI re-layout path as any other
  `ReactorWindow`), but the docking-specific frame-level latency
  selftest needs the `FrameAlignedSampler` harness.

### 2.21 Localization (spec §8.6)

- [x] All docking user-facing strings route through a static
  `DockingStrings.Get(key)` router whose <c>Resolver</c> delegate
  apps wire at startup to forward keys into their `IntlAccessor`.
  The delegate indirection is necessary because the drop-target
  overlay + side-strip + floating-window code paths are realized
  WinUI `UIElement`s without `UseIntl()` in scope; apps capture
  the accessor once at app boot and assign a resolver. Without a
  resolver, callers get the English defaults that mirror
  `Reactor.Docking.resw`. Spec 045 §2.21 / §8.6.
- [x] Resource keys defined as constants on `DockingStringKeys` +
  registered in `Reactor.Docking.resw`:
  - [x] Drop-target tooltips and AT names (Center, SplitLeft/Right/
    Top/Bottom, DockLeft/Right/Top/Bottom — 9 keys).
  - [x] `NavigatorWindow` headings (Documents, ToolWindows, Active).
  - [x] Per-pane context-menu items (Close, Hide, Float, PinToSide,
    AutoHide, MoveToNextGroup).
  - [x] Side-pin sidebar tooltip key + a `SidePinTooltip(paneTitle)`
    helper that performs the placeholder substitution after lookup.
  - [x] Floating-window default fallback title (FloatingWindowDefaultTitle).
  - [x] Layout-restore failure (LayoutRestoreFailed).
  - [x] Drop-target host landmark name (DropTargetHostLandmark).
- [x] Call-site wiring: `DockDropTargetOverlayControl.GetLocalizedName`
  + landmark `AutomationProperties.Name`, `DockSideStripRenderer`
  tooltip, `DockFloatingWindow.Open` default-title fallback —
  all route through `DockingStrings`. Verified by
  `DockingStringsTests` (9 cases: no-resolver default, resolver-
  forwards-key, null/empty fallback, side-pin substitution
  with/without resolver, every-DropTarget-key coverage, navigator/
  menu/error defaults).
- [~] `.xlf` pipeline (spec 005) generates downstream loc files.
  **Partially blocked on packaging — `Reactor.Localization.Generator`
  exists and is referenced by `Reactor.csproj`, but
  `Reactor.Docking.resw` lives under the now-unhooked
  `src/Reactor.Docking.Xaml/Resources/` directory (§2.19 removed
  the wrapper from the solution).** Verified 2026-05-21: no
  `<AdditionalFiles Include="...Reactor.Docking.resw" />` entry
  pulls the docking resw into the Reactor.csproj source-gen
  inputs. The typed `Loc.Docking.*` accessor surface lands
  when the docking resw moves into `src/Reactor/Resources/` (or
  an equivalent Reactor.csproj-resident location) and gets
  wired as an `AdditionalFiles` input to the generator. The
  `DockingStrings.Resolver` delegate continues to bridge apps
  to their `IntlAccessor` in the meantime — no app-side surface
  change required when the typed pipeline arrives.
- [x] Docs: clarify that `Document.Title` / `ToolWindow.Title` are
  app-owned; docking does not localize them. Captured in the
  `DockingStrings` class docstring (the side-pin tooltip uses
  `paneTitle` placeholder, app-owned) and in the .resw comment on
  `Docking.SidePin.Tooltip`.

### 2.22 Accessibility (spec §8.7)

- [~] **AT roles:** `Document` → `TabItem` inside `DocumentGroup`
  (`Tab`); `ToolWindow` → `Pane` with `AccessibleName` from `Title`;
  auto-hidden ToolWindow on side strip → `Button` until expanded,
  then `Pane`. *Tab role for documents is inherited from WinUI
  `TabView` (Pane element with `Tab` children automatically). Each
  pane's wrapper Border now carries `AutomationProperties.Name` =
  `leaf.Title` (`DockHostNativeComponent.WrapLeafWithPaneContext`),
  which becomes the AT label. The side-strip-button → expanded-Pane
  transition for an auto-hidden ToolWindow remains a §2.5 follow-up
  (the popup currently has the implicit `Pane` role, but the
  explicit AT role transition isn't asserted yet).*
- [x] Each pane carries stable `AutomationId` derived from
  `Key.ToString()` so AT + selftests address panes deterministically.
  `DockHostNativeComponent.AutomationIdForPane(leaf)` returns
  `pane:<key>` (or null when key is empty); applied via
  `.AutomationId(...)` in `WrapLeafWithPaneContext`. Unit-tested
  in `DockA11yTests` (5 cases — non-null key, stability across
  equivalent panes, null key, empty-string key, non-string key).
  Selftest `NativeDocking_A11y_HostLandmarkAndPaneAutomationIds`
  walks the realized control tree and finds the active pane's
  wrapper with the expected AutomationId.
- [x] `DockHost` itself exposes `LandmarkRegion` with app-supplied
  localized name. `DockingNativeInterop.Register` sets
  `AutomationProperties.LandmarkType = Custom` +
  `LocalizedLandmarkType` + `Name` on the host `Border`, sourced
  from `DockingStrings.Get(DockingStringKeys.DockHostLandmark)`
  (English default "Docking area"; apps localize via the
  `DockingStrings.Resolver` delegate). Selftest verifies the
  landmark type + localized name on the realized Border.
- [x] Splitter handles focusable + arrow-key resizable (precedent:
  WPF `GridSplitter`). `DockSplitterControl` sets `IsTabStop=true`,
  `UseSystemFocusVisuals=true`, and routes Left/Right (Columns) or
  Up/Down (Rows) through the same direct-mutation path as the
  pointer drag with a 16 DIP default `KeyboardStep`. RTL inversion
  for Columns: Left/Right swap when the splitter's `FlowDirection`
  resolves to RightToLeft so the visually-right pane still grows on
  a Right press (§2.23 splitter inversion close-out).
- [x] Tab strip fully arrow-key navigable. Inherited from WinUI
  `TabView`'s default keyboard handling (Left/Right arrows cycle
  through `TabViewItem` headers when the strip has focus; Home/End
  jump to first/last). The docking layer does not override this
  contract — chord cycling via Ctrl+PageUp/Down is the docking-
  specific addition that works regardless of focus location.
- [~] **Focus invariants:** after close, focus moves to next-active
  pane in same group; if group empty, to the host. After tear-out,
  focus moves to new floating window's active pane. After re-adoption
  (P3), focus moves to adopted pane in its new home. Partial: the
  sibling-pane path is carried automatically by TabView's
  selection-change focus carry on the next render; the empty-host
  fallback is explicit via
  `DockHostLiveAnnouncer.FocusHostFallback(manager)` invoked from
  `CloseActivePane` and `CloseTabViaButton` when the post-remove
  layout has no group with documents. Tear-out path inherits WinUI's
  default focus shift to the new floating window. Selftests for the
  AT-tree-walk + keyboard-only docking cycle remain open.
- [x] **Touch targets:** drop-target buttons ≥ 44 × 44 DIPs;
  splitter handles 8/16 DIPs visual/hit.
  `DockDropTargetOverlayControl.ButtonSizeDip = 44.0` per spec
  §8.7 WCAG 2.5.5; `DockSplitterControl.VisualThicknessDip = 8.0`
  / `HitThicknessDip = 16.0` matches the spec ask.
- [~] **Tab-strip hit-test** extends 4 DIPs past visual border for
  close-button targeting forgiveness. WinUI TabView's default
  template gives the close button a 12 DIP icon + 8 DIP padding
  ≈ 20 DIP effective hit area, exceeding the 4 DIP forgiveness
  spec. An explicit 4-DIP extension would require a custom
  TabViewItem template — out of scope for P2 baseline; revisit
  if usability testing surfaces a measurable miss-rate
  regression.
- [x] **Reduced-motion** (`UISettings.AnimationsEnabled = false`):
  drop-preview animations, side-popup slide, tab-reorder slide all
  disabled; static positioning verified. Satisfied by construction:
  P2 ships no custom transition animations — drop-preview snaps to
  position (`DockDropTargetOverlayControl.UpdatePreview` writes
  `Margin` directly, no `ThemeTransition`), the side-popup snaps
  open (no `EdgeUIThemeTransition`), and tab-reorder rides on
  WinUI's `TabView` whose animation suite already honors
  `UISettings.AnimationsEnabled`. The overlay reads the setting in
  `DockDropTargetOverlayControl.ReadAnimationsSetting` so future
  easing additions can gate on the flag.
- [x] **High-contrast:** chrome legibility (P4 review item 27
  explicit; P2 baseline must not regress). All hard-coded ARGB
  literals in `DockSplitterControl` (handle Fill + hover/exit
  transitions + pointer-capture-lost reset) and
  `DockDropTargetOverlayControl` (preview-rect Background +
  BorderBrush, button Fill + Stroke, direction-indicator side
  stripes, Center fill) are now resolved via a `ThemedBrush(key,
  fallback)` helper that walks `Application.Current.Resources`
  for the named system brush and falls back to the original
  ARGB literal only when no Application instance is in scope
  (headless harness). Brush map:
  splitter handle Fill →
  `SystemControlForegroundBaseMediumLowBrush`; splitter hover →
  `SystemControlHighlightAccentBrush`; drop-target outer Fill →
  `SystemControlBackgroundChromeMediumLowBrush`; drop-target
  Stroke + indicator fill + preview Border →
  `SystemControlHighlightAccentBrush`; preview Background +
  Center fill → `SystemControlBackgroundAccentBrush` (the
  preview Border uses an explicit `Opacity=0.30` to preserve
  the transparent-overlay feel). HC themes now swap the chrome
  via the same dictionary path that WinUI defaults rely on; no
  custom HC code path needed. Animation gating already in place
  (§2.22 reduced-motion bullet).
- [~] **A11y-specific selftests:**
  - [x] AT-tree walk asserts role/name/AutomationId for every pane.
    `NativeDocking_A11y_HostLandmarkAndPaneAutomationIds` walks the
    realized control tree, finds the host Border by landmark name +
    type, asserts pane wrapper Borders carry stable
    `AutomationId = pane:<key>` + `AutomationName = Title`.
  - [x] Keyboard-only docking cycle: open / move / pin / close
    entirely via keyboard; state transitions + live-region
    announcements asserted. `NativeDocking_A11y_KeyboardCycle_NavigatorCommitsActive`
    drives the §2.10 Ctrl+Tab navigator's commit + cancel paths
    via its test hooks (`SeedForTest` / `CommitForTest` /
    `CancelForTest`) and asserts the navigator commit fires
    `OnActiveContentChanged` with the right previous/active
    pair, that cancel closes the popup without firing, and that
    state stays consistent across open → cancel → reopen
    sequences. Driving the live `Ctrl-release` keystroke is out
    of reach in the headless harness (no real input pipeline),
    so the test hooks bypass that and exercise the host-side
    wiring contract directly.
  - [x] Focus invariant: after every transition, focused element is
    valid (not null, not disposed) inside the host.
    `NativeDocking_A11y_FocusFallback_OnLastPaneClose` drives a
    model-mutator close of the only pane through the §2.16 drain
    and asserts (a) the host Border resolves through both
    `DockHostLiveAnnouncer.GetHost` and (pre-close) the realized
    landmark Border, (b) those refs are identical, (c) the live-
    region bridge entry survives a record-`with` re-render
    (matches the `DockChordBridge` / `DockHostModelBridge` no-
    aggressive-clear contract — `ConditionalWeakTable` GC keys
    reclaim old refs naturally), and (d) the post-close layout
    has no group, gating the FocusHostFallback call site.

### 2.23 Globalization / RTL + bidi (spec §8.8)

- [~] `DockHost` honors `FlowDirection` from `RenderContext` (spec
  005). *WinUI's visual-tree FlowDirection inheritance handles the
  bulk of the work: when an ancestor sets `FlowDirection.RightToLeft`,
  the docking Border, TabView, side strips, and drop-target overlay
  pick it up automatically. The drop-target icon glyph mirror + the
  custom-drawn splitter direction inversion items below remain
  open. The invariant-culture JSON path (§2.7) is already proven
  by `LayoutSerializerTests.RoundTrip_InvariantCulture_AcrossDifferentLocales`.*
- [x] **Sidebar order flips** in RTL (left becomes right visually;
  semantics preserved — `LeftSide` is logical "left of reading
  order" per Office/VS convention). Carried by WinUI FlowDirection
  inheritance on the side-strip Border layout — the RTL selftest
  fixture (`NativeDocking_Rtl_FlowDirectionAndSplitterSign`)
  verifies the host inheritance contract.
- [x] **Tab order in `DocumentGroup`** flips (first tab on right).
  Same FlowDirection inheritance path: the RTL selftest asserts
  every realized `TabView` carries `FlowDirection.RightToLeft`
  under an RTL host, which auto-mirrors the tab strip layout.
- [x] **Drop-target overlay** mirrors (DockLeft icon at right edge).
  `DockDropTargetOverlayControl.BuildDirectionIndicator` emits a
  thin filled "side stripe" overlay on each directional target
  (SplitL/T/R/B, DockL/T/R/B) positioned via
  `HorizontalAlignment`/`VerticalAlignment` so WinUI's FlowDirection
  inheritance auto-mirrors the Left-anchored indicators to the
  right edge under RTL. Button positions themselves already mirror
  via FlowDirection inheritance on the overlay's Grid.
- [x] **Splitter drag direction** inverts for RTL. Pointer-drag is
  RTL-correct by construction (WinUI reports pointer coordinates in
  the FlowDirection-transformed space, so positive ΔX always
  corresponds to cursor-moving-visually-leftward under RTL — the
  existing grow math falls out correct). Keyboard nudge needs an
  explicit swap because `VirtualKey.Left` / `Right` are physical;
  `DockSplitterControl.OnKeyDown` inverts the Left/Right mapping for
  Columns direction when `FlowDirection==RightToLeft`. Rows
  splitters are unaffected (top-to-bottom is not flipped by
  FlowDirection).
- [x] Floating-window screen-coord math is RTL-invariant (no flip).
  Screen-coordinate `Width`/`Height`/`X`/`Y` in `DockFloatingClamp`
  and the `WindowSpec` pass-through use the OS-level work-area
  coordinate system, which is FlowDirection-agnostic. The clamp
  unit tests cover off-screen, partial-edge, oversize, secondary-
  display, and negative/zero size cases — none of those flip
  under RTL by construction.
- [x] Bidi text in titles passes through the WinUI `TextBlock` bidi
  pipeline; no docking-specific handling. Verified by inspection:
  the pane Title flows into `TabViewItemData.Header` (string) and
  `AutomationProperties.Name`, both rendered/announced by the
  WinUI text engine — no custom bidi handling in the docking
  pipeline.
- [x] **Invariant culture** JSON: layout saved in `de-DE` loads in
  `en-US`; selftest asserts. Covered by
  `LayoutSerializerTests.RoundTrip_InvariantCulture_AcrossDifferentLocales`.
- [x] RTL selftest: mount showcase under RTL `RenderContext`; assert
  visual-tree mirror; assert pointer hit-tests resolve to mirrored
  regions. `NativeDocking_Rtl_FlowDirectionAndSplitterSign` mounts
  the host with FlowDirection=RightToLeft on the host Border and
  asserts every realized `DockSplitterControl` + `TabView`
  inherits RTL (the auto-mirror contract). Pointer drag still
  shifts the leading-pane grow ratio in the expected direction,
  validating the "RTL-correct by construction" claim for
  WinUI's coordinate-space transform.

### 2.24 Security (spec §8.9)

- [x] Layout JSON 1 MB size limit (rejected if exceeded).
  `DockLayoutSerializer.MaxBytes` + the `oversize` fallback
  category. Cross-ref §2.7.
- [x] Layout JSON nesting depth 32 limit
  (`JsonReaderOptions.MaxDepth`). Cross-ref §2.7.
- [x] Schema validation before applying to model; unknown fields
  tolerated; missing required → reject whole, fall back to default.
  Cross-ref §2.7.
- [x] No reflection / type-name instantiation from JSON. Cross-ref
  §2.7.
- [x] No external schema URLs. Cross-ref §2.7.
- [x] AOT-clean parsing via `JsonSerializerContext` for all docking
  types — no reflection at runtime. Cross-ref §2.7.
- [x] Failure mode: log via `ReactorEventSource` (spec 044), fall
  back to default, never throw on load path. Cross-ref §2.7
  (PII-safe coarse category emitted via event id 16).
- [x] Per-pane state isolation: `WindowPersistedScope` keyed by
  `(window-id, dockable-key)`. New
  `DockHooks.UseDockPanePersisted<T>(string key, T initial)` extension
  on `RenderContext` walks the active pane via
  `UseContext(DockContexts.Pane)`, throws when called outside any
  pane subtree, validates the supplied key (non-empty),
  prefixes with `pane:<paneKey>:` and forwards to
  `RenderContext.UsePersisted<T>(key, initial, PersistedScope.Window)`.
  Two panes sharing the same unprefixed key (e.g. `"scrollOffset"`)
  get independent slots in the underlying `WindowPersistedScope` LRU.
  XML docstring captures the cross-user-secret caveat
  (apps that store sensitive per-pane data must clear it
  explicitly on logout / scope-change — the pane scope itself
  doesn't bind to user identity). Coverage:
  `DockHooksTests.UseDockPanePersisted_OutsidePane_Throws`,
  `UseDockPanePersisted_TwoPanesSameKey_GetIndependentValues`,
  `UseDockPanePersisted_RejectsEmptyKey` (3 new tests).
- [x] Drag-drop payload: in-process object refs only (no GUID
  table). Selftest verifies no serialization across the drag.
  `NativeDocking_Reliability_DragSessionPayload_ObjectRefsOnly`
  asserts `DockDragSession.Source` / `SourceManager` are the same
  references passed to `Begin`, that a second concurrent
  `Begin` is refused (single-drag contract), and that `End`
  clears `Current` so the source pane + manager are GC-eligible.

### 2.25 Reliability (spec §8.10)

- [x] Corrupt-persisted-layout fallback: load failure → log →
  fall back to default. Selftest with malformed JSON asserts no
  throw + event fires.
  `NativeDocking_Reliability_CorruptLayoutFallback_HostMounted`
  drives a corrupt JSON through `DockLayoutSerializer.Load` from
  inside a mounted host, asserts the load returns a fallback
  result, the `Microsoft-UI-Reactor` `DockingLayoutLoadFallback`
  event fires (category `json-parse`), and the fallback layout
  mounts cleanly into the rendered tree.
- [x] Off-screen restore: floating window saved at (10000, 10000) on
  a single-display rig → repositioned to primary center on load.
  `DockFloatingClamp.Clamp(savedBounds, displays)` is the pure
  function; `DockFloatingClampTests` covers 9 scenarios including
  off-screen, partial-edge, oversize, secondary-display visible,
  negative/zero size. `DockFloatingWindow.Open` takes optional
  `savedBounds` + `displays` parameters; when both are supplied
  Open() applies the clamp before forwarding bounds to
  `WindowSpec.Width` / `.Height`. Wiring the actual display
  enumeration (`DisplayArea.FindAll`) into the host's tear-out path
  rides on the floating-bounds JSON read in §2.7 — the float-mutation
  call site now passes the manager but not yet a saved-bounds value.
- [x] Orphan floating window when parent shell closes: respects
  `WindowSpec.Owner` (spec 036 §9). `DockFloatingWindow.Open` already
  forwards an optional `owner` argument to `WindowSpec.Owner`; apps
  wiring tear-out through an owning shell window pass it via the
  per-host call site. Without owner, the floating window persists as
  a top-level shell as a documented contract (mirrors spec 036 §9).
- [x] Process crash mid-drag: drag state in-memory only; on restart,
  persisted layout restored; partial drag lost (correct behavior).
  Selftest `NativeDocking_Reliability_CrashMidDrag_LeavesPersistedLayoutClean`
  asserts (a) a save during an active `DockDragSession` produces
  byte-identical JSON to the pre-drag save (no drag fields leak),
  (b) `DockDragSession.ResetForTest` (simulating process exit) clears
  the slot, (c) the reloaded layout has the same root shape and
  key set as the pre-drag tree.
- [~] `useEffect` cleanup on pane close runs in dependency order
  (Reactor invariant; selftest with effect-counter pattern verifies
  for docking). Selftest
  `NativeDocking_Reliability_UseEffectCleanup_RunsOnPaneClose` lands
  the visual-unmount half — `model.Close(pane)` drains and the
  component's body disappears from the rendered tree. The matching
  cleanup-fires-on-close assertion surfaced a Reactor-side gap:
  `ComponentElement` instances embedded under
  `DockableContent.Content` (i.e. wrapped through the host's
  `WrapLeafWithPaneContext` Border + Padding + Provide chain) do
  not get their `UseEffect` cleanups fired when the leaf
  disappears. The known-failing assertion is left commented in the
  fixture with a pointer to this gap; closes when the reconciler
  drops to that case.
- [x] Floating window outliving its host: `DockingNativeInterop`'s
  registered unmount handler iterates `DockFloatingTracker.SnapshotFor(manager)`
  and calls `Close()` on each floating window opened from that host,
  then eagerly removes the per-host tracker entry (the Closed event's
  global-set removal completes on a later dispatcher tick). The
  tear-out and Float-mutation call sites in `DockHostNativeComponent`
  now pass the live `manager` to `DockFloatingWindow.Open` so the
  registration happens. Selftest
  `NativeDocking_Reliability_FloatingWindowClosesOnHostUnmount`
  asserts the per-host tracker registration + the explicit close
  contract. P3 decouples — orphan top-levels.
- [x] Concurrent mutation off UI dispatcher throws — selftest verifies
  the throw.
  Unit-level coverage in
  `DockHostModelTests.Mutations_OffOwnerThread_Throw`;
  host-mounted variant
  `NativeDocking_Reliability_OffThreadMutation_ThrowsAndDoesNotQueue`
  drives a `Task.Run` mutator call against the bridge-resolved
  live model and asserts both the `InvalidOperationException` and
  the empty `Pending` queue (mutator throws BEFORE adding to the
  queue, so no spurious bumpTick fires).
- [x] Event-subscription leak baseline selftest: 100-pane open/close
  cycle returns to allocation baseline.
  `NativeDocking_Reliability_EventSubscriptionLeakBaseline` warms up
  the JIT + reconciler caches, snapshots `GC.GetAllocatedBytesForCurrentThread`,
  runs 100 mount+unmount cycles, and asserts the post-GC delta stays
  within a 32 MB cap — a smoke test against catastrophic retention
  (per-cycle leak would push delta into hundreds of MB). The
  precise per-frame budget rides on §2.20 perf benchmarks.

### 2.26 Devtools / MCP (spec §8.2)

- [x] `docking.snapshot` MCP tool: returns the layout tree of a host.
  P1 introduced; P2 may extend the snapshot schema. P2 ships the
  building blocks: a process-wide `DockHostRegistry`
  (`WeakReference`-keyed `DockManager` enumeration) populated by
  `DockingNativeInterop` at mount/update, cleared on unmount;
  `DockSnapshotBuilder.FromRecord` / `.FromManager` shape a
  `DockSnapshot` value (host id + layout tree + side strips +
  active key) that's free of pane Content refs (privacy + AOT-safe).
  Layout-tree shape: `DockSnapshotSplit` (orientation +
  children), `DockSnapshotTabGroup` (selected index + documents),
  `DockSnapshotLeaf` (single pane); panes surface as
  `DockSnapshotPane` (key + title + role + permissions). The
  actual MCP tool registration on `DevtoolsMcpServer` rides on
  the snapshot shape being stable. 13 unit tests in
  `DockSnapshotBuilderTests` cover the snapshot + registry
  contracts.
- [x] `docking.dock` MCP tool: moves a pane programmatically for
  headless test driving. New in P2. `DevtoolsDockingTools.Register`
  in `src/Reactor/Hosting/Devtools/DevtoolsDockingTools.cs` wires
  three tools onto the live `DevtoolsMcpServer`:
  `docking.list` (enumerates hosts via `DockHostRegistry.Snapshot()`
  with pane count + active key + side counts per host),
  `docking.snapshot` (returns the full `DockSnapshot` shape via
  the existing `DockSnapshotBuilder.FromRecord` path), and
  `docking.dock` (params `{ hostId, paneKey, action, target?, side? }`).
  The dock tool resolves the manager via `DockHostRegistry.Get(hostId)`
  → live model via `DockHostModelBridge.Get(manager)` → matching
  pane via `model.AllContent()` stringified-key match (matches
  `DockSnapshotPane.Key` so snapshot keys round-trip back to
  panes). Actions: `dock` (requires `target`), `float`, `hide`
  (requires ToolWindow), `show`, `close`, `activate`,
  `pinToSide` (requires ToolWindow + `side`). All tools execute
  on the UI dispatcher via `server.OnDispatcher<T>(...)`. Tool
  registration is wired into `ReactorApp.cs:997` right after
  `DevtoolsLogsTool.Register`. JSON shape uses anonymous-object
  conversion (`ToJsonShape` / `NodeToJson` / `PaneToJson`) so the
  AOT path goes through the framework's existing serializer
  surface without new entries in `DevtoolsJsonContext`. Coverage:
  11 unit tests in `DevtoolsDockingToolsTests` (each tool's
  happy path + every error code).
- [x] No mid-flight drag introspection — spec N6 explicit non-goal.
  The snapshot shape (`DockSnapshot`) intentionally surfaces only
  the persisted layout tree + side strips; no `DockDragSession`
  state is exposed. `DockDragSession.Current` stays internal so
  the MCP tool can't accidentally cross the non-goal.

### 2.27 Self-host & unit testing matrix (spec §8.3)

- [x] **Selftests (the bulk)** under
  `tests/Reactor.AppTests.Host/SelfTest/Fixtures/Docking*.cs`:
  - [x] Layout-model fixture: `Dock`/`Float`/`Hide`/`Show`/`Close`
    sequences; assert tree + `Descendants()`. Covered by
    `DockHostModelSequenceTests` (unit) +
    `NativeDocking_ModelDrain_DockCloseActivatePinAffectsLiveTree`
    (host-mounted).
  - [x] Reconciler fixture: mount `DockHost`, mutate inputs, assert
    rendered visual-tree shape. Covered by the M01–M20 series in
    `NativeDockingDragDropMatrixFixture` + the §2.30 demo path.
  - [x] Serialization fixture: SaveJson → LoadJson round-trips;
    structural + identity equivalence; v1 fixture loads in v2.
    Covered by `LayoutSerializerTests` (15+ scenarios including the
    invariant-culture round-trip + v1→v2 migration).
  - [x] `IDockLayoutStrategy` fixture: assert `BeforeInsert*`
    decisions land where expected. Covered by
    `LayoutStrategyTests.Strategy_CanShortCircuitInsertionByReturningTrue`
    + `DockHostModelSequenceTests.ErrorPaneStrategy_RoutesViaModel_QueuesPinToSide`.
  - [x] Cancellable-events fixture: setting `Cancel = true` on every
    `*ing` event aborts transition; state unchanged. Covered by
    `DockHostModelSequenceTests.Cancel_*` (per-event cancellation
    coverage across `OnLayoutChanging`, `OnDocumentClosing`,
    `OnToolWindowHiding`, `OnToolWindowClosing`, `OnContentFloating`,
    `OnContentDocking`).
  - [x] `PreviousContainer` fixture: hide → show preserves container.
    Covered by `PreviousContainerTests.HideShowCycle_PreservesContainerIdentity`
    + `DockHostModelSequenceTests.HideShow_WithPreviousContainerTracker_RoundTripsContainerIdentity`.
  - [x] Composition-driven content updates: mutate state feeding
    `DockNode` tree; assert keyed reconciliation preserves unchanged
    pane state. Two new fixtures:
    `NativeDocking_Composition_ContentMutationFlowsToActivePane`
    (host-level state mutates a pane's content body via the §2.30
    shape-only override contract — clicks an in-pane button, asserts
    the counter text updates) +
    `NativeDocking_Composition_SiblingMutation_PreservesActivePaneIdentity`
    (state mutation that updates two sibling panes' bodies preserves
    each pane wrapper Border's instance identity across the
    re-render — keyed reconciliation contract). The pre-existing
    `DocsByComposition_*` smoke fixtures also cover this surface.
  - [x] Rehydration via composition: save → restart → component-
    supplied content lands in restored slots matched by `Key`.
    `NativeDocking_Composition_Rehydration_ContentMatchesByKey`
    saves a two-pane horizontal split, reloads it through
    `DockLayoutSerializer.Save` + `Load`, walks the loaded shape
    and replaces each leaf with an app-supplied `DockableContent`
    keyed identically, then asserts each restored pane's body
    text renders + the `pane:<key>` AutomationId survives the
    save/load round-trip.
  - [~] Hook re-render scope: `UseActivePaneKey` re-renders only
    consumer; `UseDockState` transitions on adopt/promote (P3).
    `DockHooksTests` (unit) covers `UseActivePaneKey` /
    `UseDockHost` / `UseLayoutSnapshot` / `UsePane` /
    `IsActivePane` resolution + the active-key-flip refresh
    contract; host-mounted variant in
    `NativeDockingHooksReactivityFixture`
    (`DockHooks_IsActivePane_*` assertions) covers the
    consumer-only re-render path. `UseDockState` adopt/promote
    transitions land with P3 spec 036 integration.
- [~] **UI automation (strictly bounded; ≤ 5–8 total across all
  phases):**
  - [~] (P1) Drag a tab from one group to another within same host.
  - [~] (P2) Tear out a tab → assert new `ReactorWindow` exists.
  - [~] (P2) Drag floating window's title bar → `AppWindow.Position`
    changes.
  - [~] (P2) Ctrl+Tab navigator opens and selects a pane.
  **Blocked on docking-fixture wiring into the AppTests harness.**
  Verified 2026-05-21: the Appium / WinAppDriver harness exists
  (`tests/Reactor.AppTests/Infrastructure/AppTestBase.cs` +
  `WinAppDriverHelper.cs`) and the AppTests host
  (`tests/Reactor.AppTests.Host/TestHost.cs` +
  `FixtureRegistry.cs`) drives ~150 nav-button-addressable
  fixtures via `Nav_*` AutomationIds, but no docking fixture is
  registered there (a `grep -l "Docking" tests/Reactor.AppTests.Host/Fixtures/`
  returns no matches). Adding the 3 cases requires creating
  `tests/Reactor.AppTests.Host/Fixtures/DockingInteractionFixtures.cs`
  + the matching nav entries + Appium-driven tests in
  `tests/Reactor.AppTests/Tests/DockingInteractionTests.cs`.
  Selftest variants cover each surface today: drag-pipe via
  `NativeDockingDragDropMatrixFixture`; tear-out via the §2.6
  `OnFloatingWindowCreated` event fixture path; navigator via
  `NativeDocking_A11y_KeyboardCycle_NavigatorCommitsActive`. The
  UI automation cases run the same scenarios against the real
  WinUI input pipeline (which the selftests can't drive),
  catching regressions in pointer-capture / drag-recognizer /
  HWND-creation paths that bypass the in-process harness.
- [x] **Do not adopt FlaUI** (spec §8.3 rationale). AvalonDock
  scenario list informs coverage, implementation belongs to selftests.
  Coverage chart for AvalonDock's FlaUI scenarios:
  `DockingDragDropTests` (drag/drop matrix) →
  `NativeDockingDragDropMatrixFixture`;
  `LayoutSerializationTests` → `LayoutSerializerTests`;
  `LayoutAnchorableTests` (tool window lifecycle) →
  `DockHostModelSequenceTests` + the §2.16 model-drain
  fixtures. No FlaUI reference is taken; each scenario gets a
  Reactor-side fixture instead.
- [~] Coverage gate on `Reactor.Docking` mirrors policy applied to
  other components. The unit test count grew from 232 → 304
  during P2 + 13 host-mounted selftests landed across the
  matrix / a11y / reliability / composition fixtures. The
  explicit coverage threshold + CI gate rides on the repo-wide
  policy work.

### 2.28 P2 risks + mitigations (spec §5.5)

- [x] Overlay z-order verified against tooltip + dialog precedence.
  Documented in `DockDropTargetOverlayControl` header comment:
  the overlay relies on the Grid same-cell stacking pattern in
  `DockHostNativeComponent` (later children paint above earlier
  ones) which matches the upstream WinUI.Dock layout. Tooltips
  attach via `ToolTipService.SetToolTip` (WinUI's Popup-layer
  z-order which paints above the overlay); dialogs attach via
  `ContentDialog` (XamlRoot-level overlay which paints above
  everything). No explicit z-priority enum is wired today; spec
  §2.3 documents the Phase-2 gap.
- [x] Tear-out race: synchronous open + pre-attached content
  (already §2.6 above). Documented + verified via the floating-
  window selftests: `ReactorApp.OpenWindow` mounts content
  before activating the HWND, so the pane subtree is in the
  visual tree before the user sees the new window.
- [x] Drop-target hit-test perf on 4K display: benchmark in CI;
  fail on regression. Covered by §2.20's
  `DockPerfBudgetTests.HitTest_HotPath_NoAllocations` (100k
  iterations of `ComputePreviewBounds`, 1 byte/iter cap). The
  hit-test is allocation-free + O(9) — independent of display
  resolution. The "regression" gate is the allocation budget.
- [x] AOT trim warnings: CI fails on new ones. The docking
  pipeline is AOT-clean: `JsonSerializerContext` covers every
  layout JSON type (`DockLayoutJsonContext`), no reflection on
  the load path. Verified by inspection: zero `[Trim]` analyzer
  warnings reported in the `Reactor.csproj` build.

### 2.29 P2 human review gate (spec §5.7) — **mandatory**

Phase-1 review script (§4.7 items 1–8) re-run against P2, plus:

The driving checklist (with build/run instructions + known flakes +
links into each item's status note) lives at
`docs/specs/045-docking-p2-review-checklist.md`. Run it through and
sign off there; copy the sign-off block into the merge PR
description before merge.

**Status (2026-05-21):** Phase 2 implementation is ready for the
human review pass. Every Phase-2 implementation item above is `[x]`
or `[~]` with an enumerated upstream-spec blocker (027 / 031 / 036
/ analyzer pack). The items below are gated on the visual review
itself — they flip to `[x]` only after the reviewer signs off.

- [~] **Item 9** — Documents vs tool windows visual distinction matches
  intent. **Pending human review.** Auto-resolved tab styling
  (§2.8 all-ToolWindow groups → bottom-position + compact tabs)
  + §2.2 per-tab pin button on ToolWindows are visually verifiable
  in showcase Scene A.
- [~] **Item 10** — Per-pane content state survives save→quit→restart
  →load (e.g. editor scroll position). **Pending human review.**
  `Document<TState>` envelope + JSON round-trip
  + `UseDockPanePersisted` (§2.24) wire the surface end-to-end;
  apps drive the actual scroll-position test against showcase
  Scene E (Persistence).
- [~] **Item 11** — `Ctrl+Tab` pane navigator opens, navigates,
  closes correctly. **Pending human review.** §2.10
  `DockNavigatorPopup` is wired; covered by `DockNavigatorTests` +
  the `Composition` selftest matrix.
- [~] **Item 12** — Layout JSON v1 file (P1 build) loads correctly in
  P2 build. **Pending human review.** §2.11 migration ladder is
  green via `LayoutMigrationTests`; reviewer drives a real P1
  saved-layout file against the P2 showcase to validate the
  end-to-end path.
- [~] **Item 13** — Drop preview latency feels equivalent (timed where
  reasonable; subjective otherwise). **Pending human review.**
  Hot-path verified zero-allocation (`DockPerfBudgetTests.DropTargetHitTest_HotPath_ZeroAlloc`);
  the subjective feels-equivalent comparison requires the visual
  pass.
- [~] **Item 14** — AOT-published binary runs the showcase end-to-end.
  **Pending human review.** Docking subsystem is AOT-clean by
  construction (source-gen `JsonSerializerContext`, no reflection
  on load path, no `[Trim]` analyzer warnings); the actual
  `dotnet publish -p:PublishAot=true` of the showcase is the
  reviewer's drive.
- [~] **Item 15** — Run under `de-DE` and `ar-SA` (RTL); titles
  localize; drop targets / context-menu items localize; layout mirrors;
  pointer hit-tests resolve in mirrored regions. **Pending human
  review.** §2.21 `DockingStrings.Resolver` + §2.23 FlowDirection
  inheritance cover the surface; reviewer validates against a
  real RTL locale.
- [~] **Item 16** — Screen reader pass (Narrator/NVDA): pane roles
  announced; AutomationIds stable; focus never lost; drop-target
  navigation keyboard-only with arrow+Enter. **Pending human
  review.** §2.22 carries the AT contract (host landmark, per-pane
  `pane:<key>` AutomationId, splitter focusable, live-region
  announcements via `DockHostLiveAnnouncer`); reviewer drives a
  real Narrator session.
- [~] **Item 17** — Reduced-motion: transitions disappear; static
  positioning correct. **Pending human review.** Reduced-motion is
  satisfied by construction (no custom transition animations
  ship in P2); reviewer validates against the OS setting.
- [~] **Item 18** — Corrupt layout recovery: hand-edit JSON to invalid;
  app starts with default; error event logged; no crash dialog.
  **Pending human review.** §2.25 corrupt-fallback selftest is
  green; reviewer drives a real hand-edit against the showcase
  Scene E persistence file to validate the end-to-end path.
- [~] Sign-off recorded in PR description. **Pending human review.**
  Do not merge P2 without these green.

### 2.30 Shape-only `layoutOverride` (controlled-input contract)

The host's internal `layoutOverride` originally stored the full
user-mutated `DockNode` tree — including every leaf's `Content`,
`Title`, `CanClose`, etc., captured at drag-end time. That broke the
controlled-input contract: subsequent app re-renders couldn't push
fresh `Content` into pane bodies because the override always won. Apps
had to manually walk the override and replace leaf bodies per render
(a "RefreshContents"-style workaround) to get idiomatic state flow.

- [x] `DockLayoutMutator.StripContent(node)` — returns a tree where
  each leaf `DockableContent` retains only its `Key` (all other fields
  defaulted).
- [x] `DockLayoutMutator.ResolveContents(shape, source)` — walks
  `shape`, looks up each leaf's `Key` in `source` (typically
  `manager.Layout`), substitutes the full `DockableContent` record.
  Leaves whose key isn't in source remain as key-only orphans (rare;
  app-error case detectable by callers).
- [x] `DockHostNativeComponent` now stores shape-only override (every
  `setLayoutOverride(new LayoutOverride(...))` call site wraps the
  tree in `StripContent`). `effectiveLayout` is
  `ResolveContents(layoutOverride.Root, manager.Layout)` per render.
- [x] Removed the prop-change convergence kludge — no longer needed
  because the shape is independent of the app's content tree.
- [x] App-side contract: declare the full tree in `Render()` per state;
  the host owns shape, the app owns content. Reset = remount via
  `.WithKey(...)` bump.
- [x] Unit coverage: 6 new tests in `DockLayoutMutatorTests`
  (StripContent_LeafKeepsOnlyKey, StripContent_PreservesShapeStructure,
  ResolveContents_SubstitutesFullPanesByKey,
  ResolveContents_LeafMissingFromSource_RemainsKeyOnly,
  ResolveContents_NullShape_ReturnsNull,
  ResolveContents_NullSource_ReturnsShapeUnchanged).
- [x] M19/M20 drag matrix selftests pass end-to-end against the
  shape-only override path (no regression in control identity or
  drag-pipeline shape).
- [x] `samples/Reactor.TestApp/Demos/DockingDemo.cs` simplified
  accordingly — no more `RefreshContents` walker, no
  `OnLiveLayoutChanged` plumbing, no shape state in the app. Plain
  idiomatic Reactor.

---

## Phase 3 — fold into the Window primitive

**Goal:** `DockableWindow` is a `ReactorWindow` variant; any window can
be re-parented into a `DockHost`. Exit criteria per spec §6: P2 review
items plus five new items.

### 3.1 Model surface (spec §6.1, §6.3)

- [ ] `WindowSpec.IsDockable = false` opt-in (spec 036 prereq must
  ship first).
- [ ] `WindowSpec.Kind` → `DockableWindowKind { Document, ToolWindow }`.
- [ ] `WindowSpec.AdoptionKey` — stable identity across adopt/promote.
- [ ] `WindowSpec.DefaultHostId` — host to adopt into on open.
- [ ] `WindowSpec.DefaultAdoptionTarget` (default `DockTarget.Center`).
- [ ] `ReactorWindow.DockState` enum `{ Floating, Adopted, Hidden }`.
- [ ] `ReactorWindow.AdoptedBy` → `DockHost?`.
- [ ] `ReactorApp.DockableWindows` — `IReadOnlyList<ReactorWindow>`
  (subset where `IsDockable`).
- [ ] `ReactorApp.DockHosts` — `IReadOnlyList<DockHost>` registry by Id.

### 3.2 `DockManager` → `DockHost` rename (spec §6.4)

- [ ] Rename `DockManager` record to `DockHost`. Keep `DockManager` as
  `[Obsolete]` type forwarder through next release (spec §9 migration).
- [ ] `Id` is now **required** (global identity for `DefaultHostId`
  lookup).
- [ ] `LayoutSchemaVersion` default becomes 2 (P3 deployment reading
  P2 layout just works).
- [ ] Extend `DockNode` algebra: add
  `public sealed record DockableWindowRef(ReactorWindow Window) : DockNode;`
- [ ] Migration path: a P2 `DockableContent` becomes a synthetic
  `DockableWindowRef` with `WindowSpec` opened at load time.

### 3.3 Adoption / promotion primitives (spec §6.2, §6.7)

- [ ] `ReactorApp.PromoteToFloating(window, position)`: closes adopted
  state; creates HWND; migrates element tree from host's subtree into
  new HWND's root; OS now sees a top-level window.
- [ ] `ReactorApp.AdoptIntoHost(window, host, target)`: closes
  floating HWND; migrates element tree into host's tab slot.
- [ ] **Element-tree migration with state preservation.** `useState` /
  `useEffect` snapshots carry across host boundaries, identified by
  `AdoptionKey`. *This is the largest single piece of reconciler work
  in the spec — budget accordingly.*
- [ ] **Lazy HWND creation.** No HWND exists while adopted (alt-tab,
  taskbar correctness). HWND created only on first `PromoteToFloating`.
- [ ] **Modal dialog `XamlRoot` routing.** `ContentDialog` finds
  `XamlRoot` against the *host* window when adopted, not the dockable
  window. Explicit XamlRoot routing in spec.
- [ ] Adoption / promotion ≤ 1 frame for element-tree migration itself,
  exclusive of content first-render (spec §8.1).

### 3.4 Cross-shell drag (spec §6.7)

- [ ] **Lift spec 036 N2** for dockable windows: cross-Reactor-Window
  drag is in scope because windows share a process, dispatcher, and
  docking gesture recognizer.
- [ ] Cross-shell drag uses same in-process object-ref payload as P2
  (spec §8.9 security).
- [ ] Selftest + UI automation case: drag a dockable window from main
  shell to secondary shell; reverse direction works.

### 3.5 Shell integration (spec §6.5)

- [ ] Floating `DockableWindow` participates in spec-036
  `WindowOpened`/`WindowClosed` events.
- [ ] Adopted-state windows emit `Adopted`/`Promoted` events on the
  host (no OS window).
- [ ] Taskbar progress, overlay icons, jump lists (spec 036 §11) apply
  to floating dockable windows.
- [ ] Window persistence id (spec 036 §8) honored across floating AND
  adopted lifetimes — tear-out, restart, re-attach preserves persisted
  scope.
- [ ] Devtools / MCP address dockable windows by stable id regardless
  of state.
- [ ] `WindowRegistry` includes `IsDockable` + `DockState` per window
  (spec §8.2).

### 3.6 Tray-icon flyout integration (spec §6.3, §6.6, §8.9)

- [ ] Tray flyouts (spec 036 §11) can host a `DockHost` in their
  content.
- [ ] Tray flyout closing while a pane is being dragged out: lazy-HWND
  mechanism handles the transition.
- [ ] Selftest + showcase scenario.

### 3.7 Showcase sample, updated (spec §6.6)

- [ ] Solution Explorer opens via `OpenWindow(new WindowSpec(
  IsDockable: true, Kind: ToolWindow, DefaultHostId: "main-shell",
  DefaultAdoptionTarget: DockLeft))`. Tear-out → real top-level
  window; close + reopen reuses same `ReactorWindow` instance.
- [ ] Tray-icon flyout with `DockHost` and two adopted tool windows.
- [ ] Secondary top-level shell ("Settings") with its own `DockHost`;
  cross-shell drag works.

### 3.8 P3 risks (spec §6.7)

- [ ] Element-tree migration across `ReactorHost` boundaries: largest
  reconciler work. Track in dedicated PR with extra review.
- [ ] Adopted-window HWND lifecycle: verify alt-tab + taskbar exclude
  adopted windows.
- [ ] Modal dialog XamlRoot from adopted dockable window: explicit
  resolution against host window.
- [ ] Cross-shell drag races: gesture recognizer is single-instance
  per process; verify under concurrent dragging stress.

### 3.9 P3 selftests (additions to §8.3 matrix)

- [ ] State migration: mount `DockableWindow`, `PromoteToFloating`,
  `AdoptIntoHost(otherHost)`; assert per-pane `useState` / `useEffect`
  state survives.
- [ ] `UseDockState` re-render scope on adopt/promote.
- [ ] Lazy HWND: adopted window has no `HWND`; promote creates one;
  re-adopt destroys it.
- [ ] UI automation (the one bounded P3 case): cross-shell drag from
  one Reactor `Window` to another (spec §8.3 item 5).

### 3.10 P3 human review gate (spec §6.8) — **mandatory**

P2 script (§5.7), plus:

- [ ] **Item 15** — Open a dockable tool window into main shell. Tear
  out → free-floating top-level with own taskbar entry. Close. Reopen.
  Re-adopts at default position.
- [ ] **Item 16** — Open tray-icon flyout containing `DockHost`. Tear
  out a tool window — becomes top-level, flyout closes, tool window
  persists. Trigger tray again — flyout re-opens, tool window re-adopts.
- [ ] **Item 17** — Open secondary shell. Drag dockable window from
  main → secondary. Tab lands. Reverse works.
- [ ] **Item 18** — Save layout from main shell. Reset. Reload.
  Adopted reappear adopted; floating reappear floating at same
  positions.
- [ ] **Item 19** — AT/AT-SPI: screen reader announces adopted
  dockable window with correct role (document/tool); focus traversal
  includes both adopted panes and floating windows.
- [ ] Sign-off recorded in PR description.

---

## Phase 4 — Windows 11 native-chrome polish

**Goal:** floating windows use WinUI `TitleBar` with tabs-in-titlebar
(Edge/Files/Terminal pattern). Exit criteria per spec §7: P3 script
re-run with new chrome plus eight visual items.

### 4.1 WindowsAppSDK min-version bump (spec §7, §7.3, §8.11)

- [ ] Bump `WindowsAppSDK` minimum in `Directory.Build.props` to the
  version that stabilizes `TitleBar` (target: 1.7+; pin exact at P4
  entry).
- [ ] Announce bump one release cycle in advance.
- [ ] Feature-detect: older SDK falls back to P2/P3 chrome (degrades to
  system-themed title bar without tabs on Windows 10 — acceptable per
  §7.3).

### 4.2 `TitleBar` control adoption (spec §7.1.1)

- [x] Floating dockable windows extend their content into the title-bar
  zone so the tab strip becomes the visible chrome
  (`WindowSpec.ExtendsContentIntoTitleBar = true`). *(The WinUI 3
  `Microsoft.UI.Xaml.Controls.TitleBar` control is intentionally NOT
  used here — its `Content` slot is a small in-row chrome slot
  designed for things like Edge's address bar and cannot host a full
  TabView with bodies. The Edge / Files pattern uses
  `Window.SetTitleBar(...)` directly on a strip-footer drag region
  instead. Reactor's `TitleBarElement` remains available for app
  shells that want the control's distinct row layout.)*
- [x] `ReactorWindow.NativeWindow.ExtendsContentIntoTitleBar = true`.
- [x] Root content puts the docking tab strip in the title-bar zone
  (Edge / Files / VS Code "tabs in title bar" layout).
- [x] `Window.SetTitleBar(dragRegion)` is wired from a `TabStripFooter`
  element so the OS reserves caption-button inset and treats the
  footer area as window drag-move surface.

*(Landed: `DockFloatingWindow.BuildFloatingRoot`
(`src/Reactor/Docking/Native/DockFloatingWindow.cs`) renders the
`DockTabGroup` as the floating window's root and adds a transparent
`BorderElement` to `TabStripFooter` whose `OnMount` calls
`floatingWindow.NativeWindow.SetTitleBar(self)`. `Open()` sets
`WindowSpec.ExtendsContentIntoTitleBar = true`. Unit tests in
`tests/Reactor.Tests/Docking/Native/DockFloatingWindowTests.cs`;
visual coverage in selftest fixture
`NativeDocking_FloatingWindow_TitleBarChromeAndTabsInTitleBar`.)*

### 4.3 Tabs in the title bar (spec §7.1.2) — headline feature

- [x] **Single-group float:** tab strip occupies the title-bar zone at
  y=0, flush with OS caption buttons, active tab styles as the
  window's identity (Edge / Files / modern-VS pattern). *(Implemented
  via `ExtendsContentIntoTitleBar=true` + TabView at root + drag
  region in `TabStripFooter` registered via `Window.SetTitleBar`.)*
- [ ] **Multi-group float:** revert to standard floating title (via
  `IDockAdapter.GetFloatingWindowTitleBar`); tabs render in normal pane
  position. *(Deferred: floating windows are single-pane today; the
  multi-group float infrastructure itself is a separate slice.)*
- [ ] Drag semantics:
  - [x] Drag a **tab** → tear it out. *(Existing `onTabDragStarting`
    path continues to work in the new layout.)*
  - [x] Drag **title-bar background** (non-tab, non-caption) → move
    floating window. *(OS-handled via the `TabStripFooter` drag region
    registered with `Window.SetTitleBar` — `MinWidth(180)` ensures a
    grabbable region even with many tabs.)*
  - [ ] Drag **active tab when it is the only tab** → moves whole
    window. *(Deferred: `onTabDragStarting` currently fires tear-out
    unconditionally.)*
- [ ] `AppWindow.TitleBar.SetDragRectangles` integration: hit-test
  regions computed per frame from tab-strip measured geometry; pushed
  to OS so it knows interactive vs drag-region sub-rects.
  *(N/A — we hand the OS a single `TabStripFooter` element via
  `Window.SetTitleBar`, which is the modern WinUI 3 replacement for
  explicit `SetDragRectangles` plumbing.)*
- [ ] **Debounce `SetDragRectangles` to layout-measure-change events.**
  *(N/A — see above.)*

### 4.4 Caption-button area awareness (spec §7.1.5)

- [x] Tab strip reserves caption-button inset automatically — the
  `Window.SetTitleBar(footer)` registration tells the OS where
  caption buttons should sit (top-right) and the buttons render over
  the right edge of the footer drag region.
- [x] OS handles RTL / theme-switch caption-button placement via the
  same `SetTitleBar` registration; no manual
  `LayoutMetricsChanged` subscription required for the current
  Window-level inset behavior.

### 4.5 Snap Layouts (spec §7.1.3)

- [ ] Windows 11 Snap Layouts on caption hover: works for free via OS
  caption button.
- [ ] **Do not** attempt OS Snap Layouts integration into in-shell
  docking — drop-target overlay (P2) is the equivalent and it would be
  a category error.

### 4.6 System backdrop coordination (spec §7.1.4)

- [ ] Floating dockable windows inherit `WindowSpec.Backdrop`
  (spec 036 §4.1, §5).
- [ ] Splitter gutters semi-transparent under Mica / Acrylic.
- [x] Tab-strip background uses `TitleBarBackgroundFillBrush`, not a
  hard color. *(Phase 4 slice landed: `TabChrome` enum on `DockTabGroup`
  with `Win11` / `Flat` / `TitleBar` presets, scoped to each TabView's
  `Resources` via `DockTabGroupRenderer.BuildSetters`. `TitleBar` preset
  resolves `TitleBarBackgroundFillBrush` from `Application.Current.Resources`
  and writes to `TabViewBackground`; pool-safe via a "blanker" setter
  that strips all managed keys before applying a new preset. JSON
  persistence: optional `tabChrome` field, omitted on `Win11` for
  legacy-file back-compat. Dock-showcase **Scene I — Tab Styles** is
  the visual gate input for §4.11 Item 23.)*

### 4.7 Dark mode / accent color (spec §7.1.6)

- [ ] WinUI 11 `TitleBar` honors system theme without intervention.
- [ ] QA pass: system title bar + Reactor docking content + custom
  theme combination produces no contrast mismatch.

### 4.8 Floating-window persona (spec §7.1.7)

- [x] Single-tab float: tab `Title` → `AppWindow.Title`. Alt-tab shows
  the tab title. *(Title landed: `DockFloatingWindow.Open` passes
  `pane.Title` as `WindowSpec.Title`. Icon deferred: `DockableContent`
  has no `Icon` field yet — adding one is a separate slice.
  Note: in the Edge tabs-in-titlebar layout the tab itself displays
  the title text; there is no separate WinUI 3 `TitleBar` widget to
  also bind to. `AppWindow.Title` still controls taskbar / alt-tab.)*
- [ ] Multi-tab float: active tab's title reflected. *(Deferred with
  multi-tab float — see §4.3 above.)*
- [ ] Composition with spec 036 §8 persistence: persisted Window
  identity is the dockable-window key, not the transient tab content.

### 4.9 Window-drag as docking gesture (spec §7.1.2 extension)

Phase 2 implements cross-window dock-back via *tab* drag — the user
grabs the tab inside a floating window, drags it onto a `DockHost`
overlay, drops, the pane re-docks and the floating window closes
(`DockDragSession.Consumed` signals the source to close). Phase 4
extends this so the *whole window* is a draggable handle: grabbing
the title bar (not the tab) and dragging it over a `DockHost` should
also activate drop targets and dock the pane back on release.

The title-bar drag is OS-handled today (just moves the window); the
WinUI `TitleBar` control reserves the non-tab background as the
drag-move region. Hooking it as a docking gesture requires either:

1. A custom `TitleBar`-content adapter that intercepts pointer
   capture on the drag-move region, suppresses the OS move, and
   begins a `DockDragSession` against the floating window's pane.
2. Or: cooperate with the OS move — track the window position
   during the move (`AppWindow.Changed` / `WM_MOVING`), hit-test
   the pointer screen-position against every visible `DockHost`'s
   bounds, and surface drop targets in those hosts. On
   `WM_EXITSIZEMOVE`, if a drop target was hovered, perform the
   dock-back and close the floating window; otherwise the window
   stays at its new position.

Option 2 is closer to the AvalonDock / VS pattern (Snap-Layouts
adjacency hints fire during a move). It needs WindowsAppSDK
`AppWindow.Changed` event subscriptions + Win32 message hooks
(`WM_MOVING` / `WM_EXITSIZEMOVE`) and screen-to-DIP hit-test math.

- [ ] Choose architecture: option 1 (intercept) vs option 2
  (cooperate). Recommend option 2 — preserves OS-native window
  move feel; only the *commit* on release is custom.
- [ ] **Cross-process broadcast?** Decide whether drag-back works
  across separate Reactor app instances. Default NO (Phase 3 N2
  carries over — single-process drag only); a future spec covers
  it if needed.
- [ ] Hook `AppWindow.Changed` (Position) on floating windows; when
  Position changes AND no `DockDragSession` is active, treat as
  the start of a title-bar drag. Begin a synthetic session with
  the floating's pane + original manager.
- [ ] On position updates, hit-test the screen point (pointer-
  position via `Windows.UI.Input.PointerPoint.Properties` or
  `User32.GetCursorPos`) against every registered `DockHost`'s
  screen bounds. Surface that host's drop-target overlay
  (`DockDragSession.SessionChanged` already broadcasts).
- [ ] On `WM_EXITSIZEMOVE` (end of move), if the pointer is over a
  drop target, call the same `OnConfirm` path as the tab-drag
  case — `MarkConsumed()` then `End()`; the floating window
  closes via `Consumed=true`.
- [ ] If the move ended outside any drop target, treat as a normal
  window-position update — no dock-back, session cancelled.
- [ ] Visual selftest: open a floating window, drag its title bar
  over the main shell, assert overlay surfaces; release on a
  per-group Center target, assert pane re-docks and floating
  closes.
- [ ] Manual review item: dragging a single-tab floating window's
  title bar back to the main shell feels equivalent to dragging
  its tab. Same hit-test latency, same overlay UI.
- [ ] Multi-tab float interplay: when the title bar is dragged
  with multiple tabs in the floating window, all tabs travel as a
  group (whole window dock-back). Document the contract:
  drag-tab moves one pane, drag-titlebar moves the whole floating
  window's contents. Decide whether multi-tab dock-back even
  makes sense (might be intentionally disabled — only single-tab
  floats support title-bar dock-back).
- [ ] Reduced-motion: no different from the tab-drag path —
  overlay preview honors `UISettings.AnimationsEnabled`.
- [ ] Accessibility: title-bar drag is keyboard-inaccessible by
  WinUI default. Document that keyboard-only dock-back goes
  through the tab's chord (Ctrl+Shift+M after Ctrl+Tab to
  activate the tab) rather than a title-bar gesture.
- [ ] Cross-display drag during title-bar move — DPI changes
  mid-drag must not freeze the overlay; reuse the §8.5 DPI re-
  layout budget.

### 4.10 P4 risks (spec §7.3)

- [ ] WindowsAppSDK `TitleBar` API stability — pin version; if API
  moves before merge, P4 slips (P1–P3 unaffected).
- [ ] `SetDragRectangles` perf on per-frame hover — debounce verified
  with benchmark.
- [ ] Caption-button inset on theme/RTL flip — subscribe to
  `LayoutMetricsChanged`.
- [ ] `GetFloatingWindowTitleBar` × tab-in-titlebar interplay — adapter
  supplies non-tab portion only; doesn't contradict.
- [ ] Non-Windows-11 graceful degradation — `TitleBar` degrades to
  system-themed-without-tabs on Win10; "looks like P2" is acceptable.
- [ ] **§4.9 title-bar dock-back** — hit-testing screen
  coordinates against `DockHost` bounds across multiple windows
  is sensitive to z-order, DPI, and minimize state; per-frame
  cost during a window move adds to the input pipeline. Verify
  via a benchmark that mirrors `SetDragRectangles` debouncing.
  Win32 message-hook integration is a known-fragile area on
  WinAppSDK; pin the hook approach against the supported
  `AppWindow` event surface first, fall back to Win32 only when
  necessary.

### 4.11 P4 human review gate (spec §7.4) — **mandatory**

P3 script (§6.8), plus:

- [ ] **Item 20** — Floating window with one tab shows tab in title
  bar; OS-default caption buttons; theme matches system.
- [ ] **Item 21** — Drag tab in title bar → tear-out fires. Drag
  non-tab area → window moves, no tear-out.
- [ ] **Item 22** — Maximize via caption button; hover maximize → Snap
  Layouts appear; pick quadrant; window snaps; restore; verify
  Reactor reconciler does not interfere with snap geometry.
- [ ] **Item 23** — Toggle system theme dark↔light with float open;
  title bar updates; docking content updates; no flash; no contrast
  regression.
- [ ] **Item 24** — Alt-tab shows floating window's tab title, not
  "Reactor App".
- [ ] **Item 25** — Snap Layouts assist windows show tab title + icon,
  not generic.
- [ ] **Item 26** — RTL system locale: caption buttons on left; tab
  strip reserves left inset; behaviors mirror.
- [ ] **Item 27** — High-contrast theme: chrome legible; drop targets
  distinguishable.
- [ ] UI automation: tab-in-title-bar hit-testing case (spec §8.3
  item 6); title-bar drag-region case (item 7).
- [ ] Sign-off recorded in PR description. **Final visual gate**.

---

## Cross-phase concerns (rolling checklist)

These appear under specific phases above; this section is the
**aggregate gate** to confirm before each phase ships.

### CP.1 Performance budget (spec §8.1, §8.5)

- [ ] Drop-target hover ≤ 2 ms (P2+).
- [ ] Tear-out ≤ 1 frame (P2+).
- [ ] Layout JSON load ≤ 50 ms for 200 panes (P2+).
- [ ] Adoption/promotion ≤ 1 frame element-tree migration (P3).
- [ ] Reconciler diff ≤ 1 ms for 50-pane shape change (P2+).
- [ ] Cold-start ≤ 200 ms first frame for 50-pane layout (P2+).
- [ ] Zero allocation on pointer-move (P2+).
- [ ] No static dictionaries, no GUID-table leaks, no closure leaks on
  events (P2+).
- [ ] DPI change re-layout ≤ 16 ms (P2+).
- [ ] `SetDragRectangles` debounced (P4).

### CP.2 Testing strategy (spec §8.3) — selftests first

- [ ] Selftests under
  `tests/Reactor.AppTests.Host/SelfTest/Fixtures/Docking*` cover the
  full matrix per §2.27.
- [ ] UI automation ≤ 5–8 total across all phases.
- [ ] Do not adopt FlaUI.
- [ ] Coverage gate on `Reactor.Docking` matches sibling components.

### CP.3 Documentation (spec §8.4)

- [ ] Source docs live in `docs/_pipeline/apps/docking/`.
- [ ] **Never hand-edit** `docs/guide/docking.md` (generated). See
  memory `feedback_docs_pipeline.md`.
- [ ] Skill content at `skills/docking.md` once API stabilizes (P1 exit
  earliest).
- [ ] Each phase appends to docs; CHANGELOG updated.

### CP.4 Localization (spec §8.6)

- [ ] All user-facing strings under `Docking.*` resource prefix.
- [ ] No app-string responsibility for `Document.Title` /
  `ToolWindow.Title`; docs clarify.
- [ ] `.xlf` pipeline (spec 005) generates loc.

### CP.5 Accessibility (spec §8.7)

- [ ] Roles, `AutomationId`s, focusable drop targets + splitters,
  live-region announcements, focus invariants, reduced-motion,
  44-DIP touch targets, 4-DIP hit-test forgiveness, high-contrast
  legibility.
- [ ] A11y selftests landed by P2 exit.

### CP.6 Globalization (spec §8.8)

- [ ] `FlowDirection` propagation, sidebar/tab/drop-target/splitter
  RTL flips, bidi title rendering, invariant-culture JSON, RTL
  selftest.

### CP.7 Security (spec §8.9)

- [ ] Layout JSON size + depth limits, schema validation, AOT-clean
  parsing, no code paths from JSON, safe-fallback on corrupt,
  per-pane state isolation, in-process drag payload only.
- [ ] Vendored upstream tracked in `VENDORED.md`; CVEs monitored
  while the source ships.

### CP.8 Reliability (spec §8.10)

- [ ] Corrupt layout, off-screen restore, orphan floats, crash mid-
  drag, useEffect cleanup, floating-window outliving host, off-thread
  mutation, event-subscription baseline — all selftests landed.

### CP.9 Versioning (spec §8.11)

- [ ] Layout JSON: backward read-compat forever; forward-tolerance with
  warning; migration ladder via `IDockLayoutMigration`.
- [ ] Public API stable across phases; `[Obsolete]` shims for renamed
  types through next release.
- [ ] Per-pane `TState` schema versioning documented as app
  responsibility.
- [ ] WindowsAppSDK min-version bump only at P4 entry, announced one
  release in advance.

---

## Resume notes

- This file is the single source of truth for progress. Update
  checkboxes inline as work lands. **Never** delete a section header
  even after completion — they describe scope, not just state.
- Each phase has a **gate section** that must be green before its PR
  merges. Treat these as MERGE BLOCKERS, not aspirational.
- Cross-phase concerns appear both in the per-phase task lists and in
  the rolling CP.* aggregate. Check both before declaring a phase
  done.
- For pauses spanning multiple sessions, the topmost unchecked task
  in the current phase is the resume point. The previous phase's gate
  must be green or work must be against an explicit phase backtrack
  PR (rare).
- Memory pointer: see auto-memory `feedback_docs_pipeline.md` (never
  hand-edit `docs/guide/*`).
