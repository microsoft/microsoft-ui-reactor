# Perf comparison (`/perf`) — CI workflow + local runner

On-demand benchmarking for the Reactor data-grid stress harness. The same
PowerShell entry point powers two things:

- **In CI** — comment **`/perf`** on a pull request and
  [`.github/workflows/perf-compare.yml`](../../../.github/workflows/perf-compare.yml)
  builds the harness on the **PR head** and on **`main`**, runs them
  interleaved on one runner, and posts a sticky comparison comment.
- **Locally** — run [`Run-PerfBenchmark.ps1`](Run-PerfBenchmark.ps1) yourself to
  get your four numbers (and an optional A/B against a clean `main` worktree)
  before you ever push.

> There is also a Copilot skill — [`perf-compare`](../../../.github/skills/perf-compare/SKILL.md) —
> that drives the local runner for you. Just ask Copilot to "benchmark my perf
> changes vs main".

## The four metrics

Measured on the `StressPerf.ReactorOptimized` StocksGrid workload (Release, built for the host architecture — x64 on the CI runner):

| Metric | Meaning | Direction |
|---|---|:--:|
| **Renders/sec** | `Total Renders` ÷ `Duration` — render throughput | higher is better ↑ |
| **Avg Reconcile (ms)** | mean reconcile-phase time per render | lower is better ↓ |
| **Avg Diff (ms)** | mean element-tree diff time per render | lower is better ↓ |
| **Avg Memory (MB)** | mean working set during the measured window | lower is better ↓ |

Imperative WinUI3 (`StressPerf.Direct`) has no virtual-DOM, so it has **no**
reconcile/diff phase — those cells read *n/a*.

### Allocation metrics (Reactor, lower is better)

The mean-ms and working-set numbers above are largely **blind to
allocation-reduction work** — a PR that removes per-render allocations often
moves them < 1% while cutting allocations by double digits. So the comment also
reports a Reactor-only allocation table, captured by `PerfTracker` over the
measured render loop:

| Metric | Meaning | Direction |
|---|---|:--:|
| **Alloc bytes/render** | managed bytes allocated per render (`GC.GetTotalAllocatedBytes` Δ ÷ renders) | lower is better ↓ |
| **Gen0 GC / 1k renders** | Gen0 collections per 1,000 renders (`GC.CollectionCount(0)` Δ) | lower is better ↓ |

These read *n/a* for a PR head whose harness sources predate the metric — rebase
onto `main` to populate them. They are the **sensitive signal** for the
allocation-focused PRs in the current fleet.

### Reconciler micro-benchmarks (ns-resolution, WinUI-undiluted)

The StocksGrid macro workload above is **render-bound and working-set diluted**:
renders/sec is ~76% gated by the WinUI render thread, and the reconcile/diff ms
and alloc figures are measured across a live render pipeline. That makes it a poor
instrument for the **Core/Reconciler** layer most of the perf fleet actually
operates on — small reconcile-time and per-reconcile allocation deltas are buried
under render + GC noise.

So `/perf` also runs the **`PerfBench.ControlModel` micro-suite** (spec-047
M1–M13: `PropertyDiff` / `Allocation` / `StructuralSharing` / `ControlModel`) once
per side and reports a per-bench PR-vs-`main` table:

| Metric | Meaning | Direction |
|---|---|:--:|
| **ns/op** | mean reconcile time per operation, `Stopwatch` over the loop body only | lower is better ↓ |
| **B/op** | managed bytes allocated per operation (`GC.GetAllocatedBytesForCurrentThread` Δ, **per-thread** so it excludes WinUI/background allocs) | lower is better ↓ |

It runs the production **`--variant Reactor`** path as a headless loop whose
measured region brackets only the reconciler — no render pipeline, ns-resolution,
free of working-set dilution. `main` and the PR each link their **own
`src/Reactor` build** (via the project's relative `ProjectReference`), so the
delta is a clean read on the code change. Both deltas use the **same paired 95% CI**
machinery as the macro tables, but they are **read differently**:

- **B/op drives the row flag.** Allocated bytes/op is *deterministic* for identical
  code — an unchanged diff reproduces the same byte count exactly — so its paired CI
  is trustworthy. The ✅/⚠️/≈ status of each row tracks the alloc delta.
- **ns/op is informational only** (shown, never auto-flagged in v1). The two per-side
  runs are not yet rep-interleaved, so a systematic process-to-process timing offset
  (thermal/scheduling drift between the back-to-back invocations) shifts every paired
  ns difference the same way and makes the paired CI exclude 0 even for an identical
  binary. Local validation confirmed this: running the **same** ControlModel binary
  as both "main" and "PR", alloc was deterministic (14/16 benches exactly 0.0% Δ) but
  ns spuriously flagged up to −14.8% on a no-op. Flagging ns would emit false
  improvements/regressions, so the column is reported for context and excluded from
  the flag. **Rep-level interleaving of the two sides is the documented fast-follow**
  that would let ns be promoted to a flagged signal.

The whole leg is best-effort: if the micro build or run fails, the macro comment is
unaffected and the section is simply omitted.

## Prerequisites

- **Windows** with a real interactive desktop session (the harness opens a real
  WinUI window — see [Troubleshooting](#troubleshooting)).
- **.NET 10 SDK** (`dotnet --version` ≥ 10).
- **PowerShell 7+** (`pwsh`). The scripts use pwsh-7 syntax (`??`, `(if …)`
  sub-expressions) and will not run under Windows PowerShell 5.1.
- **No Windows App SDK runtime install needed.** The runner builds the harness
  with `WindowsAppSDKSelfContained=true` (via the gated `-p:PerfCiSelfContained=true`
  property), the same hermetic trick the WinUI selftest hosts use, so the
  bundled runtime ships next to the exe. Pass `-SelfContained:$false` to use a
  machine-wide runtime instead.
- **Any architecture.** The harness builds for your **host architecture** by
  default (`x64` or `ARM64`), so it runs natively. This matters on ARM64 boxes:
  an x64 build there runs under emulation and crashes WinUI composition with a
  stowed exception (`0xC000027B`). Override with `-Platform x64|ARM64` if needed.

## Run it locally

### Quick: my four numbers right now

```pwsh
pwsh tests/stress_perf/ci/Run-PerfBenchmark.ps1
```

Builds + runs `StressPerf.ReactorOptimized` **and** `StressPerf.Direct`
(vanilla WinUI3) in the current checkout, prints a console table, and writes
`tests/stress_perf/ci/out/result.json`. Both build for your host architecture.
Add a live Rust `windows-reactor` column with `-RustRepo <windows-rs checkout>`
(see [Parameters](#parameters)).

### A/B against a clean `main` baseline

Create a worktree on `main`, then point `-BaselineRoot` at it. This switches the
script into **compare mode**: it interleaves the PR-tree and main-tree
`ReactorOptimized` runs on the same machine, runs WinUI3 once, and renders the
exact sticky comment (`out/comment.md`) the CI workflow would post.

```pwsh
git worktree add ../main origin/main
pwsh tests/stress_perf/ci/Run-PerfBenchmark.ps1 -BaselineRoot ../main -Reps 3
# when done:
git worktree remove ../main
```

### Parameters

| Parameter | Default | Purpose |
|---|---|---|
| `-Root` | repo root | Checkout to build + run (the **PR head** in compare mode). |
| `-BaselineRoot` | _(unset)_ | A second checkout (the **`main`** baseline). Setting it enables **compare mode** and renders `comment.md`. |
| `-Percent` | `50` | Fraction of grid cells mutated per tick (methodology default). |
| `-Duration` | `10` | Measured seconds per run (methodology default). |
| `-Reps` | `12` | Measured runs per side whose **median** is reported and whose per-run samples feed the **paired 95% CI**. 12 resolves the ~1–3% deltas these PRs target; a 2-run median cannot. |
| `-Warmup` | `2` | Leading runs discarded before the measured `Reps` (absorbs JIT / first-window warm-up). |
| `-RefReps` | `3` | Measured runs for the **reference-only** legs (vanilla WinUI3, Rust) — a single reference absolute, not a paired CI, so it needs far fewer reps. |
| `-RefWarmup` | `1` | Warm-up runs discarded for the reference-only legs. |
| `-IncludeMicro` | `$true` | Run the `PerfBench.ControlModel` reconciler micro-suite (compare mode) and append its per-bench ns/op + B/op table. Set `$false` to skip it. |
| `-MicroReps` | `12` | Repetitions per side for the micro-suite — each feeds the paired 95% CI, mirroring `-Reps`. |
| `-MicroIterations` | `10000` | Inner iterations per repetition inside each micro-bench (amortises timer resolution). |
| `-IncludeSkipFloor` | `$true` | Run a **second interleaved A/B leg** at `-SkipFloorPercent` and append a low-mutation skip-floor table (compare mode). Set `$false` to skip it (halves the macro runtime). |
| `-SkipFloorPercent` | `0` | Mutation percent for the skip-floor leg. At `0` the workload still mutates one cell/tick (`StockDataSource.Update` clamps the count to `Math.Max(1, …)`), so reconcile/diff isolate the O(n) per-tick child skip-walk floor the 50% leg dilutes. |
| `-Apps` | `ReactorOptimized,Direct` | Single-tree mode only: which harnesses to run. |
| `-Platform` | host arch | Target architecture (`x64` or `ARM64`). Defaults to your machine's native arch so the WinUI harness runs without emulation. |
| `-SelfContained` | `$true` | Build with the bundled WinApp runtime (no machine-wide install). |
| `-SkipBuild` | off | Reuse existing binaries (skip `dotnet build`). |
| `-PinAffinity` | off | Pin each run to one CPU core (can hurt on small runners). |
| `-RustRepo` | _(unset)_ | Path to a [`microsoft/windows-rs`](https://github.com/microsoft/windows-rs) checkout. Builds + runs its `reactor_perf` harness to add a **live** Rust column (best-effort). |
| `-DefenderExclude` | off | Add a temporary Defender exclusion on the output tree (removed afterward). CI-only; opt-in locally. |
| `-OutDir` | `ci/out` | Where logs, `result.json` and `comment.md` land. |
| `-HeadSha` / `-BaseSha` / `-RunUrl` | _(empty)_ | Context echoed into the comment footer (compare mode). |

### Outputs

- **Console table** — median per metric, per app (plus a live Rust column when `-RustRepo` is set).
- **`out/result.json`** — machine-readable medians + run-to-run spread + runner identity.
- **`out/comment.md`** _(compare mode only)_ — the rendered sticky comment.
- **`out/*.log`** — per-build and per-run stdout/stderr for debugging.

## How `/perf` works in CI

1. A trusted author (**OWNER / MEMBER / COLLABORATOR**, checked via
   `github.event.comment.author_association`) comments **`/perf`** on a PR.
   `workflow_dispatch` with a `pr_number` input is also supported for manual runs.
2. The workflow runs from the **default branch** (that is how `issue_comment`
   behaves), so once this is on `main` it works on **every already-open PR with
   no rebase** — important while a fleet of perf PRs is in flight. To honour that
   promise even for PRs opened *before* the gate landed (whose tree predates the
   self-contained csproj block), `Run-PerfBenchmark.ps1` overlays the harness
   `.csproj` from the trusted baseline over the PR tree's copy before building
   (compare mode only), restoring it afterwards — see below.
3. It checks out the default branch (trusted perf scripts + the `main` baseline),
   sets up .NET 10, fetches the PR head via `refs/pull/N/head` into a worktree
   (so forks work), then runs `Run-PerfBenchmark.ps1` in compare mode.
4. It posts — or **updates in place** on re-runs, via the hidden
   `<!-- reactor-perf-compare -->` marker — one sticky comment.

In compare mode the PR tree supplies everything it normally would — `src/Reactor/`
(the code under measurement) **and** the harness/workload sources under
`tests/stress_perf/` — *except* the harness `.csproj` build recipe, which
`Build-Harness` overlays from the trusted baseline tree for the duration of the
build and then restores. That `.csproj` is fixed test scaffolding (the StocksGrid
build recipe, including the `PerfCiSelfContained` self-contained knob), not a
perf-sensitive input; sourcing only it from baseline guarantees the self-contained
build works regardless of how old the PR is, while the PR's actual `src/Reactor/`
change is still compiled in via the harness's relative `ProjectReference`. (A PR
that deliberately edits the harness *sources* still has those changes measured —
only the project file comes from baseline.) The perf scripts and the `main`
baseline also come from the trusted default branch. The `author_association` gate
is the security control, because the job has a write token.

### The comment

Several tables plus footnotes:

- **Regression vs `main`** — `Metric | main | This PR | Δ (95% CI) | Status`,
  where Status is direction-aware (`✅ improvement` / `⚠️ regression` / `≈ within
  noise`). The Δ is the **mean of the paired per-run deltas** (PR run *i* vs
  `main` run *i*, collected interleaved) with a two-sided **95% confidence
  interval**. A change is flagged improvement/regression **only when that CI
  excludes 0**; if the CI straddles 0 it is *within noise* — no change resolvable
  at this sample size. This data-driven band replaces the old fixed 4% floor,
  which both buried real sub-4% wins and rubber-stamped any sub-4% number as
  "noise" regardless of how tight the runs were.
- **Low-mutation skip-floor (`--percent 0`)** — the same four headline metrics
  from a **second interleaved A/B leg** at near-zero mutation. With ~1 cell
  changing per tick, reconcile/diff are dominated by the **O(n) positional child
  skip-walk** (`ChildReconciler` re-walks every child each tick even when nothing
  moved) — the fixed per-tick cost the 50%-mutation headline table dilutes. This
  is the column a structural-skip optimization moves cleanly: at 50% the floor is
  a fraction of the work, at 0% it *is* the work. Same paired-CI gating as Table 1;
  omitted when `-IncludeSkipFloor $false` or a side produces no metrics.
- **Allocation (Reactor)** — `Alloc bytes/render` and `Gen0 GC / 1k renders`,
  `main` vs PR with the same paired-CI band. Rendered only when the harness
  reports the metric (n/a for pre-metric PR heads). This is the table that moves
  for allocation-reduction PRs.
- **Reconciler micro-benchmarks** — per-bench `ns/op` and `B/op` from the
  `PerfBench.ControlModel` micro-suite (M1–M13), `main` vs PR. ns-resolution and
  WinUI-undiluted, so it resolves Core/Reconciler time and allocation deltas the
  render-bound macro table cannot. The row flag tracks the **deterministic B/op**
  delta; `ns/op` is shown for context but not auto-flagged in v1 (pending rep-level
  interleaving — see the section above). Omitted when the micro leg is disabled or
  fails to produce results for both sides.
- **Cross-framework reference** — `vanilla WinUI3 | Rust windows-reactor |
  Reactor (this PR)` on the same StocksGrid workload, **all measured live on the
  same runner**. The Rust column builds and runs the
  [`microsoft/windows-rs`](https://github.com/microsoft/windows-rs) `reactor_perf`
  harness (a port of this workload), pinned in CI to a known-good commit. Because
  that harness is built self-contained, two things are patched into the pinned
  checkout before it builds: its `build.rs` is set to
  `windows_reactor_setup::as_self_contained()`, and the single `bootstrap()` call
  in its `main()` is commented out. The second patch matters as much as the first:
  the harness `main()` otherwise calls the framework-dependent Windows App SDK
  bootstrapper (`MddBootstrapInitialize2`) to locate a *machine-wide* runtime —
  which a self-contained app neither needs nor finds on the runtime-less runner.
  That call adds an unsatisfiable load-time import on
  `Microsoft.WindowsAppRuntime.Bootstrap.dll` (which `as_self_contained()` does not
  stage), so the exe dies at load with `0xC0000135` (DLL-not-found) and 0-byte
  output — exactly the failure first seen in issue #674. (Even if that DLL were
  staged, the bootstrapper fail-fasts headless with no machine-wide package —
  `0xC0000602`.) Dropping the call removes the import; the embedded `app.manifest`
  then activates the runtime app-local (reg-free). The DLLs that manifest declares
  must still sit next to `test_reactor_perf.exe` at process start, so
  `Stage-RustRuntime` in `Run-PerfBenchmark.ps1` stages and verifies them
  explicitly: both the Windows App SDK runtime MSIX payload and
  `Microsoft.Web.WebView2.Core.dll` (a manifest-declared SxS file that ships in the
  separate `Microsoft.Web.WebView2` package, mirroring upstream `deploy_webview2`).
  It is best-effort: if the Rust build, staging, or run fails the column reads `n/a`
  and the PR-vs-`main` comparison is unaffected. The WinUI3 column is the local
  `StressPerf.Direct` build.

## Variance: trust the delta, not the absolutes

GitHub-hosted runners are shared and heterogeneous — absolute numbers drift
between runs and machines. We do not own them, so the design leans on
**relative** measurement and several mitigations:

- **Same-runner A/B** — PR and `main` are built and measured on the *same*
  machine in the *same* job, so machine-class differences cancel.
- **Interleaving** — runs alternate `main, PR, main, PR, …` so slow time
  windows hit both sides roughly equally, and each `main`/PR pair is measured
  back-to-back so the **paired** delta cancels time-correlated drift.
- **Warm-up discard + 12 measured reps** — the first runs are dropped; 12 paired
  reps give the delta's confidence interval enough power to resolve a few-percent
  change (a 2-run median cannot — its run-to-run swing dwarfs the effect).
- **Paired 95% CI, not a fixed floor** — a change is called only when the 95% CI
  of the paired delta excludes 0. This adapts to the actual run-to-run noise
  instead of a blanket 4% threshold that both hid real sub-4% wins and accepted
  any sub-4% number as noise.
- **Pinned runtime** — High process priority, a high-performance power plan, a
  pinned **workstation / non-concurrent GC** (`DOTNET_gcServer=0`,
  `DOTNET_gcConcurrent=0`, applied identically to both sides), and `-PinAffinity`
  in CI, all restored afterward, plus a best-effort Defender exclusion.
- **Runner identity** — CPU / cores / RAM are recorded in the comment so the
  absolute numbers are read in context.

For the steadiest local numbers: close other apps, stay on AC power, and keep the
default `-Reps 12` (drop to e.g. `-Reps 3` only for a quick smoke run).

## Files here

| File | Role |
|---|---|
| [`Run-PerfBenchmark.ps1`](Run-PerfBenchmark.ps1) | Orchestrator — build, run, interleave, render. Used by both the workflow and humans. |
| [`PerfLib.ps1`](PerfLib.ps1) | Pure, side-effect-free helpers (parse, median, spread, paired-Δ 95% CI stats, direction-aware delta, micro-suite parse/compare, comment renderer) + the sticky-comment marker. |
| `PerfLib.Tests.ps1` / `RunPerfBenchmark.Tests.ps1` | Dependency-free unit tests for the helpers + orchestrator branching (run on a Linux runner via `perf-lib-tests.yml`). |
| [`PerfBench.ControlModel`](../../perf_bench/PerfBench.ControlModel) | The reconciler micro-suite (spec-047 M1–M13) the micro leg builds + runs per side. Lives under `tests/perf_bench/`; `/perf` consumes its JSON-Lines output. |

## Troubleshooting

- **Run crashes with `0xC000027B` right after "MountAndActivate ok".** That is a
  stowed XAML/compositor exception. Most often the box cannot composite a real
  WinUI window (headless server, no GPU/desktop session, or an RDP session
  without composition) — run from an interactive desktop session. **On an ARM64
  machine** it also happens when an **x64** harness runs under emulation; the
  runner builds for your host architecture by default (so ARM64 runs natively),
  but if you forced `-Platform x64` on ARM64, drop it or pass `-Platform ARM64`.
  The build and runtime are otherwise fine — `windows-latest` CI runners
  composite XAML correctly (the selftest/E2E jobs prove it).
- **`exe … not found`.** The self-contained build nests the exe under an arch +
  RID folder (`bin\<arch>\Release\<tfm>\win-<arch>\`); the script finds it
  recursively. If it is genuinely missing, check the `out/build-*.log` for the
  real `dotnet` error.
- **Rust column reads `n/a` with the harness exiting `0xC0000135`.** The
  self-contained `test_reactor_perf.exe` must not import the framework-dependent
  Windows App SDK bootstrapper. The workflow patches the harness `main()` to skip
  its `bootstrap()` call (alongside the `build.rs` → `as_self_contained()` patch);
  if upstream `windows-rs` moves that call and the patch's regex stops matching,
  the bootstrapper's load-time import on `Microsoft.WindowsAppRuntime.Bootstrap.dll`
  returns — and on the runtime-less runner the loader fails with `0xC0000135`. The
  "Prepare Rust harness" step logs a warning when the `main()` patch finds no match;
  re-point the regex at the relocated call. See issue #674.
- **Scripts won't parse.** Use `pwsh` (PowerShell 7+), not `powershell.exe` 5.1.
