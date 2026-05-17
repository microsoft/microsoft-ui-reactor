<#
.SYNOPSIS
  Runs the canonical merged (unit + selftest) coverage workflow.

.DESCRIPTION
  Wraps the recipe from CONTRIBUTING.md so each machine/agent session can
  reproduce coverage with a single command. Outputs:

    coverage/unit.cobertura.xml
    coverage/selftest.cobertura.xml
    coverage/merged.cobertura.xml

  The output directory is .gitignored (`coverage/`).

.PARAMETER Platform
  x64 or ARM64. Defaults to the current process arch.

.PARAMETER UnitOnly
  Skip the selftest pass. Useful for fast iteration when adding unit tests.

.PARAMETER SkipBuild
  Skip the explicit Debug rebuild step. Only use this if you have just
  built with -p:Optimize=false -p:DebugType=portable on your own.

.EXAMPLE
  pwsh tools/coverage/run-coverage.ps1
  pwsh tools/coverage/run-coverage.ps1 -UnitOnly
  pwsh tools/coverage/run-coverage.ps1 -Platform ARM64
#>
[CmdletBinding()]
param(
  [ValidateSet('x64','ARM64')]
  [string]$Platform = $(if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'ARM64' } else { 'x64' }),
  [switch]$UnitOnly,
  [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
Set-Location $repoRoot

$outDir = Join-Path $repoRoot 'coverage'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$unitOut     = Join-Path $outDir 'unit.cobertura.xml'
$selfOut     = Join-Path $outDir 'selftest.cobertura.xml'
$mergedOut   = Join-Path $outDir 'merged.cobertura.xml'

if (-not (Get-Command dotnet-coverage -ErrorAction SilentlyContinue)) {
  Write-Host '==> Installing dotnet-coverage (global tool)' -ForegroundColor Yellow
  dotnet tool install -g dotnet-coverage
}

if (-not $SkipBuild) {
  Write-Host "==> Rebuilding test + product with portable PDBs (Platform=$Platform)" -ForegroundColor Cyan
  dotnet build tests/Reactor.Tests          -c Debug -p:Platform=$Platform -p:Optimize=false -p:DebugType=portable
  if ($LASTEXITCODE -ne 0) { throw "dotnet build (Reactor.Tests) failed" }

  if (-not $UnitOnly) {
    dotnet build src/Reactor                  -c Debug                       -p:Optimize=false -p:DebugType=portable --no-incremental
    if ($LASTEXITCODE -ne 0) { throw "dotnet build (src/Reactor) failed" }

    dotnet build tests/Reactor.AppTests.Host  -c Debug -p:Platform=$Platform -p:Optimize=false -p:DebugType=portable --no-incremental
    if ($LASTEXITCODE -ne 0) { throw "dotnet build (AppTests.Host) failed" }
  }
}

Write-Host '==> Unit coverage' -ForegroundColor Cyan
dotnet-coverage collect -s coverage.settings.xml `
  --output $unitOut --output-format cobertura `
  -- dotnet test tests/Reactor.Tests --no-build -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) { throw "Unit coverage collect failed" }

if ($UnitOnly) {
  Write-Host "==> UnitOnly: copying unit -> merged for downstream reporting" -ForegroundColor Yellow
  Copy-Item $unitOut $mergedOut -Force
} else {
  Write-Host '==> Locating Reactor.dll for static instrumentation' -ForegroundColor Cyan
  $dll = Get-ChildItem -Path "tests/Reactor.AppTests.Host/bin" -Recurse -Filter "Reactor.dll" |
           Where-Object { $_.FullName -notmatch 'ref[\\/]' } |
           Select-Object -First 1
  if (-not $dll) { throw "Reactor.dll not found under tests/Reactor.AppTests.Host/bin" }
  Write-Host "    Instrumenting: $($dll.FullName)"
  dotnet-coverage instrument $dll.FullName -s coverage.settings.xml
  if ($LASTEXITCODE -ne 0) { throw "instrument failed" }

  Write-Host '==> Selftest coverage' -ForegroundColor Cyan
  dotnet-coverage collect -s coverage.settings.xml `
    --output $selfOut --output-format cobertura `
    -- dotnet run --project tests/Reactor.AppTests.Host --no-build -p:Platform=$Platform -- --self-test
  if ($LASTEXITCODE -ne 0) { throw "Selftest coverage collect failed" }

  Write-Host '==> Merging cobertura reports' -ForegroundColor Cyan
  dotnet-coverage merge $unitOut $selfOut --output $mergedOut --output-format cobertura
  if ($LASTEXITCODE -ne 0) { throw "merge failed" }
}

# Summary
[xml]$x = Get-Content $mergedOut
$line = [math]::Round([double]$x.coverage.'line-rate' * 100, 2)
$covered = 0; $total = 0
foreach ($l in $x.SelectNodes('//line[@condition-coverage]')) {
  if ($l.'condition-coverage' -match '\((\d+)/(\d+)\)') {
    $covered += [int]$Matches[1]; $total += [int]$Matches[2]
  }
}
$branch = if ($total -gt 0) { [math]::Round(100.0 * $covered / $total, 2) } else { 0 }

Write-Host ''
Write-Host "===== Merged coverage =====" -ForegroundColor Green
Write-Host ("  Line   : {0,6:F2}%" -f $line)
Write-Host ("  Branch : {0,6:F2}%  ({1}/{2})" -f $branch, $covered, $total)
Write-Host "  Output : $mergedOut"
