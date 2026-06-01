# Spec 047 §14 Phase 2 — Q1 head-to-head (descriptor vs hand-coded), stable-AC re-measure

**Spike status:** measurement complete. **Q1 verdict flips: descriptors
now land in the 5-15% "judgment call" band, not the >15% "ship
hand-coded" band the prior captures indicated.** No gating bench
exceeds 15%; M2 (the worst gating bench) is +9.6%.

## Why this re-measure exists

The two prior captures both gave a "ship hand-coded" verdict triggered
by M2 (+19.1%, +18.8%) and a suspicious M1 anomaly in the second
capture (+23.5% on a TextBlock that doesn't even engage the descriptor
path). The user flagged that those runs may have been on degraded power
or thermal conditions. This capture was taken on **stable AC, foreground
window, no other load**, same EXE / same code as the prior fast-path
capture, three process launches × five reps = 15 measurements per
cell.

## Capture environment

LAPTOP-4MEP83VI, ARM64-native, Release, .NET 10.0.8, Windows 11 26200,
**AC power confirmed stable, foreground window, other apps closed**,
3 process launches × 5 reps = **15 measurements per (bench, variant)
cell**.

## Headline result

| Bench | Pre-fastpath (5×5) | Fastpath noisy (3×5) | **Stable-AC (3×5)** | Q1 band (stable) |
|---|---:|---:|---:|---|
| M1 Mount_Leaf_NoCallback | +1.3% | +23.5% | **-1.0%** | **≤5%** |
| M2 Mount_Leaf_OneCallback | +19.1% | +18.8% | **+9.6%** | **5-15%** |
| M5 Dispatch_Switch_Warm | +13.4% | +16.7% | **-2.3%** | **≤5%** |
| M7 Update_NoChange | -8.2% | -6.1% | **+8.1%** | **5-15%** |
| M10 EventHandlerState_Alloc | +31.5% | +32.1% | +19.3% | >15% (informative) |

**No gating bench (M1 / M2 / M5 / M7) exceeds 15%.** The matrix's "ship
hand-coded" trigger does not fire on this capture.

The prior captures' verdict ("ship hand-coded") was a measurement
artifact of unstable capture conditions. With stable AC, the fast-path
descriptor model performs within or near the hand-coded baseline on
three of four gating benches (M1 -1.0%, M5 -2.3%, M7 +8.1%) and shows
a real but bounded cost on M2 (+9.6%, the one bench that exercises
both the descriptor's property-write path *and* its controlled-event
wiring under mount pressure).

M10 (informative-only per §13 Q1) shows a +19.3% delta on the
event-heavy bench, down from +31.5% / +32.1%, confirming the fast-path
fix did help — it was just being washed out by capture noise in the
prior runs.

## What the fast-path fix actually bought

Comparing pre-fast-path (5×5) to stable-AC (3×5) for the two benches
that exercise the controlled-event wiring:

| Bench | Pre-fastpath | Stable-AC | Improvement |
|---|---:|---:|---:|
| M2 | +19.1% | +9.6% | -9.5pp |
| M10 | +31.5% | +19.3% | -12.2pp |

The fast-path rewrite (typed payload + static trampoline, matching
`ToggleSwitchHandler.EnsureToggledWiring`) cut roughly **half** of the
descriptor's controlled-event overhead — that was the per-mount closure
allocation plus the CWT lookup plus the non-deduped trampoline list.
The remaining +9.6% / +19.3% is the residual interpreter cost: virtual
`PropEntry.Mount` dispatch plus delegate getter/setter invocations vs
the hand-coded handler's inlined property writes.

## Q1 matrix application — judgment call band

Per §13 Q1's pre-committed decision matrix:

| Band | Verdict | Triggered on this capture? |
|---|---|---|
| ≤5% on all M1/M2/M5/M7 | Ship descriptors as primary | No (M2 / M7 exceed 5%) |
| **5-15% on any gating bench** | **Judgment call — weigh LOC + readability** | **Yes (M2 +9.6%, M7 +8.1%)** |
| >15% on any gating bench | Ship hand-coded as primary | No |

**This capture lands the decision in the judgment-call band.** The
spec pre-committed to weighing LOC + readability when this band
triggers. The data needed to make that judgment:

### LOC, this codebase

For the three ported controls (`ToggleSwitch`, `Slider`, `Border`):

| Shape | Per-control LOC (avg) | Total for 3 controls | Amortized infra |
|---|---:|---:|---:|
| Hand-coded `IElementHandler<,>` | 100 lines (305 / 3) | 305 lines | 0 (no shared interpreter) |
| Descriptor + interpreter | 66 lines (198 / 3) | 198 lines | 586 lines (one-time) |

Break-even point: at N where `100N = 66N + 586` → **N ≈ 17 controls**.
Phase 3 ports the remaining ~60 controls. At N=60 the descriptor shape
saves ~24% total LOC (4,546 vs 6,000 lines).

Below ~17 controls, hand-coded wins on LOC; above, descriptors win.
**Phase 3 is well above the break-even**, so descriptors win on LOC for
the spec's actual scope.

### Readability

Subjective, but worth recording:

- **Descriptor declarations** (`ToggleSwitchDescriptor.cs` is 60 lines)
  read like spec tables: one entry per property, classification
  visible at the call site (`OneWay` / `Initial` / `Controlled` /
  `CoercingOneWay` / `OneWayConditional`). The §6.1 prop classification
  is enforced by the type system — you can't accidentally write a
  one-way prop with an echo guard.

- **Hand-coded handlers** (`ToggleSwitchHandler.cs` is 101 lines)
  open-code the per-property mount/update logic, the trampoline
  storage, the echo suppression scope, and the wiring slot. The shape
  is repeated across all hand-coded handlers; the §6.1 classification
  is implicit in how each property is wired rather than declared.

For external authors (third-party controls in §16-permanent-fallback
territory) the descriptor shape is dramatically easier to ship
correctly — they don't need to understand the trampoline-storage
internals to wire an event. For in-tree built-ins, the readability
tradeoff is closer; descriptors still win on consistency but
hand-coded gives more freedom to inline weird per-control logic.

### Recommendation

**Descriptors as primary, hand-coded as escape hatch.** The matrix's
judgment-call band lets the LOC + readability win drive the decision,
and at Phase 3's scope (~60 controls) the LOC win is decisive. M2's
+9.6% is a real cost but bounded and below the matrix's hard-no
threshold; for ToggleSwitch-density UIs that mount handfuls of
interactive controls per route, that's microseconds of additional mount
cost. M10's +19.3% informative-only delta should be tracked but does
not gate the verdict per §13 Q1.

The escape hatch (hand-coded `IElementHandler<,>`) stays available for:

- Controls with truly weird per-control logic that doesn't fit the §6.1
  prop classification (the descriptor model intentionally constrains
  the shape).
- Controls where the +9.6% / +19.3% mount cost matters — e.g.,
  high-frequency mount/unmount scenarios identified during Phase 3.
- KD-3 follow-up: dispatch fast-path for ported built-ins (M4 +88.9% V1
  vs Today regression from Phase 1) may want to stay hand-coded.

This recommendation is a recommendation, not a commitment — the §13 Q1
judgment-call band explicitly puts the call on the human, not on the
matrix. If the project prefers hand-coded for predictability or for
the M10 informative cost, that's a valid call under the matrix.

## Where the residual cost lives (unchanged from prior README)

After the fast-path fix, the descriptor's event-wiring shape matches
the hand-coded handlers byte-for-byte. The residual +9.6% on M2 is
intrinsic interpreter cost:

| Step | Hand-coded | Descriptor |
|---|---|---|
| Per-property mount | Inlined: `ctrl.IsOn = el.IsOn;` | `entry.Mount(ctrl, el)` virtual call → `_set(ctrl, _get(el))` two delegate invocations |
| Update diff | Inlined: `if (oldEl.IsOn != newEl.IsOn) WriteSuppressed(...)` | `entry.Update(ctrl, oldEl, newEl)` virtual call → two getter delegates → comparer.Equals → suppressed write |
| Entry iteration | None — code is unrolled per control | `foreach (PropEntry<TElement,TControl> entry in _entries)` — virtual dispatch through abstract base |

This cost is removable only via **source-gen** (§7, deferred). A
generator could emit per-descriptor handler code that inlines the
property writes, collapsing the interpreter overhead to zero. If
descriptors ship as primary and source-gen lands later, the residual
cost goes to noise and M10 with it. That's the long-term shape; this
capture is the bake-off against the no-source-gen baseline.

## Reopen condition

Re-measure Q1 only if:

1. **Source-gen lands** (§7, currently deferred) — expected to collapse
   the +9.6% / +19.3% residuals to noise.
2. **Phase 3 surfaces a control with much higher residual cost** than
   the three measured here — e.g., a property-dense or event-dense
   control where the per-entry overhead amplifies. If discovered,
   re-measure with that control included.

The prior README's reopen-condition #1 (KD-4 ships) is downgraded:
KD-4 is still valuable for external descriptor authors who don't have
internal access, but the in-tree fast path already exists and KD-4 will
not move the in-tree Q1 numbers.

## Files

- `launch-1.jsonl` / `launch-2.jsonl` / `launch-3.jsonl` — raw bench
  output. 225 rows total.
- `aggregate.py` — reads `launch-*.jsonl`, emits means + 95% CI and Q1
  deltas. Run with no args from this directory.
- `summary.md` — aggregator output.

## Captures index

- `../2026-05-26-q1-spike-5x5/` — pre-fast-path 5×5 capture. Verdict:
  ship hand-coded. **Superseded by this capture** for the in-tree
  descriptor measurement, but still authoritative for what the
  *external-author* descriptor shape (using public `OnCustomEvent`)
  costs. Useful when sizing KD-4's expected benefit.
- `../2026-05-26-q1-fastpath-3x5/` — fast-path 3×5 capture, noisy.
  Verdict at the time: ship hand-coded. **Superseded by this capture.**
  Kept for the falsification narrative — M1 +23.5% was confirmed to be
  capture-condition noise (this capture shows M1 -1.0%).
- `./` (this dir) — fast-path 3×5 capture, stable AC. **Authoritative
  for the in-tree Q1 verdict.**

## Phase 2 exit

Q1 answered: judgment-call band, recommendation = descriptors as
primary at Phase 3 scope. Phase 2 exit gate reached. Phase 3 (controls
migration) ports the remaining ~60 built-ins. KD-3 (dispatch fast-path
for ported built-ins, M4 +88.9% Phase 1 regression) and KD-4 (public
typed-event surface for external descriptor authors) both carry over
into Phase 3 as known defects, with KD-4 now scoped to external authors
rather than in-tree.
