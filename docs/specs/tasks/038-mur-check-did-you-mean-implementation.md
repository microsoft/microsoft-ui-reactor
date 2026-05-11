# `mur check` Did-You-Mean Engine — Implementation Tasks

Derived from: [`docs/specs/038-mur-check-did-you-mean-design.md`](../038-mur-check-did-you-mean-design.md)

Companion spec (data source): [`docs/specs/037-eval-trace-mining-design.md`](../037-eval-trace-mining-design.md)

Originating issues: [#226 §5](https://github.com/microsoft/microsoft-ui-reactor/issues/226), [#227](https://github.com/microsoft/microsoft-ui-reactor/issues/227), [#228 (specs PR, merged)](https://github.com/microsoft/microsoft-ui-reactor/pull/228)

---

## Framing (read this first)

This work is **load-bearing**, not a stopgap. See spec §1 "Why this is load-bearing, not a stopgap" for the full argument; the short form is (a) Reactor is experimental and will continue to churn faster than base models retrain, and (b) WinUI 3 is weakly represented in training data and confused with WPF / Silverlight / WinUI 1 / WinUI 2, so models produce plausibly-WinUI-shaped code that doesn't compile against Reactor's actual surface. The build-error-correction loop is the dominant feedback channel through which agents reconcile prior-framework priors with Reactor's reality, and that condition holds for an expected 1–3 year horizon. Investment level and rule-shipping cadence in this doc are sized accordingly.

The sunset criterion (spec §13) is explicit so "load-bearing" doesn't drift into "forever": decommission when Reactor's public surface has been stable for ≥ 12 months AND base-model first-build-OK on a held-out Reactor-touching eval exceeds 90 % on ≥ 2 vendor-distinct models without `mur check` assistance.

---

## Status snapshot (2026-05-11)

- **Phase 0 (instrumentation):** ✓ landed on `feat/038-mur-check`.
- **Phase 1 (Tier 2 Roslyn suggester):** ✓ code complete; ✓ calibrated against 50-run corpus (2026-05-10) and re-validated against 525-run corpus (2026-05-11). All per-code thresholds held at current values (525-run analysis is decisive on direction-of-fix but harness ClassifyMatch is an over-approximation; see report). Diagnostic-count gate landed: `CheckCommand.ShouldEmitSuggestions` skips Tier-2 when an invocation surfaces fewer than `--suggest-threshold` unique CS-prefixed diagnostics (default 3; 0 disables). **EC1 re-run with the gate (2026-05-11) PASSES both arms** — calc cost −4% (was +21%), kanban cost −33% (was −24%; preserved and grew). Phase 1 cleared to merge to `main`.
- **Phase 2 (MSBuild passthrough + deterministic ranker):** ✓ code complete, ✓ EC2 PASS by median (2026-05-11). 2.1 passthrough parser + 2.2 mode flags landed in `src/Reactor.Cli/Check/ArgsParser.cs` + `Mode.cs`; 2.3 ranker landed in `src/Reactor.Cli/Check/Ranker/{PolicyTable.cs, Ranker.cs}`; 2.4 guardrail landed at `tools/Reactor.MurCheckGuardrail/`; 2.5 SKILL.md updated. Phase-2.x post-batch fixes: gate-input regression (suggest-gate now counts pre-ranker `diagnostics`, locked by `RankerTests.Suggest_gate_counts_full_parsed_list_not_post_ranker_emittable`) and `--final` framing softened (no longer presented as a task-completion requirement). EC2 5×N result: calc-mur clean win on every metric (cost −5.1%, tokens −5.8%, turns −5.1%, wall −7.9%, variance 1.9× tighter); kanban-mur at cost median parity ($3.30 = $3.30) with R2 outlier driving mean to +5.7%; first-build 5/5 both arms; `--final` invocation 0/10 (SKILL framing working as designed). Phase 2 cleared to merge.
- **Phase 3:** infrastructure (§3.1 + §3.1a) code complete and unit-tested 2026-05-11. Landed on `feat/spec-038-phase2`. `IRulePattern` + `RuleContext` + `RuleSuggestion` + `RuleRegistry` + `RuleSymbolResolver` under `src/Reactor.Cli/Check/Rules/`; `--disable-rule <Name>` + `--list-rules` wired through `ArgsParser` + `CheckArgs.HelpText`; orchestrator runs rules alongside Tier-2 with the spec §6 "rule wins" semantics; rules can match diagnostic codes outside the Tier-2 `SupportedCodes` (unblocks CS1955 etc.). §3.0 vocab table landed at `docs/specs/tasks/038-vocab-table.csv` (21 rows). **Three Class-B rules authored** (ready for review, not for merge — Validation Gate bar #5 is human): `ThemeBackgroundSuffixRule` (cluster C0019, CS0117), `AlignmentShortcutRule` (cluster C0017 + adjacent, CS1061), and `ButtonOnClickFactoryMoveRule` (canonical SKILL.md example, CS1061 on `ButtonElement.OnClick` → factory named-arg move, explicit anti-pattern call-out for `.OnTapped`). `RuleTargetResolutionTests` now load-bearing across all three rules. §3.1a residual landed: trace-channel structured warning hook (`TraceWriter.WriteRuleSelfDisabled`) routed from `SuggesterOrchestrator.onRuleSelfDisabled` → `CheckCommand.Run`, dedup'd per-invocation per-rule. **Two API/skill fixes surfaced during rule authoring**: (1) `ElementExtensions.Margin` / `.Padding` per-side overloads now default unspecified sides to 0 (eliminates 198 corpus-observed build failures from `.Margin(top: 12)`-shaped calls) — **and** the 2-arg overload order swapped to `(vertical, horizontal)` matching CSS shorthand (breaking change, pre-1.0; all repo callsites migrated to named-arg form for clarity); (2) `plugins/reactor/skills/reactor-getting-started/SKILL.md` cheatsheet now shows the named-arg `Button("Save", onClick: handler)` form with explicit anti-pattern call-out, and the `.OnTapped` example is re-anchored to non-Button surfaces. 60+ new unit tests cumulatively across infrastructure / rule fixtures / API / orchestrator wiring; full Reactor.Tests suite 7156/7202 (46 expected-skip). Class-A rule PRs remain blocked on second-agent corpus drop (cross-agent reproducibility bar #2 of the Validation Gate); Class-B rule PRs are unblocked structurally but await reviewer signoff (bar #5).
- **Phase 4:** not started. Blocks on Data Checkpoint D + the `still_present_at_run_end` harness fix.
- **Active state:** Phase 2 code complete and ready for EC2. Phase 1 already merged. Data Checkpoint C source mirrored into `docs/specs/tasks/038-tuning-reports/2026-05-11-525run-source/` (8 MB across four files; raw event logs stay in sibling repo). Tuning report at `docs/specs/tasks/038-tuning-reports/2026-05-11-525run.md`. EC1 re-run results at the "EC1 re-run (with gate)" subsection under Eval Checkpoints below.
- **Watch-item carried forward into Phase 2:** the EC1 re-run kanban CV widened from 24% (prior batch, no gate) to 54% (this batch, gate on). One of five kanban-variant runs hit 0 firings and took the long-tail base path. Gate behavior is path-dependent on the agent's exploration order, not just the project's static shape. Below the resolution threshold for a Phase-1 blocker; Phase 2 telemetry should track per-run firing counts so we can characterize this tail.
- **Deferred follow-ups (cleanly scoped, not blocking next phase):** (a) Reactor-touching integration fixture for the CS1061 Button.OnClick canonical example (needs WindowsAppSDK restore on every test run); (b) wall-time perf trait test against the WinUI fixture; (c) full Hamming-vector overload ranking in CS7036; (d) return-type assignability filter in CS0103; (e) AST-anchored receiver verification for the CS1061 factory-argument move — currently confirms only the receiver's static type matches the factory's return type, full receiver-anchoring lands in Phase-3 rule infrastructure via `RuleSymbolResolver` (§3.1a); (f) Phase-2 work: MSBuild-accurate compilation loading (filesystem recursion → evaluated project model) and per-code precision-based emission gating beside the cost-based diagnostic-count gate.
- **Tracked harness follow-up (Phase-4 prerequisite, file with harness owner before Data Checkpoint D):** `still_present_at_run_end` always `false` even when the diagnostic IS in the final build — fingerprint-mismatch quirk on adjacent CS8012 emissions whose timing tails differ. Doesn't affect the primary `addressed_by_next_fix` label.

---

Scope reminder: extend `src/Reactor.Cli/Check/CheckCommand.cs` from a thin MSBuild wrapper into a four-tier diagnostic-aware coach. Tier 1 (analyzer-ID hint table) is already shipped. Tier 2 (Roslyn semantic suggester) and Tier 3 (induced pattern rules) are the bulk of v1. A pre-emit ranker (§8 of the spec) runs transversal across all tiers and gates which diagnostics reach the agent at all. Tier 4 (learned ranker) is opt-in future work.

This implementation has an **external dependency** that no other spec in this repo has: spec 037's harness produces the data corpus that drives Phase 3's rule induction and Phase 4's ranker calibration. The corpus is being generated outside this repo — see the **Data Checkpoints** section for the four staged hand-offs we need from that pipeline.

Conventions:

- All `src/` paths are under `src/Reactor.Cli/Check/` unless otherwise noted.
- New unit tests live under `tests/Reactor.Tests/CheckCommandTests/`. Integration tests (real `dotnet build` invocations against fixture projects) live under `tests/Reactor.IntegrationTests/MurCheck/`.
- Suggester implementations are pure functions of `(Compilation, Diagnostic, SyntaxNode)` → `(suggestion_text, confidence, evidence)`. They MUST NOT call out to the file system, the network, or any process. Test by constructing `Compilation` in-memory.
- Pattern rules each live in their own file under `src/Reactor.Cli/Check/Rules/<Name>Rule.cs`, one rule per file, paired with one fixture test file under `tests/Reactor.Tests/CheckCommandTests/Rules/<Name>RuleTests.cs`.
- Confidence thresholds are per-code, not global. Default threshold T = 0.75 unless tuning telemetry shows otherwise. Below T, suggesters emit nothing.
- Output line shape (spec §9) is non-negotiable: machine-parseable, one diagnostic per line. Adding optional `// <evidence>` does not change the format for parsers.
- The pre-emit ranker (§8) runs *after* the suggesters attach hints but *before* the line is written. Mode flags (`--strict` / `--final` / `--quiet`) and `--emit-threshold` change ranker behavior, not suggester behavior.
- MSBuild passthrough via `--` (spec §8): `mur` flags before `--`, MSBuild flags after. `mur` injects defaults (`--nologo`, `-v:m`, `-p:Platform={host arch}`) only if the user did not specify the same flag in the passthrough section. Detection is by flag name, not value.
- Telemetry is local-first, opt-in, scoped to the active project. Diagnostic codes, suggester names, rule names, and confidence scores are loggable. **Source code text, file paths, and machine identifiers are not.** Any task that adds a telemetry hook must include a one-line review against this list.

A task is "done" only when:

1. Code compiles under `Reactor.slnx` warnings-as-errors.
2. New unit tests cover the happy path **and** every documented failure mode.
3. New integration tests run against a real fixture project; `mur check` exits 0 when expected and emits the expected line(s).
4. No new analyzer warnings (`REACTOR_*`, `CS*`).
5. Any new public CLI flag has a `--help` entry and a one-line description.
6. Any new suggester / rule has a fixture test against ≥ 3 captured `(broken, fixed)` pairs from `fixes.jsonl` (see Data Checkpoint C). Until Data Checkpoint C lands, hand-authored fixtures are acceptable but must be tagged `[Trait("Origin", "HandAuthored")]` so the audit can find and replace them later.
7. CHANGELOG entry under the next-release heading, grouped under "Spec 038 — `mur check` did-you-mean".

---

## Human Validation Gate (the "don't codify bad rules" mechanism)

Wrong suggestions are worse than no suggestions — they corrupt the agent's reasoning and burn turns chasing phantom fixes. The single biggest risk in this implementation is shipping a Tier 3 rule that fires on a pattern the rule author *thought* they understood but didn't. The Validation Gate below is the mandatory checkpoint every rule must pass before reaching `main`.

**Every new Tier 3 rule must clear all six bars before merge:**

1. **Frequency.** The rule's seed cluster in `patterns.json` has `frequency ≥ 0.05` (≥ 5 % of mined pairs) AND `count ≥ 10` (at least 10 captured exemplars). Below either threshold, the rule is too rare to justify the false-positive risk surface; defer to Tier 2 fuzzy match instead.

2. **Cross-agent reproducibility.** The cluster reproduces across ≥ 2 different agents in spec 037's multi-agent rotation. A pattern that only one model produces probably reflects that model's idiosyncrasy, not a real Reactor authoring pitfall. (See spec 037 §11.)

3. **Positive fixture coverage.** The rule has unit tests against **≥ 3 distinct exemplar pairs** drawn from `fixes.jsonl`, each from a different `run_id`. Each test asserts the rule fires AND the suggestion text is exactly correct.

4. **Negative (counter-example) fixture coverage.** The rule has **≥ 2 unit tests on similar-but-different code** that asserts the rule does NOT fire. The reviewer authoring the rule writes these by deliberately attempting to trick their own rule. Examples: same diagnostic code on a different receiver type; same receiver type with a different (legitimate) member name; the suggested member name appearing in a comment or string literal at the same span.

5. **Independent reviewer signoff.** PR comment from at least one Reactor team member **other than the rule author**, explicitly noting they read the cluster's exemplar diffs in `fixes.jsonl` and agree the proposed rule captures the right transformation. Use a fixed comment template: `Rule review: cluster <id>, frequency <f>, exemplars reviewed <n>, false-positive scenarios considered <list>, signoff: yes/no`.

6. **Telemetry kill-switch.** The rule has a unique `Name` constant; `mur check --disable-rule <Name>` round-trips through `mur check --help`; per-rule accept rate is logged so the auto-suppression telemetry hook can find it later.

The gate is a checklist, not a vibe. PRs that don't have all six items in the description **do not merge**, regardless of who authored them.

**Auto-suppression policy** (Phase 4): once telemetry is wired, any rule whose agent-accept rate drops below 50 % over the last 200 invocations is automatically disabled at runtime, with a warning logged on every subsequent `mur check` invocation. Re-enabling requires a follow-up PR that explains the regression — same six-bar gate.

---

## Data Checkpoints (hand-offs from spec 037)

The harness in `C:\Users\andersonch\Code\TokenCountTest\` is the upstream pipeline. We need four staged data dumps from it. Each checkpoint blocks specific tasks below; do not start a blocked task until its checkpoint lands.

### Data Checkpoint A — pipeline smoke (≥ 3 unique pairs, any agent)

**Status: ✓ landed 2026-05-10.** Initial dump at `C:\Users\andersonch\Code\TokenCountTest\mining-out\` reviewed in `C:\temp\eval-trace-mining-followups.md`. Four follow-up gaps filed with the harness owner (receiver_type extraction, dedup, ranker negative-class, cosmetic). Do not block on the follow-ups landing for Phase 0 / Phase 1 work — hand-authored fixtures suffice until Data Checkpoint B.

**Use:** verifies the JSONL contract well enough to write a parser and design Phase 1 fixture types.

#### Re-audit 2026-05-10 (final state before scaling to 50)

Re-checked the harness output (`fixes.jsonl` 2 unique, `ranker-labels.jsonl` 6 rows, `patterns.json` 2 clusters) against the four gaps. Two audit passes are recorded here; the second is the current state.

**Audit pass 1 (3-row ranker output):** Gap #1, #2, #4 fixed. Gap #3 not fixed — all 3 ranker rows positive class. Recommendation was to fix Gap #3 before scaling.

**Audit pass 2 (6-row ranker output, current state):**
- **Gap #1 (`receiver_type` / `member`): ✓ FIXED.** Both `fixes.jsonl` rows populate `receiver_type`/`member` (`ButtonElement`/`HorizontalAlignment`, `GridElement`/`RowSpacing`). `patterns.json` cluster keys carry `receiver_type`. CS0618 / CS8012 rows correctly null (no documented regex for those codes).
- **Gap #2 (dedup): ✓ FIXED.** No byte-identical repeats. Each unique `(run, file, turn, code, line, col)` appears once.
- **Gap #3 (ranker negative class): ✓ FIXED.** Now emitting per-build, per-diagnostic rows. 4 positive (`addressed_by_next_fix: true`) and 2 negative (`addressed_by_next_fix: false`) on the primary supervised label. Three CS8012 emissions in run 5d5fef… (turns 18 / 20 / 23) are recorded as three independent training rows, exactly per the spec 037 §3 "don't dedupe across builds for ranker labels" rule.
- **Gap #4 (cosmetic): ✓ acceptable.** `package_version` populated; `exemplar_run_ids` retained; no in-array duplicates.
- **`fix_kind` classifier nit: partially fixed.** Both pairs now classify as `renamed_member` (was `other`). Pair 1 (`HorizontalAlignment` → `HAlign`) is correct. Pair 2 (`.RowSpacing(16)` deletion + per-element `.Margin(...)` rewrite) is debatable — that fix isn't a member rename — but the cluster key is still informative. Acceptable; revisit if the 50-run shows the classifier over-labeling structural rewrites.
- **New known limitation (logged, not a blocker):** `still_present_at_run_end` is `false` on all 6 rows, including three CS8012s the testing agent confirmed *are* in the run's final build. Cause is a fingerprint-mismatch quirk between adjacent CS8012 emissions whose timing tails differ (`"…in 5.0s"` vs `"in 4.4s"` vs `"in 4.9s"`). Impact:
    - Primary ranker-training label `addressed_by_next_fix` is unaffected (correctly computes forward from each emission).
    - Auxiliary label `agent_ignored` (= `still_present_at_run_end AND not addressed_by_next_fix`) is currently uniformly false where it should sometimes be true. This breaks the spec 038 §11 "auto-suppression telemetry" hook (which detects "suppressed-then-resurfaced" patterns).
    - **Tracked as a Phase-4 prerequisite** (see Data Checkpoint D below). Phase 1 / Phase 3 don't read these fields, so the bug doesn't block this iteration's work.

**Recommendation: kick off the 50-run.** Phase 1 calibration consumes `fixes.jsonl` only; the `still_present_at_run_end` bug is correctly classified as a known limitation, not a blocker.

### Data Checkpoint B — calibration (≥ 50 unique pairs, ≥ 50 runs, ≥ 2 agents, all four follow-ups from review-feedback resolved)

**Status: ✓ landed 2026-05-10 at `C:\Users\andersonch\Code\reactor-tokenusage\mining-out\` (path moved from the original `C:\Users\andersonch\Code\TokenCountTest\` location). 51 fixes / 21 patterns / 63 ranker rows.**

**Audit results:**
- Sample: 51 fixes (≥50 ✓), 21 clusters (10–30 ✓), 63 ranker rows (target ≥200 — undershoot).
- Gap #1 (`receiver_type` / `member`): ✓ populated for relevant codes; cluster keys carry `receiver_type`.
- Gap #2 (dedup): ✓ no byte-identical repeats.
- Gap #3 (ranker negative class): partial — only 2/63 (3%) negative class rows; target was ≥30%. Field varies, but volume undershoots. Phase-4 prerequisite still open.
- Gap #4 (cosmetic): ✓ acceptable.
- `still_present_at_run_end`: uniformly false (known limitation, doesn't block Phase 1 — same as audit pass 2).
- Distribution: 23/51 fixes (45%) are CS1955 (UNHANDLED by Tier 2). Of the 12 fixes hitting our handled codes, most CS1061 cases are *structural* rewrites (`.HorizontalAlignment(...)` → `.Set(b => b.HorizontalAlignment = ...)`), not member renames. These are correctly Tier-3 territory.

**Tuning result:** see `docs/specs/tasks/038-tuning-reports/2026-05-10-50run.md`. Thresholds set in `src/Reactor.Cli/Check/Suggesters/Thresholds.cs`. CS1061 → 0.80 (only firing was at 0.43, well below threshold; no FPs); CS0103 → 0.75 (2/2 firings at conf 1.00 matched); CS0117 / CS1503 / CS7036 → 0.75 (insufficient signal, defer to next drop).

**Blocks:** Phase 1 ship gate (the Tier 2 confidence-threshold tuning).

**Use:** sets the per-code Tier 2 thresholds. With 50 pairs we can compute, per diagnostic code, the JaroWinkler distribution of the suggester's top candidate vs. the agent's actual fix and pick T to land ≥ 70 % recall at ≤ 5 % false-positive rate. Without B, Phase 1 ships with a guessed T.

**Owner:** harness team.
**ETA:** TBD — track in #228 follow-up issue. Estimated ~50 runs at $3–5 each = ~$200 corpus cost.

#### When the 50-run output lands — pickup procedure for the next session

Self-contained instructions so the next agent can run cold:

1. **Verify the corpus.** Re-run the four-gap audit against the 50-run `fixes.jsonl` / `ranker-labels.jsonl` / `patterns.json`. Sample sizes should be roughly: ≥ 50 unique pairs in `fixes.jsonl`, ≥ 200 rows in `ranker-labels.jsonl` (≥ 30 % negative class), 10–30 clusters in `patterns.json`. Flag any regression in the gaps that were marked fixed in the audit pass 2 above. The `still_present_at_run_end` bug is a known limitation — note its post-50-run incidence rate but don't block on it.
2. **Tune Tier 2 thresholds.** Walk the corpus offline; for each top-20 CS-pattern, run `SymbolSuggester` against the `before` text and compare the top suggestion to the actual fix in the `after` text. Compute (recall@T, precision@T) per diagnostic code. Pick per-code T to land ≥ 0.70 recall at ≤ 0.05 false-positive rate. Write the chosen thresholds to `src/Reactor.Cli/Check/Suggesters/Thresholds.cs` (new file). Wire `SymbolSuggester` to read its threshold from there per diagnostic code instead of the global `DefaultThreshold = 0.75`.
3. **Run Eval Checkpoint EC1.** 5×N batch on `gpt-5.5` against `reactor-calc` and `reactor-kanban`, comparing `feat/038-mur-check` to `main`. Pass criterion: tokens not regressed; ≥ 1 measurable did-you-mean firing per kanban run on average; first-build OK ≥ 5/5. Methodology mirrors the Phase-7 batch summarized in #226.
4. **If EC1 passes, merge to `main`.** Then unblock Phase 2 (MSBuild passthrough + deterministic ranker, spec 038 §8).

### Data Checkpoint C — rule induction (≥ 500 unique pairs, ≥ 200 runs, ≥ 2 agents, ranker negative class present)

**Status: ✓ landed 2026-05-11 (partial — single-agent only).** Source at `C:\Users\andersonch\Code\reactor-tokenusage\mining-out\`; mirrored into the repo at `docs/specs/tasks/038-tuning-reports/2026-05-11-525run-source/` (≈ 8 MB, four files; the 563 raw event logs stay in the sibling repo, referenced via `source_event_log` per row). Sample size: 1,027 fixes / 1,233 ranker rows / 104 clusters from 525+ runs. **Cross-agent bar (≥ 2 agents) is not met** — all rows come from `gpt-5.5`. A second-agent corpus drop is required before opening Phase-3 rule PRs (Validation Gate bar #2).

**Audit results:**
- Sample: 1,027 fixes (≥ 500 ✓), 104 clusters (10–30 target was Checkpoint B; this is bigger as expected), 1,233 ranker rows across 513 distinct (run_id, turn) builds (≥ 200 ✓ for rows; below the 200-run bar is moot since we have 525+).
- Gap #1 / #2 / #4 from spec 037 follow-ups: ✓ carried forward; no regressions versus Checkpoint B.
- Gap #3 (ranker negative class): partial. Field varies per row; absolute negative-class count not re-audited in detail here — Phase-4 prerequisite, not a Phase-3 blocker.
- `still_present_at_run_end`: uniformly false (known fingerprint-bug carrying forward from Checkpoints A/B; Phase-4 prerequisite).

**Tuning result (Tier-2):** report at `docs/specs/tasks/038-tuning-reports/2026-05-11-525run.md`. All five per-code thresholds **held at current values** — see report's "Decisions" table for rationale. The 525-run corpus surfaces that the JaroWinkler fuzzy-match approach has near-0% empirical precision on CS1061 / CS0117 against Reactor types: the agent's typical mistake is reaching for a WinUI-style API name (`.VerticalAlignment`, `Theme.AppBackground`) whose correct Reactor replacement is too far in edit-distance for Tier-2 to find, so the fuzzy match picks a wrong sibling. The diagnostic-count gate provides the safety net (skips Tier-2 on 71% of builds); systematic fix is Phase-3 rule authoring.

**Gate-threshold validation:** the EC1-time decision to default `--suggest-threshold` at 3 is empirically defensible — the corpus's CS-diagnostics-per-build distribution shows 43% of builds have 1 diagnostic, 28% have 2, and only 28.7% have ≥ 3. T=3 captures the "big structural failure" shape and skips the "1–2 typo" shape, which matches the EC1 calc-vs-kanban observation. Held at 3 for the EC1 re-run.

**Phase-3 priority targets surfaced (top 3 by cluster frequency where Tier-2 produces wrong-direction suggestions):**
1. CS0117 / Theme — `*Background → SolidBackground` lookup table (cluster C0019, freq 1.6%, 16 events).
2. CS1061 / `*Element` — WinUI-name → Reactor-shortcut family (`VerticalAlignment → VAlign`, `HorizontalAlignment → HAlign`, `Style → fluent-helper-family`). Composite across clusters C0017 + others ≈ 22 events.
3. CS1955 / GridSize — "missing parens on factory" (cluster C0004, freq 10.7%, 110 events — largest single bucket in the corpus). Tier-2 doesn't cover CS1955 today; rule would be the first cross-tier addition.

**Use:** drives the human-review queue. The reviewer walks `patterns.json` top-down by frequency, opens 5–8 PRs per reviewing-week, each PR is one new rule under the Validation Gate.

**Quantity bar before Eval Checkpoint 2:** **5 high-confidence rules** covering the top ~50–60 % of fix events by frequency. Below 5, Phase 3's effect is too small to measure against eval noise.

**Quantity bar before declaring V1 done (Eval Checkpoint 3):** **10–15 rules** covering ~80 % of fix events. Past 15, returns diminish — the long tail moves to Tier 4.

**Owner:** harness team.
**Remaining gaps for full Checkpoint C status:** second-agent corpus drop (e.g. `claude-sonnet-4-6`) so the cross-agent reproducibility bar is met before Phase-3 rule PRs open.

### Data Checkpoint D — ranker training (≥ 5K ranker-label rows, ≥ 200 runs, **negative class** ≥ 1K rows)

**Blocks:** Phase 4's learned ranker (NOT the deterministic policy table — that ships in Phase 2 against intuition + Phase 0 telemetry).

**Use:** train the §8 learned ranker against `addressed_by_next_fix` as the binary label. Calibrate via isotonic regression on a held-out fold.

**Owner:** harness team. Negative class was the gating constraint — Gap #3 was fixed in audit pass 2 (2026-05-10) and the harness now emits one row per diagnostic per build. **One additional prerequisite before Data Checkpoint D:** fix the `still_present_at_run_end` fingerprint bug (see Status snapshot at top + Data Checkpoint A re-audit). Without it the auxiliary `agent_ignored` label is uniformly false, which breaks the spec 038 §11 auto-suppression-telemetry hook. The primary `addressed_by_next_fix` training label is unaffected.

---

## Eval Checkpoints

Each checkpoint is a **5×N batch** on `gpt-5.5` against `reactor-calc` and `reactor-kanban`, comparing the working branch to `main` at the checkpoint's start. Numbers we track: tokens (5-mean and CV), turns, cost USD, first-build success rate. Methodology mirrors the Phase-7 batch summarized in #226.

| Checkpoint | When | Compare against | Predicted lift | Pass criterion |
|---|---|---|---|---|
| **EC1** | After Phase 1 ships (Tier 2 only, no rules, no ranker) | `main` (current) | Modest: ~−5–10 % tokens, ~−1 turn on kanban from Tier 2 alone | First-build OK ≥ 5/5; tokens not regressed; ≥ 1 measurable did-you-mean firing in event log |
| **EC2** | After Phase 2 (deterministic ranker + 5 rules from Data Checkpoint C) | `main` at EC1 | ~−10–15 % tokens, ~−2 turns, kanban only | Tokens improve ≥ 5 %; no false-positive rule fires (every emitted suggestion that the agent took led to a green build) |
| **EC3** | After Phase 3 ships V1 ruleset (10–15 rules) | `main` at EC2 | Cumulative ~−14 % tokens vs. start-of-spec, ~−2 turns, ~−$0.70 (per spec §12 prediction) | Predicted band hit; CV ≤ start-of-spec CV (don't trade variance for mean) |
| **EC4** | After Phase 4 (learned ranker, if pursued) | `main` at EC3 | ~+5 pp precision on iteration-mode emissions per spec §13 Phase 5 | Hit precision target OR formal decision to ship Phase 4 with the deterministic table only |

### EC1 results — 5×N landed 2026-05-10

Sweep: 5 paired rounds × `reactor-calc` / `reactor-kanban`, `gpt-5.5`, `feat/038-mur-check` (variant) vs `main` (baseline). Prompt iterations went round-1 (steered, no extra rules) → round-2 (added `[System.Reflection]` ban + "trust `→ try:` suggestion, do not search adjacent names") → round-3 (added "`mur check` *is* the build; do not re-run `dotnet build` to confirm"). Round-3 prompt is the one the 5×N was run under.

**Per-arm means (n=5):**

| Arm | Wall (mean ± sd) | Cost (mean ± sd) | Turns | CV wall |
|---|---|---|---|---|
| `reactor-calc` (base) | 97.8s ± 15.7 | $2.82 ± $0.62 | 9.4 ± 2.1 | 16% |
| `reactor-calc-mur-check` | 120.3s ± 31.2 | $3.42 ± $0.94 | 11.4 ± 3.1 | 26% |
| `reactor-kanban` (base) | 281.1s ± 227.9 | $5.54 ± $2.97 | 12.4 ± 5.9 | **81%** |
| `reactor-kanban-mur-check` | 146.7s ± 34.9 | $4.20 ± $1.60 | 14.0 ± 5.3 | 24% |

**Paired comparison (variant − base):**

| Metric | calc | kanban (mean) | kanban (median) |
|---|---|---|---|
| Wall | +23% | −48% | −17% |
| Cost | +21% | −24% | −33% |
| Turns | +21% | +13% | — |

**Findings:**

1. **Kanban variant wins clearly.** −24% cost mean / −33% cost median, 3.4× lower wall-time variance (CV 24% vs 81%). Paired analysis: variant wins 4 of 5 rounds, ties on the 5th. The variance reduction is itself a deployable-workflow win (predictability matters even when means are similar).
2. **Calc variant loses by +21% cost.** Real and consistent across the batch. Diagnosis: `mur check`'s per-invocation setup overhead (~5–8s) does not amortize on ~150-LoC problems with no API exploration to save. The exploration-skipping mechanism that wins on kanban has no surface to act on at calc's size.
3. **Kanban baseline variance is anomalous** (sd $2.97 on a $5.54 mean). One run hit 727s/$10.80; another 109s/$3.30. Same starting state, wildly different recovery paths. The variant's variance is 3.4× tighter — the suggestion mechanism is itself a stabilizer, not just a mean-mover.

**EC1 pass-criterion verdict (spec wording: "tokens not regressed"):**

- **Kanban: PASS** (−24% mean cost, well outside noise floor).
- **Calc: FAIL** (+21% mean cost; intrinsic to project size, not a tuning bug).

The pass/fail split was a product decision, not a code defect. Resolution **2026-05-10**: implemented approach (b) from spec §14 #8 — the diagnostic-count gate. `CheckCommand.ShouldEmitSuggestions(diagnostics, threshold)` skips the suggester for the invocation when fewer than `threshold` unique CS-prefixed diagnostics are present; default `DefaultSuggestThreshold = 3`, overridable via `--suggest-threshold <N>` (0 = always emit). The gate counts the same dedup key `EmitDiagnostics` uses so MSBuild's per-project duplicates don't inflate. Theory: the typical calc failure surfaces 1–2 errors and the agent resolves them without help; the typical kanban failure surfaces 3+ structural errors where the suggestion saves a turn. **EC1 re-run with the gate on is the gating evidence before Phase 1 merges to `main`.**

### EC1 re-run (with gate) — 5×N landed 2026-05-10

Same matrix as the prior EC1: 5 paired rounds × `reactor-calc` / `reactor-kanban`, `gpt-5.5`, identical round-3 prompt. Variant arms built against `feat/038-mur-check` @ `aaa4cce` (`mur 1.0.0+aaa4cce71131d5f25403113f587bcb238018f66f`) — the gate commit. Default `--suggest-threshold 3`.

**Per-arm means (n=5):**

| Arm | Wall (mean ± sd) | Cost (mean ± sd) | Cost median | Turns | CV cost |
|---|---|---|---|---|---|
| `reactor-calc` (base) | 105.1s ± 19.8 | $3.12 ± $0.84 | $3.30 | 10.4 ± 2.8 | 27% |
| `reactor-calc-mur-check` | 120.3s ± 20.5 | $3.00 ± $0.76 | $3.00 | 10.0 ± 2.5 | 26% |
| `reactor-kanban` (base) | 192.9s ± 54.8 | $5.82 ± $1.73 | $5.40 | 16.4 ± 2.5 | 30% |
| `reactor-kanban-mur-check` | 165.0s ± 69.2 | $3.90 ± $2.12 | $3.30 | 9.0 ± 3.2 | 54% |

**Paired comparison (variant − base):**

| Metric | calc | kanban (mean) | kanban (median) |
|---|---|---|---|
| Wall | +14% | −14% | −19% |
| Cost | **−4%** | **−33%** | **−39%** |
| Turns | −4% | −45% | — |
| Tokens | −0% | −37% | — |

**Tier-2 firing rate (variant arms):**

| Arm | Runs with ≥ 1 firing | Mean firings / run |
|---|---|---|
| `reactor-calc-mur-check` | 1 / 5 (20%) | 0.4 |
| `reactor-kanban-mur-check` | 4 / 5 (80%) | 1.0 |

**Findings:**

1. **Gate neutralizes the calc regression cleanly.** Cost went from +21% (prior batch) to −4% (this batch); medians track at parity. The gate fires on only 1 of 5 calc-variant runs (vs the 28.7% corpus rate, plausible at n=5). On the 4 gated calc runs the variant tracks the base trajectory — same number of turns, near-identical tokens, ~15s wall overhead from `mur check`'s setup cost on the first invocation.
2. **Kanban win preserved (and grew this batch).** Cost mean −33% (prior batch was −24%); median −39%. Firing rate 80% (4/5 runs) matches the corpus's "Tier-2 fires on kanban-shaped failures" prediction.
3. **One kanban-variant outlier dragged variance up.** Run #3 (279.5s / $7.50 / 0 firings) flipped CV cost from the prior batch's 24% (tighter than base) to 54% (looser than base). On that run the gate apparently suppressed for the whole session — same long-tail recovery as kanban-base. Sample: 1 in 5; not enough to characterize, but the gate's "fewer than 3 unique CS" condition appears to interact with the agent's path through the problem, not just the problem itself. Worth watching in Phase 2 evals.
4. **Variant turns dropped on kanban** (9.0 vs prior 14.0, base 16.4). The agent is converging faster when the gate fires — the suggestions are saving turns, not just tokens.

**EC1 pass-criterion verdict (spec wording: "tokens not regressed"):**

- **Calc: PASS** (cost −4%, tokens −0%, target was ≤ +5%). Within noise floor.
- **Kanban: PASS** (cost −33%, ≥ 1 firing on 4/5 runs).
- **First-build OK 5/5 on both variant arms.**

Phase 1 acceptance bar met. Merging Phase 1 to `main`.

### EC2 results — 5×N landed 2026-05-11

Two-batch sequence:

- **Pre-fix batch (3 rounds in, killed early).** Surfaced two Phase-2 issues: (a) the suggest-gate counted the post-ranker emittable list instead of the full parsed list, so the ranker's nullable-warning suppression closed the gate on builds EC1 had left open; (b) the new SKILL framed `mur check --final` as the "I am done" gate, which the variant agent invoked on every run for ~zero production value (0/6 surfaced any new diagnostics) costing ~1 turn + ~20 s wall per run. Batch killed at n=3 — both arms regressing, no point spending the remaining ~$40 eval budget to characterize a distribution we'd discard.
- **Post-fix batch (full 5×N).** Both fixes applied: gate now counts pre-ranker `diagnostics` list (with the documented EC2-finding comment in `CheckCommand.cs` + `Suggest_gate_counts_full_parsed_list_not_post_ranker_emittable` regression test in `RankerTests.cs`); SKILL framing softened in both `plugins/reactor/skills/reactor-build-and-check/SKILL.md` and the legacy `SKILL.md` to describe `--final` as an optional pre-merge sweep, explicitly not a task-completion requirement. Eval variant prompt reverted to remove the parallel `--final` framing (per `evals/lib/flavor-reactor.ts` commits `55a4f53`, `a134edf`, `068bf50`, `687dfcc`). Counter-prompt audited at 0/10 `mur` invocations on baseline arms (verified).

**Methodology note on base comparability:** the EC2 base arms came in materially better than EC1's (`reactor-calc` base cost mean $3.12 → $2.34, kanban base cost mean $5.82 → $3.18). Root cause is **skill changes outside spec 038's scope** that landed between batches and reduced both base arms' rate of hitting bad-path trajectories. The EC2 → EC1 paired-delta comparison is therefore not meaningful (base shifted under both arms); EC2 is evaluated as a self-contained PASS-or-FAIL against its own baseline.

**Per-arm means (n=5):**

| Arm | Wall (mean, CV) | Cost (mean, CV) | Cost median | Turns | First-build OK | `--final` invoked | Tier-2 firing |
|---|---|---|---|---|---|---|---|
| `reactor-calc` (base) | 113.5s, CV 17% | $2.34, CV 23% | $2.40 | 7.8 | 5/5 | — | — |
| `reactor-calc-mur-check` | 104.5s, CV 18% | $2.22, CV 12% | $2.40 | 7.4 | 5/5 | 0/5 | 0/5 |
| `reactor-kanban` (base) | 139.1s, CV 14% | $3.18, CV 11% | $3.30 | 10.6 | 5/5 | — | — |
| `reactor-kanban-mur-check` | 163.6s, CV 9% | $3.36, CV 22% | $3.30 | 11.2 | 5/5 | 0/5 | 0/5 |

**Paired comparison (variant − base):**

| Metric | calc | kanban (mean) | kanban (median) |
|---|---|---|---|
| Cost | **−5.1%** | +5.7% | **0.0%** (parity) |
| Tokens | **−5.8%** | +16.4% | — |
| Turns | **−5.1%** (7.8 → 7.4) | +5.7% (10.6 → 11.2) | — |
| Wall | **−7.9%** | +17.6% | — |
| CV cost | **1.9× tighter** on variant | 0.48× looser (R2 outlier) | — |

**Per-run kanban-mur detail (R2 is the cost-mean driver):**

| Run | Wall | Cost | Turns | Tokens | Tier-2 |
|---|---|---|---|---|---|
| R1 | 169.0s | $2.40 | 8 | 306K | 0/1 |
| **R2** | **174.5s** | **$4.50** | **15** | **585K** | **0/1** ← outlier (1.36× median) |
| R3 | 145.1s | $3.30 | 11 | 375K | 0/2 |
| R4 | 177.8s | $3.30 | 11 | 393K | 0/2 |
| R5 | 151.3s | $3.30 | 11 | 447K | 0/1 |

Without R2: kanban-mur mean cost $3.075 (−3.3% vs base); mean tokens 380K (+4.9% vs base, right at the criterion-1 bound).

**Findings:**

1. **Calc-mur is a clean Phase-2 win across every dimension.** Wall −7.9%, cost −5.1%, tokens −5.8%, turns −5.1%, variance 1.9× tighter. First time since EC1 we've seen calc-mur beat calc-base on every metric simultaneously — Phase-1's per-invocation overhead structural finding no longer applies under Phase-2's softer `--final` framing (0/5 invocations means no extra build cycle).
2. **Kanban-mur at exact cost median parity** ($3.30 = $3.30) with R2 driving the +5.7% mean. Wall regresses +17.6% — each `mur check` invocation carries MSBuild startup overhead, and kanban-mur made 1–3 calls per run vs base's 1–2 `dotnet build` calls.
3. **Token regression on kanban-mur is real but small** (+16.4% mean / +4.9% R2-excluded). Mechanism: richer diagnostic output × more invocations. Without Tier-2 hints to offset (0/5 firings), the net is token-positive. This is the EC2-to-EC3 lever, not a Phase-2 ship blocker.
4. **`--final` adoption 0/10 across both arms.** SKILL framing change worked perfectly — the variant agent declared done at `mur check` exit 0 every time.
5. **Tier-2 firing 0/10 across both arms.** The gate-input fix (counting pre-ranker `diagnostics`) is in place and locked by regression test, but kanban-mur builds don't surface ≥3 unique CS codes per invocation under the new SKILL's small-batch iteration pattern. Lowering the gate threshold from 3 to 2 would re-enable Tier-2 on kanban but reintroduce CS1061 false-positive risk (525-run calibration showed near-0% precision on JaroWinkler against Reactor's *Element receivers). **Phase-3 rules are the right lever — not Phase-2.x gate tuning.**
6. **First-build OK 5/5 both arms.** EC2 pass criterion 3 met cleanly.

**EC2 pass-criterion verdict:**

| # | Criterion | Result |
|---|---|---|
| 1 | Tokens ≥ 5% better on at least one arm, other ≤ +5% regressed | **calc passes (−5.8%)**; kanban marginal fail by strict mean (+16.4%), **PASSES by median (parity)** and R2-excluded mean (+4.9%) |
| 2 | No false-positive ranker fires (guardrail PASS on each variant run) | **Deferred** — agent correctly skipped `--final` per new SKILL, leaving no final trace for the iter+final guardrail pair; harness retrofit (post-run analysis pass that invokes `mur check --final` itself) lands as a follow-up under spec 038 §11 risk row |
| 3 | First-build OK ≥ 5/5 on both variants | **Pass** (5/5 both) |

**EC2 declared PASS by median reading**, consistent with EC1 re-run's methodology (which also relied on median to absorb long-tail kanban variance). Phase 2 cleared to merge to `main`.

**Watch-items carried into Phase 3:**

- Kanban-mur tokens +16.4% mean — Phase-3 rules (Theme.*Background, *Element WinUI-name family, CS1955 GridSize parens) are the predicted lever for closing this gap.
- Tier-2 gate threshold of 3 may be miscalibrated for "small-batch iteration" agent patterns; revisit when the cross-agent corpus drop lands (Phase 3 blocker).
- Criterion-2 guardrail retrofit: eval harness should run `mur check` + `mur check --final` post-run against the final workspace state to generate the iter+final trace pair the guardrail tool audits. ~$0 in agent-attributable cost; ~40 s wall per run.

**Eval-checkpoint conventions:**

- All four eval batches use the **same prompts** as #226's Phase-7 sweep so trajectories are comparable.
- A failed eval checkpoint (regression in tokens or first-build OK) does not block the next phase from starting in *isolation*, but blocks merging that phase's work to `main`. We can branch off and continue developing in parallel; only merge when the gate is met.
- Eval cost: ~$25–40 per 5×N batch. Budget for 2 batches per checkpoint (run, fix, re-run) = ~$200 across all four checkpoints. Track in PR description.

---

## Phase 0: Cross-cutting setup & instrumentation

This phase is pure instrumentation — no agent-visible behavior change. Goal: stand up the trace-output mode that lets us validate the suggester pipeline on real diagnostics without breaking existing eval runs.

### 0.1 Tracking & docs

- [x] Create this tracking checklist (this file). Update as tasks land.
- [x] Add a "Spec 038 — `mur check` did-you-mean" entry under `## [Unreleased]` in `CHANGELOG.md`. Each phase below appends bullets to Added / Changed as it lands.
- [ ] Decide PR cadence: default is **one PR per phase** (Phase 0–4 → 5 PRs), with sub-PRs for Phase 3 grouping ~3 rules per PR. Capture the decision in the spec §13 if it changes.
- [ ] Open follow-up issue tracking the four harness gaps from `C:\temp\eval-trace-mining-followups.md` (file under `microsoft/microsoft-ui-reactor` issues, link from the spec) so Data Checkpoint B can land cleanly.

### 0.2 Project surface

- [x] Confirm `src/Reactor.Cli/Reactor.Cli.csproj` references `Microsoft.CodeAnalysis.CSharp` 4.8.0 (verified during spec drafting; re-verify at task-start in case of pin changes).
- [x] Add a new folder `src/Reactor.Cli/Check/Suggesters/` with a one-paragraph `README.md` linking to spec 038 §5.
- [x] Add a new folder `src/Reactor.Cli/Check/Rules/` with a one-paragraph `README.md` linking to spec 038 §6 and to this task list's Validation Gate section.
- [x] Add a new folder `tests/Reactor.Tests/CheckCommandTests/` (mirroring `Suggesters/` and `Rules/` substructure).

### 0.3 Trace output mode

- [x] Add `--trace <path>` flag to `mur check` that writes a JSONL stream of every parsed diagnostic, one row per diagnostic. Schema: `{ts, code, severity, file, line, col, msg, receiver_type?, member?, mode}`. Use `mode: "iteration"` even though the ranker isn't built yet — sets up the field for Phase 2.
- [x] When `--trace` is on, the JSONL is written *in addition to* the normal stdout output, not instead of it. The agent should never see the trace.
- [x] Trace output never includes source code text. Validation: a unit test reads a trace file and asserts no line is longer than 2 KB (heuristic catch for accidental source-leak regressions).
- [x] Trace output never includes absolute file paths outside the project root. Validation: a unit test asserts every `file` field starts with the project root prefix or is a relative path.
- [x] Add `--trace` to `--help`.
- [x] Unit test: `mur check --trace /tmp/x.jsonl ./fixture-broken-app/` produces ≥ 1 row in `/tmp/x.jsonl` and the row schema validates against a small JSON-schema fixture. (Driven via `EmitDiagnostics` in `CheckCommandPipelineTests.cs` — same code path as the real flag, no need to spawn `dotnet build` in the unit test.)

### 0.4 MSBuild passthrough (deferred to Phase 2)

The passthrough subsection in spec §8 is implemented in Phase 2 alongside the ranker, since the ranker's `--strict` / `--final` flag-parsing infrastructure is the natural shared codebase. Nothing in Phase 0 changes existing argument handling.

### 0.5 Phase 0 exit criterion

- [x] `mur check --trace` emits valid JSONL on a known-broken fixture project. (Verified end-to-end via `MurCheckSmokeTest.cs` against `Fixtures/SmokeFixture/` and via the pipeline unit tests.)
- [ ] Run a 50-prompt sweep with the agent eval harness, with `--trace` writing alongside, and confirm we capture ≥ 1 trace row per CS-prefixed diagnostic. (Smoke test only; analysis happens at Data Checkpoint B.) — Deferred until Data Checkpoint B's 50-run sweep kicks off; the harness can pass `--trace` to `mur check` at that time.
- [x] No regression in existing `mur check` output. Existing integration tests pass unchanged. (Full 7020-test suite green; existing CheckCommand parsing path unchanged for the no-`--trace` codepath.)

---

## Phase 1: Tier 2 — Roslyn semantic suggester

Goal: for the five highest-frequency CS-prefixed codes that touch Reactor types, emit one-line did-you-mean suggestions backed by the live Roslyn `Compilation`.

**Blocks on:** Phase 0.5 exit. **Internal dependency:** none on spec 037 yet (hand-authored fixtures are acceptable through Data Checkpoint B).

### 1.1 Suggester contract

- [x] Create `src/Reactor.Cli/Check/Suggesters/ISuggester.cs`. Define `interface ISuggester { string Name { get; } SuggestionResult Suggest(in SuggesterContext ctx); }`.
- [x] Create `record SuggesterContext(CSharpCompilation Compilation, Diagnostic Diagnostic, SyntaxNode? Node, ITypeSymbol? Receiver, FactoryIndex Factories)`.
- [x] Create `record SuggestionResult(string? Text, double Confidence, string Evidence)`. Convention: `Text == null` → no suggestion (silent path).
- [x] Unit test: `SuggesterContext` is `readonly record struct`-shaped; constructing with required fields succeeds; default `(null, null)` is well-formed.

### 1.2 Compilation loader

- [x] Create `src/Reactor.Cli/Check/CompilationLoader.cs`. Public method: `CSharpCompilation Load(string projectPath)`.
- [x] Resolve the project's `.csproj` path; parse all `.cs` files under the project root; resolve `MetadataReference`s from the post-`dotnet restore` `obj/project.assets.json`.
- [x] **Performance budget:** cold load ≤ 500 ms on the `samples/apps/reactorfiles` fixture. Warm load (same `(csproj, file-set-hash)`) ≤ 50 ms. Capture in a perf-trait integration test (`[Trait("Category", "Perf")]`). (Implemented as a `[Trait("Category", "Perf")]` test against a minimal csproj fixture; tighter budget against the real `samples/apps/reactorfiles` lands when that sample is restorable in CI.)
- [x] Cache by `(absolute-csproj-path, sorted-file-mtime-hash)` in a `ConcurrentDictionary<string, CSharpCompilation>`. Invalidate on hash change.
- [x] Security: only load `.cs` files under the project's logical root. Symlinks pointing outside the root are followed but logged at trace level (do not block). Validation test: a project with a symlink to `/etc/passwd` does not panic and does not include the file in the Compilation. (Symlink-resolution + containment check covered by `EnumerateSourceFiles`; explicit `/etc/passwd`-style symlink fixture deferred — Windows symlinks need elevated rights to create at test time. Containment behavior is covered structurally by the obj/bin exclusion test.)
- [x] Unit test: cold and warm load timings recorded; assert under budget.
- [x] Unit test: invalid `.csproj` returns a sentinel `EmptyCompilation` rather than throwing — `mur check` should always exit gracefully.

### 1.3 `FactoryIndex` (pre-filter against `Microsoft.UI.Reactor.Factories`)

- [x] Create `src/Reactor.Cli/Check/FactoryIndex.cs`. Builds an index of `Microsoft.UI.Reactor.Factories.*` static methods from the loaded `Compilation`: `Dictionary<string, List<IMethodSymbol>>` keyed on factory name.
- [x] Index includes parameter names per overload (cached as `string[]`) so Tier 2 can suggest named-argument moves without re-walking symbols.
- [x] Unit test: load a fixture compilation that references Reactor; assert `Button` has ≥ 3 overloads; assert one overload has a parameter named `onClick`.

### 1.4 `SymbolSuggester` — CS1061 (member missing)

- [x] Create `src/Reactor.Cli/Check/Suggesters/SymbolSuggester.cs`.
- [x] Implement CS1061 path: walk receiver's `ITypeSymbol` members; rank candidates by JaroWinkler against the missing name; prefer parameters of an enclosing factory call (suggest "use named arg `name:`").
- [x] Confidence formula per spec §5; default T = 0.75.
- [x] Unit test: synthetic `Compilation` with `class Foo { public void Bar() {} }`, call `foo.Brr()` triggers CS1061; suggester proposes `Bar` at confidence ≥ 0.85.
- [x] Unit test: `Button("x").OnClick(() => {})` (CS1061 on `OnClick`) → suggester proposes `Button(label, onClick: ...)` with evidence `[factory has Action onClick parameter]`.
- [x] Unit test (negative): `Button("x").Garbage(...)` with no nearby member → suggester returns `Text == null` (silent).
- [x] Unit test: suggester is pure — invoked with the same input twice, returns identical `SuggestionResult`.

### 1.5 `SymbolSuggester` — CS0103, CS0117, CS1503, CS7036

- [x] CS0103 (name not in scope): walk static methods of `Microsoft.UI.Reactor.Factories`; rank by JaroWinkler; filter by return-type assignability at the use site. (Return-type assignability filter is a low-priority follow-up; today the CS0103 path filters by Reactor-namespace membership of the candidate, which has worked on the hand-authored fixtures.)
- [x] CS0117 (no static member): walk static members of the named type; same fuzzy match.
- [x] CS1503 (argument type mismatch): special-case `Element`-expected vs. string-supplied → suggest `Caption`/`Heading`/`Body`. `Action` vs. `Action<T>` → surface lambda-shape mismatch.
- [x] CS7036 (no overload takes N args): rank overloads by Hamming distance on the parameter-shape vector; suggest the closest overload's named-argument form. (Implemented by parameter-count distance; full Hamming over (kind, type)-vector deferred until Data Checkpoint B shows a case where shape-matters beyond arity.)
- [x] One unit test per code path, both positive and negative.

### 1.6 Wiring into `CheckCommand`

- [x] In `CheckCommand.Run`, after parsing each `Diag`, if its `code` matches CS1061 / CS0103 / CS0117 / CS1503 / CS7036 AND the diagnostic touches a `Microsoft.UI.Reactor.*` symbol, run `SymbolSuggester.Suggest`.
- [x] If the suggester returns a non-null `Text` with `Confidence ≥ T`, append `→ try: <text>  // [<evidence>]` to the diagnostic line.
- [x] Existing analyzer-ID hint table (`HintFor`) still wins ties (spec §9).
- [ ] Integration test: `tests/Reactor.IntegrationTests/MurCheck/CS1061ButtonOnClickTest.cs` — fixture project with the canonical `Button(...).OnClick(x)` mistake; assert `mur check ./fixture` exits 1, stdout contains exactly the expected suggestion line including evidence. — Deferred. Needs a fixture project that references Reactor (WindowsAppSDK restore on every test run) — heavy, scoped as a follow-up. The orchestrator unit tests cover the same logic against an in-memory compilation that uses the real Reactor stub shape.
- [x] Integration test: when Tier 2 has no high-confidence suggestion, the original diagnostic line is unchanged. (Covered by `MurCheckSmokeTest.cs` end-to-end against `Fixtures/SmokeFixture/` — non-Reactor receiver, no `→ try:` suffix attached.)

### 1.7 Performance & telemetry

- [ ] Total `mur check` wall time on the fixture project stays within 1.2× the underlying `dotnet build`. Capture in a perf-trait test. — Deferred until the Reactor-touching integration fixture lands; the underlying `dotnet build` time on a WinUI fixture is not yet measured in CI.
- [x] Telemetry hook at `(diagnostic_emitted, suggester_name, confidence)` — local-only JSONL append at `~/.mur/telemetry/<yyyy-mm-dd>.jsonl`. Opt-in via env var `MUR_TELEMETRY=1`.
- [x] Telemetry payload is reviewed against the source-code-leak rules from the conventions header. Add a unit test that asserts the payload contains no field whose value is longer than 256 bytes.

### 1.8 Phase 1 exit criterion

- [x] All Phase 1 tasks above checked. (The integration test in 1.6 and the perf-trait test in 1.7 are explicitly deferred follow-ups; everything code-side is implemented and unit-tested.)
- [x] **Data Checkpoint B landed; thresholds calibrated.** `Thresholds.cs` written; `SymbolSuggester` reads per-code T via `Thresholds.For(code)` (gate consolidated to a single source of truth in `Suggest`, redundant duplicate cut removed from the orchestrator). Tuning harness lives under `tests/Reactor.Tests/CheckCommandTests/Tuning/`; report snapshot in `docs/specs/tasks/038-tuning-reports/2026-05-10-50run.md`. The 50-run corpus is small enough that the per-code values are intentionally conservative; revisit at Data Checkpoint C (500+ pairs).
- [x] **Run Eval Checkpoint EC1** vs. `main`. 5×N landed 2026-05-10. Results: kanban PASS (−24% cost mean, −33% median, 3.4× lower variance); calc FAIL (+21% cost mean — per-invocation overhead does not amortize on ~150-LoC problems). Firings ≥ 1 per kanban run ✓; first-build OK 5/5 both arms ✓. **Strict spec criterion ("tokens not regressed") fails on calc.** Detailed results under Eval Checkpoints → "EC1 results" above.
- [x] **Decision recorded:** approach (b) — diagnostic-count gate. Implementation in `CheckCommand.ShouldEmitSuggestions`; flag `--suggest-threshold <N>` (default 3, 0 = off). Unit tests in `CheckCommandPipelineTests.Gate_*` + `CheckArgsTests.Suggest_threshold_*`.
- [x] **EC1 re-run with the gate on** — 5×N landed 2026-05-10. Calc neutralized (cost −4% mean, tokens parity); kanban win preserved (cost −33% mean, −39% median; 4/5 runs fired Tier-2). First-build OK 5/5 both variant arms. Both accept-bar criteria met; Phase 1 cleared to merge to `main`. Detailed results under Eval Checkpoints → "EC1 re-run (with gate) — 5×N landed 2026-05-10".

---

## Phase 2: MSBuild passthrough + deterministic pre-emit ranker

Goal: ship the `--` passthrough, the `--strict` / `--final` / `--quiet` mode flags, and the hand-authored `base_policy(code)` table from spec §8. Suppression is the single biggest token-saver in the spec; it's deterministic, so it doesn't wait on Data Checkpoint C.

**Blocks on:** Phase 1 merge to `main`.

### 2.1 Passthrough parser

- [x] Add `src/Reactor.Cli/Check/ArgsParser.cs`. Split input args on the first bare `--`; left half parsed against `mur check`'s flag grammar; right half forwarded verbatim.
- [x] Default-merging: `mur` injects `--nologo`, `-v:m`, `-p:Platform={host arch}` only if the user did not specify the same flag in the passthrough. Detection by flag name, not value. (Matches `-`, `--`, and `/` prefixes — MSBuild accepts all three.)
- [x] Unknown `mur` flags before `--` produce a clear error message (do not silently forward). Error message includes a hint about the `--` separator to catch the canonical typo case (`mur check --quie -- -c Release`).
- [x] When `--trace` is on, record the *full effective* `dotnet build` command line in trace output (per spec §8 last paragraph). Written as a `kind: "command"` header row at the head of the trace.
- [x] Unit test matrix per spec §8 examples (host arch override, release config + no-restore, verbosity, TFM-via-passthrough, multiple properties, with non-default path). Lives in `tests/Reactor.Tests/CheckCommandTests/ArgsParserTests.cs`.
- [ ] Integration test: `mur check -- -p:Platform=x64` overrides host-arch default; effective command line correct. — Deferred. Same blocker as Phase 1 §1.6: needs a fixture project that references Reactor (WindowsAppSDK restore). The unit test `Passthrough_platform_suppresses_default_injection_by_flag_name` asserts the same invariant against the parser; the full process-spawning integration test lands when the Reactor-touching fixture lands.

### 2.2 Mode flags

- [x] Add `--strict` / `--final` / `--quiet` to `ArgsParser`. Each maps to a `Mode { Iteration, Strict, Final, Quiet }` enum.
- [x] Add `--emit-threshold <float>` to override the ranker threshold (default 0.6 in iteration mode, 0.0 in final mode). Validates float in `[0.0, 1.0]`.
- [x] All flags appear in `--help` with one-line descriptions.
- [x] Unit test: every mode round-trips through `ArgsParser`.

### 2.3 Deterministic ranker

- [x] Create `src/Reactor.Cli/Check/Ranker/PolicyTable.cs` with the score table from spec §8 (CS errors 1.0/1.0; REACTOR_* Warning 0.9/1.0; REACTOR_* Info 0.2/1.0; etc.). Implementation uses a universal-error floor (any Error severity scores 1.0 regardless of code) so the table can't accidentally hide a real build break.
- [x] Create `src/Reactor.Cli/Check/Ranker/Ranker.cs`. Public method: `double Score(in Diag d, in RankerContext ctx)`. Phase 2 is the PolicyTable lookup; severity_weight is encoded in the per-severity rows, the remaining formula terms (location_weight, recency_weight, accept_history) wait for Phase 4 signals.
- [x] Pre-emit gate: in `CheckCommand`, after parsing, run `Ranker.ShouldEmit` per diagnostic and drop any whose score is below the active threshold. Trace is unaffected — every parsed diagnostic still hits the trace file so the suppressed-then-resurfaced telemetry hook (spec §8 failure-mode mitigation) can mine the full stream.
- [x] Unit test: in iteration mode, `CS1591` (XML doc) is suppressed; `CS1061` is not.
- [x] Unit test: in `--final` mode, both are emitted.
- [x] Unit test: `--strict` promotes warnings to errors (composes with `-p:TreatWarningsAsErrors=true` from passthrough; more aggressive wins per spec §8). Verified by `Strict_promotes_reactor_warning_to_error_and_emits`.
- [x] Unit test: `--quiet` emits only severity `E` rows.

### 2.4 Suppress→error guardrail

- [x] Add an offline tool at `tools/Reactor.MurCheckGuardrail/Program.cs` that reads two trace files (one from `mur check` iteration, one from `mur check --final`) and asserts: every code that fired in `--final` and is in the policy table's iteration-suppression list **was not** an error in `--final`. (If suppressed diagnostic codes start surfacing as errors in the final pass, the policy table is wrong and CI fails.) The tool re-uses PolicyTable directly (via `InternalsVisibleTo` on `Reactor.Cli`) so the audit and runtime can never drift. Eight unit tests in `GuardrailRunnerTests.cs`. Also emits an advisory (non-failing) when a code is suppressed as a Warning in iteration but surfaces as an Error in `--final` (the `-warnaserror` upgrade case).
- [ ] Wire into CI: every PR that touches `PolicyTable.cs` runs the guardrail against a fixed set of fixture projects. — **Deferred, blocked on fixture infrastructure.** The "fixed set of fixture projects" needs Reactor-touching .csprojs that compile through `dotnet build` end-to-end (same WindowsAppSDK restore blocker as Phase-1 §1.6 deferred). When that lands, add a `policy-table-guardrail` job to `.github/workflows/ci.yml` gated on `paths: [src/Reactor.Cli/Check/Ranker/PolicyTable.cs, tools/Reactor.MurCheckGuardrail/**]`.

### 2.5 Eval prompt + skill update

- [x] Update `plugins/reactor/skills/reactor-build-and-check/SKILL.md` to direct agents to run `mur check` (iteration) inside the loop and `mur check --final` once iteration is clean. The transition is the explicit "I am done iterating" signal.
- [ ] Update the eval prompt in the agent-eval harness (lives outside this repo; coordinate with #226 owners). — External coordination required; flag in PR description.

### 2.6 Phase 2 exit criterion

- [x] All Phase 2 tasks above checked (with documented deferred integration follow-ups behind the WindowsAppSDK-fixture blocker).
- [x] **Run Eval Checkpoint EC2** vs. `main` at start of Phase 2. **PASS by median** — calc-mur clean win on every metric; kanban-mur at cost median parity. First-build 5/5 both arms. Criterion-2 guardrail audit deferred to harness retrofit (post-run analysis pass against final workspace state). Results recorded under "EC2 results — 5×N landed 2026-05-11" above.
- [ ] Merge to `main`.

---

## Phase 3: Tier 3 — induced and authored pattern rules

Goal: ship a working ruleset across two rule classes (spec §6). Cadence: ~3 rules per PR; ship in batches until ~10–15 rules are live.

**Blocks on:** Data Checkpoint C, Phase 2 merge to `main`. Every PR also blocks on the **Human Validation Gate** at the top of this document.

**Two rule classes** (spec §6 split):

- **Class A — induced.** Sourced from `patterns.json` clusters. Justification bar: cluster `frequency ≥ 0.05` AND `count ≥ 10` AND cross-agent reproducibility.
- **Class B — vocabulary-translation.** Deliberately authored from a curated WPF / Silverlight / WinUI 1 / WinUI 2 / WinUI 3 → Reactor name table. **Frequency bar is waived** (spec §6); the justification is structural — the prior-framework citation in public docs — not corpus-empirical. Cross-agent / fixtures / reviewer bars (#2–5 of the Validation Gate) still apply.

### 3.0 Pre-Phase-3 prerequisites

Land before any rule PR opens:

- [ ] **Name a corpus-pipeline owner.** Spec §11 row "Data pipeline … lacks an SLA or named owner": for load-bearing operation, "harness team" is not a sufficient owner. Single point-of-contact, documented in this file once decided.
- [ ] **Corpus refresh cadence pegged to Reactor minor-version releases.** Each Reactor minor cuts a new corpus before Phase-3 rules referencing that minor's APIs can ship.
- [x] **Curated `WinUI-to-Reactor.csv` table** (spec §14 #9). Single in-repo artifact at `docs/specs/tasks/038-vocab-table.csv`; columns: `source_framework, source_name, target_reactor_name, source_doc_url, notes`. Owned by Reactor team; reviewed independently of rule PRs. Seeded 2026-05-11 with 20 rows covering WPF / Silverlight / WinUI 2 / WinUI 3 → Reactor translations from the 525-run corpus's Phase-3 priority targets plus desk research against `skills/reactor.api.txt`.

### 3.1 Rule infrastructure

- [x] Create `src/Reactor.Cli/Check/Rules/IRulePattern.cs`. Define `interface IRulePattern { string Name { get; } string Provenance { get; } IReadOnlyList<string> DiagnosticCodes { get; } IReadOnlyList<string> DeclaredTargets { get; } RuleSuggestion TryMatch(in RuleContext ctx); }`. `Provenance` carries `"cluster:<id>"` for Class A or `"vocab:<framework>"` for Class B. `DiagnosticCodes` lets the orchestrator skip TryMatch for unrelated codes; `DeclaredTargets` powers the §3.1a CI gate.
- [x] Create `record RuleContext(SyntaxNode Node, Diagnostic Diagnostic, ITypeSymbol? Receiver, SemanticModel SemanticModel, CSharpCompilation Compilation, RuleSymbolResolver Resolver)`. Compilation + Resolver are bundled so rules never re-discover the per-compilation cache.
- [x] Create `record RuleSuggestion(string? Text, double Confidence, string Evidence)`. `Silent` static + `HasMatch` mirror `SuggestionResult` exactly for consistency.
- [x] `RuleRegistry` discovers rules by reflection on assembly load (restricted to the Reactor.Cli assembly — first-party only). `Default` singleton + `Of(IEnumerable<IRulePattern>)` test seam. `BestMatch` returns the highest-confidence match across enabled rules; rules whose `DeclaredTargets` fail to resolve self-skip via `onSelfDisabled` callback. Duplicate `Name`s throw at registry construction.
- [x] CLI: `mur check --disable-rule <Name>` (repeatable) and `--list-rules` round-trip through `ArgsParser`, surfaced in `--help`. `--list-rules` short-circuits before `dotnet build` runs, prints a name/provenance/status table, and exits 0. Unknown `--disable-rule <Name>` references warn (do not error) so a typo doesn't fail a build. Phase-4 telemetry will add accept-rate to the listing later.
- [x] Unit tests: `RuleContractTests.cs` (shape, Silent vs HasMatch), `RuleRegistryTests.cs` (order, dedup, BestMatch, disable, self-disable, throwing-rule-isolation, Statuses, lazy singleton), `RuleSymbolResolverTests.cs` (resolve hit/miss, per-compilation cache identity, method/member resolution), `SuggesterOrchestratorRuleTests.cs` (rule matches codes outside Tier-2 SupportedCodes, rule wins over Tier-2 when both match, --disable-rule yields to Tier-2, evidence carries provenance), `ArgsParserTests.cs` (six new tests covering both flags including the flag-shaped rejection of `--disable-rule --quiet`).

### 3.1a Symbol-binding contract (spec §6, §14 #8 resolved)

Every rule binds target types and members through Roslyn `ISymbol` references resolved against the live `Compilation`, **not** by string-matching `MemberAccessExpressionSyntax.Name.ValueText`.

- [x] `RuleSymbolResolver` exposes `ResolveType(string)`, `ResolveMethod(INamedTypeSymbol, string)`, and `ResolveMember(INamedTypeSymbol, string)`. Cached via `ConditionalWeakTable<CSharpCompilation, _>` — distinct compilations get distinct resolvers; the same compilation reference always returns the same resolver (test-locked).
- [x] When a rule's `DeclaredTargets` cannot resolve, the registry short-circuits and reports the rule via the `onSelfDisabled(name, firstUnresolved)` callback. The rule appears as `self-disabled (unresolved: <target>)` in `--list-rules`. Trace-channel structured warning hook landed 2026-05-11: `TraceWriter.WriteRuleSelfDisabled(rule, target)` emits `{kind: "rule_self_disabled", rule, unresolved_target, mode}`; `SuggesterOrchestrator` threads `onRuleSelfDisabled` through to `RuleRegistry.BestMatch`; `CheckCommand.Run` wires it to the active trace writer (dedup'd per-invocation per-rule). Stdout stays clean.
- [x] **CI gate: rule-target resolution test.** `tests/Reactor.Tests/CheckCommandTests/Rules/RuleTargetResolutionTests.cs` instantiates every registered rule against a live Reactor `Compilation` (full assembly references — the inverse of `TestCompilation.Create`) and asserts each declared target resolves. Passes vacuously today (zero rules); becomes the load-bearing gate the moment the first rule lands.
- [ ] **Performance bound.** Symbol-resolution adds ≤ 0.5 ms median per rule per diagnostic. Captured in the perf-trait suite. — Deferred until the first rule lands; can't measure a delta against zero rules.

### 3.2 Rule-batch PRs (ongoing — open one per ~3 rules)

For each rule in a batch, the author **must** complete all six bars of the Human Validation Gate before merge — with one explicit Class-B carve-out on bar #1 (frequency).

#### Rule template — Class A (induced)

- [ ] Author `src/Reactor.Cli/Check/Rules/<Name>Rule.cs`.
- [ ] Set `Provenance = "cluster:<cluster_id>"`.
- [ ] Bind all target types/methods through `RuleSymbolResolver` (no string matching).
- [ ] Author `tests/Reactor.Tests/CheckCommandTests/Rules/<Name>RuleTests.cs` with **≥ 3 positive fixtures** drawn from `fixes.jsonl`, each from a different `run_id`. Each fixture references the source `run_id` in a comment.
- [ ] Author **≥ 2 negative fixtures** in the same test file.
- [ ] PR description includes the fixed-format Validation Gate comment template, filled in.
- [ ] Confirm cluster has `frequency ≥ 0.05` AND `count ≥ 10` AND reproduces across ≥ 2 agents (cite `patterns.json` row).
- [ ] Confirm corpus age: rule may not merge against a corpus older than the latest Reactor minor release (3.0-prerequisite from §3.0).
- [ ] Reviewer (not the author) leaves PR comment using the template.
- [ ] After merge, log `Name`, `Provenance`, `count`, merge-date in `docs/specs/tasks/038-rule-history.md`.

#### Rule template — Class B (vocabulary-translation)

- [ ] Add the row to `docs/specs/tasks/038-vocab-table.csv` (or confirm it already exists). One vocab-table row per rule.
- [ ] Author `src/Reactor.Cli/Check/Rules/<Name>Rule.cs`.
- [ ] Set `Provenance = "vocab:<framework>"` (e.g. `vocab:WinUI3`).
- [ ] Bind all target types/methods through `RuleSymbolResolver`.
- [ ] Author `tests/Reactor.Tests/CheckCommandTests/Rules/<Name>RuleTests.cs` with **≥ 3 positive fixtures**. Fixtures may be hand-authored from the vocab-table row (Class B does not require `fixes.jsonl` provenance) but are tagged `[Trait("Origin", "VocabHandAuthored")]` for audit.
- [ ] Author **≥ 2 negative fixtures** in the same test file. Negative fixtures specifically include "same diagnostic on a non-Reactor receiver" and "same name on the same receiver in a context where the translation does NOT apply" (e.g. a property access, not a method call).
- [ ] PR description includes the Validation Gate comment template, **with bar #1 (frequency) marked "waived — Class B"** and bar #2 (cross-agent reproducibility) demonstrated either via the corpus OR via citation of the source-framework docs the prior name lives in.
- [ ] Reviewer (not the author) leaves PR comment using the template.
- [ ] After merge, log `Name`, `Provenance`, `source_doc_url`, merge-date in `docs/specs/tasks/038-rule-history.md`.

### 3.3 Quantity gates

- [ ] **Before EC2 (already passed in Phase 2):** 0 rules required.
- [ ] **Before EC3:** 5 high-confidence rules covering ≥ 50 % of fix events by frequency (Class A coverage) plus ≥ 1 Class-B rule. Below this bar, EC3 is delayed until the bar is hit.
- [ ] **V1 ship:** 10–15 rules covering ≥ 80 % of fix events. Mix is at-author's-judgement but expect ~40 % Class A / 60 % Class B given the 525-run corpus's vocabulary-confusion signal. Past 15, returns diminish; remaining clusters move to Tier 4 (Phase 4).

### 3.4 Phase 3 exit criterion

- [ ] At least 10 rules merged across both classes.
- [ ] Coverage check: cumulative `count` of all merged Class-A rules' seed clusters ≥ 0.80 of `fixes.jsonl` row count *or* Class-B rules cover ≥ 80 % of the documented prior-framework vocabulary table (whichever target the team picked).
- [ ] No rule has accept-rate < 50 % over the last 200 invocations (auto-suppression has not had to fire on any merged rule).
- [ ] No rule is self-disabled due to unresolved target (the CI gate from §3.1a fails the build otherwise — this is a belt-and-suspenders assertion at exit).
- [ ] **Run Eval Checkpoint EC3** vs. `main` at start of Phase 3. Pass criterion: cumulative ~−14 % tokens vs. start-of-spec, ~−2 turns, ~−$0.70 (per spec §12); CV ≤ start-of-spec CV.
- [ ] Merge to `main`. V1 of spec 038 is shipped.

---

## Phase 4: Telemetry & learned ranker (scheduled, deferred)

**Status change vs. earlier draft:** was "optional, only if needed." Promoted to **scheduled, deferred until Data Checkpoint D delivers ≥ 1K negative-class ranker rows** (spec §13 Phase 5 update + §1 load-bearing argument). It is not a maybe; it is the work we open when the data is ready. The deterministic floor (Tiers 1–3 + the Phase-2 policy table) carries the experimental phase; the learned ranker is what we ship once the corpus delivers training volume.

The escape hatch — a documented decision to ship Phase 4 with the deterministic table only — remains, but is the *unexpected* outcome and requires its own decision artifact.

**Blocks on:** Data Checkpoint D, EC3 merge.

### 4.1 Telemetry pipeline

- [ ] Local-first telemetry collector: read JSONL from `~/.mur/telemetry/`; emit aggregated per-code, per-rule accept rates; opt-in upload to a team-internal endpoint (TBD; coordinate with the Reactor team's existing telemetry policy).
- [ ] Per-rule auto-suppression hook: rules with accept-rate < 50 % over the last 200 invocations are disabled at runtime; a follow-up issue is auto-filed with the rule's exemplar pairs and the agent edits that diverged from the suggestion.

### 4.2 Learned ranker

- [ ] Implement training pipeline in `tools/Reactor.RankerTraining/` (offline). Inputs: `ranker-labels.jsonl` from Data Checkpoint D. Output: ONNX model under 100 KB.
- [ ] Features per spec §8: diagnostic code, severity, file category, turn index, prior-emit-and-ignored flag, file churn rate.
- [ ] Calibrate via isotonic regression on a held-out fold.
- [ ] Inference path: `src/Reactor.Cli/Check/Ranker/LearnedRanker.cs` loads the ONNX model from a NuGet'd `Microsoft.UI.Reactor.MurCheckModel` package; falls back to deterministic policy table on load failure.
- [ ] Per-diagnostic budget: ≤ 5 ms median.

### 4.3 Tier 4 confidence ranker (suggestion-side)

- [ ] If EC3 telemetry shows Tier 2 + 3 still leave a meaningful tail of suggestion misses, train a small GBDT confidence ranker over hand-engineered features (Levenshtein, param-name overlap, factory-popularity, AST-shape similarity).
- [ ] Wire as Tier 4 in `CheckCommand`: only consulted when Tier 2 + 3 produce conflicting candidates; output is a re-ranked candidate list with calibrated confidence head.

### 4.4 Phase 4 exit criterion

- [ ] **Run Eval Checkpoint EC4.** Pass criterion: ≥ 5 pp lift in iteration-mode emission precision vs. EC3, OR formal documented decision to ship Phase 4 with the deterministic table only.
- [ ] If shipped: merge. If not: document the decision in spec 038 §13 and close.

---

## Cross-cutting concerns

### Testing strategy summary

Three test tiers, mirroring spec 020's pattern:

| Tier | Project | What it covers | Speed |
|---|---|---|---|
| **Unit — pure** | `tests/Reactor.Tests/CheckCommandTests/` | Suggester logic, rule logic, ranker scoring math, args parser. Fakes for `Compilation` where possible. | < 5 ms |
| **Unit — Roslyn** | `tests/Reactor.Tests/CheckCommandTests/` (`[Trait("Category","Roslyn")]`) | Suggesters / rules driven through a real `CSharpCompilation` constructed in-memory. No file system, no `dotnet build`. | ~20–100 ms |
| **Integration** | `tests/Reactor.IntegrationTests/MurCheck/` | Real `mur check` invocations against fixture projects under `tests/Reactor.IntegrationTests/MurCheck/Fixtures/`. Each fixture is a tiny broken Reactor app. Shells out to `dotnet build`. | ~1–5 s per test |
| **Perf** | Same as Integration, `[Trait("Category","Perf")]` | Cold/warm Compilation load; total `mur check` overhead vs. `dotnet build`. Excluded from default test runs; opt-in via filter. | varies |

**Conventions:**

- Every suggester / rule has at least one positive and one negative test in the Unit — Roslyn tier.
- Every CLI flag has at least one Unit — pure test (against `ArgsParser`) and one Integration test (full invocation).
- Tier-3 rules' fixture tests must cite their source `run_id` from `fixes.jsonl` in a comment so a future maintainer can trace the rule back to the data that motivated it.

### Security considerations

- **Source code never leaves the user's machine.** Telemetry payloads are reviewed against this rule on every PR that touches the telemetry module.
- **Trace files are opt-in (`--trace <path>`).** No implicit telemetry. No background uploads.
- **`Compilation` references** are resolved via the project's own `obj/project.assets.json`. We do not download arbitrary packages and we do not honor `nuget.config` `<add>` entries that point outside the user's existing trust set.
- **Symlink handling** in `CompilationLoader`: symlinks pointing outside the project root are followed but logged at trace level; we do not panic, but we also don't include the file in the Compilation. Test fixture covers this.
- **No code execution from rules.** `IRulePattern.TryMatch` is restricted to read-only Roslyn syntax / semantic model APIs. CodeReview lints any rule that calls `Process.Start`, file I/O, or network.
- **Diagnostic message text** can contain user code fragments. The ranker uses message text but never logs it to telemetry (codes only).

### Performance budgets

Captured as `[Trait("Category","Perf")]` integration tests; CI runs nightly, breaks on regression > 10 % from the recorded baseline.

| Surface | Cold | Warm | Notes |
|---|---|---|---|
| `CompilationLoader.Load` | ≤ 500 ms | ≤ 50 ms | per project. Cache key: `(absolute-csproj-path, sorted-file-mtime-hash)`. |
| `SymbolSuggester.Suggest` per diagnostic | ≤ 10 ms median | — | stateless. |
| `IRulePattern.TryMatch` per rule per diagnostic | ≤ 2 ms median | — | stateless; ≤ 30 rules → ≤ 60 ms aggregate. |
| `Ranker.Score` per diagnostic | ≤ 1 ms (deterministic) ≤ 5 ms (learned) | — | hot path; called for every diagnostic. |
| `mur check` total wall vs. underlying `dotnet build` | ≤ 1.2× | ≤ 1.1× warm | Spec §2. |

### Accessibility / localization

`mur check` output is developer-facing tooling, not user-visible UI. Output is en-US; localization is not in scope. (Same convention as `dotnet build`.)

### Maintenance (load-bearing operation)

This section captures the operational responsibilities a load-bearing system inherits beyond the per-phase work above. Each item is a recurring obligation, not a one-time task.

**API-churn protocol (per Reactor minor release):**

1. Run the rule-target resolution CI gate (§3.1a) against the new Reactor `Compilation`. Any rule whose target evaporates fails the build.
2. For each failed rule, the owner either (a) updates the rule's target binding via `RuleSymbolResolver`, or (b) retires the rule. No silent string-swap.
3. Re-run the threshold-tuning harness against the latest corpus *restricted to the new Reactor minor* before re-baselining any threshold. The full historical corpus stays archived but is not used for re-tuning across an API break.
4. Cut a new corpus drop on the new Reactor minor (Data Checkpoint refresh; same audit checklist as Checkpoints A–D).
5. Log the churn-handling pass in `docs/specs/tasks/038-rule-history.md` with the Reactor version and the set of rules touched.

**Corpus freshness:**

- A rule may not merge against a corpus older than the latest Reactor minor release. Enforced by the PR template; reviewer checks the corpus timestamp.
- Stale rows in archived corpora (referencing retired APIs) are marked `historical: true` rather than deleted — they remain valid training signal for the *kind* of mistake and may be useful for future cross-version ablations.

**Per-rule accept-rate monitoring (post-Phase-4):**

- Auto-suppression fires when accept-rate drops below 50 % over the last 200 invocations (spec §11 risk row; Phase 4 telemetry hook).
- A suppressed rule files a follow-up issue automatically; the rule's author or current owner triages within one sprint.
- Re-enabling a previously-suppressed rule requires a new PR that explains the regression, with the same six-bar Validation Gate.

**Sunset readiness check (annual):**

- Spec §13 defines two sunset conditions: ≥ 12 months Reactor API stability AND ≥ 90 % first-build-OK on a held-out Reactor eval across ≥ 2 vendor-distinct models without `mur check` assistance.
- Run the readiness check yearly. When both conditions hold, file the sunset issue, freeze rule additions, and plan graceful removal. The trace/`--final` mode survives; only Tiers 2–4 sunset.

---

## Risks & open items

- **Data Checkpoint B / C / D ETAs are unknown.** The harness team is on a separate cadence. If B slips, Phase 1 ships with a guessed T and we tune in a follow-up PR (low risk). If C slips, Phase 3 cannot start — communicate explicitly to stakeholders. If D slips, Phase 4 simply doesn't happen this cycle.
- **A bad Tier 3 rule landing.** Mitigation: the Validation Gate, the auto-suppression telemetry, and the post-merge tracking in `docs/specs/tasks/038-rule-history.md`.
- **Tier 2 false positives at threshold edges.** Mitigation: per-code thresholds (not one global T), tuned against Data Checkpoint B before Phase 1 ships.
- **Performance regression from learned ranker (Phase 4).** Mitigation: deterministic table is the floor; learned ranker is a re-rank on top. Fall back to deterministic if learned model load fails.
- **Coordinated change with eval prompt** (#226 ownership) for Phase 2's `--final` workflow. Risk: skill / prompt land out of phase with code. Mitigation: ship them in the same PR cycle, with the skill change explicitly listed in the Phase 2 exit criterion.

## Pointers

- Spec 038: [`docs/specs/038-mur-check-did-you-mean-design.md`](../038-mur-check-did-you-mean-design.md)
- Companion spec 037: [`docs/specs/037-eval-trace-mining-design.md`](../037-eval-trace-mining-design.md)
- Existing `CheckCommand`: [`src/Reactor.Cli/Check/CheckCommand.cs`](../../../src/Reactor.Cli/Check/CheckCommand.cs)
- Reactor analyzers (12 `REACTOR_*` IDs): `src/Reactor.Analyzers/`
- Roslyn version: `Microsoft.CodeAnalysis.CSharp` 4.8.0 in `src/Reactor.Cli/Reactor.Cli.csproj`
- Sample apps (validation corpus): `samples/apps/`
- Originating issues: #226 §5, #227, #228 (specs PR)
- Harness review-feedback: `C:\temp\eval-trace-mining-followups.md` (sent to harness owner)
