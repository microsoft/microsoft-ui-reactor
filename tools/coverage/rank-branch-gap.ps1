<#
.SYNOPSIS
  Ranks files by how much branch% trails line%, surfacing branch-shaped
  uplift targets that the per-class line-rate sort misses.

.DESCRIPTION
  The default gap-report.md is sorted by missed lines, which biases toward
  files with the biggest line-coverage holes. But when global branch% is
  the bottleneck (it usually is — branch% lags line% by ~12 points across
  the repo), a file with low branch but middling line% is a higher-ROI
  target than a slightly larger file with line-shaped gaps.

  This script reads coverage/merged.cobertura.xml and prints the top N
  classes ranked by (line% - branch%) descending, filtered to files in
  src/Reactor with line% below 85 and at least -MinMissed missed lines.

.PARAMETER Top
  Number of rows to print. Default 15.

.PARAMETER MinMissed
  Minimum missed-line count to include a class. Filters out tiny utility
  classes whose 100%/0% branch ratios are coincidental. Default 30.

.EXAMPLE
  pwsh tools/coverage/rank-branch-gap.ps1
  pwsh tools/coverage/rank-branch-gap.ps1 -Top 25 -MinMissed 50

.NOTES
  The merged.cobertura.xml file is written by tools/coverage/run-coverage.ps1.
  Run that first if the file is missing or stale. Branch% lower than the
  per-method numbers below the file in dotnet-coverage output is expected —
  this script computes branch% from the class-level `branch-rate` attribute,
  which is already normalized.
#>
[CmdletBinding()]
param(
  [int]$Top = 15,
  [int]$MinMissed = 30
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$merged = Join-Path $repoRoot 'coverage/merged.cobertura.xml'
if (-not (Test-Path $merged)) {
  throw "coverage/merged.cobertura.xml not found. Run tools/coverage/run-coverage.ps1 first."
}

[xml]$x = Get-Content $merged
$classes = $x.SelectNodes('//class[@filename]')
$rows = foreach ($c in $classes) {
  $fn = $c.filename
  if ($fn -notmatch 'src.Reactor.') { continue }
  $line = [double]$c.'line-rate'
  if ($line -ge 0.85) { continue }
  $branch = [double]$c.'branch-rate'
  $lines = $c.SelectNodes('lines/line')
  $total = $lines.Count
  $covered = ($lines | Where-Object { $_.hits -ne '0' }).Count
  $missed = $total - $covered
  if ($missed -lt $MinMissed) { continue }
  [pscustomobject]@{
    File      = ($fn -replace '.*src.Reactor.', '')
    Line      = [math]::Round($line * 100, 1)
    Branch    = [math]::Round($branch * 100, 1)
    BranchGap = [math]::Round(($line - $branch) * 100, 1)
    Missed    = $missed
  }
}

$rows |
  Sort-Object BranchGap -Descending |
  Select-Object -First $Top |
  Format-Table -AutoSize
