#!/usr/bin/env pwsh
# Run this AFTER closing the MSIX installer dialog and confirming VS is fully closed.
# It launches VS once with /log, then immediately checks ActivityLog for our package.

$logPath = "$env:APPDATA\Microsoft\VisualStudio\18.0_b54421b5\ActivityLog.xml"
$devenv = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\IDE\devenv.exe'

Write-Host "Make sure VS is fully closed. Press Enter to launch VS with /log..."
[void](Read-Host)

Write-Host "Launching: $devenv /log"
Start-Process -FilePath $devenv -ArgumentList '/log' -WindowStyle Normal
Write-Host ""
Write-Host "Wait until VS finishes startup, then look for View -> Other Windows -> Reactor Preview."
Write-Host "After you confirm whether the menu is there or not, close VS, then press Enter here to read the ActivityLog..."
[void](Read-Host)

[xml]$x = Get-Content $logPath
$entries = $x.activity.entry | Where-Object {
    ($_.source -match 'Reactor|VsExtension|VSIX|Pkgdef|Menu|Command') -or
    ($_.description -match 'Reactor|VsExtension|36b8ec71|pzoad|d369d334|Menus|ctmenu') -or
    ($_.type -in @('Error','Warning'))
}
Write-Host "Total log entries: $($x.activity.entry.Count); matching: $($entries.Count)"
$entries | ForEach-Object {
    $d = $_.description; if ($d.Length -gt 300) { $d = $d.Substring(0,300) + '...' }
    Write-Host ("[$($_.time)][$($_.type)] $($_.source)")
    Write-Host ("    $d")
}
