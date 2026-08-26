<#
.SYNOPSIS
    Maps the Windows App SDK version this repo pins to the machine-wide Windows
    App Runtime that can actually load it.

.DESCRIPTION
    Dot-source this from anything that needs the runtime identity. It is the
    single source of the mapping rule, which is easy to get wrong in two ways:

      1.x  shipped a side-by-side framework package per MINOR, so the winget ids
           are Microsoft.WindowsAppRuntime.1.6, .1.7, .1.8 and an app built
           against 1.7 genuinely needs the 1.7 runtime.
      2.x  ships ONE framework package for the whole MAJOR -- the SDK's own
           WindowsAppSDK-VersionInfo.json names Microsoft.WindowsAppRuntime.2 as
           the framework family for 2.1.3 -- serviced 2.0 -> 2.1 -> 2.3 in place.
           The major-only winget id tracks that servicing. The major.minor ids
           are SEPARATE winget packages pinned to a single servicing line:
           Microsoft.WindowsAppRuntime.2.0 is still 2.0.1 and cannot satisfy an
           app built against 2.1.3, and Microsoft.WindowsAppRuntime.2.1 does not
           exist at all.

    Because the 2.x id is major-wide, its presence alone does NOT prove the
    installed runtime is new enough -- 2.0.1 and 2.3.1 are both "installed" under
    Microsoft.WindowsAppRuntime.2. Callers must compare versions, which is what
    Test-WindowsAppRuntimeSatisfied does.

    Consumers: bootstrap.ps1, .github/workflows/bootstrap.yml, and
    tests/Reactor.Tests/WinAppSDKReferenceGuardTests.cs. Keeping all three on
    this one file is deliberate -- an earlier revision inlined the rule in each
    and they drifted.

.NOTES
    Must stay loadable by Windows PowerShell 5.1: no PowerShell 6+ syntax, and
    no non-ASCII inside string literals (5.1 decodes these BOM-less UTF-8 files
    as CP1252, where a smart quote closes the literal early).
#>

function Get-PinnedWindowsAppSdkVersion {
    <#
    .SYNOPSIS
        Reads <WindowsAppSDKVersion> out of Directory.Build.props.
    .DESCRIPTION
        Parsed as XML rather than scraped with a regex so a commented-out
        definition cannot be mistaken for the live one, and so reformatting the
        element does not break the read.

        Prefers the last definition that carries no Condition -- on itself or on
        any ancestor, since Condition is usually written on the enclosing
        PropertyGroup rather than the property -- and falls back to the last one
        seen. MSBuild's last-write-wins applies only among assignments whose
        Condition evaluates true, and nothing here can evaluate a Condition, so
        an unconditional definition is the only one this can be sure takes
        effect.

        bootstrap.ps1 runs before the .NET SDK is guaranteed to be present, so
        evaluating the property with `dotnet msbuild -getProperty:` is not an
        option here.
    #>
    param([Parameter(Mandatory)][string]$PropsPath)

    if (-not (Test-Path -LiteralPath $PropsPath -PathType Leaf)) { return $null }

    try {
        $xml = New-Object System.Xml.XmlDocument
        $xml.Load($PropsPath)
        $nodes = @($xml.SelectNodes('//*[local-name()="WindowsAppSDKVersion"]'))
        if ($nodes.Count -gt 0) {
            $unconditional = @($nodes | Where-Object {
                $ancestor = $_
                $conditional = $false
                while ($null -ne $ancestor -and $ancestor.NodeType -eq [System.Xml.XmlNodeType]::Element) {
                    if ($ancestor.GetAttribute('Condition')) { $conditional = $true; break }
                    $ancestor = $ancestor.ParentNode
                }
                -not $conditional
            })
            $chosen = if ($unconditional.Count -gt 0) { $unconditional[-1] } else { $nodes[-1] }
            $value = ([string]$chosen.InnerText).Trim()
            # An unexpanded $(...) reference cannot be resolved without a full
            # MSBuild evaluation; report failure rather than a bogus version.
            if ($value -and $value -notmatch '\$\(') { return $value }
        }
    } catch {
        # Fall through to the text scan below; a malformed props file is a
        # bigger problem than this function, but it should not be the thing
        # that reports it.
    }

    $text = [System.IO.File]::ReadAllText($PropsPath, [System.Text.Encoding]::UTF8)
    if ($text -match '<WindowsAppSDKVersion>\s*([^<\s$]+)\s*</WindowsAppSDKVersion>') { return $Matches[1] }
    return $null
}

function Get-WindowsAppRuntimeWingetId {
    <#
    .SYNOPSIS
        Maps an SDK version to the winget package id of its framework runtime.
    #>
    param([string]$SdkVersion)

    if ([string]::IsNullOrWhiteSpace($SdkVersion)) { return $null }
    if ($SdkVersion -notmatch '^(\d+)\.(\d+)') { return $null }

    $major = [int]$Matches[1]
    $minor = [int]$Matches[2]
    if ($major -ge 2) { return "Microsoft.WindowsAppRuntime.$major" }
    return "Microsoft.WindowsAppRuntime.$major.$minor"
}

function Get-WindowsAppRuntimeVersionFromWingetOutput {
    <#
    .SYNOPSIS
        Pulls the highest dotted version out of `winget list` output.
    .DESCRIPTION
        Separated from the winget call so it can be tested without winget. Only
        3- and 4-part versions are considered, which is what keeps the package
        id itself (Microsoft.WindowsAppRuntime.2, or .1.7 for the 1.x line) from
        being read as a version. Several rows can match when more than one
        servicing version is present; the highest is the one that will load.
    #>
    param([string]$Output)

    if ([string]::IsNullOrWhiteSpace($Output)) { return $null }

    $best = $null
    foreach ($match in [regex]::Matches($Output, '(?<![\d.])(\d+\.\d+\.\d+(?:\.\d+)?)(?![\d.])')) {
        $candidate = $null
        if ([Version]::TryParse($match.Groups[1].Value, [ref]$candidate)) {
            if ($null -eq $best -or $candidate -gt $best) { $best = $candidate }
        }
    }
    return $best
}

function Test-WindowsAppRuntimeSatisfied {
    <#
    .SYNOPSIS
        True when an installed runtime version can load apps built against the
        pinned SDK version.
    .DESCRIPTION
        Normalizes part counts before comparing: [Version]'2.1.3' has
        Revision -1 and sorts BELOW '2.1.3.0', which would report a perfectly
        good runtime as too old.
    #>
    param(
        [Parameter(Mandatory)][AllowNull()][System.Version]$Installed,
        [Parameter(Mandatory)][AllowEmptyString()][string]$RequiredSdkVersion
    )

    if ($null -eq $Installed) { return $false }

    $required = $null
    if (-not [Version]::TryParse(($RequiredSdkVersion -replace '[-+].*$', ''), [ref]$required)) {
        # Nothing to compare against; do not claim the runtime is too old.
        return $true
    }

    $normalize = {
        param($v)
        New-Object System.Version(
            [Math]::Max($v.Major, 0),
            [Math]::Max($v.Minor, 0),
            [Math]::Max($v.Build, 0),
            [Math]::Max($v.Revision, 0))
    }
    return ((& $normalize $Installed) -ge (& $normalize $required))
}
