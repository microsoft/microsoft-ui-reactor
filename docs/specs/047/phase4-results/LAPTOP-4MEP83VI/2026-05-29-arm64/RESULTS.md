# Spec-047 Post-Phase-4 Perf Capture — vs 2026-05-25 ARM64 baseline

**Machine:** `LAPTOP-4MEP83VI` (Qualcomm ARMv8, the spec-047 §4.9 baseline box)
**Arch/Runtime:** ARM64-native, Release, .NET 10.0.8 — identical to baseline
**Date:** 2026-05-30 (UTC) · **Branch:** `main` (all of spec-047 incl. Phase 4 merged)
**Suite:** micro M1–M13 (`PerfBench.ControlModel`), reps=5, iters matched to baseline
(M1–M8 @5000, M9 @2000, M10–M13 @1000). 195 rows, 0 errors.

> 📌 **M12 is SUPERSEDED — fix landed.** A post-gate re-capture on `main` at
> `2f4f0c50` (PR #468, `SetElementTagIfNeeded`) shows **M12 Reactor at 968 B/render
> (−12.2% vs baseline, −24.9% vs this capture)**. The M12 cells below are kept
> for historical context. See [`../2026-05-30-arm64-postgate/RESULTS.md`](../2026-05-30-arm64-postgate/RESULTS.md).
> The **M1 regression flagged below has likely also closed** per PR #468's commit
> message (M1 → 980 B/render), but is not independently re-captured yet.

> ⚠️ **Scope caveat — this is an INDICATIVE capture, not the formal §4.9 ratification.**
> The §15.5 environment-isolation requirements (AC power, High-Performance plan, DRR
> off, foreground non-occluded window) could **not** be enforced from this automated
> run, and the harness does not yet implement the §4.9-required randomized/interleaved
> variant ordering + CPU-clock telemetry. **Consequence: the timing (ns) numbers are
> environment-contaminated and must be disregarded** for cross-baseline comparison —
> the `Direct` variant (pure WinUI, *zero* Reactor code) is itself inflated +60–140%
> vs the baseline run, which can only be thermal/power throttling. **The allocation
> (bytes) numbers ARE valid**: managed allocation is deterministic and
> environment-independent — confirmed by `Direct` alloc matching the baseline
> byte-for-byte (M1 Direct = 3,771,824 B in both runs).

---

## Headline findings (allocation — the valid, deterministic axis)

The macro suite (L1–L14: TTFF / working-set / FPS / GC) is **not runnable** — Phase 4
deleted its projects (`StressPerf.ReactorV2`, `BlankReactorV2`). So only the §15.6
micro budgets (per-element alloc M1–M3, dispatch M4–M6, update M7–M8) are covered here.

### 1. §15.6 "M1–M3 per-element alloc must improve/equal Today" — **M1 FAILS**

| Bench | Reactor (new) B/render | Today (base) B/render | Δ vs Today | Verdict |
|---|---:|---:|---:|:--|
| **M1** TextBlock, no callback | **1,289** | 1,071 | **+20.3%** | ❌ **regressed** |
| M2 ToggleSwitch, 1 callback | 3,687 | 3,884 | −5.1% | ✅ improved |
| M3 Button + 2 pointer mods | 8,530 | 9,075 | −6.0% | ✅ improved |

### 2. Phase-4 refactor impact: current `Reactor` vs **baseline `ReactorV2`** (same V1 lineage)

This isolates what the post-baseline Phase-4 work (`ElementExtras` bucketing §4.4,
EHS split §4.3, echo hybrid §4.2) did to the V1 path's allocation:

| Bench | new B/render | base-V2 B/render | Δ | Note |
|---|---:|---:|---:|:--|
| **M1** | **1,289** | 1,077 | **+19.6%** | ❌ leanest leaf got **heavier** |
| M2 | 3,687 | 3,864 | −4.6% | ✅ |
| M3 | 8,530 | 8,633 | −1.2% | ≈ flat |
| M4 | 1,941 | 1,998 | −2.8% | ✅ |
| M5 | 1,948 | 2,212 | −11.9% | ✅ |
| M6 | 888 | 941 | −5.6% | ✅ |
| M7 | 252 | 156 | +61.4% | tiny absolute (+96 B) |
| M8 | 362 | 425 | −14.9% | ✅ |
| **M9** | 184,431 | 312,246 | **−40.9%** | ✅ big win (keyed list) |
| M10 | 3,411 | 3,949 | −13.6% | ✅ |
| M11 | 1,641 | 1,670 | −1.7% | ✅ (per-element state) |
| ~~**M12**~~ | ~~1,273~~ | ~~1,088~~ | ~~**+17.0%**~~ | ✅ **fixed by PR #468 — now 968 B/render (−12.2% vs base)** |
| M13 | 29 | 29 | −0.4% | ≈ flat |

The M1 regression is **deterministic, not noise**: every new rep (6.34–6.51 MB)
sits uniformly above every baseline rep (5.25–5.42 MB) — a consistent ~+235 B/render.
Likely sources to investigate: the added `Element.Extensions` slot on every element,
the §4.3 EHS-split, or the `ReactorState.PendingEchoMatch` slot on the mount path.
M12 (pool rent/return) similarly regressed +17%.
**Update (2026-05-30):** PR #468 (`SetElementTagIfNeeded`) gates the V1 adapter's
`SetElementTag` on `_handler.HasCallbacks`, so callback-free leaves no longer
allocate a `ReactorState` per mount. Post-gate re-capture: M12 Reactor =
**968 B/render (−12.2% vs baseline)** — regression closed. See
[`../2026-05-30-arm64-postgate/RESULTS.md`](../2026-05-30-arm64-postgate/RESULTS.md).

### 3. §11.6 absolute byte-gate (`PerformanceBudgets.cs`) — **M1, M2 FAIL**

| Bench | Target | Reactor (new) B/render | Pass? |
|---|---:|---:|:---:|
| M1 | ≤ 407 | 1,289 | ❌ (3.2×) |
| M2 | ≤ 1,520 | 3,687 | ❌ (2.4×) |
| M3 | ≤ 19,200 | 8,530 | ✅ |

Note the gate targets were defined as `baseline × 0.4`, but the *measured* ARM64
baselines were ~1,077 / 3,864 / 8,633 — so M1/M2 never had a realistic path to
407/1,520 without the deferred KD-3 binder-check fold + further leaf-alloc work, and
M3's 19,200 target was already cleared at baseline. **The byte gates as written are
not met for M1/M2.** This directly confirms the spec's own KD-3 trigger condition
("fold the M1 leading-`if` binder check … if M1 is still above budget after §4.3/§4.4")
— M1 *is* over budget, so that follow-up is now warranted.

---

## Timing (ns) — captured but NOT comparable cross-baseline

Disregard for ratification. Evidence of environment contamination (identical `Direct`
code, new vs baseline ns): M3 +139%, M4 +130%, M5 +60%, M7 +940µs absolute swing.
Within-run `Reactor`-vs-`Direct` overhead is directionally consistent with baseline
(Reactor adds dispatch cost on M1–M6, wins big on M7/M9 via pooling) but the absolute
numbers are throttled and should be re-captured under §15.5 isolation before any
timing-budget sign-off.

---

## Bottom line for §4.9

- ✅ **Build + capture reproducible on the actual ARM64 baseline box**; allocation is
  deterministic and matches baseline `Direct` byte-for-byte.
- ✅ **Most of the V1 path held or improved** vs the captured baseline on allocation
  (M2/M3/M4/M5/M6/M8/M9/M10/M11), with a standout **−41% on M9** (keyed list).
- ❌ **Two allocation regressions to fix before claiming the byte-gate pass:**
  **M1 +20%** (and 3.2× over its 407 B gate) and ~~**M12 +17%**~~ ✅ M12 fixed
  by PR #468 (post-gate: 968 B/render, −12.2% vs base; see
  [`../2026-05-30-arm64-postgate/`](../2026-05-30-arm64-postgate/)). M1 also
  likely closed per PR #468 (commit reports 980 B/render) — needs an
  independent re-capture on this box for sign-off.
- ⛔ **Not a ratification sign-off:** timing axis is environment-throttled, the
  §4.9-mandated randomized/interleaved ordering + CPU-clock telemetry isn't wired,
  and the macro suite (L1–L14) can't run (projects deleted). A real §4.9 close needs
  an isolated stable-AC re-capture (and the macro suite rebuilt against the single
  `Reactor` variant).

_Raw data: `perfbench-controlmodel-{m1-m8,m9,m10-m13}.jsonl` in this folder.
Analysis: `analyze.py`. Baseline: `docs/specs/047/baseline-results/LAPTOP-4MEP83VI/2026-05-25-arm64/`._
