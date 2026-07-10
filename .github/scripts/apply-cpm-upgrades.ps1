#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Applies NuGet version upgrades detected by `dotnet-outdated` to the central
  Directory.Packages.props, editing ONLY the Version="" attribute of each
  <PackageVersion> so comments, blank lines, and the trailing newline are
  preserved (unlike `dotnet-outdated --upgrade`, which reformats the file).

.DESCRIPTION
  Companion to .github/workflows/windows-nuget-updates.yml. That workflow exists
  because GitHub-hosted Dependabot only runs on Linux and cannot restore this
  repo's Windows-only project graph (WinUI / WindowsAppSDK apps, and even plain
  net10.0 projects like src/Reactor.Cli), so packages referenced only by those
  projects (e.g. GitHub.Copilot.SDK, YamlDotNet) never get a Dependabot PR. This
  script closes that gap on a windows-latest runner.

  Only literal versions are rewritten. Any <PackageVersion> whose Version is an
  MSBuild property expression (e.g. Version="$(WindowsAppSDKVersion)") is left
  untouched, so the Directory.Build.props-driven Windows App SDK / Win2D pins
  stay authoritative.

.PARAMETER ReportPath
  Path to the JSON report produced by `dotnet-outdated --output ... --output-format json`.

.PARAMETER PropsPath
  Path to Directory.Packages.props.

.PARAMETER IgnorePackages
  Package ids to skip (exact, case-insensitive). Accepts a comma/newline/semicolon
  separated string or an array.

.PARAMETER GitHubOutput
  Optional path to $GITHUB_OUTPUT. When set, writes `changed` (true/false) and a
  base64-encoded `summary_b64` step output (markdown, reused as the PR body).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ReportPath,
    [Parameter(Mandatory)] [string] $PropsPath,
    [string[]] $IgnorePackages = @(),
    [string] $GitHubOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ReportPath)) { throw "Report not found: $ReportPath" }
if (-not (Test-Path $PropsPath))  { throw "Props file not found: $PropsPath" }

# Normalize the ignore list (allow comma/newline/semicolon separated single strings).
$ignore = @()
foreach ($item in $IgnorePackages) {
    if ($null -ne $item) {
        $ignore += ($item -split '[,;\r\n]') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    }
}
$ignoreSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($p in $ignore) { [void]$ignoreSet.Add($p) }

# Collect Name -> highest LatestVersion across every project/target framework.
$report = Get-Content $ReportPath -Raw | ConvertFrom-Json
$wanted = @{}
foreach ($proj in @($report.Projects)) {
    foreach ($tf in @($proj.TargetFrameworks)) {
        foreach ($dep in @($tf.Dependencies)) {
            $name = $dep.Name
            $latest = $dep.LatestVersion
            if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($latest)) { continue }
            if ($ignoreSet.Contains($name)) { continue }
            if ($wanted.ContainsKey($name)) {
                # Keep the higher target if two projects disagree.
                try {
                    if ([version]($latest -replace '[^\d.].*$','') -gt [version]($wanted[$name] -replace '[^\d.].*$','')) { $wanted[$name] = $latest }
                } catch { $wanted[$name] = $latest }
            } else {
                $wanted[$name] = $latest
            }
        }
    }
}

$content = Get-Content $PropsPath -Raw
$applied = New-Object System.Collections.Generic.List[object]
$skippedProperty = New-Object System.Collections.Generic.List[string]

foreach ($name in ($wanted.Keys | Sort-Object)) {
    $to = $wanted[$name]
    $escaped = [regex]::Escape($name)
    # Match a self-closing <PackageVersion ...> tag that includes Include="NAME",
    # regardless of attribute order.
    $tagPattern = "<PackageVersion\b[^>]*\bInclude=`"$escaped`"[^>]*/>"
    $tagRegex = [regex]::new($tagPattern)
    $m = $tagRegex.Match($content)
    if (-not $m.Success) { continue }

    $tag = $m.Value
    $verMatch = [regex]::Match($tag, 'Version="([^"]*)"')
    if (-not $verMatch.Success) { continue }
    $current = $verMatch.Groups[1].Value

    if ($current -like '*$(*') { [void]$skippedProperty.Add("$name ($current)"); continue }
    if ($current -eq $to) { continue }

    $newTag = $tag -replace 'Version="[^"]*"', "Version=`"$to`""
    $content = $content.Substring(0, $m.Index) + $newTag + $content.Substring($m.Index + $m.Length)
    $applied.Add([pscustomobject]@{ Name = $name; From = $current; To = $to })
}

$changed = $applied.Count -gt 0
if ($changed) {
    # Preserve UTF-8 without BOM and the original bytes we did not touch.
    [System.IO.File]::WriteAllText((Resolve-Path $PropsPath), $content, [System.Text.UTF8Encoding]::new($false))
}

# Human-readable summary (also used as the PR body).
$sb = [System.Text.StringBuilder]::new()
if ($changed) {
    [void]$sb.AppendLine("Updated **$($applied.Count)** package version(s) in ``Directory.Packages.props``:")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| Package | From | To |')
    [void]$sb.AppendLine('| --- | --- | --- |')
    foreach ($u in $applied) { [void]$sb.AppendLine("| $($u.Name) | $($u.From) | $($u.To) |") }
} else {
    [void]$sb.AppendLine('No central package versions needed updating.')
}
if ($skippedProperty.Count -gt 0) {
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("_Skipped (MSBuild-property pins): $([string]::Join(', ', $skippedProperty))._")
}
if ($ignoreSet.Count -gt 0) {
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("_Ignored by configuration: $([string]::Join(', ', $ignoreSet))._")
}
$summary = $sb.ToString()
Write-Host $summary

if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $summary
}

if ($GitHubOutput) {
    $enc = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($summary))
    Add-Content -Path $GitHubOutput -Value "changed=$($changed.ToString().ToLowerInvariant())"
    Add-Content -Path $GitHubOutput -Value "summary_b64=$enc"
}

exit 0
