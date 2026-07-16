<#
.SYNOPSIS
    Dependency-free tests for .github/scripts/close-stale-nuget-issue.ps1 — the
    tracking-issue reconciler used by the windows-nuget-updates workflow.

.DESCRIPTION
    Runs headless with no build and no dotnet (wired into
    .github/workflows/nuget-updates-lib-tests.yml). The script shells out to `gh`,
    so these tests put a fake `gh` on PATH that records its invocations to a log and
    returns canned `gh issue list` JSON. The real script is then run as a subprocess
    and the recorded `gh` calls are asserted — every assertion fails if the script's
    find-or-close behavior is removed or broken.

    Run locally:  pwsh tests/nuget_updates/ci/CloseStaleNuGetIssue.Tests.ps1
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
function Assert-Match {
    param([string]$Haystack, [string]$Needle, [string]$Message)
    if ($Haystack.Contains($Needle)) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    missing substring: [$Needle]") }
}
function Assert-NotMatch {
    param([string]$Haystack, [string]$Needle, [string]$Message)
    if (-not $Haystack.Contains($Needle)) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    unexpected substring: [$Needle]") }
}

$scriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..' '.github' 'scripts' 'close-stale-nuget-issue.ps1')).Path
$noBom = [System.Text.UTF8Encoding]::new($false)

# ── 1. Parse-check the script. ──
$src = Get-Content $scriptPath -Raw
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseInput($src, [ref]$null, [ref]$parseErrors) | Out-Null
if ($parseErrors -and $parseErrors.Count -gt 0) {
    Write-Host "close-stale-nuget-issue.ps1 has $($parseErrors.Count) parse error(s):" -ForegroundColor Red
    $parseErrors | ForEach-Object { Write-Host "  line $($_.Extent.StartLineNumber): $($_.Message)" -ForegroundColor Red }
    exit 1
}

# ── 2. End-to-end: run the script with a stubbed `gh` and inspect its calls. ──
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("close-issue-test-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$shimDir = Join-Path $tmp 'bin'
New-Item -ItemType Directory -Path $shimDir | Out-Null

# The stub logs every `gh` invocation and, for `issue list`, echoes $GH_STUB_LIST_JSON.
$impl = @'
$log = $env:GH_STUB_LOG
if ($log) { Add-Content -LiteralPath $log -Value ($args -join ' ') }
if ($args -contains 'close') { exit 0 }
if ($args -contains 'list') {
    $json = $env:GH_STUB_LIST_JSON
    if ([string]::IsNullOrEmpty($json)) { $json = '[]' }
    Write-Output $json
    exit 0
}
exit 0
'@
[System.IO.File]::WriteAllText((Join-Path $shimDir 'gh-impl.ps1'), $impl, $noBom)
# Windows resolves `gh` -> gh.cmd (via PATHEXT); Linux/macOS -> the extensionless file.
[System.IO.File]::WriteAllText((Join-Path $shimDir 'gh.cmd'), "@echo off`r`npwsh -NoProfile -File `"%~dp0gh-impl.ps1`" %*`r`n", $noBom)
$nix = "#!/usr/bin/env bash`nexec pwsh -NoProfile -File `"`$(dirname `"`$0`")/gh-impl.ps1`" `"`$@`"`n"
[System.IO.File]::WriteAllText((Join-Path $shimDir 'gh'), $nix, $noBom)
if ($IsLinux -or $IsMacOS) { & chmod +x (Join-Path $shimDir 'gh') }

$sep = [System.IO.Path]::PathSeparator
$origPath = $env:PATH
$matchTitle = 'Windows NuGet updates ready to open'

function Invoke-CloseScript {
    param([string]$ListJson, [string[]]$ExtraArgs = @())
    $log = Join-Path $tmp ("gh-" + [Guid]::NewGuid().ToString('N') + ".log")
    $env:PATH = $shimDir + $sep + $origPath
    $env:GH_STUB_LOG = $log
    $env:GH_STUB_LIST_JSON = $ListJson
    $stdout = & pwsh -NoProfile -File $scriptPath @ExtraArgs 2>&1 | Out-String
    $exit = $LASTEXITCODE
    $env:PATH = $origPath
    Remove-Item Env:\GH_STUB_LOG, Env:\GH_STUB_LIST_JSON -ErrorAction SilentlyContinue
    $calls = if (Test-Path $log) { Get-Content $log -Raw } else { '' }
    [pscustomobject]@{ Exit = $exit; Stdout = $stdout; Calls = $calls }
}

try {
    # (a) A matching open issue is closed by number.
    $r = Invoke-CloseScript -ListJson '[{"number":884,"title":"Windows NuGet updates ready to open"}]'
    Assert-Equal 0 $r.Exit 'match: script exits 0'
    Assert-Match $r.Calls 'issue list' 'match: queries open issues'
    Assert-Match $r.Calls 'issue close 884' 'match: closes issue #884 by number'
    Assert-Match $r.Calls 'Closed automatically' 'match: leaves an auto-close comment'

    # (b) No open issue -> no close call, and a no-op message is printed.
    $r = Invoke-CloseScript -ListJson '[]'
    Assert-Equal 0 $r.Exit 'empty: script exits 0'
    Assert-NotMatch $r.Calls 'issue close' 'empty: does not close anything'
    Assert-Match $r.Stdout 'No open Windows NuGet tracking issue' 'empty: prints the no-op message'

    # (c) An unrelated open issue is left alone (exact-title match, not substring).
    $r = Invoke-CloseScript -ListJson '[{"number":42,"title":"Some other issue"}]'
    Assert-NotMatch $r.Calls 'issue close' 'unrelated: title mismatch is not closed'

    # (d) Multiple matching issues are all closed (defensive de-dup).
    $r = Invoke-CloseScript -ListJson '[{"number":10,"title":"Windows NuGet updates ready to open"},{"number":11,"title":"Windows NuGet updates ready to open"}]'
    Assert-Match $r.Calls 'issue close 10' 'multi: closes #10'
    Assert-Match $r.Calls 'issue close 11' 'multi: closes #11'

    # (e) The -Reason argument flows into the auto-close comment.
    $reason = 'this bump is now tracked by an open pull request.'
    $r = Invoke-CloseScript -ListJson '[{"number":884,"title":"Windows NuGet updates ready to open"}]' -ExtraArgs @('-Reason', $reason)
    Assert-Match $r.Calls $reason '-Reason: custom reason appears in the close comment'
}
finally {
    $env:PATH = $origPath
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# ── Report ──
Write-Host ""
Write-Host "close-stale-nuget-issue tests: $script:Pass passed, $script:Fail failed"
if ($script:Fail -gt 0) {
    Write-Host ""
    $script:Failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    exit 1
}
exit 0
