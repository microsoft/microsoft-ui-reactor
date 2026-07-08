#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Aggregate winapp-ui E2E stress results into a reliability report (retries ON or OFF).

.DESCRIPTION
  Consumes the artifacts produced by the `e2e` target in ci-stress.yml:
    * iters-<shard>.csv  - one row per iteration: shard,iter,exit (dotnet-test exit code)
    * e2e-<shard>-<i>.trx - the MSTest TRX for that iteration
  and reports:
    * Suite-level green rate      = iterations with dotnet-test exit 0 / total iterations
    * Interactive-env availability = 1 - (inconclusive results / total results)
    * Per-test failure leaderboard = failed / (runs - inconclusive), highest rate first
    * Tests ever Inconclusive      = environment / input-injection blockers

  Output goes to the console and, when GITHUB_STEP_SUMMARY is set, is appended there as
  Markdown so it renders on the workflow run's summary page.

.NOTES
  Iteration green/red uses the dotnet-test exit code, which is robust to how MSTest records
  [E2eRetry] attempts. The per-test leaderboard is mined from the TRX outcomes, which record the
  final reported outcome per test: MSTest's retry surfaces only the last attempt (RetryResult
  TryGetLast), so a failure healed by a later retry generally does NOT appear here as a failed
  attempt in a retries-ON run. True per-attempt flake is therefore measured with the retries-OFF
  lane (REACTOR_E2E_RETRIES=0), where each test runs once and its single real outcome is recorded.

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
$resEnvInconclusive = 0

# Same signature the ci.yml E2E gate uses to catch a guard-fired Inconclusive (locked / disconnected
# desktop, or no input injection). If any of these appear the interactivity guard actually fired — a
# potential failure-masking signal — so surface them loudly instead of folding them into the benign
# "prerequisite skip" bucket.
$guardMsgRegex = 'cannot inject synthetic input|workstation is|locked desktop|disconnected session'
$prereqMsgRegex = 'published|REACTOR_SPEC051|DISCOVER_PUBLISHED|missing executable'

function Get-InconclusiveCategory([string]$msg) {
    if ($msg -match $guardMsgRegex) { return 'Environmental/guard' }
    if ($msg -match $prereqMsgRegex) { return 'Prerequisite' }
    return 'Other'
}

# Precedence for "worst category seen" per test: Environmental/guard > Prerequisite > Other > None.
function Get-CategoryRank([string]$cat) {
    switch ($cat) {
        'Environmental/guard' { 3 }
        'Prerequisite'        { 2 }
        'Other'               { 1 }
        default               { 0 }
    }
}

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
                $tests[$name] = [pscustomobject]@{ Test = $name; Runs = 0; Passed = 0; Failed = 0; Inconclusive = 0; Category = 'None'; IncMsg = '' }
            }
            $rec = $tests[$name]
            $rec.Runs++
            if ($failedBucket -contains $outcome) { $rec.Failed++; $iterFailed++ }
            elseif ($inconBucket -contains $outcome) {
                $rec.Inconclusive++; $resInconclusive++
                $msgNode = $n.Node.SelectSingleNode("*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
                $msg = if ($msgNode) { $msgNode.InnerText } else { '' }
                $cat = Get-InconclusiveCategory $msg
                if ($cat -eq 'Environmental/guard') { $resEnvInconclusive++ }
                # Keep the most-serious category seen for this test (Environmental > Prerequisite > Other).
                if ((Get-CategoryRank $cat) -gt (Get-CategoryRank $rec.Category)) { $rec.Category = $cat }
                if ([string]::IsNullOrEmpty($rec.IncMsg) -and -not [string]::IsNullOrEmpty($msg)) {
                    $rec.IncMsg = ($msg -replace '\s+', ' ').Trim()
                }
            }
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

$retriesRaw = $env:REACTOR_E2E_RETRIES
$retriesMode = if ($retriesRaw -eq '0') { 'retries OFF (REACTOR_E2E_RETRIES=0)' }
    elseif ([string]::IsNullOrEmpty($retriesRaw)) { 'retries ON (default)' }
    else { "REACTOR_E2E_RETRIES=$retriesRaw" }

# Leaderboard: highest failure rate first (failed / (runs - inconclusive)), so a test with more
# excluded Inconclusive results isn't unfairly ranked below one with a lower true rate; absolute
# failed count breaks ties. Guard the denominator against zero (all-Inconclusive → rate 0).
$leader = @($tests.Values |
    Where-Object { $_.Failed -gt 0 } |
    Sort-Object -Property `
        @{ Expression = { if (($_.Runs - $_.Inconclusive) -gt 0) { $_.Failed / ($_.Runs - $_.Inconclusive) } else { 0 } }; Descending = $true }, `
        @{ Expression = { $_.Failed }; Descending = $true })

$everIncon = @($tests.Values |
    Where-Object { $_.Inconclusive -gt 0 } |
    Sort-Object -Property @{ Expression = { $_.Inconclusive }; Descending = $true })

# ---- Emit report ----
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("## $Title")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("| Metric | Value |")
[void]$sb.AppendLine("|---|---|")
[void]$sb.AppendLine("| Suite-level green rate ($retriesMode) | **$greenPct** ($iterGreen / $iterTotal iterations) |")
[void]$sb.AppendLine("| Interactive-env availability | $envAvail ($($resTotal - $resInconclusive) / $resTotal results) |")
[void]$sb.AppendLine("| Environmental/guard Inconclusive results | $resEnvInconclusive |")
[void]$sb.AppendLine("| Distinct tests observed | $($tests.Count) |")
[void]$sb.AppendLine("| Tests that failed >=1 attempt | $($leader.Count) |")
[void]$sb.AppendLine("")

if ($leader.Count -gt 0) {
    [void]$sb.AppendLine("### Failure leaderboard (per-test final outcome, $retriesMode)")
    [void]$sb.AppendLine("Fail% = failed / (runs - inconclusive), counting each iteration's final reported outcome. In a retries-ON run a flake healed by ``[E2eRetry]`` is reported Passed and won't appear here — the retries-OFF lane (``REACTOR_E2E_RETRIES=0``) is where true per-attempt flake surfaces.")
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

if ($resEnvInconclusive -gt 0) {
    [void]$sb.AppendLine("> [!WARNING]")
    [void]$sb.AppendLine("> **$resEnvInconclusive environmental/guard Inconclusive result(s) detected.** The interactivity guard fired (locked/disconnected desktop or no input injection). On a healthy CI runner this should be **0** — investigate before trusting the green rate, because a guard trip coinciding with a real failure is the classic masking vector. (The ``E2eRetry`` anti-masking policy prevents a first-run failure from being downgraded, and the ci.yml E2E gate turns these red in the main job.)")
    [void]$sb.AppendLine("")
}

if ($everIncon.Count -gt 0) {
    [void]$sb.AppendLine("### Tests ever Inconclusive")
    [void]$sb.AppendLine("**Environmental/guard** = interactivity guard fired (investigate); **Prerequisite** = self-skip for a missing prereq (e.g. an unpublished sample); **Other** = uncategorised.")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Test | Category | Inconclusive | Runs | Sample message |")
    [void]$sb.AppendLine("|---|---|--:|--:|---|")
    foreach ($r in ($everIncon | Select-Object -First 40)) {
        $m = $r.IncMsg -replace '\|', '/'
        if ($m.Length -gt 90) { $m = $m.Substring(0, 90) + '...' }
        [void]$sb.AppendLine("| $($r.Test) | $($r.Category) | $($r.Inconclusive) | $($r.Runs) | $m |")
    }
    [void]$sb.AppendLine("")
}

$report = $sb.ToString()
Write-Host $report
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $report }
