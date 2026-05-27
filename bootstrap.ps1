#requires -Version 5.1
<#
.SYNOPSIS
    One-command setup for a Microsoft.UI.Reactor source checkout.

.DESCRIPTION
    Builds the `mur` CLI, installs it as a dotnet global tool, packs the
    framework + ProjectTemplates into local-nupkgs/, installs the
    `dotnet new reactorapp` template, and (optionally) drops the Claude
    Code plugin under ~/.claude/plugins/reactor.

    Idempotent — safe to re-run after `git pull` to refresh everything.
    For a less heavyweight refresh (mur stays put), run `mur upgrade`.

.PARAMETER SkipPlugin
    Skip installing the Claude Code plugin under ~/.claude/plugins.

.PARAMETER SkipMurInstall
    Build and pack the CLI but don't run `dotnet tool install/update`.
    Useful for CI or for users who manage tool installs externally.

.PARAMETER Configuration
    Build configuration for the CLI nupkg. Default: Release.

.EXAMPLE
    ./bootstrap.ps1
    Full bootstrap.

.EXAMPLE
    ./bootstrap.ps1 -SkipPlugin
    Skip the Claude plugin step.
#>
[CmdletBinding()]
param(
    [switch]$SkipPlugin,
    [switch]$SkipMurInstall,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
Set-Location $repoRoot

function Write-Step($msg) {
    Write-Host ''
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Write-Ok($msg) {
    Write-Host "    [ok] $msg" -ForegroundColor Green
}

function Fail($msg) {
    Write-Host ''
    Write-Host "ERROR: $msg" -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# 1. Pre-flight
# ---------------------------------------------------------------------------
Write-Step 'Pre-flight checks'

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    Fail '`dotnet` not found on PATH. Install the .NET 10+ SDK: https://dotnet.microsoft.com/download'
}
$sdkOutput = & dotnet --list-sdks
$has10OrLater = $false
foreach ($line in $sdkOutput) {
    if ($line -match '^(\d+)\.') {
        if ([int]$Matches[1] -ge 10) { $has10OrLater = $true; break }
    }
}
if (-not $has10OrLater) {
    Fail @"
.NET 10+ SDK not detected — Reactor requires 10 or later.
Installed SDKs:
$($sdkOutput -join "`n")
Install the latest .NET SDK from https://dotnet.microsoft.com/download and re-run ./bootstrap.ps1.
"@
}
Write-Ok ".NET SDK present"

# ---------------------------------------------------------------------------
# 2. Pack `mur` as a global-tool nupkg
# ---------------------------------------------------------------------------
Write-Step "Packing mur (Reactor CLI) for dotnet global tool install"

$feed = Join-Path $repoRoot 'local-nupkgs'
New-Item -ItemType Directory -Path $feed -Force | Out-Null

# Match host arch so the embed-resource step (which runs the SignaturesGen
# apphost) succeeds. The packed IL itself is platform-portable.
$hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }

& dotnet pack (Join-Path $repoRoot 'src\Reactor.Cli\Reactor.Cli.csproj') `
    -c $Configuration `
    "-p:Platform=$hostArch" `
    -o $feed `
    --nologo -v:m
if ($LASTEXITCODE -ne 0) { Fail 'dotnet pack failed for Reactor.Cli' }
Write-Ok "Packed Microsoft.UI.Reactor.Cli -> $feed"

# ---------------------------------------------------------------------------
# 3. Install / update the global tool
# ---------------------------------------------------------------------------
if ($SkipMurInstall) {
    Write-Host ''
    Write-Host '    Skipping `dotnet tool install` (per -SkipMurInstall).' -ForegroundColor Yellow
} else {
    Write-Step 'Installing mur as a dotnet global tool'

    $existing = & dotnet tool list -g 2>$null | Select-String -SimpleMatch 'microsoft.ui.reactor.cli'
    if ($existing) {
        & dotnet tool update -g --add-source $feed Microsoft.UI.Reactor.Cli --no-cache --ignore-failed-sources
    } else {
        & dotnet tool install -g --add-source $feed Microsoft.UI.Reactor.Cli --no-cache --ignore-failed-sources
    }
    if ($LASTEXITCODE -ne 0) { Fail '`dotnet tool install/update` failed for Microsoft.UI.Reactor.Cli' }

    # Make ~/.dotnet/tools visible to the rest of this script even if this is
    # the first global tool the user has ever installed (dotnet adds it to the
    # User PATH but not the current process).
    $dotnetTools = Join-Path $env:USERPROFILE '.dotnet\tools'
    if (Test-Path $dotnetTools) {
        $pathParts = $env:Path -split ';'
        if ($pathParts -notcontains $dotnetTools) {
            $env:Path = "$dotnetTools;$env:Path"
        }
    }
    Write-Ok "mur installed as global tool (also on this shell's PATH)"
}

# ---------------------------------------------------------------------------
# 4. Pack the in-source framework + templates via the freshly-installed mur
# ---------------------------------------------------------------------------
Write-Step 'Packing local Microsoft.UI.Reactor + ProjectTemplates (`mur pack-local`)'

# Use the freshly-installed `mur` if available; otherwise call the source
# project directly (works for -SkipMurInstall too).
$murResolved = Get-Command mur -ErrorAction SilentlyContinue
if ($murResolved) {
    & mur pack-local
} else {
    & dotnet run --project (Join-Path $repoRoot 'src\Reactor.Cli\Reactor.Cli.csproj') `
        -c $Configuration `
        "-p:Platform=$hostArch" `
        --nologo `
        -- pack-local
}
if ($LASTEXITCODE -ne 0) { Fail 'mur pack-local failed' }

# ---------------------------------------------------------------------------
# 5. Install the `dotnet new reactorapp` template
# ---------------------------------------------------------------------------
Write-Step 'Installing `dotnet new reactorapp` template'

$templateNupkg = Join-Path $feed 'Microsoft.UI.Reactor.ProjectTemplates.0.0.0-local.nupkg'
if (-not (Test-Path $templateNupkg)) {
    Fail "Template nupkg not produced at $templateNupkg"
}

# Uninstall first so the template engine drops its cached copy by id —
# otherwise the previous install can win against a same-version repack.
& dotnet new uninstall Microsoft.UI.Reactor.ProjectTemplates 2>$null | Out-Null
& dotnet new install $templateNupkg
if ($LASTEXITCODE -ne 0) { Fail '`dotnet new install` failed' }
Write-Ok 'reactorapp template registered'

# ---------------------------------------------------------------------------
# 6. Claude Code plugin (optional)
# ---------------------------------------------------------------------------
if ($SkipPlugin) {
    Write-Host ''
    Write-Host '    Skipping Claude plugin install (per -SkipPlugin).' -ForegroundColor Yellow
} else {
    Write-Step 'Installing Reactor plugin for Claude Code'

    $pluginSrc = Join-Path $repoRoot 'plugins\reactor'
    $pluginDst = Join-Path $env:USERPROFILE '.claude\plugins\reactor'

    if (-not (Test-Path $pluginSrc)) {
        Write-Host "    [skip] $pluginSrc not present in this checkout" -ForegroundColor Yellow
    } else {
        New-Item -ItemType Directory -Path (Split-Path $pluginDst) -Force | Out-Null

        if (Test-Path $pluginDst) {
            Remove-Item $pluginDst -Recurse -Force
        }

        # Prefer symlink so plugin edits in the checkout are immediately visible.
        # Falls back to copy when symlink creation is unprivileged (Developer
        # Mode off + non-admin shell).
        $linked = $false
        try {
            New-Item -ItemType SymbolicLink -Path $pluginDst -Target $pluginSrc -ErrorAction Stop | Out-Null
            $linked = $true
        } catch {
            # Fall through to copy.
        }

        if ($linked) {
            Write-Ok "Symlinked $pluginDst -> $pluginSrc"
        } else {
            Copy-Item $pluginSrc $pluginDst -Recurse -Force
            Write-Ok "Copied $pluginSrc -> $pluginDst (re-run bootstrap or `mur upgrade` to refresh)"
        }
    }
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host 'Bootstrap complete.' -ForegroundColor Green
Write-Host ''
Write-Host 'Next:'
Write-Host '    dotnet new reactorapp -n MyApp'
Write-Host '    cd MyApp'
Write-Host '    dotnet run'
Write-Host ''
Write-Host 'Other useful commands:'
Write-Host '    mur doctor     verify your install'
Write-Host '    mur upgrade    refresh local packages + plugin after `git pull`'
Write-Host '    mur --help     full command list'
