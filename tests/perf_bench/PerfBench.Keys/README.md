# Key construction experiment

This benchmark measures the production `WithKey`, `VirtualList`, and `PropertyGrid`
paths, plus representative direct-record construction patterns. It measures
element construction, **not** control reconciliation or frame time.

## Reproduce

Create a separate baseline worktree at the pre-change commit, then run from the
prototype checkout:

```powershell
git worktree add --detach C:\src\reactor-key-base 6f7f398cdeecee9bf13517044052e6095894ef8a
pwsh tests\perf_bench\PerfBench.Keys\Run-Comparison.ps1 `
    -BaselineRoot C:\src\reactor-key-base -OutDir C:\temp\key-micro
```

The runner copies identical benchmark sources into the baseline, refusing to
overwrite different existing files. It builds Release for both trees, alternates
baseline/prototype process order over six pairs, and removes only the source files
it copied. It records source and library hashes, raw samples, medians, and paired
95% confidence intervals in `comparison.json`. Restore/build logs remain in the
output directory. No dependency or machine configuration is changed.

Each process warms every case, then alternates case order over 12 repetitions.
Allocation is `GC.GetAllocatedBytesForCurrentThread`; a static sink makes the
returned records escape so the JIT cannot eliminate them. Tiered compilation is
disabled identically on both sides. Every case validates output before timing.

For actual UI workloads use the existing orchestrator, not this microbenchmark:

```powershell
pwsh tests\stress_perf\ci\Run-PerfBenchmark.ps1 `
    -BaselineRoot C:\src\reactor-key-base -OutDir C:\temp\key-macro `
    -Reps 12 -Warmup 2 -Duration 10 `
    -IncludeMicro:$false -IncludeSkipFloor:$false `
    -IncludeFlex:$false -IncludeRowMemo:$false
```

This runs StocksGrid, keyed-list, and the real DataGrid workload. StocksGrid and
the keyed-list workload already initialize their row keys directly, so they are
controls, not expected beneficiaries. The DataGrid workload exercises the changed
row, grip, and component paths. The original workload sources are left unchanged.

## What the experiment distinguishes

- Formatting a string, copying a record to assign it, and building a native control
  are different costs. Caching a string does not remove a record copy.
- Initializers avoid a copy only when **already constructing a record directly**.
  Do not replace DSL factories with constructors indiscriminately: some factories
  perform lazy handler registration.
- Reapplying an identical key reference now avoids a copy. Fresh unkeyed elements,
  changed keys, and equal-valued but distinct key strings still need copies.
- No new key caches or string interning are introduced. Existing key values,
  formatting, and lifetimes are unchanged. Caller-owned elements must not be
  mutated to eliminate a clone.

Prefer allocation results over tiny nanosecond differences. An unchanged control
that shifts significantly means timing has a systematic/process-layout effect;
a confidence interval alone cannot establish that the product caused that shift.
Do not extrapolate a factory allocation reduction into an FPS claim. Keep the
microbenchmark and live-harness results separate when deciding whether a general
analyzer or code fix is justified.

## Scope

The initial experiment also tried numeric, type, and column-key caches. Those
were removed: allocation savings were small in real chart construction, added
retained memory, and did not establish a live rendering improvement. This change
keeps only clone elimination and same-reference `WithKey` reuse.

An earlier, broader prototype was measured over twelve paired live runs of
StocksGrid, keyed lists, and DataGrid. All live deltas were within noise. Those
results are not an end-to-end benchmark of the narrowed change and do not support
an FPS claim. The focused runner above measures the final implementation directly.

The Advanced DataGrid factory intentionally stays on the public core factory
surface. Its row/grip construction is optimized locally without widening the
approved core-internal dependency boundary.

## Narrowed results (2026-11-17)

Six paired process runs against the baseline above, x64 Release:

| Operation | Baseline B/op | Narrowed B/op |
|---|---:|---:|
| Reapply the same key reference | 160 | 0 |
| `VirtualList(500, ...)` | 280 | 216 |
| `PropertyGrid(...)` | 304.01 | 240.01 |
| Fresh record followed by `WithKey(cachedKey)` | 320 | 320 |

Both component factories remove one 64-byte record copy per call. String
formatting is unchanged. The ordinary fresh-element `WithKey` case remains an
allocation, while redundant assignment of the same key reference avoids it.
