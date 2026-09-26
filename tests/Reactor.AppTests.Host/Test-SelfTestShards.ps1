<#
.SYNOPSIS
    Checks that a selftest Host's `--shard` listings partition its corpus, and reports how many
    fixtures one shard selects.

.DESCRIPTION
    The MSTest wrappers check this in `Shards_PartitionTheCorpus`. The AOT job and the coverage
    lanes run the Host directly, so they check it here instead, with the same rules applied to the
    same Host output:

      - every non-pinned fixture is in exactly one shard, and nothing outside the corpus is listed;
      - every pinned control is in every shard;
      - every shard selects at least one fixture beyond the pins.

    With -Shard, also prints `expected=<n>` for that shard (and writes it to $GITHUB_OUTPUT when
    set), so the caller can check the run's TAP plan against it.

    The pinned names are read from tests/_shared/SelfTestShard.cs, the file the Host itself
    compiles, so this script cannot drift from the Host's list.

.EXAMPLE
    ./tests/Reactor.AppTests.Host/Test-SelfTestShards.ps1 -HostExe $exe -Count 2 -Shard 1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HostExe,
    [Parameter(Mandatory)][ValidateRange(1, 64)][int]$Count,
    [ValidateRange(0, 64)][int]$Shard = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $HostExe -PathType Leaf)) { throw "Host not found: $HostExe" }
if ($Shard -gt $Count) { throw "-Shard $Shard is outside 1..$Count." }

$shardSource = Join-Path $PSScriptRoot '..\_shared\SelfTestShard.cs'
$declaration = [regex]::Match((Get-Content -LiteralPath $shardSource -Raw),
    'PinnedFixtures\s*=\s*\[(?<body>[^\]]*)\]')
if (-not $declaration.Success) { throw "Could not find SelfTestShard.PinnedFixtures in $shardSource." }
$pins = @([regex]::Matches($declaration.Groups['body'].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
if ($pins.Count -eq 0) { throw "SelfTestShard.PinnedFixtures in $shardSource names no fixtures." }

function Get-Listing([string[]]$ExtraArgs) {
    # Piped, not assigned: the Host is a GUI-subsystem exe, and PowerShell neither waits for one
    # nor captures its output unless the call is part of a pipeline.
    $lines = & $HostExe --list-fixtures @ExtraArgs 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        throw "``--list-fixtures $ExtraArgs`` exited $LASTEXITCODE`n$($lines -join "`n")"
    }
    return , @($lines | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
}

$corpus = Get-Listing @()
if ($corpus.Count -eq 0) { throw 'The unsharded listing is empty.' }
$inCorpus = [System.Collections.Generic.HashSet[string]]::new([string[]]$corpus, [System.StringComparer]::Ordinal)
$pinned = @($pins | Where-Object { $inCorpus.Contains($_) })

$problems = [System.Collections.Generic.List[string]]::new()
$owners = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.List[int]]]::new([System.StringComparer]::Ordinal)
$sizes = @{}
for ($k = 1; $k -le $Count; $k++) {
    $listing = Get-Listing @('--shard', "$k/$Count")
    $sizes[$k] = $listing.Count
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($fixture in $listing) {
        if (-not $seen.Add($fixture)) { $problems.Add("shard $k/$Count lists '$fixture' more than once.") }
        if (-not $inCorpus.Contains($fixture)) { $problems.Add("shard $k/$Count lists '$fixture', which is not in the unsharded corpus.") }
        if ($pinned -notcontains $fixture) {
            if (-not $owners.ContainsKey($fixture)) { $owners[$fixture] = [System.Collections.Generic.List[int]]::new() }
            if (-not $owners[$fixture].Contains($k)) { $owners[$fixture].Add($k) }
        }
    }
    foreach ($pin in $pinned) {
        if (-not $seen.Contains($pin)) { $problems.Add("shard $k/$Count is missing pinned fixture '$pin'.") }
    }
    if (-not ($listing | Where-Object { $pinned -notcontains $_ })) {
        $problems.Add("shard $k/$Count selects no fixtures beyond the pinned controls.")
    }
}
foreach ($fixture in $corpus) {
    if ($pinned -contains $fixture) { continue }
    if (-not $owners.ContainsKey($fixture)) { $problems.Add("'$fixture' is in no shard, so a sharded run never executes it.") }
    elseif ($owners[$fixture].Count -gt 1) { $problems.Add("'$fixture' is in shards $($owners[$fixture] -join ', '); it must be in exactly one.") }
}

$summary = (1..$Count | ForEach-Object { "$_/$Count=$($sizes[$_])" }) -join ', '
if ($problems.Count -gt 0) {
    $shown = ($problems | Select-Object -First 20) -join "`n  "
    $more = if ($problems.Count -gt 20) { "`n  ...and $($problems.Count - 20) more" } else { '' }
    throw "The --shard listings do not partition the $($corpus.Count)-fixture corpus ($summary):`n  $shown$more"
}
Write-Host "Shard partition OK: $($corpus.Count) fixtures, $($pinned.Count) pinned in every shard; $summary."

if ($Shard -gt 0) {
    "expected=$($sizes[$Shard])"
    if ($env:GITHUB_OUTPUT) { "expected=$($sizes[$Shard])" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
}
