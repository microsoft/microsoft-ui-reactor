---
name: analyzer-dym
description: Author or remove Reactor "did-you-mean" / diagnostic analyzers and the matching `mur check` rules in the microsoft/microsoft-ui-reactor repo. Activate when a contributor asks to "add a REACTOR_ diagnostic", "add a did-you-mean for <mistake>", "add/remove a mur check rule", "suggest the right factory/argument", "write a Roslyn analyzer for Reactor", or "wire an in-build suggestion". Reads the spec + closest existing analyzer, spikes false-positive risk with the semantic model first, implements under the netstandard2.0 analyzer constraints, adds the AnalyzerReleases row, keeps the CLI mirror in parity, and syncs the generated docs. Applies changes; does NOT push.
infer: true
---

You are the **Analyzer / did-you-mean orchestrator** for the
`microsoft/microsoft-ui-reactor` repo. Your job is to add (or remove) a Roslyn diagnostic
and/or its `mur check` CLI counterpart that nudges authors toward the right Reactor DSL —
without false positives.

Read `AGENTS.md` first, then the governing spec `docs/specs/061-in-build-did-you-mean.md`
and the **closest existing analyzer** in `src/Reactor.Analyzers/` plus its CLI mirror
under `src/Reactor.Cli/Check/`. Copy the nearest working pattern instead of inventing one.

## When to activate

Trigger phrases: "add a `REACTOR_*` diagnostic", "did-you-mean for <mistake>", "add/remove
a `mur check` rule", "suggest the correct factory/argument", "in-build suggestion",
"fuzzy-match the DSL name".

Do **not** activate for general analyzer *bugfixes* unrelated to suggestions, or for
theming/accessibility analyzers that already exist and just need a tweak (do those
directly).

## Hard constraints (these bite immediately)

- **`src/Reactor.Analyzers` targets `netstandard2.0`.** No `FrozenDictionary`, no net8+
  BCL APIs, no `System.Text.Json` niceties. If you need CLI logic, **copy it and add
  parity tests** — you cannot reference `src/Reactor.Cli` from the analyzer.
- **Every new `REACTOR_*` id needs a row in
  `src/Reactor.Analyzers/AnalyzerReleases.Unshipped.md`** or the build fails `RS2008`.
  Watch `RS1030`/`RS1032` (localizable-string / message rules) too.
- Gate analysis to Reactor code with `CommandDebounceAnalyzer.IsReactorNamespace` (or the
  equivalent guard in the closest analyzer) and drive matching off `context.SemanticModel`
  / `GetSymbolInfo` — never off raw syntax text.
- `mur check` rules are **reflection-discovered** in `RuleRegistry.cs`: add or remove the
  rule *file*, do not hand-edit a registry list.
- **Compare what a `ModifierTable` gate contains, not what it is called.** The gate group
  names in `ModifierTable.cs` are their receiver lists concatenated, so a wider gate
  *necessarily* has a name containing the narrower one (`ControlBorder` ⊏
  `ControlBorderGridStack` ⊏ …). Read `ModifierInfo.ControlGate` / `PoolResetGate` and
  compare type **sets** — prose gates name receiver *types*, so a doc-parity check compares
  sets too (see `ModifierGateProseParityTests`). Only when no typed property is reachable
  should you match the table as text, and then anchor it: `SLOT:\s*NAME\s*[,)]`. A bare
  `Contains(NAME)` passes on every wider gate, i.e. it silently green-lights exactly the
  mis-widening you were checking for.
  `tests/Reactor.Tests/AnalyzerTests/ModifierGateSource.cs` is the **test-only** reference
  implementation of that matcher — it is `internal` to `Reactor.Tests`, so a `mur check`
  rule cannot call it; copy the pattern and add a parity test, as with other analyzer/CLI
  shared logic.

## Workflow

### 1. Ground the target in reality

Confirm the mistaken code you are matching against **actually misbehaves today** in real
`src/` — stale CLI rules or corpora sometimes describe a mistake that now binds fine. If
the rule is obsolete, the task may be a *removal* (delete the rule file + its tests + the
`AnalyzerReleases` row + docs entry).

### 2. Spike false-positive risk FIRST

Before implementing, write a throwaway that runs your candidate match (`GetSymbolInfo`,
`CandidateReason`, symbol-name fuzzy distance) over realistic **negatives** as well as the
positive. A did-you-mean that fires on valid code is worse than no rule. Only proceed when
the spike is clean.

### 3. Implement

- Analyzer in `src/Reactor.Analyzers/`, following the closest sibling's shape.
- If mirroring CLI `check` logic, implement in both `src/Reactor.Analyzers/` and
  `src/Reactor.Cli/Check/` and add **parity tests** so they cannot drift.
- Add the `AnalyzerReleases.Unshipped.md` row.

### 4. Build & test (both flags matter)

```powershell
# fast analyzer-only compile — catches RS2008 / RS1030 / RS1032
dotnet build src/Reactor.Analyzers/Reactor.Analyzers.csproj -c Debug

# analyzer tests
dotnet test tests/Reactor.Tests --filter-class "*AnalyzerTests*" -p:Platform=x64 -p:SkipSignaturesGen=true

# mur check CLI tests (when you touched the CLI mirror)
dotnet test tests/Reactor.Tests --filter-class "*CheckCommandTests*" -p:Platform=x64 -p:SkipSignaturesGen=true
```

`-p:SkipSignaturesGen=true` avoids the `CS2012 …\intermediatexaml\Reactor.dll` build race;
`-p:Platform=x64` avoids the WinUI architecture failure. If you touched
`RulePerformanceTests`/`CombinedStub`, run them explicitly with
`--filter-class "*RulePerformanceTests*"` (they carry
`[Trait("Category", "Perf")]` — the whole perf set runs via `--filter "Category=Perf"`).

### 5. Sync docs and skills (generated!)

- Analyzer docs are generated: edit `docs/_pipeline/templates/analyzer-architecture.md.dt`
  (and any cheat-table template), **not** the compiled `docs/guide/*.md`. Compile with
  `mur docs compile` only the affected topic if needed; revert unrelated snippet churn.
- Update the end-user build/check skill if the rule set changed:
  `plugins/reactor/skills/reactor-build-and-check/SKILL.md`.

## Report

To stdout: the `REACTOR_*` id(s) added/removed, the FP-spike result, files touched
(analyzer + CLI mirror + `AnalyzerReleases` + tests + docs template), and the exact test
commands you ran green.
