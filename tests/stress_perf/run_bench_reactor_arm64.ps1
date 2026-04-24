$exe = Join-Path $PSScriptRoot 'StressPerf.Reactor\bin\arm64\Release\net9.0-windows10.0.22621.0\StressPerf.Reactor.exe'
if (-not (Test-Path $exe -PathType Leaf)) {
    Write-Error "Benchmark executable not found at expected path:`n  $exe`nBuild StressPerf.Reactor for arm64 Release (TFM net9.0-windows10.0.22621.0) first, e.g. `n  dotnet build tests/stress_perf/StressPerf.Reactor/StressPerf.Reactor.csproj -c Release -p:Platform=arm64"
    exit 1
}
$reportPath = Join-Path (Split-Path $exe) 'StressPerf.Reactor.report.txt'
$resultsDir = Join-Path $PSScriptRoot 'bench-results'
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

$runs = if ($args[0]) { [int]$args[0] } else { 10 }
$percent = if ($args[1]) { $args[1] } else { '50' }
$duration = if ($args[2]) { $args[2] } else { '7' }

for ($i = 1; $i -le $runs; $i++) {
    Write-Host "=== run $i / $runs ==="
    if (Test-Path $reportPath) { Remove-Item $reportPath -Force }
    $p = Start-Process -FilePath $exe -ArgumentList "--headless --percent $percent --duration $duration" -PassThru -Wait
    Write-Host "  exit=$($p.ExitCode)"
    if (-not (Test-Path $reportPath)) {
        Write-Warning "  no report produced"
        continue
    }
    Copy-Item $reportPath (Join-Path $resultsDir "run-$i.txt")
}
Write-Host "Done. Reports at $resultsDir"
