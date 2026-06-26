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
