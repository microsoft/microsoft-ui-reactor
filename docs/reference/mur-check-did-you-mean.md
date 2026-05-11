# `mur check` Did-You-Mean — How It Works

This is a guided tour of the "did-you-mean" engine built into `mur check`. It is written so any reader can follow along: every section starts in plain language, then drills into the implementation for engineers, ML practitioners, and compiler folks who want the full picture.

The doc covers what landed on `feat/038-mur-check` (Phase 0 and Phase 1, plus the `--suggest-threshold` gate). Future-improvement sections sketch Phases 2–4. The doc will be updated as those land.

**Source-of-truth specs** (for the canonical version and decision history):
- Design: [`docs/specs/038-mur-check-did-you-mean-design.md`](../specs/038-mur-check-did-you-mean-design.md)
- Implementation tasks + status: [`docs/specs/tasks/038-mur-check-did-you-mean-implementation.md`](../specs/tasks/038-mur-check-did-you-mean-implementation.md)
- Companion (data pipeline): [`docs/specs/037-eval-trace-mining-design.md`](../specs/037-eval-trace-mining-design.md)

---

## Table of contents

- [1. The problem in one paragraph](#1-the-problem-in-one-paragraph)
- [2. What you actually see](#2-what-you-actually-see)
- [3. The four tiers, in plain English](#3-the-four-tiers-in-plain-english)
- [4. How the mining operation works](#4-how-the-mining-operation-works)
- [5. How data tunes the model](#5-how-data-tunes-the-model)
- [6. How a recommendation is picked](#6-how-a-recommendation-is-picked)
- [7. The "small project" gate](#7-the-small-project-gate)
- [8. What's shipped in this PR](#8-whats-shipped-in-this-pr)
- [9. Future improvements](#9-future-improvements)
- [10. Glossary](#10-glossary)

---

## 1. The problem in one paragraph

When an AI agent writes a Reactor app, it usually compiles, finds a build error, reads the error, edits the file, and tries again. Each of those round-trips costs time, tokens, and money. The biggest single bucket of that cost on agent-eval runs is the **build/fix loop**: roughly 150K tokens and 2–4 turns per run, mostly spent pasting MSBuild output into context and inferring what to do next. `mur check` is a thin wrapper around `dotnet build` that compresses each diagnostic into one short line. The "did-you-mean" engine in this PR goes further — it tries to answer *the agent's next question* in the same line, so the agent can fix the bug without an extra inspection turn.

**Concrete example.** Before:

```
Program.cs(34,16): error CS1061: 'ButtonElement' does not contain a definition for 'OnClick'
```

After:

```
Program.cs:34:16  E  CS1061  'ButtonElement' has no member 'OnClick'
                              → try: Button(label, onClick: x)   // [factory has Action onClick parameter]
```

The agent reads the suggestion, applies it, and moves on. No second turn spent grepping the codebase for the right name.

### Why we care about being right

A *wrong* suggestion is worse than no suggestion. The agent trusts it and burns turns chasing a phantom fix. So the bar to emit a hint is **high confidence or stay silent.** "Silent is correct. Wrong is not." Every design choice in this doc — thresholds, gates, validation steps — exists to keep that invariant.

### Why this is load-bearing, not a token-saving optimization

A natural reading of the section above is "nice token-saver that base-model improvements will eventually erode." That reading is wrong on a 1–3 year horizon, and the system is sized for the structural view, not the optimization view.

Two conditions make the build-error-correction loop a *primary* feedback channel rather than an incremental nicety:

1. **Reactor is experimental and will keep churning.** API names, signatures, and shapes will change faster than any base model can be retrained against them. Models will keep proposing names that don't exist in the *current* Reactor, regardless of how strong the underlying coding model gets.
2. **WinUI 3 is weakly represented in training data and confused with adjacent frameworks.** WPF, Silverlight, WinUI 1, WinUI 2, and WinUI 3 share enough vocabulary that models trained on the union produce *plausibly-WinUI-shaped code* that doesn't compile against Reactor. The 525-run corpus directly evidences this: agents reach for `.VerticalAlignment`, `.Style(...)`, `Theme.AppBackground` — WinUI/WPF muscle-memory names. Tier-2 fuzzy match can't bridge "VerticalAlignment → VAlign" by edit distance; only deterministic vocabulary-translation rules can (see §9, future improvements).

The combination — experimental API + structurally weak / cross-framework-confused training data — means the build-error-correction loop is the dominant feedback mechanism through which agents reconcile their prior-framework priors with Reactor's reality, for the foreseeable future.

**Sunset criterion (explicit, not "forever").** Decommission `mur check`'s suggestion engine when both hold: (a) Reactor's public API has been stable for ≥ 12 months, and (b) a held-out Reactor-touching eval reaches ≥ 90 % first-build-OK without suggestion assistance on ≥ 2 vendor-distinct models. The trace/`--final` mode keeps living; only Tiers 2–4 sunset.

---

## 2. What you actually see

`mur check` emits one line per diagnostic. The shape is:

```
<file>:<line>:<col>  <SEV>  <CODE>  <short message>[  → <hint>][  // <evidence>]
```

- `<SEV>` is `E`, `W`, or `I` (error, warning, info).
- `<hint>` is **either** a static skill-file pointer (when the code is one of the 12 known `REACTOR_*` analyzer IDs) **or** a `→ try: …` suggestion from the new Roslyn-backed engine.
- `<evidence>` is a short justification so the agent (and the human reader) can sanity-check the hint. Examples: `[factory has Action onClick parameter]`, `[member of TextBlockElement, similarity 0.91]`.

Exit code is whatever `dotnet build` returned. `mur check` does not invent its own exit semantics.

**Engineer detail.** The format is generated in `CheckCommand.Diag.Format` at `src/Reactor.Cli/Check/CheckCommand.cs:202`. Tier-1 hints (the analyzer-ID lookup) win ties over Tier-2 suggestions — see `Diag.HintFor`.

---

## 3. The four tiers, in plain English

The engine has four layers. Each one is more elaborate than the last. **The earlier layers do most of the work**; the ML-flavored layer (Tier 4) is a future tiebreaker, not a load-bearing pillar.

```
mur check <path>
   │
   ▼ run `dotnet build`, parse each diagnostic line
   │
   ▼ for each parsed diagnostic that touches a Microsoft.UI.Reactor.* symbol:
   │
   │   Tier 1 — analyzer-ID hint table       (SHIPPED, pre-existing)
   │             A static lookup: 12 REACTOR_* codes → SKILL.md anchor.
   │
   │   Tier 2 — Roslyn semantic suggester    (SHIPPED in this PR)
   │             Load the project, resolve the symbol at the error site,
   │             fuzzy-match against the real Reactor API surface.
   │
   │   Tier 3 — induced pattern rules        (FUTURE, Phase 3)
   │             Small hand-authored rewriters seeded from the mining corpus.
   │
   │   Tier 4 — learned confidence ranker    (FUTURE, only if needed)
   │             GBDT over hand-engineered features. Built only if Tier 2 +
   │             Tier 3 leave a meaningful tail.
   │
   ▼ attach the highest-confidence hint above threshold
   ▼ (future) pre-emit ranker decides whether the diagnostic is even worth showing
   ▼ write one line per surviving diagnostic
```

### Tier 1 — analyzer-ID hint table (already shipped)

When the Reactor analyzers fire (codes like `REACTOR_HOOKS_001`, `REACTOR_THEME_001`), they already carry meaning the team encoded. The hint table maps each code to a short pointer like `"SKILL.md §Hooks (call hooks unconditionally)"`. The agent reads five lines of guidance instead of grepping the codebase.

This tier has no ML, no fuzzy match, no probabilities. Just a `switch`. It existed before this PR; it's unchanged.

**Where it lives.** `CheckCommand.Diag.HintFor` at `src/Reactor.Cli/Check/CheckCommand.cs:225`.

### Tier 2 — Roslyn semantic suggester (the bulk of this PR)

Tier 2 handles **C# compiler errors** (codes starting with `CS`) where the receiver, type, or member is a Reactor symbol. These are the everyday "I don't remember the exact API name" mistakes: a wrong member name, a missing factory parameter, an argument with the wrong type.

In plain language: when a `CS` error fires on a Reactor type, Tier 2 loads the project into Roslyn (the C# compiler's library form), looks at the actual symbol the user named, asks "what's the closest real thing here?", and proposes that real thing if it's similar enough.

The five codes it covers:

| Code | What it means | What Tier 2 does |
|---|---|---|
| **CS1061** | "Type `Foo` has no member `Brr`" | Walk `Foo`'s members, find the closest name by similarity. Also: if the missing name lowercase-matches a parameter of an enclosing factory call (e.g. `.OnClick(...)` on `Button(...)`), suggest the named-argument form. |
| **CS0103** | "Name `X` is not in scope" | Walk Reactor factory names, find the closest. |
| **CS0117** | "Type `T` has no static member `M`" | Walk `T`'s static members, find the closest. |
| **CS1503** | "Cannot convert argument type" | Two hand-coded heuristics: string-supplied-where-Element-expected → suggest `Caption`/`Heading`/`TextBlock`; `Action<T>` supplied where `Action` expected → say "drop the parameter." |
| **CS7036** | "No overload takes N args" | Rank overloads by parameter-count distance; propose the closest as a named-argument form. |

The output is always either a single suggestion above the threshold, or silence. There is no "maybe" path.

**Implementation.** `src/Reactor.Cli/Check/Suggesters/SymbolSuggester.cs`. Orchestrated by `SuggesterOrchestrator` at `src/Reactor.Cli/Check/SuggesterOrchestrator.cs`. The compilation cache lives in `CompilationLoader.cs`. The factory index (a `Dictionary<string, List<IMethodSymbol>>` of every `Microsoft.UI.Reactor.Factories.*` static method, keyed by name) is in `FactoryIndex.cs`.

**Compiler-folks detail.**
- One `CSharpCompilation` per `mur check` invocation, parsed from all `.cs` files under the project root, references resolved from the post-restore `obj/project.assets.json`. Cached by `(absolute-csproj-path, sorted-file-mtime-hash)`. Cold load ~200–500 ms, warm load ~50 ms.
- The orchestrator walks from the MSBuild diagnostic's `(line, col)` to a `SyntaxNode`, then up the tree until it finds a `MemberAccessExpressionSyntax`, `InvocationExpressionSyntax`, `IdentifierNameSyntax`, or `ArgumentSyntax`. That node and its inferred receiver type are what the suggester actually sees.
- Suggesters are **pure functions** of `(Compilation, Diagnostic, SyntaxNode, ITypeSymbol receiver, FactoryIndex)`. No I/O. No mutable state. Unit-tested by constructing `CSharpCompilation`s in-memory.
- Roslyn `Microsoft.CodeAnalysis.CSharp` 4.8.0 was already a `PackageReference` in `src/Reactor.Cli/Reactor.Cli.csproj`; no dependency churn.

### Tier 3 — induced pattern rules (future, Phase 3)

Tier 3 will handle mistakes Tier 2 can't reach because they cross an **AST-shape boundary**, not just a name boundary. The canonical example: an agent writes `.OnClick(lambda)` *chained* on `Button(...)`. Tier 2 sees CS1061 on `OnClick`. The actual fix isn't a rename — it's moving the lambda *into the parent factory's argument list* as `onClick: lambda`. That's a tree rewrite.

Tier 3 will be a small set of hand-authored rules (one per file under `src/Reactor.Cli/Check/Rules/`), each tied to a frequent cluster found by the mining harness in spec 037. Each rule passes a six-bar **Validation Gate** before merge (frequency, cross-agent reproducibility, positive fixtures, negative counter-examples, independent reviewer signoff, kill-switch). The gate exists because a bad rule is worse than no rule — it contaminates every downstream agent session.

The infrastructure for Tier 3 (the `IRulePattern` interface, the registry, the `--disable-rule` flag) is not in this PR — it ships in Phase 3.

### Tier 4 — learned confidence ranker (future, only if needed)

Only built if telemetry shows Tier 2 + 3 leave a meaningful tail. Plan: a GBDT over hand-engineered features (Levenshtein, parameter-name overlap, factory-popularity-in-samples, AST-shape similarity, prior agent-accept rate per rule). Inputs: the candidate set produced by Tiers 2 + 3. Output: a re-ranked list with a calibrated confidence head.

We **deliberately do not propose a small LLM here.** Small models hallucinate without huge corpora, and Reactor's corpus is fundamentally limited. The deterministic system already has access to everything a small model would have to memorize (the api index, the sample apps). Tier 4 is a re-ranker, not a generator.

---

## 4. How the mining operation works

This is the data pipeline. It runs *outside this repo*, in `C:\Users\andersonch\Code\reactor-tokenusage\`, owned by the harness team. We consume its output.

### In plain language

1. We run the agent-eval harness on a prompt (e.g. "build me a kanban board with Reactor"). The harness logs every step the agent takes: every shell command, every file edit, every build attempt.
2. The harness watches for build failures. When a build fails, it records the diagnostics. When the agent then edits the source, the harness pairs the diagnostic with the agent's *next edit* — the human-equivalent fix for the error.
3. That pair — `(broken code at the diagnostic site, fixed code at the same site after the next edit)` — is a single row in `fixes.jsonl`. Pile up thousands of those rows across many runs and many prompts and you have a labeled dataset: "when the compiler said X, the human/agent fixed it by doing Y."
4. The harness also clusters similar rows. A cluster is "the same kind of mistake repeated by many runs." Each cluster becomes a candidate for a Tier-3 rule.

### Output artifacts

The harness produces four files per run-batch:

- **`fixes.jsonl`** — one row per `(broken, fixed)` pair. Fields include `run_id`, `turn`, `diag_code`, `receiver_type`, `member`, `before` text, `after` text, `fix_kind`.
- **`ranker-labels.jsonl`** — one row per (build, diagnostic) emission. The label `addressed_by_next_fix` tells us whether the agent's next edit touched that diagnostic's location. This is the supervised signal for the (future) §8 ranker.
- **`patterns.json`** — cluster summaries. Each cluster has an id, the diagnostic code, the receiver type, the fix kind, a frequency, a count, and exemplar run ids.
- **`unresolved.jsonl`** — pairs that didn't fit any cluster yet. A noise floor.

### Data checkpoints

We staged the harness handoffs into four named checkpoints:

| Checkpoint | What it produces | Blocks what | Status as of this PR |
|---|---|---|---|
| **A — pipeline smoke** | ≥ 3 unique pairs, schema verified | Phase 0 fixture types | ✓ landed 2026-05-10 |
| **B — calibration** | ≥ 50 unique pairs across ≥ 2 agents | Phase 1 threshold tuning | ✓ landed 2026-05-10 (51 fixes / 21 patterns / 63 ranker rows, single agent) |
| **C — rule induction** | ≥ 500 unique pairs across ≥ 2 agents | Phase 3 rule authoring | ⚠ partial — landed 2026-05-11 with 1,027 fixes / 104 clusters but **single agent only** (`gpt-5.5`). Cross-agent bar unmet; needs a second-agent drop before Phase-3 rule PRs open. |
| **D — ranker training** | ≥ 5K ranker-label rows, ≥ 1K negative class | Phase 4 learned ranker | not started |

The mining corpus mirrored into this repo lives at `docs/specs/tasks/038-tuning-reports/2026-05-11-525run-source/` (≈ 8 MB, four files). The raw event logs stay in the sibling repo.

### ML-practitioner detail (negative class, fingerprinting, labels)

- **Negative class.** Early audits found the harness only emitted *positive* (`addressed_by_next_fix: true`) rows. We needed both classes to train a ranker. Fixed in audit pass 2 (2026-05-10): one row per `(build, diagnostic)` emission, regardless of whether the agent fixed it.
- **Fingerprint quirk.** The harness uses a fingerprint to track whether a diagnostic survived to the run's final build. Adjacent CS8012 emissions whose timing tails differ (e.g. `"in 5.0s"` vs `"in 4.4s"`) currently fingerprint as distinct, so `still_present_at_run_end` is uniformly `false`. The primary `addressed_by_next_fix` label is unaffected, but the auxiliary `agent_ignored` label is broken. Tracked as a Phase-4 prerequisite.
- **The `ClassifyMatch` over-approximation.** When tuning we ask "did the suggester's proposed fix appear in the agent's actual fix?" The answer can be wrong in two directions: (a) the suggester proposed the right thing but the agent did an unrelated structural rewrite (false negative for us), or (b) the suggester proposed a real-but-wrong sibling member (true negative). We handle this by spot-checking the firings by hand — see "Per-code Tier-2 calibration" in the 525-run report.

---

## 5. How data tunes the model

There is no model in the ML sense yet — Tier 2 is deterministic fuzzy matching. But it has knobs, and the mining corpus is how we set them.

### In plain language

The suggester computes a **similarity score** between the agent's typed name and each real candidate name. We then turn that score into a **confidence**, and we only emit if confidence is above a **threshold**. The threshold is the knob we tune.

Tune too **high**: the suggester stays silent on cases we could have helped on. Recall drops.
Tune too **low**: the suggester confidently emits wrong suggestions. Precision drops, the agent burns turns on phantom fixes.

The tuning process is: take every captured `(broken, fixed)` pair from the corpus, run the suggester on the broken side, compare its top suggestion to the actual fix in the fixed side, and pick the threshold that gives ≥ 70% recall at ≤ 5% false-positive rate per diagnostic code.

### Engineer detail — the confidence formula

In `SymbolSuggester.ScoreToConfidence` (`src/Reactor.Cli/Check/Suggesters/SymbolSuggester.cs:330`):

```
conf = JaroWinkler(typed, candidate)                       // base signal, in [0, 1]
floor: if base < 0.70 → return 0 (the SimilarityFloor)
+ 0.1   if the margin to the runner-up is ≥ 0.2            (clear winner)
× 0.6   if the margin to the runner-up is < 0.03           (ambiguity discount)
+ 0.1   if there is only one candidate (treat as clear)
+ 0.1   if the receiver is a confirmed Microsoft.UI.Reactor.* type
cap at 1.0
```

JaroWinkler is a string-similarity metric that weights *prefix agreement* heavily. It's well-suited to programmer typos: `VerticleAlignment` matches `VerticalAlignment` at ~0.97, even though Levenshtein distance is 2.

Per-code thresholds live in `Thresholds.cs`:

| Code | Threshold | Rationale |
|---|---|---|
| CS1061 | 0.80 | Most CS1061 fixes are structural rewrites, not renames. Higher T reduces wrong-direction firings. The canonical `Button("x").OnClick(...)` factory-param case still clears it at conf 0.90. |
| CS0103 | 0.75 | Strongest signal — usually a mistyped factory name. 45/60 firings matched in the 525-run corpus. |
| CS0117 | 0.75 | Same shape as CS0103 — typo on an enum/constant. But see calibration notes: most empirical mistakes here are Reactor-name-confusion (`Theme.AppBackground` → `Theme.SolidBackground`), not edit-distance typos. Phase-3 rule territory. |
| CS1503 | 0.75 | Only fires on two hand-coded heuristics; default is fine. |
| CS7036 | 0.75 | Ranks overloads by parameter-count distance — a weak signal. Full Hamming-over-(kind, type) is a deferred follow-up. |

### ML-practitioner detail — calibration history

The 525-run report (`docs/specs/tasks/038-tuning-reports/2026-05-11-525run.md`) walks through every per-code firing in the corpus. Two findings drive the current configuration:

1. **JaroWinkler can't bridge "WinUI name → Reactor shortcut" pairs.** The agent's typical CS1061 mistake against Reactor types isn't a typo — it's a WinUI-style API name (`.VerticalAlignment`, `.Style`) whose correct Reactor replacement (`.VAlign`, fluent helpers) is too far in edit-distance for JaroWinkler to find. Similarity for `VerticalAlignment` ↔ `VAlign` is ~0.55, well below the 0.70 floor. The suggester then picks the second-closest member (`TextAlignment`) and emits a wrong answer at high confidence. **Diagnosis: this is Tier-3 rule territory, not a threshold-tuning problem.** Solution: ship the diagnostic-count gate (§7 below) as the safety net for now; author Tier-3 rules in Phase 3.

2. **CS0117 / `Theme.<X>Background` shows the same shape.** Agents write `Theme.AppBackground` (non-existent); suggester picks `Theme.Background` (closest real); correct answer is `Theme.SolidBackground` (sibling with different stem). Cluster C0019, frequency 1.6%, 16 events. Top Phase-3 rule target.

The calibration runs through a test harness at `tests/Reactor.Tests/CheckCommandTests/Tuning/`:

```pwsh
$env:MUR_TUNING_CORPUS = "docs\specs\tasks\038-tuning-reports\2026-05-11-525run-source\fixes.jsonl"
dotnet test tests\Reactor.Tests\Reactor.Tests.csproj -p:Platform=x64 `
  --filter FullyQualifiedName~ThresholdTuningTests.EndToEnd_corpus_run
```

The harness re-builds an in-memory `CSharpCompilation` from each captured fix's `before` text, runs the suggester, and labels the firing as match / no-match / silent / no-diag-in-compile. Output is a JSON snapshot and a Markdown report.

---

## 6. How a recommendation is picked

This is the step-by-step path a single diagnostic takes through the engine.

### In plain language

1. The user runs `mur check`. It shells out to `dotnet build` and reads stdout/stderr.
2. Each MSBuild diagnostic line is parsed into a `Diag` (`file`, `line`, `col`, `severity`, `code`, `message`).
3. We dedupe — MSBuild often prints the same diagnostic twice (once per project that references the file).
4. If the **diagnostic-count gate** says "skip suggestions for this invocation" (§7), we emit each diagnostic with only the Tier-1 hint (if any) and stop.
5. Otherwise, for each diagnostic in one of the five supported `CS*` codes:
   - Load the project's `CSharpCompilation` (cached).
   - Walk from the diagnostic's `(file, line, col)` to the relevant `SyntaxNode`.
   - Resolve the receiver type (for CS1061 / CS0117, the type whose member is missing).
   - **Reactor-touching check.** For CS1061 / CS0117 we only proceed if the receiver is in the `Microsoft.UI.Reactor.*` namespace. The other three codes self-filter inside the suggester.
   - Run `SymbolSuggester.Suggest`. It returns either a `SuggestionResult` with text + confidence + evidence, or `Silent`.
   - If the result is non-silent and clears the per-code threshold, the diagnostic line gets a `→ try: <text>  // [<evidence>]` suffix.
6. **Tier-1 wins ties.** If the diagnostic also matched the analyzer-ID hint table, the Tier-1 pointer is emitted instead of the Tier-2 suggestion.

### Engineer detail — control flow

```
CheckCommand.Run               (CheckCommand.cs:36)
 ├─ CheckArgs.TryParse          (CheckArgs.cs)
 ├─ shell out: dotnet build … (drains stdout+stderr concurrently to avoid deadlock)
 ├─ ParseDiagnostics            (regex against MSBuild's "(line,col): error CODE: msg [project]")
 ├─ ShouldEmitSuggestions       (CheckCommand.cs:123 — the gate)
 ├─ EmitDiagnostics             (CheckCommand.cs:151)
 │   └─ for each unique diagnostic key (file, line, col, code):
 │       └─ orchestrator.Suggest(diag, path)
 │           ├─ SupportedCodes filter         (CS1061/0103/0117/1503/7036)
 │           ├─ CompilationLoader.Load        (cached)
 │           ├─ FindTreeFor + ResolveSpan
 │           ├─ PickRelevantNode              (walk up to MemberAccess/Invocation/Identifier/Argument)
 │           ├─ ResolveReceiver
 │           ├─ IsReactorTouching             (CS1061/CS0117 gate)
 │           ├─ FactoryIndex.Build
 │           └─ SymbolSuggester.Suggest       (applies Thresholds.For(code))
 └─ trace.Write (optional, --trace)
```

The `Suggestion` (highest-confidence above threshold) is attached to the `Diag` for line formatting in `Diag.Format` (`CheckCommand.cs:202`).

### A worked example: CS1061 on `Button("hi").OnClick(...)`

1. MSBuild emits: `Program.cs(34,16): error CS1061: 'ButtonElement' does not contain a definition for 'OnClick'`
2. Parser produces `Diag(file=Program.cs, line=34, col=16, sev=error, code=CS1061, msg="…OnClick…")`.
3. Gate counts CS-prefixed diagnostics in the invocation. If ≥ 3, proceed.
4. Orchestrator loads the compilation, walks to the `MemberAccessExpressionSyntax` node for `.OnClick`, resolves the receiver as `Microsoft.UI.Reactor.ButtonElement`.
5. `SuggestForCS1061` extracts the missing name `OnClick`, lower-camels it to `onClick`, asks the `FactoryIndex` "is there a factory with a parameter named `onClick`?"
6. The factory `Button(string label, Action onClick)` matches. The suggester checks that the receiver type and the factory's return type are assignable to each other. They are.
7. Suggester builds the suggestion text `Button(label, onClick: x)` and the evidence `factory has Action onClick parameter`, with confidence 0.9.
8. 0.9 ≥ 0.80 (CS1061 threshold), so the line is emitted with the `→ try:` suffix.

### A worked example: CS1061 the engine *should* stay silent on

`MyType().Garbage(...)` where neither factory parameters nor `MyType`'s members include anything similar to `Garbage`. The fuzzy match returns scores below the 0.70 similarity floor; the suggester returns `Silent`; the line is emitted unchanged. **This is the desired behavior.** Silent is correct.

---

## 7. The "small project" gate

### In plain language

`mur check` itself has overhead: ~5–8 seconds per invocation, mostly the Roslyn compilation load. For projects with a few hundred lines of code and one or two trivial errors, that overhead is bigger than the savings. The first EC1 eval batch showed this empirically — the small `calc` benchmark regressed +21% cost when we turned the suggester on. The kanban benchmark, which has more API surface to explore and bigger build failures, won by −24%.

The fix is a **diagnostic-count gate**: skip Tier-2 suggestions on an invocation that has fewer than `N` unique CS-prefixed diagnostics. Default `N = 3`. Override via `--suggest-threshold <N>` (`0` disables the gate).

### Why N = 3

The 525-run corpus's distribution of CS-diagnostics-per-build:

```
median = 2, p75 = 3, p90 = 4, p95 = 6, mean = 2.40

  1 diagnostic  : 220 builds (42.9%)
  2 diagnostics : 146 builds (28.5%)
  3 diagnostics :  59 builds (11.5%)
  4 diagnostics :  40 builds ( 7.8%)
  5+            :  48 builds ( 9.4%)
```

With `N = 3`, 71.3% of failing builds skip Tier-2 entirely (small fixes the agent can handle unaided); 28.7% get suggestions (bigger failures where a hint saves a turn). This matches the calc-vs-kanban split exactly.

### EC1 re-run with the gate on (2026-05-10)

After landing the gate, the same 5×N matrix re-ran:

| Arm | Cost (mean) | Cost (median) | Turns | Tier-2 firing rate |
|---|---|---|---|---|
| `reactor-calc` (base) | $3.12 | $3.30 | 10.4 | — |
| `reactor-calc-mur-check` | $3.00 | $3.00 | 10.0 | 1/5 (20%) |
| `reactor-kanban` (base) | $5.82 | $5.40 | 16.4 | — |
| `reactor-kanban-mur-check` | $3.90 | $3.30 | 9.0 | 4/5 (80%) |

Paired deltas vs base: **calc −4% cost** (was +21% without the gate), **kanban −33% cost** (was −24%; preserved and grew). First-build OK 5/5 on both variant arms. Both Phase 1 pass-criterion bars met.

### Engineer detail

The gate lives at `CheckCommand.ShouldEmitSuggestions` (`src/Reactor.Cli/Check/CheckCommand.cs:123`). It counts unique `(file, line, col, code)` tuples for `CS*` diagnostics — the same dedup key `EmitDiagnostics` uses, so MSBuild's per-project repeats don't inflate the count. Threshold `0` short-circuits to "always emit."

Open watch-item carried forward to Phase 2: in the EC1 re-run, one of five kanban-variant runs hit 0 firings and tracked the long-tail base path. The gate's "fewer than 3 unique CS" condition appears to interact with the agent's path through the problem, not just the project's static shape. CV widened from 24% to 54% on the kanban variant. Not enough to block Phase 1 merge, but Phase 2 telemetry should track per-run firing counts so we can characterize this tail.

---

## 8. What's shipped in this PR

The feature branch `feat/038-mur-check` lands **Phase 0 (instrumentation)** and **Phase 1 (Tier 2 Roslyn semantic suggester)**, including the gate.

### Phase 0 — instrumentation

- `--trace <path>` flag on `mur check`. Writes a JSONL stream of every parsed diagnostic, one row per diagnostic. Schema: `{ts, code, severity, file, line, col, msg, receiver_type?, member?, mode}`. Trace is opt-in, never written by default, never includes source code text, never includes absolute paths outside the project root.
- Folder structure: `src/Reactor.Cli/Check/{Suggesters,Rules}/` with README pointers; mirrored test folders.
- A smoke fixture (`tests/Reactor.IntegrationTests/MurCheck/Fixtures/SmokeFixture/`) plus a smoke test that drives the end-to-end pipeline.

### Phase 1 — Tier 2 Roslyn suggester

New files in `src/Reactor.Cli/Check/`:

- `Suggesters/ISuggester.cs` — interface + `SuggesterContext` + `SuggestionResult` records.
- `Suggesters/SymbolSuggester.cs` — the five CS-code paths described in §3.
- `Suggesters/StringSimilarity.cs` — JaroWinkler implementation.
- `Suggesters/Thresholds.cs` — per-code thresholds + similarity floor; async-local override for parallel-test safety.
- `CompilationLoader.cs` — load + cache `CSharpCompilation` per `(csproj, file-set-hash)`.
- `FactoryIndex.cs` — pre-filter over `Microsoft.UI.Reactor.Factories.*`.
- `SuggesterOrchestrator.cs` — wiring; orchestrates Roslyn resolution and applies the Reactor-touching gate.
- `Telemetry.cs` — opt-in (`MUR_TELEMETRY=1`) local-only JSONL at `~/.mur/telemetry/<yyyy-mm-dd>.jsonl`. Codes, suggester names, confidences only; **no source code, no absolute paths**.
- `TraceWriter.cs` — the `--trace` JSONL writer.

Changes:

- `CheckCommand.cs` — wires suggester into the existing pipeline; adds `ShouldEmitSuggestions` gate; preserves Tier-1's win-ties behavior.
- `CheckArgs.cs` — parser + `--help` for `--trace`, `--suggest-threshold`, etc.

Tests:

- `tests/Reactor.Tests/CheckCommandTests/` — unit suite covering: args parsing, pipeline (gate on/off, dedup, suggestion attachment), `CompilationLoader` cold/warm timings + invalid project handling, `FactoryIndex`, each suggester code path (positive + negative), trace + telemetry payload validation.
- `tests/Reactor.Tests/CheckCommandTests/Tuning/` — the corpus-driven threshold tuner.
- `tests/Reactor.IntegrationTests/MurCheck/` — end-to-end smoke test against a fixture.

### Calibration + eval

- Thresholds calibrated against the **50-run corpus** (Data Checkpoint B) on 2026-05-10. Per-code values landed in `Thresholds.cs`.
- Re-validated against the **525-run corpus** (Data Checkpoint C, single-agent) on 2026-05-11. All thresholds held at current values. Two top Phase-3 rule targets surfaced: CS0117/Theme `*Background → SolidBackground`, and CS1061/`*Element` WinUI-name → Reactor-shortcut family.
- **EC1 re-run with the gate passes both arms** (2026-05-10): calc cost −4%, kanban cost −33%. Phase 1 merge-bar cleared.

### Deferred follow-ups (cleanly scoped, not blocking next phase)

- Reactor-touching integration fixture for the canonical CS1061 `Button.OnClick` case (needs WindowsAppSDK restore on every test run).
- Wall-time perf trait test against a WinUI fixture.
- Full Hamming-vector overload ranking in CS7036.
- Return-type assignability filter in CS0103.
- Property-accessor filter in `CollectStaticMembers` (`get_*` / `set_*` synthesized methods leak into CS0117 suggestions).

### Tracked harness-side prerequisite

- `still_present_at_run_end` is uniformly `false` because of a fingerprint quirk on adjacent CS8012 emissions with different timing tails. Doesn't affect the primary `addressed_by_next_fix` label, but breaks the auxiliary `agent_ignored` label that the future auto-suppression telemetry hook depends on. Phase-4 prerequisite, not a Phase-1 blocker.

---

## 9. Future improvements

Phases 2–4 will land after Phase 1 merges. They unlock independently of each other once their data and code blockers clear.

### The recurring failure mode: WinUI/WPF vocabulary confusion

Before describing the phases, it's worth naming the *shape* of failure the rest of this section is designed against, because it isn't an edge case — it is the central, recurring pattern the 525-run corpus surfaces, and it directly motivates the Class-B rule split in Phase 3.

**The pattern, in plain language.** An agent sits down to write Reactor. The agent has seen a lot of WPF, some Silverlight, and some WinUI (1, 2, 3) in its training data — and very little Reactor. So when it reaches for a property or method on a Reactor type, its muscle memory hands it a WinUI-shaped name: `.VerticalAlignment`, `.Style(BuiltInStyle)`, `Theme.AppBackground`, `.HorizontalAlignment`. The C# compiler rejects these because the Reactor types don't expose those members. CS1061 or CS0117 fires. Then the agent burns a turn (or several) figuring out what Reactor *does* expose. That's the build/fix loop the whole spec is targeting.

**Why Tier 2 alone can't solve it.** Tier-2 fuzzy match tries to find the closest real member name to the typo. JaroWinkler similarity between `VerticalAlignment` and `VAlign` is roughly 0.55 — well below the 0.70 floor. So Tier 2 either stays silent (good but unhelpful) or picks the second-closest real member name (e.g. `TextAlignment`, which is wrong). The 525-run report quantifies this: every empirical CS1061 firing against a Reactor `*Element` type in that corpus was a wrong-direction suggestion driven by exactly this gap.

**Why it's structural, not transient.** The five-framework lineage (WPF, Silverlight, WinUI 1, WinUI 2, WinUI 3) all share vocabulary that is *almost* but not quite Reactor. Better base models don't fix this — they're trained on the same lineage and have the same priors. New Reactor releases don't fix this either — they keep the Reactor API names roughly stable while WinUI muscle memory continues to dominate the prior. The only deterministic way to break the cycle is a *vocabulary-translation layer* that maps prior-framework names to Reactor names: a Class-B rule per pair. This is exactly what spec 038 Phase 3 schedules under the "Class B — vocabulary-translation" bucket described below.

### Phase 2 — MSBuild passthrough + deterministic pre-emit ranker

The four tiers decide *what hint to attach*. The pre-emit ranker decides *whether a diagnostic should be shown to the agent at all, right now*. This matters because raw MSBuild output is dominated by diagnostics whose resolution is **not** on the critical path to a clean build — NuGet noise, MSBuild reference-resolution chatter, IDE style hints, nullable warnings on template code. If the agent reads all of them every turn it (a) spends turns fixing things that didn't need fixing this turn, and (b) the real blocker scrolls off attention.

Phase 2 ships:

- A **policy table** (hand-authored, ~30 codes) mapping `(code, mode)` to a score. CS errors → 1.0/1.0. `REACTOR_*` Warning → 0.9/1.0. `CS1591` (XML doc) → 0.0/0.5. Etc.
- **Mode flags** — `--strict`, `--final`, `--quiet`, `--emit-threshold <N>`. The default mode (no flag) is *iteration*: aggressive suppression for the inner build/fix loop. `--final` emits everything and is the explicit "I am done iterating" gate.
- **MSBuild passthrough via `--`**. `mur check [<path>] [mur-flags...] [-- <msbuild args>...]`. Defaults like `--nologo`, `-v:m`, `-p:Platform={host arch}` inject only if the user didn't supply the same flag in the passthrough section. Detection by flag name, not value.
- **Suppress→error guardrail**. CI checks that every code suppressed in iteration mode does *not* appear as an error in a subsequent `--final` pass. If it does, the policy table is wrong and CI fails.

### Phase 3 — induced and authored pattern rules (Tier 3)

Tier-3 rules come in **two classes** (spec §6 split, motivated by the load-bearing framing in §1):

**Class A — *induced* rules.** Hand-authored small rewriters seeded from the mining clusters. Top three targets from the 525-run corpus:

1. **CS0117 / Theme — `*Background → SolidBackground`** (C0019, 1.6%, 16 events). A small lookup table from common-wrong-name → canonical Reactor token.
2. **CS1955 / GridSize — missing parens on factory** (C0004, 10.7%, 110 events; **largest single bucket in the corpus**). `GridSize.Star → GridSize.Star()`. First cross-tier addition — Tier 2 doesn't cover CS1955 today.
3. CS1061 cases that survive Tier 2 — e.g. structural rewrites the fuzzy match can't reach.

Justification bar: cluster `frequency ≥ 0.05` AND `count ≥ 10` AND cross-agent reproducibility.

**Class B — *vocabulary-translation* rules.** Deliberately authored from a curated WPF / Silverlight / WinUI 1 / WinUI 2 / WinUI 3 → Reactor name table. These are the *structurally-justified* rules from the load-bearing argument — we know prior-framework muscle memory will surface as confused Reactor code regardless of what the corpus shows in any given month. Examples:

- `.VerticalAlignment(x)` (WinUI/WPF) → `.VAlign(x)` (Reactor)
- `.HorizontalAlignment(x)` → `.HAlign(x)`
- `Theme.AppBackground` (plausibly-WinUI) → `Theme.SolidBackground` (Reactor)
- `.Style(BuiltInStyle)` → fluent-modifier family

**Frequency bar is waived for Class B** — the empirical justification is "the prior framework exists and models trained on it have strong priors that surface as confused Reactor code." Cross-agent reproducibility, positive/negative fixtures, reviewer signoff, and the kill-switch all still apply.

**Symbol-binding (decided).** Both classes bind their target types and members to Roslyn `ISymbol` references resolved against the live `Compilation`, **not** by string-matching `MemberAccess.Name.ValueText`. When Reactor renames `VAlign` in a future minor, a string-matched rule silently breaks; a symbol-bound rule fails resolution explicitly, self-disables with a trace warning, and surfaces via the CI gate. This is a one-time up-front cost that avoids rewriting every rule on future API churn.

**Cross-agent reproducibility bar.** The current 525-run corpus is `gpt-5.5`-only. Class A rules cannot open PRs until a second-agent corpus drop lands. Class B rules can — their justification is the documented prior-framework citation, not the corpus.

### Phase 4 — telemetry-driven Tier 4 + learned §8 ranker (scheduled, deferred)

**Status change.** Earlier drafts framed this as "only if needed." The load-bearing framing in §1 promotes it to **scheduled, deferred until Data Checkpoint D delivers ≥ 1K negative-class ranker rows**. It is not a maybe; it is the work we open when the data is ready. The deterministic floor (Tiers 1–3 + the Phase-2 policy table) is sized to carry the experimental phase, and the learned ranker is what we ship once corpus volume justifies it.

Two independent models:

1. **Tier-4 confidence ranker.** GBDT over hand-engineered features (Levenshtein, parameter-name overlap, factory-popularity-in-samples, AST-shape similarity, prior agent-accept rate per rule). Re-ranks the candidate set produced by Tiers 2 + 3. Inference cost: microseconds.
2. **Learned pre-emit ranker.** Trained against `addressed_by_next_fix` as the binary label. GBDT or logistic regression. <100 KB ONNX. Calibrated via isotonic regression on a held-out fold so the score behaves like an emit-worthiness probability. Complements the Phase-2 policy table — the table is the floor (always-emit / never-emit anchors), the model fills the gray middle.

Escape hatch: a documented decision to ship Phase 4 with the deterministic table only. This remains the *unexpected* outcome and requires its own decision artifact, rather than being the default.

### Things explicitly out of scope (today and probably forever)

- **Auto-fix / write-back.** `mur check` emits text. The agent edits.
- **JSON / SARIF output.** One-line text only in v1. Structured emission is a future scope.
- **Cross-project / workspace-level reasoning.** Single `Compilation` per project.
- **A small LLM-based generator.** Reactor's training set is fundamentally limited; small models hallucinate without huge corpora. This is *the same condition* the load-bearing argument in §1 rests on: weak training data is why we need `mur check` in the first place, and it's also why we won't fix that gap by training a smaller model on the same scarce data. The deterministic system already has access to the things a small model would have to memorize (the api index, the sample apps, the live `Compilation`), and the learned components in Phase 4 are *re-rankers* over deterministic candidates, not generators.
- **Localization of `mur check` output.** Developer-facing tooling; en-US, same convention as `dotnet build`.

---

## 10. Glossary

| Term | Meaning |
|---|---|
| **Tier** | One of the four layers of the suggestion engine. Tier 1 (static), Tier 2 (Roslyn), Tier 3 (rules), Tier 4 (ML re-ranker). |
| **Diagnostic** | A single error/warning/info line emitted by the C# compiler or an analyzer. |
| **CS-code** | A diagnostic ID assigned by the C# compiler itself (`CS1061`, `CS0103`, …). Distinguished from analyzer codes like `REACTOR_*`. |
| **Receiver** | In `foo.Bar()`, the type of `foo`. Tier 2's CS1061 / CS0117 paths only fire when the receiver is in the `Microsoft.UI.Reactor.*` namespace. |
| **FactoryIndex** | Cached map from factory-name (e.g. `Button`) to its overloads under `Microsoft.UI.Reactor.Factories`. The "what real APIs exist?" oracle. |
| **JaroWinkler** | A string-similarity metric weighted toward prefix agreement. Outputs a score in `[0, 1]`. Used as Tier 2's fuzzy-match base signal. |
| **Confidence** | A score in `[0, 1]` derived from JaroWinkler + margin + receiver-Reactor-ness. Compared against a per-code threshold to gate emission. |
| **Threshold** | The per-code minimum confidence required to emit a suggestion. Tuned against the mining corpus. Held in `Thresholds.cs`. |
| **The gate** | `--suggest-threshold <N>` — skip Tier-2 on invocations with fewer than `N` unique CS-diagnostics. Defaults to 3. Mitigates the small-project regression. |
| **Mining corpus** | The `(broken, fixed)` pairs produced by spec 037's harness. Lives at `docs/specs/tasks/038-tuning-reports/2026-05-11-525run-source/` in this repo (mirrored copy). |
| **Eval Checkpoint (EC)** | A staged 5×N agent-eval batch run against `reactor-calc` and `reactor-kanban` to verify a phase's predicted cost/turn lift. EC1 has landed; EC2/3/4 are future. |
| **Validation Gate** | The six-bar pre-merge checklist every Tier-3 rule must pass (Phase 3 only). Exists because a bad rule is worse than no rule. Bar #1 (frequency ≥ 5 %) is waived for Class-B rules whose justification is the documented prior-framework citation rather than a corpus cluster. |
| **Class A / Class B rule** | Class A = *induced* — sourced from a `patterns.json` cluster, justified by frequency + count + cross-agent reproducibility. Class B = *vocabulary-translation* — deliberately authored from a curated WPF/WinUI → Reactor table, justified by the structural prior-framework-confusion argument. Both classes share the same shipping infrastructure (`IRulePattern`, symbol-binding, `--disable-rule`); they differ only in justification source. |
| **Load-bearing** | The framing applied to `mur check` in §1: this is structural infrastructure for the 1–3 year window in which Reactor is experimental and WinUI 3 is weakly represented + cross-confused in training data. Not a stopgap. Phase 4 is scheduled, not optional. |
| **Sunset criterion** | The explicit conditions under which `mur check`'s suggestion engine retires: (a) Reactor API stable for ≥ 12 months AND (b) ≥ 90 % first-build-OK on a held-out Reactor eval across ≥ 2 vendor-distinct models without `mur check` assistance. Named so "load-bearing" doesn't drift into "forever." |

---

**Maintenance.** This doc covers the system as of `feat/038-mur-check`. It will be updated as Phases 2–4 land. The canonical source-of-truth for decisions and pending work remains the spec + task docs under `docs/specs/`. For ongoing operational responsibilities (API-churn protocol, corpus freshness, per-rule accept-rate monitoring, annual sunset-readiness check) see the **Maintenance (load-bearing operation)** section of the task doc.
