# Model evaluation notes

Contributor skills use the host's existing agents and tools. They do not need a
dedicated benchmarking package. Use the [model-selection policy](README.md#model-selection)
for ordinary work; the procedure below is only for an explicitly requested
comparison, not an instruction to spend credits whenever a skill runs.

## What we measured

The November 2026 experiment reviewed a frozen, pre-fix state of
[PR #1444](https://github.com/microsoft/microsoft-ui-reactor/pull/1444).
It changed 10 files, +505/-34 lines, and contained three source-verified runtime
defects with later repair commits.

- Base: `74791562c11792f7c0e2dd875f1e82b94d70b397`.
- Reviewed head: `80acfb6c6c3dd78f660a36a4948989b0752b0007`.
- Runtime repair: `dbcec359118c42eba12810fd68ec7a85c592b0bc`.
- Oracle/documentation repair: `1bcb4c6498bde619ad5f297611d803c809e91eff`.

The main comparison used five fresh sessions per model, all twenty in parallel,
with the same source, prompt and combined seven-dimension rubric. It requested
`high` reasoning and default context in Copilot CLI 1.0.105. The user explicitly
approved removing credit and time caps for those twenty trials; there were no
replacement runs. This was a one-off experiment, not maintained repository tooling.

Earlier 30-credit trials were mostly budget-censored. A subsequent single
150-credit trial per model returned three complete reports and one partial Opus
report. Those conditions are not pooled with the uncapped repetitions.

## Five-repeat results

Each outcome counts only the **three frozen runtime defects**, not all useful
findings. Runs are in repetition order, not sorted to favor the best outcome.

| Model | Known defects per run, out of 3 | Mean | Mean credits/run |
|-------|--------------------------------|------|------------------|
| GPT-6.1 Sol | 2, 1, 1, 2, 1 | 1.4 | 41.09 |
| GPT-6 Astra | 1, 1, 1, 1, 1 | 1.0 | 230.73 |
| Claude Opus 5.5 | 1, 0, 0, 0, 0 | 0.2 | 163.87 |
| Claude Sonnet 5.5 | 0, 0, 0, 0, 0 | 0.0 | 43.69 |

| Known runtime defect | Sol | Astra | Opus | Sonnet |
|----------------------|-----|-------|------|--------|
| R1: retained dialog's `Opened` delegate keeps the disposed host alive | 5/5 | 4/5 | 1/5 | 0/5 |
| R2: disposal during content mount leaves later siblings' effects without cleanup | 2/5 | 1/5 | 0/5 | 0/5 |
| R3: live update reattaches replacement content after disposal | 0/5 | 0/5 | 0/5 | 0/5 |

All twenty reports completed, using **2396.90815 recorded review-run credits**.
One Opus response required presentation-only extraction of a complete JSON report
after introductory prose; no model was rerun. None found R3 or any of the five
reference test-oracle gaps. Opus found a missing disposal-contract documentation
note in two runs that Sol missed. Findings outside the reference were adjudicated
separately, not automatically counted as false.

This supports Sol as the next correctness-review evaluation baseline on this
case, not a universal default. Five repeats of one PR are not five independent
PRs. The same effort name does not imply equal compute across providers, and
parallel latency is not isolated model speed. Security, packaging, focused
dimension passes and cross-family verification remain unmeasured.

The reference labels came from historical review evidence, source inspection and
matching repairs; no new WinUI reproduction was executed. Reports were
model-masked before adjudication, but the coordinator knew the design and run
metadata, so this was not independent human-blind grading.

### Archived evidence

The sanitized records remain available at the experiment's immutable archive
revision, outside the maintained Markdown-only change:

- [Twenty-run protocol, final reviews, usage and 53 adjudications](https://github.com/microsoft/microsoft-ui-reactor/blob/c412ca32db38382a351e7c078662fe6d9e866d0b/tools/review-eval/results/2026-11-05-repeats.json).
- [Earlier calibration record](https://github.com/microsoft/microsoft-ui-reactor/blob/c412ca32db38382a351e7c078662fe6d9e866d0b/tools/review-eval/results/2026-11-05.json).

The calibration record preserves its original scoring for auditability; its
partial Opus response is not a completed-review comparison. Raw traces and
credentials are not published. Recorded credits exclude the coordinating
conversation and are not a settled billing invoice.

## Running another comparison

Use this checklist with available host tools; do not build or install a new
runner merely to follow it.

1. **Agree on scope and cost.** Confirm available model IDs, repeat count,
   reasoning/context settings, parallelism and budget with the user. Do not
   substitute models, lift limits or retry paid trials without authorization.
2. **Freeze the inputs and reference.** Pin the Git base/head revisions, diff,
   supporting source and exact prompt. Record source hashes with existing tools.
   Define known defects and matching rules before reviewing outputs; keep labels,
   later repairs and review threads out of reviewer context.
3. **Run fresh, comparable reviews.** Give each approved run identical inputs
   and read-only instructions using the host's agent/session tools. Use actual
   host isolation when available; prompts alone are not a sandbox. Keep
   specialized dimension passes and cross-family verification as separate
   experiments rather than changing the workload between candidates.
4. **Record every scheduled trial.** Capture host-reported model identity,
   completion, coverage, elapsed time and usage where available. Mark failed,
   incomplete and unstarted trials unavailable, not clean zero-finding reviews.
   Unknown usage is not zero cost. If the host cannot expose or enforce a
   control, disclose that limitation rather than claiming it was verified.
5. **Adjudicate against the frozen source.** Mask model names where practical,
   match concrete mechanisms and deduplicate causes. Retain novel and unresolved
   findings with written evidence; do not modify the reference after seeing
   answers to improve a model's score.
6. **Report the distribution and limits.** Show every run, per-defect detection
   rates with their denominators, completion rate and observed cost. Keep the
   union across runs distinct from typical single-run performance. Preserve
   sanitized evidence as accessible artifacts, not raw traces or credentials.
7. **Promote cautiously.** Use another untouched PR and repaired controls before
   claiming general routing defaults. Model choice, prompt wording, effort and
   reviewer specialization need separate comparisons.
