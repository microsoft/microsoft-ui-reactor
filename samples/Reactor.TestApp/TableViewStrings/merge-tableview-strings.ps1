# Merges the native TableView control's localized string resources into the app's
# compiled PRI under the "Microsoft.UI.Xaml/Resources/" subtree, where the native
# C++/WinRT control (ResourceAccessor::GetLocalizedStringResource) looks them up.
#
# The control's default-style lookup is satisfied by the Reactor.Controls.TableView
# library (embedded Styles closure), but its localized UIA / live-region strings are
# resolved at runtime via the app's MainResourceMap. The public WinAppSDK 2.0.1 PRI
# does not carry the Tabular control's SR_TableView* keys, so row realization
# (TableViewRow.cpp) throws without this merge.
#
# Runs as a post-build step. Idempotent.
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [Parameter(Mandatory = $true)][string]$ScriptDir,
    [string]$AppPriName = "Reactor.TestApp.pri"
)
$ErrorActionPreference = "Stop"

function Find-MakePri {
    $roots = @("${env:ProgramFiles(x86)}\Windows Kits\10\bin", "${env:ProgramFiles}\Windows Kits\10\bin")
    foreach ($r in $roots) {
        if (Test-Path $r) {
            $c = Get-ChildItem $r -Recurse -Filter makepri.exe -ErrorAction SilentlyContinue |
                 Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
            if ($c) { return $c.FullName }
        }
    }
    return $null
}

$makepri = Find-MakePri
if (-not $makepri) { Write-Host "[tableview-strings] makepri.exe not found; skipping string merge (TableView rows may not render)."; exit 0 }

$appPri = Join-Path $OutDir $AppPriName
if (-not (Test-Path $appPri)) { Write-Host "[tableview-strings] $appPri not found; skipping."; exit 0 }

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("tvstrings_" + [System.IO.Path]::GetRandomFileName())
$stage = Join-Path $work "Microsoft.UI.Xaml"
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item (Join-Path $ScriptDir "Resources.resw") (Join-Path $stage "Resources.resw") -Force

# priconfig with initialPath so resw keys land at "Microsoft.UI.Xaml/Resources/<key>"
$cfg = Join-Path $work "priconfig.xml"
& $makepri createconfig /cf $cfg /dq en-US /o | Out-Null
(Get-Content $cfg -Raw) -replace '<indexer-config type="resw"([^>]*)initialPath=""', '<indexer-config type="resw"$1initialPath="Microsoft.UI.Xaml"' | Set-Content $cfg -Encoding UTF8

$strPri = Join-Path $work "advstrings.pri"
& $makepri new /pr $work /cf $cfg /of $strPri /IndexName Reactor.TestApp /o | Out-Null

# merge the strings pri into the app pri (PRI indexer unions resource maps)
$merge = Join-Path $work "merge"
New-Item -ItemType Directory -Force -Path $merge | Out-Null
Copy-Item $appPri (Join-Path $merge $AppPriName) -Force
Copy-Item $strPri (Join-Path $merge "advstrings.pri") -Force
$mcfg = Join-Path $work "mergecfg.xml"
& $makepri createconfig /cf $mcfg /dq en-US /o | Out-Null
$merged = Join-Path $work "merged.pri"
& $makepri new /pr $merge /cf $mcfg /of $merged /IndexName Reactor.TestApp /o | Out-Null

Copy-Item $merged $appPri -Force
Copy-Item $merged (Join-Path $OutDir "resources.pri") -Force
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "[tableview-strings] Merged native TableView strings into $AppPriName."
