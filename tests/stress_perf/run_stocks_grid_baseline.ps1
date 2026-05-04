# run_stocks_grid_baseline.ps1 — full StocksGrid matrix at 10/50/100% over 10s,
# all six variants (Direct, Bound, Wpf, DirectX, Reactor, RN-Fabric), with
# both easy-mode and accurate-mode metrics captured.
#
# MUST RUN AS ADMINISTRATOR — PresentTracer needs an ETW kernel session.
#
# Inputs:
#   • Pre-built ARM64 release binaries (`dotnet build -c Release -p:Platform=ARM64`
#     for the C# variants; `npx ... run-windows --release --arch arm64` for RN).
#   • PresentTracer.exe built (also ARM64 release).
#
# Outputs:
#   • $LogPath  — full per-scenario PresentTracer dump + variant report block.
#   • $CsvPath  — one row per (variant, percent), aggregating ETW + in-app metrics.

param(
  [string] $LogPath = 'C:\Users\andersonch\Code\reactor3\tests\stress_perf\baseline-stocks-grid.log',
  [string] $CsvPath = 'C:\Users\andersonch\Code\reactor3\tests\stress_perf\baseline-stocks-grid.csv',
  [string] $SummaryCsvPath = 'C:\Users\andersonch\Code\reactor3\tests\stress_perf\baseline-stocks-grid.summary.csv',
  [int]    $DurationSeconds = 10,
  [int[]]  $Percents = @(10, 50, 100),
  [int]    $Repeats = 1
)

$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes -ErrorAction Stop

# ── Variant table ──────────────────────────────────────────────────────────
$repo = 'C:\Users\andersonch\Code\reactor3'
$variants = @(
  [pscustomobject]@{ Name='Direct';    Exe="$repo\tests\stress_perf\StressPerf.Direct\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.Direct.exe";   IsRN=$false; ReportName='StressPerf.Direct' }
  [pscustomobject]@{ Name='Bound';     Exe="$repo\tests\stress_perf\StressPerf.Bound\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.Bound.exe";     IsRN=$false; ReportName='StressPerf.Bound' }
  [pscustomobject]@{ Name='Wpf';       Exe="$repo\tests\stress_perf\StressPerf.Wpf\bin\ARM64\Release\net9.0-windows\StressPerf.Wpf.exe";                     IsRN=$false; ReportName='StressPerf.Wpf' }
  [pscustomobject]@{ Name='DirectX';   Exe="$repo\tests\stress_perf\StressPerf.DirectX\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.DirectX.exe"; IsRN=$false; ReportName='StressPerf.DirectX' }
  [pscustomobject]@{ Name='Reactor';   Exe="$repo\tests\stress_perf\StressPerf.Reactor\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.Reactor.exe"; IsRN=$false; ReportName='StressPerf.Reactor' }
  [pscustomobject]@{ Name='ReactorOptimized'; Exe="$repo\tests\stress_perf\StressPerf.ReactorOptimized\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.ReactorOptimized.exe"; IsRN=$false; ReportName='StressPerf.ReactorOptimized' }
  [pscustomobject]@{ Name='RN-Fabric'; Exe="$repo\tests\stress_perf_rn\StocksGrid\windows\ARM64\Release\StocksGrid.exe";                                     IsRN=$true;  ReportName='StressPerf.RN.StocksGrid' }
)
$tracer = "$repo\tests\stress_perf\PresentTracer\bin\ARM64\Release\net9.0\PresentTracer.exe"

foreach ($v in $variants) {
  if (-not (Test-Path $v.Exe))   { throw "Missing exe: $($v.Exe)" }
}
if (-not (Test-Path $tracer))    { throw "Missing tracer: $tracer" }

Remove-Item $LogPath -ErrorAction SilentlyContinue
Remove-Item $CsvPath -ErrorAction SilentlyContinue
"Baseline run @ $(Get-Date -Format o)" | Tee-Object -FilePath $LogPath -Append | Out-Host

# ── Helpers ────────────────────────────────────────────────────────────────

function Read-VariantReport {
  param([string]$Exe, [string]$ReportName)
  $dir = [System.IO.Path]::GetDirectoryName($Exe)
  $path = Join-Path $dir "$ReportName.report.txt"
  if (Test-Path $path) { return Get-Content $path -Raw }
  return ''
}

function Scrape-RnReport {
  # Pulls the HeadlessReport TextBlock from the running RN window via UIA.
  param([int]$ProcId)
  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcId)
  $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
  if (-not $win) { return '' }
  $idCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'HeadlessReport')
  $r = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $idCond)
  if ($r) { return $r.Current.Name } else { return '' }
}

function Parse-Field {
  param([string]$Report, [string]$Field)
  if (-not $Report) { return '' }
  $m = [regex]::Match($Report, "(?m)^\s*$([regex]::Escape($Field))\s*:\s*(.+?)\s*$")
  if ($m.Success) { return $m.Groups[1].Value.Trim() } else { return '' }
}

function Parse-FloatField {
  param([string]$Report, [string]$Field)
  $v = Parse-Field $Report $Field
  if ($v -match '^([-\d\.]+)') { return [double]$matches[1] } else { return [double]::NaN }
}

function Parse-IntField {
  param([string]$Report, [string]$Field)
  $v = Parse-Field $Report $Field
  if ($v -match '^(\d+)') { return [int]$matches[1] } else { return 0 }
}

function Parse-TracerCsvForPresent {
  # Tracer CSV: Provider,Event,Count,PerSec,P50ms,P95ms,P99ms
  param([string]$CsvPath)
  if (-not (Test-Path $CsvPath)) { return [pscustomobject]@{ Count=0; PerSec=0.0; P50=0.0; P95=0.0; P99=0.0 } }
  $rows = Import-Csv $CsvPath
  $present = $rows | Where-Object { $_.Provider -eq 'Microsoft-Windows-DxgKrnl' -and $_.Event -eq 'Present' } | Select-Object -First 1
  if (-not $present) { return [pscustomobject]@{ Count=0; PerSec=0.0; P50=0.0; P95=0.0; P99=0.0 } }
  return [pscustomobject]@{
    Count  = [int]$present.Count
    PerSec = [double]$present.PerSec
    P50    = [double]($present.P50ms ?? 0)
    P95    = [double]($present.P95ms ?? 0)
    P99    = [double]($present.P99ms ?? 0)
  }
}

function Parse-TracerCsvForGlobalVsync {
  # PresentTracer always emits a row with Provider="GLOBAL" Event="VSync"
  # so we can capture display-refresh rate during the bench. Surfaces
  # Dynamic Refresh Rate / battery throttling effects.
  param([string]$CsvPath)
  if (-not (Test-Path $CsvPath)) { return 0.0 }
  $rows = Import-Csv $CsvPath
  $row = $rows | Where-Object { $_.Provider -eq 'GLOBAL' -and $_.Event -eq 'VSync' } | Select-Object -First 1
  if (-not $row) { return 0.0 }
  return [double]$row.PerSec
}

# ── Main loop ──────────────────────────────────────────────────────────────

$results = @()
$totalScenarios = $variants.Count * $Percents.Count * $Repeats
$scenarioIdx = 0

foreach ($run in 1..$Repeats) {
foreach ($v in $variants) {
  $exeName = [System.IO.Path]::GetFileNameWithoutExtension($v.Exe)
  foreach ($pct in $Percents) {
    $scenarioIdx++
    "" | Tee-Object -FilePath $LogPath -Append | Out-Host
    "==== [run $run/$Repeats  $scenarioIdx/$totalScenarios] $($v.Name) @ $pct% / ${DurationSeconds}s ====" | Tee-Object -FilePath $LogPath -Append | Out-Host

    # Pre-kill leftovers.
    Get-Process -Name $exeName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 250

    $tracerCsv = Join-Path $env:TEMP "trace-$($v.Name)-$pct.csv"
    Remove-Item $tracerCsv -ErrorAction SilentlyContinue

    # Launch the variant headless.
    $proc = Start-Process -FilePath $v.Exe -ArgumentList @('--headless','--percent',"$pct",'--duration',"$DurationSeconds") -PassThru
    Start-Sleep -Milliseconds 1500   # let the host create its window

    # Trace concurrently in a child job so we can sample memory in parallel.
    $traceWindow = [Math]::Max(3, $DurationSeconds - 1)
    $tracerJob = Start-Job -ScriptBlock {
      param($tracer, $procId, $dur, $csv)
      & $tracer --pid $procId --duration $dur --csv $csv 2>&1
    } -ArgumentList $tracer, $proc.Id, $traceWindow, $tracerCsv

    # Sample peak working set during the run.
    $start = Get-Date
    $peakRss = 0L
    while (((Get-Date) - $start).TotalSeconds -lt ($DurationSeconds + 0.5)) {
      try {
        $p = Get-Process -Id $proc.Id -ErrorAction Stop
        if ($p.WorkingSet64 -gt $peakRss) { $peakRss = $p.WorkingSet64 }
      } catch { break }
      Start-Sleep -Milliseconds 250
    }

    # For RN, scrape the on-screen report BEFORE we kill — it lives in the UIA tree.
    $rnReport = ''
    if ($v.IsRN) {
      Start-Sleep -Milliseconds 500
      $rnReport = Scrape-RnReport -ProcId $proc.Id
    }

    # Wait for tracer to finish then teardown.
    Wait-Job $tracerJob | Out-Null
    $tracerOut = (Receive-Job $tracerJob) -join "`n"
    Remove-Job $tracerJob
    $tracerOut | Tee-Object -FilePath $LogPath -Append | Out-Host

    # Variant exits on its own at end of duration; if it's still around, kill.
    Start-Sleep -Milliseconds 500
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

    # Read the variant's report. C# variants write to disk; RN was scraped above.
    $report = if ($v.IsRN) { $rnReport } else { Read-VariantReport -Exe $v.Exe -ReportName $v.ReportName }
    $report | Tee-Object -FilePath $LogPath -Append | Out-Host

    # Extract the headline numbers.
    $present = Parse-TracerCsvForPresent -CsvPath $tracerCsv
    $globalVsync = Parse-TracerCsvForGlobalVsync -CsvPath $tracerCsv

    # Effective Refresh — see METHODOLOGY.md. min(renders/sec, present/sec)
    # is the rate the user actually perceives. Above the min, the slower
    # side is wasting work (display-bound: render thread coalescing
    # intermediate states; framework-bound: OS re-presenting unchanged
    # content). The "Bottleneck" tag tells you which.
    $rendersPerSec = if ($DurationSeconds -gt 0) {
      [math]::Round((Parse-IntField $report 'Total Renders') / $DurationSeconds, 2)
    } else { 0.0 }
    $presentPerSec = [math]::Round($present.PerSec, 2)
    $effective = [math]::Round([math]::Min($rendersPerSec, $presentPerSec), 2)
    $bottleneck =
      if ($rendersPerSec -le 0 -or $presentPerSec -le 0)            { 'unknown' }
      elseif (($rendersPerSec / $presentPerSec) -ge 1.20)            { 'display' }
      elseif (($presentPerSec / [math]::Max(0.01,$rendersPerSec)) -ge 1.20) { 'framework' }
      else                                                           { 'balanced' }

    # Power state at run time so battery vs AC numbers are diff-able later.
    $onBattery = $false
    try {
      $batt = Get-CimInstance -ClassName Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1
      # BatteryStatus values: 1=discharging (on battery), 2=on AC, others rare.
      if ($batt -and $batt.BatteryStatus -eq 1) { $onBattery = $true }
    } catch {}
    $powerState = if ($onBattery) { 'battery' } else { 'ac' }

    # `Avg Update` is the synchronous UI-thread span (Direct/Bound/Wpf/
    # DirectX/Reactor — meaningful). `Avg Mount` is RN's rAF-after-commit
    # mount-time proxy (only emitted by the RN variants; see
    # METHODOLOGY.md). We report them in separate columns so nobody
    # mistakes one for the other — they bracket different work.
    $row = [pscustomobject]@{
      Run                  = $run
      Variant              = $v.Name
      Percent              = $pct
      EffectiveRefreshPerSec = $effective
      Bottleneck           = $bottleneck
      GlobalVsyncPerSec    = [math]::Round($globalVsync, 2)
      PowerState           = $powerState
      EtwPresentPerSec     = $presentPerSec
      EtwPresentP50ms      = [math]::Round($present.P50, 2)
      EtwPresentP95ms      = [math]::Round($present.P95, 2)
      InAppFps             = Parse-FloatField $report 'Avg FPS'
      InAppTotalRenders    = Parse-IntField   $report 'Total Renders'
      InAppRendersPerSec   = $rendersPerSec
      InAppAvgUpdateMs     = Parse-FloatField $report 'Avg Update'
      InAppAvgMountMs      = Parse-FloatField $report 'Avg Mount'
      PeakRssMB            = [math]::Round($peakRss / 1MB, 1)
    }
    $results += $row
  }
}
}  # end of $run loop

# Per-run rows.
$results | Export-Csv -Path $CsvPath -NoTypeInformation

# Per-(variant,percent) summary stats across all repeats. Filters NaN —
# Avg Update is missing on RN rows, Avg Mount is missing on C# rows, both
# come back as NaN from Parse-FloatField.
function Median {
  param([double[]]$Values)
  $clean = @($Values | Where-Object { -not [double]::IsNaN($_) })
  if ($clean.Count -eq 0) { return [double]::NaN }
  $sorted = $clean | Sort-Object
  $n = $sorted.Count
  if ($n % 2 -eq 1) { return [double]$sorted[[int]([math]::Floor($n / 2))] }
  return ([double]$sorted[$n/2 - 1] + [double]$sorted[$n/2]) / 2.0
}

$summary = $results | Group-Object -Property Variant, Percent | ForEach-Object {
  $rows = $_.Group
  $variant = $rows[0].Variant
  $percent = $rows[0].Percent
  $eff     = [double[]]@($rows | ForEach-Object { [double]$_.EffectiveRefreshPerSec })
  $etw     = [double[]]@($rows | ForEach-Object { [double]$_.EtwPresentPerSec })
  $rps     = [double[]]@($rows | ForEach-Object { [double]$_.InAppRendersPerSec })
  $upd     = [double[]]@($rows | ForEach-Object { [double]$_.InAppAvgUpdateMs })
  $mnt     = [double[]]@($rows | ForEach-Object { [double]$_.InAppAvgMountMs })
  $rss     = [double[]]@($rows | ForEach-Object { [double]$_.PeakRssMB })
  $vsync   = [double[]]@($rows | ForEach-Object { [double]$_.GlobalVsyncPerSec })
  # Mode of bottleneck across runs.
  $btnk = ($rows | Group-Object Bottleneck | Sort-Object Count -Descending | Select-Object -First 1).Name
  $power = ($rows | Group-Object PowerState | Sort-Object Count -Descending | Select-Object -First 1).Name
  [pscustomobject]@{
    Variant                       = $variant
    Percent                       = $percent
    Runs                          = $rows.Count
    PowerState                    = $power
    EffectiveRefreshPerSec_Median = [math]::Round((Median $eff), 2)
    EffectiveRefreshPerSec_Min    = [math]::Round(($eff | Measure-Object -Minimum).Minimum, 2)
    EffectiveRefreshPerSec_Max    = [math]::Round(($eff | Measure-Object -Maximum).Maximum, 2)
    Bottleneck                    = $btnk
    GlobalVsyncPerSec_Med         = [math]::Round((Median $vsync), 2)
    EtwPresent_Median             = [math]::Round((Median $etw), 2)
    EtwPresent_Min                = [math]::Round(($etw | Measure-Object -Minimum).Minimum, 2)
    EtwPresent_Max                = [math]::Round(($etw | Measure-Object -Maximum).Maximum, 2)
    InAppRendersPerSec_Med        = [math]::Round((Median $rps), 2)
    InAppAvgUpdateMs_Med          = [math]::Round((Median $upd), 2)
    InAppAvgMountMs_Med           = [math]::Round((Median $mnt), 2)
    PeakRssMB_Med                 = [math]::Round((Median $rss), 1)
  }
}
$summary | Sort-Object Variant, Percent | Export-Csv -Path $SummaryCsvPath -NoTypeInformation

"" | Tee-Object -FilePath $LogPath -Append | Out-Host
"==== PER-RUN ROWS ($($results.Count)) ====" | Tee-Object -FilePath $LogPath -Append | Out-Host
$results | Format-Table -AutoSize | Out-String | Tee-Object -FilePath $LogPath -Append | Out-Host
"==== AGGREGATED (median / min / max across $Repeats run(s)) ====" | Tee-Object -FilePath $LogPath -Append | Out-Host
$summary | Sort-Object Variant, Percent | Format-Table -AutoSize | Out-String | Tee-Object -FilePath $LogPath -Append | Out-Host
"DONE @ $(Get-Date -Format o)" | Tee-Object -FilePath $LogPath -Append | Out-Host
"`nLog:         $LogPath`nPer-run CSV: $CsvPath`nSummary CSV: $SummaryCsvPath" | Out-Host
