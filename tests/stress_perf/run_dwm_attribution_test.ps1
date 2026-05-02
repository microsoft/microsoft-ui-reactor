# run_dwm_attribution_test.ps1 — diagnostic that tests whether DirectX
# (Win2D) presents are landing on dwm.exe rather than the app PID, which
# would explain why the user perceives DirectX as much faster than our
# PID-filtered ETW Present count suggests.
#
# Method: capture present-related events with no PID filter (--all-pids)
# during three windows:
#   1. Idle desktop (no bench app running) — baseline DWM rate
#   2. WPF @ 50% bench
#   3. DirectX @ 50% bench
#   4. WPF @ 100% bench
#   5. DirectX @ 100% bench
#
# Compare DWM-attributed presents and the bench app's PID-attributed
# presents in each. Hypothesis: DirectX boosts dwm.exe present rate
# significantly above the idle baseline; WPF doesn't (because XAML / WPF
# own a swap chain that attributes presents to their own PID).
#
# MUST run elevated.

param(
  [string] $LogPath  = 'C:\Users\andersonch\Code\reactor3\tests\stress_perf\dwm-attribution.log',
  [string] $CsvDir   = 'C:\Users\andersonch\Code\reactor3\tests\stress_perf',
  [int]    $BenchSec = 10
)

$ErrorActionPreference = 'Continue'

$repo    = 'C:\Users\andersonch\Code\reactor3'
$tracer  = "$repo\tests\stress_perf\PresentTracer\bin\ARM64\Release\net9.0\PresentTracer.exe"
$wpf     = "$repo\tests\stress_perf\StressPerf.Wpf\bin\ARM64\Release\net9.0-windows\StressPerf.Wpf.exe"
$dx      = "$repo\tests\stress_perf\StressPerf.DirectX\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.DirectX.exe"

if (-not (Test-Path $tracer)) { throw "Missing tracer: $tracer" }
if (-not (Test-Path $wpf))    { throw "Missing wpf: $wpf" }
if (-not (Test-Path $dx))     { throw "Missing dx: $dx" }

Remove-Item $LogPath -ErrorAction SilentlyContinue
"DWM attribution test @ $(Get-Date -Format o)" | Tee-Object -FilePath $LogPath -Append | Out-Host
"Power state: battery (per user note)" | Tee-Object -FilePath $LogPath -Append | Out-Host

# ── Phase 1: idle baseline ─────────────────────────────────────────────────
$idleCsv = Join-Path $CsvDir 'attr-idle.csv'
"" | Tee-Object -FilePath $LogPath -Append | Out-Host
"==== Phase 1: IDLE baseline (no bench app, ${BenchSec}s) ====" | Tee-Object -FilePath $LogPath -Append | Out-Host
$out = & $tracer --all-pids --duration $BenchSec --csv $idleCsv 2>&1
$out | Tee-Object -FilePath $LogPath -Append | Out-Host

# ── Phase 2-5: bench scenarios ─────────────────────────────────────────────
function Run-Bench {
  param([string]$Label, [string]$Exe, [int]$Percent, [string]$CsvOut)
  "" | Tee-Object -FilePath $LogPath -Append | Out-Host
  "==== ${Label} ====" | Tee-Object -FilePath $LogPath -Append | Out-Host
  $exeName = [IO.Path]::GetFileNameWithoutExtension($Exe)
  Get-Process -Name $exeName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 250

  $proc = Start-Process -FilePath $Exe -ArgumentList @('--headless','--percent',"$Percent",'--duration',"$BenchSec") -PassThru
  Start-Sleep -Milliseconds 1500   # let the window come up

  $out = & $tracer --all-pids --duration ([Math]::Max(3, $BenchSec - 1)) --csv $CsvOut 2>&1
  $out | Tee-Object -FilePath $LogPath -Append | Out-Host

  Start-Sleep -Seconds 1
  Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
  return $proc.Id
}

$wpfPid50  = Run-Bench -Label 'Phase 2: WPF @ 50%'     -Exe $wpf -Percent 50  -CsvOut (Join-Path $CsvDir 'attr-wpf-50.csv')
$dxPid50   = Run-Bench -Label 'Phase 3: DirectX @ 50%' -Exe $dx  -Percent 50  -CsvOut (Join-Path $CsvDir 'attr-dx-50.csv')
$wpfPid100 = Run-Bench -Label 'Phase 4: WPF @ 100%'    -Exe $wpf -Percent 100 -CsvOut (Join-Path $CsvDir 'attr-wpf-100.csv')
$dxPid100  = Run-Bench -Label 'Phase 5: DirectX @ 100%' -Exe $dx -Percent 100 -CsvOut (Join-Path $CsvDir 'attr-dx-100.csv')

# ── Aggregate ──────────────────────────────────────────────────────────────
function Sum-Present {
  # Sum the "Present" event count across all rows for a given ProcessName.
  param([string]$Csv, [string]$ProcName)
  if (-not (Test-Path $Csv)) { return 0 }
  $rows = Import-Csv $Csv | Where-Object {
    $_.ProcessName -eq $ProcName -and $_.Event -eq 'Present'
  }
  if (-not $rows) { return 0 }
  return [int]($rows | Measure-Object -Property Count -Sum).Sum
}

function Sum-PresentBy {
  # All presents in the CSV, grouped to a small list (top sources).
  param([string]$Csv)
  if (-not (Test-Path $Csv)) { return @() }
  $rows = Import-Csv $Csv | Where-Object { $_.Event -eq 'Present' } |
          Sort-Object { [int]$_.Count } -Descending |
          Select-Object -First 8
  return $rows
}

function Show-Phase {
  param([string]$Label, [string]$Csv, [int]$Seconds)
  "" | Tee-Object -FilePath $LogPath -Append | Out-Host
  "---- $Label  (presents-by-process, sorted) ----" | Tee-Object -FilePath $LogPath -Append | Out-Host
  $top = Sum-PresentBy -Csv $Csv
  $report = $top | ForEach-Object {
    "{0,-30}  {1,8}  ({2,5:F1}/s)" -f $_.ProcessName, $_.Count, ([double]$_.Count / $Seconds)
  } | Out-String
  $report | Tee-Object -FilePath $LogPath -Append | Out-Host
}

$traceWindow = [Math]::Max(3, $BenchSec - 1)
"" | Tee-Object -FilePath $LogPath -Append | Out-Host
"==================== AGGREGATED ====================" | Tee-Object -FilePath $LogPath -Append | Out-Host
Show-Phase -Label 'Idle baseline'       -Csv (Join-Path $CsvDir 'attr-idle.csv')      -Seconds $BenchSec
Show-Phase -Label 'WPF @ 50%'           -Csv (Join-Path $CsvDir 'attr-wpf-50.csv')    -Seconds $traceWindow
Show-Phase -Label 'DirectX @ 50%'       -Csv (Join-Path $CsvDir 'attr-dx-50.csv')     -Seconds $traceWindow
Show-Phase -Label 'WPF @ 100%'          -Csv (Join-Path $CsvDir 'attr-wpf-100.csv')   -Seconds $traceWindow
Show-Phase -Label 'DirectX @ 100%'      -Csv (Join-Path $CsvDir 'attr-dx-100.csv')    -Seconds $traceWindow

# ── Hypothesis test ────────────────────────────────────────────────────────
function Get-DwmRate {
  param([string]$Csv, [int]$Seconds)
  $c = Sum-Present -Csv $Csv -ProcName 'dwm'
  if ($c -eq 0) { $c = Sum-Present -Csv $Csv -ProcName 'dwm.exe' }
  return [math]::Round($c / $Seconds, 2)
}

$idleDwm    = Get-DwmRate (Join-Path $CsvDir 'attr-idle.csv')      $BenchSec
$wpf50Dwm   = Get-DwmRate (Join-Path $CsvDir 'attr-wpf-50.csv')    $traceWindow
$dx50Dwm    = Get-DwmRate (Join-Path $CsvDir 'attr-dx-50.csv')     $traceWindow
$wpf100Dwm  = Get-DwmRate (Join-Path $CsvDir 'attr-wpf-100.csv')   $traceWindow
$dx100Dwm   = Get-DwmRate (Join-Path $CsvDir 'attr-dx-100.csv')    $traceWindow

$wpf50App   = [math]::Round((Sum-Present (Join-Path $CsvDir 'attr-wpf-50.csv')   'StressPerf.Wpf')   / $traceWindow, 2)
$dx50App    = [math]::Round((Sum-Present (Join-Path $CsvDir 'attr-dx-50.csv')    'StressPerf.DirectX') / $traceWindow, 2)
$wpf100App  = [math]::Round((Sum-Present (Join-Path $CsvDir 'attr-wpf-100.csv')  'StressPerf.Wpf')   / $traceWindow, 2)
$dx100App   = [math]::Round((Sum-Present (Join-Path $CsvDir 'attr-dx-100.csv')   'StressPerf.DirectX') / $traceWindow, 2)

"" | Tee-Object -FilePath $LogPath -Append | Out-Host
"==================== HYPOTHESIS TEST ====================" | Tee-Object -FilePath $LogPath -Append | Out-Host
("{0,-22}  {1,8}  {2,8}  {3,8}  {4,8}  {5,8}" -f 'Phase', 'app/s', 'dwm/s', 'Δdwm/s', 'app+Δdwm', 'note') | Tee-Object -FilePath $LogPath -Append | Out-Host
("{0,-22}  {1,8}  {2,8:F2}  {3,8}  {4,8}  {5}" -f 'idle baseline','-',$idleDwm,'-','-','baseline') | Tee-Object -FilePath $LogPath -Append | Out-Host

function Show-Diag {
  param([string]$label, [double]$app, [double]$dwm, [double]$idleDwm)
  $deltaDwm = [math]::Round($dwm - $idleDwm, 2)
  $effective = [math]::Round($app + [Math]::Max(0,$deltaDwm), 2)
  ("{0,-22}  {1,8:F2}  {2,8:F2}  {3,8:F2}  {4,8:F2}  {5}" -f $label, $app, $dwm, $deltaDwm, $effective, '') |
    Tee-Object -FilePath $LogPath -Append | Out-Host
}

Show-Diag 'WPF @ 50%'      $wpf50App  $wpf50Dwm  $idleDwm
Show-Diag 'DirectX @ 50%'  $dx50App   $dx50Dwm   $idleDwm
Show-Diag 'WPF @ 100%'     $wpf100App $wpf100Dwm $idleDwm
Show-Diag 'DirectX @ 100%' $dx100App  $dx100Dwm  $idleDwm

"" | Tee-Object -FilePath $LogPath -Append | Out-Host
"Interpretation:" | Tee-Object -FilePath $LogPath -Append | Out-Host
" - Δdwm/s near 0 means DWM presents at its baseline rate; the app's presents are all on its PID." | Tee-Object -FilePath $LogPath -Append | Out-Host
" - Δdwm/s much greater than 0 means DWM is doing extra presents while the app runs — those" | Tee-Object -FilePath $LogPath -Append | Out-Host
"   are 'our' content commits being attributed to dwm.exe (Win2D / DComp surface case)." | Tee-Object -FilePath $LogPath -Append | Out-Host
" - app+Δdwm gives a corrected per-app present rate that includes DWM-attributed presents." | Tee-Object -FilePath $LogPath -Append | Out-Host

"DONE @ $(Get-Date -Format o)" | Tee-Object -FilePath $LogPath -Append | Out-Host
"`nLog: $LogPath`nCSVs: $CsvDir\attr-*.csv" | Out-Host
