# In-Build "Did You Mean" — Analyzer-Driven Suggestions

## Status

**Implemented (in part) — 2026-07-07.** Phase 1 (`REACTOR_DYM_001`) and Phase 2 (`REACTOR_DYM_002`)
have shipped; the fuzzy name-resolution phases remain design-only. This spec is the design of record
for bringing Reactor's "did you mean" suggestions — today reachable only through the `mur check` CLI —
into a plain `dotnet build` and the IDE, for consumers of the `Microsoft.UI.Reactor` NuGet package.

> **Process note.** The mechanism decision below was reached by an investigation that mined the
> real eval corpus, ran empirical Roslyn spikes, and put the question to a panel of models
> (GPT‑5.5, Gemini 3.1 Pro, GPT‑5.3‑Codex) twice — once as a design review of the original
> output-driven proposal, once as an A/B/C judgment on the evidence. All three independently
> recommended the analyzer path (Option A) at high confidence. The full investigation — a decision
> memo, coverage matrix, spike findings, and cross-model review synthesis — was captured as
> design-session working notes and is not checked into the repository.

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 The mechanism decision](#2-the-mechanism-decision)
- [§3 Evidence](#3-evidence)
- [§4 Design](#4-design)
- [§5 Phase 1 — `REACTOR_DYM_001` (shipped)](#5-phase-1--reactor_dym_001-shipped)
- [§6 Phase 2 — `REACTOR_DYM_002` (shipped)](#6-phase-2--reactor_dym_002-shipped)
- [§7 Later phases — the fuzzy cases](#7-later-phases--the-fuzzy-cases)
- [§8 Risks and guardrails](#8-risks-and-guardrails)
- [§9 Relationship to `mur check`](#9-relationship-to-mur-check)
- [§10 Non-goals / superseded](#10-non-goals--superseded)

---

## §1 Motivation

`mur check` (spec 038) augments C# compiler errors on Reactor types with one-line `→ try:`
did-you-mean suggestions. It is **output-driven**: it runs `dotnet build`, parses the compiler's
diagnostic output, and fuzzy-matches (Jaro-Winkler) against factory names / members / static
members / vocabulary tables. It is powerful but reachable only through the separate `mur` global
tool, so a developer who adds `Microsoft.UI.Reactor` as a `<PackageReference>` and runs a plain
`dotnet build` gets none of it.

Goal: deliver the same class of guidance automatically during a normal build and in the IDE.

## §2 The mechanism decision

Two mechanisms were considered:

- **A — Analyzers.** A semantic-model `DiagnosticAnalyzer` surfaces did-you-mean during
  `dotnet build` **and** the VS Error List; `CodeFixProvider`s registered on the compiler's CS
  diagnostic IDs offer one-click fixes (the Roslynator pattern). `mur check` remains the
  output-driven CLI for agents and the long tail.
- **B — Output-driven in-build.** Ship a bundled tool + MSBuild targets that set Roslyn's SARIF
  error log and post-process it, running the exact existing engine.

**Decision: A.** A NuGet package **cannot** read and augment the compiler's own CS diagnostics
in-process (confirmed: `DiagnosticSuppressor` can only *suppress* non-error diagnostics; source
generators can't see sibling diagnostics; a package can't auto-register an MSBuild logger;
BuildCheck can't see compiler diagnostics). The only automatic, package-shippable way to consume
real CS output is post-build SARIF — the mechanism a three-model design review flagged as fragile
(blast radius from `RunPostBuildEvent=Always` onto other packages' post-build hooks, stale-SARIF
"ghost suggestions" on incremental builds, no Error List integration, default-on subprocess
concerns). Option A avoids all of that, integrates natively, and matches universal industry
practice (Roslynator, Meziantou, xUnit, NetAnalyzers, StyleCop are all semantic-model analyzers +
IDE code-fixes; none parse build output).

## §3 Evidence

From the 2026-05-11 525-run corpus, the did-you-mean mistakes rank:

| Rank | Mistake | CS code | ~events |
|---|---|---|---|
| 1 | `GridSize.Auto()` parens on a static property | **CS1955** | ~175 |
| 2 | mistyped factory name | CS0103 | 63 |
| 3 | `Theme.*` wrong suffix / raw key | CS0117 | 27 |
| 4 | `.VerticalAlignment` / `.HorizontalAlignment` | CS1061 | ~22 |
| 5 | `GridSize.Pixel/Fixed` → `Px` | CS0117 | 7 |
| 6 | `Button.OnClick` → `onClick:`; `TextBlock.Style` → modifiers | CS1061/0117 | ~10 |
| — | argument type/count mismatch | CS1503/CS7036 | rare (no corpus data) |

An empirical Roslyn spike confirmed every high-frequency case is cleanly analyzer-detectable —
including the #1 (CS1955): the invocation's symbol degrades in the error state, but the **receiver
type still resolves**, so the member kind is re-derived from `type.GetMembers(name)`
(property/field vs. method). None of the existing Reactor analyzers cover the did-you-mean
direction, so this is additive, not duplicative.

## §4 Design

- A family of `REACTOR_DYM_*` **`DiagnosticAnalyzer`s** in `src/Reactor.Analyzers/`, shipped via
  the existing `analyzers/dotnet/cs` packaging (no new package, no targets, no subprocess).
- Paired **`CodeFixProvider`s** for one-click IDE fixes.
- Each analyzer **binds to Reactor symbols** (gated to the `Microsoft.UI.Reactor` namespace) so it
  never fires on unrelated consumer code — the same symbol-binding principle as the Tier-3
  `mur check` rules (spec 038 §6).
- **Shared match engine (later phases).** The fuzzy cases reuse one shared, Roslyn-only match
  library (factory index + Jaro-Winkler + vocabulary set) consumed by **both** the analyzer and
  `mur check`, so the two never diverge — the unanimous cross-model recommendation.

## §5 Phase 1 — `REACTOR_DYM_001` (shipped)

`NonInvocableMemberParensAnalyzer` + `NonInvocableMemberParensCodeFix` cover the **#1** corpus
mistake: a Reactor property/field invoked like a method (e.g. `GridSize.Auto()`). It is purely
**structural** — no fuzzy matching, no shared engine — which makes it the ideal, self-contained
first increment.

- **Detection.** On an `InvocationExpression` with zero arguments whose receiver is a member
  access: report only when (a) the invocation did not bind (`GetSymbolInfo(...).Symbol == null`),
  (b) the receiver type resolves and is under `Microsoft.UI.Reactor`, and (c) the named member is
  a property/field with no method overload. Message: *"'GridSize.Auto' is a property, not a
  method — remove the parentheses."*
- **Severity: Warning.** The analyzer fires **only** when the invocation failed to bind, so it
  always co-occurs with a compiler error (CS1955). That means Warning is safe under
  `TreatWarningsAsErrors` (the build already fails) while still surfacing in a plain
  `dotnet build` — meeting the "nicer errors in a plain build" goal.
- **Fix.** `GridSize.Auto()` → `GridSize.Auto` (drop the parens), batch-fixable.
- **Tests.** Positive (`GridSize.Auto()`), negatives (`Star()`/`Px(1)`/`Auto`, and a non-Reactor
  `Widget.Thing()` to prove namespace gating), and the fix round-trip.

## §6 Phase 2 — `REACTOR_DYM_002` (shipped)

Phase 2 ships the **deterministic vocabulary** did-you-mean case that is still live against the
current Reactor surface, as a dedicated non-fuzzy analyzer + one-click code fix mirroring an existing
`mur check` Tier-3 rule (spec 038 §6). The fuzzy Jaro-Winkler typo matcher is deliberately **deferred**
to a later phase — it carries a higher false-positive risk and needs the shared match engine (below).

- **`REACTOR_DYM_002` — `ThemeBackgroundSuffixAnalyzer`** (+ `ThemeBackgroundSuffixCodeFix`). Corpus
  mistake **#3** (`Theme.*` wrong suffix, CS0117, ~27 events): an English-plausible token such as
  `Theme.AppBackground` that Reactor doesn't define. Any un-overridden `*Background` maps to
  `Theme.SolidBackground`; the exact override `Theme.LayerBackground` → `Theme.LayerFill` matches the
  CLI rule's run5 cross-trial refinement. Mirrors `ThemeBackgroundSuffixRule`; the target
  `Microsoft.UI.Reactor.Core.Theme` genuinely lacks these tokens (it has `SolidBackground`/`LayerFill`
  but no `AppBackground`/`LayerBackground`/…), so `Theme.AppBackground` still produces CS0117 on real
  code — the analyzer is live, not dead.

**Detection.** The analyzer registers a `SimpleMemberAccessExpression` action and fires only when
(a) the access did not bind (`GetSymbolInfo(...).Symbol == null`), so it always co-occurs with the
compiler's CS0117; (b) the receiver is *exactly* `Microsoft.UI.Reactor.Core.Theme` by symbol equality
(a look-alike `Theme` in another namespace is ruled out); and (c) the chosen target still exists on the
live `Theme` surface (resolved once per compilation via a `CompilationStart` action), so a future
rename self-disables the rule rather than proposing a member that no longer compiles. As in Phase 1,
firing only on the unbound shape keeps **Warning** severity safe under `TreatWarningsAsErrors` while
still surfacing in a plain `dotnet build`.

**Alignment case (#4) intentionally not shipped.** Corpus mistake #4 —
`.VerticalAlignment(...)` / `.HorizontalAlignment(...)` (CS1061) — was **already fixed upstream in the
DSL**: `Microsoft.UI.Reactor.ElementExtensions` now defines `.HorizontalAlignment(...)` and
`.VerticalAlignment(...)` as real modifier aliases for `.HAlign(...)` / `.VAlign(...)` (added in commit
`e38b57c2`, PR #319 — *"reduce reactor-dev agent build-attempt fix-loops"*). Those calls now **bind**,
so no CS1061 occurs and there is nothing to augment; an in-build analyzer here would be dead code and
could even mis-fire on a wrong-argument call to the real modifier (overload-resolution failure, not a
missing member). The mirrored `mur check` `AlignmentShortcutRule` is obsolete for the same reason —
its fixture stubs omit the aliases, masking it. **Follow-up: retire `AlignmentShortcutRule`** when the
CLI-rule change budget allows.

**Shared-engine decision (deferred).** The analyzer keeps its small vocabulary map **local** rather
than sharing the CLI's match engine. Extracting `StringSimilarity` / `FactoryIndex` out of
`src/Reactor.Cli` now would churn `mur check` and its ~28 rule/suggester test files, and the
`netstandard2.0` analyzer project cannot consume the `net8` `FrozenDictionary` APIs `FactoryIndex`
relies on. To keep the two engines from silently diverging in the meantime, a cross-check parity test
(`DidYouMeanAnalyzerParityTests`) drives the Tier-3 rule for every analyzer map entry and asserts both
resolve the same target. The **full shared match-engine unification remains a follow-up** (§7) —
required before the fuzzy phases, which must share one ranker with `mur check`.

**Tests.** Positives (`AppBackground`/`WindowBackground` → `SolidBackground`, `LayerBackground` →
`LayerFill` override), negatives (an existing `*Background` token that binds, a non-`Background` suffix,
a look-alike `Theme` in another namespace, and the target-missing self-disable), the code-fix
round-trips, and the parity cross-check.

## §7 Later phases — the fuzzy cases

Follow-up analyzers, each reusing the shared match engine and gated to Reactor symbols:

1. **Unresolved member** (CS1061/CS0117 shapes) — receiver type resolves, member unresolved →
   fuzzy-match against the receiver's members. The one deterministic vocabulary member still live in
   this family — `Theme.AppBackground` → `Theme.SolidBackground` — shipped in Phase 2 (§6) as a
   dedicated non-fuzzy analyzer; the alignment member (`.VerticalAlignment` → `.VAlign`) is now a
   no-op because the DSL added real aliases (§6). The remaining fuzzy members (`GridSize.Pixel` →
   `.Px`, `TextBlock.Style` → modifier chain, `Button.OnClick` → `onClick:`) await the shared match
   engine.
2. **Unresolved name** (CS0103 shape) — fuzzy-match against the factory index.
3. **Argument shape** (CS1503/CS7036) — `CandidateReason == OverloadResolutionFailure` +
   `ClassifyConversion`; lower priority (rare in corpus), IDE code-fix first (Roslynator ships
   these as code-fixes).
4. **`HelpLinkUri`** on `REACTOR_*` descriptors — endorsed by every reviewer; **deferred** until a
   stable published-docs URL scheme exists (the package is not yet public — spec 022).
5. **Shared match-engine extraction** — factor `StringSimilarity` + `FactoryIndex` + the vocabulary
   tables out of `src/Reactor.Cli` into one Roslyn-only library consumed by **both** `mur check` and
   the analyzers, replacing Phase 2's local maps + parity test. Blocked today by the `netstandard2.0`
   analyzer target (no `net8` `FrozenDictionary`) and the blast radius across the CLI's ~28
   rule/suggester test files; land it before the fuzzy phases so both engines share one ranker.

Each phase lands with a perf check (IDE typing latency on a representative project) and a
false-positive audit over `samples/`.

## §8 Risks and guardrails

| Risk | Guardrail |
|---|---|
| False positives under cascading / partial-edit errors | Bail when the receiver type is unresolved or an error type; only fire when the invocation didn't bind; gate to Reactor symbols; (fuzzy phases) require high similarity thresholds. |
| IDE typing latency | Use the provided `context.SemanticModel` (never `Compilation.GetSemanticModel`, RS1030); narrow syntax-node actions; cheap symbol pre-checks before any fuzzy match. |
| Divergence between analyzer and `mur check` | Phase 2: local maps locked to the CLI Tier-3 rules by a cross-check parity test (`DidYouMeanAnalyzerParityTests`). Fuzzy phases: one shared match library + shared fixtures (§7 item 5). |
| Noise (a second diagnostic atop the CS error) | Concise message; `REACTOR_DYM_*` only ever accompanies a genuine compile error; consumers can tune severity via `.editorconfig`. |

## §9 Relationship to `mur check`

`mur check` stays the output-driven CLI: it reacts to the compiler's *actual* diagnostics, so it
retains broader coverage (any CS code, the long tail) and its agent-tuned ranker/iteration modes.
The analyzers cover the common cases natively in build + IDE. The two are complementary; the fuzzy
phases share one match engine so behaviour cannot drift.

## §10 Non-goals / superseded

- **Superseded:** the earlier output-driven-in-build proposal (a `Reactor.Check` engine extraction,
  a bundled tool packed under `tools/`, `ErrorLog` SARIF wiring, and a
  `PostBuildEvent`/`RunPostBuildEvent=Always` trigger). Retained only as CLI (`mur check`).
- **Non-goal:** exact parity with every `mur check` suggestion in-build. The analyzer is
  pattern-driven; uncovered codes degrade gracefully to the raw compiler error (never worse than
  today), with `mur check` available for full breadth.
