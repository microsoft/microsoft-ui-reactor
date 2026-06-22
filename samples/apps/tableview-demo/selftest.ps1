#!/usr/bin/env pwsh
# Self-test: builds + headlessly runs the TableView-in-Reactor demo and asserts the native
# C++/WinRT TableView (separate-binary Advanced.dll, projected vs public WinAppSDK 2.0.1):
#   (1) ACTIVATES inside the Reactor mount path (TVDEMO_SELFTEST=1), and
#   (2) actually RENDERS its body -- headers + rows (TVDEMO_SHOT=1 walks the control's visual tree,
#       composition-independent, and logs the rendered cell count). This guards the blank-body
#       regression where the control activates but its default Style/template never inflates.
# Intended for local verification and as the basis of a CI step.
param([string]$Configuration = 'Release', [string]$Platform = 'x64')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Write-Host "[selftest] building TableViewDemo ($Configuration|$Platform)..."
dotnet build "$root\TableViewDemo.csproj" -c $Configuration -p:Platform=$Platform | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "build failed"; exit 1 }
$exe = Get-ChildItem "$root\bin" -Recurse -Filter 'TableViewDemo.exe' | Select-Object -First 1
$out = $exe.DirectoryName
$log = Join-Path $out 'tvdemo-selftest.log'
Remove-Item $log -ErrorAction SilentlyContinue
Write-Host "[selftest] running headless self-test..."
$env:TVDEMO_SELFTEST = '1'
$p = Start-Process -FilePath $exe.FullName -PassThru -WorkingDirectory $out
$null = $p.WaitForExit(30000)
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
$env:TVDEMO_SELFTEST = $null
if (-not (Test-Path $log)) { Write-Error "[selftest] FAIL: no self-test log (TableView never mounted)"; exit 1 }
$content = Get-Content $log -Raw
Write-Host $content
if (-not ($content -match 'PASS:.*native.*TableView.*activated')) {
    Write-Error "[selftest] FAIL — activation assertion not met."
    exit 1
}
Write-Host "[selftest] activation OK; checking the control actually renders its body..." -ForegroundColor Green

# Render check. The demo (TVDEMO_SHOT=1) measures/arranges the control and walks its visual tree for
# TextBlocks -- a composition-independent signal that survives headless/RDP-disconnected agents -- and
# logs 'rendered text (N)'. N>0 proves headers + rows actually rendered (the satellite control's
# Style/template inflated); N==0 is the blank-body bug.
$shot = Join-Path $out 'tvdemo-shot.log'
Remove-Item $shot -ErrorAction SilentlyContinue
$env:TVDEMO_SHOT = '1'
$p = Start-Process -FilePath $exe.FullName -PassThru -WorkingDirectory $out
$null = $p.WaitForExit(30000)
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
$env:TVDEMO_SHOT = $null
if (-not (Test-Path $shot)) { Write-Error "[selftest] FAIL: no render log produced."; exit 1 }
$shotContent = Get-Content $shot -Raw
Write-Host $shotContent
if ($shotContent -match 'rendered text \((\d+)\)' -and [int]$Matches[1] -ge 6) {
    Write-Host "[selftest] PASS — native TableView activated AND rendered $($Matches[1]) cells (headers + rows)." -ForegroundColor Green
    exit 0
}
Write-Error "[selftest] FAIL — control activated but rendered a blank body (no cells)."
exit 1
