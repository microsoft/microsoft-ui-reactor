<#
.SYNOPSIS
    Selects one of two complementary E2E shards and proves, before anything runs, that together
    they cover the whole suite.

.DESCRIPTION
    Shard 1 runs the test classes named in -Shard1Classes. Shard 2 runs the exact complement, so a
    newly added class lands in shard 2 without anyone editing a list. This is the pattern from
    winappCli's scripts/test-cli-shard.ps1.

    The split is checked against MTP discovery (`--list-tests`): the whole suite, shard 1 and
    shard 2 must each be non-empty, and shard 1 + shard 2 must equal the whole suite. Every class
    named for shard 1 must also match at least one test; a renamed class would otherwise fall
    silently into shard 2 and unbalance the split.

    Classes are matched as `.<Name>.` inside the fully-qualified test name, so `AccessibilityTests`
    selects that class and not ChartAccessibilityTests or AccessibilityInteractionTests.

    Writes `filter=<expr>` and `expected=<n>` to $GITHUB_OUTPUT when it is set, and prints both.
    The caller passes `filter` to `dotnet test --filter` and checks the TRX total against `expected`.

.EXAMPLE
    ./tests/Reactor.AppTests/Select-E2EShard.ps1 -Shard 1 -Shard1Classes AccessibilityTests,DataGridTests
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet(1, 2)][int]$Shard,
    [Parameter(Mandatory)][string[]]$Shard1Classes,
    [string]$Project = 'tests/Reactor.AppTests/Reactor.AppTests.csproj',
    [string]$Configuration = 'Debug',
    [string]$Platform = 'x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Accept "A,B" as a single argument too, which is how a workflow env var arrives.
$classes = @($Shard1Classes | ForEach-Object { $_ -split '[,\s]+' } | Where-Object { $_ } | Sort-Object -Unique)
if ($classes.Count -eq 0) { throw '-Shard1Classes names no classes.' }
foreach ($class in $classes) {
    if ($class -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { throw "'$class' is not a plain class name." }
}

$include = ($classes | ForEach-Object { "FullyQualifiedName~.$_." }) -join '|'
$exclude = ($classes | ForEach-Object { "FullyQualifiedName!~.$_." }) -join '&'

function Get-DiscoveredCount([string]$Filter) {
    $listArgs = @('test', $Project, '-c', $Configuration, "-p:Platform=$Platform", '--no-build', '--list-tests', '--no-ansi', '--no-progress')
    if ($Filter) { $listArgs += @('--filter', $Filter) }
    # Belt and braces for a CI terminal: colour codes are stripped and carriage-return progress
    # rewrites are split into their own lines, because an anchored match against a coloured or
    # overwritten line finds nothing (measured: the first CI run of this script did exactly that).
    $output = @(& dotnet @listArgs 2>&1 | ForEach-Object { "$_" -replace '\x1b\[[0-9;?]*[A-Za-z]', '' -split "`r" } |
        ForEach-Object { $_.Trim() })
    $exit = $LASTEXITCODE
    # MTP exits 8 when a filter matches nothing; that is a count of zero, not a broken listing.
    if ($exit -ne 0 -and $exit -ne 8) {
        throw "Test discovery failed (exit $exit) for filter '$Filter':`n$($output -join "`n")"
    }
    $summary = @($output | Select-String -Pattern '^Discovered (\d+) tests?\.$')
    if ($summary.Count -eq 1) { return [int]$summary[0].Matches[0].Groups[1].Value }

    # No run-level summary: fall back to the per-assembly lines, one per distinct assembly.
    $perAssembly = @{}
    foreach ($match in @($output | Select-String -Pattern '^Discovered (\d+) tests? in assembly - (.+)$')) {
        $perAssembly[$match.Matches[0].Groups[2].Value] = [int]$match.Matches[0].Groups[1].Value
    }
    if ($perAssembly.Count -eq 0) {
        throw "Found no discovery summary for filter '$Filter':`n$($output -join "`n")"
    }
    return [int](($perAssembly.Values | Measure-Object -Sum).Sum)
}

$all = Get-DiscoveredCount ''
$counts = @((Get-DiscoveredCount $include), (Get-DiscoveredCount $exclude))
if ($all -le 0 -or $counts[0] -le 0 -or $counts[1] -le 0 -or ($counts[0] + $counts[1]) -ne $all) {
    throw "E2E shard discovery is empty or incomplete: all=$all, shard 1=$($counts[0]), shard 2=$($counts[1])."
}

if ($Shard -eq 1) {
    $stale = @($classes | Where-Object { (Get-DiscoveredCount "FullyQualifiedName~.$_.") -eq 0 })
    if ($stale.Count -gt 0) {
        throw "Shard 1 names class(es) that match no tests: $($stale -join ', '). Rename or remove them; a stale name moves its tests into shard 2 without saying so."
    }
}

$filter = if ($Shard -eq 1) { $include } else { $exclude }
$expected = $counts[$Shard - 1]
Write-Host "E2E shard $Shard/2 selects $expected of $all tests (shard 1=$($counts[0]), shard 2=$($counts[1]))."
"filter=$filter"
"expected=$expected"
if ($env:GITHUB_OUTPUT) {
    "filter=$filter" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "expected=$expected" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
