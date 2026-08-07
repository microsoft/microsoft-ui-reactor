# Widget Creator — an app that creates apps

A Reactor (WinUI 3) desktop app that turns a one-line prompt into a **single-file
Reactor app**, builds it, and launches the result inside an **MXC sandbox** with
UI + remote network but **no local filesystem** — a web-like, run-untrusted-UI
experience. The creator itself is, of course, a Reactor app.

```
prompt ─▶ GitHub Copilot SDK ─▶ single-file Reactor app (.cs)
       ─▶ dotnet build          ─▶ widget.exe
       ─▶ MXC wxc-exec          ─▶ sandboxed window (UI ✓, network ✓, local files ✗)
```

## What it demonstrates

- **Generation** — the [`GitHub.Copilot.SDK`](https://www.nuget.org/packages/GitHub.Copilot.SDK)
  streams a complete single-file Reactor app from your prompt (same engine the
  `demo-script-tool` sample uses; rides your `gh auth` Copilot subscription). The
  system prompt bakes in the Reactor **Windows 11 design** rules (TitleBar, theme
  tokens, cards, 4px grid) so generated widgets look like first-class Win11 apps.
- **Build** — the generated `widget.cs` is scaffolded into a tiny
  self-contained Reactor project and built with the same platform-shaped output
  layout as the Reactor app template.
- **Sandboxed run** — the built `widget.exe` is launched by
  [MXC](https://github.com/microsoft/mxc) (Microsoft eXecution Containers) via the
  native `wxc-exec` binary under a policy that allows a visible window and
  outbound network but denies the local filesystem. MXC grants the sandbox
  read+execute on **only the app's own directory** (from
  `filesystem.readonlyPaths`); the user's profile, Documents, etc. are
  unreachable. We never touch filesystem ACLs ourselves.
- **Runtime repair** — each saved widget persists the Copilot session ID that
  created it. If the sandboxed app exits non-zero later, Widget Creator resumes
  that session, sends the crash code/output plus the current source back to the
  agent, rebuilds the repaired widget, saves the updated session metadata, and
  relaunches it.
- **Render-error visibility** — generated widgets include a fail-fast root
  `ErrorBoundary` wrapper. Render exceptions are written to stderr with a
  `WIDGET_CREATOR_RENDER_CRASH` marker and exit code `70`, so Reactor's normal
  visual fallback does not hide the failure from the repair agent. The generated
  helper also reports unhandled managed exceptions (`71`) and unobserved task
  exceptions (`72`) to stderr. It does not write crash files because the
  sandboxed app has no write access to its app directory.

## Run it

```pwsh
dotnet run --project samples/apps/widget-creator/widget-creator.csproj -p:Platform=ARM64
```

(Use `-p:Platform=x64` on an x64 machine.)

### Prerequisites

1. **Copilot auth** — install the [GitHub CLI](https://cli.github.com/) and run
   `gh auth login --web` with a Copilot-enabled account (`gh auth status` to
   confirm). The bundled Copilot CLI rides that account.

   The model defaults to `claude-sonnet-4.6`. Override it with
   `WIDGET_CREATOR_MODEL` (e.g. `gpt-5.4`) if that model is retired or you want
   a different one — `session.create` fails with *Model "…" is not available*
   when the id is no longer served.
2. **Windows App Runtime** — generated widgets are *framework-dependent*
   (`WindowsAppSDKSelfContained=false`), so they bind the machine-wide
   Windows App Runtime at launch instead of carrying a ~220 MB copy each.
   Widget Creator probes for it on startup and, if it is missing or older than
   the version widgets are built against, shows a banner with buttons that open
   the [direct installer](https://aka.ms/windowsappsdk) or the
   [downloads page](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)
   in your browser. You can also install it up front:

   ```pwsh
   winget install Microsoft.WindowsAppRuntime.2.1
   ```

   To check without opening the window:

   ```pwsh
   WidgetCreator.exe --check-runtime   # exit 0 = satisfied, 1 = missing/outdated
   ```

   The required package family and minimum version are not hardcoded — they come
   from the Windows App SDK's own generated `WindowsAppSDK-VersionInfo.cs`
   (compiled in via `WindowsAppSdkIncludeVersionInfo`), so the check tracks
   `WindowsAppSDKVersion` in `Directory.Build.props` automatically. Two env
   overrides exist to exercise the failure paths on a healthy machine:
   `WIDGET_CREATOR_MIN_WINAPPRUNTIME` (minimum version) and
   `WIDGET_CREATOR_WINAPPRUNTIME_FAMILY` (package family).

3. **MXC** — a build of `wxc-exec.exe`. By default the app uses the **pinned,
   vendored** copy shipped next to it (`…\mxc\<rid>\wxc-exec.exe`), which is known
   to auto-fall back off BaseContainer. Override with:
   - `WIDGET_CREATOR_WXC_EXEC` — full path to `wxc-exec.exe`,
   - `WIDGET_CREATOR_MXC_BIN` — a `sdk\bin` dir (the app appends `arm64`/`x64`),
   - `WIDGET_CREATOR_USE_LOCAL_MXC=1` — prefer a local mxc checkout
     (`…\mxc\src\target\<triple>\release\` then `…\mxc\sdk\bin\<arch>\`) over the
     vendored copy, for developers iterating on the MXC CLI itself, or
   - `WIDGET_CREATOR_MXC_ROOT` — the MXC checkout root used by the opt-in above
     (default `%USERPROFILE%\Code\mxc`).

4. **Local Reactor package** — generated widgets reference
   `Microsoft.UI.Reactor 0.0.0-local`, resolved from this repo's `local-nupkgs`
   feed. Run `mur pack-local` if it's missing. Override the feed path with
   `WIDGET_CREATOR_NUPKGS`. The widget's Windows App SDK references
   (`Microsoft.WindowsAppSDK.WinUI` + `.Runtime`) are pinned to the same versions
   this repo builds with — they are generated into the app from
   `Directory.Build.props`, because a widget that pins a different version fails
   its build in `Microsoft.WindowsAppSDK.ComponentReference.targets`.

Type a prompt, click **Generate & Run**. The generated source streams into the
right panel; the build + `wxc-exec` log streams below it. The widget window opens
sandboxed — close it to finish the run. If it crashes instead, the creator keeps
watching the sandbox process, restores the widget's saved Copilot session, and
asks the agent to repair the app from the crash details.

## Publish as a native app (NativeAOT)

Widget Creator is trim/AOT-clean and can be published as a **native** executable
(no managed `WidgetCreator.dll`, no JIT). This is behind an opt-in property so the
normal `dotnet run` / framework-dependent publish above stays unchanged:

```pwsh
dotnet publish samples/apps/widget-creator/widget-creator.csproj `
  -c Release -p:Platform=x64 -r win-x64 `
  -p:PublishAotInternal=true -p:IlcTreatWarningsAsErrors=false
```

- `-r win-x64` (or `-r win-arm64`) is required for a NativeAOT publish; match it to
  `-p:Platform`.
- `PublishAotInternal=true` is the repo-canonical AOT opt-in gate (the same property
  the other AOT samples/hosts use); it flips `PublishAot` on only for this publish.
- `-p:IlcTreatWarningsAsErrors=false` only relaxes ILC trim/AOT warnings from
  third-party dependencies we don't own (the Copilot SDK / Windows App SDK). This
  project's **own** code stays analyzed-as-errors at build time — the repo's
  IL2\*/IL3\* analyzer is on for this sample (it no longer sets
  `ReactorSkipAotAnalysis`), so the two `System.Text.Json` sidecars use source
  generation rather than reflection.
- `vswhere.exe` must be on `PATH` for the native link step; if it isn't, prepend
  `C:\Program Files (x86)\Microsoft Visual Studio\Installer` to `PATH`.

The published `WidgetCreator.exe` lands under
`bin\x64\Release\net10.0-windows10.0.22621.0\win-x64\publish\`.

> **Validated scope.** The native build has been verified to publish and to launch
> the Widget Creator window, and its source-generated `meta.json` loader runs in
> the native image. The end-to-end generate → build → MXC-sandbox workflow still
> shells out to the bundled Copilot CLI and the vendored `wxc-exec` binaries exactly
> as the framework-dependent build does; those child processes are unaffected by AOT
> and the native publish does not itself re-exercise that full pipeline.

## How the sandbox policy works

The app emits an MXC `ContainerConfig` (schema `0.6.0-alpha`, `processcontainer`
backend) and runs `wxc-exec <config>.json`:

| Surface | Setting | Effect |
|---|---|---|
| UI / display | `ui.disable = false` | the widget renders a real WinUI window |
| Network | `network.defaultPolicy = allow` + `internetClient` capability | outbound HTTP(S) works |
| Filesystem | `filesystem.readonlyPaths = [appDir]` | MXC grants read+exec to **only** the app's own dir; everything else under the user profile is default-deny |
| Clipboard / input | `clipboard = none`, `injection = false` | no clipboard, no synthetic input |

MXC's tier detector chooses how to enforce that grant (BaseContainer, AppContainer
+ BFS, or AppContainer + DACL). The app never edits ACLs — declaring the app
directory in `readonlyPaths` is enough; MXC's DACL manager stamps the grant.

> **Host note.** The app lets `wxc-exec` select the strongest available
> containment tier: it tries **BaseContainer** first and falls back to
> **AppContainer + DACL** on hosts where BaseContainer is gated by the OS build
> (`Experimental_CreateProcessInSandbox → E_NOTIMPL`). The app no longer forces
> the weaker tier — trusting the vendored `wxc-exec` (0.7.0+) to handle fallback.
> To pin the DACL tier for debugging, set `MXC_DISABLE_BASE_CONTAINER=1` in the
> environment yourself; it is inherited into the `wxc-exec` process.

## Layout

```
samples/apps/widget-creator/
  Program.cs                 ← ReactorApp.Run<WidgetCreatorShell>
  WidgetCreatorShell.cs      ← the UI + generate→build→sandbox pipeline
  Services/
    CopilotSdkClient.cs      ← streaming text completion via GitHub.Copilot.SDK
    IModelClient.cs
    WidgetGenerator.cs       ← system prompt + stream + ```csharp fence extraction
    WidgetWorkspace.cs       ← scaffolds widget.cs + widget.csproj + nuget.config
    WidgetBuilder.cs         ← dotnet publish (no ACL edits — MXC grants the app dir)
    MxcSandbox.cs            ← builds the ContainerConfig + runs wxc-exec
    WindowsAppRuntimeCheck.cs ← probes the machine-wide Windows App Runtime
    SessionLog.cs
  Resources/SystemPrompt.txt ← instructs the model to emit one single-file Reactor app
```

## Notes & caveats

- This is a demo wired against a local MXC checkout and the repo's `local-nupkgs`
  feed — paths and schema choices are host-specific and meant to be cleaned up
  before any real submission.
- MXC is an early preview; its sandbox profiles are **not** a security boundary
  yet (see the MXC README).
- A real generation call opens an interactive Copilot CLI auth flow on first use.
