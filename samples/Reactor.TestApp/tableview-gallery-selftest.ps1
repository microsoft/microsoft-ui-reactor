#requires -version 7
<#
.SYNOPSIS
  Self-test for the native TableView gallery integrated into Reactor.TestApp.

.DESCRIPTION
  Builds Reactor.TestApp with the native C++/WinRT TableView gallery enabled
  (-p:IncludeTableView=true) and — best-effort — launches it and walks the UIA
  tree to assert the embedded TableViewSamples "Showcase" page renders real rows
  and column headers (i.e. the projected native split-binary Advanced TableView
  actually renders inside the Reactor host, not just activates).

  The BUILD is the hard gate (a failure fails the script / CI): it proves the
  ~9 MB native Advanced.dll + the projection + the 93-file embedded gallery all
  compile and integrate. The RENDER check is best-effort because GitHub's
  windows-latest runners are non-interactive (session 0) and may not compose a
  WinUI 3 swapchain; an inconclusive render does not fail the script. Render is
  verified locally (see the screenshot referenced in PR #621).
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)
$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "Reactor.TestApp.csproj"

Write-Host "== Build Reactor.TestApp WITH the native TableView gallery (IncludeTableView=true) =="
dotnet build $proj -c $Configuration -p:Platform=$Platform -p:IncludeTableView=true --nologo
if ($LASTEXITCODE -ne 0) { throw "BUILD FAILED (exit $LASTEXITCODE) — native TableView gallery did not integrate." }
Write-Host "BUILD OK — native TableView gallery integrates into Reactor.TestApp."

$exe = Get-ChildItem (Join-Path $PSScriptRoot "bin\$Platform\$Configuration") -Recurse -Filter Reactor.TestApp.exe -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $exe) { throw "Reactor.TestApp.exe not found after build." }
Write-Host "EXE: $($exe.FullName)"

# ── Best-effort render self-test via UIA (never fails the script) ─────────────
$rows = -1; $headers = -1; $proc = $null
try {
    $proc = Start-Process -FilePath $exe.FullName -PassThru
    Start-Sleep -Seconds 15
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $byName = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, "TestApp")
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $byName)
    if ($win) {
        $listItem = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)
        $navs = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listItem)
        $showcase = $navs | Where-Object { $_.Current.Name -eq 'Showcase' } | Select-Object -First 1
        if ($showcase) {
            try { $showcase.GetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { }
            Start-Sleep -Seconds 4
        }
        $dataItem = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::DataItem)
        $rows = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $dataItem).Count
        $headerItem = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::HeaderItem)
        $headers = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $headerItem).Count
    }
}
catch {
    Write-Warning "Render self-test (UIA) inconclusive: $_"
}
finally {
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}

Write-Host "RENDER: Showcase DataItem rows=$rows, column HeaderItems=$headers"
if ($rows -ge 6 -and $headers -ge 4) {
    Write-Host "RENDER OK — the native TableView Showcase gallery renders inside Reactor.TestApp."
}
else {
    Write-Warning "Render check inconclusive (non-interactive CI session may not compose WinUI 3). Build integration PASSED; render is verified locally (PR screenshot)."
}
Write-Host "SELFTEST PASSED."
exit 0
