# Merged-coverage comparison PR comment

An automatic, self-updating PR comment that reports the PR's merged (unit +
selftest) line + branch coverage **against its base branch** (the `main` baseline
in the common case), with a signed per-metric delta. Informational only — the base
leg never fails a PR; only a failure to measure the PR head itself is surfaced as a
red check.

Example:

> ## 🧪 Merged coverage
>
> Coverage for `deadbee` vs the base branch (`cafef00`) — unit + selftest merged.
>
> | Metric | base | PR | Δ | |
> |---|--:|--:|--:|:-:|
> | Line   | 86.10% | 87.34% | +1.24 pp | ✅ |
> | Branch | 74.50% (500/671) | 75.20% (510/678) | +0.70 pp | ✅ |

## What is measured

The canonical repo metric — **unit + selftest merged** — computed exactly as in
[`TESTING.md`](../../../TESTING.md#code-coverage), for **both** the PR head and its
base branch:

| Metric | What | Source |
|---|---|---|
| **Line** | merged line coverage | the cobertura root `line-rate` |
| **Branch** | merged conditional-branch coverage, with the covered/total counts | summed from the per-line `condition-coverage="P% (c/t)"` attributes |

`dotnet-coverage`'s cobertura output records per-line branch data but does **not**
aggregate it (its root/class `branch-rate` is hard-coded to `1`), so the branch
rate is summed from the per-line numerators/denominators in `Get-CoberturaRates`.

Δ is reported in **percentage points** (`pp`). Higher coverage is the improvement
direction, so a rise clears as ✅, a fall as ⚠️, and a move below the noise floor
(0.1 pp) as ≈.

## Files

| File | Role |
|---|---|
| `CoverageLib.ps1` | **Pure** helpers (no filesystem side effects): the cobertura parser (`Get-CoberturaRates` / `Get-CoberturaRatesFromXml`), percent/delta formatting, `Get-CoverageDelta`, the sticky-comment renderer (`Format-CoverageComment`), and the render-time security boundary (`ConvertTo-SafeCoverageMetrics`). Unit-testable headless. |
| `CoverageLib.Tests.ps1` | Dependency-free assertions for the pure lib (parser, delta math, formatting, sanitizer). Exits non-zero on failure. |
| `Measure-Coverage.ps1` | Orchestrator: build + instrument + collect (unit + selftest) + merge in one source tree, then aggregate the merged report into a `coverage.json` of numbers. |
| `Measure-Coverage.Tests.ps1` | AST-extracted tests for the orchestrator's `Invoke-Checked` guard, the report-reading path, and the JSON contract the poster reads. |

## Workflows

Two workflows implement the standard secure `pull_request` + `workflow_run` split,
so untrusted PR build code never holds a write token **and never renders the
comment body**:

| Workflow | Trigger | Privilege | Job |
|---|---|---|---|
| `.github/workflows/coverage.yml` | `pull_request` (+ manual `workflow_dispatch`) | read-only | Builds + instruments + measures the PR head **and** base branch (each in its own worktree), then uploads **only** the machine-readable numbers (`head.coverage.json` + `base.coverage.json`). Runs untrusted PR build code. |
| `.github/workflows/coverage-comment.yml` | `workflow_run` | `pull-requests: write` | Checks out **trusted** default-branch code, validates the uploaded numbers via `ConvertTo-SafeCoverageMetrics`, **renders** the comparison comment itself, and posts/updates it. Runs **no** PR code. Resolves the target PR + base SHA from the trusted `workflow_run` head SHA, never the artifact. |
| `.github/workflows/coverage-lib-tests.yml` | `pull_request` / `push` on `tests/coverage/ci/**` | read-only | Fast headless run of both `*.Tests.ps1` files. |

A comment is posted **only for `pull_request` runs**. A manual `workflow_dispatch`
run is **measure-only**: it still measures and uploads the `coverage.json`
artifact, but the poster can't safely resolve a PR from a dispatch's head SHA, so
it posts nothing — read the numbers from the run's **job summary** / artifact.

**Why the poster renders (not the measure job):** the measure job builds untrusted
PR code, so if it produced the final markdown a PR could make the privileged bot
post arbitrary content. Instead the artifact carries only a handful of numbers per
side; the poster re-validates each through `ConvertTo-SafeCoverageMetrics`
(percentages accepted only as a plain decimal in `[0, 100]`; counts only as
non-negative integers; everything else — signs, exponents, pipes, markup — dropped
to `null`) and renders from trusted code. So the comment is fully determined by
trusted code plus a set of validated numbers — the untrusted artifact can never
inject markdown into the bot-authored comment.

### Degraded modes

- **Base measurement fails** → the comment shows the PR's absolute coverage with an
  em-dash delta and a "baseline unavailable" note. The PR's coverage **check stays
  green** (a broken `main` build shouldn't block the PR).
- **Head measurement fails** → a `> [!CAUTION]` "run failed" comment, and the
  measure job is a **red check**.
- **Base = head (no change)** → a "No coverage change beyond the noise floor" note.

The sticky comment is found + updated in place via a hidden marker
(`<!-- reactor-coverage -->`) — the same *mechanism* as
`tests/build_metrics/ci/BuildMetricsLib.ps1`, which uses its own distinct marker.

## Local runbook

Unit-test the parser + renderer (seconds, no build):

```pwsh
pwsh tests/coverage/ci/CoverageLib.Tests.ps1
pwsh tests/coverage/ci/Measure-Coverage.Tests.ps1
```

Measure the current tree and render a comment against a baseline (needs
`dotnet tool install -g dotnet-coverage`):

```pwsh
# Measure this working tree -> head.coverage.json (~10 min, instrumented).
pwsh tests/coverage/ci/Measure-Coverage.ps1 -Root . -OutFile head.coverage.json

# Measure another checkout/worktree (e.g. main) the same way -> base.coverage.json,
# then render:
. tests/coverage/ci/CoverageLib.ps1
$head = ConvertTo-SafeCoverageMetrics (Get-Content head.coverage.json -Raw | ConvertFrom-Json)
$base = ConvertTo-SafeCoverageMetrics (Get-Content base.coverage.json -Raw | ConvertFrom-Json)
Format-CoverageComment -BaseMetrics $base -HeadMetrics $head -HeadSha HEAD -BaseSha main
```

## Notes

- Measuring both sides roughly **doubles** the coverage run time versus a
  head-only measurement — the same tradeoff the `build-metrics` double build
  already accepts on every PR (see `tests/build_metrics/ci/README.md`). An
  alternative (caching a `main` baseline from a push-to-`main` run) was considered
  but not taken, to stay consistent with the in-repo build-metrics precedent.
- A small noise band (0.1 pp) keeps sub-noise branch-coverage jitter from rendering
  as a spurious ✅/⚠️.
- Not yet reported (candidate future work): **patch/diff-scoped** coverage (coverage
  of only the lines the PR changed), which complements — but does not replace — this
  whole-repo baseline delta.
