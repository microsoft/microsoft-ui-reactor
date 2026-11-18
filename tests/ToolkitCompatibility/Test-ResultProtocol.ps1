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
Write-Output 'PASS positive control and 8 corrupt-evidence cases'
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
