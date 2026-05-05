param(
    [int]$Iterations = 100,
    [string]$LogDir = "$env:TEMP\flake-runs",
    [string]$Project = "tests/Reactor.Tests"
)

$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

$summaryPath = Join-Path $LogDir 'summary.txt'
$flakePath   = Join-Path $LogDir 'flakes.txt'

# ANSI escape stripper (dotnet's default console logger emits color codes)
$ansiRegex = "`e\[[0-9;]*[A-Za-z]"
# Pattern matches xUnit's "Failed Fully.Qualified.Test.Name [123 ms]" / "[< 1 ms]" lines
$failLineRegex = '^\s*Failed\s+(?<name>[^\s].*?)\s+\[[^\]]+\]\s*$'
# Summary line: "Failed!  - Failed:     2, Passed: ..., Skipped: ..., Total: ..."
$summaryRegex  = 'Failed:\s*(?<f>\d+),\s*Passed:\s*(?<p>\d+),\s*Skipped:\s*(?<s>\d+),\s*Total:\s*(?<t>\d+)'

$runResults = @()
$failureCounts = @{}

$globalSw = [System.Diagnostics.Stopwatch]::StartNew()

for ($i = 1; $i -le $Iterations; $i++) {
    $runLog = Join-Path $LogDir ("run-{0:D3}.log" -f $i)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    & dotnet test $Project -c Release --no-build --nologo 2>&1 |
        Tee-Object -FilePath $runLog | Out-Null

    $sw.Stop()
    $exit = $LASTEXITCODE

    $rawContent = Get-Content -LiteralPath $runLog -Raw
    $content = [regex]::Replace($rawContent, $ansiRegex, '')
    $failed = 0; $passed = 0; $skipped = 0; $total = 0
    if ($content -match $summaryRegex) {
        $failed  = [int]$Matches['f']
        $passed  = [int]$Matches['p']
        $skipped = [int]$Matches['s']
        $total   = [int]$Matches['t']
    }

    $failedTests = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($line in ($content -split "`r?`n")) {
        if ($line -match $failLineRegex) {
            [void]$failedTests.Add($Matches['name'])
        }
    }
    foreach ($t in $failedTests) {
        if (-not $failureCounts.ContainsKey($t)) { $failureCounts[$t] = 0 }
        $failureCounts[$t]++
    }

    $runResults += [pscustomobject]@{
        Run         = $i
        ExitCode    = $exit
        Failed      = $failed
        Passed      = $passed
        Skipped     = $skipped
        Total       = $total
        DurationS   = [math]::Round($sw.Elapsed.TotalSeconds, 1)
        FailedTests = ($failedTests -join '; ')
    }

    $tag = if ($failed -gt 0 -or $exit -ne 0) { 'FAIL' } else { 'pass' }
    $msg = "[{0:D3}/{1}] {2}  failed={3} passed={4} skipped={5} total={6}  {7:N1}s  exit={8}" -f `
        $i, $Iterations, $tag, $failed, $passed, $skipped, $total, $sw.Elapsed.TotalSeconds, $exit
    Write-Host $msg

    # Append progress to summary file as we go
    Add-Content -LiteralPath $summaryPath -Value $msg
}

$globalSw.Stop()

$totalRuns   = $runResults.Count
$cleanRuns   = ($runResults | Where-Object { $_.Failed -eq 0 -and $_.ExitCode -eq 0 }).Count
$failedRuns  = $totalRuns - $cleanRuns

$report = @()
$report += "===== flake-loop results ====="
$report += "Iterations           : $totalRuns"
$report += "Clean runs (0 fails) : $cleanRuns"
$report += "Runs with failures   : $failedRuns"
$report += "Wall-clock           : $([math]::Round($globalSw.Elapsed.TotalMinutes,2)) min"
$report += ""

if ($failureCounts.Count -eq 0) {
    $report += "No failing tests observed across $totalRuns runs."
} else {
    $report += "Tests that failed at least once (test : fail-count / $totalRuns)"
    $report += "----------------------------------------------------------------"
    $sorted = $failureCounts.GetEnumerator() | Sort-Object -Property Value -Descending
    foreach ($kv in $sorted) {
        $isFlake = ($kv.Value -lt $totalRuns)
        $tag = if ($isFlake) { 'FLAKE     ' } else { 'CONSISTENT' }
        $report += ("{0}  {1,4} / {2}  {3}" -f $tag, $kv.Value, $totalRuns, $kv.Key)
    }
}

$report | Tee-Object -FilePath $flakePath
Write-Host ""
Write-Host "Per-run log:    $summaryPath"
Write-Host "Flake report:   $flakePath"
Write-Host "Per-iter logs:  $LogDir\run-*.log"
