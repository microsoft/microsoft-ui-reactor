<#
.SYNOPSIS
    Dependency-free tests for src/vs-reactor/VsProcessLib.ps1 — the process-tree
    timeout/kill/drain helpers behind `devenv /updateconfiguration`.

.DESCRIPTION
    Windows-only and headless: no build, no dotnet, no Visual Studio. Wired into
    .github/workflows/vs-reactor-lib-tests.yml, which runs this file under BOTH
    pwsh and Windows PowerShell 5.1 — 5.1 is a real entry point (bootstrap.ps1
    re-launches with the current host, `mur upgrade` falls back to
    powershell.exe), so nothing here may use PowerShell 6+ syntax such as the
    $IsWindows automatic variable. Exits non-zero on any failed assertion.

    The fake "devenv" is a copy of cmd.exe renamed to reactor-fake-devenv.exe,
    launched as `/c "<fake>" /c ping -n 600 127.0.0.1`. That produces a real
    two-level, same-named, genuinely-hanging process tree — which is what makes
    the orphan assertions non-vacuous: mutation-checked against the shipped
    pre-fix implementation (`$proc.Kill()`, no argument, no wait) the inner
    reactor-fake-devenv.exe survives and case 3 and case 6 redden.

    Case 6 is the attribution oracle and it cuts both ways: it runs a real
    timeout against one tree while an unrelated same-named tree is running, and
    requires that the unrelated one survives untouched. An earlier revision of
    the lib swept by process name against a pre-start snapshot, which would kill
    a developer's IDE (Reinstall-Vsix.ps1 refuses to start while any devenv
    lives, so that snapshot is always empty). That revision passes case 3 and
    fails case 6.

    Known limit, stated rather than papered over: the "-425 years" duration in
    the issue is a race on reading Process.ExitTime before the OS reaps a heavy
    process. A cmd.exe fake dies too fast to reproduce it, so the duration
    assertion here is a bounds check on the symptom, not a discriminator
    between a Stopwatch and a correctly-sequenced ExitTime read. See case 4.

    A distinctly-named fake also keeps the sweep tests safe — nothing here is
    ever pointed at `pwsh`, so an attribution regression cannot take out the
    test host. The name is further namespaced with the host pid so two
    concurrent runs cannot kill each other's fixtures; see the harness comment.

    Run locally:  pwsh tests/vs_reactor/ci/VsProcessLib.Tests.ps1
                  powershell -File tests/vs_reactor/ci/VsProcessLib.Tests.ps1
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# $IsWindows does not exist before PowerShell 6, and this file must run on 5.1.
$onWindows = if ($PSVersionTable.ContainsKey('Platform')) { $PSVersionTable.Platform -eq 'Win32NT' } else { $true }
if (-not $onWindows) {
    Write-Host "VsProcessLib tests: skipped (Windows-only process semantics)."
    exit 0
}

$script:Pass = 0
$script:Fail = 0
$script:Failures = New-Object System.Collections.Generic.List[string]

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

$vsReactor = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\src\vs-reactor')).Path
$libPath = Join-Path $vsReactor 'VsProcessLib.ps1'

# -- 1. Parse-check the lib and the script that dot-sources it. --
# ParseInput over UTF8-decoded text, not ParseFile: these files are BOM-less
# UTF-8, and Windows PowerShell 5.1's ParseFile decodes them as ANSI, which
# turns any non-ASCII comment character into a spurious syntax error.
foreach ($p in @($libPath, (Join-Path $vsReactor 'Reinstall-Vsix.ps1'))) {
    $parseErrors = $null
    $text = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
    [System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$null, [ref]$parseErrors) | Out-Null
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        Write-Host "$(Split-Path $p -Leaf) has $($parseErrors.Count) parse error(s):" -ForegroundColor Red
        $parseErrors | ForEach-Object { Write-Host "  line $($_.Extent.StartLineNumber): $($_.Message)" -ForegroundColor Red }
        exit 1
    }
}

. $libPath

# -- Fake-process harness. --
# The name is namespaced per run. Every helper below finds and kills fakes by
# process *name*, and Get-Process is machine-wide — so a shared name means two
# concurrent runs of this file (or a leftover from an aborted one) silently
# destroy each other's fixtures. Verified: two overlapping runs under a shared
# name produce spurious "process died early" failures and an Access-denied on
# Process.Start. That is the same attribution mistake this suite exists to catch
# in the product, so the harness does not get to make it either.
$fakeName = "reactor-fake-devenv-$PID"
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("vsprocesslib-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$fakeExe = Join-Path $tmp "$fakeName.exe"
# cmd.exe has no sidecar dependencies, so a renamed copy runs standalone and
# gives us a process with a name we fully control.
Copy-Item "$env:SystemRoot\System32\cmd.exe" $fakeExe

# Outer fake spawns an inner fake, which spawns ping. `>nul` on the outer
# command line suppresses the whole tree's output.
$hangArgs = '/c "' + $fakeExe + '" /c ping -n 600 127.0.0.1 >nul'
$quickArgs = '/c exit 0'

function Get-FakeCount {
    @(Get-Process -Name $fakeName -ErrorAction SilentlyContinue).Count
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

function Remove-AllFakes {
    # Kill the *tree*, not just the roots. The fake spawns ping.exe, and
    # Process.Kill() orphans it for the full 600-second ping — which is #1074's
    # defect committed in the harness of the suite that exists to catch it.
    #
    # taskkill is invoked directly rather than through Stop-ProcessTreeSafely,
    # and the drain below polls locally rather than through
    # Wait-ProcessNameCleared, so teardown stays independent of the code under
    # test: a broken lib must redden an assertion, not silently poison the next
    # case's fixture.
    foreach ($p in @(Get-Process -Name $fakeName -ErrorAction SilentlyContinue)) {
        try { & "$env:SystemRoot\System32\taskkill.exe" '/PID' $p.Id '/T' '/F' 2>&1 | Out-Null } catch { }
    }
    # taskkill reports non-zero for a pid already reaped by an earlier /T.
    $global:LASTEXITCODE = 0
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ((Get-FakeCount) -gt 0 -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
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
    Assert-Equal 0 (Get-FakeCount) 'harness: fakes are cleaned up between cases'

    # -- 3. Stop-ProcessTreeSafely reaps descendants, not just the root. --
    # This is the #1074 fix. Against `$proc.Kill()` the inner fake survives.
    $p = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 2 | Out-Null
    $exited = Stop-ProcessTreeSafely -Process $p -WaitSeconds 20
    Assert-True $exited 'Stop-ProcessTreeSafely: root process exited within the wait'
    Assert-True (Wait-ProcessNameCleared -Name $fakeName -TimeoutSeconds 20) 'Stop-ProcessTreeSafely: no descendant is left running'
    Assert-Equal 0 (Get-FakeCount) 'Stop-ProcessTreeSafely: process name is fully clear'
    # Explicit teardown so a failure here is attributed to this case rather than
    # cascading into the next one's counts.
    Remove-AllFakes

    # -- 4. Timeout path: nothing survives, duration is a real measurement. --
    $timeout = 4
    $r = Invoke-DevenvUpdateConfiguration -DevenvPath $fakeExe -Arguments $hangArgs `
        -TimeoutSeconds $timeout -ProcessName $fakeName -DrainTimeoutSeconds 20
    Assert-True $r.TimedOut 'timeout: TimedOut is reported'
    Assert-True $r.Drained  'timeout: Drained is reported true (name cleared)'
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

    # -- 5. Happy path is untouched (guards the VS 18.7 flow). --
    $r = Invoke-DevenvUpdateConfiguration -DevenvPath $fakeExe -Arguments $quickArgs `
        -TimeoutSeconds 30 -ProcessName $fakeName -DrainTimeoutSeconds 10
    Assert-Equal $false $r.TimedOut 'happy path: TimedOut is false when the process exits on its own'
    Assert-Equal 0 $r.ExitCode 'happy path: real exit code is surfaced'
    Assert-Equal 0 $r.KilledPids.Count 'happy path: nothing is killed'
    Assert-InRange $r.DurationSeconds 0 30 'happy path: DurationSeconds is non-negative and bounded'
    Assert-True $r.Drained 'happy path: Drained is true when the name really is clear'
    Remove-AllFakes

    # -- 5b. Drained is measured on the happy path, not assumed. --
    # A clean exit does not prove nothing was left behind: /updateconfiguration
    # spawns a second devenv, so "it exited, therefore the machine is clear" is
    # exactly the inference that must not be hardcoded. With a same-named
    # process running, a truthful Drained is false.
    $bystander = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 1 | Out-Null
    $r = Invoke-DevenvUpdateConfiguration -DevenvPath $fakeExe -Arguments $quickArgs `
        -TimeoutSeconds 30 -ProcessName $fakeName -DrainTimeoutSeconds 10
    Assert-Equal $false $r.TimedOut 'happy path: still a non-timeout run with a bystander present'
    Assert-Equal $false $r.Drained 'happy path: Drained is false while another same-named process runs'
    $bystander.Refresh()
    Assert-Equal $false $bystander.HasExited 'happy path: the bystander is reported on, not killed'
    Remove-AllFakes

    # -- 6. The timeout sweep is attributed, not name-matched. --
    # The scenario: a developer opens Visual Studio during the two-minute
    # window. Their IDE shares the process name but is not ours, and must
    # survive. Meanwhile every process from *our* tree must be gone — killing
    # only the root is what #1074 was.
    $victim = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 2 | Out-Null
    $victimIds = @(Get-ProcessIdsByName -Name $fakeName)

    $r = Invoke-DevenvUpdateConfiguration -DevenvPath $fakeExe -Arguments $hangArgs `
        -TimeoutSeconds 4 -ProcessName $fakeName -DrainTimeoutSeconds 4

    $victim.Refresh()
    Assert-Equal $false $victim.HasExited "attribution: an unrelated same-named process survives the sweep (pid $($victim.Id))"
    $strays = @(@(Get-ProcessIdsByName -Name $fakeName) | Where-Object { $victimIds -notcontains $_ })
    Assert-Equal 0 $strays.Count "attribution: no process from the invoked tree survives (strays: $($strays -join ', '))"
    # Drained is a report on the machine, not on our tree: an unrelated process
    # legitimately holds the name, and saying otherwise would tell the caller
    # the next run's guard will pass when it will not.
    Assert-Equal $false $r.Drained 'attribution: Drained is false while an unrelated process still holds the name'
    Remove-AllFakes

    # -- 7. Get-ProcessDescendantIds resolves the tree by parentage. --
    # This is what makes case 6 possible: without it the sweep has nothing to
    # distinguish our processes from anyone else's.
    $root = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 2 | Out-Null
    $kids = @(Get-ProcessDescendantIds -RootPid $root.Id)
    Assert-True ($kids.Count -ge 1) "descendants: the child of the fake tree is found (got $($kids.Count))"
    Assert-True ($kids -notcontains $root.Id) 'descendants: the root is excluded by default'
    $withRoot = @(Get-ProcessDescendantIds -RootPid $root.Id -IncludeRoot)
    Assert-True ($withRoot -contains $root.Id) 'descendants: -IncludeRoot returns the root'
    Assert-Equal ($kids.Count + 1) $withRoot.Count 'descendants: -IncludeRoot adds exactly the root'

    $unrelated = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 4 | Out-Null
    $kidsAgain = @(Get-ProcessDescendantIds -RootPid $root.Id)
    Assert-True ($kidsAgain -notcontains $unrelated.Id) "descendants: an unrelated same-named process is not claimed as a descendant (pid $($unrelated.Id))"
    Remove-AllFakes

    # -- 8. Wait-ProcessNameCleared reports failure rather than lying. --
    # Without a truthful negative, a false "drained" would let the next run trip
    # the guard with no explanation.
    $stubborn = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 1 | Out-Null
    Assert-Equal $false (Wait-ProcessNameCleared -Name $fakeName -TimeoutSeconds 2) 'Wait-ProcessNameCleared: returns false while a process is still running'
    Assert-True ($stubborn.Id -gt 0) 'Wait-ProcessNameCleared: observes only — the process it reported on is untouched'
    $stubborn.Refresh()
    Assert-Equal $false $stubborn.HasExited 'Wait-ProcessNameCleared: does not kill what it polls'
    Remove-AllFakes
    Assert-True (Wait-ProcessNameCleared -Name $fakeName -TimeoutSeconds 20) 'Wait-ProcessNameCleared: returns true once the name is clear'

    # -- 9. Stop-ProcessIdsSafely will not act on a pid it cannot attribute. --
    # Windows recycles pids, so by sweep time a recorded pid may belong to
    # something else. Both guards are checked against a live process.
    $bystander = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 1 | Out-Null
    $killed = @(Stop-ProcessIdsSafely -ProcessIds @($bystander.Id) -ExpectedName 'some-other-name' -WaitSeconds 5)
    Assert-Equal 0 $killed.Count 'pid reuse: a pid whose name no longer matches is left alone'
    $bystander.Refresh()
    Assert-Equal $false $bystander.HasExited 'pid reuse: the name-mismatched process is still running'

    $killed = @(Stop-ProcessIdsSafely -ProcessIds @($bystander.Id) -ExpectedName $fakeName `
            -NotStartedAfter ([DateTime]::Now.AddHours(-1)) -WaitSeconds 5)
    Assert-Equal 0 $killed.Count 'pid reuse: a pid that started after we recorded it is left alone'
    $bystander.Refresh()
    Assert-Equal $false $bystander.HasExited 'pid reuse: the too-young process is still running'

    # Positive control for the two assertions above: with both guards satisfied
    # the same call really does kill. Without this, "0 killed" would be
    # indistinguishable from a Stop-ProcessIdsSafely that never kills anything.
    $killed = @(Stop-ProcessIdsSafely -ProcessIds @($bystander.Id) -ExpectedName $fakeName -WaitSeconds 20)
    Assert-Equal 1 $killed.Count 'pid reuse: an attributable pid IS killed (positive control)'
    Remove-AllFakes

    # -- 10. Stop-ProcessTreeSafely does not leak $LASTEXITCODE. --
    # It shells out to taskkill on every host now, and taskkill reports non-zero
    # when its target is already gone — #1074's third defect is exactly that
    # class of leak riding out to the caller's exit code.
    #
    # Honest scope: this asserts the boundary contract (the lib hands back 0
    # after shelling out). It cannot force taskkill's already-gone status
    # without racing the HasExited check, so deleting the reset line alone will
    # not redden it. The line is kept because it costs one statement and the
    # failure mode is a red CI job with no local repro.
    $global:LASTEXITCODE = 42
    $live = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 1 | Out-Null
    Stop-ProcessTreeSafely -Process $live -WaitSeconds 20 | Out-Null
    Assert-Equal 0 $LASTEXITCODE 'Stop-ProcessTreeSafely: leaves $LASTEXITCODE at 0 after shelling out to taskkill'
}
finally {
    Remove-AllFakes
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

# -- Report --
Write-Host ""
Write-Host "VsProcessLib tests: $script:Pass passed, $script:Fail failed"
if ($script:Fail -gt 0) {
    Write-Host ""
    $script:Failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }
    exit 1
}
exit 0
