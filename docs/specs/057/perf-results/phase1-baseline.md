# Spec 057 Phase 1 — edge-free re-render perf gate

> **Status.** Phase 1 perf snapshot for tasks 0.4 + 1.5. Validates that
> the reference-edge commit (`e0420d27`) does not measurably regress the
> edge-free re-render/update path versus its parent (`e0420d27^` =
> `df5ec690`).

---

## 1. Cost-model claim being validated

Spec 057 Phase 1 adds reference-edge bookkeeping for elements that opt in to
reference properties. Edge-free trees should keep the old hot path:

- `.Ref` population remains guarded by `if (m.Ref is not null)`, so elements
  without `.Ref` do not call `ElementRef.SetCurrent`.
- The top-level reconcile bracket adds one thread-static increment plus one
  decrement/null-or-empty check per pass.
- Descriptor reference subscriptions are only created by reference-property
  entries; the M7 fixture below has none.

Expected result: an edge-free no-op re-render remains within run-to-run noise.

---

## 2. Bench selected

`tests\perf_bench\PerfBench.ControlModel`, bench **M7**
`Update_NoChange`, variant **Reactor**.

Why this bench: M7 is the existing spec-047 micro-bench for a no-op re-render
of a 1000-element edge-free `TextBlock` tree. It repeatedly diffs the same
mounted element/control pairs, which isolates the "nothing changed" update hot
path. The fixture does not create `.Ref` cells and does not use descriptor
reference properties.

Command used from each worktree:

```pwsh
dotnet build tests\perf_bench\PerfBench.ControlModel -c Release -p:Platform=ARM64 --nologo
dotnet run -c Release -p:Platform=ARM64 --no-build --project tests\perf_bench\PerfBench.ControlModel -- --test M7 --variant Reactor --iterations 500000 --reps 5 --headless --out bench-m7-*.jsonl
```

One reported `meanNs` is one M7 operation: a no-op update pass over the
1000-element tree.

---

## 3. Machine / runtime

**Run.** 2026-06-09, local dev box.

| Item | Value |
|---|---|
| Machine | `LAPTOP-4MEP83VI` |
| CPU | Snapdragon(R) X 12-core X1E80100 @ 3.40 GHz |
| RAM | 31.6 GB |
| OS | Microsoft Windows 11 Enterprise 10.0.26200 build 26200 |
| .NET SDK | 10.0.300 |
| Runtime reported by bench | .NET 10.0.8 |
| Process architecture | ARM64 native |
| Configuration | Release |

Note: initial x64-emulated and short 50k-iteration probes were noisy on this
ARM64 machine, so the recorded gate uses native ARM64 and longer 500k-iteration
repetitions.

---

## 4. Results

| Tree / commit | Reps | Mean ns/op | Median ns/op | Range ns/op | Delta vs baseline |
|---|---:|---:|---:|---:|---:|
| Baseline `e0420d27^` (`df5ec690`) | 5 | 14,326.8 | 14,685.1 | 12,462.8-15,228.2 | — |
| Current `e0420d27` | 5 | 14,354.2 | 14,132.0 | 13,452.4-15,498.3 | +0.19% mean / -3.77% median |

Raw repetitions (`meanNs`):

| Commit | Rep values (ns/op) |
|---|---|
| Baseline | 15,228.2; 14,201.8; 15,056.0; 12,462.8; 14,685.1 |
| Current | 13,452.4; 14,603.6; 14,084.9; 15,498.3; 14,132.0 |

Allocation/GC stayed equivalent: each repetition reported the same steady
allocation pattern as the harness setup (`740,424` or `781,488` bytes depending
on repetition parity) and no Gen0/Gen1/Gen2 collections except one baseline
rep with a single Gen0/Gen1 collection.

---

## 5. Interpretation

The current build is effectively identical to the pre-057 baseline on the
edge-free M7 no-op update path: +0.19% by mean and slightly faster by median,
with both ranges overlapping. This is well inside the observed local run-to-run
noise and supports the structural cost model that edge-free trees skip the
reference-population work and only pay the tiny top-level commit bracket.

**Edge-free perf gate: PASS.** No regression beyond measurement noise was
observed.

---

## 6. With-edges measurement

N/A for this run. M7 cannot express reference edges without changing the bench
fixture, and this task was measurement-only with no source/test changes. A
future follow-up should add or reuse a dedicated ref-edge bench if task 1.5
needs an explicit with-edges cost curve.

---

## 7. Reproducibility

Baseline was captured from a detached git worktree at `e0420d27^`:

```pwsh
git worktree add ..\reactor2-baseline e0420d27^
cd ..\reactor2-baseline
dotnet build tests\perf_bench\PerfBench.ControlModel -c Release -p:Platform=ARM64 --nologo
dotnet run -c Release -p:Platform=ARM64 --no-build --project tests\perf_bench\PerfBench.ControlModel -- --test M7 --variant Reactor --iterations 500000 --reps 5 --headless --out bench-m7-baseline-arm64-500k.jsonl
```

Current was captured from the main worktree at `e0420d27` with the same build
and run command, changing only the output file name.
