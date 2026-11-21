# Toolkit compatibility validation - 2026-11-19

Historical results for [#1603](https://github.com/microsoft/microsoft-ui-reactor/issues/1603)
and [#1504](https://github.com/microsoft/microsoft-ui-reactor/issues/1504).
**Four unpackaged lanes completed with blocked coverage, not full compatibility.**
Later deployment preparation did not execute those deployment scenarios.
Publishing this report does not validate the publication branch or current main.

## Revisions and package provenance

| Phase | Source and runner revision | Scope |
|---|---|---|
| Measured live runs | `7e3d0254b1e56ca3eba463aecdfb41304417a023` | Project-reference and package-consumer JIT/NativeAOT, unpackaged only |
| Headless deployment preparation | `09eeca44e7ee4af5dcb84ede284daee7808e2c3a` | Four unsigned MSIX archives and four loose layouts; none installed, registered or launched |

The measured revision matched live main before each lane. Main advanced to
`1cf3840e3ddcdd8bd15c28bef3ff6b6515014cf6` by the final check. The later
`09eeca44` preparation included #1604, #1605, #1606 and #1608; it is not a rerun of
the four measured lanes. Subsequent commits and unmerged UIA, E2E and text-scaling
work are outside these results.

Both phases used x64 Windows `10.0.26100.0`, .NET SDK `11.0.100`, Windows SDK
projection `10.0.22621.57`, Toolkit controls `8.3.260402-preview2`, Windows App SDK
`2.2.0` and WinUI `2.2.1`. The linked sample mappings had SHA-256
`83ACDAFBBE6F0890CC983398EC0C3136A650196157202841311634788A337F07`.

| Phase | Local Microsoft.UI.Reactor version | Nupkg SHA-256 |
|---|---|---|
| Measured | `0.0.0-compat.7e3d0254b1e5.850c271c` | `BEE57FF800F94D27559AB0A7311FBE5E47525593092B041A8C16B34CAB369505` |
| Preparation only | `0.0.0-compat.09eeca44e7ee.ef904bba` | `76FB465A0A4E67AA9BD20D15FFAAAD255ADF3681B41E65CC05B4BDD749F95A64` |

The measured package's `Reactor.dll` SHA-256 was
`E1BAA9AF09F961144FB001AD355AF25990E39AC5B493C35856AAA512F298A7DB`;
the live untransformed package-JIT output matched it. Twenty-three Toolkit/WinUI
DLL hashes matched between the live project/package JIT outputs. The external
consumer had no ProjectReferences and used its own Directory.Build/Packages
imports. These checks do not equate transformed AOT bytes with JIT assemblies.

## Observed unpackaged results

| Consumer/runtime | Rows | Passed | Failed | Blocked | Inapplicable |
|---|---:|---:|---:|---:|---:|
| Project JIT | 340 | 222 | 0 | 105 | 13 |
| Project NativeAOT | 340 | 222 | 0 | 105 | 13 |
| Package JIT | 340 | 222 | 0 | 105 | 13 |
| Package NativeAOT | 340 | 222 | 0 | 105 | 13 |
| **Total** | **1,360** | **888** | **0** | **420** | **52** |

There were 315 control/scenario keys plus an environment key. The runner starts
one process per control, repeating the environment guard 25 times, so each
complete lane contains 340 rows. All 100 process exits were **2**, with
`captureComplete=true`, `timedOut=false`, matching TAP/JSONL counts and completion
trailers. Each matrix runner exited **1** for blocked coverage; each summarizer
exited **0** for successful report generation, not compatibility success.

All environment guards passed. JIT reported dynamic-code support; all 50 AOT
control processes reported `dynamic-code=False`. All runs reported no package
identity or installed location. Owned consumer processes were closed after each
separately coordinated lane.

The following counts were identical in **each** of the four lanes.

| Control | Passed | Failed | Blocked | Inapplicable |
|---|---:|---:|---:|---:|
| SettingsCard | 10 | 0 | 4 | 1 |
| SettingsExpander | 12 | 0 | 3 | 0 |
| RadialGauge | 9 | 0 | 3 | 1 |
| Segmented | 9 | 0 | 4 | 0 |
| CameraPreview | 1 | 0 | 13 | 0 |
| ColorPicker | 9 | 0 | 4 | 0 |
| ImageCropper | 10 | 0 | 3 | 0 |
| GridSplitter | 9 | 0 | 4 | 0 |
| TokenizingTextBox | 10 | 0 | 4 | 0 |
| RangeSelector | 10 | 0 | 3 | 1 |
| HeaderedContentControl | 9 | 0 | 4 | 0 |
| HeaderedItemsControl | 9 | 0 | 4 | 0 |
| LayoutTransformControl | 8 | 0 | 4 | 1 |
| ContentSizer | 10 | 0 | 4 | 0 |
| PropertySizer | 10 | 0 | 4 | 0 |
| MetadataControl | 9 | 0 | 4 | 0 |
| ConstrainedBox | 7 | 0 | 4 | 2 |
| UniformGrid | 8 | 0 | 4 | 1 |
| DockPanel | 8 | 0 | 4 | 1 |
| StaggeredPanel | 8 | 0 | 4 | 1 |
| WrapPanel | 8 | 0 | 4 | 1 |
| HeaderedTreeView | 9 | 0 | 4 | 0 |
| SwitchPresenter | 8 | 0 | 4 | 2 |
| TabbedCommandBar | 10 | 0 | 4 | 1 |
| RichSuggestBox | 12 | 0 | 4 | 0 |

Per lane, 61 blocked rows were classified `approval` and 44 `not-implemented`.
CameraPreview passed only its environment guard; no camera was opened.
Segmented's missing-selection-provider check remained blocked. The separate
[#1599](https://github.com/microsoft/microsoft-ui-reactor/issues/1599) investigation
did not replace these rows or change the pinned harness's classification.

Each lane produced 96 Light/Dark/LTR/RTL PNGs plus `ImageCropper.crop.png`,
97 nonempty files with hashes, 388 across all four lanes. The crop check verified
160x120 output. These establish capture and scenario assertions, not human visual
review, physical keyboard/pointer behavior or pixel-perfect parity.

## Commands and reproduction boundaries

The commands below show the executed scope with portable evidence-directory
names. Use a separate clean checkout of the **measured revision** to reproduce
that scope, not this report's publication HEAD. A new approved run must record its
own hashes, actual dependencies and results. The historical packages are local
validation packages, not published NuGet releases; repacking generates a new
version and hash rather than reproducing the recorded bytes.

On an approved quiet interactive x64 Windows host with NativeAOT tools available,
use a Visual Studio x64 developer PowerShell. A lane opens windows and performs
programmatic focus/UIA actions and captures. Obtain a coordinated slot for each
invocation; these commands grant no registration, install, camera or OS-setting
permission.

```powershell
if ((git rev-parse HEAD).Trim() -ne '7e3d0254b1e56ca3eba463aecdfb41304417a023') {
    throw 'Use the measured source snapshot or label a new baseline.'
}
if (@(git status --porcelain=v1).Count) { throw 'Use a clean source snapshot.' }
$evidenceRoot = Join-Path $env:TEMP ('toolkit-evidence-' + [Guid]::NewGuid().ToString('N'))
$runner = 'tests\ToolkitCompatibility\Run-Matrix.ps1'
$summary = 'tests\ToolkitCompatibility\Summarize-Matrix.ps1'

pwsh -NoProfile -File $runner -EvidenceDirectory "$evidenceRoot\project-jit" `
    -Runtime jit -Deployment unpackaged -TimeoutSeconds 60
$LASTEXITCODE | Set-Content "$evidenceRoot\project-jit\runner-exit-code.txt"
pwsh -NoProfile -File $summary -EvidenceDirectory "$evidenceRoot\project-jit"

pwsh -NoProfile -File $runner -EvidenceDirectory "$evidenceRoot\project-aot" `
    -Runtime aot -Deployment unpackaged -TimeoutSeconds 60
$LASTEXITCODE | Set-Content "$evidenceRoot\project-aot\runner-exit-code.txt"
pwsh -NoProfile -File $summary -EvidenceDirectory "$evidenceRoot\project-aot"

pwsh -NoProfile -File tests\ToolkitCompatibility\Prepare-PackageConsumer.ps1 `
    -Destination "$evidenceRoot\package-consumer"
$consumer = "$evidenceRoot\package-consumer\consumer\ToolkitCompatibility.csproj"
pwsh -NoProfile -File $runner -EvidenceDirectory "$evidenceRoot\package-jit" `
    -ConsumerProject $consumer -Runtime jit -Deployment unpackaged -TimeoutSeconds 60
$LASTEXITCODE | Set-Content "$evidenceRoot\package-jit\runner-exit-code.txt"
pwsh -NoProfile -File $summary -EvidenceDirectory "$evidenceRoot\package-jit"

pwsh -NoProfile -File $runner -EvidenceDirectory "$evidenceRoot\package-aot" `
    -ConsumerProject $consumer -Runtime aot -Deployment unpackaged -TimeoutSeconds 60
$LASTEXITCODE | Set-Content "$evidenceRoot\package-aot\runner-exit-code.txt"
pwsh -NoProfile -File $summary -EvidenceDirectory "$evidenceRoot\package-aot"
```

In the recorded run, each of the four matrix invocations returned **1** with
complete blocked results, and all four summaries returned **0**. The recipe
records each outer exit before another process overwrites it. Do not treat a
nonzero runner exit as grounds to discard evidence; if preparation fails before
an evidence directory or validated inventory exists, report that failure rather
than inventing result files.

No Filter, NativeBaseline, AllowCamera or CameraProbeOnly option was used.
The [runner](../Run-Matrix.ps1) republishes to new layouts; it does not offer a
skip-publish mode to execute a previously approved archive.

Additional recorded headless checks passed:

| Command/check | Result |
|---|---|
| `pwsh -NoProfile -File tests\ToolkitCompatibility\Test-ResultProtocol.ps1` | Exit 0, including malformed/incomplete result cases |
| `pwsh -NoProfile -File tests\ToolkitCompatibility\Test-RunnerSafety.ps1` | Exit 0; mocked Appx operations and owned console processes only |
| Separately prepared unpackaged outputs at the measured revision with `--list-scenarios`, `--list-camera-probe`, `--protocol-self-test` | Twelve complete exit-0 processes; inventories agreed, nine assertions per protocol self-test |
| Source/package/staged-file/layout hashes and consumer import isolation | Passed at the recorded snapshots |

Those headless checks preceded the runner's republishing. They do not establish
that all three modes ran against the rebuilt live binaries; each live lane
independently obtained and validated its own scenario inventory.

Release publishing retained its warning policy. NU1900 vulnerability-feed access
warnings and MSB3277 framework-reference conflicts were recorded, not suppressed.
Successful builds do not establish a successful vulnerability audit.

## Preparation-only deployment work and remaining gates

At `09eeca44`, `Run-Matrix.ps1 -Deployment msix -PrepareMsixOnly` prepared project
and package JIT/AOT archives. Both runner invocations returned **1** with explicit
approval blockers, stopping before any Appx query/install or consumer launch.
Four loose layouts were separately restored/published headlessly; the normal
runner has no loose prepare-only mode. No packaged binary was launched even for
inventory, so no complete compatibility report was inferred from those builds.

The historical planning ledger distinguished **1,360 executed rows** from
**2,720 not-executed rows** in eight loose/MSIX combinations. Preparing artifacts
at a different revision did not satisfy those rows.

| Gate | Still required |
|---|---|
| Registered loose layouts | Explicit current-user registration and exact-owned-package cleanup approval on an already configured host; no implicit Developer Mode/trust/elevation changes |
| [Installed MSIX, #1598](https://github.com/microsoft/microsoft-ui-reactor/issues/1598) | Exact reviewed archive hashes/identities, administrator approval for unsigned executable **all-users** install and exact-owned-package **all-users** cleanup, installed-location/identity guards and complete live results |
| [Camera, #1600](https://github.com/microsoft/microsoft-ui-reactor/issues/1600) | Explicit device consent and working hardware; live frames, fresh generations and awaited cleanup, not denied/no-device substitutes |
| [OS accessibility, #1602](https://github.com/microsoft/microsoft-ui-reactor/issues/1602) | Approved host/VM and permission to change and restore real OS settings; the measured harness's text-scale check always blocked, and high-contrast capture required the real setting already active |
| [Input, #1601](https://github.com/microsoft/microsoft-ui-reactor/issues/1601) | Independent physical-input evidence; programmatic focus/UIA actions here do not establish it |

Prepared request commands are not authorization. The MSIX runner generates fresh
identities and bytes when rerun; an approval for one archive does not cover an
unreviewed rebuild. Cleanup must retain the exact full name returned for the
uniquely verified run-owned identity, never remove by prefix or wildcard.

## Evidence availability

The narrative handoff is preserved in artifact 1 on
[#1603](https://github.com/microsoft/microsoft-ui-reactor/issues/1603).
Raw TAP/JSONL, process metadata, resolved assets, file/PNG hashes and local
packages/layouts were retained in session evidence, **not committed or published
as downloadable assets with this report**. This document is a source-pinned
summary, not a self-contained raw-evidence archive.

For a new run, retain `run.json`, `commands.txt`, build/dependency inventories,
validated scenario inventory, every control's stdout/stderr and JSONL,
`controls.json`, `matrix.json`, captures and `scenario-matrix.json`/`summary.md`.
The pinned runner's lane-level exitCode was null; actual process exits were in
`controls.json`, and outer runner exits were recorded separately. Missing exits,
timeouts, incomplete streams, absent rows or completion trailers must never
contribute passes. No historical or focused run should fill an unexecuted lane.
