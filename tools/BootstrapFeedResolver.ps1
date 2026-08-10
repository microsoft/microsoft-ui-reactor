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

function Get-ReactorToolArguments {
    param(
        [Parameter(Mandatory)][string]$Feed,
        [string]$NuGetConfig,
        [string]$NuGetSource
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
