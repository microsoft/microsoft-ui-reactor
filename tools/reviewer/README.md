# Code Review System

Parallelized code review of the Reactor framework using specialized Claude Code agents with an expert C# review pipeline. Findings go through a manager approve/decline workflow, then approved fixes are handed to an AI for implementation.

## Prerequisites

- [Claude Code CLI](https://claude.ai/claude-code) installed and authenticated
- PowerShell 7+ (`pwsh`)

## Quick Start

```powershell
# 1. Run the full review (all 6 agents in parallel, then consolidation)
./tools/reviewer/run-review.ps1

# 2. Manager reviews and approves/declines findings
./tools/reviewer/manager-review.ps1

# 3. Implement approved fixes
./tools/reviewer/apply-fixes.ps1
```

## Directory Structure

```
tools/reviewer/
  README.md               — This file
  manifest.json           — All in-scope files, categories, batch assignments (91 batches)
  run-review.ps1          — Main orchestrator: runs agents in parallel, consolidates
  manager-review.ps1      — Interactive manager workflow: approve/decline each finding
  apply-fixes.ps1         — Hands approved findings to AI for implementation

  expert/                 — Expert C# review pipeline (from agentic.es)
    expert-cs.agent.md              — Orchestrator: 3-stage pipeline definition
    classifier/
      change-classifier.instructions.md — Stage 1: domain signal detection, risk scoring
    expert-routing.instructions.md  — Stage 2: skill selection, risk triggers
    signal-to-noise-gate.instructions.md — Stage 2.5: Team Lead Test, filtering
    risk-assessor/
      risk-assessor.instructions.md — Stage 3: 5-dimension risk model, HITL matrix
    code-review-output-schema.instructions.md — Output contract (JSON schema v2.0)

  skills/                 — Domain skill catalogs (9 skills, 281 patterns total)
    cs-ui-framework.md        — 42 patterns: DependencyProperty, binding, MVVM, lifecycle
    cs-concurrency.md         — 38 patterns: async/await, locks, threading, races
    cs-security.md            — 35 patterns: injection, deserialization, auth, crypto
    cs-performance.md         — 35 patterns: allocations, LINQ, boxing, Span
    cs-memory-lifecycle.md    — 34 patterns: IDisposable, GC pressure, unsafe, pooling
    cs-api-design.md          — 30 patterns: type design, nullability, DI, versioning
    cs-error-handling.md      — 28 patterns: exceptions, null safety, validation
    cs-test-infrastructure.md — 20 patterns: test correctness, reliability, design
    cs-build-packaging.md     — 19 patterns: MSBuild, NuGet, analyzers

  prompts/                — Agent prompt templates (read by orchestrator)
    safety.md             — Thread safety, race conditions, dispatcher, deadlocks
    lifecycle.md          — Dispose, event unsub, memory leaks, mount/unmount
    interop.md            — P/Invoke, COM interop, WinRT, platform assumptions
    security.md           — Server exposure, input validation, injection, deps
    test-quality.md       — Test realism grading (A/B/C/D per file)
    general.md            — Readability, naming, dead code, API surface (runs last)

  reports/                — Generated at runtime (one .md per batch + consolidated fix-list)
```

## How It Works

### Phase 1: Specialist Reviews (parallel)

Five specialist agents run in parallel, each processing their assigned batches:

| Agent | Focus | Skills Used | Batches |
|-------|-------|-------------|---------|
| **safety** | Thread safety, race conditions, dispatcher, shared mutable state | cs-concurrency, cs-ui-framework, cs-memory-lifecycle | 5 |
| **lifecycle** | IDisposable, event cleanup, memory leaks, closure captures | cs-memory-lifecycle, cs-ui-framework, cs-error-handling | 12 |
| **interop** | P/Invoke, COM interop, WinRT projections, marshalling | cs-memory-lifecycle, cs-security, cs-performance | 3 |
| **security** | Server exposure, injection, credentials, dependency audit | cs-security, cs-build-packaging | 4 |
| **test-quality** | Test realism: real coverage vs vanity tests | cs-test-infrastructure | 35 |

Each agent:
1. Reads the expert pipeline docs and its assigned skill files
2. Receives a batch of 5-10 logically related files
3. Can read any file in the repo for additional context
4. Applies the Team Lead Test: only emit findings a senior lead would keep
5. Cites pattern IDs from skill catalogs for every finding

### Phase 2: General Review (after specialists)

The general agent runs after specialists complete. It:
- Receives ALL specialist findings as context
- Does NOT re-report known issues
- Focuses on readability, naming, dead code, error handling, API design
- Catches anything the specialists missed

### Phase 3: Consolidation

All batch reports are merged into a single `reports/fix-list.md`:
- Deduplicated (same file + same lines + same root cause = one finding)
- Sorted by severity (critical first), then priority (P0 first)
- Each finding gets a unique ID (F001, F002, ...)
- Each finding has manager decision and implementation tracking fields

## Manager Workflow

After the review runs, a manager reviews each finding:

```powershell
# Interactive review — prompts for each finding
./tools/reviewer/manager-review.ps1

# Review only critical and high severity
./tools/reviewer/manager-review.ps1 -SeverityFilter high
```

For each finding, the manager can:
- **Approve** — finding is real, should be fixed
- **Decline** — false positive or not worth fixing (with reason)
- **Skip** — defer decision

Decisions are saved directly into `fix-list.md`. The file can also be edited manually in any text editor — just change `_pending_` to `APPROVED` or `DECLINED`.

### Fix-List Finding Format

```markdown
## F001
- **File**: Reactor/Core/Reconciler.cs:145-167
- **Severity**: high
- **Priority**: P1
- **Domain**: concurrency
- **Pattern**: CONC-ASYNC-02
- **Agent**: safety
- **Status**: PENDING
- **Finding**: Task.Result called on UI thread — potential deadlock
- **Evidence**: Line 152 calls .Result inside render loop which runs on dispatcher thread
- **Fix**: Replace with await pattern, ensure caller is async
- **Manager Decision**: _pending_
- **Implementation**: Not started
```

## Implementation Workflow

After manager approval:

```powershell
# Implement all approved findings
./tools/reviewer/apply-fixes.ps1

# Implement a specific finding
./tools/reviewer/apply-fixes.ps1 -FindingId F003

# Dry run — see what would be implemented
./tools/reviewer/apply-fixes.ps1 -DryRun
```

The implementation agent:
- Groups approved findings by file for efficient editing
- Makes minimal, targeted changes (no refactoring beyond what's flagged)
- Updates fix-list.md with completion timestamps
- Reports what changed for each finding ID

## Scope

**Included** (485 files across 91 batches):
- `Reactor/` — Core framework (reconciler, elements, hosting, flex, yoga, animation, markdown, property grid)
- `Reactor.Cli/` — CLI tool
- `Reactor.Localization.Generator/` — Source generator
- `ReactorCharting/` — D3 visualization library
- `vscode-reactor/` — VS Code extension
- `tests/` — All test projects (reviewed for quality, not just correctness)
- Build config (`Directory.Build.props`, `*.csproj`, `*.sln`)

**Excluded**:
- `samples/` — Sample apps
- `docs/` — Documentation markdown
- `selfhost/` — Runtime config files
- `tests/Reactor.Tests/YogaGenerated/` — Auto-generated upstream test fixtures
- `tests/Reactor.Tests/Md4cGenerated/` — Auto-generated CommonMark spec tests
- Binary/image/config files

## Advanced Usage

```powershell
# Run a single batch
./tools/reviewer/run-review.ps1 -Batch safety-batch-1

# Increase parallelism (default: 4)
./tools/reviewer/run-review.ps1 -MaxParallel 8

# Re-consolidate without re-running agents
./tools/reviewer/run-review.ps1 -ConsolidateOnly

# Dry run — see all batches without invoking agents
./tools/reviewer/run-review.ps1 -DryRun
```

## Expert Pipeline

Each agent follows the expert C# code review pipeline:

1. **CLASSIFY** — Detect language, identify domain signals, compute risk score
2. **ROUTE** — Select domain skills based on risk thresholds and triggers
3. **ANALYZE** — Apply selected skills, cite pattern IDs for every finding
4. **GATE** — Apply signal-to-noise filter (Team Lead Test, confidence check)
5. **SYNTHESIZE** — Produce structured findings

Every finding must:
- Cite a pattern ID from the skill catalogs (no uncited observations)
- Include confidence justification (evidence-graded, not self-reported feeling)
- Pass the Team Lead Test: "Would a senior lead keep this or delete it as noise?"
- Follow severity auto-escalation rules (e.g., `BinaryFormatter` = always critical)
