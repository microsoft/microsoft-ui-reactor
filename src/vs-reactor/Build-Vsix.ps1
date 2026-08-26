<#
.SYNOPSIS
    Build the Reactor Preview VSIX with desktop MSBuild.

.PARAMETER NuGetConfig
    Restore through an explicit NuGet.Config instead of the repo's public one.

.PARAMETER NuGetSource
    Restore through an explicit NuGet source instead of the repo's public
    config. Ignored when -NuGetConfig is supplied; see .NOTES for why that
    precedence is resolved here rather than by callers.

.PARAMETER MSBuildPath
    Use a specific MSBuild.exe instead of asking vswhere for the latest one.
    An escape hatch for non-standard Visual Studio layouts.

.NOTES
    Feed selection matters here in a way it does not elsewhere in the repo.
    Not every network can reach nuget.org, and this is the one restore that
    bootstrap.ps1 does not run itself: it shells out to Reinstall-Vsix.ps1, and
    the restore environment bootstrap sets up around its own commands is scoped
    to those commands, so this build never sees it. bootstrap.ps1 therefore
    passes the feed it already resolved as an argument.

    Explicit arguments rather than inherited RestoreSources/RestoreConfigFile
    environment variables, because this script also detects a mirror on its own
    for direct runs -- and an MSBuild /p: switch outranks an environment
    variable, so self-detection would silently override an explicit
    -NuGetConfig handed down from bootstrap. Resolve-ReactorNuGetFeedOverride
    gives explicit arguments precedence over detection, which keeps the two
    entry points consistent.
#>
[CmdletBinding()]
param(
    [switch]$NoRestore,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$Version,
    [string]$NuGetConfig,
    [string]$NuGetSource,
    [string]$MSBuildPath
)

$ErrorActionPreference = 'Stop'

if ($MSBuildPath) {
    # -PathType Leaf: a directory satisfies a bare Test-Path and would then be
    # invoked as a command, failing later with something far less obvious.
    if (-not (Test-Path -LiteralPath $MSBuildPath -PathType Leaf)) {
        Write-Error "-MSBuildPath '$MSBuildPath' is not an existing file."
        exit 1
    }
    $msbuild = $MSBuildPath
} else {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        Write-Error "vswhere.exe was not found at '$vswhere'. Install Visual Studio 2022 or later with the 'Visual Studio extension development' workload."
        exit 1
    }

    $msbuildCandidates = & $vswhere -find 'MSBuild\**\Bin\MSBuild.exe' -latest -prerelease -products *
    if ($LASTEXITCODE -ne 0 -or -not $msbuildCandidates) {
        Write-Error "Desktop MSBuild was not found. Install Visual Studio 2022 or later with the 'Visual Studio extension development' workload."
        exit 1
    }

    $msbuild = @($msbuildCandidates)[0]
    if (-not (Test-Path -LiteralPath $msbuild)) {
        Write-Error "vswhere returned '$msbuild', but that file does not exist."
        exit 1
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

$feedResolver = Join-Path $repoRoot 'tools\BootstrapFeedResolver.ps1'
if (-not (Test-Path -LiteralPath $feedResolver -PathType Leaf)) {
    Write-Error "Missing feed resolver: $feedResolver"
    exit 1
}
. $feedResolver

$project = Join-Path $repoRoot 'src\vs-reactor\Reactor.VsExtension\Reactor.VsExtension.csproj'
$vsix = Join-Path $repoRoot ("src\vs-reactor\Reactor.VsExtension\bin\$Configuration\Reactor.VsExtension.vsix")
$manifest = Join-Path $repoRoot 'src\vs-reactor\Reactor.VsExtension\source.extension.vsixmanifest'

function Convert-ToVsixVersion([string]$InputVersion) {
    if ([string]::IsNullOrWhiteSpace($InputVersion)) { return $null }
    if ($InputVersion -match '^(\d+)\.(\d+)\.(\d+)\.(\d+)$') { return $InputVersion }
    if ($InputVersion -notmatch '^(\d+)\.(\d+)\.(\d+)(?:[-+]([^+]+))?') {
        throw "Version '$InputVersion' cannot be converted to a VSIX-safe numeric version."
    }

    $major = [int]$Matches[1]
    $minor = [int]$Matches[2]
    $patch = [int]$Matches[3]
    $suffix = $Matches[4]
    $revision = 0
    if ($suffix) {
        $numbers = [regex]::Matches($suffix, '\d+') | ForEach-Object { [int]$_.Value }
        if ($numbers) {
            $revision = ($numbers | Select-Object -Last 1)
        }
    }
    return "$major.$minor.$patch.$revision"
}

function Set-VsixManifestVersion([string]$ManifestPath, [string]$VsixVersion) {
    [xml]$xml = Get-Content -LiteralPath $ManifestPath
    $xml.PackageManifest.Metadata.Identity.Version = $VsixVersion
    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Indent = $true
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $writer = [System.Xml.XmlWriter]::Create($ManifestPath, $settings)
    try {
        $xml.Save($writer)
    } finally {
        $writer.Dispose()
    }
}

$vsixVersion = Convert-ToVsixVersion $Version
$originalManifestText = $null
if ($vsixVersion) {
    $originalManifestText = Get-Content -LiteralPath $manifest -Raw
    Set-VsixManifestVersion $manifest $vsixVersion
    Write-Host "Stamped VSIX manifest version: $vsixVersion (from '$Version')"
}

$arguments = @(
    $project,
    "/p:Configuration=$Configuration",
    '/p:Platform=AnyCPU',
    '/p:DotnetVsixBuild=false',
    '/v:minimal'
)

# See .NOTES: without this the restore falls through to the repo's public
# nuget.config, which not every network can reach.
$feedSelection = Resolve-ReactorNuGetFeedOverride -NuGetConfig $NuGetConfig -NuGetSource $NuGetSource
if ($feedSelection -and $feedSelection.ConfigPath) {
    Write-Host "NuGet config from $($feedSelection.Origin): $($feedSelection.ConfigPath)"
    $arguments += "/p:RestoreConfigFile=$($feedSelection.ConfigPath)"
} elseif ($feedSelection -and $feedSelection.Source) {
    Write-Host "NuGet source from $($feedSelection.Origin): $($feedSelection.Source)"
    $arguments += "/p:RestoreSources=$($feedSelection.Source)"
}

if (-not $NoRestore) {
    $arguments += '/restore'
}

try {
    Write-Host "Using MSBuild: $msbuild"
    & $msbuild @arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Error "VSIX build failed. If the log shows NU1301 against nuget.org, pass -NuGetSource <url> or -NuGetConfig <path> to restore through a reachable feed; otherwise ensure Visual Studio 2022 or later has the 'Visual Studio extension development' workload and VSSDK targets installed."
        exit $LASTEXITCODE
    }

    if (-not (Test-Path -LiteralPath $vsix)) {
        Write-Error "Expected VSIX was not produced at '$vsix'. Ensure Visual Studio 2022 or later has the 'Visual Studio extension development' workload and VSSDK targets installed."
        exit 1
    }
} finally {
    if ($null -ne $originalManifestText) {
        [System.IO.File]::WriteAllText($manifest, $originalManifestText, [System.Text.UTF8Encoding]::new($false))
    }
}

Write-Host "VSIX produced: $vsix"
