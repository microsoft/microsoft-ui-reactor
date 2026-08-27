function Test-ReactorPackageFeedUrl {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }

    $uri = $null
    if (-not [Uri]::TryCreate($Value.Trim(), [UriKind]::Absolute, [ref]$uri)) { return $false }
    if ($uri.Scheme -ne 'https' -and -not ($uri.Scheme -eq 'http' -and $uri.IsLoopback)) { return $false }
    return [string]::IsNullOrEmpty($uri.UserInfo) -and
        [string]::IsNullOrEmpty($uri.Query) -and
        [string]::IsNullOrEmpty($uri.Fragment)
}

function Resolve-ReactorNpmRegistry {
    param(
        [string]$ExplicitRegistry,
        [string]$UserProfile = $env:USERPROFILE
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitRegistry)) {
        if (-not (Test-ReactorPackageFeedUrl $ExplicitRegistry)) {
            throw "-NpmRegistry must be an HTTPS URL without credentials, query, or fragment (HTTP is allowed only for loopback): '$ExplicitRegistry'"
        }
        return [pscustomobject]@{
            Registry = $ExplicitRegistry.Trim().TrimEnd('/')
            Explicit = $true
        }
    }

    $configuredRegistry = $env:NPM_CONFIG_REGISTRY
    if ([string]::IsNullOrWhiteSpace($configuredRegistry)) {
        $npmConfigs = New-Object System.Collections.Generic.List[string]
        if (-not [string]::IsNullOrWhiteSpace($env:NPM_CONFIG_USERCONFIG)) {
            $npmConfigs.Add($env:NPM_CONFIG_USERCONFIG)
        } elseif (-not [string]::IsNullOrWhiteSpace($UserProfile)) {
            $npmConfigs.Add((Join-Path $UserProfile '.npmrc'))
        }

        foreach ($config in @($npmConfigs | Select-Object -Unique)) {
            if (-not (Test-Path -LiteralPath $config -PathType Leaf)) { continue }
            foreach ($line in Get-Content -LiteralPath $config) {
                if ($line -match '^\s*registry\s*=\s*(\S.*?)\s*$') {
                    $configuredRegistry = $Matches[1].Trim().Trim('"').Trim("'")
                }
            }
        }
    }

    if (-not (Test-ReactorPackageFeedUrl $configuredRegistry)) { return $null }

    $uri = [Uri]$configuredRegistry
    if ($uri.Host -ne 'packagefeedproxy.microsoft.io') { return $null }

    return [pscustomobject]@{
        Registry = $configuredRegistry.Trim().TrimEnd('/')
        Explicit = $false
    }
}

function Test-ReactorNpmRegistryAccess {
    param(
        [Parameter(Mandatory)][string]$Registry,
        [Parameter(Mandatory)][ValidateSet('win32-x64', 'win32-arm64')][string]$Platform,
        [int]$TimeoutSec = 20,
        [scriptblock]$MetadataRequest,
        [scriptblock]$TarballRequest
    )

    if (-not (Test-ReactorPackageFeedUrl $Registry)) { return $false }

    try {
        $normalized = $Registry.Trim().TrimEnd('/')
        $metadataUrl = "$normalized/@github%2Fcopilot-$Platform"
        $metadata = if ($MetadataRequest) {
            & $MetadataRequest $metadataUrl
        } else {
            Invoke-RestMethod -Uri $metadataUrl -Method Get -TimeoutSec $TimeoutSec
        }

        $version = [string]$metadata.'dist-tags'.latest
        if ([string]::IsNullOrWhiteSpace($version)) { return $false }

        $tarballUrl = "$normalized/@github/copilot-$Platform/-/copilot-$Platform-$version.tgz"
        if ($TarballRequest) {
            return [bool](& $TarballRequest $tarballUrl)
        }

        $response = Invoke-WebRequest -Uri $tarballUrl -Method Get `
            -Headers @{ Range = 'bytes=0-0' } -UseBasicParsing -TimeoutSec $TimeoutSec
        return $response.StatusCode -eq 200 -or $response.StatusCode -eq 206
    } catch {
        return $false
    }
}

function Resolve-ReactorNuGetFeed {
    param(
        [string]$ExplicitConfig,
        [string]$AppData = $env:APPDATA,
        [string]$UserProfile = $env:USERPROFILE
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitConfig)) {
        $resolved = Resolve-Path -LiteralPath $ExplicitConfig -ErrorAction SilentlyContinue
        if (-not $resolved -or -not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) {
            throw "-NuGetConfig does not exist: '$ExplicitConfig'"
        }
        return [pscustomobject]@{
            ConfigPath = $resolved.Path
            Source = $null
            SourceKey = $null
            Explicit = $true
        }
    }

    $configs = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($AppData)) {
        $configs.Add((Join-Path $AppData 'NuGet\NuGet.Config'))
    }
    if (-not [string]::IsNullOrWhiteSpace($UserProfile)) {
        $configs.Add((Join-Path $UserProfile '.nuget\NuGet\NuGet.Config'))
    }

    foreach ($config in @($configs | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $config -PathType Leaf)) { continue }
        try {
            [xml]$xml = Get-Content -LiteralPath $config -Raw
            $disabled = @{}
            foreach ($entry in @($xml.SelectNodes('//disabledPackageSources/add'))) {
                if ([string]$entry.value -eq 'true') {
                    $disabled[[string]$entry.key] = $true
                }
            }
            foreach ($source in @($xml.SelectNodes('//packageSources/add'))) {
                $key = [string]$source.key
                if ($disabled.ContainsKey($key)) { continue }
                $value = [string]$source.value
                if (-not (Test-ReactorPackageFeedUrl $value)) { continue }
                if (([Uri]$value).Host -eq 'packagefeedproxy.microsoft.io') {
                    return [pscustomobject]@{
                        ConfigPath = $config
                        Source = $value.Trim().TrimEnd('/')
                        SourceKey = $key
                        Explicit = $false
                    }
                }
            }
        } catch {
            continue
        }
    }

    return $null
}

# Feed selection for scripts a developer runs directly (the VSIX build), where
# bootstrap.ps1's probe-and-fall-back is neither available nor wanted. Explicit
# arguments win; otherwise we reuse the same user-configuration discovery
# bootstrap does, without the network probe — the probe asks whether the feed
# carries GitHub.Copilot.SDK, which is a bootstrap concern, not a VSIX one.
#
# Returns $null when nothing is configured, which is the public-contributor
# path: no restore override, repo nuget.config stays in effect.
function Resolve-ReactorNuGetFeedOverride {
    param(
        [string]$NuGetConfig,
        [string]$NuGetSource,
        [string]$AppData = $env:APPDATA,
        [string]$UserProfile = $env:USERPROFILE
    )

    # A config file is the more complete statement of intent than a bare source,
    # so it wins when both are supplied — matching Get-ReactorRestoreArguments.
    if (-not [string]::IsNullOrWhiteSpace($NuGetConfig)) {
        $explicit = Resolve-ReactorNuGetFeed -ExplicitConfig $NuGetConfig
        return [pscustomobject]@{
            ConfigPath = $explicit.ConfigPath
            Source     = $null
            Origin     = 'explicit config'
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($NuGetSource)) {
        if (-not (Test-ReactorPackageFeedUrl $NuGetSource)) {
            throw "-NuGetSource must be an HTTPS URL without credentials, query, or fragment (HTTP is allowed only for loopback): '$NuGetSource'"
        }
        return [pscustomobject]@{
            ConfigPath = $null
            Source     = $NuGetSource.Trim().TrimEnd('/')
            Origin     = 'explicit source'
        }
    }

    $detected = Resolve-ReactorNuGetFeed -AppData $AppData -UserProfile $UserProfile
    if (-not $detected) { return $null }

    return [pscustomobject]@{
        ConfigPath = $null
        Source     = $detected.Source
        Origin     = "user configuration ($($detected.SourceKey))"
    }
}

function Test-ReactorNuGetSourceAccess {
    param(
        [Parameter(Mandatory)][string]$Source,
        [scriptblock]$SearchRequest
    )

    if (-not (Test-ReactorPackageFeedUrl $Source)) { return $false }

    try {
        if ($SearchRequest) {
            $result = & $SearchRequest $Source
            $exitCode = [int]$result.ExitCode
            $output = [string]$result.Output
        } else {
            $output = & dotnet package search GitHub.Copilot.SDK `
                --source $Source --take 1 --format json 2>$null | Out-String
            $exitCode = $LASTEXITCODE
            $global:LASTEXITCODE = 0
        }
        return $exitCode -eq 0 -and $output -match '"id"\s*:\s*"GitHub\.Copilot\.SDK"'
    } catch {
        return $false
    }
}

function Get-ReactorRestoreArguments {
    param(
        [string]$NuGetConfig,
        [string]$NuGetSource,
        [string]$NpmRegistry
    )

    $arguments = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($NuGetConfig)) {
        $arguments.Add("-p:RestoreConfigFile=$NuGetConfig")
    } elseif (-not [string]::IsNullOrWhiteSpace($NuGetSource)) {
        $arguments.Add("-p:RestoreSources=$NuGetSource")
    }
    if (-not [string]::IsNullOrWhiteSpace($NpmRegistry)) {
        $arguments.Add("-p:CopilotNpmRegistryUrl=$NpmRegistry")
    }
    return @($arguments)
}

# Version stamp for the locally-packed `mur` global tool.
#
# Without an explicit -p:Version the SDK defaults to 1.0.0 on *every* commit, so
# re-running bootstrap packed a byte-new binary under a version NuGet had already
# seen. `dotnet tool update` compared 1.0.0 to 1.0.0, decided nothing was newer,
# no-opped, and exited 0 — leaving a months-stale `mur` on PATH while bootstrap
# reported success.
#
# The encoding has to clear three constraints at once, and the two obvious
# candidates each fail one:
#   * 1.<yyMMdd>.<HHmm> overflows AssemblyVersion/FileVersion, whose components
#     are capped at 65534 (UInt16) -> CS7034 at build time.
#   * 1.0.0-local.<stamp> is a prerelease of 1.0.0 and therefore sorts BELOW the
#     stable 1.0.0 already installed; `dotnet tool update` refuses to move to a
#     lower version even when pinned with --version.
# So: a stable version, strictly greater than 1.0.0, every component inside
# UInt16, and unique per run so a rapid re-pack is still installable.
#   major    1
#   minor    days since 2020-01-01 UTC   (~2400 today; UInt16 lasts ~179 years)
#   patch    minute of day               (0-1439)
#   revision second*1000 + millisecond   (0-59999)
#
# The revision folds milliseconds in rather than using the second alone: two
# runs started within the same second would otherwise collide, and a pinned
# `dotnet tool update --version <same>` reports "already installed" and keeps
# the older binary — the original failure mode. 59*1000+999 = 59999 still fits
# UInt16.
#
# -Now is injectable so the shape, bounds, and ordering can be tested without
# depending on the wall clock.
function Get-ReactorLocalCliVersion {
    param(
        [datetime]$Now = (Get-Date).ToUniversalTime()
    )

    $utc = $Now.ToUniversalTime()
    $days = [int]($utc.Date - [datetime]'2020-01-01').TotalDays
    if ($days -lt 1 -or $days -gt 65534) {
        throw "Local CLI version stamp is out of range: days-since-2020 = $days must be 1..65534 (system clock wrong?)."
    }

    # Floor, not [int]: PowerShell's [int] cast rounds half-to-even, so
    # 23:59:59.999 (TotalMinutes 1439.99998) would become 1440 — outside the
    # documented range, and every minute's last 30 seconds would be stamped
    # with the *next* minute.
    $minuteOfDay = [int][Math]::Floor($utc.TimeOfDay.TotalMinutes)

    return '1.{0}.{1}.{2}' -f $days, $minuteOfDay, ($utc.Second * 1000 + $utc.Millisecond)
}

function Get-ReactorToolArguments {
    param(
        [Parameter(Mandatory)][string]$Feed,
        [string]$NuGetConfig,
        [string]$NuGetSource,
        [string]$Version
    )

    $arguments = New-Object System.Collections.Generic.List[string]
    $arguments.Add('-g')
    $arguments.Add('--add-source')
    $arguments.Add($Feed)
    if (-not [string]::IsNullOrWhiteSpace($NuGetSource)) {
        $arguments.Add('--add-source')
        $arguments.Add($NuGetSource)
    }
    $arguments.Add('Microsoft.UI.Reactor.Cli')
    # Pin the exact version we just packed. Without this, `dotnet tool update`
    # resolves the highest version in the feed and compares it to what is
    # installed — and because the pack used to produce a constant `1.0.0` on
    # every commit, that comparison said "already up to date" and silently
    # no-opped with exit 0.
    #
    # Pinning does NOT make ordering irrelevant: `dotnet tool update --version`
    # still refuses to move to a *lower* version ("is lower than existing
    # version ..."), and treats an equal version as already-installed. It only
    # removes the "highest in feed" resolution step. Get-ReactorLocalCliVersion
    # is therefore responsible for producing a version that is both strictly
    # greater than 1.0.0 and unique per run.
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $arguments.Add('--version')
        $arguments.Add($Version)
    }
    $arguments.Add('--no-cache')
    $arguments.Add('--ignore-failed-sources')
    if (-not [string]::IsNullOrWhiteSpace($NuGetConfig)) {
        $arguments.Add('--configfile')
        $arguments.Add($NuGetConfig)
    }
    return @($arguments)
}

function Invoke-ReactorWithRestoreEnvironment {
    param(
        [string]$NuGetConfig,
        [string]$NuGetSource,
        [Parameter(Mandatory)][scriptblock]$Action,
        [Parameter(Mandatory)][ref]$ExitCode
    )

    $previousConfig = [Environment]::GetEnvironmentVariable('RestoreConfigFile', 'Process')
    $previousSources = [Environment]::GetEnvironmentVariable('RestoreSources', 'Process')
    try {
        if (-not [string]::IsNullOrWhiteSpace($NuGetConfig)) {
            $env:RestoreConfigFile = $NuGetConfig
            [Environment]::SetEnvironmentVariable('RestoreSources', $null, 'Process')
        } elseif (-not [string]::IsNullOrWhiteSpace($NuGetSource)) {
            [Environment]::SetEnvironmentVariable('RestoreConfigFile', $null, 'Process')
            $env:RestoreSources = $NuGetSource
        }
        & $Action
        $ExitCode.Value = $LASTEXITCODE
    } finally {
        [Environment]::SetEnvironmentVariable('RestoreConfigFile', $previousConfig, 'Process')
        [Environment]::SetEnvironmentVariable('RestoreSources', $previousSources, 'Process')
    }
}
