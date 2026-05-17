<#
.SYNOPSIS
  Parses coverage/merged.cobertura.xml and prints a ranked gap report.

.DESCRIPTION
  Emits two reports:

    1. By file — line %, branch %, lines missed, branches missed.
       Sorted by absolute lines-missed (highest impact first).
    2. Hot spots — files under a chosen threshold sorted by lines-missed.

  The output is also written as Markdown to coverage/gap-report.md so it
  can be pasted into the tracking doc.

.PARAMETER ReportPath
  Path to the merged cobertura XML. Defaults to coverage/merged.cobertura.xml.

.PARAMETER Threshold
  Files with line% strictly below this number are flagged as hot spots.
  Default 85.

.PARAMETER Top
  How many top hot spots to print. Default 25.
#>
[CmdletBinding()]
param(
  [string]$ReportPath = "$PSScriptRoot/../../coverage/merged.cobertura.xml",
  [double]$Threshold  = 85,
  [int]$Top           = 25
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ReportPath)) {
  throw "Cobertura report not found: $ReportPath. Run tools/coverage/run-coverage.ps1 first."
}

[xml]$x = Get-Content $ReportPath

# Cobertura "class" nodes are per-file (one class per source file in the
# dotnet-coverage output). Aggregate by filename so partial classes merge.
$byFile = @{}
foreach ($cls in $x.SelectNodes('//class')) {
  $file = $cls.filename
  if (-not $file) { continue }
  if (-not $byFile.ContainsKey($file)) {
    $byFile[$file] = [pscustomobject]@{
      File             = $file
      LinesCovered     = 0
      LinesTotal       = 0
      BranchesCovered  = 0
      BranchesTotal    = 0
    }
  }
  $row = $byFile[$file]
  foreach ($l in $cls.SelectNodes('.//line')) {
    $row.LinesTotal++
    if ([int]$l.hits -gt 0) { $row.LinesCovered++ }
    if ($l.'condition-coverage' -match '\((\d+)/(\d+)\)') {
      $row.BranchesCovered += [int]$Matches[1]
      $row.BranchesTotal   += [int]$Matches[2]
    }
  }
}

$rows = $byFile.Values | ForEach-Object {
  $linePct = if ($_.LinesTotal -gt 0) { 100.0 * $_.LinesCovered / $_.LinesTotal } else { 100.0 }
  $brPct   = if ($_.BranchesTotal -gt 0) { 100.0 * $_.BranchesCovered / $_.BranchesTotal } else { 100.0 }
  [pscustomobject]@{
    File          = ($_.File -replace '\\','/')
    LinePct       = [math]::Round($linePct, 1)
    BranchPct     = [math]::Round($brPct, 1)
    LinesMissed   = $_.LinesTotal - $_.LinesCovered
    BranchesMissed= $_.BranchesTotal - $_.BranchesCovered
    LinesTotal    = $_.LinesTotal
    BranchesTotal = $_.BranchesTotal
  }
}

$hot = $rows |
  Where-Object { $_.LinePct -lt $Threshold -and $_.LinesTotal -ge 10 } |
  Sort-Object LinesMissed -Descending |
  Select-Object -First $Top

# Overall
$totLines    = ($rows | Measure-Object LinesTotal -Sum).Sum
$covLines    = ($rows | ForEach-Object { $_.LinesTotal - $_.LinesMissed } | Measure-Object -Sum).Sum
$totBranches = ($rows | Measure-Object BranchesTotal -Sum).Sum
$covBranches = ($rows | ForEach-Object { $_.BranchesTotal - $_.BranchesMissed } | Measure-Object -Sum).Sum
$overallLine   = if ($totLines    -gt 0) { [math]::Round(100.0 * $covLines    / $totLines, 2) } else { 0 }
$overallBranch = if ($totBranches -gt 0) { [math]::Round(100.0 * $covBranches / $totBranches, 2) } else { 0 }

Write-Host ""
Write-Host "===== Overall =====" -ForegroundColor Green
Write-Host ("  Line   : {0,6:F2}%  ({1}/{2})" -f $overallLine, $covLines, $totLines)
Write-Host ("  Branch : {0,6:F2}%  ({1}/{2})" -f $overallBranch, $covBranches, $totBranches)
Write-Host ""

Write-Host "===== Hot spots (line% < $Threshold, sorted by lines missed) =====" -ForegroundColor Yellow
$hot | Format-Table File, LinePct, BranchPct, LinesMissed, BranchesMissed, LinesTotal -AutoSize

# Markdown report
$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Coverage gap report")
[void]$md.AppendLine("")
[void]$md.AppendLine("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$md.AppendLine("Source:    `coverage/merged.cobertura.xml`")
[void]$md.AppendLine("")
[void]$md.AppendLine("## Overall")
[void]$md.AppendLine("")
[void]$md.AppendLine("| Metric | Coverage |")
[void]$md.AppendLine("|---|---|")
[void]$md.AppendLine("| Line   | $overallLine% ($covLines / $totLines) |")
[void]$md.AppendLine("| Branch | $overallBranch% ($covBranches / $totBranches) |")
[void]$md.AppendLine("")
[void]$md.AppendLine("## Hot spots (line% < $Threshold, top $Top by lines missed)")
[void]$md.AppendLine("")
[void]$md.AppendLine("| File | Line % | Branch % | Lines missed | Branches missed | Lines total |")
[void]$md.AppendLine("|---|---:|---:|---:|---:|---:|")
foreach ($r in $hot) {
  [void]$md.AppendLine("| ``$($r.File)`` | $($r.LinePct)% | $($r.BranchPct)% | $($r.LinesMissed) | $($r.BranchesMissed) | $($r.LinesTotal) |")
}

$mdPath = Join-Path (Split-Path $ReportPath -Parent) 'gap-report.md'
[System.IO.File]::WriteAllText($mdPath, $md.ToString())
Write-Host "Markdown report written: $mdPath" -ForegroundColor Cyan
