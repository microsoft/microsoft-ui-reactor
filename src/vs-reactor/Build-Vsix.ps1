[CmdletBinding()]
param(
    [switch]$NoRestore,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
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

Write-Host "VSIX produced: $vsix"
