# Visual Studio Reactor Preview

Live embedded preview pane for Microsoft.UI.Reactor apps in Visual Studio 2022.

> ⚠️ **Very rough / experimental.** This VSIX is the roughest surface in an already experimental repository. It exists to validate the embedded-preview direction and collect feedback; expect install friction, solution-load races, HWND/DPI quirks, blank-window bugs, and breaking changes while it is hardened.

## Install

Build the full VSIX, double-click it, install into the target Visual Studio hive, then restart Visual Studio:

```powershell
pwsh -File src\vs-reactor\Build-Vsix.ps1
```

This requires Visual Studio 2022 (17.8+) or Visual Studio 2026 (18.x) with the **Visual Studio extension development** workload. Phase 1 may ship unsigned for dev distribution, so Visual Studio may show an unsigned-extension warning. Treat installs as developer smoke builds, not stable product drops.

## Debug

Open the VSIX solution/project and the Reactor repo (`Reactor.slnx`) in Visual Studio, set `Reactor.VsExtension` as the startup project, then press F5. The VS SDK launches the Experimental hive with `/RootSuffix Exp` automatically.

Set `Reactor_VsExtension_DebugBreakOnAttach=1` before launching `devenv.exe` to force a debugger attach during package initialization.

## Settings

None yet. Phase 2 adds a Tools → Options page for Reactor Preview settings.

## Troubleshooting

- **First rule: assume the VSIX is experimental.** If a preview session behaves strangely, restart the preview and inspect the **Reactor Preview** output pane before assuming the app is broken.
- **Tool window shows error: DPI mismatch** — move Visual Studio and the embedded Reactor window to monitors with matching DPI awareness, then reload.
- **Embedded preview window is blank or crashes immediately** — the embedded Reactor app must use WinAppSDK 1.6+ (Windows App SDK). Older versions don't support the cross-process HWND reparenting that the embed protocol relies on. Update the target app's `Microsoft.WindowsAppSDK` PackageReference to 1.6 or newer.
- **"PerMonitorV2" exit on app startup** — the embedded Reactor app must declare PerMonitorV2 DPI awareness. Add `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` to the csproj's `<PropertyGroup>`.
- **Tool window shows error: VS is elevated** — restart Visual Studio non-elevated. Elevated VS can silently drop input to a non-elevated child due to UIPI.
- **Tool window stuck on "Launching…"** — check the **Reactor Preview** output channel for `dotnet watch`, port/token, and handshake errors.
- **Build fails with "VSCT not compiled"** — use desktop MSBuild through `Build-Vsix.ps1`; plain `dotnet build` only creates a structural CI VSIX.
- **Build fails with `NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json`** — the restore could not reach nuget.org. This is the one restore `bootstrap.ps1` does not run itself (it shells out to `Reinstall-Vsix.ps1`), so it has to be told which feed to use. `Build-Vsix.ps1` picks up a package mirror already configured in your user NuGet.Config; if you use a different one, pass it explicitly:

  ```powershell
  pwsh -File src\vs-reactor\Build-Vsix.ps1 -NuGetSource https://your.mirror/nuget/v3/index.json
  # ...or point at a whole config
  pwsh -File src\vs-reactor\Build-Vsix.ps1 -NuGetConfig C:\path\to\NuGet.Config
  ```

  `Reinstall-Vsix.ps1` takes the same two parameters and forwards them; `bootstrap.ps1` passes whichever feed it already resolved for its own restores.

## Architecture

The VSIX targets .NET Framework 4.7.2 and talks to Reactor only over loopback HTTP plus stdout port/token discovery. It does not reference Reactor projects. The embed state machine lives in `Session/EmbedSession.cs`.

## Build commands

```powershell
pwsh -File Build-Vsix.ps1
```

Full VSIX with VSCT/pkgdef for install testing.

```powershell
dotnet build Reactor.VsExtension\Reactor.VsExtension.csproj
```

Structural VSIX for CI validation.

## CI behavior (GitHub vs. OneBranch)

These projects are `Reactor.slnx` members, so a solution-wide build touches them — but they
are `net472` VSSDK projects and the extension ships only as a `.vsix` via
`.github/workflows/release.yml` (never to nuget.org). The internal **OneBranch** pipeline
restores the slnx in a feed-only container that lacks the VSSDK packages and desktop VSIX
tooling, so the three csproj self-disable there using a guard on the Azure-DevOps-only variable
`TF_BUILD`:

- **`TF_BUILD` set (OneBranch)** → inert empty `net10.0` build, no VSSDK restore.
- **`TF_BUILD` unset (GitHub Actions, local)** → full `net472` VSIX build, as above.

Do not remove the `'$(TF_BUILD)' == 'true'` conditions — without them the OneBranch slnx
restore fails (NU1101) on the VSSDK packages. Rationale is also inline in each csproj.

## Test commands

```powershell
dotnet test Tests\Reactor.VsExtension.Tests
```

Tier A pure-logic tests; currently 66 discovered tests.

```powershell
dotnet test Tests\Reactor.VsExtension.SdkTests
```

Tier B in-process SDK tests; partial status and skipped SDK harness gaps are tracked in [TESTING.md](TESTING.md).

## More information

- [Manual smoke checklist](TESTING.md)
- [Visual Studio Embedded Preview spec](../../docs/specs/056-visual-studio-embedded-preview.md)
- User guide template: `../../docs/_pipeline/templates/vs-extension.md.dt`
