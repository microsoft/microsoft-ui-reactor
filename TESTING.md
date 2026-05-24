# Testing Reactor

Reactor has three test suites. Each lives in its own project, so there are no filters to remember — one command per suite.

| # | Suite | Project | Runner | What it tests |
|---|-------|---------|--------|---------------|
| 1 | **Unit** | `tests/Reactor.Tests` | xUnit | Algorithms, reconciliation, Yoga layout, hooks, D3 — no WinUI window |
| 2 | **Selftest** | `tests/Reactor.SelfTests` | MSTest (wraps TAP subprocess) | Full reconciler pipeline against real WinUI controls, in-process |
| 3 | **E2E** | `tests/Reactor.AppTests` | MSTest + Appium/WinAppDriver | Cross-process UIA validation, real user input |

## Three commands

```bash
# 1. Unit
dotnet test tests/Reactor.Tests

# 2. Selftest (in-process WinUI, ~10s; no filter needed)
dotnet test tests/Reactor.SelfTests

# 3. E2E (requires WinAppDriver)
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

## 3. E2E tests (`tests/Reactor.AppTests`) — MSTest + WinAppDriver

End-to-end tests that use Appium/WinAppDriver to simulate real user input (clicks, keyboard, tab navigation) through the cross-process UI Automation pipeline. These verify the full input → render → output path and validate that UIA properties are visible to assistive technology.

**When to run:** before shipping. Slow, and requires WinAppDriver.

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

> **Requires:** [WinAppDriver](https://github.com/microsoft/WinAppDriver/releases) installed at `C:\Program Files (x86)\Windows Application Driver\WinAppDriver.exe`. Unit and selftest runs don't need it.
>
> **WinForms tests** also require `Reactor.WinFormsTests.Host` to build. It launches a separate WinForms app with a XAML Island.

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

Replace `$(RuntimeIdentifier)` with `ARM64` or `x64`, or omit the platform segment if you used the default platform from `Directory.Build.props`. The `coverage.settings.xml` file in the repo root controls which modules are included and excludes generated code (`obj/`, `*.g.cs`) and test-host scaffolding exercised only by the Appium/E2E runner.
