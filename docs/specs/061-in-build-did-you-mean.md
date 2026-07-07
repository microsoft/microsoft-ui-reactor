# In-Build "Did You Mean" — Analyzer-Driven Suggestions

## Status

**Proposed — 2026-07-07.** First analyzer (`REACTOR_DYM_001`) implemented; the fuzzy
name-resolution phases are design-only. This spec is the design of record for bringing Reactor's
"did you mean" suggestions — today reachable only through the `mur check` CLI — into a plain
`dotnet build` and the IDE, for consumers of the `Microsoft.UI.Reactor` NuGet package.

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
- [§6 Later phases — the fuzzy cases](#6-later-phases--the-fuzzy-cases)
- [§7 Risks and guardrails](#7-risks-and-guardrails)
- [§8 Relationship to `mur check`](#8-relationship-to-mur-check)
- [§9 Non-goals / superseded](#9-non-goals--superseded)

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

## §6 Later phases — the fuzzy cases

Follow-up analyzers, each reusing the shared match engine and gated to Reactor symbols:

1. **Unresolved member** (CS1061/CS0117 shapes) — receiver type resolves, member unresolved →
   fuzzy-match against the receiver's members; covers
   `Theme.AppBackground` → `.SolidBackground`, `GridSize.Pixel` → `.Px`, `TextBlock.Style` →
   modifier chain, `Button.OnClick` → `onClick:`.
   *(The `.VerticalAlignment` / `.HorizontalAlignment` → `.VAlign` / `.HAlign` case that
   originally motivated this shape is now resolved at the API level: `ElementExtensions` ships
   real long-form `.HorizontalAlignment(...)` / `.VerticalAlignment(...)` aliases, so those calls
   bind and raise no diagnostic. The corresponding `mur check` `AlignmentShortcutRule` has been
   removed as dead code.)*
2. **Unresolved name** (CS0103 shape) — fuzzy-match against the factory index.
3. **Argument shape** (CS1503/CS7036) — `CandidateReason == OverloadResolutionFailure` +
   `ClassifyConversion`; lower priority (rare in corpus), IDE code-fix first (Roslynator ships
   these as code-fixes).
4. **`HelpLinkUri`** on `REACTOR_*` descriptors — endorsed by every reviewer; **deferred** until a
   stable published-docs URL scheme exists (the package is not yet public — spec 022).

Each phase lands with a perf check (IDE typing latency on a representative project) and a
false-positive audit over `samples/`.

## §7 Risks and guardrails

| Risk | Guardrail |
|---|---|
| False positives under cascading / partial-edit errors | Bail when the receiver type is unresolved or an error type; only fire when the invocation didn't bind; gate to Reactor symbols; (fuzzy phases) require high similarity thresholds. |
| IDE typing latency | Use the provided `context.SemanticModel` (never `Compilation.GetSemanticModel`, RS1030); narrow syntax-node actions; cheap symbol pre-checks before any fuzzy match. |
| Divergence between analyzer and `mur check` | One shared match library + shared fixtures (fuzzy phases). |
| Noise (a second diagnostic atop the CS error) | Concise message; `REACTOR_DYM_*` only ever accompanies a genuine compile error; consumers can tune severity via `.editorconfig`. |

## §8 Relationship to `mur check`

`mur check` stays the output-driven CLI: it reacts to the compiler's *actual* diagnostics, so it
retains broader coverage (any CS code, the long tail) and its agent-tuned ranker/iteration modes.
The analyzers cover the common cases natively in build + IDE. The two are complementary; the fuzzy
phases share one match engine so behaviour cannot drift.

## §9 Non-goals / superseded

- **Superseded:** the earlier output-driven-in-build proposal (a `Reactor.Check` engine extraction,
  a bundled tool packed under `tools/`, `ErrorLog` SARIF wiring, and a
  `PostBuildEvent`/`RunPostBuildEvent=Always` trigger). Retained only as CLI (`mur check`).
- **Non-goal:** exact parity with every `mur check` suggestion in-build. The analyzer is
  pattern-driven; uncovered codes degrade gracefully to the raw compiler error (never worse than
  today), with `mur check` available for full breadth.
