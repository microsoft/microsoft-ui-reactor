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

`ElementPool` recycles WinUI controls. `PoolableTypes` includes interactive controls (`Button`, `TextBox`, `ToggleSwitch`) alongside the non-interactive ones: their event trampolines subscribe **once for the control's lifetime** and read the current element from attached state at invocation time, so a recycled control dispatches to the new element's callbacks. `Reconciler.ReturnControl<T>` therefore deliberately **preserves** `ReactorState.ControlEventState` across rent/return (issue #114) — clearing it would re-allocate on every rent and double-subscribe; the box is dropped only on full detach (`DetachReactorState`). New event wiring on a poolable control must go through that one-time trampoline, never a fresh per-rent subscription. It also keeps a `ConditionalWeakTable<UIElement, object>` (`_compositorTainted`) of elements that have had `GetElementVisual()` called on them — those permanently lose the XAML implicit-transition APIs (`OpacityTransition`, `ScaleTransition`, …), so they are excluded from pooling rather than handed to a future user that might need those APIs.

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
3. **Register** it. Spec 048 §3.4 removed the old bootstrap: built-in handlers now
   self-register **lazily on the first factory call**, via the per-control
   `Reg<>` / `RegDecorator<>` cctor latch in `Dsl.cs`. A `[GenerateReactorWrapper]`
   element gets a static constructor emitting `ControlRegistry.Register` (spec 058).
   To register a third-party control or override a built-in globally, call
   `ControlRegistry.Register<TElement, TControl>` (or `RegisterDecorator`,
   `RegisterForDerivedTypes`) at startup. `ReactorApp.RegisterAllBuiltIns()` is the
   opt-in bulk path for direct-record/AOT callers. Note `new MyElement(...)` alone
   registers nothing — the factory call is what latches it.
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
  treat the markup errors as a likely cascade: fix the earlier error first, then confirm
  they disappear before investigating them independently.
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

Applies to xUnit assertions, selftest `H.Check`s, **and the ad-hoc commands you verify
state with**. A broken instrument is trusted by default, so the last one bites hardest.

- **An assertion must fail when its target is broken.** Delete or no-op the code and
  confirm it reddens. Bare non-null, "no throw" on a `void`, and always-emitted shape
  markers are vacuous. Prefer differential oracles, structural counts, or
  corrupt-then-recompute.
- **An oracle proves nothing where its healthy and broken branches coincide.** If a live
  value is `0`, `NaN`, empty, or already equal to what it's compared against, the
  comparison is a tautology regardless of the product. Log the input, not just the verdict
  — mutation testing cannot catch an environment-derived oracle, because the defect is
  upstream of the code it perturbs.
- **A no-match is not a measurement** until a positive control — same probe, same wrapping,
  same file set — shows it can match. Zero from a broken grep and zero from a clean repo are
  the same character on screen. Applies to greps, `gh --jq`, CI queries. Renames make today's
  identifier structurally blind to older revisions.
- **Self-consistency is not currency.** Counts from one stale checkout corroborate each
  other perfectly. Pin measurements to a commit OID and compare against a live remote ref.
- **Green runs corroborate a fix; a mechanism establishes it.** At a 25% failure rate,
  three clean passes happen 42% of the time.

**When to stop.** Validate each instrument once — then stop. Verify the assertion that gates
correctness, not the verifier of the verifier. If you are adding a *second* layer of checking,
or correcting the wording of a comment rather than the behaviour of code, you are past the
point of return: stop and ship.

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
- Don't build under deep or OneDrive-synced paths — WinUI can fail with `MSB3073`/`PRI210`
  or XAML compiler `WMC1006`/`WMC9999`, sometimes naming an unrelated project such as
  `Reactor.AppTests.ThirdPartyControls`. Before concluding your branch broke the build, check
  out the same HEAD at a short path (e.g. `C:\src\probe`); if it passes, the path was the cause.

### Repo skills (`.github/skills/`)

Contributor-facing orchestration skills — read the `SKILL.md` and drive it with your own
tools: `pr-review` (multi-dimensional branch review), `perf-compare` (stress-harness delta
vs `main`), `coverage-uplift` (non-vacuous coverage across tiers), `analyzer-dym`
(did-you-mean / `mur check` authoring). Not shipped to end users.
