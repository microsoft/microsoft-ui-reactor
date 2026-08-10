function Test-ReactorHttpUrl {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }

    $uri = $null
    return [Uri]::TryCreate($Value.Trim(), [UriKind]::Absolute, [ref]$uri) -and
        ($uri.Scheme -eq 'http' -or $uri.Scheme -eq 'https')
}

function Resolve-ReactorNpmRegistry {
    param(
        [string]$ExplicitRegistry,
        [string]$UserProfile = $env:USERPROFILE
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitRegistry)) {
        if (-not (Test-ReactorHttpUrl $ExplicitRegistry)) {
            throw "-NpmRegistry must be an absolute HTTP(S) URL: '$ExplicitRegistry'"
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

    if (-not (Test-ReactorHttpUrl $configuredRegistry)) { return $null }

    $uri = [Uri]$configuredRegistry
    if ($uri.Host -ne 'packagefeedproxy.microsoft.io') { return $null }

    return [pscustomobject]@{
        Registry = $configuredRegistry.Trim().TrimEnd('/')
        Explicit = $false
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
            foreach ($source in @($xml.SelectNodes('//packageSources/add'))) {
                $value = [string]$source.value
                if (-not (Test-ReactorHttpUrl $value)) { continue }
                if (([Uri]$value).Host -eq 'packagefeedproxy.microsoft.io') {
                    return [pscustomobject]@{
                        ConfigPath = $null
                        Source = $value.Trim().TrimEnd('/')
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

function New-ReactorBootstrapNuGetConfig {
    param(
        [Parameter(Mandatory)][string]$Source,
        [string]$BasePath = ([IO.Path]::GetTempPath())
    )

    if (-not (Test-ReactorHttpUrl $Source)) {
        throw "NuGet source must be an absolute HTTP(S) URL: '$Source'"
    }

    $directory = Join-Path $BasePath 'Microsoft.UI.Reactor'
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $path = Join-Path $directory 'bootstrap.nuget.config'

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $writer = [System.Xml.XmlWriter]::Create($path, $settings)
    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement('configuration')
        $writer.WriteStartElement('packageSources')
        $writer.WriteStartElement('clear')
        $writer.WriteEndElement()
        $writer.WriteStartElement('add')
        $writer.WriteAttributeString('key', 'configured-proxy')
        $writer.WriteAttributeString('value', $Source.Trim().TrimEnd('/'))
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    } finally {
        $writer.Dispose()
    }

    return $path
}
