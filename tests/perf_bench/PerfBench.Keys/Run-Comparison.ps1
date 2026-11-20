param(
    [Parameter(Mandatory)][string]$BaselineRoot,
    [Parameter(Mandatory)][string]$OutDir,
    [ValidateRange(2, 100)][int]$Reps = 6,
    [ValidateSet('x64', 'ARM64')][string]$Platform = 'x64'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$BaselineRoot = (Resolve-Path $BaselineRoot).Path
if ($root -eq $BaselineRoot) { throw 'Baseline must be a separate checkout.' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
. (Join-Path $root 'tests\stress_perf\ci\PerfLib.ps1')

$relative = 'tests\perf_bench\PerfBench.Keys'
$baselineProject = Join-Path $BaselineRoot $relative
$created = [Collections.Generic.List[string]]::new()
$oldTiering = $env:DOTNET_TieredCompilation
try {
    New-Item -ItemType Directory -Path $baselineProject -Force | Out-Null
    foreach ($name in 'PerfBench.Keys.csproj', 'Program.cs') {
        $source = Join-Path $PSScriptRoot $name
        $destination = Join-Path $baselineProject $name
        if (Test-Path $destination) {
            if ((Get-FileHash $source).Hash -ne (Get-FileHash $destination).Hash) {
                throw "Baseline already contains different benchmark source: $destination"
            }
        } else {
            Copy-Item -LiteralPath $source -Destination $destination
            $created.Add($destination)
        }
    }

    $trees = @{ baseline = $BaselineRoot; prototype = $root }
    $assemblies = @{}
    $hashes = @{}
    foreach ($side in 'baseline', 'prototype') {
        $project = Join-Path $trees[$side] $relative
        & dotnet restore $project "-p:Platform=$Platform" --verbosity quiet *> (Join-Path $OutDir "$side-restore.log")
        if ($LASTEXITCODE -ne 0) { throw "$side restore failed; see $OutDir\$side-restore.log" }
        & dotnet build $project --no-restore -c Release "-p:Platform=$Platform" -p:SkipSignaturesGen=true -m:1 -nodereuse:false --verbosity quiet *> (Join-Path $OutDir "$side-build.log")
        if ($LASTEXITCODE -ne 0) { throw "$side build failed; see $OutDir\$side-build.log" }
        $rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
        $bin = Join-Path $project "bin\$Platform\Release\net10.0-windows10.0.22621.0\$rid"
        $assemblies[$side] = Join-Path $bin 'PerfBench.Keys.dll'
        $hashes[$side] = (Get-FileHash (Join-Path $bin 'Reactor.dll')).Hash
    }

    $env:DOTNET_TieredCompilation = '0'
    $runs = @{ baseline = @(); prototype = @() }
    for ($rep = 0; $rep -lt $Reps; $rep++) {
        $order = if ($rep % 2 -eq 0) { @('baseline', 'prototype') } else { @('prototype', 'baseline') }
        foreach ($side in $order) {
            $output = Join-Path $OutDir "$side-$rep.json"
            & dotnet $assemblies[$side] $output *> (Join-Path $OutDir "$side-$rep.log")
            if ($LASTEXITCODE -ne 0) { throw "$side run $rep failed (exit $LASTEXITCODE); see $OutDir\$side-$rep.log" }
            $samples = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
            if (-not $samples.Count) { throw "$side run $rep produced no measurements." }
            $run = @{}
            foreach ($group in ($samples | Group-Object Name)) {
                if ($group.Count -ne 12) { throw "Incomplete samples for $($group.Name)." }
                $run[$group.Name] = @{
                    ns = Get-PerfMedian @($group.Group.NsPerOp)
                    bytes = Get-PerfMedian @($group.Group.BytesPerOp)
                }
            }
            $runs[$side] += $run
        }
    }

    $results = foreach ($name in ($runs.baseline[0].Keys | Sort-Object)) {
        $bNs = @($runs.baseline | ForEach-Object { $_[$name].ns })
        $pNs = @($runs.prototype | ForEach-Object { $_[$name].ns })
        $bBytes = @($runs.baseline | ForEach-Object { $_[$name].bytes })
        $pBytes = @($runs.prototype | ForEach-Object { $_[$name].bytes })
        [pscustomobject]@{
            name = $name
            baselineNs = Get-PerfMedian $bNs
            prototypeNs = Get-PerfMedian $pNs
            baselineBytes = Get-PerfMedian $bBytes
            prototypeBytes = Get-PerfMedian $pBytes
            timeDelta = Get-PerfPairedDeltaStats $bNs $pNs
            allocationDelta = Get-PerfPairedDeltaStats $bBytes $pBytes
        }
    }
    [ordered]@{
        baselineCommit = (& git -C $BaselineRoot rev-parse HEAD)
        prototypeBaseCommit = (& git -C $root rev-parse HEAD)
        sourceHash = (Get-FileHash (Join-Path $PSScriptRoot 'Program.cs')).Hash
        reactorAssemblyHashes = $hashes
        tieredCompilation = $false
        processPairs = $Reps
        results = @($results)
    } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutDir 'comparison.json')
    $results | Format-Table name, baselineBytes, prototypeBytes, baselineNs, prototypeNs -AutoSize
} finally {
    $env:DOTNET_TieredCompilation = $oldTiering
    foreach ($path in $created) { Remove-Item -LiteralPath $path }
}
