[CmdletBinding()]
param(
    [switch]$NoRestore,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    Write-Error "vswhere.exe was not found at '$vswhere'. Install Visual Studio 2022 with the 'Visual Studio extension development' workload."
    exit 1
}

$msbuildCandidates = & $vswhere -find 'MSBuild\**\Bin\MSBuild.exe' -latest -prerelease -products *
if ($LASTEXITCODE -ne 0 -or -not $msbuildCandidates) {
    Write-Error "Desktop MSBuild was not found. Install Visual Studio 2022 with the 'Visual Studio extension development' workload."
    exit 1
}

$msbuild = @($msbuildCandidates)[0]
if (-not (Test-Path -LiteralPath $msbuild)) {
    Write-Error "vswhere returned '$msbuild', but that file does not exist."
    exit 1
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
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

if (-not $NoRestore) {
    $arguments += '/restore'
}

try {
    Write-Host "Using MSBuild: $msbuild"
    & $msbuild @arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Error "VSIX build failed. Ensure Visual Studio 2022 has the 'Visual Studio extension development' workload and VSSDK targets installed."
        exit $LASTEXITCODE
    }

    if (-not (Test-Path -LiteralPath $vsix)) {
        Write-Error "Expected VSIX was not produced at '$vsix'. Ensure Visual Studio 2022 has the 'Visual Studio extension development' workload and VSSDK targets installed."
        exit 1
    }
} finally {
    if ($null -ne $originalManifestText) {
        [System.IO.File]::WriteAllText($manifest, $originalManifestText, [System.Text.UTF8Encoding]::new($false))
    }
}

Write-Host "VSIX produced: $vsix"
