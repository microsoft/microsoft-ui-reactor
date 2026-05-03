# run_spec034_bench.ps1 — focused bench for spec 034 close-out.
# Runs Reactor / ReactorOptimized / Direct headless at 20/50/100 % @ 10 s,
# parses the per-variant *.report.txt, emits a CSV under baselines/.

param(
  [int] $DurationSeconds = 10,
  [int[]] $Percents = @(20, 50, 100),
  [string] $OutCsv = (Join-Path $PSScriptRoot 'baselines\spec-034-final.csv'),
  [string] $OutLog = (Join-Path $PSScriptRoot 'baselines\spec-034-final.log')
)

$ErrorActionPreference = 'Continue'

$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$tfm = 'net9.0-windows10.0.22621.0'
$plat = 'ARM64'
$cfg = 'Release'

$variants = @(
  [pscustomobject]@{ Name='Reactor';          Exe="$repo\tests\stress_perf\StressPerf.Reactor\bin\$plat\$cfg\$tfm\StressPerf.Reactor.exe";                   ReportName='StressPerf.Reactor' }
  [pscustomobject]@{ Name='ReactorOptimized'; Exe="$repo\tests\stress_perf\StressPerf.ReactorOptimized\bin\$plat\$cfg\$tfm\StressPerf.ReactorOptimized.exe"; ReportName='StressPerf.ReactorOptimized' }
  [pscustomobject]@{ Name='Direct';           Exe="$repo\tests\stress_perf\StressPerf.Direct\bin\$plat\$cfg\$tfm\StressPerf.Direct.exe";                     ReportName='StressPerf.Direct' }
)

$baselineDir = Split-Path -Parent $OutCsv
if (-not (Test-Path $baselineDir)) { New-Item -ItemType Directory -Path $baselineDir | Out-Null }

foreach ($v in $variants) {
  if (-not (Test-Path $v.Exe)) { throw "Missing exe: $($v.Exe). Build with: dotnet build -c $cfg -p:Platform=$plat" }
}

Remove-Item $OutLog -ErrorAction SilentlyContinue
Remove-Item $OutCsv -ErrorAction SilentlyContinue
"Spec 034 final bench @ $(Get-Date -Format o) ($plat $cfg)" | Tee-Object -FilePath $OutLog -Append | Out-Host

function Parse-Field {
  param([string]$Report, [string]$Field)
  $m = [regex]::Match($Report, "(?m)^\s*$([regex]::Escape($Field))\s*:\s*(.+?)\s*$")
  if ($m.Success) { return $m.Groups[1].Value.Trim() } else { return '' }
}
function Num { param($v) if ($v -match '^([-\d\.]+)') { return [double]$matches[1] } else { return [double]::NaN } }
function Int_ { param($v) if ($v -match '^(\d+)') { return [int]$matches[1] } else { return 0 } }

$rows = @()
foreach ($pct in $Percents) {
  foreach ($v in $variants) {
    "" | Tee-Object -FilePath $OutLog -Append | Out-Host
    "==== $($v.Name) @ $pct% / ${DurationSeconds}s ====" | Tee-Object -FilePath $OutLog -Append | Out-Host

    $exeName = [System.IO.Path]::GetFileNameWithoutExtension($v.Exe)
    Get-Process -Name $exeName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 250

    # Clean stale report.
    $exeDir = Split-Path -Parent $v.Exe
    Remove-Item (Join-Path $exeDir "$($v.ReportName).report.txt") -ErrorAction SilentlyContinue

    $proc = Start-Process -FilePath $v.Exe -ArgumentList @('--headless','--percent',"$pct",'--duration',"$DurationSeconds") -PassThru
    $deadline = (Get-Date).AddSeconds($DurationSeconds + 10)
    while (-not $proc.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }

    $reportPath = Join-Path $exeDir "$($v.ReportName).report.txt"
    if (-not (Test-Path $reportPath)) {
      "  (no report file)" | Tee-Object -FilePath $OutLog -Append | Out-Host
      $rows += [pscustomobject]@{ Variant=$v.Name; Percent=$pct; Renders=0; FpsAvg=0; UpdateMsAvg=0; ReconcileMsAvg=0; PeakMemMB=0 }
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
      Variant       = $v.Name
      Percent       = $pct
      Renders       = $renders
      RendersPerSec = if ($DurationSeconds -gt 0) { [math]::Round($renders / $DurationSeconds, 2) } else { 0 }
      FpsAvg        = $fps
      UpdateMsAvg   = $updMs
      ReconcileMsAvg= $reconMs
      PeakMemMB     = $peakMem
    }
  }
}

$rows | Sort-Object Percent, Variant | Export-Csv -Path $OutCsv -NoTypeInformation
"" | Tee-Object -FilePath $OutLog -Append | Out-Host
"==== RESULTS ====" | Tee-Object -FilePath $OutLog -Append | Out-Host
$rows | Sort-Object Percent, Variant | Format-Table -AutoSize | Out-String | Tee-Object -FilePath $OutLog -Append | Out-Host
"DONE @ $(Get-Date -Format o)" | Tee-Object -FilePath $OutLog -Append | Out-Host
"`nCSV: $OutCsv`nLog: $OutLog" | Out-Host
