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
| [`perf-compare/`](perf-compare/SKILL.md) | Benchmark the `StressPerf.ReactorOptimized` data-grid harness on the current branch vs a clean `main` worktree (interleaved, same machine) and report a direction-aware delta for the four headline metrics. The local equivalent of commenting `/perf` on a PR. Reports to stdout; does not apply fixes. |

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
- Output goes to stdout unless the user explicitly asks for a file.
