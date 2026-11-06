# Widget Creator — an app that creates apps

A Reactor (WinUI 3) desktop app that turns a one-line prompt into a **single-file
Reactor app**, builds it, and launches the result inside an **MXC sandbox** with
UI, opt-in network, and **no local filesystem** — a web-like, run-untrusted-UI
experience. The creator itself is, of course, a Reactor app.

```
prompt ─▶ GitHub Copilot SDK ─▶ single-file Reactor app (.cs)
       ─▶ dotnet build          ─▶ widget.exe
       ─▶ MXC SDK (in-process)  ─▶ sandboxed window (UI ✓, network opt-in, local files ✗)
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
  [MXC](https://github.com/microsoft/mxc) (Microsoft eXecution Containers) through
  the in-process [`Microsoft.Mxc.Sdk`](https://www.nuget.org/packages/Microsoft.Mxc.Sdk)
  (stable 1.0.x, Windows ProcessContainer backend) under a least-privilege policy:
  a visible window, **no** network unless granted per widget, and no local
  filesystem. MXC grants the sandbox read+execute on **only the app's own
  directory** (from `filesystem.readonlyPaths`); the user's profile, Documents,
  etc. are unreachable. We never touch filesystem ACLs ourselves.
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
   winget install Microsoft.WindowsAppRuntime.2
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

3. **MXC** — nothing to install. The sandbox comes from the
   `Microsoft.Mxc.Sdk` NuGet package, which carries the native `mxc_ffi` runtime
   for win-x64 and win-arm64 and runs in-process (no MXC executable is vendored or
   launched). The ProcessContainer backend needs a current Windows 11 build — see
   MXC's [Windows OS-version support](https://github.com/microsoft/mxc/blob/v1.0.1/docs/backends/process-container/os-version-support.md)
   table. If the host can't run it, the title bar shows *MXC sandbox unavailable*
   and launches fail with the reason MXC reports.

4. **Local Reactor package** — generated widgets reference
   `Microsoft.UI.Reactor 0.0.0-local`, resolved from this repo's `local-nupkgs`
   feed. Run `mur pack-local` if it's missing. Override the feed path with
   `WIDGET_CREATOR_NUPKGS`. The widget's Windows App SDK references
   (`Microsoft.WindowsAppSDK.WinUI` + `.Runtime`) are pinned to the same versions
   this repo builds with — they are generated into the app from
   `Directory.Build.props`, because a widget that pins a different version fails
   its build in `Microsoft.WindowsAppSDK.ComponentReference.targets`.

### The Copilot CLI

`GitHub.Copilot.SDK` downloads its native Copilot CLI at **build** time from the
`github/copilot-cli` releases and verifies it against the release's
`SHA256SUMS.txt`. On a restricted network, use the SDK's own hooks: point
`CopilotCliReleaseBaseUrl` (or the `COPILOT_CLI_DOWNLOAD_BASE_URL` environment
variable) at a mirror, or supply a pre-downloaded binary with
`CopilotCliBinaryPath` (see the repo `README.md` and `Directory.Build.props`).

If Widget Creator was built without a bundled CLI (for example with
`-p:CopilotSkipCliDownload=true`), it resolves one at **run** time instead: set
`COPILOT_CLI_PATH` to a local `copilot.exe`, or install the standalone Copilot CLI
in its default per-user location. It deliberately never searches `PATH`. Note that
`gh auth` only supplies Copilot credentials — it is not itself a runnable
`copilot.exe`.

Type a prompt, click **Generate & Run**. The generated source streams into the
right panel; the build + sandbox log streams below it. The widget window opens
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
  third-party dependencies we don't own (the Copilot SDK / Windows App SDK / MXC
  SDK). This
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
> the native image. `Microsoft.Mxc.Sdk` is itself marked AOT-compatible (it
> P/Invokes its bundled native `mxc_ffi` and uses source-generated JSON), so the
> sandbox runs in-process in the native image too. Generation still shells out to
> the bundled Copilot CLI, and the native publish does not itself re-exercise the
> full generate → build → sandbox pipeline.

## How the sandbox policy works

Each widget's policy is a small JSON document using the field names of the stable
MXC 1.0 containment contract. `MxcPolicy.ToContainerRequest` maps it onto the typed
`Microsoft.Mxc.Sdk.V1.ContainerRequest` (the SDK owns the wire schema version) and
`MxcSandbox` launches it with `MxcContainer.SpawnAsync` on the
`Containment.ProcessContainer` backend, streaming the widget's stdout/stderr into
the log. The default policy:

| Surface | Setting | Effect |
|---|---|---|
| UI / display | `ui.disable = false` | the widget renders a real WinUI window (MXC disables UI by default) |
| Network | `network.egress.default = deny`, `network.ingress.default = deny`, `hostLoopback = deny` | no network; "Internet access" in Permissions sets `egress.default = allow` |
| Filesystem | `filesystem.readonlyPaths = [appDir]` (always added) | MXC grants read+exec to **only** the app's own dir; everything else under the user profile is default-deny |
| Clipboard / input | `ui.clipboard = none`, `ui.injection = false` | no clipboard, no synthetic input |

MXC derives the AppContainer network capabilities from the directional policy:
`egress` allow ⇒ `internetClient`, `ingress.default` allow ("Also allow the local
network") ⇒ `privateNetworkClientServer`. The dialog only offers local-network
access together with internet access: MXC's AppContainer fallback tier rejects
inbound/private-network access while outbound is denied, because that capability
is bidirectional there. Other capabilities can be listed under
`processContainer.capabilities` in the Advanced editor. The policy document is
closed like the MXC contract — unknown fields are rejected — and policies saved by
older Widget Creator builds (the pre-1.0 `wxc-exec` `ContainerConfig` shape) are
migrated automatically when read. A legacy policy that uses settings MXC 1.0
can't express (for example `network.blockedHosts` or `leastPrivilege: true`) is
not migrated — it is reported as invalid and the widget runs with the default
least-privilege policy until you review and save it again.

MXC picks the strongest isolation tier the host supports (BaseContainer, then
AppContainer + BFS, then AppContainer + DACL) and enforces the grants itself. The
app never edits ACLs.

### Containment checks (threat-model C-2)

- **Out-of-band token check** — after launch, the host inspects the access token
  of the contained process (`MxcProcess.Id`). If it is positively running at
  medium+ integrity outside an AppContainer, the widget is killed and the run is
  reported as a sandbox failure (never sent to the repair agent).
- **Strict mode** — set `WIDGET_CREATOR_REQUIRE_BASE_CONTAINER=1` to refuse to
  launch unless `MxcContainer.Probe` confirms the strong **BaseContainer** tier for
  the request, instead of accepting the AppContainer fallback.
- **Runtime integrity** — the MXC runtime ships in the Microsoft-signed
  `Microsoft.Mxc.Sdk` package (author-signed by Microsoft and countersigned by
  nuget.org; NuGet verifies both on restore), so there are no vendored binaries
  or hash pins to maintain. The native runtime version is logged at startup and
  shown in the title bar.

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
    MxcPolicy.cs             ← per-widget policy JSON → Microsoft.Mxc.Sdk ContainerRequest
    MxcSandbox.cs            ← launches the widget via MxcContainer.SpawnAsync
    MxcContainmentVerifier.cs ← tier probe + out-of-band token check (C-2)
    WindowsAppRuntimeCheck.cs ← probes the machine-wide Windows App Runtime
    SessionLog.cs
  Resources/SystemPrompt.txt ← instructs the model to emit one single-file Reactor app
```

## Notes & caveats

- This is a demo wired against the repo's `local-nupkgs` feed — paths are
  host-specific and meant to be cleaned up before any real submission.
- MXC enforcement is backend- and host-dependent: a schema-valid policy can still
  be rejected if the host can't enforce it, and some features (e.g. `deniedPaths`)
  are best-effort on some tiers. See the
  [MXC 1.0 containment policy](https://github.com/microsoft/mxc/blob/v1.0.1/docs/containment-policy/1.0.0/policy.md)
  before relying on a policy for untrusted code.
- A real generation call opens an interactive Copilot CLI auth flow on first use.
