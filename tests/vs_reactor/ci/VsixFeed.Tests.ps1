<#
.SYNOPSIS
    Headless tests for the NuGet feed plumbing on the VSIX build path:
    bootstrap.ps1 -> Reinstall-Vsix.ps1 -> Build-Vsix.ps1 -> MSBuild.

.DESCRIPTION
    bootstrap.ps1 resolves a package feed it can actually reach and uses it for
    its own restores, but the VSIX build runs in a child process that never sees
    that decision. Before the fix it fell through to the repo's public
    nuget.config, so on a network that cannot reach nuget.org the VSIX was the
    one thing bootstrap could not install.

    Nothing here builds, restores, or needs Visual Studio: the "MSBuild" under
    test is a .cmd that writes its own command line to a file, and the
    "Build-Vsix.ps1" under test is a stub that records its bound parameters.
    Each assertion is therefore about the arguments the shipped scripts
    actually emit, not about a build succeeding somewhere.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Pass = 0
$script:Fail = 0
$script:Failures = New-Object System.Collections.Generic.List[string]

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -eq $Actual) { $script:Pass++ }
    else {
        $script:Fail++
        $script:Failures.Add("$Message`n    expected: [$Expected]`n    actual:   [$Actual]")
    }
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if ($Condition) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add($Message) }
}

# These files are BOM-less UTF-8. Windows PowerShell 5.1 decodes such files as
# ANSI, which corrupts every non-ASCII comment character and yields spurious
# parse errors, so read them with an explicit encoding rather than -Raw.
function Get-Utf8Text {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$bootstrap = Join-Path $repoRoot 'bootstrap.ps1'
$buildVsix = Join-Path $repoRoot 'src\vs-reactor\Build-Vsix.ps1'
$reinstall = Join-Path $repoRoot 'src\vs-reactor\Reinstall-Vsix.ps1'
$vsProcessLib = Join-Path $repoRoot 'src\vs-reactor\VsProcessLib.ps1'

# bootstrap.ps1 re-launches Reinstall-Vsix.ps1 with the *current* host, so the
# child processes below use it too: that is the shipped shape, and it is what
# makes the Windows PowerShell 5.1 leg of the CI matrix load-bearing.
$hostExe = (Get-Process -Id $PID).Path

$proxy = 'https://packagefeedproxy.microsoft.io/nuget/v3/index.json'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("vsix-feed-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null

$originalAppData = $env:APPDATA
$originalUserProfile = $env:USERPROFILE
$originalArgsFile = $env:REACTOR_TEST_ARGS_FILE

# Every child below is *expected* to fail: a stub build produces no .vsix, so
# the shipped scripts stop right after recording what we came to measure.
# Windows PowerShell 5.1 promotes native stderr to a terminating error while
# $ErrorActionPreference is 'Stop', which would abort the suite on a working
# script. The assignment here is function-scoped, so it cannot leak.
function Invoke-HostScript {
    param([string[]]$Arguments)
    $ErrorActionPreference = 'Continue'
    & $hostExe @Arguments 2>&1 | Out-Null
    # Surface the child's status to the caller: `| Out-Null` does not disturb
    # $LASTEXITCODE, and the -MSBuildPath case below asserts on it.
    $global:LASTEXITCODE = $LASTEXITCODE
}

# Invokes the real Build-Vsix.ps1 against a fake MSBuild and returns the
# command line it was handed. $AppData/$UserProfile stand in for the user's
# NuGet configuration, which is where a configured mirror is discovered.
function Get-BuildVsixMSBuildCommandLine {
    param(
        [string]$AppData,
        [string]$UserProfile,
        [string[]]$ExtraArguments = @()
    )

    $capture = Join-Path $tmp ("msbuild-args-" + [Guid]::NewGuid().ToString('N') + ".txt")
    $env:REACTOR_TEST_ARGS_FILE = $capture
    $env:APPDATA = $AppData
    $env:USERPROFILE = $UserProfile
    try {
        $arguments = @(
            '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', $buildVsix,
            '-MSBuildPath', $stubMsBuild
        ) + $ExtraArguments
        # Build-Vsix.ps1 fails after the stub "build" because no .vsix appears.
        # Irrelevant: the oracle is the command line the stub recorded.
        Invoke-HostScript -Arguments $arguments
    } finally {
        $env:APPDATA = $originalAppData
        $env:USERPROFILE = $originalUserProfile
        $env:REACTOR_TEST_ARGS_FILE = $originalArgsFile
    }

    if (-not (Test-Path -LiteralPath $capture)) { return $null }
    return (Get-Utf8Text $capture)
}

try {
    # -- 0. Fixtures. --
    $stubMsBuild = Join-Path $tmp 'stub-msbuild.cmd'
    Set-Content -LiteralPath $stubMsBuild -Value @(
        '@echo off'
        'echo %* > "%REACTOR_TEST_ARGS_FILE%"'
        'exit /b 0'
    ) -Encoding ASCII

    # A user configuration carrying a mirror the resolver recognises, sitting
    # alongside a disabled nuget.org.
    $mirrored = Join-Path $tmp 'mirrored'
    $mirroredNuGetDir = Join-Path $mirrored 'NuGet'
    New-Item -ItemType Directory -Path $mirroredNuGetDir -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $mirroredNuGetDir 'NuGet.Config') -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="azure-default" value="$proxy" />
  </packageSources>
  <disabledPackageSources>
    <add key="nuget.org" value="true" />
  </disabledPackageSources>
</configuration>
"@

    # A public contributor: no user-level NuGet configuration at all.
    $public = Join-Path $tmp 'public'
    New-Item -ItemType Directory -Path $public -Force | Out-Null

    # -- 1. A configured mirror reaches MSBuild. --
    # This is the defect. Before the fix Build-Vsix.ps1 emitted no feed
    # property at all and the restore fell through to nuget.org.
    $mirroredLine = Get-BuildVsixMSBuildCommandLine -AppData $mirrored -UserProfile $public
    Assert-True ($null -ne $mirroredLine) 'stub MSBuild was invoked and recorded its command line'
    if ($null -ne $mirroredLine) {
        Assert-True ($mirroredLine -match [regex]::Escape("/p:RestoreSources=$proxy")) `
            'Build-Vsix.ps1 restores through the mirror found in user configuration'
        Assert-True ($mirroredLine -match '/restore') `
            'Build-Vsix.ps1 still asks MSBuild to restore'
    }

    # -- 2. Public contributor: nothing changes. --
    # The discriminating control for #1. If this ever starts emitting a feed
    # property, the assertion above is measuring the harness, not the script.
    $publicLine = Get-BuildVsixMSBuildCommandLine -AppData $public -UserProfile $public
    Assert-True ($null -ne $publicLine) 'stub MSBuild was invoked for the public-default case'
    if ($null -ne $publicLine) {
        Assert-True ($publicLine -notmatch 'RestoreSources') `
            'no user-configured proxy leaves RestoreSources unset (repo nuget.config stays in effect)'
        Assert-True ($publicLine -notmatch 'RestoreConfigFile') `
            'no user-configured proxy leaves RestoreConfigFile unset'
    }

    # -- 3. Explicit overrides beat detection. --
    $explicitConfig = Join-Path $tmp 'explicit.config'
    Set-Content -LiteralPath $explicitConfig -Value '<configuration />'
    $explicitLine = Get-BuildVsixMSBuildCommandLine -AppData $mirrored -UserProfile $public `
        -ExtraArguments @('-NuGetConfig', $explicitConfig)
    Assert-True ($null -ne $explicitLine) 'stub MSBuild was invoked for the explicit-config case'
    if ($null -ne $explicitLine) {
        Assert-True ($explicitLine -match [regex]::Escape("/p:RestoreConfigFile=$explicitConfig")) `
            '-NuGetConfig reaches MSBuild as RestoreConfigFile'
        Assert-True ($explicitLine -notmatch 'RestoreSources') `
            '-NuGetConfig suppresses the detected source rather than stacking with it'
    }

    $sourceLine = Get-BuildVsixMSBuildCommandLine -AppData $public -UserProfile $public `
        -ExtraArguments @('-NuGetSource', 'https://contoso.example.test/nuget/v3/index.json')
    Assert-True ($null -ne $sourceLine) 'stub MSBuild was invoked for the explicit-source case'
    if ($null -ne $sourceLine) {
        Assert-True ($sourceLine -match [regex]::Escape('/p:RestoreSources=https://contoso.example.test/nuget/v3/index.json')) `
            '-NuGetSource reaches MSBuild as RestoreSources'
    }

    # -- 3b. A bad -MSBuildPath fails before anything is invoked. --
    # The discriminating part is that the stub is NOT reached: without the guard the
    # script would try to execute a nonexistent program instead of reporting it.
    $missingMsBuild = Join-Path $tmp 'no-such-msbuild.cmd'
    $badPathCapture = Join-Path $tmp ("msbuild-args-badpath-" + [Guid]::NewGuid().ToString('N') + ".txt")
    $env:REACTOR_TEST_ARGS_FILE = $badPathCapture
    try {
        Invoke-HostScript -Arguments @(
            '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', $buildVsix,
            '-MSBuildPath', $missingMsBuild
        )
        $badPathExit = $LASTEXITCODE
    } finally {
        $env:REACTOR_TEST_ARGS_FILE = $originalArgsFile
    }
    Assert-Equal 1 $badPathExit '-MSBuildPath pointing at a missing file exits 1'
    Assert-Equal $false (Test-Path -LiteralPath $badPathCapture) `
        '-MSBuildPath validation rejects before invoking any build'

    # -- 4. Reinstall-Vsix.ps1 forwards the feed to Build-Vsix.ps1. --
    # Run a copy of the shipped script beside a stub Build-Vsix.ps1 that records
    # its bound parameters, so $PSScriptRoot resolves to the stub.
    $fakeDir = Join-Path $tmp 'reinstall'
    New-Item -ItemType Directory -Path $fakeDir -Force | Out-Null
    Copy-Item -LiteralPath $reinstall -Destination (Join-Path $fakeDir 'Reinstall-Vsix.ps1')
    Copy-Item -LiteralPath $vsProcessLib -Destination (Join-Path $fakeDir 'VsProcessLib.ps1')
    Set-Content -LiteralPath (Join-Path $fakeDir 'Build-Vsix.ps1') -Value @(
        'param('
        '    [switch]$NoRestore,'
        '    [string]$Configuration,'
        '    [string]$Version,'
        '    [string]$NuGetConfig,'
        '    [string]$NuGetSource,'
        '    [string]$MSBuildPath'
        ')'
        '[System.IO.File]::WriteAllText($env:REACTOR_TEST_ARGS_FILE, "config=$NuGetConfig;source=$NuGetSource")'
        'exit 0'
    )

    $forwardCapture = Join-Path $tmp 'reinstall-args.txt'
    $env:REACTOR_TEST_ARGS_FILE = $forwardCapture
    try {
        # Reinstall-Vsix.ps1 stops right after the build step (no .vsix in the
        # stub tree). The recorded parameters are the oracle.
        Invoke-HostScript -Arguments @(
            '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $fakeDir 'Reinstall-Vsix.ps1'),
            '-NuGetConfig', 'C:\feeds\internal.config',
            '-NuGetSource', $proxy
        )
    } finally {
        $env:REACTOR_TEST_ARGS_FILE = $originalArgsFile
    }
    # Under Windows PowerShell 5.1 this also depends on Reinstall-Vsix.ps1 being
    # loadable at all, which section 8 of BootstrapExitCode.Tests.ps1 owns: a
    # non-ASCII character inside one of its string literals decodes to a CP1252
    # smart quote and unbalances the literal. That guard names the cause; this
    # one would only report the symptom.
    Assert-True (Test-Path -LiteralPath $forwardCapture) 'Reinstall-Vsix.ps1 invoked Build-Vsix.ps1'
    if (Test-Path -LiteralPath $forwardCapture) {
        Assert-Equal "config=C:\feeds\internal.config;source=$proxy" (Get-Utf8Text $forwardCapture) `
            'Reinstall-Vsix.ps1 forwards both feed parameters to Build-Vsix.ps1'
    }

    # -- 5. bootstrap.ps1 hands its resolved feed to that call site. --
    # bootstrap.ps1 is far too heavy to run here, so this is a source-level
    # assertion — but it is scoped to the Reinstall-Vsix.ps1 invocation, so an
    # unrelated mention of the variables elsewhere cannot satisfy it.
    $bootstrapText = Get-Utf8Text $bootstrap
    $bootstrapTokens = $null
    $bootstrapErrors = $null
    $bootstrapAst = [System.Management.Automation.Language.Parser]::ParseInput(
        $bootstrapText, [ref]$bootstrapTokens, [ref]$bootstrapErrors)
    Assert-Equal 0 @($bootstrapErrors).Count 'bootstrap.ps1 parses'

    $reinstallCall = $bootstrapAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.CommandAst] -and
            $node.Extent.Text -match '-File \$reinstall'
        }, $true)
    Assert-Equal 1 @($reinstallCall).Count 'bootstrap.ps1: found the Reinstall-Vsix.ps1 call site'
    if (@($reinstallCall).Count -eq 1) {
        $call = @($reinstallCall)[0]
        Assert-True ($call.Extent.Text -match '@vsixFeedArgs') `
            'bootstrap.ps1 splats the resolved feed arguments into the VSIX install'
        $lines = $bootstrapText -split "`r?`n"
        # The argument list is assembled immediately above the call, so an
        # unrelated mention of these variables elsewhere cannot satisfy this.
        $from = [Math]::Max(0, $call.Extent.StartLineNumber - 15)
        $window = ($lines[$from..($call.Extent.StartLineNumber - 1)]) -join "`n"
        Assert-True ($window -match '''-NuGetConfig'',\s*\$effectiveNuGetConfig') `
            'bootstrap.ps1 forwards the resolved NuGet config to the VSIX build'
        Assert-True ($window -match '''-NuGetSource'',\s*\$effectiveNuGetSource') `
            'bootstrap.ps1 forwards the resolved NuGet source to the VSIX build'
    }

    # -- 6. No carriage return outside a CRLF pair. --
    # These files are CRLF. An edit that inserts a bare LF leaves a mid-line CR,
    # which merges the following statement onto the brace line — still valid
    # PowerShell, so it survives a parse check and every assertion above.
    foreach ($file in @($bootstrap, $buildVsix, $reinstall)) {
        $bytes = [System.IO.File]::ReadAllBytes($file)
        $stray = 0
        for ($i = 0; $i -lt $bytes.Length; $i++) {
            if ($bytes[$i] -eq 13 -and ($i + 1 -ge $bytes.Length -or $bytes[$i + 1] -ne 10)) { $stray++ }
        }
        Assert-Equal 0 $stray "$([System.IO.Path]::GetFileName($file)): no carriage return outside a CRLF pair"
    }
}
finally {
    $env:APPDATA = $originalAppData
    $env:USERPROFILE = $originalUserProfile
    $env:REACTOR_TEST_ARGS_FILE = $originalArgsFile
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "VSIX feed plumbing tests: $script:Pass passed, $script:Fail failed"
if ($script:Fail -gt 0) {
    Write-Host ""
    $script:Failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    exit 1
}
exit 0
