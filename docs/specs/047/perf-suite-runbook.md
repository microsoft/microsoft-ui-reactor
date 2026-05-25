# Spec 047 Perf-Suite Operator Runbook — Phase 0 §14 Deliverable 3.6

Spec 047 §15.5 environment-isolation requirements, as an operational
checklist. Both the micro suite (`tests/perf_bench/PerfBench.ControlModel`,
M1–M13) and the macro suite (`tests/stress_perf/StressPerf.ReactorV2`,
`tests/startup_perf/BlankReactorV2`, L1–L11) require these conditions.

> **Cross-reference:** Two prior incidents are encoded as memory entries —
> `stress_perf benchmark window-throttling gotcha` and `stress_perf
> battery / Dynamic Refresh Rate`. Both describe the same family of
> environment-side measurement traps. Future operators should land at the
> same source of truth — keep this runbook in sync.

> **Implementation status (Phase 0).** Operator-side requirements are
> enforced **manually** at Phase 0. The harness stamps only the core
> identifying fields (`MachineSku`, `Cpu`, `OsBuild`, `DotnetVersion`,
> `Architecture`, `Configuration`) and the aggregator rejects rows missing
> any of those. Every "Harness assertion" paragraph below describes the
> **planned** automated check for Phase 1 — at Phase 0 the operator is
> the harness. Sections explicitly tagged 🟢 are wired; sections tagged
> 🟡 are planned (the runbook section still applies, just enforced by
> hand).

---

## 1. Window state — foreground + non-occluded

**Why.** `tests/stress_perf` macros use `CompositionTarget.Rendering` to count
FPS; DWM stops firing rendering callbacks at full rate when a window is
occluded or on an inactive virtual desktop. Additionally, after ~10–15 s of
non-foreground state, Windows applies `PROCESS_POWER_THROTTLING_EXECUTION_SPEED`
and pins threads to E-cores. Observed degradation: ~1.85× FPS drop, ~1.47×
reconcile-time inflation. Sharp transition at ~13 s with a sustained plateau
afterward is the signature.

**Operator action.**

- Keep the bench window visible and on the active virtual desktop for the
  full duration of every run.
- Do **not** switch virtual desktops or alt-tab away mid-run.
- Do **not** RDP into or out of the test machine while a run is in progress.

**Harness assertion (🟡 planned).** Phase 1 wires a z-order check
immediately before the timing phase that aborts if the window is not
top-most-of-its-process or is fully occluded, plus an end-of-run check
that stamps `WindowOccluded=true` on any row that lost foreground state
mid-run. At Phase 0 the operator confirms manually before launching.

**Alternative for headless runs.** Call
`SetProcessInformation(ProcessInformationClass::ProcessPowerThrottling, …)`
at startup with `PROCESS_POWER_THROTTLING_EXECUTION_SPEED` disabled. This
fixes the EcoQoS-driven reconcile-time inflation but does **not** restore
DWM compositing for occluded windows. The macro suite therefore still
requires a real foreground window.

---

## 2. Power source — AC only

**Why.** Windows 11 Dynamic Refresh Rate (DRR) lowers the panel refresh
when GPU activity is low — and the threshold depends on what app is running.
A WPF surface gets ~29 Hz at 50% activity on battery; a Direct2D surface
gets ~45 Hz at the same activity. The display refresh becomes a function of
the app under test rather than a fixed environmental constant.

**Operator action.**

- Run every result row on **AC power**. DRR mostly pins to display max on AC.
- Battery and AC numbers are **separate baselines**. They are not diff-able.

**Harness assertion (🟡 planned).** Phase 1 stamps `PowerState`
(AC/Battery) and `GlobalVsyncPerSec` on every row and the aggregator
flags `PowerState != AC` rows as non-comparable. At Phase 0 the operator
confirms AC before launching.

---

## 3. Display refresh — fixed, DRR disabled

**Why.** Even on AC, some panels are configured for variable refresh by
default. A run that ticks across a DRR transition produces unstable
FPS data.

**Operator action.**

- Disable Dynamic Refresh Rate for the session: **Settings → Display →
  Advanced display → Choose a refresh rate** — pick the panel's max rate, not
  "Dynamic."
- Record the locked refresh rate in the run metadata (`LockedRefreshHz`).
- Confirm via `Get-DisplayInfo` / `dwm-attribution-test` (see
  `tests/stress_perf/METHODOLOGY.md`) that the panel is reporting the locked
  rate during a 5-second warm-up.

**Harness assertion (🟡 planned).** Phase 1 stamps `LockedRefreshHz`
captured immediately before the timed section and the aggregator rejects
mismatched rows. At Phase 0 the operator records the refresh rate
manually in `machines.md` per machine entry.

---

## 4. Session state — no virtual-desktop or projection switches

**Why.** Beyond foreground/occluded state, an explicit virtual-desktop or
projection-mode switch invalidates the timed section entirely (DWM
recreates compositing context, the process power state is re-evaluated,
and CompositionTarget callbacks pause and resume).

**Operator action.**

- Lock the session before kicking off a long-running macro.
- Do not extend / mirror displays mid-run.
- Console session only; no RDP-in or RDP-out during a run.

**Harness assertion (🟡 planned).** Phase 1 wires
`WTSRegisterSessionNotification` and aborts on `WTS_SESSION_LOCK`,
`WTS_SESSION_LOGOFF`, `WTS_REMOTE_CONNECT`, or `WTS_CONSOLE_DISCONNECT`
mid-run, marking the result row `SessionInterrupted=true`. L5 / L11
specifically gate on this being wired. At Phase 0 the operator owns
session integrity manually.

---

## 5. Power plan — High Performance (or documented alternative)

**Why.** The default "Balanced" power plan throttles CPU below 100% even on
AC, especially after 30+ seconds of sustained load. This bites macro runs
L11 (`LongLived_HeapStability`, 30-minute session) hardest.

**Operator action.**

- Set the power plan to **High Performance** (`powercfg /setactive
  8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c`).
- If a non-High-Performance plan is required for some reason, record the
  plan name in the run metadata (`PowerPlan` field).

**Harness assertion (🟡 planned).** Phase 1 stamps `PowerPlan` on every
row and the aggregator tags `PowerPlanMismatch=true` when rows in the
same comparison group differ. At Phase 0 the operator confirms manually.

---

## 6. Process priority / affinity

**Default:** the test process runs at **normal priority** and **unpinned
affinity**. The benchmarks are designed to measure the steady-state
managed-code path, not an artificially-isolated micro-environment.

**If pinning is used** (only for diagnostic deep-dives), record:

- `ProcessPriority` (Normal / AboveNormal / High).
- `AffinityMask` (hex bitmask of allowed cores).
- Whether the pinning targets P-cores or E-cores explicitly.

**Harness assertion (🟡 planned).** Phase 1 stamps `ProcessPriority`
and `AffinityMask` and tags `PriorityMismatch=true` for mismatched
groups. Phase 0 leaves both at their defaults and does not stamp.

---

## 7. Warm-up policy

**Micro suite (Phase 0 — custom `BenchRunner`, M1–M13).** Phase 0 uses
a dependency-light custom runner (`tests/perf_bench/PerfBench.ControlModel/Variants/BenchRunner.cs`),
not BenchmarkDotNet. Policy:
- `Iterations = 10000`, `Repetitions = 5`, `WarmupReps = 2` (defaults;
  overridable via `--iterations` / `--reps` CLI).
- Each timed repetition starts with a forced `GC.Collect()` →
  `WaitForPendingFinalizers()` → `GC.Collect()` so per-rep allocation /
  GC counts are attributable to the timed window.
- Mean ns + Gen0/Gen1/Gen2 counts + allocated bytes + heap delta are
  emitted per repetition. The aggregator computes 95% CI (z=1.96 ×
  stderr) across the 5 reps.

Adopting BenchmarkDotNet's pilot-phase / CV-aware warmup is **planned
for Phase 1** alongside L7–L9 (the FPS-sensitive macros where instability
detection matters more).

**Macro suite (L1–L11).**

- 3-iteration warm-up before the first timed iteration. Warm-up results
  appear in the JSON-Lines stream tagged `Warmup=true` and are excluded from
  the comparison emitter's medians.
- Cold and warm timings are both reported (3+ reps each).
- Median and p95 are reported across the 3+ warm reps. Single-rep p95
  is not computed (the comparison emitter requires ≥3 reps).

---

## 8. Run-metadata schema

Every JSON-Lines result row stamps the following (per §15.5). Any row
missing a required field is rejected by the comparison emitter.

| Field | Phase 0 status | Source |
|---|---|---|
| `MachineSku` | 🟢 stamped | hard-coded per `machines.md` entry |
| `Cpu` | 🟢 stamped | `RuntimeInformation.ProcessorArchitecture` + envvar |
| `OsBuild` | 🟢 stamped | `Environment.OSVersion.Version` |
| `DotnetVersion` | 🟢 stamped | `RuntimeInformation.FrameworkDescription` |
| `Architecture` | 🟢 stamped | `RuntimeInformation.OSArchitecture` |
| `Configuration` | 🟢 stamped | `Debug` / `Release` from build symbol |
| `LockedRefreshHz` | 🟡 planned (Phase 1) | sampled pre-run from DXGI |
| `PowerState` | 🟡 planned (Phase 1) | `GetSystemPowerStatus` |
| `MonitorConfig` | 🟡 planned (Phase 1) | `EnumDisplayMonitors` snapshot |
| `WindowOccluded` | 🟡 planned (Phase 1) | z-order check start + end |
| `SessionInterrupted` | 🟡 planned (Phase 1) | WTS notification flag |
| `PowerPlan` | 🟡 planned (Phase 1) | `powercfg /getactivescheme` GUID |
| `ProcessPriority` | 🟡 planned (Phase 1) | `Process.PriorityClass` |
| `AffinityMask` | 🟡 planned (Phase 1) | `Process.ProcessorAffinity` |
| `BenchVariant` | 🟢 stamped | one of `Direct` / `ReactorToday` / `ReactorV2` |
| `Scenario` (`BenchId` + `BenchName`) | 🟢 stamped | one of M1–M13, L1–L11 |
| `Iteration` | 🟢 stamped | 0-indexed; warmup reps not emitted |
| `Result` (`MeanNs`, `AllocBytes`, `Gen0/1/2`, `HeapDeltaBytes`) | 🟢 stamped | per-repetition |

**At Phase 0** the aggregator rejects rows missing any of the 🟢 fields
(`MachineSku` / `Cpu` / `OsBuild` / `DotnetVersion` / `Architecture`),
groups by `(BenchId, BenchVariant, Architecture)` so ARM64-native vs
x64-emulated runs cannot silently mix, and computes 95% CI from the
5 reps within each group. The full LockedRefreshHz / WindowOccluded
rejection pipeline lands in Phase 1.

---

## 9. Pre-flight checklist

Before kicking off any baseline-quality run:

- [ ] AC power confirmed; battery icon shows charging.
- [ ] Power plan set to High Performance.
- [ ] Display refresh locked to panel max (DRR off).
- [ ] All other GUI apps closed; only the bench window is visible.
- [ ] Defender / antivirus exclusion verified for the test process tree
      (real-time scanning during heap-sampling skews L11 results).
- [ ] No backup, sync, or update tasks scheduled to run during the window.
- [ ] If running L5 / L11 (long-lived): laptop lid will stay open, no
      sleep / hibernate schedule active.
- [ ] Machine SKU entry exists in
      `docs/specs/047/baseline-results/machines.md`.

---

## 10. Known machine quirks (extend as encountered)

This section is empty at Phase 0 freeze; the first published baselines may
expose machine-specific gotchas (e.g., a particular ARM64 Surface whose
DRR can't be locked, a workstation whose Defender exclusions need a reboot
to take effect). Add entries here so the next operator inherits the
work-around.
