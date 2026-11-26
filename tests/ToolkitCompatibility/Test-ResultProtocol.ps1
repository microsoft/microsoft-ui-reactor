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
    $validInventory = @('environment/identity-runtime-location', 'SettingsCard/mount')
    $validInventory | Set-Content (Join-Path $lane 'inventory.stdout.txt')
    $validInventory | Set-Content (Join-Path $lane 'inventory.validated.txt')
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

    # A lane can fail after valid results, for example when owned-package cleanup fails.
    $summaryRows[1].Status = 'passed'
    $summaryRows | ForEach-Object { $_ | ConvertTo-Json -Compress } |
        Set-Content (Join-Path $lane 'SettingsCard.results.jsonl')
    $summaryTap | Set-Content (Join-Path $lane 'SettingsCard.stdout.txt')
    @{ control = 'SettingsCard'; status = 'passed'; detail = 'complete'; exitCode = 0
       execution = @{ timedOut = $false; captureComplete = $true } } |
        ConvertTo-Json -Depth 3 | Set-Content (Join-Path $lane 'controls.json')
    $cleanupError = 'Cleanup failed for exact owned package Rct.Wct.fixture: still registered'
    @{ lane = 'jit-unpackaged'; status = 'failed'; detail = "1 controls, 0 failed, 0 blocked. $cleanupError" } |
        ConvertTo-Json | Set-Content (Join-Path $directory 'matrix.json')
    & (Join-Path $PSScriptRoot 'Summarize-Matrix.ps1') -EvidenceDirectory $directory | Out-Null
    $summary = @(Get-Content (Join-Path $directory 'scenario-matrix.json') -Raw | ConvertFrom-Json)
    $report = Get-Content (Join-Path $directory 'summary.md') -Raw
    if (@($summary | Where-Object status -eq 'passed').Count -ne 2 -or
        @($summary | Where-Object { $_.laneStatus -ne 'failed' -or -not $_.laneDetail.Contains($cleanupError) }).Count -or
        -not $report.Contains("jit-unpackaged: failed.") -or -not $report.Contains($cleanupError)) {
        $summaryFailures.Add('Summary hid a failed lane outcome behind valid scenario results.')
    } else { Write-Output 'PASS summary lane-cleanup-failure' }

    # A failed inventory leaves partial raw output in another lane; it must not plan the summary.
    $failedLane = Join-Path $directory 'aot-unpackaged'
    New-Item $failedLane -ItemType Directory | Out-Null
    'environment/identity-runtime-location' | Set-Content (Join-Path $failedLane 'inventory.stdout.txt')
    try {
        & (Join-Path $PSScriptRoot 'Summarize-Matrix.ps1') -EvidenceDirectory $directory | Out-Null
        $summary = @(Get-Content (Join-Path $directory 'scenario-matrix.json') -Raw | ConvertFrom-Json)
        if (@($summary | Where-Object status -eq 'passed').Count -ne 2) { throw 'Expected both valid scenario rows.' }
        Write-Output 'PASS summary failed-inventory-beside-valid-lane'
    } catch { $summaryFailures.Add("Partial inventory from a failed lane planned the summary: $_") }

    # An MSIX archive request is written before every installation, so it is not an approval blocker.
    @{
        revision = 'synthetic-protocol-fixture'
        controls = @('SettingsCard')
        parameters = @{ Runtime = @('jit'); Deployment = @('unpackaged', 'msix') }
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $directory 'run.json')
    ConvertTo-Json -InputObject @(
        @{ lane = 'jit-unpackaged'; status = 'passed'; detail = 'Synthetic fixture' },
        @{ lane = 'jit-msix'; status = 'failed'; detail = 'Inventory process failed.' }) |
        Set-Content (Join-Path $directory 'matrix.json')
    $msixLane = Join-Path $directory 'jit-msix'
    New-Item $msixLane -ItemType Directory | Out-Null
    '{"archive":"synthetic.msix"}' | Set-Content (Join-Path $msixLane 'installation-request.json')
    foreach ($blockerCase in @(@{ Name = 'installed-msix-inventory-failure'; Blocker = $false; Kind = 'lane-failure'; Status = 'failed' },
                               @{ Name = 'approval-blocked-msix'; Blocker = $true; Kind = 'approval'; Status = 'blocked' })) {
        if ($blockerCase.Blocker) {
            @{ reasonKind = 'approval'; detail = 'Synthetic approval blocker' } | ConvertTo-Json |
                Set-Content (Join-Path $msixLane 'blocker.json')
        }
        & (Join-Path $PSScriptRoot 'Summarize-Matrix.ps1') -EvidenceDirectory $directory | Out-Null
        $msixRows = @(Get-Content (Join-Path $directory 'scenario-matrix.json') -Raw | ConvertFrom-Json |
            Where-Object lane -EQ 'jit-msix')
        if ($msixRows.Count -eq 0 -or
            @($msixRows | Where-Object { $_.reasonKind -ne $blockerCase.Kind -or $_.status -ne $blockerCase.Status }).Count) {
            $summaryFailures.Add("MSIX lane rows were not $($blockerCase.Status)/$($blockerCase.Kind): $($blockerCase.Name)")
        } else { Write-Output "PASS summary $($blockerCase.Name)" }
    }

    $catalog = @(
        @{ Control = 'DockPanel'; Label = 'attached-dock-last-child-fill-geometry'; Proof = 'update' },
        @{ Control = 'UniformGrid'; Label = 'first-column-keyed-reorder'; Proof = 'update' },
        @{ Control = 'SwitchPresenter'; Label = 'actual-matching-default-case-collection'; Proof = 'sample-native-case-selection' },
        @{ Control = 'TabbedCommandBar'; Label = 'actual-tabs-command-invocation'; Proof = 'sample-native-tabs-commands' },
        @{ Control = 'SettingsCard'; Label = 'rich-slots-clear-restore-click'; Proof = 'update' },
        @{ Control = 'Segmented'; Label = 'uia-value-patterns'; Proof = 'update' }
    )
    $coverageLanes = @('jit-unpackaged', 'aot-unpackaged')
    @{
        revision = 'synthetic-coverage-fixture'
        controls = @($catalog.Control)
        parameters = @{ Runtime = @('jit', 'aot'); Deployment = @('unpackaged') }
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $directory 'run.json')
    $coverageLanes | ForEach-Object { @{ lane = $_; status = 'blocked'; detail = 'Synthetic coverage fixture' } } |
        ConvertTo-Json | Set-Content (Join-Path $directory 'matrix.json')
    $coverageCases = @(
        @{ Name = 'complete'; Covered = $true; Valid = $true },
        @{ Name = 'missing-planned-counterpart'; Covered = $false; Valid = $true },
        @{ Name = 'missing-result'; Covered = $false; Valid = $false },
        @{ Name = 'failed-counterpart'; Covered = $false; Valid = $true },
        @{ Name = 'blocked-counterpart-other-lane-passes'; Covered = $false; Valid = $true },
        @{ Name = 'inapplicable-counterpart'; Covered = $false; Valid = $true },
        @{ Name = 'duplicate-counterpart'; Covered = $false; Valid = $false },
        @{ Name = 'duplicate-outcome'; Covered = $false; Valid = $false },
        @{ Name = 'wrong-control'; Covered = $false; Valid = $false },
        @{ Name = 'wrong-scenario-case'; Covered = $false; Valid = $false },
        @{ Name = 'incomplete-capture'; Covered = $false; Valid = $false },
        @{ Name = 'truncated'; Covered = $false; Valid = $false },
        @{ Name = 'approval-label'; Covered = $false; Valid = $true },
        @{ Name = 'failed-label'; Covered = $false; Valid = $true },
        @{ Name = 'unrelated-assertion-failed'; Covered = $true; Valid = $true }
    )
    foreach ($case in $coverageCases) {
        $coverageInventory = @('environment/identity-runtime-location') + @(
            foreach ($entry in $catalog) {
                "$($entry.Control)/$($entry.Label)"
                "$($entry.Control)/$(if ($case.Name -eq 'missing-planned-counterpart' -and $entry.Control -eq 'DockPanel') { 'mount' } else { $entry.Proof })"
                "$($entry.Control)/os-text-scaling"
            }
        )
        $coverageInventory | Set-Content (Join-Path $lane 'inventory.validated.txt')
        foreach ($laneName in $coverageLanes) {
            $destination = Join-Path $directory $laneName
            $outcomes = @(
                foreach ($entry in $catalog) {
                    $control = $entry.Control
                    $proofName = if ($case.Name -eq 'missing-planned-counterpart' -and $control -eq 'DockPanel') { 'mount' } else { $entry.Proof }
                    $processKeys = @($coverageInventory | Where-Object { $_ -eq 'environment/identity-runtime-location' -or $_.StartsWith("$control/") })
                    $resultRows = @(
                        [pscustomobject]@{ Control = 'environment'; Scenario = 'identity-runtime-location'; Status = 'passed'; Detail = 'Synthetic environment' },
                        [pscustomobject]@{ Control = $control; Scenario = $entry.Label; Status = 'blocked'; Detail = 'Original named blocker'; ReasonKind = 'not-implemented' },
                        [pscustomobject]@{ Control = $control; Scenario = $proofName; Status = 'passed'; Detail = 'Finite synthetic assertion' },
                        [pscustomobject]@{ Control = $control; Scenario = 'os-text-scaling'; Status = 'blocked'; Detail = 'Original OS blocker'; ReasonKind = 'not-implemented' }
                    )
                    $subject = $control -eq 'DockPanel' -and $laneName -eq 'jit-unpackaged'
                    if ($subject) {
                        switch ($case.Name) {
                            'missing-result' { $resultRows = @($resultRows[0], $resultRows[1], $resultRows[3]) }
                            'failed-counterpart' { $resultRows[2].Status = 'failed' }
                            'blocked-counterpart-other-lane-passes' { $resultRows[2].Status = 'blocked' }
                            'inapplicable-counterpart' { $resultRows[2].Status = 'inapplicable' }
                            'duplicate-counterpart' { $resultRows += $resultRows[2] }
                            'wrong-control' { $resultRows[2].Control = 'UniformGrid' }
                            'wrong-scenario-case' { $resultRows[2].Scenario = 'Update' }
                            'approval-label' { $resultRows[1].ReasonKind = 'approval' }
                            'failed-label' { $resultRows[1].Status = 'failed' }
                            'unrelated-assertion-failed' { $resultRows[3].Status = 'failed' }
                        }
                    }
                    $failureCount = @($resultRows | Where-Object Status -EQ 'failed').Count
                    $blockCount = @($resultRows | Where-Object Status -EQ 'blocked').Count
                    $tapLines = @('TAP version 13') + @(
                        for ($i = 0; $i -lt $resultRows.Count; $i++) {
                            $result = $resultRows[$i]
                            $prefix = if ($result.Status -in @('passed', 'inapplicable')) { 'ok' } else { 'not ok' }
                            $suffix = if ($result.Status -eq 'inapplicable') { ' # SKIP native contract' } else { '' }
                            "$prefix $($i + 1) - $($result.Control)/$($result.Scenario)$suffix"
                        }
                    ) + @("1..$($processKeys.Count)", "# Total failures: $failureCount", "# Total blocked: $blockCount")
                    if (-not ($subject -and $case.Name -eq 'truncated')) { $tapLines += '# COMPLETE' }
                    $tapLines | Set-Content (Join-Path $destination "$control.stdout.txt")
                    $resultRows | ForEach-Object { $_ | ConvertTo-Json -Compress } |
                        Set-Content (Join-Path $destination "$control.results.jsonl")
                    $outcome = @{
                        control = $control; status = if ($failureCount) { 'failed' } else { 'blocked' }
                        detail = 'Synthetic process'; exitCode = if ($failureCount) { 1 } else { 2 }
                        execution = @{ timedOut = $false; captureComplete = -not ($subject -and $case.Name -eq 'incomplete-capture') }
                    }
                    $outcome
                    if ($subject -and $case.Name -eq 'duplicate-outcome') { $outcome }
                }
            )
            $outcomes | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $destination 'controls.json')
        }
        $inputs = @(Get-ChildItem $directory -File -Recurse |
            Where-Object Name -NotIn @('summary.md', 'scenario-matrix.json'))
        $before = @($inputs | Get-FileHash | ForEach-Object Hash) -join ','
        & (Join-Path $PSScriptRoot 'Summarize-Matrix.ps1') -EvidenceDirectory $directory | Out-Null
        if ((@($inputs | Get-FileHash | ForEach-Object Hash) -join ',') -cne $before) {
            throw "Coverage summary mutated raw evidence or inventory: $($case.Name)"
        }
        $summary = @(Get-Content (Join-Path $directory 'scenario-matrix.json') -Raw | ConvertFrom-Json)
        $report = Get-Content (Join-Path $directory 'summary.md') -Raw
        if ($summary.Count -ne 48) { throw "Coverage summary changed the planned row count: $($case.Name)" }
        foreach ($row in $summary) {
            $entry = @($catalog | Where-Object { "$($_.Control)/$($_.Label)" -ceq $row.scenario })
            $expectedProof = $null
            if ($entry.Count -eq 1 -and $entry[0].Control -notin @('SettingsCard', 'Segmented')) {
                $shouldCover = if ($entry[0].Control -ne 'DockPanel') { $true }
                    elseif ($case.Name -eq 'missing-planned-counterpart') { $false }
                    elseif ($row.lane -eq 'jit-unpackaged') { $case.Covered }
                    else { $true }
                if ($shouldCover) { $expectedProof = "$($entry[0].Control)/$($entry[0].Proof)" }
            }
            if ($row.coveredByScenario -cne $expectedProof) {
                throw "Incorrect coverage link: $($case.Name), $($row.lane)/$($row.scenario), expected '$expectedProof', actual '$($row.coveredByScenario)'"
            }
            if ($expectedProof -and (-not $row.validatedCompleteProcess -or $row.status -cne 'blocked' -or
                $row.reasonKind -cne 'not-implemented' -or $row.detail -cne 'Original named blocker' -or
                -not $report.Contains("| $($row.lane) | $($row.scenario) | $expectedProof |"))) {
                throw "Coverage annotation changed the raw blocker or is missing from the report: $($case.Name)"
            }
        }
        $subjectRow = @($summary | Where-Object {
            $_.lane -eq 'jit-unpackaged' -and $_.scenario -ceq 'DockPanel/attached-dock-last-child-fill-geometry'
        })[0]
        if ($subjectRow.validatedCompleteProcess -ne $case.Valid) {
            throw "Unexpected process validation: $($case.Name)"
        }
        if ($case.Name -eq 'complete') {
            foreach ($entry in $catalog) {
                if (-not $report.Contains("| $($entry.Control) | P2 F0 B2 N0 | P2 F0 B2 N0 |")) {
                    throw "Coverage annotation changed raw totals for $($entry.Control)."
                }
            }
            if (-not $report.Contains('- not-implemented: 24 planned checks.')) {
                throw 'Coverage annotation changed raw block classification totals.'
            }
        }
        Write-Output "PASS coverage $($case.Name)"
    }
    foreach ($duplicate in @('control', 'lane')) {
        $run = Get-Content (Join-Path $directory 'run.json') -Raw | ConvertFrom-Json
        $run.controls = @($catalog.Control)
        $run.parameters.Runtime = @('jit', 'aot')
        if ($duplicate -eq 'control') { $run.controls += 'DockPanel' }
        else { $run.parameters.Runtime += 'jit' }
        $run | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $directory 'run.json')
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'Summarize-Matrix.ps1') -EvidenceDirectory $directory | Out-Null }
        catch {
            if ($_.Exception.Message -cne 'Duplicate planned controls or lanes make scenario references ambiguous.') { throw }
            $rejected = $true
        }
        if (-not $rejected) { throw "Coverage summary accepted ambiguous planned $duplicate references." }
        Write-Output "PASS coverage duplicate-planned-$duplicate"
    }
} finally {
    Remove-Item -LiteralPath $directory -Recurse -Force
}
if ($summaryFailures.Count) { throw ($summaryFailures -join "`n") }
