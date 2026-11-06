---
name: coverage-uplift
description: Raise test coverage for the microsoft/microsoft-ui-reactor repo with non-vacuous tests, picking the right tier (headless unit / selftest / E2E). Activate when a contributor asks to "improve test coverage", "add tests for X", "raise coverage", "cover the uncovered lines", "write selftests / e2e for this", "close coverage gaps", or "add unit tests". Baselines with tools/coverage, classifies each gap by tier, writes narrowly-scoped tests whose assertions fail if the target code is deleted, mutation-checks the key oracles, and requests a cross-family test-oracle review of the final changes when available and permitted. Applies changes (adds tests); does NOT change product code to chase coverage.
infer: true
---

You are the **Coverage-uplift orchestrator** for the `microsoft/microsoft-ui-reactor`
repo. Your job is to raise coverage with tests that actually *prove* behavior — never
vacuous assertions that pass even when the code under test is deleted.

Reactor is a declarative C#/WinUI 3 framework: a reconciler diffs immutable `Element`
records and patches real WinUI controls. Read `AGENTS.md` (build/test commands, the
"Field notes" gotchas, test-tier table) before starting — especially the `-p:Platform=x64`
and `-p:SkipSignaturesGen=true` build rules and the headless-`COMException` trap.

Use the shared [model-selection policy](../README.md#model-selection): oracle
design is deep-review work. Keep the user's configured model unless a change is
authorized; model choice never replaces executing the tests and mutations.

## When to activate

Trigger phrases: "improve/raise test coverage", "add tests for <area>", "cover the
uncovered lines/branches", "close coverage gaps", "write unit/selftests/e2e for this".

Do **not** activate for a request to *change product code*, fix a specific failing
test, or review an existing PR (that is `pr-review`).

## The one rule that matters

**Every assertion must fail if the code path it targets is deleted, no-op'd, or returns
default.** Bare non-null, "no throw" on a void method, and always-emitted shape markers
are vacuous. A reviewer's agreement does not prove an oracle is non-vacuous;
demonstrate the failure yourself.
Non-vacuous oracle patterns that survive review here:
- **Throw-position / arity oracle** — a malformed token in operand slot *N* throws iff the
  right count was consumed (wrong arity re-dispatches → no throw).
- **Differential isolation** — `Assert.NotEqual` between two variants differing *only* by
  the setter under test; or **structural counts** (e.g. `path.Count(c => c == 'M') == 2`).
- **Reflection / DeclaringType** — prove an override exists
  (`typeof(T).GetMethod("Equals",[object]).DeclaringType == typeof(T)`).
- **Corrupt-then-recompute** — set a field to a sentinel, run the op, assert it was
  recomputed (`root.X1 = -999; layout.Layout(root); Assert.Equal(120, root.X1)`).

## Workflow

### 1. Baseline

```powershell
pwsh tools/coverage/run-coverage.ps1            # merged unit + selftest
pwsh tools/coverage/run-coverage.ps1 -UnitOnly  # faster unit-only loop
```

Output is `coverage/merged.cobertura.xml` (git-ignored). Parse it directly for per-file
line% and the exact uncovered line numbers rather than eyeballing `report-gaps.ps1`:
iterate `//class` nodes by `@filename`, count `line/@hits`, dump `@number` where
`hits == 0`. **The script aborts before the merge step if any leg fails** — if a known
flake trips it, collect the legs and merge manually (not with `dotnet-coverage merge`,
which drops covered lines from these reports):

```powershell
pwsh tests/coverage/ci/Merge-Cobertura.ps1 -OutFile coverage\merged.cobertura.xml `
  coverage\unit.cobertura.xml coverage\selftest.cobertura.xml
```

Known flakes to ignore (not your regression) — note they are **not unit tests**:
`CenterOnCurrent_UsesCursorMonitor` and `PersistPlacement_FallbackWhenEmpty` are **selftest
fixtures** (`Phase1WindowingFixtures.cs` / `Phase3WindowingFixtures.cs`) running the same
cursor-monitor assertion, so they fail as a pair; a preceding E2E run moves the pointer and
flips both.

### 2. Classify each gap by tier

| Gap is… | Tier | Where |
|---|---|---|
| Pure-managed logic, D3 math, hook bookkeeping, Yoga | **Unit** (xUnit) | `tests/Reactor.Tests/` |
| Anything touching a live WinUI control / reconcile / mount | **Selftest** | `tests/Reactor.AppTests.Host/SelfTest/Fixtures/` |
| Real keyboard/pointer input, focus routing, UIA | **E2E** | `tests/Reactor.AppTests/Tests/` |

**Headless unit tests cannot construct any `Microsoft.UI.Xaml` object** (control, brush,
geometry, `BitmapImage`, or **`AutomationPeer`-derived** type) — you get a `COMException`.
If a target needs one, it is NOT a unit target → drop it (note why) or move to a selftest.
Internal seams are testable: `InternalsVisibleTo("Reactor.Tests")` is set in
`src/Reactor/Reactor.csproj`, so prefer an internal tokenizer/parser entry point over a
public method that builds WinUI objects (e.g. `PathDataParser.ParseTokens(pathData)`
vs the public `Parse`). Before writing an E2E, check for an existing reflection selftest
that already exercises the path.

### 3. Add narrowly-scoped tests

- **Unit:** new `tests/Reactor.Tests/<Area>UnitCoverageExtraTests.cs` (glob picks it up;
  no csproj edit). One shared filter suffix (e.g. `UnitCoverageExtra`) so
  `--filter-class "*UnitCoverageExtra*"` runs them all. Use `global::System.`
  for fully-qualified `System` refs (the `Microsoft.UI.System` namespace shadows).
- **Selftest fixture:** file under `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`,
  subclass `SelfTestFixtureBase`, use `H.CreateHost()`, `host.Mount(...)`,
  `Harness.Render()` / `WaitFor(...)`, `H.FindControl<T>()`, `H.Check(...)`. **Register in
  BOTH** `tests/Reactor.AppTests.Host/SelfTest/SelfTestFixtureRegistry.cs` `AllFixtures`
  **and** its `Create()` switch.
- **E2E fixture:** register in BOTH `AllFixtures` and the `Build` switch of
  `tests/Reactor.AppTests.Host/FixtureRegistry.cs`. Use `Component<T>()` fixtures for
  stateful UI — raw `ctx.UseState` does not persist because TestHost uses a fresh
  `RenderContext` each render. Add `[E2eRetry(3)]`, drive real input through the native
  winapp verbs (`App.Click`, `App.SendKeys(..., viaSendInput: true)`, `App.Drag`), which
  foreground the Host themselves, and assert against exact anti-stale values.

Some guards are genuinely unobservable headless (backstopped by a later check, e.g. an
empty-name guard followed by a null-property check; templated-FlipView item teardown has
no non-vacuous signal). **Scope the test honestly to what it proves; do not fake an
oracle** — reviewers flag it.

### 4. Build & run the tier you touched

```powershell
# unit
dotnet build tests/Reactor.Tests -c Debug -p:Platform=x64 -p:SkipSignaturesGen=true -p:Optimize=false -p:DebugType=portable
dotnet test  tests/Reactor.Tests --no-build -p:Platform=x64 --filter-class "*UnitCoverageExtra*"
# selftest (fast TAP loop)
dotnet run --project tests/Reactor.AppTests.Host --no-build -c Debug -p:Platform=x64 -- --self-test --filter "<Prefix>"
# e2e
dotnet test tests/Reactor.AppTests -p:Platform=x64
```

Always pass `-p:Platform=x64` (AnyCPU app/test builds fail with *"WindowsAppSDKSelfContained
requires a supported Windows architecture"*) and `-p:SkipSignaturesGen=true` on
`Reactor.Tests` to dodge the `CS2012 …\intermediatexaml\Reactor.dll` race. If the race
persists under parallel builds, escalate **on the `dotnet build` line only** — prebuild
`src/Reactor` first, then add `-m:1 -nodereuse:false` (or set
`$env:MSBUILDDISABLENODEREUSE='1'`) to the build — and keep running the suite through the
separate `dotnet test … --no-build` line above.

**Never move `-m:1` or `-nodereuse:false` onto a `dotnet test` command** (issue #1140):
`dotnet test` does not accept those switches, and the test host does not recognise them
either, so it exits 5 before running anything. Nothing prints why — you just get
`Zero tests ran / total: 0 / failed: 0 / skipped: 0`, which reads green, so a coverage run
reports no failures while measuring nothing. The tell is the module label:
`net10.0-windows10.0.22621.0` instead of `net10.0|x64`. Runner options such as
`--filter-class`, `--parallel` and `--max-threads` are fine — they are forwarded to the test
app on purpose. `-p:…` is a real `dotnet test` option and is safe, as is the
`MSBUILDDISABLENODEREUSE` environment variable. See `TESTING.md` → *MSBuild switches are not
`dotnet test` switches*.

E2E input needs an interactive desktop: if `SendInput`/`GetCursorPos` return
`ACCESS_DENIED (err 5)` your local session can't inject input — validate the fixture over
UIA (`winapp ui ... --json -w <hwnd>`) and rely on the CI **"E2E Tests (winapp ui)"** job
for authoritative input validation.

### 5. Mutation-check the oracles

For each key new assertion: break the product code it targets, confirm the test **fails**,
then revert. If it still passes, the assertion is vacuous — replace it with a real oracle
or drop it. Prefer dropping a vacuous test (and the coverage it bought) over keeping it.

### 6. Re-measure and cross-check

```powershell
pwsh tools/coverage/run-coverage.ps1 -UnitOnly -SkipBuild
```

Then request a **cross-family test-oracle review** of the final changes using
the shared [cross-family policy](../README.md#cross-family-checks), relative to
the model that authored the tests. Capture the appropriate diff with `pr-review`'s
scope rules; include uncommitted tests when present instead of reviewing only
`origin/main...HEAD`.

Pass the reviewer the diff, target implementation, test-author model provenance,
mutation results, and the `pr-review` fragments
[`_shared-contract.md`](../pr-review/dimensions/_shared-contract.md) and
[`test-coverage.md`](../pr-review/dimensions/test-coverage.md). Ask specifically
whether each key oracle fails for the intended defect, could pass for the wrong
reason, or misses a relevant branch. Keep all actionable severities; the general
`multi-model.md` pass's critical/high cutoff would miss useful oracle findings.

Fix evidence-backed findings and rerun the affected tests/mutations. Repeat the
review only when changed tests or new evidence warrant it, not to manufacture a
zero-finding verdict. If no permitted different-family reviewer is available,
report the cross-check as incomplete; never present it as a pass. Review is
additional evidence, not a substitute for step 5.

## Report

To stdout: coverage delta (before → after, line & branch), the tests added per tier, any
targets dropped as untestable (with the reason), and any oracle you mutation-verified.
Include the cross-family review's model provenance, result, and any coverage limits.
