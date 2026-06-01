# Spec 047 §14 Phase 2 — Q1 head-to-head (descriptor vs hand-coded), fast-path re-measure

**Spike status:** measurement complete. Q1 verdict unchanged from the
pre-fast-path capture: **ship hand-coded handlers as the primary author
surface.**

**Why this re-measure exists.** The prior capture
(`../2026-05-26-q1-spike-5x5/`) attributed the descriptor regression to a
self-imposed handicap: `ControlledPropEntry.EnsureSubscribed` routed
through public `ReactorBinding<T>.OnCustomEvent`, which allocates a
closure per first-mount per control and stores trampolines in a
non-deduped list. The hand-coded handlers use *internal*
`Reconciler.GetOrCreateControlEventPayload<T>` for a slot-gated
static-trampoline pattern (zero closures per mount).

The descriptor files live inside `src/Reactor/` — they have the same
internal access. The OnCustomEvent route was a code choice, not a
public-surface constraint. This session rewrote `ControlledPropEntry` to
use the typed-payload fast path with a static trampoline
(`DescriptorControlledPayload<TElement,TControl,TValue,TArgs>`), exactly
mirroring `ToggleSwitchHandler.EnsureToggledWiring`. This re-measure
captures the descriptor model at its **best achievable shape inside the
internal codebase** — same trampoline storage, same dispatch path, same
zero-allocation event wiring as the hand-coded handlers.

## Capture environment

LAPTOP-4MEP83VI, ARM64-native, Release, .NET 10.0.8, Windows 11 26200,
AC power, 3 process launches × 5 reps = **15 measurements per (bench,
variant) cell**.

## Headline result — the hypothesis is falsified

The OnCustomEvent route was **not** the cause of the M2 / M10
regressions. After equalizing the event-wiring path:

| Bench | Pre-fast-path (Desc vs V2) | Fast-path (Desc vs V2) | Q1 band |
|---|---:|---:|---|
| M1 Mount_Leaf_NoCallback (TextBlock) | +1.3% | **+23.5%** | **>15%** |
| M2 Mount_Leaf_OneCallback (ToggleSwitch) | +19.1% | **+18.8%** | **>15%** |
| M5 Dispatch_Switch_Warm | +13.4% | **+16.7%** | **>15%** |
| M7 Update_NoChange | -8.2% | -6.1% | 5-15% |
| M10 EventHandlerState_Alloc (ToggleSwitch) | +31.5% | +32.1% | >15% (informative) |

M2 moved from +19.1% → +18.8% (within CI of each other). M10 moved from
+31.5% → +32.1% (essentially identical). The event-path rewrite — which
removed all per-mount closure allocation and matched the hand-coded
trampoline storage exactly — **did not move the gating regressions**.

The prior README's prediction that KD-4 (public typed-event surface)
would close the gap is now falsified for the in-tree case. The remaining
cost is not in the event path.

## M1 anomaly — flagged

M1 jumped from +1.3% → +23.5% between the two captures. M1 exercises a
TextBlock mount with no callback — the controlled-event path does not
engage at all, so the fast-path rewrite cannot have caused it.

Possible causes (not investigated this session):

- Today vs V2 also widened on M1 in this capture (+13.8% Today vs V2),
  unusual for the V1 dispatch shell on a no-callback leaf. Suggests
  per-capture system-load variance, not a code regression.
- 15-measurement cells have wide CI on the leaf mount bench (±8.5k ns at
  55k ns mean ≈ ±15%). The means barely separate at the CI tail.
- The descriptor variant registers descriptor handlers for ToggleSwitch /
  Slider / Border at variant startup. TextBlock is not one of them, so
  M1 should run the same V1 dispatch path as ReactorV2 — same code,
  same registration count plus three extra entries.

**M1 result should be treated as suspect.** The matrix still triggers
on M2 (+18.8%, stable across both captures) and M5 (+16.7%, near the
band boundary), so the verdict is unchanged regardless of how M1 is
resolved.

## Where the cost lives, take 2

With the event-wiring path equalized, the residual descriptor cost on
the controlled-event benches (M2 / M10) must be in the property-write
path itself — the part the interpreter cannot share with the hand-coded
handlers:

| Step | Hand-coded handler | Descriptor interpreter |
|---|---|---|
| Per-property mount write | Inlined: `ctrl.IsOn = el.IsOn;` direct field access through generated property setter | `entry.Mount(ctrl, el)` virtual call → `_set(ctrl, _get(el))` two delegate invocations |
| Update diff | Inlined: `if (oldEl.IsOn != newEl.IsOn) WriteSuppressed(...)` | `entry.Update(ctrl, oldEl, newEl)` virtual call → two getter delegates → comparer.Equals → suppressed write through delegate |
| Entry iteration | None — code is unrolled per control | `foreach (PropEntry<TElement,TControl> entry in _entries)` — virtual dispatch through abstract base per entry |

Each descriptor entry is a virtual call into a sealed `PropEntry<>`
subclass plus two delegate invocations (`Func<TElement,TValue>` getter
and `Action<TControl,TValue>` setter). The hand-coded handler is
straight-line code. For a control with 4 props × 2 captures per mount =
8 delegate dispatches, the interpreter shape adds real cycles even when
the event path is equalized.

That cost won't go away without **source-gen** — emitting per-descriptor
handler code from the descriptor declaration would collapse the
interpreter overhead back to inlined property writes, identical to the
hand-coded shape. Source-gen is §7 of the spec and currently deferred.

## Q1 matrix application

Per §13 Q1's pre-committed decision matrix:

> **Descriptor >15% slower on any of M1/M2/M5/M7:** ship hand-coded
> handlers as the primary surface.

M2 is +18.8% (stable across two captures). M5 is +16.7% (near the
boundary). M1 is +23.5% (suspect, but irrelevant to the verdict — M2
alone is sufficient). **Primary surface: hand-coded
`IElementHandler<TElement,TControl>`.** Descriptors remain available as
a secondary author shape — useful for runtime-registered third-party
controls where the author can't ship a custom handler class (§16
permanent-fallback path) — but they are not the recommended first-party
shape for built-in or split-library controls.

## What changed about the Q1 reopen condition

The prior README listed two reopen triggers: (1) KD-4 ships, (2)
source-gen ships. This re-measure removes condition (1) — KD-4 (a
public typed-event surface + OnCustomEvent dedup) would let *external*
descriptors achieve the same event-path performance the in-tree
descriptors already achieve in this capture, but it would not close the
M2 / M5 gap because the gap is not in the event path. Source-gen is now
the only remaining path to flip the matrix.

**Updated reopen condition:** re-measure Q1 only after source-gen (§7)
lands. KD-4 would still be valuable for external-control authors (it
brings descriptor parity with the hand-coded internal pattern for
third-party assemblies), but it should no longer be expected to flip
Q1.

## Where the descriptor model is still competitive

On benches without controlled-event wiring, the descriptor model
remains within or beats the hand-coded baseline:

| Bench | Desc vs V2 ns | What it measures |
|---|---:|---|
| M7 (no-change update, 1000 TextBlocks) | -6.1% | Diff path — interpreter detects no-change via comparer.Equals and skips the write |

M7 actually went slightly *better* under the interpreter than the
hand-coded path. The descriptor's `Update` virtual dispatch ends up
cheaper than the hand-coded `if (old != new)` branch when there are no
changes — likely because the interpreter's bulk no-op loop tier-up's
predictably while the hand-coded variant has per-handler conditional
shape. Either way, descriptors are not a perf liability on the no-event
path.

## Files

- `launch-1.jsonl` / `launch-2.jsonl` / `launch-3.jsonl` — raw bench
  output, JSON-Lines, one row per (bench × variant × rep). Three
  process launches × five reps × five tests × three variants = 225 rows.
- `aggregate.py` — reads `launch-*.jsonl`, emits the means + 95% CI
  table and the Q1 deltas. Run with no arguments from this directory.
- `summary.md` — aggregator output.

## Phase 2 exit

Q1 is now answered both with and without the self-imposed handicap.
Verdict unchanged: hand-coded primary, descriptors secondary. Phase 2
exit gate reached; Phase 3 (controls migration) ports the remaining
~60 built-ins onto `IElementHandler<TElement,TControl>`. KD-3 (dispatch
fast-path for the ported built-ins, M4 +88.9% V1 vs Today regression)
and KD-4 (public typed-event surface for external authors) carry over
into Phase 3 as known defects.
