# Demo Script Tool

A WinUI 3 desktop sample app built on Reactor. Author a coding demo as a
single Markdown file (`demo-script.md`) and let the GitHub Copilot SDK
generate runnable .NET code for each step plus speaker notes you can paste
into your slides.

The app doubles as a Reactor exemplar covering streaming state into a card
collection, file-watcher integration with hooks, async command pipelines,
clipboard / folder-picker interop, and Windows 11 design.

Designed against [docs/specs/035-demo-script-tool-design.md](../../../docs/specs/035-demo-script-tool-design.md).

---

## 1. Run from source

```pwsh
dotnet run --project samples/apps/demo-script-tool/App/DemoScriptTool.csproj
```

To skip the Open Folder dialog and load a project on launch, pass the
folder path as a positional argument:

```pwsh
dotnet run --project samples/apps/demo-script-tool/App/DemoScriptTool.csproj -- C:\dev\my-demo
```

(Reactor's own `--devtools` flags pass straight through; the first
non-flag argument is treated as the folder path.)

### Authentication

The app uses the [`GitHub.Copilot.SDK`](https://www.nuget.org/packages/GitHub.Copilot.SDK)
NuGet package, which talks to a bundled Copilot CLI. That CLI piggybacks on
whatever account `gh auth` currently considers active, so:

1. Make sure you have the [GitHub CLI](https://cli.github.com/) installed and
   on `PATH`.
2. Sign in with a Copilot-enabled account: `gh auth login --web`.
3. Confirm with `gh auth status` — the active account needs to have GitHub
   Copilot Pro / Pro+ / Business / Enterprise so the SDK can issue Copilot
   chat completions on your behalf.

The default model is `claude-sonnet-4.5`; change it by passing a different
id to the `CopilotSdkClient` constructor in `DemoScriptShell`. Use the
**⚡ Devtools → Log available Copilot models…** menu item to see what model
ids your account can use.

If a generation call hits an authentication failure mid-stream, the pipeline
runs `gh auth login` (in a spawned console) and resets the cached Copilot
SDK client so the retry picks up the refreshed credentials.

## 2. Author a demo

Open any folder. The app expects a `demo-script.md` file at the root —
missing files scaffold to an empty model and are written on first save.

```markdown
# My Spectre.Console chat demo

## Demo Prompt

This demo shows how to build a real-time chat UI using .NET 10 top-level
statements and the Spectre.Console library. Single-file mode. Each step
should compile and run via `dotnet run`. Use NuGet inline references
where needed.

## Steps

1. **Hello World baseline**
   Start with a minimal top-level-statements app that prints a styled
   greeting using Spectre.Console.

2. **Add a message list**
   Render a static list of fake chat messages using Spectre.Console's
   table component.

3. **Simulate live updates**
   Add a loop that appends a new message every second using
   `AnsiConsole.Live`.
```

Sentinel phrases that flip multi-file mode: `multi-file`, `multi file mode`,
`multifile mode`. Without one of these, single-file mode is assumed.

Click **Generate All** (Ctrl+G). Each step's code streams into its card
token-by-token; the build-and-fix loop tries the build up to three times
before surfacing the compiler output inline.

Per-card actions: **▶ Run** launches the step via `dotnet run`; **📋 Copy
Delta** puts the presenter notes for that step on the clipboard.

## 3. Build a shareable MSIX

```pwsh
pwsh samples/apps/demo-script-tool/build/package.ps1
```

The script ensures a self-signed code-signing cert exists under
`build/DemoScriptTool.pfx` (created the first time, password from
`$env:DEMOSCRIPT_PFX_PASSWORD`), runs `dotnet publish`, and packs the MSIX
to `out/DemoScriptTool_<version>_<arch>.msix`. Pass `-Arch x64,arm64` for a
multi-arch build.

The public certificate (`build/DemoScriptTool.cer`) is checked in so
recipients can install the package without needing the signing key.

## 4. Install on another machine

```pwsh
# In an elevated PowerShell:
Import-Certificate -FilePath .\DemoScriptTool.cer `
    -CertStoreLocation Cert:\LocalMachine\TrustedPeople

Add-AppxPackage .\DemoScriptTool_<version>_<arch>.msix
```

The cert import requires elevation. After install, "Demo Script Tool"
appears in Start.

---

## Project layout

```
samples/apps/demo-script-tool/
  App/                     ← unpackaged head (F5 default)
    Program.cs             ← ReactorApp.Run<DemoScriptShell>(...)
    DemoScriptShell.cs     ← top-level component
    Components/            ← HeaderBar, DemoPromptPanel, StepsPanel, StepCard, InlineBanner
    Services/              ← parser, store, watcher, pipeline, runner, AI client
    Models/                ← DemoScriptModel, StepModel, BuildState
    Resources/SystemPrompt.txt    ← Layer 1 system prompt (embedded)
  Package/                 ← MSIX packaging head (F5 to debug packaged mode)
  build/                   ← sign-cert.ps1 / package.ps1 / install.ps1
  demo-projects/           ← seeded demos referenced by this README
```

## Demo projects

Two ready-to-go demos live under `demo-projects/`:

- `spectre-chat/` — single-file Spectre.Console chat demo
- `aspire-hello/` — multi-file .NET Aspire hello-world demo

Open either folder from the app to walk through the format.
