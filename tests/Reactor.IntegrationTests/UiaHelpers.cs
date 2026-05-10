using System.Diagnostics;
using System.Text;
using Xunit.Sdk;

namespace Microsoft.UI.Reactor.IntegrationTests
{
    internal static class UiaHelpers
    {
        internal static UIAFindResult FindUIALement(Process launchedProcess, string uiaName, out string? details)
        {
            details = null;

            try
            {
                var probe = RunHelpers.RunProcess(
                    "powershell.exe",
                    BuildFindUIAElement(launchedProcess.Id, uiaName),
                    Environment.SystemDirectory,
                    environmentVariables: new Dictionary<string, string?>(),
                    timeoutMs: 15_000,
                    throwOnFailure: false);

                details = string.IsNullOrWhiteSpace(probe.Stdout)
                    ? probe.Stderr.Trim()
                    : probe.Stdout.Trim();

                if (probe.ExitCode == 0)
                {
                    return UIAFindResult.Found;
                }

                if (probe.ExitCode == 2)
                {
                    return UIAFindResult.NotFound;
                }

                return UIAFindResult.NotReady;
            }
            catch (XunitException ex)
            {
                details = $"UI Automation probe failed: {ex.Message}";
                return UIAFindResult.NotReady;
            }
            catch (InvalidOperationException ex)
            {
                details = $"UI Automation probe failed: {ex.Message}";
                return UIAFindResult.NotReady;
            }
        }

        internal static string BuildFindUIAElement(int processId, string uiaName)
        {
            var script = $$"""
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
$processId = {{processId}}
$root = [System.Windows.Automation.AutomationElement]::RootElement
$condition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
    $processId)
$elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
$names = New-Object 'System.Collections.Generic.List[string]'

for ($i = 0; $i -lt $elements.Count; $i++) {
    $name = [string]$elements.Item($i).GetCurrentPropertyValue([System.Windows.Automation.AutomationElement]::NameProperty)
    if (-not [string]::IsNullOrWhiteSpace($name) -and -not $names.Contains($name)) {
        [void]$names.Add($name)
        if ($names.Count -ge 20) {
            break
        }
    }
}

if ($names.Contains('{{uiaName}}')) {
    Write-Output 'Found template NameInput automation name.'
    exit 0
}

$renderError = $names | Where-Object { $_ -like '*Render error*' } | Select-Object -First 1
if ($renderError) {
    Write-Output $renderError
    exit 2
}

if ($names.Count -eq 0) {
    Write-Output 'No UI Automation names are visible for the launched process yet.'
    exit 1
}

Write-Output ('Observed names: ' + [string]::Join(', ', $names))
exit 1
""";

            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            return $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}";
        }

        internal enum UIAFindResult
        {
            NotReady,
            Found,
            NotFound,
        }
    }
}
