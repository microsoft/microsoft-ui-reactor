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
    $buildArgs = @{ Configuration = $Configuration }
    if ($NoRestore) { $buildArgs.NoRestore = $true }
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
# IMPORTANT: VS's per-user data directory always uses <Major>.0 (NOT major.minor).
# `installationVersion` is e.g. "18.6.11806.211", but the data dir is `18.0_<hash>`.
# Using "18.6" silently targets a non-existent folder, so cleanup misses old installs
# and the `extensions.configurationchanged` trigger never reaches VS — which is why
# new menu entries fail to render after a re-install.
$major = ($instanceVersion -split '\.')[0]
$majorMinor = "$major.0"
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
# VSIXInstaller commits its directory entries asynchronously; poll up to 30 seconds.
function Find-ReactorInstalls {
    param([string[]]$Roots, [string]$Id)
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($root in $Roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($dir in (Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)) {
            $manifest = Join-Path $dir.FullName 'extension.vsixmanifest'
            if (-not (Test-Path -LiteralPath $manifest)) { continue }
            try {
                [xml]$m = Get-Content -LiteralPath $manifest -ErrorAction Stop
            } catch {
                continue
            }
            if ($m.PackageManifest.Metadata.Identity.Id -eq $Id) {
                $out.Add($dir) | Out-Null
            }
        }
    }
    return ,$out.ToArray()
}

$installedFolders = @()
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    Start-Sleep -Milliseconds 500
    $installedFolders = Find-ReactorInstalls -Roots @($extRoot, $machineExtRoot) -Id $packageId
    if (@($installedFolders).Count -ge 1) { break }
}

Write-Host ""
Write-Host "=== Result ==="
$installedFoldersArray = @($installedFolders)
$skipUpdateConfig = $false
if ($installedFoldersArray.Count -eq 0) {
    Write-Warning "Install reported success (VSIXInstaller exit 0) but no Reactor.VsExtension folder appeared within 30s. Proceeding with /updateconfiguration anyway. If VS still doesn't show the menus, inspect %TEMP%\dd_VSIXInstaller_*.log."
} elseif ($installedFoldersArray.Count -gt 1) {
    Write-Warning "More than one Reactor.VsExtension install detected. VS may silently disable all of them. Folders:"
    $installedFoldersArray | ForEach-Object { Write-Warning "  $($_.FullName)" }
    Write-Warning "Skipping /updateconfiguration; clean up duplicates and re-run."
    $skipUpdateConfig = $true
} else {
    $folder = $installedFoldersArray[0]
    [xml]$m = Get-Content (Join-Path $folder.FullName 'extension.vsixmanifest')
    Write-Host ("Installed v{0} at {1}" -f $m.PackageManifest.Metadata.Identity.Version, $folder.FullName)
}

if (-not $skipUpdateConfig) {
    Write-Host ""
    Write-Host "Running 'devenv /updateconfiguration' to force the menu/pkgdef merge synchronously."
    Write-Host "Without this step, the first VS launch on VS 2026 sometimes silently skips the merge"
    Write-Host "and our menu entry never appears. This step blocks until the merge completes (~10-30s)."
    $devenv = Join-Path $installationPath 'Common7\IDE\devenv.exe'
    if (Test-Path -LiteralPath $devenv) {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $devenv
        $psi.Arguments = '/updateconfiguration'
        $psi.UseShellExecute = $false
        $proc = [System.Diagnostics.Process]::Start($psi)
        if (-not $proc.WaitForExit(120000)) { $proc.Kill(); Write-Warning "/updateconfiguration timed out after 2 min." }
        Write-Host ("  /updateconfiguration exit code: {0} ({1:0.0}s)" -f $proc.ExitCode, ($proc.ExitTime - $proc.StartTime).TotalSeconds)
    } else {
        Write-Warning "devenv.exe not found at $devenv. Run /updateconfiguration manually before launching VS."
    }
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  1. Launch Visual Studio (no special flags needed)."
    Write-Host "  2. View -> Other Windows -> Reactor Preview."
}
