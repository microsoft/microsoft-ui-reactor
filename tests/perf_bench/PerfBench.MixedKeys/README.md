# Mixed-key reconciliation experiment

This WinUI probe calls the production `Reconciler.Reconcile` path over real
`StackPanel`/`TextBlock` controls, without opening a window. It measures managed
allocation on the calling thread and elapsed time, not layout, rendering, FPS,
CPU cycles, native allocation, or retained memory.

Inputs and native controls are created before measurement. Each operation uses
prebuilt immutable elements, returns its control to a static sink, and mutates
the live collection. Between samples the probe verifies every control's identity,
order, and text in both directions. The mixed reorder swaps the keyed first and
last children while leaving the unkeyed middle at its original positions.
Explicit reorder and stable mixed/explicit/positional lists are controls.

## Reproduce

Use the same benchmark sources in baseline and candidate worktrees. Build each
with the following commands, substituting its root for `C:\src\reactor-base`.
The original baseline is `b0d9d31776213760461e1d3e1493da57658f3cf6`.

```powershell
Set-Location C:\src\reactor-base
dotnet restore tests\perf_bench\PerfBench.MixedKeys -p:Platform=x64
dotnet build tests\perf_bench\PerfBench.MixedKeys --no-restore -c Release `
    -p:Platform=x64 -p:SkipSignaturesGen=true -m:1 -nodereuse:false
```

Preserve the complete `bin\x64\Release\net10.0-windows10.0.22621.0\win-x64`
directory from each build. Independent builds can embed different source paths
or commit metadata even when the benchmark sources are identical. Reuse the
baseline's compiled benchmark DLL and matching PDB in both preserved directories,
leaving each side's Reactor DLL and other dependencies unchanged.

The shared benchmark DLL binds to the Reactor assembly name it was compiled against, so
both sides must come from the same side of the assembly rename. Builds before it ship
`Reactor.dll`, later builds ship `Microsoft.UI.Reactor.dll`, and the runner refuses a pair
that mixes the two. The recorded run below predates the rename.

```powershell
Copy-Item C:\temp\mixed-base-bin\PerfBench.MixedKeys.dll C:\temp\mixed-candidate-bin\PerfBench.MixedKeys.dll
Copy-Item C:\temp\mixed-base-bin\PerfBench.MixedKeys.pdb C:\temp\mixed-candidate-bin\PerfBench.MixedKeys.pdb
```

Then run from the candidate checkout during a quiet machine window.

```powershell
pwsh tests\perf_bench\PerfBench.MixedKeys\Run-Comparison.ps1 `
    -BaselineBin C:\temp\mixed-base-bin -CandidateBin C:\temp\mixed-candidate-bin `
    -BaselineCommit b0d9d31776213760461e1d3e1493da57658f3cf6 `
    -OutDir C:\temp\mixed-results -Pairs 4
```

The runner rejects different benchmark DLLs, fingerprints both Reactor DLLs,
disables tiered compilation on both sides, and alternates AB/BA process order.
It preserves all raw samples, GC counts, logs, medians, and paired 95% timing
intervals. Each process runs six samples of 400 updates per workload, after
warmup. The **process pair**, not each sample, is the independent repetition.
Source/binary provenance depends on building the declared baseline; the commit
argument is recorded metadata, not proof of a binary's origin.

## Results (2026-11-19, x64 Release)

Four process pairs against the baseline above gave the following median managed
bytes per update. All control-identity/content checks passed on both sides.
The recorded experiment built baseline and candidate sequentially in the same
worktree before committing, preserving the baseline output before the product
edit. Their benchmark DLL hashes were already identical when the runner checked
them. The explicit copy above is needed to reproduce that invariant across
separate worktrees, not a correction to the recorded measurements.

| Workload | Children | Baseline B/update | Candidate B/update |
|---|---:|---:|---:|
| Mixed reorder | 32 | 6,481.30 | 2,161.30 |
| Mixed reorder | 128 | 25,345.30 | 6,769.30 |
| Explicit reorder | 32 | 2,161.30 | 2,161.30 |
| Explicit reorder | 128 | 6,769.30 | 6,769.30 |
| Stable mixed, explicit, or positional | 32 / 128 | 41.06 | 41.06 |

The saving is exactly the two fallback strings per unkeyed middle child
(4,320 / 18,576 bytes per update). Other reconciliation allocation is unchanged.
There is no new cache or retained-key storage.

The small timing cohort suggested faster mixed reorders, but an unchanged
positional control also shifted significantly. That is evidence of timing
contamination/systematic variation, so **no runtime-speed or FPS improvement is
claimed**. Sibling agents and the coordinator were quiet; unrelated machine
activity was not controlled. Builds required `-p:WarningsNotAsErrors=NU1900`
because the vulnerability feed was unreachable; they also emitted existing
`MSB3277` dependency-version warnings.

Measured SHA-256 fingerprints (harness source at `9782f8ba1`; the later
output-path initialization refactor is outside the measured operation):

- Benchmark source `Program.cs` — `AFE067B3DE247B4B4C8D3F29C40368A42B493644BC65B06674892CEE68F3E67D`
- Identical benchmark DLL — `2EA6C2B4C0E64929EA85AA0721C0F4A6383B9739CF82D448B5F136B479032060`
- Baseline Reactor DLL — `D17C6CAD5A062648538F53E4C271C5504F2E13FFC655AAB1BF225AB340682547`
- Candidate Reactor DLL — `55B24F6178DC39F3218D02CDB5E4D6E4038BDDDB92FD0F85A028AC3BC7356B30`

## Identity compatibility

Only the keyed-middle match lookup changes. The pure positional path,
prefix/suffix matching, duplicate diagnostics, native mapping, LIS moves, exit
handling, and reentrant buffer ownership are unchanged.

Normally an unkeyed middle child can only update the old child at the same
filtered absolute position. `CanUpdate` still checks the exact runtime type,
key, and wrapper-specific identity. Two types with the same display name cannot
update one another.

Old explicit keys beginning `__pos_` retain the original string map, including
last-wins collisions. A generated fallback can shadow an explicit key, or vice
versa, causing a remount after `CanUpdate` rejects the mismatched key. Removing
that shadowing would change existing identity behavior, so the rare path still
allocates. The probe's allocation result does not apply to that compatibility path.
