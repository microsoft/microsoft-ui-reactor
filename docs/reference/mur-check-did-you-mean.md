# `mur check` Did-You-Mean — How It Works

This is a guided tour of the "did-you-mean" engine built into `mur check`. It is written so any reader can follow along: every section starts in plain language, then drills into the implementation for engineers, ML practitioners, and compiler folks who want the full picture.

The doc covers what's shipped through **Phase 0 (instrumentation)**, **Phase 1 (Tier-2 Roslyn suggester + diagnostic-count gate)**, **Phase 2 (MSBuild passthrough + deterministic pre-emit ranker)**, and the Phase-3 work currently in flight on `eval/spec-038-ec3-2026-05-11`: six Tier-3 rules + two critical correctness fixes + the cross-agent corpus drop (`claude-sonnet-4.6` × 525 runs) that closes Data Checkpoint C's reproducibility bar. Future-improvement sections sketch the remainder of Phase 3 (additional rules pending a third agent corpus) and Phase 4 (telemetry + learned ranker). The doc will be updated as those land.

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
- [8. What's shipped so far](#8-whats-shipped-so-far)
- [9. Future improvements](#9-future-improvements)
- [10. Glossary](#10-glossary)

---

## 1. The problem in one paragraph

When an AI agent writes a Microsoft.UI.Reactor (Reactor) app, it usually compiles, finds a build error, reads the error, edits the file, and tries again. Each of those round-trips costs time, tokens, and money. The biggest single bucket of that cost on agent-eval runs is the **build/fix loop**: roughly 150K tokens and 2–4 turns per run, mostly spent pasting MSBuild output into context and inferring what to do next. `mur check` is a thin wrapper around `dotnet build` that compresses each diagnostic into one short line. The "did-you-mean" engine in this PR goes further — it tries to answer *the agent's next question* in the same line, so the agent can fix the bug without an extra inspection turn.

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
   ▼ pre-emit ranker (Phase 2): drop diagnostics below the active threshold
   │   for this mode (iteration/strict/final/quiet). Trace records the full
   │   parsed list either way; stdout only sees the survivors.
   │
   ▼ diagnostic-count gate (Phase 1): on small builds (<3 unique CS errors)
   │   skip Tier-2 fuzzy match. Tier-3 rules still run — they're precision-
   │   anchored and don't share Tier-2's noise concern.
   │
   ▼ for each surviving diagnostic:
   │
   │   Tier 1 — analyzer-ID hint table       (SHIPPED, pre-existing)
   │             A static lookup: 12 REACTOR_* codes → SKILL.md anchor.
   │
   │   Tier 2 — Roslyn semantic suggester    (SHIPPED, Phase 1)
   │             Load the project, resolve the symbol at the error site,
   │             fuzzy-match against the real Reactor API surface.
   │
   │   Tier 3 — induced + vocabulary rules   (SHIPPED in part, Phase 3)
   │             Small symbol-bound rewriters. Six rules live today
   │             (three Class-A induced + three Class-B vocabulary).
   │             Rules can cover diagnostic codes Tier-2 doesn't
   │             (e.g. CS1955). Rules WIN over Tier-2 when both match.
   │
   │   Tier 4 — learned confidence ranker    (FUTURE, scheduled, Phase 4)
   │             GBDT over hand-engineered features. Blocked on Data
   │             Checkpoint D's 5K labeled rows with negative class.
   │
   ▼ attach the highest-confidence hint above threshold (Tier-3 wins ties
   ▼   over Tier-2; Tier-1 wins ties over Tier-2 at the format layer)
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

### Tier 3 — induced + vocabulary rules (shipped, Phase 3, ongoing)

Tier 3 handles mistakes Tier 2 can't reach because they cross either an **AST-shape boundary** (the fix isn't a name change — it's a structural rewrite) or a **vocabulary-distance boundary** (the right answer is too far from the wrong answer for fuzzy match to find it). Two examples make the boundary concrete:

- **AST-shape.** Agent writes `Button("Save").OnClick(handler)` — chained on the factory. Tier 2 sees CS1061 on `.OnClick`. The actual fix isn't a rename; it's moving the lambda *into the parent factory's argument list* as `onClick: handler`. Tier 3's `ButtonOnClickFactoryMoveRule` knows that shape and emits exactly the right rewrite.
- **Vocabulary-distance.** Agent writes `GridSize.Auto()` (extra parens). The fix is `GridSize.Auto` (no parens, because `Auto` is a property, not a method). JaroWinkler can't help here — there's no edit-distance gradient; the typo is *structural*. Tier 3's `GridSizeFactoryParensRule` matches the exact symbol shape and proposes the parens-removal rewrite. 146 events in the cross-corpus mining data, top single bucket.

Each rule is a small file under `src/Reactor.Cli/Check/Rules/`, registered into `RuleRegistry.Default` by reflection on assembly load. Rules MUST bind their target types and members through `RuleSymbolResolver` — Roslyn `ISymbol` lookup against the live `Compilation`, never raw string-matching against `MemberAccess.Name.ValueText`. When Reactor renames an API in a future minor release, a string-matched rule silently breaks; a symbol-bound rule fails resolution explicitly, self-disables, and surfaces in `--list-rules` as `self-disabled (unresolved: <target>)`. The CI gate at `RuleTargetResolutionTests` runs every registered rule's `DeclaredTargets` through the live Reactor compilation on every build, so a missed rename breaks CI loudly.

Each rule passes a six-bar **Validation Gate** before merge (frequency, cross-agent reproducibility, positive fixtures, negative counter-examples, independent reviewer signoff, kill-switch). The gate exists because a bad rule is worse than no rule — it contaminates every downstream agent session. Class-B (vocabulary-translation) rules waive bar #1 (frequency) — their justification is the documented prior-framework citation rather than a corpus cluster. The audit at `docs/specs/tasks/038-tuning-reports/2026-05-11-cross-agent-audit.md` is where bar #2 (cross-agent) gets formally cleared per cluster.

**Six rules shipped to date** (three Class-A induced + three Class-B vocabulary):

| Rule | Class | Code(s) | Receiver | What it suggests | Cross-agent events |
|---|---|---|---|---|---|
| `ThemeBackgroundSuffixRule` | B (also clears A) | CS0117 | `Theme` | `Theme.SolidBackground` for any `Theme.<X>Background` miss | 27 |
| `AlignmentShortcutRule` | B | CS1061 | Reactor `*Element` | `.HAlign(...)` / `.VAlign(...)` for the WinUI alignment property names | 22 |
| `ButtonOnClickFactoryMoveRule` | B | CS1061 | `ButtonElement` | `Button(..., onClick: ...)` factory move, with explicit anti-pattern call-out on `.OnTapped` | gpt-5.5 only; vocab-justified |
| `GridSizeFactoryParensRule` | A | CS1955 | `GridSize` | drop the parens (`GridSize.Auto`); **first cross-tier rule** (CS1955 outside Tier-2 codes) | **146** |
| `GridSizePxRenameRule` | A | CS0117 | `GridSize` | `GridSize.Px(...)` for `Pixel`/`Pixels`/`Fixed` legacy names | 9 |
| `TextBlockStyleHintRule` | A | CS1061 / CS0117 | `TextBlockElement` | fluent text helpers (`.FontSize`, `.SemiBold`, …); Reactor has no `Style` member | 5 across both `.Style(...)` and `with { Style = ... }` shapes |

### Suggest-gate carve-out for Tier-3 rules

The `--suggest-threshold` gate from §7 (default 3 unique CS errors) was originally a single bar gating the whole suggester block. That calibration is correct for Tier-2 fuzzy match — JaroWinkler against Reactor's `*Element` receivers has near-0 % empirical precision below 3 diagnostics. It's wrong for Tier-3 rules, which are symbol-bound and high-precision by design. **Rules now run regardless of the gate.** Concretely: the orchestrator takes a `tier2Enabled: bool` constructor flag; `CheckCommand.Run` always builds the orchestrator (when the compilation loads) and passes the gate result in. Tier-2 stays gated; Tier-3 always runs when its diagnostic code surfaces.

This is the EC2 watch-item ("Phase-3 rules are the right lever — not Phase-2.x gate tuning") finally addressed in code. Without the carve-out, a 1–2-diagnostic build never runs any rule even when a rule would have nailed the fix. Locked down by `SuggesterOrchestratorRuleTests.Rule_fires_even_when_tier2_is_disabled_by_the_suggest_gate` + `Tier2_only_code_returns_null_when_tier2_is_disabled_and_no_rule_covers`.

### `CompilationLoader` ProjectReference fix (the silent-failure mode)

End-to-end smoke testing against `samples/apps/wordpuzzle` during Phase 3 surfaced a critical bug that hadn't been caught by unit tests: **every rule self-disabled on every real invocation against a Reactor app.** The unit tests use synthetic in-memory `CSharpCompilation`s built with stubs, so target resolution worked there. But `CompilationLoader.Load` produces a compilation from on-disk `project.assets.json`, and that loader only parsed the `targets` section (NuGet packages) — missing the `libraries.<id>` entries with `type=project`. Reactor itself is a *project reference* for every sample app, so its types were invisible to `RuleSymbolResolver.ResolveType`, every rule's `DeclaredTargets` failed, and the entire rule registry self-disabled silently.

The fix walks `libraries.<id>` entries with `type=project`, reads the referenced csproj's `<AssemblyName>` (or falls back to the basename), and locates the most-recently-built matching `.dll` under that project's `bin/` subtree. Regression locked by `CompilationLoaderTests.Resolves_ProjectReference_built_dll_from_project_assets_json` — a self-contained test that constructs a synthetic refproj + a real `MetadataReference.CreateFromFile`-loadable empty assembly + an assets.json that points at it, and asserts the dll is in the returned reference set.

The class of bug is worth naming: a unit-test suite that doesn't exercise the on-disk loader path can pass while every production invocation silently no-ops. The lesson generalized is that **rule infrastructure needs an end-to-end smoke test against a real Reactor app**, not just against synthetic compilations. The wordpuzzle smoke pattern in §9 of the EC3 eval handoff doc is the new floor for any rule-touching PR.

### Tier 4 — learned confidence ranker (future, scheduled)

Scheduled, deferred until Data Checkpoint D delivers ≥ 1K negative-class ranker rows. Plan: a GBDT over hand-engineered features (Levenshtein, parameter-name overlap, factory-popularity-in-samples, AST-shape similarity, prior agent-accept rate per rule). Inputs: the candidate set produced by Tiers 2 + 3. Output: a re-ranked list with a calibrated confidence head.

We **deliberately do not propose a small LLM here.** Small models hallucinate without huge corpora, and Reactor's corpus is fundamentally limited — that's the same condition the load-bearing argument in §1 rests on. The deterministic system already has access to everything a small model would have to memorize (the api index, the sample apps, the live `Compilation`). Tier 4 is a re-ranker over deterministic candidates, not a generator.

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

| Checkpoint | What it produces | Blocks what | Status |
|---|---|---|---|
| **A — pipeline smoke** | ≥ 3 unique pairs, schema verified | Phase 0 fixture types | ✓ landed 2026-05-10 |
| **B — calibration** | ≥ 50 unique pairs across ≥ 2 agents | Phase 1 threshold tuning | ✓ landed 2026-05-10 (51 fixes / 21 patterns / 63 ranker rows, single agent) |
| **C — rule induction** | ≥ 500 unique pairs across ≥ 2 agents | Phase 3 rule authoring | ✓ **closed 2026-05-11** by the cross-agent audit. First drop (gpt-5.5 × 525): 1,027 fixes / 104 clusters. Second drop (claude-sonnet-4.6 × 525): 368 fixes / 564 ranker rows / 41 clusters. Audit at `docs/specs/tasks/038-tuning-reports/2026-05-11-cross-agent-audit.md`. |
| **D — ranker training** | ≥ 5K ranker-label rows, ≥ 1K negative class | Phase 4 learned ranker | not started |

The gpt-5.5 mining corpus mirrored into this repo lives at `docs/specs/tasks/038-tuning-reports/2026-05-11-525run-source/` (≈ 8 MB, four files); the sonnet-4.6 corpus is in the sibling repo's `mining-out/`. The raw event logs stay in the sibling repo on both sides.

### Cross-agent mining: comparing models to surface what's *structural* vs. *idiosyncratic*

A single model's failure distribution is suggestive but not definitive. A pattern that only one model produces might just reflect that model's quirks rather than a real Reactor authoring pitfall. The **cross-agent reproducibility bar** (Validation Gate bar #2) makes the distinction explicit: before we author a Tier-3 rule for a cluster, we want to see the same `(diag_code, receiver_type, fix_kind)` key produced by at least two distinct agents at non-trivial frequency.

The 2026-05-11 expansion drop captured that: **claude-sonnet-4.6 × 525 runs** alongside the existing **gpt-5.5 × 525 runs** at the same prompt set. The cross-corpus audit then compares cluster keys side-by-side and assigns one of three verdicts to each candidate:

- **STRONG** — the same key appears with `count ≥ 3` in both corpora. Class-A rule is unblocked.
- **WEAK** — both agents represented but one or both at `count < 3`. Author the rule with a WEAK tag in the Validation Gate template; the rule may still ship, but the bar #2 line cites the weaker evidence.
- **SINGLE** — present in only one corpus. Class-A is blocked until a third corpus surfaces the key. Class-B may still apply if a documented prior-framework citation justifies it structurally.

The same audit also surfaces interesting *anti-signals*: clusters that are heavy in one corpus and zero in the other. The 525-run audit flagged `CS1955` on `GridElement` (29 events in gpt-5.5, **0 in sonnet**) and `CS1061` on `ButtonElement.OnClick` (5 events gpt-5.5, **0 in sonnet**) as exactly this shape. Two hypotheses fit the data: (a) sonnet's skill-context steered it away from those mistakes, or (b) gpt-5.5 has a specific WinUI confusion that sonnet doesn't share. Either way, those rules are deferred to a third-agent corpus drop. The `ButtonOnClickFactoryMoveRule` is the carve-out — it ships as **Class B** because the WinUI 3 docs justify it structurally even though the empirical evidence is single-corpus.

The skilled-vs-unaided split in the sonnet corpus deserves its own note. The skilled half (125 of the 525 runs) deliberately tells the agent **not** to call `mur`, so it only sees `dotnet build` for diagnostics — but the agent is *still* reading the Reactor skill API guidance in its context. That corner surfaces errors agents make *while following* the skill, not while being rescued by `mur check`. The 160 ranker rows from the skilled flavor over-represent in clusters about skill-following, which is precisely the population we want for vocabulary-translation rules. The methodology note is in the handoff at `C:\temp\mur-handoff-sonnet-525.md` — including how the corpus owner ran extract+aggregate with the gpt-5.5 event logs temporarily moved aside, then restored, verifying zero leakage (every fixes.jsonl row carries `agent="claude-sonnet-4.6"`).

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

The 525-run report (`docs/specs/tasks/038-tuning-reports/2026-05-11-525run.md`) walks through every per-code firing in the corpus. Two findings drove the original Phase-1 configuration, and both have now had Phase-3 rules authored against them:

1. **JaroWinkler can't bridge "WinUI name → Reactor shortcut" pairs.** The agent's typical CS1061 mistake against Reactor types isn't a typo — it's a WinUI-style API name (`.VerticalAlignment`, `.Style`) whose correct Reactor replacement (`.VAlign`, fluent helpers) is too far in edit-distance for JaroWinkler to find. Similarity for `VerticalAlignment` ↔ `VAlign` is ~0.55, well below the 0.70 floor. The suggester then picks the second-closest member (`TextAlignment`) and emits a wrong answer at high confidence. **Diagnosis: this is Tier-3 rule territory, not a threshold-tuning problem.** Now addressed by `AlignmentShortcutRule` (Class-B, vocab-justified) and `TextBlockStyleHintRule` (Class-A, cross-agent STRONG after fix_kind collapse).

2. **CS0117 / `Theme.<X>Background` shows the same shape.** Agents write `Theme.AppBackground` (non-existent); Tier-2 picks `Theme.Background` or `Theme.CardBackground` (closest real); correct answer is `Theme.SolidBackground` (sibling with different stem). Now addressed by `ThemeBackgroundSuffixRule` (originally Class-B, promoted to Class-A by the cross-agent audit: 27 events combined across both corpora).

The cross-agent audit also surfaced a third shape neither of the above touches:

3. **CS1955 / `GridSize.Auto()` — extra parens on a static property.** Tier-2 doesn't cover CS1955 at all (it's outside `SupportedCodes`). The mistake isn't fuzzy — it's structural. Top single bucket in both corpora at 146 combined events; now addressed by `GridSizeFactoryParensRule`, the first cross-tier rule. Tier-3 unlocked the entire CS1955 code surface for `mur check` suggestions.

### ML-practitioner detail — what changed when the second-agent corpus landed

Before the sonnet-4.6 drop, Phase-1 calibration used a single-agent corpus and the 525-run report carried the disclaimer that "cluster reproducibility is suggestive, not definitive." After the drop, the cross-agent audit produced explicit STRONG/WEAK/SINGLE verdicts per cluster. The verdicts changed three things:

- **Rules that were going to ship as Class-B (vocab) for lack of better evidence** got promoted to Class-A where bar #2 (cross-agent reproducibility) cleared with `count ≥ 3` in both corpora. `ThemeBackgroundSuffixRule` is the canonical example — same rule, stronger evidence shelf.
- **Rules that LOOKED reproducible in single-agent data** got deferred when sonnet didn't reproduce them. `CS1955` against `GridElement` was 29 events in gpt-5.5 and zero in sonnet — a clean single-corpus signal that needs a third agent before becoming rule-eligible. This is a kind of evidence that wasn't accessible in a single-agent world.
- **Rules emerged that wouldn't have been visible at all without sonnet.** `TextBlockStyleHintRule` is the case in point: gpt-5.5 hits `.Style(...)` (fluent shape), sonnet hits `with { Style = ... }` (record-update shape) — the *same conceptual mistake* expressed in different syntax. Collapsing the two corpora's fix_kind classification produced a STRONG verdict that neither corpus could have produced alone.

The skilled-vs-unaided split in the sonnet corpus is also load-bearing for future calibration. Skilled rows (125 of 525) see the Reactor skill API guidance but no `mur check` — that's the corner of the corpus where errors-while-following-the-skill surface. Vocabulary-translation rules over-represent here because the agent is actively trying to *follow* Reactor's vocabulary; what surfaces is the cases where the skill's coverage was thin or the WinUI muscle memory dominated anyway.

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
4. The **pre-emit ranker** (Phase 2) drops diagnostics whose score is below the active mode's threshold. The trace file records every parsed diagnostic regardless; only stdout is filtered.
5. The **diagnostic-count gate** (Phase 1) checks whether the surviving CS-prefixed diagnostic count meets the threshold. If not, we mark Tier-2 disabled for this invocation. **Tier-3 rules still run** — they're precision-anchored, not subject to the same noise calibration.
6. For each surviving diagnostic, the orchestrator:
   - Loads the project's `CSharpCompilation` (cached). The loader walks both NuGet package compile assets *and* ProjectReference build outputs from `project.assets.json`. Without the ProjectReference path, Reactor itself is invisible and every rule self-disables — see §3 "`CompilationLoader` ProjectReference fix" for the bug story.
   - Walks from the diagnostic's `(file, line, col)` to the relevant `SyntaxNode` (`MemberAccessExpressionSyntax`, `InvocationExpressionSyntax`, `IdentifierNameSyntax`, or `ArgumentSyntax` — picked by `PickRelevantNode`).
   - Resolves the receiver type (for CS1061 / CS0117, the type whose member is missing).
   - **Tier-2 path (gated by diagnostic count + Reactor-touching check):** if the code is in `SupportedCodes` AND the Tier-2 gate is open AND the receiver namespace check passes, run `SymbolSuggester.Suggest`. Per-code threshold applies.
   - **Tier-3 path (always runs when a rule covers the code):** `RuleRegistry.BestMatch` iterates every enabled rule whose `DiagnosticCodes` contains this diag's code. Each rule first runs `TargetsResolve` (cheap, cached) — if any declared target fails to resolve, the rule self-skips and (if a trace is open) emits a `rule_self_disabled` event. Surviving rules call `TryMatch` and return either a non-silent `RuleSuggestion` or `Silent`. The highest-confidence match across rules wins.
   - **Tier-3 wins over Tier-2 when both match.** The rule's `Provenance` (`cluster:<id>` or `vocab:<framework>`) is appended to the evidence string so a maintainer can grep back to the motivating data.
7. **Tier-1 still wins format-level ties.** If the diagnostic also matched the analyzer-ID hint table, the Tier-1 pointer is emitted instead of the Tier-2/Tier-3 suggestion (`Diag.HintFor` at the format layer).

### Engineer detail — control flow

```
CheckCommand.Run               (CheckCommand.cs)
 ├─ CheckArgs.TryParse          (CheckArgs.cs)
 ├─ shell out: dotnet build … (drains stdout+stderr concurrently to avoid deadlock)
 ├─ ParseDiagnostics            (regex against MSBuild's "(line,col): error CODE: msg [project]")
 ├─ Ranker.PreEmit              (Phase 2 policy table; drops below-threshold diagnostics)
 ├─ ShouldEmitSuggestions       (the diag-count gate — controls Tier-2 only)
 ├─ CompilationLoader.Load      (cached; resolves NuGet + ProjectReference deps)
 ├─ build orchestrator with tier2Enabled = gate-result
 ├─ EmitDiagnostics
 │   └─ for each unique diagnostic key (file, line, col, code):
 │       └─ orchestrator.SuggestAgainst(diag, compilation)
 │           ├─ tier2Applies = tier2Enabled AND SupportedCodes.Contains(code)
 │           ├─ rulesApply    = any rule's DiagnosticCodes contains code
 │           ├─ if neither: return null (Tier-1 hint may still apply at format layer)
 │           ├─ FindTreeFor + ResolveSpan + PickRelevantNode + ResolveReceiver
 │           ├─ if tier2Applies AND IsReactorTouching(code, receiver):
 │           │     FactoryIndex.Build + SymbolSuggester.Suggest  → tier2Best
 │           └─ if rulesApply:
 │                 RuleSymbolResolver.For(compilation) (cached)
 │                 RuleRegistry.BestMatch(ctx, disabledRules, onRuleSelfDisabled)
 │                     ├─ for each rule: TargetsResolve (else self-disable callback)
 │                     └─ TryMatch; pick highest confidence
 │                 RULE WINS over tier2Best (spec §6)
 └─ trace.Write (optional, --trace; records all parsed diagnostics + rule self-disables + rule fires)
```

The `Suggestion` (highest-confidence above threshold, rule preferred over Tier-2) is attached to the `Diag` for line formatting in `Diag.Format`.

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

The gate lives at `CheckCommand.ShouldEmitSuggestions`. It counts unique `(file, line, col, code)` tuples for `CS*` diagnostics — the same dedup key `EmitDiagnostics` uses, so MSBuild's per-project repeats don't inflate the count. Threshold `0` short-circuits to "always emit."

### Tier-3 carve-out (Phase 3)

The gate originally wrapped the **entire** suggester block. Phase 3 demoted it to Tier-2-only: rules always run when their diagnostic code surfaces, regardless of how many CS errors are in the build. The orchestrator takes a `tier2Enabled: bool` constructor flag; `CheckCommand.Run` always builds the orchestrator (when the compilation loads) and passes the gate result in. Why: Tier-2 fuzzy match has near-0 % empirical precision on small builds (per the 525-run calibration on CS1061 against `*Element` receivers); rules are symbol-bound and don't share that noise profile. The EC2 watch-item phrased it as "Phase-3 rules are the right lever — not Phase-2.x gate tuning." This is that lever, in code. Locked down by two `SuggesterOrchestratorRuleTests` cases.

A consequence worth knowing: on iteration-mode builds with 1–2 diagnostics, the agent now sees rule suggestions where it previously saw none. This is the desired behavior, but it does introduce a new UX shape worth watching in EC3 — rules firing repeatedly across consecutive builds on the same unfixed line. Per-rule per-invocation dedup may need to land if traces show that pattern in practice.

### EC1 watch-item, retrospective

Open watch-item from Phase 1: in the EC1 re-run, one of five kanban-variant runs hit 0 firings and tracked the long-tail base path. The gate's "fewer than 3 unique CS" condition appeared to interact with the agent's path through the problem, not just the project's static shape. CV widened from 24 % to 54 % on the kanban variant. **The Phase-3 rule carve-out addresses this directly**: even on a 1-firing run, the agent now gets rule suggestions when the diagnostic matches a rule's code. EC3 will measure whether the long-tail tightens.

---

## 8. What's shipped so far

Phases 0–2 are merged to `main`. Phase 3 is in flight on `eval/spec-038-ec3-2026-05-11` with six rules + two critical correctness fixes; EC3 eval pending.

### Phase 0 — instrumentation (merged)

- `--trace <path>` flag on `mur check`. Writes a JSONL stream, one row per parsed diagnostic (plus auxiliary structured events). Trace is opt-in, never written by default, never includes source code text, never includes absolute paths outside the project root. Row kinds:
  - **diagnostic row** (default, no `kind` field): `{ts, code, severity, file, line, col, msg, receiver_type?, member?, mode}`.
  - **command header** (`kind: "command"`): `{ts, kind, argv, mode}` — full effective `dotnet build` argv at the head of the trace.
  - **rule self-disabled** (`kind: "rule_self_disabled"`): `{ts, kind, rule, unresolved_target, mode}` — emitted when a Tier-3 rule's declared target fails to resolve against the live compilation. Dedup'd per-invocation per-rule.
  - **rule fired** (`kind: "rule_fired"`): `{ts, kind, rule, code, confidence, evidence, file, line, mode}` — emitted whenever a Tier-3 rule attaches a suggestion to a diagnostic. Tier-2 hits do not emit this row; their firing rate is visible via the opt-in `MUR_TELEMETRY=1` channel.
- Folder structure: `src/Reactor.Cli/Check/{Suggesters,Rules}/` with README pointers; mirrored test folders.
- A smoke fixture (`tests/Reactor.IntegrationTests/MurCheck/Fixtures/SmokeFixture/`) plus a smoke test that drives the end-to-end pipeline.

### Phase 1 — Tier-2 Roslyn suggester + diagnostic-count gate (merged)

New files in `src/Reactor.Cli/Check/`:

- `Suggesters/ISuggester.cs` — interface + `SuggesterContext` + `SuggestionResult` records.
- `Suggesters/SymbolSuggester.cs` — the five CS-code paths described in §3.
- `Suggesters/StringSimilarity.cs` — JaroWinkler implementation.
- `Suggesters/Thresholds.cs` — per-code thresholds + similarity floor; async-local override for parallel-test safety.
- `CompilationLoader.cs` — load + cache `CSharpCompilation` per `(csproj, file-set-hash)`. (Extended in Phase 3 to resolve ProjectReferences — see below.)
- `FactoryIndex.cs` — pre-filter over `Microsoft.UI.Reactor.Factories.*`.
- `SuggesterOrchestrator.cs` — wiring; orchestrates Roslyn resolution and applies the Reactor-touching gate.
- `Telemetry.cs` — opt-in (`MUR_TELEMETRY=1`) local-only JSONL at `~/.mur/telemetry/<yyyy-mm-dd>.jsonl`. Codes, suggester names, confidences only; **no source code, no absolute paths**.
- `TraceWriter.cs` — the `--trace` JSONL writer.

**EC1 re-run with the gate (2026-05-10):** calc cost −4 % (mean), kanban cost −33 % (median −39 %). First-build OK 5/5 on both variant arms. Phase 1 merge-bar cleared.

### Phase 2 — MSBuild passthrough + deterministic pre-emit ranker + mode flags (merged)

The deterministic ranker decides *whether each diagnostic should be shown to the agent at all*, separately from "what hint should attach to it." This is where suppression of XML-doc warnings, MSBuild reference-resolution chatter, and nullable noise lives.

- **`Ranker/PolicyTable.cs`** — hand-authored score table for ~30 codes. CS errors → 1.0/1.0. `REACTOR_*` Warning → 0.9/1.0. `CS1591` (XML doc) → 0.0/0.5. A universal-error floor ensures any Error severity scores 1.0 regardless of code (the table can't accidentally hide a real build break).
- **`Ranker/Ranker.cs`** — pure `Score(in Diag, in RankerContext) → double` plus `ShouldEmit` gate. Phase-2 implementation is the PolicyTable lookup; recency, location, and accept-history terms wait for Phase-4 signals.
- **Mode flags** in `ArgsParser`/`CheckArgs`: `--strict` (promotes warnings to errors), `--final` (emits everything, the "I am done iterating" gate), `--quiet` (severity E only), `--emit-threshold <float>` overrides the active mode's threshold.
- **MSBuild passthrough via `--`**: `mur check [path] [mur-flags] [-- <msbuild args>]`. `mur` injects `--nologo`, `-v:m`, `-p:Platform={host arch}` only if the user didn't supply the same flag (detection by flag name, not value). When `--trace` is on, the effective `dotnet build` command line is recorded as a `kind: "command"` header row.
- **Suppress→error guardrail** at `tools/Reactor.MurCheckGuardrail/`. CI tool reads two trace files (iteration + final) and asserts every code suppressed in iteration mode does **not** appear as an error in `--final`. The tool re-uses `PolicyTable` directly via `InternalsVisibleTo`, so audit and runtime can never drift.

**EC2 PASS by median (2026-05-11):** calc-mur clean win on every metric (cost −5.1 %, tokens −5.8 %, turns −5.1 %, wall −7.9 %, variance 1.9× tighter); kanban-mur at exact cost median parity. First-build OK 5/5 both arms. SKILL framing softened so `--final` is described as an optional pre-merge sweep, not a task-completion requirement.

### Phase 3 — Tier-3 induced + vocabulary rules + symbol-binding contract (in flight)

Branch: `eval/spec-038-ec3-2026-05-11` at commit `2b7090f`. Six rules + the two critical correctness fixes + new tests. EC3 eval pending (handoff doc at `C:\temp\mur-ec3-handoff.md`).

**Infrastructure** in `src/Reactor.Cli/Check/Rules/`:

- `IRulePattern.cs` — interface: `Name`, `Provenance`, `DiagnosticCodes`, `DeclaredTargets`, `TryMatch(in RuleContext) → RuleSuggestion`. `RuleContext` bundles the `SyntaxNode`, `Diagnostic`, `ITypeSymbol? Receiver`, `SemanticModel`, `CSharpCompilation`, and `RuleSymbolResolver` so rules never re-discover the per-compilation cache.
- `RuleRegistry.cs` — reflection-based discovery on assembly load (Reactor.Cli only; no plugin loading from disk). Duplicate `Name`s throw at registry construction. `BestMatch` returns the highest-confidence match across enabled rules; pre-flight `TargetsResolve` short-circuits rules whose targets don't exist in the current compilation (self-skip via `onSelfDisabled` callback).
- `RuleSymbolResolver.cs` — `ResolveType(string)`, `ResolveMethod(INamedTypeSymbol, string)`, `ResolveMember(INamedTypeSymbol, string)`. Cached via `ConditionalWeakTable<CSharpCompilation, _>` so two callers with the same compilation reference share caches; per-compilation resolver identity is test-locked.
- CLI: `--disable-rule <Name>` (repeatable), `--list-rules` (prints name/provenance/status table; short-circuits before `dotnet build` runs). Unknown `--disable-rule` names warn instead of error.

**The six rules** — see the table in §3 for the full inventory. Three Class-A (induced from the cross-corpus audit) + three Class-B (vocabulary-translation, structurally justified). All six bind through `RuleSymbolResolver`; no string-matching.

**Two critical correctness fixes** (both blocked any real-world rule firing prior; surfaced by end-to-end smoke testing during Phase 3):

1. **`CompilationLoader` ProjectReference resolution.** The loader previously only handled NuGet packages from `project.assets.json`'s `targets` section. Reactor itself is a *project reference* for every sample app, so its types were invisible to `RuleSymbolResolver.ResolveType`. Every rule's `DeclaredTargets` failed to resolve and the entire registry self-disabled on every real `mur check` invocation — even though all unit tests passed (those use synthetic in-memory compilations). New code walks `libraries.<id>` entries with `type=project`, reads the referenced csproj's `<AssemblyName>` (falls back to the basename), and locates the most-recently-built matching `.dll` under that project's `bin/` subtree. Regression locked by `CompilationLoaderTests.Resolves_ProjectReference_built_dll_from_project_assets_json`, which constructs a synthetic refproj + a real `MetadataReference.CreateFromFile`-loadable empty assembly + an assets.json that references it.

2. **Suggest-gate carve-out for Tier-3 rules.** The gate wrapped the entire suggester block — when closed, neither Tier-2 nor rules ran. `SuggesterOrchestrator` now takes `tier2Enabled: bool`; `CheckCommand.Run` always builds the orchestrator and passes the gate result in. Tier-3 rules always run; Tier-2 stays gated on small builds where its fuzzy match has near-0 % precision. The EC2 watch-item ("Phase-3 rules are the right lever — not Phase-2.x gate tuning") finally in code. Locked down by `SuggesterOrchestratorRuleTests.Rule_fires_even_when_tier2_is_disabled_by_the_suggest_gate` + `Tier2_only_code_returns_null_when_tier2_is_disabled_and_no_rule_covers`.

**The `§3.1a` perf bound** is captured by `RulePerformanceTests.BestMatch_median_under_per_rule_budget` (`[Trait("Category","Perf")]`). Asserts median over 1000 iters stays under `0.5 ms × rule_count × 4× CI slack` on the canonical CS1061-on-`ButtonElement.OnClick` fixture. The factor scales automatically as the rule set grows.

**Cross-agent reproducibility audit** at `docs/specs/tasks/038-tuning-reports/2026-05-11-cross-agent-audit.md` — three STRONG Class-A targets (`GridSizeFactoryParensRule`, `ThemeBackgroundSuffixRule`'s reclassification, `GridSizePxRenameRule`); two more STRONG-after-collapse (`TextBlockStyleHintRule`'s two shapes, the `TemplatedListView` family generalized over `<T>`); one striking gpt-5.5-only signal (`CS1955` against `GridElement`, 29 events, zero in sonnet) deferred to a third-corpus drop.

**Test counts:** 7175 passing / 46 expected skips on the eval branch. CheckCommand subset: 217 (52 rules tests + 165 supporting).

### Deferred follow-ups (cleanly scoped, not blocking next phase)

- Reactor-touching integration fixture for the canonical CS1061 `Button.OnClick` case (needs WindowsAppSDK restore on every test run).
- Wall-time perf trait test against a WinUI fixture.
- Full Hamming-vector overload ranking in CS7036.
- Return-type assignability filter in CS0103.
- Property-accessor filter in `CollectStaticMembers` (`get_*` / `set_*` synthesized methods leak into CS0117 suggestions).
- Per-rule per-invocation dedup if EC3 traces show rules firing repeatedly across consecutive unfixed-line builds (introduced by the gate carve-out).

### Tracked harness-side prerequisite

- `still_present_at_run_end` is uniformly `false` because of a fingerprint quirk on adjacent CS8012 emissions with different timing tails. Doesn't affect the primary `addressed_by_next_fix` label, but breaks the auxiliary `agent_ignored` label that the future auto-suppression telemetry hook depends on. Phase-4 prerequisite, not a Phase-3 blocker.

---

## 9. Future improvements

Phase 2 has merged; Phase 3 is in flight on `eval/spec-038-ec3-2026-05-11`. What's left below is the remainder of Phase 3 (more rules pending a third-agent corpus) and all of Phase 4 (telemetry-driven Tier-4 ranker + learned §8 emit ranker). Each unlocks independently once its data and code blockers clear.

### The recurring failure mode: WinUI/WPF vocabulary confusion

Before describing what's left, it's worth re-naming the *shape* of failure the rest of this section is designed against, because it isn't an edge case — it is the central, recurring pattern both the gpt-5.5 and sonnet-4.6 525-run corpora surface, and it directly motivates the Class-B rule split in Phase 3.

**The pattern, in plain language.** An agent sits down to write Reactor. The agent has seen a lot of WPF, some Silverlight, and some WinUI (1, 2, 3) in its training data — and very little Reactor. So when it reaches for a property or method on a Reactor type, its muscle memory hands it a WinUI-shaped name: `.VerticalAlignment`, `.Style(BuiltInStyle)`, `Theme.AppBackground`, `GridSize.Pixel(x)`. The C# compiler rejects these because the Reactor types don't expose those members. CS1061 or CS0117 fires. Then the agent burns a turn (or several) figuring out what Reactor *does* expose. That's the build/fix loop the whole spec is targeting.

**Why Tier 2 alone can't solve it.** Tier-2 fuzzy match tries to find the closest real member name to the typo. JaroWinkler similarity between `VerticalAlignment` and `VAlign` is roughly 0.55 — well below the 0.70 floor. So Tier 2 either stays silent (good but unhelpful) or picks the second-closest real member name (e.g. `TextAlignment`, which is wrong). The 525-run report quantifies this: every empirical CS1061 firing against a Reactor `*Element` type in that corpus was a wrong-direction suggestion driven by exactly this gap.

**Why it's structural, not transient.** The five-framework lineage (WPF, Silverlight, WinUI 1, WinUI 2, WinUI 3) all share vocabulary that is *almost* but not quite Reactor. Better base models don't fix this — they're trained on the same lineage and have the same priors. New Reactor releases don't fix this either — they keep the Reactor API names roughly stable while WinUI muscle memory continues to dominate the prior. The only deterministic way to break the cycle is a *vocabulary-translation layer* that maps prior-framework names to Reactor names — Class-B rules. The cross-agent audit's "STRONG" verdicts confirm exactly this: the rules with the strongest evidence (`GridSize.Auto` parens, `Theme.*Background → SolidBackground`, `*Element.{H,V}orizontalAlignment → .{H,V}Align`) all describe vocabulary translations, not edit-distance typos.

### Remainder of Phase 3 — more rules, pending corpus and review

The six shipped rules cover the top frequency targets cleared by bar #2 of the Validation Gate. The audit identifies follow-on work in three buckets:

**Bucket A — STRONG-after-design-decision.** The `TemplatedListViewElement<T>` `missing_with_key` family appears in both corpora (gpt-5.5 7 events across `<string>`, `<ListItem>`, `<int>`, `<WorkItem>`, `<DemoItem>`; sonnet 1 event on `<Product>`). Rule must match the open generic type, not a closed-generic literal. Authoring blocker is rule-design (how to express "any `TemplatedListViewElement<_>`" in `DeclaredTargets`), not data. Likely the next rule to author.

**Bucket B — single-corpus STRONG, deferred to a third-agent drop.** Two notable signals:

- `CS1955` against `GridElement`, 29 events combined across `other`/`fluent_to_named`/`renamed_member`/`import_added` fix_kinds — gpt-5.5 only. Striking gap.
- `CS1061` against `ButtonElement` (renamed_member + other), 5 events gpt-5.5, 0 sonnet. Already covered structurally by Class-B `ButtonOnClickFactoryMoveRule` for the `.OnClick` shape; non-`OnClick` `*Element` member renames remain single-corpus.

Authoring these as Class-A is blocked until a third agent (e.g. `claude-opus-*` or `gemini-*`) mines a corroborating corpus. Authoring them as Class-B is possible *if* a documented prior-framework citation justifies each one structurally.

**Bucket C — Class-B vocabulary catalog expansion.** The vocab table at `docs/specs/tasks/038-vocab-table.csv` has 21 rows seeded from the 525-run report; it's the curated WPF/Silverlight/WinUI → Reactor name lookup. Each row that doesn't yet have a corresponding rule is a Class-B candidate. The rule-per-row authoring cost is small; the review cost dominates. The §3.2 "≥3 rules per PR" cadence is the throttle.

**Validation Gate bar #5 (independent reviewer signoff)** is still the open bar on every authored rule. The six shipped on `eval/spec-038-ec3-2026-05-11` need that signoff before they can land on `main`.

### Phase 4 — telemetry-driven Tier 4 + learned §8 ranker (scheduled, deferred)

Promoted to **scheduled, deferred until Data Checkpoint D delivers ≥ 1K negative-class ranker rows** (per the load-bearing framing in §1). It is not a maybe; it is the work we open when the data is ready. The deterministic floor (Tiers 1–3 + the Phase-2 policy table) is sized to carry the experimental phase, and the learned ranker is what we ship once corpus volume justifies it.

**Two independent models, one telemetry pipeline:**

1. **Tier-4 confidence ranker.** GBDT over hand-engineered features (Levenshtein, parameter-name overlap, factory-popularity-in-samples, AST-shape similarity, prior agent-accept rate per rule). Re-ranks the candidate set produced by Tiers 2 + 3. Inference cost: microseconds. Inputs to its training set come from the `~/.mur/telemetry/<date>.jsonl` agent-accept logs once those reach volume.
2. **Learned pre-emit ranker.** Trained against `addressed_by_next_fix` as the binary label, complementing the Phase-2 policy table. GBDT or logistic regression. <100 KB ONNX. Calibrated via isotonic regression on a held-out fold so the score behaves like an emit-worthiness probability. The table provides the floor (always-emit / never-emit anchors); the model fills the gray middle.

**Telemetry pipeline** (the §11 auto-suppression hook). Local-first JSONL aggregation reads `~/.mur/telemetry/` and computes per-code, per-rule accept rates. A rule whose accept-rate drops below 50 % over the last 200 invocations is auto-disabled at runtime with a warning logged on every subsequent `mur check`. A follow-up issue is auto-filed with the rule's exemplar pairs and the agent edits that diverged from the suggestion. Re-enabling requires a new PR with the same six-bar Validation Gate.

**Blocked on:**

- Data Checkpoint D from the harness team (≥ 5K ranker-label rows, ≥ 1K negative class). The negative class is now emitting per-build per-diagnostic — gap #3 was fixed in the audit pass 2 — but volume still has to accumulate.
- The `still_present_at_run_end` fingerprint bug (Phase-4 prerequisite). The auxiliary `agent_ignored` label is uniformly false right now because adjacent CS8012 emissions with different timing tails fingerprint distinctly; needs fixed before the auto-suppression hook can detect "suppressed-then-resurfaced" patterns.

**Escape hatch.** A documented decision to ship Phase 4 with the deterministic table only. This remains the *unexpected* outcome and requires its own decision artifact, rather than being the default.

### What EC3 will tell us

EC3 is the 5×N paired eval batch on `gpt-5.5` against `reactor-calc` and `reactor-kanban`, run from the `eval/spec-038-ec3-2026-05-11` branch. Handoff is at `C:\temp\mur-ec3-handoff.md`. The pass criterion is cumulative ~−14 % tokens vs. start-of-spec / ~−2 turns / CV ≤ start-of-spec CV.

**What the eval is actually testing.** Phase 2 measured the deterministic ranker's effect on small builds (calc) and big builds (kanban) — calc-mur won every metric, kanban-mur held cost parity by median but regressed by mean on a single outlier. Phase 3's bet is that the six shipped rules close that kanban-mean gap by catching the WinUI/WPF vocabulary-confusion misses that Tier-2 couldn't reach. Notably the EC2 batch measured **0/10 Tier-2 firings on both arms** — that result reflected the CompilationLoader silent failure plus the suggest-gate's pre-carve-out coverage, not the absence of useful suggestions. EC3 is the first eval where rules can actually fire end-to-end. Whatever the verdict, it isn't an incremental delta on EC2; it's a fresh measurement.

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
| **The gate (suggest-threshold)** | `--suggest-threshold <N>` — skip Tier-2 on invocations with fewer than `N` unique CS-diagnostics. Defaults to 3. Mitigates the small-project regression. **Tier-3 rules are NOT gated** — see "rule carve-out". |
| **Rule carve-out** | Phase-3 change: rules always run when their diagnostic code surfaces, regardless of the suggest-gate's state. The orchestrator takes `tier2Enabled: bool` and passes through the gate result; Tier-2 stays gated, Tier-3 doesn't. Lets rule suggestions appear on 1–2-diagnostic builds where the gate previously suppressed everything. |
| **Pre-emit ranker** | The Phase-2 deterministic policy table (`Ranker/PolicyTable.cs`) plus its `Ranker.cs` driver. Decides whether each diagnostic is shown at all, separately from "what hint should attach to it." Mode flags (`--strict` / `--final` / `--quiet`) change ranker thresholds, not suggester behavior. |
| **Symbol-binding contract (§3.1a)** | Every Tier-3 rule resolves its target types and members through `RuleSymbolResolver` (Roslyn `ISymbol` lookup against the live `Compilation`), never by string-matching `MemberAccess.Name.ValueText`. When Reactor renames an API, a string-matched rule silently breaks; a symbol-bound rule fails resolution explicitly, self-disables, and surfaces in `--list-rules`. CI gate: `RuleTargetResolutionTests`. |
| **ProjectReference resolution** | Phase-3 fix to `CompilationLoader` that walks `project.assets.json`'s `libraries.<id>` entries with `type=project` and locates the most-recently-built matching `.dll` under that project's `bin/` tree. Without it, Reactor (a project reference for every sample app) is invisible to `RuleSymbolResolver` and every rule self-disables — silently, since unit tests use synthetic compilations. |
| **Mining corpus** | The `(broken, fixed)` pairs produced by spec 037's harness. gpt-5.5 525-run mirror at `docs/specs/tasks/038-tuning-reports/2026-05-11-525run-source/` in this repo; sonnet-4.6 525-run lives in the sibling `reactor-tokenusage` repo. |
| **Cross-agent audit** | The verdict table at `docs/specs/tasks/038-tuning-reports/2026-05-11-cross-agent-audit.md` comparing cluster keys `(diag_code, receiver_type, fix_kind)` across the gpt-5.5 and claude-sonnet-4.6 corpora. Assigns STRONG / WEAK / SINGLE to each candidate cluster; this is how Validation Gate bar #2 gets formally cleared per rule. |
| **Eval Checkpoint (EC)** | A staged 5×N agent-eval batch run against `reactor-calc` and `reactor-kanban` to verify a phase's predicted cost/turn lift. EC1 + EC2 have landed; EC3 is pending on the eval branch; EC4 is the Phase-4 gate. |
| **Validation Gate** | The six-bar pre-merge checklist every Tier-3 rule must pass (Phase 3 only). Exists because a bad rule is worse than no rule. Bar #1 (frequency ≥ 5 %) is waived for Class-B rules whose justification is the documented prior-framework citation rather than a corpus cluster. |
| **Class A / Class B rule** | Class A = *induced* — sourced from a `patterns.json` cluster, justified by frequency + count + cross-agent reproducibility. Class B = *vocabulary-translation* — deliberately authored from a curated WPF/WinUI → Reactor table, justified by the structural prior-framework-confusion argument. Both classes share the same shipping infrastructure (`IRulePattern`, symbol-binding, `--disable-rule`); they differ only in justification source. |
| **Provenance** | A short string on every `IRulePattern` (`cluster:<id>` for Class A or `vocab:<framework>` for Class B) recorded with every fired suggestion. Lets a future maintainer grep `cluster:C0019` back to the motivating cluster, or `vocab:WinUI3` back to the doc-citation row in `docs/specs/tasks/038-vocab-table.csv`. |
| **Load-bearing** | The framing applied to `mur check` in §1: this is structural infrastructure for the 1–3 year window in which Reactor is experimental and WinUI 3 is weakly represented + cross-confused in training data. Not a stopgap. Phase 4 is scheduled, not optional. |
| **Sunset criterion** | The explicit conditions under which `mur check`'s suggestion engine retires: (a) Reactor API stable for ≥ 12 months AND (b) ≥ 90 % first-build-OK on a held-out Reactor eval across ≥ 2 vendor-distinct models without `mur check` assistance. Named so "load-bearing" doesn't drift into "forever." |

---

**Maintenance.** This doc covers the system through `main` at Phase 2 plus the in-flight Phase-3 work on `eval/spec-038-ec3-2026-05-11`. It will be updated as the rest of Phase 3 + Phase 4 land. The canonical source-of-truth for decisions and pending work remains the spec + task docs under `docs/specs/`. For ongoing operational responsibilities (API-churn protocol, corpus freshness, per-rule accept-rate monitoring, annual sunset-readiness check) see the **Maintenance (load-bearing operation)** section of the task doc.
