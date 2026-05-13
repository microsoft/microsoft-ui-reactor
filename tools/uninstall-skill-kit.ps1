# Uninstall the Reactor skill kit.
#
# Removes the installed kit directory and cleans the user PATH entries that
# install-skill-kit.ps1 added.  This is the inverse of install-skill-kit.ps1
# and addresses https://github.com/microsoft/microsoft-ui-reactor/issues/238.
#
# Usage:
#   .\uninstall-skill-kit.ps1                 # default: ~/.claude/skills/reactor
#   .\uninstall-skill-kit.ps1 -Path C:\foo    # custom location

[CmdletBinding()]
param(
    [string] $Path = (Join-Path $env:USERPROFILE '.claude\skills\reactor')
)

$ErrorActionPreference = 'Stop'

$absPath = [System.IO.Path]::GetFullPath($Path)

# Safety: refuse to delete drive roots, profile root, system dirs, etc.
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
        throw "Refusing to delete '$absPath' — that's a system or user-data root."
    }
}
if ($absPath.Length -lt 12) {
    throw "Refusing to delete '$absPath' — path is suspiciously short."
}

# 1. Remove PATH entries for both architectures so we don't leave stale
#    entries behind (e.g. installed on x64, uninstalling from ARM64).
$userPathRaw = [Environment]::GetEnvironmentVariable('Path', 'User')
$entries = ($userPathRaw -split ';') | Where-Object { $_ -ne '' }
$archDirs = @(
    [System.IO.Path]::GetFullPath((Join-Path $absPath 'bin\x64')),
    [System.IO.Path]::GetFullPath((Join-Path $absPath 'bin\arm64'))
)

$cleaned = @()
$removedEntries = @()
foreach ($e in $entries) {
    $eNorm = [System.IO.Path]::GetFullPath($e).TrimEnd('\')
    $match = $false
    foreach ($a in $archDirs) {
        if ($eNorm -ieq $a.TrimEnd('\')) {
            $match = $true
            $removedEntries += $e
            break
        }
    }
    if (-not $match) { $cleaned += $e }
}

if ($removedEntries.Count -gt 0) {
    $newPath = $cleaned -join ';'
    [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
    foreach ($r in $removedEntries) {
        Write-Host "  Removed from user PATH: $r"
    }
} else {
    Write-Host "  No skill-kit PATH entries found."
}

# 2. Delete the installed directory.
if (Test-Path $absPath) {
    Remove-Item -Recurse -Force $absPath
    Write-Host "  Removed install directory: $absPath"
} else {
    Write-Host "  Install directory not found: $absPath (already removed?)"
}

Write-Host ""
Write-Host "Done. Reactor skill kit has been uninstalled."
