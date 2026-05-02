param(
    [int]$Duration = 7,
    [int[]]$Percents = @(10, 50, 100)
)

$ErrorActionPreference = 'Stop'
$stressDir = $PSScriptRoot
$resultsDir = Join-Path $stressDir 'sweep-results'
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

$variants = @(
    @{ Name = 'WinUI.Direct';      Exe = "$stressDir\StressPerf.Direct\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.Direct.exe" },
    @{ Name = 'WinUI.Bound';       Exe = "$stressDir\StressPerf.Bound\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.Bound.exe" },
    @{ Name = 'WinUI.Reactor';     Exe = "$stressDir\StressPerf.Reactor\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.Reactor.exe" },
    @{ Name = 'WinUI.ReactorGrid'; Exe = "$stressDir\StressPerf.ReactorGrid\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.ReactorGrid.exe" },
    @{ Name = 'WinUI.DirectX';     Exe = "$stressDir\StressPerf.DirectX\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.DirectX.exe" },
    @{ Name = 'WPF.Direct';        Exe = "$stressDir\StressPerf.Wpf\bin\ARM64\Release\net9.0-windows\StressPerf.Wpf.exe" }
)

$rows = @()
$rows += 'App,Percent,Duration_s,Avg_FPS,Min_FPS,Max_FPS,Avg_Update_ms,Max_Update_ms,Avg_Memory_MB,Peak_Memory_MB'

function Parse-Report {
    param([string]$File, [string]$App, [int]$Pct)
    if (-not (Test-Path $File)) {
        return "$App,$Pct,0,0,0,0,0,0,0,0"
    }
    $text = Get-Content -Raw $File
    function GetVal([string]$pat) {
        $m = [regex]::Match($text, $pat)
        if ($m.Success) { return $m.Groups[1].Value } else { return '' }
    }
    $dur     = GetVal 'Duration:\s+([0-9.]+)\s*s'
    $avgFps  = GetVal 'Avg FPS:\s+([0-9.]+)'
    $minFps  = GetVal 'Min FPS:\s+([0-9.]+)'
    $maxFps  = GetVal 'Max FPS:\s+([0-9.]+)'
    $avgUpd  = GetVal 'Avg Update:\s+([0-9.]+)\s*ms'
    $maxUpd  = GetVal 'Max Update:\s+([0-9.]+)\s*ms'
    $avgMem  = GetVal 'Avg Memory:\s+([0-9.]+)\s*MB'
    $peakMem = GetVal 'Peak Memory:\s+([0-9.]+)\s*MB'
    return "$App,$Pct,$dur,$avgFps,$minFps,$maxFps,$avgUpd,$maxUpd,$avgMem,$peakMem"
}

foreach ($pct in $Percents) {
    Write-Host "--- $pct% update rate ---"
    foreach ($v in $variants) {
        $exe = $v.Exe
        $name = $v.Name
        if (-not (Test-Path $exe)) {
            Write-Host "  SKIP $name (missing: $exe)"
            $rows += "$name,$pct,0,0,0,0,0,0,0,0"
            continue
        }
        $exeDir = Split-Path $exe
        Get-ChildItem -Path $exeDir -Filter '*.report.txt' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

        Write-Host ("  Running {0,-20} @ {1,3}% ..." -f $name, $pct)
        $proc = Start-Process -FilePath $exe -ArgumentList "--headless --percent $pct --duration $Duration" -PassThru -Wait -WindowStyle Hidden
        Write-Host "    exit=$($proc.ExitCode)"

        $report = Get-ChildItem -Path $exeDir -Filter '*.report.txt' -ErrorAction SilentlyContinue | Select-Object -First 1
        $row = Parse-Report -File ($report.FullName) -App $name -Pct $pct
        $rows += $row

        if ($report) {
            $copy = Join-Path $resultsDir ("{0}_{1}pct.txt" -f $name, $pct)
            Copy-Item $report.FullName $copy -Force
        }
    }
}

$out = Join-Path $stressDir 'sweep_results.csv'
$rows | Set-Content -Path $out -Encoding utf8
Write-Host ""
Write-Host "Done. CSV: $out"
Write-Host "Reports: $resultsDir"
$rows | ForEach-Object { Write-Host $_ }
