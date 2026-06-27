<#
.SYNOPSIS
    Dependency-free unit tests for the testable functions in Run-PerfBenchmark.ps1
    (the /perf benchmark orchestrator): the compare-mode .csproj overlay in
    Build-Harness and the required-set idempotency gate in Stage-RustRuntime.

.DESCRIPTION
    Run-PerfBenchmark.ps1 isn't dot-sourceable (it has a param block + a main run
    flow), so each function under test is extracted via the PowerShell AST and
    defined in isolation with `dotnet` / the runtime download stubbed out. Runs
    headless and cross-platform with no WinUI harness and no network, so it is safe
    on the cheap Linux runner in .github/workflows/perf-lib-tests.yml. Exits
    non-zero if any assertion fails.

    The full Stage-RustRuntime download/extract/copy path is Windows + network +
    MSIX-extraction bound; it is validated by the live `/perf` run (the stated
    final acceptance for #674), not here. This file covers the pure branching that
    does NOT need a runner: the overlay/restore guarantees and the staging
    idempotency gate.

    Run locally:  pwsh tests/stress_perf/ci/RunPerfBenchmark.Tests.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Pass = 0
$script:Fail = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()
function Assert-True {
    param([bool]$Condition, [string]$Message)
    if ($Condition) { $script:Pass++ } else { $script:Fail++; $script:Failures.Add($Message) }
}

# --- Extract the functions under test from the orchestrator script. ---
$scriptPath = Join-Path $PSScriptRoot 'Run-PerfBenchmark.ps1'
$src = Get-Content $scriptPath -Raw
# Capture parse errors instead of discarding them: a syntax error in the orchestrator
# script makes every extracted function extent unreliable, so fail fast and let CI flag
# the broken script rather than silently testing garbage.
$parseTokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($src, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors -and $parseErrors.Count -gt 0) {
    Write-Host "Run-PerfBenchmark.ps1 has $($parseErrors.Count) parse error(s):" -ForegroundColor Red
    $parseErrors | ForEach-Object { Write-Host "  line $($_.Extent.StartLineNumber): $($_.Message)" -ForegroundColor Red }
    exit 1
}
function Get-Func([string]$name) {
    $f = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true) |
        Select-Object -First 1
    if (-not $f) { throw "$name not found in Run-PerfBenchmark.ps1" }
    $f.Extent.Text
}

$global:LogLines = [System.Collections.Generic.List[string]]::new()
function Write-Log { param($Message, $Color) $global:LogLines.Add([string]$Message) }
# Stub `dotnet`: record the .csproj content the build actually saw, honor a forced exit code.
function dotnet { $global:SAW = Get-Content $args[1] -Raw; $global:LASTEXITCODE = $global:ForceExit }

Invoke-Expression (Get-Func 'Build-Harness')
Invoke-Expression (Get-Func 'Stage-RustRuntime')

# ===========================================================================
#  Build-Harness — compare-mode .csproj overlay / restore
# ===========================================================================
# Script-scope inputs Build-Harness reads.
$Platform = 'x64'; $SelfContained = $true
$OutDir = Join-Path ([IO.Path]::GetTempPath()) 'perfci-bh-out'
$null = New-Item -ItemType Directory -Force $OutDir | Out-Null

$meta = @{ AppName = 'RO'; ProjectRel = [IO.Path]::Combine('h', 'app.csproj') }
$baseTree = Join-Path ([IO.Path]::GetTempPath()) 'perfci-bh-base'
$prTree   = Join-Path ([IO.Path]::GetTempPath()) 'perfci-bh-pr'
$prCsproj = Join-Path $prTree $meta.ProjectRel

function New-Tree([string]$root, [string]$content) {
    if (Test-Path $root) { Remove-Item $root -Recurse -Force }
    $proj = Join-Path $root $meta.ProjectRel
    $null = New-Item -ItemType Directory -Force (Split-Path $proj) | Out-Null
    Set-Content -LiteralPath $proj -Value $content -NoNewline
}

# 1. compare mode, PR tree, trusted present -> build sees TRUSTED; PR tree restored; no leftover.
$global:ForceExit = 0
New-Tree $baseTree 'TRUSTED'; New-Tree $prTree 'PRORIG'
$BaselineRoot = $baseTree
Build-Harness -TreeRoot $prTree -AppMeta $meta
Assert-True ($global:SAW -eq 'TRUSTED')                  '[1] compare/PR: build saw trusted csproj'
Assert-True ((Get-Content $prCsproj -Raw) -eq 'PRORIG')  '[1] compare/PR: PR csproj restored'
Assert-True (-not (Test-Path "$prCsproj.perfci-orig"))   '[1] compare/PR: no backup leftover'

# 2. compare mode, building the BASELINE itself -> no overlay, no backup.
New-Tree $baseTree 'TRUSTED'
$BaselineRoot = $baseTree
Build-Harness -TreeRoot $baseTree -AppMeta $meta
$baseCsproj = Join-Path $baseTree $meta.ProjectRel
Assert-True ($global:SAW -eq 'TRUSTED')                  '[2] baseline-self: build saw own csproj'
Assert-True (-not (Test-Path "$baseCsproj.perfci-orig")) '[2] baseline-self: no backup created'

# 3. local single-tree mode ($BaselineRoot empty) -> no overlay.
New-Tree $prTree 'PRORIG'
$BaselineRoot = ''
Build-Harness -TreeRoot $prTree -AppMeta $meta
Assert-True ($global:SAW -eq 'PRORIG')                   '[3] local: build saw PR csproj (no overlay)'
Assert-True ((Get-Content $prCsproj -Raw) -eq 'PRORIG')  '[3] local: PR csproj unchanged'

# 4. compare mode, PR tree, trusted MISSING -> graceful fallback, PR untouched.
New-Tree $prTree 'PRORIG'
if (Test-Path $baseTree) { Remove-Item $baseTree -Recurse -Force }
$null = New-Item -ItemType Directory -Force $baseTree | Out-Null
$BaselineRoot = $baseTree
Build-Harness -TreeRoot $prTree -AppMeta $meta
Assert-True ($global:SAW -eq 'PRORIG')                   '[4] trusted-missing: fallback to PR csproj'
Assert-True ((Get-Content $prCsproj -Raw) -eq 'PRORIG')  '[4] trusted-missing: PR csproj untouched'

# 5. compare mode, build FAILS -> throws, but finally restores + removes backup.
$global:ForceExit = 1
New-Tree $baseTree 'TRUSTED'; New-Tree $prTree 'PRORIG'
$BaselineRoot = $baseTree
$threw = $false
try { Build-Harness -TreeRoot $prTree -AppMeta $meta } catch { $threw = $true }
Assert-True $threw                                       '[5] build-fails: Build-Harness threw'
Assert-True ((Get-Content $prCsproj -Raw) -eq 'PRORIG')  '[5] build-fails: PR csproj restored'
Assert-True (-not (Test-Path "$prCsproj.perfci-orig"))   '[5] build-fails: no backup leftover'

# 6. compare mode, the trusted overlay Copy-Item THROWS after the backup is created ->
#    finally must still restore from backup and remove it (the new cleanup guarantee).
$global:ForceExit = 0
New-Tree $baseTree 'TRUSTED'; New-Tree $prTree 'PRORIG'
$BaselineRoot = $baseTree
# Shadow Copy-Item so ONLY the overlay copy (source in the baseline tree) fails; the
# backup-create copy and the finally restore copy (source = *.perfci-orig) still work.
function Copy-Item {
    param([string]$LiteralPath, [string]$Destination, [switch]$Force)
    if ($LiteralPath -like '*perfci-bh-base*') { throw 'simulated overlay copy failure' }
    Microsoft.PowerShell.Management\Copy-Item -LiteralPath $LiteralPath -Destination $Destination -Force:$Force
}
$threw6 = $false
try { Build-Harness -TreeRoot $prTree -AppMeta $meta } catch { $threw6 = $true }
Remove-Item function:Copy-Item
Assert-True $threw6                                      '[6] overlay-throws: Build-Harness rethrew'
Assert-True ((Get-Content $prCsproj -Raw) -eq 'PRORIG')  '[6] overlay-throws: PR csproj restored'
Assert-True (-not (Test-Path "$prCsproj.perfci-orig"))   '[6] overlay-throws: no backup leftover'

# ===========================================================================
#  Stage-RustRuntime — completion-marker idempotency gate (network-free)
# ===========================================================================
# Early-return requires BOTH the completion marker AND the core DLLs present next to the
# exe. The marker is written only after a fully verified stage (so a partial stage can't
# set it); the core-DLL re-check guards against the marker surviving an external wipe.

# (1) marker + core DLLs present -> early-return before any download/extract/tar.
$exeDir = Join-Path ([IO.Path]::GetTempPath()) 'perfci-rust-exedir'
if (Test-Path $exeDir) { Remove-Item $exeDir -Recurse -Force }
$null = New-Item -ItemType Directory -Force $exeDir | Out-Null
Set-Content -LiteralPath (Join-Path $exeDir '.perfci-runtime-staged') -Value 'x' -NoNewline
foreach ($f in 'microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll', 'Microsoft.Web.WebView2.Core.dll') {
    Set-Content -LiteralPath (Join-Path $exeDir $f) -Value 'x' -NoNewline
}
$global:LogLines.Clear()
Stage-RustRuntime -ExeDir $exeDir -Platform 'x64'
Assert-True (@($global:LogLines | Where-Object { $_ -match 'already staged' }).Count -ge 1) `
    '[stage] marker + core DLLs present -> idempotent early-return (no download)'

# (2) a DLL subset present but NO marker must NOT early-return — a partial prior stage has
# to be allowed to finish. Point SystemRoot at a dir with no System32\tar.exe so the
# function bails network-free right after the gate on any OS, and assert it never logged
# 'already staged' (i.e. the DLL subset did not satisfy the gate).
$exeDir2 = Join-Path ([IO.Path]::GetTempPath()) 'perfci-rust-exedir2'
if (Test-Path $exeDir2) { Remove-Item $exeDir2 -Recurse -Force }
$null = New-Item -ItemType Directory -Force $exeDir2 | Out-Null
foreach ($f in 'microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll') {
    Set-Content -LiteralPath (Join-Path $exeDir2 $f) -Value 'x' -NoNewline
}
$savedSysRoot = $env:SystemRoot
$env:SystemRoot = [IO.Path]::GetTempPath()
$global:LogLines.Clear()
try { Stage-RustRuntime -ExeDir $exeDir2 -Platform 'x64' } finally { $env:SystemRoot = $savedSysRoot }
Assert-True (@($global:LogLines | Where-Object { $_ -match 'already staged' }).Count -eq 0) `
    '[stage] DLL subset without marker -> does NOT early-return (no false idempotency)'
Remove-Item $exeDir2 -Recurse -Force -ErrorAction SilentlyContinue

# (3) marker present but the core DLLs were externally removed -> the marker is stale: the
# function must delete it and NOT early-return (it falls through to restage). Force the tar
# probe to miss so it bails network-free after dropping the marker.
$exeDir3 = Join-Path ([IO.Path]::GetTempPath()) 'perfci-rust-exedir3'
if (Test-Path $exeDir3) { Remove-Item $exeDir3 -Recurse -Force }
$null = New-Item -ItemType Directory -Force $exeDir3 | Out-Null
$marker3 = Join-Path $exeDir3 '.perfci-runtime-staged'
Set-Content -LiteralPath $marker3 -Value 'x' -NoNewline
$savedSysRoot = $env:SystemRoot
$env:SystemRoot = [IO.Path]::GetTempPath()
$global:LogLines.Clear()
try { Stage-RustRuntime -ExeDir $exeDir3 -Platform 'x64' } finally { $env:SystemRoot = $savedSysRoot }
Assert-True (@($global:LogLines | Where-Object { $_ -match 'already staged' }).Count -eq 0) `
    '[stage] stale marker (core DLLs missing) -> does NOT early-return'
Assert-True (-not (Test-Path $marker3)) `
    '[stage] stale marker (core DLLs missing) -> marker deleted before restage'
Remove-Item $exeDir3 -Recurse -Force -ErrorAction SilentlyContinue

# (4) marker + the WinAppSDK core DLLs present but the WebView2 Core DLL MISSING -> the stage
# is incomplete (WebView2 is a manifest-required SxS file that ships in a SEPARATE package), so
# the gate must treat the marker as stale, drop it, and NOT early-return. Force the tar probe to
# miss so it bails network-free right after the gate.
$exeDir4 = Join-Path ([IO.Path]::GetTempPath()) 'perfci-rust-exedir4'
if (Test-Path $exeDir4) { Remove-Item $exeDir4 -Recurse -Force }
$null = New-Item -ItemType Directory -Force $exeDir4 | Out-Null
$marker4 = Join-Path $exeDir4 '.perfci-runtime-staged'
Set-Content -LiteralPath $marker4 -Value 'x' -NoNewline
foreach ($f in 'microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll') {
    Set-Content -LiteralPath (Join-Path $exeDir4 $f) -Value 'x' -NoNewline
}
$savedSysRoot = $env:SystemRoot
$env:SystemRoot = [IO.Path]::GetTempPath()
$global:LogLines.Clear()
try { Stage-RustRuntime -ExeDir $exeDir4 -Platform 'x64' } finally { $env:SystemRoot = $savedSysRoot }
Assert-True (@($global:LogLines | Where-Object { $_ -match 'already staged' }).Count -eq 0) `
    '[stage] WebView2 Core DLL missing -> does NOT early-return (WebView2 is required)'
Assert-True (-not (Test-Path $marker4)) `
    '[stage] WebView2 Core DLL missing -> stale marker dropped before restage'
Remove-Item $exeDir4 -Recurse -Force -ErrorAction SilentlyContinue

# ===========================================================================
#  Invoke-MicroRun — output-integrity discard paths (timeout / non-zero exit)
# ===========================================================================
# PerfBench.ControlModel writes results.jsonl incrementally (one line per rep), so a
# killed or crashed run leaves a TRUNCATED prefix. Invoke-MicroRun must return $null
# (omit the micro section) rather than hand back a silent subset. Stub Start-Process so
# each scenario drives WaitForExit / ExitCode without launching a real child; the stub
# simulates the child writing output by honoring an --out path it finds in ArgumentList.
Invoke-Expression (Get-Func 'Invoke-MicroRun')

$global:MicroWaitResult = $true     # $false => WaitForExit timed out
$global:MicroExit = 0               # process exit code
$global:MicroWriteContent = $null   # non-null => stub writes it to the --out file
function Start-Process {
    param(
        [string]$FilePath, [string[]]$ArgumentList, [switch]$PassThru,
        [string]$RedirectStandardOutput, [string]$RedirectStandardError, $WindowStyle
    )
    $outIdx = [Array]::IndexOf($ArgumentList, '--out')
    if ($outIdx -ge 0 -and $global:MicroWriteContent) {
        Set-Content -LiteralPath $ArgumentList[$outIdx + 1] -Value $global:MicroWriteContent -NoNewline
    }
    $exit = $global:MicroExit; $wait = $global:MicroWaitResult
    [pscustomobject]@{ PriorityClass = $null } |
        Add-Member -MemberType NoteProperty -Name ExitCode -Value $exit -PassThru |
        Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { param($ms) $wait }.GetNewClosure() -PassThru |
        Add-Member -MemberType ScriptMethod -Name Kill -Value { param($entireTree) } -PassThru
}
function Start-Sleep { param([int]$Seconds, [int]$Milliseconds) }   # no-op: skip the post-kill settle wait

# Wrap the scenarios in try/finally so the stub functions are removed deterministically:
# if any assertion throws, an un-finally'd Remove-Item would be skipped and the
# Start-Process / Start-Sleep stubs would leak into the rest of the script.
try {
    # A. clean exit 0 + non-empty output -> returns the jsonl path.
    $global:MicroWaitResult = $true; $global:MicroExit = 0; $global:MicroWriteContent = '{"benchId":"M1"}'
    $resA = Invoke-MicroRun -Exe 'x.exe' -Tag 'okrun' -RepCount 2 -IterCount 10
    Assert-True ($resA -eq (Join-Path $OutDir 'micro-okrun.jsonl')) '[micro] clean exit 0 + output -> returns jsonl path'

    # B. timeout (WaitForExit=$false) + partial output present -> $null (truncated prefix discarded).
    $global:MicroWaitResult = $false; $global:MicroExit = 0; $global:MicroWriteContent = '{"benchId":"M1"}'
    $resB = Invoke-MicroRun -Exe 'x.exe' -Tag 'timeoutrun' -RepCount 2 -IterCount 10
    Assert-True ($null -eq $resB) '[micro] timeout + partial output -> $null (truncated prefix discarded)'

    # C. non-zero exit + non-empty output -> $null (a crash leaves a silent subset).
    $global:MicroWaitResult = $true; $global:MicroExit = 3; $global:MicroWriteContent = '{"benchId":"M1"}'
    $resC = Invoke-MicroRun -Exe 'x.exe' -Tag 'crashrun' -RepCount 2 -IterCount 10
    Assert-True ($null -eq $resC) '[micro] non-zero exit + output -> $null (truncated prefix discarded)'

    # D. clean exit 0 but NO output file written -> $null (nothing produced).
    $global:MicroWaitResult = $true; $global:MicroExit = 0; $global:MicroWriteContent = $null
    $resD = Invoke-MicroRun -Exe 'x.exe' -Tag 'emptyrun' -RepCount 2 -IterCount 10
    Assert-True ($null -eq $resD) '[micro] clean exit but empty/no output -> $null'
}
finally {
    Remove-Item function:Start-Process -ErrorAction SilentlyContinue
    Remove-Item function:Start-Sleep -ErrorAction SilentlyContinue
}

# ===========================================================================
#  Micro-suite budget — iterations + per-side timeout sized to actually finish
# ===========================================================================
# The 16-bench suite was silently dropped from every comment because at
# -MicroIterations 10000 it ran ~3x over the per-side timeout (completed only
# M1-M4, then Invoke-MicroRun discarded the truncated prefix). PR5a cut the inner
# iteration count to fit the per-side budget; PR5c raised it to 2000 (still 5x under
# the 10000 that overran) to steady the per-op alloc variance for the min-effect band.
# It stays a pure capacity knob — each bench remains hundreds of thousands of times the
# timer floor, so the per-op ns/alloc math is unchanged.
$mip = $ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'MicroIterations' } | Select-Object -First 1
Assert-True ($null -ne $mip) '[micro] -MicroIterations parameter exists'
Assert-True ($mip -and $mip.DefaultValue -and $mip.DefaultValue.Extent.Text -eq '2000') '[micro] -MicroIterations defaults to 2000 (fits the per-side budget; steadies per-op alloc variance for the band)'
$mrp = $ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'MicroReps' } | Select-Object -First 1
Assert-True ($mrp -and $mrp.DefaultValue -and $mrp.DefaultValue.Extent.Text -eq '12') '[micro] -MicroReps defaults to 12 (paired-CI sample count unchanged)'
$mwp = $ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'MicroWarmup' } | Select-Object -First 1
Assert-True ($mwp -and $mwp.DefaultValue -and $mwp.DefaultValue.Extent.Text -eq '1') '[micro] -MicroWarmup defaults to 1 (one interleaved warmup round dropped per side)'
$mtp = $ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'MicroRepTimeoutSec' } | Select-Object -First 1
Assert-True ($mtp -and $mtp.DefaultValue -and $mtp.DefaultValue.Extent.Text -eq '180') '[micro] -MicroRepTimeoutSec defaults to 180 (per-round launch budget, not the whole suite)'
$microFn = Get-Func 'Invoke-MicroRun'
# Invoke-MicroRun is now the single-launch primitive; its timeout is a param (default
# 600) that the interleaver overrides per round via -MicroRepTimeoutSec, rather than a
# hardcoded whole-suite constant.
Assert-True ($microFn -match '\[int\]\$TimeoutSec\s*=\s*600') '[micro] Invoke-MicroRun -TimeoutSec defaults to 600s'
Assert-True ($microFn -match '\$timeoutSec\s*=\s*\$TimeoutSec') '[micro] Invoke-MicroRun honors the -TimeoutSec param (no hardcoded constant)'

# ===========================================================================
#  Compare-mode micro call-site wiring — the glue that feeds the helpers
# ===========================================================================
# ConvertTo-MicroRepLines / Invoke-MicroInterleaved are unit-tested in isolation
# below with a synthetic launch stub, but the MAIN-FLOW wiring that drives them in a
# real /perf compare run is only as good as the call site. Lock it so a refactor
# can't silently break rep interleaving (drop -TimeoutSec, mis-pass the rep/warmup
# counts, pick the wrong side exe, or read the wrong accumulator). The call site
# lives in the script body (after the last function), so assert against the raw
# source scoped to the $IncludeMicro block.
$microWire = [regex]::Match($src, '(?s)if \(\$IncludeMicro\).*?\$main = Measure-PerfRuns').Value
Assert-True ($microWire.Length -gt 0) '[micro-wiring] $IncludeMicro call-site block located in the orchestrator body'
Assert-True ($microWire -match '\$exe\s*=\s*if.*\$microMainExe.*\$microPrExe') '[micro-wiring] launchRep selects the side exe (main vs pr)'
Assert-True ($microWire -match 'Invoke-MicroRun\s+-Exe\s+\$exe\s+-Tag\s+.+-RepCount\s+1\s+-IterCount\s+\$MicroIterations\s+-TimeoutSec\s+\$MicroRepTimeoutSec') '[micro-wiring] launchRep runs ONE rep/round at the micro iter + per-round timeout budget'
Assert-True ($microWire -match 'Invoke-MicroInterleaved\s+-LaunchRep\s+\$launchRep\s+-RepCount\s+\$MicroReps\s+-WarmupCount\s+\$MicroWarmup') '[micro-wiring] interleaver gets MicroReps measured + MicroWarmup warmup rounds'
Assert-True (($microWire -match '\$microInter\.MainJson') -and ($microWire -match '\$microInter\.PrJson')) '[micro-wiring] comparison reads the interleaved accumulators (.MainJson/.PrJson)'
Assert-True ($microWire -match 'Get-PerfMicroComparison') '[micro-wiring] interleaved accumulators feed Get-PerfMicroComparison'
# PR5c: incompleteness must reach the COMMENT, not just the run log. Lock that the omit
# reason is captured when the leg produces no comparison (timeout / missing-exe / thrown) and
# that it threads into Format-PerfComment so the section renders a visible "incomplete" callout
# instead of silently vanishing (the #693 regression).
Assert-True ($microWire -match '\$microOmitReason\s*=\s*"') '[micro-wiring] an omit reason is captured when the micro leg yields no comparison'
Assert-True ($src -match 'Format-PerfComment .*-MicroOmitReason \$microOmitReason') '[micro-wiring] the captured omit reason threads into Format-PerfComment'
# Every silent-omit PATH must capture a reason — one per failure mode — so none of them can
# regress back to a vanished section. Four sites: too-few-rounds, zero comparable rows after a
# successful interleave (the residual path PR5c added), exe-not-built, and the catch-all throw.
Assert-True ($microWire -match 'fewer than 2 paired rounds')        '[micro-wiring] omit reason set on the interleave-null (too-few-rounds) path'
Assert-True ($microWire -match 'no bench produced a comparable ok') '[micro-wiring] omit reason set when the interleave succeeds but yields zero comparable rows'
Assert-True ($microWire -match 'micro exe was not built')           '[micro-wiring] omit reason set on the exe-not-found path'
Assert-True ($microWire -match 'the micro leg threw')               '[micro-wiring] omit reason set on the thrown-leg (catch) path'

# ===========================================================================
#  Invoke-OneRun — --percent threading (-RunPercent defaults to $Percent)
# ===========================================================================
# The low-mutation skip-floor leg drives the SAME exe at a different mutation
# percent via -RunPercent. Lock the contract that the harness CLI actually
# receives that percent (and that omitting -RunPercent falls back to $Percent),
# so a floor run can't silently re-measure the 50% workload. Stub Start-Process
# to capture the ArgumentList, plus the two PerfLib helpers Invoke-OneRun calls
# (Read-HarnessMetrics / Format-PerfNumber) that aren't loaded in this file.
Invoke-Expression (Get-Func 'Invoke-OneRun')
$Percent = 50; $Duration = 10; $PinAffinity = $false
$roMeta = @{ AppName = 'RO' }
$oneRunExe = Join-Path ([IO.Path]::GetTempPath()) 'RO.exe'
function Read-HarnessMetrics { param($Directory, $AppName) [pscustomobject]@{ Source = 'json'; RendersPerSec = 1; AvgReconcileMs = 1; AvgDiffMs = 1; AvgMemoryMB = 1 } }
function Format-PerfNumber { param($Value, $Digits) [string]$Value }
$global:SAWARGS = $null
function Start-Process {
    param([string]$FilePath, [string[]]$ArgumentList, [switch]$PassThru,
        [string]$RedirectStandardOutput, [string]$RedirectStandardError, $WindowStyle)
    $global:SAWARGS = $ArgumentList
    [pscustomobject]@{ PriorityClass = $null; ExitCode = 0 } |
        Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { param($ms) $true } -PassThru |
        Add-Member -MemberType ScriptMethod -Name Kill -Value { param($t) } -PassThru
}
function Start-Sleep { param([int]$Seconds, [int]$Milliseconds) }
try {
    # default: -RunPercent omitted -> harness gets the script-level $Percent (50).
    $null = Invoke-OneRun -Exe $oneRunExe -AppMeta $roMeta -Index 1 -Tag 'main'
    $pIdx = [Array]::IndexOf($global:SAWARGS, '--percent')
    Assert-True (($pIdx -ge 0) -and ($global:SAWARGS[$pIdx + 1] -eq '50')) '[onerun] -RunPercent omitted -> harness gets --percent 50 ($Percent)'

    # explicit -RunPercent 0 -> the skip-floor leg drives the harness at --percent 0.
    $null = Invoke-OneRun -Exe $oneRunExe -AppMeta $roMeta -Index 1 -Tag 'main-floor' -RunPercent 0
    $fIdx = [Array]::IndexOf($global:SAWARGS, '--percent')
    Assert-True (($fIdx -ge 0) -and ($global:SAWARGS[$fIdx + 1] -eq '0')) '[onerun] -RunPercent 0 -> harness gets --percent 0 (skip-floor leg)'
}
finally {
    Remove-Item function:Start-Process -ErrorAction SilentlyContinue
    Remove-Item function:Start-Sleep -ErrorAction SilentlyContinue
    Remove-Item function:Read-HarnessMetrics -ErrorAction SilentlyContinue
    Remove-Item function:Format-PerfNumber -ErrorAction SilentlyContinue
}

# ===========================================================================
#  Keyed-list leg — static wiring contract (param + registry + leg + comment)
# ===========================================================================
# The keyed-list leg lives in the orchestrator's main run flow (not a dot-sourceable
# function), exactly like the headline + skip-floor legs, so — as with those — its
# Invoke-OneRun threading is covered by the -RunPercent test above (the keyed leg omits
# -RunPercent, so it inherits $Percent). What is NEW and worth locking here is the
# static wiring: the opt-out switch defaults on, the registry resolves the right
# exe/csproj, the interleave runs both sides, and the aggregates reach the renderer.
$kp = $ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'IncludeKeyedList' } | Select-Object -First 1
Assert-True ($null -ne $kp) '[keyed] -IncludeKeyedList parameter exists'
Assert-True ($kp -and $kp.DefaultValue -and $kp.DefaultValue.Extent.Text -eq '$true') '[keyed] -IncludeKeyedList defaults to $true (on unless opted out)'
Assert-True ($src -match "KeyedList\s*=\s*@\{\s*AppName\s*=\s*'StressPerf\.KeyedList';\s*ProjectRel\s*=\s*'tests\\stress_perf\\StressPerf\.KeyedList") '[keyed] AppRegistry maps KeyedList -> StressPerf.KeyedList exe + csproj'
Assert-True ($src -match "-Tag 'main-keyed'") '[keyed] leg interleaves the main side (main-keyed)'
Assert-True ($src -match "-Tag 'pr-keyed'")   '[keyed] leg interleaves the PR side (pr-keyed)'
Assert-True ($src -match '-MainKeyed \$mainKeyed') '[keyed] Format-PerfComment receives the main keyed aggregate'
Assert-True ($src -match '-PrKeyed \$prKeyed')     '[keyed] Format-PerfComment receives the PR keyed aggregate'

# Opt-out + best-effort build fallback: the keyed build is guarded by the switch, and a
# build failure flips the switch off (omit the table, never throw) so the leg is skipped.
Assert-True ($src -match 'if \(\$IncludeKeyedList -and -not \$SkipBuild\)') '[keyed] build is guarded by -IncludeKeyedList (and -SkipBuild)'
Assert-True ($src -match '(?s)keyed-list workload build failed.*?\$IncludeKeyedList = \$false') '[keyed] a build failure flips -IncludeKeyedList off (best-effort: omit table, never throw)'
Assert-True ($src -match '\$mainKeyedRuns = @\(\); \$prKeyedRuns = @\(\)\s*\r?\n\s*if \(\$IncludeKeyedList\)') '[keyed] the run leg is skipped unless -IncludeKeyedList is on'

# Paired drop-both alignment: a complete keyed pair appends BOTH sides; a one-sided
# failure drops BOTH halves so the paired CI's main[i]/pr[i] zip stays index-aligned.
Assert-True ($src -match 'if \(\$mm -and \$pm\) \{ \$mainKeyedRuns \+= \$mm; \$prKeyedRuns \+= \$pm \}') '[keyed] a complete pair appends both main + pr samples'
Assert-True ($src -match 'elseif \(\$mm -or \$pm\) \{ Write-Log "  keyed pair #\$i incomplete') '[keyed] a one-sided keyed run drops both halves (paired CI stays aligned)'

# ===========================================================================
#  Flex leg — static wiring contract (param + registry + leg + comment)
# ===========================================================================
# Mirrors the keyed-list leg contract above: a fourth interleaved A/B leg
# (StressPerf.Flex) whose opt-out switch defaults on, registry resolves the right
# exe/csproj, interleave runs both sides with drop-both pairing, and aggregates reach
# the renderer. Its Invoke-OneRun threading inherits $Percent (no -RunPercent), as the
# keyed leg does.
$fp = $ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'IncludeFlex' } | Select-Object -First 1
Assert-True ($null -ne $fp) '[flex] -IncludeFlex parameter exists'
Assert-True ($fp -and $fp.DefaultValue -and $fp.DefaultValue.Extent.Text -eq '$true') '[flex] -IncludeFlex defaults to $true (on unless opted out)'
Assert-True ($src -match "Flex\s*=\s*@\{\s*AppName\s*=\s*'StressPerf\.Flex';\s*ProjectRel\s*=\s*'tests\\stress_perf\\StressPerf\.Flex") '[flex] AppRegistry maps Flex -> StressPerf.Flex exe + csproj'
Assert-True ($src -match "-Tag 'main-flex'") '[flex] leg interleaves the main side (main-flex)'
Assert-True ($src -match "-Tag 'pr-flex'")   '[flex] leg interleaves the PR side (pr-flex)'
Assert-True ($src -match '-MainFlex \$mainFlex') '[flex] Format-PerfComment receives the main flex aggregate'
Assert-True ($src -match '-PrFlex \$prFlex')     '[flex] Format-PerfComment receives the PR flex aggregate'

# Opt-out + best-effort build fallback: the flex build is guarded by the switch, and a
# build failure flips the switch off (omit the table, never throw) so the leg is skipped.
Assert-True ($src -match 'if \(\$IncludeFlex -and -not \$SkipBuild\)') '[flex] build is guarded by -IncludeFlex (and -SkipBuild)'
Assert-True ($src -match '(?s)flex workload build failed.*?\$IncludeFlex = \$false') '[flex] a build failure flips -IncludeFlex off (best-effort: omit table, never throw)'
Assert-True ($src -match '\$mainFlexRuns = @\(\); \$prFlexRuns = @\(\)\s*\r?\n\s*if \(\$IncludeFlex\)') '[flex] the run leg is skipped unless -IncludeFlex is on'

# Paired drop-both alignment: a complete flex pair appends BOTH sides; a one-sided
# failure drops BOTH halves so the paired CI's main[i]/pr[i] zip stays index-aligned.
Assert-True ($src -match 'if \(\$mm -and \$pm\) \{ \$mainFlexRuns \+= \$mm; \$prFlexRuns \+= \$pm \}') '[flex] a complete pair appends both main + pr samples'
Assert-True ($src -match 'elseif \(\$mm -or \$pm\) \{ Write-Log "  flex pair #\$i incomplete') '[flex] a one-sided flex run drops both halves (paired CI stays aligned)'

# result.json carries the flex aggregates so downstream tooling can read the leg.
Assert-True ($src -match 'mainFlex = \$mainFlex')   '[flex] result.json object includes the main flex aggregate'
Assert-True ($src -match 'prFlex = \$prFlex')       '[flex] result.json object includes the PR flex aggregate'

# ===========================================================================
#  Micro rep-interleave — ConvertTo-MicroRepLines + Invoke-MicroInterleaved
# ===========================================================================
# Per-rep interleaving runs a FRESH process per round per side (so the
# process-to-process timing offset is randomized into the paired variance rather
# than biasing every rep one way). Each --reps 1 launch emits "repetition":0 on
# every bench line; the accumulator must look byte-compatible with a single
# multi-rep launch (Get-PerfMicroComparison pairs main<->pr BY repetition value),
# so surviving rounds are renumbered to dense indices 0,1,2,... on BOTH sides, and
# a one-sided failure drops the whole round to keep the indices aligned. Stub the
# launch with a scriptblock so the loop is exercised without the exe.
Invoke-Expression (Get-Func 'ConvertTo-MicroRepLines')
Invoke-Expression (Get-Func 'Invoke-MicroInterleaved')

$microIntTmp = Join-Path ([IO.Path]::GetTempPath()) ("perfci-microint-" + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Force $microIntTmp | Out-Null

# A per-round launch writes one line per bench, every line carrying "repetition":0
# (exactly what a single --reps 1 PerfBench.ControlModel launch produces).
function New-RoundFile([string]$side, [int]$round, [string[]]$benchIds) {
    $p = Join-Path $microIntTmp ("round-{0}-{1}.jsonl" -f $side, $round)
    $lines = $benchIds | ForEach-Object { '{"benchId":"' + $_ + '","variant":"Reactor","repetition":0,"meanNs":100,"allocBytes":200}' }
    Set-Content -LiteralPath $p -Value $lines -Encoding UTF8
    $p
}
function Get-RepSeq([string[]]$lines, [string]$benchId) {
    # The repetition indices (in file order) for one bench across the accumulator.
    ($lines | Where-Object { $_ -match ('"benchId":"' + $benchId + '"') } |
        ForEach-Object { ([regex]'"repetition":(\d+)').Match($_).Groups[1].Value }) -join ','
}

try {
    # --- ConvertTo-MicroRepLines: surgical repetition renumber. ---
    $roundD = New-RoundFile 'main' 9 @('M1', 'M2', 'M3')
    $rw = ConvertTo-MicroRepLines -RoundFile $roundD -RepIndex 7
    Assert-True (@($rw).Count -eq 3) '[rewrite] returns one line per bench'
    Assert-True (@($rw | Where-Object { $_ -match '"repetition":7' }).Count -eq 3) '[rewrite] every line renumbered to the target rep index'
    Assert-True (@($rw | Where-Object { $_ -match '"repetition":0' }).Count -eq 0) '[rewrite] no line keeps the original repetition:0'
    Assert-True (@($rw | Where-Object { $_ -match '"benchId":"M2"' -and $_ -match '"meanNs":100' -and $_ -match '"allocBytes":200' }).Count -eq 1) '[rewrite] payload (benchId/meanNs/allocBytes) preserved — only repetition changes'
    Assert-True ($null -eq (ConvertTo-MicroRepLines -RoundFile (Join-Path $microIntTmp 'nope.jsonl') -RepIndex 0)) '[rewrite] missing file -> $null (drop the round)'
    $emptyF = Join-Path $microIntTmp 'empty.jsonl'; Set-Content -LiteralPath $emptyF -Value '' -NoNewline
    Assert-True ($null -eq (ConvertTo-MicroRepLines -RoundFile $emptyF -RepIndex 0)) '[rewrite] empty file -> $null (drop the round)'

    # --- Invoke-MicroInterleaved: the launch stub. FailRounds drives one-sided $null
    #     failures; ThrowRounds drives one-sided EXCEPTIONS (a transient runner hiccup). ---
    $global:FailRounds = @{}
    $global:ThrowRounds = @{}
    # Plain scriptblock (NOT .GetNewClosure()): a closure rebinds to a module whose
    # scope-parent is GLOBAL, which can't see the script-scoped New-RoundFile when the
    # test runs in its own script scope (CI invokes the file via the call operator, not
    # dot-source). A plain scriptblock stays bound to THIS session state where both
    # New-RoundFile and the explicit $global:FailRounds resolve.
    $launch = {
        param($side, $round)
        if ($global:ThrowRounds.ContainsKey("${side}:${round}")) { throw "synthetic transient launch failure ${side}:${round}" }
        if ($global:FailRounds.ContainsKey("${side}:${round}")) { return $null }
        New-RoundFile $side $round @('M1', 'M2')
    }

    # A. happy path: 1 warmup + 3 measured rounds -> 3 dense reps on both sides.
    $global:FailRounds = @{}
    $accMain = Join-Path $microIntTmp 'acc-main.jsonl'; $accPr = Join-Path $microIntTmp 'acc-pr.jsonl'
    $res = Invoke-MicroInterleaved -LaunchRep $launch -RepCount 3 -WarmupCount 1 -MainOut $accMain -PrOut $accPr
    Assert-True ($null -ne $res -and $res.Reps -eq 3) '[interleave] 1 warmup + 3 measured rounds -> 3 kept reps'
    $mL = @(Get-Content $accMain); $pL = @(Get-Content $accPr)
    Assert-True ($mL.Count -eq 6 -and $pL.Count -eq 6) '[interleave] each accumulator has 3 reps x 2 benches = 6 lines'
    Assert-True ((Get-RepSeq $mL 'M1') -eq '0,1,2') '[interleave] M1 renumbered to dense 0,1,2 (warmup round not numbered)'
    Assert-True ((Get-RepSeq $pL 'M2') -eq '0,1,2') '[interleave] pr side carries the SAME dense indices (paired by repetition value)'

    # B. one-sided failure drops the WHOLE round; surviving reps stay dense (no gap).
    $global:FailRounds = @{ 'pr:3' = $true }   # round 3: pr launch fails
    $accMain2 = Join-Path $microIntTmp 'acc2-main.jsonl'; $accPr2 = Join-Path $microIntTmp 'acc2-pr.jsonl'
    $res2 = Invoke-MicroInterleaved -LaunchRep $launch -RepCount 3 -WarmupCount 1 -MainOut $accMain2 -PrOut $accPr2
    Assert-True ($null -ne $res2 -and $res2.Reps -eq 2) '[interleave] one-sided round failure drops both -> 2 kept (not 3)'
    $mL2 = @(Get-Content $accMain2); $pL2 = @(Get-Content $accPr2)
    Assert-True ($mL2.Count -eq 4 -and $pL2.Count -eq 4) '[interleave] drop-both keeps main/pr line counts equal'
    Assert-True ((Get-RepSeq $mL2 'M1') -eq '0,1') '[interleave] after a dropped round, surviving reps stay dense 0,1 (no gap)'
    Assert-True (@($mL2 | Where-Object { $_ -match '"benchId":"M1"' }).Count -eq 2) '[interleave] the failed round''s main file was NOT appended (no orphan rep)'

    # C. fewer than 2 paired rounds survive -> $null (too few for a paired CI).
    $global:FailRounds = @{ 'pr:3' = $true; 'pr:4' = $true }   # only round 2 survives
    $accMain3 = Join-Path $microIntTmp 'acc3-main.jsonl'; $accPr3 = Join-Path $microIntTmp 'acc3-pr.jsonl'
    $res3 = Invoke-MicroInterleaved -LaunchRep $launch -RepCount 3 -WarmupCount 1 -MainOut $accMain3 -PrOut $accPr3
    Assert-True ($null -eq $res3) '[interleave] <2 paired rounds survive -> $null (omit micro section)'

    # D. stale accumulators are cleared before the run (no cross-run bleed).
    $accMain4 = Join-Path $microIntTmp 'acc4-main.jsonl'; $accPr4 = Join-Path $microIntTmp 'acc4-pr.jsonl'
    Set-Content -LiteralPath $accMain4 -Value 'STALE' -Encoding UTF8
    Set-Content -LiteralPath $accPr4 -Value 'STALE' -Encoding UTF8
    $global:FailRounds = @{}
    $res4 = Invoke-MicroInterleaved -LaunchRep $launch -RepCount 2 -WarmupCount 0 -MainOut $accMain4 -PrOut $accPr4
    Assert-True ($null -ne $res4 -and @(Get-Content $accMain4 | Where-Object { $_ -match 'STALE' }).Count -eq 0) '[interleave] pre-existing accumulator content is cleared before appending'

    # E. a launcher that THROWS (transient runner hiccup) is caught and drops only that
    #    round like a $null return — surviving reps are kept and the leg still produces a
    #    result, rather than the exception aborting the whole micro leg.
    $global:FailRounds = @{}
    $global:ThrowRounds = @{ 'pr:3' = $true }   # round 3: pr launch throws
    $accMain5 = Join-Path $microIntTmp 'acc5-main.jsonl'; $accPr5 = Join-Path $microIntTmp 'acc5-pr.jsonl'
    $res5 = Invoke-MicroInterleaved -LaunchRep $launch -RepCount 3 -WarmupCount 1 -MainOut $accMain5 -PrOut $accPr5
    Assert-True ($null -ne $res5 -and $res5.Reps -eq 2) '[interleave] a throwing launch drops only that round (not the whole leg) -> 2 kept'
    Assert-True ((Get-RepSeq @(Get-Content $accMain5) 'M1') -eq '0,1') '[interleave] reps surviving a thrown round stay dense 0,1'
    $global:ThrowRounds = @{}
}
finally {
    Remove-Item function:New-RoundFile -ErrorAction SilentlyContinue
    Remove-Item function:Get-RepSeq -ErrorAction SilentlyContinue
    Remove-Variable -Name FailRounds -Scope Global -ErrorAction SilentlyContinue
    Remove-Variable -Name ThrowRounds -Scope Global -ErrorAction SilentlyContinue
    if (Test-Path $microIntTmp) { Remove-Item $microIntTmp -Recurse -Force -ErrorAction SilentlyContinue }
}

# cleanup
foreach ($d in @($baseTree, $prTree, $OutDir, $exeDir)) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
if ($script:Fail -gt 0) {
    Write-Host "Run-PerfBenchmark tests: $script:Pass passed, $script:Fail FAILED" -ForegroundColor Red
    $script:Failures | ForEach-Object { Write-Host "  FAIL: $_" -ForegroundColor Red }
    exit 1
}
Write-Host "PASSED: all $script:Pass assertions" -ForegroundColor Green
# Explicit success exit: the perf-lib-tests workflow invokes this via `pwsh -Command
# ". '<script>'"` (dot-source), under which a non-zero $LASTEXITCODE left over from the
# stubbed `dotnet` build-failure scenario leaks into the process exit code on Linux even
# though every assertion passed. Pin the exit code to the test result. (Mirrors
# PerfLib.Tests.ps1.)
exit 0
