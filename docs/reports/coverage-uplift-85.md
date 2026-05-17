# Coverage uplift to 85% (tracking)

**Branch:** `chore/coverage-uplift-85` (push to `origin` after every iteration)
**Started:** 2026-05-17
**Owner:** rotating — multi-machine, multi-session.
**Goal:** Restore merged (unit + selftest) **line coverage to ≥ 85%** on the
product DLL (`Reactor.dll`), without juicing the metric.

> **Quality bar.** A real test asserts a behavior or invariant that would fail
> if the product code regressed. Smoke tests that only call a factory and
> assert it returned non-null do **not** count, and existing ones should be
> deleted or rewritten when we find them. We would rather ship 70% honest
> coverage than 85% vanity coverage.

---

## How to pick up this work (read first)

You are in the middle of an iterative effort. Everything you need is in this
repo — no external state.

### 1. Sync the branch

```pwsh
git fetch origin
git checkout chore/coverage-uplift-85
git pull --ff-only
```

If the branch was deleted upstream because the uplift PR merged, this work is
complete — check `git log origin/main` for the merge commit.

### 2. Read this doc end-to-end

The **Status log** at the bottom is append-only. Each entry shows the
machine, the date, what was tried, the resulting numbers, and any
unblocked-but-not-finished follow-ups.

### 3. Reproduce the current number

```pwsh
# One-shot wrapper around the CONTRIBUTING.md recipe
pwsh tools/coverage/run-coverage.ps1

# Hot-spot ranking (writes coverage/gap-report.md too)
pwsh tools/coverage/report-gaps.ps1
```

If those scripts disappear, the canonical recipe is in
`CONTRIBUTING.md § Code coverage`. Re-create the scripts from there.

### 4. Pick the next hot spot

Take the top entry in `coverage/gap-report.md` that isn't already marked
**done** or **blocked** in the [Hot-spot worklist](#hot-spot-worklist) below.
Add real tests for it, re-measure, append to the [Status log](#status-log).

### 5. Commit and push every iteration

```pwsh
git add -A
git commit -m "test(coverage): <area> — <delta>"
git push -u origin chore/coverage-uplift-85
```

Even a small +0.3% commit is worth pushing so the next session does not
re-do the same work.

---

## Reference: the coverage recipe

Verbatim from `CONTRIBUTING.md`, captured here so this doc is self-contained.

```pwsh
# (install once: dotnet tool install -g dotnet-coverage)

# --- Unit tests ---
dotnet build tests/Reactor.Tests -c Debug -p:Optimize=false -p:DebugType=portable
dotnet-coverage collect -s coverage.settings.xml `
  --output unit.cobertura.xml --output-format cobertura `
  -- dotnet test tests/Reactor.Tests --no-build

# --- Selftest ---
# 1. Rebuild product + host with portable PDBs
dotnet build src/Reactor                 -c Debug -p:Optimize=false -p:DebugType=portable --no-incremental
dotnet build tests/Reactor.AppTests.Host -c Debug -p:Optimize=false -p:DebugType=portable --no-incremental

# 2. Statically instrument Reactor.dll inside the host bin folder.
#    Dynamic instrumentation skips referenced assemblies.
dotnet-coverage instrument `
  "tests/Reactor.AppTests.Host/bin/<RID>/Debug/net10.0-windows10.0.22621.0/Reactor.dll" `
  -s coverage.settings.xml

# 3. Collect
dotnet-coverage collect -s coverage.settings.xml `
  --output selftest.cobertura.xml --output-format cobertura `
  -- dotnet run --project tests/Reactor.AppTests.Host --no-build -- --self-test

# --- Merge ---
dotnet-coverage merge unit.cobertura.xml selftest.cobertura.xml `
  --output merged.cobertura.xml --output-format cobertura
```

`coverage.settings.xml` already restricts the measurement to `Reactor.dll`
and excludes generated code (`obj/`, `*.g.cs`) and
`[ExcludeFromCodeCoverage]` members.

### Why the line% may differ from branch%

`dotnet-coverage`'s cobertura emitter sets root `branch-rate="1"` regardless
of reality. The CI workflow (`.github/workflows/coverage.yml`) recomputes
branch% from per-line `condition-coverage` attributes; `report-gaps.ps1`
does the same. Trust the script output, not the cobertura header.

---

## Operating principles

1. **No vanity tests.** Each new test must contain assertions tied to
   observable product behavior. A test that just calls a factory and asserts
   non-null is not enough — assert the property values that matter for the
   element's contract.
2. **Prefer unit tests, then selftests.** E2E tests do not contribute to the
   measured coverage. Per the test-tier table in `AGENTS.md`, prefer unit
   tests (headless xUnit) for everything that does not need a real WinUI
   control. Use selftest fixtures only when you genuinely need to observe a
   mounted control's behavior.
3. **Audit before you add.** When a file's line% is low, first read the
   *existing* tests for it. If they're shallow (no asserts, or asserts that
   don't tie to the uncovered paths), strengthen them before writing new
   ones. Document any deletions in the Status log so future agents know why
   the count moved.
4. **Iterate on a single area at a time.** Don't open ten partial
   directions. Pick one file or subsystem, get it from X% → high-X%, commit,
   push, move on. The Status log is a series of small wins.
5. **Re-measure after every commit.** Coverage moves in non-obvious ways
   when you delete tests, refactor product code, or add fixtures that
   accidentally exercise neighboring paths.
6. **Don't change product behavior for coverage.** Bug fixes that happen to
   bring formerly-dead code under test are fine; refactors that delete
   uncovered code "for coverage" must be discussed with the user first.
7. **Selftest fixtures have to be registered.** New fixtures go in
   `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`, get registered in
   `SelfTestFixtureRegistry`, and produce a `[TestMethod]` in
   `SelfTestBatch`. Forgetting the registration step means the fixture
   compiles but does not run.
8. **Console-mutating unit tests need `[Collection("ConsoleTests")]`** to
   avoid cross-test interference.
9. **Match the existing naming.** Files are `XyzTests.cs` for unit tests
   and `Xyz_<scenario>` fixture-id strings for selftests. Use the same
   conventions as neighboring tests so reviewers don't push back on style.

---

## Hot-spot worklist

Filled in after the first baseline run completes. Each row is one
prioritized target; update the **Status** column as you go.

| Priority | File / area | Baseline line % | Current line % | Status | Notes |
|---|---|---:|---:|---|---|
| _to fill on first baseline_ |   |   |   |   |   |

Status values:
- **todo** — not started.
- **wip** — claimed; see Status log for who/where.
- **blocked** — investigation revealed a real product issue or design
  question; add a comment in the Status log.
- **done** — coverage now at or above the per-file goal AND tests are real
  (audited).
- **deferred** — covered by something low-value (e.g. error logging
  branches that aren't worth fixturing); document why and move on.

---

## Vanity-test audit findings

Append-only list. Each entry: file/test, why it is vanity, action taken.

_(none yet — to be filled as audits happen)_

---

## Status log

Append-only. Newest at the bottom. Date format `YYYY-MM-DD`.

### 2026-05-17 — bootstrap (machine A)

- Created branch `chore/coverage-uplift-85`.
- Added `tools/coverage/run-coverage.ps1` and `report-gaps.ps1` so the
  workflow is one command per step.
- Wrote this tracking doc.
- **Next:** run baseline coverage and populate the Hot-spot worklist.
