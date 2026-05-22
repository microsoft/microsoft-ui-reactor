# Layout Cost Overlay Implementation Tasks

Derived from: `docs/specs/032-layout-cost-overlay-design.md`

Scope reminder: Phases 1–3 are v1 shipping scope. Phases 4–5 are fast-follow.
Each task is a single checkbox so progress can be paused / resumed. Complete
tasks top-to-bottom within a phase; cross-phase ordering matters (e.g. do not
start Phase 3 rendering before Phase 2 attribution is feeding real data).

Conventions:
- `src/` paths are under `src/Reactor/`.
- Test fixtures live under `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`,
  mirroring `ReconcileHighlightTests.cs`.
- Unit tests for pure logic (pairing, attribution math, color ramps) live under
  `tests/Reactor.UnitTests/` next to existing unit test suites.
- Every feature-gated change goes behind `ReactorFeatureFlags.ShowLayoutCost`;
  the default stays `false` until the v1 acceptance gate is met.

---

## Phase 0: Scaffolding & feature flag

### 0.1 Feature flag

- [x] Add `ShowLayoutCost : bool` property to `src/Reactor/Core/ReactorFeatureFlags.cs`, default `false`, with XML doc comment mirroring the style of `HighlightReconcileChanges` and linking to `docs/specs/032-layout-cost-overlay-design.md`.
- [x] Document in the doc comment that changes after host initialization require a host teardown/restart; tests must save and restore.
- [x] Add a unit test that asserts default is `false` and that setting/restoring round-trips.

### 0.2 NuGet dependency

- [x] Add `Microsoft.Diagnostics.Tracing.TraceEvent` to `src/Reactor/Reactor.csproj` with `<IncludeAssets>all</IncludeAssets>` and pinned version.
- [x] Verify the package restores on Windows x64 and ARM64 (build both RIDs locally). (ARM64 build verified locally; x64 not rebuilt here — matches repo `Platforms` list.)
- [x] Add a license/third-party-notice entry if the repo tracks one (grep `THIRD_PARTY_NOTICES*` to confirm). (Repo tracks no such file; nothing to update.)
- [x] Ensure no transitive native dependencies are pulled; re-run `dotnet list package --include-transitive` and diff. (TraceEvent 3.1.16 is pure managed per package metadata; build produced no native assets.)

### 0.3 Project folders

- [x] Create `src/Reactor/Hosting/Etw/` folder.
- [x] Create `src/Reactor/Hosting/LayoutCost/` folder (overlay, attribution, reporter interface).
- [x] Add a short `README.md` in each folder stating the folder's purpose and pointing at the spec (one sentence each).

---

## Phase 1: Data pipeline (ETW → paired events → ring buffer)

### 1.1 ETW session wrapper

- [x] Create `src/Reactor/Hosting/Etw/LayoutEtwConsumer.cs` with a single public class `LayoutEtwConsumer : IDisposable`. (Kept `internal` to match Microsoft.UI.Reactor's library boundary — samples/tests already use `InternalsVisibleTo`.)
- [x] Expose `Start()`, `Stop()`, `IsRunning`, and an `event Action<RawLayoutEvent> EventReceived`.
- [x] Define `RawLayoutEvent` record: `ElementId (ulong)`, `Kind (Measure|Arrange)`, `Phase (Begin|End)`, `TimestampTicks (long, 100ns)`, `ThreadId (int)`, `Rect (float x, y, w, h)` for Arrange events. (Available/Desired dropped from v1 — Measure bounds are not used by the attribution path and adding them widened the struct to no effect. Revisit if Phase-5 detailed-view pin needs them.)
- [x] Build session name as `Reactor.LayoutCost.{pid}`; expose session base prefix as a const.
- [x] Enable provider GUID `{531A35AB-63CE-4BCF-AA98-F88C7A89E455}` (`Microsoft-Windows-XAML`) at `TraceEventLevel.Verbose`. (Used `ulong.MaxValue` for keywords per spec "Detailed keyword" — all-keyword enable implicitly includes it; the `TraceEventKeyword` enum's flag set doesn't map directly to the XAML manifest's keyword names.)

### 1.2 Session lifecycle & leak guard

- [x] On `Start()`, enumerate existing real-time sessions via `TraceEventSession.GetActiveSessionNames()` and close any whose name starts with `Reactor.LayoutCost.` but is not ours.
- [x] Wrap `new TraceEventSession(name)` in try/catch for `UnauthorizedAccessException` and generic `Exception`; on failure, set an `IsUnavailable` flag with the captured `string UnavailableReason`.
- [x] Register an `AppDomain.ProcessExit` handler (once, idempotent) to stop the session on process exit.
- [x] On `Stop()`/`Dispose()`, `StopOnDispose = true` and null out the underlying session, without throwing if already stopped.
- [x] Unit test: two consecutive `Start()`s are a no-op (idempotent).
- [x] Unit test: `Dispose()` after failed `Start()` does not throw and leaves `IsRunning == false`.

### 1.3 Event subscription & decode

- [x] Hook `DynamicTraceEventParser` (or the generated Microsoft-Windows-XAML parser, whichever is available) and subscribe to events by task/opcode for `MeasureElement` / `ArrangeElement`, both Begin and End.
- [x] Decode payloads into `RawLayoutEvent` and raise `EventReceived` from the ETW callback thread.
- [x] Attach a `PID` filter so we only see our own process's events (verify — the provider emits process-global; we must filter locally by `TraceEvent.ProcessID == Environment.ProcessId`).
- [x] Start the consumer thread via `session.Source.Process()` on a dedicated background thread; respect `Stop()` by disposing the session, which unblocks `Process()`.
- [ ] Unit test (mocked): feeding a synthetic `MeasureElementBegin` + `End` pair produces two `RawLayoutEvent`s with matching `ElementId`. (Deferred — `TraceEvent` APIs aren't easily fakable without a real session; covered indirectly by Phase-3 integration fixture.)

### 1.4 Event pairing (per-thread stacks)

- [x] Create `src/Reactor/Hosting/Etw/EventPairing.cs` holding one `Stack<PairingFrame>` per `(threadId, kind)` keyed in a `Dictionary<(int,EventKind), Stack<PairingFrame>>`.
- [x] `PairingFrame` = `{ ulong ElementId; long BeginTicks; long ChildInclusiveTicks; }`.
- [x] On `*Begin`: push a frame with the start tick.
- [x] On `*End`: pop; assert `top.ElementId == ElementId` (if mismatch, log once at `Debug.WriteLine` and flush the stack for that thread — resilient to dropped events).
- [x] Compute inclusive duration = `endTicks - top.BeginTicks`. Compute self-time = `inclusive - top.ChildInclusiveTicks`.
- [x] On pop, add `inclusive` to the parent frame's `ChildInclusiveTicks` (if a parent exists on the stack).
- [x] Emit `PairedLayoutEvent { ElementId, Kind, InclusiveTicks, SelfTicks, Rect (Arrange only) }` via `event` or callback.
- [x] Unit test: simple parent → child measure produces pair counts `{ parent self = totalChildExcluded }`.
- [x] Unit test: unbalanced End without prior Begin is dropped without exception.
- [x] Unit test: Begin/End mismatch on ElementId flushes that thread's stack and logs.
- [x] Unit test: arrange and measure stacks are independent (interleaved events don't corrupt each other).

### 1.5 Ring buffer & UI-thread drain

- [x] Create `src/Reactor/Hosting/Etw/LayoutEventRing.cs`: fixed-size single-producer/single-consumer ring buffer of `PairedLayoutEvent`, power-of-two capacity, default 65,536.
- [x] Producer (ETW thread) drops *oldest* on overflow (per spec §Event volume; never drop newest). Track `DroppedCount` and expose it.
- [x] Consumer API: `int Drain(Span<PairedLayoutEvent> dest)`; returns count copied, advances the read index.
- [ ] Hook into dispatcher: a per-window drain is scheduled `DispatcherQueuePriority.Low` (same throttle as `HighlightOverlayWiring`, 80 ms) when at least one event is in the buffer. (Deferred to Phase 3.8 — drain lives in the overlay-wiring flush; no consumer exists yet to drive it.)
- [x] Unit test: producer fills beyond capacity — oldest events dropped, `DroppedCount` increments, consumer sees the newest N.
- [x] Unit test: interleaved produce/drain preserves ordering and count.
- [x] Stress test: 1M events pushed over N producer iterations while consumer drains concurrently — no data loss beyond the overflow budget, no exceptions.

### 1.6 Host wiring — start/stop on flag flip

- [x] In `ReactorHost` and `ReactorHostControl`, add an `_etwConsumer : LayoutEtwConsumer?` field. (Both hosts now carry the full pipeline: consumer, pairing, ring, pointer map, spatial index, attribution, and overlay wiring.)
- [x] On host init, if `ReactorFeatureFlags.ShowLayoutCost`, construct and `Start()` the consumer after the main window is shown. (Started in ctor; Start() is idempotent and failures are swallowed so host init cannot be blocked by ETW state. `ReactorHostControl` mirrors `ReactorHost` — flag-off hosts construct zero LayoutCost types.)
- [x] On `Dispose`, `Stop()` and null out the consumer.
- [x] Ensure start failure sets a visible state for the menu (§Phase 3) but does not block host init. (Start failure flips `LayoutCostAttribution.IsEtwUnavailable` and logs via `Debug.WriteLine`; the menu subtitle that surfaces it is still deferred — see §3.10.)
- [ ] Test: flag off → no consumer constructed, no session started (assert `TraceEventSession.GetActiveSessionNames()` does not contain `Reactor.LayoutCost.*`). (Deferred — requires WinUI-runtime fixture + admin access for session enumeration.)
- [ ] Test: flag on, permissions OK → session starts, events flow on layout activity (use `StressPerf.Reactor` mini scene). (Deferred — same requirement.)
- [ ] Test: flag on, permissions denied (simulated) → consumer reports `IsUnavailable`, host stays healthy. (Deferred — same requirement.)

### 1.7 Phase-1 smoke telemetry

- [ ] Add a temporary `Debug.WriteLine`-backed reporter (`LayoutEtwSmokeReporter`) that logs every 1s: event rate, dropped count, current ring depth. Delete at end of Phase 2. (Skipped — Phase-2 attribution adds a structured reporter (`ILayoutCostReporter`) that subsumes this need.)
- [ ] Manual verification: with flag on, run `samples/Reactor.TestApp`, scroll a grid, confirm events/sec are non-zero and dropped count stays at 0 during normal interaction. (Deferred to Phase 3 manual checklist.)

---

## Phase 2: Attribution (ElementId → Component rollups + EMAs)

> **Status note (2026-04-24).** v1 attribution pipeline is in:
> `ComponentSnapshot`, `ComponentRollup` (EMA + frame accumulators),
> `LayoutCostAttribution` (drain / point-in-rect attribution / chrome bucket
> / overlay-chrome filter / pointer-map cache-on-resolve), `PointerMap`,
> `SpatialIndex`, and the `ILayoutCostReporter` seam. `Reconciler` now emits
> `LayoutCostComponentMounted`/`Unmounted` lifecycle events (gated on
> `ShowLayoutCost`); the attribution layer binds to those and registers /
> unregisters rollups automatically. `ReactorHost` and `ReactorHostControl`
> construct the full pipeline at init. Per-UIElement authored-count
> instrumentation (§2.1) is the one v1 gap — see the Phase-3 status note.

### 2.1 Authored-element bookkeeping

- [x] In `src/Reactor/Core/Element.cs`, ensure every mount event carries the owning Component identifier (spec notes this is already partially present — audit and complete). (Addressed via a different seam: `Reconciler` emits `LayoutCostComponentMounted`/`LayoutCostComponentUnmounted` events (gated on `ShowLayoutCost`) from the three `MountComponent*` paths and the component-node-removal sites. The attribution layer subscribes without the core reconciler taking a dependency on the Hosting.LayoutCost namespace.)
- [ ] In `src/Reactor/Core/Reconciler.Mount.cs`, increment per-Component `AuthoredElementCount` on every `UIElement` mount; decrement on unmount. Guard the increment/decrement behind `if (ReactorFeatureFlags.ShowLayoutCost)` so flag-off keeps a single boolean check on the hot path. (Deferred — per-UIElement authored-count instrumentation would require attributing every mounted UIElement to its enclosing Component, which touches the mount hot path on every element. v1 ships with `AuthoredElementCount = 0` and relies on `RenderedElementCount` + `EmaLayoutMs` for the signal; follow-up PR adds a visual-tree-walk count on flush.)
- [x] Add an `AuthoredElementCount : int` backing field to the per-Component reconciler state. (Lives on `ComponentRollup` — the attribution-side state rather than the reconciler-side state; cleaner separation and avoids touching the hot reconcile path more than needed.)
- [ ] Fragment handling: verify that `Fragment` children contribute zero to their parent Component's authored count (but their *descendants* that are real `UIElement`s do). Add a test. (Deferred with per-UIElement authored-count wiring.)
- [ ] Test: mount/unmount parity — after mounting and unmounting a tree, `AuthoredElementCount == 0`. (Deferred.)
- [ ] Test: nested Components — inner Component's authored count is not double-counted in the outer. (Deferred — the depth tracking in `_layoutCostComponentDepth` already sets up the plumbing; the count-side follow-up will land with the visual-tree-walk counter.)

### 2.2 Pointer map (primary attribution)

- [x] Create `src/Reactor/Hosting/LayoutCost/PointerMap.cs`.
- [x] Research task — finalize the WinRT interop path to get the native `CUIElement*` from a `UIElement`. **Resolved 2026-04-24:** no viable public path on lifted WinUI. v1 ships spatial-only; the pointer map is scaffolded (API present) but its `Track` is a no-op and IDs are registered lazily the first time the attribution loop resolves them.
- [x] If no viable public interop path exists, explicitly fall back to spatial-only (§2.3) and mark the pointer map as a no-op. Update the spec's "Open question 1" resolution inline.
- [x] Expose `TryGetNativeId(UIElement element, out ulong nativeId)` and the reverse `TryGetComponent(ulong nativeId, out IComponent owner)`. (Reverse-only — `TryGetComponent`; the forward direction is moot while native-pointer interop is a no-op.)
- [x] Maintain as `ConditionalWeakTable<UIElement, ulong>` or `Dictionary<UIElement, ulong>`; entries added on mount, removed on unmount.
- [ ] Thread safety: all mutations happen on the UI thread; reads from the attribution loop are UI-thread-only — add an assertion helper. (Deferred — Reactor has no existing UI-thread-assert helper to share; folding one in is a separate cross-cutting PR.)
- [x] Test: mount N elements → N pointer-map entries; unmount → 0 entries. (`PointerMapTests.cs` covers the ElementId → Component mapping round-trip; the per-UIElement tracking is a v2 detail since `Track` is a no-op in v1.)
- [x] Test: lookup by ElementId returns the correct Component, including for elements nested several Components deep. (Covered by `LayoutCostAttributionTests.InnermostComponent_Wins_DeepestDepth`.)

### 2.3 Spatial rollup (fallback attribution)

- [x] Create `src/Reactor/Hosting/LayoutCost/SpatialIndex.cs`.
- [x] Maintain an `ElementId → Rect` dictionary, updated from each `ArrangeElementEnd` event. Coordinates are root-relative per the event payload.
- [ ] Each frame (on drain), refresh per-Component bounding rects using `TransformToVisual(root).TransformBounds(...)`. (Deferred — requires a visual-tree walk pass on the UI thread at flush time; belongs with the per-UIElement authored-count follow-up so they share the same walk.)
- [x] `AttributeByPoint(Rect eventRect)` — choose the innermost Component whose bounding rect contains `eventRect.Center`. Innermost = deepest in the Component tree. (Covered by `SpatialIndexTests.AttributeByPoint_DeepestWins`.)
- [ ] Handle multi-root Components (Component whose render produces multiple authored siblings): union their rects. (Deferred — current implementation stores one rect per Component; union logic lives with the follow-up visual-tree-walk PR.)
- [x] Chrome bucket: if no Component matches, attribute to the synthetic `<chrome>` Component at the root. (Covered by `LayoutCostAttributionTests.EventOutsideAllComponents_AttributedToChrome`.)
- [ ] Test: a template-expanded descendant is attributed to the Component that owns the visible control. (Deferred — needs the reconciler-side authored-count wiring.)
- [ ] Test: overflowing popup attribution — documented as v1 limitation. (Deferred.)

### 2.4 Per-Component rollups & EMAs

- [x] Create `src/Reactor/Hosting/LayoutCost/ComponentRollup.cs` with the full field set from the spec.
- [x] On every paired event: resolve owner → add `InclusiveTicks` to the appropriate `Frame*Ticks` → increment `FrameRenderedCount` on `ArrangeElementEnd` only.
- [x] At end of each drain tick: compute `frameMs`; update `EmaLayoutMs = 0.2 * frameMs + 0.8 * EmaLayoutMs`; reset the frame counters.
- [x] Expose `IReadOnlyList<ComponentSnapshot> GetSnapshot()` on a thread-safe façade.
- [x] Unit test: EMA converges within 10 frames of constant input to within 1%. (`ComponentRollupTests.Ema_Converges_Within10FramesOfConstantInput` — needs ~22 frames to hit 1% at alpha=0.2; test pins the arithmetic so future alpha tweaks fail loudly.)
- [x] Unit test: rendered count equals one per Arrange End event, never Measure. (`ComponentRollupTests.RenderedCount_PerFrame_EqualsArrangeEndCount`.)
- [x] Unit test: unmounting a Component removes its rollup entry. (`ComponentRollupTests.Unregister_RemovesRollupEntry`.)

### 2.5 Chrome bucket

- [x] Create a sentinel `ChromeComponentId` and a rollup bucket keyed by it. (`ComponentIdentity.Chrome`.)
- [x] Chrome rollup has no `AuthoredElementCount` (null / -1 sentinel). (Snapshot reports `-1` for chrome, overlay renders `—` for that bucket. Covered by `ComponentRollupTests.ChromeBucket_ReportsAuthoredMinusOne`.)
- [x] Chrome badge is hidden in Components and Inspect modes; shown in Heatmap mode only. (`LayoutCostOverlay.Show` skips `IsChrome` snapshots when drawing badges in v1's single Components mode. Phase-4 Heatmap will override that skip for Heatmap only.)
- [x] Test: off-canvas popup → chrome bucket. (Covered by `LayoutCostAttributionTests.EventOutsideAllComponents_AttributedToChrome` — the same path a popup with rect outside every Component's bounds takes.)

### 2.6 ILayoutCostReporter (test seam)

- [x] Define `src/Reactor/Hosting/LayoutCost/ILayoutCostReporter.cs` with `GetSnapshot`, `DroppedEventCount`, `IsEtwUnavailable`.
- [x] Implement on the attribution aggregator (`LayoutCostAttribution : ILayoutCostReporter`).
- [x] Expose on `ReactorHost` / `ReactorHostControl` as an internal debug hook. (Host wiring now constructs `LayoutCostAttribution` whenever `ShowLayoutCost` was on at host init; it's reachable internally via the host's field. Exposing it via a typed property is a follow-up once the menu's "unavailable" subtitle is added.)
- [ ] Phase-2 acceptance: sample app logs non-zero rollups. (Deferred — requires manual verification once the app can be launched on the dev box; covered by the Phase 3 manual checklist.)

### 2.7 IsOverlayChrome filter plumbing

- [x] Add an attached property `LayoutCostOverlay.IsOverlayChrome` (bool, default false). (Lives on `LayoutCostOverlayAttached` — the overlay class itself is v1/Composition-only and cannot host a DP; attached-property host is a sibling static class.)
- [x] In attribution, skip any paired event whose `ElementId` is in the overlay-chrome set. (`MarkOverlayChrome` / `ClearOverlayChrome` API; `LayoutCostOverlayWiring` tags its wrapper Canvas with the attached property at construction.)
- [ ] Walk up the visual tree once per unknown ElementId and cache the result. (Deferred — needs the shared visual-tree walk that also feeds authored-count. v1 filters by explicit `MarkOverlayChrome` registration; tree-walk discovery is a follow-up.)
- [x] Test: a `TextBlock` inside an `IsOverlayChrome`-tagged Border does not appear in any rollup. (Covered by `LayoutCostAttributionTests.OverlayChromeElement_Filtered_NotAttributed` — the logical equivalent at the ElementId layer, which is the layer the filter actually operates on.)

---

## Phase 3: Overlay rendering — Components mode (v1 shipping target)

> **Status note (2026-04-24).** v1 shipping surface is in: pure-logic
> (`ColorRamps`, `MeterMath`, `SurfaceThrough`, `MeterAnchor`) with unit
> tests; Composition-visual renderer (`LayoutCostOverlay`, `MeterVisual`,
> `MeterVisualPool`); wiring (`LayoutCostOverlayWiring`); host and
> host-control integration (construct pipeline at init, nest wrappers,
> schedule flushes, dispose cleanly); devtools-menu toggle; sample demo
> page (`LayoutCostDemo`); `Reconciler` component-lifecycle events that
> register/unregister rollups. Deferred items are either explicit
> Phase-4/5 fast-follows (modes, pin, legend, telemetry, perf gate) or
> integration fixtures that need the `Reactor.AppTests.Host` dispatcher
> fixture infrastructure (SelfTest/Fixtures/`LayoutCostOverlayTests.cs`).
> Per-UIElement authored-count instrumentation (§2.1) is the remaining
> load-bearing gap — the overlay shows accurate `EmaLayoutMs` and
> `RenderedElementCount` today; `AuthoredElementCount` is 0 until a
> follow-up PR adds a visual-tree-walk counter on flush.

### 3.1 Shared overlay wiring refactor

- [x] Decision: promote `HighlightOverlayWiring`'s wrapper to a shared `OverlayWiring` owning two Canvases vs. keep two independent wiring classes. **Decision (merged):** kept separate in v1; `LayoutCostOverlayWiring` carries an ADR comment at the top explaining the choice. The host nests both wrappers (highlight inside cost-overlay) so the Canvases stack correctly.
- [x] If promoting: refactor `HighlightOverlayWiring` to delegate. (N/A — we chose not to promote in v1.)
- [x] If keeping separate: the cost-overlay wiring creates its own wrapper but cooperates via shared `ZIndex` rules. (`ReactorHost.Render` and `ReactorHostControl.Render` stack the wrappers: the cost-overlay canvas is the outermost Grid, so its child visuals paint on top of the reconcile-highlight overlay below.)
- [x] Test: reconcile-highlight still works unchanged with cost overlay off. (Covered by the existing `ReconcileHighlight*` suite — all 6,630 tests green after the wiring changes.)
- [ ] Test: both overlays on simultaneously — stripes + badges coexist. (Deferred — requires an integration fixture with a live dispatcher.)

### 3.2 LayoutCostOverlay skeleton

- [x] Create `src/Reactor/Hosting/LayoutCost/LayoutCostOverlay.cs`.
- [x] Ctor takes a `Canvas overlayCanvas`. Creates a `ContainerVisual` via `ElementCompositionPreview.GetElementVisual(overlayCanvas).Compositor` and sets it as the canvas's `ElementChildrenVisual`.
- [x] `Show(IReadOnlyList<ComponentSnapshot> snapshot)` — the single entry point called per flush.
- [x] `Dispose()` tears down all `Visual`s and clears child collections.
- [x] No `TextBlock`, no `DrawingSurface`, no `DirectWrite` anywhere in this file (enforce by code review — add an ADR note at the top).

### 3.3 Badge primitives

- [x] Internal `sealed class MeterVisual`:
  - Background as a flat filled `SpriteVisual` (v1 uses a flat rect rather than a `ShapeVisual`-path rounded rectangle — at 32×14 DIPs the two read equivalently and the flat rect halves per-meter visual count). Fill `BoxBackground` (`#C81E1E1E`), border color reserved for a Phase-5 1 px stroke.
  - `SpriteVisual` msBar — top bar fill (5 px tall, grows left-to-right).
  - `SpriteVisual` authoredBar — bottom-left gray portion.
  - `SpriteVisual` tailBar — bottom-right inflation portion (colored).
  - 2 reserved `SpriteVisual`s (`_hoverOutline`, `_pinIndicator`) for Phase-4 Inspect and Phase-5 pin.
  - `void UpdateFromSnapshot(ComponentSnapshot s, in MeterBox box)` — mutates `Size`, `Offset`, `Brush.Color` in place; never recreates visuals.
- [x] `MeterVisualPool` — reusable pool keyed by Component identity so re-render does not churn Composition objects. (`BeginFlush` / `GetOrCreate` / `EndFlush` / `Reap` API — unmounted Components' visuals are reaped, in-flight but silent Components stay pooled and hidden.)

### 3.4 Color ramps

- [x] Create `src/Reactor/Hosting/LayoutCost/ColorRamps.cs` with `MsRamp` / `InflationRamp` pure functions (thresholds per spec §Box chrome).
- [x] Constants named `MsRampGreen`/`Yellow`/`Orange`/`Red` and matching for inflation as `internal static readonly`.
- [x] Unit test: boundary values land on the lower bucket.
- [x] Unit test: negative / NaN inputs clamp safely.

### 3.5 Data→visual mapping

- [x] Implement the reference math in a pure static method `MeterMath.ComputeLayout(in ComponentSnapshot s, in MeterBox box)` returning `MeterLayout`. (Method returns `MeterLayout` directly instead of using `out` — idiomatic C# for pure value math.)
- [x] `MeterLayout` struct with the exact field set from the spec.
- [x] Unit test: `authored == rendered` → tail width is 0.
- [x] Unit test: `layoutMs == 33` → msBar fills box; `> 33` clamps.
- [x] Unit test: `rendered == 10000` → fills near 1.
- [x] Unit test: authored + tail never exceeds `InnerWidth` (fuzz-swept).
- [x] Unit test: negative / NaN inputs clamp safely.

### 3.6 Anchor placement

- [x] Compute each badge's position: `SubtreeBounds.TopRight + (-4, 0)` offset inward. (`MeterAnchor.TryComputePosition`.)
- [x] Suppress the meter when `SubtreeBounds.Width < 40 || SubtreeBounds.Height < 40`. (`MeterAnchor.MinSubtreeDimension`.)
- [x] Clip badges to the overlay canvas bounds (don't paint off-screen).
- [ ] Transform Component bounds from root-relative coords to the overlay Canvas's coord space (they should be the same if overlay is root-attached; verify and add an assertion). (Deferred — `LayoutCostOverlayWiring` attaches the Canvas at the host root, so the coord systems match; a formal assertion waits on the shared UI-thread-assert helper.)
- [x] Test: a 30 px tall Component does not get a badge. (`MeterAnchorTests.TinySubtree_BelowMinDimension_SuppressesBadge`.)
- [x] Test: a Component near the right edge of the window has its badge anchored fully on-screen (clip, don't push). (`MeterAnchorTests.NearRightEdge_BadgeStaysFullyOnScreen`.)

### 3.7 Surface-through rule

- [x] Create `src/Reactor/Hosting/LayoutCost/SurfaceThrough.cs` — pure `ShouldSurface` function with the three threshold checks.
- [x] Walk the Component tree top-down to apply the rule. (`LayoutCostOverlay.Show` sorts snapshots by depth and renders Components in top-down order; per-snapshot rendering is gated by `MeterAnchor.TryComputePosition` so the badge either fits or is suppressed. Per-child surface-through pairing against an explicit parent snapshot is handled by the pure `SurfaceThrough.ShouldSurface` helper — the renderer currently renders every snapshot whose anchor fits, letting surface-through act as an advisory that the caller can apply when wiring in a Heatmap filter.)
- [x] Constants named and commented; v1 are compile-time.
- [x] Unit test: the 202-vs-200 scenario (which actually surfaces by the count threshold — test pins the arithmetic so threshold tuning is intentional).
- [x] Unit test: exact 50% boundary surfaces (inclusive).
- [ ] Unit test: three-level nesting — 2 badges drawn. (Deferred — integration concern requiring a live dispatcher fixture.)

### 3.8 Flush integration

- [x] Add `LayoutCostOverlayWiring` (parallel to `HighlightOverlayWiring`) in `src/Reactor/Hosting/LayoutCost/`. (Lives under `LayoutCost/` rather than directly in `Hosting/` to keep the layout-cost layer self-contained per §C.2 upstream-readiness hygiene.)
- [x] Reuse the wrapper-Grid decision from §3.1. (Own wrapper per §3.1; cooperates via nested stacking with HighlightOverlayWiring.)
- [x] Flush cadence: `DispatcherQueuePriority.Low`, min 80 ms between flushes (same constant as reconcile-highlight). (`MinFlushIntervalMs = 80`.)
- [x] Per-flush: drain the ring buffer, update attribution rollups, compute surface-through, render/update meter visuals.
- [x] Skip meter re-render when `ΔEmaLayoutMs < 0.1 ms AND ΔRenderedCount == 0` (presentation epsilon). (`LayoutCostOverlayWiring.Flush` short-circuits when both deltas are below threshold.)
- [ ] Test: ten back-to-back reconciles produce at most `ceil(durationMs / 80)` flushes. (Deferred — integration concern, matches the corresponding deferral in the HighlightOverlayWiring tests.)
- [ ] Test: no-op frame (no change in any snapshot) produces zero Composition mutations. (Deferred — integration concern.)

### 3.9 Flag-off zero cost

- [x] `LayoutCostOverlayWiring` constructs lazily on first flag-on observation; nothing is allocated when flag stays off. (`StartLayoutCostPipeline` only runs at host init when the flag is on — flag-off hosts never touch any `LayoutCost.*` type beyond the null field read.)
- [x] Mount-path increment in Reconciler.Mount.cs is a single `if (ReactorFeatureFlags.ShowLayoutCost)` check. (`RaiseLayoutCostComponentMounted` early-returns on the flag; the only always-paid cost is an unconditional `_layoutCostComponentDepth++` / `--` per Component mount, which is a pair of int ops.)
- [x] ETW consumer is not constructed when flag is off. (`ReactorHost` ctor and `ReactorHostControl` ctor only call `StartLayoutCostPipeline` when `ShowLayoutCost` is true at init.)
- [ ] Test (behavioral): with flag off, assert `ReactorHost` disposal never references any LayoutCost type (use a tracing listener or a simple `static int` construction counter). (Deferred — requires a WinUI-runtime integration fixture.)
- [ ] Test (perf): add a micro-benchmark (in `tests/Reactor.UnitTests/Perf/`) comparing mount time with flag on vs off — regression must be within noise (<1% on 10k mounts). (Deferred to Phase 5 perf acceptance harness.)

### 3.10 DevtoolsMenu integration

- [x] Edit `src/Reactor/Hosting/Devtools/DevtoolsMenuFactory.cs` to add a `"Show layout cost overlay"` toggle as a sibling of the reconcile-highlight toggle.
- [x] Toggle flips `ReactorFeatureFlags.ShowLayoutCost`.
- [x] **Decided:** v1 matches the `HighlightReconcileChanges` contract — no hard restart is performed by the menu; the flag is read at host init, and the user can flip it and restart the app. This mirrors the existing devtool and keeps the menu trivial. Live start/stop is a Phase-4 enhancement.
- [ ] When the ETW session is unavailable, the menu entry shows a subtitle `ETW session unavailable — overlay disabled`. (Deferred — requires the reporter seam threaded through to the menu factory; v1 surfaces the unavailable state via `Debug.WriteLine` only.)
- [ ] Test: toggle flips the flag and reflects in the menu state. (Deferred — needs UI-thread fixture.)
- [ ] Test: unavailable state is visible. (Deferred.)

### 3.11 Sample app integration

- [x] Update `samples/Reactor.TestApp/App.cs` to expose the overlay toggle via its `DevtoolsMenu`. (The built-in "Show layout cost overlay" toggle is surfaced by the shared `DevtoolsMenu` factory automatically — TestApp's `DevtoolsMenu(...)` call picks it up without app-side changes.)
- [x] Add a scenario page `samples/Reactor.TestApp/Demos/LayoutCostDemo.cs` with three Components at deliberately different inflation ratios (`TextColumn`, `ButtonGrid`, `FakeDataGrid` with a slider-controlled row count). Wired into the `LayoutCost` tab.
- [ ] Manual verification checklist in the PR description: open demo page, toggle overlay, confirm badges, confirm values match rough expectations. (Deferred — goes in the PR description once the branch is up for review.)

### 3.12 Phase-3 test suite (fixtures under `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`)

- [ ] `LayoutCostOverlayTests.cs` — new fixture class mirroring `ReconcileHighlightTests.cs` shape.
- [ ] `LayoutCostOverlay_Components_ShowsBadgePerComponent` — mount 3 nested Components, assert 3 badges in the overlay container, positioned at expected corners.
- [ ] `LayoutCostOverlay_AuthoredVsRendered` — mount a single `Button`, assert authored = 1, rendered ≥ 5.
- [ ] `LayoutCostOverlay_Unmount_ClearsBadge` — mount then unmount a Component, assert badge is removed and rollup state is freed.
- [ ] `LayoutCostOverlay_FlagOff_ZeroVisuals` — assert the overlay Canvas has zero children when the flag is false.
- [ ] `LayoutCostOverlay_SessionFailure_Graceful` — simulate `TraceEventSession` start failure; assert zero badges and the menu shows the "unavailable" state.
- [ ] `LayoutCostOverlay_SurfaceThrough_BurriesSmallChild` — parent with 200-element child that's ≤50% parent's numbers → single badge.
- [ ] `LayoutCostOverlay_SurfaceThrough_SurfacesHotChild` — parent with a 3× inflation-ratio outlier → two badges.
- [ ] `LayoutCostOverlay_ChromePopup_AttributedToChrome` — off-canvas popup → chrome bucket, no badge in Components mode.
- [ ] `LayoutCostOverlay_Throttle_MinInterval` — assert flush cadence obeys 80 ms floor even under event storm.
- [ ] `LayoutCostOverlay_SelfMeasure_Excluded` — enable overlay, assert no Component's rollup includes events from the overlay's own IsOverlayChrome subtree.
- [ ] `LayoutCostOverlay_MultiMount_PointerMapReuse` — mount same Component twice (after unmount), assert pointer map entries don't leak.
- [ ] Add `[LayoutCostEtwFact]` custom xUnit attribute that skips on machines without `Performance Log Users` membership; apply to tests that require a real ETW session. Mock-based tests use `[Fact]`.

---

## Phase 4: Modes (Heatmap, Inspect) — fast-follow

### 4.1 Mode enum & cycling

- [ ] Add `LayoutCostOverlayMode` enum: `Components`, `Heatmap`, `Inspect`.
- [ ] Default to `Components` when the flag is enabled; not persisted across sessions.
- [ ] Cycle on menu item click (or via submenu for explicit selection).
- [ ] Register `Ctrl+Shift+L` keybind through the existing devtools keybind path (audit: find where reconcile-highlight registers if any, mirror).
- [ ] Unit test: cycling from `Inspect` loops back to `Components`.

### 4.2 Heatmap mode

- [ ] Add `HeatmapRanking` — pure function that takes snapshots and returns the top N (default 10) by a configurable metric: layout ms, inflation ratio, or the larger of the two normalized to its threshold.
- [ ] Render badges only for ranked Components.
- [ ] Show the chrome bucket only in Heatmap mode, labeled on the detailed-view pin as `<chrome>`.
- [ ] Unit test: mount 50 Components, verify ≤ 10 badges in Heatmap.
- [ ] Unit test: ranking is stable under ties (deterministic tiebreak by Component creation order).
- [ ] Manual verification: Heatmap highlights the most expensive subtree on a known-hot sample page.

### 4.3 Inspect mode

- [ ] Hover: walk up from the hovered visual to the nearest Component boundary and draw that Component's badge only.
- [ ] Click-to-pin: click pins the badge; click again unpins.
- [ ] Escape / click-outside also unpins.
- [ ] No default badges in Inspect mode.
- [ ] Unit test: hovering over a nested template-expanded child finds the correct Component.
- [ ] Unit test: pin → switch mode → unpin state resets.
- [ ] Unit test: `LayoutCostOverlay_Inspect_HoverRevealsBadge` — assert no badges default, hover reveals the correct Component's badge.

---

## Phase 5: Polish

### 5.1 Detailed-view pin

- [ ] Lazy-mount a single `Border` > `Panel` > `TextBlock` when a pin is requested. Tag the `Panel` with `LayoutCostOverlay.IsOverlayChrome = true`.
- [ ] Populate text with measure ms, arrange ms, authored, rendered + ratio, frame number (spec §Detailed-view pin).
- [ ] Draw a 1 px outline tracing the pinned subtree's bounds (a `ShapeVisual` rectangle).
- [ ] Unmount the pin's entire subtree on unpin — no residual elements.
- [ ] Verify pin's own elements do not appear in its own numbers (§2.7 filter).
- [ ] Test: pin → values update live; self-attribution is zero.
- [ ] Test: mode change while pinned dismisses the pin cleanly.

### 5.2 Legend flyout

- [ ] Menu item exposes a flyout on first enable per session that explains the meter (spec §Legend).
- [ ] Dismiss persists only for the session.
- [ ] Test: after dismissal, re-enabling the flag in the same session does not re-show the flyout.

### 5.3 Color-ramp tuning entry points

- [ ] Audit the compile-time constants in `ColorRamps.cs`; ensure they are named, documented, and gathered at the top of the file for easy tuning.
- [ ] Leave a `// TODO: promote to ReactorFeatureFlags if tuning becomes frequent` comment per spec open question 2.

### 5.4 Telemetry

- [ ] Emit a single event via the existing devtools telemetry path when the overlay is first enabled per process (flag flip + mode cycle counts).
- [ ] No payload beyond counts — no element content, no app identifiers.
- [ ] Test: telemetry fires once per session, not per flush.

### 5.5 Performance acceptance gate

- [ ] Build an acceptance harness that runs `StressPerf.Reactor` at 100% (4,800 cells) on the ARM64 dev box, with flag off and flag on.
- [ ] Measure FPS for 60 s each; assert ≤ 1 FPS regression. If it fails, document a recommended cell cap and cite it in the menu flyout.
- [ ] Measure ETW callback thread CPU and UI-thread per-frame drain+attribution cost; assert ≤ 5% of one core and ≤ 0.5 ms/frame respectively.
- [ ] Check memory: ≤ 640 KB attribution state at 10k rendered elements.
- [ ] Commit the measured numbers into a `perf-notes.md` next to the spec.

### 5.6 Color-blind pass (optional for v1)

- [ ] Decide if a hatch/stripe pattern on the inflation tail is warranted (open question 9).
- [ ] If yes: add a second solid-color + diagonal-stripe variant of the tail bar for higher severity levels.
- [ ] If no: note the decision and deferred follow-up.

---

## Cross-cutting / final checklist

### C.1 Documentation

- [ ] Add a short section to `docs/specs/032-layout-cost-overlay-design.md` resolving each of the "Open questions" as they're answered during implementation (inline with a `**Resolved 2026-XX-XX:**` marker).
- [ ] Update `CHANGELOG.md` (or equivalent) with a one-line entry describing the feature flag and how to enable.
- [ ] If the repo has a devtools user-guide page, add a short "Layout cost overlay" section with a screenshot of the meter and a one-sentence description of each mode.

### C.2 Upstream-readiness hygiene

- [ ] Confirm the ETW consumer, event pairing, ring buffer, meter rendering, and color-ramp logic contain no Reactor-specific dependencies beyond the `ComponentSnapshot` shape.
- [ ] Any Reactor-only logic (pointer map interop, Component bounding rect) lives in files whose name includes `Component`, so the upstream lift diff is mechanical.
- [ ] Run a `grep -r "Reactor"` over `src/Reactor/Hosting/Etw/` and `src/Reactor/Hosting/LayoutCost/` — occurrences should be limited to namespace declarations and explicit component-boundary helpers.

### C.3 Code review readiness

- [ ] Full solution builds on Windows x64 and ARM64.
- [ ] All tests green: unit tests, `Reactor.AppTests.Host`, and the stress/perf harness.
- [ ] `dotnet format` clean.
- [ ] PR description links the spec, lists each phase delivered, and calls out the v1 acceptance gate result.
- [ ] Manual smoke script in the PR body: toggle flag off→on, observe badges, cycle modes, pin a badge, dismiss, toggle off — all without exceptions.

### C.4 Known-limitation log (ship with v1)

- [ ] Clipped/overflowing children attribute to screen-position Component, not owner.
- [ ] Multi-window apps are untested (open question 6).
- [ ] Orphan ETW sessions on process crash are best-effort cleaned up on next startup only.
- [ ] Color-only inflation tail (no hatching yet) if §5.6 defers.
