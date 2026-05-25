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

**Harness assertion.** Each macro entry-point performs a z-order check
immediately before the timing phase and aborts if its window is not
top-most-of-its-process or is fully occluded. The check runs again at the
end-of-run boundary; a session that lost foreground state mid-run is flagged
as `WindowOccluded=true` in the JSON-Lines output and excluded from the
comparison emitter.

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

**Harness assertion.** The result-row metadata includes `PowerState`
(AC/Battery) and `GlobalVsyncPerSec` (sampled before and after the timed
section). Rows with `PowerState != AC` are flagged as non-comparable by the
comparison emitter.

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

**Harness assertion.** The JSON-Lines result row stamps the observed
refresh rate captured immediately before the timed section. The comparison
emitter rejects any two rows whose `LockedRefreshHz` differs.

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

**Harness assertion.** Each macro registers for `WTSRegisterSessionNotification`
(or its WinUI equivalent) and aborts on `WTS_SESSION_LOCK`,
`WTS_SESSION_LOGOFF`, `WTS_REMOTE_CONNECT`, or
`WTS_CONSOLE_DISCONNECT` mid-run. The result row is marked
`SessionInterrupted=true`.

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

**Harness assertion.** `PowerPlan` is stamped on every row. Comparison
emitter accepts mixed plans but tags the comparison output as
`PowerPlanMismatch=true`.

---

## 6. Process priority / affinity

**Default:** the test process runs at **normal priority** and **unpinned
affinity**. The benchmarks are designed to measure the steady-state
managed-code path, not an artificially-isolated micro-environment.

**If pinning is used** (only for diagnostic deep-dives), record:

- `ProcessPriority` (Normal / AboveNormal / High).
- `AffinityMask` (hex bitmask of allowed cores).
- Whether the pinning targets P-cores or E-cores explicitly.

The comparison emitter does not reject mismatched priority/affinity rows,
but it tags the comparison output as `PriorityMismatch=true`.

---

## 7. Warm-up policy

**Micro suite (BenchmarkDotNet M1–M13).** BenchmarkDotNet's default warm-up
strategy applies — pilot phase + warmup-count auto-detection. Runs that
complete with high CV (>5%) are flagged and re-run; results not converging
after 3 retries are marked unstable.

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

| Field | Required | Source |
|---|---|---|
| `MachineSku` | yes | hard-coded per `machines.md` entry |
| `Cpu` | yes | `wmic cpu get name` at run start |
| `OsBuild` | yes | `[Environment]::OSVersion.Version` + UBR |
| `DotnetVersion` | yes | `RuntimeInformation.FrameworkDescription` |
| `LockedRefreshHz` | yes | sampled pre-run; rejected if missing |
| `PowerState` | yes | `GetSystemPowerStatus` |
| `MonitorConfig` | yes | `EnumDisplayMonitors` snapshot (count + primary resolution) |
| `WindowOccluded` | yes | z-order check at start and end of timed section |
| `SessionInterrupted` | yes | WTS notification flag |
| `PowerPlan` | yes | `powercfg /getactivescheme` GUID |
| `ProcessPriority` | yes | `Process.PriorityClass` |
| `AffinityMask` | yes (hex) | `Process.ProcessorAffinity` |
| `Timestamp` | yes | ISO-8601 UTC at row write |
| `BenchVariant` | yes | one of `Direct` / `ReactorToday` / `ReactorV2` |
| `Scenario` | yes | one of M1–M13, L1–L11 |
| `Iteration` | yes | 0-indexed; `Warmup=true` rows excluded |
| `Result` | yes | numeric value(s) per scenario contract |

The comparison emitter's first pass rejects rows missing any required
field. The second pass groups rows by `(Scenario, BenchVariant, MachineSku)`
and computes median / p95 within the group. The third pass emits the three
§15.6 tables. Rows whose environment differs (e.g., different
`LockedRefreshHz`) inside a single comparison group are flagged as
**non-comparable** and excluded from the comparison output, with the count
of excluded rows surfaced in the table footer.

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
