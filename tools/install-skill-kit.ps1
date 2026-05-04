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
