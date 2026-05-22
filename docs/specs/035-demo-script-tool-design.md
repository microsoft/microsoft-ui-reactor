# Demo Script Tool — Sample App Design Spec

## Overview

Demo Script Tool is a WinUI 3 desktop sample app built on Microsoft.UI.Reactor (Reactor). It lets a
presenter author a code demo as a Markdown file (`demo-script.md`), generate
runnable .NET code and speaker notes for each step via the GitHub Models AI
SDK, and execute individual steps directly from the UI through `dotnet run`.

The app ships under `samples/apps/demo-script-tool/` alongside the existing
sample apps (`chat`, `headtrax`, `regedit-winui`, etc.) and serves as a
medium-complexity exemplar exercising:

- Reactor's SAX-style Markdown parser (read/write of `demo-script.md`)
- Streaming state updates into a scrollable collection of cards
- File-system watcher integration with Reactor's hook model
- Long-running async work (AI generation, dotnet build, dotnet run) driven
  from UI commands without blocking the render loop
- Clipboard, file picker, and process-launching interop

All artifacts (the script, generated code files, exported speaker notes) live
on disk as plain text in a user-selected project root, so the demo project is
fully version-controllable independently of the tool.

---

## Goals

- Let a presenter author a demo as a structured list of step prompts in a
  single Markdown file.
- Generate runnable .NET code per step via AI (GitHub Models — Claude
  `claude-opus-4-6` by default).
- Produce a "presenter delta" per step: annotated speaker notes describing
  what code to type or paste, with precise placement instructions.
- Execute individual steps directly from the tool via `dotnet run`.
- Store all artifacts as plain files (Markdown + code) that live with the
  project in version control.
- Demonstrate idiomatic Reactor patterns for streaming, file-watching, and
  async command pipelines in a real (non-toy) sample.

## Non-goals

- Not a XAML sample. The app is pure Reactor C#.
- Not a generic AI client. The model, system prompt, and contract are fixed
  for v1.
- No per-step regeneration, diff viewer, settings UI, in-app terminal, drag
  reorder, or cloud sync (see [Out of Scope](#out-of-scope-v1)).

---

## File Layout

All files for a given demo live in a single **project root directory** opened
by the user on launch. The tool itself has no global state — closing and
reopening on the same folder reproduces the same UI.

### Single-file mode

```
/project-root/
  demo-script.md
  step-01.cs
  step-02.cs
  step-03.cs
```

### Multi-file mode

```
/project-root/
  demo-script.md
  step-01/
    Program.cs
    step-01.csproj
    ...
  step-02/
    Program.cs
    step-02.csproj
    ...
```

The user declares single-file or multi-file intent in the **Demo Prompt**
section of `demo-script.md`. The AI and tool both respect this declaration.
If a `.csproj` is needed, the user notes this in the Demo Prompt and is
responsible for including project setup instructions in the prompt.

---

## The Markdown Format (`demo-script.md`)

The tool reads and writes this file. It is human-editable outside the tool.

```markdown
# Demo Title

## Demo Prompt

This demo shows how to build a real-time chat UI using .NET 10 top-level
statements and the Spectre.Console library. Single-file mode. Each step
should compile and run standalone with `dotnet run`. Use NuGet inline
references where needed.

## Steps

1. **Hello World baseline**
   Start with a minimal top-level statements app that prints a styled
   greeting using Spectre.Console. This establishes the file structure and
   confirms the NuGet reference works.

2. **Add a message list**
   Render a static list of fake chat messages using Spectre.Console's table
   component. Show how to style sender names in different colors.

3. **Simulate live updates**
   Add a loop that appends a new message every second using
   AnsiConsole.Live. The app should run until the user presses a key.
```

### Markdown Sections

| Section | Required | Description |
|---|---|---|
| `# Title` | Yes | Top-level heading, displayed in the app header |
| `## Demo Prompt` | Yes | Persistent context passed to the AI for every generation call |
| `## Steps` | Yes | Numbered list of step prompts; each step can be multiple sentences |

Step entries use a bold title followed by one or more sentences of detail.
Both the title and body are passed to the AI as the per-step prompt.

### Parser

Parsing uses Reactor's built-in SAX-style Markdown parser
(`Reactor.Documents.Markdown`). The tool subscribes to the parser's section
and list events to extract the three sections above; nothing else in the
file is interpreted. Unknown sections are preserved verbatim on round-trip
write.

---

## Two-Layer Prompt Architecture

### Layer 1 — Tool System Prompt (baked into the application)

Hardcoded in the binary. Governs AI behavior and output contract:

- **Role**: You are a code demo script generator. Your job is to produce
  complete, runnable .NET code for each step of a live coding demo, plus
  presenter notes.
- **Per-step output contract**: Each step must produce two artifacts: (1) a
  **full runnable file** for that step, and (2) a **presenter delta** — an
  annotated description of what the presenter types or pastes, referencing
  specific locations in the code (e.g., "Paste this method after the
  `BuildLayout()` call on line 12").
- **Continuity rule**: Each step's full code must build naturally on the
  previous step. The delta should only describe the net-new changes from
  the prior step.
- **Runability rule**: Every step's code file must compile and run
  independently via `dotnet run`. Do not produce incomplete stubs.
- **Single vs. multi-file**: Respect the declaration in the Demo Prompt.
  For single-file, output one `.cs` file. For multi-file, output all files
  needed for that step.
- **Delta format**: Use plain English with precise placement instructions.
  Example: *"Add the following method inside the `App` class, directly
  after `OnStartup`:"* followed by the code block. Avoid vague instructions
  like "add this somewhere."
- **Fix mode** (see [Build-and-Fix Pipeline](#build-and-fix-pipeline)):
  When given a build error and the previously generated code, return only
  the corrected complete file — no explanation, no partial diffs. Preserve
  all intent from the original generation; fix only what the compiler
  rejects.

### Layer 2 — Demo Prompt (user-authored, per demo)

The `## Demo Prompt` section of `demo-script.md`. Passed to the AI on every
generation call as persistent context. Should include:

- The technology/framework being demonstrated
- Single-file vs. multi-file declaration
- Target runtime / NuGet packages
- Any constraints (e.g., "do not use LINQ", "keep each file under 80 lines")
- Audience level (beginner, intermediate, expert)

---

## UI Layout

The UI is a single window divided into three vertical regions.

### 1. Header Bar

- App title / current demo title (from `# Title` in the Markdown)
- **Open Folder** button — opens a folder picker; loads `demo-script.md`
  from the selected directory. If the file does not exist, the tool offers
  to scaffold one with empty Title / Demo Prompt / Steps sections.
- **Generate All** button — triggers full AI generation for all steps
  (destructive, no confirmation)
- **Export Speaker Notes** button — exports all presenter deltas to a
  single `speaker-notes.txt` file in the project root

### 2. Demo Prompt Panel (top of main area)

A single multi-line text box bound to the `## Demo Prompt` section. Edits
are written back to `demo-script.md` automatically with a 500 ms debounce
after the user stops typing. A `FileSystemWatcher` on `demo-script.md`
reloads the UI if the file is modified outside the app (see
[Filesystem Watcher](#filesystem-watcher)).

### 3. Steps Panel (scrollable, main body)

Each step is rendered as a **card** with three columns:

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Step 1 — Hello World baseline                                            │
├─────────────────────┬────────────────────────────┬───────────────────────┤
│ PROMPT              │ GENERATED CODE             │ ACTIONS               │
│                     │                            │                       │
│ [Multi-line         │ [Read-only plain text      │ [▶ Run]               │
│  text box]          │  viewer, monospace font]   │ [📋 Copy Delta]       │
│                     │                            │                       │
└─────────────────────┴────────────────────────────┴───────────────────────┘
```

- **Prompt column**: Editable multi-line text box. User edits step prompt
  here; changes persist to `demo-script.md` with the same 500 ms debounce
  as the Demo Prompt panel.
- **Generated Code column**: Read-only plain text viewer using a
  monospace/coding font. No syntax highlighting required for v1. Shows an
  empty/placeholder state before generation.
- **Actions column**:
  - **▶ Run** — executes this step's code via `dotnet run`. Disabled if no
    generated file exists for that step.
  - **📋 Copy Delta** — copies this step's presenter delta to the
    clipboard. Disabled if no delta exists yet.

### Streaming / Generation Animation

When **Generate All** is triggered:

- Steps are generated sequentially so each step can inform the next.
- Each card animates in as its generation completes (fade or slide
  transition driven by Reactor's animation hooks; see spec 014).
- A progress indicator shows the current step being processed
  ("Generating step 2 of 5...").
- Generated code streams into the code viewer token-by-token as it arrives
  from the API.
- The **Generate All** button is disabled and shows a cancel affordance
  during generation.

---

## Design Guidelines

This sample is also a vehicle for Reactor's Windows 11 design guidance — it
should look and feel like a first-class Windows 11 app. The rules below
restate the relevant pieces of `skills/design.md` for this app's surfaces.
When in doubt, defer to that skill; this section calls out the choices the
implementing agent must make.

### Window Backdrop

Use **Mica** as the window backdrop. The app is a long-lived shell with
clear chrome, which is the canonical Mica use case.

```csharp
return Grid([GridSize.Star()], [GridSize.Auto, GridSize.Star()],
    titleBar.Grid(row: 0),
    mainBody.Grid(row: 1)
).Backdrop(BackdropKind.Mica);
```

For Mica to show through, the root element must not paint an opaque
background. Drop any `Theme.SolidBackground` from the root. Per-region
backgrounds (cards, the prompt panel) still apply, just not on the root.

### Title Bar

Use the `TitleBar(...)` factory as the window's top region — never a custom
header row. It integrates with the Windows caption (drag region, system
menu, min/max/close) and themes correctly.

```csharp
var titleBar = (TitleBar("Demo Script Tool") with
{
    Subtitle = currentDemoTitle ?? "(no demo open)",
    Content = HeaderActions(),       // Open Folder / Generate All / Export
    RightHeader = TextBlock(generationStatus)
        .FontSize(12).Opacity(0.6),
}).Flex(shrink: 0);
```

Place the **Open Folder**, **Generate All**, and **Export Speaker Notes**
commands in the `Content` slot of the title bar so they live in the
caption strip rather than a redundant second header row. Per-step actions
(▶ Run, 📋 Copy Delta) live on the cards, not in the title bar.

### Typography

Use semantic text factories — never raw `FontSize` / `FontWeight` for
standard UI text:

| Surface | Factory |
|---|---|
| Demo title (rendered in `TitleBar` Subtitle) | (handled by `TitleBar`) |
| Step card title (`Step 1 — Hello World baseline`) | `SubHeading(...)` (20px, 600) |
| Column labels (`PROMPT`, `GENERATED CODE`, `ACTIONS`) | `Caption(...)` (12px) |
| Body / prompt text | `TextBlock(...)` (14px) |
| Inline build error output | `TextBlock(...)` with monospace font family |
| Empty-state placeholder ("No code generated yet.") | `TextBlock(...).Foreground(Theme.SecondaryText)` |

The generated-code viewer uses `{ThemeResource SymbolThemeFontFamily}`-
style override to set a monospace coding font via `.Set(tb => tb.FontFamily = ...)`.

For emphasis use **SemiBold (600)**, not Bold (700) — except `Heading()`,
which we don't use in this app.

### Theming

All colors come from `Theme.*` tokens. No hardcoded hex on themed surfaces.

| Surface | Token |
|---|---|
| Step card background | `Theme.CardBackground` |
| Step card stroke | `Theme.CardStroke` |
| Demo Prompt panel background | `Theme.LayerFill` |
| Body / prompt text | `Theme.PrimaryText` |
| Empty-state and disabled labels | `Theme.SecondaryText` / `Theme.DisabledText` |
| Primary CTA (Generate All) accent | `Theme.Accent` (resource override on Button) |
| Build-success checkmark | `SystemFillColorSuccessBrush` (via `Theme.Ref`) |
| Build-warning amber ("Fixing...") | `SystemFillColorCautionBrush` |
| Build-failure red | `SystemFillColorCriticalBrush` |
| Dividers between cards | `DividerStrokeColorDefaultBrush` |

The **Generate All** primary button uses `.Resources(...)` to override
`ButtonBackground` / `ButtonForeground` to the accent palette — never
`.Background(Theme.Accent)` directly, which loses hover/pressed/disabled
states.

### High Contrast

The app must work in HC (especially NightSky):

- No opacity tricks on borders or text — encode translucency in alpha for
  Light/Dark only.
- The step card's outline becomes a `SystemColorHighlightColor` border in
  HC so the interactive region is visible.
- The "build succeeded / fixing / failed" indicators must use shape +
  text (✓, "Fixing...", "Build failed"), not color alone.
- The streaming code viewer never animates color/gradient — text appears
  in a single system brush in HC.
- Set `Application.Current.HighContrastAdjustment = ApplicationHighContrastAdjustment.None`
  at startup.

### Layout & Spacing (4 px grid)

Every margin, padding, and gap is a multiple of 4. Concrete values:

- **Window edge inset:** 16 px on each side, applied at the main body
  container (not at every card).
- **Demo Prompt panel padding:** 16 px; bottom margin 12 px before the
  steps panel.
- **Card outer gap (vertical between cards):** 12 px.
- **Card inner padding:** 16 px.
- **Card inner column gap:** 16 px between Prompt / Code / Actions
  columns.
- **Vertical rhythm inside the actions column:** 8 px between buttons.
- **Card corner radius:** 8 px (overlay-class surface).
- **Inline-error / build-output box corner radius:** 4 px (control-class).

Use `FlexColumn` / `FlexRow` (CSS Flexbox semantics) as the default for
linear layout. Reserve `Grid` for the card's three-column structure where
the prompt and code columns must respect explicit weights and the code
column needs `GridSize.Star()` for `TextTrimming` to fire on long single-
line outputs.

```csharp
// Card: Grid because we need column weighting + trimmable code
Grid(
    columns: [GridSize.Pixels(280), GridSize.Star(), GridSize.Pixels(140)],
    rows:    [GridSize.Auto, GridSize.Auto],
    SubHeading($"Step {n} — {title}").Grid(row: 0, columnSpan: 3),
    PromptColumn().Grid(row: 1, column: 0),
    CodeColumn().Grid(row: 1, column: 1),
    ActionsColumn().Grid(row: 1, column: 2))
```

Use `MinHeight` not fixed `Height` on the prompt and code text areas —
text scaling at 200%+ must not clip.

### Streaming Animation

Streaming-token updates into the code viewer must remain smooth at any
display scale and not cause layout thrash. The viewer is a `TextView` with
`IsAppendOnly = true` (or equivalent) so the reconciler appends rather
than re-flowing the whole control on each token.

Cards animate in on first generation using:

```csharp
Border(card).WithTransitions(Transition.Fade, Transition.Slide(Edge.Bottom))
```

No gradient animation, no parallax — both fail HC and add nothing to a
demo authoring tool.

### Card Surface

The step card is the primary visual unit. Build it as a single bordered
surface with shadow lift on hover:

```csharp
Border(cardContent)
    .Background(Theme.CardBackground)
    .WithBorder(Theme.CardStroke, 1)
    .CornerRadius(8)
    .Padding(16)
    .Translation(0, 0, isHovered ? 32 : 0)
    .Set(b =>
    {
        b.BackgroundSizing = BackgroundSizing.InnerBorderEdge;
        b.Shadow = new ThemeShadow();
    })
    .ScaleTransition()                 // smooth elevation lift
    .AutomationName($"Step {n} — {title}")
```

The card is interactive (clicking focuses the prompt) so it gets a
`Cursor = Hand` affordance and a HC-mode `SystemColorHighlightColor`
border.

### Buttons

| Button | Style | Notes |
|---|---|---|
| Open Folder | Standard (default style) | In TitleBar `Content` |
| Generate All | Primary (accent override via `.Resources`) | Disabled while generating; shows cancel affordance |
| Export Speaker Notes | Standard | Disabled until at least one delta exists |
| ▶ Run | Standard with leading symbol icon | Disabled if no `step-NN.cs` on disk |
| 📋 Copy Delta | Subtle (per `.Resources` override pattern) | Disabled until delta exists |

Buttons use `MinHeight(40)` only when needed for touch — do not set a
fixed `Height(32)`, which clips at large text scales.

### Accessibility

- Every icon-only or symbol-prefixed button has `.AutomationName(...)`:
  - `▶ Run` → `AutomationName($"Run step {n}")`
  - `📋 Copy Delta` → `AutomationName($"Copy speaker notes for step {n}")`
- Heading hierarchy: the `TitleBar` title is L1; each card's
  `SubHeading($"Step n — title")` is L2. Don't skip levels.
- The steps panel is wrapped in `.Landmark(AutomationLandmarkType.Main)`;
  the title-bar action group uses `AutomationLandmarkType.Navigation`.
- The Demo Prompt panel and each step's prompt field use `FormField` so
  the label, required state, and error messages are wired into AT
  automatically.
- Generation progress ("Generating step 2 of 5...") is announced via
  `UseAnnounce` (polite) so screen-reader users follow the pipeline.
  Place `announce.Region` once near the root.
- Build failure (max retries) is announced **assertive**.
- Each step card sets `.PositionInSet(n, total)` for screen-reader
  navigation through the list.
- Hit-test targets for the per-card hover surface use
  `Background("#00000000")` (visible-to-AT, transparent visually).
- Dividers between cards use `DividerStrokeColorDefaultBrush` — never a
  custom brush with opacity (breaks HC).
- The three Roslyn analyzers (`REACTOR_A11Y_001..003`) are promoted to
  errors in the sample's `.csproj`:
  ```xml
  <WarningsAsErrors>REACTOR_A11Y_001;REACTOR_A11Y_002;REACTOR_A11Y_003</WarningsAsErrors>
  ```

### Reconciliation

- Every step card uses `.WithKey($"step-{n}")` so the reconciler keeps
  identity stable when steps are added/removed by external edits to
  `demo-script.md`.
- The token stream into the code viewer is bound via
  `IObservableStream<string>` so reconciliation on each token is O(1) per
  card, not O(N) over all cards.

### Test Matrix

The sample's selftest fixtures must mount and screenshot in:

- Light / Dark / NightSky (HC) themes
- 100 / 150 / 200 / 250 % display scaling
- Maximum text scaling (Settings > Accessibility > Text size = max)
- An empty demo (no steps) and a 10-step demo (scrolled state)

---

## Generation Pipeline

1. User clicks **Generate All**.
2. Tool reads `demo-script.md` and parses the Demo Prompt and all step
   prompts.
3. Tool constructs the API request:
   - **System prompt**: Layer 1 (tool system prompt)
   - **User message**: Layer 2 (Demo Prompt) + all step prompts in
     sequence, requesting all steps be generated together for narrative
     coherence
4. Tool streams the response from GitHub Models (Claude `claude-opus-4-6`).
5. As each step's output arrives, the UI updates that step's card live.
6. On completion, generated code files are written to disk
   (`step-NN.cs` or `step-NN/`).
7. `demo-script.md` is **not** modified by generation — only code files
   are written.

### Cancellation

If the user cancels mid-generation, the pipeline stops at the current
step. Any steps already generated and written to disk are left as-is —
the tool makes no attempt to clean up partial state. The step cards
reflect whatever was completed before cancellation. Users who want a
clean slate should re-run **Generate All** or use version control.

### Regeneration is destructive

Re-running **Generate All** overwrites all existing generated files
without confirmation. This is by design. Users who want to preserve
prior generations should use version control (git).

---

## Play (Run) Behavior

Clicking **▶ Run** on a step:

1. Identifies the file(s) for that step (`step-NN.cs` or `step-NN/`).
2. Shells out to `dotnet run` in the project root (single-file) or the
   step subdirectory (multi-file).
3. The demo apps are GUI applications — `dotnet run` launches the GUI
   directly; no terminal window is needed or opened.
4. Build output (stdout/stderr from the build phase) is captured in-app
   and surfaced only if the build fails (see
   [Build-and-Fix Pipeline](#build-and-fix-pipeline)).

The **▶ Run** button is disabled if no generated file exists for that
step, and shows a spinner while a previously launched run is still
spawning.

---

## Build-and-Fix Pipeline

After each step's code is generated and written to disk, the tool
automatically attempts a build before moving on. If the build fails, the
tool enters an AI-driven fix loop rather than surfacing the error to the
user.

### Build loop (per step, post-generation)

1. Run `dotnet build` on the step's file(s) and capture stdout/stderr.
2. If build succeeds, continue to the next step.
3. If build fails:
   - Send the compiler error output back to the AI with the original
     generated code and a fix instruction.
   - Stream the corrected code into the step's card UI (same streaming
     animation as initial generation).
   - Write the corrected file(s) to disk and attempt the build again.
   - Repeat up to **3 fix attempts** per step.
4. If the build still fails after 3 attempts, mark the step card with an
   error state and display the final compiler output inline in the card so
   the user can inspect it.
5. Generation continues to subsequent steps regardless — a failed step
   does not halt the pipeline.

### Build state indicators (per step card)

| State | Indicator |
|---|---|
| Not yet built | No indicator |
| Build in progress | Spinner on the card |
| Build succeeded | Subtle green checkmark |
| Build failed (fix in progress) | Amber indicator, "Fixing..." label |
| Build failed (max retries exceeded) | Red indicator, error output shown inline |

### Build invocation

- **Single-file mode**: `dotnet run --project <step-NN.cs>` (or, if file-
  based apps are unavailable on the target SDK, the tool wraps each
  `step-NN.cs` in a transient `obj/demo/step-NN/` SDK-style project for
  the build phase). Working directory: project root.
- **Multi-file mode**: `dotnet build` then `dotnet run --no-build` in
  `step-NN/`. Working directory: the step subdirectory.

The exact invocation is encapsulated in a `DotnetRunner` service so the
UI layer never shells out directly.

---

## Speaker Notes Export

Clicking **Export Speaker Notes**:

1. Collects the presenter delta for each step as generated by the AI.
2. Writes a single `speaker-notes.txt` file to the project root.
3. Confirms export with a toast notification.

### Speaker notes format

```
DEMO SCRIPT — [Demo Title]
Generated: [timestamp]

─────────────────────────────────────
STEP 1 — Hello World baseline
─────────────────────────────────────

Add the following NuGet reference at the top of Program.cs:

    #r "nuget: Spectre.Console, 0.49.1"

Then replace the body of the file with:

    using Spectre.Console;
    AnsiConsole.MarkupLine("[bold green]Hello, demo![/]");

─────────────────────────────────────
STEP 2 — Add a message list
─────────────────────────────────────

After the greeting line, add the following table definition...
```

Speaker notes are also accessible per-step via **📋 Copy Delta**, which
copies that step's notes to the clipboard without writing a file.

---

## AI Backend

| Setting | Value |
|---|---|
| Provider | GitHub Models |
| SDK | GitHub Models AI SDK (.NET) |
| Default model | `claude-opus-4-6` (Claude Opus 4.6 via GitHub Models) |
| Model configurability | Hardcoded for v1 |
| Streaming | Yes — token-by-token streaming to the UI |
| Auth | GitHub token via interactive `gh auth login` console session |

### GitHub auth flow

On first launch (or on any API call that returns an auth error), the
tool spawns an interactive `gh auth login` session in a console window
to complete GitHub authentication. If an API call fails with an
authentication error after the user appears to be logged in, the tool
triggers the login flow a second time before surfacing an error to the
user. This handles token expiry without requiring the user to manually
intervene.

---

## Reactor Integration Notes

This sample is also a Reactor exemplar. Implementation should follow the
patterns in the specs below; deviations require justification in the PR.

### Streaming into card collections

Each step's code viewer binds to an `IObservableStream<string>` exposed
by the `StepCardModel`. Token deltas from the GitHub Models SDK are
appended via the model's `AppendToken(text)` API, which dispatches a
single batched re-render on the next frame (see spec
009 — State and Components). The plain-text viewer is a thin wrapper
over Reactor's `TextView` component, configured with `IsReadOnly = true`
and `FontFamily = MonospaceCodingFont`.

### Filesystem watcher

`FileSystemWatcher` events are marshaled onto the UI dispatcher through
`UseEffect` cleanup (spec 020 — Async Resources). External edits to
`demo-script.md` win over in-flight debounced edits — the in-flight
buffer is discarded and the UI reloads from disk. This matches the
existing convention in `samples/apps/regedit-winui/` for live registry
keys.

### Commanding

All header buttons (**Open Folder**, **Generate All**, **Export Speaker
Notes**) and per-card actions (**▶ Run**, **📋 Copy Delta**) are
`ICommand`s registered via Reactor's commanding system (spec 012). This
gives them automatic enable/disable wiring against background-task state
and free keyboard-shortcut binding (e.g., Ctrl+G for Generate All).

### Async resources

The generation pipeline, build loop, and `dotnet run` invocation are
each modeled as `AsyncResource<T>` instances (spec 020) with
cancellation tokens tied to the lifetime of the owning step card. This
ensures that closing the folder or quitting the app cleanly cancels any
in-flight work.

### Markdown parser

`Reactor.Documents.Markdown` (the SAX-style parser referenced in spec
013) handles `demo-script.md` parsing. Round-tripping unknown sections
verbatim is required — the parser exposes a `RawSpan` on each event
that the writer concatenates for sections it does not own.

---

## Project Layout

```
samples/apps/demo-script-tool/
  App/
    Program.cs                       ← ReactorApp.Run<DemoScriptShell>(...)
    DemoScriptShell.cs               ← top-level component (header + panels)
    Components/
      DemoPromptPanel.cs
      StepsPanel.cs
      StepCard.cs
      HeaderBar.cs
    Services/
      DemoScriptStore.cs             ← read/write demo-script.md
      DemoScriptParser.cs            ← thin wrapper over Reactor.Documents.Markdown
      GenerationPipeline.cs          ← drives Layer1 + Layer2 → streamed steps
      DotnetRunner.cs                ← dotnet build / dotnet run shell-out
      GithubModelsClient.cs          ← GitHub Models AI SDK wrapper
      GhAuth.cs                      ← gh auth login spawner + retry
      SpeakerNotesExporter.cs
    Models/
      DemoScriptModel.cs             ← title + demo prompt + List<StepModel>
      StepModel.cs                   ← prompt, code stream, delta, build state
    Resources/
      SystemPrompt.txt               ← Layer 1 prompt (embedded resource)
    DemoScriptTool.csproj
  README.md
  demo-projects/                     ← seeded sample demos for the README walkthrough
    spectre-chat/
      demo-script.md
    aspire-hello/
      demo-script.md
```

The `.csproj` references `Reactor.WinUI`, `Reactor.Documents.Markdown`,
and the GitHub Models AI SDK NuGet package. It is added to
`reactor2.sln` under the `samples/apps` solution folder, matching the
other apps in that folder.

---

## Filesystem Watcher

The watcher policy is intentionally simple:

- **External change wins.** If `demo-script.md` is modified on disk while
  the user has unsaved debounced edits, the in-flight buffer is discarded
  and the UI reloads from the new disk contents. A toast notifies the
  user that external changes were picked up.
- The watcher also detects deletes (the file being removed out from under
  the app) and falls back to an empty/scaffolded state.
- Generated `step-NN.cs` files are **not** watched — the tool is the
  authoritative writer. Manual edits to those files between runs are
  silently overwritten on the next regeneration.

---

## Error Surfacing

| Situation | UX |
|---|---|
| `gh auth login` fails | Inline banner; pipeline halts |
| Network/API error mid-generation | Step card shows red border, error text inline; pipeline continues to next step |
| Build fails after 3 fix attempts | Red indicator + compiler output inline in card |
| `dotnet run` exits non-zero | Toast with "Run failed (exit code N) — see output", stderr captured into a per-step run log under `obj/demo/step-NN.runlog` |
| `demo-script.md` malformed | Inline banner above the steps panel describing the parse error and pointing at the offending line; the steps panel is hidden until the file parses cleanly |

No modal dialogs anywhere — the app is keyboard-driven and demo-friendly.

---

## Packaging & Distribution

The app must support **two run modes** and a one-command path to a
shareable installer.

### Run modes

| Mode | When | Built by |
|---|---|---|
| **Unpackaged** | Inner-loop / debugging — `F5` from VS / VS Code, `dotnet run` | `WindowsPackageType=None` (default `.csproj` from spec 031 / `SKILL.md`) |
| **Packaged (MSIX)** | Sharing with others, demoing on clean machines, validating Mica + capabilities | `WindowsPackageType=MSIX` via the `Package/` project below |

Both modes share the same `App` assembly and component code — only the
`.csproj` head changes. There must be no `#if PACKAGED` divergence in
component code; runtime mode is detected at startup if needed via
`Windows.ApplicationModel.Package.Current` (wrapped in a try/catch since
unpackaged throws).

### Project layout addition

```
samples/apps/demo-script-tool/
  App/
    DemoScriptTool.csproj        ← unpackaged head (debug default)
  Package/
    DemoScriptTool.Package.wapproj  ← MSIX packaging project
    Package.appxmanifest
    Images/
      StoreLogo.png   Square44x44Logo.png   Square150x150Logo.png
      Wide310x150Logo.png   SplashScreen.png
  build/
    sign-cert.ps1                ← creates / installs the self-signed cert
    package.ps1                  ← one-shot: build → MSIX → sign → output
    install.ps1                  ← installs the cert + MSIX on a target box
```

`F5` on `App/DemoScriptTool.csproj` launches the unpackaged app for
debugging. `F5` on `Package/DemoScriptTool.Package.wapproj` launches the
packaged app under the MSIX deployment.

### `Package.appxmanifest`

- **Identity / PFN** — `andersonch.DemoScriptTool` (per-user; not Store-
  bound for v1).
- **Publisher** — `CN=DemoScriptTool Dev` (matches the self-signed cert
  subject below). Update before publishing externally.
- **MinVersion / MaxVersionTested** — Windows 11 22621.
- **Visual assets** — populated from `Package/Images/` so the Start menu
  tile, taskbar icon, and splash screen render correctly.
- **Capabilities** — `runFullTrust` (we shell out to `dotnet`), no
  broadFileSystemAccess (we use the folder picker instead).

### Self-signed certificate (developer signing)

Sharing an MSIX outside the store requires it to be signed with a cert
the target machine trusts. For sample sharing we use a **self-signed
cert**; recipients install the cert into `LocalMachine\TrustedPeople`
once.

`build/sign-cert.ps1`:

1. If `build/DemoScriptTool.pfx` already exists, exit (idempotent).
2. Generate a self-signed code-signing cert:
   ```powershell
   $cert = New-SelfSignedCertificate `
       -Type CodeSigningCert `
       -Subject "CN=DemoScriptTool Dev" `
       -KeyUsage DigitalSignature `
       -FriendlyName "DemoScriptTool Dev Signing" `
       -CertStoreLocation "Cert:\CurrentUser\My" `
       -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
   ```
3. Export to `build/DemoScriptTool.pfx` with a password read from
   `$env:DEMOSCRIPT_PFX_PASSWORD` (or a script-prompted secure string if
   unset). Also export the public `.cer` to `build/DemoScriptTool.cer`
   for recipient install.
4. Print the thumbprint so the wapproj can pick it up.

**The `.pfx` is gitignored.** Only `build/DemoScriptTool.cer` (the
public cert) is checked in, so recipients can install it without
needing the signing key.

### `build/package.ps1` — one-command MSIX

```powershell
# 1. Ensure cert exists
.\build\sign-cert.ps1

# 2. Build + pack
dotnet publish .\App\DemoScriptTool.csproj -c Release -r win-x64 --self-contained
msbuild .\Package\DemoScriptTool.Package.wapproj `
    /p:Configuration=Release `
    /p:Platform=x64 `
    /p:AppxPackageDir=..\out\ `
    /p:AppxBundlePlatforms=x64 `
    /p:UapAppxPackageBuildMode=SideloadOnly `
    /p:AppxPackageSigningEnabled=true `
    /p:PackageCertificateKeyFile=..\build\DemoScriptTool.pfx `
    /p:PackageCertificatePassword=$env:DEMOSCRIPT_PFX_PASSWORD

# 3. Surface the resulting .msix path
Get-ChildItem .\out\*.msix | Select-Object -ExpandProperty FullName
```

Output: `out/DemoScriptTool_<version>_x64.msix`.

ARM64 is supported via `--Platform=ARM64` and a second pack pass; the
`package.ps1` script accepts `-Arch x64,arm64` and produces both.

### `build/install.ps1` — recipient one-liner

```powershell
# 1. Trust the publisher
Import-Certificate -FilePath .\DemoScriptTool.cer `
    -CertStoreLocation Cert:\LocalMachine\TrustedPeople

# 2. Install
Add-AppxPackage .\DemoScriptTool_<version>_x64.msix
```

The README ships these two commands together with a note that the cert
import requires elevation. After install, "Demo Script Tool" appears in
Start.

### CI and PR validation

A GitHub Actions job (`.github/workflows/sample-demo-script-tool.yml`)
on PRs that touch `samples/apps/demo-script-tool/**`:

1. Builds the unpackaged head (`dotnet build App/DemoScriptTool.csproj`).
2. Runs the selftest fixtures.
3. Generates an ephemeral CI cert (not the dev cert), runs
   `build/package.ps1`, and uploads the resulting `.msix` as a build
   artifact for manual smoke-testing on a clean VM.

### Documented in the README

`samples/apps/demo-script-tool/README.md` includes three sections in
this order:

1. **Run from source** — `dotnet run` against the unpackaged head, the
   default for development.
2. **Build a shareable MSIX** — `pwsh build/package.ps1` end-to-end,
   first-run cert prompt explained.
3. **Install on another machine** — `Import-Certificate` + `Add-AppxPackage`
   from the recipient's perspective, including the elevation requirement.

---

## Open Questions

These were carried over from the source product spec; the implementing
agent should resolve them during design review.

1. **Reactor streaming pattern.** Confirm the recommended idiom for
   binding a stream of tokens into a plain-text viewer in a card
   collection. Candidate: `IObservableStream<string>` + `TextView`
   with `IsAppendOnly = true`.
2. **Reactor Markdown parser API.** Confirm the SAX-style event surface
   covers numbered lists with rich-text bodies (the Steps section).
3. **Filesystem watcher / edit conflict.** Recommended default: external
   file change wins and reloads the UI, discarding in-flight edits.
   Confirm or propose alternative.
4. **Build invocation.** Confirm the exact `dotnet` CLI invocation for
   building without running for both single-file and multi-file modes,
   and the working-directory expectations.
5. **GitHub Models SDK packaging.** Confirm the SDK is redistributable
   via NuGet under the license terms used elsewhere in `samples/apps/`.

---

## Out of Scope (v1)

- Per-step regeneration (only full regeneration supported)
- In-app diff viewer between steps
- Settings screen or model selection UI
- In-app terminal / output capture beyond build error surfacing
- Step reordering via drag-and-drop
- Collaborative editing or cloud sync
- Syntax highlighting in the code viewer
- Authoring multiple demos within one window (one folder = one demo)

---

## Source

Adapted from the upstream product spec at
`C:\Users\andersonch\Downloads\demo-script-tool-spec.md` (received
2026-05-02). This spec restates that document's requirements, fits them
to the Reactor sample-app conventions, and adds Reactor-specific
implementation guidance under
[Reactor Integration Notes](#reactor-integration-notes) and
[Project Layout](#project-layout).
