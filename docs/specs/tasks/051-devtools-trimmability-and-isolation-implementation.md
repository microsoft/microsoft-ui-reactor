# Devtools Trimmability and Isolation — Implementation Tasks

Derived from: [`docs/specs/051-devtools-trimmability-and-isolation.md`](../051-devtools-trimmability-and-isolation.md).
Tracks [issue #497](https://github.com/microsoft/microsoft-ui-reactor/issues/497).

> **Status:** Implemented and locally verified. Spec 051 is code-complete; this tracker records the landed artifacts and remaining CI-only confirmations.
>
> **Implementation status (2026-06-01):** Baseline `hello-world-aot` was
> `14,576,640 B` at commit `931d9270`; the switch-off publish is below the
> `≤12.5 MB` gate (locally measured `6,647,296 B` in final verification; prior
> close-out recorded ~8.8 MB), mstat absence checks are clean, and the Phase 2
> E2E gate passed against published NativeAOT binaries. Relevant commits:
> `931d9270` baseline, `59a4336c` switch, `c213928c` tests/tool, `315d2401`
> docs, `f79bf044` split, `933415e4` phase2 gates.
>
> **How to use this tracker:** Every actionable item is a checkbox. Mark `[x]`
> only when its artifact (code + tests + doc update + verified number) is landed
> and green. The work pauses/resumes cleanly at any checkbox boundary — always
> finish a phase's exit gate before treating that phase as done. Phase 0 spikes
> resolve the spec's open questions and must precede Phase 1 coding.
>
> **Conventions** (mirroring `048-control-registration-and-trimming-implementation.md`):
> - Order matters. Phase 0 de-risks (spikes the open questions). Phase 1 lands
>   the FeatureSwitch + parameter removal and captures the full ~1.5 MB binary
>   win in one PR. Phase 2 splits devtools into its own package so the IL no
>   longer ships in `Reactor.dll`.
> - Each phase keeps full xunit + selftest + solution build green before it is
>   marked done.
> - New selftest fixtures are registered in **two** places in
>   `SelfTestFixtureRegistry.cs` (the fixture-name list AND the name→ctor switch
>   arm) — see the repo testing convention.
> - Build unit tests first, then `dotnet test --no-build -p:Platform=x64`; plain
>   `dotnet test` can hang/over-run (see repo testing memory).
> - **Docs are generated — never edit `docs/guide/*` directly.** Read
>   `docs/_pipeline/ai-author-skill.md` first. Each guide page has two sources
>   under `docs/_pipeline/`: a prose **template** (`templates/<topic>.md.dt`)
>   and a compilable **doc app** (`apps/<topic>/App.cs` + `.csproj`) whose code
>   is snippet-extracted into the page. `ReactorApp.Run(..., devtools: true)`
>   lives in the **doc apps**, not the templates. Edit the template and/or the
>   doc app, then run `mur docs compile` to regenerate `docs/guide/*`. Preserve
>   `<!-- ai:lock -->` sections verbatim.


## Spec citations cross-checked against current tree

- `src/Reactor/Hosting/Devtools/` exists with 27 files incl. `DevtoolsMcpServer.cs`,
  `LockfileRegistry.cs`, `DevtoolsDockingTools.cs`, `DevtoolsCliParser.cs`,
  `DevtoolsMenuFactory.cs`, `DevtoolsJsonContext.cs`. ✅ confirmed.
- `src/Reactor/Hosting/PreviewCaptureServer.cs` exists. ✅ confirmed.
- `src/Reactor/Hooks/UseDevtools.cs` exists. ✅ confirmed.
- `samples/apps/hello-world-aot` exists and is the switch-off measurement target.
- `src/Reactor/build/Reactor.targets` exists and supplies the default-off switch.
- Line numbers in the spec (e.g. `ReactorApp.cs:298,313,925,1017`) are
  approximate against current `main`; re-grep before editing rather than
  trusting the literal line.

---

## Overall exit gate (all must hold to declare 051 done)

1. With `Reactor.DevtoolsSupport` off and `PublishAot=true`, `hello-world-aot.exe`
   drops from ~14.03 MB to **≤ 12.5 MB** (spec §3.1, §15 Phase 1 gate).
2. The published mstat for the switch-off `hello-world-aot` contains **none** of:
   any `Microsoft.UI.Reactor.Hosting.Devtools.*` type, `PreviewCaptureServer`,
   `JsonTypeInfo<>`/`JsonPropertyInfo<>`/`JsonConverter<>` reflection-mode types,
   `HttpClient`/`SocketsHttpHandler`/`HttpConnection`/`Http2Connection`,
   `SslStream`, `HttpListener`, or `Microsoft.UI.Reactor.Docking.*` (modulo the
   two records pinned from `Element.cs` — #498 territory) (spec §3.1, §11).
3. No `[UnconditionalSuppressMessage("Trimming", "IL2026", ...)]` remains on
   either `Run` overload (spec §3.3, §8.1).
4. `Run<TRoot>` / `Run` lose the `bool devtools = false` and `bool preview = false`
   parameters; `ResolveDevtoolsParam` and `_previewParamDeprecationWarned` are
   deleted; no new public knob replaces them (spec §3.4, §8.1).
5. Activation stays two-layered: build-time switch (and, after Phase 2, package
   reference) grants capability; runtime `--devtools` arg grants activation
   (spec §3.5, §7).
6. A switch-off binary that receives `--devtools run` prints the actionable
   `<RuntimeHostConfigurationOption ...>` error rather than silently opening a
   normal window (spec §3.6, §5.4).
7. The fix uses only stock .NET attributes (`[FeatureSwitchDefinition]`,
   `[FeatureGuard]`) and the stock `RuntimeHostConfigurationOption` item — no new
   analyzer / source generator / custom MSBuild prop (spec §3.7).
8. **(Phase 2)** An ILSpy/Cecil scan of retail `Reactor.dll` contains no
   `Microsoft.UI.Reactor.Hosting.Devtools.*` type, no `PreviewCaptureServer`,
   no `LockfileRegistry` (spec §3.8).
9. **(Phase 2)** Apps that do not reference `Microsoft.UI.Reactor.Devtools`
   cannot transitively resolve any devtools type (spec §3.9).
10. `tools/Reactor.MstatVerifier/` exists, is wired into CI, and gates PRs that
    touch `src/Reactor/Hosting/**` or `src/Reactor.Devtools/**` (spec §11, §12.4).
11. Full xunit + selftest + solution build green; AOT-selftests CI job passes.

---

## Phase 0 — De-risk: resolve open questions (spikes, no shipping code)

Closes the spec §14 open questions that gate the Phase 1 implementation shape.
Keep findings in the PR description / spec §14 close-out, not in shipped code.

### 0.1 Q1 — `RuntimeHostConfigurationOption` last-write-wins (spec §5.3, §14 Q1)

- [x] Build a throwaway app that imports a `.targets` declaring
      `<RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"
      Value="false" Trim="true" />`, then in the consumer csproj re-declares the
      same `Include` with `Value="true"`. Publish and inspect the generated
      `*.runtimeconfig.json` to confirm the consumer value wins (no duplicate
      key, last-write-wins).
- [x] If last-write-wins does **not** hold on the .NET 10 SDK, record the
      working consumer idiom (`Remove` then re-add) so Task 1.4 and the docs
      encode the idiom that actually works.

### 0.2 Q3/Q4 — confirm decisions are still valid against current tree

- [x] Q3: confirm hard-delete of the `devtools:`/`preview:` parameters is
      acceptable (pre-1.0). Record the `[Obsolete]`-window fallback as the
      contingency if review surfaces external consumers (spec §8.4, §14 Q3).
- [x] Q4: confirm `Reactor.AppTests.Host` will default the switch **on** (it is
      not a retail target and needs AOT-with-devtools coverage); selftests that
      need switch-off behavior flip it per-test via
      `AppContext.SetSwitch("Reactor.DevtoolsSupport", false)` (spec §14 Q4).

### 0.3 Create the measurement baseline app (spec §3.1, §11)

- [x] Create `samples/apps/hello-world-aot/` — a one-line
      `TextBlock("Hello, world!")` root component, `PublishAot=true`, with all the
      size-stripping flags from issue #497 (`InvariantGlobalization`,
      `StackTraceSupport=false`, trim-mode full, etc.). This is the spec's
      headline measurement target and the mstat regression subject.
- [x] Publish it on current `main` (before any 051 change) and record the
      **baseline** size (expected ~14.03 MB) and the baseline mstat type list so
      the Phase 1 delta is provable. Store the number in the PR / spec close-out.
- [x] Confirm the baseline mstat actually contains the devtools tail
      (`Hosting.Devtools.*`, `PreviewCaptureServer`, `HttpClient`,
      `JsonTypeInfo<>`) — i.e. that the leak the spec describes reproduces here.

### 0.4 Verify `DevtoolsCliParser` is transitively trim-clean (spec §5.4, §14 Q2)

- [x] Inspect `DevtoolsCliParser.cs` + its option record(s): confirm they carry
      primitives only and have **no** transitive reference into
      `DevtoolsMcpServer` / `PreviewCaptureServer` / `LockfileRegistry` / JSON /
      HTTP. If they do, plan to factor the option types into a pure-data leaf
      record so the parser can stay in core unguarded.

### 0.5 Phase 0 exit gate

- [x] Q1 idiom decided and documented; baseline app + numbers captured; parser
      confirmed trim-clean (or refactor planned). No shipping code yet.

---

## Phase 1 — FeatureSwitch + FeatureGuard + parameter removal (1 PR, ~500 lines)

Captures the full binary-size win and removes the suppression. Devtools IL still
ships in `Reactor.dll` (that's Phase 2); here it becomes statically **dead** when
the switch is off.

### 1.1 Add the feature switch type (spec §5.1)

- [x] Add `ReactorFeatures` (`internal static class`) in `src/Reactor/Hosting/`
      (or a new `src/Reactor/Hosting/Features/` folder if the namespace lands
      cleaner) with `internal static bool IsDevtoolsSupported =>
      AppContext.TryGetSwitch("Reactor.DevtoolsSupport", out var on) && on;`.
- [x] Annotate the property with `[FeatureSwitchDefinition("Reactor.DevtoolsSupport")]`,
      `[FeatureGuard(typeof(RequiresDynamicCodeAttribute))]`, and
      `[FeatureGuard(typeof(RequiresUnreferencedCodeAttribute))]` (both guards
      required — `TryRunDevtools` is `[RequiresUnreferencedCode]` and the
      JSON/MCP chain pulls `[RequiresDynamicCode]`).
- [x] Include the XML-doc consent-gate comment from spec §5.1 (the csproj
      `RuntimeHostConfigurationOption` snippet) so authors discover the knob.

### 1.2 Gate the `Run` overloads + delete the parameters (spec §5.2, §8.1)

- [x] Re-grep `src/Reactor/Hosting/ReactorApp.cs` for the two `Run` overloads
      (generic `Run<TRoot>` and non-generic `Run`), `ResolveDevtoolsParam`,
      `_previewParamDeprecationWarned`, and the two
      `[UnconditionalSuppressMessage("Trimming", "IL2026", ...)]` attributes —
      capture current line numbers.
- [x] Replace `var effectiveDevtools = ResolveDevtoolsParam(devtools, preview);
      if (effectiveDevtools && TryRunDevtools(...)) return;` with
      `if (ReactorFeatures.IsDevtoolsSupported && TryRunDevtools(...)) return;`.
- [x] Delete the `bool devtools = false` and `bool preview = false` parameters
      from both overloads, the `effectiveDevtools` local, the
      `ResolveDevtoolsParam` helper, and the `_previewParamDeprecationWarned`
      field.
- [x] Delete both `[UnconditionalSuppressMessage("Trimming", "IL2026", ...)]`
      attributes — the FeatureGuard provides the analyzer relief.
- [x] Confirm no other public/internal call site references the deleted
      parameters or helper.

### 1.3 Split CLI parse from dispatch + actionable fallback (spec §5.4)

- [x] Refactor `TryRunDevtools` so the **parse** (`DevtoolsCliParser.Parse`) is
      unguarded and stays reachable in core, and the **dispatch**
      (`DispatchDevtoolsSubverb`) is behind the `IsDevtoolsSupported` guard.
- [x] When `options.Subverb` is set but `IsDevtoolsSupported` is false, write the
      actionable stderr message containing the exact
      `<RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"
      Value="true" Trim="true" />` snippet, and `return true` (consume the
      launch — do not fall through to a normal window).
- [x] When `options.Subverb` is null, `return false` (normal run).
- [x] Annotate `PreviewCaptureServer`'s ctor with
      `[RequiresUnreferencedCode("Devtools subsystem; gated by
      Reactor.DevtoolsSupport.")]` so out-of-tree callers get the warning
      instead of a silent leak (spec §5.2 point 2).

### 1.4 MSBuild plumbing — ship the default-off switch (spec §5.3)

- [x] Create `src/Reactor/build/Reactor.targets` (new `build/` folder) declaring
      the default `<RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"
      Value="false" Trim="true" />`, using whichever idiom Task 0.1 proved works.
- [x] Wire the targets into the nupkg `build/` folder (csproj `<None
      Include="build\Reactor.targets" Pack="true" PackagePath="build\" />` or
      equivalent packaging metadata) so consumers import it automatically.
- [x] Verify the default flows into a fresh consumer's `runtimeconfig.json` as
      `false` and that a consumer override to `true` wins (re-run the Task 0.1
      check against the real targets file).

### 1.5 Update in-repo call sites that pass `devtools:` / `preview:` (spec §8.5)

Mechanical: remove the deleted parameters and preserve whatever each call site
asked for today, re-expressed as the csproj switch.

- [x] Grep `samples/`, `tests/`, `docs/_pipeline/` and `src/` for
      `devtools:` / `preview:` arguments to `Run(...)`. Remove the argument and
      add the `RuntimeHostConfigurationOption` to the corresponding csproj where
      devtools is actually wanted.
- [x] `tests/Reactor.AppTests.Host/`: add the switch to its csproj (default
      **on** per Q4) and drop the `devtools:`/`preview:` args from its
      `Program.cs`.
- [x] Any `samples/apps/*` that opted into devtools: csproj edit + `Program.cs`
      edit.
- [x] **Doc apps** (`docs/_pipeline/apps/<topic>/App.cs`) call
      `ReactorApp.Run(..., devtools: true)` and must compile after the param is
      removed. Confirmed call sites: `apps/calculator/App.cs`,
      `apps/docking/App.cs`, `apps/extending-reactor-controls/App.cs`,
      `apps/getting-started/App.cs`, `apps/todo-app/App.cs`,
      `apps/v1-protocol/App.cs`. Drop the `devtools:` arg from each `App.cs`
      **and** add the `RuntimeHostConfigurationOption` switch to that app's
      `<topic>.csproj` (default **on** — screenshot capture during
      `mur docs compile` depends on devtools being enabled; see Task 1.9).
      There is no shared `Directory.Build.props` under `docs/_pipeline/apps/`,
      so either edit each csproj or add one shared props file there.
- [x] After the doc-app edits, run `mur docs compile` and confirm the
      regenerated `docs/guide/*` pages still build with screenshots intact.
      Never hand-edit `docs/guide/*` (see Conventions above).

### 1.5a Blanket-enable devtools in **Debug** builds across the repo (spec §8.3)

Beyond preserving existing opt-ins (1.5), make devtools available by default
during development so it "just works" in Debug while retail/Release publishes
stay trimmed. Uses the §8.3 Debug-conditional pattern:
`<RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport" Value="true"
Trim="true" Condition="'$(Configuration)' == 'Debug'" />`. Keep the
`hello-world-aot` measurement apps (Task 0.3 / 1.8) **excluded** — they must
publish switch-off to prove the trimming number.

- [x] Decide the rollout vehicle and record it here: either (a) a shared
      `Directory.Build.props`/imported `.props` under `samples/` and `tests/`
      that injects the Debug-conditional switch for all projects in scope, or
      (b) per-csproj edits. Prefer (a) to avoid drift; explicitly opt the
      `hello-world-aot*` apps out (`Condition` exclusion or a sentinel prop).
- [x] Apply to all `samples/apps/*` (a11y-showcase, animated-list-demo, chat,
      demo-script-tool, dock-showcase, headtrax, minesweeper, monaco-editor,
      netpulse, reactor-ide, regedit, validation-showcase, wordpuzzle) — Debug
      enables devtools, Release/AOT does not.
- [x] Apply to `samples/scenarios/*` and any other runnable sample hosts.
- [x] Apply to test host(s) that launch a real app
      (`tests/Reactor.AppTests.Host`, plus any AppTests fixtures that publish a
      sample) — Debug-on, with selftests still flipping switch-off paths via
      `AppContext.SetSwitch` per-test (Q4).
- [x] Exclude `samples/apps/hello-world-aot` and
      `samples/apps/hello-world-aot-devtools-on` from the blanket props so the
      mstat verifier (1.8) keeps measuring the intended switch states.
- [x] Verify a `dotnet build -c Debug` of a sample yields a `runtimeconfig.json`
      with `Reactor.DevtoolsSupport=true`, and `-c Release` yields `false`
      (or absent → defaults false via `Reactor.targets`).

### 1.6 Unit tests (spec §12.1)

- [x] `tests/Reactor.Tests/Hosting/ReactorFeaturesTests.cs`:
      `IsDevtoolsSupported_ReadsAppContextSwitch` (toggle via
      `AppContext.SetSwitch`) and `IsDevtoolsSupported_DefaultsOff`.
- [x] `DevtoolsCliParser_RecognizesDevtoolsVerbs_WithoutLoadingHandlers` — parse
      `--devtools run --mcp-port 1234`; assert the options record is populated
      and no handler type (`DevtoolsMcpServer`) is constructed during the test.
- [x] `ReactorApp_Run_SwitchOff_WithDevtoolsArg_EmitsActionableError` — capture
      stderr, run `Run` with `--devtools run` in
      `Environment.GetCommandLineArgs` and switch off; assert the message
      contains the `<RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"`
      snippet. Group with `[Collection("ConsoleTests")]` (console-mutating).

### 1.7 Selftest fixtures (spec §12.2)

- [x] Add `tests/Reactor.AppTests.Host/SelfTest/Fixtures/Spec051*` fixtures:
      `DevtoolsMenu_SwitchOff_RendersEmpty`,
      `DevtoolsMenu_SwitchOn_RendersTrigger`,
      `UseDevtools_SwitchOff_ReturnsFalse`,
      `UseDevtools_SwitchOn_PlusCli_ReturnsTrue` (flip the switch per-fixture via
      `AppContext.SetSwitch`).
- [x] Register all four in `SelfTestFixtureRegistry.cs` in **both** the
      fixture-name list and the name→ctor switch arm.
- [x] Run `dotnet run --project tests/Reactor.AppTests.Host -p:Platform=x64 --
      --self-test --filter Spec051` and confirm green.

### 1.8 mstat regression gate tool (spec §11, §12.4)

- [x] Create `tools/Reactor.MstatVerifier/` — a small (~30-line core) C# runner
      that parses a `.mstat` (via `Mono.Cecil` or an upstreamed copy of
      sizoscope's `MstatData*.cs` parser; do **not** depend on a dev-machine
      temp path).
- [x] Implement the **absence** assertions for the switch-off
      `hello-world-aot` mstat: no `Microsoft.UI.Reactor.Hosting.Devtools.*`, no
      `PreviewCaptureServer`, no `HttpClient`/`SocketsHttpHandler`/`HttpConnection`/
      `Http2Connection`, no `SslStream`, no `HttpListener`, no
      `JsonTypeInfo<>`/`JsonConverter<>`.
- [x] Implement the EXE size assertion: `hello-world-aot.exe` ≤ 12.5 MB.
- [x] Create the **positive control** `samples/apps/hello-world-aot-devtools-on`
      (switch on in csproj) and assert those types **are** present — catches the
      regression where the switch silently becomes a no-op.
- [ ] Wire a CI lane (`.github/workflows/ci.yml`): publish both apps with
      `PublishAot=true`, run the verifier; trigger on PRs touching
      `src/Reactor/Hosting/**` (and later `src/Reactor.Devtools/**`) plus a
      nightly unconditional lane. (authored; CI confirms on PR)

### 1.9 Documentation (spec §13)

> Verified paths against the tree: `docs/aot-support.md` is hand-written (edit
> directly). The user-facing devtools guide is `docs/guide/devtools-internals.md`,
> **generated** from `docs/_pipeline/templates/devtools-internals.md.dt` — edit
> the template, not the compiled `.md`. Templates that currently contain a
> `devtools:` arg are: `dev-tooling.md.dt`, `devtools-internals.md.dt`,
> `getting-started.md.dt`, `packaging.md.dt`. The devtools skill is a single
> file: `plugins/reactor/skills/reactor-devtools/SKILL.md`. The actual
> `Run(..., devtools: true)` **code** lives in the doc apps (handled in Task
> 1.5), and the authoring guidance that mandates it lives in
> `docs/_pipeline/ai-author-skill.md` (updated below).

- [x] `docs/aot-support.md` (hand-written) — replace the devtools/preview
      language with a `Reactor.DevtoolsSupport` + two-layer-activation section;
      delete the stale "Navigation serializes deep-link state via JsonSerializer"
      claim.
- [x] Edit the `devtools:`-bearing templates under `docs/_pipeline/templates/`
      (`dev-tooling.md.dt`, `devtools-internals.md.dt`, `getting-started.md.dt`,
      `packaging.md.dt`) — change the enable recipe from "pass `devtools: true`"
      to "add the `RuntimeHostConfigurationOption` + pass `--devtools` on the
      CLI". Then `mur docs compile` to regenerate the `docs/guide/*` outputs
      (`devtools-internals.md`, `getting-started.md`, etc.). Edit templates, not
      compiled output.
- [x] **`docs/_pipeline/ai-author-skill.md` (the authoring skill itself)** —
      update the "App Code Guidelines" block (currently ~line 341/344) that tells
      authors to write `ReactorApp.Run<MyApp>(..., devtools: true)` and says
      "Always include `devtools: true` — this enables the screenshot capture
      system." After the param is removed this guidance compiles-breaks every new
      doc app. Replace it with the new contract: `Run` takes no devtools arg, and
      the doc-app `.csproj` carries the `RuntimeHostConfigurationOption`
      `Reactor.DevtoolsSupport=true` switch (which is what now enables screenshot
      capture). If a shared `docs/_pipeline/apps/Directory.Build.props` is added
      in Task 1.5, point authors at that instead of per-app csproj edits.
- [x] `plugins/reactor/skills/reactor-devtools/SKILL.md` — update agent-kit
      guidance from the `devtools: true` arg to the csproj switch + `--devtools`
      CLI pattern (and note the Debug-conditional convention from Task 1.5a).
- [x] Sweep the other skills under `plugins/reactor/skills/*/SKILL.md` for any
      `devtools:` / `Run(... devtools` references (e.g. `reactor-getting-started`,
      `reactor-build-and-check`) and update them to the new pattern.
- [x] `CHANGELOG.md` — "Breaking changes": document the `devtools:`/`preview:`
      parameter removal with the one-line csproj migration.

### 1.10 Phase 1 exit gate (spec §15 Phase 1 gate)

- [x] `samples/apps/hello-world-aot` published with default (switch-off) config
      is **≤ 12.5 MB** (down from ~14.03 MB).
- [x] `tools/Reactor.MstatVerifier/` passes (absence asserts + positive control).
- [x] No `[UnconditionalSuppressMessage("Trimming", "IL2026")]` remains on `Run`.
- [x] Full xunit green:
      `dotnet build tests/Reactor.Tests/Reactor.Tests.csproj -p:Platform=x64`
      then `dotnet test ... --no-build -p:Platform=x64`.
- [x] Full selftest green:
      `dotnet run --project tests/Reactor.AppTests.Host -p:Platform=x64 -- --self-test`.
- [x] Solution build clean: `dotnet build Reactor.slnx -p:Platform=x64`.

---

## Phase 2 — Package split + `IReactorDevtoolsHost` contract (separate, larger PR)

Removes devtools IL from `Reactor.dll` entirely. Apps that don't reference the
new package cannot reach `HttpListener`/`JsonSerializer`/MCP types under any
config.

### 2.1 Define the contract types in core (spec §6.1, §6.2)

- [x] In `src/Reactor/Hosting/Devtools/Contract/` add (namespace
      `Microsoft.UI.Reactor.Hosting.Devtools`):
      `public sealed record ReactorDevtoolsBootRequest(...)`,
      `public interface IReactorDevtoolsHost { bool TryHandleCommandLine(...);
      Element? BuildDevtoolsMenu(...); }`, and
      `public static class ReactorDevtoolsBootstrap` with
      `Register(IReactorDevtoolsHost)` + internal `Current`.
- [x] Confirm the contract has **zero** dependency on JSON/HTTP/MCP/capture/
      docking — primitives + `Element` + standard WinUI types only.

### 2.2 Create the `Reactor.Devtools` package (spec §6.2, §14 Q6)

- [x] Create `src/Reactor.Devtools/` csproj → `Microsoft.UI.Reactor.Devtools.nupkg`,
      separate csproj in the same repo, `<ProjectReference>` to `Reactor.csproj`
      for the contract types (Q6 decision).
- [x] Apply the WinUI library conventions: `IsAotCompatible=true`,
      `WindowsAppSDKSelfContained=false`, AOT/trim warnings as errors (core-lib
      parity).
- [x] Add the project to `Reactor.slnx`.
- [x] Share the `Version` MSBuild property with `Reactor.csproj` so the two
      nupkgs version in lockstep (spec §14 Q5).

### 2.3 Move the devtools implementation into the package (spec §6.2)

- [x] Move all of `src/Reactor/Hosting/Devtools/*` (the 27 files) to
      `src/Reactor.Devtools/`, **except** the `Contract/` types from 2.1 and the
      `DevtoolsCliParser` (stays in core per §6.2 / Q2).
- [x] Move `src/Reactor/Hosting/PreviewCaptureServer.cs` to
      `src/Reactor.Devtools/`.
- [x] Move the `[RequiresUnreferencedCode]` subverb dispatch
      (`RunListSubverb`/`RunRunSubverb`/`RunScreenshotSubverb`/
      `RunSlotMachineSubverb`) out of `ReactorApp.cs` into
      `src/Reactor.Devtools/DevtoolsHost.cs` as the `IReactorDevtoolsHost`
      implementation.
- [x] Update namespaces / `using`s; resolve any now-internal members the moved
      code needed (expose minimal `internal`-via-`InternalsVisibleTo` or promote
      to the contract as required — prefer the contract).

### 2.4 Self-register via module initializer (spec §6.3)

- [x] Add `src/Reactor.Devtools/ModuleInit.cs` with a `[ModuleInitializer]` that
      calls `ReactorDevtoolsBootstrap.Register(new DevtoolsHost())`.
- [x] Verify: when the package is referenced the host self-registers on first
      type load; when it is not referenced `ReactorDevtoolsBootstrap.Current`
      stays null.

### 2.5 Rework core's Run loop to the interface-dispatch shape (spec §6.4, §6.5)

- [x] In `Run<TRoot>`/`Run`, parse args, and when a subverb is present dispatch
      through `ReactorDevtoolsBootstrap.Current` **and**
      `ReactorFeatures.IsDevtoolsSupported` (belt-and-braces dual gate, §6.5);
      otherwise call `EmitDevtoolsNotAvailableMessage(options)` and return.
- [x] Confirm `Run` carries no `[RequiresUnreferencedCode]` and no
      `[UnconditionalSuppressMessage]` after the rework (the interface call with
      no implementation in the closure set is provably uncallable).

### 2.6 Shim `Factories.DevtoolsMenu` (spec §6.2, §8.2)

- [x] Move the `Factories.DevtoolsMenu` body to
      `src/Reactor.Devtools/DevtoolsMenuFactory.cs` as
      `IReactorDevtoolsHost.BuildDevtoolsMenu`.
- [x] Replace the core `Factories.DevtoolsMenu` with a one-line shim:
      `ReactorDevtoolsBootstrap.Current?.BuildDevtoolsMenu(...) ?? Empty()` —
      keep the public signature bit-for-bit source-compatible.

### 2.7 Update all consumers of the moved types (spec §8.5, §8.3, §13)

- [x] Add `<PackageReference Include="Microsoft.UI.Reactor.Devtools"
      Version="$(ReactorVersion)" />` to every consumer that needs devtools:
      `tests/Reactor.AppTests.Host/`, relevant `samples/apps/*`, and the
      agent-kit examples.
- [x] Make the blanket Debug-only enablement from Task 1.5a complete after the
      split: the **package reference** must also be Debug-conditional so Debug
      builds get the devtools package and Release/AOT builds don't ship it.
      Pair it with the switch in the same shared props / condition
      (`Condition="'$(Configuration)' == 'Debug'"`), per spec §8.3:
      ```xml
      <ItemGroup Condition="'$(Configuration)' == 'Debug'">
        <RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport"
                                        Value="true" Trim="true" />
        <PackageReference Include="Microsoft.UI.Reactor.Devtools"
                          Version="$(ReactorVersion)" />
      </ItemGroup>
      ```
      Keep `hello-world-aot*` excluded; the `-devtools-on` measurement app
      references the package + switch unconditionally (its whole job is the
      positive control).
- [x] Fix any direct references to moved types (`DevtoolsMcpServer`,
      `PreviewCaptureServer`, etc.) so they live behind the package reference or
      the contract.

### 2.8 Extend the mstat verifier with the `Reactor.dll` IL scan (spec §11, §3.8)

- [x] Add an assertion to `tools/Reactor.MstatVerifier/` (or a sibling Cecil
      check): scan the framework `Reactor.dll` itself (regardless of consumer
      publish) and assert it contains **zero** types under
      `Microsoft.UI.Reactor.Hosting.Devtools.*` (excluding the `Contract/`
      namespace types), no `PreviewCaptureServer`, no `LockfileRegistry`.
- [x] Add a negative-resolution test: an app without the Devtools package
      reference fails to resolve a devtools type at **compile** time, not runtime
      (spec §3.9, §15 Phase 2 gate).

### 2.9 Phase 2 tests (spec §12.1–§12.3)

- [x] Port/extend the Phase 1 unit + selftest fixtures so they still pass against
      the package-split shape (DevtoolsMenu shim returns `Empty()` with no
      package; trigger renders with the package + switch on + CLI).
- [x] E2E (`tests/Reactor.AppTests/Tests/`):
      `Spec051_DevtoolsCliFallback_EmitsActionableError` (switch off, published
      binary, assert exit code + stderr) and
      `Spec051_DevtoolsEndToEnd_SwitchOn_McpServerStarts` (switch on +
      `--devtools run --mcp-port 0`, assert MCP server announces ready and
      answers a `tools/list` JSON-RPC request). Passed locally against published
      NativeAOT binaries in final verification.

### 2.10 Phase 2 documentation (spec §13)

- [x] `docs/specs/022-packaging-and-distribution.md` (hand-written) — document
      the new optional `Microsoft.UI.Reactor.Devtools` package and the
      lockstep-versioning rule (§14 Q5).
- [x] `docs/_pipeline/templates/packaging.md.dt` — add the optional Devtools
      package to the packaging/install guidance; `mur docs compile` to
      regenerate. (Edit the template, not the compiled `docs/guide/*`.)
- [x] `docs/_pipeline/templates/extending-reactor-controls.md.dt` (generates
      `docs/guide/extending-reactor-controls.md`) — note devtools-adjacent hooks
      now live in the optional package; recompile.
- [x] Top-level `README.md` — mention the optional Devtools package alongside
      the main install line.
- [x] `CHANGELOG.md` — "New optional package `Microsoft.UI.Reactor.Devtools`."

### 2.11 Phase 2 exit gate (spec §15 Phase 2 gate)

- [x] ILSpy/Cecil decomp of `Reactor.dll` shows no devtools IL (no
      `Hosting.Devtools.*` impl types, no `PreviewCaptureServer`,
      no `LockfileRegistry`).
- [x] A consumer omitting the new package fails at **compile** time (not runtime)
      if it constructs devtools types directly.
- [x] Devtools developer flow unchanged in spirit: package ref + switch on +
      `--devtools run`/`app` yields the same surface as today.
- [ ] Full xunit + selftest + solution build green; AOT-selftests CI job passes; (local verification complete; AOT-selftests CI confirms on PR)
      mstat verifier (both lanes) green. (local verifier clean; devtools-trim-mstat CI lane confirms on PR)

---

## Out of scope for spec 051 (do **not** check these here)

- `Element.cs:845-851` Docking equality switch — second half of #498; the only
  remaining core→Docking edge after 051 lands.
- `LockfileRegistry`'s HTTP-vs-TCP probe refactor — orthogonal devtools-on
  optimization; may fold into Phase 2 since `LockfileRegistry` moves anyway.
- Source-gen `JsonSerializerContext` for `DevtoolsMcpServer` — orthogonal
  devtools-on optimization; not a gate requirement.
