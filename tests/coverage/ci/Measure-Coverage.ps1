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
      5. Merge the cobertura reports.
      6. Aggregate the merged report into line % + branch % (+ branch covered/total)
         via Get-CoberturaRates (CoverageLib.ps1).

    -Part picks how much of that runs, so CI can spread it across runners:

      All       everything above, in one tree (the default, and the local runbook).
      Unit      steps 1-2 for Reactor.Tests only; writes unit.cobertura.xml to -WorkDir.
      SelfTest  steps 1, 3 and 4 for the Host only; writes selftest.cobertura.xml to -WorkDir,
                or selftest-<k>.cobertura.xml when -Shard k/n runs one slice of the corpus.
      Merge     steps 5-6 over the reports already in -PartsDir. -ExpectedParts names every
                report that must be there (for example unit, selftest-1, selftest-2), and a
                missing or unexpected one fails the merge rather than skewing the numbers.

    Coverage is combined by `dotnet-coverage merge`, which takes the union of covered lines
    across reports. Averaging per-lane percentages would be wrong, because the lanes cover
    overlapping code.

    The output JSON (All and Merge) is consumed by CoverageLib.ps1's Format-CoverageComment:
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
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory)][string]$Root,
    [string]$OutFile,
    [string]$WorkDir,
    [string]$Platform = 'x64',
    [string]$SettingsFile = 'coverage.settings.xml',
    [ValidateSet('All', 'Unit', 'SelfTest', 'Merge')][string]$Part = 'All',
    [string]$Shard,
    [string]$PartsDir,
    [string[]]$ExpectedParts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Accept "unit,selftest-1" as one argument too: `pwsh -File` passes it that way.
$ExpectedParts = @($ExpectedParts | ForEach-Object { $_ -split '[,\s]+' } | Where-Object { $_ })

. (Join-Path $PSScriptRoot 'CoverageLib.ps1')

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

function Resolve-CoveragePlan {
    <#
    .SYNOPSIS
        Which steps a -Part runs, plus its validated inputs. Pure, so the lane contract is
        testable without building anything (Measure-Coverage.Tests.ps1).
    #>
    param(
        [Parameter(Mandatory)][string]$Part,
        [string]$Shard,
        [string]$OutFile,
        [string]$PartsDir,
        [string[]]$ExpectedParts
    )

    $shardIndex = 0
    if ($Shard) {
        if ($Part -ne 'SelfTest') { throw "-Shard only applies to -Part SelfTest (got -Part $Part)." }
        if ($Shard -notmatch '^\s*(\d+)/(\d+)\s*$') { throw "-Shard must look like k/n, e.g. 1/2 (got '$Shard')." }
        $shardIndex = [int]$Matches[1]
        $shardCount = [int]$Matches[2]
        if ($shardCount -lt 1 -or $shardIndex -lt 1 -or $shardIndex -gt $shardCount) {
            throw "-Shard $Shard is out of range: need 1 <= k <= n."
        }
    }
    if ($Part -in 'All', 'Merge' -and -not $OutFile) { throw "-Part $Part needs -OutFile." }
    if ($Part -eq 'Merge') {
        if (-not $PartsDir) { throw '-Part Merge needs -PartsDir.' }
        if (-not $ExpectedParts -or $ExpectedParts.Count -eq 0) { throw '-Part Merge needs -ExpectedParts.' }
    }

    $selftestReport = if ($shardIndex -gt 0) { "selftest-$shardIndex.cobertura.xml" } else { 'selftest.cobertura.xml' }
    [pscustomobject]@{
        BuildUnit      = $Part -in 'All', 'Unit'
        BuildSelfTest  = $Part -in 'All', 'SelfTest'
        CollectUnit    = $Part -in 'All', 'Unit'
        CollectSelf    = $Part -in 'All', 'SelfTest'
        Merge          = $Part -in 'All', 'Merge'
        SelfTestReport = $selftestReport
        Shard          = if ($shardIndex -gt 0) { $Shard.Trim() } else { $null }
    }
}

function Assert-ExpectedParts {
    <#
    .SYNOPSIS
        Throws unless the lane reports found are exactly the expected ones. A lane that failed to
        upload, or one nobody asked for, would otherwise shift the merged numbers without failing.
    #>
    param([string[]]$Found, [Parameter(Mandatory)][string[]]$Expected)
    $foundSet = @($Found | Sort-Object -Unique)
    $expectedSet = @($Expected | ForEach-Object { "$_.cobertura.xml" } | Sort-Object -Unique)
    $missing = @($expectedSet | Where-Object { $foundSet -notcontains $_ })
    $extra = @($foundSet | Where-Object { $expectedSet -notcontains $_ })
    if (@($Found).Count -ne $foundSet.Count) { throw "Duplicate lane reports: $($Found -join ', ')." }
    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        throw "Lane reports do not match -ExpectedParts. Missing: [$($missing -join ', ')]. Unexpected: [$($extra -join ', ')]."
    }
}

$plan = Resolve-CoveragePlan -Part $Part -Shard $Shard -OutFile $OutFile -PartsDir $PartsDir -ExpectedParts $ExpectedParts

$Root = (Resolve-Path -LiteralPath $Root).Path

# Ensure the -OutFile parent exists before Set-Content at the end (Set-Content does
# not create intermediate directories).
if ($OutFile) {
    $outParent = Split-Path -Parent $OutFile
    if ($outParent -and -not (Test-Path -LiteralPath $outParent)) {
        New-Item -ItemType Directory -Force -Path $outParent | Out-Null
    }
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
$selftestReport = Join-Path $WorkDir $plan.SelfTestReport
$mergedReport   = Join-Path $WorkDir 'merged.cobertura.xml'

Push-Location $Root
try {
    # 1. Build for instrumentation. Reactor.Tests + AppTests.Host build for the
    #    requested platform; src/Reactor is AnyCPU (a library), matching coverage.yml.
    if ($plan.BuildUnit) {
        Invoke-Checked "Build Reactor.Tests ($Platform)" {
            dotnet build tests/Reactor.Tests -c Debug -p:Platform=$Platform -p:Optimize=false -p:DebugType=portable
        }
    }
    if ($plan.BuildSelfTest) {
        Invoke-Checked 'Build src/Reactor (AnyCPU)' {
            dotnet build src/Reactor -c Debug -p:Optimize=false -p:DebugType=portable --no-incremental
        }
        Invoke-Checked "Build Reactor.AppTests.Host ($Platform)" {
            dotnet build tests/Reactor.AppTests.Host -c Debug -p:Platform=$Platform -p:Optimize=false -p:DebugType=portable --no-incremental
        }
    }

    # 2. Unit coverage.
    if ($plan.CollectUnit) {
        Invoke-Checked 'Collect unit coverage' {
            dotnet-coverage collect -s $SettingsFile `
                --output $unitReport --output-format cobertura `
                -- dotnet test tests/Reactor.Tests --no-build -p:Platform=$Platform
        }
    }

    if ($plan.CollectSelf) {
        # A shard is checked before anything is instrumented: the listings must partition the
        # corpus, and the expected count is what the run's TAP plan is held to below.
        $expected = $null
        if ($plan.Shard) {
            $hostExe = Get-ChildItem -Path (Join-Path $Root 'tests/Reactor.AppTests.Host/bin') -Recurse -Filter 'Reactor.AppTests.Host.exe' -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match "[\\/]$([regex]::Escape($Platform))[\\/]Debug[\\/]" } |
                Select-Object -First 1
            if (-not $hostExe) { throw "Reactor.AppTests.Host.exe ($Platform, Debug) not found under tests/Reactor.AppTests.Host/bin" }

            $shardIndex, $shardCount = $plan.Shard -split '/'
            $check = & (Join-Path $Root 'tests/Reactor.AppTests.Host/Test-SelfTestShards.ps1') `
                -HostExe $hostExe.FullName -Count ([int]$shardCount) -Shard ([int]$shardIndex)
            $expected = [int](($check | Where-Object { $_ -like 'expected=*' } | Select-Object -Last 1) -replace '^expected=', '')
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

        # 4. Selftest coverage. The TAP is kept so a shard's run can be held to its listing.
        $selftestArgs = @('--self-test')
        if ($plan.Shard) { $selftestArgs += @('--shard', $plan.Shard) }
        $tapFile = Join-Path $WorkDir ($plan.SelfTestReport -replace '\.cobertura\.xml$', '.tap')
        Invoke-Checked "Collect selftest coverage$(if ($plan.Shard) { " (shard $($plan.Shard))" })" {
            dotnet-coverage collect -s $SettingsFile `
                --output $selftestReport --output-format cobertura `
                -- dotnet run --project tests/Reactor.AppTests.Host --no-build -p:Platform=$Platform -- @selftestArgs |
                Tee-Object -FilePath $tapFile
        }.GetNewClosure()

        if ($null -ne $expected) {
            $tap = Get-Content -LiteralPath $tapFile
            $planLine = $tap | Where-Object { $_ -match '^1\.\.\d+$' } | Select-Object -First 1
            if (-not $planLine) { throw "The selftest run printed no TAP plan (1..N)." }
            $planned = [int]$planLine.Substring(3)
            if ($planned -ne $expected) {
                throw "The selftest run planned $planned fixture(s), but shard $($plan.Shard) lists $expected."
            }
            if (-not ($tap | Where-Object { $_ -like '# Total failures:*' })) {
                throw "The selftest run never reached its '# Total failures:' trailer."
            }
            Write-Host "Shard $($plan.Shard) ran all $planned listed fixture(s)."
        }
    }

    # 5. Merge.
    if ($plan.Merge) {
        $inputs = if ($Part -eq 'Merge') {
            $found = @(Get-ChildItem -Path $PartsDir -Recurse -Filter '*.cobertura.xml' -File)
            Assert-ExpectedParts -Found @($found | ForEach-Object Name) -Expected $ExpectedParts
            # Keep the lane reports next to the merged one, so the reports artifact has all of them.
            foreach ($report in $found) { Copy-Item -LiteralPath $report.FullName -Destination $WorkDir -Force }
            @($found | Sort-Object Name | ForEach-Object { Join-Path $WorkDir $_.Name })
        } else {
            @($unitReport, $selftestReport)
        }
        Invoke-Checked "Merge cobertura reports ($($inputs.Count))" {
            dotnet-coverage merge @inputs --output $mergedReport --output-format cobertura
        }.GetNewClosure()
    }
}
finally {
    Pop-Location
}

if (-not $plan.Merge) {
    Write-Host ''
    Write-Host "Cobertura reports in $WorkDir"
    return
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
