# Fully Extensible Control Model — Implementation Tasks

Derived from: `docs/specs/047-extensible-control-model.md`

> **Status:** Phase 0 complete (PR #414 — greenlight). **Phase 1 code-complete**
> in PR #414 — see [`047-extensible-control-model-phase1-implementation.md`](047-extensible-control-model-phase1-implementation.md)
> for the per-section tracker and [`../047/phase1-results/`](../047/phase1-results/)
> for the Phase 1 deliverables, exit-gate deferrals (1.17 AOT publish, 1.18
> macro catch-up, 1.19 final perf validation — all gated on baseline-machine
> runs), and the Q14 dispatcher-affinity note.
>
> **Phase 2 complete (2026-05-26).** §13 Q1 resolved — descriptors are the
> primary first-party surface; hand-coded `IElementHandler<,>` is the escape
> hatch. Stable-AC capture landed the worst gating bench (M2) at +9.6%,
> inside the 5-15% judgment-call band; LOC (~24% saving at Phase 3 scope)
> and §6.1 readability resolved the call to descriptors. Three captures
> documented under [`../047/phase2-results/LAPTOP-4MEP83VI/`](../047/phase2-results/LAPTOP-4MEP83VI/);
> the `2026-05-26-q1-fastpath-3x5-stableac/` capture is authoritative. See
> "Phase 2 wrap-up" and "Phase 3 prerequisites" sections below.
>
> **Phase 3 complete (PR #440); Phase 4 code-complete — V1 is the unconditional
> production path; migration closed.** The full close-out is tracked in
> [`047-extensible-control-model-phase4-implementation.md`](047-extensible-control-model-phase4-implementation.md).
> Landed: 100% V1 registration + the production flip (§4.0/§4.1); legacy
> `MountXxx`/`UpdateXxx` switch + all A|B/`UseV1Protocol` dead code deleted
> (§4.5/§4.6); the public author surface graduated out of `[Experimental]` +
> locked, KD-4 closed (§4.7); the §8 echo path migrated to a value-diff/counter
> **hybrid** (`ChangeEchoSuppressor` intentionally retained, spec §8.3); the §9
> `EventHandlerState` split into `ModifierEventHandlerState` + per-control
> `ControlEventStateBox` (§4.3); the §11.7 bucketed `Element` base
> (`ElementExtras`) + the §11.6 byte-gate target constants (§4.4); and the final
> author docs (§4.8). **Outstanding (baseline-machine-only, handed off):** the
> ARM64 stable-AC perf ratification (§4.9) and the §11.6 hard byte-gate
> *measurement/enforcement* (§4.4) — both blocked on `LAPTOP-4MEP83VI`. Full
> x64 validation green throughout (build / xunit 9128 / selftests).
>
> Phase 0 below is *spec-process* work — audits, suite infrastructure, baseline
> measurements, and decision criteria. It cleared its exit gate (PR #414) and
> Phase 1 (v1 protocol behind a feature flag, 5 ported controls + external
> assembly proof, Roslyn analyzer, public author surface) shipped on top.
> See spec §14 for the phase boundaries and §15 for the perf suite this phase
> stood up.

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
      (Carve-out PR flipped this to 0; baseline JSONL preserved as the
      failing-state witness — see `baseline-results/summary.md` follow-up.)
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
      as a standalone PR ahead of Phase 1. (Carve-out landed: `ApplySetters`
      now runs inside a scope-based suppression scope on the control's
      `ReactorState`. M13 flipped from `OnIsOnChangedFireCount = 1` to `0`.)

---

## Phase 2 wrap-up — §13 Q1 measurement gate

Phase 2 is the descriptor-vs-handler head-to-head per spec §14. Exit gate:
the §13 Q1 decision matrix produces an answer.

- [x] 2.1 Build `ControlDescriptor<TElement, TControl>` interpreter under
      `src/Reactor/Core/V1Protocol/Descriptor/` (fluent builder,
      `PropEntry` types, `DescriptorHandler<,>`). Internal-access fast path
      via `Reconciler.GetOrCreateControlEventPayload<DescriptorControlledPayload<…>>`.
- [x] 2.2 Implement the three Q1 controls as descriptors against the same
      v1 protocol surface as the hand-coded ports:
      `ToggleSwitchDescriptor` (1 controlled event), `SliderDescriptor`
      (1 controlled + 2 coercing one-way), `BorderDescriptor` (zero events).
      Behavior parity verified by 23/23 `Desc_*` AppTests.Host self-test
      assertions; Phase 1 `V1_*` fixtures still 20/20.
- [x] 2.3 Run §15.3 micro suite (M1, M2, M5, M7, M10) across all three
      variants (`ReactorToday` / `ReactorV2` / `ReactorDescriptors`).
      Three captures committed under
      `docs/specs/047/phase2-results/LAPTOP-4MEP83VI/`:
      - `2026-05-26-q1-spike-5x5/` — pre-fast-path baseline (5×5).
        Public `OnCustomEvent` path. Verdict at the time: ship hand-coded
        (M2 +19.1%, M10 +31.5%).
      - `2026-05-26-q1-fastpath-3x5/` — internal typed-payload fast path
        (3×5). Capture noisy (M1 +23.5% on a TextBlock that doesn't engage
        the descriptor path — flagged as suspect at the time).
      - `2026-05-26-q1-fastpath-3x5-stableac/` — **authoritative.** Same
        code as the noisy capture, stable AC + foreground, 3×5. M2 +9.6%,
        no gating bench exceeds 15%.
- [x] 2.4 Apply the §13 Q1 decision matrix to the authoritative capture.
      Worst gating bench M2 at +9.6% — judgment-call band. LOC + readability
      inputs resolved to descriptors (~24% LOC saving at Phase 3 scope;
      §6.1 classifications visible at call sites).
- [x] 2.5 Spec edits committed: §13 Q1 status flipped to "Resolved"; §6.1
      extended with `.HandCodedControlled` / `.HandCodedEvent` classifications
      (§6.1.1); §9.2 extended with per-descriptor TPayload composition
      (§9.2.1); §14 Phase 2 marked complete and Phase 3 prerequisites added;
      `decision-criteria.md` Q1 marked Resolved.

**Phase 2 exit gate met.** Descriptors are the primary first-party surface
going forward (§6.1). Hand-coded `IElementHandler<,>` stays as escape hatch
for irregular controls, perf-critical mount paths, and multi-event
composition (§6.1.1).

---

## Phase 3 prerequisites — multi-event composition + author guidance

Before Phase 3 bulk-ports the ~60 remaining controls (spec §14 Phase 3 order),
these prerequisites must land. They follow from the Phase 2 verdict + the
single-slot `ControlEventStateBox` constraint identified during the Phase 2
spec discussion (see §9.2.1).

- [x] 3.0.1 Shipped `.HandCodedControlled` / `.HandCodedEvent` builders
      and the `HandCodedControlledPropEntry` / `HandCodedEventPropEntry`
      classes (PR #424). Author supplies the static trampoline + typed
      slot accessors per §6.1.1.
- [x] 3.0.2 Ported `TextBox` as the 2-event proof point (TextChanged +
      SelectionChanged) using the shared `TextBoxEventPayload` (PR #424).
      AppTests.Host self-test fixtures (`Desc_TextBox_*`) cover both
      events independently and together.
- [x] 3.0.3 Re-benched M2 / M10 against the TextBox descriptor port
      (PR #424, x64 advisory capture under
      `docs/specs/047/phase3-results/CPC-ander-YTZ3O-x64-advisory/2026-05-27-textbox-proof-3x5/`).
      ARM64 stable-AC ratification on LAPTOP-4MEP83VI deferred — protocol
      captured in the same dir's README.
- [ ] 3.0.4 Phase 3 author onboarding doc: lift the §6.1.1 classification
      table verbatim, plus a worked example (TextBox descriptor walk-through)
      and the "when to fall through to `IElementHandler<,>`" guidance.
      Land under `docs/guide/` once the path is committed. **Deferred** —
      not gating the bulk-port; can land any later session.

---

## Phase 3 bulk-port progress

Tracks per-family descriptor ports against the §14 migration order.
Each batch is a single PR landing the descriptors + self-test fixtures +
bench factory registration. Legacy `MountXxx` / `UpdateXxx` arms stay in
place while V1 is flag-gated (V1-OFF authors still hit the legacy path);
shrink lands after V1 ships ON by default.

### Value-bearing family

- [x] **Batch 1** — `CheckBox`, `RadioButton`, `RatingControl`,
      `ToggleSplitButton`. All `.Controlled` single-event ports;
      `CheckBox`/`RadioButton` wire both `Checked` and `Unchecked` events
      to the same trampoline in their subscribe lambdas. Fixtures:
      `Desc_CheckBox_MountUpdate`, `Desc_RadioButton_MountUpdate`,
      `Desc_RatingControl_MountUpdate`, `Desc_ToggleSplitButton_MountUpdate`
      — all pass under V1 ON and V1 OFF.
      **Known gaps** (mirrored on `CheckBoxDescriptor`):
      `IsThreeState=true` mode (controlled value source is `CheckedState`
      not `IsChecked`) and the `OnCheckedStateChanged` callback are not
      yet handled by the descriptor; three-state authors continue on the
      legacy arm. `ToggleSplitButtonDescriptor` does not yet express the
      `Flyout` child — author via setters chain for now.
- [x] **Batch 2** — `ColorPicker`, `CalendarDatePicker`, `DatePicker`,
      `TimePicker`. All `.Controlled` single-event ports against
      `TypedEventHandler` / `EventHandler<TArgs>` event signatures.
      Fixtures: `Desc_ColorPicker_MountUpdate`,
      `Desc_CalendarDatePicker_MountUpdate`, `Desc_DatePicker_MountUpdate`,
      `Desc_TimePicker_MountUpdate` — all pass under V1 ON and V1 OFF.
      **Note on Update parity:** the legacy `UpdateCalendarDatePicker` /
      `UpdateDatePicker` arms didn't re-write `Header` / `MinDate` /
      `MaxDate` / format props on subsequent renders; the descriptor's
      `OneWayConditional` entries do (positive divergence — element
      changes flow through).
- [x] **Batch 3** — `TextBlock`, `Image`, `PersonPicture`, `ProgressBar`,
      `ProgressRing`, `InfoBadge`. All `.OneWay` / `.OneWayConditional`
      zero-event ports under the Display family. Fixtures:
      `Desc_TextBlock_MountUpdate`, `Desc_Image_MountUpdate`,
      `Desc_PersonPicture_MountUpdate`, `Desc_ProgressBar_MountUpdate`,
      `Desc_ProgressRing_MountUpdate`, `Desc_InfoBadge_MountUpdate` —
      all pass under V1 ON and V1 OFF.
      **Known gaps:**
      - `RichTextBlock` was *not* ported — its
        `MountRichTextBlock`/`UpdateRichTextBlock` arms build a stateful
        `Paragraphs/Inlines` tree and the `Update` path does incremental
        per-paragraph inline diffing that doesn't fit a `.OneWay` lambda
        without regressing the fast paths. Escape-hatched; legacy arm
        continues to serve V1 OFF authors and V1 ON falls through.
      - `ImageDescriptor` does not subscribe to `ImageOpened` /
        `ImageFailed` (Batch 3 is zero-event only). The legacy arm
        continues to fire `OnImageOpened` / `OnImageFailed` callbacks;
        descriptor authors who need image-load events fall through.
      - `InfoBadgeDescriptor` does not write `Icon` (the legacy arm
        doesn't either — mirrored gap, not regressed).
- [x] **Batch 4** — `Button`, `HyperlinkButton`, `RepeatButton`,
      `ToggleButton`, `DropDownButton`, `SplitButton`. Button-family
      ports: `.HandCodedEvent<...EventPayload, RoutedEventHandler>` for
      Click + `.OneWay` props. ToggleButton's Click handler fires both
      `OnIsCheckedChanged` and `OnCheckedStateChanged` via the same
      trampoline (mirrors legacy). Four new payload types added to
      ControlEventPayloads.cs (`HyperlinkButtonEventPayload`,
      `RepeatButtonEventPayload`, `ToggleButtonEventPayload`,
      `SplitButtonEventPayload`); existing `ButtonEventPayload` reused
      for `ButtonDescriptor`. `SplitButtonEventPayload.ClickTrampoline`
      uses `TypedEventHandler<SplitButton, SplitButtonClickEventArgs>`
      (SplitButton's Click signature is typed, not RoutedEventHandler).
      Fixtures: `Desc_Button_MountUpdate`, `Desc_HyperlinkButton_MountUpdate`,
      `Desc_RepeatButton_MountUpdate`, `Desc_ToggleButton_MountUpdate`,
      `Desc_DropDownButton_MountUpdate`, `Desc_SplitButton_MountUpdate` —
      all pass under V1 ON and V1 OFF.
      **Known gaps:**
      - `Flyout` on `DropDownButton` / `SplitButton` is escape-hatched
        (requires `CreateFlyoutFromElement` engine-internal helper, not
        expressible via the descriptor builders this session). Authors
        needing a Flyout fall through to the legacy arm (V1 OFF) or wire
        via setters chain.
      - `ButtonElement.ContentElement` (Button hosting a child Element
        rather than a string label) not expressed by `ButtonDescriptor` —
        descriptor handles the string-Content fast path only; nested
        element content falls through to the legacy arm.
- [x] **Batch 5** — `RichEditBox`, `PasswordBox`, `RadioButtons` (plural
      group control; the singular `RadioButton` was Batch 1). Value-bearing
      input ports: `.HandCodedControlled<...EventPayload, TValue, TDelegate>`
      for the controlled DP + change event. Three new payload types added
      to ControlEventPayloads.cs (`RichEditBoxEventPayload`,
      `PasswordBoxEventPayload`, `RadioButtonsEventPayload`).
      PasswordBox trampoline keeps the manual
      `ChangeEchoSuppressor.ShouldSuppress` gate (mirrors legacy) so even
      author-driven suppressor tokens (e.g. from a future coercing setter)
      still gate the user-vs-engine echo on top of HandCodedControlled's
      WriteSuppressed wrap.
      Fixtures: `Desc_RichEditBox_MountUpdate`, `Desc_PasswordBox_MountUpdate`,
      `Desc_RadioButtons_MountUpdate` — all pass under V1 ON and V1 OFF.
      **Known gaps:**
      - `RichEditBoxDescriptor.Text` write is gated on
        `!IsNullOrEmpty` (mirrors the legacy mount guard) — programmatic
        clears via `Text=""` on Update are NOT propagated; authors who
        need a fully-controlled empty document stay on the legacy arm.
        No symmetric snap-back, same pattern as `TextBoxDescriptor`.
      - `RadioButtonsDescriptor.Items` uses Clear+Add when the new array
        differs by sequence — no keyed reconciliation. Suitable for the
        typical 3–7 fixed-option case; large dynamic item lists fall
        through to the legacy arm.
      - `RadioButtonsElement` only carries a `string[]`; Element-typed
        items (icon-rich radios) are not in scope this batch.
- [x] **Batch 6** — `AutoSuggestBox`, `ComboBox`. First multi-event
      descriptor ports — each mixes one `.HandCodedControlled` round-trip
      with two `.HandCodedEvent` fire-only subscriptions over a shared
      per-control payload. Two new payload types added to
      ControlEventPayloads.cs (`AutoSuggestBoxEventPayload`,
      `ComboBoxEventPayload`), each with three trampoline slots.
      `AutoSuggestBoxDescriptor.Text` trampoline filters on
      `args.Reason == UserInput` (mirrors legacy) on top of the
      `ChangeEchoSuppressor` gate from `HandCodedControlled`'s
      `WriteSuppressed` wrap. `ComboBox.SelectedIndex` is gated by the
      same suppressor pattern. Fixtures:
      `Desc_AutoSuggestBox_MountUpdate`, `Desc_ComboBox_MountUpdate` —
      both pass under V1 ON and V1 OFF.
      **Known gaps:**
      - `ComboBoxDescriptor.Items` is escape-hatched. ComboBox's items
        collection requires the legacy mode-switch logic (string[] vs
        Element[] keyed reconciliation against `requestRerender`), none
        of which the descriptor builders can yet express. Authors who
        need ComboBox items must run V1 OFF (legacy arm handles Items)
        or populate `cb.Items` via a `.Set` setter (imperative escape).
        The Batch 6 fixture exercises the setter route to prove
        SelectedIndex coordinates with a populated list.
      - `AutoSuggestBoxDescriptor.QueryIcon` is re-resolved each pass
        when present (legacy arm gates on
        `!ReferenceEquals(o.QueryIcon, n.QueryIcon)` — descriptor's
        OneWay path can't see the previous element ref). Same visual
        result, slightly more work per pass when QueryIcon is set.
      - `AutoSuggestBoxDescriptor.Suggestions` transition to empty
        does not clear the previous `ItemsSource` (mirrors the legacy
        mount guard's `Length > 0` gate).
      - `ComboBoxElement.IsDropDownOpen` is not exposed on the element
        record itself; descriptor doesn't surface it. (Legacy parity.)
- [x] **Batch 7** — `Viewbox`, `Expander`, `ScrollViewer`, `ScrollView`.
      First single-content container ports — all use the
      `SingleContent<TElement, TControl>` children strategy for the
      primary child slot. Viewbox is pure data (zero events, two
      `.OneWayConditional` enum props); Expander adds a
      `.HandCodedControlled` for `IsExpanded` paired with a
      `.HandCodedEvent` for `Collapsed` on a new
      `ExpanderEventPayload` (two `TypedEventHandler` slots, one per
      direction); ScrollViewer + ScrollView add a single
      `.HandCodedEvent` for `ViewChanged` on the pre-existing
      `ScrollViewerEventPayload` / `ScrollViewEventPayload`. One new
      payload type added to ControlEventPayloads.cs
      (`ExpanderEventPayload`).
      Fixtures: `Desc_Viewbox_MountUpdate`, `Desc_Expander_MountUpdate`,
      `Desc_ScrollViewer_MountUpdate`, `Desc_ScrollView_MountUpdate` —
      all pass under V1 ON and V1 OFF.
      **Known gaps:**
      - `ExpanderDescriptor.HeaderTemplate` (Element-typed header) is
        not surfaced. Mounting an Element into a non-primary slot
        requires reconciler context the descriptor builders can't yet
        express (no NamedSlots for a single Element slot that also
        coexists with a string fallback). The string `Header` path is
        fully supported. Authors with Element headers stay on V1 OFF
        (legacy arm reconciles `HeaderTemplate` via `ReconcileChild`).
      - `ExpanderDescriptor.ContentTransitions` is not surfaced
        (same reason — escape-hatched via setter when needed).
- [x] **Batch 8** — `StackPanel`, `Grid`, `Canvas`, `FlexPanel`,
      `RelativePanel`. First panel container ports — all zero-event, all
      use the `Panel<TElement, TControl>` children strategy from
      `ChildrenStrategy.cs`. Each descriptor wires container-level
      one-way props through `.OneWay` / `.OneWayConditional`. `Grid` ports
      the imperative `RowDefinitions` / `ColumnDefinitions` rebuild as a
      single `.OneWay<GridDefinition>` whose set lambda clears + rebuilds
      both collections through `Reconciler.ParseRowDef` /
      `ParseColumnDef`; the comparer is reference-equality so the rebuild
      only fires when the element's `Definition` instance changes
      (mirrors the legacy `!ReferenceEquals(o.Definition, n.Definition)`
      gate). No new payload types.
      Fixtures: `Desc_StackPanel_MountUpdate`, `Desc_Grid_MountUpdate`,
      `Desc_Canvas_MountUpdate`, `Desc_FlexPanel_MountUpdate`,
      `Desc_RelativePanel_MountUpdate` — all pass under V1 ON and V1 OFF.
      **Known gaps:**
      - **Per-child attached properties are not applied** on
        descriptor-mounted children for any panel. The legacy hand-coded
        path applies `GridAttached` (Row/Column/RowSpan/ColumnSpan),
        `CanvasAttached` (Left/Top), `FlexAttached` (Grow/Shrink/Basis
        etc.), `RelativePanelAttached` (the two-pass name-map for
        RightOf/Below/AlignWithPanel), and `WrapGridAttached` (RowSpan /
        ColumnSpan) as a post-children-mount step. The Panel strategy in
        `V1HandlerAdapter` doesn't surface a per-child post-mount hook
        yet — descriptor-mounted children stack at the panel origin /
        Row 0 / default Yoga config. Authors who depend on attached
        positioning stay on V1 OFF (legacy arm). Container-level layout
        (spacing, orientation, definitions) has full parity.
      - **`WrapGridElement` (VariableSizedWrapGrid) is escape-hatched** —
        the legacy element is hand-coded only. WrapGrid's primary use is
        items-positioned (RowSpan / ColumnSpan per child via
        `WrapGridAttached`), so without the per-child post-mount hook a
        descriptor port has no meaningful coverage. Re-evaluate when the
        Panel strategy grows an attached-props hook.
- [x] **Batch 9** — `SplitView`, `InfoBar`, `TeachingTip`. First named-slot
      container ports — all use the
      `NamedSlots<TElement, TControl>` children strategy. SplitView
      surfaces two Element slots (Pane + Content) with `GetCurrentChild`
      for structural reconciliation, plus twin `.HandCodedEvent` entries
      on `PaneOpening` / `PaneClosing` that dispatch the same
      `OnPaneOpenChanged(bool)` callback with the corresponding direction
      (legacy parity — no echo suppression; the WinUI events fire on both
      user and programmatic transitions, matching the hand-coded arm).
      InfoBar has a single Content slot plus a `.HandCodedEvent` on
      `Closed`; TeachingTip has two named slots (Content + HeroContent)
      plus two `.HandCodedEvent` entries (`ActionButtonClick` and
      `Closed`). Three new payload types: `SplitViewEventPayload` (two
      typed slots), `InfoBarEventPayload` (one slot), `TeachingTipEventPayload`
      (two slots). IconSource on InfoBar / TeachingTip routes through
      `Reconciler.ResolveIconSource` via `.OneWayConditional` with a
      private reference-equality comparer (mirrors the legacy
      `!ReferenceEquals` gate).
      Fixtures: `Desc_SplitView_MountUpdate`, `Desc_InfoBar_MountUpdate`,
      `Desc_TeachingTip_MountUpdate` — all pass under V1 ON and V1 OFF.
      **Known gaps:**
      - **InfoBar `ActionButtonContent` + `OnActionButtonClick` is
        escape-hatched.** The legacy arm constructs an inner `Button`
        dynamically inside the InfoBar's `ActionButton` slot when
        `ActionButtonContent` is non-null, then wires `Click` on that
        dynamically-created child. The descriptor framework binds events
        to the primary control, not to a sub-control created during
        mount, so this asymmetric pattern doesn't fit. Authors who need
        the action button stay on V1 OFF (legacy arm), or use a `.Set`
        imperative setter to construct the button themselves.
      - **TeachingTip `Target` / `PlacementTarget` is escape-hatched.**
        `TeachingTip.Target` is a `FrameworkElement` reference pointing
        at a sibling control the tip is anchored to (not a child the tip
        mounts). The descriptor framework can't express "reference
        another element's mounted control"; legacy authors set `Target`
        directly via a `.Set` imperative setter and the descriptor
        follows the same escape.
      - **TeachingTip `HeroContent`** uses the standard NamedSlot
        reconciliation path, which preserves descendant state across
        re-renders (the legacy arm re-mounts wholesale on every swap).
        Strictly an improvement, documented for parity audit visibility.
- [x] **Batch 10** — `Rectangle`, `Ellipse`, `Line`, `Path`, `AnimatedIcon`.
      Five zero-event leaves with no children — pure
      `.OneWay` / `.OneWayConditional`. Shape descriptors live under the
      `WinShapes = Microsoft.UI.Xaml.Shapes` alias.
      Fixtures: `Desc_Rectangle_MountUpdate`, `Desc_Ellipse_MountUpdate`,
      `Desc_Line_MountUpdate`, `Desc_Path_MountUpdate`,
      `Desc_AnimatedIcon_MountUpdate` — all pass under V1 ON and V1 OFF.
      **Known gaps:**
      - **`PathElement.Data` / `PathDataString` is escape-hatched.** The
        legacy `MountPath` branches across three strategies (XamlReader
        load of a constructed `<Path Data="…"/>`, pre-built
        `Geometry` assignment with structured error reporting, and a
        `PathDataParser.Parse` fallback) and the legacy `UpdatePath` gates
        the Data write on a string-diff of `PathDataString` (the parser
        creates fresh COM `PathGeometry` instances per call, so reference
        equality is never true). None of these compose with a plain
        `.OneWay` setter — the engine's per-prop comparer can't replicate
        the string-diff-against-old-element trick. Authors who need
        `Path.Data` stay on V1 OFF. The descriptor still covers the bulk
        of the Path styling surface (Fill / Stroke / dash / cap / join /
        transform), which is the per-render write pressure for D3 charts.
      - **`PathElement.FillRule` is escape-hatched** — the legacy handler
        propagates `FillRule` onto the inner `PathGeometry`, but the
        descriptor doesn't own that `PathGeometry` (Data is escape-hatched
        above).
      - **`IconElement` is escape-hatched (not ported).** The legacy
        `MountIcon` is polymorphic — it dispatches the `IconData` subtype
        through `ResolveIcon` to construct one of `FontIcon` /
        `SymbolIcon` / `PathIcon` / `BitmapIcon` (different `IconElement`
        subtypes). `ControlDescriptor<TElement, TControl>` is single-`TControl`
        by construction, so a single descriptor can't carry the dispatch.
        Worse, `UpdateIcon` can swap the entire native control when the
        `IconData` subtype changes (returning a replacement `UIElement`),
        a path the descriptor framework's update protocol doesn't
        currently express. Authors stay on V1 OFF.
      - **`AnimatedIcon.Source` is shape-checked** in the descriptor's
        `set` lambda (mirrors legacy behavior — non-`IAnimatedVisualSource2`
        values silently no-op). The descriptor doesn't expose a typed
        Source slot because `AnimatedIconElement.Source` is `object?` on
        the element record.
      - **Shape mount-time `> 0` gates** — the legacy `Mount*` arms write
        `StrokeThickness` / `RadiusX` / `RadiusY` only when `> 0`; the
        legacy `Update*` arms write them unconditionally. The descriptors
        mirror the update path (plain `.OneWay`), which lines up with
        every `Update*` write and the element's default zero values; the
        visible output is the same for callers who never set them. No
        behavior delta for non-zero callers.
- [x] **Batch 11** — Long-tail triage: `PipsPager`, `ListBox`,
      `SelectorBar`, `BreadcrumbBar` ported. `FrameElement` and
      `CalendarViewElement` deferred (escape-hatched).
      Fixtures: `Desc_PipsPager_MountUpdate`, `Desc_ListBox_MountUpdate`,
      `Desc_SelectorBar_MountUpdate`, `Desc_BreadcrumbBar_MountUpdate` —
      all pass under V1 ON and V1 OFF.
      **Ported (4):**
      - **PipsPager** — `SelectedPageIndex` round-trip via
        `.HandCodedControlled` against the new `PipsPagerEventPayload`;
        `NumberOfPages` / `WrapMode` / `MaxVisiblePips` /
        `PreviousButtonVisibility` / `NextButtonVisibility` as
        `.OneWay`. Trampoline gates on `ChangeEchoSuppressor`.
      - **ListBox** — `Items` (non-keyed Clear+Add cycle on sequence
        delta, mirroring `RadioButtonsDescriptor`) + `SelectedIndex`
        round-trip. The single `SelectionChanged` trampoline fires
        BOTH `OnSelectedIndexChanged` and the multi-select snapshot
        `OnSelectionChanged` — matches the legacy arm's twin-invoke
        shape, including the `IndexOf`-against-Items snapshot
        reconstruction.
      - **SelectorBar** — `Items` cycle (Text + Icon per item) +
        `SelectedIndex` round-trip mapped through `SelectedItem` ref
        (SelectorBar exposes `SelectedItem`, not `SelectedIndex`, as
        the live property). Item icon resolution reuses
        `Reconciler.ResolveIconForDescriptor` via a
        `SymbolIconData` wrapper.
      - **BreadcrumbBar** — `Items` → `ItemsSource` (label list) +
        `ItemClicked` fire-only event. Trampoline maps
        `args.Index` back to `el.Items[idx]` per the legacy arm.
      **Escape-hatched (2) — documented gaps:**
      - **FrameElement** — `Navigate(SourcePageType, NavigationParameter)`
        is an imperative API call invoked only at Mount time (the
        legacy `UpdateFrame` is just `SetElementTag` + `ApplySetters`,
        no re-navigate). The descriptor builders don't distinguish
        mount-only writes from update writes — a `.OneWay` for
        `SourcePageType` would re-Navigate on every update pass.
        The 3 events (`Navigated`, `Navigating`, `NavigationFailed`)
        could be ported in isolation, but a descriptor that handles
        only events while losing the Mount-time navigation would be
        a regression vs. V1 OFF. Authors who need declarative `Frame`
        stay on V1 OFF; future work is a mount-only entry shape.
      - **CalendarViewElement** — `SelectedDates` is an
        `IObservableVector<DateTimeOffset>` collection that the legacy
        arm mutates element-by-element with per-mutation
        `ChangeEchoSuppressor.BeginSuppress` tokens
        (`UpdateCalendarView` → `SyncSelectedDates`: a hash-set diff
        with one suppress per Add/Remove). The descriptor builders
        don't express collection diffs with per-element suppression
        — a single `.OneWay` write to `SelectedDates` would either
        echo per element or require a custom collection-aware entry
        shape. Authors who need declarative multi-date selection stay
        on V1 OFF.
- [x] Batch 3-followup — addressed by Phase 3-final Batch B (`Frame`,
      `RichTextBlock`, `NumberBox`) and Batch C (`CalendarView`). Engine
      shapes (`.Immediate`, `.OneWayBridged`, `.CollectionDiffControlled`)
      added in Phase 3-final Batch A. Documented residual carve-outs
      retained — see Phase 3-final batch entries below.

**Phase 3-final descriptor scale-out** (delivers the Phase 3 follow-ups
plus within-control partial-port gaps from PR #435 batches 3–11):

- [x] **Batch A — engine shapes.** Added `.OneWayBridged<TValue>` (set
      lambda gets `(TControl, TValue, Reconciler, Action requestRerender)`
      — for dynamically-constructed child controls), `.Immediate<TPayload>`
      (pure subscription wiring), `.CollectionDiffControlled` (`IList<T>`
      two-way with hash-set diff under `BeginSuppress`),
      `Panel<>.PerChildAttached`, `ItemsHost<TElement,TControl>` (flat),
      and `Reconciler.CreateFlyoutForDescriptor`. Engine-only commit —
      no controls.
- [x] **Batch B** — `Frame`, `RichTextBlock`, `NumberBox`.
      - **Frame** — three `.HandCodedEvent` subscriptions
        (`Navigated`/`Navigating`/`NavigationFailed`) gate on
        callback-at-mount. Legacy `MountFrame` subscribes unconditionally
        so late-attached callbacks fire through. Common case covered;
        authors who attach callbacks after Mount stay on V1 OFF.
      - **RichTextBlock** — `Paragraphs` rebuild via reference-equality.
        Legacy `UpdateRichTextBlock` does incremental per-paragraph diff;
        authors needing the incremental shape stay on V1 OFF.
      - **NumberBox** — plain `.OneWay` `Minimum`/`Maximum` (no coercion
        suppression). `.CoercingOneWay` could be wired later.
- [x] **Batch C** — `CalendarView` via `.CollectionDiffControlled`.
      Null `SelectedDates` is treated as empty list (descriptor clears
      the vector); legacy treats null as uncontrolled (preserves user
      picks). Call sites must pass a list whenever selection is
      controlled.
- [x] **Batch D** — `DropDownButton`/`SplitButton`/`ToggleSplitButton`
      Flyout child via `.OneWayBridged` + `Reconciler.CreateFlyoutForDescriptor`.
      Closes the Batch 4 Flyout escape-hatch.
- [x] **Batch E** — `Grid`/`Canvas`/`FlexPanel` per-child attached props
      via `Panel.PerChildAttached`; `WrapGrid` ported with a tailored
      panel shape. Closes the Batch 8 per-child attached gap (except
      `RelativePanel` — see carve-outs).
- [x] **Batch F** — `Image` events (`ImageOpened`/`ImageFailed` via
      `.HandCodedEvent` over the existing `ImageEventPayload`),
      `Path.Data` (pre-built `Geometry` via `.OneWayConditional` gated
      on `PathDataString` being null), `InfoBar.ActionButton` (via
      `.OneWayBridged` with a Click trampoline).
- [x] **Batch G-prep — engine ordering fix.**
      `ItemsHost.GetCollection` retyped from `System.Collections.IList`
      to `IList<object>` (WinUI `ItemCollection` does not implement the
      non-generic projection under CsWinRT). `DescriptorHandler` now
      dispatches `ItemsHost` inline between `RentControl` and the prop
      loop on Mount, and before the prop Update loop on Update, so
      selection-tracking initial writes (`SelectedIndex`/`SelectedItem`)
      land against a populated collection. Strategy shape unchanged for
      hand-coded handlers — V1HandlerAdapter dispatch path is preserved.
- [x] **Batch G1 — flat `ItemsHost` ports.** `ListBox`, `ComboBox`,
      `RadioButtons` migrate from `.OneWay<string[]>` items entries to
      `Children = new ItemsHost<...>(GetItems: e => (IReadOnlyList<object>)e.Items, GetCollection: c => c.Items)`.
      `ComboBox.ItemElements` (`Element[]?`) supported alongside `Items`
      (`string[]`); the engine routes `Element` items through
      `MountChild`. Fixtures: `Desc_ListBox_Items`, `Desc_ComboBox_Items`,
      `Desc_RadioButtons_Items`.

**Phase 3 close-out** (`spec/047-phase3-close-out` branch, off PR #436
HEAD) — engine shapes + ports that close the largest Phase 3-final
carve-outs:

- [x] **Engine (1) — `Panel<>.PerChildAttachedAfterAll`.** Two-pass
      shape: optional `Action<TControl, IReadOnlyList<(UIElement,Element)>>?`
      callback fired once after every child has been mounted (Mount
      path) or reconciled (Update path) with the full ordered pair
      list. Distinct from `PerChildAttached` (which fires per-child
      mid-pass and cannot see siblings that haven't mounted yet). Pair
      list allocated lazily — `null` consumers pay no overhead. Existing
      `Grid`/`Canvas`/`FlexPanel`/`WrapGrid` unchanged.
- [x] **Engine (2) — `TemplatedItems<>` strategy +
      `Reconciler.BindKeyedItemsSource`.** New record
      `TemplatedItems<TItem, TElement, TControl>` (open in TItem;
      descriptor authors of new typed templated lists declare items,
      key selector, view builder). Engine binder
      `BindKeyedItemsSource<TItem>` wires `ReactorListState` +
      shared `ContainerContentChanging` + spec-042 `KeyedListDiff.Apply`
      against a new `IItemViewSource` stash on `ReactorState`.
      MVP supports `WinUI.ListViewBase`; `ItemsRepeater` / `Lazy*Stack`
      surface a descriptive `InvalidOperationException` at the
      dispatch switch (purely additive to add).
- [x] **Port (4) — `RelativePanel` via `PerChildAttachedAfterAll`.**
      Closes the Phase-4 carve-out documented on
      `RelativePanelDescriptor`. The descriptor's after-all callback
      builds a name → control map across mounted children, assigns
      `FrameworkElement.Name`, then writes
      `RelativePanel.SetRightOf` / `SetBelow` / `SetAlignLeftWith` /
      etc. against sibling references. Body lifted from legacy
      `MountRelativePanel`. Fixture: `Desc_RelativePanel_MountUpdate`
      (now includes 5 sibling-named two-pass assertions).
- [x] **Port (5) G2 — `TemplatedListView<T>` / `TemplatedGridView<T>`
      via base-derived registration + `TemplatedItemsErased<>`.**
      The handoff prompt assumed each closed T would need its own
      registration. The realistic shape mirrors what the legacy
      `Reconciler.Mount` switch does: T-erasure at a non-generic
      abstract base. Engine extensions:
      - `V1HandlerRegistry.AddForDerivedTypes` + cached base-walk
        in `TryGet`. Exact-type registrations always win; base-derived
        entries catch every closed-T variant via the runtime type's
        base chain.
      - `Reconciler.RegisterHandlerForDerivedTypes<TBase,TControl>`
        surfaces the registry capability on the public v1 API.
      - New empty intermediate bases `TemplatedListViewElementBase`,
        `TemplatedGridViewElementBase`, `TemplatedFlipViewElementBase`
        under `TemplatedListElementBase`. No fields, seal
        `ControlKind`; leaf record equality unchanged because the
        leaf type still owns its `EqualityContract`.
      - `TemplatedItemsErased<TElement,TControl>` strategy +
        non-generic `IErasedTemplatedItemsStrategy` dispatch marker.
        Strategy is non-generic in TItem; items + keys read through
        the element's `IKeyedItemSource` implementation.
      - `Reconciler.BindErasedKeyedItemsSource` — companion to the
        TItem-carrying binder. Same realization pipeline; selection
        + click event wiring inlined so the descriptor needs no new
        `ControlEventState` payload box.
      - Two descriptors register on the intermediate bases; the
        registry walk routes every closed-T variant to the same
        descriptor. `TemplatedFlipView<T>` intentionally not ported
        (FlipView pre-mounts items; no `ContainerContentChanging`,
        no OC delta channel).
      Fixtures: `Desc_TemplatedListView_MountUpdate`,
      `Desc_TemplatedGridView_MountUpdate` (17 assertions covering
      Mount, keyed insert/remove diff, same-ref idempotency).
- [x] **Selftest baselines (Cloud PC x64, post close-out).**
      V1 ON `Desc_`: 556 ok / 0 failures.
      V1 OFF `Desc_`: 556 ok / 0 failures (parity preserved).
      Legacy `KLR_` keyed-list fixtures: 73 ok / 0 failures (engine
      refactor of `RefreshRealizedContainers` + the shared CCC
      handler is behavior-neutral for the legacy path).

**Phase 3 close-out carve-outs — status after Phase 3 finish:**

- [x] **Expander.HeaderTemplate** — closed by Phase 3 finish via
      Engine (2) `.ImperativeBridged`; two-strategy composition
      resolved at the property level (`Children` stays as
      `SingleContent`).
- [x] **TeachingTip.Target** — closed by Engine (3) audit. Legacy
      doesn't set Target either; setter escape is the contract in
      both paths. Declarative deferred-resolution shape is future
      polish, not a Phase 3 gate.
- [x] **Path.PathDataString** — closed by Phase 3 finish via
      Engine (4) `.Imperative`. Single entry drives all three legacy
      strategies (XamlReader.Load → pre-built Geometry →
      PathDataParser.Parse) end-to-end with the same multi-source
      `ArgumentException` rethrow path.
- [x] **NumberBox coercion** — closed by Engine (5) audit; existing
      `.CoercingOneWay` already matched `UpdateNumberBox`'s
      suppression pattern line-for-line. NumberBoxDescriptor.Min/Max
      ported through.
- [x] **`Lazy*Stack<T>` G2 port** — closed by Phase 3 finish (Port (6)).
      `BindErasedKeyedItemsSource` gained a `case WinUI.ItemsRepeater`
      arm; `LazyStackElementBase` implements both `IKeyedItemSource`
      and a new internal `IItemsRepeaterFactorySource`. Single
      base-derived descriptor catches every closed-T variant. Behavior
      diff: descriptor's TControl is `WinUI.ItemsRepeater` directly
      (no auto-`ScrollViewer` wrapping).
- [x] **`ItemsRepeater<T>` G2 port** — closed by Phase 3 finish. New
      `ItemsRepeaterElementBase` + `ItemsRepeaterElement<T>` records
      (Element.cs) model on `LazyStackElementBase` and implement
      `IKeyedItemSource` + `IItemsRepeaterFactorySource`, so dispatch
      flows through Engine (1)'s ItemsRepeater arm with no new engine
      work. Legacy `MountItemsRepeater` / `UpdateItemsRepeater` arms
      added (the element type is new — there was no legacy arm before).
      DSL surface: `ItemsRepeater<T>` factory in `Dsl.cs`. Single
      base-derived `ItemsRepeaterDescriptor` catches every closed-T
      variant. 11 new fixtures (Desc_ItemsRepeater_*). Every typed-items
      host family scoped in Phase 3 now has a V1 descriptor (the
      precise close-out claim — the broader "every Element type"
      audit is captured under the "Phase 3 deferred / not-attempted"
      section below).
- [x] **G3 typed lists — `TreeView`, `FlipView`, `TabView`, `Pivot`** —
      closed by Phase 3 finish. **Note:** "FlipView" here is the
      simple non-templated `FlipViewElement` (Element[] items). The
      typed `TemplatedFlipViewElement<T>` peer was ported in Phase 3
      completion via the new `PreMountedItems<>` strategy + base-derived
      `TemplatedFlipViewDescriptor` registered on
      `TemplatedFlipViewElementBase` — see the Phase 3 completion entry
      below.
      - **TreeView** via new `TreeChildren<TElement, TControl>`
        strategy (hierarchical, positional rebuild on Update,
        recursive `ContentElement` mount).
      - **FlipView** reuses existing `ItemsHost<>` (alternative (b) —
        no new strategy needed).
      - **TabView + Pivot** share a new
        `TabItemsHost<TElement, TControl, TItem>` strategy with a
        per-descriptor `CreateContainer` lambda
        (`TabViewItem` / `PivotItem`).
      - TabView's `TabStripHeader` / `TabStripFooter` and spec 045
        §2.4 docking drag pipeline + §2.2 pinnable headers stay on
        the legacy arm; documented in the descriptor xmldoc.
      - 29 new fixtures across the four descriptors (Desc_TreeView_*,
        Desc_FlipView_*, Desc_TabView_*, Desc_Pivot_*). Total Desc_
        baseline: 602 ok / 0 failures both V1 ON and V1 OFF.
        (Total grows to 613 after Port (7) ItemsRepeater<T> above.)

**Phase 3 deferred / not-attempted** (element types in the legacy
`Reconciler.Mount` switch that have neither a Phase 1 V1 handler nor a
Phase 3 descriptor — out of scope for the Phase 3 batch list, recorded
here for a future Phase 3.5 / Phase 4 prelude). Cross-referenced from
the audit at the end of `spec/047-phase3-finish`:

- **Genuine engine gap (CLOSED — Phase 3 completion):**
  `TemplatedFlipViewElement<T>` — ported via the new
  `PreMountedItems<TElement, TControl>` ChildrenStrategy and
  `TemplatedFlipViewDescriptor`, registered base-derived against
  `TemplatedFlipViewElementBase`. The strategy pre-mounts every item
  up-front into `FlipView.Items` (no `ContainerContentChanging` to
  drive realization) and positionally reconciles via
  `Reconciler.ReconcileV1Child` on Update.
- **Untyped items hosts (CLOSED — Phase 3 completion, partial):**
  `ItemsViewElementBase`, `ItemContainerElement` — ported as
  standard descriptors and registered in `RegisterV1BuiltInHandlers`.
  `GridViewElement` (plain Element[]) — descriptor authored
  (`GridViewDescriptor`) but **carved during PR #440 CR**: the
  descriptor's `ItemsHost<>` strategy pre-mounts every item into
  `GridView.Items` (one container per item, no virtualization),
  while the legacy `MountGridView` arm uses
  `ItemsSource = Range(0..N) + ItemTemplate + ContainerContentChanging`
  to realize containers lazily (matches Phase 1 `ListViewHandler`).
  Closing this needs either a hand-coded `GridViewHandler` or a new
  ChildrenStrategy variant wrapping CCC. Tracked alongside TabView /
  overlays / NavigationHost gap-closure.
- **Heavy / specialized controls (CLOSED — Phase 3 completion):**
  `WebView2Element`, `NavigationViewElement`, `TitleBarElement`,
  `MediaPlayerElementElement`, `AnimatedVisualPlayerElement`,
  `MapControlElement`, `SemanticZoomElement`,
  `AnnotatedScrollBarElement`, `RefreshContainerElement`,
  `SwipeControlElement`, `ParallaxViewElement` — all descriptors
  authored and registered. (`NavigationHostElement` stays deferred —
  see below.)
- **Polymorphic / a11y (CLOSED — Phase 3 completion):**
  `IconElement` (decorator-style handler via the
  `IDecoratorElementHandler` engine extension landed this phase),
  `SemanticElement`, `AnnounceRegionElement` — all registered.

**Phase 3 completion — still deferred to the next PR (not regressions;
scoped carve list documented inline in `RegisterV1BuiltInHandlers`):**

- **Dialog / overlay family:** `ContentDialogElement`,
  `FlyoutElement`, `PopupElement`, `MenuBarElement`,
  `MenuFlyoutElement`, `CommandBarElement`,
  `CommandBarFlyoutElement`. Modal lifecycle (control-side-mounted,
  not parent-tree-mounted) requires decorator-style ports beyond
  the IDecoratorElementHandler shape used for `IconElement`.
- **Stateful host:** `NavigationHostElement`. Per-instance
  route/cache/transition state is intercepted in
  `Reconciler.UnmountRecursive` BEFORE the V1 dispatch arm; needs
  a small refactor to internal-expose `MountNavigationHost` /
  `UpdateNavigationHost` and duplicate cleanup logic in the V1
  handler before it can route through V1.
- **`TabViewDescriptor` (descriptor exists, registration carved):**
  Bisect (3× clean V1 ON full selftest with only TabViewDescriptor
  carved, vs. 1–4 random docking-text-find failures per run when
  registered: DockHooks / PixDoc / RoleAware / Composition /
  FloatRoot) ratifies the descriptor's documented gaps as hot in
  the docking suite — missing spec 045 §2.4 drag pipeline
  (`OnTabDragStarting` / `OnTabDragCompleted`), §2.2 pinnable
  headers (`BuildTabHeader` / `BuildPinButton` / in-place
  `TryUpdatePinHeaderInPlace`), in-place CanUpdate for tab content
  (preserves focus/state on re-renders), conditional `SelectedIndex`
  write, and `TabStripHeader` / `TabStripFooter` Element slots.
  Closing them requires engine work (post-children mount-hook so
  `SelectionChanged` subscribes after children-add + an
  `ImperativeBridged` shape for the named tab strip slots).
- **`GridViewDescriptor` (descriptor exists, registration carved
  during PR #440 CR):** The descriptor's `ItemsHost<>` ChildrenStrategy
  pre-mounts every item into `GridView.Items` (one container per item,
  no virtualization). The legacy `MountGridView` arm uses
  `ItemsSource = Range(0..N) + ItemTemplate + ContainerContentChanging`
  to realize containers lazily — matching Phase 1
  `ListViewHandler`. A|B tests pass either way (no fixture stresses
  GridView scale), but production memory/lifecycle would silently
  regress. Closing this needs either a hand-coded `GridViewHandler`
  mirroring `ListViewHandler`'s CCC virtualization, or a new
  ChildrenStrategy variant (e.g. `RecyclingItemsHost<>`) that wraps
  the `ItemsSource` + `ContainerContentChanging` realization
  contract.
- **Interop bridges:** `XamlHostElement`, `XamlPageElement`. V1
  descriptors exist (`XamlHostDescriptor`, `XamlPageDescriptor`)
  but stay unregistered because `XamlInterop.Register(reconciler)`
  populates the external `_typeRegistry` at app startup; auto-
  registering V1 would clash via `EnsureRegistrableElementType`.
  Unification is Phase 4 follow-up.

**Reactor composition primitives (intentionally above the V1
protocol — Phase 4 cleanup keeps their legacy arms):**

- `ComponentElement`, `FuncElement`, `MemoElement`,
  `ErrorBoundaryElement`, `CommandHostElement`,
  `Validation.FormFieldElement` /
  `ValidationVisualizerElement` / `ValidationRuleElement`. These
  orchestrate child reconciliation rather than wrap a single WinUI
  control, so the V1 handler protocol does not apply.
  (`ModifiedElement` is intentionally NOT in this list — it's
  unwrapped to its wrapped element BEFORE dispatch at the top of
  `Reconciler.Mount`, so it never reaches the switch and does not
  count as a carved arm.)

**Phase 3 completion status (PR #440 — landed-pending-merge):**
Every element type in the production codebase either (a) routes
through V1 dispatch (Phase 1 hand-coded handler OR Phase 3
descriptor registered in `RegisterV1BuiltInHandlers`), (b) is a
Reactor composition primitive intentionally kept above the V1
protocol, or (c) is in the explicit deferred carve list above with
a documented gap-closure path. The A|B parity bar — V1 ON ≡ V1 OFF
across the full xunit + selftest matrix — is met for every
registered element: 9134 xunit + 4410 selftest, 0 failures both
flags. Phase 4 cleanup can delete every legacy `MountXxx` /
`UpdateXxx` method that backs an element that has been registered
through V1; the legacy switch arms for the composition primitives
+ the deferred carve list must remain until their respective
follow-up PRs land.

**Quantified V1 dispatch coverage (post-PR #440):**

| Bucket | Arms | % of total |
|---|---:|---:|
| Routed through V1 (75 = 5 Phase 1 + 6 base-derived + 63 standard descriptors + 1 decorator) | 75 | 79% |
| Reachable-but-deferred (overlays 7, NavigationHost 1, TabView 1, GridView 1, XamlHost/Page 2) | 12 | 12.6% |
| Intentionally above V1 (composition primitives — permanent carve) | 8 | 8.4% |
| **Total `Reconciler.Mount.cs` switch arms** | **95** | **100%** |

- **Coverage of V1-reachable surface (excludes 8 composition primitives):** 75 / 87 ≈ **86%**.
- **Coverage of all switch arms:** 75 / 95 ≈ **79%**.
- **Path to 100% reachable:** the follow-up PR closes the 12 deferred:
  1. Port the 7 overlay descriptors (ContentDialog, Flyout, Popup, MenuBar, MenuFlyout, CommandBar, CommandBarFlyout) — needs a decorator strategy variant for modal lifecycle beyond `IDecoratorElementHandler`.
  2. Refactor `NavigationHostElement` cleanup path so V1 can own it (internal-expose `MountNavigationHost` / `UpdateNavigationHost`, duplicate cleanup logic in the V1 handler, remove the `UnmountRecursive` intercept).
  3. Close `TabViewDescriptor` gaps (engine post-children mount-hook + `ImperativeBridged` for named slots + port `BuildTabHeader` / `BuildPinButton` / `TryUpdatePinHeaderInPlace` + drag pipeline trampolines + conditional `SelectedIndex` write + in-place `CanUpdate`).
  4. Close `GridViewDescriptor` lifecycle gap — either author a Phase 1 hand-coded `GridViewHandler` mirroring `ListViewHandler`'s CCC virtualization, or introduce a `RecyclingItemsHost<>` ChildrenStrategy variant.
  5. Unify `XamlInterop.Register` with V1 auto-registration so `XamlHostElement` / `XamlPageElement` descriptors can register without `EnsureRegistrableElementType` clash.

Phase 4 cleanup (deletion of legacy switch arms + `UseV1Protocol`
flag) is unblocked for the 75 routed arms today; the remaining 12
arms unblock as the follow-up PR lands each closure.

**Carry-forward known defects** (from Phase 1):

- **KD-3** — dispatch fast-path for ported built-ins (M4 +88.9% V1 vs Today).
  Intersects with descriptor shape; address as part of Phase 3 migration.
- **KD-4** — public typed-event surface for external descriptor authors.
  Scope narrowed by Phase 2 to external-author-only (in-tree fast path is
  shipped via `DescriptorControlledPayload<T>` + the new `.HandCodedControlled`
  / `.HandCodedEvent` per-descriptor TPayload pattern).

**Phase 3 exit reopen condition for Q1:** none from the in-tree work. The
only Q1 reopen trigger is source-gen (§7) landing.
