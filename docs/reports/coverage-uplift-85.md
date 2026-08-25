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

### 2026-05-17 audit — `Controls/TypedEditorsTests.cs`

The 217-line file (17 tests) attached to the spec-030 typed-editor work. Found
**heavy vanity** — most tests assert `Assert.NotNull(factory)` against a
delegate-returning method that, by C# language semantics, cannot return null.
Specific findings:

| Test | Verdict | Action |
|---|---|---|
| `Bool_Standard_Editor_Is_Distinct_From_Compact` | **Real** — asserts `Assert.NotSame(standard, compact)`. A regression collapsing them into one delegate would fail. | Keep. |
| `Registry_Resolves_Editor_For_Primitive` (Theory × 7) | **Vanity** — `Assert.NotNull(r.ResolveEditor(...))` cannot fail because the resolver returns a Func. | Strengthen: invoke the resolved factory with a typed value and assert the returned Element's shape (see `EditorsBehaviorTests` for the pattern). Until then, supersedes are in place. |
| `Registry_Falls_Back_To_Builtin_CellRenderer` (Theory × 7) | **Vanity** — same shape. | Same action. |
| `Explicit_CellRenderer_Registration_Wins_Over_Fallback` | **Real** — `Assert.Same(custom, resolved)`. | Keep. |
| `DataType_Url_On_String_Sets_Hyperlink_Renderer` | **Mixed** — asserts NotNull on Editor + CellRenderer. The real assertion would be: invoke the resolved CellRenderer with a Uri and confirm it renders a Hyperlink element. | Tighten. |
| `Range_On_Numeric_Sets_Editor` | **Vanity** — Editor is non-null trivially. The real assertion is that the Range attribute wired Minimum/Maximum on the NumberBox. | Tighten. |
| `Plain_String_Has_No_Attribute_Editor` | **Real** — asserts both Editor and CellRenderer are null. | Keep. |
| `Number_Editor_For_Int_Returns_Factory` | **Vanity** — the comment in the test acknowledges this ("we can't exercise the onChange path end-to-end here"). This is now wrong — `EditorsBehaviorTests.Number_Int_OnValueChanged_Returns_Int_Not_Double` exercises that path against the record callbacks. | **Delete** (superseded). Doing in a follow-up to keep the current commit focused on additions. |
| `Number_Editor_Decimal_Min_Max_Accept_Decimal_Literals` | **Vanity** — proves the signature compiles, not that the literals reach the NumberBox. | Tighten or delete (superseded by `Number_Min_Max_Set_When_Provided`). |
| Typed column factories (`NumberColumn_...`, `ToggleSwitchColumn_...`, `ColorColumn_...`, `ComboBoxColumn_...`, `DateColumn_...`, `HyperlinkColumn_...`) | **Vanity** — every test only asserts `Editor` and `CellRenderer` are non-null. Should instead invoke the wired editor and assert the inner Element shape (e.g. NumberColumn's Editor returns a NumberBox with the configured min/max; ToggleSwitchColumn returns a ToggleSwitch not a CheckBox). | Tighten — the new harness in `EditorsBehaviorTests` shows how. |

**Did not delete the existing vanity tests in this commit.** Doing so would
drop coverage of the type-registration code paths without a replacement
(the existing tests at least exercise `ResolveEditor` / `GetCellRenderer`,
which the new `EditorsBehaviorTests` does not — the new file tests the
factory catalog directly). Next-session candidate: rewrite
`TypedEditorsTests.cs` to invoke the resolved factory and assert shape,
which would both raise the bar AND cover the type-registration paths
end-to-end.

### 2026-05-17 audit — `TreeChartsTests.cs`

351-line file (43 tests) for the TreeChart / ForceGraph fluent builders and
their IChartAccessibilityData implementations. Mixed:

| Pattern | Count | Verdict |
|---|---|---|
| `Same(chart, chart.Foo(...))` fluent-return assertions | ~20 tests | **Vanity.** Each setter is `Foo(x) { _foo = x; return this; }`. `return this` is a language-level guarantee for the chosen pattern; the test can only fail if a deliberate redesign rewrites the body to `return new TreeChartElement<T>(...)`. That's not a regression, it's an explicit API change — and the test would be expected to be updated alongside. |
| `chart.Width(1000)…; Assert.NotNull(chart)` (fluent-chaining) | ~2 tests | **Vanity.** Returning non-null is also a language guarantee. |
| IChartAccessibilityData property reads (`Name`, `Description`, `ChartTypeName`, `Series.Count`, depth values, etc.) | ~13 tests | **Real.** These hit `BuildElement`-internal state via the IChartAccessibilityData getters and assert observable values. |

**Constraint preventing rewrite this iteration:** the fluent setters store
into `private` fields (`_width`, `_height`, `_linkColor`, …); only the
IChartAccessibilityData getters expose state, and those expose only the
ChartSeriesDescriptor (label + depth) shape — not _width/_height. The
strongest replacement would test that `BuildElement` produces a Canvas
with the configured width/height, but `BuildElement` calls
`Brush(_linkColor)` which constructs a `SolidColorBrush` — **host-bound,
same trap as the ColorCompact swatch in the Editors iteration.** A
selftest fixture is the right home for the BuildElement assertions; the
fluent-setter coverage they deliver is a side-effect that the existing
tests already get with the `return this` pattern.

**Recommendation:** keep TreeChart fluent-setter tests as-is until a
selftest fixture replaces them with mount-time assertions on the rendered
Canvas. The fluent-return assertions are weak but not actively misleading —
they at least exercise the setter line.

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

### 2026-05-17 — LogCaptureInstall (machine B)

- Re-baselined the branch: **79.50% line / 67.23% branch** (essentially
  unchanged from 79.52% / 67.25% in the bootstrap entry — the 11 devtools
  tests added in the prior session moved the global metric by less than
  the rounding noise). Confirms the heuristic in the doc: small-batch
  pure-helper tests inside a file that already has coverage barely
  budge the global percentage.
- Area picked: **`Hosting/Devtools/LogCaptureInstall.cs`** (was 58.8% / 38% /
  98 missed / 238 total). Mid-list candidate from the gap report, chosen
  for three reasons: (a) pure C# — no WinUI activation, (b) the file's
  three types had only 2 shallow tests against ~20 distinct branches, (c)
  the `Install` method itself (~50 lines mutating process-wide
  Console.Out / Console.Error / Trace.Listeners) was **entirely
  untested**, despite being the only public entry point and the source of
  every stdio-MCP corruption incident this file was written to prevent.
- Audited the two pre-existing tests
  (`TeeTextWriter_AppendsLineOnNewline`, `BufferTraceListener_CapturesDebugWriteLine`):
  both are real (assert behavior, not just non-null), so left in place
  and added new coverage *alongside* them rather than replacing.
- Added **20 new tests** in
  `tests/Reactor.Tests/Devtools/LogCaptureBufferTests.cs`:
  - **TeeTextWriter (11 new tests)** — Write(char) with embedded newline,
    Write(char[], int, int) slice respect, Write(char[]) with embedded
    newline (separate path from Write(string)), CR stripping, multi-newline
    splits, null-string no-op, Flush() emits pending partial line,
    Flush() no-pending does NOT emit a phantom blank entry,
    WriteLine() no-arg flushes pending, forwarding works (writes to both
    sink and buffer), Encoding follows forward writer when present,
    Encoding falls back to UTF-8 when forward is null.
  - **BufferTraceListener (4 new tests)** — Write(null) / Write("")
    no-op, Flush emits pending partial line, Write strips CR + splits on
    LF, WriteLine(null) doesn't drop the pending payload or NRE.
  - **`LogCaptureInstall.Install` (6 new tests)** in a new
    `[Collection("ConsoleTests")]` class — idempotency (returns same
    buffer, adds at most one BufferTraceListener), captures
    Console.Write / Console.Error.Write into the right LogSource,
    forwardConsole=false does NOT corrupt the original stream (the
    stdio-MCP-transport contract that motivated the entire file),
    forwardConsole=true DOES forward, Trace.WriteLine captured as
    LogSource.Debug, ResetForTests clears the static reference.
- Every test names the product bug it would catch — kept the bar above
  vanity per the doc's quality rule. No `Setters.Count == 1`-style asserts.
- **Branch-coverage emphasis honored**: every test was written to drive a
  previously-untouched if/switch arm — char vs char[] vs string overload
  paths in AppendChar/AppendString, the `forward != null` vs null branches
  in Encoding + Write, the `_lineBuf.Length > 0` branch in
  FlushLineBuffer, the idempotent-return branch in Install.
- **Surprises / non-obvious findings:**
  - The two pre-existing tests use `[InternalsVisibleTo]` and reach
    `internal sealed` types directly via `new TeeTextWriter(...)`. So
    LogCaptureInstall.Install can be invoked from xUnit despite its
    `internal` visibility — confirmed by the new Install tests.
  - `Install` permanently mutates Console.Out / Console.Error /
    Trace.Listeners. Every Install test must save and restore them in a
    `finally`, otherwise other tests in the assembly observe a tee'd
    Console. The doc had not flagged this — adding a note here so the
    next session doesn't get bitten.
  - The build of `tests/Reactor.Tests` fails when `Platform` is unset
    (Minesweeper sample needs an explicit ARM64/x64). The exact build
    command that works (and matches `run-coverage.ps1`) is:
    `dotnet build tests/Reactor.Tests -c Debug -p:Platform=x64 -p:Optimize=false -p:DebugType=portable`.
    Likewise `dotnet test ... --no-build -p:Platform=x64`. Without the
    `-p:Platform=` argument, MSBuild evaluates against ARM64 (or whatever
    `Platforms` lists first), which trips the Minesweeper guard. Noted
    here in case a fresh agent burns 5 minutes on the same path.
- **Test results:** 33/33 `LogCapture*` tests pass.
- **Per-file delta** (from `coverage/unit.cobertura.xml`):
  - `TeeTextWriter`: line 100%, branch 88.2% (was inferred ~70% / ~50%)
  - `BufferTraceListener`: line 100%, branch 100%
  - `LogCaptureInstall`: line 100%, branch 100%
  - Combined file (LogCaptureInstall.cs): essentially fully covered. The
    one remaining TeeTextWriter branch arm is likely the rarely-hit
    `c != '\r' && c != '\n'` else branch in `AppendChar` on a code path
    that's not interesting for product behavior.
- **Merged delta** (full unit + selftest run): **79.50% → 79.57% line**
  (+0.07), **67.23% → 67.29% branch** (+0.06). Honest, modest gain
  consistent with a single ~240-line file: ~95 newly-covered lines on a
  denominator of ~101,473 is ~0.09% by arithmetic; the residual
  difference is the few selftests that hit the same lines.
- **Lesson for next session — pacing:** a single focused file even at the
  *easy* end of the worklist moves the global metric by less than 0.1%.
  To close 5.5 points we need ~60 of these, or a few large
  selftest-driven Tier-1 sweeps (Reconciler.Update / Mount each carry
  >900 missed lines). The unit-test runway is short — the next agent
  should consider whether the cheapest path to 85% is actually a couple
  of new selftest fixtures rather than a long tail of pure-helper tests.
  Document any [ExcludeFromCodeCoverage] proposals against the
  honest-deferrals list (PreviewCaptureServer, JumpListComInterop,
  TrayFlyoutHostWindow, TaskbarOverlay, ChartAutomationPeer) for user
  approval — together they're worth ~1.6 points without a single test.

### 2026-05-17 — Element.OwnPropsEqual (machine B, second pass)

- Baseline at start: **79.56% line / 67.29% branch** (no need to
  re-measure — git state hadn't changed since the LogCaptureInstall
  iteration finished 25 min earlier; treating the previous run's
  `coverage/merged.cobertura.xml` + `gap-report.md` as canonical saved
  ~6 min of build+collect. The doc previously did not mention this
  optimization — adding it now: **if your commit is the only diff
  against the last full run, you can skip step 3.**)
- Area picked: **`Element.OwnPropsEqual`** — surfaced by drilling into
  per-method coverage on `Core/Element.cs`. The method was at
  **19% line / 17% branch** (the worst-covered method in the file by a
  wide margin), with **zero direct tests** despite gating every
  reconcile-highlight-overlay decision on every render. The 99 missed
  lines in Element.cs cluster around this single method.
- Audit: no existing tests directly cover `OwnPropsEqual`. The 19%
  came from incidental invocation in `ReconcilerCorrectnessTests` /
  `PanelChildReconciliationTests`. No vanity to strip.
- Added **44 new tests** in `tests/Reactor.Tests/OwnPropsEqualTests.cs`
  covering every switch arm in the method:
  - **Reference + type-tag fast paths** — `ReferenceEquals(a, a)` short
    circuits, different types short circuit.
  - **Container layouts** — Stack (Orientation/Spacing/H/V Alignment),
    Grid (RowSpacing/ColumnSpacing/Definition-by-reference), Border
    (CornerRadius/Padding/BorderThickness), ScrollView (all 6 DPs),
    Flex (Direction/Justify/AlignItems/AlignContent/Wrap/Gap×2/Padding),
    WrapGrid (Orientation/ItemWidth/ItemHeight/MaximumRowsOrColumns),
    Canvas/RelativePanel/Viewbox children-don't-trigger-rebuild guard.
  - **Structural wrappers** — NavigationHost / CommandHost / Group /
    ErrorBoundary / Component / Func / Memo / Modified all return true
    so their own re-render doesn't strobe the highlight overlay. Each
    test passes *different* args to prove the arm is wired
    correctly (a regression that started inspecting args would fail).
  - **TitleBar** — 5 own-props vary individually; explicit pinning that
    Content / RightHeader are NOT own-props (the source-level comment
    says "without this, TitleBar flashes yellow on every reconcile when
    only descendants changed" — now there's a test enforcing it).
  - **Popup** — IsOpen / IsLightDismissEnabled vary individually.
  - **Flyout family** — MenuFlyout / ContentFlyout / MenuFlyoutContent /
    Flyout all always-equal even with different Target / Content.
  - **Selection collections** — ComboBox (SelectedIndex/Placeholder/
    Header/IsEditable + the "fresh items array doesn't flash" pin),
    ListView / GridView (SelectedIndex/SelectionMode/Header), FlipView,
    Pivot (SelectedIndex/Title), TabView (SelectedIndex/
    IsAddTabButtonVisible), TreeView (SelectionMode/CanDragItems/
    AllowDrop/CanReorderItems), SelectorBar, ListBox,
    RadioButtons (SelectedIndex/Header), BreadcrumbBar setters-only.
  - **Fallback** — Leaf type (ButtonElement) returns false so the
    reconciler always re-applies props. Test pins the default-false
    contract; a future arm that accidentally collapsed a leaf to "equal"
    would silently stop pushing label / state updates.
- Every test names a concrete product bug. The framing is:
  "if `OwnPropsEqual` returns true when X changes, the WinUI control
  keeps the stale value." Reviewer can verify by mentally inverting
  the assertion: `Assert.True` → "what if it returned false?" gives
  "highlight overlay flashes". `Assert.False` → "what if it returned
  true?" gives "user sees stale UI." Both are observable.
- **Test results:** 85/85 `OwnPropsEqual` tests pass. One initial
  test about `OnMount(...)` mutating Setters reference was removed when
  it turned out `.OnMount` wraps in `ModifiedElement` instead of
  cloning the leaf element's `Setters` array. That's a useful pin
  for the next agent: **Setters-reference inequality is hard to
  trigger from outside the assembly** because the public fluent API
  goes through `ModifiedElement` wrappers, not direct Setters
  mutation. To hit the `ReferenceEquals(setters, setters)` branch of
  `OwnPropsEqual` reliably, future tests would need to construct the
  element record directly with a custom `Setters` array assignment via
  the `internal` initializer, which requires being in the same
  `InternalsVisibleTo` boundary (we are) but also using the right
  syntax (`new StackElement(...) { Setters = [...] }` — and Setters
  is `internal init`, so a `with`-expression from the test assembly
  cannot mutate it).
- **Coverage delta** (merged unit + selftest):
  **79.57% → 79.80% line (+0.23)** and
  **67.29% → 67.80% branch (+0.51).** The branch swing is roughly 2× the
  line swing — that's exactly the payoff for picking a method full of
  switch arms and varying one prop at a time. Pacing implication: a
  *branch-shaped* target (large switch, many ?:, many if-chains) gives
  much more branch-% than a scalar new file of equivalent line count.
  Recommended priority signal for future iterations: prefer files whose
  branch% trails line% by >25 points (e.g. ChartKeyboardNavigator at
  35.7%/5.6%, SemanticPanel at 36.8%/2.7%) when there is a host-bound
  vs unit-testable trade-off — the branch math compounds faster.

### 2026-05-17 — DragData lazy providers + FormatEntry state machine (machine B, third pass)

- Baseline (no need to re-measure — git state unchanged since prior
  iteration): **79.80% / 67.80%**.
- Picked **`Input/DragData.cs`** (45.5% line / 18.9% branch / 97 missed).
  Per-method drill-down: every `With{Uri,Html,Rtf,Bitmap,Files,
  CustomFormat}` overload at 0% line, the `FormatEntry` resolve state
  machine partially covered, the transfer-registry static methods
  internal-only and untested. The branch% gap of 26.6 points placed it
  third on the new "branch-gap ranking" produced from the merged
  cobertura at the end of the previous iteration (a tooling lift worth
  capturing — see _Self-improvement_ below).
- Audited existing `DragDataTests.cs` (28 tests): all real, all kept.
  Coverage gaps clustered in three areas: (a) lazy-provider variants
  for non-text formats, (b) the `FormatEntry` resolve-precedence
  contract (eager > async > sync in async; eager > sync in sync; async-only
  is a no-block in sync), (c) the `Register / Resolve / Unregister`
  in-memory transfer registry. The host-bound `PopulatePackage` and the
  bitmap / files paths that need a real `IStorageItem` are deliberately
  skipped — first attempt at mocking `IStorageItem` would be a flaky
  rabbit hole.
- Added **34 new tests**, all passing:
  - **Lazy providers (12)** — `WithUri / WithRtf / WithHtml /
    WithCustomFormat` × (sync provider, async provider) plus the
    "sync provider satisfies `TryGet*`" and "async-only provider
    falls through to false in `TryGet*` and resolves in `Get*Async`"
    pair that pins the UI-thread no-block contract.
  - **AvailableFormats / HasFormat / FormatEntries (5)** — covers the
    standard-format key mapping (regression catcher: WithUri writing to
    the Text key would advertise the wrong format), the
    ProcId-marker-always-present invariant, and the internal-getter
    reference identity (PopulatePackage relies on it; copy-semantics
    would silently fork the package state).
  - **Overwrite / last-write-wins (2)** — `WithText("a").WithText("b")`
    overwrites, doesn't append; sync-then-eager pin (catches a bug
    where `FormatEntry` retains stale `SyncProvider` after `WithUri`
    promotion to eager).
  - **GetCustomFormatAsync / TryGetCustomFormat type-mismatch (4)** —
    absent → default/false (not throw), wrong-type → default/false
    (not InvalidCastException). The spec contract is "silent fall-through"
    so consumers can chain.
  - **Transfer registry (3)** — Register/Resolve/Unregister cycle,
    Resolve(unknown) returns null (not KeyNotFoundException),
    Unregister(unknown) is idempotent (DropCompleted can fire twice).
  - **FormatEntry state-machine (12, new test class)** — each precedence
    arm of `ResolveAsync` / `ResolveSync`, the `HasEager` invariant
    (false when any provider set or eager is null), and the
    cancellation-token propagation pin. The critical pin:
    `ResolveSync_AsyncOnly_ReturnsNullWithoutBlocking` — a regression
    that called `.GetAwaiter().GetResult()` here would freeze the UI
    dispatcher on every drop with a lazy-async format. The test asserts
    *both* that the result is null *and* that the async provider is
    not invoked.
- **Coverage delta** (merged):
  **79.80% → 79.82% line (+0.02)** and
  **67.80% → 67.85% branch (+0.05).** Per-file:
  `DragData.cs` 45.5% → 52.8% line, 18.9% → 27.0% branch. The modest
  global delta on a 34-test batch is the file's *natural ceiling* for
  unit tests — the remaining `PopulatePackage`, eager-bitmap path,
  `WithFiles(IEnumerable<IStorageItem>)`, and `TryGetSafeLocalFiles`
  UNC/DOS/reparse safety filter all require either a real
  `DataPackage` (WinRT activation) or `IStorageItem` mocks that the
  CsWinRT projection makes painful from a headless xUnit. **Deferral
  candidate flagged:** the `TryGetSafeLocalFiles` security branch
  (TASK-069) is unreachable from xUnit without `IStorageItem` mocks;
  the next agent should consider a selftest fixture that pops up a
  receiver and synthesizes a `DataPackageView` with crafted paths to
  exercise the UNC / DOS-device / reparse rejections. That's the
  highest-value uncovered code in this file.

#### Self-improvement: branch-gap ranking query

Added `tools/coverage/rank-branch-gap.ps1` — surfaces files where
branch% trails line% by the widest margin (the heuristic from the
prior iteration), with `-Top` and `-MinMissed` knobs. Use it after
`run-coverage.ps1` to pick the next branch-shaped target.

### 2026-05-17 — Editors catalog (machine B, fourth pass)

- Baseline at start: **79.82% line / 67.85% branch** (re-confirmed by
  running `run-coverage.ps1`; the latest commit was the one we wanted
  to measure against).
- Area picked: **`Controls/Editors/Editors.cs`** (29.7% line / 24.4%
  branch / 294 missed). Picked over the bigger absolute targets
  (Reconciler.Update at 1696, ReactorApp at 1251) because:
  - The file is **a catalog of pure-C# editor factories** returning
    `Func<object, Action<object>, Element>`, and Reactor elements are
    record types — no WinUI activation required to inspect the
    returned shape.
  - The existing `TypedEditorsTests.cs` exercises type-registration
    plumbing but **does not call the factories** — its assertions are
    all `Assert.NotNull(factory)`, which can't fail by C# semantics.
    The Number/Decimal/Float/Long/Short/Byte type-coercion paths (the
    biggest source of "InvalidCastException in production when
    NumberBox hands a double to an int setter" bugs) had **zero real
    coverage**.
  - The `ToDouble` switch (13 arms) and `FromDouble` if-chain (12
    arms) together carry ~30 uncovered branches. The doc's previous
    branch-shaped-target heuristic flagged this directly.
- Audited the existing tests (`TypedEditorsTests.cs`, 17 tests,
  217 lines). Wrote a Vanity-test audit findings entry above; bottom
  line is most TypedEditorsTests assertions are vanity but the
  type-registration paths they exercise are not duplicated by my new
  file, so I did **not** delete them this iteration. Follow-up
  candidate.
- Added **66 new tests** in
  `tests/Reactor.Tests/Controls/EditorsBehaviorTests.cs`:
  - **Numeric round-trip (15)** — every `ToDouble` switch arm
    (int/long/decimal/float/short/byte/uint/double/null/string-via-
    fallback) plus the FromDouble inverse for the most common
    target types. Each `OnValueChanged_Returns_<T>` test asserts
    `Assert.IsType<T>(captured)` — a regression that dropped the
    target-type switch would fail loudly on the type assertion, not
    silently on the value.
  - **Text / CheckBox / Toggle (8)** — null-default coercion, value
    pass-through, MaxLength branch counting (the `maxLength is { } max`
    ternary's true/false arms), placeholder propagation, on-content
    forwarding.
  - **Date variants (8)** — DateTime/DateTimeOffset/DateOnly initial-
    value handling, plus the `DateTimeKind.Unspecified → Local`
    branch (the bug shape: passing a JSON-deserialized DateTime
    shifts hours by UTC offset). Pin: `Date_OnChange_Returns_DateTime`
    (not DTO) — catches the InvalidCastException that would hit any
    model property typed as `DateTime`.
  - **Time variants (8)** — TimeSpan/TimeOnly coercion in both
    directions, including the subtle "factory captures original
    value type, emits back same type" contract for `TimeOfDay()`.
    Pin: `TimeOfDay_OnChange_Returns_TimeOnly_When_Source_Was_TimeOnly`
    catches a regression that dropped the `value is TimeOnly` check.
  - **Uri (6)** — `TryCreate` gating (partial-input no-op),
    RelativeOrAbsolute accepts paths, Uri-object stringification,
    null default. Pin: `Uri_OnChange_Truly_Invalid_String_Does_Not_Commit`
    — uses an embedded control char to force TryCreate failure.
  - **Combo / EnumCombo (8)** — index lookup with hit, miss-defaults-
    to-zero (cardinal UI bug: stale data picking "no selection"
    would silently lose the field), strongly-typed onChange, name
    projection of null choices, enum parse-back.
  - **Color (2)** — null-default-Transparent, value pass-through and
    Color round-trip through OnColorChanged.
- **Initially included 9 ColorCompact tests** but they all failed with
  COMException on `SolidColorBrush` construction inside `.Background(hex)`.
  The swatch's brush eagerly builds a WinUI brush — host-bound. **Lesson
  for next session:** even "pure C# returning Element records" can hide
  WinUI activation if a factory chains `.Background(...)` on a Border —
  `BrushHelper.Parse` calls `new SolidColorBrush(color)`, which requires
  a packaged WinUI runtime. Removed the ColorCompact block and left a
  comment in the test file flagging the deferral (`TryParseHexColor`'s
  3 length-based arms + exception path are not unit-reachable; a
  selftest fixture that mounts a ColorCompact cell in a DataGrid is the
  right path).
- **Did NOT delete vanity tests this iteration.** The existing
  `TypedEditorsTests` exercise `ResolveEditor` / `GetCellRenderer` /
  `ReflectionTypeMetadataProvider` which the new file does not cover.
  Deleting them now would drop ~5-10 lines of registry-path coverage.
  Next iteration: rewrite each NotNull-only test to invoke the resolved
  factory and assert shape — would both raise the bar AND keep registry
  coverage.
- **Test results:** 66/66 EditorsBehaviorTests pass; 7,866 / 7,912 full
  unit suite pass (no regressions; 46 unchanged YogaGenerated skips).
- **Per-file delta** (`Controls/Editors/Editors.cs`): merged
  29.7% → 67.6% line; the remaining ~32% are the ColorCompact /
  WithBorder paths that build SolidColorBrush eagerly.
- **Merged delta** (full unit + selftest):
  **79.82% → 80.04% line (+0.22)** and
  **67.85% → 68.24% branch (+0.39).** Branch swing ≈ 2× line — again
  validating the branch-gap heuristic from the OwnPropsEqual /
  DragData iterations. A 71-line slice of a 317-line file moves
  branch% by 0.4 *points* of a 48k-branch denominator because every
  test was deliberately picked to hit a different switch arm.
- **Surprises / non-obvious findings:**
  - `Editors.ColorCompact()` is **not** unit-testable in headless
    xUnit, despite returning an Element. The reason is buried two
    extension methods deep: `Border(...).Background("#80000000", 1)`
    calls `BrushHelper.Parse` which `new SolidColorBrush(color)`s
    eagerly. Recommendation: split `BrushHelper.Parse` into a "store
    the hex string, materialize the brush at mount time" two-phase
    pattern, OR mark `ColorCompact`'s 18-line factory with
    `[ExcludeFromCodeCoverage]` and write a selftest fixture for it.
    Deferral candidate — **add to honest-deferrals list**.
  - The `TypedEditorsTests` file was checked in **with a comment
    saying "we can't exercise the onChange path end-to-end here"**.
    That comment is now wrong — the OnXxx callbacks on the record
    types are publicly invokable from the test assembly. The vanity
    tests there were defensible at the time of writing but no longer
    are; a future cleanup should rewrite them.
  - `factory(null!, _ => {})` is the cleanest way to test null-input
    paths from C# (`object?` can't be inferred from `null` literal
    alone). Using the null-forgiving operator on a parameter typed
    `object` makes the call site readable and survives nullable-
    reference-type tightening.
- **Pacing observation:** +0.22% line from 66 tests on a 317-line file
  is the unit-test-only ceiling for a single mid-tier source file.
  The math: a file with N missed lines, when bumped from ~30% to ~70%
  line coverage, adds 0.4*N covered lines. On a 101k-line denominator
  that's 0.0004*N points. So Editors.cs's 294 missed × 0.4 = ~118 newly-
  covered lines = +0.12% expected; we got +0.22% (the rest came from
  shared lines in Element record constructors and Dsl factory calls).
  To close 5 points purely in unit tests, the next agent should batch
  3-5 small files per iteration rather than one — the build+coverage
  loop is 5-10 min regardless of how many test files changed.

### 2026-05-17 — RenderContext hook-order exceptions (machine B, fifth pass)

- Baseline at start: **80.04% line / 68.24% branch** (re-confirmed by
  rerunning `run-coverage.ps1` after the Editors commit landed).
- Picked **`Core/RenderContext.cs`** — specifically the
  `HookOrderException` throw paths for `UseReducer<T>`,
  `UseReducer<TState, TAction>`, `UseMemo<T>`, and `UseRef<T>`. Existing
  `HookStateRefactorTests.cs` already covers UseState↔UseEffect hook-
  order swaps, but the four other hook flavours had **zero direct
  coverage** of the throw path despite each one being part of the
  framework's "loud failure for cardinal React sin" contract.
- Did not find a larger unit-testable hot spot this iteration after
  scoping `DevtoolsUiaTools` (heavy UIA peer / pattern provider —
  host-bound), `DevtoolsTools` (97% of the main class is covered;
  remaining 101 missed lines are tool handler lambdas that need a
  real DispatcherQueue + Window), `SelectorResolver` (NodeId branch
  fully covered, rest is VisualTreeHelper.GetParent — host-bound),
  `LayoutEtwConsumer` (real ETW session — admin-bound), and
  `ChartKeyboardNavigator` (HandleKeyDown is a FuncElement closure
  invoked by sealed `KeyRoutedEventArgs` — host-bound). Recording the
  scan here so the next session doesn't re-tour the same dead ends.
- Audited `TreeChartsTests.cs` (351 lines, 43 tests). Wrote an audit
  entry above; bottom line is ~20 fluent-setter tests are
  `Assert.Same(chart, result)` after `chart.Foo(...)` — vanity, but
  not actively misleading. Kept as-is for now per the doc rule
  "don't drop coverage without replacement." A selftest fixture that
  mounts a TreeChart and asserts the live Canvas Width / Height /
  Brush colors would be the right replacement.
- Added **12 new tests** in
  `tests/Reactor.Tests/RenderContextHookOrderTests.cs`:
  - **HookOrderException paths (7)** — UseReducer<T> at effect slot,
    UseReducer<TState, TAction> at effect slot, UseReducer<TState, TAction>
    with generic-type mismatch on existing UseState slot, UseMemo at
    effect slot, UseMemo with different generic type at same slot,
    UseRef at effect slot, UseRef with different generic type at same
    slot. Each asserts the exception message includes both the
    actual and expected hook-state type names so the developer-facing
    error remains diagnostic.
  - **UseStateSetterByIndex (3)** — happy path (updates cell, triggers
    re-render, next render observes the new value); out-of-range
    index (silent no-op, no re-render); type-mismatch index (silent
    no-op, original cell intact). Pin: a regression that threw on
    mismatch instead of silently failing would break the devtools
    state-mutation tool, since it walks the snapshot and tries
    multiple types when it doesn't know the cell's T.
  - **UseColorScheme null-Application path (2)** — verifies the hook
    doesn't NRE when `Application.Current` is null (the unit-test
    state). Bug shape: a regression that dereferenced `theme.Value`
    instead of using the null-conditional `theme?` operator would
    crash every headless RenderContext test. The wrapper
    `UseIsDarkTheme()` gets a separate test for the same reason.
- **Test results:** 12/12 pass; 7,878/7,924 full unit suite passes
  (was 7,866 — clean +12).
- **Coverage delta** (merged):
  **80.04% → 80.10% line (+0.06)**, **68.24% → 68.29% branch (+0.05)**.
  Expected scale: 4 hook-order throw paths × ~3 lines each + 3
  setter-by-index paths × ~3 lines + 2 color-scheme paths × ~4
  lines ≈ 30 lines on a 101k denominator = +0.03% line. Got
  +0.06% — the rest came from inferred-arm coverage in
  `HookStateRefactorTests`' shared fixture-style helpers.
- **Surprises / non-obvious findings:**
  - The `HookOrderException` message templates differ slightly
    between UseRef ("expected ValueHookState<Ref<X>>, got Y") and
    UseReducer ("Hook at index N is X, expected ValueHookState<T>
    (UseReducer)"). A future cleanup could unify them, but the
    asymmetry is what the source ships today; tests pin both.
  - **Untested but unit-reachable still:** `MarshalIfOffUIThread`'s
    two error paths (no captured UI dispatcher off-thread; TryEnqueue
    refused because the dispatcher is shutting down). Both require a
    real `DispatcherQueue` in a shutting-down state, which xUnit
    cannot set up reliably without a host. Flagged as selftest
    candidate.
- **Deferral-candidate proposal (for user approval next iteration):**
  the doc lists ~1,639 lines across `PreviewCaptureServer` (665),
  `JumpListComInterop` (338), `ChartAutomationPeer` (308),
  `TrayFlyoutHostWindow` (230), `TaskbarOverlay` (98) that are
  **inherently host-bound** and cannot be unit tested. Adding
  `[ExcludeFromCodeCoverage]` to these would shift the denominator
  by ~1,639 / 101,473 ≈ 1.6 percentage points without writing a
  single test, taking us from 80.10% → ~81.7%. The trade-off the
  doc flagged: "the user's mandate is 'no vanity coverage,' and
  excluding code from the metric is the opposite mistake." Per the
  doc rule, this requires explicit user confirmation in this log
  before the next session applies it.

### 2026-05-17 — TypedColumns vanity-to-real rewrite (machine B, sixth pass)

- Baseline: **80.10% line / 68.29% branch**.
- Area picked: **`TypedEditorsTests.cs` strengthening + TypedColumns
  branch coverage**. The previous Editors iteration noted that
  `TypedEditorsTests` was full of `Assert.NotNull(factory)` vanity; this
  iteration *replaces* that bar by invoking each resolved editor and
  asserting the returned Element's shape.
- Added **26 new tests** in
  `tests/Reactor.Tests/Controls/TypedColumnsBehaviorTests.cs`:
  - **TypeRegistry resolutions (9)** — DateTime / DateTimeOffset /
    DateOnly / TimeSpan / TimeOnly / Uri / Color / Bool-Standard /
    Bool-Compact. Each invokes the resolved factory and asserts the
    Element record type (DatePicker / TimePicker / TextField /
    ColorPicker / Toggle vs CheckBox), then exercises OnXxx where
    applicable to verify type-preserving onChange round-trips.
  - **ReflectionTypeMetadataProvider (3)** — `[DataType(DataType.Url)]`
    on `string` vs `Uri` properties (separate branches of the if-chain
    in ReflectionTypeMetadataProvider), and `[Range]` on int with
    Setters-count + FromDouble round-trip assertion.
  - **TypedColumns factories (14)** — NumberColumn for int / decimal /
    long (FromDouble inverse coverage), CheckBoxColumn vs
    ToggleSwitchColumn (compact vs explicit), DateColumn for all three
    type-switch arms (DateTime / DateTimeOffset / DateOnly — the
    last two were previously uncovered), TimeColumn for TimeSpan vs
    TimeOnly, HyperlinkColumn for Uri vs string (the string branch was
    previously uncovered), ComboBoxColumn with strongly-typed choices,
    ColorColumn editor + CellRenderer wiring.
- **Surfaced a contract pin (not a bug):** initial test asserted
  `[DataType(DataType.Url)]` on a string property commits Uri. The test
  failed and surfaced that the source explicitly comments:
  "[DataType.Url] on string → URL text input + Hyperlink display; on
  Uri → Uri editor + Hyperlink display." String properties keep
  receiving strings — the Hyperlink display happens at render time, not
  via Editors.Uri. Test rewritten to pin this branch faithfully.
- **Did NOT delete the old vanity TypedEditorsTests this iteration.**
  Same reason as last iteration: they exercise type-registration code
  paths that the new file doesn't reach (the new file invokes
  `TypeRegistry.ResolveEditor` but doesn't exercise the
  `RegisterCellRenderer` / `GetCellRenderer` round-trip the way the
  old `Explicit_CellRenderer_Registration_Wins_Over_Fallback` does).
  Follow-up: rewrite each remaining vanity test into the
  invoke-and-assert pattern, then delete what's truly superseded.
- **Test results:** 26/26 pass; 7,904/7,950 full unit suite (was
  7,878 — clean +26).
- **Coverage delta** (merged):
  **80.10% → 80.14% line (+0.04)**, **68.29% → 68.34% branch (+0.05)**.
  Small because TypedColumns is a thin wrapper — most of the lines
  exercised by the new tests are inside `Editors.*` (already at high
  coverage from iteration 4) and `Factories.Column<T>`. The real value
  of this batch is the regression net: each test catches a *wiring*
  regression in TypedColumns / ReflectionTypeMetadataProvider that
  the old NotNull-only tests would have silently passed through.
- **Lesson:** invoke-and-assert tests on already-covered helpers don't
  move coverage much, but they do firm up the boundaries between
  layers. Next iteration's choice between "new code to cover" and
  "stiffen existing covered code" should weigh: the metric only moves
  for genuinely-uncovered lines, but the test bar moves on both.

### 2026-05-17 — CellRenderers catalog (machine B, seventh pass)

- Baseline: **80.14% line / 68.34% branch**.
- Area picked: **`Controls/Editors/CellRenderers.cs`** — sister catalog
  to Editors (which was iteration 4's target). No existing test coverage
  beyond incidental exercise via `TypedColumnsBehaviorTests`. Text-shaped
  renderers (Text, Number, Date, Time, Enum, Hyperlink) are pure-C# over
  Reactor records — same shape as the Editors factories.
- Added **24 new tests** in
  `tests/Reactor.Tests/Controls/CellRenderersTests.cs`:
  - **Text (4)** — null guard, ToString fallback, IFormattable + format
    (locale-tolerant), non-IFormattable + format (falls through).
  - **Number (3)** — TextAlignment.Right + HAlign.Stretch contract
    (the source's "stretch so right-alignment takes effect" pin), N0
    format, null guard.
  - **Date (3)** — default-d format, invariant "yyyy-MM-dd" format,
    null guard.
  - **Time (3)** — `hh\:mm\:ss` TimeSpan custom format (escaped colons,
    a sharp edge — see surprise below), DateTime + standard "t" format,
    null guard.
  - **Enum (2)** — ToString of enum value, null → empty.
  - **Hyperlink (6)** — three branches of the if-chain (Uri → button;
    string parseable as absolute → button; non-Uri string → TextBlock
    fallback) plus displayTextFormat usage, null guard, relative-path
    fallback. Pin: a regression that emitted a HyperlinkButton with
    null NavigateUri would crash WinUI on click.
  - **FormatValue helper (3)** — IFormattable without format,
    non-IFormattable without format, invariant "R" round-trip.
- **Surprises / non-obvious findings:**
  - **`HAlign(...)` vs the TextBlockElement's own HorizontalAlignment
    property are two different shapes.** `.HAlign(value)` (extension)
    writes to `el.Modifiers.HorizontalAlignment` (the generic
    Element-modifier slot). The record's own `HorizontalAlignment
    { get; init; }` init prop is set by a different code path (not
    used by the Number renderer). Initial test assertion against
    `el.HorizontalAlignment` failed with `null` — pin the right slot
    or the test never catches a HAlign regression. Wrote a comment in
    the test for the next agent.
  - **TimeSpan custom format strings need escaped colons.** Passing
    `HH:mm:ss` (DateTime format) to a TimeSpan via
    `IFormattable.ToString(format, …)` throws FormatException. Use
    `hh\:mm\:ss` (backslash-escaped). This is a real product sharp
    edge: `CellRenderers.Time(string format = "t")`'s default `"t"` is
    also not a standard TimeSpan format — calling Time() on a TimeSpan
    column would throw at render time. Not fixing the product here, but
    pinning the workaround so the next agent doesn't re-discover it.
    Filed as a deferred follow-up: either add a TimeSpan→DateTime
    conversion in the renderer or document the constraint clearly on
    the public API.
- **Coverage delta** (merged):
  **80.14% → 80.18% line (+0.04)**, **68.34% → 68.41% branch (+0.07)**.
  Same shape as the TypedColumns iteration — CellRenderers is small
  (131 lines), and ~40 lines of that are brush-bound and unreachable
  from xUnit. The 24 tests cover the text-shaped renderers (≈ 50 lines)
  but most of those lines were already incidentally touched by
  `TypedColumnsBehaviorTests`. The branch swing is the better signal:
  +0.07 from a 71-line file is good ROI per line because many of the
  new tests hit previously-untouched if-chain arms (Hyperlink's three
  branches especially).
- **Lesson on small files vs metric movement:** the four most recent
  iterations have averaged +0.09 line per iteration, with each
  iteration cleanly identifying a real product behavior. The metric is
  starting to plateau against unit-testable code — the remaining unit
  surface tilts toward "stiffening existing coverage" rather than
  "covering new code." If the deferral candidates (~1,639 lines)
  are approved, they'd jump the metric ~1.6 points in a single commit
  while honoring the no-vanity rule (the excluded code is genuinely
  not unit-testable). Without that approval, the path to 85% is a
  long tail of small-file iterations PLUS at least one selftest
  fixture push targeting Reconciler.Mount / Reconciler.Update.

### 2026-05-17 — Persistence stores edge cases (machine B, eighth pass)

- Baseline: **80.18% line / 68.41% branch**.
- Area picked: **`Hosting/Persistence/JsonFileStore.cs`** (76.4% / 35
  missed) and **`Hosting/Persistence/PackagedSettingsStore.cs`** (0% /
  38 missed). Both are pure-ish — JsonFileStore is file I/O over a
  temp path, PackagedSettingsStore wraps WinRT `ApplicationData.Current`
  which throws in unpackaged xUnit hosts. The catch arms are the
  testable surface.
- Audited existing `JsonFileStoreTests.cs` (9 tests, all real — happy
  path + missing file + malformed JSON + oversize + ctor guard +
  DefaultPath). Kept all and added orthogonal edge cases.
- Added **19 new tests** total:
  - **JsonFileStoreTests +13 tests:**
    - **Early-return guards (3)** — Write with empty id / null data /
      empty id-read short-circuit before touching the filesystem.
      Pin: a regression that dropped the guards would create
      `<ProcessName>/reactor-windows.json` on every program start with
      a placeholder entry.
    - **Per-entry corruption arms (4)** — malformed base64 in a valid
      JSON envelope (FormatException catch), non-string entry type
      mismatch, non-object root, empty-string entry. Each branch was
      previously 0% — a regression that propagated would crash every
      app whose persistence file was tampered with or partially
      written from an OS crash.
    - **Merge over tampered file (1)** — if the existing file is a
      top-level array, ReadDocumentOrEmpty's catch arm returns an
      empty dict, the Write proceeds, and the tampered content is
      replaced. Pin: a regression that propagated the JsonException
      would crash the *next* save after any tampering.
    - **AppendQuotedString escape arms (5)** — round-trip ids
      containing `"`, `\`, `\n`+`\t`, control chars < 0x20 (Unicode
      \u#### escape), `\r`+`\b`+`\f`. Each new test forces a unique
      switch arm in the escape ladder. Bug shape: any unescaped
      special character in an id would produce invalid JSON that
      breaks every subsequent TryRead — and ReactorWindow keys are
      user-controlled.
  - **PackagedSettingsStoreTests +6 tests (new file):**
    - Early-return guards (3) — empty id / null data / empty-id read.
    - WinRT-unavailable catch arms (2) — TryRead and Write in an
      unpackaged context must NOT throw the
      InvalidOperationException 0x80073D54 that
      `ApplicationData.Current` raises. Bug shape: a regression that
      removed the try/catch would crash every unpackaged host at
      startup (ReactorApp's auto-detection logic calls TryRead during
      bring-up).
    - `IsAvailable()` returns false in unpackaged context — pins the
      contract that the static probe never throws (used by spec §8
      auto-detection to pick the right store).
- **Test results:** 28/28 in the persistence cluster pass; 7,947/7,993
  full unit suite (was 7,928 — clean +19, plus the file-rebuild also
  surfaced a few new xUnit-skipped Yoga tests).
- **Coverage delta** (merged):
  **80.18% → 80.26% line (+0.08)**, **68.41% → 68.50% branch (+0.09)**.
  Best yield since iteration 4. Two reasons:
  1. **PackagedSettingsStore was at 0%** — every test moves new lines
     into the covered set, no incidental coverage to share.
  2. **JsonFileStore's catch arms are branch-dense** — each malformed-
     payload test hits a distinct catch handler that has a unique
     line + branch contribution.
- **Surprises / non-obvious findings:**
  - **PackagedSettingsStore is unit-testable when the goal is
    catch-arm coverage.** Despite the class's reliance on
    `Windows.Storage.ApplicationData.Current` (which throws on
    unpackaged), every public method's contract is "warn-and-default
    on failure" — and the failure path *is* the unpackaged path. So
    headless xUnit naturally drives the unhappy path. The doc had
    listed this file as "0% / 38 missed" without flagging it as a
    cheap pickup; corrected now.
  - **TimeSpan custom format strings (carried over from
    CellRenderers iteration):** the carry-over insight is that
    "host-bound" sometimes means "WinUI XAML controls only" — pure
    .NET WinRT projections (Windows.Storage, etc.) that throw
    deterministically in unpackaged still produce reproducible
    test paths. A future scan should re-examine the "0% / host-bound"
    cluster in the worklist for this shape:
    `JumpListComInterop`, `PreviewCaptureServer`, `TaskbarOverlay`,
    `TrayFlyoutHostWindow` *might* be reachable for catch-arm
    coverage if their WinRT/COM calls throw deterministically. Worth
    a 30-min scout before reaching for `[ExcludeFromCodeCoverage]`.

### 2026-05-17 — Shell-API previously-deferred files (machine B, ninth pass)

- Baseline: **80.26% line / 68.50% branch**.
- Carried the "WinRT projections still test their catch arms in
  unpackaged xUnit" lesson from iteration 8 forward to the shell
  cluster. Picked two of the previously-flagged deferral candidates
  (`TaskbarOverlay` 0% / 98 missed; `JumpList` 46.5% / 200 missed).
  `JumpListComInterop` (0% / 338 missed) deferred — the COM coclass
  cast `(ICustomDestinationList)new DestinationList()` happens
  *before* any try block and would need a real shell host. Updated
  the worklist commentary accordingly.
- Added **17 new tests** across two new files:
  - **`TaskbarOverlayTests.cs` (9 tests):**
    - Constructor + Icon getter/setter + nullable round-trip (3).
    - `_isDisposed()` short-circuit in Apply — setters after dispose
      are silent no-ops, NOT throws (2).
    - `TaskbarComSingleton.TryGet() is null` second early return —
      the live-but-uninitialised-COM path also no-ops cleanly (1).
    - `LoadIconFor` private static helper, reached via reflection
      (3): null icon → 0, IsResource icon → 0 (resource paths can't
      become HICONs), missing file → 0 without throw (LoadImageW
      returns 0 for non-existent files + catch arm swallows).
  - **`JumpListUpdateValidationTests.cs` (8 tests):**
    - `UpdateAsync(null)` → ArgumentNullException (1).
    - Per-entry validation: null entry → ArgumentException; task /
      custom item with empty title → ArgumentException; separator
      with empty title → permitted (the kind-aware gate) (4).
    - Unpackaged-without-AppUserModelId → InvalidOperationException;
      `ClearAsync` inherits the same gate (2).
    - Happy-ish path: valid items + AppUserModelId set → Task
      completes without throwing even when the inner COM call
      fails in xUnit (1). Pin: a regression that propagated the
      COM exception would crash every startup-path JumpList update.
- **Did not duplicate** the existing `JumpListStateTests` (in
  `JumpListItemTests.cs`) which already covers the static state
  round-trips for AppUserModelId / ShowRecent / ShowFrequent /
  ResetForTests. Renamed the new class to
  `JumpListUpdateValidationTests` to avoid the collision after
  initial compile failure surfaced the duplication.
- **Test results:** 17/17 in new classes pass; 7,964 / 8,010 full
  unit suite (was 7,947 — clean +17, plus a couple of new
  YogaGenerated entries from the test-build refresh).
- **Coverage delta** (merged):
  **80.26% → 80.55% line (+0.29)**, **68.50% → 68.70% branch (+0.20)**.
  Strongest single-iteration jump since iteration 4 (Editors).
  Reasons:
  1. **TaskbarOverlay was at 0%** — every test net-new covered lines.
  2. **JumpList.UpdateAsync** is a large async method; the
     validation loop and `TryUpdatePackaged` / unpackaged-fallback
     branches together carry ~30 previously-uncovered lines that
     the test net-newly exercised.
  3. **Reflection-driven `LoadIconFor` tests** hit a private static
     helper that had zero callers in test code — every line was net
     new.
- **Surprises / non-obvious findings:**
  - **`JumpList.UpdateAsync` doesn't throw even with no
    AppUserModelId — until you actually need the unpackaged path.**
    The synchronous AppUserModelId check fires only AFTER
    `TryUpdatePackaged` returns false. In an actually-packaged app
    the unset AppUserModelId is harmless (the package manifest
    supplies one). In unpackaged xUnit, `PackageRuntime.IsPackaged`
    returns false → TryUpdatePackaged returns false → AppUserModelId
    gate fires. Pin: a regression that moved the AppUserModelId
    check to the top would break packaged apps that legitimately
    leave the static unset.
  - **`ClearAsync` is `=> UpdateAsync(Array.Empty<JumpListItem>())`.**
    It doesn't short-circuit on empty input — the same validation
    + AppUserModelId gate applies. A "clear" call on an unconfigured
    unpackaged app throws, even though it has nothing to clear.
    Probably right: a Clear-without-config is a developer mistake,
    not a benign no-op. Pinned.
  - **Reflection access to `private static` helpers is fine for
    coverage hooks**, but the resulting test is brittle to rename.
    Used a clear failure message in `InvokeLoadIconFor` so a future
    rename surfaces the broken reflection lookup loudly.
- **Pacing observation:** revisiting "deferred" files for catch-arm
  coverage with the lesson from iteration 8 paid off. **The cluster
  remaining for that scan:** `PreviewCaptureServer` (665 missed) —
  ctor takes DispatcherQueue + Window so most paths are gated, but
  the GenerateToken static and the per-request validators *might*
  be reachable; `TrayFlyoutHostWindow` (230 missed) — ctor builds
  a Window directly so very little is reachable; `ChartAutomationPeer`
  (308 missed) — needs a real AutomationPeer host. Of those,
  `PreviewCaptureServer` is the best remaining catch-arm scout.

### 2026-05-17 — PreviewCaptureServer pure helpers (machine B, tenth pass)

- Baseline: **80.55% line / 68.70% branch**.
- Carried the iteration-9 scout note forward — `PreviewCaptureServer`
  *did* have unit-reachable surface. The ctor is gated on
  DispatcherQueue + Window, but six security-critical helpers are
  reachable: 4 via straight reflection on private static methods
  (`GenerateToken`, `IsAllowedOrigin`, `ReadCappedBody`,
  `AcquireFreePortHolding`), and 2 via uninitialized-object +
  field-injection for instance methods (`BearerMatches`,
  `IsAllowedHost`). The `<Port>k__BackingField` reflection trick
  let the instance methods reach their `Port` dependency without
  going through the ctor's TCP-port acquisition.
- Added **17 new tests** in
  `tests/Reactor.Tests/PreviewCaptureServerTests.cs`:
  - **GenerateToken (2)** — 43-char url-safe base64 (no `+`/`/`/`=`)
    + RNG generates distinct tokens across calls. Pin: a regression
    that statically initialised the RNG buffer would emit a fixed
    token (catastrophic auth weakening across launches).
  - **IsAllowedOrigin (Theory × 6 accept + Theory × 6 reject + 1 gap
    pin)** — vscode-webview, http://127.0.0.1, http://localhost,
    https://localhost arms all hit. Rejects: arbitrary HTTP/HTTPS,
    file://, ftp://, empty, `http://evil.com/localhost`. Documented
    a real product gap (see surprises below).
  - **ReadCappedBody (4)** — TASK-023 4 MB cap enforcement: read
    within cap, throw on over-cap, empty stream returns empty
    string, exact-cap boundary succeeds (the `total > cap` not
    `total >= cap` boundary).
  - **AcquireFreePortHolding (1)** — returns a real loopback-bound
    TcpListener with a valid ephemeral port; test cleans up the
    holder. Pin: a regression that returned port 0 (without binding)
    would let an attacker race in between Reactor and HttpListener.
  - **BearerMatches (6)** — null/empty/missing-prefix/correct/wrong-
    same-length/wrong-length/whitespace-trim. Pin: the constant-
    time XOR comparison contract and the `presented.Length !=
    expected.Length` early-out (without which a malformed header
    would IndexOutOfRange).
  - **IsAllowedHost (5)** — null/empty/127.0.0.1:port/localhost:port
    (case-insensitive)/DNS-rebinding rejection
    (`attacker.com:54321` even with matching port)/wrong-port
    rejection. Pin: the DNS-rebinding defense at the
    host-header layer (TASK-020), the actual security fence
    backing the advisory CORS allow-list.
- **Surprises / non-obvious findings:**
  - **REAL SECURITY FINDING: `IsAllowedOrigin` has a StartsWith-based
    subdomain attack surface.** The current code uses
    `origin.StartsWith("http://localhost", OrdinalIgnoreCase)` —
    which matches `http://localhost.evil.com` because that string
    genuinely starts with "http://localhost". Currently this is
    backstopped by the `IsAllowedHost` host-header check (the actual
    security fence), but the advisory CORS allow-list is over-broad
    against direct browser-driven attacks. **Filed as a deferred
    follow-up.** The fix: require the next char after "localhost"
    to be `:`, `/`, or end-of-string. The test
    `IsAllowedOrigin_StartsWith_Has_Known_Subdomain_Gap` pins the
    current behavior so a tightening fix reverts predictably (the
    test would need to flip from Assert.True to Assert.False).
  - **Test surface via uninitialized-object reflection** worked
    cleanly for the two instance methods that don't touch the
    host-bound fields (`_dispatcherQueue`, `_window`, etc.).
    Setting `_authToken` for `BearerMatches` and the
    `<Port>k__BackingField` for `IsAllowedHost` is enough. A future
    test could exercise the JSON request/response shapers
    (`ServeStatus`, `ServeFrame`, `ServeComponents`) similarly,
    by mocking `HttpListenerResponse` — though that's a heavier
    lift and not strictly catch-arm coverage.
- **Coverage delta** (merged):
  **80.55% → 80.68% line (+0.13)**, **68.70% → 68.85% branch (+0.15)**.
  Smaller than iteration 9 (TaskbarOverlay was simpler / smaller; the
  PreviewCaptureServer ctor + ListenAsync + capture loop + HTTP
  handlers all remain uncovered) but the per-test ROI is high:
  17 tests for +0.13% on a 665-line file with the highest-value
  net-new arms being the security pins.
- **Hand-off:** Three new files exhausted from the "deferral
  candidates" list now have real coverage. The remaining 0% files
  in the worklist (`TrayFlyoutHostWindow`, `ChartAutomationPeer`,
  rest of `PreviewCaptureServer`'s HTTP machinery, large parts of
  `JumpListComInterop`) are genuinely host-bound — the catch-arm
  scout has been thorough. The next batch of metric wins really does
  require either (a) the deferral approval (~1.6%) or (b) selftest
  fixtures for Reconciler.Mount / Reconciler.Update. Documented as
  next-session triage. The remaining unit-test runway in mid-tier
  pure-C# code is probably ≤ 1 percentage point, gained across
  ~10 more iterations at the current pace.

### 2026-05-17 — LayoutEtwConsumer initial-state + helpers (machine B, eleventh pass)

- Baseline: **80.68% line / 68.85% branch**.
- Area picked: **`Hosting/Etw/LayoutEtwConsumer.cs`** (26.9% / 490
  missed). Existing 3 tests already cover Dispose-without-start +
  double-Start + post-Dispose IsRunning. Strengthened the file's
  catch-arm coverage and pinned the public-constant contracts
  without standing up a real ETW session.
- Added **10 new tests** to existing `LayoutEtwConsumerTests.cs`:
  - **Initial state (1)** — counters zero, IsRunning/IsUnavailable
    false, UnavailableReason null. Pin: a regression that init'd
    counters via null-backed Interlocked.Read would NRE on first
    diagnostic poll.
  - **Public constants (2)** — `SessionNamePrefix` value pin
    (orphan-cleanup matches against this), `XamlProviderGuid` pin
    (a typo'd GUID would silently subscribe to the wrong ETW
    provider with no session-level diagnostics).
  - **Stop / Dispose lifecycle (2)** — Stop without Start is a
    silent no-op (the early-out guard); Dispose idempotent (the
    `_disposed` guard's second-call path).
  - **IsProcessAlive private static (5, via reflection)** — pid ≤ 0
    (Theory × 3) → false without calling GetProcessById; current
    process → true; bogus high PID → false (the
    ArgumentException catch arm — that's what lets orphan-session
    cleanup actually clean up).
- **Did NOT call Start() in the new tests** to avoid leaking
  real ETW sessions across test runs. The existing 3 tests already
  exercise the Start path; the new tests cover the parts that
  don't depend on platform success/failure.
- **Test results:** 13/13 LayoutEtwConsumer tests pass; 8,006/8,052
  full unit suite (was 7,996 — clean +10).
- **Coverage delta** (merged):
  **80.68% → 80.70% line (+0.02)**, **68.85% → 68.83% branch (-0.02)**.
  Smallest yield this session. Two reasons:
  1. LayoutEtwConsumer is 551 lines with most of the body inside
     `OnEtwEvent`, `ProcessLoop`, payload decoders, orphan cleanup —
     all reachable only with a real ETW session active.
  2. Branch% wiggled -0.02 from the selftest leg's natural noise
     (a few rerolls of the ETW-bound branches that don't hit
     deterministically across machines). The unit-only leg of this
     commit added net-positive coverage; the merge dilution
     averaged it down.
- **Hand-off note:** the unit-test runway is now genuinely tapped.
  This session ran 8 iterations from 79.82% → 80.70% (+0.88%).
  The remaining gap to 85% (4.30 pts) needs:
  - **Deferral approval** for `PreviewCaptureServer`'s remaining
    HTTP machinery (~390 lines uncovered), `TrayFlyoutHostWindow`
    (230), `ChartAutomationPeer` (308), and `JumpListComInterop`'s
    COM body (~300). Together that's ~1,228 lines = ~1.2 pts.
  - **Selftest fixtures** for `Reconciler.Mount`'s remaining 5
    points worth (932 missed, mostly `UpdateXxx` element-specific
    handlers) and one or two `Reconciler.Update` element clusters.
    Each fixture adds 30-100 covered lines.
  - **A second pass at the deferral candidates already documented**
    (`PreviewCaptureServer.cs`, `JumpListComInterop.cs`,
    `TaskbarOverlay.cs`, `TrayFlyoutHostWindow.cs`) — though some
    are now partially covered, the rest may need
    `[ExcludeFromCodeCoverage]` on the genuinely-host-bound
    methods within those files (per-method exclusions, not
    per-file).

### 2026-05-18 — PropertyGrid EditChain + statics (machine A, twelfth pass)

- Baseline at start: **80.70% line / 68.83% branch**.
- Area picked: **`Controls/PropertyGrid/PropertyGridComponent.cs`**
  (68.4% line / 57.6% branch / 178 missed) — the `EditChain` internal
  class and the file's static helpers (`RenderReadOnlyValue`,
  `IsPrimitiveOrEnum`). Picked because:
  - The file's `Render()` and `BlankButton` are host-bound
    (`SolidColorBrush`, `FlexColumn` mount-time, `WithFlyout`) — but
    the bottom half of the file (≈110 lines of `EditChain` + 3
    static helpers) is pure C# with substantial branch logic.
  - `EditChain.CannotPropagate` has 5 distinct decision points
    gating the read-only-fallback in `RenderEditor` — each one
    represents a real product-bug shape (e.g. "mutable ancestor
    in chain makes leaf editable even without leaf SetValue").
  - **Auditing existing tests** (`PropertyGridDecompositionTests.cs`):
    `EditChain` was already partly exercised by
    `Fully_Immutable_Root_Fires_OnRootChanged` and
    `EditChain_Propagates_Through_Multiple_Immutable_Levels`, but
    `CannotPropagate`, `PropagateNewOwner`, `BuildPath`, and the
    "mutable ancestor absorbs composed child" branch of
    `PropagateImmutableEdit` (lines 379-384) had zero coverage.
- **Picked up the untracked `PropertyGridDefaultsTests.cs` from a
  prior session's worktree** (14 tests, all passing). Targets
  `PropertyGridDefaults` templates: `PropertyLabelTemplate` (label
  fallback, AutomationName prefix, indent×4 margin, tooltip),
  `PropertyRowTemplate` (FlexRow shape, editor AutomationName,
  indent×16 padding), `ArrayItemTemplate` (expand glyph
  ▶/▼, toggle inversion, [N] bracket, 3/4/6-child branches,
  remove ✕ callback). `ArrayToolbarTemplate` skipped — calls
  `.SemiBold()` which dereferences `FontWeights.SemiBold`, a WinRT
  activation factory that throws COMException without packaged WinUI
  (same trap class as iteration 4's ColorCompact and iteration 7's
  brush-shaped CellRenderers).
- Added **31 new tests** in
  `tests/Reactor.Tests/Controls/EditChainTests.cs`:
  - **BuildPath (2)** — empty-chain returns just the property name;
    multi-level joins with `.` (pin: a regression to `/` would
    break every saved expand-state key).
  - **CannotPropagate (6)** — direct SetValue short-circuits false;
    empty-path + Compose-less root + null callback → true (the
    only "read-only" terminal); empty-path + OnRootChanged → false;
    empty-path + Compose → false; path-entry SetValue → false
    (mutable ancestor unfreezes a leaf); path-entry SetValue=null
    AND Compose=null → true (terminal mid-chain).
  - **PropagateNewOwner (4)** — empty-path hits OnRootChanged with
    the new owner; mutable ancestor `SetValue` returning Same(parent)
    stops propagation (pin: a regression that always invoked
    OnRootChanged would do a redundant root reassignment on every
    leaf edit); multi-level Compose chain reaches root with
    fully-immutable hierarchy; chain entry with neither SetValue
    nor Compose silently drops.
  - **PropagateImmutableEdit (2)** — mutable-ancestor branch
    (lines 379-384): Compose builds new immutable child, then
    SetValue on the mutable holder absorbs it. Pin: dropping the
    SetValue call after Compose would lose the edit silently.
    No-Compose-chain + no-Compose-root + callback → silent drop
    without NRE.
  - **RenderReadOnlyValue via reflection (6)** — bool true →
    `ToggleSwitchElement.IsOn=true` with `Modifiers.IsEnabled=false`;
    bool null → IsOn=false (the `?? false` coercion);
    string → `TextFieldElement` with `Modifiers.IsEnabled=false`;
    string null → Value=""; other-type → `TextBlockElement` with
    `ToString()` content; other-type null → `(null)` sentinel
    (pin: a regression to empty would visually hide null values).
  - **IsPrimitiveOrEnum via reflection (Theory × 11)** — int/long/
    double/bool/byte = primitive; string + decimal = special-cased
    true; enum = true; object/class/record = false. Pin: the
    `IsPrimitive || IsEnum || string || decimal` predicate exactly,
    so that decimals don't get auto-decomposed in the grid.
- **Test results:** 45/45 new (14 PropertyGridDefaults + 31
  EditChain) pass; **8,053 / 8,099 unit suite pass** (was 7,996 — clean
  +57; one transient WindowPersistedScopeIsolation flake on first run
  passed cleanly on second).
- **Coverage delta** (merged):
  **80.70% → 80.79% line (+0.09)**, **68.83% → 69.00% branch (+0.17)**.
  Branch swing 1.9× line — again validating the branch-shaped-target
  heuristic. The 45 new tests hit ~70 net-new lines on a 101k
  denominator (≈0.07% by arithmetic; the 0.02% surplus came from
  incidental Element-record / FieldDescriptor constructor coverage).
- **Surprises / non-obvious findings:**
  - **`TypeRegistry.Register` is generic-only**
    (`Register<T>(TypeMetadata)`). My first draft used
    `Register(typeof(X), ...)` and didn't compile. The next
    agent should remember: to inject a Compose-less metadata
    for an existing record-shaped type, call the generic
    overload — there's no `Register(Type, ...)` non-generic.
  - **`Decompose` on a Compose-less re-registration**: when you
    `Register<T>(new TypeMetadata { Decompose = oldMeta.Decompose })`
    you can keep field decomposition while explicitly removing
    Compose, which is exactly the setup needed to test the
    "terminal mid-chain Compose=null" branch.
  - **The `WindowPersistedScopeIsolation` test fails intermittently
    in parallel runs** — there's a `tools/flake-loop.ps1` and
    `.flake-runs/` from prior work tracking it. It's pre-existing,
    not caused by this iteration. The coverage script should retry
    on transient failures; consider adding `-MaxRetry 1` to
    `run-coverage.ps1` for the unit leg.
- **Hand-off:** This iteration validates that the PropertyGrid
  family still has unit-testable surface despite the WinUI-bound
  `Render()` method — the pure-C# helpers and the EditChain logic
  are the testable core. Same shape as the iteration-8 lesson:
  "host-bound files often have a pure-C# core that's underrated."
  The remaining mid-tier unit-testable targets (per gap-report):
  - `DevtoolsPropertyTools.cs` (714 missed, U) — reflection over
    records, the biggest remaining unit-only hot spot. The most
    valuable next pick if focusing on raw line gain.
  - `ReflectionTypeMetadataProvider.cs` (sibling of TypeRegistry,
    PropertyGrid family) — likely ≤30 lines uncovered after the
    existing TypedColumnsBehaviorTests, but worth a 10-min audit.
  - The deferral approval discussion (still pending) — would
    jump the metric ~1.2 points without writing a single test.
