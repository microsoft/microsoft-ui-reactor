# BlankReactor (MSIX) — Reactor perf-gate synthetic blank app

This is the **MSIX-packaged** variant of the Reactor blank perf app, used
to measure Reactor (Microsoft.UI.Reactor) + WinUI 3 cold-launch cost
through the standard packaged-deploy path. It sits alongside the
**unpackaged** `BlankReactor/` sibling in the same directory — that one
is `WindowsPackageType=None` for fast local iteration; this one builds a
real MSIX so a perf-gate harness can `Add-AppxPackage` it the way a
shipped app would be installed.

Both Reactor variants — together with `BlankWinUI3/` and `BlankRNW/` —
emit the same `BenchmarkSyntheticApps` ETW regions so the same WPA
analysis resolves across stacks and packaging modes. This packaged
variant uses `AppName="blank_reactor_msix"` so side-by-side traces do not
collapse into the unpackaged `blank_reactor` row.

It is **not** a feature demo — see `samples/Reactor.TestApp` for that — and
it is **not** a microbenchmark — see `tests/perf_bench/` for those. It is a
real WinUI 3 MSIX app deliberately kept to a single `TextBlock` (no
`TextBox`, no on-screen metrics readout, no state hooks) so the measured
time is framework + Reactor overhead rather than user code — matching the
unpackaged `BlankReactor/` sibling exactly so the two variants differ only
in deployment shape.

## What it measures

The app emits self-describing ETW events on the **`BenchmarkSyntheticApps`**
provider so WPA / the perf-gate analyzers can pick the same regions out of
every framework's trace:

| Event           | Maps to                                | Reactor hook                                                     |
| --------------- | -------------------------------------- | ---------------------------------------------------------------- |
| `wWinMainEntry` | WPF `App.Main` entry                   | Before `ReactorApp.Run<>(...)`                                   |
| `XamlAppLoaded` | WPF `App.OnLaunched`                   | First `BlankApp.Render()`                                        |
| `WindowLoaded`  | WPF `Window.Loaded`                    | First post-commit `UseEffect`                                    |
| `FirstRender`   | WPF `Window.ContentRendered`           | First `CompositionTarget.Rendering` after commit                 |
| `FirstIdle`     | WPF `DispatcherPriority.ApplicationIdle` | `DispatcherQueuePriority.Low` enqueue after `FirstRender`      |
| `ProcessStop`   | WPF `App.OnExit`                       | After `ReactorApp.Run<>(...)` returns                            |

Provider: `BenchmarkSyntheticApps`, GUID `FD80D616-E92B-4B2B-9BED-131ADA36A8FD`,
keyword `MICROSOFT_KEYWORD_MEASURES` (bit 46 — `0x0000400000000000`).

The app also computes `FirstFrameMs` and `InteractiveMs` from
`Stopwatch.GetTimestamp()` (consumed only by the harness via ETW — they
are intentionally **not** rendered into the UI so the on-screen content
stays a single static `TextBlock`).

## Project shape

- `AssemblyName` is **`BlankReactor`** so a perf harness's `ProcessName` filter matches.
- ETW `AppName` is **`blank_reactor_msix`** so WPA can distinguish this
  package-deployment mode from the unpackaged `blank_reactor` sibling.
- `WindowsPackageType=MSIX` so a perf harness can deploy/install it the standard way.
- `SelfContained=true` so perf-test machines (which may only carry an older
  .NET runtime for sibling baseline apps) don't need a separate .NET 10
  install.
- `WindowsAppSDKSelfContained=false` because the perf-test environment is
  expected to provide `Microsoft.WindowsAppRuntime.2.msix` — the project
  intentionally overrides the repo-root default (`Directory.Build.props`)
  which is `true` for the other Reactor samples.

## Building

The **command line is the supported build flow** for this project. It is
the path CI exercises, and it gives you full, explicit control over the
`Platform` × `Configuration` × `RuntimeIdentifier` × signing matrix that
the MSIX tooling needs. Visual Studio (open `Reactor.slnx` or this csproj
directly) can build and IntelliSense the project, but the `Create App
Packages` wizard and other Publish UIs are **best-effort only** — quirks
in the `.slnx` ↔ wizard handshake mean the commands below are the
reliable way to produce a deployable `.msix`.

### Unsigned MSIX (fastest local repro)

Works on a fresh clone with no cert setup. Install it with
`Add-AppxPackage` when Windows is in Developer Mode.

```powershell
dotnet build tests\startup_perf\BlankReactorMsix\BlankReactor.csproj `
    -p:Platform=x64 `
    -p:RuntimeIdentifier=win-x64 `
    -c Release `
    -p:GenerateAppxPackageOnBuild=true
```

The build emits the `.msix` at:

```
tests\startup_perf\BlankReactorMsix\AppPackages\BlankReactor_<version>_x64_Test\BlankReactor_<version>_x64.msix
```

For ARM64, swap `x64` → `ARM64` for `Platform` and `win-x64` → `win-arm64`
for `RuntimeIdentifier`.

### Signed x64 Release MSIX (perf-gate harness target)

The harness (and most lab installers) expect a signed MSIX whose cert
chains to a machine-trusted root. Two things have to line up before
running the build command:

1. **The signing cert.** Either generate a self-signed one once and
   import its public part into the target machine's
   `Cert:\LocalMachine\TrustedPeople` store:

   ```powershell
   # Subject must match Package.appxmanifest <Identity Publisher="..."> below.
   New-SelfSignedCertificate -Type Custom -Subject "CN=BlankReactor" `
       -KeyUsage DigitalSignature -FriendlyName "BlankReactor dev cert" `
       -CertStoreLocation "Cert:\CurrentUser\My" `
       -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
   ```

   Export to a `.pfx` (`Export-PfxCertificate`), or use a `.pfx` supplied
   by the lab. Do **not** check the `.pfx` into the repo — `*.pfx` is
   already covered by `.gitignore`.

2. **The manifest.** `Package.appxmanifest` ships with
   `<Identity Publisher="CN=BlankReactor" .../>` to match the
   `New-SelfSignedCertificate` subject above. If your `.pfx` has a
   different Subject, update this attribute first — they must match
   exactly or signing fails with `APPX1101` / `APPX0105`. Check the
   `.pfx` Subject with:

   ```powershell
   (Get-PfxCertificate -FilePath path\to\your.pfx).Subject
   ```

Then build with signing enabled:

```powershell
dotnet build tests\startup_perf\BlankReactorMsix\BlankReactor.csproj `
    -p:Platform=x64 `
    -p:RuntimeIdentifier=win-x64 `
    -c Release `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=true `
    -p:PackageCertificateKeyFile="C:\path\to\your.pfx"
```

If the `.pfx` is password-protected add either
`-p:PackageCertificatePassword=<password>` (visible in shell history) or
import the `.pfx` into `Cert:\CurrentUser\My` and pass
`-p:PackageCertificateThumbprint=<thumbprint>` instead.

If a previous build left stale outputs that interfere with signing, nuke
them first:

```powershell
Remove-Item tests\startup_perf\BlankReactorMsix\obj, `
            tests\startup_perf\BlankReactorMsix\bin, `
            tests\startup_perf\BlankReactorMsix\AppPackages `
    -Recurse -Force -ErrorAction SilentlyContinue
```

## Running

`tests\startup_perf\run_startup_bench.ps1` intentionally does **not** run
this variant. That script launches file-path executables and owns the
returned process handle for window detection, working-set sampling, and
teardown. A real MSIX measurement must install the package and launch it
by AUMID; launching the loose build output would either fail activation
or stop measuring the packaged path this variant exists to cover.

After installing the produced MSIX, launch it through your perf-gate
harness or by its Start-menu/AUMID entry, then capture the same
`BenchmarkSyntheticApps` provider used by the default startup suite.

## Verifying the ETW emissions

```powershell
# Start a trace just for our provider, run the app, stop, view in WPA.
wpr -start GeneralProfile -start "BenchmarkSyntheticApps:0x0000400000000000:5"
# ... launch the installed BlankReactor package and let it idle ...
wpr -stop blank-reactor.etl
wpa.exe blank-reactor.etl
```

You should see six `wWinMainEntry` / `XamlAppLoaded` / `WindowLoaded` /
`FirstRender` / `FirstIdle` / `ProcessStop` events in order, all carrying
`AppName="blank_reactor_msix"`.
