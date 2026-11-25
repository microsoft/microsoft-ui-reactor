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

Evidence refreshed **2026-11-24**. Keep model IDs and selection rationale here,
not in individual prompt fragments. These are task-fit candidates, not validated
per-dimension defaults. The [2026-11-05 single-PR comparison](model-evaluation.md#2026-11-05-single-pr-comparison)
favored GPT-6.1 Sol over Astra, Opus and Sonnet. The
[2026-11-24 multi-PR comparison](model-evaluation.md#2026-11-24-multi-pr-comparison)
then ran GPT-6.2 Sol and Gemini 4 Carbon over four pre-fix PR snapshots and an
intended clean control, two reviews each. Both found 16 of 30 reference defect
opportunities. Sol did it at about one-seventh the credits and one-twenty-fifth
the wall time (`medium` effort, concurrent runs), while Carbon found defects
Sol missed in three of the five snapshots, including a regression in the
control. Ten reviews per model (eight on the labeled snapshots, two on the
control) are still a small sample, not a repository-wide ranking.

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
| **Measured correctness-review candidate / evaluation baseline** | `gpt-6.2-sol` | Found 16/30 reference defect opportunities across four PRs at about 33 credits and 70 s per review, with the same labels in both reviews of every case at the lowest measured cost. Replaces `gpt-6.1-sol` (same API price, released 2026-11-17), but the two were not compared head-to-head. Raised no supported findings on the intended clean control, which turned out to contain a real regression. Also a different-family candidate after a Claude or Gemini first pass. |
| **Third-family cross-check candidate** | `gemini-4-carbon` | Matched Sol's 16/30 and found defects Sol missed on three of five snapshots, including the control's regression in both runs. It costs about 219 credits and 31 minutes of wall time per review at `medium` effort, so use it where a different family matters, not as a primary fan-out default. One of eleven runs (in calibration) ignored the JSON output contract. |
| **Deep-review challenger / Claude cross-check candidate** | `claude-opus-5.5` | Found a documentation gap 6.1 Sol missed in the 2026-11-05 case, but most reference runtime bugs escaped its reviews. Not re-measured on 2026-11-24. Available for complex judgment and a permitted different-family check after OpenAI; not a blanket deep-review default. |
| **Bounded-review candidate** | `claude-sonnet-5.5` | Candidate for docs/sample synchronization and straightforward inventories. No reference hits in the 2026-11-05 combined-review case; focused bounded tasks remain unmeasured. Do not infer correctness, security or AOT-review suitability from price or vendor positioning. |
| **Unmeasured comparator** | `gpt-6.2-astra` | Successor to `gpt-6-astra`, which found one known bug per repeat on 2026-11-05 at several times Sol's cost. Not measured here. It is not a different-family check after Sol. |
| **Mechanical workflows** | Inherit the configured model; no override | Running the committed perf script, reading CI state, and lifecycle bookkeeping do not need a frontier-model fan-out. Nontrivial fixes still warrant deep reasoning. |

The [PR review dimension table](pr-review/SKILL.md#4-fan-out-the-primary-dimensions)
maps these workloads to each review lens as hypotheses. The other skills
reference this same policy; none needs its own aging model list.

### Cross-family checks

Choose relative to the **actual first-pass reviewer or test author**, not merely
the orchestrator. Opus and Sonnet are both Claude; changing between them, or
between two GPT versions, is not family diversity. When a cross-check model is
yours to pick: after Claude reviewers prefer `gpt-6.2-sol`; after GPT
reviewers choose a Claude or Gemini candidate. Reach for `gemini-4-carbon`
when the reviewers already span Claude and GPT, or when the extra wall time
and credits are acceptable for the change at hand. When the orchestrator does
the first-pass work itself (pr-review never does), it is the first-pass reviewer.

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
| [OpenAI: GPT-6.2 Astra and Sol, 2026-11-17](https://openai.com/index/introducing-gpt-6-2-astra-sol/) | Keeps GPT-6.1 Sol's API token prices. Reports 6.2 Sol within 0.5 points of 6.2 Astra on FrontierCode 2.0 at about one-fifth the cost (both at max), and 5.3 points above 6.1 Sol on GDPval v2. | A Reactor review win over 6.1 Sol. Our 2026-11-24 run measured 6.2 Sol without a 6.1 arm. |
| [Google: choosing a Gemini model](https://aistudio.google.com/docs/choosing-a-model) | Positions Gemini 4 Carbon as the strongest, most deliberate reasoning tier (above Neon and Hydrogen), for multi-file debugging and security audits. | Review speed: Carbon was by far the slowest reviewer we measured. |
| [GitHub: supported models](https://docs.github.com/en/copilot/reference/ai-models/supported-models) | Lists supported options; availability varies by client, plan, and policy. Claude Fable 5.2 is listed GA but needs separate enterprise enablement and was not exposed in the 2026-11-24 host. | Access in the current session, or permission to override its configured model. |

Keep benchmark versions, harnesses, effort settings, safeguards, and budgets
attached to scores. For example, [Vals' Terminal-Bench 4.0 methodology](https://www.vals-ai.com/benchmarks/terminal-bench-4)
uses mini-swe-agent, 66 tasks, and three pass@1 runs; those results are not
interchangeable with a vendor harness or a different Terminal-Bench version.
Missing or incomparable results are **unknown**, not evidence of inferiority.

Before promoting a future default, refresh the availability and source dates.
For a Reactor-specific ranking, compare candidates on the same fixed diffs,
context, tools, and budget, covering all seven review dimensions and clean
negative cases. Have maintainers adjudicate confirmed defects, false positives,
missed defects, severity, latency, and actual cost. The
[evaluation notes and procedure](model-evaluation.md) describe the local evidence
and how to repeat a comparison with existing host tools. They measure within-case repeatability,
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
