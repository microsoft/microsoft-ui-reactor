# Shared by the CI execution gate and the stress report. Keep legacy diagnostics
# readable while using a stable marker for the more precise cursor-error report.
function Test-InputGuardMessage {
    param([AllowNull()][AllowEmptyString()][string]$Message)
    return $Message -match '\[REACTOR_E2E_INPUT_BLOCKED\]|cannot inject synthetic input|workstation is|locked desktop|disconnected session'
}

function Get-InputGuardBlockedTests {
    param([Parameter(Mandatory)][xml]$Results)
    $nodes = $Results.SelectNodes("//*[local-name()='UnitTestResult' and (@outcome='Inconclusive' or @outcome='NotExecuted' or @outcome='Skipped')]")
    foreach ($node in $nodes) {
        $messageNode = $node.SelectSingleNode("*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
        $message = if ($messageNode) { $messageNode.InnerText } else { '' }
        if (Test-InputGuardMessage $message) {
            $node.GetAttribute('testName')
        }
    }
}
