Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'InputGuardClassification.ps1')

$script:Assertions = 0
function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -cne $Actual) { throw "$Message`nExpected: [$Expected]`nActual: [$Actual]" }
    $script:Assertions++
}

# Read the producer's marker, not a second copy that could drift with the consumer.
$producer = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Infrastructure\SessionInteractivityGuard.cs') -Raw
$markerMatch = [regex]::Match($producer, 'InputBlockedMarker\s*=\s*"([^"]+)"')
if (-not $markerMatch.Success) { throw 'InputBlockedMarker was not found in the readiness guard.' }
$marker = $markerMatch.Groups[1].Value

# Test the stress report's actual function without running its artifact scan or
# writing a GitHub step summary.
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'StressReport.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw "StressReport.ps1 has parse errors: $errors" }
$prerequisite = @($ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
    $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
    $node.Left.VariablePath.UserPath -eq 'prereqMsgRegex'
}, $true))
$classifier = @($ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
    $node.Name -eq 'Get-InconclusiveCategory'
}, $true))
Assert-Equal 1 $prerequisite.Count 'Stress prerequisite classifier must be discoverable.'
Assert-Equal 1 $classifier.Count 'Stress category function must be discoverable.'
Invoke-Expression $prerequisite[0].Extent.Text
Invoke-Expression $classifier[0].Extent.Text

foreach ($errorCode in 0, 5, 87) {
    $message = "Assert.Inconclusive. $marker Cannot perform 'drag': GetCursorPos returned false; nativeError=$errorCode."
    Assert-Equal $false ($message -match 'cannot inject synthetic input|workstation is|locked desktop|disconnected session') 'Positive control: old prose matcher misses the precise diagnostic.'
    Assert-Equal $true (Test-InputGuardMessage $message) "Recognize cursor error $errorCode."
    Assert-Equal 'Environmental/guard' (Get-InconclusiveCategory $message) "Stress report must retain cursor error $errorCode as a guard blocker."
    foreach ($outcome in 'Inconclusive', 'NotExecuted', 'Skipped') {
        [xml]$report = @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>
<UnitTestResult testName="blocked-$errorCode" outcome="$outcome"><Output><ErrorInfo><Message>$message</Message></ErrorInfo></Output></UnitTestResult>
<UnitTestResult testName="passed-control" outcome="Passed"><Output><ErrorInfo><Message>$message</Message></ErrorInfo></Output></UnitTestResult>
<UnitTestResult testName="prerequisite" outcome="NotExecuted"><Output><ErrorInfo><Message>missing executable for published host</Message></ErrorInfo></Output></UnitTestResult>
</Results></TestRun>
"@
        Assert-Equal "blocked-$errorCode" ((Get-InputGuardBlockedTests $report) -join '|') "CI must reject $outcome input blockers, not passed/prerequisite controls."
    }
}

foreach ($legacy in 'cannot inject synthetic input', 'workstation is Locked', 'locked desktop', 'disconnected session') {
    Assert-Equal $true (Test-InputGuardMessage $legacy) 'Retain existing guard diagnostics.'
}
Assert-Equal $false (Test-InputGuardMessage 'missing executable for published host') 'Prerequisite absence is not an input-guard message.'
Assert-Equal 'Prerequisite' (Get-InconclusiveCategory 'missing executable for published host') 'Stress prerequisite category is preserved.'
Assert-Equal 'Other' (Get-InconclusiveCategory 'Unrelated skip') 'Unrelated skips retain their category.'
Assert-Equal $false (Test-InputGuardMessage '') 'An empty message is not a guard failure.'

Write-Host "Input guard classification tests: $script:Assertions passed."