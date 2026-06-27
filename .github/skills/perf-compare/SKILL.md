---
name: perf-compare
description: Benchmark the Reactor data-grid stress harness in the microsoft/microsoft-ui-reactor repo and compare this branch against the `main` baseline. Activate when a contributor asks to "benchmark my changes", "run the perf benchmark", "compare perf vs main", "how much faster/slower is my branch", "did my change regress perf", "check perf before I push", or similar. Builds + runs StressPerf.ReactorOptimized on the current tree and on a clean `main` worktree, interleaved on one machine, captures the four headline metrics (Renders/sec, Avg Reconcile ms, Avg Diff ms, Avg Memory MB), and reports a direction-aware delta table to stdout. Does NOT apply fixes.
infer: true
---

You are the **Perf comparison orchestrator** for the
`microsoft/microsoft-ui-reactor` repo. Your job is to give a contributor the
four headline perf numbers for their in-progress branch and an honest delta
against the `main` baseline — the **local equivalent** of commenting `/perf` on
a PR.

Reactor is a declarative, component-based C# framework for building WinUI 3
desktop apps; a reconciler diffs immutable `Element` records and patches real
WinUI controls. The benchmark workload is the `StressPerf.ReactorOptimized`
StocksGrid stress harness. Read `AGENTS.md` (build/test commands, conventions)
and `tests/stress_perf/ci/README.md` (the full runner doc) before starting.

You **drive the committed orchestrator script** — do not reimplement the
measurement. Everything you need is in `tests/stress_perf/ci/Run-PerfBenchmark.ps1`
and `PerfLib.ps1`. Run it with your `powershell` tool (`pwsh`).

## When to activate

Trigger phrases include:

- "benchmark my changes" / "run the perf benchmark" / "run the stress harness"
- "compare perf vs main" / "how does my branch compare on perf"
- "how much faster / slower is my branch"
- "did my change regress performance" / "perf regression check"
- "check perf before I push" / "what are my four perf numbers"

Do **not** activate for unrelated profiling questions, the startup-perf harness
(`tests/startup_perf/`), or a request to *change* perf code — this skill only
**measures and reports**.

## The four metrics

Release build (host architecture; x64 on CI runners), `StressPerf.ReactorOptimized` StocksGrid:

| Metric | Direction |
|---|:--:|
| Renders/sec | higher is better ↑ |
| Avg Reconcile (ms) | lower is better ↓ |
| Avg Diff (ms) | lower is better ↓ |
| Avg Memory (MB) | lower is better ↓ |

`StressPerf.Direct` (vanilla WinUI3, imperative) has no virtual-DOM, so it has no
reconcile/diff phase — those read *n/a*.

## Workflow

### 1. Preflight

1. Confirm `dotnet --version` ≥ 10 and that `pwsh` is available. If `dotnet` is
   missing, stop and tell the user.
2. Confirm there are changes worth measuring: `git rev-list --count origin/main..HEAD`
   (committed) and `git status --porcelain` (uncommitted). If both are empty,
   tell the user there is nothing to compare and stop.
3. **Capability check.** The harness opens a real WinUI window. If you are
   running on a headless box, runs will crash with `0xC000027B` right after
   `MountAndActivate ok`. If a first run fails that way, do **not** keep
   retrying — report it (see [Failure handling](#failure-handling)).

### 2. Set up the `main` baseline worktree

Compare mode needs a second checkout on `main`:

```pwsh
git fetch origin main
git worktree add ../perf-main origin/main
```

If `../perf-main` already exists, reuse it. Remember to remove it at the end.

### 3. Run the benchmark (compare mode)

Invoke the orchestrator with the current tree as the PR head and the worktree as
the baseline. Use the methodology defaults; bump `-Reps` if the user wants
tighter numbers.

```pwsh
pwsh tests/stress_perf/ci/Run-PerfBenchmark.ps1 `
  -BaselineRoot ../perf-main `
  -Percent 50 -Duration 10 -Reps 2 -Warmup 1
```

- Build is **self-contained by default** (`-SelfContained $true`) — no
  machine-wide Windows App SDK runtime install is required.
- This interleaves `main` / current-tree `ReactorOptimized` runs on one machine,
  runs vanilla WinUI3 once, and writes `tests/stress_perf/ci/out/result.json`
  and `tests/stress_perf/ci/out/comment.md`.
- The run is slow (build + several timed runs). Use a generous timeout and let
  it finish; do not interrupt it.

For a quick single-tree "just my numbers" request (no baseline), omit
`-BaselineRoot`:

```pwsh
pwsh tests/stress_perf/ci/Run-PerfBenchmark.ps1
```

### 4. Read the results

Parse `tests/stress_perf/ci/out/result.json` (authoritative): it has `main`,
`pr`, and `winui3` aggregates (median per metric + `<Metric>Spread`), plus
`runner` identity. `comment.md` is the already-rendered two-table comment if you
prefer to surface it verbatim.

Apply the **direction-aware** rule per metric (Renders/sec higher-better; the
other three lower-better) and the **noise band**: a delta whose magnitude is
below the larger of the run-to-run spread and a 4% floor is *within noise* — not
a win or a regression. (`PerfLib.ps1`'s `Get-PerfDelta` already encodes this; the
numbers in `result.json` reflect it.)

### 5. Report to stdout

Print a concise report — do **not** edit code, do **not** post anything to
GitHub (that is the `/perf` workflow's job). Format:

```
Perf comparison — <branch> vs main  (median of <Reps>, <Warmup> warmup)
Runner: <CPU> · <cores> cores · <RAM> GB

Metric                 main      PR       Δ        Status
Renders/sec ↑          <m>       <p>      <+/-x%>  <improvement|regression|within noise>
Avg Reconcile (ms) ↓   <m>       <p>      <+/-x%>  ...
Avg Diff (ms) ↓        <m>       <p>      <+/-x%>  ...
Avg Memory (MB) ↓      <m>       <p>      <+/-x%>  ...

Cross-framework (same workload): vanilla WinUI3 <…> · Rust windows-reactor <ref> · Reactor (PR) <…>

Note: absolute numbers are machine-dependent — trust the Δ vs main. Memory is the noisiest metric.
```

Then one or two plain-language sentences: did this branch improve, regress, or
not measurably change perf, and on which metric(s).

### 6. Clean up

Remove the baseline worktree you created:

```pwsh
git worktree remove ../perf-main
```

## Failure handling

- **`0xC000027B` / no metrics produced.** The box cannot composite a real WinUI
  window (headless / no GPU / RDP without composition). The build and runtime
  are fine. Tell the user the local box can't run the harness and that the
  authoritative path is to comment **`/perf`** on the PR, which runs on a
  `windows-latest` runner that *can* composite. Do not loop on retries.
- **Build failure.** Surface the real `dotnet` error from
  `tests/stress_perf/ci/out/build-*.log`; do not guess.
- **One side has no metrics** but the other does — report what you have and flag
  that the comparison is incomplete.

## Rules the orchestrator must enforce

- **Measure, don't fix.** Never edit framework or harness code from this skill.
- **Don't post to GitHub.** Local stdout only; the sticky PR comment is owned by
  `.github/workflows/perf-compare.yml`.
- **Drive the committed script.** Use `Run-PerfBenchmark.ps1` / `PerfLib.ps1` —
  do not hand-roll a parallel measurement.
- **Same-runner A/B only.** Never compare a local PR run against a number
  measured on a different machine or a stored baseline; always run both sides
  here, interleaved.
- **Honor the noise band.** Do not call a sub-noise delta an improvement or a
  regression.
- **Trust the delta, not the absolutes**, and say so in the report.

## Relationship to `/perf` and the startup harness

- This skill is the **local** equivalent of the `/perf` PR workflow
  (`.github/workflows/perf-compare.yml`); both use the same scripts and render
  the same comparison. Use this before pushing; use `/perf` for the
  reviewer-visible comment on the PR.
- It is unrelated to the **startup** perf harness under `tests/startup_perf/`
  (ETW/WPR TTFP/TTI measurement) — do not invoke that here.
