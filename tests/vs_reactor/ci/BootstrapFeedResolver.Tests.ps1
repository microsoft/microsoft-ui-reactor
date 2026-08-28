<#
.SYNOPSIS
    Headless tests for bootstrap's user-scoped npm and NuGet feed selection.

.DESCRIPTION
    Runs under PowerShell 7 and Windows PowerShell 5.1. No network access,
    package restore, credentials, or developer machine configuration required.
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

function Assert-Throws {
    param([scriptblock]$Action, [string]$Message)
    try {
        & $Action
        $script:Fail++
        $script:Failures.Add($Message)
    } catch {
        $script:Pass++
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
. (Join-Path $repoRoot 'tools\BootstrapFeedResolver.ps1')

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("bootstrap-feeds-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$originalRegistry = $env:NPM_CONFIG_REGISTRY
$originalUserConfig = $env:NPM_CONFIG_USERCONFIG
$originalRestoreConfig = $env:RestoreConfigFile
$originalRestoreSources = $env:RestoreSources

try {
    $env:NPM_CONFIG_REGISTRY = $null
    $env:NPM_CONFIG_USERCONFIG = $null

    $profile = Join-Path $tmp 'profile'
    New-Item -ItemType Directory -Path $profile | Out-Null
    Set-Content (Join-Path $profile '.npmrc') @(
        'registry=https://registry.npmjs.org/'
        'registry=https://packagefeedproxy.microsoft.io/npm/'
    )

    $npm = Resolve-ReactorNpmRegistry -UserProfile $profile
    Assert-Equal 'https://packagefeedproxy.microsoft.io/npm' $npm.Registry `
        'automatic npm selection honors the Microsoft proxy in the user .npmrc'
    Assert-Equal $false $npm.Explicit 'automatic npm selection is marked non-explicit'

    $env:NPM_CONFIG_REGISTRY = 'https://packagefeedproxy.microsoft.io/npm/env/'
    $npm = Resolve-ReactorNpmRegistry -UserProfile $profile
    Assert-Equal 'https://packagefeedproxy.microsoft.io/npm/env' $npm.Registry `
        'NPM_CONFIG_REGISTRY takes precedence over npmrc files'
    $env:NPM_CONFIG_REGISTRY = $null

    Set-Content (Join-Path $profile '.npmrc') 'registry=https://registry.npmjs.org/'
    Assert-Equal $null (Resolve-ReactorNpmRegistry -UserProfile $profile) `
        'public npm configuration leaves the SDK public default unchanged'

    $explicitUserNpmrc = Join-Path $tmp 'managed.npmrc'
    Set-Content $explicitUserNpmrc 'registry=https://packagefeedproxy.microsoft.io/npm/'
    $env:NPM_CONFIG_USERCONFIG = $explicitUserNpmrc
    $npm = Resolve-ReactorNpmRegistry -UserProfile $profile
    Assert-Equal 'https://packagefeedproxy.microsoft.io/npm' $npm.Registry `
        'NPM_CONFIG_USERCONFIG takes precedence over the default user .npmrc'
    $env:NPM_CONFIG_USERCONFIG = $null

    $explicitNpm = Resolve-ReactorNpmRegistry -ExplicitRegistry 'https://mirror.example.test/npm/'
    Assert-Equal 'https://mirror.example.test/npm' $explicitNpm.Registry `
        'explicit npm mirror supports non-Microsoft registries'
    Assert-Equal $true $explicitNpm.Explicit 'explicit npm selection is marked explicit'

    Assert-Throws { Resolve-ReactorNpmRegistry -ExplicitRegistry 'http://mirror.example.test/npm' } `
        'remote plaintext HTTP npm registry is rejected'
    Assert-Throws { Resolve-ReactorNpmRegistry -ExplicitRegistry 'https://user:secret@mirror.example.test/npm' } `
        'credential-bearing npm registry is rejected'
    Assert-Throws { Resolve-ReactorNpmRegistry -ExplicitRegistry 'https://mirror.example.test/npm?token=secret' } `
        'query-bearing npm registry is rejected'
    $loopbackNpm = Resolve-ReactorNpmRegistry -ExplicitRegistry 'http://localhost:4873/npm/'
    Assert-Equal 'http://localhost:4873/npm' $loopbackNpm.Registry `
        'loopback HTTP npm registry remains available for local development'

    $script:ObservedMetadataUrl = $null
    $script:ObservedTarballUrl = $null
    $metadataRequest = {
        param($Url)
        $script:ObservedMetadataUrl = $Url
        return [pscustomobject]@{
            'dist-tags' = [pscustomobject]@{ latest = '1.2.3' }
        }
    }
    $tarballRequest = {
        param($Url)
        $script:ObservedTarballUrl = $Url
        return $true
    }
    Assert-True (Test-ReactorNpmRegistryAccess `
            -Registry 'https://packagefeedproxy.microsoft.io/npm' `
            -Platform 'win32-arm64' `
            -MetadataRequest $metadataRequest `
            -TarballRequest $tarballRequest) `
        'npm proxy probe succeeds only after metadata and tarball access succeed'
    Assert-Equal 'https://packagefeedproxy.microsoft.io/npm/@github%2Fcopilot-win32-arm64' `
        $script:ObservedMetadataUrl 'npm probe requests architecture-specific package metadata'
    Assert-Equal 'https://packagefeedproxy.microsoft.io/npm/@github/copilot-win32-arm64/-/copilot-win32-arm64-1.2.3.tgz' `
        $script:ObservedTarballUrl 'npm probe requests the architecture-specific tarball without npm authentication'
    Assert-Equal $false (Test-ReactorNpmRegistryAccess `
            -Registry 'https://packagefeedproxy.microsoft.io/npm' `
            -Platform 'win32-x64' `
            -MetadataRequest $metadataRequest `
            -TarballRequest { return $false }) `
        'npm proxy probe fails when unauthenticated tarball access fails'

    $appData = Join-Path $tmp 'appdata'
    $nugetDir = Join-Path $appData 'NuGet'
    New-Item -ItemType Directory -Path $nugetDir | Out-Null
    Set-Content (Join-Path $nugetDir 'NuGet.Config') @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="azure-default" value="https://packagefeedproxy.microsoft.io/nuget/v3/index.json" />
  </packageSources>
</configuration>
'@

    $nuget = Resolve-ReactorNuGetFeed -AppData $appData -UserProfile $profile
    Assert-Equal 'https://packagefeedproxy.microsoft.io/nuget/v3/index.json' $nuget.Source `
        'automatic NuGet selection finds the Microsoft proxy in user configuration'
    Assert-Equal 'azure-default' $nuget.SourceKey 'automatic NuGet selection retains the source key'
    Assert-Equal (Join-Path $nugetDir 'NuGet.Config') $nuget.ConfigPath `
        'automatic NuGet selection retains the originating config path'
    Assert-Equal $false $nuget.Explicit 'automatic NuGet selection is marked non-explicit'

    $profileNugetDir = Join-Path $profile '.nuget\NuGet'
    New-Item -ItemType Directory -Path $profileNugetDir -Force | Out-Null
    Set-Content (Join-Path $profileNugetDir 'NuGet.Config') @'
<configuration>
  <packageSources>
    <add key="profile-proxy" value="https://packagefeedproxy.microsoft.io/nuget/profile/v3/index.json" />
  </packageSources>
</configuration>
'@
    Set-Content (Join-Path $nugetDir 'NuGet.Config') @'
<configuration>
  <packageSources>
    <add key="disabled-proxy" value="https://packagefeedproxy.microsoft.io/nuget/disabled/v3/index.json" />
  </packageSources>
  <disabledPackageSources>
    <add key="disabled-proxy" value="true" />
  </disabledPackageSources>
</configuration>
'@
    $nuget = Resolve-ReactorNuGetFeed -AppData $appData -UserProfile $profile
    Assert-Equal 'profile-proxy' $nuget.SourceKey `
        'disabled Microsoft proxy is skipped in favor of an enabled secondary config'

    Set-Content (Join-Path $nugetDir 'NuGet.Config') '<configuration><broken>'
    $nuget = Resolve-ReactorNuGetFeed -AppData $appData -UserProfile $profile
    Assert-Equal 'profile-proxy' $nuget.SourceKey `
        'malformed NuGet config is skipped in favor of a valid secondary config'

    $nugetSearchSuccess = {
        param($Source)
        return [pscustomobject]@{
            ExitCode = 0
            Output = '{"searchResult":[{"packages":[{"id":"GitHub.Copilot.SDK"}]}]}'
        }
    }
    Assert-True (Test-ReactorNuGetSourceAccess `
            -Source 'https://packagefeedproxy.microsoft.io/nuget/v3/index.json' `
            -SearchRequest $nugetSearchSuccess) `
        'NuGet proxy probe succeeds only when the required SDK package is present'
    Assert-Equal $false (Test-ReactorNuGetSourceAccess `
            -Source 'https://packagefeedproxy.microsoft.io/nuget/v3/index.json' `
            -SearchRequest {
                return [pscustomobject]@{ ExitCode = 0; Output = '{"searchResult":[]}' }
            }) `
        'NuGet proxy probe rejects successful searches that omit the required SDK package'
    Assert-Equal $false (Test-ReactorNuGetSourceAccess `
            -Source 'https://packagefeedproxy.microsoft.io/nuget/v3/index.json' `
            -SearchRequest {
                return [pscustomobject]@{ ExitCode = 1; Output = '{"id":"GitHub.Copilot.SDK"}' }
            }) `
        'NuGet proxy probe rejects failed package searches even when output contains the package ID'

    $explicitConfig = Join-Path $tmp 'explicit.config'
    Set-Content $explicitConfig '<configuration />'
    $explicitNuget = Resolve-ReactorNuGetFeed -ExplicitConfig $explicitConfig
    Assert-Equal (Resolve-Path $explicitConfig).Path $explicitNuget.ConfigPath `
        'explicit NuGet config resolves to an absolute path'
    Assert-Equal $true $explicitNuget.Explicit 'explicit NuGet selection is marked explicit'
    Assert-Throws { Resolve-ReactorNuGetFeed -ExplicitConfig (Join-Path $tmp 'missing.config') } `
        'missing explicit NuGet config is rejected'

    $restoreArgs = Get-ReactorRestoreArguments `
        -NuGetSource 'https://packagefeedproxy.microsoft.io/nuget/v3/index.json' `
        -NpmRegistry 'https://packagefeedproxy.microsoft.io/npm'
    Assert-Equal '-p:RestoreSources=https://packagefeedproxy.microsoft.io/nuget/v3/index.json|-p:CopilotNpmRegistryUrl=https://packagefeedproxy.microsoft.io/npm' `
        ($restoreArgs -join '|') 'automatic feed arguments propagate NuGet and npm proxies to MSBuild'

    $restoreArgs = Get-ReactorRestoreArguments `
        -NuGetConfig $explicitConfig `
        -NuGetSource 'https://ignored.example.test/nuget' `
        -NpmRegistry 'https://mirror.example.test/npm'
    Assert-Equal "-p:RestoreConfigFile=$explicitConfig|-p:CopilotNpmRegistryUrl=https://mirror.example.test/npm" `
        ($restoreArgs -join '|') 'explicit NuGet config takes precedence in MSBuild arguments'

    $toolArgs = Get-ReactorToolArguments `
        -Feed 'C:\repo\local-nupkgs' `
        -NuGetSource 'https://packagefeedproxy.microsoft.io/nuget/v3/index.json'
    Assert-Equal '-g|--add-source|C:\repo\local-nupkgs|--add-source|https://packagefeedproxy.microsoft.io/nuget/v3/index.json|Microsoft.UI.Reactor.Cli|--no-cache|--ignore-failed-sources' `
        ($toolArgs -join '|') 'tool arguments include both local and automatic proxy sources'

    # Resolve-ReactorNuGetFeedOverride is what scripts a developer runs directly
    # (Build-Vsix.ps1) use: explicit arguments first, then the same user-config
    # discovery, and no network probe. $profile still carries `profile-proxy`
    # from the discovery cases above.
    $override = Resolve-ReactorNuGetFeedOverride -AppData $appData -UserProfile $profile
    Assert-Equal 'https://packagefeedproxy.microsoft.io/nuget/profile/v3/index.json' $override.Source `
        'feed override falls back to the Microsoft proxy in user configuration'
    Assert-Equal $null $override.ConfigPath 'detected override selects a source, not a config file'
    Assert-Equal 'user configuration (profile-proxy)' $override.Origin `
        'detected override reports which configured source it picked'

    $override = Resolve-ReactorNuGetFeedOverride `
        -NuGetConfig $explicitConfig `
        -NuGetSource 'https://ignored.example.test/nuget' `
        -AppData $appData -UserProfile $profile
    Assert-Equal (Resolve-Path $explicitConfig).Path $override.ConfigPath `
        'explicit config wins over both an explicit source and discovery'
    Assert-Equal $null $override.Source 'explicit config suppresses any source override'
    Assert-Equal 'explicit config' $override.Origin 'explicit config reports its origin'

    $override = Resolve-ReactorNuGetFeedOverride `
        -NuGetSource 'https://contoso.example.test/nuget/v3/index.json/' `
        -AppData $appData -UserProfile $profile
    Assert-Equal 'https://contoso.example.test/nuget/v3/index.json' $override.Source `
        'explicit source wins over discovery and is normalized'
    Assert-Equal 'explicit source' $override.Origin 'explicit source reports its origin'

    Assert-Throws { Resolve-ReactorNuGetFeedOverride -NuGetSource 'http://contoso.example.test/nuget' } `
        'explicit source over plain HTTP to a remote host is rejected'
    Assert-Throws { Resolve-ReactorNuGetFeedOverride -NuGetConfig (Join-Path $tmp 'missing.config') } `
        'missing explicit config is rejected by the override resolver too'

    $emptyHome = Join-Path $tmp 'no-user-config'
    New-Item -ItemType Directory -Path $emptyHome | Out-Null
    Assert-Equal $null (Resolve-ReactorNuGetFeedOverride -AppData $emptyHome -UserProfile $emptyHome) `
        'no configured proxy yields no override, preserving the repo public default'

    # An omitted -Version must not emit a --version flag (back-compat with the
    # pre-existing call shape asserted above).
    Assert-True ($toolArgs -notcontains '--version') `
        'tool arguments omit --version when no version is supplied'

    # Regression guard for the stale-global-tool bug: bootstrap packs a per-run
    # version stamp and must pin it explicitly, because `dotnet tool update`
    # silently no-ops (exit 0) when the resolved version equals the installed
    # one. Sample value matches the shipped stamp shape.
    $toolArgs = Get-ReactorToolArguments `
        -Feed 'C:\repo\local-nupkgs' `
        -Version '1.2429.1352.7'
    Assert-Equal '-g|--add-source|C:\repo\local-nupkgs|Microsoft.UI.Reactor.Cli|--version|1.2429.1352.7|--no-cache|--ignore-failed-sources' `
        ($toolArgs -join '|') 'tool arguments pin the explicitly supplied version'

    # ---- Get-ReactorLocalCliVersion -------------------------------------
    # The stamp is what actually broke: a constant 1.0.0 made `dotnet tool
    # update` a silent no-op. These assert the three constraints the encoding
    # has to satisfy at once, each of which a plausible alternative violates.

    $v = Get-ReactorLocalCliVersion -Now ([datetime]::new(2026, 8, 26, 15, 13, 7, [DateTimeKind]::Utc))
    Assert-Equal '1.2429.913.7000' $v 'stamp encodes days-since-2020, minute-of-day, and second+millisecond'

    # Must outrank the stable 1.0.0 that older bootstraps installed, or
    # `dotnet tool update` refuses to move to it.
    Assert-True ([version]$v -gt [version]'1.0.0') 'stamp sorts above the legacy constant 1.0.0'

    # AssemblyVersion/FileVersion components are UInt16; 1.<yyMMdd>.<HHmm>
    # overflows and fails the build with CS7034. The revision is the tightest
    # component: 59*1000+999 = 59999.
    $maxRevision = Get-ReactorLocalCliVersion -Now ([datetime]::new(2026, 8, 26, 23, 59, 59, 999, [DateTimeKind]::Utc))
    foreach ($part in $maxRevision.Split('.')) {
        Assert-True ([int]$part -le 65534) "stamp component '$part' fits in UInt16 at the worst-case instant"
    }

    # minute-of-day must stay within 0-1439. A plain [int] cast rounds
    # half-to-even, so the last 30 seconds of every minute would be stamped with
    # the following minute and 23:59:59.999 would emit 1440.
    Assert-Equal '1.2429.1439.59999' $maxRevision 'the last instant of a day floors to minute 1439, not 1440'
    Assert-Equal '1.2429.913.42000' `
        (Get-ReactorLocalCliVersion -Now ([datetime]::new(2026, 8, 26, 15, 13, 42, [DateTimeKind]::Utc))) `
        'a second past the half-minute stays in the current minute'

    # Unique per run: two packs within the same *second* must still yield
    # distinct, ordered versions, otherwise the second install is a no-op that
    # silently keeps the older binary.
    $sameSecondA = Get-ReactorLocalCliVersion -Now ([datetime]::new(2026, 8, 26, 15, 13, 7, 120, [DateTimeKind]::Utc))
    $sameSecondB = Get-ReactorLocalCliVersion -Now ([datetime]::new(2026, 8, 26, 15, 13, 7, 880, [DateTimeKind]::Utc))
    Assert-True ([version]$sameSecondB -gt [version]$sameSecondA) `
        'two stamps in the same second are distinct and ordered'

    # Ordering must also hold across the coarser boundaries.
    $sameMinuteA = Get-ReactorLocalCliVersion -Now ([datetime]::new(2026, 8, 26, 15, 13, 7, [DateTimeKind]::Utc))
    $sameMinuteB = Get-ReactorLocalCliVersion -Now ([datetime]::new(2026, 8, 26, 15, 13, 42, [DateTimeKind]::Utc))
    Assert-True ([version]$sameMinuteB -gt [version]$sameMinuteA) `
        'two stamps in the same minute are distinct and ordered'

    # Ordering must hold across a day boundary, where minute-of-day resets.
    $endOfDay = Get-ReactorLocalCliVersion -Now ([datetime]::new(2026, 8, 26, 23, 59, 59, [DateTimeKind]::Utc))
    $nextDay  = Get-ReactorLocalCliVersion -Now ([datetime]::new(2026, 8, 27, 0, 0, 0, [DateTimeKind]::Utc))
    Assert-True ([version]$nextDay -gt [version]$endOfDay) 'stamp keeps ordering across a day boundary'

    # A wrong system clock would otherwise emit a negative or absurd minor and
    # produce a confusing NuGet error far from the cause.
    Assert-Throws { Get-ReactorLocalCliVersion -Now ([datetime]::new(2019, 12, 31, 0, 0, 0, [DateTimeKind]::Utc)) } `
        'a pre-epoch clock is rejected rather than emitting a bad version'

    $env:RestoreConfigFile = 'before.config'
    $env:RestoreSources = 'before-source'
    $script:ObservedRestoreConfig = 'not-run'
    $script:ObservedRestoreSources = 'not-run'
    $nativeExit = 0
    Invoke-ReactorWithRestoreEnvironment `
        -NuGetSource 'https://packagefeedproxy.microsoft.io/nuget/v3/index.json' `
        -ExitCode ([ref]$nativeExit) `
        -Action {
            $script:ObservedRestoreConfig = $env:RestoreConfigFile
            $script:ObservedRestoreSources = $env:RestoreSources
            & $env:ComSpec /d /c 'exit 7'
        }
    Assert-True ([string]::IsNullOrEmpty($script:ObservedRestoreConfig)) `
        'automatic source clears an ambient explicit restore config during the command'
    Assert-Equal 'https://packagefeedproxy.microsoft.io/nuget/v3/index.json' $script:ObservedRestoreSources `
        'automatic source is visible to nested restore commands'
    Assert-Equal 7 $nativeExit 'native command exit code is captured through the restore environment wrapper'
    Assert-Equal 'before.config' $env:RestoreConfigFile 'restore config environment is restored after the command'
    Assert-Equal 'before-source' $env:RestoreSources 'restore sources environment is restored after the command'
}
finally {
    $env:NPM_CONFIG_REGISTRY = $originalRegistry
    $env:NPM_CONFIG_USERCONFIG = $originalUserConfig
    $env:RestoreConfigFile = $originalRestoreConfig
    $env:RestoreSources = $originalRestoreSources
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Bootstrap feed resolver tests: $script:Pass passed, $script:Fail failed"
if ($script:Fail -gt 0) {
    Write-Host ""
    $script:Failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    exit 1
}
exit 0
