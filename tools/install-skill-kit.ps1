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

# mur is framework-dependent — needs the .NET 9 desktop runtime. Detect early
# and give a useful error rather than letting the consumer hit an opaque
# "framework not found" at first invocation.
$dotnet = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw ".NET 9 runtime is required but `dotnet.exe` is not on PATH. Install with: winget install Microsoft.DotNet.Runtime.9"
}
$has9 = (& $dotnet.Source --list-runtimes) | Where-Object { $_ -match '^Microsoft\.NETCore\.App 9\.' }
if (-not $has9) {
    Write-Warning "No .NET 9 runtime found. mur will fail to start until you install it:"
    Write-Warning "  winget install Microsoft.DotNet.Runtime.9"
    Write-Warning "Continuing with kit install anyway."
}

Write-Host "Installing Reactor skill kit to: $Path"
if (Test-Path $Path) {
    Write-Host "  Removing existing install"
    Remove-Item -Recurse -Force $Path
}
New-Item -ItemType Directory -Force -Path $Path | Out-Null
Copy-Item -Recurse -Force "$source\*" $Path

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
