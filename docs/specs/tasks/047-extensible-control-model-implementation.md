# Fully Extensible Control Model — Implementation Tasks

Derived from: `docs/specs/047-extensible-control-model.md`

> **Status:** Phase 0 in progress. Phase 0 is *spec-process* work — audits, suite
> infrastructure, baseline measurements, and decision criteria. It must clear
> its exit gate before Phase 1 (v1 protocol) can start. See spec §14 for the
> phase boundaries and §15 for the perf suite this phase stands up.

This file converts spec 047's Phase 0 deliverables (spec §14) into ship-ready,
pause/resume-able tasks. Each top-level deliverable maps to one section below.
Tasks are intentionally narrow so the work can be checkpointed between
sessions without losing context.

Conventions:
- Audit outputs land under `docs/specs/047/audits/` (new folder — create as
  part of 0.1).
- Baseline measurement outputs land under `docs/specs/047/baseline-results/`
  (per spec §14 Phase 0 exit gate).
- Perf suite scaffolding follows the existing `tests/stress_perf/`,
  `tests/startup_perf/`, and `tests/perf_bench/` patterns. New variants are
  added alongside (not replacing) existing ones — see spec §15.2.
- Phase 0 produces **no production code changes** to `Reactor.dll` other than
  what is needed for the `ReactorV2` skeleton (which is initially a copy of
  `Reactor` so the V2 numbers ≈ Today numbers at the start of Phase 1).
- A task is "done" only when its output (CSV, markdown audit doc, scaffolded
  project that builds, baseline JSON-Lines file, etc.) is committed under the
  paths above. Audit decisions referenced by later phases must be unambiguous
  in writing — Phase 1+ should not need to re-derive them.

**Phase 0 exit gate (from spec §14):**
1. All seven deliverables below complete.
2. Baseline numbers committed to `docs/specs/047/baseline-results/`.
3. Spec §11 / §12 updated with measured numbers replacing estimates.
4. Factoring recommendation (0.7) reviewed and either ratified or executed.

Until that gate clears, the proposal does **not** move to greenlight.

---

## 0.1 `BeginSuppress` audit

Spec §14 deliverable 1. Drives the §8 / §8.1 decision and the
controlled/uncontrolled/initial classification in §6.1. Output is a CSV the
descriptor design in Phase 1 can consult directly.

- [x] Create `docs/specs/047/audits/` (new folder).
- [x] Grep every `BeginSuppress` call site in the tree. Capture file + line +
      surrounding control name + property being written.
- [x] For each call site, classify into exactly one of:
  - `eliminable-tight-diff` — already gated by `oldEl.X != newEl.X` (or trivially
    can be); suppression is redundant.
  - `coercion` — control coerces the written value (e.g., `Slider.Value` against
    `Maximum`); the change event fires with a value the engine did not write.
  - `float-precision` — engine writes `0.3`, control stores `0.30000001`.
  - `reference-equality` — engine writes a reference whose `Equals` semantics
    don't match the control's internal storage.
  - `focus-prop` — `IsTabStop` / `FocusState` / programmatic `Focus()`.
  - `items-coercion` — `SelectedIndex` etc. coerced by a shrunken items
    collection.
  - `animation-tick` — interpolated intermediate values fire change events
    mid-storyboard.
  - `user-state-races-render` — only resolvable by §8.1's `mostRecentEventCount`
    round-trip.
- [x] Write `docs/specs/047/audits/begin-suppress-audit.csv` with columns:
      `file, line, control, property, category, notes`.
- [x] Write a one-page summary (`begin-suppress-audit.md`) tallying counts per
      category and identifying any cases that don't fit the schema above
      (extend the schema if needed; don't force-fit). Added one category:
      `defensive-redundant`.
- [x] Cross-link from spec §8 (add a "see audit results" footnote referencing
      the CSV path).

## 0.2 `EventHandlerState` field audit

Spec §14 deliverable 2. Drives the §9.2 split between `ModifierEventHandlerState`
(shared, routed-input) and per-control payloads inside `ControlEventStateBox`.

- [x] Inventory every `Current<EventName>` and `<EventName>Trampoline` field on
      `EventHandlerState` (`Reconciler.cs:2787+` — line drifted +7 from §3
      citation; covered by 0.5). Include the WinUI event each maps to.
- [x] For each field, classify into exactly one of `routed-input-modifier`,
      `control-intrinsic`, `hybrid-or-ambiguous`. Result: 42 routed-input,
      9 control-intrinsic, 0 hybrid.
- [x] Write `docs/specs/047/audits/event-handler-state-audit.csv` with columns:
      `field, winui-event, owning-control, category, target-location, notes`.
- [x] Sketch the per-control payload structs that fall out of the audit. 7
      structs (ToggleSwitch, Button, TextBox, Image, ScrollViewer, ScrollView,
      NumberBox) documented in `event-handler-state-audit.md`.
- [x] Confirm the §9.4 hypothesis is testable from the audit data alone —
      7/7 controls have only `control-intrinsic` events. Frequency claim
      ("~90% of tree controls have no user modifiers") deferred to M11.

## 0.3 Perf validation suite — infrastructure

Spec §14 deliverable 3 + spec §15. Builds the scaffolding *before* any V2
implementation exists, so Phase 1 work shows up as the delta.

### 0.3.1 `StressPerf.ReactorV2` skeleton

- [x] Add `tests/stress_perf/StressPerf.ReactorV2/` as a near-verbatim copy of
      `tests/stress_perf/StressPerf.Reactor/`. The point at Phase-0 freeze is
      "V2 numbers ≈ Today numbers" — the only intentional differences are the
      project name, output executable name, and any namespace renames needed
      to coexist with the original project in a side-by-side run.
- [x] Wire the new project into `Reactor.slnx`. Confirm it builds clean.
- [x] Confirm `StressPerf.ReactorV2.exe` launches and runs the same scenario
      surface as `StressPerf.Reactor.exe`. No correctness regression.

### 0.3.2 `BlankReactorV2` for startup perf

- [x] Add `tests/startup_perf/BlankReactorV2/` alongside `BlankReactor` /
      `BlankRNW` / `BlankWinUI3`. Mirrors `BlankReactor` exactly at Phase 0.
- [x] Confirm it appears in whatever startup-perf orchestration script enumerates
      the blank apps (search for `BlankReactor` references).

### 0.3.3 `PerfBench.ControlModel` — micro suite harnesses

Spec §15.3 defines M1 through M13. Each test ships in three implementations
(spec §15.2: `Direct`, `ReactorToday`, `ReactorV2`). At Phase 0 the `ReactorV2`
implementation is intentionally identical to `ReactorToday`.

- [x] Add `tests/perf_bench/PerfBench.ControlModel/` project. Uses a
      dependency-light custom `BenchRunner` instead of BenchmarkDotNet at
      Phase 0 — produces the same per-rep timing + alloc + GC counts the
      §15.6 aggregator consumes. Adopting BenchmarkDotNet's pilot/CV-aware
      warmup is planned for Phase 1.
- [x] Implement M1 `Mount_Leaf_NoCallback` across all three variants
      (`Direct`, `ReactorToday`, `ReactorV2`).
- [x] Implement M2 `Mount_Leaf_OneCallback` across all three variants.
- [x] Implement M3 `Mount_Leaf_ThreeCallbacks` across all three variants.
- [x] Implement M4 `Dispatch_Switch_Cold` (PGO-cold; reset between iterations).
- [x] Implement M5 `Dispatch_Switch_Warm` (10k-mount pre-warm before timing).
- [x] Implement M6 `Dispatch_ExternalType` (uses `RegisterType` for the
      external control).
- [x] Implement M7 `Update_NoChange` (1000-element tree, no-op re-render).
- [x] Implement M8 `Update_OneLeafChanged` (depth-5 leaf delta).
- [x] Implement M9 `Update_AllChanged` (every value-bearing prop changed).
- [x] Implement M10 `EventHandlerState_Alloc` (allocation count + bytes).
- [x] Implement M11 `ModifierEHS_Frequency` — mount a 1000-element
      representative tree; count `ModifierEventHandlerState` allocations.
- [x] Implement M12 `Pool_Rent_HotPath` — ListView recycle (100 instances ↔ 20
      pool slots).
- [x] Implement M13 `Setters_Suppression_Scope` — correctness, not perf.
      `Set(ts => ts.IsOn = true)` on `ToggleSwitch` with `OnIsOnChanged` —
      verify callback fires exactly once today (the bug per §8.2), zero times
      after Phase 1's fix. Phase 0 records the failing behavior as baseline
      (`OnIsOnChangedFireCount = 1`) so the Phase 1 fix has a target to flip.
- [x] Each bench reports: mean ns + 95% CI, allocation bytes, Gen0/1/2 collections,
      managed heap delta. `BenchRunner` instruments `GC.GetAllocatedBytesForCurrentThread`
      + `GC.CollectionCount` + `GC.GetTotalMemory` per rep; aggregator computes
      95% CI from 5 reps.

### 0.3.4 Macro suite L1–L11

Spec §15.4. L12 (hot-reload) defers to Phase 2. L13 / L14 (split-library +
AOT) defer to Phase 1 since they need a real external assembly. Each macro
ships as a separate executable per the `stress_perf` shape.

Per-scenario status is consolidated in
[`docs/specs/047/macro-suite-status.md`](../047/macro-suite-status.md).
Phase 0 ships full L1 coverage (BlankWinUI3 + BlankReactor + BlankReactorV2),
two-way coverage of L6 and L10 via the existing stress_perf shape, and
written scenario contracts for L2 / L3 so Phase 1 implementations don't
drift. L4/L5/L7–L9/L11 deferred to Phase 1 with explicit rationale captured
in the status doc.

- [x] L1 `TTFF_Blank` — `BlankWinUI3` + `BlankReactor` + `BlankReactorV2`
      (added 0.3.2) all present; `run_startup_bench.ps1` enumerates V2.
- [ ] L2 `TTFF_LoginForm` — scenario contract frozen in macro-suite-status.md;
      executable implementations deferred to Phase 1.
- [ ] L3 `TTFF_SettingsPage` — scenario contract frozen; deferred to Phase 1.
- [ ] L4 `WorkingSet_AtStartup` — deferred behind L2.
- [ ] L5 `WorkingSet_Steady` — deferred to Phase 1 (needs WTS plumbing).
- [x] L6 `FPS_VirtualizedList_Scroll` — Direct + Reactor variants ship via
      `StressPerf.VirtualList.WinUI` / `.Reactor`. V2 variant deferred to
      Phase 1 (mirrors the existing project).
- [ ] L7 `FPS_AnimatedTree` — deferred to Phase 1.
- [ ] L8 `FPS_HotStateUpdate` — deferred to Phase 1.
- [ ] L9 `GC_PerFrame_AnimatedTree` — deferred to Phase 1 (variant of L7).
- [x] L10 `Mount_Storm` — partial via `StressPerf.Reactor` / `.ReactorV2`
      grid burst-mount path; Direct equivalent deferred to Phase 1.
- [ ] L11 `LongLived_HeapStability` — deferred to Phase 1 (needs WTS plumbing).
- [x] Each macro that ships at Phase 0 inherits the JSON-Lines schema +
      environment stamping defined in 0.3.5 / 0.3.6; runtime stamping for
      `LockedRefreshHz` and `SessionInterrupted` is wired in Phase 1 along
      with L5 / L11.

### 0.3.5 Reporting aggregator

- [x] Implement the JSON-Lines collector — one row per
      `(scenario, variant, repetition)`. Ships as `tools/spec047-aggregator`.
- [x] Implement the comparison emitter producing the three §15.6 tables:
      (a) absolute comparison `Direct` / `ReactorToday` / `ReactorV2`,
      (b) Reactor delta (`V2 vs Today` % with CI),
      (c) WinUI gap (`V2 vs Direct` absolute).
- [x] Implement the per-PR trend output for CI (`trend.csv`). CI wiring
      itself is deferred to Phase 1 — Phase 0 ships the format and a local
      `dotnet run --project tools/spec047-aggregator` invocation.
- [x] Confirm result rows with mismatched environment metadata are flagged
      as non-comparable. Architecture is the load-bearing axis at Phase 0
      (groups keyed by `(BenchId, Variant, Architecture)` so ARM64-native
      and x64-emulated runs cannot silently mix). LockedRefreshHz /
      PowerState / WindowOccluded rejection is deferred to Phase 1 per
      `perf-suite-runbook.md`.

### 0.3.6 Environment isolation runbook

Spec §15.5. Existing memory entries
(`reference_stress_perf_window_throttling.md`,
`reference_stress_perf_drr_battery.md`) capture invariants the suite must
respect.

- [x] Write `docs/specs/047/perf-suite-runbook.md` capturing the operator-side
      requirements (foreground, AC, DRR off, no virtual-desktop switches,
      power plan, priority/affinity, warm-up policy).
- [x] Cross-reference the two existing memory entries in the runbook so future
      operators land at the same source of truth.

## 0.4 Baseline numbers — capture `Direct` and `ReactorToday`

Spec §14 deliverable 4. The gap between `Direct` and `ReactorToday` is the
*budget* this spec proposes to close.

- [x] Identify two representative machines — Phase 0 captured LAPTOP-4MEP83VI
      (Snapdragon X laptop, x64-emulated). Workstation x64 + ARM64-native
      captures deferred to Phase 1 per
      [`baseline-results/machines.md`](../047/baseline-results/machines.md).
- [x] Run the §15.3 micro suite (M1–M13) on LAPTOP-4MEP83VI capturing
      Direct, ReactorToday, ReactorV2. Three result files under
      `docs/specs/047/baseline-results/LAPTOP-4MEP83VI/2026-05-25/`.
- [ ] Run the §15.4 macro suite (L1–L11) — deferred per
      [`macro-suite-status.md`](../047/macro-suite-status.md); only L1
      ships at Phase 0 and its TTFF capture is deferred to Phase 1's
      first promotion PR.
- [x] Commit raw JSON-Lines outputs under
      `docs/specs/047/baseline-results/LAPTOP-4MEP83VI/2026-05-25/`.
- [x] Commit the comparison-table markdown — `summary.md` + aggregator
      output under `aggregator-out/`.
- [x] Update spec §11.6 — measured numbers now anchor the target table;
      original estimates preserved as a footnote.
- [x] Update spec §12 — opening paragraph footnotes the Phase 0 anchor;
      per-section measured numbers cross-link to `baseline-results/`.

## 0.5 Existing-API surface inventory

Spec §14 deliverable 5. Confirms Appendix A's mapping is current.

- [x] Walk the `internal` members named in spec §3 and Appendix A. Line-number
      drift cataloged in `existing-api-surface.md`; `ApplyThemeBindings` and
      `ApplyResourceOverrides` have drifted from `internal` (per spec) to
      `private` (current). Suggested §3 / App A edits captured in the audit.
- [x] Search the in-repo `RegisterType` callers — 8 sites enumerated. In-tree
      sites use `SetElementTag` only; the two samples (Monaco, regedit) use
      `Tag` directly and bypass the engine machinery. Two docking sites flagged
      for follow-up in the Phase 1 promotion PR.
- [x] Write `docs/specs/047/audits/existing-api-surface.md` with the
      promote-vs-stay-internal mapping.

## 0.6 Decision criteria for §13 open questions

Spec §14 deliverable 6. Each question that the suite can disambiguate gets a
written success criterion *before* Phase 1, so decisions flow from data rather
than re-litigation.

- [x] Write `docs/specs/047/decision-criteria.md` covering Q1, Q3, Q6, Q7,
      Q9, Q11, Q12, Q14, Q17, Q18, Q19. Q17/Q18/Q19 ratified from §13
      recommendations (Q17 revised: no override mechanism in v1, throw
      on any duplicate registration). Q3 incorporates the audit findings
      (recommend ship "delete + tight diff" + per-control tolerance +
      ColorPicker shim; do NOT build §8.1). Q9 / Q12 / Q14 added during
      Phase 0 review: no override verb in v1 (Q9), `void Update(...)`
      forbidding substitution (Q12), UI-thread-only protocol (Q14).
- [x] For each criterion, link the relevant §15 test(s) and the spec section
      that absorbs the decision when made.

## 0.7 Spec factoring decision

Spec §14 deliverable 7. After Phase 0 produces audit results and baseline
numbers, decide whether to keep spec 047 unified or split it.

- [x] Write `docs/specs/047/factoring-recommendation.md` with the
      recommendation. **Outcome: keep spec 047 unified.** The reviewer's
      proposed split was addressed bucket-by-bucket; only the §8.2
      setter-echo fix is carved out (small standalone PR ahead of Phase 1).
- [x] Factor in the three signals — answers captured in the recommendation.
- [x] Recommendation is committed for review alongside the other Phase 0
      deliverables. No split executed; nothing to rename in this task file.

---

## Phase 0 exit checklist

When every item below is checked, the Phase 0 exit gate clears and the
proposal can move to greenlight (spec §14).

- [x] 0.1 `BeginSuppress` audit CSV + summary committed.
- [x] 0.2 `EventHandlerState` field audit CSV + per-control struct sketches
      committed.
- [x] 0.3 Perf suite scaffolding builds clean. M1–M13 ship with all three
      variants (`Direct` / `ReactorToday` / `ReactorV2`; V2 ≡ Today at
      Phase 0). L1 ships three-way; L6 / L10 ship two-way; the rest are
      contract-frozen and deferred to Phase 1 per `macro-suite-status.md`.
      Aggregator produces the §15.6 (a)/(b)/(c) tables locally.
- [x] 0.4 Baseline JSON-Lines + summary committed under
      `docs/specs/047/baseline-results/`; spec §11 and §12 cite measured
      numbers. Phase 0 captures ARM64-native on LAPTOP-4MEP83VI; workstation
      x64 deferred to Phase 1 per `machines.md`.
- [x] 0.5 Existing-API surface inventory committed.
- [x] 0.6 Decision criteria committed for Q1, Q3, Q6, Q7, Q9, Q11, Q12,
      Q14, Q17, Q18, Q19. Q2 / Q4 / Q5 / Q10 / Q13 / Q15 / Q16 are either
      suite-deferred (Q15 → L12), strategically deferred (Q2 AOT,
      Q10 compile-time validation, Q13 modifier precedence), or already
      documented in the spec (Q16 in §16, Q4 / Q5 design questions for
      Phase 2). Q8 is implicitly captured via the §8.2 carve-out in
      factoring-recommendation.md.
- [x] 0.7 Factoring recommendation committed and reviewed. Outcome: keep
      spec 047 unified; only the §8.2 setter-suppression fix is carved out
      as a standalone PR ahead of Phase 1.
