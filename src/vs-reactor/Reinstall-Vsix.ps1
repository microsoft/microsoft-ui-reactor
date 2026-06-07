[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$NoRestore,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$VsInstanceId
)

$ErrorActionPreference = 'Stop'

# 1. Build the VSIX unless caller asks us to skip it.
if (-not $SkipBuild) {
    $buildArgs = @('-Configuration', $Configuration)
    if ($NoRestore) { $buildArgs += '-NoRestore' }
    & (Join-Path $PSScriptRoot 'Build-Vsix.ps1') @buildArgs
    if ($LASTEXITCODE -ne 0) { Write-Error "Build-Vsix.ps1 failed."; exit $LASTEXITCODE }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$vsix = Join-Path $repoRoot ("src\vs-reactor\Reactor.VsExtension\bin\$Configuration\Reactor.VsExtension.vsix")
if (-not (Test-Path -LiteralPath $vsix)) {
    Write-Error "VSIX not found at $vsix"
    exit 1
}

# 2. Discover the target VS instance via vswhere.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    Write-Error "vswhere.exe not found. Install Visual Studio 2022 or 2026 with the 'Visual Studio extension development' workload."
    exit 1
}

$instances = & $vswhere -all -prerelease -format json | ConvertFrom-Json
if (-not $instances) {
    Write-Error "No Visual Studio instances found."
    exit 1
}

if ($VsInstanceId) {
    $target = $instances | Where-Object { $_.instanceId -eq $VsInstanceId }
    if (-not $target) {
        Write-Error "Visual Studio instance '$VsInstanceId' not found. Available: $($instances.instanceId -join ', ')"
        exit 1
    }
} else {
    # Default to the highest version (VS 2026 wins over VS 2022).
    $target = $instances | Sort-Object installationVersion -Descending | Select-Object -First 1
}

$instanceHash = $target.instanceId
$instanceVersion = $target.installationVersion
$majorMinor = ($instanceVersion -split '\.')[0..1] -join '.'
$dataDirLocal = "$env:LOCALAPPDATA\Microsoft\VisualStudio\${majorMinor}_${instanceHash}"
$extRoot = Join-Path $dataDirLocal 'Extensions'

Write-Host "Target VS: $($target.displayName) ($instanceHash, $instanceVersion)"
Write-Host "Per-user extension root: $extRoot"

# 3. Make sure VS is closed (a running devenv locks the extension DLL).
$vsRunning = Get-Process devenv -ErrorAction SilentlyContinue
if ($vsRunning) {
    Write-Error "Visual Studio is running (PIDs: $($vsRunning.Id -join ', ')). Close it and re-run."
    exit 1
}

# 4. Remove any previous Reactor.VsExtension folders (BOTH per-user + per-machine).
$packageId = 'Microsoft.UI.Reactor.VsExtension.d369d334-c8d0-4443-b837-99a961e08b0f'
function Remove-ReactorExtensionsUnder([string]$root) {
    if (-not (Test-Path -LiteralPath $root)) { return }
    Get-ChildItem $root -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $manifest = Join-Path $_.FullName 'extension.vsixmanifest'
        if (Test-Path $manifest) {
            try {
                [xml]$m = Get-Content $manifest -ErrorAction Stop
                if ($m.PackageManifest.Metadata.Identity.Id -eq $packageId) {
                    Write-Host "Removing existing install: $($_.FullName)"
                    Remove-Item -Recurse -Force $_.FullName
                }
            } catch { }
        }
    }
}

Remove-ReactorExtensionsUnder $extRoot

# Machine-wide install path under VS 2026 (created when /admin is used).
$installationPath = $target.installationPath
$machineExtRoot = Join-Path $installationPath 'Common7\IDE\Extensions'
Remove-ReactorExtensionsUnder $machineExtRoot

# 5. Force a re-scan on next launch.
$marker = Join-Path $extRoot 'extensions.configurationchanged'
if (-not (Test-Path -LiteralPath (Split-Path $marker -Parent))) {
    New-Item -ItemType Directory -Path (Split-Path $marker -Parent) -Force | Out-Null
}
[System.IO.File]::WriteAllText($marker, [DateTime]::UtcNow.ToString('o'))
Write-Host "Touched: $marker"

# 6. Install fresh. Drop /admin so VS picks the per-user path; use /quiet to skip UI.
$installer = Join-Path $installationPath 'Common7\IDE\VSIXInstaller.exe'
if (-not (Test-Path -LiteralPath $installer)) {
    Write-Error "VSIXInstaller not found at $installer."
    exit 1
}

Write-Host "Installing: $vsix"
$installArgs = @($vsix, '/quiet', "/instanceIds:$instanceHash")
& $installer @installArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "VSIXInstaller exited with code $LASTEXITCODE. Check %TEMP%\dd_VSIXInstaller_*.log."
    exit $LASTEXITCODE
}

# 7. Verify exactly one Reactor extension is now installed.
Start-Sleep -Seconds 2
$installedFolders = @()
foreach ($root in @($extRoot, $machineExtRoot)) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    $installedFolders += Get-ChildItem $root -Directory -ErrorAction SilentlyContinue | Where-Object {
        $manifest = Join-Path $_.FullName 'extension.vsixmanifest'
        if (-not (Test-Path $manifest)) { return $false }
        try { [xml]$m = Get-Content $manifest -ErrorAction Stop } catch { return $false }
        $m.PackageManifest.Metadata.Identity.Id -eq $packageId
    }
}

Write-Host ""
Write-Host "=== Result ==="
if ($installedFolders.Count -eq 0) {
    Write-Error "Install completed (exit 0) but no Reactor.VsExtension folder is visible yet. Check %TEMP%\dd_VSIXInstaller_*.log."
    exit 1
} elseif ($installedFolders.Count -gt 1) {
    Write-Warning "More than one Reactor.VsExtension install detected. VS may silently disable all of them. Folders:"
    $installedFolders | ForEach-Object { Write-Warning "  $($_.FullName)" }
    exit 1
} else {
    $folder = $installedFolders[0]
    [xml]$m = Get-Content (Join-Path $folder.FullName 'extension.vsixmanifest')
    Write-Host ("Installed v{0} at {1}" -f $m.PackageManifest.Metadata.Identity.Version, $folder.FullName)
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  1. Launch Visual Studio: devenv /RootSuffix Exp  (or just devenv)"
    Write-Host "  2. View -> Other Windows -> Reactor Preview"
    Write-Host "  3. If menu still missing: devenv /updateconfiguration (forces pkgdef re-merge)"
}
