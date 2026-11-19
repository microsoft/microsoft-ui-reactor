[CmdletBinding()]
param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'ResultProtocol.ps1')
$directory = (Resolve-Path $EvidenceDirectory).Path
$run = Get-Content (Join-Path $directory 'run.json') -Raw | ConvertFrom-Json
$lanes = @(Get-Content (Join-Path $directory 'matrix.json') -Raw | ConvertFrom-Json)
$runtime = if ($run.parameters.PSObject.Properties['Runtime']) { @($run.parameters.Runtime) } else { @('jit', 'aot') }
$deployment = if ($run.parameters.PSObject.Properties['Deployment']) { @($run.parameters.Deployment) } else { @('unpackaged', 'loose', 'msix') }
$plannedLanes = @($runtime | ForEach-Object { $r = $_; $deployment | ForEach-Object { "$r-$_" } })
$inventoryFiles = @(Get-ChildItem $directory -Filter 'inventory.validated.txt' -File -Recurse | Where-Object Length -GT 0)
if (-not $inventoryFiles) { throw 'No validated scenario inventory exists; cannot invent a per-scenario matrix.' }
$inventory = @(Get-Content $inventoryFiles[0].FullName | Where-Object { $_ })
foreach ($other in $inventoryFiles | Select-Object -Skip 1) {
    if ((@(Get-Content $other.FullName | Where-Object { $_ }) -join "`n") -cne ($inventory -join "`n")) {
        throw "Validated inventories differ: $($inventoryFiles[0].FullName) and $($other.FullName)."
    }
}
if (@($inventory | Sort-Object -Unique).Count -ne $inventory.Count) { throw 'Duplicate planned scenario keys.' }
$rows = [Collections.Generic.List[object]]::new()

foreach ($laneName in $plannedLanes) {
    $laneDirectory = Join-Path $directory $laneName
    $lane = @($lanes | Where-Object lane -EQ $laneName)
    if ($lane.Count -gt 1) { throw "Duplicate lane $laneName" }
    $controlFile = Join-Path $laneDirectory 'controls.json'
    $controlOutcomes = if (Test-Path $controlFile) { @(Get-Content $controlFile -Raw | ConvertFrom-Json) } else { @() }
    foreach ($control in $run.controls) {
        $keys = @($inventory | Where-Object {
            $_ -eq 'environment/identity-runtime-location' -or $_.StartsWith("$control/")
        })
        if ($keys.Count -lt 2) { throw "No planned operations for $control." }
        $outcome = @($controlOutcomes | Where-Object control -CEQ $control)
        $validated = $false
        $failure = ''
        $results = @()
        if ($outcome.Count -eq 1) {
            try {
                $execution = $outcome[0].execution
                if ($null -eq $execution -or
                    $execution.timedOut -isnot [bool] -or $execution.captureComplete -isnot [bool] -or
                    $execution.timedOut -or -not $execution.captureComplete) {
                    throw 'Process completion metadata is missing, timed out or incompletely captured.'
                }
                $results = @(Get-Content (Join-Path $laneDirectory "$control.results.jsonl") |
                    ForEach-Object { $_ | ConvertFrom-Json })
                $tap = Get-Content (Join-Path $laneDirectory "$control.stdout.txt") -Raw
                $verdict = Test-CompatibilityResults $tap $results $keys $outcome[0].exitCode
                if ($verdict.Status -cne $outcome[0].status) {
                    throw 'Recorded process verdict disagrees with revalidated results.'
                }
                $validated = $true
            } catch { $failure = $_.Exception.Message }
        } elseif ($outcome.Count -gt 1) {
            $failure = 'Duplicate control outcomes.'
        }
        foreach ($key in $keys) {
            if ($validated) {
                $result = @($results | Where-Object { "$($_.Control)/$($_.Scenario)" -ceq $key })[0]
                $status = $result.Status
                $detail = $result.Detail
                $kind = if ($result.PSObject.Properties['ReasonKind']) { $result.ReasonKind } else { $null }
                if (-not $kind -and $status -eq 'blocked') {
                    $kind = if ($detail.StartsWith('Not yet exercised')) { 'not-implemented' }
                        elseif ($detail -match 'consent|permission-approved') { 'approval' }
                        elseif ($detail -match 'Pinned Toolkit template') { 'native-contract' }
                        else { 'unclassified' }
                }
            } else {
                $status = if ($failure) { 'failed' } else { 'blocked' }
                $kind = if ($failure) { 'incomplete-evidence' }
                    elseif (Test-Path (Join-Path $laneDirectory 'installation-request.json')) { 'approval' }
                    else { 'not-executed' }
                $detail = if ($failure) { $failure }
                    elseif ($lane.Count) { $lane[0].detail }
                    else { 'Planned lane did not execute.' }
            }
            $rows.Add([pscustomobject]@{
                lane = $laneName
                control = $control
                scenario = $key
                status = $status
                reasonKind = $kind
                detail = $detail
                validatedCompleteProcess = $validated
                laneStatus = if ($lane.Count) { $lane[0].status } else { 'not-executed' }
                laneDetail = if ($lane.Count) { $lane[0].detail } else { 'Planned lane did not execute.' }
            })
        }
    }
}
$rows | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $directory 'scenario-matrix.json')
$text = [Collections.Generic.List[string]]::new()
$text.Add('# Toolkit compatibility evidence')
$text.Add('')
$text.Add("Source revision: ``$($run.revision)``.")
$text.Add('Counts below include each process identity/runtime/location guard. P = passed, F = failed, B = blocked, N = inapplicable.')
$text.Add('Unexecuted lanes expand to blocked rows; incomplete/truncated process output never contributes passed rows.')
$text.Add('')
$text.Add('| Control | ' + ($plannedLanes -join ' | ') + ' |')
$text.Add('|---|' + (($plannedLanes | ForEach-Object { '---' }) -join '|') + '|')
foreach ($control in $run.controls) {
    $cells = foreach ($laneName in $plannedLanes) {
        $items = @($rows | Where-Object { $_.control -ceq $control -and $_.lane -eq $laneName })
        $passed = @($items | Where-Object status -EQ 'passed').Count
        $failed = @($items | Where-Object status -EQ 'failed').Count
        $blocked = @($items | Where-Object status -EQ 'blocked').Count
        $notApplicable = @($items | Where-Object status -EQ 'inapplicable').Count
        "P$passed F$failed B$blocked N$notApplicable"
    }
    $text.Add("| $control | " + ($cells -join ' | ') + ' |')
}
$text.Add('')
$text.Add('## Lane outcomes')
$text.Add('')
$text.Add('Valid scenario rows are kept even when the lane later failed, for example during package cleanup.')
$text.Add('')
foreach ($laneName in $plannedLanes) {
    $lane = @($lanes | Where-Object lane -EQ $laneName)
    if ($lane.Count) {
        $laneDetail = ([string]$lane[0].detail -split '\r?\n' | Where-Object { $_.Trim() }) -join ' '
        $text.Add("- ${laneName}: $($lane[0].status). $laneDetail")
    } else {
        $text.Add("- ${laneName}: not executed.")
    }
}
$text.Add('')
$text.Add('## Block classification')
$text.Add('')
foreach ($group in @($rows | Where-Object status -EQ 'blocked' | Group-Object reasonKind)) {
    $text.Add("- $($group.Name): $($group.Count) planned checks.")
}
$text.Add('')
$text.Add('See `scenario-matrix.json` for every operation and exact reason, and the original per-process TAP/JSONL for observed values. This report does not claim that every Toolkit feature works.')
$text | Set-Content (Join-Path $directory 'summary.md')
Write-Output (Join-Path $directory 'summary.md')
