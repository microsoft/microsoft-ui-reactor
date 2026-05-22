# Reactor Eval Trace Mining — Design Spec

## Status

**Proposed** — 2026-05-09. No code yet.

This spec describes a **standalone harness** that drives a coding agent to author small Microsoft.UI.Reactor (Reactor) apps from natural-language prompts, captures the full build/fix loop the agent runs through, and emits a structured corpus of `(broken_source, diagnostic, fixed_source)` triples. The corpus seeds the pattern rules in spec [038 — `mur check` Did-You-Mean Engine](./038-mur-check-did-you-mean-design.md).

The harness is deliberately built to run **outside** the `microsoft-ui-reactor` source tree. The agent it drives must not have file-system access to the Reactor source, samples, or internal skills — only what a real downstream consumer would have (the public NuGet feed, the `mur` CLI binary, whatever public docs ship with the package). This is not a packaging accident; it is the **point** of the harness. We want the agent to struggle in the same ways a real first-time Reactor user / agent would, so the failures we capture reflect real prediction needs and not artifacts of source-code peeking.

This spec is written so an external agent — one *without* access to this repo — can implement the harness from this document alone. Do not reference internal Reactor source paths from inside the harness implementation; the package surface, the CLI, and what is documented in this spec are the contract.

---

## Table of Contents

- [§1 Motivation](#1-motivation)
- [§2 Goals and non-goals](#2-goals-and-non-goals)
- [§3 What the agent under test sees](#3-what-the-agent-under-test-sees)
- [§4 Architecture](#4-architecture)
- [§5 Prompt grid](#5-prompt-grid)
- [§6 Sandbox layout](#6-sandbox-layout)
- [§7 Trace capture](#7-trace-capture)
- [§8 Pair extraction](#8-pair-extraction)
- [§9 Output schema](#9-output-schema)
- [§10 Aggregation and rule induction](#10-aggregation-and-rule-induction)
- [§11 Operational concerns](#11-operational-concerns)
- [§12 Implementation phases](#12-implementation-phases)
- [§13 Open questions](#13-open-questions)

---

## §1 Motivation

The `mur check` Roslyn-backed suggestion engine (spec 038) needs a frequency-ranked list of the mistake patterns a real coding agent makes when authoring Reactor apps. Without that list we have to *guess* which pattern rules to write, and a wrong guess wastes engineering effort on a code path that never fires.

Three sources of truth exist in principle:

1. **Production telemetry** from `mur check` invocations. Doesn't exist yet — `mur check` ships only the analyzer-ID hint table today, so we have no signal on CS-prefixed errors against Reactor types.
2. **Synthetic mutation** of known-good Reactor source: rename methods, fluentize named args, swap overloads, drop `WithKey` calls, etc. Cheap to scale to 100K pairs but distribution is artificial — captures what we *can* corrupt rather than what an LLM agent *does* corrupt.
3. **Replay of real agent runs.** The closest thing to ground truth: drive an LLM agent to write a Reactor app, capture every build failure and the eventual fix, take the diff. Errors are sampled from the real distribution; fixes are validated by the C# compiler.

This spec implements (3). It is the load-bearing data source for spec 038. (1) takes over once 038 ships and runs at scale; (2) is retained as a validation set, never as training data.

The corpus serves a **second** consumer in spec 038: the **pre-emit warning ranker** (spec 038 §8). The same per-turn trace that lets us extract `(broken, diagnostic, fixed)` pairs also tells us, for every diagnostic that fired, whether the agent's eventual fix actually touched the line / file / symbol the diagnostic pointed at. That binary label — *did this diagnostic correlate with a real fix, or did the agent ignore it?* — is the supervised signal for training a learned ranker that decides which warnings to surface mid-iteration vs. defer to a final-pass mode. The harness emits this label alongside the pair triples so the ranker training pipeline does not need to re-walk the trace.

A second, equally important constraint: **the agent under test must not be able to read the Reactor source code or samples.** If it can, the failures we capture stop reflecting a downstream user's experience and start reflecting a uniquely-privileged author's. The harness enforces this isolation; see §3 and §6.

## §2 Goals and non-goals

### Goals

- **Drive a coding agent through a prompt grid** broad enough to surface the long tail of mistake patterns, not just the two scenarios in the existing eval suite.
- **Sandbox the agent's view** so it cannot read Reactor source, internal skills, or the existing sample apps. It sees only the public NuGet package and `mur` binary.
- **Capture every build cycle** — the source state at each failed `dotnet build`, the resulting diagnostics, and the source state at the next successful build.
- **Emit a normalized JSONL corpus** of `(broken, diagnostic, fixed)` triples consumable by spec 038's pair-extraction and rule-induction stages.
- **Be reproducible.** Same prompt, same model, same package version → roughly the same trace shape. Hermetic working directories. No reliance on user-specific state.
- **Be cheap to extend.** New prompts, new control combinations, new agents (Claude / GPT / Copilot) plug in by configuration, not code changes.

### Non-goals

- **Not a benchmark of agent quality.** We don't score the agent on whether it eventually succeeded; we keep the trace either way. The data is the product, not the leaderboard.
- **Not a CI gate.** This is an offline data-generation pipeline run on a cadence (weekly, when Reactor cuts a release, etc.). It is not in the build path.
- **Not a `mur check` substitute.** This harness produces training data; spec 038 is the runtime feature that uses it.
- **Not an end-to-end UI tester.** We grade compile, not behavior. UIA-driven runtime verification is out of scope.
- **Not bound to any one agent.** The harness drives whatever agent supports stdin/stdout tool calls; Claude Code / OpenAI Agents SDK / a custom MCP shim are all targets, but only one is required for v1.

## §3 What the agent under test sees

This is the most important section of the spec. Get it wrong and the corpus is contaminated.

The agent runs in a hermetic working directory provisioned per prompt. Inside that directory it has:

- **A `.NET SDK** of the version Reactor's NuGet declares. Installed system-wide, available on `PATH`.
- **The public Reactor NuGet packages**, restored from `nuget.org` (or a private feed configured by env var — see §11) the first time it runs `dotnet build`. The agent never sees the source — only compiled assemblies in the local NuGet cache.
- **The `mur` CLI binary**, installed as a dotnet tool from the same NuGet feed: `dotnet tool install -g mur`. The agent invokes `mur new`, `mur check`, etc., as a black box.
- **A starter prompt** specifying the app to build (see §5).
- **No skills, no API index, no cheatsheet, no samples.** The point is to capture what an agent does when it has to *infer* the API shape from compiler errors. If we ship guidance, we are training the rules to handle a populated-skill scenario, not a struggling one.

What the agent does **not** have:

- Read access to `microsoft-ui-reactor` source on disk. The harness is a separate repo / separate working tree; nothing in `$PWD` or any ancestor path is a reactor checkout.
- Access to the agent-eval skill packs (`reactor-getting-started`, `reactor-dsl`, `reactor-build-and-check`). The harness explicitly does not load them.
- Access to the existing sample apps under `samples/apps/`. Same reason.
- Network access to repositories that mirror Reactor source (GitHub web fetch of `microsoft/microsoft-ui-reactor`, etc.). The sandbox blocks egress to those hostnames; allow-list only `nuget.org` and the LLM API.

If the agent's tool surface includes a generic web-fetch or web-search tool, the harness must either disable it or apply a domain allow-list before each run. See §6 for the enforcement mechanism.

We accept that the agent will fail more often than it does in the populated-skill scenario. That is the data we are after.

## §4 Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│  reactor-eval-mine (this harness, separate repo)                     │
│                                                                       │
│   ┌────────────────┐     ┌─────────────────┐    ┌────────────────┐  │
│   │ PromptGen      │────▶│ Runner          │───▶│ TraceWriter    │  │
│   │  - grid        │     │  - sandbox      │    │  - JSONL       │  │
│   │  - JSONL out   │     │  - spawn agent  │    │  - per-run     │  │
│   │                │     │  - capture I/O  │    │                │  │
│   └────────────────┘     └────────┬────────┘    └───────┬────────┘  │
│                                   │                     │           │
│                                   ▼                     ▼           │
│                          ┌─────────────────┐    ┌────────────────┐  │
│                          │  Sandbox        │    │ PairExtractor  │  │
│                          │   workdir/      │    │  - replay log  │  │
│                          │   .nuget cache  │    │  - emit triples│  │
│                          │   no reactor src│    │  - normalize   │  │
│                          └─────────────────┘    └───────┬────────┘  │
│                                                         │           │
│                                                         ▼           │
│                                                ┌────────────────┐   │
│                                                │  fixes.jsonl   │   │
│                                                │   (the corpus) │   │
│                                                └────────────────┘   │
└──────────────────────────────────────────────────────────────────────┘
```

Five components, each with a single responsibility:

1. **PromptGen** — emits a JSONL of prompts. Combinatorial grid (§5) plus optional LLM-rewriting for natural-language variation. Output: `prompts.jsonl`.
2. **Runner** — for each prompt, provisions a sandbox, spawns the agent process, pipes the prompt in, captures every tool call and file edit and shell output. Output: one `trace-<run_id>.jsonl` per run.
3. **Sandbox** — the per-run hermetic working directory plus its enforcement policy (no reactor src on path; egress allow-list).
4. **PairExtractor** — replays each trace, finds every `dotnet build` invocation, captures the source state immediately before and the source state at the next successful build, computes the diff, classifies the fix kind. Output: `fixes-<run_id>.jsonl`.
5. **Aggregator** — merges all per-run JSONL into `fixes.jsonl`, deduplicates, ranks by frequency.

Components are pipelinable but each can be invoked standalone for debugging.

## §5 Prompt grid

A prompt is a templated string filled from a Cartesian product of axes. Suggested axes for v1:

| Axis | Values |
|---|---|
| App shape | `single page`, `tabbed app`, `master-detail`, `dialog flow` |
| Primary control | `Button`, `CheckBox`, `ComboBox`, `DataGrid`, `TextBox`, `Slider`, `RadioButton`, `Toggle` |
| Interaction | `triggers an action`, `binds to state`, `filters a list`, `edits a record`, `submits a form` |
| State shape | `string`, `int counter`, `enum selection`, `record`, `list of records`, `tree of nodes` |
| Layout | `Grid`, `HStack`, `VStack`, `Flex`, `Wrap` |
| Theming | `default`, `dark mode toggle`, `accent color override` |

A v1 grid that takes the cross-product of one value per axis at a time gives ~8K prompts; in practice the harness samples a stratified subset (see §11) to keep cost bounded.

A prompt template:

```
Build me a {app_shape} that uses a {primary_control} to {interaction} a {state_shape},
laid out in a {layout}. Theme it with {theming}. Use Microsoft.UI.Reactor (the C#/WinUI
React-style framework). The app must build cleanly with `dotnet build`. Do not add
unrelated features.
```

Optional: an LLM-rewriting pass takes each templated prompt and produces three to five natural-language variants (different word order, different framing, light paraphrase) to keep the agent from pattern-matching on the template itself.

The harness emits each prompt with a stable `prompt_id` (hash of the template-fill) so re-runs are joinable.

## §6 Sandbox layout

For each run, Runner creates:

```
<harness-root>/
  runs/
    <run_id>/
      workdir/                  ← agent's CWD; ONLY thing the agent sees
        (initially empty)
      trace.jsonl               ← Runner writes here (outside agent's view)
      meta.json                 ← prompt, agent config, package versions
      .stdout / .stderr         ← agent process logs
```

Enforcement of "no Reactor source visible":

- `workdir` lives under a path that does **not** contain the substring `microsoft-ui-reactor` or `reactor2`. The harness aborts startup if it does.
- The agent process is spawned with `CWD=workdir` and a scrubbed `PATH` that includes only the .NET SDK, `mur`, and standard system tools. No editor, no `git` against the reactor remote.
- Network egress (if controllable in the agent's runtime) is filtered to: the LLM provider, `api.nuget.org`, `*.blob.core.windows.net` (NuGet CDN). Block `github.com/microsoft/microsoft-ui-reactor` and any mirror.
- If the agent has a web-fetch tool that the harness cannot intercept, the harness logs each fetch URL and post-hoc filters out runs that touched a forbidden domain. Better than nothing, worse than blocking.

The first build the agent runs causes NuGet to populate `~/.nuget/packages` with the public Reactor packages. That cache is shared across runs (it's a public artifact, identical to what any user gets) and survives between runs to keep cold-start cost down.

## §7 Trace capture

For each agent action, Runner appends one JSON object to `trace.jsonl`:

```json
{
  "run_id": "...", "turn": 7, "ts": "2026-05-09T12:34:56Z",
  "kind": "tool_call",
  "tool": "Bash",
  "input": "dotnet build .\\App.csproj -p:Platform=ARM64",
  "output": "...",
  "exit_code": 1,
  "files_after": [
    { "path": "Program.cs", "sha": "...", "content": "..." }
  ]
}
```

Five `kind` values cover the full surface:

| `kind` | When |
|---|---|
| `prompt` | turn 0; the initial user message |
| `assistant_text` | the agent's textual reasoning between tool calls |
| `tool_call` | every tool invocation; for shell-style tools, includes stdin/stdout/exit_code |
| `file_snapshot` | a full content dump of every file in `workdir` after each tool call that mutated the tree |
| `terminal` | end of run; final disposition (`succeeded` / `failed` / `timeout`) |

`file_snapshot` is what makes pair extraction tractable. It is verbose; gzip the trace at end of run.

## §8 Pair extraction

Replay walks `trace.jsonl` and slides a window over `tool_call` entries with `tool == "Bash"` and the command matching `dotnet build` or `mur check`. For each such entry:

- If `exit_code != 0` and the parsed output contains a CS-prefixed diagnostic on a Reactor type, it's a **failed-build event**. Snapshot the file referenced by the diagnostic at the entry's `files_after`. Mark this as `before`.
- Continue forward through the trace. If a subsequent `dotnet build` / `mur check` exits 0 **and** the same file's content has changed, the snapshot at that entry is `after`.
- Emit a triple: `(before, diagnostic, after)`. If the same file is modified again before the build succeeds, retain only the *closest* `before` (the one nearest to the success) — that is the state the eventual fix transformed.

Edge cases:

- **No subsequent success.** Run ended in failure. The pair is incomplete; record it under `unresolved.jsonl` for manual triage. Do not feed unresolved pairs into rule induction.
- **Multiple files changed between fail and success.** The fix is multi-file; emit one triple per file that changed and tag them with a shared `bundle_id` so rule induction can either treat them independently or as a unit.
- **The diagnostic moves between turns.** If `dotnet build` reports five errors at turn 7 and three different errors at turn 11, treat each `(before, diag)` pair independently against its own `after`.

## §9 Output schema

Each row in `fixes.jsonl`:

```json
{
  "run_id": "p001-r03",
  "prompt_id": "tabbed/Button/triggers/int-counter/Grid/dark",
  "turn_before": 7, "turn_after": 11,
  "file": "Program.cs",
  "diag": {
    "code": "CS1061",
    "msg": "'ButtonElement' does not contain a definition for 'OnClick'",
    "line": 34, "col": 16,
    "receiver_type": "Microsoft.UI.Reactor.ButtonElement"
  },
  "before": { "sha": "...", "text": "...full file..." },
  "after":  { "sha": "...", "text": "...full file..." },
  "delta": {
    "hunks": [ { "before": "Button(\"+\").OnClick(...)", "after": "Button(\"+\", onClick: ...)" } ]
  },
  "fix_kind": "fluent_to_named",
  "bundle_id": null,
  "agent": "claude-opus-4-7",
  "package_version": "0.0.0-local"
}
```

`fix_kind` is a small enum the extractor assigns heuristically:

- `renamed_member` — receiver type unchanged, member name swapped.
- `fluent_to_named` — chained call removed, named argument added on the parent factory call.
- `overload_swap` — same factory, different overload (different positional shape).
- `factory_swap` — different factory entirely (`Button` → `HyperlinkButton`).
- `argument_type_fix` — same call shape, argument types adjusted.
- `missing_with_key` — `.WithKey(...)` appended to elements in a dynamic list.
- `import_added` — fix was a new `using` statement.
- `other` — extractor couldn't classify; rule induction treats these as a residual bucket.

### Per-diagnostic ranker labels

In addition to the pair triples, the extractor emits one row per diagnostic that fired during the run (whether it became a "blocker" eventually fixed, or was carried over silently across builds, or never resurfaced). This is the supervised signal for spec 038 §8's learned warning ranker. Schema:

```json
{
  "run_id": "p001-r03",
  "turn": 7,
  "file": "Program.cs",
  "diag": { "code": "CS1591", "msg": "...", "line": 12, "col": 1 },
  "severity": "W",
  "addressed_by_next_fix": false,
  "addressed_within_run": false,
  "still_present_at_run_end": true,
  "agent_ignored": true
}
```

Output to `ranker-labels-<run_id>.jsonl`, aggregated alongside `fixes.jsonl`. The label `addressed_by_next_fix` is the primary supervised signal: a diagnostic the agent's *next* edit touched is emit-worthy in iteration mode; one it didn't is a candidate for suppression. `addressed_within_run` (eventually addressed before run end) and `still_present_at_run_end` (carried over silently) are auxiliary labels useful for distinguishing "deferred but real" warnings from "ignored as noise."

## §10 Aggregation and rule induction

Aggregator clusters by `(diag.code, fix_kind, AST-shape-of-before)` and emits `patterns.json`:

```json
[
  {
    "cluster_id": "C0001",
    "diag_code": "CS1061",
    "receiver_type": "Microsoft.UI.Reactor.ButtonElement",
    "fix_kind": "fluent_to_named",
    "frequency": 0.087,
    "exemplar_pairs": ["p001-r03", "p014-r02", "p044-r01"],
    "proposed_rule": "On CS1061 against ButtonElement member 'OnClick', suggest moving to named argument onClick: on the parent Button(...) factory call."
  },
  ...
]
```

`patterns.json` is the human-reviewed handoff to spec 038's Phase 3 rule authors. The harness does **not** auto-generate runtime rules from this — every cluster gets human review before becoming a Tier 3 rule, because false positives (Phase 1 §risk) cost more than missed suggestions.

The "AST-shape-of-before" is computed from a Roslyn parse of the snippet around the diagnostic span. Since the harness runs outside the reactor repo, it depends on `Microsoft.CodeAnalysis.CSharp` from NuGet — same Roslyn version spec 038 uses — and treats the snippet as standalone text (no full Compilation needed at this stage; rule induction is structural, not semantic).

## §11 Operational concerns

**Cost.** Each run costs roughly $3–5 of LLM tokens at current Claude / GPT prices, by analogy to the existing eval suite. A 500-prompt sweep with 3 LLM-rewritten variants per prompt and 1 run per variant ≈ 1500 runs ≈ $4.5K–$7.5K. Stratify the prompt grid: bias toward axes (`Primary control`, `Interaction`) that are likely to surface novel patterns, downsample axes (`Theming`) that probably don't.

**Agent flakiness.** Agents time out, get rate-limited, refuse to act on prompts they misread. Runner enforces a per-run wall-clock budget (default 15 min) and a max-turn budget (default 30). Runs that exceed either are marked `timeout` and excluded from `fixes.jsonl` aggregation. Their `unresolved.jsonl` rows are still useful for cost estimation.

**Determinism.** Even with `temperature=0` agents are not bit-deterministic. Runs are tagged with the model id, package version, and SDK version so re-runs against a different baseline can be compared like-for-like. Don't treat any single run's pairs as canonical; rely on cluster frequency.

**Privacy and PII.** The agent under test is given a synthetic prompt and an empty workdir. Nothing in the trace should contain user identifiers. The harness still scrubs absolute paths, usernames, and machine names from each trace before writing.

**Storage.** Each run is small (~1–5 MB compressed). 1500 runs ≈ 5 GB. Keep raw traces on local disk for the active sweep; archive to a blob store keyed by `(prompt_id, agent, package_version, run_id)` after rule induction completes.

**Reactor version pinning.** The package version under test is recorded per run. When Reactor cuts a new release, the harness re-runs against the new version — patterns rules induced against an old surface may need refresh.

## §12 Implementation phases

### Phase A — minimal harness, one prompt, one agent

- PromptGen with a single hand-authored prompt.
- Runner that spawns Claude Code (or whatever LLM agent) against a temp `workdir`, pipes the prompt in, tees stdout/stderr.
- TraceWriter capturing `tool_call` and `file_snapshot` only.
- PairExtractor that pulls one (before, diag, after) triple end-to-end.

**Exit:** one row in `fixes.jsonl` from one real run.

### Phase B — grid expansion

- Combinatorial PromptGen (§5).
- Stratified sampling.
- Runner parallelism (4–8 concurrent runs).
- LLM-rewriting pass for prompt variants.

**Exit:** 200-row `fixes.jsonl` from a 100-prompt sweep.

### Phase C — aggregation and review

- Aggregator with AST-shape clustering.
- `patterns.json` emission.
- Human-review workflow: a Markdown report per cluster with exemplar diffs, ready to hand off to spec 038 rule authors.

**Exit:** `patterns.json` covering the top 20 clusters by frequency, each with ≥ 3 exemplar pairs.

### Phase D — operationalization

- Sandbox enforcement hardening (egress filter, source-tree assertion).
- Multi-agent support (rotate Claude, GPT, Copilot).
- Cost dashboarding.
- Scheduled re-runs against new Reactor releases.

**Exit:** harness runs unattended on a schedule; outputs feed spec 038's Phase 0 instrumentation.

## §13 Open questions

1. **Which agent for v1?** Claude Code is the closest match for the existing eval setup, but it is not strictly necessary — any LLM agent that exposes a tool-call protocol works. Document the chosen target in `meta.json` per run; don't lock the harness to one.
2. **How much guidance is too little?** The default is "no skills, no cheatsheet" — pure struggle. We may discover that *zero* guidance produces too-noisy traces (every run flails on the same scaffolding question) and that a *tiny* always-loaded preamble (e.g. "Reactor is a C# WinUI framework; use `mur new` to scaffold") is needed to keep traces focused on the questions we actually care about. Tune empirically in Phase A.
3. **What fraction of runs must succeed for the corpus to be useful?** Failed-but-illuminating runs (`unresolved.jsonl`) still tell us where the agent gets stuck. The threshold for *training-quality* pairs (`fixes.jsonl`) is probably 30–50 % run success; below that the corpus is dominated by terminal flailing rather than recoverable mistakes.
4. **How do we keep the prompt grid fresh?** Patterns rules tuned on today's grid may overfit to today's controls. Open: rotate axes quarterly, or seed the grid from public Reactor sample-app *names* (not source) once we stabilize.
5. **Does the harness need its own license?** It's a separate repo and embeds nothing from `microsoft-ui-reactor` source. Recommend MIT or Apache-2.0; coordinate with the Reactor team on the canonical answer before publishing.

---

This spec is the data-side counterpart of [038 — `mur check` Did-You-Mean Engine](./038-mur-check-did-you-mean-design.md). The corpus emitted here (`fixes.jsonl`, `patterns.json`) is the input to spec 038's Phase 3 rule induction. Spec 038 also adds production telemetry that, once running at scale, supersedes this harness as the dominant signal source.
