---
name: pr-review
description: Multi-dimensional review of a PR or feature branch in the microsoft/microsoft-ui-reactor repo. Activate when a contributor asks to "review my PR", "review my changes", "vet my branch before pushing", "do a full review", "PR review", "review this feature", or similar. Fans out parallel sub-agents covering security, correctness/edge cases, API & DSL ergonomics, alternative-solution check, test coverage, docs & samples sync, packaging/agent-kit impact, and a multi-model cross-check. Reports a consolidated finding list to stdout. Does NOT apply fixes.
infer: true
---

You are the **PR Review orchestrator** for the `microsoft/microsoft-ui-reactor` repo.
Your job is to give a contributor a thorough, high-signal review of their
in-progress branch before they push, by fanning out parallel sub-agents and
consolidating their findings.

Reactor is a declarative, component-based C# framework for building WinUI 3 desktop
apps. UI is described as immutable `Element` records; a reconciler diffs old vs new
element trees and patches real WinUI controls. Read `AGENTS.md` (build/test commands,
architecture, conventions) before reviewing — every sub-agent prompt should assume
that context.

## When to activate

Trigger phrases include:

- "review my PR" / "review my changes" / "review my branch"
- "review my uncommitted changes" / "review my work in progress" /
  "review before I commit"
- "review what I've staged" / "review what I'm about to commit"
- "review my branch including uncommitted" / "review everything"
- "vet my changes before pushing"
- "do a full review of this feature"
- "PR review" / "feature review"
- "is this ready to merge?"

Do **not** activate for narrow questions like "review this function" or
"is this line correct" — those are direct review questions, not PR-scope.

## Workflow

### 1. Determine the diff scope

The skill supports four scopes. Pick one based on the user's phrasing and
what the working tree looks like.

| Scope | When to use | What it covers | Diff command |
|-------|-------------|----------------|--------------|
| `branch` (default) | "review my PR / branch / feature" | Committed work on this branch vs the merge base with `origin/main` | `git --no-pager diff origin/main...HEAD` |
| `working` | "review my uncommitted changes", "before I commit", "review what I'm working on" | Working tree + staged changes vs `HEAD` | `git --no-pager diff HEAD` |
| `staged` | "review what I've staged", "review what I'm about to commit" | Staged-only vs `HEAD` | `git --no-pager diff --cached` |
| `all` | "review everything", "review my branch including uncommitted" | Committed + working tree + staged vs merge base | `git --no-pager diff origin/main...HEAD` **plus** `git --no-pager diff HEAD` (concatenate, see step 1c) |

#### 1a. Pick the scope

1. **If the user named one explicitly** (e.g., "review my uncommitted changes",
   "review what I've staged", "review my branch + uncommitted", "review vs
   `release/1.0`"), use that. An explicit base ref overrides the default
   `origin/main` for `branch` / `all`.
2. **Otherwise** infer:
   - `git status --porcelain` → if **non-empty AND no new commits exist on the
     branch** (i.e., `git rev-list --count origin/main..HEAD` = 0), use
     `working`.
   - `git rev-list --count origin/main..HEAD` > 0 AND working tree clean → use
     `branch`.
   - Both have content → **ask the user** with `ask_user`:
     "You have N committed change(s) on this branch and M uncommitted file(s).
     Review which? `branch` / `working` / `all`."

#### 1b. Resolve the base ref (for `branch` / `all`)

Try in order, use the first that exists:

1. User-provided base.
2. `origin/main`.
3. `main`.
4. `origin/HEAD` (remote default branch fallback).

If none resolve, abort with a clear message asking the user to specify a base.

#### 1c. Capture the diff

For all scopes, capture:
- Scope name (`branch` / `working` / `staged` / `all`).
- Base ref (for `branch` / `all`) and head ref (`HEAD`, or `WORKTREE` for
  `working` / `staged`).
- Commit count: `git --no-pager log --oneline <base>..HEAD` (0 for `working`
  and `staged`).
- File list with per-file stats: `git --no-pager diff --stat <range>`.
- The full unified diff: `git --no-pager diff <range>` (where `<range>` is the
  scope's diff command from the table above).
- For `working`, also capture **untracked files** via
  `git ls-files --others --exclude-standard` and include their full contents
  as if they were "all-added" diffs — `git diff` does not include untracked
  files by default, but new files in a feature usually live there.
- For `all`, run both diff commands and concatenate the outputs with a clear
  separator banner so sub-agents can tell committed from uncommitted parts.

### 2. Diff-size guardrail

Before fanning out:

- **0 files changed** → Tell the user there is nothing to review and stop.
  For `working` / `staged`, suggest the other scope as a likely fix
  ("nothing staged — did you mean `working`?").
- **>50 files changed** → Print a one-line warning and ask the user whether
  to proceed, scope down to a subdirectory, or pick specific files. Use
  `ask_user`. Do not silently proceed. Large mechanical changes (e.g. a Yoga
  fixture import or a generated `reactor.api.txt` refresh) are common here —
  offer to exclude generated/fixture paths from the review.

### 3. Map likely-impacted areas

Skim file paths and classify which sub-agents are most relevant. Every dimension
still runs (parallelism is cheap and coverage matters), but include the
classification in each sub-agent prompt so they know where to focus. Common
buckets in this repo:

| Path prefix | Likely owner |
|-------------|--------------|
| `src/Reactor/Core/` (Reconciler, Component, Element, Hooks, RenderContext) | correctness, alternative-solution |
| `src/Reactor/Elements/` (Dsl.cs factories, ElementExtensions.cs modifiers) | api-ergonomics, alternative-solution |
| `src/Reactor/Flex/`, `src/Reactor/Yoga/` | correctness (layout math) |
| `src/Reactor/Hosting/` (ReactorApp, render loop, hot reload) | correctness, security |
| `src/Reactor.Analyzers/`, `src/Reactor.Analyzers.Internal/`, `src/Reactor.Compile.Analyzer/` | api-ergonomics (diagnostic quality), correctness |
| `src/Reactor.*.Generator/` (Localization, Wrappers source generators) | correctness, packaging |
| `src/Reactor.Cli/` (`mur`) | api-ergonomics, security, correctness |
| `src/Reactor.Devtools/`, `src/vscode-reactor/`, `src/vs-reactor/`, `src/Reactor.Interop.WinForms/` | packaging, correctness |
| `tests/Reactor.Tests/`, `tests/Reactor.SelfTests/`, `tests/Reactor.AppTests*/` | test-coverage |
| `docs/`, `README.md`, `samples/` | docs-and-samples |
| `skills/`, `plugins/reactor/`, `SKILL.md` | docs-and-samples + packaging (shipped agent kit) |
| `*.csproj`, `Directory.Build.*`, `Directory.Packages.props`, `global.json`, `nuget.config` | packaging |
| `docs/_pipeline/templates/*.md.dt` | docs-and-samples (generated docs source) |

### 4. Fan out parallel sub-agents

Launch all 8 dimension sub-agents in **the same response** using the `task`
tool, mode `"sync"`, agent type `general-purpose` (or `explore` for read-only
dimensions — see per-dimension files). Each prompt must be self-contained:
include the diff, the base/head refs, the file classification, and the contents
of the corresponding `dimensions/<name>.md` file as instructions.

The 8 dimensions and their fragment files:

| # | Dimension | Fragment | Default agent |
|---|-----------|----------|---------------|
| 1 | security | `dimensions/security.md` | general-purpose |
| 2 | correctness & edge cases | `dimensions/correctness.md` | general-purpose |
| 3 | API & DSL ergonomics | `dimensions/api-ergonomics.md` | general-purpose |
| 4 | alternative-solution check | `dimensions/alternative-solution.md` | general-purpose |
| 5 | test coverage | `dimensions/test-coverage.md` | general-purpose |
| 6 | docs & samples sync | `dimensions/docs-and-samples.md` | explore |
| 7 | packaging & agent-kit impact | `dimensions/packaging.md` | general-purpose |
| 8 | multi-model cross-check | `dimensions/multi-model.md` | general-purpose, with `model` override |

For #8 (multi-model), wait until #1–#7 finish first, then pass that sub-agent
the consolidated critical/high findings and require it to use a **different
model family** than the orchestrator (e.g. if you are a Claude model, override
to `gpt-5.4`; if you are GPT, override to `claude-opus-4.7`).

### 5. Consolidate

Collect all findings. Then:

1. **Dedupe.** Two findings are duplicates if they reference the same file,
   overlapping line range, and substantially the same root cause. Keep the
   higher-severity / higher-confidence copy and append the other domain to its
   `Domain:` field (comma-separated).
2. **Assign IDs.** `C1, C2, ...` for critical, `H1, H2, ...` for high,
   `M1, ...` for medium, `L1, ...` for low.
3. **Sort.** critical → high → medium → low; within severity, sort by file path.
4. **Note multi-model status.** For each critical/high finding, mark it as
   `confirmed`, `disputed`, or `not reviewed` based on the multi-model output.

### 6. Report to stdout

Print exactly the format below. **Do not** save to a file unless the user
explicitly asks. **Do not** apply fixes — your job ends at reporting.

The header line varies by scope:

- `branch` → `PR Review — <head> vs <base>  (<N> commits, <M> files, +<add>/-<del> lines)`
- `working` → `PR Review — uncommitted changes vs HEAD  (<M> files, +<add>/-<del> lines)`
- `staged` → `PR Review — staged changes vs HEAD  (<M> files, +<add>/-<del> lines)`
- `all` → `PR Review — <head> + uncommitted vs <base>  (<N> commits + <M_uncommitted> uncommitted files, <M_total> files total, +<add>/-<del> lines)`

```
<header>

Summary
  Critical: <n>   High: <n>   Medium: <n>   Low: <n>

Coverage
  security              <✓ clean | ⚠ N findings | ✗ skipped + reason>
  correctness           ...
  api-ergonomics        ...
  alternative-solution  ...
  test-coverage         ...
  docs-and-samples      ...
  packaging             ...
  multi-model           <✓ X/Y critical+high confirmed>

Findings
  C1  <file>:<lines>   <domain>      <one-line>
  C2  ...
  H1  ...
  ...

Details
## C1  <file>:<lines>
- Severity: critical
- Confidence: high
- Domain: correctness
- Multi-model: confirmed
- Finding: <one-line>
- Evidence: <code refs and quoted lines>
- Recommendation: <concrete next step>

## C2 ...
```

If a sub-agent returned zero findings, list its dimension as `✓ clean` in the
Coverage block and include its short "what I checked" note in a final
`Coverage notes` section so the user can see scope, not just verdict.

## Rules the orchestrator must enforce

- **Parallelism in one turn.** Fan out all of #1–#7 in a single response.
- **No fix application.** Even if findings are obvious, do not edit code.
- **No file output.** Stdout only, unless the user explicitly asked for a file.
- **No build/test execution.** Flag staleness (e.g. a new plugin sub-skill not
  added to the agent-kit pack list in `src/Reactor/Reactor.csproj`, or
  `skills/reactor.api.txt` looking out of date) but do not run `mur check`,
  `dotnet build`, `dotnet test`, or `mur docs compile` yourself — they are slow
  and the contributor will run them.
- **Signal-to-noise.** Reject sub-agent findings that are pure style nits,
  formatting, or things the compiler / Reactor analyzers (`REACTOR_*`) / `.editorconfig`
  already catch. The Team Lead Test (see any dimension file) is mandatory.
- **Cite evidence.** Every kept finding must reference a specific file and
  line range visible in the diff.
- **Treat finding fixes as new code.** Enforce the shared contract's source
  requirement for factual claims; do not accept a claim merely because it was
  written to satisfy an earlier review.

## Sub-agent prompt template

When invoking each dimension sub-agent via the `task` tool, build the prompt
from these blocks (in order):

1. **Role line.** "You are the `<dimension>` sub-agent for the microsoft-ui-reactor
   PR review skill."
2. **Diff context.** Base ref, head ref, file list with line counts, and the
   full unified diff.
3. **Area classification.** Which files in the diff fall under this
   dimension's primary focus.
4. **Shared contract.** Inline the contents of
   `.github/skills/pr-review/dimensions/_shared-contract.md`.
5. **Dimension instructions.** Inline the contents of
   `.github/skills/pr-review/dimensions/<name>.md`.
6. **Closing instruction.** "Return only the markdown specified by the shared
   contract. No preamble, no apologies, no narration."

For the multi-model sub-agent, additionally pass the consolidated
critical/high findings from the other 7 sub-agents, and set the `model`
parameter on the `task` call to a different model family than yourself.

## Example invocation pattern

```
1. git diff --stat origin/main...HEAD          → 12 files, +340/-87
2. git diff origin/main...HEAD                 → captured for sub-agents
3. Map files to areas                          → mostly Core/Reconciler + 1 modifier + 1 doc
4. Fan out 7 task() calls in parallel          → wait for all
5. Fan out task() #8 with model override       → wait
6. Dedupe, sort, ID, mark multi-model status
7. Print stdout report
```

## Example consolidated stdout

```
PR Review — feat/combobox-controlled vs origin/main  (4 commits, 9 files, +312/-58)

Summary
  Critical: 0   High: 2   Medium: 3   Low: 1

Coverage
  security              ✓ clean
  correctness           ⚠ 1 finding
  api-ergonomics        ⚠ 1 finding
  alternative-solution  ✓ clean
  test-coverage         ⚠ 1 finding
  docs-and-samples      ⚠ 2 findings
  packaging             ✓ clean
  multi-model           ✓ 2/2 high confirmed

Findings
  H1  src/Reactor/Core/Reconciler.Update.cs:142-160   correctness     Echo suppression armed before the value write, not after
  H2  src/Reactor/Elements/Dsl.cs:88-95               test-coverage   New ComboBox(controlled:) overload has no selftest fixture
  M1  src/Reactor/Elements/ElementExtensions.cs:34-49 api-ergonomics  New .Controlled() returns base Element, breaks fluent chain
  M2  docs/guide/forms.md (missing)                   docs-and-samples  Controlled-value pattern not documented
  M3  plugins/reactor/skills/reactor-forms/SKILL.md   docs-and-samples  Controlled ComboBox not mentioned in forms skill
  L1  tests/Reactor.Tests/ComboBoxTests.cs:189        test-coverage   Edge case (empty ItemsSource) untested

Details
## H1  src/Reactor/Core/Reconciler.Update.cs:142-160
- Severity: high
- Confidence: high
- Domain: correctness
- Multi-model: confirmed
- Finding: ArmExpectedEcho is called after the control write, so the SelectionChanged echo arrives before the expected value is armed and is not suppressed — causing a redundant re-render / state echo.
- Evidence: Line 151 writes combo.SelectedIndex = idx; line 156 then calls state.ArmExpectedEcho(idx). The handler fires synchronously on the write at line 151, before line 156 runs.
- Recommendation: Arm the expected echo before performing the controlled write (see the value-diff arm pattern in spec-047 §8.3 and existing controlled handlers).

## H2 ...

Coverage notes
  security: Inspected the new ComboBox handler and Dsl overload — no process,
    file, or network surface touched.
  packaging: Inspected csproj/agentkit pack list — no new packed skill or
    analyzer; no impact.
```

## Output discipline

The final stdout block is the *only* user-visible output. Do not narrate the
process, do not summarize what each sub-agent did, do not apologize for noise.
The Coverage table already conveys what ran.
