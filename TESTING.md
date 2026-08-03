# Testing Reactor

Reactor has three test suites. Each lives in its own project, so there are no filters to remember — one command per suite.

| # | Suite | Project | Runner | What it tests |
|---|-------|---------|--------|---------------|
| 1 | **Unit** | `tests/Reactor.Tests` | xUnit | Algorithms, reconciliation, Yoga layout, hooks, D3 — no WinUI window |
| 2 | **Selftest** | `tests/Reactor.SelfTests` | MSTest (wraps TAP subprocess) | Full reconciler pipeline against real WinUI controls, in-process |
| 3 | **E2E** | `tests/Reactor.AppTests` | MSTest + winapp ui | Cross-process UIA validation, real user input |

## Three commands

```bash
# 1. Unit
dotnet test tests/Reactor.Tests -p:Platform=x64

# 2. Selftest (in-process WinUI, ~10s; no filter needed)
dotnet test tests/Reactor.SelfTests

# 3. E2E (requires the winapp CLI)
dotnet test tests/Reactor.AppTests

# All three
dotnet test tests/Reactor.Tests && dotnet test tests/Reactor.SelfTests && dotnet test tests/Reactor.AppTests
```

Both `Reactor.SelfTests` and `Reactor.AppTests` declare a `ProjectReference` to `Reactor.AppTests.Host` with `ReferenceOutputAssembly="false"`, so `dotnet test` rebuilds the Host first. No stale binaries.

## When to write which test

| If you're testing… | Write a… |
|---|---|
| An algorithm, pure function, record equality, hook bookkeeping, D3 math — anything that doesn't need a WinUI window | **Unit test** in `tests/Reactor.Tests/` |
| How an element mounts/updates against a real WinUI control, layout math against real Yoga+XAML, reconciler behavior end-to-end, assertions via `VisualTreeHelper` | **Selftest fixture** in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/` (registered in `SelfTestFixtureRegistry`, wrapped by a `[TestMethod]` in `SelfTestBatch`) |
| Real user input (clicks, keystrokes, tab navigation), UIA properties as seen by assistive tech, cross-process behavior, XAML Island interop | **E2E test** in `tests/Reactor.AppTests/Tests/` |

Rule of thumb: start with a unit test. Drop to selftest only when you need a live control. Reach for E2E only when you need cross-process UIA — E2E is the slowest and flakiest tier.

### Mutation checks must prove the mutation ran

Before interpreting a green mutation run, verify the source diff contains the intended edit at
exactly the target site, the build and test processes both exited successfully, and the product
assembly containing that source was rebuilt after the edit. Do not infer success by matching
stdout: pipelines can swallow failures and stale binaries can still report `Passed`.
CRLF-sensitive replacements, ambiguous anchors, failed compilation, node races, and
timestamp-preserving restores can all make a mutation look tested when it was absent or applied
elsewhere. After restoring, require `git status --porcelain` to match the pre-mutation state
rather than treating a subsequent passing test as proof of the restore.

### Judging whether a flake fix worked

N clean runs only support a fix if `(1-p)^N` is small for an observed failure rate with
`0 < p < 1`. At `p = 0.25`, three consecutive passes happen **42%** of the time, and roughly
`N = 10` is needed for ~95% confidence. Synthetic-input E2E tests for timing defects often have
`p` fixed at `0` or `1` by queue ordering; reruns then have zero statistical power, so
mutation-test the detector instead. Prefer a *mechanism* that explains the observed failure text
over any number of green runs; run counts corroborate a cause, they don't establish one.

**Budget increases only refute one class of race.** Raising a poll/wait budget and still failing
rules out "we sampled too early". It does not rule out an event that never fires, an ordering
race decided before the first poll, or a lost wakeup — all budget-insensitive. The sound
conclusion is "not a *too-short-poll* problem", which is narrower than "not a race". Don't let
the narrower finding promote a fixture into an "environmental" bucket it then stops being
investigated in; confirm that label by running it on a quiet machine with a known
window-manager state, and only apply it if it goes clean there.

---

## 1. Unit tests (`tests/Reactor.Tests`) — xUnit

xUnit tests covering framework internals **without a WinUI window**: element creation, reconciliation algorithms (LIS, keyed/positional), Yoga layout, localization, property hashing, control pooling, hooks, D3 charting math.

**When to run:** after any code change. Fast, no prerequisites beyond the .NET SDK.

```bash
dotnet test tests/Reactor.Tests

# Run a specific test class
dotnet test tests/Reactor.Tests --filter "FullyQualifiedName~ReconcilerMountUpdateTests"
```

### Console-mutating tests need collection isolation

Tests that write to `Console.Out`/`Console.Error` must be grouped with `[Collection("ConsoleTests")]` to prevent cross-test interference.

### Repo lints ride in this tier

Some tests here parse repo *sources* with Roslyn rather than exercising Reactor at runtime, so a gallery or docs edit can fail `dotnet test tests/Reactor.Tests` with no code change at all. The ones under `Tooling/`:

| Test | Fails when |
|---|---|
| `GallerySnippetAgreementTests` | A `SampleCard` snippet uses a lowerCamelCase name that does not exist in the live code beside it — the snippet renamed, or the card did and the snippet did not. Direction is snippet → live only: a snippet may omit, it may not invent. |
| `GallerySampleLintTests` | An ItemsView view builder returns something other than an item-container root; a shape paints with `.Background(...)` instead of `Fill`/`Stroke`; or an `ms-appx` asset is missing or not copied to the output. |
| `SearchIndexGeneratorTests` | `samples/ReactorGallery/reactor-search-index.json` is stale. Regenerate with `dotnet run --project tools/Reactor.SearchIndex`. |

A snippet-agreement failure names the page, line, card title and identifier, and says which of the two sides to move. There is no allowlist: fix the snippet or fix the card.

---

## 2. Selftest (`tests/Reactor.SelfTests`) — MSTest + TAP

In-process checks that run inside a real WinUI window at CPU speed. Each fixture (in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`) mounts UI via `ReactorHost`, runs assertions through `VisualTreeHelper`, and emits TAP to stdout. `SelfTestBatch` launches the Host subprocess, parses TAP, and maps each fixture to a `[TestMethod]` so MSTest reports them individually. This is the **only** way to test the reconciler end-to-end against real WinUI controls.

**When to run:** after reconciler, control mount/update, or any UI-related changes.

```bash
dotnet test tests/Reactor.SelfTests
```

For faster iteration with raw TAP output, you can bypass MSTest and run the Host directly:

```bash
# Raw TAP output
dotnet run --project tests/Reactor.AppTests.Host -- --self-test

# Filter by fixture name prefix
dotnet run --project tests/Reactor.AppTests.Host -- --self-test --filter "Flex"
```

### Fixture registration is two-place — and what that does to name searches

Selftest: add the fixture to `AllFixtures` **and** to the `Create()` switch in
`tests/Reactor.AppTests.Host/SelfTest/SelfTestFixtureRegistry.cs`; miss the second and
`--list-fixtures` reports a name the run cannot produce. E2E: add it to `AllFixtures` **and**
to the `Build` switch in `tests/Reactor.AppTests.Host/FixtureRegistry.cs` — `--list-fixtures`
is selftest-only, so nothing warns you about the E2E half.

That split also makes searching for a TAP name unsafe unless you search the whole tree. TAP
carries two kinds of name and they live in different files: *fixture* names (registry only, and
the class they map to is often spelled differently — `CenterOnCurrent_UsesCursorMonitor` →
`CenterOnCurrentUsesCursorMonitor`) and *check* names (string literals in `H.Check(...)` inside
`Fixtures/`). Neither location is a superset:

| probe | blind spot |
|---|---|
| grep `SelfTest/Fixtures/` only | fixture names whose class name differs |
| grep `SelfTestFixtureRegistry.cs` only | check-only names — e.g. `WindowLevel_RuntimeFlip_Topmost`, `TabViewFill_Mounted`, `ExitTr_Removed` are all registry=0 |
| **grep `tests/Reactor.AppTests.Host/` whole** | **literal names: none — use this** |

One blind spot survives even the whole-tree grep: check names built by interpolation —
`H.Check($"A11y_Role_{role}_Mounted", …)` — match no literal anywhere, so grepping the TAP name
you actually saw returns zero. Search the invariant stem (`A11y_Role_`) instead.

Both narrow probes return a *confident zero*, which reads as "this fixture doesn't exist" and
invites re-attributing a real flake as branch-local or renamed. Both directions were hit during
one flakiness audit, and the check-only example above is the assertion at the centre of issue
#927 — a rule that silently fails on the most-discussed flake in the audit is worse than no
rule, because its user has no reason to doubt the answer. Two probes with complementary blind
spots don't compose into coverage unless you run both, and if you're running both the
whole-tree grep is cheaper than remembering why.

### Selftest waiting patterns — `Render` vs `WaitFor` vs `WaitForIdleAsync`

Selftests run against a real WinUI dispatcher: most user-visible work (template realization, layout, content-presenter materialization, control intrinsic `Loaded` handlers) lands on *later* dispatcher waves, not synchronously with the mount call. Always wait on a concrete idle signal — never `Task.Delay(<n>)` — and pick the right primitive for the host you're driving:

| You want to… | Use | Why |
|---|---|---|
| Pump the dispatcher once because you only need an event queue drain (no realization, no layout) | `await Harness.Render()` | Drains the **shared harness host** (`ReactorApp.PrimaryWindow?.Host`) to idle, runs `UpdateLayout`, yields Low, re-layouts. Cheap when nothing is dirty, but only knows about the harness window. |
| Wait until a probe predicate against the live visual tree holds | `await Harness.WaitFor(() => H.FindText("Foo") is not null)` | Re-queries the predicate each pass against the current tree. The contention-proof alternative to a one-shot snapshot. Pass `maxPasses`/`perPassMs` to tune. **The predicate must re-query** — capturing a value from before the loop defeats it. |
| Drain an **isolated** `ReactorHost` (one you constructed with `new ReactorHost(myWindow)`, not `H.CreateHost()`) | `await host.WaitForIdleAsync(); ((UIElement)window.Content).UpdateLayout();` | `Harness.Render()` only drives `ReactorApp.PrimaryWindow`'s host, **not** your isolated one. Drive the isolated host directly. Add a `Low`-priority `TryEnqueue` yield after `UpdateLayout` if you need to drain WinUI's deferred realization messages (TabView content-presenters, TitleBar template parts, etc.). |

**Anti-pattern — blind delays:**

```csharp
// ❌ Flaky: Task.Delay is a guess; it won't catch a slower runner.
host.Mount(...);
await Task.Delay(200);
await Harness.Render();    // pumps the WRONG host if `host` is isolated
var ctrl = FindFirst<MyControl>(window.Content);   // may be null on this pass
```

**Correct pattern — bounded convergence against the right host:**

```csharp
// ✅ Drives the isolated host to idle, lays out its window, then
//    polls the predicate against re-queried visual-tree state.
host.Mount(...);
await host.WaitForIdleAsync();
((UIElement)window.Content).UpdateLayout();

MyControl? ctrl = null;
for (int pass = 0; pass < 8; pass++)
{
    ctrl = FindFirst<MyControl>(window.Content);
    if (ctrl is not null) break;
    await host.WaitForIdleAsync();
    ((UIElement)window.Content).UpdateLayout();
}
```

Rules of thumb:

- **`Render` only when you literally need an event-queue pump.** Anything that may be delayed (layout, control realization, template part materialization, `Loaded` cascades, lazy content presenters) needs `WaitFor` (predicate-based) or a bounded `WaitForIdleAsync` loop — not `Render` alone.
- **`WaitFor` over a single `Render` + snapshot probe.** A one-shot probe right after `Render()` is the classic flake source on contended/AOT runners. `WaitFor` re-queries; `Render` is a single wave.
- **But NOT for negative or exact-count assertions — `WaitFor` short-circuits.** `WaitFor` evaluates the predicate *before* its first `Render`, so it returns immediately when the predicate is already true. That makes it the wrong primitive whenever the delay is itself the observation window:
  - *"X did not happen"* / *"fired exactly once"* / *"state is unchanged"* — e.g. `count == 0`, `CycleCountForTests == 1`, `IsExpanded` still true. Converting these to `WaitFor` makes them **vacuous**: they pass at t=0 without ever giving a violation time to appear. Keep an explicit `Render(N)` window and assert after it.
  - Conversely, `WaitFor` is correct for *positive eventual* conditions — a control appears, a bit flips, a callback lands — because the predicate is false until the transition completes.
  - The test: **can the predicate be true before the action you are testing?** If yes, either keep the fixed window or strengthen the predicate so it cannot (e.g. gate on `sv.VerticalOffset < 1` as well as the row text, so a still-realized row can't satisfy it while scrolled away).
- **Converting a fixed delay to `WaitFor` is not a safe default — the remediation can itself be the vacuous one.** Because `WaitFor` establishes its predicate *at the moment it returns* and nothing more, the conversion is correct for an *eventual* assertion (false at t=0, converges) and silently vacuous for a *survival* assertion — "still visible", "still expanded" — which is true at t=0 and short-circuits at zero elapsed time. Before converting, ask: **if the predicate is true the instant `WaitFor` is called, does the next assertion still mean what I think it means?**
- **Ask the same of a precondition `throw`.** Report-and-return when the fixture's other checks remain meaningful; throw loudly when the fixture is structurally invalid (a renamed reflection target) and they do not.
- **Converting a gate can strip a settle a *later* assertion relied on.** If a following `H.Check` depended on the removed `Render(N)` rather than on its own wait, it will start failing. Give each check its own `WaitFor` instead of letting it inherit someone else's delay.
- **`host.WaitForIdleAsync()` for isolated hosts.** If you constructed your own `ReactorHost`, neither `Harness.Render` nor `Harness.WaitFor` knows about it.
- **Save and restore `ReactorApp.ActiveHostInternal`** when using an isolated host so subsequent fixtures see the shared host they expect (`var prev = ReactorApp.ActiveHostInternal; try { … } finally { if (prev is not null) ReactorApp.ActiveHostInternal = prev; }`).

### Suite duration budget — and why a single arbitrary fixture failure may not be about that fixture

The whole `--self-test` subprocess runs under **one shared process budget**. When it expires, the wrapper kills the Host and attributes the kill to whichever fixture happened to be in flight. That attribution is **positional, not causal** — the named fixture is not shown to be at fault, and the name changes from run to run.

This shape cost six separate investigations before it was diagnosed (issue #988), because the report looks exactly like a normal assertion failure. **Read the abort reason before debugging the named fixture:**

| `_abortedReason` prefix | What actually happened | What to do |
|---|---|---|
| `Run aborted by dispatcher-starvation hang on fixture 'X'` | The Host's off-dispatcher watchdog saw no fixture progress for 60 s, named `X`, and fast-failed the process. **Causal** — `X` is the culprit. This is the ordinary hang path. | Debug `X`. `--filter X` reproduces it. Set `DOTNET_DbgEnableMiniDump=1` for a dump. |
| `Run aborted by dispatcher-starvation hang on fixture 'X' (FailFast did not land; the wrapper's budget killed the process)` | Same signal, but the process would not die even under `FailFast`. **Still causal**, and the extra clause is the diagnosis: it points below the CLR — a native lock or a wedged UI thread. | Debug `X` as above, but expect a native cause. A dump is worth more than a managed stack here. |
| `Run aborted: suite exceeded its <N>s budget with fixture 'X' in flight (POSITIONAL attribution…)` | The suite ran out of its shared budget with no hang signal. `X` was merely in flight. | Look at the reported elapsed-vs-budget, **not** at `X`. `--filter X` removes the other ~1400 fixtures that shared the budget — it removes the cause, so it passes whether or not `X` is healthy, in *either* direction. |
| `Run aborted after fixture 'X'` / `Run aborted before fixture 'X'` — with ` (Host died mid-run with no '# Total failures:' trailer — NOT a budget kill; issue #978)` | The Host process stopped mid-fixture on its own (usually a native crash). **This is not #988**, and raising the budget does nothing for it. | Read the exit-code classification in the message first. Check the tail output and stderr for the native fault. |
| Same prefixes, with ` (Host finished its run, then exited abnormally)` | The Host reached the end of its run — the trailer is present — and then failed to exit cleanly, **or** it ran to completion without ever naming the missing fixtures. | Look at teardown, not at `X`. Issue #680 (0xC0000005 at final process exit) is one known cause; the other is two-place fixture registration — a name `--list-fixtures` reports that the run's `Create()` switch does not produce. |

> **Do not let the two "arbitrary victim" failures collapse into one.** A budget kill (#988) and a silent mid-run death (#978) look nearly identical from outside: one fixture blamed, everything after it missing, and the victim moving between runs. The discriminator is the **`# Total failures:` trailer** — a budget kill interrupts a Host that *would* have printed it, whereas a Host that dies mid-fixture never reaches it. Prefer the trailer over elapsed time: timing corroborates but cannot decide, because a slow machine and a fast crash produce overlapping durations. The abort reasons above state which one fired, so this is readable off any skipped fixture without a re-run.

> **A third mode is deliberately absent from the table: an all-green run whose process still exits non-zero.** Every fixture reports `ok`, the trailer is present, nothing is skipped — and the Host then faults on the way out (`STATUS_STOWED_EXCEPTION` / `0xC000027B` is the usual one under WinUI). There is no `_abortedReason` for this and there should not be: nothing was interrupted and no fixture can be blamed, so a row keyed on an abort prefix would be unreachable. It surfaces as its own named failure instead — **`HostProcessExitsCleanly_NoTeardownCrash`** — which prints the exit code with its NTSTATUS interpretation. **If that test is the only red one, read the classified code and look at teardown: not at a fixture, and not at the budget.** Note this diagnosis does *not* reach the coverage leg: `tools/coverage/run-coverage.ps1` runs the Host directly under `dotnet-coverage` rather than through this wrapper, so the same fault there appears only as a non-zero exit from the collect step.

Positional attribution means **unproven, not exonerated**. The absence of a `HANG_DETECTED` signal does not clear `X`: the watchdog can be disabled by env or by an attached debugger, and a pathologically slow or order-dependent fixture can pump the dispatcher often enough never to trip it. Start with suite duration; if duration looks normal, `X` becoming the suspect again is the one scenario that fits.

> **The trailer states what the Host *got to do* — not *which binary did it*.** Every reading above assumes the Host you ran was built from the tree you are debugging, and `--no-build` does not check that. If the preceding build failed and an earlier binary is still on disk, the run proceeds against the stale one and emits a byte-identical trailer, exit code and TAP plan. Nothing in the output distinguishes it. Note the direction: this fails toward **a false green, which ends an investigation**, whereas the more familiar staleness traps produce a false red, which starts one. So whenever you pass `--no-build`, make the build a separate step that fails closed:
>
> ```powershell
> dotnet build tests/Reactor.AppTests.Host -c Debug -p:Platform=x64
> if ($LASTEXITCODE -ne 0) { throw "build failed - selftest output would be STALE" }
> dotnet run --project tests/Reactor.AppTests.Host --no-build -c Debug -p:Platform=x64 -- --self-test --filter "<Prefix>"
> ```

Fixtures with no result are reported **Skipped (`Assert.Inconclusive`)**, not passed. A skipped fixture carries no information about itself.

**The budget is a backstop, not the hang detector.** Two Host-side watchdogs detect hangs and both name a culprit: a per-fixture graceful timeout (emits `not ok <n> <fixture>_TIMEOUT`) and the 60 s off-dispatcher watchdog (emits `Bail out! HANG_DETECTED: <name>`). The wrapper cap only fires when both were unable to. So it is sized as *"the suite could not legitimately take this long"*, not *"the suite normally takes this long"* — raising it does not delay hang detection.

| Knob | Default | Purpose |
|---|---|---|
| `REACTOR_SELFTEST_TIMEOUT_SECONDS` | 900 | Hard process budget. Malformed or non-positive values fall back to the default. |
| `REACTOR_SELFTEST_HANG_TIMEOUT_SECONDS` | 60 | Host off-dispatcher hang watchdog. **`0` or negative disables it entirely** (useful when attaching a debugger, which also auto-disables it); malformed values fall back to 60. |
| `REACTOR_SELFTEST_VIZ_PACING` | unset | Set to `1` to restore the human-observable pacing in the docking visual-demo fixtures. |

**Keeping the margin.** The Host emits `# Suite elapsed: <seconds>` and `# Fixture time: <name> <ms>` as TAP comments (inert to `ParseTap` and to CI's `^not ok ` greps), and `SelfTestBatch.SuiteDuration_WithinBudget` reports elapsed / budget / % every run. Above `SuiteDurationWarnSeconds` it also reports Inconclusive — deliberately not a failure, since duration depends on runner speed and a hard gate here would itself be a flake. If you see that warning, trim suite time or raise the budget **deliberately**, rather than discovering the cap in an unrelated red PR.

> **Where the number actually shows up on CI: the job summary, not the log.** Measured, because the obvious channel does not work: under `dotnet test` the tests run in a child `testhost` process whose stdout the runner does **not** forward. A probe writing a `::warning::` line via `Console.WriteLine` *and* via the raw standard-output handle produced neither marker at `console;verbosity=normal`, and `Assert.Inconclusive`'s message was not shown either — the run printed only `Skipped SuiteDuration_WithinBudget`. So the duration report is written to `$GITHUB_STEP_SUMMARY` (plain file I/O by the test process, immune to whatever the runner does with stdout) and renders on the run page. The console lines are kept for local `vstest`/IDE runs, where they *are* visible. If you add another diagnostic here, check which channel it lands in before trusting it — a report nobody receives is the same silent erosion this section exists to prevent.

To find what to trim, rank the per-fixture times:

```powershell
Select-String -Path run.tap -Pattern '^# Fixture time: ' | ForEach-Object {
  $p = $_.Line.Substring(16).Trim() -split '\s+'
  [pscustomobject]@{ Name = $p[0]; Ms = [double]$p[1] }
} | Sort-Object Ms -Descending | Select-Object -First 20
```

Before converting a fixed wait you find that way, re-read the `WaitFor` short-circuit rule above — several of the slowest fixtures spend their time on *stimulus* (repeated flips feeding a bounded-growth leak guard, framerate sampling), and shortening those silently weakens the assertion instead of speeding up the suite.

> **Already tried and rejected: replacing `Harness.Render`'s `Task.Delay(16 + ms)` with a compositor-frame wait.** It is the obvious target — 2718 call sites all paying a fixed ~16 ms, nominally ≥43 s of the suite — so it will be proposed again. Measured, it does not pay:
>
> | arm | host-reported suite elapsed |
> |---|---|
> | `CompositionTarget.Rendering` frame signal, capped at 16 ms | 306.6 s, 293.4 s, + one run that died natively at fixture 1366/1401 |
> | unconditional `Task.Delay(16 + ms)` (shipping behaviour) | 328.0 s (cold), 300.8 s, 300.3 s |
>
> Interleaved on one machine, alternating arms, three pairs: **≈0–3 %**, inside the run-to-run noise. The reason it doesn't pay is the same reason it looks attractive — subscribing to `CompositionTarget.Rendering` makes the UI thread produce frames continuously, so the frame arm buys back most of the delay it saves. And the delay is not arbitrary padding: it is a documented guard against fixture transitions outpacing the compositor and faulting natively, which is what the truncated run above looks like.
>
> An **earlier** measurement of the same change showed a 14.4 % win. It was wrong: the "legacy" arm it compared against still subscribed the frame counter before checking the opt-out flag, so the baseline carried the continuous-frame cost and the win was mostly the instrument. If you re-attempt this, verify the control arm is byte-for-byte the shipping path before believing any number it produces.

### The two `CenterOnCurrent` fixtures fail — or skip — as a pair, for two different reasons

`CenterOnCurrent_UsesCursorMonitor` (`Phase1WindowingFixtures.cs`) and
`PersistPlacement_FallbackWhenEmpty` (`Phase3WindowingFixtures.cs`) are **selftest** fixtures,
not unit tests — don't go hunting for them in `Reactor.Tests`. They are the *same assertion
twice*: both open a `WindowStartPosition.CenterOnCurrent` window and check its centre lands in
the work area of the monitor under the mouse, so they move as a **pair** — seeing only one of
them is the surprise, not seeing both. Neither is a render-timing race, so `WaitFor` will not
help. **Two different mechanisms in two different environments — check which one you are in
before theorising:**

- **Non-interactive session (CI agents, RDP-disconnected, locked, headless).** `GetCursorPos`
  returns **ACCESS_DENIED (err 5)** and never writes its `out` param, so the cursor monitor
  cannot be determined at all — a **100% deterministic** condition, not a flake. Confirmed by
  direct P/Invoke probe on two separate machines. Both fixtures now `H.Skip` here rather than
  assert, so the expected symptom on such a machine is a **skip, not a red**; a red means this
  is not your mechanism. Note `System.Windows.Forms.Cursor.Position` **hides** the condition —
  it surfaces the uninitialised `(0,0)` instead of the failure, so probe `GetCursorPos(out p)`
  directly (`False` / `LastError=5`) rather than through it.
- **Interactive multi-monitor box.** A TOCTOU: `GetCursorPos` is sampled *before*
  `OpenAndSettle`, so a cursor crossing a monitor boundary while the window opens invalidates
  the captured work rect. Intermittent, and **structurally impossible on a single display** —
  if you are on one virtual desktop with no boundary to cross, this is not your mechanism.
  This is the only one of the two that still produces a red.

Do not assume "quiet machine ⇒ passes": that holds only in the interactive case.

### Running selftests under NativeAOT

The Host app supports an AOT-published build so the selftest suite doubles as Reactor's primary AOT regression gate. The framework itself is AOT-clean (see [`docs/aot-support.md`](docs/aot-support.md)) but a meaningful slice of selftest *fixtures* still trip over reflection paths the AOT compiler can't preserve. Those fixtures are pre-skipped via a baked-in pattern list so the run completes and the remaining failures are visible.

**1. Publish the Host with AOT.** The publish step shells out to MSVC's `link.exe`, so it must run inside a Visual Studio Developer environment. From a Developer Command Prompt / Developer PowerShell (or after sourcing `Launch-VsDevShell.ps1`):

```powershell
dotnet publish tests/Reactor.AppTests.Host `
    -c Release -p:Platform=x64 -r win-x64 `
    -p:PublishAotInternal=true --self-contained `
    -o artifacts/aot-host
```

`PublishAotInternal=true` is the internal opt-in property that flips `PublishAot` on for the Host (kept opt-in so an ordinary `dotnet build Reactor.slnx` doesn't pay the AOT compile cost). Swap `-r win-x64` / `-p:Platform=x64` for `win-arm64` / `ARM64` on ARM machines.

`-o artifacts/aot-host` pins the publish output to a stable, predictable path. Without it, the binary lands under the default-shape `tests/Reactor.AppTests.Host/bin/<Platform>/<Config>/<TFM>/<RID>/publish/Reactor.AppTests.Host.exe` — fine, but the TFM/RID/SDK-version segments drift over time, so the explicit `-o` is friendlier for scripts and docs.

**2. Run the suite.** Same `--self-test` flag as the JIT build:

```bash
./artifacts/aot-host/Reactor.AppTests.Host.exe --self-test
```

Output is the same TAP stream as a normal selftest run. The runner detects AOT at startup (`RuntimeFeature.IsDynamicCodeSupported == false`) and emits `# SKIP crashes/hangs under NativeAOT` lines for known-bad fixtures.

**3. Filtering known-bad fixtures.** The skip list lives in `DefaultAotSkipPatterns` in `tests/Reactor.AppTests.Host/SelfTest/SelfTestRunner.cs`. Entries are either an exact fixture name or a prefix-wildcard ending in `*` — by convention these match a fixture family, e.g. `MyFamily_*`. When you discover a new AOT crasher, you have two choices:

- **Without rebuilding** (best for iteration): append patterns via the `REACTOR_AOT_SKIP` env var. They merge into the defaults — they do *not* replace them.

  ```bash
  REACTOR_AOT_SKIP="MyFixture_Crasher,SomeFamily_*" \
    ./.../Reactor.AppTests.Host.exe --self-test
  ```

- **Permanent**: add the pattern to `DefaultAotSkipPatterns` and re-publish. Leave a comment naming the family / observed crash mode so a future contributor can verify whether the underlying issue has been fixed and drop the entry.

A native crash terminates the AOT process — the per-fixture managed watchdog can't fire. Iterate by tailing the TAP output for the *last* `# Running: <name>` line before exit, add that name to the skip list, and re-run. Be conservative when wildcarding a family: many `Family_*` fixtures pass even when one member crashes.

**4. Expected pass count.** As of 2026-05-20, an AOT run of the suite produces roughly: 735 fixtures total → 192 skipped, ~543 passed, 0 failed. The skip list covers fixtures that exercise subsystems documented as not-yet-AOT-clean in [`docs/aot-support.md`](docs/aot-support.md) (PropertyGrid auto-discovery, devtools/MCP, UseObservable on POCOs, theme resource lookup, XAML-metadata-dependent control hosting). When you fix one of those subsystems, drop the corresponding entries from `DefaultAotSkipPatterns`. The non-AOT run on the same commit is 735/735 pass.

---

## 3. E2E tests (`tests/Reactor.AppTests`) — MSTest + winapp ui

End-to-end tests that use the winapp CLI (`winapp ui`) to simulate real user input (clicks, keyboard, tab navigation) through the cross-process UI Automation pipeline. These verify the full input → render → output path and validate that UIA properties are visible to assistive technology.

**When to run:** before shipping. Slow, and requires the winapp CLI.

E2E test classes (across two host apps):

| Class | Host | What it tests |
|-------|------|---------------|
| `InteractiveTests` | WinUI | Counter clicks, observable mutation |
| `AccessibilityTests` | WinUI | WCAG property validation via UIA |
| `AccessibilityInteractionTests` | WinUI | Keyboard nav, live regions, headings, semantic panels |
| `EventHandlerTests` | WinUI | OnTapped, OnSizeChanged, OnPointerPressed, OnKeyDown, UseReducer |
| `DataGridTests` | WinUI | Click-to-edit, keyboard commit |
| `WinFormsInteropTests` | WinForms | XAML Island rendering, tab navigation, UIA across boundaries |

```bash
dotnet test tests/Reactor.AppTests

# A specific class
dotnet test tests/Reactor.AppTests --filter "ClassName=Reactor.AppTests.Tests.AccessibilityTests"
```

> **Requires:** the **winapp CLI** (`winapp ui`). Install it with `winget install Microsoft.WinAppCli` (or run `./bootstrap.ps1`, which installs it for you). The harness resolves it from `%LOCALAPPDATA%\Microsoft\WindowsApps\winapp.exe` or `winapp` on PATH. Unit and selftest runs don't need it.
>
> **WinForms tests** also require `Reactor.WinFormsTests.Host` to build. It launches a separate WinForms app with a XAML Island.

When `viaSendInput: true` injects a modifier chord, it can compress the gap between key messages
enough that queue ordering makes a timing-sensitive race always won or always lost (`p` is `0`
or `1`) for that environment and tool version. If the defect is *when* state is read rather than
*whether* input arrived, prove the detector with a mutation before counting repeated green E2E
runs as evidence.

### Don't co-locate the E2E and selftest tiers

CI runs them as separate jobs on separate runners today, and that isolation is load-bearing
rather than incidental. E2E drives real pointer input, foregrounds windows and changes Z-order;
some selftest fixtures read live desktop state — the two `CenterOnCurrent`-based ones sample the
cursor's monitor while opening a real window. Running E2E first on one machine and then
`--self-test` took a clean unmodified tree from 0 to 8 failures — reproducible, and the standing
repro for those fixtures. So if you consolidate jobs to save runner minutes, or run both tiers
locally back-to-back, expect selftest reds that are an artefact of tier ordering and not of your
change. Fix the fixtures' desktop-state dependence before merging the jobs, not after.

---

## Code coverage

The canonical coverage metric is **unit + selftest merged**. Run both and merge:

```bash
# (install once: dotnet tool install -g dotnet-coverage)

# --- Unit tests ---
dotnet build tests/Reactor.Tests -c Debug -p:Optimize=false -p:DebugType=portable
dotnet-coverage collect -s coverage.settings.xml \
  --output unit.cobertura.xml --output-format cobertura \
  -- dotnet test tests/Reactor.Tests --no-build

# --- Selftest ---
# Step 1: Rebuild with explicit Debug settings (required for instrumentation)
dotnet build src/Reactor                      -c Debug -p:Optimize=false -p:DebugType=portable --no-incremental
dotnet build tests/Reactor.AppTests.Host      -c Debug -p:Optimize=false -p:DebugType=portable --no-incremental

# Step 2: Instrument Reactor.dll statically
#         (dynamic instrumentation skips referenced assemblies)
dotnet-coverage instrument \
  "tests/Reactor.AppTests.Host/bin/$(RuntimeIdentifier)/Debug/net10.0-windows10.0.22621.0/Reactor.dll" \
  -s coverage.settings.xml

# Step 3: Collect
dotnet-coverage collect -s coverage.settings.xml \
  --output selftest.cobertura.xml --output-format cobertura \
  -- dotnet run --project tests/Reactor.AppTests.Host --no-build -- --self-test

# --- Merge ---
dotnet-coverage merge unit.cobertura.xml selftest.cobertura.xml \
  --output merged.cobertura.xml --output-format cobertura
```

Replace `$(RuntimeIdentifier)` with `ARM64` or `x64`, or omit the platform segment if you used the default platform from `Directory.Build.props`. The `coverage.settings.xml` file in the repo root controls which modules are included and excludes generated code (`obj/`, `*.g.cs`) and test-host scaffolding exercised only by the winapp/E2E runner.

### When `run-coverage.ps1` aborts before the merge

`tools/coverage/run-coverage.ps1` (`-UnitOnly`, `-SkipBuild`) drives the recipe above and writes
`coverage/merged.cobertura.xml`. It **aborts before the merge step on any test failure**, so a
red selftest fixture — the `CenterOnCurrent` pair in §2 above is the usual one — leaves you with
both legs collected and no merged file. Merge them by hand:

```powershell
dotnet-coverage merge coverage\unit.cobertura.xml coverage\selftest.cobertura.xml --output coverage\merged.cobertura.xml --output-format cobertura
```

### Running coverage in CI

You don't have to run the merge locally — the **Coverage** workflow
(`.github/workflows/coverage.yml`) runs this same unit + selftest recipe and
reports the merged line/branch numbers, **compared against a cached `main`
baseline**.

- **Automatic on every PR:** it runs on each PR commit, measures the merged
  coverage of the **PR head** (one instrumented pass), and posts a **sticky
  comment** showing `base | PR | Δ` for line + branch, with a direction-aware
  status (✅ higher / ⚠️ lower / ≈ within noise). It updates in place on new
  commits. No action needed — it just works. (Doc-only PRs are skipped.)
- **Baseline refresh on `main`:** each push to `main` (re)measures main and stores
  a `coverage-baseline` artifact; PRs compare against the newest one, so they never
  re-measure main. Until the first post-merge push publishes a baseline, PRs render
  "baseline unavailable".
- **Manual trigger (PR):** use **Actions → Coverage → Run workflow** and enter the
  PR number, or run `gh workflow run coverage.yml -f pr_number=<PR>`. This measures
  that PR head (works for forks) and writes the numbers to the run's **job
  summary** — it does not post a PR comment (the poster only comments for automatic
  `pull_request` runs, where the PR is resolved from the trusted head SHA).
- **Manual trigger (branch):** run it with the PR number left **blank** to measure
  the selected branch — e.g. `gh workflow run coverage.yml --ref main` (on the
  default branch this refreshes the baseline). The **absolute** numbers are written
  to the run's **job summary**.

The measurement runs in the unprivileged `pull_request` context (it builds and
runs PR code, so it holds no write token) and uploads only the raw numbers; the
privileged `coverage-comment.yml` (`workflow_run`) checks out trusted code,
re-validates the head numbers **and** the cached `main` baseline, renders the
comparison comment, and posts it, resolving the target PR from the trusted
`workflow_run` head SHA. Measuring only the head keeps a PR at ~one instrumented
pass; a missing baseline is non-fatal (it degrades to a "baseline unavailable"
note). See [`tests/coverage/ci/README.md`](tests/coverage/ci/README.md) and the
workflow headers for the full layout + security rationale.
