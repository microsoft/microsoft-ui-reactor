# run_full_matrix.ps1 — full StocksGrid matrix at 10/20/50/100 % @ 10s,
# all C# variants, with ETW Present-tracking via PresentTracer for
# ground-truth render rate.
#
# MUST RUN AS ADMINISTRATOR — PresentTracer subscribes to the
# Microsoft-Windows-DxgKrnl kernel provider, which requires an elevated
# ETW session. The script aborts with a clear message if it isn't.
#
# Outputs (under tests/stress_perf/baselines/<timestamp>/):
#   • run.log           — full per-scenario PresentTracer dump + variant report block
#   • run.csv           — one row per (run, variant, percent), ETW + in-app metrics
#   • run.summary.csv   — per-(variant, percent) median / min / max across repeats
#
# Defaults: 10 / 20 / 50 / 100 % at 10 s each, single repeat, ARM64 Release.
# Use -Repeats N to sample more for noise control. Use -SkipBuild to reuse
# already-built binaries.

param(
  [int]    $DurationSeconds = 10,
  [int[]]  $Percents = @(10, 20, 50, 100),
  [int]    $Repeats = 1,
  [string] $Configuration = 'Release',
  [string] $Platform = 'ARM64',
  [string] $OutputRoot = (Join-Path $PSScriptRoot 'baselines'),
  [string[]] $VariantFilter = @(),  # empty = all
  [switch] $SkipBuild,
  [switch] $SkipETW            # last-resort: in-app metrics only (no admin needed)
)

$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes -ErrorAction Stop

# ── Paths ──────────────────────────────────────────────────────────────────
$repoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$stressDir  = Join-Path $repoRoot 'tests\stress_perf'
$tfmWinUI   = 'net9.0-windows10.0.22621.0'
$tfmWpf     = 'net9.0-windows'
$tfmTracer  = 'net9.0'

$tracerExe  = Join-Path $stressDir "PresentTracer\bin\$Platform\$Configuration\$tfmTracer\PresentTracer.exe"

# Variant table — name, exe path, in-app report file name. Matches
# StressPerf.<X>.report.txt that PerfTracker.WriteReportFile emits next
# to the executable. RN-Fabric is intentionally omitted; its build
# pipeline (npx run-windows) is separate and needs its own driver.
$variants = @(
  [pscustomobject]@{ Name='Direct';            Csproj="$stressDir\StressPerf.Direct\StressPerf.Direct.csproj";                       Exe="$stressDir\StressPerf.Direct\bin\$Platform\$Configuration\$tfmWinUI\StressPerf.Direct.exe";                       ReportName='StressPerf.Direct' }
  [pscustomobject]@{ Name='Bound';             Csproj="$stressDir\StressPerf.Bound\StressPerf.Bound.csproj";                         Exe="$stressDir\StressPerf.Bound\bin\$Platform\$Configuration\$tfmWinUI\StressPerf.Bound.exe";                         ReportName='StressPerf.Bound' }
  [pscustomobject]@{ Name='Wpf';               Csproj="$stressDir\StressPerf.Wpf\StressPerf.Wpf.csproj";                             Exe="$stressDir\StressPerf.Wpf\bin\$Platform\$Configuration\$tfmWpf\StressPerf.Wpf.exe";                               ReportName='StressPerf.Wpf' }
  [pscustomobject]@{ Name='DirectX';           Csproj="$stressDir\StressPerf.DirectX\StressPerf.DirectX.csproj";                     Exe="$stressDir\StressPerf.DirectX\bin\$Platform\$Configuration\$tfmWinUI\StressPerf.DirectX.exe";                     ReportName='StressPerf.DirectX' }
  [pscustomobject]@{ Name='Reactor';           Csproj="$stressDir\StressPerf.Reactor\StressPerf.Reactor.csproj";                     Exe="$stressDir\StressPerf.Reactor\bin\$Platform\$Configuration\$tfmWinUI\StressPerf.Reactor.exe";                     ReportName='StressPerf.Reactor' }
  [pscustomobject]@{ Name='ReactorOptimized';  Csproj="$stressDir\StressPerf.ReactorOptimized\StressPerf.ReactorOptimized.csproj";   Exe="$stressDir\StressPerf.ReactorOptimized\bin\$Platform\$Configuration\$tfmWinUI\StressPerf.ReactorOptimized.exe";   ReportName='StressPerf.ReactorOptimized' }
  [pscustomobject]@{ Name='ReactorGrid';       Csproj="$stressDir\StressPerf.ReactorGrid\StressPerf.ReactorGrid.csproj";             Exe="$stressDir\StressPerf.ReactorGrid\bin\$Platform\$Configuration\$tfmWinUI\StressPerf.ReactorGrid.exe";             ReportName='StressPerf.ReactorGrid' }
  [pscustomobject]@{ Name='VirtualListReactor';Csproj="$stressDir\StressPerf.VirtualList.Reactor\StressPerf.VirtualList.Reactor.csproj"; Exe="$stressDir\StressPerf.VirtualList.Reactor\bin\$Platform\$Configuration\$tfmWinUI\StressPerf.VirtualList.Reactor.exe"; ReportName='StressPerf.VirtualList.Reactor' }
)

if ($VariantFilter.Count -gt 0) {
  $variants = $variants | Where-Object { $VariantFilter -contains $_.Name }
  if ($variants.Count -eq 0) { throw "VariantFilter matched no variants. Names: Direct, Bound, Wpf, DirectX, Reactor, ReactorOptimized, ReactorGrid, VirtualListReactor" }
}

# ── Pre-flight: admin check (skip allowed for in-app-only mode) ────────────
function Test-Admin {
  $current = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = [Security.Principal.WindowsPrincipal]::new($current)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$useETW = -not $SkipETW
if ($useETW -and -not (Test-Admin)) {
  Write-Host "==============================================================" -ForegroundColor Red
  Write-Host " PresentTracer needs an elevated ETW kernel session."          -ForegroundColor Red
  Write-Host " Re-run from an admin PowerShell, or pass -SkipETW for"        -ForegroundColor Red
  Write-Host " in-app-metrics-only mode (no Present/sec, no DxgKrnl rates)." -ForegroundColor Red
  Write-Host "==============================================================" -ForegroundColor Red
  exit 1
}

# ── Pre-flight: output dir ────────────────────────────────────────────────
$stamp     = Get-Date -Format 'yyyy-MM-dd-HHmmss'
$outDir    = Join-Path $OutputRoot "full-matrix-$stamp"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$logPath   = Join-Path $outDir 'run.log'
$csvPath   = Join-Path $outDir 'run.csv'
$summaryPath = Join-Path $outDir 'run.summary.csv'

"Spec full-matrix bench @ $(Get-Date -Format o)" | Tee-Object -FilePath $logPath -Append | Out-Host
"Configuration: $Configuration $Platform"          | Tee-Object -FilePath $logPath -Append | Out-Host
"Duration:      ${DurationSeconds}s"               | Tee-Object -FilePath $logPath -Append | Out-Host
"Percents:      $($Percents -join ', ')"           | Tee-Object -FilePath $logPath -Append | Out-Host
"Repeats:       $Repeats"                          | Tee-Object -FilePath $logPath -Append | Out-Host
"ETW tracking:  $(if ($useETW) { 'enabled (admin verified)' } else { 'DISABLED (-SkipETW)' })" | Tee-Object -FilePath $logPath -Append | Out-Host
"Variants:      $(($variants | ForEach-Object Name) -join ', ')" | Tee-Object -FilePath $logPath -Append | Out-Host
"Output dir:    $outDir"                           | Tee-Object -FilePath $logPath -Append | Out-Host
""                                                  | Tee-Object -FilePath $logPath -Append | Out-Host

# ── Build phase ────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
  "==== Build: dotnet build (Configuration=$Configuration Platform=$Platform) ====" | Tee-Object -FilePath $logPath -Append | Out-Host
  # Build each variant project explicitly. Building Reactor.sln rebuilds 80+
  # projects we don't need; building the variant csprojs covers them and
  # their transitive dependencies (StressPerf.Shared, Reactor, etc.).
  $buildTargets = @($variants | ForEach-Object Csproj)
  if ($useETW) { $buildTargets += "$stressDir\PresentTracer\PresentTracer.csproj" }
  foreach ($csproj in $buildTargets) {
    "  build: $(Split-Path -Leaf $csproj)" | Tee-Object -FilePath $logPath -Append | Out-Host
    & dotnet build $csproj -c $Configuration -p:Platform=$Platform -v q -nologo 2>&1 | Tee-Object -FilePath $logPath -Append | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $csproj (see $logPath)" }
  }
  "" | Tee-Object -FilePath $logPath -Append | Out-Host
}

# Verify outputs exist after build (or up-front if -SkipBuild).
foreach ($v in $variants) {
  if (-not (Test-Path $v.Exe)) { throw "Missing exe for $($v.Name): $($v.Exe). Drop -SkipBuild or build manually." }
}
if ($useETW -and -not (Test-Path $tracerExe)) {
  throw "Missing PresentTracer: $tracerExe. Drop -SkipBuild or build PresentTracer.csproj."
}

# ── Helpers ────────────────────────────────────────────────────────────────
function Read-VariantReport {
  param([string]$Exe, [string]$ReportName)
  $dir = Split-Path -Parent $Exe
  $path = Join-Path $dir "$ReportName.report.txt"
  if (Test-Path $path) { return Get-Content $path -Raw }
  return ''
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
  param([string]$CsvFile)
  if (-not (Test-Path $CsvFile)) { return [pscustomobject]@{ Count=0; PerSec=0.0; P50=0.0; P95=0.0; P99=0.0 } }
  $rows = Import-Csv $CsvFile
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
  param([string]$CsvFile)
  if (-not (Test-Path $CsvFile)) { return 0.0 }
  $rows = Import-Csv $CsvFile
  $row = $rows | Where-Object { $_.Provider -eq 'GLOBAL' -and $_.Event -eq 'VSync' } | Select-Object -First 1
  if (-not $row) { return 0.0 }
  return [double]$row.PerSec
}

function Median {
  param([double[]]$Values)
  if ($Values.Count -eq 0) { return 0 }
  $sorted = $Values | Sort-Object
  $n = $sorted.Count
  if ($n % 2 -eq 1) { return [double]$sorted[[int]([math]::Floor($n / 2))] }
  return ([double]$sorted[$n/2 - 1] + [double]$sorted[$n/2]) / 2.0
}

function Get-PowerState {
  try {
    $batt = Get-CimInstance -ClassName Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($batt -and $batt.BatteryStatus -eq 1) { return 'battery' }
  } catch {}
  return 'ac'
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
    "" | Tee-Object -FilePath $logPath -Append | Out-Host
    "==== [run $run/$Repeats  $scenarioIdx/$totalScenarios] $($v.Name) @ $pct% / ${DurationSeconds}s ====" | Tee-Object -FilePath $logPath -Append | Out-Host

    # Pre-kill stale instances and clear stale report.
    Get-Process -Name $exeName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 250
    $exeDir = Split-Path -Parent $v.Exe
    Remove-Item (Join-Path $exeDir "$($v.ReportName).report.txt") -ErrorAction SilentlyContinue

    $tracerCsv = Join-Path $env:TEMP "trace-$($v.Name)-$pct-run$run.csv"
    Remove-Item $tracerCsv -ErrorAction SilentlyContinue

    # Launch the variant headless.
    $proc = Start-Process -FilePath $v.Exe -ArgumentList @('--headless','--percent',"$pct",'--duration',"$DurationSeconds") -PassThru
    Start-Sleep -Milliseconds 1500   # let the host create its window before tracing

    # Trace concurrently in a child job. Tracer window is duration - 1 to give
    # us time to attach after the process spawns and to settle before we tear down.
    $tracerJob = $null
    if ($useETW) {
      $traceWindow = [Math]::Max(3, $DurationSeconds - 1)
      $tracerJob = Start-Job -ScriptBlock {
        param($tracer, $procId, $dur, $csv)
        & $tracer --pid $procId --duration $dur --csv $csv 2>&1
      } -ArgumentList $tracerExe, $proc.Id, $traceWindow, $tracerCsv
    }

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

    # Wait for tracer to finish (if any) then teardown.
    if ($tracerJob) {
      Wait-Job $tracerJob | Out-Null
      $tracerOut = (Receive-Job $tracerJob) -join "`n"
      Remove-Job $tracerJob
      $tracerOut | Tee-Object -FilePath $logPath -Append | Out-Host
    }

    # Variant exits on its own at end of duration; if it's still around, kill.
    Start-Sleep -Milliseconds 500
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

    # Read the variant's report.
    $report = Read-VariantReport -Exe $v.Exe -ReportName $v.ReportName
    $report | Tee-Object -FilePath $logPath -Append | Out-Host

    # Extract headline numbers.
    if ($useETW) {
      $present     = Parse-TracerCsvForPresent -CsvFile $tracerCsv
      $globalVsync = Parse-TracerCsvForGlobalVsync -CsvFile $tracerCsv
    } else {
      $present     = [pscustomobject]@{ Count=0; PerSec=0.0; P50=0.0; P95=0.0; P99=0.0 }
      $globalVsync = 0.0
    }

    $rendersPerSec = if ($DurationSeconds -gt 0) {
      [math]::Round((Parse-IntField $report 'Total Renders') / $DurationSeconds, 2)
    } else { 0.0 }
    $presentPerSec = [math]::Round($present.PerSec, 2)

    # Effective Refresh — see METHODOLOGY.md. min(renders/sec, present/sec)
    # is the rate the user actually perceives. Above the min, the slower
    # side is wasting work (display-bound: render thread coalescing
    # intermediate states; framework-bound: OS re-presenting unchanged
    # content). The "Bottleneck" tag tells you which.
    $effective = if ($useETW) { [math]::Round([math]::Min($rendersPerSec, $presentPerSec), 2) } else { $rendersPerSec }
    $bottleneck =
      if (-not $useETW)                                                  { 'unknown (no ETW)' }
      elseif ($rendersPerSec -le 0 -or $presentPerSec -le 0)              { 'unknown' }
      elseif (($rendersPerSec / $presentPerSec) -ge 1.20)                  { 'display' }
      elseif (($presentPerSec / [math]::Max(0.01,$rendersPerSec)) -ge 1.20){ 'framework' }
      else                                                                { 'balanced' }

    $row = [pscustomobject]@{
      Run                    = $run
      Variant                = $v.Name
      Percent                = $pct
      EffectiveRefreshPerSec = $effective
      Bottleneck             = $bottleneck
      GlobalVsyncPerSec      = [math]::Round($globalVsync, 2)
      PowerState             = (Get-PowerState)
      EtwPresentPerSec       = $presentPerSec
      EtwPresentP50ms        = [math]::Round($present.P50, 2)
      EtwPresentP95ms        = [math]::Round($present.P95, 2)
      EtwPresentP99ms        = [math]::Round($present.P99, 2)
      InAppFps               = Parse-FloatField $report 'Avg FPS'
      InAppTotalRenders      = Parse-IntField   $report 'Total Renders'
      InAppRendersPerSec     = $rendersPerSec
      InAppAvgUpdateMs       = Parse-FloatField $report 'Avg Update'
      InAppAvgReconcileMs    = Parse-FloatField $report 'Avg Reconcile'
      InAppAvgMemoryMB       = Parse-FloatField $report 'Avg Memory'
      InAppPeakMemoryMB      = Parse-FloatField $report 'Peak Memory'
      PeakRssMB              = [math]::Round($peakRss / 1MB, 1)
    }
    $results += $row
  }
}
}  # end of $run loop

# Per-run rows.
$results | Export-Csv -Path $csvPath -NoTypeInformation

# Per-(variant,percent) summary stats across all repeats.
$summary = $results | Group-Object -Property Variant, Percent | ForEach-Object {
  $rows = $_.Group
  $variant = $rows[0].Variant
  $percent = $rows[0].Percent
  $eff   = [double[]]@($rows | ForEach-Object { [double]$_.EffectiveRefreshPerSec })
  $etw   = [double[]]@($rows | ForEach-Object { [double]$_.EtwPresentPerSec })
  $rps   = [double[]]@($rows | ForEach-Object { [double]$_.InAppRendersPerSec })
  $upd   = [double[]]@($rows | ForEach-Object { [double]$_.InAppAvgUpdateMs })
  $recon = [double[]]@($rows | ForEach-Object { [double]$_.InAppAvgReconcileMs })
  $rss   = [double[]]@($rows | ForEach-Object { [double]$_.PeakRssMB })
  $vsync = [double[]]@($rows | ForEach-Object { [double]$_.GlobalVsyncPerSec })
  $btnk  = ($rows | Group-Object Bottleneck | Sort-Object Count -Descending | Select-Object -First 1).Name
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
    InAppAvgReconcileMs_Med       = [math]::Round((Median $recon), 2)
    PeakRssMB_Med                 = [math]::Round((Median $rss), 1)
  }
}
$summary | Sort-Object Variant, Percent | Export-Csv -Path $summaryPath -NoTypeInformation

"" | Tee-Object -FilePath $logPath -Append | Out-Host
"==== PER-RUN ROWS ($($results.Count)) ====" | Tee-Object -FilePath $logPath -Append | Out-Host
$results | Format-Table -AutoSize | Out-String | Tee-Object -FilePath $logPath -Append | Out-Host
"==== AGGREGATED (median / min / max across $Repeats run(s)) ====" | Tee-Object -FilePath $logPath -Append | Out-Host
$summary | Sort-Object Variant, Percent | Format-Table -AutoSize | Out-String | Tee-Object -FilePath $logPath -Append | Out-Host
"DONE @ $(Get-Date -Format o)" | Tee-Object -FilePath $logPath -Append | Out-Host
"`nLog:         $logPath`nPer-run CSV: $csvPath`nSummary CSV: $summaryPath" | Out-Host
