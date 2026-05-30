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

.PARAMETER InstallWinAppSdk
    Install the Windows App Runtime 2.0 via winget without prompting.
    Useful for CI / one-shot dev-box automation. Mutually exclusive with
    -NoWinAppSdk. The framework defaults to self-contained, so the
    runtime is only required for framework-dependent deployment.

.PARAMETER NoWinAppSdk
    Skip the Windows App Runtime 2.0 prompt silently. Useful for
    non-interactive scripts that explicitly don't want the runtime
    installed. Mutually exclusive with -InstallWinAppSdk.

.PARAMETER Verbose
    Common parameter (enabled by [CmdletBinding]). Surfaces extra
    diagnostic output at every decision point: detected SDK list,
    winget probe exit codes, host arch / pack args, global-tool
    install-vs-update branch, template uninstall decision, plugin
    symlink-vs-copy fallback, etc. Use this when bootstrap fails or
    behaves unexpectedly and you want to see *why* a branch was taken.

.EXAMPLE
    ./bootstrap.ps1
    Full bootstrap (prompts before installing WindowsAppRuntime).

.EXAMPLE
    ./bootstrap.ps1 -SkipPlugin
    Skip the Claude plugin step.

.EXAMPLE
    ./bootstrap.ps1 -InstallWinAppSdk -SkipPlugin
    Non-interactive: install everything (incl. WindowsAppRuntime) and
    skip the agent plugin. Suitable for CI / fresh-dev-box automation.

.EXAMPLE
    ./bootstrap.ps1 -Verbose
    Print extra `VERBOSE:` diagnostics at every decision point. Useful
    for debugging install failures or unexpected branch behavior.
#>
[CmdletBinding()]
param(
    [switch]$SkipPlugin,
    [switch]$SkipMurInstall,
    [string]$Configuration = 'Release',
    [switch]$InstallWinAppSdk,
    [switch]$NoWinAppSdk
)

if ($InstallWinAppSdk -and $NoWinAppSdk) {
    Write-Host ''
    Write-Host "ERROR: -InstallWinAppSdk and -NoWinAppSdk are mutually exclusive." -ForegroundColor Red
    exit 1
}

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
Set-Location $repoRoot

# -Verbose is a common parameter (CmdletBinding); $VerbosePreference flips to
# 'Continue' automatically when the caller passes it. We expose a tiny helper
# so verbose blocks can short-circuit (e.g. skipping expensive `Out-String`
# captures) when the flag is off.
$script:VerboseOn = ($VerbosePreference -ne 'SilentlyContinue')

function Write-Dbg($msg) {
    Write-Verbose $msg
}

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

# Install a winget package and refresh $env:Path so the freshly-installed tool
# is resolvable in this same shell. Hard-fails if winget itself is missing —
# that's an OS-level prerequisite this script doesn't try to repair.
function Install-WithWinget {
    param(
        [Parameter(Mandatory)][string]$Id,
        [string]$Reason = $Id
    )
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Fail "Need to install '$Reason' but winget is not on PATH. Install App Installer from the Microsoft Store, then re-run ./bootstrap.ps1."
    }
    Write-Host "    Installing $Reason via winget ($Id)..." -ForegroundColor Yellow
    Write-Dbg "winget install --id $Id --accept-source-agreements --accept-package-agreements --silent --disable-interactivity"
    & winget install --id $Id --accept-source-agreements --accept-package-agreements --silent --disable-interactivity
    Write-Dbg "winget exit code: $LASTEXITCODE"
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189) {
        # -1978335189 = APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE (already installed / up-to-date)
        Fail "winget install $Id failed (exit $LASTEXITCODE). Install $Reason manually and re-run ./bootstrap.ps1."
    }
    # winget edits the Machine + User PATH but the current process keeps its
    # original. Rebuild $env:Path from the registry so subsequent commands in
    # this script can find the freshly-installed binaries.
    $env:Path = (
        [Environment]::GetEnvironmentVariable('Path', 'Machine'),
        [Environment]::GetEnvironmentVariable('Path', 'User')
    ) -join ';'
    Write-Dbg "Refreshed `$env:Path from registry (Machine + User scopes)"
}

# ---------------------------------------------------------------------------
# 1. Pre-flight
# ---------------------------------------------------------------------------
Write-Step 'Pre-flight checks'

Write-Dbg "PowerShell $($PSVersionTable.PSVersion); OS arch $env:PROCESSOR_ARCHITECTURE; repo $repoRoot"

function Test-DotnetSdk10 {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    foreach ($line in (& dotnet --list-sdks)) {
        Write-Dbg "  dotnet SDK: $line"
        if ($line -match '^(\d+)\.' -and [int]$Matches[1] -ge 10) { return $true }
    }
    return $false
}

if (-not (Test-DotnetSdk10)) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Host "    [info] dotnet not found on PATH." -ForegroundColor Yellow
    } else {
        Write-Host "    [info] dotnet present but no .NET 10+ SDK detected. Installed:" -ForegroundColor Yellow
        & dotnet --list-sdks | ForEach-Object { Write-Host "          $_" -ForegroundColor Yellow }
    }
    Install-WithWinget -Id 'Microsoft.DotNet.SDK.10' -Reason '.NET 10 SDK'
    if (-not (Test-DotnetSdk10)) {
        Fail '.NET 10 SDK install reported success but `dotnet --list-sdks` still does not show a 10.x entry. Open a new shell and re-run ./bootstrap.ps1.'
    }
}
Write-Ok ".NET SDK present"

# Windows App SDK runtime — optional but recommended.
#
# The framework defaults to WindowsAppSDKSelfContained=true (see
# Directory.Build.props), so builds and scaffolded apps work *without* the
# machine-wide runtime — every app's bin/ output ships its own copy of WAS
# native binaries from NuGet restore.
#
# But many devs prefer framework-dependent deployment: smaller per-app
# output, faster incremental builds, and the runtime installed once on the
# machine. For that path the user needs the WindowsAppRuntime 2.0 install
# matching our WindowsAppSDKVersion=2.0.1.
#
# So we prompt by default. `-InstallWinAppSdk` to force-install,
# `-InstallWinAppSdk:$false` to skip the prompt non-interactively.

function Test-WindowsAppRuntime20 {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Dbg "winget not on PATH; skipping WindowsAppRuntime probe"
        return $true  # nothing we can check without winget
    }
    # --accept-source-agreements is needed even for `list` on a winget that
    # hasn't been used before (e.g. a fresh CI runner). Without it, winget
    # prompts for msstore terms and fails on a non-interactive shell with
    # exit -1978335166.
    Write-Dbg "winget list --id Microsoft.WindowsAppRuntime.2.0 --exact --accept-source-agreements"
    if ($script:VerboseOn) {
        & winget list --id Microsoft.WindowsAppRuntime.2.0 --exact --accept-source-agreements 2>&1 |
            ForEach-Object { Write-Dbg "  winget> $_" }
    } else {
        & winget list --id Microsoft.WindowsAppRuntime.2.0 --exact --accept-source-agreements 2>$null | Out-Null
    }
    $rc = $LASTEXITCODE
    Write-Dbg "winget list exit code: $rc"
    $global:LASTEXITCODE = 0  # don't let winget's status leak out of the probe
    return ($rc -eq 0)
}

if (-not (Test-WindowsAppRuntime20)) {
    if ($InstallWinAppSdk) {
        Install-WithWinget -Id 'Microsoft.WindowsAppRuntime.2.0' -Reason 'Windows App Runtime 2.0'
    } elseif ($NoWinAppSdk) {
        Write-Host '    [skip] Windows App Runtime 2.0 not installed (skipped per -NoWinAppSdk).' -ForegroundColor Yellow
    } else {
        Write-Host ''
        Write-Host '    Windows App Runtime 2.0 is not installed on this machine.' -ForegroundColor Yellow
        Write-Host '    Reactor builds default to WindowsAppSDKSelfContained=true, so this is optional —'
        Write-Host '    your apps will work either way. Installing it enables framework-dependent'
        Write-Host '    deployment (smaller per-app output, faster builds) when you override'
        Write-Host '    WindowsAppSDKSelfContained=false in a consuming project.'
        $answer = Read-Host '    Install Windows App Runtime 2.0 via winget now? [y/N]'
        if ($answer -match '^[Yy]') {
            Install-WithWinget -Id 'Microsoft.WindowsAppRuntime.2.0' -Reason 'Windows App Runtime 2.0'
        } else {
            Write-Host "    Skipped. Re-run later with: winget install Microsoft.WindowsAppRuntime.2.0" -ForegroundColor Cyan
        }
    }
} else {
    Write-Ok 'Windows App Runtime 2.0 installed'
}

# ---------------------------------------------------------------------------
# 2. Pack `mur` as a global-tool nupkg
# ---------------------------------------------------------------------------
Write-Step "Packing mur (Reactor CLI) for dotnet global tool install"

$feed = Join-Path $repoRoot 'local-nupkgs'
New-Item -ItemType Directory -Path $feed -Force | Out-Null
Write-Dbg "Local nupkg feed: $feed"

# Match host arch so the embed-resource step (which runs the SignaturesGen
# apphost) succeeds. The packed IL itself is platform-portable.
$hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
Write-Dbg "Host arch resolved to: $hostArch"

Write-Dbg "dotnet pack src\Reactor.Cli\Reactor.Cli.csproj -c $Configuration -p:Platform=$hostArch -o $feed"
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
        Write-Dbg "Existing global tool detected ($($existing.Line.Trim())); using 'dotnet tool update'"
        & dotnet tool update -g --add-source $feed Microsoft.UI.Reactor.Cli --no-cache --ignore-failed-sources
    } else {
        Write-Dbg "No existing global tool; using 'dotnet tool install'"
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
            Write-Dbg "Prepending $dotnetTools to current-process PATH"
            $env:Path = "$dotnetTools;$env:Path"
        } else {
            Write-Dbg "$dotnetTools already on current-process PATH"
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
    Write-Dbg "Using installed mur at $($murResolved.Source)"
    & mur pack-local
} else {
    Write-Dbg "mur not on PATH; falling back to 'dotnet run' against Reactor.Cli source"
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
# `dotnet new uninstall` (no args) lists installed template packages; skip the
# uninstall on first run when our package isn't there yet (else the non-zero
# exit code becomes a terminating error under $ErrorActionPreference = 'Stop'
# in PS 7.4+, which `2>$null` doesn't intercept).
Write-Dbg "Probing installed templates via 'dotnet new uninstall' (no args)"
if ($script:VerboseOn) {
    # Capture the full listing so we can both echo each line as a debug
    # breadcrumb AND substring-match for our template id below.
    $installedTemplates = & dotnet new uninstall 2>&1 | Out-String
    foreach ($line in ($installedTemplates -split "`r?`n")) {
        if ($line.Trim()) { Write-Dbg "  templates> $line" }
    }
    $hasReactorTemplate = $installedTemplates -match 'Microsoft\.UI\.Reactor\.ProjectTemplates'
} else {
    # Quiet path: stream through Select-String -Quiet so we never materialize
    # the multi-KB listing for what is, semantically, a single boolean test.
    $hasReactorTemplate = [bool](& dotnet new uninstall 2>&1 |
        Select-String -SimpleMatch 'Microsoft.UI.Reactor.ProjectTemplates' -Quiet)
}
if ($hasReactorTemplate) {
    Write-Dbg "Existing Microsoft.UI.Reactor.ProjectTemplates detected; uninstalling stale copy"
    if ($script:VerboseOn) {
        & dotnet new uninstall Microsoft.UI.Reactor.ProjectTemplates
    } else {
        & dotnet new uninstall Microsoft.UI.Reactor.ProjectTemplates | Out-Null
    }
    if ($LASTEXITCODE -ne 0) { Fail '`dotnet new uninstall` failed' }
} else {
    Write-Dbg "No prior install of Microsoft.UI.Reactor.ProjectTemplates; skipping uninstall (first-run path)"
}
Write-Dbg "dotnet new install $templateNupkg"
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
    Write-Dbg "Plugin src: $pluginSrc"
    Write-Dbg "Plugin dst: $pluginDst"

    if (-not (Test-Path $pluginSrc)) {
        Write-Host "    [skip] $pluginSrc not present in this checkout" -ForegroundColor Yellow
    } else {
        New-Item -ItemType Directory -Path (Split-Path $pluginDst) -Force | Out-Null

        if (Test-Path $pluginDst) {
            Write-Dbg "Removing existing $pluginDst before re-linking"
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
            Write-Dbg "Symlink failed: $($_.Exception.Message); falling back to copy"
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
