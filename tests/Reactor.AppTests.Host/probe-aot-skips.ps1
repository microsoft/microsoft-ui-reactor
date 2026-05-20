#!/usr/bin/env pwsh
# Probe each AOT-skipped fixture in isolation under --no-aot-skip and categorize
# behavior: PASS, ASSERT_FAIL, NATIVE_CRASH, HANG, TIMEOUT_NO_SIGNAL.
#
# Use this after AOT framework changes to find stale entries in
# DefaultAotSkipPatterns that have started passing, and to triage what's still
# broken. Requires the AOT-published Host at
#   tests/Reactor.AppTests.Host/bin/x64/Release/net10.0-windows10.0.22621.0/win-x64/publish/Reactor.AppTests.Host.exe
# Publish first with:
#   dotnet publish tests/Reactor.AppTests.Host -p:PublishAotInternal=true -p:Platform=x64 -r win-x64 -c Release
# See docs/aot-support.md for the full workflow.

param(
    [int]$HangTimeoutSec = 10,
    [int]$ProcessTimeoutSec = 30,
    [string]$OutputCsv,
    [string[]]$Only
)

$ErrorActionPreference = 'Stop'

# Repo root is two levels up: tests/Reactor.AppTests.Host/probe-aot-skips.ps1 → repo root.
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$exe = Join-Path $repoRoot 'tests\Reactor.AppTests.Host\bin\x64\Release\net10.0-windows10.0.22621.0\win-x64\publish\Reactor.AppTests.Host.exe'
if (-not (Test-Path $exe)) {
    throw "AOT-published host not found at: $exe`nPublish first with: dotnet publish tests/Reactor.AppTests.Host -p:PublishAotInternal=true -p:Platform=x64 -r win-x64 -c Release"
}

if (-not $OutputCsv) {
    $outDir = Join-Path $repoRoot '.aot_runs'
    if (-not (Test-Path $outDir)) { [void](New-Item -ItemType Directory $outDir) }
    $OutputCsv = Join-Path $outDir 'probe_results.csv'
}

# Extract DefaultAotSkipPatterns string literals from SelfTestRunner.cs.
$runnerCs = Get-Content (Join-Path $repoRoot 'tests\Reactor.AppTests.Host\SelfTest\SelfTestRunner.cs') -Raw
$skipBlockMatch = [regex]::Match($runnerCs, '(?s)private static readonly string\[\] DefaultAotSkipPatterns\s*=\s*\{(.*?)\};')
if (-not $skipBlockMatch.Success) { throw 'Could not locate DefaultAotSkipPatterns block in SelfTestRunner.cs' }
$patterns = [regex]::Matches($skipBlockMatch.Groups[1].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
$exactPatterns = $patterns | Where-Object { -not $_.EndsWith('*') }
$wildcardPatterns = $patterns | Where-Object { $_.EndsWith('*') }

# Get full fixture list once.
$allFixtures = & $exe --list-fixtures | Where-Object { $_ -match '\S' }

# Expand wildcards against the fixture registry.
$fixturesToTest = New-Object System.Collections.Generic.HashSet[string]
foreach ($p in $exactPatterns) { if ($allFixtures -contains $p) { [void]$fixturesToTest.Add($p) } }
foreach ($wp in $wildcardPatterns) {
    $prefix = $wp.TrimEnd('*')
    foreach ($f in $allFixtures) { if ($f.StartsWith($prefix)) { [void]$fixturesToTest.Add($f) } }
}

if ($Only) { $fixturesToTest = [System.Collections.Generic.HashSet[string]]::new([string[]]$Only) }

Write-Host "Probing $($fixturesToTest.Count) skipped fixtures (hang_timeout=${HangTimeoutSec}s, proc_timeout=${ProcessTimeoutSec}s)" -ForegroundColor Cyan

$results = New-Object System.Collections.Generic.List[object]
$i = 0
foreach ($fixture in ($fixturesToTest | Sort-Object)) {
    $i++
    Write-Host -NoNewline "[$i/$($fixturesToTest.Count)] $fixture ... "

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = "--self-test --no-aot-skip --filter $fixture"
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables["REACTOR_SELFTEST_HANG_TIMEOUT_SECONDS"] = "$HangTimeoutSec"

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    $soutTask = $p.StandardOutput.ReadToEndAsync()
    $serrTask = $p.StandardError.ReadToEndAsync()
    $exited = $p.WaitForExit($ProcessTimeoutSec * 1000)
    if (-not $exited) {
        try { $p.Kill($true) } catch {}
        $p.WaitForExit()
    }
    $sw.Stop()

    $sout = $soutTask.Result
    $serr = $serrTask.Result
    $exitCode = $p.ExitCode

    # Categorize based on exit code + watchdog signal.
    $category = 'UNKNOWN'
    $detail = ''
    if (-not $exited) {
        $category = 'TIMEOUT_NO_SIGNAL'
        $detail = "process timeout (${ProcessTimeoutSec}s) without hang signal"
    } elseif (($sout + $serr) -match 'HANG_DETECTED:\s*(\S+)') {
        $category = 'HANG'
        $detail = "watchdog fired: $($Matches[1])"
    } elseif ($exitCode -eq -1073740791) {
        # 0xC0000409 as a signed 32-bit Win32 exit code. PowerShell hex literals
        # widen to Int64 so a direct `-eq 0xC0000409` would never match a
        # negative Process.ExitCode — compare the normalized signed form.
        $category = 'NATIVE_CRASH'
        $detail = "STATUS_STACK_BUFFER_OVERRUN (0xC0000409)"
    } elseif ($exitCode -lt 0) {
        $category = 'NATIVE_CRASH'
        $detail = "exit=$exitCode (native fault)"
    } elseif ($exitCode -eq 0) {
        $category = 'PASS'
        $detail = 'exit=0'
    } else {
        $category = 'ASSERT_FAIL'
        $detail = "exit=$exitCode"
        $notOk = ($sout -split "`n") | Where-Object { $_ -match '^not ok' } | Select-Object -First 1
        if ($notOk) { $detail += " | $($notOk.Trim())" }
    }

    $color = switch ($category) {
        'PASS' { 'Green' }
        'HANG' { 'Yellow' }
        'NATIVE_CRASH' { 'Red' }
        'ASSERT_FAIL' { 'Magenta' }
        default { 'Gray' }
    }
    Write-Host -ForegroundColor $color "$category ($([math]::Round($sw.Elapsed.TotalSeconds,1))s) $detail"

    $results.Add([pscustomobject]@{
        Fixture = $fixture
        Category = $category
        ExitCode = $exitCode
        ElapsedSec = [math]::Round($sw.Elapsed.TotalSeconds, 2)
        Detail = $detail
    })
}

$results | Export-Csv -Path $OutputCsv -NoTypeInformation
Write-Host "`nWritten: $OutputCsv" -ForegroundColor Cyan
$results | Group-Object Category | Sort-Object Count -Descending | Format-Table Count, Name -AutoSize

