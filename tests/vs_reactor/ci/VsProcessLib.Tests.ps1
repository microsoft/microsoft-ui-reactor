<#
.SYNOPSIS
    Dependency-free tests for src/vs-reactor/VsProcessLib.ps1 — the process-tree
    timeout/kill/drain helpers behind `devenv /updateconfiguration`.

.DESCRIPTION
    Windows-only and headless: no build, no dotnet, no Visual Studio. Wired into
    .github/workflows/vs-reactor-lib-tests.yml. Exits non-zero on any failed
    assertion.

    The fake "devenv" is a copy of cmd.exe renamed to reactor-fake-devenv.exe,
    launched as `/c "<fake>" /c ping -n 600 127.0.0.1`. That produces a real
    two-level, same-named, genuinely-hanging process tree, which is what makes
    these assertions non-vacuous: mutation-checked against the shipped pre-fix
    implementation (`$proc.Kill()`, no argument, no wait) the inner
    reactor-fake-devenv.exe survives and the tree assertions redden.

    Known limit, stated rather than papered over: the "-425 years" duration in
    the issue is a race on reading Process.ExitTime before the OS reaps a heavy
    process. A cmd.exe fake dies too fast to reproduce it, so the duration
    assertion here is a bounds check on the symptom, not a discriminator
    between a Stopwatch and a correctly-sequenced ExitTime read. See case 5.

    A distinctly-named fake also keeps these tests safe: nothing here is ever
    pointed at `pwsh`, so an attribution regression cannot take out the test host.

    Run locally:  pwsh tests/vs_reactor/ci/VsProcessLib.Tests.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    Write-Host "VsProcessLib tests: skipped (Windows-only process semantics)."
    exit 0
}

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
    if ($Condition) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add($Message) }
}
function Assert-InRange {
    param([double]$Value, [double]$Min, [double]$Max, [string]$Message)
    if ($Value -ge $Min -and $Value -le $Max) { $script:Pass++ }
    else { $script:Fail++; $script:Failures.Add("$Message`n    expected: [$Min..$Max]`n    actual:   [$Value]") }
}

$vsReactor = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' '..' 'src' 'vs-reactor')).Path
$libPath = Join-Path $vsReactor 'VsProcessLib.ps1'

# -- 1. Parse-check the lib and the script that dot-sources it. --
foreach ($p in @($libPath, (Join-Path $vsReactor 'Reinstall-Vsix.ps1'))) {
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseInput(
        (Get-Content $p -Raw), [ref]$null, [ref]$parseErrors) | Out-Null
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        Write-Host "$(Split-Path $p -Leaf) has $($parseErrors.Count) parse error(s):" -ForegroundColor Red
        $parseErrors | ForEach-Object { Write-Host "  line $($_.Extent.StartLineNumber): $($_.Message)" -ForegroundColor Red }
        exit 1
    }
}

. $libPath

# -- Fake-process harness. --
$fakeName = 'reactor-fake-devenv'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("vsprocesslib-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$fakeExe = Join-Path $tmp "$fakeName.exe"
# cmd.exe has no sidecar dependencies, so a renamed copy runs standalone and
# gives us a process with a name we fully control.
Copy-Item "$env:SystemRoot\System32\cmd.exe" $fakeExe

# Outer fake spawns an inner fake, which spawns ping. `>nul` on the outer
# command line suppresses the whole tree's output.
$hangArgs  = '/c "' + $fakeExe + '" /c ping -n 600 127.0.0.1 >nul'
$quickArgs = '/c exit 0'

# Pings that already existed before this run. Teardown asserts we add no
# permanent ones — an earlier revision killed only $fakeName and leaked the
# grandchild ping on every case.
$script:PreExistingPings = @(Get-Process -Name 'PING' -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })

function Get-FakeCount {
    @(Get-Process -Name $fakeName -ErrorAction SilentlyContinue).Count
}

function Get-LeakedPingCount {
    @(Get-Process -Name 'PING' -ErrorAction SilentlyContinue |
        Where-Object { $script:PreExistingPings -notcontains $_.Id }).Count
}

function Start-Fake {
    param([string]$Arguments)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $fakeExe
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false
    [System.Diagnostics.Process]::Start($psi)
}

function Wait-ForFakeCount {
    param([int]$AtLeast, [int]$TimeoutSeconds = 15)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ((Get-FakeCount) -lt $AtLeast -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    Get-FakeCount
}

# Teardown must take the whole tree, not just the $fakeName layer: the grandchild
# is `ping`, and killing its parent does not kill it.
function Remove-AllFakes {
    foreach ($p in @(Get-Process -Name $fakeName -ErrorAction SilentlyContinue)) {
        try { Stop-ProcessTreeViaTaskkill -Id $p.Id } catch { }
    }
    foreach ($p in @(Get-Process -Name 'PING' -ErrorAction SilentlyContinue |
                     Where-Object { $script:PreExistingPings -notcontains $_.Id })) {
        try { $p.Kill() } catch { }
    }
}

function Wait-FakeNameCleared {
    param([int]$TimeoutSeconds = 20)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        if ((Get-FakeCount) -eq 0) { return $true }
        if ([DateTime]::UtcNow -ge $deadline) { return $false }
        Start-Sleep -Milliseconds 250
    }
}

try {
    # -- 2. The harness itself really does build a multi-process tree. --
    # Positive control: if this were 1, every "tree" assertion below would be
    # satisfiable by a single-process kill and would prove nothing.
    $p = Start-Fake -Arguments $hangArgs
    $count = Wait-ForFakeCount -AtLeast 2
    Assert-True ($count -ge 2) "harness: fake devenv spawns a real tree (saw $count '$fakeName' processes, expected >= 2)"
    Remove-AllFakes
    Assert-True (Wait-FakeNameCleared) 'harness: fakes are cleaned up between cases'

    # -- 3. Stop-ProcessTreeSafely reaps descendants, not just the root. --
    # This is the #1074 fix. Against `$proc.Kill()` the inner fake survives.
    $p = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 2 | Out-Null
    $exited = Stop-ProcessTreeSafely -Process $p -WaitSeconds 20
    Assert-True $exited 'Stop-ProcessTreeSafely: root process exited within the wait'
    Assert-True (Wait-FakeNameCleared) 'Stop-ProcessTreeSafely: no descendant is left running'
    Assert-Equal 0 (Get-FakeCount) 'Stop-ProcessTreeSafely: process name is fully clear'
    Remove-AllFakes
    Wait-FakeNameCleared | Out-Null

    # -- 4. The taskkill fallback works on its own. --
    # Under pwsh the reflection probe in Stop-ProcessTreeSafely always finds
    # Kill([bool]), so the Windows PowerShell 5.1 branch never runs in CI.
    # Calling it directly is what stops that branch from rotting: pointing
    # taskkill.exe at a bogus path reddens these two assertions.
    $p = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 2 | Out-Null
    $global:LASTEXITCODE = 0
    Stop-ProcessTreeViaTaskkill -Id $p.Id
    Assert-True (Wait-FakeNameCleared) 'Stop-ProcessTreeViaTaskkill: kills the whole tree'
    Assert-Equal 0 $LASTEXITCODE 'Stop-ProcessTreeViaTaskkill: leaves $LASTEXITCODE at 0'
    Remove-AllFakes
    Wait-FakeNameCleared | Out-Null

    # -- 5. Timeout path: nothing of ours survives, duration is a real measurement. --
    $timeout = 4
    $r = Invoke-DevenvUpdateConfiguration -DevenvPath $fakeExe -Arguments $hangArgs `
        -TimeoutSeconds $timeout -ProcessName $fakeName -DrainTimeoutSeconds 20
    Assert-True $r.TimedOut 'timeout: TimedOut is reported'
    Assert-True $r.Drained  'timeout: Drained is reported true'
    Assert-Equal 0 (Get-FakeCount) 'timeout: no fake devenv survives the call'
    # The pre-fix instrument read ExitTime on a process that had not been reaped
    # yet, which returns the 1601 epoch and printed "-13430263500.8s". This
    # bounds check catches that class of nonsense. It does NOT distinguish a
    # Stopwatch from a *correctly sequenced* ExitTime-StartTime read, because
    # once we wait for the kill to complete the two agree — verified by
    # mutation: swapping the Stopwatch back for ExitTime-StartTime does not
    # redden this suite, whereas removing the tree kill reddens it immediately.
    # The Stopwatch is kept because it cannot be wrong; the assertion below is
    # honest about guarding the symptom, not the implementation choice.
    Assert-InRange $r.DurationSeconds $timeout ($timeout + 30) 'timeout: DurationSeconds is a plausible wall-clock measurement'
    # ExitCode must be readable, i.e. we waited for the kill to complete rather
    # than racing it.
    Assert-True ($null -ne $r.ExitCode) 'timeout: ExitCode is defined after the tree kill'
    Remove-AllFakes
    Wait-FakeNameCleared | Out-Null

    # -- 6. Happy path is untouched (guards the VS 18.7 flow). --
    $r = Invoke-DevenvUpdateConfiguration -DevenvPath $fakeExe -Arguments $quickArgs `
        -TimeoutSeconds 30 -ProcessName $fakeName -DrainTimeoutSeconds 10
    Assert-Equal $false $r.TimedOut 'happy path: TimedOut is false when the process exits on its own'
    Assert-Equal 0 $r.ExitCode 'happy path: real exit code is surfaced'
    Assert-Equal 0 $r.KilledPids.Count 'happy path: nothing is killed'
    Assert-InRange $r.DurationSeconds 0 30 'happy path: DurationSeconds is non-negative and bounded'

    # -- 7. Ancestry: we kill our own tree and nobody else's. --
    # THE safety property. `devenv` is a shared process name, so identifying
    # victims by name would take out a developer's IDE. Attribution is by
    # Win32_Process ancestry captured while the tree is still alive.
    $mine = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 2 | Out-Null
    $theirs = Start-Fake -Arguments $hangArgs   # stands in for the developer's IDE
    Wait-ForFakeCount -AtLeast 4 | Out-Null

    $descendants = @(Get-DescendantProcessIds -ParentId $mine.Id)
    Assert-True ($descendants.Count -ge 1) "ancestry: finds the descendant of pid $($mine.Id) (saw $($descendants.Count))"
    Assert-True ($descendants -notcontains $theirs.Id) 'ancestry: an unrelated same-named process is not a descendant'

    $ours = @($mine.Id) + $descendants
    $killed = @(Stop-ProcessIds -Ids $ours -RequireName $fakeName -WaitSeconds 20)
    Assert-True ($killed -contains $mine.Id) 'attribution: our own root is killed'
    $theirs.Refresh()
    Assert-Equal $false $theirs.HasExited "attribution: the unrelated process (pid $($theirs.Id)) is left running"
    Assert-True ($killed -notcontains $theirs.Id) 'attribution: the unrelated process is never targeted'
    Remove-AllFakes
    Assert-True (Wait-FakeNameCleared) 'attribution: teardown clears the name'

    # -- 8. END-TO-END: a foreign same-named process survives a timeout. --
    # The safety property that matters, asserted through the real entry point.
    # Case 7 exercises the helpers directly, so it does NOT catch a regression
    # in how Invoke-DevenvUpdateConfiguration decides what is "ours" — verified
    # by mutation: swapping the ancestry call for a name lookup leaves case 7
    # green and reddens only the assertions below.
    $theirs = Start-Fake -Arguments $hangArgs   # the developer's open IDE
    Wait-ForFakeCount -AtLeast 2 | Out-Null
    $theirsId = $theirs.Id

    $r = Invoke-DevenvUpdateConfiguration -DevenvPath $fakeExe -Arguments $hangArgs `
        -TimeoutSeconds 3 -ProcessName $fakeName -DrainTimeoutSeconds 15
    Assert-True $r.TimedOut 'e2e attribution: the run timed out (precondition for the sweep path)'
    $theirs.Refresh()
    Assert-Equal $false $theirs.HasExited "e2e attribution: the developer's IDE (pid $theirsId) is still running"
    Assert-True ($r.KilledPids -notcontains $theirsId) 'e2e attribution: the foreign pid is never killed'
    Assert-True ($r.ForeignPids -contains $theirsId) 'e2e attribution: the foreign pid is reported, not silently ignored'
    Remove-AllFakes
    Wait-FakeNameCleared | Out-Null

    # -- 9. Stop-ProcessIds refuses a pid whose name no longer matches. --
    # Guards pid reuse between the ancestry snapshot and the kill.
    $survivor = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 1 | Out-Null
    $killed = @(Stop-ProcessIds -Ids @($survivor.Id) -RequireName 'some-other-name' -WaitSeconds 5)
    Assert-Equal 0 $killed.Count 'pid reuse: a pid whose name does not match RequireName is skipped'
    $survivor.Refresh()
    Assert-Equal $false $survivor.HasExited 'pid reuse: the mismatched process is left alone'
    Remove-AllFakes
    Wait-FakeNameCleared | Out-Null

    # -- 10. Wait-ProcessIdsCleared reports failure rather than lying. --
    # Without a truthful negative, a false "drained" would let the next run trip
    # the guard with no explanation.
    $stubborn = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 1 | Out-Null
    Assert-Equal $false (Wait-ProcessIdsCleared -Ids @($stubborn.Id) -TimeoutSeconds 2) 'Wait-ProcessIdsCleared: false while the pid is still running'
    Remove-AllFakes
    Wait-FakeNameCleared | Out-Null
    Assert-True (Wait-ProcessIdsCleared -Ids @($stubborn.Id) -TimeoutSeconds 20) 'Wait-ProcessIdsCleared: true once the pid is gone'

    # -- 11. Drained=false actually propagates to the caller. --
    # Shadow the drain helper so the failure is deterministic; without this the
    # Drained field could be hard-coded $true and the suite would not notice.
    function Wait-ProcessIdsCleared { param([int[]]$Ids = @(), [int]$TimeoutSeconds = 30) return $false }
    $r = Invoke-DevenvUpdateConfiguration -DevenvPath $fakeExe -Arguments $hangArgs `
        -TimeoutSeconds 2 -ProcessName $fakeName -DrainTimeoutSeconds 3
    Assert-Equal $false $r.Drained 'Drained: a failed drain is reported, not swallowed'
    . $libPath   # restore the real implementation
    Remove-AllFakes
    Wait-FakeNameCleared | Out-Null

    # -- 12. Stop-ProcessTreeSafely does not leak $LASTEXITCODE. --
    # The 5.1 path shells out to taskkill, whose non-zero "not found" status
    # would otherwise ride out to the caller's exit code (the #1074 defect class).
    $done = Start-Fake -Arguments $quickArgs
    $done.WaitForExit(10000) | Out-Null
    $global:LASTEXITCODE = 0
    Stop-ProcessTreeSafely -Process $done -WaitSeconds 5 | Out-Null
    Assert-Equal 0 $LASTEXITCODE 'Stop-ProcessTreeSafely: leaves $LASTEXITCODE at 0'

    # -- 13. The exit-code contract three scripts depend on. --
    # Reinstall-Vsix.ps1 exits with this; bootstrap.ps1 and `mur upgrade` branch
    # on it. Mutating the mapping reddens here instead of silently changing what
    # CI does with a partly-failed install.
    Assert-Equal 3 (Get-VsixReinstallExitCode -UpdateConfigIncomplete $true)  'exit contract: incomplete /updateconfiguration maps to 3'
    Assert-Equal 0 (Get-VsixReinstallExitCode -UpdateConfigIncomplete $false) 'exit contract: a completed run maps to 0'
}
finally {
    Remove-AllFakes
    Wait-FakeNameCleared | Out-Null
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# The harness spawns `ping` grandchildren; leaking them would slowly fill a CI
# runner's process table across repeated runs.
Assert-Equal 0 (Get-LeakedPingCount) 'teardown: the harness leaks no ping processes'

# -- Report --
Write-Host ""
Write-Host "VsProcessLib tests: $script:Pass passed, $script:Fail failed"
if ($script:Fail -gt 0) {
    Write-Host ""
    $script:Failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    exit 1
}
exit 0
