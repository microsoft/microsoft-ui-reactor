function Test-CompatibilityResults {
    param(
        [Parameter(Mandatory)][string]$Tap,
        [Parameter(Mandatory)][object[]]$Results,
        [Parameter(Mandatory)][string[]]$ExpectedKeys,
        [Parameter(Mandatory)][Nullable[int]]$ExitCode
    )
    $plans = [regex]::Matches($Tap, '(?m)^1\.\.(\d+)\r?$')
    if ($plans.Count -ne 1 -or [int]$plans[0].Groups[1].Value -ne $ExpectedKeys.Count -or
        $Results.Count -ne $ExpectedKeys.Count -or
        $Tap -notmatch '(?m)^# COMPLETE\r?$') {
        throw 'Missing, duplicate or incomplete TAP plan/results/completion trailer.'
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($result in $Results) {
        $key = "$($result.Control)/$($result.Scenario)"
        if (-not $seen.Add($key) -or $key -cnotin $ExpectedKeys) {
            throw "Unexpected or duplicate result: $key"
        }
        if ($result.Status -cnotin @('passed', 'failed', 'blocked', 'inapplicable') -or
            [string]::IsNullOrWhiteSpace($result.Detail)) {
            throw "Missing or invalid status/detail: $key"
        }
    }
    $tapRows = [regex]::Matches($Tap, '(?m)^(ok|not ok) (\d+) - ([^\r\n]+)\r?$')
    if ($tapRows.Count -ne $Results.Count) { throw 'TAP row count differs from result count.' }
    for ($i = 0; $i -lt $Results.Count; $i++) {
        $row = $tapRows[$i]
        $result = $Results[$i]
        $verdict = if ($result.Status -in @('passed', 'inapplicable')) { 'ok' } else { 'not ok' }
        $expectedKey = "$($result.Control)/$($result.Scenario)"
        if ($result.Status -eq 'inapplicable') { $expectedKey += ' # SKIP native contract' }
        if ([int]$row.Groups[2].Value -ne $i + 1 -or $row.Groups[1].Value -ne $verdict -or
            $row.Groups[3].Value -cne $expectedKey) {
            throw 'TAP and structured results disagree.'
        }
    }
    $failures = @($Results | Where-Object Status -eq 'failed').Count
    $blocks = @($Results | Where-Object Status -eq 'blocked').Count
    if ($Tap -notmatch "(?m)^# Total failures: $failures\r?$" -or
        $Tap -notmatch "(?m)^# Total blocked: $blocks\r?$") {
        throw 'Missing or inconsistent totals.'
    }
    $expectedExit = if ($failures) { 1 } elseif ($blocks) { 2 } else { 0 }
    if ($ExitCode -ne $expectedExit) { throw "Exit $ExitCode disagrees with results ($expectedExit)." }
    [pscustomobject]@{
        Status = if ($failures) { 'failed' } elseif ($blocks) { 'blocked' } else { 'passed' }
        Detail = "$($Results.Count) complete results, $failures failed, $blocks blocked."
    }
}
