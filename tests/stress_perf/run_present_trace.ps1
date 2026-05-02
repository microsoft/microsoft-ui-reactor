# run_present_trace.ps1 — runs the Reactor + RN stress demos at 10/50/100%
# under PresentTracer ETW capture. Must run elevated (ETW kernel session).
#
# Output: single log file with sections per (framework, percent), each
# containing the tracer's count + interval table for matched events.

param(
  [string] $LogPath = 'C:\Users\andersonch\Code\reactor3\present-trace.log',
  [int]    $DurationSeconds = 10,
  [int[]]  $Percents = @(10, 50, 100)
)

$ErrorActionPreference = 'Continue'

$tracer  = 'C:\Users\andersonch\Code\reactor3\tests\stress_perf\PresentTracer\bin\ARM64\Release\net9.0\PresentTracer.exe'
$reactor = 'C:\Users\andersonch\Code\reactor3\tests\stress_perf\StressPerf.Reactor\bin\ARM64\Release\net9.0-windows10.0.22621.0\StressPerf.Reactor.exe'
$rn      = 'C:\Users\andersonch\Code\reactor3\tests\stress_perf_rn\StocksGrid\windows\ARM64\Release\StocksGrid.exe'

if (-not (Test-Path $tracer))  { throw "PresentTracer.exe not found at $tracer" }
if (-not (Test-Path $reactor)) { throw "Reactor exe not found at $reactor" }
if (-not (Test-Path $rn))      { throw "RN exe not found at $rn" }

# Clean previous log.
Remove-Item $LogPath -ErrorAction SilentlyContinue
"PresentTracer batch run @ $(Get-Date -Format o)" | Tee-Object -FilePath $LogPath -Append | Out-Host

function Run-Scenario {
  param(
    [string] $Framework,
    [string] $Exe,
    [int]    $Percent,
    [int]    $Duration
  )
  $appArgs = @('--headless', '--percent', "$Percent", '--duration', "$Duration")
  $section = "==== $Framework @ $Percent% / ${Duration}s ===="
  $section | Tee-Object -FilePath $LogPath -Append | Out-Host

  # Pre-kill any leftover instance.
  $exeName = [IO.Path]::GetFileNameWithoutExtension($Exe)
  Get-Process -Name $exeName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

  $proc = Start-Process -FilePath $Exe -ArgumentList $appArgs -PassThru
  Start-Sleep -Milliseconds 1500   # let WinUI initialize before tracing

  # Trace for slightly less than the bench duration so we don't catch the
  # quit-out flutter at the end.
  $traceWindow = [Math]::Max(3, $Duration - 1)
  $tracerOut = & $tracer --pid $proc.Id --duration $traceWindow 2>&1
  $tracerOut | Tee-Object -FilePath $LogPath -Append | Out-Host

  Start-Sleep -Seconds 2
  Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
  "" | Tee-Object -FilePath $LogPath -Append | Out-Host
}

foreach ($pct in $Percents) {
  Run-Scenario -Framework 'Reactor'   -Exe $reactor -Percent $pct -Duration $DurationSeconds
  Run-Scenario -Framework 'RN-Fabric' -Exe $rn      -Percent $pct -Duration $DurationSeconds
}

"DONE @ $(Get-Date -Format o)" | Tee-Object -FilePath $LogPath -Append | Out-Host
