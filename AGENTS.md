# Copilot Instructions — Microsoft.UI.Reactor

Reactor is a declarative, component-based C# framework for building WinUI 3 desktop apps. It renders real WinUI controls via a virtual element tree and reconciler — similar to React's programming model but targeting native Windows UI.

## Build, Test, Lint

```bash
# Build (platform defaults to machine arch for apps; libraries are AnyCPU)
dotnet build Reactor.slnx

# Unit tests — xUnit, headless, fast (~2200 tests incl. 590 Yoga fixtures)
dotnet test tests/Reactor.Tests

# Single test class
dotnet test tests/Reactor.Tests --filter "FullyQualifiedName~ReconcilerMountUpdateTests"

# Selftests — real WinUI window, in-process (~10s)
dotnet test tests/Reactor.SelfTests

# Raw TAP output (faster iteration, supports --filter prefix)
dotnet run --project tests/Reactor.AppTests.Host -- --self-test --filter "Flex"

# E2E — winapp ui CLI (install: winget install Microsoft.WinAppCli, or run ./bootstrap.ps1)
dotnet test tests/Reactor.AppTests

# Single E2E class
dotnet test tests/Reactor.AppTests --filter "ClassName=Reactor.AppTests.Tests.AccessibilityTests"
```

CI runs unit tests + selftests + full solution build on every PR. .NET 10 SDK, `windows-latest` runner.

Full testing guide — tier selection, NativeAOT runs, code coverage — in [`TESTING.md`](TESTING.md).

## Architecture

### Virtual DOM model

UI is described as **immutable C# records** (`Element` subclasses), not WinUI controls. The reconciler diffs old vs. new element trees and patches only what changed on real controls.

```
Component.Render() → Element tree (records)
                        ↓
                   Reconciler
                   ├── Mount  → creates WinUI controls
                   └── Update → diffs & patches controls
```

### Reconciler is split across partial classes

- `Reconciler.cs` — orchestration, child reconciliation, unmount, helpers
- `Reconciler.Mount.cs` — mount dispatch + composition-primitive handlers (controls mount via their registered `ControlDescriptor`/`IElementHandler`)
- `Reconciler.Update.cs` — update dispatch + composition-primitive handlers (controls update via the same registered descriptors/handlers)

### Hooks follow React rules

Hooks (`UseState`, `UseEffect`, `UseReducer`, `UseMemo`, etc.) are tracked by call order in `RenderContext`. They must be called unconditionally, in the same order every render — no conditional hooks. Pass `threadSafe: true` for cross-thread state updates.

### Echo suppression for value controls

Echo handling is a documented hybrid (spec-047 §8.3). Synchronous, exact-comparable, single-controlled-value round-trips (ComboBox, FlipView, GridView, ListBox, Pivot, PipsPager, RadioButtons, SelectorBar, TabView, TemplatedFlipView, ToggleSwitch, TextBox) use a value-diff arm (`ReactorState.PendingEchoMatch` + `ArmExpectedEcho`/`ShouldSuppressEcho`, opt-in `valueDiffEcho`). `ChangeEchoSuppressor` is **retained** as the suppress-counter fallback for the rest: doubles (Slider/NumberBox value), NumberBox coercion, CalendarView collection diff, deferred/coercion strings (AutoSuggest/Password/RichEdit), Expander, CheckBox path-B, the `ApplySetters` suppression scope, and the public `WriteSuppressed` primitive. Authors keep using the stable `WriteSuppressed` primitive (or declare `.Controlled` / `valueDiffEcho` on a descriptor) — never the suppressor directly.

### Element pooling

`ElementPool` recycles WinUI controls. Poolable types track one-time event wiring via `ConditionalWeakTable<FrameworkElement, PoolableWireFlags>` to avoid double-subscribing across rent/return cycles.

### Per-element state via attached DP

`ReactorAttached.StateProperty` stores `ReactorState` (Element pointer + `ModifierEventHandlerState` — the routed-input family, lazily allocated — + per-control `ControlEventStateBox` for control-intrinsic events) on native elements — not `FrameworkElement.Tag` or a CWT.

## Key Conventions

### Elements are immutable records

```csharp
public record MyControlElement(string Label, Action? OnClick = null) : Element;
```

Use `with` expressions for variations. Never mutate.

### Factory methods over constructors

The DSL entry point is `using static Microsoft.UI.Reactor.Factories;`. Factory methods return Element records, never WinUI controls:

```csharp
TextBlock("hello")       // not new TextBlockElement("hello")
Button("+", () => ...)   // not new ButtonElement(...)
VStack(child1, child2)   // layout containers
```

`Factories` is `public static partial class` — factory methods can be added from multiple files.

### Fluent modifiers preserve concrete types

Extension methods use `<T> where T : Element` to maintain the concrete type through chains:

```csharp
Text("Hello").Bold().Margin(16).Set(tb => tb.TextWrapping = TextWrapping.Wrap)
// Still TextBlockElement throughout the chain
```

### Adding a new WinUI control

The legacy Element-record + `MountXxx`/`UpdateXxx` dispatch-switch path is gone. The current path:

1. **Element record** in `src/Reactor/Core/Element.cs`
2. **Authoring shape** — a `ControlDescriptor<TElement, TControl>` (the primary path) or a hand-coded `IElementHandler<TElement, TControl>` for irregular controls.
3. **Register** it in `RegisterV1BuiltInHandlers`.
4. **Selftest fixture** in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`.

See [`docs/guide/extending-reactor-controls.md`](docs/guide/extending-reactor-controls.md) for the authoring-shape decision tree (prop/engine shapes, children strategies, echo handling, pooling).

Optionally: a factory method in `src/Reactor/Elements/Dsl.cs`, fluent modifiers in `ElementExtensions.cs`, and unit tests in `Reactor.Tests/`.

### Test tier selection

| Testing… | Write a… | Location |
|---|---|---|
| Algorithm, pure function, hook bookkeeping, D3 math | Unit test (xUnit) | `tests/Reactor.Tests/` |
| Element mount/update against real WinUI controls | Selftest fixture | `tests/Reactor.AppTests.Host/SelfTest/Fixtures/` |
| Real user input, UIA properties, cross-process | E2E test (winapp ui) | `tests/Reactor.AppTests/Tests/` |

Start with unit tests. Use selftests only when you need a live WinUI control. E2E is the slowest tier.

### Console-mutating tests need collection isolation

Tests that write to `Console.Out`/`Console.Error` must be grouped with `[Collection("ConsoleTests")]` to prevent cross-test interference.

### AOT compatibility

`IsAotCompatible=true` is set for all net10.0+ projects. The core Reactor library promotes IL trimming/AOT warnings to errors — new reflection usage must be annotated before merging. Non-Reactor projects (tests, samples) suppress these warnings.

### WinUI library projects

Class libraries must set `WindowsAppSDKSelfContained=false`. Only app executables own Windows App SDK self-contained packaging.

### No XAML

Everything is C#. No `.xaml` files for UI (except `ReactorApplication.xaml` which loads `XamlControlsResources` for AOT compatibility).

### User guide docs are generated

Docs under `docs/guide/` are compiled from `docs/_pipeline/templates/*.md.dt` via `mur docs compile`. Edit the templates, not the compiled output.

## Project Layout

```
src/Reactor/              Core framework
  Core/                   Reconciler, Component, Element, Hooks, RenderContext
  Elements/               DSL factories (Dsl.cs) + fluent modifiers (ElementExtensions.cs)
  Flex/                   FlexPanel — CSS Flexbox via Yoga
  Yoga/                   Pure C# port of Meta's Yoga layout engine
  Hosting/                ReactorApp entry point, render loop, hot reload
src/Reactor.Cli/          CLI tool (scaffolding, localization, preview)
src/Reactor.Analyzers/    Roslyn analyzers (theming, accessibility)
src/vscode-reactor/       VS Code live preview extension
tests/
  Reactor.Tests/          Unit tests (xUnit, headless)
  Reactor.SelfTests/      Selftest runner (MSTest, wraps TAP subprocess)
  Reactor.AppTests.Host/  Selftest host app + winapp ui fixture navigator
  Reactor.AppTests/       E2E tests (MSTest + winapp ui)
samples/                  Demo apps and samples
docs/
  guide/                  User documentation (generated from templates)
  specs/                  Numbered design specs
  reference/              API and subsystem reference
```

## Field notes (gotchas from past sessions)

Hard-won specifics that repeatedly cost sessions time. Prefer these exact commands.

### Building & running tests

- **Pass `-p:Platform=x64` for any WinUI app/test project build.** AnyCPU builds of
  `Reactor.AppTests`, `Reactor.AppTests.Host`, etc. fail with *"WindowsAppSDKSelfContained
  requires a supported Windows architecture"*. (`dotnet build Reactor.slnx` handles the
  solution defaults; single app/test projects usually do not.)
- **A green Debug build does not clear the `Build solution` CI job.**
  `TreatWarningsAsErrors` is Release-only and CI builds Release, so verify with
  `dotnet build Reactor.slnx -c Release`. If `WMC0110` / `WMC1509` follows a C# error,
  fix the earlier error; those markup errors are a cascade, not the root cause.
- **Add `-p:SkipSignaturesGen=true` to local `tests/Reactor.Tests` builds** to avoid the
  XAML-markup/SignaturesGen race: `CSC error CS2012: Cannot open '...\obj\...\intermediatexaml\Reactor.dll' ... used by another process`. If it still races under
  parallel WinUI builds, prebuild `src/Reactor` alone first, then add `-m:1`
  `-nodereuse:false` and `$env:MSBUILDDISABLENODEREUSE='1'`. (`-p:CI=true` also skips it.)
- **Fast selftest loop** (TAP `ok`/`not ok`): `dotnet run --project tests/Reactor.AppTests.Host --no-build -c Debug -p:Platform=x64 -- --self-test --filter "<Prefix>"`.
- **Headless unit tests cannot construct any `Microsoft.UI.Xaml` object** — control, brush,
  geometry, `BitmapImage`, or **`AutomationPeer`-derived** type — you get a `COMException`.
  Test only pure-managed logic + WinRT value structs/enums; push anything live to a
  selftest. Internal seams are fair game: `InternalsVisibleTo("Reactor.Tests")` is set, so
  prefer an internal tokenizer/parser (e.g. `PathDataParser.ParseTokens(pathData)`)
  over a public method that builds WinUI objects.
- In the `Microsoft.UI.Reactor.*.Tests` namespaces, `Microsoft.UI.System` shadows `System`
  — use `global::System.IO.Path`, `global::System.IO.File`, etc.
- **E2E input needs an interactive desktop.** `SendInput`/`GetCursorPos` returning
  `ACCESS_DENIED (err 5)` means your session can't inject input — validate the fixture over
  UIA (`winapp ui ... --json -w <hwnd>`) and rely on the CI *"E2E Tests (winapp ui)"* job.
  For stateful E2E/selftest UI use `Component<T>()` fixtures; raw `ctx.UseState` doesn't
  persist (TestHost renders with a fresh `RenderContext`).

### Checks that actually prove something

Applies to anything whose output you would act on: xUnit assertions, selftest
`H.Check`s, and the ad-hoc commands you verify state with — log greps, freshness
checks, CI queries. The failure mode is identical in all three, and the last one is
the one that bites hardest, because a broken instrument is trusted by default.

- **Every assertion must fail if its target code is deleted / no-op'd / returns default.**
  Bare non-null, "no throw" on a `void`, and always-emitted shape markers are vacuous.
  Use throw-position/arity, differential-isolation (`Assert.NotEqual` between two variants
  differing only by the setter), structural counts, reflection `DeclaringType`, or
  corrupt-then-recompute oracles. **Copilot review does not catch vacuous assertions** —
  run the `.github/skills/pr-review/` multi-model dimension (different model family, high
  reasoning) on the *final* commit and fix every finding.
- **A value oracle proves nothing where its healthy and broken branches coincide.**
  This is the shared failure behind deleting a "redundant" guard and citing a passing but
  non-differential check as an oracle. Before either, identify where both branches produce
  the same value; preserve a guard or use a structural/differential oracle that cannot.
- **Mutation testing does not reach an assertion whose *inputs* are environment-derived.**
  It perturbs the code under test, so it only exposes an oracle whose value depends on that
  code. `double preOffset = sv.VerticalOffset;` … `Math.Abs(sv.VerticalOffset - preOffset)
  <= 3.0` passes identically in a healthy and a degraded environment when both sides are
  `0.00` — no product mutation separates them. **Log the input once, not just the verdict:**
  if a value read from live state is `0`, `NaN`, empty, or equal to the thing it is compared
  against, the assertion is a tautology regardless of what the product does. Same shape as a
  `-1` "not found" sentinel satisfying `bodyOn > bodyOff * 3` (`-1 > -3`) — prefer
  `double.NaN`, which makes every comparison false. The bug is upstream of the comparison.
- **The obvious remediation can itself be the vacuous one.** `Harness.WaitFor(P)` establishes
  P *at the moment it returns* and nothing more — it evaluates P **before** its first
  `Render`. So converting a fixed delay to a `WaitFor` is correct for an *eventual* assertion
  (false at t=0, converges) and silently vacuous for a *survival* assertion ("still visible"),
  which is true at t=0 and short-circuits at zero elapsed time. Before converting, ask: **if
  the predicate is true the instant `WaitFor` is called, does the next assertion still mean
  what I think it means?** Same question for a precondition `throw`: report-and-return when
  the fixture's other checks remain meaningful, throw loudly when it is structurally invalid
  (a renamed reflection target) and they do not.
- **This applies to your verification tooling, not just to tests.** A check that cannot fail
  loudly will eventually report something false with total confidence, and the direction is
  arbitrary — it can manufacture an alarm or an all-clear, and nobody audits the one that says
  what they hoped. A malformed `gh --jq` expression exits non-zero to stderr while a pipeline
  reading stdout sees an empty string indistinguishable from a legitimate zero. Prefer
  parameterised GraphQL (`-f`/`-F`, no interpolation), or fetch JSON and filter in the host
  language so a parse failure throws. **The tell is implausible uniformity:** an identical
  value across subjects that have nothing in common is a bug report about the instrument.
  The unifying question for a test and a tool alike is the same — **could this have come out
  the other way?** If the answer is "no — it always passes", that is the finding, not
  confirmation. **A no-match result is not a measurement** until a positive control proves
  the same probe, wrapping, and file set can produce a match.
- **Fixture registration is two-place.** Selftest: add to `AllFixtures` **and** the
  `Create()` switch in `tests/Reactor.AppTests.Host/SelfTest/SelfTestFixtureRegistry.cs`.
  E2E: add to `AllFixtures` **and** the `Build` switch in
  `tests/Reactor.AppTests.Host/FixtureRegistry.cs`.
- **…which makes searching for a TAP name unsafe unless you search the whole tree.** TAP
  carries two kinds of name and they live in different files: *fixture* names (registry only,
  and the class they map to is often spelled differently — `CenterOnCurrent_UsesCursorMonitor`
  → `CenterOnCurrentUsesCursorMonitor`) and *check* names (string literals in `H.Check(...)`
  inside `Fixtures/`). Neither location is a superset:

  | probe | blind spot |
  |---|---|
  | grep `SelfTest/Fixtures/` only | fixture names whose class name differs |
  | grep `SelfTestFixtureRegistry.cs` only | check-only names — e.g. `WindowLevel_RuntimeFlip_Topmost`, `TabViewFill_Mounted`, `ExitTr_Removed` are all registry=0 |
  | **grep `tests/Reactor.AppTests.Host/` whole** | **none — use this** |

  Both narrow probes return a *confident zero*, which reads as "this fixture doesn't exist"
  and invites re-attributing a real flake as branch-local or renamed. Both directions were hit
  during one flakiness audit, and the check-only example above is the assertion at the centre
  of issue #927 — a rule that silently fails on the most-discussed flake in the audit is worse
  than no rule, because its user has no reason to doubt the answer. Two probes with
  complementary blind spots don't compose into coverage unless you run both, and if you're
  running both the whole-tree grep is cheaper than remembering why.
- **Coverage** starts from `tools/coverage/run-coverage.ps1` (`-UnitOnly`, `-SkipBuild`);
  output `coverage/merged.cobertura.xml`. The script **aborts before the merge step on any
  test failure** — if a known flake trips it (`CenterOnCurrent_UsesCursorMonitor`,
  `PersistPlacement_FallbackWhenEmpty`), merge the legs manually with
  `dotnet-coverage merge coverage\unit.cobertura.xml coverage\selftest.cobertura.xml --output coverage\merged.cobertura.xml --output-format cobertura`.
  Both are **selftest** fixtures (`Phase1WindowingFixtures.cs` / `Phase3WindowingFixtures.cs`),
  not unit tests — don't go hunting for them in `Reactor.Tests`. They are the
  *same assertion twice*: both open a `WindowStartPosition.CenterOnCurrent` window and check
  its centre lands in the work area of the monitor under the mouse, so they fail as a **pair**
  — seeing only one of them is the surprise, not seeing both. Neither is a render-timing race,
  so `WaitFor` will not help. **Two different mechanisms in two different environments — check
  which one you are in before theorising:**
  - **Non-interactive session (CI agents, RDP-disconnected, locked, headless).**
    `GetCursorPos` returns **ACCESS_DENIED (err 5)** and never writes its `out` param, so the
    cursor monitor cannot be determined at all. This is a **100% deterministic failure, not a
    flake** — the pair simply cannot pass there. Confirmed by direct P/Invoke probe on two
    separate machines. Fixed by skipping rather than asserting when the cursor is
    undeterminable; if you see these fail, probe first:
    `GetCursorPos(out p)` → `False` / `LastError=5` means you are in this case and the fixtures
    are innocent. Note `System.Windows.Forms.Cursor.Position` **hides** this — it surfaces the
    uninitialised `(0,0)` instead of the failure.
  - **Interactive multi-monitor box.** A TOCTOU: `GetCursorPos` is sampled *before*
    `OpenAndSettle`, so a cursor crossing a monitor boundary while the window opens invalidates
    the captured work rect. Intermittent, and **structurally impossible on a single display** —
    if you are on one virtual desktop with no boundary to cross, this is not your mechanism.
  Do not assume "quiet machine ⇒ passes": that holds only in the interactive case.
- **Don't co-locate the E2E and selftest tiers.** CI runs them as separate jobs on separate
  runners today, and that isolation is load-bearing rather than incidental. E2E drives real
  pointer input, foregrounds windows and changes Z-order; several selftest fixtures read live
  desktop state (the two `CenterOnCurrent`-based ones above read the cursor's monitor;
  `UseIsCovered_RerendersOnZOrderChange` reads Z-order). Running E2E first on one machine and
  then `--self-test` took a clean unmodified tree from 0 to 8 failures — reproducible, and the
  standing repro for those fixtures. So if you consolidate jobs to save runner minutes, or run
  both tiers locally back-to-back, expect selftest reds that are an artefact of tier ordering
  and not of your change. Fix the fixtures' desktop-state dependence before merging the jobs,
  not after.
- **Judging whether a flake fix worked.** N clean runs only supports a fix if `(1-p)^N` is
  small for an observed failure rate with `0 < p < 1`. At `p = 0.25`, three consecutive
  passes happen **42%** of the time, and roughly `N = 10` is needed for ~95% confidence.
  Synthetic-input E2E tests for timing defects often have `p` fixed at `0` or `1` by queue
  ordering; reruns then have zero statistical power, so mutation-test the detector instead.
  Prefer a *mechanism* that explains the observed failure text over any number of green runs;
  run counts corroborate a cause, they don't establish one.
- **Budget increases only refute one class of race.** Raising a poll/wait budget and still
  failing rules out "we sampled too early". It does not rule out an event that never fires,
  an ordering race decided before the first poll, or a lost wakeup — all budget-insensitive.
  The sound conclusion is "not a *too-short-poll* problem", which is narrower than "not a
  race". Don't let the narrower finding promote a fixture into an "environmental" bucket it
  then stops being investigated in; confirm that label by running it on a quiet machine with
  a known window-manager state, and only apply it if it goes clean there.

### Analyzers, CLI checks, docs & public API

- `src/Reactor.Analyzers` targets **`netstandard2.0`** — no `FrozenDictionary`/net8+ APIs,
  and you **cannot reference `src/Reactor.Cli`**; copy shared logic and add parity tests.
  Every new `REACTOR_*` id needs a row in `src/Reactor.Analyzers/AnalyzerReleases.Unshipped.md`
  (else `RS2008`). `mur check` rules are reflection-discovered in `RuleRegistry.cs` — add or
  remove a rule *file*, don't hand-edit a list.
- Docs are generated (see above): compile only the topic you touched and revert unrelated
  snippet churn.
- A new public API surface has **two byte-identical index copies** —
  `skills/reactor.api.txt` and `plugins/reactor/skills/reactor-dsl/references/reactor.api.txt`
  — regenerate via `mur --regen-api`; keep them in sync.
- The **ReactorGallery search index** (`samples/ReactorGallery/reactor-search-index.json`,
  consumed by the external `winui-search` CLI) is generated from the gallery source +
  `tools/Reactor.SearchIndex/editorial.json`. After adding/renaming a gallery control or
  changing its first sample snippet, regenerate via
  `dotnet run --project tools/Reactor.SearchIndex` (a `Reactor.Tests` gate byte-compares it,
  so a stale index fails CI). Curate keywords/usings/overrides in `editorial.json`, never the
  generated JSON.
- A new common-element modifier touches every seam: the `ElementModifiers` field, skip
  equality, `Merge`, `ApplyModifiers`, and the fluent extension. Pair a `.HasValue` write
  with `fe.ClearValue(<DP>Property)` on unset unless intentionally matching a no-reset sibling.

### Environment

- Work in a clean worktree, not `main`: `git worktree add -b <branch> <path> origin/main`.
- Don't build under deep or OneDrive-synced paths — WinUI can fail with `MSB3073`/`PRI210`;
  prefer a short local path (e.g. `C:\src\`).

### Repo skills (`.github/skills/`)

Contributor-facing orchestration skills — read the `SKILL.md` and drive it with your own
tools: `pr-review` (multi-dimensional branch review), `perf-compare` (stress-harness delta
vs `main`), `coverage-uplift` (non-vacuous coverage across tiers), `analyzer-dym`
(did-you-mean / `mur check` authoring). Not shipped to end users.
