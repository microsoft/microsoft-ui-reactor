# Demo Script Tool — Implementation Tasks

Derived from: `docs/specs/035-demo-script-tool-design.md`

Scope reminder: this is an internal sample app, but it doubles as a Reactor
exemplar for streaming, file-watching, async commands, packaging, and Windows 11
design. Tasks are sized to be paused/resumed; complete top-to-bottom within a
phase. Cross-phase ordering matters (don't wire UI before models/services exist;
don't ship without selftest fixtures and CI).

Conventions:
- App lives under `samples/apps/demo-script-tool/`. Paths in this doc are
  relative to that root unless stated otherwise.
- Source paths under the app: `App/`, `Package/`, `build/`, `demo-projects/`.
- The unpackaged head (`App/DemoScriptTool.csproj`) is the F5 default; the
  packaged head (`Package/DemoScriptTool.Package.wapproj`) builds the MSIX.
- Component code must not branch on packaged vs. unpackaged via `#if`; runtime
  detection through `Windows.ApplicationModel.Package.Current` only.
- Selftest fixtures live under `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`
  matching existing samples; UI-driver tests under `tests/Reactor.AppTests/`.
- Public services expose `IDisposable` / cancellation tokens — every long-lived
  resource has a teardown path tied to the owning component's lifetime.
- All user-visible text routed through resx where the existing samples do so;
  diagnostic-only `Debug.WriteLine` strings remain en-US literals.
- Spec section anchors are referenced in task bodies (e.g. `(spec §UI Layout)`)
  so reviewers can cross-check intent without re-reading the whole doc.

A task is "done" only when:
1. Code compiles under `Reactor.sln` warnings-as-errors.
2. Public API surface has XML doc comments (no `CS1591`).
3. New unit tests cover the happy path **and** every documented failure mode.
4. The three `REACTOR_A11Y_001..003` analyzers are clean (and promoted to errors
   in the app's `.csproj`).
5. Selftest fixture for the touched surface mounts successfully under
   Light / Dark / NightSky themes at 100% and 200% scaling.

---

## Phase 0: Scaffolding & solution wiring

### 0.1 Folder layout

- [ ] Create `samples/apps/demo-script-tool/` with subfolders `App/`, `App/Components/`, `App/Services/`, `App/Models/`, `App/Resources/`, `Package/`, `Package/Images/`, `build/`, `demo-projects/`.
- [ ] Add a one-line `README.md` in each subfolder *if existing samples do* (grep `samples/apps/chat/**/README.md`); otherwise skip — do not create README clutter the rest of the repo doesn't have.
- [ ] Add a top-level `samples/apps/demo-script-tool/README.md` placeholder; Phase 12 fills it in.

### 0.2 Unpackaged head — `App/DemoScriptTool.csproj`

- [ ] Mirror `samples/apps/chat/App/ChatSample.csproj` as the structural template (TargetFramework, `Platforms=x64;ARM64`, `UseWinUI=true`, `WindowsPackageType=None`, `LangVersion=preview`, `Nullable=enable`).
- [ ] `RootNamespace` = `DemoScriptTool.App`; `AssemblyName` = `DemoScriptTool`; `Product` = `Demo Script Tool`.
- [ ] `OutputType` = `WinExe`; `ApplicationIcon` = `Assets\demo-script-tool.ico` (Phase 11 supplies the icon — placeholder OK until then).
- [ ] `<PackageReference Include="Microsoft.WindowsAppSDK" Version="$(WindowsAppSDKVersion)" />` — use the repo-pinned version variable.
- [ ] `<ProjectReference Include="..\..\..\..\src\Reactor\Reactor.csproj" />`.
- [ ] Add the Reactor.Documents.Markdown project reference once Phase 2 confirms the project name (search `src/**/*Markdown*.csproj`); update this task with the exact path before checking off.
- [ ] Add the GitHub Models AI SDK NuGet package reference (`Azure.AI.OpenAI` / `Microsoft.Extensions.AI` family — confirm the redistributable package name during open-question §5 resolution; record the exact package + version in the `.csproj` and in the open-questions log).
- [ ] Promote the three accessibility analyzers to errors: `<WarningsAsErrors>REACTOR_A11Y_001;REACTOR_A11Y_002;REACTOR_A11Y_003</WarningsAsErrors>`.
- [ ] Embed `App/Resources/SystemPrompt.txt` as `<EmbeddedResource>` so the binary is self-contained.

### 0.3 Solution wiring

- [ ] Add `App/DemoScriptTool.csproj` to `reactor2.sln` under the `samples/apps` solution folder, matching the placement of `samples/apps/chat/App/ChatSample.csproj`.
- [ ] Verify `dotnet build samples/apps/demo-script-tool/App/DemoScriptTool.csproj` succeeds on a clean clone (no missing refs).
- [ ] Verify `dotnet format` / repo-wide formatter rules pass (run whatever the repo uses — check `.editorconfig`/`Directory.Build.props` for hooks).

### 0.4 Smoke shell

- [ ] Add `App/Program.cs` with `ReactorApp.Run<DemoScriptShell>(...)` calling into a minimal `DemoScriptShell` that renders an empty `Grid` with `Backdrop(BackdropKind.Mica)` and a `TitleBar("Demo Script Tool")`. (Spec §Window Backdrop, §Title Bar.)
- [ ] Set `Application.Current.HighContrastAdjustment = ApplicationHighContrastAdjustment.None` at startup. (Spec §High Contrast.)
- [ ] Confirm F5 launches a Mica-backed empty window in Light, Dark, and NightSky.

---

## Phase 1: Domain models

### 1.1 `Models/StepModel.cs`

- [ ] Public record-class `StepModel` holding: `int Number`, `string Title`, `string Prompt`, `string? GeneratedCode`, `string? Delta`, `BuildState BuildState`, `string? BuildOutput`, `int FixAttempts`.
- [ ] Expose `IObservableStream<string> CodeStream` for token-by-token streaming into the code viewer (spec §Reactor Integration Notes — Streaming into card collections).
- [ ] `void AppendToken(string text)` API that writes to the stream and updates `GeneratedCode` (single batched re-render — see spec 009).
- [ ] `enum BuildState { NotBuilt, Building, Succeeded, Fixing, Failed }`.
- [ ] Unit test: `AppendToken` produces a single render notification per frame even when called many times in a tight loop.
- [ ] Unit test: `BuildState` transitions are observable by an `IDisposable` subscription path that mirrors the rest of Reactor's models (use the same pattern as `ChatTimelineReducer`).

### 1.2 `Models/DemoScriptModel.cs`

- [ ] Public record-class `DemoScriptModel`: `string Title`, `string DemoPrompt`, `IReadOnlyList<StepModel> Steps`, `bool IsMultiFile`.
- [ ] `IsMultiFile` is parsed from a sentinel phrase in the demo prompt (spec §Single-file mode / §Multi-file mode); fall back to single-file. Document the exact sentinel(s) we accept.
- [ ] Round-trip-preserved unknown sections kept as `string RawTail` so the writer can append them verbatim (spec §Parser).
- [ ] Unit test: empty model produces a valid empty `demo-script.md` with the three required sections present.
- [ ] Unit test: `IsMultiFile` true/false drives the right parse outcome from a representative demo prompt for each.

---

## Phase 2: Markdown parser/serializer

### 2.1 Parser wrapper — `Services/DemoScriptParser.cs`

- [ ] Thin wrapper over `Reactor.Documents.Markdown` (SAX-style, spec §Parser; spec 013).
- [ ] Subscribe to section/list events; extract `# Title`, `## Demo Prompt`, `## Steps`. Anything else → captured into `RawTail` for round-trip preservation.
- [ ] Step entry recognizer: numbered list item with **bold** title prefix; rest of the item body becomes the step prompt. (Open question §2 — confirm the SAX surface covers numbered lists with rich-text bodies; if it does not, file a Reactor-side issue and use a regex shim with a `// TODO(spec 035 §OQ2)` marker.)
- [ ] Surface parse errors as a typed `DemoScriptParseError { int Line, int Column, string Message }` rather than throwing — Phase 7 renders this in the UI banner.
- [ ] Unit test: golden-file round-trip on a 5-step demo with extra unknown sections (must come back byte-identical except for normalized newlines).
- [ ] Unit test: malformed step list (missing bold title) yields a `DemoScriptParseError` pointing at the offending line.
- [ ] Unit test: title with inline markdown (e.g. backtick code spans) preserves the rendered text for the UI but the raw source for round-trip.

### 2.2 Store — `Services/DemoScriptStore.cs`

- [ ] `Task<DemoScriptModel> LoadAsync(string projectRoot, CancellationToken ct)`; reads `demo-script.md`. Missing file → returns scaffolded empty model (spec §UI Layout — Open Folder).
- [ ] `Task SaveAsync(DemoScriptModel model, string projectRoot, CancellationToken ct)` writing the three sections + appended `RawTail`. Atomic write: write to `demo-script.md.tmp`, `File.Move` over.
- [ ] Debounce policy is owned by the UI components (Phase 6); the store itself does not debounce.
- [ ] Unit test: scaffold-on-missing produces a model with empty Title / DemoPrompt / Steps and a writeback yields a usable file.
- [ ] Unit test: concurrent save calls serialize correctly (no torn files); use a small `SemaphoreSlim` inside the store.

---

## Phase 3: GitHub auth + Models client

### 3.1 `Services/GhAuth.cs`

- [ ] `Task<bool> EnsureAuthenticatedAsync(CancellationToken ct)` — checks for an existing GitHub token (env / gh CLI cache); if absent, spawns interactive `gh auth login` in a console window (spec §GitHub auth flow).
- [ ] On API auth failure, retry the login flow **once** before surfacing an error to the caller. Track retry count per process to avoid infinite loops.
- [ ] Public `event Action<string> StatusChanged` so the title bar's `RightHeader` can show "Authenticating…" state during the flow.
- [ ] Resilience: `gh` not on PATH → return a structured `AuthUnavailableError` so the inline banner in §Error Surfacing has actionable copy.
- [ ] Unit test (with mocked process runner): missing `gh` produces `AuthUnavailableError`; cancellation token cancels the wait.

### 3.2 `Services/GithubModelsClient.cs`

- [ ] Wraps the GitHub Models AI SDK; default model `claude-opus-4-6` (spec §AI Backend).
- [ ] `IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt, CancellationToken ct)` — yields token deltas.
- [ ] System prompt loaded once from the embedded `Resources/SystemPrompt.txt` (Phase 4 supplies the content).
- [ ] Translate SDK auth errors to a typed `AuthExpiredException` so `GenerationPipeline` can trigger the GhAuth retry once.
- [ ] Cancellation token must abort the streaming HTTP call (no orphaned background reads).
- [ ] Unit test (with a fake transport): a 50-token response produces 50 yielded strings in order.
- [ ] Unit test: auth-expired mid-stream propagates `AuthExpiredException` cleanly without partial token leakage.

---

## Phase 4: System prompt (Layer 1)

### 4.1 `Resources/SystemPrompt.txt`

- [ ] Author per spec §Layer 1 — Tool System Prompt: role, per-step output contract (full file + presenter delta), continuity rule, runability rule, single-vs-multi-file directive, delta format, and **fix mode** behavior.
- [ ] Specify the exact output shape we will parse (e.g. fenced code blocks tagged with the step number; delta in a labeled section). Phase 5 parses this — keep the shape simple and machine-grep-able.
- [ ] Include a one-shot example for a single-file step and a one-shot example for a multi-file step so the model anchors on output format.
- [ ] Document the parsed output shape in `App/Resources/SystemPrompt.format.md` (next to the `.txt`) so future spec authors can update both at once.

### 4.2 Output parser — `Services/GeneratedOutputParser.cs`

- [ ] Stateful streaming parser: consumes token deltas, surfaces `(int stepNumber, string codeChunk)` and `(int stepNumber, string deltaChunk)` events as they arrive.
- [ ] Robust to fenced-code-block delimiters spanning a token boundary (the streaming chunks split arbitrarily).
- [ ] Unit test: split a known model output into 1-character chunks and feed them; the reassembled per-step code and delta must equal the original.
- [ ] Unit test: malformed output (missing closing fence on the last step) emits a partial-step warning but does not throw — the UI shows whatever arrived.

---

## Phase 5: Generation pipeline

### 5.1 `Services/GenerationPipeline.cs`

- [ ] Orchestrates the full pipeline (spec §Generation Pipeline):
  1. Read & parse `demo-script.md`.
  2. Build the API request: Layer 1 system prompt + Layer 2 demo prompt + all step prompts in sequence (spec §Two-Layer Prompt Architecture).
  3. Stream via `GithubModelsClient.StreamAsync`.
  4. Feed deltas into `GeneratedOutputParser`.
  5. As each step completes, write `step-NN.cs` (single-file) or the `step-NN/` folder (multi-file) and trigger Phase 6 build-and-fix.
- [ ] Steps generated **sequentially** so each can inform the next (spec §Streaming / Generation Animation).
- [ ] `CancellationToken` support: cancellation halts at the current step; previously written files remain (spec §Cancellation). UI surfaces a "Cancelled at step N" toast via `IStatusReporter`.
- [ ] On `AuthExpiredException`, run `GhAuth.EnsureAuthenticatedAsync` once, then retry the in-flight call. After a second failure, surface to UI.
- [ ] Idempotent overwrite policy (spec §Regeneration is destructive) — overwrite without prompts.
- [ ] Unit test: 3-step demo produces 3 files in order, each appearing on disk only after the previous step completes.
- [ ] Unit test: cancellation between steps 2 and 3 leaves steps 1–2 on disk and step 3 absent.

### 5.2 File writer — `Services/StepFileWriter.cs`

- [ ] `Write(StepModel step, string projectRoot, bool multiFile)` — single-file mode writes `step-NN.cs` directly under `projectRoot`; multi-file mode writes `projectRoot/step-NN/Program.cs` plus any extra files supplied by the model.
- [ ] Multi-file: model output is expected to label additional files (per Phase 4 contract); writer creates them under `step-NN/`. If the model emits a `.csproj`, that wins; otherwise the writer scaffolds a minimal one (open question §1 — settle on which during impl).
- [ ] Unit test: single-file mode produces `step-01.cs` with expected contents.
- [ ] Unit test: multi-file mode with `Program.cs` + `Helpers.cs` produces both under `step-01/`.

---

## Phase 6: Build-and-fix pipeline + Run pipeline

### 6.1 `Services/DotnetRunner.cs`

- [ ] Encapsulates all `dotnet` shell-outs so the UI never spawns processes directly (spec §Build invocation).
- [ ] `Task<BuildResult> BuildAsync(StepModel step, ...)`: single-file → `dotnet run --project step-NN.cs` (or transient SDK-style project wrap if file-based apps unavailable on this SDK; detect via `dotnet --version`). Multi-file → `dotnet build` in `step-NN/`. Working directory per spec.
- [ ] `Task RunAsync(StepModel step, ...)`: single-file → `dotnet run --project step-NN.cs`; multi-file → `dotnet run --no-build` after a successful build. Spawned process is non-blocking; the UI is informed of spawn success/failure only.
- [ ] `BuildResult { bool Succeeded, string CombinedOutput, int ExitCode }`. Combined stdout + stderr captured; never logged unless requested.
- [ ] Capture `dotnet run` stderr to `obj/demo/step-NN.runlog` per spec §Error Surfacing.
- [ ] Cancellation token kills the underlying process (use `Process.Kill(entireProcessTree: true)`).
- [ ] Open question §4 resolution recorded in this file's comments before merging.
- [ ] Unit test (with mocked process runner): build success path and build failure path both produce correct `BuildResult`.
- [ ] Unit test: cancellation during build kills the process and yields `Succeeded == false`.

### 6.2 Build-and-fix loop in `GenerationPipeline.cs`

- [ ] Per step (spec §Build loop): post-write, run `BuildAsync`. On success → continue. On failure → call `GithubModelsClient.StreamAsync` with the fix-mode prompt (system prompt §Fix mode), the prior code, and the compiler error.
- [ ] Stream the corrected code through the same `StepModel.AppendToken` path so the UI animates the fix identically to initial generation.
- [ ] Cap at **3 fix attempts** per step; on exhaustion, set `BuildState = Failed` and store the final compiler output in `StepModel.BuildOutput`.
- [ ] A failed step does NOT halt the pipeline (spec §Build loop §5).
- [ ] Unit test: deterministic-fail mock model exhausts 3 attempts and surfaces `BuildState.Failed`.
- [ ] Unit test: fix-on-second-try produces `BuildState.Succeeded` and updates `FixAttempts == 1`.

### 6.3 Run-from-card command

- [ ] `RunStepCommand` registered via Reactor's commanding system (spec 012; spec §Commanding). Bound to ▶ Run on each card.
- [ ] Auto-disable predicate ties to: existence of generated file(s) on disk + no in-flight run for this step.
- [ ] Unit test: command is disabled when no file exists; flips to enabled after generation completes.

---

## Phase 7: Filesystem watcher

### 7.1 `Services/DemoScriptWatcher.cs`

- [ ] `FileSystemWatcher` on `projectRoot/demo-script.md` (spec §Filesystem Watcher).
- [ ] Marshal events onto the UI dispatcher via `UseEffect` cleanup (spec 020).
- [ ] On Changed: discard any in-flight debounced edit buffer and reload from disk; raise a "External changes picked up" toast.
- [ ] On Deleted: fall back to scaffolded empty state.
- [ ] Generated `step-NN.cs` files are NOT watched (spec §Filesystem Watcher).
- [ ] Debounce inbound FS events at ≤100 ms to coalesce save-storms (editors often emit multiple rapid events).
- [ ] Unit test: writing the same file from a second process triggers exactly one reload (debounced).
- [ ] Unit test: deleting the file produces a scaffold-empty model.

---

## Phase 8: UI components

### 8.1 `App/Components/HeaderBar.cs`

- [ ] Implements the title-bar `Content` slot (spec §Title Bar): **Open Folder**, **Generate All**, **Export Speaker Notes** as `ICommand`s (spec 012).
- [ ] `RightHeader` shows `generationStatus` text via `TextBlock(...).FontSize(12).Opacity(0.6)`.
- [ ] `Generate All` is the primary button — accent override via `.Resources(...)` overriding `ButtonBackground`/`ButtonForeground` (spec §Theming — never `.Background(Theme.Accent)` directly).
- [ ] Disabled-while-generating, with a cancel affordance shown in the same button slot.
- [ ] AutomationName + AutomationLandmarkType.Navigation on the action group (spec §Accessibility).

### 8.2 `App/Components/DemoPromptPanel.cs`

- [ ] Multi-line `TextBox` bound to `DemoScriptModel.DemoPrompt`.
- [ ] 500 ms debounced writeback to `DemoScriptStore.SaveAsync` (spec §Demo Prompt Panel).
- [ ] Wrap in `FormField` so labels and error states wire to AT (spec §Accessibility).
- [ ] Background `Theme.LayerFill`; padding 16; bottom margin 12 (spec §Layout & Spacing).
- [ ] Unit test (UI-driver): typing "abc" produces exactly one save call after the debounce window closes; typing "abc" then "d" within the window produces one save call with the final text.

### 8.3 `App/Components/StepCard.cs`

- [ ] Bordered surface with `Theme.CardBackground`, `Theme.CardStroke`, corner radius 8, inner padding 16, hover lift via `.Translation(0, 0, isHovered ? 32 : 0)` and `Border.Shadow = new ThemeShadow()` (spec §Card Surface).
- [ ] Three-column `Grid`: `[Pixels(280), Star(), Pixels(140)]` for Prompt / Code / Actions; rows `[Auto, Auto]` for header + body (spec §Layout & Spacing — exact code block).
- [ ] Header: `SubHeading($"Step {n} — {title}")` (spec §Typography).
- [ ] Column captions: `Caption("PROMPT")`, `Caption("GENERATED CODE")`, `Caption("ACTIONS")`.
- [ ] Prompt column: editable `TextBox` with the same 500 ms debounce as the demo prompt panel; updates the matching `StepModel.Prompt`.
- [ ] Code column: read-only `TextView` (spec §Reactor Integration Notes — Streaming into card collections), `IsAppendOnly = true`, monospace font via `.Set(tb => tb.FontFamily = ...)` (spec §Typography). Bound to `StepModel.CodeStream`.
- [ ] Actions column: ▶ Run, 📋 Copy Delta — both `ICommand`-bound. AutomationNames per spec §Accessibility.
- [ ] Card uses `.WithKey($"step-{n}")` for stable reconciliation identity (spec §Reconciliation).
- [ ] `.PositionInSet(n, total)` and `Cursor = Hand` per spec §Card Surface / §Accessibility.
- [ ] Build-state indicator: shape + text per spec §Build state indicators table (✓, "Fixing...", "Build failed"). Inline build output (read-only monospace `TextBlock`) shown only on `BuildState.Failed`.
- [ ] Card mounts under `.Landmark(AutomationLandmarkType.Main)` from the parent `StepsPanel`.
- [ ] Selftest fixture: card renders in `BuildState.NotBuilt`, `Succeeded`, `Fixing`, `Failed` — screenshot in Light/Dark/NightSky at 100/150/200/250%.

### 8.4 `App/Components/StepsPanel.cs`

- [ ] Vertical scroller of `StepCard` keyed by step number.
- [ ] Outer card vertical gap: 12 px (spec §Layout & Spacing).
- [ ] Wraps in `.Landmark(AutomationLandmarkType.Main)`.
- [ ] Empty state: a `TextBlock(...).Foreground(Theme.SecondaryText)` placeholder ("No steps yet — add steps in `demo-script.md`").
- [ ] Selftest: empty demo and 10-step demo (scrolled) per spec §Test Matrix.

### 8.5 `App/DemoScriptShell.cs`

- [ ] Top-level component composing `HeaderBar` + `DemoPromptPanel` + `StepsPanel` inside the Mica-backdrop root grid.
- [ ] Root must NOT paint an opaque background (drop `Theme.SolidBackground`) so Mica shows through (spec §Window Backdrop).
- [ ] Wires the `IStatusReporter` and `UseAnnounce` (polite) for generation progress messages (spec §Accessibility — "Generating step 2 of 5...").
- [ ] Build failure (max retries) announced via `UseAnnounce` (assertive).
- [ ] Window edge inset 16 px on the main body container, NOT on every card (spec §Layout & Spacing).
- [ ] Selftest: empty state, mid-generation state, all-complete state, and error state.

### 8.6 Streaming animation

- [ ] Cards fade-and-slide in on first generation: `Border(card).WithTransitions(Transition.Fade, Transition.Slide(Edge.Bottom))` (spec §Streaming Animation; spec 014).
- [ ] No gradient or parallax animation (HC + design-skill rule).
- [ ] Verify token-stream re-renders are O(1) per card via the `.WithKey` + `IObservableStream<string>` pattern (spec §Reconciliation).
- [ ] Selftest: a 10-step stream completes without any frame skipping > 33 ms in the captured trace (use existing perf-trace harness from spec 034 if available; otherwise file follow-up).

---

## Phase 9: Speaker notes export

### 9.1 `Services/SpeakerNotesExporter.cs`

- [ ] `Task ExportAsync(DemoScriptModel model, string projectRoot, CancellationToken ct)` writing `speaker-notes.txt` per spec §Speaker notes format.
- [ ] Skip steps with no delta yet; do not error if `Steps.All(s => s.Delta is null)` — write a header + "No deltas generated yet." placeholder.
- [ ] Atomic write (`.tmp` + `File.Move`).
- [ ] Toast confirmation on success (spec §Speaker Notes Export).
- [ ] Unit test: 3-step model with deltas produces a file matching the canonical format byte-for-byte (golden file).

### 9.2 Per-step Copy Delta command

- [ ] `CopyDeltaCommand` writes `StepModel.Delta` to the clipboard via `DataPackage` API.
- [ ] Disabled when `StepModel.Delta is null`.
- [ ] AutomationName = `Copy speaker notes for step {n}` (spec §Accessibility).
- [ ] Unit test (UI-driver): clicking the button puts the expected string on the clipboard.

---

## Phase 10: Error surfacing & non-modal UX

### 10.1 Inline banner component

- [ ] Reusable banner control for: auth failure, malformed `demo-script.md`, generation halts (spec §Error Surfacing).
- [ ] Sits above `StepsPanel`; hides the steps panel only for the malformed-markdown case.
- [ ] Uses `SystemFillColorCriticalBrush` accent + shape (icon) + text — never color alone (spec §High Contrast).
- [ ] Selftest: banner renders correctly in NightSky (HC).

### 10.2 Toast surface

- [ ] Wire toast notifications for: external file change picked up, speaker notes exported, run failure (`Run failed (exit code N) — see output`).
- [ ] No modal dialogs anywhere — all status surfaces are non-blocking (spec §Error Surfacing).

---

## Phase 11: Visual assets & app icon

### 11.1 Icon

- [ ] Author `App/Assets/demo-script-tool.ico` (multi-resolution: 16/24/32/48/64/256). Match the visual language of the other sample icons (chat, regedit-winui).
- [ ] Reference from `<ApplicationIcon>` in the unpackaged `.csproj`.

### 11.2 Package visual assets

- [ ] Populate `Package/Images/` with `StoreLogo.png`, `Square44x44Logo.png`, `Square150x150Logo.png`, `Wide310x150Logo.png`, `SplashScreen.png` at the WinUI 3 / WinAppSDK required resolutions.
- [ ] All assets reference the same brand mark as the `.ico`.

---

## Phase 12: Packaging (MSIX)

### 12.1 `Package/DemoScriptTool.Package.wapproj`

- [ ] Standard WinAppSDK packaging project referencing `..\App\DemoScriptTool.csproj`.
- [ ] `Platforms=x64;ARM64` to match the unpackaged head.
- [ ] Wire `Package.appxmanifest` (Phase 12.2) and `Package/Images/` (Phase 11.2).

### 12.2 `Package/Package.appxmanifest`

- [ ] Identity: `andersonch.DemoScriptTool` (per-user, not Store-bound for v1) (spec §Package.appxmanifest).
- [ ] Publisher: `CN=DemoScriptTool Dev`.
- [ ] MinVersion / MaxVersionTested: Windows 11 22621.
- [ ] Visual assets bound to `Package/Images/`.
- [ ] Capabilities: `runFullTrust`. **Do not** add `broadFileSystemAccess` — folder picker is sufficient (spec §Capabilities).
- [ ] App entry point and protocol associations: none in v1.

### 12.3 `build/sign-cert.ps1`

- [ ] Idempotent: exit if `build/DemoScriptTool.pfx` exists (spec §Self-signed certificate).
- [ ] Generate via `New-SelfSignedCertificate` per the spec's exact PowerShell snippet.
- [ ] Export `.pfx` (private; password from `$env:DEMOSCRIPT_PFX_PASSWORD` or secure-string prompt) and `.cer` (public).
- [ ] Print thumbprint to stdout for the wapproj to consume.
- [ ] Add `build/DemoScriptTool.pfx` and `build/DemoScriptTool.pfx.password` to `.gitignore` at the repo root (or the nearest scoped `.gitignore`). Verify the public `.cer` is NOT ignored.

### 12.4 `build/package.ps1`

- [ ] One-shot script: ensure cert → `dotnet publish` → `msbuild` the wapproj with the SideloadOnly + signing parameters from the spec — exact arg block per spec §build/package.ps1.
- [ ] Accept `-Arch x64,arm64` parameter; produce `out/DemoScriptTool_<version>_<arch>.msix` for each architecture (spec §ARM64).
- [ ] On success, print full path(s) of the produced `.msix`(es).
- [ ] Surface a friendly error if `$env:DEMOSCRIPT_PFX_PASSWORD` is unset and no interactive password was provided.

### 12.5 `build/install.ps1`

- [ ] Recipient one-liner per spec §build/install.ps1: `Import-Certificate` into `Cert:\LocalMachine\TrustedPeople`, then `Add-AppxPackage`.
- [ ] Detect non-elevated invocation and print a clear message ("Re-run from an elevated PowerShell — Import-Certificate to LocalMachine requires admin").
- [ ] Default `.msix` path to the most recently produced one in `out/` if `-Path` is not supplied; otherwise honor `-Path`.

### 12.6 Verify packaged path

- [ ] `F5` against `Package/DemoScriptTool.Package.wapproj` launches the packaged app under MSIX deployment.
- [ ] Verify Mica still renders in the packaged head (Mica-on-MSIX is a known-working but worth-confirming combo).
- [ ] Verify `Windows.ApplicationModel.Package.Current` works in packaged mode and throws/returns null in unpackaged mode (try/catch as spec §Run modes prescribes — record the exact behavior in a comment).
- [ ] Smoke test: install via `build/install.ps1` on a clean Windows 11 VM and confirm "Demo Script Tool" appears in Start, launches, opens a folder, and runs a step.

---

## Phase 13: CI workflow

### 13.1 `.github/workflows/sample-demo-script-tool.yml`

- [ ] Trigger on PRs touching `samples/apps/demo-script-tool/**`.
- [ ] Job 1: `dotnet build samples/apps/demo-script-tool/App/DemoScriptTool.csproj` (warnings-as-errors).
- [ ] Job 2: run the selftest fixtures (`tests/Reactor.AppTests.Host` filter for `DemoScriptTool*`).
- [ ] Job 3: generate an ephemeral CI signing cert (NOT the dev cert), run `build/package.ps1`, and upload the resulting `.msix` as a build artifact (spec §CI and PR validation).
- [ ] Reuse the existing matrix style from other `sample-*` workflows in `.github/workflows/` if any exist; otherwise model on the closest analog and record the choice in a top-of-file comment.

---

## Phase 14: README & docs

### 14.1 `samples/apps/demo-script-tool/README.md`

- [ ] Section 1 — **Run from source**: `dotnet run` against `App/DemoScriptTool.csproj`. Note the GitHub auth flow on first launch.
- [ ] Section 2 — **Build a shareable MSIX**: `pwsh build/package.ps1` walkthrough; explain the first-run cert prompt.
- [ ] Section 3 — **Install on another machine**: `Import-Certificate` + `Add-AppxPackage` + the elevation requirement (spec §Documented in the README).
- [ ] Add a 4th section — **Authoring a demo**: brief tour of the Markdown format (`# Title`, `## Demo Prompt`, `## Steps`) with one example.
- [ ] Cross-link to `docs/specs/035-demo-script-tool-design.md` for design details.
- [ ] Include a screenshot of the running app once Phase 8 is stable.

### 14.2 Sample demo projects

- [ ] `demo-projects/spectre-chat/demo-script.md` — single-file Spectre.Console chat demo (mirroring spec §The Markdown Format example).
- [ ] `demo-projects/aspire-hello/demo-script.md` — multi-file .NET Aspire hello-world demo. Demo prompt explicitly declares multi-file mode with project-setup instructions per spec §File Layout.
- [ ] Both demos must successfully `Generate All` end-to-end during pre-merge validation (record the run in the PR body).

---

## Phase 15: Selftest matrix & accessibility verification

### 15.1 Theme matrix

- [ ] Selftest fixtures mount the full app and screenshot in: Light, Dark, NightSky (HC) — empty demo and 10-step demo (spec §Test Matrix).
- [ ] Capture at 100 / 150 / 200 / 250% display scaling.
- [ ] Capture once at maximum text scaling (Settings > Accessibility > Text size = max).

### 15.2 Accessibility

- [ ] Verify the three `REACTOR_A11Y_001..003` analyzers report zero warnings (they're errors per Phase 0.2).
- [ ] Run a Narrator pass: heading hierarchy (TitleBar=L1, SubHeading per card=L2 — no skips); landmark navigation (Navigation for header actions, Main for steps panel); per-card `PositionInSet` reports correctly.
- [ ] Verify build-state indicators are distinguishable in HC by shape+text alone (no color-only signaling).
- [ ] Verify keyboard-only flow: tab order through HeaderBar → DemoPromptPanel → each card's prompt → ▶ Run → 📋 Copy Delta. Ctrl+G triggers Generate All (spec §Commanding).

### 15.3 Reconciliation correctness

- [ ] Add a self-test that mutates the markdown file externally to add/remove/reorder steps and confirms `.WithKey($"step-{n}")` keeps card identity stable for survivors and tears down only the changed cards (spec §Reconciliation).

---

## Phase 16: Open question resolution & cleanup

### 16.1 Resolve carried-over open questions (spec §Open Questions)

- [ ] §1 Streaming pattern: confirm `IObservableStream<string>` + `TextView` with `IsAppendOnly = true` is the recommended idiom; if Reactor needs an API addition, file a separate spec/issue and link from this task.
- [ ] §2 Markdown parser API: confirm SAX surface covers numbered lists with rich-text bodies. If gaps exist, document them inline in `Services/DemoScriptParser.cs` with a `// TODO(spec 035 §OQ2)` and file a Reactor-side issue.
- [ ] §3 Filesystem watcher / edit conflict policy: external-wins is the default; record any deviations in `Services/DemoScriptWatcher.cs` XML doc.
- [ ] §4 Build invocation: confirm exact `dotnet` invocation for build-without-run in both modes; record final commands in `Services/DotnetRunner.cs` XML doc.
- [ ] §5 GitHub Models SDK packaging: confirm SDK is redistributable under terms used in other `samples/apps/`; record license check in `App/DemoScriptTool.csproj` comment + repo `THIRD_PARTY_NOTICES*` if one exists.

### 16.2 Final review

- [ ] Cross-check all spec §Out of Scope items are NOT implemented (no per-step regen, no diff viewer, no settings UI, no in-app terminal, no drag reorder, no syntax highlighting, no multi-demo windows, no cloud sync).
- [ ] Walk through `docs/specs/035-demo-script-tool-design.md` section by section confirming each requirement is either a checked box above or has a deliberate, recorded deviation.
- [ ] Update `CHANGELOG.md` (per the convention established by spec 033) with a `Spec 035 — Demo Script Tool sample app` entry under `## [Unreleased]`.
- [ ] Tag PR as touching the `samples/apps/demo-script-tool/**` path so the Phase 13 CI workflow runs.
