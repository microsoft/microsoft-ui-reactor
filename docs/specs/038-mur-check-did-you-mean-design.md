# `mur check` Did-You-Mean Engine — Design Spec

## Status

**Proposed** — 2026-05-09. No code yet.

This spec proposes extending the existing `mur check` command (a thin MSBuild wrapper at `src/Reactor.Cli/Check/CheckCommand.cs`) into a **diagnostic-aware coach** that augments C# compiler errors landing on Microsoft.UI.Reactor (Reactor) types with concrete *did-you-mean* suggestions. Today `mur check` parses MSBuild output into one-line diagnostics and looks up skill-file pointers for the 12 known `REACTOR_*` analyzer codes. This spec adds three further tiers — Roslyn semantic suggestions, induced pattern rules, and an optional learned ranker — driven by data the harness in [037 — Reactor Eval Trace Mining](./037-eval-trace-mining-design.md) produces.

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
- [§8 Warning ranking and noise reduction](#8-warning-ranking-and-noise-reduction)
- [§9 Output format](#9-output-format)
- [§10 Telemetry](#10-telemetry)
- [§11 Risks](#11-risks)
- [§12 Predicted impact](#12-predicted-impact)
- [§13 Implementation phases](#13-implementation-phases)
- [§14 Open questions](#14-open-questions)
- [§15 Pointers](#15-pointers)

---

## §1 Motivation

The Phase-7 eval results in #226 show Reactor at 1.00× / 1.11× WinUI XAML on tokens but still 3–4× HTML. The remaining gap is **structural overhead** — and the largest single bucket inside it is the **build/fix loop**, ~150 K tokens / 2–4 turns per kanban run. Every cycle, the agent runs `dotnet build`, dumps 1.5–3 K tokens of MSBuild output into context, reads it, infers what to change, and edits.

#226 §5 proposes replacing `dotnet build` with `mur check` as the default verification path; that alone shrinks each diagnostic dump from kilobytes to ~50–150 tokens. This spec goes further: instead of `mur check` only forwarding shorter diagnostics, have it run a Roslyn-backed pass that *answers the agent's likely next question*. When the diagnostic is `'ButtonElement' does not contain a definition for 'OnClick'`, the agent's next move is almost always to inspect the `Button` factory and discover the `Action onClick` parameter. We can save that inspection turn by emitting the answer in the diagnostic itself.

The catch: a *wrong* suggestion is worse than no suggestion. The agent will trust it and burn turns chasing a phantom fix. So the bar to emit a suggestion is high confidence, and the bar to ship a new pattern rule is empirical evidence (from spec 037's corpus) that real agents make the mistake the rule covers.

### Why this is load-bearing, not a stopgap

A natural framing of `mur check`'s did-you-mean engine is "token-saving optimization that base-model improvements will erode." That framing is wrong for the next 1–3 years, and the spec is sized accordingly.

Two structural conditions make this load-bearing rather than incremental:

1. **Reactor is experimental and churning.** The API surface will continue to change faster than any base model can be retrained against it. Models *will* keep guessing names that don't exist in the current Reactor, regardless of how good the underlying coding model gets.
2. **WinUI 3 is weakly represented in training data, and confused with adjacent frameworks.** WPF, Silverlight, WinUI 1, WinUI 2, and WinUI 3 all share enough vocabulary that models trained on the union produce *plausibly-WinUI-shaped* code that doesn't compile against Reactor's actual surface. The 525-run corpus directly evidences this: agents reach for `.VerticalAlignment`, `.Style(...)`, `Theme.AppBackground` — WinUI/WPF muscle-memory names. The Tier-2 fuzzy match can't bridge "VerticalAlignment → VAlign" by edit distance; only deterministic vocabulary-translation rules can (see §6).

The combination of those two — experimental API + structurally weak / cross-framework-confused training data — means the build-error-correction loop is the dominant feedback channel for the foreseeable future. It is the *primary mechanism* by which an agent reconciles its prior-framework priors with Reactor's actual surface, not an optimization on a mechanism that will go away.

**Operational consequence.** Phase 4 (the learned ranker) is scheduled work, not optional. The deterministic floor (Tiers 1–3 + the policy table) is enough for the experimental phase; the learned ranker is what we ship once the corpus delivers the volume to train it properly. The sunset criterion is named explicitly in §13.

**What sunset looks like.** Decommission `mur check`'s suggestion engine when both hold: (a) Reactor's public API surface has been stable for ≥ 12 months (no breaking changes in a release), and (b) base-model accuracy on a held-out Reactor-touching eval set exceeds 90 % first-build-OK without `mur check` assistance. Until both hold, this is infrastructure.

## §2 Goals and non-goals

### Goals

- For every C# compiler error (CS-prefixed) whose receiver, type, or member references a `Microsoft.UI.Reactor.*` symbol, emit a single-line suggestion with confidence ≥ T.
- Stay silent below T. Silent is correct. Wrong is not.
- Compose with the existing `REACTOR_*` analyzer hint table, not replace it.
- **Suppress noise.** Most MSBuild and analyzer output an agent sees mid-iteration is boilerplate or low-priority hints that distract from the load-bearing fix. Surface only the diagnostics whose resolution is on the critical path to a clean build; defer the rest to a final-pass mode (§8).
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
    │
    ▼ Pre-emit ranker (§8) — transversal across all tiers:
    │   score every diagnostic against an emission policy.
    │   - errors:     always emit
    │   - warnings:   emit only if score >= mode threshold
    │   - hints/info: suppress in iteration mode; emit in --final mode
    │   default mode is "iteration" — aggressive suppression so the
    │   agent sees only diagnostics on the critical path to a clean build.
    │
    ▼ format and write one line per surviving diagnostic
```

The tier structure is deliberate: **most of the value is deterministic.** ML enters only as a tiebreaker, only after measured data shows where Tier 2 + 3 fall short. The four tiers decide *what suggestion to attach*; the ranker (§8) is orthogonal — it decides *whether to emit the diagnostic at all*.

## §4 Tier 1 — analyzer-ID hint table

**Status: shipped.** Today `CheckCommand.HintFor(code)` returns a static skill-file pointer for 12 `REACTOR_*` analyzer codes (see §15). This tier is unchanged by this spec. New analyzer codes added by future analyzers slot into the same table.

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

## §6 Tier 3 — induced and authored pattern rules

Tier 3 catches mistakes Tier 2 can't reach because they cross the AST-shape boundary, not just the symbol-name boundary. Examples:

- `.OnClick(x)` chained on a `Button(...)` call. Tier 2 sees CS1061 on `OnClick`; the *fix* moves the lambda into the parent factory's argument list as `onClick: x`. That's a tree rewrite, not a rename.
- `.Style(new { Color = "red" })`. Tier 2 sees CS1061; the fix is a `.With(Modifiers.Foreground(Theme.Critical))` chain — entirely different shape.
- React-attr-style `className=`, `key=`, `ref=`. Tier 2 sees a syntax error on the `=`; the fix is to use the relevant Reactor modifier or the `WithKey` extension.

Rules come in **two classes** with different justification bars but the same shipping bar (the Validation Gate in the task doc):

### Class A — *induced* rules

Sourced from spec 037's mining clusters. Each cluster has `cluster_id`, `diag_code`, `fix_kind`, frequency, and exemplar pairs. A human reviewer walks `patterns.json` top-down by frequency and authors rules against the high-frequency clusters where Tier 2 produces wrong-direction suggestions or stays silent. **Justification bar:** cluster `frequency ≥ 0.05` AND `count ≥ 10` AND cross-agent reproducibility (see Validation Gate bar #2).

This is the data-driven path; it answers "what mistakes are real agents making in practice?"

### Class B — *vocabulary-translation* rules

The 525-run corpus and the §1 load-bearing argument both point at the same structural failure: agents trained on WPF / Silverlight / WinUI 1 / WinUI 2 / WinUI 3 have strong priors that surface as confused Reactor code. `.VerticalAlignment(...)` (WinUI/WPF) → `.VAlign(...)` (Reactor). `Theme.AppBackground` (plausibly-WinUI) → `Theme.SolidBackground` (Reactor). `.Style(BuiltInStyle)` → fluent-modifier family.

These are deliberately authored from a curated vocabulary-translation table (input source: WPF / WinUI public docs + Reactor's own api index), not waited-for-in-the-mining-cluster. **Justification bar:** the source vocabulary is documented (cite the public docs the prior-framework name comes from) and the target Reactor symbol exists in the live `Compilation`. The frequency bar from Class A is **waived** — we know structurally that prior-framework muscle memory will surface; we don't need 5 % cluster evidence to act on it. Cross-agent reproducibility (bar #2), positive/negative fixtures (#3/#4), and reviewer signoff (#5) still apply.

The seam: when the corpus has both volume *and* points at the same vocabulary-translation rule, it counts as Class A and the corpus serves as evidence. When the cluster is small but the prior-framework citation is strong, it counts as Class B. The two classes coexist; one rule belongs to one class.

### Rule infrastructure (both classes)

Each rule is a small class implementing `IRulePattern`:

```csharp
interface IRulePattern
{
    string Name { get; }          // for telemetry / --disable-rule
    string Provenance { get; }    // "cluster:C0019" (Class A) or "vocab:WinUI" (Class B)
    bool TryMatch(in RuleContext ctx, out RuleSuggestion suggestion);
}
```

Rules live in `src/Reactor.Cli/Check/Rules/<Name>Rule.cs`, one rule per file, each with a unit test against ≥ 3 positive fixtures and ≥ 2 negative counter-examples. Each shipped rule carries a per-rule confidence (default 0.85) and a kill-switch (`mur check --disable-rule <Name>`).

### Symbol-binding (decided): rules match by Roslyn `ISymbol`, not string

For a load-bearing system with 1–3 years of expected Reactor API churn, the implementation choice is to **bind rule targets to Roslyn `INamedTypeSymbol` / `IMethodSymbol` references resolved against the live `Compilation`, not to hardcoded type/method name strings.**

Concretely, a Class-B rule that maps `.VerticalAlignment(...)` → `.VAlign(...)` on `*Element` types does not match by checking `MemberAccessExpressionSyntax.Name.ValueText == "VerticalAlignment"`. It matches by resolving the would-be member's containing type to `Microsoft.UI.Reactor.Core.TextBlockElement` (or its base hierarchy) and checking that the type *does* have a `VAlign` method and *does not* have a `VerticalAlignment` method. Both probes go through `SemanticModel` and the `FactoryIndex`, not string compare.

**Why:** when Reactor renames `VAlign` to (say) `VerticalAlign` in a future release, a string-matched rule silently breaks — it still fires on the old WinUI name but proposes a no-longer-existing target. A symbol-bound rule fails resolution and either (a) self-disables with a warning, or (b) trips the CI gate (see §11). The cost of retrofitting later is rewriting every shipped rule; the cost up-front is one extra resolution call per rule per diagnostic, which is a non-issue at our call volume.

**CI gate.** A test that builds the rule registry against the live Reactor `Compilation` and asserts every rule's target symbol resolves. Any rule whose target evaporates fails the build until the rule is updated or retired. Land alongside the first Class-B rule.

**Why human review and not auto-induction?** Rules are public-facing suggestions to the agent; a bad rule contaminates every downstream session. The cost of one bad rule landing is high; the cost of one cluster waiting another sprint for review is low. The asymmetry justifies the review step.

## §7 Tier 4 — confidence ranker

**Status: future.** Build only if telemetry (§10) shows Tier 2 + 3 leave a meaningful long tail.

If built: a gradient-boosted tree over hand-engineered features — Levenshtein distance, parameter-name overlap, factory-popularity-in-samples (counted from `samples/apps/`), AST-shape similarity, prior agent-accept rate per rule. Inputs: the candidate set produced by Tiers 2 + 3. Output: re-ranked list with a calibrated confidence head.

Inference cost is negligible (microseconds). Training cost is one offline pipeline against `fixes.jsonl`.

We deliberately do **not** propose a small LLM here. Small models hallucinate without huge corpora, and Reactor's training set is fundamentally limited; the deterministic system already has access to the things a small model would have to memorize (the api index, the samples).

## §8 Warning ranking and noise reduction

The four suggestion tiers answer *what hint to attach to a diagnostic*. The pre-emit ranker answers a separate question: *should this diagnostic be sent to the agent at all, right now?* This matters because raw MSBuild output is dominated by diagnostics whose resolution is **not** on the critical path to a clean build — and every one of them costs context, distracts the agent, and reduces the chance of a 1-step fix.

### Why ranking matters

A representative kanban-run `dotnet build` emits ~30–80 diagnostic lines. Of those, typically 1–3 are the actual blockers. The rest are some mix of:

- **NuGet restore noise** — `NU1701`, `NU1605`, package-version downgrades, target-framework fallbacks.
- **MSBuild reference-resolution chatter** — `MSB3245`, `MSB3277`, `MSB3270` lines that resolve at the next build with no source change.
- **IDE style hints** — `IDE0xxx` series: prefer expression-bodied member, unused using, etc.
- **CS warnings on auto-generated or template code** — `CS8602` nullable, `CS0168` unused variable, `CS1591` missing XML doc — almost never the right thing to fix mid-iteration.
- **Reactor analyzer Info-severity warnings** — e.g. `REACTOR_HOOKS_006` (non-idempotent fetcher heuristic) that are *guidance*, not blockers.
- **Boilerplate** — `Build succeeded with N Warning(s)`, target hits, paths, timing.

If the agent reads all of these every turn, two pathologies follow: (a) it spends turns "fixing" warnings that didn't need fixing this turn, and (b) the *real* blocker scrolls off attention. The ranker exists to flatten this: in iteration mode, only the critical path; in final mode, everything.

### Modes

| Mode | Flag | Behavior |
|---|---|---|
| **iteration** *(default)* | `mur check` | Emit errors always; emit warnings only if rank ≥ iteration threshold; suppress info / style hints. |
| **strict** | `mur check --strict` | Treat warnings as errors. Useful for one-shot CI gates; not recommended for the inner loop. |
| **final** | `mur check --final` | Emit every diagnostic, no suppression. Run this once before declaring done. |
| **quiet** | `mur check --quiet` | Errors only. Maximally aggressive suppression for sub-iteration loops. |

The agent-eval prompt for Reactor (#226 §5) directs the agent to use `mur check` (iteration) during the build/fix loop and `mur check --final` once iteration mode is clean. The transition is the explicit "I am done iterating" signal.

### CLI shape and MSBuild passthrough

```
mur check [<path>] [mur-flags...] [-- <msbuild args>...]
```

Today's `CheckCommand` accepts only `<path>` and hardcodes `--nologo`, `-v:m`, and `-p:Platform={host arch}` against `dotnet build`. That's the right default but a fragile contract — agents and humans both routinely need to override `Platform`, pick a `Configuration`, skip restore, change verbosity, or pass arbitrary `-p:` properties. Without an escape hatch the only fallback is "drop `mur check` and run `dotnet build` directly," which discards every benefit this spec adds.

The fix is the standard double-dash convention: everything after a literal `--` is forwarded verbatim to `dotnet build`. `mur`-owned flags appear before `--`; MSBuild-owned flags appear after.

```
# default: ranker on, iteration mode, host-arch platform
mur check

# build x64 even on an ARM64 host
mur check -- -p:Platform=x64

# release config + skip restore + final-pass mode
mur check --final -- -c Release --no-restore

# escalate verbosity for debugging the wrapper
mur check -- -v:n

# explicit TFM
mur check -- -f net10.0-windows10.0.22621.0

# multiple properties
mur check -- -p:Platform=x64 -p:DefineConstants=FOO

# all of the above with a non-default path
mur check ./MyApp -- -c Release -p:Platform=x64
```

**Default-merging rules.** `mur` always passes `--nologo` (output stability is non-negotiable for diagnostic parsing). For `-v:` and `-p:Platform=`, `mur` injects its defaults *only if* the user did not supply the same flag in the passthrough section. Detection is by flag name, not value — `-p:Platform=x64` in passthrough wins over the auto-injected host arch; `-v:n` in passthrough wins over the wrapper's `-v:m`.

**Boundary semantics.** A bare `--` is the unambiguous separator. Tokens before it are parsed against `mur`'s own grammar; tokens after are not. Emit a clear error if the user passes an unknown `mur-flag` before `--` rather than silently forwarding (helps catch typos like `mur check --quie -- -c Release`).

**Ranker is unchanged.** Passthrough alters how `dotnet build` runs, not how its diagnostics are scored. The ranker (§8) and the suggesters (Tiers 1–4) operate on the parsed diagnostic stream regardless of which build invocation produced it. The one exception: `--strict` (which the ranker promotes warnings to errors) composes with `-p:TreatWarningsAsErrors=true` from passthrough — both apply, and the more aggressive of the two wins on each diagnostic.

**Tracing.** When `mur check --trace` is on, the trace records the *full* effective command line passed to `dotnet build`, including default-merged flags, so replays are bit-faithful.

### Ranking policy

Each diagnostic gets a score from 0.0 (suppress) to 1.0 (always emit), computed as:

```
score = base_policy(code) * code_weight
      + severity_weight(severity)            // E=1.0, W=0.5, I=0.1
      + location_weight(file, span)          // user-edited > generated
      + recency_weight(turns_since_touched)  // freshly written > stale
      + accept_history(code, rule_name)      // telemetry-driven, optional
```

`base_policy(code)` is a hand-authored table (§8 of this spec, owned by the Reactor team) covering the ~30 highest-frequency diagnostic codes. Sketch:

| Code prefix / id | Iteration score | Final score | Notes |
|---|---:|---:|---|
| Any CS error | 1.0 | 1.0 | Always emit. |
| `REACTOR_*` Warning | 0.9 | 1.0 | Hooks/A11y/Theme are correctness-adjacent. |
| `REACTOR_*` Info | 0.2 | 1.0 | Heuristic; suppress mid-iteration. |
| `CS8600`–`CS8625` (nullable) | 0.3 | 1.0 | Mostly noise unless the agent is fixing a null-deref. |
| `CS0168` (unused var) | 0.0 | 0.7 | Never blocks a build. |
| `CS1591` (XML doc) | 0.0 | 0.5 | Cosmetic. |
| `IDE0xxx` | 0.0 | 0.3 | Style. |
| `NU1701`, `NU1605` | 0.0 | 0.6 | Transient — usually next build. |
| `MSB3245`, `MSB3270`, `MSB3277` | 0.0 | 0.4 | Resolution flakiness. |
| Unknown | 0.5 | 1.0 | Conservative; surface unknown codes by default in iteration too — better to over-emit a novel code than to hide a real bug behind silence. |

Iteration threshold: **≥ 0.6**. Final threshold: **≥ 0.0** (everything). Tunable via `mur check --emit-threshold`.

### Learned ranker (Phase D)

The deterministic table is the v1 baseline. Once spec 037's corpus is producing pairs at scale, train a small classifier:

- **Label:** for each diagnostic that fired in a trace, did the agent's eventual fix touch the line/symbol/file the diagnostic pointed at? Yes → emit-worthy; no → noise.
- **Features:** diagnostic code, severity, file path category (user / generated / nuget cache), turn index, whether prior `mur check` already surfaced this diagnostic and the agent ignored it, whether other higher-priority diagnostics are in the same emission, file churn rate.
- **Model:** GBDT or logistic regression. Tiny (<100 KB ONNX). Microseconds to score per diagnostic.
- **Calibration:** isotonic regression on a held-out fold so the score behaves like a probability of "this diagnostic is emit-worthy."

The learned ranker complements rather than replaces the policy table — the table is the floor (always-emit / never-emit anchors), the model fills the gray middle.

### Why not just trust MSBuild's severity?

MSBuild severity is what the *language* / *analyzer* author chose, not what the *agent's-build-loop* needs. `CS1591` is severity Warning by spec; in the inner loop it is suppress-grade noise. `REACTOR_HOOKS_006` is severity Info; in the inner loop it is borderline because a hook misuse can cause runtime corruption. The ranker overlays a *task-shaped* severity on top of the language-shaped one.

### Failure modes the ranker must not introduce

- **Hiding a load-bearing warning the agent needed to see.** Mitigation: telemetry tracks suppressed diagnostics that later became errors in subsequent builds; auto-promote any code that crosses the suppression-then-error threshold > N times.
- **Suppressing in iteration but never emitting in final.** Mitigation: the harness asserts `mur check --final` is run on the success build of every spec 037 trace; any diagnostic that the user-mode lint would have flagged but `--final` didn't is a CI failure.
- **Confusing the agent with mode-dependent output.** Mitigation: include the mode in every emission's metadata (`// mode: iteration`) so the agent can reason about why a diagnostic does or doesn't appear.

## §9 Output format

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

## §10 Telemetry

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

## §11 Risks

| Risk | Mitigation |
|---|---|
| Wrong suggestions corrupt the agent's reasoning | Per-rule confidence threshold; emit only above T. Telemetry on agent-accept rate per rule, auto-suppress rules whose accept rate falls below 50 %. |
| Roslyn `Compilation` load is slow on cold runs | Single load per `mur check` invocation, ~200–500 ms one-time vs. the multi-second `dotnet build` we already pay for. Cache by file-set hash for incremental loops. |
| `reactor.api.txt` and the live `Reactor.dll` drift apart | Tier 2 reads from the live `Compilation`, not the api index. The api index is a pre-filter only. |
| Random-app corpus over-represents simple controls | Stratify the prompt grid in spec 037; weight rules by both frequency *and* per-cluster turn-cost (rare patterns that cost 5 turns matter more than common ones that cost 1). |
| Pattern rules become a maintenance treadmill | Auto-generate the `samples/`-derived validation set; CI fails when a Tier 3 rule change regresses a captured pair. |
| Tier 2 fuzzy-match emits an embarrassingly wrong rename | Whitelist threshold T. Per-code thresholds, not one global T. |
| The corpus encodes a model's idiosyncrasies | Spec 037 supports multi-agent rotation. Tier 3 rules ship only when a cluster reproduces across ≥ 2 agents. |
| Ranker hides a load-bearing warning the agent needed to see | Telemetry on suppress→error transitions; auto-promote codes whose suppression precedes a related error > N times. `mur check --final` is mandatory before "done" — captured in eval prompt and CI. |
| Over-suppression makes the build/fix loop *worse* by hiding novel diagnostics | Default base score for unknown codes is 0.5 (above iteration threshold) — better to over-emit a novel code than to silence a real bug. Threshold tunable per agent via telemetry. |
| `mur check` per-invocation overhead (~5–8s) regresses tokens on small projects with little API surface to explore (validated empirically at EC1: ~150 LoC calc app regressed +21% cost mean even with all prompt loopholes closed; kanban won −24% in the same batch). Suggestion mechanism's amortization floor is project-size-dependent. | **Mitigated 2026-05-10:** diagnostic-count gate in `CheckCommand.ShouldEmitSuggestions`. Default skips Tier-2 when an invocation surfaces < 3 unique CS-prefixed diagnostics; `--suggest-threshold <N>` overrides (0 disables). Default N validated at Data Checkpoint C (28.7% emit rate matches calc-vs-kanban shape). **EC1 re-run with gate confirms the mitigation:** calc cost −4% (was +21%), kanban cost −33% (was −24% — preserved and grew); both pass the "tokens not regressed" bar. Residual: kanban CV widened (54% vs prior 24%) — one outlier run hit 0 firings; gate is path-dependent on agent's exploration order. Worth watching in Phase 2. See §14 #8 + spec 038 task doc "EC1 re-run" section. |
| **Reactor API churn invalidates Tier-3 rules or the mined corpus.** A load-bearing system with 1–3 years of expected lifespan needs to survive Reactor renaming `VAlign` to `VerticalAlign`, retiring a factory, or moving a member up/down the type hierarchy. String-matched rules silently break (still fire on the old name, propose a target that no longer exists). Mined corpus rows referencing retired APIs become misleading training signal. | Two mitigations, both decided in §6: (1) Rules bind to Roslyn `ISymbol` references resolved against the live `Compilation`, not name strings. A rule whose target symbol evaporates fails resolution and self-disables with a warning. (2) A CI gate (lands with the first Class-B rule) compiles the rule registry against Reactor's live surface and fails the build on any unresolved rule target. For the corpus: when an API renames, mark affected `fixes.jsonl` rows as historical (don't delete — they remain valid training signal for the *kind* of mistake) and re-run the tuning harness against the trimmed live-API subset before promoting any threshold changes. |
| **Data pipeline (spec 037) lacks an SLA or named owner.** Today's task doc lists "harness team is on a separate cadence" as a non-blocking risk because Phase 1 ships with hand-authored fixtures. For load-bearing operation that stance doesn't hold: Phases 3 and 4 are *fully dependent* on corpus refreshes that track API churn, agent rotation, and Reactor-version-pinned eval pairs. A stale corpus produces stale rules. | Before opening Phase 3 rule PRs: name an owner for the corpus pipeline (single point of contact, not a team-shaped responsibility), document a refresh cadence pegged to Reactor minor-version releases (each minor cuts a new corpus), and add a corpus-freshness check to the rule-shipping path (no rule merges against a corpus older than the latest Reactor release). Track as a Phase-3 prerequisite, not a Phase-4-only concern. |

## §12 Predicted impact

If the per-kanban-run breakdown in #226 (build + fix cycles ≈ 150 K tokens, 2–4 turns) is right, and Tier 2 + 3 remove ~70 % of those turns:

- Tokens: ~−100 K per kanban run (~14 % of total).
- Turns: ~−2 (16.8 → ~14.8).
- Cost: ~−$0.70 per run.

Stacking with #226 §1 (richer template) + §2 (inline cheatsheet): kanban tokens 738 K → ~380 K, putting Reactor decisively under WinUI on cost and within ~2× HTML — at the realistic ceiling identified in #226.

These numbers are estimates; the empirical question is settled by Phase 0 instrumentation (§13).

## §13 Implementation phases

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

### Phase 4 — pre-emit ranker, deterministic table

- Land §8's hand-authored `base_policy(code)` table covering the top ~30 highest-frequency diagnostic codes from Phase 0's sweep.
- Add `--strict`, `--final`, `--quiet`, `--emit-threshold` flags to `mur check`.
- **Land `--` MSBuild passthrough** per §8. Implementation: split `args` on the first bare `--`, validate the left half against `mur`'s flag grammar (error on unknowns), then default-merge the right half with `--nologo` / `-v:m` / `-p:Platform={host arch}` only where the user didn't already specify the same flag. Round-trip the effective command line into `--trace` output so replays are reproducible.
- Update the eval prompt and the `reactor-build-and-check` skill to direct agents to use iteration mode in the inner loop and `--final` once iteration is clean.
- Add the suppress→error CI guardrail: every `mur check --final` run on a successful build must surface no diagnostic that, by code alone, the table would have flagged in iteration mode but didn't.

**Exit:** the agent sees ≥ 50 % fewer diagnostic lines per turn in iteration mode without missing any blockers (measured against a 50×N replay of Phase 0 traces).

### Phase 5 — telemetry-driven Tier 4 + learned ranker (scheduled, deferred)

**Status change vs. earlier draft:** this was originally framed as "only if needed." The §1 load-bearing argument promotes it to **scheduled, deferred until Data Checkpoint D delivers ≥ 1K negative-class ranker rows.** It is not a maybe; it is the work we open when the data is ready. The deterministic floor (Tiers 1–3 + the Phase-4 policy table) is enough for the experimental phase; the learned ranker is what we ship once the corpus delivers the volume to train it properly.

- Log `(diagnostic, candidates, picked, accepted-by-agent)` from production `mur check` invocations.
- Train a small GBDT confidence ranker over hand-engineered features as the Tier-4 re-ranker on Tier 2 + 3 candidates.
- In parallel, train the §8 learned ranker against spec 037's pair corpus using the "did the agent's eventual fix touch this diagnostic's location?" label. Calibrate to behave as an emit-worthiness probability; combine with the policy-table floor.

**Exit:** ≥ 5-point lift in the precision of "diagnostics emitted in iteration mode that the agent's next fix actually touched," vs. the deterministic table baseline, OR a documented decision to ship Phase 4 with the deterministic table only (escape hatch, not the planned outcome).

### Sunset criterion (explicit)

The `mur check` suggestion engine has a finite expected lifespan. Decommission when **both** hold simultaneously:

1. **Reactor API stability.** No breaking changes in the public API surface for ≥ 12 consecutive months across at least two minor releases.
2. **Base-model accuracy.** A held-out Reactor-touching agent-eval set achieves ≥ 90 % first-build-OK without `mur check` suggestion assistance (Tier 1 hints alone allowed, since those are docs pointers rather than fix text), on at least two distinct models from different vendors.

Until both hold, this is infrastructure and the investment level matches. When both hold, file a sunset issue, freeze rule additions, and plan a graceful removal. (The trace/telemetry/`--final` mode behavior remains useful even after the suggestion engine retires; only Tiers 2–4 sunset.)

Naming this criterion explicitly prevents the alternative failure mode of "load-bearing forever," which would be the wrong commitment given how fast base-model coding ability is advancing.

## §14 Open questions

1. **Trace format for the random-app generator (spec 037).** Does its trace produce a transcript rich enough to extract before/after source pairs without re-running the build? See spec 037 §7. Coordinate before either lands.
2. **Confidence threshold T.** Start strict (≥ 0.85 JaroWinkler-equivalent) and loosen with telemetry, or start loose and tighten? Defaulting to strict preserves "silent is OK" — propose strict.
3. **Should Tier 2 rewrite the surface form?** Today suggestions are text-only ("try: `Button(label, onClick: x)`"). A future variant could emit a unified-diff hunk; that is a separate scope.
4. **Roslyn workspaces vs. compilations.** Workspaces give project-graph reasoning but cost more to load. Start with `Compilation` only; revisit if rules need cross-project context.
5. **Per-agent rule profiles?** If different LLM agents make different mistakes, Tier 3 rule sets could be agent-keyed. Likely premature; cross-agent rules are simpler. Reconsider once telemetry shows agent-specific deltas.
6. **Iteration-mode emit threshold (§8).** Default proposed at 0.6. Tuning lever: too high silences load-bearing warnings; too low restores the noise. Land conservative (0.5–0.55) and tighten as the policy table covers more codes? Or go aggressive (0.7) and accept that novel codes get surfaced via the unknown-code default? Settle empirically once Phase 0 produces the diagnostic-frequency distribution.
7. **Should the ranker score Tier 1 hints as well?** Today every `REACTOR_*` analyzer warning carries a static skill pointer and is implicitly emit-worthy. The ranker could in principle suppress low-priority `REACTOR_*` Info diagnostics in iteration mode (e.g. `REACTOR_HOOKS_006`, the non-idempotent fetcher heuristic). Recommendation: yes, treat Tier 1 emissions as just another diagnostic for ranking purposes; the policy table is the single source of truth for emit/suppress decisions.
8. **Rule-symbol-binding strategy (resolved 2026-05-11).** Should Tier-3 rules match their targets by name string (`MemberAccess.Name.ValueText == "VerticalAlignment"`) or by Roslyn `ISymbol` references resolved against the live `Compilation`? **Resolved: symbol-binding.** For a load-bearing system with 1–3 years of expected Reactor API churn, string-matched rules silently break when Reactor renames a target (still fire on the old name, propose a no-longer-existing replacement). Symbol-bound rules fail resolution explicitly and either self-disable with a warning or trip the CI gate (see §6 "Symbol-binding" and §11 "Reactor API churn" risk row). Cost up-front: one extra resolution call per rule per diagnostic — non-issue at our call volume. Cost of retrofitting later: rewriting every shipped rule. Lands with the first Class-B rule in Phase 3.

9. **Source of the Class-B vocabulary-translation table.** §6 says rules in Class B cite the public docs the prior-framework name comes from. Open: do we maintain a single in-repo `WinUI-to-Reactor.csv` (or similar) as the curated table feeding all Class-B rules, or does each Class-B rule embed its own one-row citation? Recommendation: single in-repo table, owned by the Reactor team, reviewed independently of any rule PR. The table itself is the artifact under "ship 5–8 vocab translations / sprint" cadence; individual rules wire to rows of it. Land as part of the first Class-B rule PR.

10. **Project-size gate for the suggestion mechanism.** EC1 5×N (see spec 038 task doc) showed `mur check`'s per-invocation overhead regresses small projects (calc, ~150 LoC, +21% cost) even when the prompt closes every explore-around-the-suggestion side door, while medium projects (kanban) win cleanly (−24% cost, −33% median, 3.4× lower variance). The amortization floor is real and structural. Open design question: gate `SymbolSuggester` activation by (a) source-file count / total LoC, (b) per-invocation CS-diagnostic count, (c) `--mode iteration|small` flag the agent sets explicitly, or (d) leave gating to the agent prompt ("don't bother with `mur check` on apps under 200 LoC")? Approach (b) feels strongest — it self-tunes (a project with one error doesn't pay for fuzzy-match overhead) and composes with Phase 2's iteration/`--final` distinction. **Resolved 2026-05-10 with approach (b); validated 2026-05-11.** Implemented in `CheckCommand.ShouldEmitSuggestions` with default threshold N=3 unique CS-prefixed diagnostics per invocation; overridable via `--suggest-threshold <N>` (0 disables). Default cross-validated against the 525-run corpus (Data Checkpoint C, distribution: 71.3% of builds < 3, 28.7% ≥ 3). EC1 re-run with the gate (2026-05-11): calc cost −4% (was +21%), kanban cost −33% (was −24%); both pass. New residual question: kanban CV widened on the re-run because one of five runs hit 0 firings and tracked the long-tail base path — gate behavior is path-dependent on the agent's exploration order, not just the project's static shape. Telemetry at scale (post-Phase-4) will tell us whether this stays a 1-in-5 tail or grows.

## §15 Pointers

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
