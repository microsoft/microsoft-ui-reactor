#!/usr/bin/env pwsh
# Self-test: builds + headlessly runs the TableView-in-Reactor demo with
# TVDEMO_SELFTEST=1 and asserts the native C++/WinRT TableView (separate-binary
# Advanced.dll, projected vs public WinAppSDK 2.0.1) activates inside the Reactor
# mount path. Intended for local verification and as the basis of a CI step.
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
if ($content -match 'PASS:.*native.*TableView.*activated') {
    Write-Host "[selftest] PASS — native TableView is live in the Reactor mount path." -ForegroundColor Green
    exit 0
}
Write-Error "[selftest] FAIL — activation assertion not met."
exit 1
