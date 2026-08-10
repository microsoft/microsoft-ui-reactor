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

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
. (Join-Path $repoRoot 'tools\BootstrapFeedResolver.ps1')

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("bootstrap-feeds-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$originalRegistry = $env:NPM_CONFIG_REGISTRY
$originalUserConfig = $env:NPM_CONFIG_USERCONFIG

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
    Assert-Equal $false $nuget.Explicit 'automatic NuGet selection is marked non-explicit'

    $generated = New-ReactorBootstrapNuGetConfig -Source $nuget.Source -BasePath $tmp
    Assert-True (Test-Path -LiteralPath $generated) 'bootstrap writes a temporary internal-only NuGet config'
    [xml]$generatedXml = Get-Content -LiteralPath $generated -Raw
    $generatedSources = @($generatedXml.SelectNodes('//packageSources/add'))
    Assert-Equal 1 $generatedSources.Count 'generated NuGet config contains exactly one package source'
    Assert-Equal $nuget.Source ([string]$generatedSources[0].value) `
        'generated NuGet config contains the selected proxy'

    $explicitConfig = Join-Path $tmp 'explicit.config'
    Set-Content $explicitConfig '<configuration />'
    $explicitNuget = Resolve-ReactorNuGetFeed -ExplicitConfig $explicitConfig
    Assert-Equal (Resolve-Path $explicitConfig).Path $explicitNuget.ConfigPath `
        'explicit NuGet config resolves to an absolute path'
    Assert-Equal $true $explicitNuget.Explicit 'explicit NuGet selection is marked explicit'
}
finally {
    $env:NPM_CONFIG_REGISTRY = $originalRegistry
    $env:NPM_CONFIG_USERCONFIG = $originalUserConfig
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
