# In-Build "Did You Mean" — Analyzer-Driven Suggestions

## Status

**Proposed — 2026-07-07.** First analyzer (`REACTOR_DYM_001`) and the first fuzzy analyzer
(`REACTOR_DYM_003`, the CS0103 mistyped-factory-name case) implemented; the remaining fuzzy
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
   fuzzy-match against the receiver's members; covers `.VerticalAlignment` → `.VAlign`,
   `Theme.AppBackground` → `.SolidBackground`, `GridSize.Pixel` → `.Px`, `TextBlock.Style` →
   modifier chain, `Button.OnClick` → `onClick:`.
2. **Unresolved name** (CS0103 shape) — fuzzy-match against the factory index.
   **Shipped as `REACTOR_DYM_003` (`FuzzyFactoryNameAnalyzer` + `FuzzyFactoryNameCodeFix`) — see §6.1.**
3. **Argument shape** (CS1503/CS7036) — `CandidateReason == OverloadResolutionFailure` +
   `ClassifyConversion`; lower priority (rare in corpus), IDE code-fix first (Roslynator ships
   these as code-fixes).
4. **`HelpLinkUri`** on `REACTOR_*` descriptors — endorsed by every reviewer; **deferred** until a
   stable published-docs URL scheme exists (the package is not yet public — spec 022).

Each phase lands with a perf check (IDE typing latency on a representative project) and a
false-positive audit over `samples/`.

### §6.1 `REACTOR_DYM_003` — mistyped factory name (CS0103, shipped)

`FuzzyFactoryNameAnalyzer` covers the **#2** corpus mistake (~63 events): a mistyped Reactor factory
name in call position (e.g. `Buton("x")` for `Button`, `Vstack(...)` for `VStack`). It is the first
**fuzzy** analyzer, so **false-positive control is the design centre** ("a wrong suggestion is worse
than no suggestion", spec 038 §1): precision is favoured over recall.

- **Detection.** On an `InvocationExpression` whose callee is a **bare identifier** (`Foo(...)` — the
  factory-call shape; member access `x.Foo()` is phase 1's CS1061/CS0117 territory), report only when
  the name is genuinely unbound (`GetSymbolInfo(...).Symbol == null` **and** no `CandidateSymbols` —
  i.e. the CS0103 shape, not an overload/accessibility failure).
- **Live factory set.** Candidate names are enumerated once per compilation from the real
  `Microsoft.UI.Reactor.Factories` type via `GetTypeByMetadataName` (public static ordinary methods,
  deduped) — always current with the referenced package, and a no-op in non-Reactor projects. The
  CLI's `FactoryIndex` is deliberately **not** reused: it depends on net8 `FrozenDictionary` APIs
  unavailable in the netstandard2.0 analyzer.
- **Similarity.** Jaro-Winkler, ported **verbatim** from the CLI's `StringSimilarity` into
  `src/Reactor.Analyzers/StringSimilarity.cs` (the net10 CLI engine can't be referenced from the
  netstandard2.0 analyzer). A parity unit test (`StringSimilarityParityTests`) asserts the two copies
  return **bit-identical** scores across a battery of inputs, so the analyzer and `mur check` cannot
  drift.
- **False-positive gating (all must hold).** (a) unbound name; (b) bare-identifier call position;
  (c) **PascalCase** first letter — excludes the dominant CS0103 false-positive shape, a typo'd
  camelCase local/parameter; (d) length ≥ 4; (e) not itself an exact factory name (that's a missing
  `using static`, a different fix); (f) the closest factory — searched **only among names within ±2
  of the same length** — clears the threshold and is a **unique** best (a tie such as `Stack` between
  `HStack`/`VStack` stays silent).
- **Threshold: 0.88** (stricter than the CLI's CS0103 floor of 0.75, as an always-on analyzer
  demands). Chosen from an empirical false-positive spike over the 156 live factory names, 40
  realistic factory typos, and 66 realistic non-factory unknown identifiers (typo'd camelCase locals,
  unrelated PascalCase types/methods, and short words that prefix long factories). Results:
  0.75 with no extra gating → **41 false positives** (unusable always-on); the full gate at **0.88 /
  length-Δ ≤ 2 / minLen ≥ 4 / PascalCase / tie-guard → 0 false positives at 40/40 recall**. The
  firing positives cluster at ≥ 0.90 (`Vstack` → `VStack` = 0.900 is the floor); the closest
  non-firing negative is `Compute` → `Component` at 0.871 — 0.88 sits in that valley. The **length-Δ
  gate is the key lever**: it defeats the Jaro-Winkler common-prefix inflation that would otherwise
  "correct" `List` → `ListBox`, `Text` → `TextBox`, `Command` → `CommandBar` (all 0.90+ but Δlen ≥ 3).
- **Severity: Warning.** Fires only alongside the compiler's CS0103, so Warning is safe under
  `TreatWarningsAsErrors` (the build already fails) while still surfacing in a plain `dotnet build`
  and the IDE. Tunable via `.editorconfig`.
- **Fix.** Renames the identifier to the suggested factory (`Buton("x")` → `Button("x")`),
  batch-fixable; the suggestion travels via the diagnostic property bag so the fix never re-computes
  similarity.
- **Tests.** Positive (typo, wrong-case, nested-in-a-real-factory, message-arguments); the gating
  negatives (valid calls, camelCase name, unrelated name, exact-name-missing-using, bound non-factory
  method, member-access typo, short-prefix-of-long-factory, ambiguous tie, no-Reactor compilation);
  the code-fix round-trip; and the `StringSimilarity` parity test.
- **Scope note.** Generic-name callees (`Foo<int>(...)`) are intentionally out of scope for v1 to keep
  the fix trivial and the gate tight; they can be added later if the corpus shows demand.

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
