# Model evaluation notes

Contributor skills use the host's existing agents and tools. They do not need a
dedicated benchmarking package. Use the [model-selection policy](README.md#model-selection)
for ordinary work; the procedure below is only for an explicitly requested
comparison, not an instruction to spend credits whenever a skill runs.

## 2026-11-05 single-PR comparison

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

### Five-repeat results

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

## 2026-11-24 multi-PR comparison

GPT-6.2 Sol and Gemini 4 Carbon shipped after the single-PR study, so this run
asked whether they hold up across several PRs instead of repeats of one. The
user chose the two models, two reviews per model per snapshot, `medium`
reasoning and a 4,500-credit ceiling. There was no GPT-6.1 Sol arm, so any
comparison with 6.1 is indirect: different PRs, effort and rubric revision.

The archived runner was reused unchanged except for two recorded additions: a
per-case comparison base, and a schema normalization for coverage written as
`covered: true/false` and findings written as `description`/`impact` instead
of `evidence`. Every review ran in a fresh Copilot CLI 1.0.113 home with only
`view`, `glob` and `grep`, no Git history and no PR threads, using the current
`pr-review` dimension fragments as the rubric. Up to nine reviews ran
concurrently. All 20 reviews completed, passed model-identity, input-hash and
tool-isolation checks, and matched the JSON contract exactly; none of them
needed the normalization.

| Case | Snapshot | Known runtime defects |
|------|----------|-----------------------|
| c1 | [#1636](https://github.com/microsoft/microsoft-ui-reactor/pull/1636) pre-fix head `b6d499f283d21926604089235a105a0e74494325` | 3: Style cache key collision, stale themed Style when no setters remain, ancestor target type dropping ThemeRefs |
| c2 | [#1631](https://github.com/microsoft/microsoft-ui-reactor/pull/1631) pre-fix head `b1a0cd64a23921cf764cec124ee8a5af24ad2119` | 4: stuck caret suppression, scrollbar press clears selection, Shift+drag anchor jump, click count reset while streaming |
| c3 | [#1686](https://github.com/microsoft/microsoft-ui-reactor/pull/1686) pre-fix head `fd1b5e2bf3f93e0a60a1d3f85ebea389100d047a` | 6: three focus-recovery gaps, plus three latent defects flagged on a later commit but present here |
| c4 | [#1682](https://github.com/microsoft/microsoft-ui-reactor/pull/1682) pre-fix head `25a3027489527c1c14311049ff17b3c772059884` | 2: undetached `LayoutUpdated` handler, recovery overwriting a scroll-linked opacity expression |
| c5 | [#1552](https://github.com/microsoft/microsoft-ui-reactor/pull/1552) merged head `741f045cb27fd7c7c8807ca4537522bd00727214` | 0: intended clean control (no post-review correctness fixes found); it turned out to contain a regression |

Labels came from review threads on each pre-fix state and the repair commits
in the same PR. They were frozen before any model ran and kept outside the
reviewers' folders. Five blind adjudicators, one per case, ran on the
session's default sub-agent model (Claude, not host-verified; neither family
under test) and graded every finding against the frozen source without access
to the run-to-model map.

| Model | Reference defects found per review (c1–c4, two reviews each) | Total | Mean credits/review (all 10) | Mean wall time/review (all 10) |
|-------|-------------------------------------------------------------|-------|---------------------|------------------|
| GPT-6.2 Sol | c1 3, 3 · c2 1, 1 · c3 3, 3 · c4 1, 1 | 16/30 | 33.0 (27–38) | 69 s (54–89) |
| Gemini 4 Carbon | c1 3, 2 · c2 2, 2 · c3 3, 1 · c4 2, 1 | 16/30 | 219.3 (154–320) | 1,847 s (1,473–2,932) |

- **Different misses.** Every label Sol found, Carbon also found at least
  once. Carbon additionally found the caret-suppression bug (c2, 2/2) and the
  scroll-linked opacity overwrite (c4, 1/2). Sol was the more repeatable of the
  two: it found the same labels in both reviews of every case, while Carbon
  did so only on c2. Neither model found the
  scrollbar or click-count bugs in c2, c3's off-screen focus target, or two of
  c3's three latent defects.
- **Intended clean control.** The control was not clean. Both Carbon reviews found that
  #1552's record equality on `AnimationExtras` compares freshly allocated
  transition and keyframe arrays by reference, so `.Keyframes()`,
  `.ScrollLinked()` and `.OpacityTransition()` elements never skip on
  re-render and scroll expressions restart every render. The adjudicator
  verified this, and it is still present on `main`, tracked in
  [#1702](https://github.com/microsoft/microsoft-ui-reactor/issues/1702).
  Sol's control findings were all real but pre-existing, so out of scope.
- **Precision.** Of findings judged supported, unsupported or out of scope,
  Sol had 41/54 supported with 4 unsupported, and Carbon had 42/50 with 3
  unsupported. Both models produced 25–26 supported findings outside the
  reference, so recall against the labels understates useful output.
- **Output contract.** A separate three-run calibration (225-credit cap) was
  halted when its one Carbon review ignored the JSON coverage field, which
  motivated the normalization. That review's 190-credit cost on the smallest
  case also showed the cap would censor Carbon, so the main run used a
  450-credit cap. The calibration spent 248 credits; the 20 main reviews spent
  2,523.
- **Wall time.** Times are elapsed per review at `medium` effort with up to
  nine reviews in flight, so they include any provider throttling and are not
  isolated model speed.

Read this as: 6.2 Sol is a sound cheap primary and the best cost/consistency
baseline measured, and Carbon earns its cost as a different-family second
opinion, not as a primary. Two reviews per model per case cannot separate the
models' recall; one control is not a false-positive rate; the labels lean
toward what hosted Copilot review flagged; and `medium` is a shared setting
name, not equal compute. Security and packaging still have no positive labels.

### Archived evidence (2026-11-24)

The sanitized protocol, frozen reference, masked reviews, blind map,
adjudications, scores and calibration records are in
[`2026-11-24-multi-pr.json`](https://github.com/microsoft/microsoft-ui-reactor/blob/91796f8d98ac59e2272b64af49e11c1c627d9715/tools/review-eval/results/2026-11-24-multi-pr.json).
The same revision holds the runner with both patches. Raw traces, reasoning
and CLI homes are not published.

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
