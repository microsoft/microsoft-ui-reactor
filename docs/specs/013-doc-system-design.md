# Reactor Documentation System — Design Spec

## Problem

Microsoft.UI.Reactor's documentation contains code examples and screenshots that go stale as the framework evolves. When APIs change, docs show code that no longer compiles and screenshots that no longer match reality. Keeping them in sync manually is unsustainable.

## Goal

A "compilable documentation" system where:

1. **Every code snippet** in the docs is extracted from a real, building, running app
2. **Every screenshot** is captured from a live Reactor app window
3. **Prose** is AI-authored by default, steered by a `goal` in each template
4. Running `mur docs compile` produces final documentation with everything up to date
5. CI can enforce that docs stay in sync with the framework

---

## Architecture Overview

```
docs/
  _pipeline/
    apps/                        ← Doc sample apps (source of truth)
      getting-started/
        App.cs                   ← Minimal Reactor app with snippet markers
        doc-manifest.yaml        ← Screenshot regions, window size, delays
        getting-started.csproj
      commanding/
        App.cs
        doc-manifest.yaml
        commanding.csproj
    templates/                   ← Doc templates (human + AI authored)
      getting-started.md.dt      ← Markdown with snippet/screenshot directives
      commanding.md.dt
  guide/                         ← Compiled docs (generated, not hand-edited)
    getting-started.md
    commanding.md
    images/
      getting-started/
        hero.png
        state-example.png
      commanding/
        standard-commands.png
```

---

## 1. Doc Apps

Each documentation topic has a dedicated app in `docs/_pipeline/apps/`. These are minimal Reactor apps whose *only purpose* is to provide working code examples and screenshot targets.

### Structure

```
docs/_pipeline/apps/{topic}/
  App.cs                   ← Single-file Reactor app
  doc-manifest.yaml        ← Metadata for doc compilation
  {topic}.csproj           ← Standard Reactor project file
```

### Snippet Markers

Code that should be extracted into docs is bracketed with comment markers:

```csharp
// <snippet:creating-a-button>
var button = Button("Click me", () => count++);
// </snippet:creating-a-button>

// <snippet:using-state>
var (count, setCount) = UseState(0);
var (name, setName) = UseState("World");
// </snippet:using-state>
```

Rules:
- Snippet IDs are globally unique across all doc apps (scoped by `{topic}/{id}`)
- Snippets can be nested (outer snippet includes inner snippet code)
- Snippet markers are stripped from extracted code
- Leading common indentation is trimmed in the extracted output

### App Manifest (`doc-manifest.yaml`)

```yaml
app:
  title: "Getting Started"
  width: 800
  height: 600
  theme: light              # light | dark | both (captures both themes)
  startup-delay: 2000       # ms to wait after launch before first capture

screenshots:
  - id: hero
    description: "Full app window"
    region: full             # full | client | custom
    format: png

  - id: state-example
    description: "Counter after clicking"
    region: custom
    bounds: { x: 20, y: 100, width: 400, height: 200 }
    format: png
    setup:                   # Optional: actions to perform before capture
      - click: "Click me"   # Future: simple automation DSL
      - wait: 500

  - id: dark-mode-hero
    description: "Full app in dark theme"
    region: full
    theme: dark
    format: png

snippets:                    # Optional: override snippet extraction settings
  trim-namespace-usings: true
  max-lines-warning: 30     # Warn if a snippet exceeds this length
```

### App as Test

Every doc app is implicitly a test. The compile pipeline:
1. Builds the app (`dotnet build`)
2. Launches it (`dotnet run`)
3. Waits for the startup delay
4. Verifies the window appeared and is responsive
5. Captures screenshots
6. Shuts down the app

If any step fails, the doc compile fails — guaranteeing that every snippet comes from working code.

---

## 2. Doc Templates

Templates live in `docs/_pipeline/templates/` with a `.md.dt` extension ("doc template"). They are Markdown files with directives that reference snippets and screenshots.

### Snippet Directive

Injects a code block extracted from a doc app:

~~~markdown
```csharp snippet="getting-started/creating-a-button"
```
~~~

The compiler replaces this with the extracted code:

~~~markdown
```csharp
var button = Button("Click me", () => count++);
```
~~~

**Options:**

~~~markdown
```csharp snippet="getting-started/using-state" title="Managing state with UseState"
```
~~~

Adds a comment header above the code block in the compiled output.

### Screenshot Directive

Injects a screenshot captured from a running doc app:

```markdown
![Getting started hero](screenshot://getting-started/hero)
```

The compiler replaces the `screenshot://` URL with a relative path to the generated image:

```markdown
![Getting started hero](images/getting-started/hero.png)
```

### AI-First Authoring Model

The AI writes the **entire document** by default. The template is a prompt — not a partially-authored draft. The AI is given the full set of available snippets, screenshots, and framework knowledge, and produces the complete doc.

The only marker is `ai:lock` for content that humans want to control directly:

```markdown
<!-- ai:lock -->
> **Warning:** The commanding API is experimental and may change before v1.
<!-- /ai:lock -->
```

- Everything **outside** `ai:lock` markers is AI-generated and can be regenerated at any time
- `ai:lock` sections are preserved verbatim through every regeneration
- The AI is taught the snippet and screenshot directive syntax and uses them in its output
- The AI can recommend new snippets/apps when existing ones don't cover a topic

### Template Metadata

Each template starts with a YAML front-matter block that serves as the AI's instructions:

```markdown
---
title: "Getting Started with Reactor"
app: getting-started
order: 1
audience: beginner
goal: |
  Introduce Reactor to a new developer. Show them how to create their first app
  with a text box and reactive state. End with next steps toward components.
---
```

The `goal` field is the core instruction to the AI. It describes what the document should accomplish, what it should cover, and the narrative arc. The AI uses this — along with available snippets and screenshots — to write the entire document.

---

## 3. Compile Pipeline

Invoked via:

```bash
duct docs compile [--topic <name>] [--no-ai] [--no-screenshots] [--validate-only]
```

### Phases

#### Phase 1: Validate
- Parse all templates, extract all snippet and screenshot references
- Scan all doc app source files for snippet markers
- Report errors: missing snippets, duplicate IDs, orphaned snippets (defined but never referenced)
- Report warnings: snippets exceeding `max-lines-warning`

#### Phase 2: Build
- `dotnet build` all doc apps
- Fail fast on any build error (this means a code snippet is broken)

#### Phase 3: Capture
- Launch each doc app that has screenshot definitions
- Use the existing `PreviewCaptureServer` infrastructure (Win32 `PrintWindow`)
- Wait for startup delay, execute any setup actions
- Capture screenshots per manifest regions
- Save to `docs/guide/images/{topic}/{id}.{format}`
- Shut down apps

#### Phase 4: Extract
- Parse snippet markers from all doc app source files
- Trim markers and normalize indentation
- Build a snippet registry: `{topic}/{id}` → extracted code

#### Phase 5: AI Author
- For each `.md.dt` template:
  - Build the AI prompt (see §5) with front-matter goal, available snippets, screenshot references
  - AI generates the full document, using snippet and screenshot directives
  - `ai:lock` sections from any previous output are preserved verbatim
- Write compiled `.md` files to `docs/guide/`

#### Phase 6: Assemble
- Process the AI-generated output:
  - Replace `snippet=` directives with extracted code
  - Replace `screenshot://` URLs with relative image paths
- Write final `.md` files to `docs/guide/`

### Pipeline Flags

| Flag | Description |
|---|---|
| `--topic <name>` | Compile only a specific topic |
| `--no-screenshots` | Skip screenshot capture (use existing images) |
| `--no-ai` | Skip AI authoring (use previously generated output) |
| `--validate-only` | Check references without building or capturing |
| `--ci` | Strict mode: fail on warnings, verify output matches committed docs |

---

## 4. Screenshot Capture

### Leveraging `PreviewCaptureServer`

Reactor already has `PreviewCaptureServer` which captures WinUI window frames via Win32 `PrintWindow` and serves JPEG over HTTP. The doc system extends this:

1. **Launch app** with a `--doc-capture` flag that enables the capture server
2. **Wait** for startup delay
3. **HTTP GET** `/frame` to capture the current frame
4. **Crop** to the region specified in the manifest
5. **Save** as PNG (higher quality than JPEG for docs)

### Region Types

| Region | Behavior |
|---|---|
| `full` | Entire window including title bar |
| `client` | Client area only (no chrome) — default from `PreviewCaptureServer` |
| `custom` | Pixel bounds `{x, y, width, height}` relative to client area |

### Future: Setup Actions

The manifest's `setup` array allows simple pre-capture automation:

```yaml
setup:
  - wait: 1000
  - click: "Button Text"     # Find and click a button by content
  - type: "Hello World"      # Type text into the focused element
  - wait: 500
```

This is aspirational — v1 can simply capture the initial state.

---

## 5. AI Integration

### AI-First Model

The AI is the primary author of all documentation. Templates provide goals and constraints; the AI produces complete, polished documents. Humans intervene only via `ai:lock` sections and by editing the template's `goal` field.

### The AI Prompt

Each document is generated from a structured prompt that teaches the AI how to use the doc system:

```
You are writing developer documentation for Reactor, a WinUI 3 framework
for building native Windows apps with a React-like declarative component model.

## Document goal
Title: {title from front-matter}
Audience: {audience from front-matter}
Goal: {goal from front-matter}

## How to reference code snippets

You have access to working code snippets extracted from real sample apps.
To include a snippet, use this directive on its own line:

  ```csharp snippet="{topic}/{snippet-id}"
  ```

The build system will replace this with the actual code. Available snippets:

{for each snippet: id, source file, and the actual extracted code}

## How to reference screenshots

To include a screenshot from a running app, use this image syntax:

  ![description](screenshot://{topic}/{screenshot-id})

The build system will replace the URL with the generated image. Available screenshots:

{for each screenshot: id, description from manifest}

## Writing new sample apps

If the available snippets don't cover what you need to explain, you can
recommend a new snippet by writing:

  <!-- doc:needs-snippet id="{suggested-id}" description="{what it should demonstrate}" -->

The doc compile pipeline will report these as action items. A developer
(or you in a subsequent pass) can then create the sample app and snippet
to fill the gap.

## Locked sections

The following sections are human-authored and must be included exactly
as shown, at the appropriate location in the document:

{for each ai:lock section from previous output: the verbatim content}

## Style guidelines
- Write concise, practical prose. Developers skim — use short paragraphs.
- Lead with what the code does, not what it is.
- Use the snippets to teach by example. Don't describe code the reader can see.
- After each snippet, briefly explain the "why" or call out non-obvious details.
- Use ## and ### headings to create scannable structure.
- End with a "Next steps" section linking to related topics when applicable.
```

### AI Capabilities

The AI can:
- **Structure the document** — choose headings, ordering, and narrative flow
- **Select which snippets to include** and in what order
- **Write all prose** around the snippets and screenshots
- **Flag missing content** via `<!-- doc:needs-snippet -->` directives
- **Recommend new sample apps** when existing ones don't cover a topic

The AI cannot:
- Modify `ai:lock` sections
- Invent code (all code must come from real snippets)
- Remove or alter snippet/screenshot directives in locked sections

### Workflow

1. **Developer creates a template** (`.md.dt`) with front-matter: title, audience, goal
2. **Developer creates a doc app** with snippet markers and a manifest
3. **`duct docs compile`** builds apps, captures screenshots, runs AI, assembles output
4. **Developer reviews output** — edits `goal` to steer AI, adds `ai:lock` for critical content
5. **On framework changes**: re-run compile. Apps are rebuilt, screenshots refreshed, AI rewrites prose around the updated snippets.
6. **AI flags gaps**: `<!-- doc:needs-snippet -->` directives appear in output when the AI needs code that doesn't exist yet. Developer creates the snippet, re-compiles.

### `doc:needs-snippet` Directive

When the AI determines it needs a code example that doesn't exist:

```markdown
<!-- doc:needs-snippet id="getting-started/use-effect-timer" description="UseEffect hook with a timer that increments a counter every second" -->
```

The compile pipeline collects these and reports them:

```
⚠ Missing snippets requested by AI:
  - getting-started/use-effect-timer: "UseEffect hook with a timer that increments a counter every second"
```

A developer (or the AI in a subsequent agent pass) can then create the doc app code to fill the gap, and the next compile will pick it up.

### Keeping AI in Bounds

- `ai:lock` sections are always preserved verbatim, in position
- All code blocks must reference real snippets (the AI cannot fabricate code)
- The `goal` field in front-matter is the primary steering mechanism
- If the AI's output is wrong, fix the `goal` — don't hand-edit the output (it will be overwritten)

---

## 6. CI Integration

### PR Validation

Add a CI step that runs:

```bash
duct docs compile --ci --no-screenshots
```

This validates:
- All doc apps build successfully (snippets are valid code)
- All snippet references resolve (no stale `snippet=` directives)
- All screenshot references have matching manifest entries

Screenshots are excluded from CI validation because they require a graphical environment. Screenshot freshness is enforced by a periodic scheduled build or a manual `compile` run.

### Full Compile (Scheduled / Manual)

A scheduled pipeline or manual trigger runs the full compile with screenshots:

```bash
duct docs compile --ci
```

This captures fresh screenshots and verifies that the committed `docs/guide/` matches the generated output. If they differ, the build fails — signaling that docs need to be recompiled.

---

## 7. File Format Summary

| Path | Format | Purpose |
|---|---|---|
| `docs/_pipeline/apps/{topic}/App.cs` | C# with `// <snippet:id>` markers | Source of truth for code examples |
| `docs/_pipeline/apps/{topic}/doc-manifest.yaml` | YAML | Screenshot regions, app config |
| `docs/_pipeline/templates/{topic}.md.dt` | YAML front-matter + optional `ai:lock` sections | AI instructions (human-edited) |
| `docs/guide/{topic}.md` | Markdown | AI-generated compiled docs (generated) |
| `docs/guide/images/{topic}/{id}.png` | PNG | Captured screenshots (generated) |

---

## 8. Example: End-to-End

### Step 1: Doc App (`docs/_pipeline/apps/getting-started/App.cs`)

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<GettingStartedApp>("Getting Started", width: 600, height: 400);

class GettingStartedApp : Component
{
    public override Element Render()
    {
        // <snippet:hello-world>
        var (name, setName) = UseState("World");

        return VStack(
            Text($"Hello, {name}!").FontSize(24).Bold(),
            TextBox(name, setName).Width(200)
        );
        // </snippet:hello-world>
    }
}
```

### Step 2: Manifest (`docs/_pipeline/apps/getting-started/doc-manifest.yaml`)

```yaml
app:
  title: "Getting Started"
  width: 600
  height: 400
  startup-delay: 1500

screenshots:
  - id: hello-world
    description: "Hello World app running"
    region: client
    format: png
```

### Step 3: Template (`docs/_pipeline/templates/getting-started.md.dt`)

~~~markdown
---
title: "Getting Started with Reactor"
app: getting-started
order: 1
audience: beginner
goal: |
  Introduce Reactor to a new developer. Show them how to create their first app
  using ReactorApp.Run, UseState, and basic elements (Text, TextBox, VStack).
  Use the hello-world snippet and screenshot. End with next steps.
---

<!-- ai:lock -->
> **Prerequisites:** .NET 10+ and the Windows App SDK. See the [setup guide](setup.md).
<!-- /ai:lock -->
~~~

Note: The template is minimal — just front-matter with a `goal` and any locked sections. The AI writes everything else.

### Step 4: Compiled Output (`docs/guide/getting-started.md`)

The AI generates the full document, using available snippets and screenshots:

~~~markdown
# Getting Started with Reactor

> **Prerequisites:** .NET 10+ and the Windows App SDK. See the [setup guide](setup.md).

Reactor is a declarative UI framework for building native Windows apps.
No XAML, no data binding — you describe your UI in plain C# and Reactor
keeps the screen in sync.

## Your First App

Create a new file called `App.cs` and add this:

```csharp
var (name, setName) = UseState("World");

return VStack(
    Text($"Hello, {name}!").FontSize(24).Bold(),
    TextBox(name, setName).Width(200)
);
```

Run it and you'll see this:

![Hello World app](images/getting-started/hello-world.png)

Type in the text box and the greeting updates instantly. That's
`UseState` — it returns the current value and a setter. When you call
the setter, Reactor re-renders the component with the new state.

## Next Steps

- Learn about [components](components.md) to break your UI into reusable pieces
- Explore [hooks](hooks.md) like `UseEffect` and `UseMemo`
~~~

Note how the locked prerequisites block appears verbatim. The AI chose to use the `hello-world` snippet and screenshot, structured the doc with headings, and wrote all the prose.

---

## 9. Implementation Phases

### Phase 1: Core Infrastructure
- Snippet extraction tool (parse markers, build registry)
- Template parser (resolve `snippet=` and `screenshot://` directives)
- `duct docs compile --validate-only` and `--no-screenshots` modes
- First doc app + template as proof of concept

### Phase 2: Screenshot Capture
- Extend `PreviewCaptureServer` or build a standalone capture harness
- Support `full`, `client`, and `custom` region types
- Integrate into compile pipeline

### Phase 3: AI Authoring
- Prompt engineering for full-document generation
- Teach AI snippet/screenshot directive syntax in prompt
- `ai:lock` marker preservation
- `doc:needs-snippet` reporting for content gaps
- Feedback loop: adjust `goal` in front-matter to steer output

### Phase 4: CI & Polish
- CI validation pipeline
- Scheduled full-compile with screenshots
- Error reporting, diagnostics, and `--verbose` output
- Documentation for the documentation system itself

---

## 10. Design Decisions

1. **Snippet deduplication**: Cross-app references are supported. Any template can reference `snippet="other-topic/id"` from any doc app.

2. **Theme screenshots**: When `theme: both` is specified, the pipeline generates two images: `{id}-light.png` and `{id}-dark.png`. The AI can reference either or both.

3. **Automation DSL scope**: Start with simple `click` / `type` / `wait` actions for pre-capture setup. Extend with richer actions (scroll, hover, drag) only as needed.

4. **Output format**: Compiled output is Markdown. Checked into the repo for browsing.

5. **Versioning compiled output**: `docs/guide/` is committed to the repo. CI verifies that committed output matches a fresh compile.

6. **AI-created apps**: The compile pipeline supports an agent mode where the AI autonomously creates doc apps and snippets to fill `doc:needs-snippet` gaps. The developer reviews the generated apps before committing.

7. **Doc ordering / navigation**: The system generates a table of contents for larger documents from the `order` fields in front-matter.
