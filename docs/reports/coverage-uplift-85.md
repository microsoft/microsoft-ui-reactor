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

## Baseline (2026-05-17)

| Metric | Coverage |
|---|---|
| **Line**   | **79.52%** (80,690 / 101,473) |
| **Branch** | **67.25%** (32,630 / 48,522) |

Gap to 85% line: ~5,562 additional lines must be covered. (Goal-relative;
the actual delta will be smaller once any dead/genuinely-unreachable code
is excluded with `[ExcludeFromCodeCoverage]`.)

The current ranked gap table (top 40) lives in `coverage/gap-report.md`
and is regenerated by `tools/coverage/report-gaps.ps1`. Rather than
duplicate it here, the table below classifies each top target by the
**test tier required to cover it** — knowing the tier saves the next agent
from re-discovering that, e.g., the `Capture()` path of WindowPlacementCodec
needs an HWND and can't be done in xUnit.

## Hot-spot worklist

Status values:
- **todo** — not started.
- **wip** — claimed; see Status log for who/where.
- **blocked** — investigation revealed a real product issue or design
  question; add a comment in the Status log.
- **done** — coverage now at or above the per-file goal AND tests are real
  (audited).
- **deferred** — covered by something low-value (e.g. COM/WinRT/HWND code
  that can't be tested without standing up a real window) — document why
  and consider `[ExcludeFromCodeCoverage]` to stop dragging the average down.

Test-tier legend:
- **U** = unit test (xUnit, `tests/Reactor.Tests/`)
- **S** = selftest fixture (`tests/Reactor.AppTests.Host/SelfTest/Fixtures/`)
- **U+S** = best ROI splits across both tiers

### Tier-1: Reconciler core (biggest absolute gaps)

| File | Line % | Missed | Tier | Status | Notes |
|---|---:|---:|---|---|---|
| `Core/Reconciler.Update.cs`  | 72.0% | 1696 | S    | todo | 40+ `UpdateXxx` handlers; many element types lack a selftest fixture. Add fixtures that mount, mutate a prop, re-render, assert the WinUI control changed. |
| `Core/Reconciler.cs`         | 71.6% | 1326 | U+S  | todo | Orchestration; `ChildReconciler*` already has good unit tests but `Reconciler.cs` itself (the partial-class orchestrator) has weak coverage. Look at error/edge branches. |
| `Core/Reconciler.Mount.cs`   | 83.3% |  932 | S    | todo | 5 percentage points from done; small selftest additions for the few uncovered element types should be enough. |
| `Core/Reconciler.Gestures.cs`| 32.6% |  438 | S    | todo | Gesture mount/unmount needs a real input source. Tap/Double-tap/Hold via WinAppDriver may be required for the full path. |
| `Core/RenderContext.cs`      | 77.0% |  413 | U    | todo | The hook engine — pure C#. Look for `Use*` paths not exercised. ContextSystem + Memo cells already have tests. |
| `Core/Reconciler.DragDrop.cs`| 31.5% |  300 | U+S  | todo | `BuildDragEndContext` already has unit tests. The drag start/over/leave paths need selftest fixtures. |
| `Core/Navigation/TransitionEngine.cs` | 39.9% | 208 | S | todo | Pure helpers already covered; `RunSlide/Fade/DrillIn/SpringSlide` need a navigation selftest fixture that verifies post-animation visual state. |

### Tier-2: Hosting / app entry (often blocked by Application activation)

| File | Line % | Missed | Tier | Status | Notes |
|---|---:|---:|---|---|---|
| `Hosting/ReactorApp.cs`            | 27.4% | 1251 | S | blocked-ish | `ReactorApp.Run<T>()` cannot be invoked twice per process. Most of this file is the activation/dispatcher bootstrap — tractable only via a dedicated app-host or via `[ExcludeFromCodeCoverage]` on the static-state branches. Audit before testing. |
| `Hosting/ReactorWindow.cs`         | 40.7% |  645 | S | todo | Window lifecycle — covered partially by Window selftests. Look for missing close/move/persist branches. |
| `Hosting/ReactorHostControl.cs`    | 52.8% |  318 | S | todo | Embeddable host — needs a `ReactorHostControl` selftest. |
| `Hosting/ReactorHost.cs`           | 78.0% |  204 | U+S | todo | Already mostly covered; look at render-loop/state-batching edge cases (`HostRenderLoopTests.cs` exists). |
| `Hosting/PreviewCaptureServer.cs`  |  0.0% |  665 | S | deferred? | TCP listener for live preview. Hard to test without standing up the server. **Recommendation: gate with `[ExcludeFromCodeCoverage]` unless someone's actively iterating on the preview pipe.** |
| `Hosting/Persistence/WindowPlacementCodec.cs` | 38.0% | 134 | U | mostly-done | The remaining coverage gap is COM/HWND `Capture` and `SetWindowPlacement`. Unit tests already cover Restore/Plausibility well; the rest is genuinely untestable in xUnit. Consider partial-exclude. |

### Tier-3: Devtools (sizeable, mostly unit-testable)

| File | Line % | Missed | Tier | Status | Notes |
|---|---:|---:|---|---|---|
| `Hosting/Devtools/DevtoolsPropertyTools.cs` | 37.1% | 714 | U | todo | Reflection over element records. Pure logic — high-ROI unit-test target. |
| `Hosting/Devtools/DevtoolsMcpServer.cs`     | 49.9% | 376 | U | todo | MCP request/response shaping; mockable I/O. |
| `Hosting/Devtools/DevtoolsUiaTools.cs`      | 77.1% | 370 | U+S | todo | UIA queries — branch coverage low; widen selector tests. |
| `Hosting/Devtools/DevtoolsTools.cs`         | 69.1% | 300 | U | todo | Tool descriptors / dispatch. |
| `Hosting/Devtools/DevtoolsMenuFactory.cs`   | 30.8% | 148 | U+S | todo | Menu shape — many factory branches uncovered. |
| `Hosting/Devtools/SelectorResolver.cs`      | 60.0% | 132 | U | todo | Pure parser — `SelectorResolverTests.cs` exists; deepen it. |
| `Hosting/Devtools/LogCaptureInstall.cs`     | 58.8% |  98 | U | todo |   |

### Tier-4: Controls (mostly unit-testable)

| File | Line % | Missed | Tier | Status | Notes |
|---|---:|---:|---|---|---|
| `Controls/DataGrid/DataGridComponent.cs`         | 49.3% | 1026 | U+S | todo | `DataGrid*Tests.cs` already cover the state machine; lots of render/measure-time branches uncovered. Likely needs selftest fixtures for column resize, virtualized scrolling, header click. |
| `Controls/Editors/Editors.cs`                    | 29.7% |  294 | U+S | todo | Cell editors. |
| `Controls/PropertyGrid/PropertyGridComponent.cs` | 68.4% |  178 | U+S | todo | `PropertyGrid*Tests.cs` exist; gap is array/dictionary editing paths. |

### Tier-5: Input / Charting / Accessibility

| File | Line % | Missed | Tier | Status | Notes |
|---|---:|---:|---|---|---|
| `Input/DragData.cs`                                  | 41.4% | 292 | U+S | todo | `DragDataTests.cs` covers the typed/eager/lazy round-trips. Gap: `PopulatePackage` (needs `DataPackage` instance — WinRT, see if it activates in unit test) and `TryGetSafeLocalFiles` edge cases (mockable). |
| `Charting/Accessibility/ChartKeyboardNavigator.cs`   | 35.7% | 382 | U   | todo | Pure key/state logic wrapped in a `FuncElement`. Should be testable headlessly. |
| `Charting/Accessibility/ChartAutomationPeer.cs`      |  0.0% | 308 | S   | deferred? | Live UIA peer; needs a real `AutomationPeer` host. Consider `[ExcludeFromCodeCoverage]` if it's stable. |
| `Charting/Accessibility/ChartPointProvider.cs`       | 17.4% | 152 | S   | todo |   |
| `Accessibility/SemanticPanel.cs`                     | 36.8% | 120 | S   | todo | Branch coverage is 2.7% — almost no conditions tested. |

### Tier-6: Shell integration (COM/Win32-heavy)

| File | Line % | Missed | Tier | Status | Notes |
|---|---:|---:|---|---|---|
| `Hosting/Shell/JumpListComInterop.cs`   | 0.0% | 338 | S | deferred? | COM aggregate. `[ExcludeFromCodeCoverage]` is reasonable. |
| `Hosting/Shell/JumpList.cs`             | 46.5% | 200 | U+S | todo | Builder is testable; commit-to-shell is not. |
| `Hosting/Shell/ReactorTrayIcon.cs`      | 58.2% | 164 | S | todo | Already partially covered by selftests. |
| `Hosting/Shell/TrayHiddenWindow.cs`     | 55.9% | 164 | S | todo |   |
| `Hosting/Shell/TrayFlyoutHostWindow.cs` |  0.0% | 230 | S | deferred? | Hidden host window — only meaningful in a real app. |
| `Hosting/Shell/TaskbarOverlay.cs`       |  0.0% |  98 | S | deferred? | Same. |

### Tier-7: Smaller wins

| File | Line % | Missed | Tier | Status | Notes |
|---|---:|---:|---|---|---|
| `Markdown/Md4cParser.cs`        | 80.8% | 132 | U | todo | Already heavily tested by md4c upstream fixtures; the rest is edge cases. |
| `Animation/AnimationHelper.cs`  | 71.5% | 118 | U+S | todo | Pure-math helpers + Composition calls. |
| `Hooks/UseFocusTrap.cs`         |  0.0% | 142 | S | todo | New hook — needs a focus-trap selftest fixture. |
| `Charting/Charts.Tree.cs`       | 78.6% | 120 | U | todo | Layout math — extend `TreeChartsTests.cs`. |
| `Charting/D3Charts.cs`          | 78.1% | 106 | U | todo |   |

---

## Strategy summary (one screen)

1. **Easiest 5% point gain** likely comes from **Reconciler.Mount.cs** (83.3% → 90%+, ~360 lines) and the **devtools cluster** (DevtoolsPropertyTools is 714 missed lines of *reflection over records* — unit-testable).
2. **Highest absolute gain** is **Reconciler.Update.cs / DataGridComponent** — but they need selftest fixtures, not unit tests. Each fixture is more work.
3. **Honest deferrals**: PreviewCaptureServer (665), JumpListComInterop (338), TrayFlyoutHostWindow (230), TaskbarOverlay (98), ChartAutomationPeer (308). These ~1639 lines are inherently hard to test from a headless harness. **Audit each: if the code is stable and well-isolated, mark with `[ExcludeFromCodeCoverage]` so the denominator drops by ~1639 and the percentage moves ~1.6 points toward 85% without writing a single test.** Discuss with the user before doing this in bulk — the user's mandate is "no vanity coverage," and excluding code from the metric is the opposite mistake.
4. **Don't forget branch coverage.** Branch% (67.25%) is far below line% (79.52%) — many tests cover an `if` body but never the else. New tests must vary inputs across decision points.

---

## Vanity-test audit findings

Append-only list. Each entry: file/test, why it is vanity, action taken.

### 2026-05-17 audit — partial sweep of `*Coverage*Tests.cs`

The `tests/Reactor.Tests/` directory has 14 files matching `*Coverage*Tests.cs`
totaling ~5,228 lines, plus `MoreCoverageTests.cs` / `MoreCoverageTests2.cs`
at 749 + 1,085 lines. A focused audit produced these findings:

| File | Verdict | Action |
|---|---|---|
| `SetExtensionsCoverageTests.cs` (345 lines, ~40 tests) | **Vanity.** Each test calls `.Set(x => ...)` and asserts `Setters.Count == 1`. The setter delegate is never invoked, so a regression in the actual property name or the reconciler's invocation of the setter wouldn't be caught. The tests *do* protect against the `.Set()` extension being deleted entirely — but that's a low-value contract. | **Replace.** Rewrite as selftest fixtures that mount the element with `.Set(c => c.IsEnabled = false)` and assert the live WinUI control's `IsEnabled` actually became `false`. That covers the same coverage *and* catches reconciler regressions. Until then, keep the file in place — deleting now would drop coverage without a replacement. |
| `ElementRecordCoverageTests.cs` (91 lines) | **Mixed.** Some asserts are real (record property propagation: `Assert.Equal("Home", sym.Symbol)`). Others are pure vanity (`Assert.NotNull(new ToggleSplitButtonElement("X").Setters)` — `Setters` defaults to `[]`, never null; the assertion can never fail). | **Tighten.** Replace `Assert.NotNull(...Setters)` with assertions on property values that future record-shape changes would actually break. Low priority. |
| `CoverageGapTargetedTests.cs` (1011 lines) | **Mostly real.** Sankey / QuantileScale / D3 edge-case tests with concrete numerical assertions. A few weak ones (`Assert.True(graph.Nodes.All(n => double.IsFinite(n.X0)))` — "doesn't NaN" is a smoke test, not a behavior contract). | Keep as-is for now. When a Sankey assertion fails, deepen the test before fixing the product. |
| `MoreCoverageTests.cs` / `MoreCoverageTests2.cs` (1,834 lines combined) | **Mostly real.** WindowIdAllocator slug rules, NodeRegistry tombstone semantics, McpToolRegistry duplicate-throw, etc. Assertions tie to behavior. | Keep. |
| `ElementExtensionsCoverageTests.cs` (614 lines) | _Not yet audited — pick up here._ |
| `AttachedExtensionsCoverageTests.cs` (106 lines) | _Not yet audited._ |
| Other `*CoverageTests.cs` | _Not yet audited._ |

**Audit philosophy:** an assertion is "real" if you can imagine a product bug
the assertion would catch. `Assert.Single(el.Setters)` after `.Set(x => x.Foo = 1)`
catches nothing — Setters has count 1 after `Set` is called, by definition.
A test that asserts *the actual side effect of the setter on a live control*
catches typos in property names, reconciler delegation regressions, and pooled-
control state leaks. Always reach for the second kind.

---

## Status log

Append-only. Newest at the bottom. Date format `YYYY-MM-DD`.

### 2026-05-17 — bootstrap (machine A)

- Created branch `chore/coverage-uplift-85`.
- Added `tools/coverage/run-coverage.ps1` and `report-gaps.ps1` so the
  workflow is one command per step. Notes:
  - `coverage/` is git-ignored; `tools/coverage/` is now carved out of
    that ignore in `.gitignore`.
  - The script wraps the exact `dotnet-coverage` recipe from `CONTRIBUTING.md`;
    if it ever drifts, treat `CONTRIBUTING.md § Code coverage` as the source
    of truth.
  - `report-gaps.ps1` recomputes branch% from per-line `condition-coverage`
    attributes because dotnet-coverage's cobertura emitter hard-codes the
    root `branch-rate` to 1.
- Ran the baseline: **79.52% line / 67.25% branch**. Confirmed the regression
  below 85%. Full hot-spot table in `coverage/gap-report.md`.
- Filed hot-spot worklist organized by test tier (U/S/U+S) — this is the
  most important section for the next session: it saves you from
  rediscovering which files are unit-testable and which need a real WinUI
  window.
- Audited the suspicious `*Coverage*Tests.cs` files. Big finding:
  `SetExtensionsCoverageTests.cs` is ~345 lines of pure vanity. Plan
  recorded above — replace with selftest fixtures, don't delete pre-emptively.
- **Did not yet add new tests this session.** The session ran out on
  diagnosis + tooling. The next agent should pick the highest-ROI Tier-1
  or Tier-3 entry from the worklist and add real tests.
- **Tips for the next session:**
  - `coverage/merged.cobertura.xml` is the source of truth; reopen
    `coverage/gap-report.md` for the ranked view.
  - When considering a file at the top of the list, **first read the
    existing tests for it** (filename pattern `<Subject>Tests.cs`) — much of
    the easy line coverage is already done; what remains tends to be
    in code paths that need WinUI activation. Don't waste a session trying
    to unit-test something that's inherently host-bound.
  - Run with `-UnitOnly` for fast iteration when you're focused on a single
    unit-testable file (selftest leg takes ~30s and rebuilds the host).
  - Branch% lags far behind line% — every new test should consciously vary
    inputs across `if`/`switch`/`?:` boundaries.
- **Next:** Tier-3 (Devtools) and Tier-1 (Reconciler.Mount.cs polish) are
  the highest-confidence wins. Devtools because it's reflection over
  records (pure C#, mockable); Mount.cs because it's only 5 points from
  the per-file goal and the selftest infrastructure is already there.

### 2026-05-17 — worked-example batch: DevtoolsPropertyTools pure helpers

- Added 11 new tests to `tests/Reactor.Tests/Devtools/DevtoolsPropertyToolTests.cs`
  targeting previously-uncovered branches of `FormatValue`, `ParseValue`,
  `TryParseColor`, `TryParseThickness`, `TryParseCornerRadius`. New tests
  exercise:
  - IFormattable invariant-culture formatting (decimal)
  - 2-value `Thickness` path through `ParseValue` (not just direct `TryParseThickness`)
  - Comma-implies-Thickness branch when targetType is null
  - Generic enum path via `FlowDirection` (distinct from the well-known Visibility/HA/VA arms)
  - Mixed-case bool parsing
  - 8-digit color with A=0x00 (alpha preservation)
  - Lowercase hex color
  - 5-digit and empty hex color rejection
  - Negative Thickness acceptance
  - Discovered behavior: **`TryParseCornerRadius` propagates `ArgumentException`
    when components are negative** (because WinUI's `CornerRadius` ctor
    validates). The new test pins this *as the current contract*; the
    method's `TryParse*` name is misleading and a future fix to catch +
    return false would be an intentional API change.
- All 60 tests in the file pass.
- This batch is intentionally small — a demonstration of the audit-first /
  pin-real-behavior workflow rather than a coverage sprint.
- **Did not re-measure coverage in this session** — the second
  `run-coverage.ps1` invocation would take another 5-10 minutes. The next
  session should re-baseline before claiming a delta. Expectation: marginal
  improvement (these are 11 tests covering ~10-20 lines of branch coverage
  each), but the real value is the worked example.
- **Lesson for next session:** before adding any negative-value test for
  WinUI structs, check whether the struct validates in its ctor. The
  parser swallowing exceptions vs. propagating them is a real product
  decision — make the test pin what you find, then file a follow-up if the
  current behavior is wrong.
