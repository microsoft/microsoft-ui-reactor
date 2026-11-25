# Contributing to Reactor

Reactor lives at **[github.com/microsoft/microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor)**. Reactor is an experimental project — the API surface, DSL, and layering are all subject to change as we iterate in the open. Contributions and feedback are welcome from day one.

- **Report a bug or propose a feature:** [open an issue](https://github.com/microsoft/microsoft-ui-reactor/issues/new/choose)
- **Ask a question or float an idea:** [start a discussion](https://github.com/microsoft/microsoft-ui-reactor/discussions)
- **Submit a change:** open a PR against `main` — please link the issue it addresses, keep the change focused, and include tests

When filing an issue, include the platform (`x64` / `ARM64`), .NET SDK version, and a minimal repro. For bugs that involve real WinUI controls, a selfhost fixture (see below) is the ideal repro format.

---

## Contributor License Agreement

This project welcomes contributions and suggestions. Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit [https://cla.opensource.microsoft.com](https://cla.opensource.microsoft.com).

When you submit a pull request, a CLA bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g., status check, comment). Follow the instructions provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

---

## Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) to build (pinned in
  `global.json`; Reactor uses C# 15) and the
  [.NET 10 Desktop runtime](https://dotnet.microsoft.com/download/dotnet/10.0) to run the
  `net10.0` apps and tests. `./bootstrap.ps1` installs both. Apps that *consume* Reactor
  still only need the .NET 10 SDK: `Directory.Build.targets` pins `WindowsSdkPackageVersion`
  so the build SDK doesn't change the Windows SDK projection the shipped libraries compile
  against (a newer one makes .NET 10 SDK consumers fail with NETSDK1148).
- Windows App SDK 2.2 or newer — restored automatically from NuGet, no manual install required
  (the pinned floor lives in `WindowsAppSDKVersion` / `WindowsAppSDKWinUIVersion` in
  `Directory.Build.props`)
- Visual Studio 2026 or VS Code with C# Dev Kit

> **Windows App SDK packages:** The right package is injected centrally by `Directory.Build.targets` based on each project's shape: framework-dependent projects — **libraries and apps alike**, including the shipped `Microsoft.UI.Reactor` — reference the lean `Microsoft.WindowsAppSDK.WinUI` sub-package plus `Microsoft.WindowsAppSDK.Runtime` (a library's dependencies become its consumers', and without Runtime the SDK silently builds a consumer app self-contained); **self-contained / MSIX** projects keep the full `Microsoft.WindowsAppSDK` metapackage (it bundles the runtime + DWriteCore redist). The metapackage's AI/ML/Widgets/DWrite slices are otherwise unused. Versions are pinned in `Directory.Build.props` (`WindowsAppSDKWinUIVersion` and `WindowsAppSDKVersion`) — update them there to change the version for every project at once.

> **Clone to a short path (Windows):** Clone into a short root directory such as
> `C:\src\` (for example `C:\src\microsoft-ui-reactor`) rather than a deeply
> nested or OneDrive-synced folder. The Windows App SDK XAML markup compiler
> (`XamlCompiler.exe`) and resource indexer (`MakePri.exe`) are 32-bit tools with
> no long-path support, so when the repository sits on a long path a project's
> generated `obj`/`bin` output can exceed the Windows 260-character `MAX_PATH`
> limit and the build fails with errors such as `MSB3073`, `PRI210`, or
> `APPX0002`. This is most common on ARM64 dev boxes, where these tools run under
> x86 emulation. Enabling `git config --global core.longpaths true` lets Git
> itself check out long paths, but the Windows App SDK build tools still require
> the short checkout path. See
> [issue #678](https://github.com/microsoft/microsoft-ui-reactor/issues/678).

---

## Setting up a source checkout

Clone the repository and run the bootstrap script once. The last line starts the script in the PowerShell you are using now and bypasses the execution policy for this one run:

```powershell
git clone https://github.com/microsoft/microsoft-ui-reactor.git
cd microsoft-ui-reactor
& (Get-Process -Id $PID).Path -ExecutionPolicy Bypass -File .\bootstrap.ps1
```

You can run the script again at any time. It keeps the tools and the template pack you already have, and repacks `mur` and the framework.

`bootstrap.ps1` does these steps, in order:

1. Checks for the .NET 11 SDK and the .NET 10 Desktop runtime, and installs them with winget if they are missing. It also checks the Windows App Runtime and the `winapp` CLI that the E2E tests use.
2. Packs the `mur` CLI (`src/Reactor.Cli`) and installs it as a `dotnet tool` global tool, so `mur` is on PATH in every shell.
3. Runs `mur pack-local`, which packs the framework from your working tree into `local-nupkgs/`.
4. Installs the Windows App SDK template pack (`Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`), which provides `dotnet new reactor`.
5. Installs the Reactor agent plugin under `~/.claude/plugins/reactor` (a symlink when allowed, a copy otherwise). Skip it with `-SkipPlugin`.
6. Builds and installs the Reactor Visual Studio preview extension, if Visual Studio has the *Visual Studio extension development* workload. Skip it with `-SkipVsExtension`.

Run `Get-Help ./bootstrap.ps1 -Detailed` for the other switches.

Apps created with `dotnet new reactor` reference the public `Microsoft.UI.Reactor` package from NuGet.org. To make a scaffolded app use the package you built from source, pass `--reactor-version 0.0.0-local`. Only need the framework package? Reference `Microsoft.UI.Reactor` from NuGet.org and skip the bootstrap.

The optional `Microsoft.UI.Reactor.Advanced` and `Microsoft.UI.Reactor.Devtools` packages always have the same version as `Microsoft.UI.Reactor` when we publish them. Release owners: read the [release runbook](docs/contributing/release-runbook.md) before you tag a new version.

### Templates

By default, the bootstrap script installs the template pack only when it is missing. To hold it at a version you know works, pin it:

```powershell
./bootstrap.ps1 -WinAppSdkTemplatesVersion <version>
```

The `Microsoft.UI.Reactor.ProjectTemplates` package, which used to provide `dotnet new reactorapp`, was removed from this repository. Its versions on NuGet.org are deprecated. Use `dotnet new reactor` or `winapp new -t reactor` instead.

### On a restricted network

If your network blocks the public NuGet feed, add a package mirror to your user NuGet.Config. The bootstrap script looks there for a known mirror. If the mirror works without credentials, the script uses it for this run only, including the optional Visual Studio extension build. Without one, the script uses the public feed, as before. To choose a mirror yourself, pass its config file:

```powershell
./bootstrap.ps1 -NuGetConfig <path>
```

Keep credentials in your user NuGet config. The script never writes them to the repository. Feed URLs must use HTTPS (a loopback feed for local development is the exception), and must not contain credentials, a query string or a fragment.

`src/Reactor.Cli` and two samples use `GitHub.Copilot.SDK`. When you build, run or test them, the SDK downloads a native `copilot` CLI from the [github/copilot-cli releases](https://github.com/github/copilot-cli/releases) and checks it against the release's `SHA256SUMS.txt`. If your network blocks that download, you have two options:

- Point the download at a mirror of the release files with the `CopilotCliReleaseBaseUrl` MSBuild property or the `COPILOT_CLI_DOWNLOAD_BASE_URL` environment variable.
- Use a binary you downloaded yourself with `CopilotCliBinaryPath`.

`CopilotSkipCliDownload=true` also gets the build going, but only to compile. It leaves out the native binary, and `mur loc translate` and the two Copilot SDK samples need that binary at run time. CI sets it because CI never runs those paths.

### After `git pull`

A pull changes your source, but not the packages in `local-nupkgs/` or the installed `mur` tool. A copied plugin does not change either (a symlinked one does). Rebuild them with one of these:

```powershell
mur upgrade           # repacks the framework and refreshes the plugin and VS extension
./bootstrap.ps1       # the same, plus a new `mur` global tool
```

`mur upgrade` is the faster option. Re-run `bootstrap.ps1` when `src/Reactor.Cli` changed, because a running `mur` process cannot replace its own binary.

Neither command repacks the `dotnet new reactor` templates. They ship in the Windows App SDK template pack, so a `git pull` never makes them stale. To update the pack, run `winapp new --list --template-version latest`.

### Verify the install

```powershell
mur doctor
```

`mur doctor` checks the .NET SDK, the `mur` install, the packages in `local-nupkgs/`, the template pack and the optional plugin. It prints PASS, WARN or FAIL for each check, with a fix for each failure.

### Manual setup

If you prefer not to run `bootstrap.ps1`, these are the same steps by hand. Steps 1 to 4 use only `dotnet`, `git` and a helper script from this repository. Step 5 uses the `mur` you install in step 4. After step 2, run every step from the repository root.

| # | Step | What it produces |
|---|------|------------------|
| 1 | `dotnet --list-sdks` and `dotnet --list-runtimes` | Confirms the .NET 11 SDK and the .NET 10 runtime |
| 2 | `git clone` and `cd` | A local source checkout |
| 3 | `dotnet pack src/Reactor.Cli` | `Microsoft.UI.Reactor.Cli.<version>.nupkg` in `local-nupkgs/` |
| 4 | `dotnet tool install -g` | `mur` on PATH from `~/.dotnet/tools` |
| 5 | `mur pack-local` | Source-built framework packages in `local-nupkgs/` |
| 6 | `dotnet new install` | The `dotnet new reactor` templates |
| 7 | Symlink or copy `plugins/reactor` | The Reactor agent plugin under `~/.claude/plugins/reactor` (optional) |
| 8 | `mur doctor` | A check that steps 1 to 7 worked |

**1. Check the prerequisites.** See [Prerequisites](#prerequisites).

```powershell
dotnet --list-sdks       # expect an entry that starts with "11.0."
dotnet --list-runtimes   # expect Microsoft.NETCore.App 10.0.x and Microsoft.WindowsDesktop.App 10.0.x
```

**2. Clone the repository.**

```powershell
git clone https://github.com/microsoft/microsoft-ui-reactor.git
cd microsoft-ui-reactor
```

**3. Pack `mur` as a global-tool package.** `Reactor.Cli.csproj` sets `PackAsTool=true`. Pass the host architecture as the platform: the build runs the SignaturesGen apphost, which must match the machine. Give each pack a new, higher version, as `bootstrap.ps1` does. `dotnet tool update` skips a version that is already installed, so a repack with the same version would leave the old `mur` in place.

```powershell
. ./tools/BootstrapFeedResolver.ps1
$hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
$murVersion = Get-ReactorLocalCliVersion
dotnet pack src/Reactor.Cli/Reactor.Cli.csproj `
    -c Release `
    "-p:Platform=$hostArch" `
    "-p:Version=$murVersion" `
    -o local-nupkgs `
    --nologo
```

**4. Install `mur` as a global tool.** Install the version you just packed. If `mur` is already installed, use `update` instead of `install`.

```powershell
dotnet tool install -g `
    --add-source ./local-nupkgs `
    Microsoft.UI.Reactor.Cli `
    --version $murVersion `
    --no-cache --ignore-failed-sources

# If mur is already installed:
# dotnet tool update -g --add-source ./local-nupkgs Microsoft.UI.Reactor.Cli --version $murVersion --no-cache --ignore-failed-sources
```

`dotnet tool install -g` adds `~/.dotnet/tools` to the user PATH, but the current shell does not see that change. For this shell only, add it yourself so the next step finds `mur`. New shells pick it up on their own.

```powershell
$env:Path = "$env:USERPROFILE\.dotnet\tools;$env:Path"
```

**5. Pack the framework from source.** This creates the `0.0.0-local` packages, so recipes, samples and smoke tests in this clone use your working tree instead of the published package.

```powershell
mur pack-local
# Produces:
#   local-nupkgs/Microsoft.UI.Reactor.0.0.0-local.nupkg
#   local-nupkgs/Microsoft.UI.Reactor.Advanced.0.0.0-local.nupkg
#   local-nupkgs/Microsoft.UI.Reactor.Devtools.0.0.0-local.nupkg
```

To run it without the installed `mur`, call the source project:

```powershell
dotnet run --project src/Reactor.Cli/Reactor.Cli.csproj `
    -c Release "-p:Platform=$hostArch" -- pack-local
```

**6. Install the `dotnet new reactor` templates.** They come from the Windows App SDK template pack on NuGet.org, not from this repository. The pack is prerelease only, so pin a version: a plain `dotnet new install` finds stable versions only.

```powershell
dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates::<version>
```

`winapp new --list` installs the newest pack for you, which is what `bootstrap.ps1` does.

**7. (Optional) Install the Reactor agent plugin.** If you use Claude Code, put the plugin folder from this repository in its plugin path. A symlink is best, because changes in your checkout show up at once. A copy works when you cannot create symlinks (Developer Mode off and a non-admin shell).

```powershell
$pluginSrc = (Resolve-Path "plugins/reactor").Path
$pluginDst = "$env:USERPROFILE\.claude\plugins\reactor"
New-Item -ItemType Directory -Path (Split-Path $pluginDst) -Force | Out-Null
if (Test-Path $pluginDst) { Remove-Item $pluginDst -Recurse -Force }
try {
    New-Item -ItemType SymbolicLink -Path $pluginDst -Target $pluginSrc -ErrorAction Stop | Out-Null
} catch {
    Copy-Item $pluginSrc $pluginDst -Recurse -Force
}
```

For Copilot CLI or another agent, follow that tool's plugin install steps and point it at `<repo>/plugins/reactor`.

**8. Verify.**

```powershell
mur doctor
```

#### Refreshing after `git pull`

Without the bootstrap script, repeat **step 5** after every pull. Repeat **steps 3 and 4** only when `src/Reactor.Cli/` changed, and run them from a shell that is not running `mur`. You do not need to repeat step 6 (see [After `git pull`](#after-git-pull)).

> **Why a global tool?** You can also run `mur` from `bin/<arch>/`, because the CLI project copies `mur.exe` there after every build. The global tool is the simpler default: `mur` works from any shell and any folder without PATH changes, and `dotnet tool update -g` updates it.

---

## Building

### From the command line

```bash
# Restore packages (pulls experimental WinUI 3 from NuGet)
dotnet restore Reactor.slnx

# Build the entire solution (framework, tests, test app, samples)
dotnet build Reactor.slnx

# Build just the framework
dotnet build src/Reactor/Reactor.csproj
```

### Reproducing the CI build gate

The plain `dotnet build Reactor.slnx` above is a **Debug** build, and a green Debug build does not clear the `Build solution` CI job. `TreatWarningsAsErrors` is scoped to `Configuration=Release` in `Directory.Build.props`, so Debug stays warning-tolerant for a fast inner loop while the CI PR-gate builds Release and fails on any compiler or analyzer warning.

Before opening a PR, reproduce that job — restore first, then build Release with `--no-restore`:

```bash
dotnet restore Reactor.slnx
dotnet build Reactor.slnx --no-restore -c Release
```

Keep those as two steps, the way the `Restore` and `Build` steps of the `build-solution` job in [`ci.yml`](.github/workflows/ci.yml) do. The one-shot `dotnet build Reactor.slnx -c Release` runs its implicit restore under `Configuration=Release` as well, and NuGet honors `TreatWarningsAsErrors` during restore — so `NU`-prefixed restore warnings (an unreachable feed, missing package vulnerability data) become hard errors that CI, which restores without `-c Release`, never hits.

### From Visual Studio

1. Open `Reactor.slnx` in Visual Studio 2026
2. Select the **x64** or **ARM64** platform from the toolbar
3. Build the solution (Ctrl+Shift+B)

Visual Studio will restore NuGet packages on first load, pulling the experimental Windows App SDK.

### Platforms

Library projects (`Reactor`, `Reactor.Interop.WinForms`) are architecture-neutral (`AnyCPU`). Application projects (samples, tests, CLI) target `x64` and `ARM64`.

When building via the solution (`dotnet build Reactor.slnx`), the platform is selected automatically. When building or running a single app project directly, pass `-p:Platform=x64` (or `ARM64`). Without it, the build fails with a `WindowsAppSDKSelfContained` error:

```bash
dotnet build tests/Reactor.Tests -p:Platform=x64
dotnet test  tests/Reactor.Tests -p:Platform=x64
```

---

## Running tests

See [`TESTING.md`](TESTING.md) for the full guide — four suites (unit / selftest / packaged selftest / E2E), how to pick a tier, NativeAOT runs, and the code-coverage workflow.

For timing-sensitive selftests, distinguish eventual completion from survival across
a timer window. See [timer lifetime tests](TESTING.md#timer-lifetime-tests) for completion
callbacks with bounded timeouts, controlled expiry schedules, and mutation checks
that preserve the original assertion's meaning.

Quick reference:

```bash
dotnet test tests/Reactor.Tests       # unit (xUnit, headless, fast)
dotnet test tests/Reactor.SelfTests   # selftest (real WinUI, in-process)
dotnet test tests/Reactor.PackagedTests -p:Platform=x64   # packaged selftest (MSIX identity)
dotnet test tests/Reactor.AppTests    # E2E (winapp ui)
```

---

## Running the demo app

The interactive demo app exercises every built-in control:

```bash
dotnet run --project samples/Reactor.TestApp
```

---

## Project layout

```
src/Reactor/                      Core framework library
  Core/
    Component.cs                  Base Component class, hook methods
    Element.cs                    40+ virtual element record types
    RenderContext.cs              Hook state storage, effect tracking
    Reconciler.cs                 Tree diff orchestration
    Reconciler.Mount.cs           Mount dispatch and composition-primitive handlers
    Reconciler.Update.cs          Update dispatch and composition-primitive handlers
    V1Protocol/                  Control registry, handlers, descriptors, and child strategies
    ChildReconciler.cs            Keyed child list reconciliation
    ElementPool.cs                Control reuse pool
    PropValueRegistry.cs          Property value caching/hashing
  Elements/
    Dsl.cs                        200+ static factory methods (TextBlock, Button, VStack, Flex, etc.)
    ElementExtensions.cs          Fluent modifiers (.Bold(), .Margin(), .Width(), etc.)
    FlexExtensions.cs             .Flex() attached property modifier for flex children
  Flex/
    FlexPanel.cs                  CSS Flexbox panel backed by Yoga layout engine
  Yoga/
    YogaAlgorithm.cs              Pure C# port of Meta's Yoga layout algorithm
    YogaNode.cs                   Yoga node tree structure
    YogaStyle.cs                  Style properties (direction, justify, align, etc.)
    YogaEnums.cs                  Yoga enum types (YogaFlexDirection, YogaJustify, etc.)
  Hosting/
    ReactorApp.cs                 Static entry point — ReactorApp.Run<T>()
    ReactorHost.cs                Render loop, state batching, dispatcher scheduling
    ReactorHostControl.cs         Embeddable host for existing WinUI apps
    HotReloadService.cs           .NET Hot Reload integration for Visual Studio
src/Reactor.Cli/                  CLI scaffolding tool
tests/
  Reactor.Tests/                  1. Unit tests — xUnit (no UI window; includes D3 charting tests)
  Reactor.SelfTests/              2. Selftest runner — MSTest wrapper that subprocess-launches the Host and parses TAP
  Reactor.AppTests.Host/          2. Host app — hosts selftest fixtures and the winapp ui fixture navigator
  Reactor.PackagedTests/          3. Packaged selftest runner — runs fixtures under MSIX identity
  Reactor.PackagedTests.Host/     3. Packaged host — shares the selftest fixture sources
  Reactor.AppTests/               4. E2E tests — MSTest + winapp ui
  stress_perf/                    Performance benchmarks
samples/
  Reactor.TestApp/                Interactive control showcase / demo app
  apps/                           Sample apps (wordpuzzle, regedit, minesweeper, chat, netpulse, etc.)
  TodoApp/                        Todo app sample
```

---

## How to add a new element type

Controls mount and update through a registered `ControlDescriptor<TElement, TControl>` or `IElementHandler<TElement, TControl>`. The [native-control extension guide](docs/guide/extending-reactor-controls.md) explains the authoring choices, event wiring, echo suppression, pooling, and child strategies; its [StarMeter sample](docs/_pipeline/apps/extending-reactor-controls/App.cs) shows the complete implementation.

1. **Define an immutable element record** with the control's properties and callbacks. Built-in element records live in `src/Reactor/Core/Element.cs`.
2. **Describe the control's behavior.** Prefer a `ControlDescriptor<TElement, TControl>` for property, event, and child mappings; use an `IElementHandler<TElement, TControl>` when the descriptor cannot express the control's lifecycle. The handler and descriptor infrastructure lives under `src/Reactor/Core/V1Protocol/`.
3. **Register the handler before the first mount.** For a custom control, follow the sample's factory holder: its static constructor calls `ControlRegistry.Register<TElement, TControl>` with a static handler factory. Generated wrappers emit their own registration. See [registration](docs/guide/control-reconciler-protocol.md#registration) for the supported registration shapes.
4. **Expose the authoring API.** A factory returns the element record; built-in DSL factories live in `src/Reactor/Elements/Dsl.cs`. Add fluent modifiers in `src/Reactor/Elements/ElementExtensions.cs` where appropriate, and follow the documentation requirements below for new public surface.

### Add tests

Use unit tests in `tests/Reactor.Tests/` for element creation and pure managed logic. Verify mount, update, event callbacks, and reuse against real WinUI controls with selftest fixtures in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/`. Use the packaged selftest tier for behavior that depends on MSIX identity, and E2E tests in `tests/Reactor.AppTests/Tests/` for real input or cross-process UI Automation. See [test tier selection](TESTING.md#when-to-write-which-test) for fixture registration and packaged-tier requirements.

---

## How to add a new hook

Hooks live in `src/Reactor/Core/Component.cs` (public API) and `src/Reactor/Core/RenderContext.cs` (implementation).

1. Add the hook method to `Component` (delegates to `RenderContext`)
2. Implement the logic in `RenderContext`, using `GetOrCreateHook<T>()` to manage state
3. Follow the convention: hooks must be called in the same order every render, no conditional calls
4. Add tests in `tests/Reactor.Tests/`

---

## Documenting changes

New public surface in `src/Reactor/` lands with a doc page — no merge without doc. The bar is **Solid tier or higher** (spec [041 §11](docs/specs/041-docs-comprehensive-uplift.md)); Comprehensive is preferred when the surface is top-traffic or has non-obvious mental-model implications.

What "lands with a doc page" means in practice depends on the change:

| Change | Doc obligation |
|---|---|
| New element factory in `Dsl.cs` | Extend the matching topic page (e.g. add to the controls catalog under `text-and-media`, `forms`, `collections`, etc.) with at least a row in the reference table + a snippet ref. Add a `<!-- ref:Member -->` marker so the generated reference page backlinks the topic. |
| New hook on `RenderContext` / `Component` | Add to `hooks.md` reference table + a Pattern section showing real usage. Add a `<!-- ref:UseX -->` marker. |
| New public type that doesn't fit an existing topic | Author a new template under `docs/_pipeline/templates/`. Solid tier is the minimum for net-new pages. Add an entry to `reference-map.yaml` so reference generation routes its members. |
| Internal refactor with no public-API change | No doc page required, but if you renamed a public symbol that templates reference, update those templates in the same PR. |

The CI tier-drift gate (`docs-check-tier` in `.github/workflows/ci.yml`, spec [041 §5.2](docs/specs/041-docs-comprehensive-uplift.md)) blocks merges that knock a template's declared tier out of compliance with its §11 structural shape. The doc pipeline also emits `REACTOR_DOC_REGISTRY_W002` when a registry-declared guide page has no inbound `<!-- ref:Member -->` markers — that warning surfaces public API that has slipped through the doc-coverage gate.

For the full doc-pipeline workflow (compile, check-tier, render-diagrams), see [`docs/contributing/doc-pipeline.md`](docs/contributing/doc-pipeline.md).

---

## Code style

- **Elements are immutable records.** Use `with` expressions for variations.
- **Hooks follow React conventions.** Same order every render, no conditional hooks.
- **Factory methods over constructors.** `TextBlock("hello")` not `new TextBlockElement("hello")`.
- **Fluent modifiers for layout.** `.Margin(16).Bold()` not constructor parameters.
- **Event dispatch through attached state.** Event trampolines read the current element from `ReactorState`, stored in the `ReactorAttached.StateProperty` attached dependency property, so callbacks stay current across updates and pool reuse.
- **No XAML.** Everything is C#.

### Diagnostics: audience, not severity, decides the channel

`Debug.WriteLine` exists for the framework contributor reading the Output window in Visual Studio; it disappears in Release builds. `ReactorEventSource` (the `Microsoft-UI-Reactor` provider) exists for the app developer, SRE, and support engineer — it's release-visible, keyword-gated, and zero-allocation when no consumer is attached. New code that reports an error, a swallowed exception, or a failing HRESULT belongs on the EventSource side; new code that traces internal framework state for a contributor's benefit (reconciler bookkeeping, scheduler queue depth) stays on `Debug.WriteLine`.

For swallowed exceptions and HRESULT-return diagnostics, route through the `DiagnosticLog` helper:

```csharp
catch (COMException ex) when (ex.HResult is HResults.RPC_E_DISCONNECTED or HResults.E_FAIL)
{
    DiagnosticLog.SwallowedError(LogCategory.Hosting, "AppWindow.Close", ex);
}
```

`DiagnosticLog.SwallowedError` and `DiagnosticLog.HResultFailed` emit the typed ETW event under `Keywords.Errors` at `Warning` in Release **and** mirror a richer line (including `ex.Message`) to `Debug.WriteLine` in Debug builds via a `[Conditional("DEBUG")]` helper. The exception message never reaches the ETW payload — see [`docs/guide/diagnostics.md`](docs/guide/diagnostics.md) for the PII discipline and the full capture workflow.

**Adding a new swallow site means adding a ledger row.** Spec 044 §6.7 (G9) makes every `catch` that absorbs an exception in `src/Reactor/` or `src/Reactor.Advanced/` a written, adjudicated decision, recorded in [`docs/specs/044/swallowed-error-audit.md`](docs/specs/044/swallowed-error-audit.md). Add a row to the section for your file using the canonical schema — `| Site(s) | Sites | Verdict | Status | Notes |` — with one verdict token from that file's vocabulary and a declared site count.

The summary table at the top of that file is **derived**, not maintained: it is the sum of the ledger rows, and `tests/Reactor.Tests/Docs/SwallowedErrorAuditTests.cs` fails on any drift. Never hand-edit a cell in it. Instead run

```bash
dotnet test tests/Reactor.Tests -p:Platform=x64 -p:SkipSignaturesGen=true -p:SkipReactorApiGen=true --filter-class "*SwallowedErrorAudit*"
```

and paste the table the failure prints. (The two skip flags are the pair CI passes; together they keep a ledger-only edit from leaving `skills/reactor.api.txt` modified in your tree.) Hand-incrementing was how the published counts became unreproducible (issue #959): two branches editing `37 → 38` from the same base produce identical edits that git auto-resolves as agreement, silently dropping one increment.

---

## Hot reload

Reactor supports .NET Hot Reload via `HotReloadService.cs`. When you edit code in Visual Studio and save, the framework re-renders with your changes while preserving hook state. No special setup needed — it hooks into the standard `MetadataUpdateHandler` mechanism.
