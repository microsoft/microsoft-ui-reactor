$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ResultProtocol.ps1')
$rows = @(
    [pscustomobject]@{ Control = 'environment'; Scenario = 'identity'; Status = 'passed'; Detail = 'x64 JIT, unpackaged' },
    [pscustomobject]@{ Control = 'SettingsCard'; Scenario = 'mount'; Status = 'passed'; Detail = 'header=initial' }
)
$keys = @('environment/identity', 'SettingsCard/mount')
$tap = "TAP version 13`nok 1 - environment/identity`nok 2 - SettingsCard/mount`n1..2`n# Total failures: 0`n# Total blocked: 0`n# COMPLETE`n"
$positive = Test-CompatibilityResults $tap $rows $keys 0
if ($positive.Status -ne 'passed') { throw 'Positive control failed.' }
$mutations = @(
    @{ Name = 'truncated'; Tap = $tap.Replace('# COMPLETE', ''); Rows = $rows; Exit = 0 },
    @{ Name = 'empty'; Tap = 'TAP version 13'; Rows = @(); Exit = 0 },
    @{ Name = 'missing-result'; Tap = $tap; Rows = @($rows[0]); Exit = 0 },
    @{ Name = 'duplicate'; Tap = $tap; Rows = @($rows[0], $rows[0]); Exit = 0 },
    @{ Name = 'crash-after-results'; Tap = $tap; Rows = $rows; Exit = -1 },
    @{ Name = 'missing-exit'; Tap = $tap; Rows = $rows; Exit = $null },
    @{ Name = 'wrong-plan'; Tap = $tap.Replace('1..2', '1..1'); Rows = $rows; Exit = 0 },
    @{ Name = 'duplicate-plan'; Tap = $tap + "1..2`n"; Rows = $rows; Exit = 0 },
    @{ Name = 'wrong-verdict'; Tap = $tap.Replace('ok 2 -', 'not ok 2 -'); Rows = $rows; Exit = 0 }
)
foreach ($mutation in $mutations) {
    $rejected = $false
    try { Test-CompatibilityResults $mutation.Tap $mutation.Rows $keys $mutation.Exit | Out-Null }
    catch { $rejected = $true }
    if (-not $rejected) { throw "Accepted broken evidence: $($mutation.Name)" }
    Write-Output "PASS rejected $($mutation.Name)"
}
Write-Output "PASS positive control and $($mutations.Count) corrupt-evidence cases"
$native = @([pscustomobject]@{
    Control = 'Panel'; Scenario = 'value-pattern'; Status = 'inapplicable'; Detail = 'Native layout root has no value pattern'
})
$nativeTap = "TAP version 13`nok 1 - Panel/value-pattern # SKIP native contract`n1..1`n# Total failures: 0`n# Total blocked: 0`n# COMPLETE`n"
$nativeResult = Test-CompatibilityResults $nativeTap $native @('Panel/value-pattern') 0
if ($nativeResult.Status -ne 'passed') { throw 'Explicit native-contract skip did not validate.' }
$native[0].Status = 'passed'
$rejected = $false
try { Test-CompatibilityResults $nativeTap $native @('Panel/value-pattern') 0 | Out-Null }
catch { $rejected = $true }
if (-not $rejected) { throw 'Skip directive was accepted as an executed pass.' }
Write-Output 'PASS explicit native-contract skip cannot masquerade as an executed pass'

$directory = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'reactor-toolkit-summary-' + [Guid]::NewGuid().ToString('N'))
$lane = Join-Path $directory 'jit-unpackaged'
New-Item $lane -ItemType Directory | Out-Null
$summaryFailures = [Collections.Generic.List[string]]::new()
try {
    @{
        revision = 'synthetic-protocol-fixture'
        controls = @('SettingsCard')
        parameters = @{ Runtime = @('jit'); Deployment = @('unpackaged') }
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $directory 'run.json')
    @{ lane = 'jit-unpackaged'; status = 'blocked'; detail = 'Synthetic fixture' } |
        ConvertTo-Json | Set-Content (Join-Path $directory 'matrix.json')
    @('environment/identity-runtime-location', 'SettingsCard/mount') |
        Set-Content (Join-Path $lane 'inventory.stdout.txt')
    $summaryRows = @(
        [pscustomobject]@{ Control = 'environment'; Scenario = 'identity-runtime-location'; Status = 'passed'; Detail = 'Synthetic environment' },
        [pscustomobject]@{ Control = 'SettingsCard'; Scenario = 'mount'; Status = 'passed'; Detail = 'Synthetic mount' }
    )
    $summaryTap = $tap.Replace('environment/identity', 'environment/identity-runtime-location')
    $cases = @(
        @{ Name = 'complete-pass'; Status = 'passed'; Exit = 0; Capture = $true; Timeout = $false; ExpectedPasses = 2 },
        @{ Name = 'complete-assertion-failure'; Status = 'failed'; Exit = 1; Capture = $true; Timeout = $false; ExpectedPasses = 1 },
        @{ Name = 'incomplete-capture'; Status = 'failed'; Exit = 0; Capture = $false; Timeout = $false; ExpectedPasses = 0 },
        @{ Name = 'timed-out'; Status = 'failed'; Exit = 0; Capture = $true; Timeout = $true; ExpectedPasses = 0 },
        @{ Name = 'missing-execution'; Status = 'passed'; Exit = 0; Capture = $true; Timeout = $false; ExpectedPasses = 0 },
        @{ Name = 'missing-exit'; Status = 'passed'; Exit = $null; Capture = $true; Timeout = $false; ExpectedPasses = 0 },
        @{ Name = 'recorded-verdict-mismatch'; Status = 'failed'; Exit = 0; Capture = $true; Timeout = $false; ExpectedPasses = 0 }
    )
    foreach ($case in $cases) {
        $summaryRows[1].Status = if ($case.Exit -eq 1) { 'failed' } else { 'passed' }
        $summaryRows | ForEach-Object { $_ | ConvertTo-Json -Compress } |
            Set-Content (Join-Path $lane 'SettingsCard.results.jsonl')
        $caseTap = if ($case.Exit -eq 1) { $summaryTap.Replace('ok 2 -', 'not ok 2 -').Replace('Total failures: 0', 'Total failures: 1') }
            else { $summaryTap }
        $caseTap | Set-Content (Join-Path $lane 'SettingsCard.stdout.txt')
        $outcome = @{
            control = 'SettingsCard'; status = $case.Status; detail = $case.Name; exitCode = $case.Exit
        }
        if ($case.Name -ne 'missing-execution') {
            $outcome.execution = @{ timedOut = $case.Timeout; captureComplete = $case.Capture }
        }
        $outcome | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $lane 'controls.json')
        & (Join-Path $PSScriptRoot 'Summarize-Matrix.ps1') -EvidenceDirectory $directory | Out-Null
        $summary = @(Get-Content (Join-Path $directory 'scenario-matrix.json') -Raw | ConvertFrom-Json)
        $passes = @($summary | Where-Object status -eq 'passed').Count
        if ($passes -ne $case.ExpectedPasses -or
            ($case.ExpectedPasses -eq 0 -and @($summary | Where-Object validatedCompleteProcess).Count -ne 0)) {
            $summaryFailures.Add("Summary accepted incorrect process evidence: $($case.Name)")
        } else { Write-Output "PASS summary $($case.Name)" }
    }
} finally {
    Remove-Item -LiteralPath $directory -Recurse -Force
}
if ($summaryFailures.Count) { throw ($summaryFailures -join "`n") }
