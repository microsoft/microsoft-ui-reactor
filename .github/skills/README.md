# Developer skills

Skills in this directory are for **contributors working on the
`microsoft/microsoft-ui-reactor` repository itself**. They are read by Copilot CLI
(and other agents) to perform repo-specific developer tasks like reviewing a PR
before push.

> **Not the same as the shipped agent kit.** The end-user skills that help people
> *build apps* with Reactor live in the repo-root `skills/` directory and in
> `plugins/reactor/` (the shipped Copilot/Claude plugin). Those are packed into the
> `Microsoft.UI.Reactor` NuGet under `agentkit/` (see `src/Reactor/Reactor.csproj`).
> Skills under `.github/skills/` are hand-written contributor tooling and are **not**
> shipped to end users.

## Available skills

| Skill | Purpose |
|-------|---------|
| [`pr-review/`](pr-review/SKILL.md) | Multi-dimensional review of a PR / feature branch diff (security, correctness, API & DSL ergonomics, alternative solutions, test coverage, docs & samples sync, packaging/agent-kit impact, multi-model cross-check). Reports findings to stdout; does not apply fixes. |
| [`pr-lifecycle/`](pr-lifecycle/SKILL.md) | Take a PR to a clean `ready-for-review` state: run `pr-review` once and fix Critical/High/Medium findings, then loop GitHub Copilot review rounds until no meaningful findings remain. Every round requires green CI and a reply + resolve on each Copilot and `github-code-quality` thread. Tracks state with `agent-preparing` / `agent-blocked` / `ready-for-review` labels; waits via a session wakeup. Applies fixes and pushes; never merges. |
| [`perf-compare/`](perf-compare/SKILL.md) | Benchmark the `StressPerf.ReactorOptimized` data-grid harness on the current branch vs a clean `main` worktree (interleaved, same machine) and report a direction-aware delta for the four headline metrics. The local equivalent of commenting `/perf` on a PR. Reports to stdout; does not apply fixes. |
| [`coverage-uplift/`](coverage-uplift/SKILL.md) | Raise test coverage with non-vacuous tests: baseline via `tools/coverage`, classify each gap into the right tier (headless unit / selftest / E2E), write narrowly-scoped tests whose assertions fail if the target code is deleted, mutation-check the oracles, and request a cross-family test-oracle review. Adds tests. |
| [`analyzer-dym/`](analyzer-dym/SKILL.md) | Author or remove Reactor "did-you-mean" / diagnostic analyzers and their `mur check` mirrors under the `netstandard2.0` analyzer constraints: FP-spike first, add the `AnalyzerReleases.Unshipped.md` row, keep CLI parity, sync generated docs. Applies changes. |

## Model selection

Evidence refreshed **2026-11-05**. Keep model IDs and selection rationale here,
not in individual prompt fragments. These are task-fit candidates, not validated
per-dimension defaults. The [first Reactor pilot](../../tools/review-eval/results/2026-11-05.md)
and [five uncapped repeats per model](../../tools/review-eval/results/2026-11-05-repeats.md)
favored GPT-6.1 Sol on one snapshot: mean known runtime defects found per review
were 1.4 for Sol, 1.0 for Astra, 0.2 for Opus, and 0 for Sonnet, out of three.
Sol also used fewer credits. Keep it as the correctness-review evaluation
baseline; repeated samples of one PR do not establish repository-wide defaults.

### Runtime selection

1. **Honor the user's selection and the host's rules first.** Do not change the
   orchestrator's model or override configured agent preferences just to match
   this table. Keep configured defaults for ordinary primary reviews; consult
   the candidates for an authorized model choice or experiment. If the host
   requires an explicit user choice before a sub-agent override, omit `model`,
   `reasoning_effort`, and `context_tier` unless authorized; this document does
   not bypass that restriction.
2. **Check the current host's model catalog and tool schema before invoking.**
   A public model announcement does not establish access in this client,
   account, or organization. Use an enabled, exact host ID; provider API IDs
   are not necessarily interchangeable with Copilot IDs. Never invent an ID by
   incrementing a version or copy an example that the host no longer supports.
3. **Keep configured reasoning and context defaults.** Do not set `max` effort
   or long context just because a published score used them. When an override
   is authorized, use only settings supported by that model and host. More
   effort is not monotonically better; pass relevant evidence rather than
   padding every prompt with the entire repository.
4. **Report degraded coverage.** If a recommended model is unavailable, use an
   allowed, enabled alternative suited to the same work, or keep the configured
   default and disclose the substitution. Cross-family checks additionally need
   a different family. If that cannot be established, report them as incomplete,
   not clean. Do not retry refusals or safety fallbacks through another model to
   evade a restriction.

### Task-fit candidates

| Workload | Candidate Copilot model ID | Use and limits |
|----------|------------------------------|----------------|
| **Measured correctness-review candidate / evaluation baseline** | `gpt-6.1-sol` | Best known-runtime-defect/cost result in the five-repeat case. Also a different-family candidate after a Claude first pass; cross-family verification and other dimensions remain unvalidated. |
| **Deep-review challenger / Claude cross-check candidate** | `claude-opus-5.5` | Found a documentation gap Sol missed, but most reference runtime bugs escaped its reviews. Available for complex judgment and a permitted different-family check after OpenAI; not a blanket deep-review default. |
| **Bounded-review candidate** | `claude-sonnet-5.5` | Candidate for docs/sample synchronization and straightforward inventories. No reference hits in this combined-review case; focused bounded tasks remain unmeasured. Do not infer correctness, security or AOT-review suitability from price or vendor positioning. |
| **Measured comparator** | `gpt-6-astra` | Found one known runtime bug in each repeat at substantially higher recorded cost than Sol. This case does not justify preferring it as a new default. It is not a different-family check after Sol. |
| **Mechanical workflows** | Inherit the configured model; no override | Running the committed perf script, reading CI state, and lifecycle bookkeeping do not need a frontier-model fan-out. Nontrivial fixes still warrant deep reasoning. |

The [PR review dimension table](pr-review/SKILL.md#4-review-the-dimensions)
maps these workloads to each review lens as hypotheses. The other skills
reference this same policy; none needs its own aging model list.

### Cross-family checks

Choose relative to the **actual first-pass reviewer or test author**, not merely
the orchestrator. Opus and Sonnet are both Claude; changing between them, or
between two GPT versions, is not family diversity. For direct reviews, the
orchestrator is the first-pass reviewer.

Track model provenance per finding and reviewed area. If first-pass reviewers
used mixed families, a single second-pass model may not independently cover
every finding. Use the smallest permitted set of cross-checks that covers them;
mark any uncovered finding `not reviewed`. A deduplicated finding retains all
contributing reviewers' provenance.

Record the requested model ID and any effective model/fallback reported by the
host. Do not infer identity from a sub-agent's prose. If the effective family
cannot be established, say that diversity is unverified. A same-family second
opinion can still be useful, but must not be reported as a cross-family
confirmation. Agreement between models is corroboration, not proof: retain the
code evidence and counterexample for every finding.

### Evidence and refresh policy

These sources support a shortlist, not a universal ranking:

| Source | Relevant evidence | What it does not establish |
|--------|-------------------|----------------------------|
| [Anthropic: Opus 5.5, 2026-09-22](https://www.anthropic.com/claude-opus-5-5) | Reports FrontierCode v1.1 at 54.4% with adaptive thinking at max effort, and Terminal-Bench 4.0 at 66.4% at xhigh. The evaluation includes production safeguards and fallback models. | Reactor/C#/WinUI review accuracy, or a head-to-head at our host's default effort. These are vendor-reported agentic evaluations. |
| [Anthropic: Sonnet 5.5, 2026-09-28](https://www.anthropic.com/claude-sonnet-5-5) | Positions Sonnet for well-scoped work and Opus for complex judgment. Reports FrontierCode at 52.1% at xhigh versus 46.2% at max: maximum effort is not automatically best. | A reason to use the cheaper model for every security, correctness, or test-oracle decision. |
| [OpenAI: GPT-6.1 Sol, 2026-09-29](https://openai.com/index/introducing-gpt-6-1-sol/) | Describes near-Astra agentic coding capability at one-fifth of Astra's standard input/output token prices. [GitHub's task comparison](https://docs.github.com/en/copilot/reference/ai-models/model-comparison) recommends it for complex coding. | A measured win over Opus on PR review, or the cost of a Copilot review run. API token prices are not Copilot credit measurements. |
| [GitHub: supported models](https://docs.github.com/en/copilot/reference/ai-models/supported-models) | Lists supported options; availability varies by client, plan, and policy. | Access in the current session, or permission to override its configured model. |

Keep benchmark versions, harnesses, effort settings, safeguards, and budgets
attached to scores. For example, [Vals' Terminal-Bench 4.0 methodology](https://www.vals-ai.com/benchmarks/terminal-bench-4)
uses mini-swe-agent, 66 tasks, and three pass@1 runs; those results are not
interchangeable with a vendor harness or a different Terminal-Bench version.
Missing or incomparable results are **unknown**, not evidence of inferiority.

Before promoting a future default, refresh the availability and source dates.
For a Reactor-specific ranking, compare candidates on the same fixed diffs,
context, tools, and budget, covering all seven review dimensions and clean
negative cases. Have maintainers adjudicate confirmed defects, false positives,
missed defects, severity, latency, and actual cost. The first local calibration
and repeated study are linked above; they measure within-case repeatability,
not corpus-wide reliability, the dimension mappings, or the end-to-end routing
policy. Do not run a paid evaluation or add models to every review merely to
refresh this document.

## Conventions

- Each skill is a directory containing a `SKILL.md` (the entry point the
  orchestrating agent reads) and any supporting prompt fragments.
- A skill is instructions, not an executable. The orchestrating agent uses its
  own tools (`task`, `grep`, `view`, `powershell`, etc.) following the
  `SKILL.md`. A skill may direct the agent to invoke a **committed** repo script
  via its `powershell` tool (e.g. `perf-compare` drives
  `tests/stress_perf/ci/Run-PerfBenchmark.ps1`), but the skill itself ships no
  runnable code.
- Prompt fragments meant to be passed verbatim to sub-agents live under a
  `dimensions/` (or similarly named) subfolder.
- Output goes to stdout unless the user explicitly asks for a file. The exception is
  an authorized `pr-lifecycle` run, which also pushes fixes, replies to and resolves
  review threads, and sets the PR's lifecycle label.
