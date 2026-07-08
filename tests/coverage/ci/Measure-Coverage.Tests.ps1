<#
.SYNOPSIS
    Dependency-free unit tests for the testable parts of Measure-Coverage.ps1 (the
    merged-coverage orchestrator): the Invoke-Checked exit-code guard, the
    cobertura-report reading path, and the JSON contract with the poster.

.DESCRIPTION
    Measure-Coverage.ps1 isn't dot-sourceable (it has a param block + a main run
    flow that shells out to `dotnet` / `dotnet-coverage`), so Invoke-Checked is
    extracted via the PowerShell AST and defined in isolation. The heavy build +
    instrument + collect path is validated by the live Coverage run, not here; this
    covers the pure guard + the numbers contract so a regression in the JSON shape
    (which the trusted poster reads) is caught headless on the cheap Linux runner in
    .github/workflows/coverage-lib-tests.yml. Exits non-zero on any failure.

    Run locally:  pwsh tests/coverage/ci/Measure-Coverage.Tests.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'CoverageLib.ps1')

$script:Pass = 0
$script:Fail = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()
function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -eq $Actual) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    expected: [$Expected]`n    actual:   [$Actual]") }
}
function Assert-Null {
    param($Actual, [string]$Message)
    if ($null -eq $Actual) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    expected: <null>`n    actual:   [$Actual]") }
}
function Assert-True {
    param([bool]$Condition, [string]$Message)
    if ($Condition) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add($Message) }
}

# --- Parse-check + extract Invoke-Checked from the orchestrator via AST. ---
$scriptPath = Join-Path $PSScriptRoot 'Measure-Coverage.ps1'
$src = Get-Content $scriptPath -Raw
$parseTokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($src, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors -and $parseErrors.Count -gt 0) {
    Write-Host "Measure-Coverage.ps1 has $($parseErrors.Count) parse error(s):" -ForegroundColor Red
    $parseErrors | ForEach-Object { Write-Host "  line $($_.Extent.StartLineNumber): $($_.Message)" -ForegroundColor Red }
    exit 1
}
function Get-Func([string]$name) {
    $f = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true) |
        Select-Object -First 1
    if (-not $f) { throw "$name not found in Measure-Coverage.ps1" }
    $f.Extent.Text
}
Invoke-Expression (Get-Func 'Invoke-Checked')

# --- Invoke-Checked: throws on a non-zero exit code, silent on zero. ---
$threw = $false
try { Invoke-Checked -What 'ok step' -Action { $global:LASTEXITCODE = 0 } }
catch { $threw = $true }
Assert-True (-not $threw) 'Invoke-Checked does not throw on exit 0'

$threw = $false
$msg = ''
try { Invoke-Checked -What 'boom step' -Action { $global:LASTEXITCODE = 5 } }
catch { $threw = $true; $msg = $_.Exception.Message }
Assert-True $threw 'Invoke-Checked throws on a non-zero exit code'
Assert-True ($msg -like '*boom step*') 'Invoke-Checked error names the failing step'
Assert-True ($msg -like '*exit 5*')    'Invoke-Checked error includes the exit code'
$global:LASTEXITCODE = 0

# --- Get-CoberturaRates: file-reading path used at the end of the orchestrator. ---
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("cov-measure-tests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
try {
    $mergedPath = Join-Path $tmp 'merged.cobertura.xml'
    Set-Content -LiteralPath $mergedPath -Encoding UTF8 -Value @'
<coverage line-rate="0.9012" branch-rate="1">
  <packages><package><classes><class><lines>
    <line number="1" hits="1" condition-coverage="100% (2/2)" />
    <line number="2" hits="1" condition-coverage="50% (1/2)" />
  </lines></class></classes></package></packages>
</coverage>
'@
    $rates = Get-CoberturaRates -Path $mergedPath
    Assert-Equal 90.12 $rates.Line            'reads line-rate from a merged report file'
    Assert-Equal 75    $rates.Branch          'aggregates branch rate from a merged report file'
    Assert-Equal 3     $rates.BranchesCovered 'branch covered summed from file (2+1)'
    Assert-Equal 4     $rates.BranchesTotal   'branch total summed from file (2+2)'

    # Missing report -> all-null metrics (degrades to a failed/unavailable render).
    $missing = Get-CoberturaRates -Path (Join-Path $tmp 'does-not-exist.xml')
    Assert-Null $missing.Line   'missing report file -> null line'
    Assert-Null $missing.Branch 'missing report file -> null branch'

    # --- JSON contract: what Measure-Coverage writes, the poster must read back. ---
    # Mirrors the [ordered] payload the orchestrator emits, round-tripped through the
    # poster's security-boundary projection.
    $payload = [ordered]@{
        line            = $rates.Line
        branch          = $rates.Branch
        branchesCovered = $rates.BranchesCovered
        branchesTotal   = $rates.BranchesTotal
        outcome         = 'success'
    }
    $roundTrip = $payload | ConvertTo-Json -Depth 3 | ConvertFrom-Json
    $metrics = ConvertTo-SafeCoverageMetrics $roundTrip
    Assert-Equal 90.12 $metrics.Line            'poster reads line from the emitted JSON'
    Assert-Equal 75    $metrics.Branch          'poster reads branch from the emitted JSON'
    Assert-Equal 3     $metrics.BranchesCovered 'poster reads covered from the emitted JSON'
    Assert-Equal 4     $metrics.BranchesTotal   'poster reads total from the emitted JSON'
    Assert-Equal 'success' $roundTrip.outcome   'outcome survives the round trip'
    Assert-True (Test-CoverageMetricsPresent $metrics) 'round-tripped metrics are present'

    # A no-branch tree emits null branch fields that must survive as null.
    $noBranch = [ordered]@{ line = 42.0; branch = $null; branchesCovered = $null; branchesTotal = $null; outcome = 'success' }
    $nbMetrics = ConvertTo-SafeCoverageMetrics ($noBranch | ConvertTo-Json -Depth 3 | ConvertFrom-Json)
    Assert-Equal 42 $nbMetrics.Line   'null-branch payload keeps line'
    Assert-Null  $nbMetrics.Branch    'null-branch payload keeps branch null'
}
finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "Measure-Coverage tests: $script:Pass passed, $script:Fail failed."
if ($script:Fail -gt 0) {
    Write-Host ''
    Write-Host 'Failures:' -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}
exit 0
