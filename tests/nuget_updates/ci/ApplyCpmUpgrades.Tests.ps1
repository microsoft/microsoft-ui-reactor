<#
.SYNOPSIS
    Dependency-free unit + integration tests for
    .github/scripts/apply-cpm-upgrades.ps1 — the surgical Central-Package-Management
    version editor used by the windows-nuget-updates workflow.

.DESCRIPTION
    Runs headless with no build, no dotnet, and no external test framework, so it is
    safe on any runner (wired into .github/workflows/nuget-updates-lib-tests.yml on
    changes under .github/scripts/apply-cpm-upgrades.ps1 or tests/nuget_updates/ci/**).
    Exits non-zero if any assertion fails.

    Two layers:
      1. Test-NuGetVersionGreater, extracted via the PowerShell AST (the script has a
         param block + runs on load, so it isn't dot-sourceable), covering SemVer
         precedence incl. numeric prerelease identifiers and the 4-segment fallback.
      2. End-to-end: invoke the script as a subprocess against a synthetic
         outdated.json + a fixture Directory.Packages.props and assert the surgical
         rewrite — attribute preserved, blank-line/formatting/no-BOM/trailing-newline
         preserved, $()-property-pin skip, ignore-list, and highest-version dedup.

    Run locally:  pwsh tests/nuget_updates/ci/ApplyCpmUpgrades.Tests.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Pass = 0
$script:Fail = 0
$script:Failures = [System.Collections.Generic.List[string]]::new()

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -eq $Actual) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    expected: [$Expected]`n    actual:   [$Actual]") }
}
function Assert-True {
    param([bool]$Condition, [string]$Message)
    if ($Condition) { $script:Pass++ } else { $script:Fail++; $script:Failures.Add($Message) }
}
function Assert-False {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { $script:Pass++ } else { $script:Fail++; $script:Failures.Add($Message) }
}
function Assert-Match {
    param([string]$Haystack, [string]$Needle, [string]$Message)
    # Ordinal substring test (not -like, whose wildcards would misfire on '[', '$(' etc.).
    if ($Haystack.Contains($Needle)) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    missing substring: [$Needle]") }
}
function Assert-NotMatch {
    param([string]$Haystack, [string]$Needle, [string]$Message)
    if (-not $Haystack.Contains($Needle)) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    unexpected substring: [$Needle]") }
}

$scriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..' '.github' 'scripts' 'apply-cpm-upgrades.ps1')).Path

# ── 1. Parse-check the script + extract Test-NuGetVersionGreater via AST. ──
# The script isn't dot-sourceable (param block + runs on load), so lift the pure
# comparison function out by AST and define it in isolation.
$src = Get-Content $scriptPath -Raw
$parseTokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($src, [ref]$parseTokens, [ref]$parseErrors)
if ($parseErrors -and $parseErrors.Count -gt 0) {
    Write-Host "apply-cpm-upgrades.ps1 has $($parseErrors.Count) parse error(s):" -ForegroundColor Red
    $parseErrors | ForEach-Object { Write-Host "  line $($_.Extent.StartLineNumber): $($_.Message)" -ForegroundColor Red }
    exit 1
}
$funcAst = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Test-NuGetVersionGreater' }, $true) |
    Select-Object -First 1
if (-not $funcAst) { Write-Host 'Test-NuGetVersionGreater not found in apply-cpm-upgrades.ps1' -ForegroundColor Red; exit 1 }
. ([ScriptBlock]::Create($funcAst.Extent.Text))

# ── Test-NuGetVersionGreater: SemVer 2.0 precedence ──
Assert-True  (Test-NuGetVersionGreater '1.0.6' '1.0.1')   'patch: 1.0.6 > 1.0.1'
Assert-False (Test-NuGetVersionGreater '1.0.1' '1.0.6')   'patch: 1.0.1 !> 1.0.6'
Assert-True  (Test-NuGetVersionGreater '18.1.0' '18.0.0') 'minor: 18.1.0 > 18.0.0'
Assert-False (Test-NuGetVersionGreater '1.0.6' '1.0.6')   'equal: 1.0.6 !> 1.0.6'
Assert-True  (Test-NuGetVersionGreater '1.0.6' '1.0.6-preview.1') 'stable > its own prerelease'
Assert-False (Test-NuGetVersionGreater '1.0.6-preview.1' '1.0.6') 'prerelease !> stable'
Assert-True  (Test-NuGetVersionGreater '1.0.0-preview.10' '1.0.0-preview.9') 'numeric prerelease id: preview.10 > preview.9'
Assert-True  (Test-NuGetVersionGreater '11.0.0-preview.6.26302.115' '11.0.0-preview.5.26302.115') 'NuGet dotted prerelease: preview.6 > preview.5'
# 3/4-part release versions (4-segment aren't SemVer-parseable → numeric fallback).
Assert-True  (Test-NuGetVersionGreater '17.14.40265' '17.14.40260') '3-part: 17.14.40265 > 17.14.40260'
Assert-True  (Test-NuGetVersionGreater '1.2.3.5' '1.2.3.4') '4-segment fallback: 1.2.3.5 > 1.2.3.4'
Assert-False (Test-NuGetVersionGreater '1.2.3.4' '1.2.3.5') '4-segment fallback: 1.2.3.4 !> 1.2.3.5'

# ── 2. End-to-end: run the script as a subprocess against fixtures. ──
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("cpm-test-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$noBom = [System.Text.UTF8Encoding]::new($false)
try {
    $propsPath  = Join-Path $tmp 'Directory.Packages.props'
    $reportPath = Join-Path $tmp 'outdated.json'
    $outPath    = Join-Path $tmp 'ghoutput.txt'

    # Fixture props: comments, blank lines between/around groups, a $() property pin,
    # and a trailing newline — UTF-8 without BOM (mirrors the real file).
    $propsLines = @(
        '<Project>'
        ''
        '  <ItemGroup>'
        '    <!-- pinned via a build property; must be left alone -->'
        '    <PackageVersion Include="Microsoft.Graphics.Win2D" Version="$(Win2DVersion)" />'
        '    <PackageVersion Include="GitHub.Copilot.SDK" Version="1.0.1" />'
        '    <PackageVersion Include="YamlDotNet" Version="18.0.0" />'
        '    <PackageVersion Include="MessagePack" Version="2.5.301" />'
        '    <PackageVersion Include="Microsoft.Data.Sqlite" Version="11.0.0-preview.5.26302.115" />'
        '    <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />'
        '    <PackageVersion Include="Serilog.Sinks.Console" Version="6.0.0" />'
        '  </ItemGroup>'
        ''
        '</Project>'
        ''
    )
    [System.IO.File]::WriteAllText($propsPath, ($propsLines -join "`n"), $noBom)

    # Synthetic report. Microsoft.Data.Sqlite appears in two TFMs with DIFFERENT
    # LatestVersion to exercise highest-version dedup + prerelease comparison.
    $report = @'
{ "Projects": [
  { "Name": "A", "TargetFrameworks": [ { "Name": "net10.0", "Dependencies": [
      { "Name": "GitHub.Copilot.SDK",        "ResolvedVersion": "1.0.1",                       "LatestVersion": "1.0.6" },
      { "Name": "YamlDotNet",                "ResolvedVersion": "18.0.0",                      "LatestVersion": "18.1.0" },
      { "Name": "MessagePack",               "ResolvedVersion": "2.5.301",                     "LatestVersion": "3.1.8" },
      { "Name": "Microsoft.Graphics.Win2D",  "ResolvedVersion": "1.4.0",                       "LatestVersion": "1.5.0" },
      { "Name": "Microsoft.Data.Sqlite",     "ResolvedVersion": "11.0.0-preview.5.26302.115",  "LatestVersion": "11.0.0-preview.5.26302.115" },
      { "Name": "Newtonsoft.Json",           "ResolvedVersion": "13.0.3",                      "LatestVersion": "13.0.1" },
      { "Name": "serilog.sinks.console",     "ResolvedVersion": "6.0.0",                       "LatestVersion": "6.0.1" }
  ] } ] },
  { "Name": "B", "TargetFrameworks": [ { "Name": "net10.0", "Dependencies": [
      { "Name": "Microsoft.Data.Sqlite",     "ResolvedVersion": "11.0.0-preview.5.26302.115",  "LatestVersion": "11.0.0-preview.6.26302.115" }
  ] } ] }
] }
'@
    [System.IO.File]::WriteAllText($reportPath, $report, $noBom)

    & pwsh -NoProfile -File $scriptPath -ReportPath $reportPath -PropsPath $propsPath -IgnorePackages 'MessagePack' -GitHubOutput $outPath | Out-Null
    Assert-Equal 0 $LASTEXITCODE 'script exits 0'

    $after = Get-Content $propsPath -Raw

    # Applied upgrades.
    Assert-Match $after '<PackageVersion Include="GitHub.Copilot.SDK" Version="1.0.6" />' 'Copilot.SDK bumped to 1.0.6'
    Assert-Match $after '<PackageVersion Include="YamlDotNet" Version="18.1.0" />'        'YamlDotNet bumped to 18.1.0'
    # Highest-version dedup across the two TFMs picks the higher prerelease.
    Assert-Match $after '<PackageVersion Include="Microsoft.Data.Sqlite" Version="11.0.0-preview.6.26302.115" />' 'Sqlite dedup picks preview.6'
    # Case-insensitive id match: report says "serilog.sinks.console", props says
    # "Serilog.Sinks.Console" — still upgraded, original Include casing preserved.
    Assert-Match $after '<PackageVersion Include="Serilog.Sinks.Console" Version="6.0.1" />' 'case-insensitive id match upgrades and preserves Include casing'
    # Ignore list + property-pin backstop leave these untouched.
    Assert-Match $after '<PackageVersion Include="MessagePack" Version="2.5.301" />' 'MessagePack ignored (unchanged)'
    Assert-Match $after '<PackageVersion Include="Microsoft.Graphics.Win2D" Version="$(Win2DVersion)" />' 'Win2D property pin skipped'
    # Downgrade guard: report's LatestVersion (13.0.1) is LOWER than the pin (13.0.3) — leave it.
    Assert-Match $after '<PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />' 'downgrade guard: higher pin left untouched'
    Assert-NotMatch $after '13.0.1' 'downgrade target 13.0.1 never written'
    Assert-NotMatch $after '1.5.0' 'Win2D literal 1.5.0 never written'
    Assert-NotMatch $after '3.1.8' 'MessagePack 3.1.8 never written'

    # Formatting preserved: the blank line between </ItemGroup> and </Project> stays,
    # no BOM, and the file still ends with a newline.
    Assert-Match $after "</ItemGroup>`n`n</Project>" 'blank-line formatting preserved'
    $afterBytes = [System.IO.File]::ReadAllBytes($propsPath)
    $hasBom = ($afterBytes.Length -ge 3 -and $afterBytes[0] -eq 0xEF -and $afterBytes[1] -eq 0xBB -and $afterBytes[2] -eq 0xBF)
    Assert-False $hasBom 'no UTF-8 BOM introduced'
    Assert-Equal 0x0A $afterBytes[-1] 'file still ends with a newline'

    # Step outputs, and the decoded summary.
    $ghout = Get-Content $outPath -Raw
    Assert-Match $ghout 'changed=true' 'changed=true emitted'
    Assert-Match $ghout 'summary_b64=' 'summary_b64 emitted'
    $b64 = ([regex]::Match($ghout, 'summary_b64=(\S+)')).Groups[1].Value
    $summaryText = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
    Assert-Match $summaryText 'GitHub.Copilot.SDK' 'summary lists an applied package'
    Assert-Match $summaryText 'Ignored by configuration: MessagePack' 'summary notes MessagePack ignored'
    Assert-Match $summaryText 'Skipped (MSBuild-property pins)' 'summary notes the property-pin skip'
    # Summary uses the file's Include casing (Serilog.Sinks.Console), not the report's.
    Assert-Match $summaryText 'Serilog.Sinks.Console' 'summary uses the file Include casing'
    Assert-NotMatch $summaryText 'serilog.sinks.console' 'summary does not echo the report casing'

    # Ignored-list ordering is deterministic (sorted) regardless of HashSet order.
    $ordProps = Join-Path $tmp 'ord.props'
    [System.IO.File]::WriteAllText($ordProps, ($propsLines -join "`n"), $noBom)
    $ordOut = Join-Path $tmp 'ord-out.txt'
    & pwsh -NoProfile -File $scriptPath -ReportPath $reportPath -PropsPath $ordProps -IgnorePackages 'Zzebra,Aardvark,Mango' -GitHubOutput $ordOut | Out-Null
    $ordSummary = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(([regex]::Match((Get-Content $ordOut -Raw), 'summary_b64=(\S+)')).Groups[1].Value))
    Assert-Match $ordSummary 'Ignored by configuration: Aardvark, Mango, Zzebra' 'ignored list is sorted deterministically'

    # No-op run: a report whose LatestVersion == the current pin leaves the file byte-identical.
    $noopReport = Join-Path $tmp 'noop.json'
    [System.IO.File]::WriteAllText($noopReport, '{ "Projects": [ { "Name": "A", "TargetFrameworks": [ { "Name": "net10.0", "Dependencies": [ { "Name": "GitHub.Copilot.SDK", "ResolvedVersion": "1.0.1", "LatestVersion": "1.0.1" } ] } ] } ] }', $noBom)
    $noopProps = Join-Path $tmp 'noop.props'
    [System.IO.File]::WriteAllText($noopProps, ($propsLines -join "`n"), $noBom)
    $noopOut = Join-Path $tmp 'noop-out.txt'
    $noopBefore = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($noopProps))
    & pwsh -NoProfile -File $scriptPath -ReportPath $noopReport -PropsPath $noopProps -GitHubOutput $noopOut | Out-Null
    $noopAfter = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($noopProps))
    Assert-Equal $noopBefore $noopAfter 'no-op leaves the props file byte-identical'
    Assert-Match (Get-Content $noopOut -Raw) 'changed=false' 'no-op emits changed=false'
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# ── Report ──
Write-Host ""
Write-Host "apply-cpm-upgrades tests: $script:Pass passed, $script:Fail failed"
if ($script:Fail -gt 0) {
    Write-Host ""
    $script:Failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    exit 1
}
exit 0
