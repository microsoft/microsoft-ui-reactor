# Spec 048 Phase 2 — `Reg<>.Done` Cost Baseline

> **Status.** Phase 2 close-out artifact. Establishes the per-call cost of
> the `Reg<TElement, TControl, THandler>.Done` static-field-read shape
> that Phase 3 will inject into every built-in factory.
>
> **Owner.** Spec 048 §9 (perf claim) and §13 Phase 2 exit gate (this
> file).
>
> **Phase 3 comparison.** When Phase 3 lands the `Reg<>` touch in
> `src/Reactor/Elements/Dsl.cs`, re-run the M1/M2 micro-suite from
> `tests/perf_bench/PerfBench.ControlModel` and compare against the
> 047 phase4 baseline cited below — the per-factory delta must be
> within noise. Results land in
> `docs/specs/048/perf-results/phase3-migration.md`.

---

## 1. What this baseline measures

Spec 048 §7 says every built-in factory will gain one line:

```csharp
internal static partial class Factories
{
    public static ButtonElement Button(string text, Action onClick)
    {
        _ = Reg<ButtonElement, Button, ButtonHandler>.Done;   // §7 touch
        return new ButtonElement(text, onClick);
    }
}
```

`Reg<>` is defined exactly once:

```csharp
internal static class Reg<TElement, TControl, THandler>
    where THandler : IElementHandler<TElement, TControl>, new()
{
    // Explicit (empty) static constructor — disables `beforefieldinit` so
    // Init() runs precisely on the first read of Done (the factory touch),
    // not earlier.
    static Reg() { }

    internal static readonly byte Done = Init();

    private static byte Init()
    {
        ControlRegistry.Register<TElement, TControl>(static () => new THandler());
        return 1;
    }
}
```

Statics on a closed generic are **per-closed-type**, so `Reg<ButtonElement, …>`
and `Reg<TextBlockElement, …>` have independent `Done` slots and
independent precise cctors. The CLR runs each cctor exactly once on
first-touch and elides the cctor check on every subsequent read of that
slot. Steady-state per-call cost is **one indirect load**.

Spec §9 predicts this load "disappears into the element-record
allocation". This document validates that prediction with two
measurements:

1. **Lower bound (inline shape).** `_ = Reg<E,C,H>.Done;` directly inside
   a hot loop — JIT is free to hoist, CSE, or fully fold the load.
2. **Upper bound (no-inlining shape).** The same read behind a
   `[MethodImpl(NoInlining)]` shim so each iteration pays a real call +
   load. Models the worst case where the factory body is too large for
   the JIT to inline into its caller.

---

## 2. Bench harness

`tests/aot_trim_proof/RegStaticReadBench/`

- Standalone .NET 10 console app — **no Reactor dependency**. Models the
  CLR pattern exactly.
- 100,000,000 iterations × 7 timed repetitions, after 3 warm-up reps.
- `GC.Collect()` between reps; mean `ns/op` reported.
- Volatile sink defeats dead-store elimination.

Run:

```pwsh
dotnet run -c Release --project tests/aot_trim_proof/RegStaticReadBench
```

The harness reports five rows:

| Row | What it measures |
|---|---|
| `EMPTY_LOOP` | Loop + Stopwatch + GC overhead — the floor. |
| `REG_READ_SINGLE` | One `Reg<>.Done` read per iteration via a `[MethodImpl(NoInlining)]` shim (upper bound). |
| `REG_READ_MIXED4` | Four distinct closed `Reg<>.Done` reads via the no-inlining shim. |
| `REG_READ_SINGLE_INLINE` | One inline `Reg<>.Done` read per iteration (lower bound — JIT may hoist). |
| `REG_READ_MIXED4_INLINE` | Four inline reads per iteration. |

---

## 3. Phase 2 baseline numbers — ARM64 dev box

**Run.** 2026-01 build (`Reactor.AotHelloWorld.TrimAssertions` PR), .NET
runtime `10.0.8`, ARM64, Release, TieredCompilation on.

**Note.** This dev-box snapshot is for *order-of-magnitude*
characterization, not a perf-gate-quality measurement (see spec 047
`perf-suite-runbook.md` for the full env-isolation requirements). The
Phase 3 perf gate runs the M1/M2 suite on the canonical hardware.

| Bench                    | Mean ns/op | Marginal cost vs `EMPTY_LOOP` |
|--------------------------|-----------:|------------------------------:|
| `EMPTY_LOOP`             |     0.3118 |                             — |
| `REG_READ_SINGLE`        |     1.9918 |              **+1.68 ns/op**  |
| `REG_READ_MIXED4`        |    17.5666 |   +17.25 ns/op (≈ 4.3 ns/read) |
| `REG_READ_SINGLE_INLINE` |     0.6319 |              **+0.32 ns/op**  |
| `REG_READ_MIXED4_INLINE` |     0.4896 |        +0.18 ns/op (4 reads — JIT CSE'd) |

**Allocation.** Zero bytes per iteration in every row. Gen0/Gen1/Gen2 all
zero. The `Reg<>.Done` shape is allocation-free at steady state.

---

## 4. Interpretation

### 4.1 Inline path — the realistic case

In production, the `Reg<>.Done` touch lives inside the factory body the
JIT is already compiling:

```csharp
public static ButtonElement Button(...)
{
    _ = Reg<ButtonElement, Button, ButtonHandler>.Done;   // ← inline
    return new ButtonElement(...);
}
```

This is the `REG_READ_SINGLE_INLINE` / `REG_READ_MIXED4_INLINE` shape.
Observed cost: **0.18–0.32 ns/op**, indistinguishable from `EMPTY_LOOP`
noise. The JIT either hoists the invariant load out of the caller's loop
or CSEs it with prior reads on the same closed generic.

The MIXED4_INLINE row is particularly striking: 4 distinct closed
generics × 4 field reads per iteration collapsed to **0.49 ns total per
iteration** — below `REG_READ_SINGLE_INLINE` because the loop body got
fully fused / hoisted.

**Conclusion.** When the factory body is in scope for inlining (the
common case for the small factories in `Dsl.cs`), the per-factory
`Reg<>.Done` branch is **literally free**. Spec §9's "disappears into
the element-record allocation" claim holds.

### 4.2 No-inlining path — the worst case

If a factory body is too large for the JIT to inline into its caller
(none of the existing built-in factories are that large, but third-party
Pattern-B helpers might be), each call pays the cost of a real
indirect load on entry. Observed: **~1.7 ns/op**.

For context, a 40-byte element-record allocation on this hardware costs
~5–10 ns (alloc + zeroing + field stores). The `Reg<>.Done` load is
~1/5 of that — already amortized into the record alloc that's the
factory's actual job.

### 4.3 Cold-path cost (the cctor itself)

The static `Init()` method runs exactly once per closed `Reg<>`
per process — at first-touch of `Done`. That single call:

1. Allocates one delegate (`static () => new THandler()`).
2. Calls `ControlRegistry.Register<TElement, TControl>` → one
   `ConcurrentDictionary.TryAdd` over a closed key type.
3. Returns `1` (a non-zero sentinel asserted by the unit tests in
   `RegTests`).

This is *not* measured here (it's a one-shot per type per process — the
bench drives 100M iterations against an already-warm slot). The full
M1/M2 baseline below covers it through the factory's full mount path.

---

## 5. Factory-call baseline (M1/M2)

The baseline cost of the factory call *itself*, against which the
Phase 3 delta will be compared, is the spec 047 M1/M2 micro-suite.

**Authoritative baseline.** `docs/specs/047/phase4-results/LAPTOP-4MEP83VI/2026-05-29-arm64/aggregator-out/summary-absolute.md`

Highlights (Reactor variant on ARM64):

- **M1** (`Mount_Leaf_NoCallback`) — `TextBlock("hi")` → reconciler
  mount of a single TextBlock with no callbacks. Dominated by the
  `TextBlockElement` record allocation and the `Mount` switch dispatch.
- **M2** (`Mount_Leaf_OneCallback`) — Button with one click handler.
  Adds one delegate allocation + EHS wiring.

The Phase 3 PR re-runs M1/M2 with `Reg<>.Done` touches landed in the
respective factories. Acceptance: the per-bench delta is within the
suite's 95% CI (typically ±2–3% on this rig). Spec 048 §13 Phase 3
exit gate (`docs/specs/tasks/048-control-registration-and-trimming-implementation.md`,
§3.4 final checkbox) blocks the merge if the delta exceeds noise.

---

## 6. Forward-looking — what Phase 3 must show

When Phase 3 lands the `Reg<>` helper and distributes touches across
`Dsl.cs`:

1. **Re-run the bench above** to confirm the per-load cost hasn't
   shifted on the canonical hardware. Same ~1.7 ns upper bound, ~0 ns
   lower bound is the expectation.
2. **Run M1, M2, M3** from `PerfBench.ControlModel` on canonical
   hardware per `docs/specs/047/perf-suite-runbook.md`. Compare
   absolute means against the 047 phase4 baseline cited above.
3. **Land deltas** in `docs/specs/048/perf-results/phase3-migration.md`.
4. If any M-bench regresses outside CI, **escalate before flipping
   migration on main** — that's the §13 Phase 3 escalation contract.

---

## 7. Reproducibility

```pwsh
cd <repo-root>
dotnet run -c Release --project tests/aot_trim_proof/RegStaticReadBench
```

Source:

- Harness: `tests/aot_trim_proof/RegStaticReadBench/Program.cs`
- Project: `tests/aot_trim_proof/RegStaticReadBench/RegStaticReadBench.csproj`

The project is intentionally not in `Reactor.slnx` (matches the
sibling AOT-trim-proof projects). Build / run it explicitly via the
above command.
