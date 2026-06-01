# BlankReactor — Reactor perf-gate synthetic blank app

`BlankReactor` is a synthetic blank app for measuring Reactor
(Microsoft.UI.Reactor) + WinUI 3 cold-launch cost. It complements the
in-repo `tests/startup_perf/BlankWinUI3` and `tests/startup_perf/BlankRNW`
siblings: all three emit the same `BenchmarkSyntheticApps` ETW regions so
the same WPA analysis resolves across stacks, enabling apples-to-apples
comparison of the Reactor + WinUI 3 stack against the other UI frameworks.

It is **not** a feature demo — see `samples/Reactor.TestApp` for that — and
it is **not** a microbenchmark — see `tests/perf_bench/` for those. It is a
real WinUI 3 MSIX app deliberately kept to a single screen (a TextBlock, a
TextField, and a status line) so the bulk of the measured time is framework
overhead rather than user code.

## What it measures

The app emits self-describing ETW events on the **`BenchmarkSyntheticApps`**
provider so WPA / the perf-gate analyzers can pick the same regions out of
every framework's trace:

| Event           | Maps to                                | Reactor hook                                                     |
| --------------- | -------------------------------------- | ---------------------------------------------------------------- |
| `wWinMainEntry` | WPF `App.Main` entry                   | Before `ReactorApp.Run<>(...)`                                   |
| `WindowLoaded`  | WPF `Window.Loaded`                    | First `Window.Activated`                                         |
| `FirstRender`   | WPF `Window.ContentRendered`           | First `CompositionTarget.Rendered` after activation (post-paint) |
| `FirstIdle`     | WPF `DispatcherPriority.ApplicationIdle` | `DispatcherQueuePriority.Low` enqueue after `FirstRender`      |
| `ProcessStop`   | WPF `App.OnExit`                       | After `ReactorApp.Run<>(...)` returns                            |

Provider: `BenchmarkSyntheticApps`, GUID `FD80D616-E92B-4B2B-9BED-131ADA36A8FD`,
keyword `MICROSOFT_KEYWORD_MEASURES` (bit 46 — `0x0000400000000000`).

The app also computes `FirstFrameMs` and `InteractiveMs` from
`Stopwatch.GetTimestamp()` and displays them in the UI for quick visual
verification.

## Project shape

- `AssemblyName` is **`BlankReactor`** so a perf harness's `ProcessName` filter matches.
- `WindowsPackageType=MSIX` so a perf harness can deploy/install it the standard way.
- `SelfContained=true` so perf-test machines (which may only carry an older
  .NET runtime for sibling baseline apps) don't need a separate .NET 10
  install.
- `WindowsAppSDKSelfContained=false` because the perf-test environment is
  expected to provide `Microsoft.WindowsAppRuntime.2.msix` — the project
  intentionally overrides the repo-root default (`Directory.Build.props`)
  which is `true` for the other Reactor samples.

## Building

```powershell
# Unsigned MSIX — works on a fresh clone, can be installed with
# Add-AppxPackage when Windows is in Developer Mode.
dotnet build samples\apps\blank-reactor\BlankReactor.csproj /p:Platform=x64 -c Release
```

The build emits an unsigned `.msix` at
`samples\apps\blank-reactor\bin\Release\net10.0-windows10.0.22621.0\win-x64\AppPackages\…`.

## Signing for the perf-gate harness

The harness (and most lab installers) expect a signed MSIX whose cert chains
to a machine-trusted root. Two pieces have to line up:

1. The signing cert. Generate one once and import its public part into the
   target machine's `Cert:\LocalMachine\TrustedPeople` store:

   ```powershell
   # Subject must match Package.appxmanifest <Identity Publisher="..."> below.
   New-SelfSignedCertificate -Type Custom -Subject "CN=BlankReactor" `
       -KeyUsage DigitalSignature -FriendlyName "BlankReactor dev cert" `
       -CertStoreLocation "Cert:\CurrentUser\My" `
       -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
   ```

   Export it to a `.pfx` (`Export-PfxCertificate`). Do **not** check the
   `.pfx` into the repo — `*.pfx` is already covered by `.gitignore`.

2. The manifest. `Package.appxmanifest` ships with
   `<Identity Publisher="CN=BlankReactor" .../>` to match the cert subject
   above. If you use a different cert subject, update this attribute.

Then build with signing enabled:

```powershell
dotnet build samples\apps\blank-reactor\BlankReactor.csproj `
    /p:Platform=x64 -c Release `
    /p:AppxPackageSigningEnabled=true `
    /p:PackageCertificateKeyFile=path\to\your.pfx
```

## Verifying the ETW emissions

```powershell
# Start a trace just for our provider, run the app, stop, view in WPA.
wpr -start GeneralProfile -start "BenchmarkSyntheticApps:0x0000400000000000:5"
# … launch BlankReactor and let it idle …
wpr -stop blank-reactor.etl
wpa.exe blank-reactor.etl
```

You should see five `wWinMainEntry` / `WindowLoaded` / `FirstRender` /
`FirstIdle` / `ProcessStop` events in order, all carrying `AppName="blank_reactor"`.
