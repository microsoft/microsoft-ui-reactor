<#
.SYNOPSIS
    Headless tests for the repo's dependency on GitHub.Copilot.SDK's MSBuild
    contract: the reviewed SDK version, and the CI-only CopilotSkipCliDownload
    default that Directory.Build.props derives from $(CI).

.DESCRIPTION
    Evaluates Directory.Build.props from a bare non-SDK project in a temp directory
    outside the repo and asserts the resolved $(CopilotSkipCliDownload). No network
    access, package restore, or credentials required.

    This suite is the only coverage the contract gets. The ordinary CI matrix runs
    with the download already skipped, so it would stay green if a future SDK
    renamed or dropped CopilotSkipCliDownload (and started downloading the native
    CLI in every CI build), or if the $(CI) block in Directory.Build.props were
    deleted. GitHub.Copilot.SDK 1.0.14 dropping CopilotNpmRegistryUrl is the
    precedent: the version pin below is what caught it.
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

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$buildProps = Join-Path $repoRoot 'Directory.Build.props'
$packagesProps = Join-Path $repoRoot 'Directory.Packages.props'

# Re-reviewing on every SDK bump is the point: reading the SDK's own targets here
# would need a package restore and a reachable feed. On failure, diff
# build/GitHub.Copilot.SDK.targets between the old and new SDK tags
# (gh api repos/github/copilot-sdk/compare/v<old>...v<new>) and confirm that
# CopilotSkipCliDownload still gates the native CLI download, and that
# CopilotCliReleaseBaseUrl / COPILOT_CLI_DOWNLOAD_BASE_URL / CopilotCliBinaryPath
# still behave as Directory.Build.props and README.md describe. Then bump this.
$reviewedSdkVersion = '1.0.14'
$sdkPinned = [regex]::Match(
    [IO.File]::ReadAllText($packagesProps),
    '<PackageVersion\s+Include="GitHub\.Copilot\.SDK"\s+Version="(?<v>[^"]+)"').Groups['v'].Value
Assert-Equal $reviewedSdkVersion $sdkPinned `
    'the GitHub.Copilot.SDK version whose MSBuild contract was reviewed is still the pinned one'

# Deliberately outside the repo, so the probe imports Directory.Build.props exactly
# once rather than also inheriting it implicitly.
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("copilot-sdk-contract-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$probe = Join-Path $tmp 'probe.proj'
Set-Content -LiteralPath $probe -Encoding UTF8 -Value @"
<Project DefaultTargets="Echo">
  <Import Project="$buildProps" />
  <Target Name="Echo">
    <Message Importance="high" Text="SKIP=[`$(CopilotSkipCliDownload)]" />
  </Target>
</Project>
"@

# The probe inherits this process's environment and the CI runner exports CI=true,
# so clear the inputs once and let each case opt back in.
$scrubbed = @('CI', 'CopilotSkipCliDownload')
$saved = @{}
foreach ($name in $scrubbed) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

function Invoke-Probe {
    param(
        [hashtable]$Environment = @{},
        [string[]]$Properties = @()
    )

    foreach ($name in $scrubbed) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    foreach ($key in $Environment.Keys) { [Environment]::SetEnvironmentVariable($key, $Environment[$key], 'Process') }

    $output = & dotnet msbuild $probe -nologo -v:m @Properties 2>&1 | Out-String
    $rc = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if ($rc -ne 0) { throw "dotnet msbuild exited $rc`n$output" }

    $match = [regex]::Match($output, 'SKIP=\[(?<v>[^\]]*)\]')
    if (-not $match.Success) { throw "Probe produced no SKIP marker:`n$output" }
    return $match.Groups['v'].Value
}

try {
    Assert-Equal 'true' (Invoke-Probe -Environment @{ CI = 'true' }) `
        'CI=true from the environment (GitHub Actions) skips the native CLI download'
    Assert-Equal 'true' (Invoke-Probe -Properties @('-p:CI=true')) `
        'CI=true as a global property (OneBranch reactor-build-steps.yml) skips the native CLI download'
    Assert-Equal '' (Invoke-Probe) `
        'a local build without CI leaves the SDK download on, so mur loc translate and the samples get their CLI'
    Assert-Equal '' (Invoke-Probe -Environment @{ CI = 'false' }) `
        'CI=false does not skip the native CLI download'
    Assert-Equal 'false' (Invoke-Probe -Environment @{ CI = 'true' } -Properties @('-p:CopilotSkipCliDownload=false')) `
        'an explicit CopilotSkipCliDownload=false opts a CI build back into the download'
}
finally {
    foreach ($name in $scrubbed) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Copilot SDK contract: $script:Pass passed, $script:Fail failed"
if ($script:Fail -gt 0) {
    foreach ($failure in $script:Failures) { Write-Host "  [FAIL] $failure" -ForegroundColor Red }
    exit 1
}
