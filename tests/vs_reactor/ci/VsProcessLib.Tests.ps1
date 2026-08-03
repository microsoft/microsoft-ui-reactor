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
    between a Stopwatch and a correctly-sequenced ExitTime read. See case 4.

    A distinctly-named fake also keeps the sweep tests safe — Stop-NewProcessesByName
    is never pointed at `pwsh`, so an exclude-list regression cannot take out the
    test host.

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
    foreach ($p in @(Get-Process -Name $fakeName -ErrorAction SilentlyContinue)) {
        try { $p.Kill() } catch { }
    }
}

try {
    # -- 2. The harness itself really does build a multi-process tree. --
    # Positive control: if this were 1, every "tree" assertion below would be
    # satisfiable by a single-process kill and would prove nothing.
    $p = Start-Fake -Arguments $hangArgs
    $count = Wait-ForFakeCount -AtLeast 2
    Assert-True ($count -ge 2) "harness: fake devenv spawns a real tree (saw $count '$fakeName' processes, expected >= 2)"
    try { $p.Kill() } catch { }
    Remove-AllFakes
    Assert-True (Wait-ProcessNameCleared -Name $fakeName -TimeoutSeconds 15) 'harness: fakes are cleaned up between cases'

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
    Wait-ProcessNameCleared -Name $fakeName -TimeoutSeconds 20 | Out-Null

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

    # -- 5. Happy path is untouched (guards the VS 18.7 flow). --
    $r = Invoke-DevenvUpdateConfiguration -DevenvPath $fakeExe -Arguments $quickArgs `
        -TimeoutSeconds 30 -ProcessName $fakeName -DrainTimeoutSeconds 10
    Assert-Equal $false $r.TimedOut 'happy path: TimedOut is false when the process exits on its own'
    Assert-Equal 0 $r.ExitCode 'happy path: real exit code is surfaced'
    Assert-Equal 0 $r.KilledPids.Count 'happy path: nothing is killed'
    Assert-InRange $r.DurationSeconds 0 30 'happy path: DurationSeconds is non-negative and bounded'

    # -- 6. The sweep spares pre-existing processes. --
    # A developer who opens VS mid-run must not have their IDE killed; the
    # pre-start snapshot is what protects them.
    $pre = Start-Fake -Arguments $hangArgs
    # Wait for the *whole* pre-existing tree before snapshotting: if the inner
    # process were missed by the snapshot the sweep would kill it, the outer
    # would exit with it, and "excluded process is still alive" would flake.
    Wait-ForFakeCount -AtLeast 2 | Out-Null
    $preIds = @(Get-Process -Name $fakeName -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $new = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast ($preIds.Count + 1) | Out-Null

    $killed = @(Stop-NewProcessesByName -Name $fakeName -ExcludePids $preIds -WaitSeconds 20)
    Assert-True ($killed -contains $new.Id) "sweep: kills the process started after the snapshot (pid $($new.Id))"
    Assert-True ($killed -notcontains $pre.Id) "sweep: does not kill the excluded pre-existing process (pid $($pre.Id))"
    $pre.Refresh()
    Assert-Equal $false $pre.HasExited 'sweep: the excluded process is still alive'
    Assert-True $new.HasExited 'sweep: the swept process is gone'

    Remove-AllFakes
    Assert-True (Wait-ProcessNameCleared -Name $fakeName -TimeoutSeconds 20) 'sweep: teardown clears the name'

    # -- 7. Wait-ProcessNameCleared reports failure rather than lying. --
    # Without a truthful negative, a false "drained" would let the next run trip
    # the guard with no explanation.
    $stubborn = Start-Fake -Arguments $hangArgs
    Wait-ForFakeCount -AtLeast 1 | Out-Null
    Assert-Equal $false (Wait-ProcessNameCleared -Name $fakeName -TimeoutSeconds 2) 'Wait-ProcessNameCleared: returns false while a process is still running'
    Remove-AllFakes
    Assert-True (Wait-ProcessNameCleared -Name $fakeName -TimeoutSeconds 20) 'Wait-ProcessNameCleared: returns true once the name is clear'

    # -- 8. Stop-ProcessTreeSafely does not leak $LASTEXITCODE. --
    # The 5.1 path shells out to taskkill, whose non-zero "not found" status
    # would otherwise ride out to the caller's exit code (the #1074 defect class).
    $done = Start-Fake -Arguments $quickArgs
    $done.WaitForExit(10000) | Out-Null
    $global:LASTEXITCODE = 0
    Stop-ProcessTreeSafely -Process $done -WaitSeconds 5 | Out-Null
    Assert-Equal 0 $LASTEXITCODE 'Stop-ProcessTreeSafely: leaves $LASTEXITCODE at 0'
}
finally {
    Remove-AllFakes
    Wait-ProcessNameCleared -Name $fakeName -TimeoutSeconds 20 | Out-Null
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
