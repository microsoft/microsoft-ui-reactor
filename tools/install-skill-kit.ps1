# Install the Reactor skill kit.
#
# Run this from inside the extracted kit directory. It copies the kit to a
# stable location and prepends the matching bin/<arch> to your user PATH so
# that `mur` resolves from any shell.
#
# Usage:
#   .\install-skill-kit.ps1                 # default: ~/.claude/skills/reactor
#   .\install-skill-kit.ps1 -Path C:\foo    # custom location
#   .\install-skill-kit.ps1 -SkipPath       # don't touch PATH
#
# See docs/specs/022-packaging-and-distribution.md for the rollout plan.

[CmdletBinding()]
param(
    [string] $Path = (Join-Path $env:USERPROFILE '.claude\skills\reactor'),
    [switch] $SkipPath
)

$ErrorActionPreference = 'Stop'

$source = $PSScriptRoot
if (-not (Test-Path (Join-Path $source 'SKILL.md'))) {
    throw "SKILL.md not found next to this script. Run install-skill-kit.ps1 from inside the extracted kit."
}

$arch = switch ($env:PROCESSOR_ARCHITECTURE) {
    'AMD64' { 'x64' }
    'ARM64' { 'arm64' }
    default { throw "Unsupported architecture: $env:PROCESSOR_ARCHITECTURE" }
}

$archBin = Join-Path $source "bin\$arch"
if (-not (Test-Path (Join-Path $archBin 'mur.exe'))) {
    throw "bin\$arch\mur.exe not found in kit. Re-download the matching release."
}

# mur is framework-dependent - needs the .NET 10 desktop runtime. Detect early
# and give a useful error rather than letting the consumer hit an opaque
# "framework not found" at first invocation.
$dotnet = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw ".NET 10 runtime is required but `dotnet.exe` is not on PATH. Install with: winget install Microsoft.DotNet.Runtime.10"
}
$has10 = (& $dotnet.Source --list-runtimes) | Where-Object { $_ -match '^Microsoft\.NETCore\.App 10\.' }
if (-not $has10) {
    Write-Warning "No .NET 10 runtime found. mur will fail to start until you install it:"
    Write-Warning "  winget install Microsoft.DotNet.Runtime.10"
    Write-Warning "Continuing with kit install anyway."
}

# Safety guards - `Remove-Item -Recurse -Force` is destructive enough that a
# typo'd -Path could nuke real data. Refuse anything that would obviously be
# wrong (kit's own dir, a drive root, profile root, system dirs) before we
# touch anything.
$absPath = [System.IO.Path]::GetFullPath($Path)
$absSource = [System.IO.Path]::GetFullPath($source)
if ($absPath -ieq $absSource -or $absSource.StartsWith($absPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to install into '$absPath' - that's the extracted kit itself or a parent of it. Pass a different -Path."
}
$forbidden = @(
    [System.IO.Path]::GetPathRoot($absPath).TrimEnd('\'),
    $env:USERPROFILE,
    $env:SystemRoot,
    "$env:SystemRoot\System32",
    $env:ProgramFiles,
    "${env:ProgramFiles(x86)}",
    "$env:USERPROFILE\Desktop",
    "$env:USERPROFILE\Documents",
    "$env:USERPROFILE\Downloads"
) | Where-Object { $_ }
foreach ($f in $forbidden) {
    if ($absPath -ieq $f.TrimEnd('\')) {
        throw "Refusing to install into '$absPath' - that's a system or user-data root. Pass a more specific -Path (default is ~/.claude/skills/reactor)."
    }
}
if ($absPath.Length -lt 12) {   # e.g. C:\, C:\foo
    throw "Refusing to install into '$absPath' - path is suspiciously short. Pass a more specific -Path."
}

Write-Host "Installing Reactor skill kit to: $absPath"
if (Test-Path $absPath) {
    Write-Host "  Removing existing install"
    Remove-Item -Recurse -Force $absPath
}
New-Item -ItemType Directory -Force -Path $absPath | Out-Null
Copy-Item -Recurse -Force "$source\*" $absPath
$Path = $absPath

if (-not $SkipPath) {
    $targetBin = Join-Path $Path "bin\$arch"
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $entries = ($userPath -split ';') | Where-Object { $_ -ne '' }
    if ($entries -notcontains $targetBin) {
        $newPath = (@($targetBin) + $entries) -join ';'
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
        Write-Host "  Added to user PATH: $targetBin"
        Write-Host "  Open a new shell to pick it up."
    } else {
        Write-Host "  Already on user PATH: $targetBin"
    }
}

Write-Host ""
Write-Host "Done. Verify with: mur --version"
