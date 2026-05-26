# spec047-phase1-checkpoint

The Phase 1 regression checkpoint script for spec 047.
See `docs/specs/tasks/047-extensible-control-model-phase1-implementation.md` §1.2.

## Usage

```powershell
# Full checkpoint between section boundaries
pwsh tools/spec047-phase1-checkpoint/Run-Checkpoint.ps1 -SectionId 1.6

# Intra-section fast check (skips perf gates)
pwsh tools/spec047-phase1-checkpoint/Run-Checkpoint.ps1 -Quick
```

## Output

Each invocation writes:

- A JSON-Lines trend row to
  `docs/specs/047/phase1-results/<machine>/<date>/checkpoint-trend.jsonl`
- A human-readable log to
  `docs/specs/047/phase1-results/<machine>/<date>/checkpoint-<HHMMSS>.log`

## Exit codes

- 0 — all steps passed
- 1 — at least one step failed (see the log)

## Steps

| # | Step | Hard fail when |
|---|------|---------------|
| 1 | `dotnet test` on `Reactor.Tests` with `REACTOR_USE_V1_PROTOCOL=false` | any test fails |
| 2 | `dotnet test` on `Reactor.Tests` with `REACTOR_USE_V1_PROTOCOL=true`  | any test fails |
| 3 | Micro suite M1/M2/M5/M7/M13 — both flag states (gated on PerfBench.ControlModel) | > 10% regression vs Phase 0 baseline (deferred to 1.19) |
| 4 | M13 `OnIsOnChangedFireCount = 0` on both flag states | invariant fails |
| 5 | `StressPerf.ReactorV2.exe` launches | build fails (launch deferred to CI) |
| 6 | Trim-warning spot-check on Reactor.csproj | warnings rose above baseline |

Steps that depend on infrastructure that lands later in Phase 1 (PerfBench.ControlModel
project from 1.19, StressPerf.ReactorV2 launch harness from 1.18) emit `DEGRADED`
notes in the trend row rather than hard-failing — the checkpoint becomes
progressively stricter as later sections land their pieces.
