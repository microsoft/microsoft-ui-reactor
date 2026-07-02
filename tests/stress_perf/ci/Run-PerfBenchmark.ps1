<#
.SYNOPSIS
    Build + run the StressPerf WinUI harnesses, capture the four headline perf
    metrics, and (in compare mode) render the sticky PR-comparison comment.

.DESCRIPTION
    Used both locally (developers) and by .github/workflows/perf-compare.yml.

    Two modes, auto-selected:

      * Local / single-tree (no -BaselineRoot): builds and runs the requested
        harness(es) in -Root, prints a results table to the console, and writes
        result.json. Good for "what are my four numbers right now, and how do
        they line up against vanilla WinUI3 and the Rust reference?".

      * Compare (with -BaselineRoot): builds + runs StressPerf.ReactorOptimized
        in BOTH trees, INTERLEAVED on the same machine (main, PR, main, PR, ...)
        to cancel time-correlated drift, plus vanilla WinUI3 once, then renders
        the two-table sticky comment to comment.md for the workflow to post.

    Variance mitigations (we don't own CI runners): same-runner A/B, interleaved
    reps, warm-up discard, median of N with a paired 95%-confidence-interval band
    on the deltas (PerfLib flags a change only when its CI excludes 0), High
    process priority, a pinned workstation/non-concurrent GC, a high-performance
    power plan for the duration, and an opt-in Defender exclusion
    (-DefenderExclude). Runner identity (CPU / cores / RAM) is recorded in the
    comment so absolute numbers are interpreted in context — trust the delta, not
    the absolutes.

    The Rust `windows-reactor` cross-framework column is measured live when
    -RustRepo points at a microsoft/windows-rs checkout (its `test_reactor_perf`
    crate is a port of this harness with the same CLI + report). Without it, the
    Rust column reads n/a.

.PARAMETER Root
    Checkout to build + run (the PR head in compare mode). Defaults to the repo
    root inferred from this script's location.

.PARAMETER BaselineRoot
    Second checkout (the `main` baseline). When set, the script runs in compare
    mode and renders comment.md.

.PARAMETER Percent
    Fraction of grid cells mutated per tick. Methodology default 50.

.PARAMETER Duration
    Measured seconds per run. Methodology default 10.

.PARAMETER Reps
    Measured runs per side whose median is reported and whose per-run samples feed
    the paired 95% CI (default 12 — enough to resolve the ~1-3% deltas these perf
    PRs target; a 2-run median cannot). Lower it only for a quick local smoke run.

.PARAMETER Warmup
    Leading runs discarded before the Reps measured runs (default 2) to absorb
    JIT/tiered-compilation and first-window warm-up.

.PARAMETER RefReps
    Measured runs for the reference-only legs — vanilla WinUI3 (StressPerf.Direct)
    and the Rust column (default 3). These contribute a single reference absolute,
    not a paired CI, so they don't need the full -Reps budget; keeping them small
    bounds total runner time.

.PARAMETER RefWarmup
    Warm-up runs discarded for the reference-only legs (default 1).

.PARAMETER MicroReps
    Measured interleaved rep rounds for the reconciler micro-suite
    (PerfBench.ControlModel, spec-047 M1&ndash;M13). Default 12. Each round runs a
    FRESH main process then a fresh PR process (one inner --reps each), alternating
    per round so the process-to-process timing offset randomizes into the paired
    variance rather than biasing every rep one way; the per-round samples feed the
    paired 95% CI on ns/op and allocated bytes/op.

.PARAMETER MicroWarmup
    Interleaved warm-up rounds discarded before the measured MicroReps rounds
    (default 1). Like the macro -Warmup, the first round per side pays cold-start /
    JIT costs, so it is dropped from the accumulator on both sides.

.PARAMETER MicroIterations
    Inner iterations per micro repetition (default 1000) over which each bench's
    mean ns/op and allocated bytes/op are averaged. The suite runs 17 benches
    (the spec-047 M1&ndash;M14 set plus 3 supplementary) and the heaviest
    (M7&ndash;M9 reconcile a 1000-node tree per op) run >=120 us/op,
    so even 1000 iterations keep each per-rep mean >=120 ms &mdash; hundreds of
    thousands of times the Stopwatch floor &mdash; while letting each per-round
    launch finish inside -MicroRepTimeoutSec. (At 10000 the suite ran ~3x over its
    budget, completed only M1&ndash;M4, and was silently dropped from every comment.)

.PARAMETER MicroRepTimeoutSec
    Per-round launch timeout in seconds for one micro side (default 180). Each
    interleaved round runs the 17-bench suite once (one inner --reps) per side; a
    round that exceeds this is dropped on BOTH sides to keep the rep indices
    aligned. Replaces the old whole-suite timeout that silently truncated the
    suite to M1&ndash;M4.

.PARAMETER IncludeMicro
    Run the reconciler micro-suite on each side and append its per-bench ns/op +
    alloc-bytes/op PR-vs-main table (ns-resolution, WinUI-undiluted) to the
    comment. Default $true; disable with -IncludeMicro:$false.

.PARAMETER IncludeSkipFloor
    Run a second interleaved A/B leg at -SkipFloorPercent and append a low-mutation
    skip-floor table to the comment (compare mode). Default $true; disable with
    -IncludeSkipFloor:$false to halve the macro runtime.

.PARAMETER SkipFloorPercent
    Mutation percent for the skip-floor leg (default 0). At 0 the workload still
    mutates one cell/tick (StockDataSource.Update clamps the count to Math.Max(1, ...)),
    so reconcile/diff isolate the O(n) per-tick child skip-walk floor the 50% leg
    dilutes — the fixed cost a structural-skip optimization targets.

.PARAMETER IncludeKeyedList
    Run a third interleaved A/B leg on StressPerf.KeyedList — a ~500-row stably
    keyed list whose rows are REORDERED / inserted / removed each tick — and append
    its own PR-vs-main table to the comment (compare mode). This drives the child
    reconciler's KEYED arm (ReconcileKeyed → ReconcileKeyedMiddle, the LIS-based
    minimal-move pass) that the positional StocksGrid cells can never reach, so it
    is the sensitive macro measure for keyed-diff optimizations. Default $true;
    build is best-effort (a KeyedList build failure just omits the table). Disable
    with -IncludeKeyedList:$false to skip the extra leg.

.PARAMETER IncludeFlex
    Run a fourth interleaved A/B leg on StressPerf.Flex — a deep nested, fully-realized
    (non-virtualized) flex tree (~2000 leaf cells) whose per-child flex inputs
    (grow / basis / width) are re-rolled on a `--percent` fraction of the leaves each
    tick, forcing a real Yoga measure/layout pass every frame — and append its own
    PR-vs-main table to the comment (compare mode). This exercises the FlexPanel / Yoga
    LAYOUT engine (the Flex/ + Yoga/ subsystems) that the positional StocksGrid and the
    keyed-list legs can never reach, so it is the sensitive macro measure for Yoga/Flex
    layout-engine allocation + memory optimizations. Default $true; build is best-effort
    (a Flex build failure just omits the table). Disable with -IncludeFlex:$false to skip
    the extra leg.

.PARAMETER IncludeDataGrid
    Run a fifth interleaved A/B leg on StressPerf.DataGrid — the real DataGrid control
    (DataGridComponent) over a 30-column × 200-row IObservableDataSource whose cells are
    mutated on a `--percent` fraction each tick, forcing a full DataGridComponent.Render()
    every frame — and append its own PR-vs-main table to the comment (compare mode). This
    exercises the DataGrid control's per-render array/LINQ allocation path (#663/#669) and
    its per-cell/row modifier-delegate churn (#671) that the StocksGrid / keyed-list / flex
    legs never reach, so it is the sensitive macro measure for DataGrid allocation +
    delegate-stability optimizations. Default $true; build is best-effort (a DataGrid build
    failure just omits the table). Disable with -IncludeDataGrid:$false to skip the extra leg.

.PARAMETER IncludeRowMemo
    Run a SINGLE-TREE, best-effort leg that builds + runs PerfBench.RowMemo from the PR
    head ONLY (never the `main` baseline — `main` lacks the opt-in `Memo(key, () => row)`
    API this measures, so building it there would fail) and appends a same-build
    Baseline-vs-Memo table demonstrating the keyed row-memoization win (#327): a realistic
    9-node variable-height row recycled ~1,000,000× through the real ElementFactory.BuildOrCache
    realize path, with vs without `Memo`. The StocksGrid macro legs can't surface this — they
    never opt into the API, so the optimization is dormant there and every metric reads
    within-noise. Default $true; build + run are best-effort (any failure just omits the
    table, leaving the rest of the comment unaffected). Disable with -IncludeRowMemo:$false.

.PARAMETER Apps
    Which harnesses to run in single-tree mode: ReactorOptimized, Direct, KeyedList,
    Flex, DataGrid. Ignored in compare mode (which always does ReactorOptimized both sides +
    Direct once for the WinUI3 column, and — unless -IncludeKeyedList:$false /
    -IncludeFlex:$false / -IncludeDataGrid:$false — KeyedList, Flex and DataGrid both sides).

.PARAMETER OutDir
    Where logs, comment.md and result.json land. Defaults to ci\out next to this
    script.

.PARAMETER SkipBuild
    Reuse existing binaries (skip dotnet build).

.PARAMETER SelfContained
    Build the harness self-contained (WindowsAppSDKSelfContained=true + the
    matching win-x64 / win-arm64 RID) so no machine-wide Windows App SDK runtime
    install is needed. Default $true. Disable with -SelfContained:$false.

.PARAMETER Platform
    Target architecture (x64 or ARM64). Defaults to the host's native
    architecture, so an ARM64 box builds and runs the harness natively instead
    of x64-under-emulation — emulated WinUI composition crashes with a stowed
    exception (0xC000027B). GitHub-hosted runners are x64, so CI builds x64.

.PARAMETER PinAffinity
    Pin each harness to a single CPU core (opt-in; can hurt on busy 2-core
    runners, off by default).

.PARAMETER RustRepo
    Path to a microsoft/windows-rs checkout. When set, the script builds + runs
    its `test_reactor_perf` crate (cargo, release) and fills the Rust
    cross-framework column with a live measurement. Omit to leave it n/a.

.PARAMETER DefenderExclude
    Opt in to a best-effort Microsoft Defender exclusion on -Root for the run
    (restored on exit). Off by default; intended for ephemeral CI runners, not
    developer machines.

.PARAMETER HeadSha
    PR head SHA echoed into the comment footer (compare mode).

.PARAMETER BaseSha
    Baseline (`main`) SHA echoed into the comment footer (compare mode).

.PARAMETER RunUrl
    Workflow run URL linked in the comment footer (compare mode).

.EXAMPLE
    # Local: my four numbers + cross-framework reference
    pwsh tests/stress_perf/ci/Run-PerfBenchmark.ps1

.EXAMPLE
    # Local A/B vs a clean main worktree
    git worktree add ../main origin/main
    pwsh tests/stress_perf/ci/Run-PerfBenchmark.ps1 -BaselineRoot ../main -Reps 3

.EXAMPLE
    # Local A/B + a live Rust column from a windows-rs checkout
    pwsh tests/stress_perf/ci/Run-PerfBenchmark.ps1 -BaselineRoot ../main -RustRepo ../windows-rs
#>
[CmdletBinding()]
param(
    [string]$Root,
    [string]$BaselineRoot = '',
    [double]$Percent = 50,
    [int]$Duration = 10,
    [int]$Reps = 12,
    [int]$Warmup = 2,
    [int]$RefReps = 3,
    [int]$RefWarmup = 1,
    [int]$MicroReps = 12,
    [int]$MicroWarmup = 1,
    [int]$MicroIterations = 2000,
    [int]$MicroRepTimeoutSec = 180,
    [bool]$IncludeMicro = $true,
    [double]$SkipFloorPercent = 0,
    [bool]$IncludeSkipFloor = $true,
    [bool]$IncludeKeyedList = $true,
    [bool]$IncludeFlex = $true,
    [bool]$IncludeDataGrid = $true,
    [bool]$IncludeRowMemo = $true,
    [ValidateSet('ReactorOptimized', 'Direct', 'KeyedList', 'Flex', 'DataGrid')]
    [string[]]$Apps = @('ReactorOptimized', 'Direct'),
    [string]$OutDir,
    [switch]$SkipBuild,
    [bool]$SelfContained = $true,
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = $(if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'ARM64' } else { 'x64' }),
    [switch]$PinAffinity,
    [string]$RustRepo = '',
    [switch]$DefenderExclude,
    [string]$HeadSha = '',
    [string]$BaseSha = '',
    [string]$RunUrl = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'PerfLib.ps1')

if (-not $Root)   { $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path }
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'out' }
$Root = (Resolve-Path $Root).Path
if ($BaselineRoot) { $BaselineRoot = (Resolve-Path $BaselineRoot).Path }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$Compare = [bool]$BaselineRoot
$tfmGuess = 'net10.0-windows10.0.22621.0'

$AppRegistry = @{
    ReactorOptimized = @{ AppName = 'StressPerf.ReactorOptimized'; ProjectRel = 'tests\stress_perf\StressPerf.ReactorOptimized\StressPerf.ReactorOptimized.csproj' }
    Direct           = @{ AppName = 'StressPerf.Direct';           ProjectRel = 'tests\stress_perf\StressPerf.Direct\StressPerf.Direct.csproj' }
    KeyedList        = @{ AppName = 'StressPerf.KeyedList';         ProjectRel = 'tests\stress_perf\StressPerf.KeyedList\StressPerf.KeyedList.csproj' }
    Flex             = @{ AppName = 'StressPerf.Flex';              ProjectRel = 'tests\stress_perf\StressPerf.Flex\StressPerf.Flex.csproj' }
    DataGrid         = @{ AppName = 'StressPerf.DataGrid';          ProjectRel = 'tests\stress_perf\StressPerf.DataGrid\StressPerf.DataGrid.csproj' }
    MicroControlModel = @{ AppName = 'PerfBench.ControlModel';     ProjectRel = 'tests\perf_bench\PerfBench.ControlModel\PerfBench.ControlModel.csproj' }
    RowMemo          = @{ AppName = 'PerfBench.RowMemo';            ProjectRel = 'tests\perf_bench\PerfBench.RowMemo\PerfBench.RowMemo.csproj' }
}

function Write-Log {
    param([string]$Message, [string]$Color = 'Gray')
    $ts = (Get-Date).ToString('HH:mm:ss')
    Write-Host "[$ts] $Message" -ForegroundColor $Color
}

function Get-RunnerInfo {
    $info = [ordered]@{ Cpu = ''; Cores = [Environment]::ProcessorCount; MemoryGB = ''; Runner = $env:RUNNER_NAME }
    try { $info.Cpu = (Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1).Name.Trim() } catch {}
    try { $info.MemoryGB = [math]::Round((Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).TotalPhysicalMemory / 1GB) } catch {}
    return [pscustomobject]$info
}

function Resolve-HarnessExe {
    param([string]$TreeRoot, [hashtable]$AppMeta)
    $projDir = Split-Path (Join-Path $TreeRoot $AppMeta.ProjectRel)
    $binRoot = Join-Path $projDir "bin\$Platform\Release"
    if (-not (Test-Path $binRoot)) { return $null }
    $candidates = @(Get-ChildItem -Path $binRoot -Recurse -Filter ("{0}.exe" -f $AppMeta.AppName) -ErrorAction SilentlyContinue)
    if ($SelfContained) {
        # Prefer the RID-specific (self-contained) output; a stale framework-dependent
        # exe from an earlier build can otherwise win on LastWriteTime and fail to launch.
        $ridDir = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
        $rid = @($candidates | Where-Object { $_.FullName -match "\\$ridDir\\" })
        if ($rid.Count) { $candidates = $rid }
    }
    $exe = $candidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($exe) { return $exe.FullName }
    return $null
}

function Build-Harness {
    param([string]$TreeRoot, [hashtable]$AppMeta)
    $proj = Join-Path $TreeRoot $AppMeta.ProjectRel
    if (-not (Test-Path $proj)) { throw "Project not found: $proj" }
    Write-Log "build $($AppMeta.AppName)  [$TreeRoot]" 'Cyan'

    # Compare mode only: overlay the harness .csproj from the trusted baseline tree
    # over the PR tree's copy for the duration of the build, then restore it. The
    # harness csproj is fixed test scaffolding — the build recipe for the StocksGrid
    # workload, including the PerfCiSelfContained self-contained knob — NOT the code
    # under measurement. The PR's actual perf change lives in src/Reactor/, which the
    # harness still compiles via its relative ProjectReference into the PR tree, so
    # overlaying only the csproj is fair. This guarantees the self-contained build
    # block is present even for PRs opened before the gate landed (whose tree predates
    # that csproj block), so /perf needs no rebase. The overlay is build-scoped: the
    # original csproj is restored in the finally below, so the checkout is never left
    # modified — important for local compare runs against a developer's own worktree,
    # and it keeps repeated runs deterministic. Never runs in local single-tree mode
    # ($BaselineRoot empty) or when building the baseline itself ($TreeRoot -eq
    # $BaselineRoot).
    # $overlayBackup is set only AFTER the backup file is successfully written, and the
    # overlay itself lives inside the try below. That ordering keeps the "checkout is
    # never left modified" guarantee even if a Copy-Item throws mid-overlay: the finally
    # restores from (and removes) the backup whenever it exists, so a failed trusted copy
    # can't orphan a *.perfci-orig file or leave the PR csproj swapped out.
    $overlayBackup = $null
    try {
        if ($BaselineRoot -and ($TreeRoot -ne $BaselineRoot)) {
            $trusted = Join-Path $BaselineRoot $AppMeta.ProjectRel
            if (Test-Path $trusted) {
                $bak = "$proj.perfci-orig"
                Copy-Item -LiteralPath $proj -Destination $bak -Force
                $overlayBackup = $bak
                Copy-Item -LiteralPath $trusted -Destination $proj -Force
                Write-Log "  overlaid trusted csproj (self-contained knob) from baseline" 'DarkGray'
            } else {
                Write-Log "  trusted csproj not found in baseline ($trusted) — using PR tree copy" 'Yellow'
            }
        }

        $log = Join-Path $OutDir ("build-{0}-{1}.log" -f $AppMeta.AppName, ([IO.Path]::GetFileName($TreeRoot)))
        $buildArgs = @($proj, '-c', 'Release', "-p:Platform=$Platform", '--nologo')
        if ($SelfContained) { $buildArgs += '-p:PerfCiSelfContained=true' }
        & dotnet build @buildArgs 2>&1 | Tee-Object -FilePath $log | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host (Get-Content $log -Raw) -ForegroundColor DarkRed
            throw "dotnet build failed for $($AppMeta.AppName) in $TreeRoot (see $log)"
        }
    } finally {
        if ($overlayBackup -and (Test-Path $overlayBackup)) {
            Copy-Item -LiteralPath $overlayBackup -Destination $proj -Force
            Remove-Item -LiteralPath $overlayBackup -Force -ErrorAction SilentlyContinue
            Write-Log "  restored original PR csproj (overlay is build-scoped)" 'DarkGray'
        }
    }
}

function Build-RustHarness {
    <# Build the windows-rs `test_reactor_perf` crate (release) for the Rust column.
       Bounded by -TimeoutSec so a slow/stuck cargo build can never consume the
       whole job or starve the C# comparison (the Rust leg is best-effort). #>
    param([string]$RepoRoot, [int]$TimeoutSec = 1500)
    Write-Log "build Rust test_reactor_perf  [$RepoRoot] (timeout ${TimeoutSec}s)" 'Cyan'
    $log = Join-Path $OutDir 'build-rust.out.log'
    $err = Join-Path $OutDir 'build-rust.err.log'
    $p = Start-Process -FilePath 'cargo' -ArgumentList @('build', '--release', '-p', 'test_reactor_perf') `
        -WorkingDirectory $RepoRoot -PassThru -NoNewWindow `
        -RedirectStandardOutput $log -RedirectStandardError $err
    if (-not $p.WaitForExit($TimeoutSec * 1000)) {
        try { $p.Kill($true) } catch { try { $p.Kill() } catch {} }
        throw "cargo build for test_reactor_perf exceeded ${TimeoutSec}s — aborted"
    }
    if ($p.ExitCode -ne 0) {
        if (Test-Path $err) { Write-Host (Get-Content $err -Raw) -ForegroundColor DarkRed }
        throw "cargo build failed for test_reactor_perf in $RepoRoot (exit=$($p.ExitCode); see $log / $err)"
    }
}

function Resolve-RustExe {
    param([string]$RepoRoot)
    $exe = Join-Path $RepoRoot 'target\release\test_reactor_perf.exe'
    if (Test-Path $exe) { return (Resolve-Path $exe).Path }
    return $null
}

function Stage-RustRuntime {
    <# Stage the Windows App SDK self-contained runtime DLLs next to the Rust
       test_reactor_perf.exe, with hard verification + loud logging (issue #674).

       The crate's build.rs is patched to windows_reactor_setup::as_self_contained(),
       which embeds an app.manifest that declares the WinAppSDK runtime DLLs as SxS
       <file> entries — so the loader requires those DLLs *next to the exe at process
       start*. as_self_contained() also tries to copy them there during `cargo build`,
       but every download/extract/copy step in the upstream helper swallows its error
       (`let _ = ...` / `.ok()`), so a single transient runner hiccup leaves ZERO DLLs
       staged while the build still "Finishes". The exe then dies at load with
       0xC0000135 (STATUS_DLL_NOT_FOUND) and 0-byte stdout/stderr, and the Rust column
       reads n/a. This re-does the staging explicitly and verifies the result.

       Best-effort: any failure here only leaves the Rust column n/a — the C# legs are
       self-contained independently of this and are unaffected. Mirrors the upstream
       layout: nupkg -> (strip leading dir) -> MSIX\win10-<arch>\...2.msix -> extract
       -> copy the root *.dll / *.pri next to the exe. We copy the full runtime DLL set
       (a superset of the crate's runtime.txt allow-list) so every manifest-referenced
       file is present. The embedded manifest also declares Microsoft.Web.WebView2.Core.dll,
       which ships in the SEPARATE Microsoft.Web.WebView2 package (not the runtime MSIX), so we
       stage it too — mirroring upstream windows_reactor_setup::deploy_webview2. #>
    param([string]$ExeDir, [string]$Platform)

    $arch = if ($Platform -match 'arm64') { 'arm64' } else { 'x64' }
    # Idempotency is gated on a positive completion marker that we write ONLY after a
    # full, verified stage (below) — NOT on the presence of a subset of DLLs. The per-file
    # copy loop intentionally swallows errors, so a prior *partial* stage could leave a few
    # runtime DLLs present while others are missing, which still faults the loader at
    # process start; keying the early-return on a DLL subset would wrongly short-circuit
    # that incomplete state and never finish staging. The marker only exists once every
    # manifest-referenced file has been verified next to the exe — and we still re-check the
    # core set when the marker is present, so an external wipe can't leave a stale marker
    # pointing at a now-broken exe.
    $required = @('microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll', 'Microsoft.Web.WebView2.Core.dll')
    $sentinel = 'microsoft.ui.xaml.dll'
    $marker = Join-Path $ExeDir '.perfci-runtime-staged'
    if (Test-Path $marker) {
        if (-not ($required | Where-Object { -not (Test-Path (Join-Path $ExeDir $_)) })) {
            Write-Log "  Rust runtime already staged next to exe (completion marker present)" 'DarkGray'
            return
        }
        # Marker survived but a core DLL was removed (external cleanup / partial dir wipe):
        # the marker is stale and must not short-circuit into a broken exe — drop it and
        # fall through to a full restage.
        Write-Log "  stale Rust runtime marker (core DLL missing) — re-staging" 'Yellow'
        Remove-Item $marker -Force -ErrorAction SilentlyContinue
    }

    try {
        $tar = Join-Path $env:SystemRoot 'System32\tar.exe'
        if (-not (Test-Path $tar)) { Write-Log "  System32\tar.exe not found — cannot stage Rust runtime (#674)" 'Yellow'; return }
        $base = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { $env:TEMP }
        $cache = Join-Path $base 'windows-reactor-setup\temp'

        # 1. Locate the runtime nupkg. Prefer the pinned version ($pkg.$ver.nupkg) when it is
        #    already cached, so runs are reproducible and match the version this script would
        #    otherwise download. Only if the pinned copy is absent but other versions linger in
        #    the shared windows-reactor-setup cache (e.g. local experimentation) fall back to
        #    the highest *version* — never the largest file, which is arbitrary and could stage
        #    a mismatched runtime and reintroduce 0xC0000135. Else download the pinned version.
        $pkg = 'Microsoft.WindowsAppSDK.Runtime'; $ver = '2.1.3'
        $nupkg = $null
        $pinned = Join-Path $cache "$pkg.$ver.nupkg"
        if (Test-Path $pinned) {
            $nupkg = $pinned
        } elseif (Test-Path $cache) {
            $nupkg = Get-ChildItem $cache -Filter "$pkg.*.nupkg" -File -ErrorAction SilentlyContinue |
                Sort-Object @{ Expression = {
                    $p = [version]'0.0'
                    try { [void][version]::TryParse($_.BaseName.Substring($pkg.Length + 1), [ref]$p) } catch {}
                    $p } } -Descending |
                Select-Object -First 1 -ExpandProperty FullName
        }
        if (-not $nupkg) {
            $null = New-Item -ItemType Directory -Force -Path $cache -ErrorAction SilentlyContinue
            $nupkg = $pinned
            $url = "https://www.nuget.org/api/v2/package/$pkg/$ver"
            Write-Log "  staging WinAppSDK runtime for Rust harness: downloading $pkg $ver" 'DarkGray'
            $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
            if (Test-Path $curl) { & $curl -s -L -o $nupkg $url }
            else { Invoke-WebRequest -Uri $url -OutFile $nupkg }
        }
        if (-not $nupkg -or -not (Test-Path $nupkg) -or (Get-Item $nupkg).Length -lt 1MB) {
            # A truncated/partial nupkg (failed download or corrupt cache entry) would be
            # preferred again next run via the pinned-cache fast path above, permanently
            # poisoning the cache and pinning the Rust leg at n/a. Evict it so the next
            # invocation re-downloads (or falls back to another cached version) instead of
            # looping on a bad artifact — the top of the same self-healing chain applied to
            # $msix and $msixOut below.
            if ($nupkg -and (Test-Path $nupkg)) { Remove-Item $nupkg -Force -ErrorAction SilentlyContinue }
            Write-Log "  WinAppSDK runtime nupkg unavailable — Rust column may read n/a (#674)" 'Yellow'
            return
        }

        # 2. Extract the nupkg (strip the leading 'tools/'-style component, matching upstream)
        #    so the MSIX lands at MSIX\win10-<arch>\... Key the extract dir by the selected
        #    nupkg name (version) so a different version selected later cannot reuse a stale
        #    cross-version MSIX payload from a stable path.
        $extract = Join-Path $cache ("perfci-runtime-extract-" + [IO.Path]::GetFileNameWithoutExtension($nupkg))
        $msixDir = Join-Path $extract "MSIX\win10-$arch"
        # Resolve the per-arch framework MSIX. 2.x ships Microsoft.WindowsAppRuntime.2.msix
        # (the '2' is the stable WinAppSDK API-contract major, identical across 2.1.3/2.10/…),
        # so that exact name is the fast path. Only if it is absent — e.g. the non-pinned
        # fallback above selected a cached package from a future major whose framework MSIX is
        # numbered differently (…\Microsoft.WindowsAppRuntime.N.msix) — glob for the framework
        # MSIX by its stable stem so a valid runtime still stages instead of failing outright.
        # The glob is scoped to the 'Microsoft.WindowsAppRuntime.<major>.msix' shape (no embedded
        # dot after the stem) so it can't pick an unrelated package (DDLM/Singleton) that lacks
        # the runtime DLLs.
        $resolveMsix = {
            $exact = Join-Path $msixDir 'Microsoft.WindowsAppRuntime.2.msix'
            if (Test-Path $exact) { return $exact }
            Get-ChildItem -Path $msixDir -Filter 'Microsoft.WindowsAppRuntime.*.msix' -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^Microsoft\.WindowsAppRuntime\.\d+\.msix$' } |
                Select-Object -First 1 -ExpandProperty FullName
        }
        $msix = & $resolveMsix
        if (-not $msix) {
            $null = New-Item -ItemType Directory -Force -Path $extract -ErrorAction SilentlyContinue
            & $tar -xf $nupkg -C $extract --strip-components=1
            $msix = & $resolveMsix
        }
        if (-not $msix -or -not (Test-Path $msix)) {
            Write-Log "  runtime MSIX not found after extract ($msixDir) — Rust column may read n/a (#674)" 'Yellow'
            return
        }

        # 3. Extract the per-arch MSIX (its root holds the runtime DLLs + .pri). We only
        #    reach here when the completion marker is absent, so the cached scratch dir is
        #    untrusted: clear any prior (possibly partial) payload and re-extract fresh, then
        #    require BOTH a clean tar exit and the sentinel before trusting $msixOut as the
        #    completeness source below — a partial/stale extraction must never feed the
        #    verification and write the marker from an incomplete payload.
        $msixOut = Join-Path $extract ".msix_extract-$arch"
        if (Test-Path $msixOut) { Remove-Item $msixOut -Recurse -Force -ErrorAction SilentlyContinue }
        $null = New-Item -ItemType Directory -Force -Path $msixOut -ErrorAction SilentlyContinue
        & $tar -xf $msix -C $msixOut
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $msixOut $sentinel))) {
            # The MSIX itself is corrupt/partial (a truncated nupkg extract can yield a bad
            # .msix that tar -xf still "produces"). Evict the cached $msix so the next call's
            # step 2 — which skips re-extract while $msix exists — is forced to re-extract a
            # fresh MSIX from the nupkg instead of looping forever on the poisoned cache.
            Remove-Item $msix -Force -ErrorAction SilentlyContinue
            Write-Log "  runtime MSIX extraction failed/incomplete — Rust column may read n/a (#674)" 'Yellow'
            return
        }

        # 4. Copy the runtime DLLs + resource indices next to the exe.
        $staged = 0
        Get-ChildItem $msixOut -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in '.dll', '.pri' } |
            ForEach-Object { try { Copy-Item -LiteralPath $_.FullName -Destination $ExeDir -Force; $staged++ } catch {} }

        # 5. Stage the WebView2 Core DLL (issue #674). The embedded app.manifest declares
        #    Microsoft.Web.WebView2.Core.dll as a load-time SxS <file>, but unlike the other
        #    16 manifest files it does NOT ship in the WinAppSDK runtime MSIX — it lives in the
        #    SEPARATE Microsoft.Web.WebView2 package. The runtime copy above can never produce
        #    it, so the loader faults with 0xC0000135 at process start. Mirror upstream
        #    windows_reactor_setup::deploy_webview2 (reactor-setup/src/lib.rs @ the pinned
        #    RUST_REF): stage Microsoft.Web.WebView2 <ver> and copy its
        #    win-<arch>/native_uap/Microsoft.Web.WebView2.Core.dll next to the exe, reusing the
        #    same shared windows-reactor-setup cache + self-healing (evict truncated nupkg /
        #    partial extract) as the runtime above.
        $wpkg = 'Microsoft.Web.WebView2'; $wver = '1.0.4022.49'; $wdll = 'Microsoft.Web.WebView2.Core.dll'
        $wnupkg = Join-Path $cache "$wpkg.$wver.nupkg"
        if ((Test-Path $wnupkg) -and (Get-Item $wnupkg).Length -lt 1MB) {
            Remove-Item $wnupkg -Force -ErrorAction SilentlyContinue
        }
        if (-not (Test-Path $wnupkg)) {
            $wurl = "https://www.nuget.org/api/v2/package/$wpkg/$wver"
            $wcurl = Join-Path $env:SystemRoot 'System32\curl.exe'
            Write-Log "  staging WebView2 Core DLL for Rust harness: downloading $wpkg $wver" 'DarkGray'
            if (Test-Path $wcurl) { & $wcurl -s -L -o $wnupkg $wurl }
            else { Invoke-WebRequest -Uri $wurl -OutFile $wnupkg }
        }
        if ((Test-Path $wnupkg) -and (Get-Item $wnupkg).Length -ge 1MB) {
            $wextract = Join-Path $cache "$wpkg-$wver"
            $wsrc = Join-Path $wextract "win-$arch\native_uap\$wdll"
            $wok = $true
            if (-not (Test-Path $wsrc)) {
                # Clear any prior partial extract, then re-extract fresh (strip the leading
                # package dir, matching upstream stage_pkg's --strip-components=1).
                if (Test-Path $wextract) { Remove-Item $wextract -Recurse -Force -ErrorAction SilentlyContinue }
                $null = New-Item -ItemType Directory -Force -Path $wextract -ErrorAction SilentlyContinue
                & $tar -xf $wnupkg -C $wextract --strip-components=1
                # A truncated nupkg that still cleared the coarse 1MB gate can make tar emit a
                # partial/corrupt Core.dll that Test-Path alone would accept. Mirror the
                # runtime-MSIX guard above: require a clean tar exit before trusting the extract.
                if ($LASTEXITCODE -ne 0) { $wok = $false }
            }
            if ($wok -and (Test-Path $wsrc)) {
                try { Copy-Item -LiteralPath $wsrc -Destination $ExeDir -Force; $staged++ } catch {}
            } else {
                # Either tar failed on a truncated-but->1MB nupkg (partial/corrupt extract) or the
                # package extracted cleanly but genuinely lacks the DLL. Either way evict the nupkg
                # AND clear the partial extract so the next run re-downloads a fresh copy instead of
                # staging a broken Core.dll and writing the completion marker over it — but log the
                # two causes distinctly so a tar failure isn't misread as a missing-from-package.
                Remove-Item $wnupkg -Force -ErrorAction SilentlyContinue
                Remove-Item $wextract -Recurse -Force -ErrorAction SilentlyContinue
                $wwhy = if (-not $wok) { 'extract failed (tar exit nonzero)' } else { 'DLL absent from package' }
                Write-Log "  WebView2 Core DLL not staged ($wwhy) — Rust column may read n/a (#674)" 'Yellow'
            }
        } else {
            if (Test-Path $wnupkg) { Remove-Item $wnupkg -Force -ErrorAction SilentlyContinue }
            Write-Log "  WebView2 package unavailable — Rust column may read n/a (#674)" 'Yellow'
        }

        # Verify completeness against the actual MSIX payload for this package version (not
        # just one sentinel): every runtime file we copied (.dll AND .pri) from the MSIX root
        # must now sit next to the exe, and the required core DLL set must be present — so a
        # swallowed .pri copy failure also blocks the completion marker.
        $srcFiles = @(Get-ChildItem $msixOut -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.dll', '.pri' })
        $notCopied = @($srcFiles | Where-Object { -not (Test-Path (Join-Path $ExeDir $_.Name)) })
        $coreMissing = @($required | Where-Object { -not (Test-Path (Join-Path $ExeDir $_)) })
        if ($notCopied.Count -eq 0 -and $coreMissing.Count -eq 0) {
            # Completion marker: written only now that staging is fully verified, so a later
            # call can trust it and skip re-staging (a partial stage never reaches here).
            Set-Content -LiteralPath $marker -Value (Get-Date -Format o) -ErrorAction SilentlyContinue
            Write-Log "  staged $staged self-contained file(s) next to test_reactor_perf.exe (WinAppSDK runtime + WebView2 Core)" 'Green'
        } else {
            # List exactly what's missing — MSIX payload files by name plus any required core /
            # SxS file (e.g. the WebView2 Core DLL) — so a partial stage is diagnosable from the log.
            $missing = @(@($notCopied | ForEach-Object { $_.Name }) + @($coreMissing) | Where-Object { $_ } | Select-Object -Unique)
            Write-Log "  runtime staging incomplete (copied $staged; missing: $($missing -join ', ')) — Rust column may read n/a (#674)" 'Yellow'
        }
    } catch {
        Write-Log "  Rust runtime staging failed ($($_.Exception.Message)) — Rust column may read n/a (#674)" 'Yellow'
    }
}

function Invoke-RustLeg {
    <# Build + run the windows-rs `test_reactor_perf` crate for the Rust column.
       Best-effort: any failure logs a warning and yields $null (column reads n/a). #>
    if (-not $RustRepo) { return $null }
    if (-not (Test-Path $RustRepo)) { Write-Log "RustRepo '$RustRepo' not found — Rust column n/a" 'Yellow'; return $null }
    try {
        if (-not $SkipBuild) { Build-RustHarness -RepoRoot $RustRepo }
        $rustExe = Resolve-RustExe -RepoRoot $RustRepo
        if (-not $rustExe) { Write-Log "test_reactor_perf.exe not found after build — Rust column n/a" 'Yellow'; return $null }
        Stage-RustRuntime -ExeDir (Split-Path $rustExe) -Platform $Platform
        Write-Log "Rust windows-reactor (test_reactor_perf)" 'Green'
        # The Rust port writes StressPerf.Reactor.report.txt next to its exe and has
        # no --json mode, so run with -NoJson and read the report.
        $rustRuns = Measure-Sequential -Exe $rustExe -AppMeta @{ AppName = 'StressPerf.Reactor' } -Tag 'rust' -NoJson -RepCount $RefReps -WarmupCount $RefWarmup
        if ($rustRuns.Count) { return Measure-PerfRuns -Runs $rustRuns }
        Write-Log "Rust harness produced no metrics — Rust column n/a" 'Yellow'
        return $null
    } catch {
        Write-Log "Rust leg failed ($($_.Exception.Message)) — Rust column n/a" 'Yellow'
        return $null
    }
}

function Invoke-OneRun {
    <# Run the harness once; return a metric object (Read-HarnessMetrics) or $null. #>
    param([string]$Exe, [hashtable]$AppMeta, [int]$Index, [string]$Tag, [switch]$NoJson, [double]$RunPercent = $Percent)

    $exeDir = Split-Path $Exe
    foreach ($ext in 'metrics.json', 'report.txt', 'samples.csv') {
        $p = Join-Path $exeDir ("{0}.{1}" -f $AppMeta.AppName, $ext)
        if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
    }

    $stdout = Join-Path $OutDir ("run-{0}-{1}-{2}.out.log" -f $AppMeta.AppName, $Tag, $Index)
    $stderr = Join-Path $OutDir ("run-{0}-{1}-{2}.err.log" -f $AppMeta.AppName, $Tag, $Index)
    $inv = [System.Globalization.CultureInfo]::InvariantCulture
    # The Rust port has no --json mode; it always writes report.txt. C# harnesses get --json.
    $harnessArgs = @('--headless', '--percent', $RunPercent.ToString($inv), '--duration', $Duration.ToString($inv))
    if (-not $NoJson) { $harnessArgs += '--json' }
    $timeoutSec = $Duration + 90

    Write-Log ("  run [{0} #{1}] {2} --percent {3} --duration {4}" -f $Tag, $Index, $AppMeta.AppName, $RunPercent, $Duration)
    $proc = Start-Process -FilePath $Exe -ArgumentList $harnessArgs -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden
    try {
        $proc.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::High
        if ($PinAffinity) { $proc.ProcessorAffinity = [IntPtr]([int64]1 -shl (($Index - 1) % [Environment]::ProcessorCount)) }
    } catch {}

    if (-not $proc.WaitForExit($timeoutSec * 1000)) {
        Write-Log "  TIMEOUT after ${timeoutSec}s — killing $($AppMeta.AppName)" 'Yellow'
        try { $proc.Kill($true) } catch { try { $proc.Kill() } catch {} }
        Start-Sleep -Seconds 2
    }

    $metrics = Read-HarnessMetrics -Directory $exeDir -AppName $AppMeta.AppName
    if ($metrics.Source -eq 'none') {
        Write-Log "  no metrics for $($AppMeta.AppName) run #$Index (exit=$($proc.ExitCode)). stderr tail:" 'Yellow'
        if (Test-Path $stderr) { Get-Content $stderr -Tail 8 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkYellow } }
        return $null
    }
    Write-Log ("  -> renders/sec={0}  reconcile={1}  diff={2}  mem={3}  ({4})" -f `
        (Format-PerfNumber $metrics.RendersPerSec 2), (Format-PerfNumber $metrics.AvgReconcileMs 1), `
        (Format-PerfNumber $metrics.AvgDiffMs 1), (Format-PerfNumber $metrics.AvgMemoryMB 1), $metrics.Source) 'DarkGray'
    return $metrics
}

function Measure-Sequential {
    # RepCount/WarmupCount default to the script-level paired-A/B budget but are
    # overridable so the reference-only legs (vanilla WinUI3, Rust) can run a small
    # fixed number of reps — they contribute a single reference absolute, not a
    # paired CI, so spending the full -Reps on them is wasted runner time.
    param(
        [string]$Exe, [hashtable]$AppMeta, [string]$Tag, [switch]$NoJson,
        [int]$RepCount = $Reps, [int]$WarmupCount = $Warmup
    )
    $runs = @()
    for ($i = 1; $i -le ($WarmupCount + $RepCount); $i++) {
        $m = Invoke-OneRun -Exe $Exe -AppMeta $AppMeta -Index $i -Tag $Tag -NoJson:$NoJson
        if ($i -le $WarmupCount) { Write-Log "  (warmup #$i discarded)" 'DarkGray'; continue }
        if ($m) { $runs += $m }
    }
    return , $runs
}

function Invoke-MicroRun {
    <# Run PerfBench.ControlModel once for the production Reactor variant and return the
       results .jsonl path (or $null if it produced nothing). The measured region inside
       BenchRunner is bracketed by per-thread alloc + GC counters, so unlike the macro
       StressPerf legs this is ns-resolution and free of WinUI render / working-set
       dilution. This is the single-launch primitive: Invoke-MicroInterleaved drives it
       once per round per side with -RepCount 1 so each round is a FRESH process, which
       randomizes the process-to-process timing offset (ASLR/layout/scheduling) into the
       paired variance instead of biasing every rep one direction. That per-rep interleave
       is what lets the ns/op paired CI be trusted (see PerfLib Get-PerfMicroRowStatus);
       allocated bytes/op was already deterministic and flagged. No single-core pin: the
       within-process reps are GC-bracketed + warmed, and pinning would only contend the
       app's dispatcher/render threads. #>
    param([string]$Exe, [string]$Tag, [int]$RepCount, [int]$IterCount, [int]$TimeoutSec = 600)

    $outJson = Join-Path $OutDir ("micro-{0}.jsonl" -f $Tag)
    if (Test-Path $outJson) { Remove-Item $outJson -Force -ErrorAction SilentlyContinue }
    $stdout = Join-Path $OutDir ("micro-{0}.out.log" -f $Tag)
    $stderr = Join-Path $OutDir ("micro-{0}.err.log" -f $Tag)
    $inv = [System.Globalization.CultureInfo]::InvariantCulture
    $microArgs = @('--variant', 'Reactor', '--reps', $RepCount.ToString($inv),
        '--iterations', $IterCount.ToString($inv), '--out', $outJson, '--headless')
    # Per-launch budget. When the interleaver calls this with -RepCount 1 the launch runs
    # the 17-bench suite once (each bench still does its own internal warmup + one timed
    # rep), so the default 180s the caller passes is sized with wide headroom over the
    # tens of seconds a single round needs at 2000 iters, while a genuine hang is still
    # bounded. (At 420s with 10000 iterations the whole-suite single launch timed out
    # after only M1-M4, so the micro section was silently absent from every comment —
    # hence the iter cut + the per-rep launches.)
    $timeoutSec = $TimeoutSec

    Write-Log ("  micro [{0}] PerfBench.ControlModel --variant Reactor --reps {1} --iterations {2}" -f $Tag, $RepCount, $IterCount)
    $proc = Start-Process -FilePath $Exe -ArgumentList $microArgs -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden
    try { $proc.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::High } catch {}

    if (-not $proc.WaitForExit($timeoutSec * 1000)) {
        # The bench writes results.jsonl incrementally (one line per rep, flushed
        # between benches), so a killed run leaves a TRUNCATED file — a prefix subset
        # of benches/reps. Treat a timeout as "no results" rather than comparing a
        # silent subset: omit the micro section instead of reporting misleading data.
        Write-Log "  micro TIMEOUT after ${timeoutSec}s — killing PerfBench.ControlModel ($Tag); discarding any partial output" 'Yellow'
        try { $proc.Kill($true) } catch { try { $proc.Kill() } catch {} }
        Start-Sleep -Seconds 2
        return $null
    }

    if (-not (Test-Path $outJson) -or (Get-Item $outJson).Length -eq 0) {
        Write-Log "  micro produced no results for '$Tag' (exit=$($proc.ExitCode)). stderr tail:" 'Yellow'
        if (Test-Path $stderr) { Get-Content $stderr -Tail 8 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkYellow } }
        return $null
    }
    # A clean run exits 0 (per-bench exceptions are caught and written as a filtered
    # status:"error" row, so they don't fail the process). A non-zero exit means the
    # harness crashed — and because the file is written incrementally, whatever is on
    # disk is a truncated prefix. Discard it rather than compare a silent subset.
    if ($proc.ExitCode -ne 0) {
        Write-Log "  micro exited non-zero ($($proc.ExitCode)) for '$Tag' — discarding possibly-truncated output" 'Yellow'
        if (Test-Path $stderr) { Get-Content $stderr -Tail 8 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkYellow } }
        return $null
    }
    return $outJson
}

function Invoke-RowMemoRun {
    <# Run PerfBench.RowMemo once (a single self-contained PR-head build) and return the
       parsed Baseline-vs-Memo result (Read-RowMemoResults) or $null. The bench runs the
       measurement headlessly IN-PROCESS — no window, no main-vs-PR interleave: it internally
       A/Bs a 9-node row realized through ElementFactory.BuildOrCache with vs without
       Memo(i, () => row), then writes stable key=value lines to the --out path, which we
       parse. Best-effort: a timeout / nonzero exit / missing or unparseable output yields
       $null so the caller omits the row-memo table. Mirrors Invoke-MicroRun's launch shape
       (High priority, redirected std streams, bounded wait). #>
    param([string]$Exe, [int]$TimeoutSec = 180)

    $outKv = Join-Path $OutDir 'rowmemo.kv.txt'
    if (Test-Path $outKv) { Remove-Item $outKv -Force -ErrorAction SilentlyContinue }
    $stdout = Join-Path $OutDir 'rowmemo.out.log'
    $stderr = Join-Path $OutDir 'rowmemo.err.log'
    $rmArgs = @('--headless', '--out', $outKv)

    Write-Log "  row-memo PerfBench.RowMemo --out rowmemo.kv.txt"
    $proc = Start-Process -FilePath $Exe -ArgumentList $rmArgs -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden
    try { $proc.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::High } catch {}

    # On any failure, dump BOTH the stderr and stdout tails. PerfBench.RowMemo prints the
    # machine-parseable key=value block to stdout as well as to --out, so when the --out FILE
    # write is what failed (parse returned $null) the stdout tail still shows the block — that
    # is the difference between "the bench crashed" and "the bench ran fine but couldn't write
    # the file", which stderr alone can't distinguish.
    $dumpTails = {
        if (Test-Path $stderr) {
            $et = @(Get-Content $stderr -Tail 8)
            if ($et.Count) { Write-Host "      stderr tail:" -ForegroundColor DarkYellow; $et | ForEach-Object { Write-Host "        $_" -ForegroundColor DarkYellow } }
        }
        if (Test-Path $stdout) {
            $ot = @(Get-Content $stdout -Tail 12)
            if ($ot.Count) { Write-Host "      stdout tail (PerfBench.RowMemo echoes the key=value block here):" -ForegroundColor DarkYellow; $ot | ForEach-Object { Write-Host "        $_" -ForegroundColor DarkYellow } }
        }
    }

    if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
        Write-Log "  row-memo TIMEOUT after ${TimeoutSec}s — killing PerfBench.RowMemo" 'Yellow'
        try { $proc.Kill($true) } catch { try { $proc.Kill() } catch {} }
        Start-Sleep -Seconds 2
        & $dumpTails
        return $null
    }
    if ($proc.ExitCode -ne 0) {
        Write-Log "  row-memo exited non-zero ($($proc.ExitCode)). stderr + stdout tails:" 'Yellow'
        & $dumpTails
        return $null
    }
    $parsed = Read-RowMemoResults -Path $outKv
    if ($null -eq $parsed) {
        Write-Log "  row-memo produced no parseable key=value output (exit=$($proc.ExitCode)) — the --out file was missing/partial. stderr + stdout tails:" 'Yellow'
        & $dumpTails
        return $null
    }
    Write-Log ("  -> baseline {0} ns / {1} B; memo {2} ns / {3} B; memo_rebuilds={4} can_skip={5}" -f `
        (Format-PerfNumber $parsed.BaselineNs 1), (Format-PerfNumber $parsed.BaselineBytes 0), `
        (Format-PerfNumber $parsed.MemoNs 1), (Format-PerfNumber $parsed.MemoBytes 0), `
            $parsed.MemoRebuilds, $parsed.MemoCanSkip) 'DarkGray'
    return $parsed
}

function ConvertTo-MicroRepLines {
    <# Read one per-round micro .jsonl (produced by a single --reps 1 launch, so every
       line carries "repetition":0) and renumber its repetition to $RepIndex, returning
       the rewritten line array — or $null if the file is missing/empty so the caller can
       drop BOTH sides of the round and keep the paired indices aligned. The accumulated
       per-side file must look byte-compatible with a single multi-rep launch because
       Get-PerfMicroComparison pairs main<->pr BY repetition value (not file position), so
       both sides have to carry the SAME dense indices 0,1,2,... The serializer writes the
       repetition field compact + camelCase ("repetition":0, no space — MeasurementResult
       uses WriteIndented=false), and -RepCount 1 means the value is always the literal 0,
       so the surgical literal replace can never collide with another numeric field. #>
    param([string]$RoundFile, [int]$RepIndex)
    if (-not $RoundFile -or -not (Test-Path $RoundFile)) { return $null }
    $lines = @(Get-Content -LiteralPath $RoundFile | Where-Object { $_.Trim() })
    if ($lines.Count -eq 0) { return $null }
    $inv = [System.Globalization.CultureInfo]::InvariantCulture
    $repl = '"repetition":' + $RepIndex.ToString($inv)
    return @($lines | ForEach-Object { $_ -replace '"repetition":0', $repl })
}

function Invoke-MicroInterleaved {
    <# Per-rep interleave of the reconciler micro-suite. Each round launches a FRESH
       main-side then pr-side process (via $LaunchRep, a (Side,Round)->jsonl-path-or-$null
       scriptblock so the loop is unit-testable without the exe) so the process-to-process
       timing offset that otherwise makes the ns paired CI exclude 0 for identical code is
       randomized round-to-round into the paired variance instead of biasing every rep the
       same direction. Mirrors the macro legs' warmup-drop + drop-both alignment: warmup
       rounds are discarded, and a round is kept only if BOTH sides produced output — the
       surviving rounds are appended to the per-side accumulators with contiguous dense
       repetition indices (0,1,2,...) on both sides. Best-effort per side: a launcher that
       THROWS (a transient Start-Process / runner hiccup) is caught and treated like a
       $null return, so only that round is dropped and the surviving reps still produce a
       micro section instead of the exception aborting the whole leg. Returns @{ MainJson; PrJson; Reps } or
       $null if fewer than 2 paired rounds survive (too few for a paired CI). #>
    param(
        [scriptblock]$LaunchRep,
        [int]$RepCount,
        [int]$WarmupCount,
        [string]$MainOut,
        [string]$PrOut
    )
    foreach ($f in @($MainOut, $PrOut)) {
        if (Test-Path $f) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    }
    $kept = 0
    for ($round = 1; $round -le ($WarmupCount + $RepCount); $round++) {
        # Best-effort per side: a launcher that THROWS (e.g. a transient Start-Process
        # failure) is treated like a $null return — only this round is dropped (drop-both
        # keeps the paired indices aligned) instead of the exception aborting the whole
        # micro leg and discarding every surviving rep.
        $mainRound = $null; $prRound = $null
        try { $mainRound = & $LaunchRep 'main' $round } catch { Write-Log ("  micro round #{0} main launch threw ({1}) — dropping the round" -f $round, $_) 'Yellow' }
        try { $prRound = & $LaunchRep 'pr' $round } catch { Write-Log ("  micro round #{0} pr launch threw ({1}) — dropping the round" -f $round, $_) 'Yellow' }
        if ($round -le $WarmupCount) {
            Write-Log ("  (micro warmup round #{0} discarded)" -f $round) 'DarkGray'
            continue
        }
        if ($mainRound -and $prRound) {
            # Validate BOTH sides BEFORE appending either, so a one-sided empty file drops
            # the whole round and never leaves the accumulators at mismatched rep counts.
            $mLines = ConvertTo-MicroRepLines -RoundFile $mainRound -RepIndex $kept
            $pLines = ConvertTo-MicroRepLines -RoundFile $prRound -RepIndex $kept
            if ($mLines -and $pLines) {
                Add-Content -LiteralPath $MainOut -Value $mLines -Encoding UTF8
                Add-Content -LiteralPath $PrOut -Value $pLines -Encoding UTF8
                $kept++
            } else {
                Write-Log ("  micro round #{0} produced empty output (main={1} pr={2}) — dropped" -f $round, [bool]$mLines, [bool]$pLines) 'Yellow'
            }
        } elseif ($mainRound -or $prRound) {
            Write-Log ("  micro round #{0} incomplete (main={1} pr={2}) — dropped to keep the paired CI aligned" -f $round, [bool]$mainRound, [bool]$prRound) 'Yellow'
        }
    }
    if ($kept -lt 2) {
        Write-Log ("  micro interleave kept only {0} paired round(s) — too few for a paired CI; omitting micro section" -f $kept) 'Yellow'
        return $null
    }
    return @{ MainJson = $MainOut; PrJson = $PrOut; Reps = $kept }
}

# ── Power plan + Defender (best-effort, restored on exit) ────────────────────
$prevScheme = $null
try {
    $active = (& powercfg /getactivescheme) 2>$null
    if ($active -match '([0-9a-fA-F-]{36})') { $prevScheme = $Matches[1] }
    & powercfg /setactive SCHEME_MIN 2>$null | Out-Null   # High performance
    Write-Log "power plan -> High performance (was $prevScheme)" 'DarkGray'
} catch { Write-Log "power plan unchanged ($_)" 'DarkGray' }

# ── GC mode pinning (restored on exit) ───────────────────────────────────────
# Pin a deterministic GC for every harness child process so run-to-run variance
# is governed by the workload, not by background-GC scheduling, and so main and
# PR are compared under identical runtime settings. Workstation + non-concurrent
# is the low-variance choice for this single-window render loop; env vars override
# each harness' runtimeconfig and are inherited by Start-Process. Captured and
# restored in the finally below so we never leak runtime state to later steps.
$prevGcServer     = $env:DOTNET_gcServer
$prevGcConcurrent = $env:DOTNET_gcConcurrent
$env:DOTNET_gcServer     = '0'
$env:DOTNET_gcConcurrent = '0'
Write-Log "GC pinned -> workstation, non-concurrent (DOTNET_gcServer=0 DOTNET_gcConcurrent=0)" 'DarkGray'

if ($DefenderExclude) {
    try { Add-MpPreference -ExclusionPath $Root -ErrorAction Stop; Write-Log "Defender exclusion added for $Root" 'DarkGray' }
    catch { Write-Log "Defender exclusion skipped ($_)" 'DarkGray' }
}

$runner = Get-RunnerInfo
Write-Log ("runner: {0} | {1} cores | {2} GB | {3}" -f $runner.Cpu, $runner.Cores, $runner.MemoryGB, ($runner.Runner ?? 'local')) 'Cyan'
$modeSuffix = if ($Compare) {
    # COMPARE mode runs the interleaved A/B legs, so the skip-floor / keyed-list
    # opt-out switches are what actually decide which legs run.
    "skip-floor={0} | keyed-list={1} | flex={2} | datagrid={3} | row-memo={4}" -f `
        $(if ($IncludeSkipFloor) { "on (--percent $SkipFloorPercent)" } else { 'off' }), `
        $(if ($IncludeKeyedList) { 'on' } else { 'off' }), `
        $(if ($IncludeFlex) { 'on' } else { 'off' }), `
        $(if ($IncludeDataGrid) { 'on' } else { 'off' }), `
        $(if ($IncludeRowMemo) { 'on (PR head only)' } else { 'off' })
} else {
    # LOCAL mode ignores the interleaved-leg switches entirely; the workload set is
    # whatever -Apps selects, so report that instead of a misleading on/off.
    "apps={0}" -f ($Apps -join ',')
}
Write-Log ("mode: {0} | platform={1} | percent={2} duration={3} reps={4} warmup={5} | {6}" -f ($(if ($Compare) { 'COMPARE' } else { 'LOCAL' })), $Platform, $Percent, $Duration, $Reps, $Warmup, $modeSuffix) 'Cyan'

$exit = 0
try {
    if ($Compare) {
        # ---- Compare mode: interleaved ReactorOptimized A/B + WinUI3 once -----
        $ro = $AppRegistry.ReactorOptimized
        $direct = $AppRegistry.Direct
        $keyed = $AppRegistry.KeyedList
        $flex = $AppRegistry.Flex
        $datagrid = $AppRegistry.DataGrid
        $microMeta = $AppRegistry.MicroControlModel
        $rowMemoMeta = $AppRegistry.RowMemo

        if (-not $SkipBuild) {
            Build-Harness -TreeRoot $BaselineRoot -AppMeta $ro
            Build-Harness -TreeRoot $Root -AppMeta $ro
            Build-Harness -TreeRoot $Root -AppMeta $direct
        }
        if ($IncludeMicro -and -not $SkipBuild) {
            try {
                Build-Harness -TreeRoot $BaselineRoot -AppMeta $microMeta
                Build-Harness -TreeRoot $Root -AppMeta $microMeta
            } catch {
                Write-Log "reconciler micro-suite build failed ($_) — omitting micro-benchmarks" 'Yellow'
                $IncludeMicro = $false
            }
        }
        if ($IncludeKeyedList -and -not $SkipBuild) {
            try {
                Build-Harness -TreeRoot $BaselineRoot -AppMeta $keyed
                Build-Harness -TreeRoot $Root -AppMeta $keyed
            } catch {
                Write-Log "keyed-list workload build failed ($_) — omitting the keyed-list table" 'Yellow'
                $IncludeKeyedList = $false
            }
        }
        if ($IncludeFlex -and -not $SkipBuild) {
            try {
                Build-Harness -TreeRoot $BaselineRoot -AppMeta $flex
                Build-Harness -TreeRoot $Root -AppMeta $flex
            } catch {
                Write-Log "flex workload build failed ($_) — omitting the flex table" 'Yellow'
                $IncludeFlex = $false
            }
        }
        if ($IncludeDataGrid -and -not $SkipBuild) {
            try {
                Build-Harness -TreeRoot $BaselineRoot -AppMeta $datagrid
                Build-Harness -TreeRoot $Root -AppMeta $datagrid
            } catch {
                Write-Log "datagrid workload build failed ($_) — omitting the datagrid table" 'Yellow'
                $IncludeDataGrid = $false
            }
        }
        if ($IncludeRowMemo -and -not $SkipBuild) {
            try {
                # SINGLE-TREE: build the row-memo bench from the PR head ONLY. It calls the
                # opt-in Memo(key, () => row) API (#327) that exists on the PR but NOT on the
                # `main` baseline, so building it against $BaselineRoot would fail the compile —
                # which is exactly why this leg is single-tree. Best-effort: a build failure
                # just omits the row-memo table and leaves the rest of the comment intact.
                Build-Harness -TreeRoot $Root -AppMeta $rowMemoMeta
            } catch {
                Write-Log "row-memo bench build failed ($_) — omitting the row-memo table" 'Yellow'
                $IncludeRowMemo = $false
            }
        }
        $mainExe = Resolve-HarnessExe -TreeRoot $BaselineRoot -AppMeta $ro
        $prExe = Resolve-HarnessExe -TreeRoot $Root -AppMeta $ro
        $directExe = Resolve-HarnessExe -TreeRoot $Root -AppMeta $direct
        if (-not $mainExe) { throw "main ReactorOptimized exe not found under $BaselineRoot" }
        if (-not $prExe) { throw "PR ReactorOptimized exe not found under $Root" }

        Write-Log "interleaving main/PR ReactorOptimized ($($Warmup) warmup + $($Reps) measured each)" 'Green'
        $mainRuns = @(); $prRuns = @()
        for ($i = 1; $i -le ($Warmup + $Reps); $i++) {
            $mm = Invoke-OneRun -Exe $mainExe -AppMeta $ro -Index $i -Tag 'main'
            $pm = Invoke-OneRun -Exe $prExe -AppMeta $ro -Index $i -Tag 'pr'
            if ($i -le $Warmup) { Write-Log "  (warmup pair #$i discarded)" 'DarkGray'; continue }
            # Keep the A/B pair index-aligned: the paired CI zips main[i] vs pr[i], so a
            # one-sided failure must drop BOTH halves of the pair, not shift later runs.
            if ($mm -and $pm) { $mainRuns += $mm; $prRuns += $pm }
            elseif ($mm -or $pm) { Write-Log "  pair #$i incomplete (main=$([bool]$mm) pr=$([bool]$pm)) — dropped to keep the paired CI aligned" 'Yellow' }
        }

        # Second interleaved A/B leg at near-zero mutation. Same two exes, same paired
        # interleaving, but at $SkipFloorPercent so reconcile/diff isolate the O(n)
        # positional child skip-walk floor the 50% leg dilutes. Each side's delta is
        # internally interleaved (main-floor vs pr-floor pair-by-pair), so it controls
        # for drift the same way the headline leg does.
        $mainFloorRuns = @(); $prFloorRuns = @()
        if ($IncludeSkipFloor) {
            Write-Log "interleaving main/PR skip-floor (--percent $SkipFloorPercent; $($Warmup) warmup + $($Reps) measured each)" 'Green'
            for ($i = 1; $i -le ($Warmup + $Reps); $i++) {
                $mm = Invoke-OneRun -Exe $mainExe -AppMeta $ro -Index $i -Tag 'main-floor' -RunPercent $SkipFloorPercent
                $pm = Invoke-OneRun -Exe $prExe -AppMeta $ro -Index $i -Tag 'pr-floor' -RunPercent $SkipFloorPercent
                if ($i -le $Warmup) { Write-Log "  (floor warmup pair #$i discarded)" 'DarkGray'; continue }
                if ($mm -and $pm) { $mainFloorRuns += $mm; $prFloorRuns += $pm }
                elseif ($mm -or $pm) { Write-Log "  floor pair #$i incomplete (main=$([bool]$mm) pr=$([bool]$pm)) — dropped to keep the paired CI aligned" 'Yellow' }
            }
            if ($mainFloorRuns.Count -lt $Reps -or $prFloorRuns.Count -lt $Reps) {
                Write-Log "  skip-floor leg short (main $($mainFloorRuns.Count)/$Reps, PR $($prFloorRuns.Count)/$Reps) — its paired CI uses fewer samples" 'Yellow'
            }
        }

        # Third interleaved A/B leg: the keyed-list workload. StressPerf.KeyedList
        # renders a ~500-row stably KEYED list and reorders/inserts/removes rows each
        # tick, driving the child reconciler's keyed arm (ReconcileKeyed →
        # ReconcileKeyedMiddle, the LIS minimal-move pass) that StocksGrid's positional
        # cells never reach. Same paired interleaving + drop-both alignment as above.
        # Best-effort: if either exe is missing (build omitted/failed) the leg is
        # skipped and the keyed-list table is omitted — the StocksGrid comparison is
        # unaffected.
        $mainKeyedRuns = @(); $prKeyedRuns = @()
        if ($IncludeKeyedList) {
            $mainKeyedExe = Resolve-HarnessExe -TreeRoot $BaselineRoot -AppMeta $keyed
            $prKeyedExe   = Resolve-HarnessExe -TreeRoot $Root -AppMeta $keyed
            if (-not $mainKeyedExe -or -not $prKeyedExe) {
                Write-Log "keyed-list exe not found (main=$([bool]$mainKeyedExe) pr=$([bool]$prKeyedExe)) — omitting the keyed-list table" 'Yellow'
            } else {
                Write-Log "interleaving main/PR keyed-list (--percent $Percent; $($Warmup) warmup + $($Reps) measured each)" 'Green'
                for ($i = 1; $i -le ($Warmup + $Reps); $i++) {
                    $mm = Invoke-OneRun -Exe $mainKeyedExe -AppMeta $keyed -Index $i -Tag 'main-keyed'
                    $pm = Invoke-OneRun -Exe $prKeyedExe -AppMeta $keyed -Index $i -Tag 'pr-keyed'
                    if ($i -le $Warmup) { Write-Log "  (keyed warmup pair #$i discarded)" 'DarkGray'; continue }
                    if ($mm -and $pm) { $mainKeyedRuns += $mm; $prKeyedRuns += $pm }
                    elseif ($mm -or $pm) { Write-Log "  keyed pair #$i incomplete (main=$([bool]$mm) pr=$([bool]$pm)) — dropped to keep the paired CI aligned" 'Yellow' }
                }
                if ($mainKeyedRuns.Count -lt $Reps -or $prKeyedRuns.Count -lt $Reps) {
                    Write-Log "  keyed-list leg short (main $($mainKeyedRuns.Count)/$Reps, PR $($prKeyedRuns.Count)/$Reps) — its paired CI uses fewer samples" 'Yellow'
                }
            }
        }

        # Fourth interleaved A/B leg: the flex workload. StressPerf.Flex renders a deep
        # nested, fully-realized (non-virtualized) flex tree (~2000 leaf cells) and
        # re-rolls the per-child flex inputs (grow / basis / width) on a --percent
        # fraction of the leaves each tick, forcing a real Yoga measure/layout pass every
        # frame — the FlexPanel / Yoga LAYOUT engine that StocksGrid's positional cells
        # and the keyed-list reorder leg never reach. Same paired interleaving +
        # drop-both alignment as above. Best-effort: if either exe is missing
        # (build omitted/failed) the leg is skipped and the flex table is omitted — the
        # StocksGrid comparison is unaffected.
        $mainFlexRuns = @(); $prFlexRuns = @()
        if ($IncludeFlex) {
            $mainFlexExe = Resolve-HarnessExe -TreeRoot $BaselineRoot -AppMeta $flex
            $prFlexExe   = Resolve-HarnessExe -TreeRoot $Root -AppMeta $flex
            if (-not $mainFlexExe -or -not $prFlexExe) {
                Write-Log "flex exe not found (main=$([bool]$mainFlexExe) pr=$([bool]$prFlexExe)) — omitting the flex table" 'Yellow'
            } else {
                Write-Log "interleaving main/PR flex (--percent $Percent; $($Warmup) warmup + $($Reps) measured each)" 'Green'
                for ($i = 1; $i -le ($Warmup + $Reps); $i++) {
                    $mm = Invoke-OneRun -Exe $mainFlexExe -AppMeta $flex -Index $i -Tag 'main-flex'
                    $pm = Invoke-OneRun -Exe $prFlexExe -AppMeta $flex -Index $i -Tag 'pr-flex'
                    if ($i -le $Warmup) { Write-Log "  (flex warmup pair #$i discarded)" 'DarkGray'; continue }
                    if ($mm -and $pm) { $mainFlexRuns += $mm; $prFlexRuns += $pm }
                    elseif ($mm -or $pm) { Write-Log "  flex pair #$i incomplete (main=$([bool]$mm) pr=$([bool]$pm)) — dropped to keep the paired CI aligned" 'Yellow' }
                }
                if ($mainFlexRuns.Count -lt $Reps -or $prFlexRuns.Count -lt $Reps) {
                    Write-Log "  flex leg short (main $($mainFlexRuns.Count)/$Reps, PR $($prFlexRuns.Count)/$Reps) — its paired CI uses fewer samples" 'Yellow'
                }
            }
        }

        # Fifth interleaved A/B leg: the DataGrid workload. StressPerf.DataGrid stands up
        # the real DataGrid control (DataGridComponent) over a 30-column × 200-row
        # IObservableDataSource and mutates a --percent fraction of cells each tick,
        # forcing a full DataGridComponent.Render() every frame — the per-render
        # array/LINQ allocation path (#663/#669) and per-cell/row .OnTapped/.OnPointerPressed
        # delegate churn (#671) that StocksGrid's native Grid, the keyed-list reorder leg
        # and the flex layout leg never reach. Same paired interleaving + drop-both
        # alignment as above. Best-effort: if either exe is missing (build omitted/failed)
        # the leg is skipped and the datagrid table is omitted — the StocksGrid comparison
        # is unaffected.
        $mainDataGridRuns = @(); $prDataGridRuns = @()
        if ($IncludeDataGrid) {
            $mainDataGridExe = Resolve-HarnessExe -TreeRoot $BaselineRoot -AppMeta $datagrid
            $prDataGridExe   = Resolve-HarnessExe -TreeRoot $Root -AppMeta $datagrid
            if (-not $mainDataGridExe -or -not $prDataGridExe) {
                Write-Log "datagrid exe not found (main=$([bool]$mainDataGridExe) pr=$([bool]$prDataGridExe)) — omitting the datagrid table" 'Yellow'
            } else {
                Write-Log "interleaving main/PR datagrid (--percent $Percent; $($Warmup) warmup + $($Reps) measured each)" 'Green'
                for ($i = 1; $i -le ($Warmup + $Reps); $i++) {
                    $mm = Invoke-OneRun -Exe $mainDataGridExe -AppMeta $datagrid -Index $i -Tag 'main-datagrid'
                    $pm = Invoke-OneRun -Exe $prDataGridExe -AppMeta $datagrid -Index $i -Tag 'pr-datagrid'
                    if ($i -le $Warmup) { Write-Log "  (datagrid warmup pair #$i discarded)" 'DarkGray'; continue }
                    if ($mm -and $pm) { $mainDataGridRuns += $mm; $prDataGridRuns += $pm }
                    elseif ($mm -or $pm) { Write-Log "  datagrid pair #$i incomplete (main=$([bool]$mm) pr=$([bool]$pm)) — dropped to keep the paired CI aligned" 'Yellow' }
                }
                if ($mainDataGridRuns.Count -lt $Reps -or $prDataGridRuns.Count -lt $Reps) {
                    Write-Log "  datagrid leg short (main $($mainDataGridRuns.Count)/$Reps, PR $($prDataGridRuns.Count)/$Reps) — its paired CI uses fewer samples" 'Yellow'
                }
            }
        }

        # Row-memoization leg (#327 opt-in win). SINGLE-TREE, NOT a main-vs-PR interleave:
        # one self-contained PR-head build internally A/Bs Baseline vs Memo(i, () => row) and
        # prints key=value lines, so it needs no `main` side (which couldn't even compile the
        # Memo API) and doesn't use the paired-CI machinery — the two columns come from the
        # bench's own two arms. Best-effort: a missing exe / nonzero exit / unparseable output
        # leaves $rowMemo = $null and the table is omitted; the StocksGrid comparison and every
        # other leg are unaffected.
        $rowMemo = $null
        if ($IncludeRowMemo) {
            try {
                $rowMemoExe = Resolve-HarnessExe -TreeRoot $Root -AppMeta $rowMemoMeta
                if (-not $rowMemoExe) {
                    Write-Log "row-memo exe not found under the PR head — omitting the row-memo table" 'Yellow'
                } else {
                    Write-Log "row-memo bench (PerfBench.RowMemo, PR head only; same-build Baseline vs Memo)" 'Green'
                    $rowMemo = Invoke-RowMemoRun -Exe $rowMemoExe
                }
            } catch {
                Write-Log "row-memo leg failed ($_) — omitting the row-memo table" 'Yellow'
                $rowMemo = $null
            }
        }

        $winRuns = @()
        if ($directExe) {
            Write-Log "vanilla WinUI3 (StressPerf.Direct)" 'Green'
            $winRuns = Measure-Sequential -Exe $directExe -AppMeta $direct -Tag 'winui3' -RepCount $RefReps -WarmupCount $RefWarmup
        } else {
            Write-Log "StressPerf.Direct exe not found — WinUI3 column will read n/a" 'Yellow'
        }

        $rust = Invoke-RustLeg

        # Reconciler micro-suite (ns-resolution, WinUI-undiluted). Best-effort: any
        # failure here leaves $micro = $null and the macro comment is unaffected.
        $micro = $null
        $microOmitReason = $null
        if ($IncludeMicro) {
            try {
                $microMainExe = Resolve-HarnessExe -TreeRoot $BaselineRoot -AppMeta $microMeta
                $microPrExe   = Resolve-HarnessExe -TreeRoot $Root -AppMeta $microMeta
                if ($microMainExe -and $microPrExe) {
                    Write-Log "reconciler micro-suite (PerfBench.ControlModel --variant Reactor; per-rep interleaved, $MicroWarmup warmup + $MicroReps measured each side)" 'Green'
                    $microMainJson = Join-Path $OutDir 'micro-main.jsonl'
                    $microPrJson   = Join-Path $OutDir 'micro-pr.jsonl'
                    # Fresh process per round per side. A plain scriptblock (NOT a
                    # GetNewClosure) so it stays bound to THIS script scope: it reads the
                    # exes/iter/timeout here and calls Invoke-MicroRun, which itself reads
                    # the script-param $OutDir — a module-bound closure would sever that.
                    # The captured vars are not reassigned before the loop runs them.
                    $launchRep = {
                        param($side, $round)
                        $exe = if ($side -eq 'main') { $microMainExe } else { $microPrExe }
                        Invoke-MicroRun -Exe $exe -Tag ("{0}-r{1}" -f $side, $round) -RepCount 1 -IterCount $MicroIterations -TimeoutSec $MicroRepTimeoutSec
                    }
                    $microInter = Invoke-MicroInterleaved -LaunchRep $launchRep -RepCount $MicroReps -WarmupCount $MicroWarmup -MainOut $microMainJson -PrOut $microPrJson
                    if ($microInter) {
                        $micro = Get-PerfMicroComparison `
                            -Main (Read-MicroBenchResults $microInter.MainJson) `
                            -Pr (Read-MicroBenchResults $microInter.PrJson)
                        Write-Log ("  micro: {0} bench(es) compared across {1} interleaved rep(s)" -f @($micro).Count, $microInter.Reps) 'DarkGray'
                        if (@($micro).Count -eq 0) {
                            # >=2 paired rounds survived, but NO bench produced a comparable ok
                            # row on both sides (every bench filtered/errored, or no benchId
                            # overlapped) -> $micro is empty and Format-PerfMicroSection would
                            # otherwise omit the section SILENTLY. Capture a reason so it renders
                            # the loud callout instead, closing the last silent-omit path.
                            $microOmitReason = 'the rep-interleave completed but no bench produced a comparable ok Reactor row on both sides'
                        }
                    } else {
                        # Invoke-MicroInterleaved returned $null: fewer than 2 paired rounds
                        # survived (a per-round timeout, or truncated/empty output on a side).
                        # Capture WHY so Format-PerfComment renders a visible "incomplete"
                        # callout instead of silently dropping the section (the #693 bug); the
                        # per-round detail is already in the run log above.
                        $microOmitReason = "the rep-interleave kept fewer than 2 paired rounds (a per-round timeout at ${MicroRepTimeoutSec}s, or truncated/empty output on one or both sides)"
                    }
                } else {
                    Write-Log "micro-suite exe not found (main=$([bool]$microMainExe) pr=$([bool]$microPrExe)) — omitting micro-benchmarks" 'Yellow'
                    $microOmitReason = "the PerfBench.ControlModel micro exe was not built for one or both sides (main=$([bool]$microMainExe) pr=$([bool]$microPrExe))"
                }
            } catch {
                Write-Log "reconciler micro-suite leg failed ($_) — omitting micro-benchmarks" 'Yellow'
                $micro = $null
                $microOmitReason = "the micro leg threw: $($_.Exception.Message)"
            }
        }

        $main = Measure-PerfRuns -Runs $mainRuns
        $pr = Measure-PerfRuns -Runs $prRuns
        $winui3 = if ($winRuns.Count) { Measure-PerfRuns -Runs $winRuns } else { $null }
        $mainFloor = if ($mainFloorRuns.Count) { Measure-PerfRuns -Runs $mainFloorRuns } else { $null }
        $prFloor = if ($prFloorRuns.Count) { Measure-PerfRuns -Runs $prFloorRuns } else { $null }
        $mainKeyed = if ($mainKeyedRuns.Count) { Measure-PerfRuns -Runs $mainKeyedRuns } else { $null }
        $prKeyed = if ($prKeyedRuns.Count) { Measure-PerfRuns -Runs $prKeyedRuns } else { $null }
        $mainFlex = if ($mainFlexRuns.Count) { Measure-PerfRuns -Runs $mainFlexRuns } else { $null }
        $prFlex = if ($prFlexRuns.Count) { Measure-PerfRuns -Runs $prFlexRuns } else { $null }
        $mainDataGrid = if ($mainDataGridRuns.Count) { Measure-PerfRuns -Runs $mainDataGridRuns } else { $null }
        $prDataGrid = if ($prDataGridRuns.Count) { Measure-PerfRuns -Runs $prDataGridRuns } else { $null }

        $note = $null
        if ($prRuns.Count -eq 0 -or $mainRuns.Count -eq 0) {
            $note = 'One or both of the main/PR ReactorOptimized runs produced no metrics — the harness may have failed to open a window on this runner. See the workflow run log and the uploaded ``perf-logs`` artifact.'
            $exit = 1
        }
        else {
            $short = @()
            if ($mainRuns.Count -lt $Reps) { $short += "main $($mainRuns.Count)/$Reps" }
            if ($prRuns.Count -lt $Reps) { $short += "PR $($prRuns.Count)/$Reps" }
            if ($short.Count) {
                $note = "Some measured runs produced no metrics ($($short -join ', ')); the reported median uses fewer than $Reps samples, so treat the delta with extra caution. See the uploaded ``perf-logs`` artifact."
                $exit = 1
            }
        }

        $ctx = @{
            Percent = $Percent; Duration = $Duration; Reps = $Reps; Warmup = $Warmup
            SkipFloorPercent = $SkipFloorPercent
            Platform = $Platform
            MainSamples = $mainRuns.Count; PrSamples = $prRuns.Count
            MainFloorSamples = $mainFloorRuns.Count; PrFloorSamples = $prFloorRuns.Count
            MainKeyedSamples = $mainKeyedRuns.Count; PrKeyedSamples = $prKeyedRuns.Count
            MainFlexSamples = $mainFlexRuns.Count; PrFlexSamples = $prFlexRuns.Count
            MainDataGridSamples = $mainDataGridRuns.Count; PrDataGridSamples = $prDataGridRuns.Count
            BaseSha = $(if ($BaseSha) { $BaseSha.Substring(0, [Math]::Min(7, $BaseSha.Length)) } else { '' })
            HeadSha = $(if ($HeadSha) { $HeadSha.Substring(0, [Math]::Min(7, $HeadSha.Length)) } else { '' })
            Runner = $runner.Runner; Cpu = $runner.Cpu; Cores = $runner.Cores; MemoryGB = $runner.MemoryGB
            RunUrl = $RunUrl; Timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'); Note = $note
        }
        $comment = Format-PerfComment -Main $main -Pr $pr -WinUI3 $winui3 -Rust $rust -Micro $micro -MicroOmitReason $microOmitReason -MainFloor $mainFloor -PrFloor $prFloor -MainKeyed $mainKeyed -PrKeyed $prKeyed -MainFlex $mainFlex -PrFlex $prFlex -MainDataGrid $mainDataGrid -PrDataGrid $prDataGrid -RowMemo $rowMemo -Context $ctx
        $commentPath = Join-Path $OutDir 'comment.md'
        Set-Content -LiteralPath $commentPath -Value $comment -Encoding UTF8
        Write-Log "comment.md written -> $commentPath" 'Green'

        $result = [pscustomobject]@{ main = $main; pr = $pr; winui3 = $winui3; mainFloor = $mainFloor; prFloor = $prFloor; mainKeyed = $mainKeyed; prKeyed = $prKeyed; mainFlex = $mainFlex; prFlex = $prFlex; mainDataGrid = $mainDataGrid; prDataGrid = $prDataGrid; rowMemo = $rowMemo; rust = $rust; micro = $micro; runner = $runner; context = $ctx }
        $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'result.json') -Encoding UTF8

        Write-Host "`n----- comment.md -----" -ForegroundColor DarkGray
        Write-Host $comment
    }
    else {
        # ---- Local single-tree mode ------------------------------------------
        $aggs = [ordered]@{}
        foreach ($key in $Apps) {
            $meta = $AppRegistry[$key]
            if (-not $SkipBuild) { Build-Harness -TreeRoot $Root -AppMeta $meta }
            $exe = Resolve-HarnessExe -TreeRoot $Root -AppMeta $meta
            if (-not $exe) { Write-Log "exe for $key not found — skipping" 'Yellow'; continue }
            $runs = Measure-Sequential -Exe $exe -AppMeta $meta -Tag $key
            $aggs[$key] = Measure-PerfRuns -Runs $runs
        }

        $rust = Invoke-RustLeg
        if ($rust) { $aggs['Rust'] = $rust }

        Write-Host ""
        Write-Host "==== Perf results ($Platform, median of $Reps, $Warmup warmup) ====" -ForegroundColor Green
        $rows = foreach ($m in $script:PerfMetricSpec) {
            $row = [ordered]@{ Metric = ("{0} {1}" -f $m.Label, $(if ($m.LowerIsBetter) { '(lower better)' } else { '(higher better)' })) }
            foreach ($key in $aggs.Keys) { $row[$key] = Format-PerfNumber $aggs[$key].($m.Key) $m.Digits }
            [pscustomobject]$row
        }
        $rows | Format-Table -AutoSize | Out-String | Write-Host
        if ($RustRepo -and -not $rust) { Write-Host "Rust column n/a — see warnings above." -ForegroundColor DarkGray }
        elseif (-not $RustRepo) { Write-Host "Tip: pass -RustRepo <windows-rs checkout> to add a live Rust column." -ForegroundColor DarkGray }

        $result = [pscustomobject]@{ apps = $aggs; runner = $runner }
        $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutDir 'result.json') -Encoding UTF8
        Write-Log "result.json written -> $(Join-Path $OutDir 'result.json')" 'Green'
    }
}
finally {
    if ($prevScheme) { try { & powercfg /setactive $prevScheme 2>$null | Out-Null; Write-Log "power plan restored -> $prevScheme" 'DarkGray' } catch {} }
    $env:DOTNET_gcServer     = $prevGcServer
    $env:DOTNET_gcConcurrent = $prevGcConcurrent
    if ($DefenderExclude) { try { Remove-MpPreference -ExclusionPath $Root -ErrorAction SilentlyContinue } catch {} }
}

exit $exit
