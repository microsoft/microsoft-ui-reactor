# Build metrics (artifact size-diff) PR comment

An automatic, self-updating PR comment that reports how much each shipped Reactor
artifact grew or shrank versus the PR's base branch. Informational only — it never fails a PR.

Example:

> ## 📦 Build metrics
>
> Artifact sizes for `deadbee` vs the base branch (`cafef00`).
>
> ### Packages (compressed .nupkg)
>
> | Artifact | base | PR | Δ | |
> |---|--:|--:|--:|:-:|
> | Microsoft.UI.Reactor.nupkg | 1.94 MB | 1.95 MB | +8.4 KB (+0.42%) | ⚠️ |
>
> ### Assemblies (uncompressed)
>
> | Artifact | base | PR | Δ | |
> |---|--:|--:|--:|:-:|
> | Reactor.dll | 3.16 MB | 3.16 MB | +0 B (0.00%) | ≈ |

## What is measured

For each shipped package the report tracks two numbers:

| Group | What | Why |
|---|---|---|
| **Packages (compressed .nupkg)** | the `.nupkg` file size | the download a consumer actually pays for |
| **Assemblies (uncompressed)** | the primary DLL inside `lib/<tfm>/` | the real "did our code grow" signal, unaffected by zip compression noise |

Tracked packages (see `$targets` in `Measure-BuildMetrics.ps1`):

- `Microsoft.UI.Reactor` → `Reactor.dll`
- `Microsoft.UI.Reactor.Advanced` → `Reactor.Advanced.dll`
- `Microsoft.UI.Reactor.Devtools` → `Microsoft.UI.Reactor.Devtools.dll`

Adding a package is a one-line entry in that `$targets` array.

## Files

| File | Role |
|---|---|
| `BuildMetricsLib.ps1` | **Pure** helpers (no filesystem side effects): byte formatting, `Get-SizeDelta`, the sticky-comment renderer, the trusted `Get-BuildMetricsTargetSpec` and `ConvertTo-SafeMeasurements` (the render-time security boundary). Unit-testable headless. |
| `BuildMetricsLib.Tests.ps1` | Dependency-free assertions for the pure lib (incl. the sanitizer). Exits non-zero on failure. |
| `Measure-BuildMetrics.ps1` | Orchestrator: `dotnet pack` each package in a source tree and emit a `sizes.json` of measurements. |
| `Measure-BuildMetrics.Tests.ps1` | AST-extracted tests for the orchestrator's `Select-LatestNupkg` + `Get-NupkgAssemblyBytes` (synthesizes real `.nupkg` ZIPs; no `dotnet`). |

## Workflows

Two workflows implement the standard secure `pull_request` + `workflow_run`
split, so untrusted PR build code never holds a write token **and never renders
the comment body**:

| Workflow | Trigger | Privilege | Job |
|---|---|---|---|
| `.github/workflows/build-metrics.yml` | `pull_request` (+ manual `workflow_dispatch`) | read-only | Builds + packs the PR head **and** base and measures both, then uploads **only** the machine-readable data (`head.sizes.json` + `base.sizes.json`). Runs untrusted PR build code. |
| `.github/workflows/build-metrics-comment.yml` | `workflow_run` | `pull-requests: write` | Checks out **trusted** default-branch code, validates the uploaded numbers via `ConvertTo-SafeMeasurements`, **renders** the comment itself, and posts/updates it. Runs **no** PR code. Resolves the target PR from the trusted `workflow_run` head SHA, never the artifact. |
| `.github/workflows/build-metrics-lib-tests.yml` | `pull_request` / `push` on `tests/build_metrics/ci/**` | read-only | Fast headless run of both `*.Tests.ps1` files. |

A comment is posted **only for `pull_request` runs**. A manual `workflow_dispatch`
run is **measure-only**: it still builds and uploads the `sizes.json` artifact, but
the poster can't safely resolve a PR from a dispatch's head SHA (which points at
the default branch), so it posts nothing — read the numbers from the run's
artifact.

**Why the poster renders (not the measure job):** the measure job builds
untrusted PR code, so if it produced the final markdown a PR could make the
privileged bot post arbitrary content. Instead the artifact carries only
per-artifact byte counts (the `sizes.json` also includes label/group strings, but
the poster **ignores** them). The trusted poster re-maps every row through a fixed
`Key → Label` spec — dropping unknown keys, using its own trusted labels/groups,
and accepting only non-negative integer byte counts — so the comment is fully
determined by trusted code plus a handful of validated integers.

The sticky comment is found + updated in place via a hidden marker
(`<!-- reactor-build-metrics -->`) — the same *mechanism* as
`tests/stress_perf/ci/PerfLib.ps1`, which uses its own distinct marker
(`<!-- reactor-perf-compare -->`).

## Local runbook

Unit-test the renderer (seconds, no build):

```pwsh
pwsh tests/build_metrics/ci/BuildMetricsLib.Tests.ps1
```

Measure the current tree and render a comment against a baseline:

```pwsh
# Measure this working tree.
pwsh tests/build_metrics/ci/Measure-BuildMetrics.ps1 -Root . -OutFile head.sizes.json

# Measure another checkout/worktree (e.g. main) the same way -> base.sizes.json,
# then render:
. tests/build_metrics/ci/BuildMetricsLib.ps1
$head = Get-Content head.sizes.json -Raw | ConvertFrom-Json
$base = Get-Content base.sizes.json -Raw | ConvertFrom-Json
Format-BuildMetricsComment -BaseMeasurements $base -HeadMeasurements $head -HeadSha HEAD -BaseSha main
```

## Notes

- A fixed package version (`0.0.0-buildmetrics`) is used for both sides so the
  version string embedded in the `.nuspec` never contributes to the diff.
- A small noise band (64 B **and** 0.05%, both must clear) keeps `.nupkg` zip
  jitter from rendering as a spurious regression.
- Growth is the regression direction for every artifact, so a shrink is flagged
  as the improvement (✅) and growth as the regression (⚠️).
- Not yet tracked (candidate future `$targets` entries): the NativeAOT-published
  `hello-world-aot.exe` — the highest-value size signal for this AOT/trim-focused
  repo — and the `mur` CLI publish. Left out of the initial cut to keep the double
  build fast and reliable; both slot in as additional targets.
