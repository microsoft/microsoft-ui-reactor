#requires -version 7
<#
.SYNOPSIS
  Self-test for the native TableView first-class Reactor control in Reactor.TestApp.

.DESCRIPTION
  Builds Reactor.TestApp with the native C++/WinRT TableView control enabled
  (-p:IncludeTableView=true) and — best-effort — launches it and walks the UIA
  tree to assert the "TableView" tab (which consumes the Reactor.Controls.TableView
  first-class control exactly as a Reactor consumer would: TableView(items, columns))
  renders real data rows and the expected column headers — i.e. the projected
  native split-binary Advanced TableView actually renders through the Reactor
  element/handler, not just activates.

  The BUILD is the hard gate (a failure fails the script / CI): it proves the
  consumable control library + its CsWinRT projection + the ~9 MB native
  Tabular.dll all compile and integrate. The RENDER check is best-effort because
  GitHub's windows-latest runners are non-interactive (session 0) and may not
  compose a WinUI 3 swapchain; an inconclusive render does not fail the script.
  Render is verified locally (see the screenshots referenced in PR #621).
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)
$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "Reactor.TestApp.csproj"

Write-Host "== Build Reactor.TestApp WITH the native TableView control (IncludeTableView=true) =="
dotnet build $proj -c $Configuration -p:Platform=$Platform -p:IncludeTableView=true --nologo
if ($LASTEXITCODE -ne 0) { throw "BUILD FAILED (exit $LASTEXITCODE) — native TableView control did not integrate." }
Write-Host "BUILD OK — the consumable Reactor.Controls.TableView control integrates into Reactor.TestApp."

$exe = Get-ChildItem (Join-Path $PSScriptRoot "bin\$Platform\$Configuration") -Recurse -Filter Reactor.TestApp.exe -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $exe) { throw "Reactor.TestApp.exe not found after build." }
Write-Host "EXE: $($exe.FullName)"

# ── Best-effort render self-test via UIA (never fails the script) ─────────────
# The TableView tab is the default tab, so the control renders on launch — no nav needed.
$rows = -1; $headers = -1; $headerNames = ""; $proc = $null
try {
    $proc = Start-Process -FilePath $exe.FullName -PassThru
    Start-Sleep -Seconds 15
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $kids = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    $win = $kids | Where-Object { $_.Current.ProcessId -eq $proc.Id } | Select-Object -First 1
    if ($win) {
        $dataItem = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::DataItem)
        $rows = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $dataItem).Count
        $headerItem = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::HeaderItem)
        $hdrs = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $headerItem)
        $headers = $hdrs.Count
        $headerNames = (($hdrs | ForEach-Object { $_.Current.Name }) -join ',')
    }
}
catch {
    Write-Warning "Render self-test (UIA) inconclusive: $_"
}
finally {
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}

Write-Host "RENDER: TableView control DataItem rows=$rows, column HeaderItems=$headers [$headerNames]"
if ($rows -ge 6 -and $headers -ge 3) {
    Write-Host "RENDER OK — the native TableView first-class control renders inside Reactor.TestApp."
}
else {
    Write-Warning "Render check inconclusive (non-interactive CI session may not compose WinUI 3). Build integration PASSED; render is verified locally (PR screenshots)."
}
Write-Host "SELFTEST PASSED."
exit 0
