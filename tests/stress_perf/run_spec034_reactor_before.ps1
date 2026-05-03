# run_spec034_reactor_before.ps1 — bench the pre-spec-034 StressPerf.Reactor
# binary that lives in the ../reactor1-prespec034 worktree (built from commit
# 247a525, parent of the Component A merge). Same shape as run_spec034_bench.ps1
# but pinned to a single variant + a different exe path.

param(
  [int] $DurationSeconds = 10,
  [int[]] $Percents = @(20, 50, 100),
  [string] $OutCsv = (Join-Path $PSScriptRoot 'baselines\spec-034-reactor-before.csv'),
  [string] $OutLog = (Join-Path $PSScriptRoot 'baselines\spec-034-reactor-before.log'),
  [string] $PrespecRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\reactor1-prespec034')).Path
)

$ErrorActionPreference = 'Continue'

$tfm = 'net9.0-windows10.0.22621.0'
$plat = 'ARM64'
$cfg = 'Release'
$exe = Join-Path $PrespecRoot "tests\stress_perf\StressPerf.Reactor\bin\$plat\$cfg\$tfm\StressPerf.Reactor.exe"
$reportName = 'StressPerf.Reactor'

if (-not (Test-Path $exe)) { throw "Missing exe: $exe" }

$baselineDir = Split-Path -Parent $OutCsv
if (-not (Test-Path $baselineDir)) { New-Item -ItemType Directory -Path $baselineDir | Out-Null }

Remove-Item $OutLog -ErrorAction SilentlyContinue
Remove-Item $OutCsv -ErrorAction SilentlyContinue
"Pre-spec-034 Reactor bench @ $(Get-Date -Format o) ($plat $cfg, exe=$exe)" | Tee-Object -FilePath $OutLog -Append | Out-Host

function Parse-Field {
  param([string]$Report, [string]$Field)
  $m = [regex]::Match($Report, "(?m)^\s*$([regex]::Escape($Field))\s*:\s*(.+?)\s*$")
  if ($m.Success) { return $m.Groups[1].Value.Trim() } else { return '' }
}
function Num { param($v) if ($v -match '^([-\d\.]+)') { return [double]$matches[1] } else { return [double]::NaN } }
function Int_ { param($v) if ($v -match '^(\d+)') { return [int]$matches[1] } else { return 0 } }

$rows = @()
foreach ($pct in $Percents) {
  "" | Tee-Object -FilePath $OutLog -Append | Out-Host
  "==== Reactor (pre-spec-034) @ $pct% / ${DurationSeconds}s ====" | Tee-Object -FilePath $OutLog -Append | Out-Host

  $exeName = [System.IO.Path]::GetFileNameWithoutExtension($exe)
  Get-Process -Name $exeName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 250

  $exeDir = Split-Path -Parent $exe
  Remove-Item (Join-Path $exeDir "$reportName.report.txt") -ErrorAction SilentlyContinue

  $proc = Start-Process -FilePath $exe -ArgumentList @('--headless','--percent',"$pct",'--duration',"$DurationSeconds") -PassThru
  $deadline = (Get-Date).AddSeconds($DurationSeconds + 10)
  while (-not $proc.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
  if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }

  $reportPath = Join-Path $exeDir "$reportName.report.txt"
  if (-not (Test-Path $reportPath)) {
    "  (no report file)" | Tee-Object -FilePath $OutLog -Append | Out-Host
    $rows += [pscustomobject]@{ Variant='Reactor (pre-spec-034)'; Percent=$pct; Renders=0; FpsAvg=0; UpdateMsAvg=0; ReconcileMsAvg=0; PeakMemMB=0 }
    continue
  }
  $report = Get-Content $reportPath -Raw
  $report | Tee-Object -FilePath $OutLog -Append | Out-Host

  $renders   = Int_ (Parse-Field $report 'Total Renders')
  $fps       = Num  (Parse-Field $report 'Avg FPS')
  $updMs     = Num  (Parse-Field $report 'Avg Update')
  $reconMs   = Num  (Parse-Field $report 'Avg Reconcile')
  $peakMem   = Num  (Parse-Field $report 'Peak Memory')

  $rows += [pscustomobject]@{
    Variant       = 'Reactor (pre-spec-034)'
    Percent       = $pct
    Renders       = $renders
    RendersPerSec = if ($DurationSeconds -gt 0) { [math]::Round($renders / $DurationSeconds, 2) } else { 0 }
    FpsAvg        = $fps
    UpdateMsAvg   = $updMs
    ReconcileMsAvg= $reconMs
    PeakMemMB     = $peakMem
  }
}

$rows | Sort-Object Percent | Export-Csv -Path $OutCsv -NoTypeInformation
"" | Tee-Object -FilePath $OutLog -Append | Out-Host
"==== RESULTS ====" | Tee-Object -FilePath $OutLog -Append | Out-Host
$rows | Sort-Object Percent | Format-Table -AutoSize | Out-String | Tee-Object -FilePath $OutLog -Append | Out-Host
"DONE @ $(Get-Date -Format o)" | Tee-Object -FilePath $OutLog -Append | Out-Host
"`nCSV: $OutCsv`nLog: $OutLog" | Out-Host
