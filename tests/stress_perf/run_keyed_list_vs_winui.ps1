<#
.SYNOPSIS
  Paired-run perf comparison with N-rep median: spec 042 perf gate
  for the keyed-list reconciler.

.DESCRIPTION
  Runs StressPerf.VirtualList.Reactor and StressPerf.VirtualList.WinUI
  through the same workload matrix (count × edit rate) and captures
  per-cell frame-time percentiles. Each cell is repeated `-Repetitions`
  times; the report uses **median** across reps so a single VM hiccup
  or DRR transition doesn't dominate the number.

  Cells are interleaved Reactor/WinUI/Reactor/WinUI to keep DRR /
  battery state from drifting between paired measurements. Warm-up
  rep is discarded.

.NOTES
  Bench windows must stay foregrounded — DWM compositing pauses for
  occluded windows on Win11. The runner brings each process forward
  best-effort, but a real user shouldn't alt-tab during the matrix.

.PARAMETER OutDir
  Output directory under tests/stress_perf/baselines/. Auto-named if
  blank.

.PARAMETER Counts
  Seed counts to test (default 1000, 5000, 10000).

.PARAMETER EditRates
  Edits-per-second rates. 0 = scroll-only (no --with-edits).
  Default 0, 4 (paired scroll-only + 4 eps).

.PARAMETER DurationSeconds
  Per-run benchmark duration. Default 10.

.PARAMETER Repetitions
  Measurement reps per cell (median across these). Default 5.

.PARAMETER WarmupReps
  Discarded warmup reps before measurement. Default 1.

.PARAMETER SkipBuild
  Skip the dotnet build step.
#>
param(
    [string]   $OutDir = "",
    [int[]]    $Counts = @(1000, 5000, 10000),
    [int[]]    $EditRates = @(0, 4),
    [int]      $DurationSeconds = 10,
    [int]      $Repetitions = 5,
    [int]      $WarmupReps = 1,
    [switch]   $SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$reactorProj = Join-Path $repoRoot "tests/stress_perf/StressPerf.VirtualList.Reactor"
$winuiProj   = Join-Path $repoRoot "tests/stress_perf/StressPerf.VirtualList.WinUI"

$reactorExe = Join-Path $reactorProj "bin/ARM64/Release/net10.0-windows10.0.22621.0/StressPerf.VirtualList.Reactor.exe"
$winuiExe   = Join-Path $winuiProj   "bin/ARM64/Release/net10.0-windows10.0.22621.0/StressPerf.VirtualList.WinUI.exe"

if (-not $SkipBuild) {
    Write-Host "=== Building both variants (Release ARM64) ===" -ForegroundColor Cyan
    foreach ($p in @($reactorProj, $winuiProj)) {
        dotnet build $p -c Release -p:Platform=ARM64 --nologo -v:m | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $p" }
    }
}

if (-not (Test-Path $reactorExe)) { throw "Reactor exe missing: $reactorExe" }
if (-not (Test-Path $winuiExe))   { throw "WinUI exe missing: $winuiExe" }

if ([string]::IsNullOrEmpty($OutDir)) {
    $stamp = Get-Date -Format "yyyy-MM-dd-HHmmss"
    $OutDir = Join-Path $repoRoot "tests/stress_perf/baselines/keyed-list-vs-winui-$stamp"
}
$null = New-Item -ItemType Directory -Force -Path $OutDir
Write-Host "Output dir: $OutDir" -ForegroundColor Cyan

# Add a Win32 SetForegroundWindow helper once.
if (-not ([System.Management.Automation.PSTypeName]'SP_KLR.NativeWin32').Type) {
    Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr hWnd);' `
        -Name NativeWin32 -Namespace SP_KLR -PassThru | Out-Null
}

function Invoke-Bench {
    param(
        [string] $Exe,
        [int]    $Count,
        [int]    $DurationSeconds,
        [int]    $EditsPerSecond
    )
    $argList = @('--headless', '--count', $Count, '--duration', $DurationSeconds)
    if ($EditsPerSecond -gt 0) {
        $argList += '--with-edits'
        $argList += '--edits-per-second'
        $argList += $EditsPerSecond
    }
    $proc = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru
    Start-Sleep -Milliseconds 250
    try {
        if ($proc -and -not $proc.HasExited -and $proc.MainWindowHandle -ne [IntPtr]::Zero) {
            [SP_KLR.NativeWin32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
        }
    } catch {}
    $waitMs = ($DurationSeconds + 8) * 1000
    if (-not $proc.WaitForExit($waitMs)) {
        try { $proc.Kill() } catch {}
        throw "Bench hung: $Exe count=$Count dur=$DurationSeconds eps=$EditsPerSecond"
    }
    if ($proc.ExitCode -ne 0) {
        throw "Bench exited non-zero ($($proc.ExitCode)): $Exe count=$Count eps=$EditsPerSecond"
    }
}

function Read-Report {
    param([string] $ExeDir, [string] $AppName)
    $reportPath = Join-Path $ExeDir "$AppName.report.txt"
    if (-not (Test-Path $reportPath)) { return $null }
    $raw = Get-Content $reportPath -Raw
    $parse = {
        param($pattern)
        if ($raw -match $pattern) { return [double]$Matches[1] }
        return $null
    }
    return [PSCustomObject]@{
        AppName        = $AppName
        Raw            = $raw
        Count          = & $parse 'Count:\s+(\d+)'
        Edits          = & $parse 'Edits:\s+(\d+)'
        Frames         = & $parse 'Frames:\s+(\d+)'
        WallClockMs    = & $parse 'WallClock:\s+([\d\.]+)'
        AvgMs          = & $parse 'Avg dt:\s+([\d\.]+)'
        P50Ms          = & $parse 'P50 dt:\s+([\d\.]+)'
        P95Ms          = & $parse 'P95 dt:\s+([\d\.]+)'
        P99Ms          = & $parse 'P99 dt:\s+([\d\.]+)'
        MaxMs          = & $parse 'Max dt:\s+([\d\.]+)'
        WorkingSetMB   = & $parse 'WS:\s+(\d+)'
        PeakWSMB       = & $parse 'PeakWS:\s+(\d+)'
        PrivateMB      = & $parse 'Private:\s+(\d+)'
        ManagedHeapMB  = & $parse 'ManagedHeap:\s+(\d+)'
    }
}

function Get-Median {
    param([double[]] $Values)
    if ($null -eq $Values -or $Values.Count -eq 0) { return $null }
    $sorted = $Values | Sort-Object
    $mid = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 0) {
        return ($sorted[$mid - 1] + $sorted[$mid]) / 2.0
    }
    return $sorted[$mid]
}

$reactorBin = Split-Path $reactorExe
$winuiBin   = Split-Path $winuiExe
$reactorAppName = "StressPerf.VirtualList.Reactor"
$winuiAppName   = "StressPerf.VirtualList.WinUI"

$rows = New-Object System.Collections.Generic.List[object]
$allRunsLog = New-Object System.Collections.Generic.List[object]

$cellIdx = 0
foreach ($count in $Counts) {
    foreach ($eps in $EditRates) {
        $cellIdx++
        $modeTag = if ($eps -gt 0) { "edits${eps}" } else { "scroll" }
        $cellTag = "count${count}-${modeTag}"
        Write-Host "`n=== [$cellIdx] count=$count mode=$modeTag duration=${DurationSeconds}s ${Repetitions}x ===" -ForegroundColor Yellow

        # Warm-up reps (discarded).
        for ($w = 1; $w -le $WarmupReps; $w++) {
            Write-Host "  warmup $w/$WarmupReps" -ForegroundColor DarkGray
            Invoke-Bench -Exe $reactorExe -Count $count -DurationSeconds $DurationSeconds -EditsPerSecond $eps
            Invoke-Bench -Exe $winuiExe   -Count $count -DurationSeconds $DurationSeconds -EditsPerSecond $eps
        }

        $reactorReps = @()
        $winuiReps   = @()
        for ($r = 1; $r -le $Repetitions; $r++) {
            Write-Host "  rep $r/$Repetitions" -ForegroundColor DarkGray
            # Interleave Reactor/WinUI so DRR / battery / thermal state
            # can't drift much within a single pair.
            Invoke-Bench -Exe $reactorExe -Count $count -DurationSeconds $DurationSeconds -EditsPerSecond $eps
            $rRep = Read-Report -ExeDir $reactorBin -AppName $reactorAppName
            Invoke-Bench -Exe $winuiExe   -Count $count -DurationSeconds $DurationSeconds -EditsPerSecond $eps
            $wRep = Read-Report -ExeDir $winuiBin   -AppName $winuiAppName

            if ($rRep -eq $null -or $wRep -eq $null) {
                Write-Warning "  missing report at rep $r"
                continue
            }
            $reactorReps += ,@($rRep)
            $winuiReps   += ,@($wRep)

            # Persist per-rep raw frames CSV for forensics.
            Copy-Item -Force (Join-Path $reactorBin "$reactorAppName.frames.csv") `
                      (Join-Path $OutDir "reactor.$cellTag.rep$r.frames.csv")
            Copy-Item -Force (Join-Path $winuiBin "$winuiAppName.frames.csv") `
                      (Join-Path $OutDir "winui.$cellTag.rep$r.frames.csv")

            $allRunsLog.Add([PSCustomObject]@{
                Cell    = $cellTag
                Rep     = $r
                Reactor_P50_ms        = $rRep.P50Ms
                Reactor_P95_ms        = $rRep.P95Ms
                Reactor_P99_ms        = $rRep.P99Ms
                Reactor_Avg_ms        = $rRep.AvgMs
                Reactor_WallClock_ms  = $rRep.WallClockMs
                Reactor_Frames        = $rRep.Frames
                Reactor_Edits         = $rRep.Edits
                Reactor_WS_MB         = $rRep.WorkingSetMB
                Reactor_PeakWS_MB     = $rRep.PeakWSMB
                Reactor_Private_MB    = $rRep.PrivateMB
                Reactor_Heap_MB       = $rRep.ManagedHeapMB
                WinUI_P50_ms          = $wRep.P50Ms
                WinUI_P95_ms          = $wRep.P95Ms
                WinUI_P99_ms          = $wRep.P99Ms
                WinUI_Avg_ms          = $wRep.AvgMs
                WinUI_WallClock_ms    = $wRep.WallClockMs
                WinUI_Frames          = $wRep.Frames
                WinUI_Edits           = $wRep.Edits
                WinUI_WS_MB           = $wRep.WorkingSetMB
                WinUI_PeakWS_MB       = $wRep.PeakWSMB
                WinUI_Private_MB      = $wRep.PrivateMB
                WinUI_Heap_MB         = $wRep.ManagedHeapMB
            }) | Out-Null
        }

        if ($reactorReps.Count -eq 0 -or $winuiReps.Count -eq 0) {
            Write-Warning "  no successful reps for $cellTag"
            continue
        }

        # Median across reps.
        $rP50 = Get-Median ($reactorReps | ForEach-Object { $_[0].P50Ms })
        $rP95 = Get-Median ($reactorReps | ForEach-Object { $_[0].P95Ms })
        $rP99 = Get-Median ($reactorReps | ForEach-Object { $_[0].P99Ms })
        $rAvg = Get-Median ($reactorReps | ForEach-Object { $_[0].AvgMs })
        $wP50 = Get-Median ($winuiReps   | ForEach-Object { $_[0].P50Ms })
        $wP95 = Get-Median ($winuiReps   | ForEach-Object { $_[0].P95Ms })
        $wP99 = Get-Median ($winuiReps   | ForEach-Object { $_[0].P99Ms })
        $wAvg = Get-Median ($winuiReps   | ForEach-Object { $_[0].AvgMs })

        $deltaP50 = if ($wP50 -gt 0) { [Math]::Round(100.0 * ($rP50 - $wP50) / $wP50, 1) } else { $null }
        $deltaP95 = if ($wP95 -gt 0) { [Math]::Round(100.0 * ($rP95 - $wP95) / $wP95, 1) } else { $null }
        $deltaAvg = if ($wAvg -gt 0) { [Math]::Round(100.0 * ($rAvg - $wAvg) / $wAvg, 1) } else { $null }

        # Median across reps — wall clock + memory.
        $rWall   = Get-Median ($reactorReps | ForEach-Object { $_[0].WallClockMs })
        $wWall   = Get-Median ($winuiReps   | ForEach-Object { $_[0].WallClockMs })
        $rFrames = Get-Median ($reactorReps | ForEach-Object { [double]$_[0].Frames })
        $wFrames = Get-Median ($winuiReps   | ForEach-Object { [double]$_[0].Frames })
        $rWS     = Get-Median ($reactorReps | ForEach-Object { [double]$_[0].WorkingSetMB })
        $wWS     = Get-Median ($winuiReps   | ForEach-Object { [double]$_[0].WorkingSetMB })
        $rPeakWS = Get-Median ($reactorReps | ForEach-Object { [double]$_[0].PeakWSMB })
        $wPeakWS = Get-Median ($winuiReps   | ForEach-Object { [double]$_[0].PeakWSMB })
        $rPriv   = Get-Median ($reactorReps | ForEach-Object { [double]$_[0].PrivateMB })
        $wPriv   = Get-Median ($winuiReps   | ForEach-Object { [double]$_[0].PrivateMB })
        $rHeap   = Get-Median ($reactorReps | ForEach-Object { [double]$_[0].ManagedHeapMB })
        $wHeap   = Get-Median ($winuiReps   | ForEach-Object { [double]$_[0].ManagedHeapMB })

        $deltaPeak = if ($wPeakWS -gt 0) { [Math]::Round(100.0 * ($rPeakWS - $wPeakWS) / $wPeakWS, 1) } else { $null }
        $deltaHeap = if ($wHeap   -gt 0) { [Math]::Round(100.0 * ($rHeap   - $wHeap)   / $wHeap,   1) } else { $null }

        $rows.Add([PSCustomObject]@{
            Cell                  = $cellTag
            Count                 = $count
            EditsPerSec           = $eps
            Reps                  = $reactorReps.Count
            Reactor_P50_med       = [Math]::Round($rP50, 2)
            WinUI_P50_med         = [Math]::Round($wP50, 2)
            DeltaP50_pct          = $deltaP50
            Reactor_P95_med       = [Math]::Round($rP95, 2)
            WinUI_P95_med         = [Math]::Round($wP95, 2)
            DeltaP95_pct          = $deltaP95
            Reactor_P99_med       = [Math]::Round($rP99, 2)
            WinUI_P99_med         = [Math]::Round($wP99, 2)
            Reactor_Avg_med       = [Math]::Round($rAvg, 2)
            WinUI_Avg_med         = [Math]::Round($wAvg, 2)
            DeltaAvg_pct          = $deltaAvg
            Reactor_WallClock_ms  = [Math]::Round($rWall, 0)
            WinUI_WallClock_ms    = [Math]::Round($wWall, 0)
            Reactor_Frames        = [int]$rFrames
            WinUI_Frames          = [int]$wFrames
            Reactor_WS_MB         = [int]$rWS
            WinUI_WS_MB           = [int]$wWS
            Reactor_PeakWS_MB     = [int]$rPeakWS
            WinUI_PeakWS_MB       = [int]$wPeakWS
            DeltaPeakWS_pct       = $deltaPeak
            Reactor_Private_MB    = [int]$rPriv
            WinUI_Private_MB      = [int]$wPriv
            Reactor_Heap_MB       = [int]$rHeap
            WinUI_Heap_MB         = [int]$wHeap
            DeltaHeap_pct         = $deltaHeap
        }) | Out-Null
    }
}

# Emit CSV summary, per-rep log, and markdown.
$allRunsLog | Export-Csv -NoTypeInformation -Path (Join-Path $OutDir "per-rep.csv")
$rows       | Export-Csv -NoTypeInformation -Path (Join-Path $OutDir "summary.csv")

Write-Host "`n=== Median Summary (across $Repetitions reps) ===" -ForegroundColor Cyan
$rows | Format-Table -AutoSize | Out-Host

$md = @()
$md += "# Spec 042 perf gate — Reactor vs WinUI vanilla virtualizing list"
$md += ""
$md += "Captured: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
$md += "Duration: ${DurationSeconds}s per run, ${Repetitions}× repetitions, warm-up discarded (${WarmupReps} rep)."
$md += ""
$md += "Reactor variant: ``StressPerf.VirtualList.Reactor`` — Reactor ``LazyVStack<ListItem>`` on top of ``ItemsRepeater`` + ``KeyedListDiff`` from spec 042 Phase 1."
$md += "WinUI variant:   ``StressPerf.VirtualList.WinUI`` — hand-written ``ItemsRepeater`` + ``ObservableCollection<ListItem>`` + recycling element factory. Edits mutate the OC in place."
$md += ""
$md += "Both variants share ``StressPerf.Shared.ListItemSource`` for data + row metrics, identical scroll tween, identical edit policy (50/50 insert/remove, deterministic seed 1234567)."
$md += ""
$md += "## Median across ${Repetitions} reps (ms — lower is better)"
$md += ""
$md += "| Count | Edits/s | Reps | Reactor P50 | WinUI P50 | Δ P50 % | Reactor P95 | WinUI P95 | Δ P95 % | Reactor Avg | WinUI Avg | Δ Avg % |"
$md += "|------:|--------:|-----:|------------:|----------:|--------:|------------:|----------:|--------:|------------:|----------:|--------:|"
foreach ($row in $rows) {
    $md += ("| {0,6} | {1,7} | {2,4} | {3,11:F2} | {4,9:F2} | {5,7} | {6,11:F2} | {7,9:F2} | {8,7} | {9,11:F2} | {10,9:F2} | {11,7} |" -f `
        $row.Count, $row.EditsPerSec, $row.Reps, $row.Reactor_P50_med, $row.WinUI_P50_med, $row.DeltaP50_pct, `
        $row.Reactor_P95_med, $row.WinUI_P95_med, $row.DeltaP95_pct, $row.Reactor_Avg_med, $row.WinUI_Avg_med, $row.DeltaAvg_pct)
}
$md += ""
$md += "Negative Δ values indicate Reactor is faster than the WinUI baseline."
$md += ""
$md += "## Pass criteria"
$md += ""
$md += "Spec 042 perf gate: Reactor stays within **+5%** of WinUI on the steady-state scroll case, **+10%** under the edit-stress mode. The reconciler does extra work (it computes a keyed diff between two array references before producing OC events) so a small positive delta is expected; a delta larger than the threshold is a regression."
$md += ""
$md += "## Raw artefacts"
$md += ""
$md += "- ``summary.csv`` — one row per cell, median across reps"
$md += "- ``per-rep.csv`` — every individual rep's percentiles"
$md += "- ``{reactor,winui}.<cell>.rep<N>.frames.csv`` — every captured frame delta per rep, for forensic analysis"

$mdPath = Join-Path $OutDir "summary.md"
$md -join "`n" | Set-Content $mdPath -Encoding UTF8

Write-Host "Summary written:"
Write-Host "  $mdPath"
Write-Host "  $(Join-Path $OutDir 'summary.csv')"
Write-Host "  $(Join-Path $OutDir 'per-rep.csv')"
