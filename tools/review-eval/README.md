# Historical PR review eval

A small, local experiment runner for comparing **primary review models** on
fixed Reactor PR snapshots. It does not modify or execute the reviewed code,
post reviews, or run the entire `pr-review` orchestration.

The [2026-11-05 pilot report](results/2026-11-05.md) records the first budget
calibration, the four-model comparison, adjudication limits and costs.
The [five-repeat study](results/2026-11-05-repeats.md) records twenty fresh,
uncapped reviews of the same snapshot, including every run's outcome.
Sanitized evidence is committed alongside the reports:
[pilot data](results/2026-11-05.json) and
[five-repeat data](results/2026-11-05-repeats.json). These contain returned
reviews, explicit adjudications, protocol inputs, and recorded usage, not raw
traces, credentials, or CLI homes.

Use paired historical revisions: a state with evidenced defects, a repair, and
optionally an intermediate state with test/documentation gaps. A merged revision
is a negative control for the repaired defects, **not proof that no other bugs
remain**.

## Requirements

- Python 3.11+, Git, PowerShell 7, authenticated `gh`, and Copilot CLI.
- A CLI supporting `--available-tools`, `--no-custom-instructions`,
  `--usage-output-file`, `--max-ai-credits`, and `--no-auto-update`.
- Explicit approval of the model IDs, reasoning setting, run count, and budget.
  CLI credit limits are soft; an in-flight response can exceed its cap.

No Python dependencies are required. The runner obtains the current `gh` token
for the CLI's process environment only; it does not print it or write it to the
experiment artifacts. Every model run has a fresh `COPILOT_HOME`.

## Prepare

Supply a JSON configuration with these fields:

| Field | Meaning |
|-------|---------|
| `base` | Full, immutable comparison-base commit OID |
| `cases` | Objects with opaque alphanumeric `id` and full `revision` OID |
| `source_paths` | Git tree paths to export; include every changed text file and supporting code |
| `models` | Explicit, available CLI model IDs; do not use `auto` |
| `reasoning_effort` | One supported setting shared across candidates |
| `seed`, `repetitions` | Reproducible scheduling and fresh-session repeats |
| `max_runs` | Approved ceiling on scheduled reviews |
| `credits_per_run`, `total_credits` | Approved credit caps; CLI minimum is 30 per capped run. Explicit `null` disables that cap; an uncapped run requires an uncapped total |
| `timeout_seconds` | Wall-clock timeout per review, or explicit `null` for no experiment timeout; record timeouts, do not score them as clean |

```powershell
python tools\review-eval\review_eval.py prepare `
  --config C:\eval\config.json --output C:\eval\pilot
```

The output directory must not already exist and must be outside the source
checkout, so a review cannot inherit its live Git context. Preparation exports historical
source text, a diff against the common base, and the same seven-dimension review
rubric into each case. Git metadata, PR comments, later revisions, and grading
labels are not exported. The source scope and SHA-256 digests are recorded.
Non-text assets are excluded and must not be evaluated as missing files.

Before any model runs, write `reference.json` in the output directory, **outside
`cases`**, with the hidden reference defects, per-case presence/absence, source
and fix evidence, matching rules, acceptable severities, and known limitations.
Distinguish source-verified historical reproduction reports from failures you
actually reproduced. Freeze the scoring rules before reading model answers.

## Run and inspect

```powershell
# Verify transport, tool isolation, identity, output shape, and accounting first.
python tools\review-eval\review_eval.py run `
  --output C:\eval\pilot --max-starts 1 --workers 1

# Next calibrate a balanced block containing every model before a larger matrix.
# Set the number of starts to the remaining models in that first block.
python tools\review-eval\review_eval.py run `
  --output C:\eval\pilot --max-starts 3 --workers 3
```

The runner exposes only `view`, `glob`, and `grep`, denies shell/write/URL
permissions, disables custom instructions, built-in MCP, history sync and client
telemetry, and does not enable account memory. It retains CWD-only path checks
and disables automatic temp-directory access. The CLI's path/tool restrictions
are the boundary; a separate folder alone is not a sandbox.

For an explicitly authorized uncapped experiment, set all three limit fields
to `null`. The CLI credit-limit argument is then omitted and the runner imposes
no wall-clock timeout. The fixed schedule and `max_runs` still bound the number
of trials; usage is still measured, provider limits still apply, and failures
are not silently retried. Keep this condition separate from capped trials.

`protocol-lock.json` freezes the experiment, reference labels and CLI version.
Each run saves its invocation, JSONL events, stderr, usage export, and result.
Unknown usage halts further scheduling instead of counting as zero cost.
Unfinished runs require inspection/recovery; they are never automatically
replayed. Completed runs are not repeated when resuming.

An incomplete report also stops further scheduling; already-running trials
finish and retain their usage exports. This is especially important during
calibration: a cap affordable for one model can cut another off before its first
finding. These are censored reviews, not evidence that the model found no bugs.
Change budgets or prompts only as a separately identified experiment.
Structural validity (`review_valid`) is separate from completed coverage
(`review_complete`). A report declaring any dimension `incomplete`, a timeout,
a nonzero exit, or a budget-exhausted session cannot enter complete-review
aggregates. Its returned text and usage remain available. Exceeding a soft cap
alone does not make an otherwise completed review incomplete.

To pause gracefully, create `pause.flag` in the output directory. No further
trials start after it is observed; in-flight runs finish so accounting is not
lost. The invocation records each owned process ID for emergency termination.

For a completed run whose recorder could not parse usage, inspect the raw export
and correct the parser, then use `recover --output C:\eval\pilot`. This recalculates
metadata without another model call and preserves the previous result. The
[Copilot SDK accounting contract](https://github.com/github/copilot-sdk/blob/main/docs/features/usage-and-billing.md)
defines one AI credit as 1e9 nano-AIU; the runner requires complete accounting.
`recover --refresh` re-extracts completed reports after a recorder correction,
without repeating inference. Display fences, prose preceding a single complete
JSON report, and the rubric's human-readable dimension headings are normalized
explicitly; ambiguous multiple objects or trailing text are not repaired.
`review_schema_exact` and
`schema_normalizations` preserve that distinction from strict schema compliance.
Recovery, export, and scoring all verify the original experiment/reference lock.
Recovery never upgrades a timed-out or failed process to successful completion.
Raw traces can include reasoning/provider metadata: keep them local and publish
only sanitized final findings, metrics and adjudications.

Check the **effective** model and effort in host telemetry, not a model's
self-description. Audit tool calls and snapshot hashes. A fallback, unauthorized
read, malformed final report, timeout or exhausted budget is a recorded
limitation, not a clean review.

## Adjudicate

Blind model names before judging findings. Deduplicate by cause and match a
specific failure mechanism, not a keyword. A new finding absent from the
reference is **unjudged**, not automatically false: investigate it against that
snapshot and record its evidence/disposition.

```powershell
python tools\review-eval\score_eval.py export `
  --experiment C:\eval\pilot --adjudication C:\eval\pilot\adjudication
# Inspect P*.json without opening blind-map.json. Complete adjudications.json.
python tools\review-eval\score_eval.py score `
  --experiment C:\eval\pilot --adjudication C:\eval\pilot\adjudication
```

Every finding needs an explicit verdict and reason. Supported findings can match
reference labels or be recorded as novel supported findings. The scorer rejects
credit for a label that is already fixed in that snapshot, preserves unresolved
findings in precision bounds, and does not call an empty review perfect precision.
The map masks model names but does not make an experiment coordinator an
independent, fully blinded adjudicator.
Reports that fail model-identity, input-integrity, or tool/path-isolation checks
are excluded from scoring rather than treated as valid comparisons.
The masked export also locks run artifacts and its complete packet population.
Changing a result, review, packet mapping, or protocol requires a new export;
do not overwrite the old judgments to conceal that change.

Report runtime-defect recall separately from test-oracle/documentation recall,
precision and unresolved findings, false positives on repaired counterparts,
severity calibration, repeatability, completion rate, elapsed time, tokens and
actual credits. Do not equate repeated samples of one PR with independent PRs.
Dimensions without positive reference cases remain unmeasured.

For repetitions, the scorer groups by model **and snapshot**. It preserves each
run's runtime-detection count, reports per-bug hits with scored/scheduled
denominators, and includes means, medians, ranges, and the union of detections.
Unscored trials remain null rather than becoming clean zero-finding reviews.
Parallel-run latency can include provider throttling or host contention; do not
present it as isolated single-model speed. Keep capped and uncapped conditions
in separate experiments.

Hold prompt and tools fixed to isolate model effects. Prompt wording, specialized
worker routing and cross-family verification need separate ablations. Use an
untouched PR holdout before promoting defaults; do not turn one pilot into a
repository-wide model ranking.

## Runner checks

```powershell
python -m unittest discover -s tools\review-eval -p test_review_eval.py
```

These are local recorder checks and make no model calls.
CI runs the same suite, including offline replay of the committed evidence.
To replay only the published adjudications and measurements:

```powershell
python -m unittest discover -s tools\review-eval -p test_review_eval.py -k published
```

The pilot JSON preserves its original scoring record for auditability. Its
partial Opus response is not a complete-review comparison under the hardened
completion checks; the pilot report marks it explicitly. The uncapped study's
twenty reports all satisfy the completed-coverage requirement.
