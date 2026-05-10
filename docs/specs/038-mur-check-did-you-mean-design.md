# `mur check` Did-You-Mean Engine — Design Spec

## Status

**Proposed** — 2026-05-09. No code yet.

This spec proposes extending the existing `mur check` command (a thin MSBuild wrapper at `src/Reactor.Cli/Check/CheckCommand.cs`) into a **diagnostic-aware coach** that augments C# compiler errors landing on Reactor types with concrete *did-you-mean* suggestions. Today `mur check` parses MSBuild output into one-line diagnostics and looks up skill-file pointers for the 12 known `REACTOR_*` analyzer codes. This spec adds three further tiers — Roslyn semantic suggestions, induced pattern rules, and an optional learned ranker — driven by data the harness in [037 — Reactor Eval Trace Mining](./037-eval-trace-mining-design.md) produces.

The motivating example, from agent-eval traces summarized in [#226](https://github.com/microsoft/microsoft-ui-reactor/issues/226):

```
Program.cs(34,16): error CS1061: 'ButtonElement' does not contain a definition for 'OnClick'
```

After this spec:

```
Program.cs:34:16  E  CS1061  'ButtonElement' has no member 'OnClick'
                              -> try: Button(label, onClick: x)         [factory has Action onClick parameter]
```

A 2–4-turn build/fix cycle (~150 K tokens of context per kanban run, per the breakdown in #226) compresses to a 1-turn correction.

This spec was filed first as design proposal [#227](https://github.com/microsoft/microsoft-ui-reactor/issues/227); this document is the canonical, in-tree version and supersedes the issue once landed.

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 Goals and non-goals](#2-goals-and-non-goals)
- [§3 Architecture: four tiers](#3-architecture-four-tiers)
- [§4 Tier 1 — analyzer-ID hint table](#4-tier-1--analyzer-id-hint-table)
- [§5 Tier 2 — Roslyn semantic suggester](#5-tier-2--roslyn-semantic-suggester)
- [§6 Tier 3 — induced pattern rules](#6-tier-3--induced-pattern-rules)
- [§7 Tier 4 — confidence ranker](#7-tier-4--confidence-ranker)
- [§8 Output format](#8-output-format)
- [§9 Telemetry](#9-telemetry)
- [§10 Risks](#10-risks)
- [§11 Predicted impact](#11-predicted-impact)
- [§12 Implementation phases](#12-implementation-phases)
- [§13 Open questions](#13-open-questions)
- [§14 Pointers](#14-pointers)

---

## §1 Motivation

The Phase-7 eval results in #226 show Reactor at 1.00× / 1.11× WinUI XAML on tokens but still 3–4× HTML. The remaining gap is **structural overhead** — and the largest single bucket inside it is the **build/fix loop**, ~150 K tokens / 2–4 turns per kanban run. Every cycle, the agent runs `dotnet build`, dumps 1.5–3 K tokens of MSBuild output into context, reads it, infers what to change, and edits.

#226 §5 proposes replacing `dotnet build` with `mur check` as the default verification path; that alone shrinks each diagnostic dump from kilobytes to ~50–150 tokens. This spec goes further: instead of `mur check` only forwarding shorter diagnostics, have it run a Roslyn-backed pass that *answers the agent's likely next question*. When the diagnostic is `'ButtonElement' does not contain a definition for 'OnClick'`, the agent's next move is almost always to inspect the `Button` factory and discover the `Action onClick` parameter. We can save that inspection turn by emitting the answer in the diagnostic itself.

The catch: a *wrong* suggestion is worse than no suggestion. The agent will trust it and burn turns chasing a phantom fix. So the bar to emit a suggestion is high confidence, and the bar to ship a new pattern rule is empirical evidence (from spec 037's corpus) that real agents make the mistake the rule covers.

## §2 Goals and non-goals

### Goals

- For every C# compiler error (CS-prefixed) whose receiver, type, or member references a `Microsoft.UI.Reactor.*` symbol, emit a single-line suggestion with confidence ≥ T.
- Stay silent below T. Silent is correct. Wrong is not.
- Compose with the existing `REACTOR_*` analyzer hint table, not replace it.
- Keep the diagnostic format machine-parseable: one line per diagnostic, predictable column structure.
- Be fast: total `mur check` wall time stays within ~1.2× the underlying `dotnet build` it wraps.

### Non-goals

- Suggestions for non-Reactor compile errors (NuGet, target framework, generic CS1002 syntax). Out of scope; those go to standard MSBuild output unchanged.
- Auto-fix / write-back to source. Emit text only; the agent edits.
- Replacing the existing analyzers. The 12 `REACTOR_*` codes ship as Roslyn analyzers and continue to fire through MSBuild. This spec adds a *parallel* analysis path for CS-codes only.
- Cross-project / workspace-level reasoning in v1. Single `Compilation` per project, loaded fresh each invocation.
- Fancy output formats (JSON, SARIF). One-line text only in v1; structured emission is a future scope.

## §3 Architecture: four tiers

```
mur check <path>
    │
    ▼ spawn dotnet build, parse diagnostics  (existing — CheckCommand.cs)
    │
    ▼ for each diagnostic that references a Microsoft.UI.Reactor.* symbol:
    │
    │   Tier 1 — analyzer-ID hint table              (existing — HintFor() in CheckCommand)
    │             REACTOR_HOOKS_001 -> SKILL.md §Hooks
    │             12 IDs covered today.
    │
    │   Tier 2 — Roslyn semantic suggester           (NEW)
    │             Load Compilation, resolve symbol at span,
    │             fuzzy-match against in-scope members + factory set.
    │             Handles CS1061, CS0103, CS0117, CS1503, CS7036.
    │
    │   Tier 3 — pattern rules (React-ism reductions) (NEW)
    │             .OnClick(x)        -> onClick: x
    │             .Style(...)        -> .With(...) modifier chain
    │             .Children([...])   -> factory(elements[])  positional
    │             className=         -> not a thing; surface modifier API
    │
    │   Tier 4 — confidence ranker / tiebreaker      (FUTURE — only if Tier 2+3 leave gaps)
    │             GBDT over hand-engineered features (Levenshtein,
    │             param-name overlap, factory-popularity-in-samples,
    │             AST-shape similarity).
    │
    ▼ append "-> try: <suggestion>" only when confidence >= T
```

The tier structure is deliberate: **most of the value is deterministic.** ML enters only as a tiebreaker, only after measured data shows where Tier 2 + 3 fall short.

## §4 Tier 1 — analyzer-ID hint table

**Status: shipped.** Today `CheckCommand.HintFor(code)` returns a static skill-file pointer for 12 `REACTOR_*` analyzer codes (see §14). This tier is unchanged by this spec. New analyzer codes added by future analyzers slot into the same table.

## §5 Tier 2 — Roslyn semantic suggester

The new code lives in `src/Reactor.Cli/Check/Suggesters/SymbolSuggester.cs`. Inputs: a `CSharpCompilation`, the parsed `Diagnostic`, the `SyntaxNode` at the diagnostic's `(line, col)`. Output: an ordered list of `(suggestion_text, confidence, evidence)`.

**Diagnostic codes handled:**

| Code | Meaning | Suggester logic |
|---|---|---|
| CS1061 | Member missing on type | Walk receiver's `ITypeSymbol` members; rank by JaroWinkler against the missing name. If a top-3 candidate is a *parameter* of an enclosing factory call, prefer "use named arg `name:`". |
| CS0103 | Name not in scope | Walk static methods of `Microsoft.UI.Reactor.Factories`; rank by JaroWinkler. Filter to factories whose return type is assignable to the expected type at the use site. |
| CS0117 | No static member | Walk static members of the named type; same fuzzy-match. |
| CS1503 | Argument type mismatch | If the expected parameter is `Element` and the supplied is a string-typed value, suggest the relevant text factory (`Caption`, `Heading`, `Body`). If expected is `Action` and supplied is `Action<T>`, surface the lambda-shape mismatch. |
| CS7036 | No overload takes N args | Walk overloads of the called factory; rank by Hamming distance on the parameter-shape vector. Suggest the closest overload's named-argument form. |

**Confidence scoring** is a function of:

- Top candidate's similarity score (JaroWinkler ≥ 0.85 → 1.0; below 0.7 → 0.0).
- Margin between top-1 and top-2 (close ties → discount).
- Whether the candidate is in the same `Element` / `Modifier` family as the receiver (boost).
- Whether the receiver is a confirmed Reactor type (`Microsoft.UI.Reactor.*` namespace; boost).

Default threshold T = 0.75. Tunable via `mur check --suggest-threshold`. Below T the suggester emits nothing for that diagnostic.

**Compilation lifecycle.** The suggester loads a single `CSharpCompilation` per `mur check` invocation, parsing all `.cs` files in the project tree and resolving references against the .NET SDK + Reactor NuGet that `dotnet build` already restored. ~200–500 ms one-time cost; cheaper than the underlying build it wraps. Cache by `(csproj-path, file-set-hash)` for the multi-invocation devloop scenario.

`Microsoft.CodeAnalysis.CSharp` 4.8.0 is already a `PackageReference` in `src/Reactor.Cli/Reactor.Cli.csproj`, so no dependency churn.

## §6 Tier 3 — induced pattern rules

Tier 3 catches mistakes Tier 2 can't reach because they cross the AST-shape boundary, not just the symbol-name boundary. Examples:

- `.OnClick(x)` chained on a `Button(...)` call. Tier 2 sees CS1061 on `OnClick`; the *fix* moves the lambda into the parent factory's argument list as `onClick: x`. That's a tree rewrite, not a rename.
- `.Style(new { Color = "red" })`. Tier 2 sees CS1061; the fix is a `.With(Modifiers.Foreground(Theme.Critical))` chain — entirely different shape.
- React-attr-style `className=`, `key=`, `ref=`. Tier 2 sees a syntax error on the `=`; the fix is to use the relevant Reactor modifier or the `WithKey` extension.

Each rule is a small class implementing `IRulePattern`:

```csharp
interface IRulePattern
{
    string Name { get; }          // for telemetry
    bool TryMatch(in RuleContext ctx, out RuleSuggestion suggestion);
}
```

Rules live in `src/Reactor.Cli/Check/Rules/<Name>Rule.cs`, one rule per file, each with a unit test against a captured `(broken, fixed)` pair from the spec 037 corpus.

**Rule induction process** (the seam to spec 037):

1. Spec 037's harness produces `fixes.jsonl` and the aggregated `patterns.json`. Each cluster has `cluster_id`, `diag_code`, `fix_kind`, frequency, and a set of exemplar pairs.
2. A human reviewer walks `patterns.json` top-down by frequency. For each cluster they decide:
   - **Yes** — author an `IRulePattern`. Use the exemplars as test fixtures.
   - **Tier 2 already covers this** — no rule needed.
   - **No** — false-positive risk too high, frequency too low, or the cluster reflects a flaw in Reactor's API surface that should be fixed at the framework level instead.
3. Each shipped rule carries a per-rule confidence (default 0.85) and a kill-switch (`mur check --disable-rule <Name>`).

**Why human review and not auto-induction?** Rules are public-facing suggestions to the agent; a bad rule contaminates every downstream session. The cost of one bad rule landing is high; the cost of one cluster waiting another sprint for review is low. The asymmetry justifies the review step.

## §7 Tier 4 — confidence ranker

**Status: future.** Build only if telemetry (§9) shows Tier 2 + 3 leave a meaningful long tail.

If built: a gradient-boosted tree over hand-engineered features — Levenshtein distance, parameter-name overlap, factory-popularity-in-samples (counted from `samples/apps/`), AST-shape similarity, prior agent-accept rate per rule. Inputs: the candidate set produced by Tiers 2 + 3. Output: re-ranked list with a calibrated confidence head.

Inference cost is negligible (microseconds). Training cost is one offline pipeline against `fixes.jsonl`.

We deliberately do **not** propose a small LLM here. Small models hallucinate without huge corpora, and Reactor's training set is fundamentally limited; the deterministic system already has access to the things a small model would have to memorize (the api index, the samples).

## §8 Output format

`mur check` emits one line per diagnostic, format:

```
<file>:<line>:<col>  <SEV>  <CODE>  <message>[ -> <hint>][ // <evidence>]
```

Where:

- `<SEV>` is `E` / `W` / `I` (error, warning, info).
- `<message>` is truncated to ~100 chars.
- `<hint>` is the Tier 1 hint pointer if present, else the highest-confidence Tier 2 / Tier 3 suggestion above threshold.
- `<evidence>` (optional, after `//`) is a brief justification — `[factory has Action onClick parameter]`, `[matched rule: FluentToNamed]`, `[skill: SKILL.md §Hooks]`. Always present so the agent can sanity-check.

If multiple suggestions cross threshold for the same diagnostic, emit the highest-confidence one only. Ties broken by Tier 1 > Tier 3 > Tier 2 (analyzer hints are most reliable, hand-authored rules next, fuzzy matches last).

Exit code preserves `dotnet build`'s exit code. `mur check` does not invent its own exit semantics.

## §9 Telemetry

Each `mur check` invocation logs (locally; opt-in upload):

```json
{
  "session_id": "...", "ts": "...",
  "diagnostic": { "code": "CS1061", "receiver_type": "ButtonElement", "member": "OnClick" },
  "tiers_fired": ["Tier2", "Tier3"],
  "suggestion_emitted": "Button(label, onClick: x)",
  "confidence": 0.91,
  "rule_name": "FluentToNamed"
}
```

If running inside an agent harness, the harness can additionally report whether the agent's *next edit* matched the suggestion (proxy for accept rate). Per-rule accept rate is the primary signal for promoting / demoting / killing rules.

Telemetry is local-first, opt-in, scoped to the active project. No source code, no PII, no machine identifiers.

## §10 Risks

| Risk | Mitigation |
|---|---|
| Wrong suggestions corrupt the agent's reasoning | Per-rule confidence threshold; emit only above T. Telemetry on agent-accept rate per rule, auto-suppress rules whose accept rate falls below 50 %. |
| Roslyn `Compilation` load is slow on cold runs | Single load per `mur check` invocation, ~200–500 ms one-time vs. the multi-second `dotnet build` we already pay for. Cache by file-set hash for incremental loops. |
| `reactor.api.txt` and the live `Reactor.dll` drift apart | Tier 2 reads from the live `Compilation`, not the api index. The api index is a pre-filter only. |
| Random-app corpus over-represents simple controls | Stratify the prompt grid in spec 037; weight rules by both frequency *and* per-cluster turn-cost (rare patterns that cost 5 turns matter more than common ones that cost 1). |
| Pattern rules become a maintenance treadmill | Auto-generate the `samples/`-derived validation set; CI fails when a Tier 3 rule change regresses a captured pair. |
| Tier 2 fuzzy-match emits an embarrassingly wrong rename | Whitelist threshold T. Per-code thresholds, not one global T. |
| The corpus encodes a model's idiosyncrasies | Spec 037 supports multi-agent rotation. Tier 3 rules ship only when a cluster reproduces across ≥ 2 agents. |

## §11 Predicted impact

If the per-kanban-run breakdown in #226 (build + fix cycles ≈ 150 K tokens, 2–4 turns) is right, and Tier 2 + 3 remove ~70 % of those turns:

- Tokens: ~−100 K per kanban run (~14 % of total).
- Turns: ~−2 (16.8 → ~14.8).
- Cost: ~−$0.70 per run.

Stacking with #226 §1 (richer template) + §2 (inline cheatsheet): kanban tokens 738 K → ~380 K, putting Reactor decisively under WinUI on cost and within ~2× HTML — at the realistic ceiling identified in #226.

These numbers are estimates; the empirical question is settled by Phase 0 instrumentation (§12).

## §12 Implementation phases

Phased so each phase is independently shippable.

### Phase 0 — instrumentation (no behavior change)

- Extend `CheckCommand` to emit a structured trace (`mur check --trace <path>`) capturing every CS-diagnostic that resolves to a `Microsoft.UI.Reactor.*` symbol, even when no suggestion fires today.
- Coordinate with spec 037's pair-extraction so traces are joinable with the harness output.
- Run a 50×N sweep on the existing calc + kanban prompts. Output: a frequency-ranked list of CS codes + receiver-types that touch Reactor.

**Exit:** ranked list of the top ~20 CS-diagnostic patterns the agent hits.

### Phase 1 — Tier 2 (Roslyn semantic suggester)

- Add `Reactor.Cli.Check.Suggesters.SymbolSuggester` per §5.
- Cover CS1061, CS0103, CS0117, CS1503, CS7036.
- Per-code confidence threshold tunable via CLI flag.

**Exit:** for the top ~20 patterns from Phase 0, ≥ 70 % of cases get a correct suggestion at confidence ≥ T, with false-positive rate ≤ 5 % on a held-out validation set (the `samples/apps/` mutations from spec 037 §10).

### Phase 2 — wait for spec 037's corpus

- Spec 037's harness lands and produces `fixes.jsonl` + `patterns.json`.
- Human-review pass over the top clusters.

**Exit:** a vetted list of pattern rules to author, sized for the next sprint.

### Phase 3 — Tier 3 (pattern rules, induced)

- For each high-frequency cluster, hand-author a rule (`IRulePattern`) matched against the failing AST and the diagnostic code.
- Rules live in `src/Reactor.Cli/Check/Rules/*.cs`, one per file, each with a unit test against a captured pair.

**Exit:** Tier 3 catches every cluster with frequency ≥ 5 % in the random-app corpus.

### Phase 4 — telemetry-driven Tier 4 (only if needed)

- Log `(diagnostic, candidates, picked, accepted-by-agent)` from production `mur check` invocations.
- If Tier 2 + 3 still leave a meaningful tail, train a small GBDT ranker over hand-engineered features. Defer until data justifies it.

**Exit:** measured improvement on the long-tail clusters or formal decision to not pursue.

## §13 Open questions

1. **Trace format for the random-app generator (spec 037).** Does its trace produce a transcript rich enough to extract before/after source pairs without re-running the build? See spec 037 §7. Coordinate before either lands.
2. **Confidence threshold T.** Start strict (≥ 0.85 JaroWinkler-equivalent) and loosen with telemetry, or start loose and tighten? Defaulting to strict preserves "silent is OK" — propose strict.
3. **Should Tier 2 rewrite the surface form?** Today suggestions are text-only ("try: `Button(label, onClick: x)`"). A future variant could emit a unified-diff hunk; that is a separate scope.
4. **Roslyn workspaces vs. compilations.** Workspaces give project-graph reasoning but cost more to load. Start with `Compilation` only; revisit if rules need cross-project context.
5. **Per-agent rule profiles?** If different LLM agents make different mistakes, Tier 3 rule sets could be agent-keyed. Likely premature; cross-agent rules are simpler. Reconsider once telemetry shows agent-specific deltas.

## §14 Pointers

- Existing `mur check` implementation: `src/Reactor.Cli/Check/CheckCommand.cs`
- Existing analyzers (12 `REACTOR_*` IDs): `src/Reactor.Analyzers/{HookRulesAnalyzer,UseMemoCellsAnalyzer,UseThemeRefAnalyzer,RequestedThemeSetAnalyzer,UseLightweightStylingAnalyzer,AccessibilityAnalyzers,MissingWithKeyAnalyzer}.cs`
- API surface index generator: `tools/Reactor.SignaturesGen/Program.cs` → writes `skills/reactor.api.txt` and `plugins/reactor/skills/reactor-dsl/references/reactor.api.txt`
- Build/check skill (cheat table for known IDs): `plugins/reactor/skills/reactor-build-and-check/SKILL.md`
- Sample apps (validation corpus for Tier 3): `samples/apps/` (13 projects)
- Roslyn version available today: `Microsoft.CodeAnalysis.CSharp` 4.8.0 in `src/Reactor.Cli/Reactor.Cli.csproj`
- Originating issue: [#226 — Agent-eval: close the Reactor → HTML gap](https://github.com/microsoft/microsoft-ui-reactor/issues/226) §5
- Originating proposal issue: [#227 — Roslyn-backed did-you-mean suggestions for `mur check`](https://github.com/microsoft/microsoft-ui-reactor/issues/227)
- Companion data-generation spec: [037 — Reactor Eval Trace Mining](./037-eval-trace-mining-design.md)

---

This spec extends and depends on **#226 §5**. It is independent of, but complementary to, **#226 §1** and **#226 §2**.
