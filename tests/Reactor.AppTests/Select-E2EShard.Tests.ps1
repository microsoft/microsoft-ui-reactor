<#
.SYNOPSIS
    Dependency-free tests for the pure parts of Select-E2EShard.ps1: the shard-1 class list, the
    complementary filter pair, and the parser that reads MTP's discovery summary.

.DESCRIPTION
    Select-E2EShard.ps1 shells out to `dotnet test --list-tests`, so its pure functions are
    extracted via the PowerShell AST and tested in isolation, the same way
    tests/coverage/ci/Measure-Coverage.Tests.ps1 tests its orchestrator. The discovery parser is
    the part worth pinning: the script's first CI run failed because MTP coloured its summary line,
    and an anchored match cannot see through colour codes. The E2E job runs this before selecting
    its shard. Exits non-zero on any failure.

    Run locally:  pwsh tests/Reactor.AppTests/Select-E2EShard.Tests.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Pass = 0
$script:Fail = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()
function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($null -ne $Actual -and $Expected -ceq $Actual) { $script:Pass++ }
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
function Assert-Throws {
    param([scriptblock]$Action, [string]$Like, [string]$Message)
    $threw = $false; $text = ''
    try { & $Action } catch { $threw = $true; $text = $_.Exception.Message }
    Assert-True ($threw -and $text -like $Like) "$Message (threw=$threw, message=[$text])"
}

# --- Parse-check the script and extract its pure functions via the AST. ---
$scriptPath = Join-Path $PSScriptRoot 'Select-E2EShard.ps1'
$parseTokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors -and $parseErrors.Count -gt 0) {
    Write-Host "Select-E2EShard.ps1 has $($parseErrors.Count) parse error(s):" -ForegroundColor Red
    $parseErrors | ForEach-Object { Write-Host "  line $($_.Extent.StartLineNumber): $($_.Message)" -ForegroundColor Red }
    exit 1
}
foreach ($name in 'ConvertTo-ShardClassList', 'Get-ShardFilters', 'Read-DiscoveredCount') {
    $function = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true) |
        Select-Object -First 1
    if (-not $function) { throw "$name not found in Select-E2EShard.ps1" }
    Invoke-Expression $function.Extent.Text
}

$esc = [char]27

# --- Read-DiscoveredCount: the summary MTP prints for `dotnet test --list-tests`. ---
# A plain transcript, shaped like the real one: a per-assembly line, the test names, then the
# run-level summary.
$plain = @(
    'Discovering tests from C:\r\Reactor.AppTests.dll (net10.0|x64)',
    '',
    'Discovered 211 tests in assembly - C:\r\Reactor.AppTests.dll (net10.0|x64)',
    '  A11y_2_1_1_TabOrderFollowsTabIndex',
    '',
    'Discovered 211 tests.')
Assert-Equal 211 (Read-DiscoveredCount $plain) 'reads the run-level summary from a plain transcript'

# Colour codes around the summary: the case that failed the first CI run. The positive control
# shows the anchored pattern really cannot match the raw coloured line, so stripping is what makes
# the next assertion pass rather than something incidental.
$coloured = "$esc[32;1mDiscovered 49 tests.$esc[0m"
Assert-True (-not ($coloured -match '^Discovered (\d+) tests?\.$')) 'positive control: the raw coloured line defeats the anchored match'
Assert-Equal 49 (Read-DiscoveredCount @("$esc[36mDiscovered 49 tests in assembly - X.dll$esc[0m", $coloured)) 'strips colour codes before matching'

# A progress line rewritten in place with a carriage return, then the summary on the same line.
Assert-Equal 41 (Read-DiscoveredCount @("[+0/x0/?0] Reactor.AppTests.dll (net10.0|x64)`rDiscovered 41 tests.")) 'splits carriage-return rewrites into their own lines'

# No run-level summary: one count per distinct assembly, so a repeated progress line is not
# counted twice.
$perAssembly = @(
    'Discovered 3 tests in assembly - A.dll',
    'Discovered 3 tests in assembly - A.dll',
    'Discovered 4 tests in assembly - B.dll')
Assert-Equal 7 (Read-DiscoveredCount $perAssembly) 'sums distinct per-assembly counts when there is no run-level summary'

Assert-Equal 0 (Read-DiscoveredCount @('Discovered 0 tests in assembly - X.dll', 'Discovered 0 tests.')) 'reads zero (the exit-8 case) as zero'
Assert-Equal 1 (Read-DiscoveredCount @('Discovered 1 test.')) 'accepts the singular'
Assert-Null (Read-DiscoveredCount @('Discovering tests from X.dll', 'Test discovery aborted.')) 'returns null when there is no summary at all'
Assert-Null (Read-DiscoveredCount @()) 'returns null for no output'

# --- Get-ShardFilters: shard 2 must be the exact complement of shard 1. ---
$filters = Get-ShardFilters @('AccessibilityTests', 'DataGridTests')
Assert-Equal 'FullyQualifiedName~.AccessibilityTests.|FullyQualifiedName~.DataGridTests.' $filters.Include 'shard 1 selects any listed class'
Assert-Equal 'FullyQualifiedName!~.AccessibilityTests.&FullyQualifiedName!~.DataGridTests.' $filters.Exclude 'shard 2 selects everything else'

# --- ConvertTo-ShardClassList: the list arrives as one comma-separated env var in CI. ---
Assert-Equal 'AccessibilityTests|DataGridTests' ((ConvertTo-ShardClassList @('DataGridTests, AccessibilityTests', 'AccessibilityTests')) -join '|') 'splits, trims, dedupes and sorts'
Assert-Equal 'A|B' ((ConvertTo-ShardClassList @("A`n  B")) -join '|') 'accepts whitespace, including folded YAML newlines'
Assert-Throws { ConvertTo-ShardClassList @(' , ') } '*names no classes*' 'rejects an empty list'
Assert-Throws { ConvertTo-ShardClassList @('Tests.AccessibilityTests') } "*is not a plain class name*" 'rejects a namespace-qualified name'
Assert-Throws { ConvertTo-ShardClassList @('A|B') } "*is not a plain class name*" 'rejects filter syntax'

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host "Select-E2EShard tests: $script:Pass passed, $script:Fail failed."
if ($script:Fail -gt 0) {
    Write-Host ''
    Write-Host 'Failures:' -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}
exit 0
