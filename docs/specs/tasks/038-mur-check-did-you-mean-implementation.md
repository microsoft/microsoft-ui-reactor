# `mur check` Did-You-Mean Engine — Implementation Tasks

Derived from: [`docs/specs/038-mur-check-did-you-mean-design.md`](../038-mur-check-did-you-mean-design.md)

Companion spec (data source): [`docs/specs/037-eval-trace-mining-design.md`](../037-eval-trace-mining-design.md)

Originating issues: [#226 §5](https://github.com/microsoft/microsoft-ui-reactor/issues/226), [#227](https://github.com/microsoft/microsoft-ui-reactor/issues/227), [#228 (specs PR, merged)](https://github.com/microsoft/microsoft-ui-reactor/pull/228)

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

### Data Checkpoint B — calibration (≥ 50 unique pairs, ≥ 50 runs, ≥ 2 agents, all four follow-ups from review-feedback resolved)

**Blocks:** Phase 1 ship gate (the Tier 2 confidence-threshold tuning).

**Use:** sets the per-code Tier 2 thresholds. With 50 pairs we can compute, per diagnostic code, the JaroWinkler distribution of the suggester's top candidate vs. the agent's actual fix and pick T to land ≥ 70 % recall at ≤ 5 % false-positive rate. Without B, Phase 1 ships with a guessed T.

**Owner:** harness team.
**ETA:** TBD — track in #228 follow-up issue. Estimated ~50 runs at $3–5 each = ~$200 corpus cost.

### Data Checkpoint C — rule induction (≥ 500 unique pairs, ≥ 200 runs, ≥ 2 agents, ranker negative class present)

**Blocks:** Phase 3 entirely. Tier 3 rule authoring cannot begin without C — every rule's cluster has to clear the Frequency bar (≥ 5 %) of the Validation Gate, and a 50-pair corpus is too small for that math to be meaningful (one pair is 2 %).

**Use:** drives the human-review queue. The reviewer walks `patterns.json` top-down by frequency, opens 5–8 PRs per reviewing-week, each PR is one new rule under the Validation Gate.

**Quantity bar before Eval Checkpoint 2:** **5 high-confidence rules** covering the top ~50–60 % of fix events by frequency. Below 5, Phase 3's effect is too small to measure against eval noise.

**Quantity bar before declaring V1 done (Eval Checkpoint 3):** **10–15 rules** covering ~80 % of fix events. Past 15, returns diminish — the long tail moves to Tier 4.

**Owner:** harness team.
**ETA:** TBD. ~200 runs at $3–5 each = ~$1K corpus cost.

### Data Checkpoint D — ranker training (≥ 5K ranker-label rows, ≥ 200 runs, **negative class** ≥ 1K rows)

**Blocks:** Phase 4's learned ranker (NOT the deterministic policy table — that ships in Phase 2 against intuition + Phase 0 telemetry).

**Use:** train the §8 learned ranker against `addressed_by_next_fix` as the binary label. Calibrate via isotonic regression on a held-out fold.

**Owner:** harness team. Negative class is the gating constraint — the harness must emit one row per diagnostic per build, not just per fix-pair. (Gap #3 in `C:\temp\eval-trace-mining-followups.md`.)

---

## Eval Checkpoints

Each checkpoint is a **5×N batch** on `gpt-5.5` against `reactor-calc` and `reactor-kanban`, comparing the working branch to `main` at the checkpoint's start. Numbers we track: tokens (5-mean and CV), turns, cost USD, first-build success rate. Methodology mirrors the Phase-7 batch summarized in #226.

| Checkpoint | When | Compare against | Predicted lift | Pass criterion |
|---|---|---|---|---|
| **EC1** | After Phase 1 ships (Tier 2 only, no rules, no ranker) | `main` (current) | Modest: ~−5–10 % tokens, ~−1 turn on kanban from Tier 2 alone | First-build OK ≥ 5/5; tokens not regressed; ≥ 1 measurable did-you-mean firing in event log |
| **EC2** | After Phase 2 (deterministic ranker + 5 rules from Data Checkpoint C) | `main` at EC1 | ~−10–15 % tokens, ~−2 turns, kanban only | Tokens improve ≥ 5 %; no false-positive rule fires (every emitted suggestion that the agent took led to a green build) |
| **EC3** | After Phase 3 ships V1 ruleset (10–15 rules) | `main` at EC2 | Cumulative ~−14 % tokens vs. start-of-spec, ~−2 turns, ~−$0.70 (per spec §12 prediction) | Predicted band hit; CV ≤ start-of-spec CV (don't trade variance for mean) |
| **EC4** | After Phase 4 (learned ranker, if pursued) | `main` at EC3 | ~+5 pp precision on iteration-mode emissions per spec §13 Phase 5 | Hit precision target OR formal decision to ship Phase 4 with the deterministic table only |

**Eval-checkpoint conventions:**

- All four eval batches use the **same prompts** as #226's Phase-7 sweep so trajectories are comparable.
- A failed eval checkpoint (regression in tokens or first-build OK) does not block the next phase from starting in *isolation*, but blocks merging that phase's work to `main`. We can branch off and continue developing in parallel; only merge when the gate is met.
- Eval cost: ~$25–40 per 5×N batch. Budget for 2 batches per checkpoint (run, fix, re-run) = ~$200 across all four checkpoints. Track in PR description.

---

## Phase 0: Cross-cutting setup & instrumentation

This phase is pure instrumentation — no agent-visible behavior change. Goal: stand up the trace-output mode that lets us validate the suggester pipeline on real diagnostics without breaking existing eval runs.

### 0.1 Tracking & docs

- [ ] Create this tracking checklist (this file). Update as tasks land.
- [ ] Add a "Spec 038 — `mur check` did-you-mean" entry under `## [Unreleased]` in `CHANGELOG.md`. Each phase below appends bullets to Added / Changed as it lands.
- [ ] Decide PR cadence: default is **one PR per phase** (Phase 0–4 → 5 PRs), with sub-PRs for Phase 3 grouping ~3 rules per PR. Capture the decision in the spec §13 if it changes.
- [ ] Open follow-up issue tracking the four harness gaps from `C:\temp\eval-trace-mining-followups.md` (file under `microsoft/microsoft-ui-reactor` issues, link from the spec) so Data Checkpoint B can land cleanly.

### 0.2 Project surface

- [ ] Confirm `src/Reactor.Cli/Reactor.Cli.csproj` references `Microsoft.CodeAnalysis.CSharp` 4.8.0 (verified during spec drafting; re-verify at task-start in case of pin changes).
- [ ] Add a new folder `src/Reactor.Cli/Check/Suggesters/` with a one-paragraph `README.md` linking to spec 038 §5.
- [ ] Add a new folder `src/Reactor.Cli/Check/Rules/` with a one-paragraph `README.md` linking to spec 038 §6 and to this task list's Validation Gate section.
- [ ] Add a new folder `tests/Reactor.Tests/CheckCommandTests/` (mirroring `Suggesters/` and `Rules/` substructure).

### 0.3 Trace output mode

- [ ] Add `--trace <path>` flag to `mur check` that writes a JSONL stream of every parsed diagnostic, one row per diagnostic. Schema: `{ts, code, severity, file, line, col, msg, receiver_type?, member?, mode}`. Use `mode: "iteration"` even though the ranker isn't built yet — sets up the field for Phase 2.
- [ ] When `--trace` is on, the JSONL is written *in addition to* the normal stdout output, not instead of it. The agent should never see the trace.
- [ ] Trace output never includes source code text. Validation: a unit test reads a trace file and asserts no line is longer than 2 KB (heuristic catch for accidental source-leak regressions).
- [ ] Trace output never includes absolute file paths outside the project root. Validation: a unit test asserts every `file` field starts with the project root prefix or is a relative path.
- [ ] Add `--trace` to `--help`.
- [ ] Unit test: `mur check --trace /tmp/x.jsonl ./fixture-broken-app/` produces ≥ 1 row in `/tmp/x.jsonl` and the row schema validates against a small JSON-schema fixture.

### 0.4 MSBuild passthrough (deferred to Phase 2)

The passthrough subsection in spec §8 is implemented in Phase 2 alongside the ranker, since the ranker's `--strict` / `--final` flag-parsing infrastructure is the natural shared codebase. Nothing in Phase 0 changes existing argument handling.

### 0.5 Phase 0 exit criterion

- [ ] `mur check --trace` emits valid JSONL on a known-broken fixture project.
- [ ] Run a 50-prompt sweep with the agent eval harness, with `--trace` writing alongside, and confirm we capture ≥ 1 trace row per CS-prefixed diagnostic. (Smoke test only; analysis happens at Data Checkpoint B.)
- [ ] No regression in existing `mur check` output. Existing integration tests pass unchanged.

---

## Phase 1: Tier 2 — Roslyn semantic suggester

Goal: for the five highest-frequency CS-prefixed codes that touch Reactor types, emit one-line did-you-mean suggestions backed by the live Roslyn `Compilation`.

**Blocks on:** Phase 0.5 exit. **Internal dependency:** none on spec 037 yet (hand-authored fixtures are acceptable through Data Checkpoint B).

### 1.1 Suggester contract

- [ ] Create `src/Reactor.Cli/Check/Suggesters/ISuggester.cs`. Define `interface ISuggester { string Name { get; } SuggestionResult Suggest(in SuggesterContext ctx); }`.
- [ ] Create `record SuggesterContext(CSharpCompilation Compilation, Diagnostic Diagnostic, SyntaxNode? Node, ITypeSymbol? Receiver, FactoryIndex Factories)`.
- [ ] Create `record SuggestionResult(string? Text, double Confidence, string Evidence)`. Convention: `Text == null` → no suggestion (silent path).
- [ ] Unit test: `SuggesterContext` is `readonly record struct`-shaped; constructing with required fields succeeds; default `(null, null)` is well-formed.

### 1.2 Compilation loader

- [ ] Create `src/Reactor.Cli/Check/CompilationLoader.cs`. Public method: `CSharpCompilation Load(string projectPath)`.
- [ ] Resolve the project's `.csproj` path; parse all `.cs` files under the project root; resolve `MetadataReference`s from the post-`dotnet restore` `obj/project.assets.json`.
- [ ] **Performance budget:** cold load ≤ 500 ms on the `samples/apps/reactorfiles` fixture. Warm load (same `(csproj, file-set-hash)`) ≤ 50 ms. Capture in a perf-trait integration test (`[Trait("Category", "Perf")]`).
- [ ] Cache by `(absolute-csproj-path, sorted-file-mtime-hash)` in a `ConcurrentDictionary<string, CSharpCompilation>`. Invalidate on hash change.
- [ ] Security: only load `.cs` files under the project's logical root. Symlinks pointing outside the root are followed but logged at trace level (do not block). Validation test: a project with a symlink to `/etc/passwd` does not panic and does not include the file in the Compilation.
- [ ] Unit test: cold and warm load timings recorded; assert under budget.
- [ ] Unit test: invalid `.csproj` returns a sentinel `EmptyCompilation` rather than throwing — `mur check` should always exit gracefully.

### 1.3 `FactoryIndex` (pre-filter against `Microsoft.UI.Reactor.Factories`)

- [ ] Create `src/Reactor.Cli/Check/FactoryIndex.cs`. Builds an index of `Microsoft.UI.Reactor.Factories.*` static methods from the loaded `Compilation`: `Dictionary<string, List<IMethodSymbol>>` keyed on factory name.
- [ ] Index includes parameter names per overload (cached as `string[]`) so Tier 2 can suggest named-argument moves without re-walking symbols.
- [ ] Unit test: load a fixture compilation that references Reactor; assert `Button` has ≥ 3 overloads; assert one overload has a parameter named `onClick`.

### 1.4 `SymbolSuggester` — CS1061 (member missing)

- [ ] Create `src/Reactor.Cli/Check/Suggesters/SymbolSuggester.cs`.
- [ ] Implement CS1061 path: walk receiver's `ITypeSymbol` members; rank candidates by JaroWinkler against the missing name; prefer parameters of an enclosing factory call (suggest "use named arg `name:`").
- [ ] Confidence formula per spec §5; default T = 0.75.
- [ ] Unit test: synthetic `Compilation` with `class Foo { public void Bar() {} }`, call `foo.Brr()` triggers CS1061; suggester proposes `Bar` at confidence ≥ 0.85.
- [ ] Unit test: `Button("x").OnClick(() => {})` (CS1061 on `OnClick`) → suggester proposes `Button(label, onClick: ...)` with evidence `[factory has Action onClick parameter]`.
- [ ] Unit test (negative): `Button("x").Garbage(...)` with no nearby member → suggester returns `Text == null` (silent).
- [ ] Unit test: suggester is pure — invoked with the same input twice, returns identical `SuggestionResult`.

### 1.5 `SymbolSuggester` — CS0103, CS0117, CS1503, CS7036

- [ ] CS0103 (name not in scope): walk static methods of `Microsoft.UI.Reactor.Factories`; rank by JaroWinkler; filter by return-type assignability at the use site.
- [ ] CS0117 (no static member): walk static members of the named type; same fuzzy match.
- [ ] CS1503 (argument type mismatch): special-case `Element`-expected vs. string-supplied → suggest `Caption`/`Heading`/`Body`. `Action` vs. `Action<T>` → surface lambda-shape mismatch.
- [ ] CS7036 (no overload takes N args): rank overloads by Hamming distance on the parameter-shape vector; suggest the closest overload's named-argument form.
- [ ] One unit test per code path, both positive and negative.

### 1.6 Wiring into `CheckCommand`

- [ ] In `CheckCommand.Run`, after parsing each `Diag`, if its `code` matches CS1061 / CS0103 / CS0117 / CS1503 / CS7036 AND the diagnostic touches a `Microsoft.UI.Reactor.*` symbol, run `SymbolSuggester.Suggest`.
- [ ] If the suggester returns a non-null `Text` with `Confidence ≥ T`, append `→ try: <text>  // [<evidence>]` to the diagnostic line.
- [ ] Existing analyzer-ID hint table (`HintFor`) still wins ties (spec §9).
- [ ] Integration test: `tests/Reactor.IntegrationTests/MurCheck/CS1061ButtonOnClickTest.cs` — fixture project with the canonical `Button(...).OnClick(x)` mistake; assert `mur check ./fixture` exits 1, stdout contains exactly the expected suggestion line including evidence.
- [ ] Integration test: when Tier 2 has no high-confidence suggestion, the original diagnostic line is unchanged.

### 1.7 Performance & telemetry

- [ ] Total `mur check` wall time on the fixture project stays within 1.2× the underlying `dotnet build`. Capture in a perf-trait test.
- [ ] Telemetry hook at `(diagnostic_emitted, suggester_name, confidence)` — local-only JSONL append at `~/.mur/telemetry/<yyyy-mm-dd>.jsonl`. Opt-in via env var `MUR_TELEMETRY=1`.
- [ ] Telemetry payload is reviewed against the source-code-leak rules from the conventions header. Add a unit test that asserts the payload contains no field whose value is longer than 256 bytes.

### 1.8 Phase 1 exit criterion

- [ ] All Phase 1 tasks above checked.
- [ ] **Wait for Data Checkpoint B.** Once it lands, run the Tier 2 suggester offline against the `fixes.jsonl` corpus; for each top-20 CS-pattern, compute (recall@T, precision@T) and tune per-code T so recall ≥ 0.70 at precision ≥ 0.95. Record the chosen thresholds in `src/Reactor.Cli/Check/Suggesters/Thresholds.cs`.
- [ ] **Run Eval Checkpoint EC1** vs. `main`. Pass criterion: tokens not regressed; ≥ 1 measurable did-you-mean firing per kanban run on average; first-build OK ≥ 5/5.
- [ ] Merge to `main`.

---

## Phase 2: MSBuild passthrough + deterministic pre-emit ranker

Goal: ship the `--` passthrough, the `--strict` / `--final` / `--quiet` mode flags, and the hand-authored `base_policy(code)` table from spec §8. Suppression is the single biggest token-saver in the spec; it's deterministic, so it doesn't wait on Data Checkpoint C.

**Blocks on:** Phase 1 merge to `main`.

### 2.1 Passthrough parser

- [ ] Add `src/Reactor.Cli/Check/ArgsParser.cs`. Split input args on the first bare `--`; left half parsed against `mur check`'s flag grammar; right half forwarded verbatim.
- [ ] Default-merging: `mur` injects `--nologo`, `-v:m`, `-p:Platform={host arch}` only if the user did not specify the same flag in the passthrough. Detection by flag name, not value.
- [ ] Unknown `mur` flags before `--` produce a clear error message (do not silently forward).
- [ ] When `--trace` is on, record the *full effective* `dotnet build` command line in trace output (per spec §8 last paragraph).
- [ ] Unit test matrix per spec §8 examples (host arch override, release config + no-restore, verbosity, TFM, multiple properties, with non-default path).
- [ ] Integration test: `mur check -- -p:Platform=x64` overrides host-arch default; effective command line correct.

### 2.2 Mode flags

- [ ] Add `--strict` / `--final` / `--quiet` to `ArgsParser`. Each maps to a `Mode { Iteration, Strict, Final, Quiet }` enum.
- [ ] Add `--emit-threshold <float>` to override the ranker threshold (default 0.6 in iteration mode, 0.0 in final mode).
- [ ] All flags appear in `--help` with one-line descriptions.
- [ ] Unit test: every mode round-trips through `ArgsParser`.

### 2.3 Deterministic ranker

- [ ] Create `src/Reactor.Cli/Check/Ranker/PolicyTable.cs` with the score table from spec §8 (CS errors 1.0/1.0; REACTOR_* Warning 0.9/1.0; REACTOR_* Info 0.2/1.0; etc.). Cover the top 30 codes from Phase 0's sweep — the seed is the table in the spec, but if Phase 0 trace data shows a different top-30 distribution, update the table to match.
- [ ] Create `src/Reactor.Cli/Check/Ranker/Ranker.cs`. Public method: `double Score(in Diag d, in Mode m, in RankerContext ctx)`. Implements the formula in spec §8 (`base_policy * code_weight + severity_weight + location_weight + recency_weight + accept_history`).
- [ ] Pre-emit gate: in `CheckCommand`, after attaching tier hints, drop any diagnostic whose ranker score is below the active threshold for the current mode.
- [ ] Unit test: in iteration mode, `CS1591` (XML doc) is suppressed; `CS1061` is not.
- [ ] Unit test: in `--final` mode, both are emitted.
- [ ] Unit test: `--strict` promotes warnings to errors (composes with `-p:TreatWarningsAsErrors=true` from passthrough; more aggressive wins per spec §8).
- [ ] Unit test: `--quiet` emits only severity `E` rows.

### 2.4 Suppress→error guardrail

- [ ] Add an offline tool at `tools/Reactor.MurCheckGuardrail/Program.cs` that reads two trace files (one from `mur check` iteration, one from `mur check --final`) and asserts: every code that fired in `--final` and is in the policy table's iteration-suppression list **was not** an error in `--final`. (If suppressed diagnostic codes start surfacing as errors in the final pass, the policy table is wrong and CI fails.)
- [ ] Wire into CI: every PR that touches `PolicyTable.cs` runs the guardrail against a fixed set of fixture projects.

### 2.5 Eval prompt + skill update

- [ ] Update `plugins/reactor/skills/reactor-build-and-check/SKILL.md` to direct agents to run `mur check` (iteration) inside the loop and `mur check --final` once iteration is clean. The transition is the explicit "I am done iterating" signal.
- [ ] Update the eval prompt in the agent-eval harness (lives outside this repo; coordinate with #226 owners).

### 2.6 Phase 2 exit criterion

- [ ] All Phase 2 tasks above checked.
- [ ] **Run Eval Checkpoint EC2** vs. `main` at start of Phase 2. Pass criterion: tokens improve ≥ 5 %; no false-positive emission causes a regression in first-build OK rate.
- [ ] Merge to `main`.

---

## Phase 3: Tier 3 — induced pattern rules

Goal: take the human-reviewed `patterns.json` clusters from Data Checkpoint C, author one rule per top cluster, ship in batches of ~3 rules per PR until ~10–15 rules are live.

**Blocks on:** Data Checkpoint C, Phase 2 merge to `main`. Every PR also blocks on the **Human Validation Gate** at the top of this document.

### 3.1 Rule infrastructure

- [ ] Create `src/Reactor.Cli/Check/Rules/IRulePattern.cs`. Define `interface IRulePattern { string Name { get; } string SeedClusterId { get; } RuleSuggestion? TryMatch(in RuleContext ctx); }`.
- [ ] Create `record RuleContext(SyntaxNode Node, Diagnostic Diagnostic, ITypeSymbol? Receiver, SemanticModel SemanticModel)`.
- [ ] Create `record RuleSuggestion(string Text, double Confidence, string Evidence)`.
- [ ] `RuleRegistry` discovers rules by reflection on assembly load and exposes `GetMatches(RuleContext)` returning all candidates above their per-rule confidence threshold.
- [ ] CLI: `mur check --disable-rule <Name>` round-trips through `--help`; rules listed in `--list-rules` with status (enabled/disabled, accept-rate-if-known).
- [ ] Unit test: registry discovers fixture rules placed under `Rules/`; `--disable-rule` excludes them.

### 3.2 Rule-batch PRs (ongoing — open one per ~3 rules)

For each rule in a batch, the author **must** complete all six bars of the Human Validation Gate before merge. The list below is a template; clone for each new rule.

#### Rule template (copy per rule)

- [ ] Author `src/Reactor.Cli/Check/Rules/<Name>Rule.cs`.
- [ ] Author `tests/Reactor.Tests/CheckCommandTests/Rules/<Name>RuleTests.cs` with **≥ 3 positive fixtures** drawn from `fixes.jsonl`, each from a different `run_id`. Each fixture references the source `run_id` in a comment.
- [ ] Author **≥ 2 negative fixtures** in the same test file.
- [ ] PR description includes the fixed-format Validation Gate comment template, filled in.
- [ ] Confirm cluster has `frequency ≥ 0.05` AND `count ≥ 10` AND reproduces across ≥ 2 agents (cite `patterns.json` row).
- [ ] Reviewer (not the author) leaves PR comment using the template.
- [ ] After merge, log the rule's `Name`, `SeedClusterId`, `count`, and merge-date in `docs/specs/tasks/038-rule-history.md` (create that file in the first rule's PR).

### 3.3 Quantity gates

- [ ] **Before EC2 (already passed in Phase 2):** 0 rules required.
- [ ] **Before EC3:** 5 high-confidence rules covering ≥ 50 % of fix events by frequency. Below this bar, EC3 is delayed until the bar is hit.
- [ ] **V1 ship:** 10–15 rules covering ≥ 80 % of fix events. Past 15, returns diminish; remaining clusters move to Tier 4 (Phase 4).

### 3.4 Phase 3 exit criterion

- [ ] At least 10 rules merged.
- [ ] Coverage check: cumulative `count` of all merged rules' seed clusters ≥ 0.80 of `fixes.jsonl` row count.
- [ ] No rule has accept-rate < 50 % over the last 200 invocations (auto-suppression has not had to fire on any merged rule).
- [ ] **Run Eval Checkpoint EC3** vs. `main` at start of Phase 3. Pass criterion: cumulative ~−14 % tokens vs. start-of-spec, ~−2 turns, ~−$0.70 (per spec §12); CV ≤ start-of-spec CV.
- [ ] Merge to `main`. V1 of spec 038 is shipped.

---

## Phase 4: Telemetry & learned ranker (optional)

Goal: only pursue if EC3 leaves a measurable tail of either (a) Tier 2/3 misses, or (b) noise the deterministic ranker doesn't suppress.

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
