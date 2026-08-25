<#
.SYNOPSIS
    Measure merged (unit + selftest) coverage for ONE source tree and emit a small
    JSON of the line/branch numbers for the coverage PR comment
    (.github/workflows/coverage.yml).

.DESCRIPTION
    Runs the exact merged-coverage recipe from TESTING.md against $Root:

      1. Build Reactor.Tests, src/Reactor, and Reactor.AppTests.Host (Debug, portable
         PDBs, no optimization) so the assemblies can be instrumented.
      2. Collect UNIT coverage (dotnet test Reactor.Tests) as cobertura.
      3. Statically instrument the built Reactor.dll.
      4. Collect SELFTEST coverage (the AppTests.Host --self-test run) as cobertura.
      5. Merge the two cobertura reports.
      6. Aggregate the merged report into line % + branch % (+ branch covered/total)
         via Get-CoberturaRates (CoverageLib.ps1).

    The output JSON is consumed by CoverageLib.ps1's Format-CoverageComment:
      { "line": <num|null>, "branch": <num|null>,
        "branchesCovered": <int|null>, "branchesTotal": <int|null>,
        "outcome": "success" }

    Intermediate + merged cobertura reports are written to -WorkDir so the workflow
    can upload them as debugging artifacts. The script throws on any build/collect
    failure so the caller can mark the step failed; the workflow runs the base leg
    with continue-on-error so a base failure degrades to "baseline unavailable"
    rather than breaking the PR's coverage check.

    Run locally (measures the current tree):
      pwsh tests/coverage/ci/Measure-Coverage.ps1 -Root . -OutFile head.coverage.json
    (install once: dotnet tool install -g dotnet-coverage)
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$OutFile,
    [string]$WorkDir,
    [string]$Platform = 'x64',
    [string]$SettingsFile = 'coverage.settings.xml'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'CoverageLib.ps1')

$Root = (Resolve-Path -LiteralPath $Root).Path

# Ensure the -OutFile parent exists before Set-Content at the end (Set-Content does
# not create intermediate directories).
$outParent = Split-Path -Parent $OutFile
if ($outParent -and -not (Test-Path -LiteralPath $outParent)) {
    New-Item -ItemType Directory -Force -Path $outParent | Out-Null
}

if (-not $WorkDir) {
    # Derive a stable, tree-specific work dir so a base + head run in the same job
    # never share (or clobber) each other's cobertura output.
    $hash = [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA1]::Create().ComputeHash(
            [System.Text.Encoding]::UTF8.GetBytes($Root))).Replace('-', '').Substring(0, 12)
    $WorkDir = Join-Path ([System.IO.Path]::GetTempPath()) "reactor-coverage-$hash"
}
if (Test-Path -LiteralPath $WorkDir) { Remove-Item -LiteralPath $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
$WorkDir = (Resolve-Path -LiteralPath $WorkDir).Path

$unitReport     = Join-Path $WorkDir 'unit.cobertura.xml'
$selftestReport = Join-Path $WorkDir 'selftest.cobertura.xml'
$mergedReport   = Join-Path $WorkDir 'merged.cobertura.xml'

function Invoke-Checked {
    <#
    .SYNOPSIS
        Run a native command and throw on a non-zero exit code, so a failed build /
        collect / merge aborts the measurement (rather than silently producing a
        bogus report).
    #>
    param([Parameter(Mandatory)][string]$What, [Parameter(Mandatory)][scriptblock]$Action)
    Write-Host "==> $What"
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$What failed (exit $LASTEXITCODE)."
    }
}

Push-Location $Root
try {
    # 1. Build for instrumentation. Reactor.Tests + AppTests.Host build for the
    #    requested platform; src/Reactor is AnyCPU (a library), matching coverage.yml.
    Invoke-Checked "Build Reactor.Tests ($Platform)" {
        dotnet build tests/Reactor.Tests -c Debug -p:Platform=$Platform -p:Optimize=false -p:DebugType=portable
    }
    Invoke-Checked 'Build src/Reactor (AnyCPU)' {
        dotnet build src/Reactor -c Debug -p:Optimize=false -p:DebugType=portable --no-incremental
    }
    Invoke-Checked "Build Reactor.AppTests.Host ($Platform)" {
        dotnet build tests/Reactor.AppTests.Host -c Debug -p:Platform=$Platform -p:Optimize=false -p:DebugType=portable --no-incremental
    }

    # 2. Unit coverage.
    Invoke-Checked 'Collect unit coverage' {
        dotnet-coverage collect -s $SettingsFile `
            --output $unitReport --output-format cobertura `
            -- dotnet test tests/Reactor.Tests --no-build -p:Platform=$Platform
    }

    # 3. Instrument the built product DLLs (dynamic instrumentation skips referenced
    #    assemblies, so they are instrumented statically). Exclude the ref/ facade copy.
    #    Spec 062 Track B moved charting/docking/markdown/data-grid into
    #    Reactor.Advanced.dll, so it must be instrumented alongside core — otherwise the
    #    selftest leg contributes no coverage for those subsystems.
    foreach ($dllName in @('Reactor.dll', 'Reactor.Advanced.dll')) {
        $dll = Get-ChildItem -Path (Join-Path $Root 'tests/Reactor.AppTests.Host/bin') -Recurse -Filter $dllName -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch 'ref[\\/]' } |
            Select-Object -First 1
        if (-not $dll) { throw "$dllName not found under tests/Reactor.AppTests.Host/bin" }
        Invoke-Checked "Instrument $($dll.Name)" {
            dotnet-coverage instrument $dll.FullName -s $SettingsFile
        }.GetNewClosure()
    }

    # 4. Selftest coverage.
    Invoke-Checked 'Collect selftest coverage' {
        dotnet-coverage collect -s $SettingsFile `
            --output $selftestReport --output-format cobertura `
            -- dotnet run --project tests/Reactor.AppTests.Host --no-build -p:Platform=$Platform -- --self-test
    }

    # 5. Merge.
    Invoke-Checked 'Merge cobertura reports' {
        dotnet-coverage merge $unitReport $selftestReport `
            --output $mergedReport --output-format cobertura
    }
}
finally {
    Pop-Location
}

# 6. Aggregate the merged report into the headline numbers.
$rates = Get-CoberturaRates -Path $mergedReport
Write-Host ''
Write-Host "Line:   $(Format-Percent $rates.Line)"
Write-Host "Branch: $(Format-BranchCell $rates.Branch $rates.BranchesCovered $rates.BranchesTotal)"

$payload = [ordered]@{
    line            = $rates.Line
    branch          = $rates.Branch
    branchesCovered = $rates.BranchesCovered
    branchesTotal   = $rates.BranchesTotal
    outcome         = 'success'
}
$json = ConvertTo-Json -InputObject $payload -Depth 3
Set-Content -LiteralPath $OutFile -Value $json -Encoding UTF8
Write-Host ''
Write-Host "Wrote coverage numbers to $OutFile"
Write-Host "Cobertura reports in $WorkDir"
