param(
    [Parameter(Mandatory)][string]$BaselineBin,
    [Parameter(Mandatory)][string]$CandidateBin,
    [Parameter(Mandatory)][string]$BaselineCommit,
    [Parameter(Mandatory)][string]$OutDir,
    [ValidateRange(2, 100)][int]$Pairs = 4
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
. (Join-Path $root 'tests\stress_perf\ci\PerfLib.ps1')
$bins = @{
    baseline = (Resolve-Path $BaselineBin).Path
    candidate = (Resolve-Path $CandidateBin).Path
}
if ($bins.baseline -eq $bins.candidate) { throw 'Use separately preserved binaries.' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$hashes = @{}
foreach ($side in 'baseline', 'candidate') {
    $hashes[$side] = @{
        reactor = (Get-FileHash (Join-Path $bins[$side] 'Reactor.dll')).Hash
        benchmark = (Get-FileHash (Join-Path $bins[$side] 'PerfBench.MixedKeys.dll')).Hash
    }
}
if ($hashes.baseline.benchmark -ne $hashes.candidate.benchmark) {
    throw 'Both sides must use the identical benchmark binary.'
}
$oldTiering = $env:DOTNET_TieredCompilation
$runs = @{ baseline = @(); candidate = @() }
$started = [DateTimeOffset]::Now
try {
    $env:DOTNET_TieredCompilation = '0'
    for ($pair = 0; $pair -lt $Pairs; $pair++) {
        $order = if ($pair % 2 -eq 0) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
        foreach ($side in $order) {
            $output = Join-Path $OutDir "$side-$pair.json"
            $exe = Join-Path $bins[$side] 'PerfBench.MixedKeys.exe'
            $process = Start-Process -FilePath $exe -ArgumentList "`"$output`"" -PassThru `
                -RedirectStandardOutput (Join-Path $OutDir "$side-$pair.log") `
                -RedirectStandardError (Join-Path $OutDir "$side-$pair.error.log")
            if (-not $process.WaitForExit(120000)) {
                Stop-Process -Id $process.Id
                $process.WaitForExit()
                throw "$side pair $pair exceeded 120 seconds."
            }
            if ($process.ExitCode -ne 0) {
                throw "$side pair $pair exited $($process.ExitCode); see its error log."
            }
            $samples = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
            if ($samples.Count -ne 60) { throw "Expected 60 samples, got $($samples.Count)." }
            $run = @{}
            foreach ($group in ($samples | Group-Object Name, Count)) {
                if ($group.Count -ne 6) { throw "Incomplete samples for $($group.Name)." }
                $run[$group.Name] = @{
                    ns = Get-PerfMedian @($group.Group.NsPerOp)
                    bytes = Get-PerfMedian @($group.Group.BytesPerOp)
                }
            }
            $runs[$side] += $run
            Write-Host "Completed $side pair $pair"
        }
    }
    $results = foreach ($name in ($runs.baseline[0].Keys | Sort-Object)) {
        $bNs = @($runs.baseline | ForEach-Object { $_[$name].ns })
        $cNs = @($runs.candidate | ForEach-Object { $_[$name].ns })
        [pscustomobject]@{
            name = $name
            baselineBytes = Get-PerfMedian @($runs.baseline | ForEach-Object { $_[$name].bytes })
            candidateBytes = Get-PerfMedian @($runs.candidate | ForEach-Object { $_[$name].bytes })
            baselineNs = Get-PerfMedian $bNs
            candidateNs = Get-PerfMedian $cNs
            pairedTimeDelta = Get-PerfPairedDeltaStats $bNs $cNs
        }
    }
    [ordered]@{
        baselineCommit = $BaselineCommit
        candidateBaseCommit = (& git -C $root rev-parse HEAD)
        sourceHash = (Get-FileHash (Join-Path $PSScriptRoot 'Program.cs')).Hash
        binaries = $hashes
        started = $started
        completed = [DateTimeOffset]::Now
        tieredCompilation = $false
        independentProcessPairs = $Pairs
        results = @($results)
    } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutDir 'comparison.json')
    $results | Format-Table name, baselineBytes, candidateBytes, baselineNs, candidateNs -AutoSize
} finally {
    $env:DOTNET_TieredCompilation = $oldTiering
}
