#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Aggregate winapp-ui E2E stress results (retries-ON baseline) into a reliability report.

.DESCRIPTION
  Consumes the artifacts produced by the `e2e` target in ci-stress.yml:
    * iters-<shard>.csv  - one row per iteration: shard,iter,exit (dotnet-test exit code)
    * e2e-<shard>-<i>.trx - the MSTest TRX for that iteration
  and reports:
    * Suite-level green rate      = iterations with dotnet-test exit 0 / total iterations
    * Interactive-env availability = 1 - (inconclusive results / total results)
    * Per-test failure leaderboard = failed / (runs - inconclusive), worst first
    * Tests ever Inconclusive      = environment / input-injection blockers

  Output goes to the console and, when GITHUB_STEP_SUMMARY is set, is appended there as
  Markdown so it renders on the workflow run's summary page.

.NOTES
  Iteration green/red uses the dotnet-test exit code, which is robust to how MSTest records
  [Retry(3)] attempts in the TRX. The TRX is only mined for per-test detail: a test that
  failed one attempt but was healed by [Retry(3)] can still surface here as a failed attempt
  ("healed by retry" signal) without making the suite iteration red.

  This is report-only: it never sets a non-zero exit code, so it never fails the shard.
#>
param(
    [string[]]$Path = @('.'),
    [string]$Title = 'E2E winapp-ui stress reliability'
)

$ErrorActionPreference = 'Stop'

$roots = @()
foreach ($p in $Path) { if (Test-Path $p) { $roots += (Resolve-Path $p).Path } }
if ($roots.Count -eq 0) { $roots = @((Get-Location).Path) }

$csvFiles = @(Get-ChildItem -Path $roots -Recurse -Filter 'iters-*.csv' -File -ErrorAction SilentlyContinue)
$trxFiles = @(Get-ChildItem -Path $roots -Recurse -Filter '*.trx' -File -ErrorAction SilentlyContinue)

# ---- Suite-level green rate (exit-code based) ----
$iterTotal = 0
$iterGreen = 0
foreach ($f in $csvFiles) {
    try {
        Import-Csv $f.FullName | ForEach-Object {
            $iterTotal++
            if ([int]$_.exit -eq 0) { $iterGreen++ }
        }
    }
    catch { Write-Warning "Could not read $($f.FullName): $_" }
}

# ---- Per-test detail + inconclusive rate (TRX based) ----
$tests = @{}
$trxIterTotal = 0
$trxIterGreen = 0
$resTotal = 0
$resInconclusive = 0
$failedBucket = @('Failed', 'Error', 'Timeout', 'Aborted')
$inconBucket = @('Inconclusive', 'NotExecuted', 'Pending', 'Warning', 'NotRunnable', 'Disconnected')

foreach ($t in $trxFiles) {
    try {
        $nodes = @(Select-Xml -Path $t.FullName -XPath "//*[local-name()='UnitTestResult']")
        if ($nodes.Count -eq 0) { continue }
        $trxIterTotal++
        $iterFailed = 0
        foreach ($n in $nodes) {
            $name = $n.Node.GetAttribute('testName')
            $outcome = $n.Node.GetAttribute('outcome')
            if ([string]::IsNullOrEmpty($name)) { continue }
            $resTotal++
            if (-not $tests.ContainsKey($name)) {
                $tests[$name] = [pscustomobject]@{ Test = $name; Runs = 0; Passed = 0; Failed = 0; Inconclusive = 0 }
            }
            $rec = $tests[$name]
            $rec.Runs++
            if ($failedBucket -contains $outcome) { $rec.Failed++; $iterFailed++ }
            elseif ($inconBucket -contains $outcome) { $rec.Inconclusive++; $resInconclusive++ }
            elseif ($outcome -eq 'Passed') { $rec.Passed++ }
        }
        if ($iterFailed -eq 0) { $trxIterGreen++ }
    }
    catch { Write-Warning "Could not parse $($t.FullName): $_" }
}

# Fall back to TRX-derived iteration counts when no exit-code CSVs were found.
if ($iterTotal -eq 0 -and $trxIterTotal -gt 0) {
    $iterTotal = $trxIterTotal
    $iterGreen = $trxIterGreen
}

function Format-Pct($num, $den) {
    if ($den -le 0) { return 'n/a' }
    return ('{0:N2}%' -f (100.0 * $num / $den))
}

$greenPct = Format-Pct $iterGreen $iterTotal
$envAvail = if ($resTotal -gt 0) { Format-Pct ($resTotal - $resInconclusive) $resTotal } else { 'n/a' }

$leader = @($tests.Values |
    Where-Object { $_.Failed -gt 0 } |
    Sort-Object -Property `
        @{ Expression = { $_.Failed }; Descending = $true }, `
        @{ Expression = { if (($_.Runs - $_.Inconclusive) -gt 0) { $_.Failed / ($_.Runs - $_.Inconclusive) } else { 0 } }; Descending = $true })

$everIncon = @($tests.Values |
    Where-Object { $_.Inconclusive -gt 0 } |
    Sort-Object -Property @{ Expression = { $_.Inconclusive }; Descending = $true })

# ---- Emit report ----
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("## $Title")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("| Metric | Value |")
[void]$sb.AppendLine("|---|---|")
[void]$sb.AppendLine("| Suite-level green rate (retries ON) | **$greenPct** ($iterGreen / $iterTotal iterations) |")
[void]$sb.AppendLine("| Interactive-env availability | $envAvail ($($resTotal - $resInconclusive) / $resTotal results) |")
[void]$sb.AppendLine("| Distinct tests observed | $($tests.Count) |")
[void]$sb.AppendLine("| Tests that failed >=1 attempt | $($leader.Count) |")
[void]$sb.AppendLine("")

if ($leader.Count -gt 0) {
    [void]$sb.AppendLine("### Failure leaderboard (attempt-level, retries ON)")
    [void]$sb.AppendLine("Fail% = failed / (runs - inconclusive). With ``[Retry(3)]`` a listed test may still have been *healed by retry* in the suite run, so this is an early-warning signal, not necessarily a red suite.")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Test | Failed | Runs | Inconclusive | Fail% |")
    [void]$sb.AppendLine("|---|--:|--:|--:|--:|")
    foreach ($r in ($leader | Select-Object -First 40)) {
        $den = $r.Runs - $r.Inconclusive
        $fp = if ($den -gt 0) { '{0:N2}%' -f (100.0 * $r.Failed / $den) } else { 'n/a' }
        [void]$sb.AppendLine("| $($r.Test) | $($r.Failed) | $($r.Runs) | $($r.Inconclusive) | $fp |")
    }
    [void]$sb.AppendLine("")
}

if ($everIncon.Count -gt 0) {
    [void]$sb.AppendLine("### Tests ever Inconclusive (environment / input-injection)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Test | Inconclusive | Runs |")
    [void]$sb.AppendLine("|---|--:|--:|")
    foreach ($r in ($everIncon | Select-Object -First 40)) {
        [void]$sb.AppendLine("| $($r.Test) | $($r.Inconclusive) | $($r.Runs) |")
    }
    [void]$sb.AppendLine("")
}

$report = $sb.ToString()
Write-Host $report
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $report }
