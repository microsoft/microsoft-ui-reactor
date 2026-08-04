<#
.SYNOPSIS
    Process-tree helpers for the Reactor VSIX install scripts.

.DESCRIPTION
    Dot-sourced by Reinstall-Vsix.ps1. Exists as a separate file so the
    timeout / kill / drain logic can be unit-tested headlessly against fake
    processes without running a real VSIX install
    (tests/vs_reactor/ci/VsProcessLib.Tests.ps1).

    Why this exists (issue #1074): on Visual Studio 18.8,
    `devenv /updateconfiguration` never returns. The original code hit its
    2-minute timeout and called `$proc.Kill()` — which terminates *only* the
    top process, not its children, and returns without waiting. The second
    `devenv` that /updateconfiguration spawns survived, and every later
    Reinstall-Vsix.ps1 run on that machine tripped the "Visual Studio is
    running" guard and exited 1. It also read `$proc.ExitTime` on a process
    that had not exited yet, which returns the 1601 epoch and printed a
    duration of roughly -425 years.

    ATTRIBUTION, NOT NAME MATCHING: the post-timeout sweep acts only on pids
    this module recorded as descendants of the process *it* started. An earlier
    revision swept by process name with a pre-start snapshot as the exclude
    list, which is unsafe: Reinstall-Vsix.ps1 refuses to start while any devenv
    is alive, so that snapshot is always empty and the sweep degenerated to
    "kill every devenv" — including an IDE a developer opened during the
    two-minute window. Anything we cannot attribute is reported (see Drained),
    never killed.

    COMPATIBILITY: these functions must stay Windows PowerShell 5.1 clean.
    bootstrap.ps1 re-launches Reinstall-Vsix.ps1 with `(Get-Process -Id
    $PID).Path`, and `mur upgrade` falls back to powershell.exe when pwsh is
    absent. That rules out `Process.Kill([bool])` (.NET 5+),
    `ProcessStartInfo.ArgumentList` (.NET Core only), and the `$IsWindows`
    automatic variable (PowerShell 6+).
#>

# Ids of every running process with the given name. Callers wrap with @() —
# returning a bare array (rather than the `,$array` idiom) keeps the shape
# predictable for 0, 1 and N matches.
function Get-ProcessIdsByName {
    param([Parameter(Mandatory)][string]$Name)

    return @(Get-Process -Name $Name -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
}

# Every descendant pid of $RootPid, breadth-first, via the Win32_Process
# parent links. Add -IncludeRoot to get the root back in the result.
#
# Windows recycles pids, and a process keeps its ParentProcessId after that
# parent exits — so a stale ppid can point at an unrelated recycled process.
# A child that was created *before* its claimed parent cannot really be its
# child, so those edges are dropped.
function Get-ProcessDescendantIds {
    param(
        [Parameter(Mandatory)][int]$RootPid,
        [switch]$IncludeRoot
    )

    $all = @(Get-CimInstance -ClassName Win32_Process -Property ProcessId, ParentProcessId, CreationDate -ErrorAction SilentlyContinue)
    if ($all.Count -eq 0) {
        # CIM unavailable. Report only what we know for certain rather than
        # guessing by name; the caller's drain poll still tells the truth.
        if ($IncludeRoot) { return @($RootPid) }
        return @()
    }

    $childrenOf = @{}
    $createdAt = @{}
    foreach ($p in $all) {
        $processId = [int]$p.ProcessId
        $parentId = [int]$p.ParentProcessId
        $createdAt[$processId] = $p.CreationDate
        if (-not $childrenOf.ContainsKey($parentId)) {
            $childrenOf[$parentId] = New-Object System.Collections.Generic.List[int]
        }
        $childrenOf[$parentId].Add($processId) | Out-Null
    }

    $seen = New-Object System.Collections.Generic.HashSet[int]
    $seen.Add($RootPid) | Out-Null
    $found = New-Object System.Collections.Generic.List[int]
    $queue = New-Object System.Collections.Generic.Queue[int]
    $queue.Enqueue($RootPid)

    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if (-not $childrenOf.ContainsKey($current)) { continue }
        foreach ($child in $childrenOf[$current]) {
            if (-not $seen.Add($child)) { continue }
            $childBirth = $createdAt[$child]
            $parentBirth = $createdAt[$current]
            if ($null -ne $childBirth -and $null -ne $parentBirth -and $childBirth -lt $parentBirth) { continue }
            $found.Add($child) | Out-Null
            $queue.Enqueue($child)
        }
    }

    if ($IncludeRoot) { return @(@($RootPid) + $found.ToArray()) }
    return $found.ToArray()
}

# Terminate a process AND its descendants, then block until it is really gone.
# Returns $true when the process exited within $WaitSeconds.
function Stop-ProcessTreeSafely {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [int]$WaitSeconds = 30
    )

    # Report only what is known. An exception in here means the state is
    # *unknown*, which is not the same as good: HasExited can throw for a live
    # process we lack rights to inspect, and answering $true there would let
    # Stop-ProcessIdsSafely record a still-running pid as reaped — reporting a
    # cleanup that never happened, which is the failure mode this module exists
    # to remove. So unknown falls through to the kill attempt, and an unknown
    # wait answers $false, leaving the verdict to the drain poll, which observes
    # the machine instead of a handle.
    $processId = 0
    try { $processId = $Process.Id } catch { return $false }
    try { if ($Process.HasExited) { return $true } } catch { }

    # `taskkill /T` walks the tree on every supported Windows and on both
    # PowerShell hosts. Process.Kill($true) would do the same but needs .NET 5+,
    # which Windows PowerShell 5.1 does not have — and its no-argument sibling
    # Kill() is the exact call that caused #1074. One path, tested everywhere,
    # beats a host-dependent pair where only one half runs in CI.
    try {
        & "$env:SystemRoot\System32\taskkill.exe" '/PID' $processId '/T' '/F' 2>&1 | Out-Null
    } catch {
        # Raced with a natural exit, or access denied on a process owned by
        # another user. Fall through to the wait, which reports the truth.
    }
    # taskkill returns non-zero when the process is already gone. That is not an
    # error here, and letting it linger would leak into the exit code of
    # whichever script dot-sourced us — #1074's third defect, in miniature.
    $global:LASTEXITCODE = 0

    try { return $Process.WaitForExit($WaitSeconds * 1000) } catch { return $false }
}

# Kill the given pids (tree and all) and return the ones that were still alive.
#
# This is the belt to Stop-ProcessTreeSafely's braces: `taskkill /T` enumerates
# descendants at kill time, so a grandchild that has already re-parented is
# missed. The caller records the tree *before* killing and passes it here.
#
# $ExpectedName and $NotStartedAfter exist because Windows recycles pids: by the
# time we sweep, a recorded pid may belong to something else entirely. A pid
# that no longer carries the expected name, or that started after we recorded
# it, is somebody else's and is left alone.
function Stop-ProcessIdsSafely {
    param(
        [int[]]$ProcessIds = @(),
        [string]$ExpectedName = '',
        [datetime]$NotStartedAfter = [datetime]::MaxValue,
        [int]$WaitSeconds = 30
    )

    $killed = New-Object System.Collections.Generic.List[int]
    foreach ($processId in @($ProcessIds)) {
        $p = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if (-not $p) { continue }
        if ($ExpectedName -and $p.Name -ne $ExpectedName) { continue }
        try { if ($p.StartTime -gt $NotStartedAfter) { continue } } catch { continue }
        # Record only what was *confirmed* reaped. Adding the pid before the
        # kill would make the caller's "Reaped leftover devenv PIDs" line claim
        # a process that is still running — a log that lies about the cleanup is
        # the same class of broken instrument as #1074's -425-year duration.
        # Anything that survives is still caught by the drain report.
        if (Stop-ProcessTreeSafely -Process $p -WaitSeconds $WaitSeconds) {
            $killed.Add($processId) | Out-Null
        }
    }
    # Bare array, not `,$array` — see Get-ProcessIdsByName. Callers wrap with @().
    return $killed.ToArray()
}

# Poll until no process named $Name is left, or the timeout elapses.
# Returns $true when the name is clear. Observes only — never kills.
#
# This answers the question the caller actually has: will the *next*
# Reinstall-Vsix.ps1 invocation trip its "Visual Studio is running" guard? That
# guard matches by name with no exclusions, so this must too.
function Wait-ProcessNameCleared {
    param(
        [Parameter(Mandatory)][string]$Name,
        [int]$TimeoutSeconds = 30
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        if (@(Get-ProcessIdsByName -Name $Name).Count -eq 0) { return $true }
        if ([DateTime]::UtcNow -ge $deadline) { return $false }
        Start-Sleep -Milliseconds 250
    }
}

<#
.SYNOPSIS
    Run `devenv /updateconfiguration` with a hard timeout that leaves nothing
    of its own running behind it.

.OUTPUTS
    [pscustomobject] with:
      TimedOut        [bool]  the process did not exit within $TimeoutSeconds
      ExitCode        [int?]  exit code, or $null if it could not be read
      DurationSeconds [double] wall-clock time, measured with a Stopwatch
      KilledPids      [int[]] ids reaped by the post-timeout sweep
      Drained         [bool]  no $ProcessName process is left on the machine.
                              Computed on every path — polled while draining
                              after a timeout, point-checked otherwise.

.NOTES
    DurationSeconds is deliberately measured with a Stopwatch rather than
    ($proc.ExitTime - $proc.StartTime): ExitTime on a process that has not
    exited returns the 1601 epoch, which is how #1074 came to log "-13430263500.8s".

    Drained is deliberately unfiltered — it reports the machine state the next
    run's guard will see, which may legitimately be $false because a developer
    has Visual Studio open. It is a report, not a kill list.
#>
function Invoke-DevenvUpdateConfiguration {
    param(
        [Parameter(Mandatory)][string]$DevenvPath,
        [string]$Arguments = '/updateconfiguration',
        [int]$TimeoutSeconds = 120,
        [string]$ProcessName = 'devenv',
        [int]$DrainTimeoutSeconds = 30
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $DevenvPath
    # .Arguments (not .ArgumentList) — see the compatibility note in the file header.
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = [System.Diagnostics.Process]::Start($psi)
    $exited = $proc.WaitForExit($TimeoutSeconds * 1000)

    $killed = @()
    if (-not $exited) {
        # Record the tree BEFORE killing anything: `taskkill /T` resolves
        # descendants at kill time, so a child whose own parent dies during the
        # kill drops out of the walk mid-flight. Capturing first closes that
        # window.
        #
        # Scope, stated plainly: this does NOT recover a process that was
        # already re-parented before the capture — once its parent is gone
        # nothing can attribute it to us, and killing it by name would mean
        # killing a developer's IDE. That case is *reported* instead, via
        # Drained, and Reinstall-Vsix.ps1 turns it into an actionable warning.
        $tree = @(Get-ProcessDescendantIds -RootPid $proc.Id -IncludeRoot)
        # Timestamp AFTER the walk, never before. Every pid in $tree was already
        # running when the walk observed it, so it necessarily started at or
        # before this instant — whereas a timestamp taken first would be earlier
        # than a descendant spawned *during* the walk, and Stop-ProcessIdsSafely
        # would then skip that pid as "too young" and leave the stray behind.
        $recordedAt = [DateTime]::Now

        Stop-ProcessTreeSafely -Process $proc -WaitSeconds $DrainTimeoutSeconds | Out-Null
        $killed = @(Stop-ProcessIdsSafely -ProcessIds $tree -ExpectedName $ProcessName -NotStartedAfter $recordedAt -WaitSeconds $DrainTimeoutSeconds)
        # We are actively draining here, so wait for it.
        $drained = Wait-ProcessNameCleared -Name $ProcessName -TimeoutSeconds $DrainTimeoutSeconds
    }
    else {
        # Measured, not assumed: a clean exit does not prove nothing was left
        # behind — /updateconfiguration spawns a second devenv, and hardcoding
        # $true here would report the one thing this field exists to deny. No
        # poll, because there is nothing to wait for on this path.
        $drained = (@(Get-ProcessIdsByName -Name $ProcessName).Count -eq 0)
    }
    $sw.Stop()

    $exitCode = $null
    try { if ($proc.HasExited) { $exitCode = $proc.ExitCode } } catch { $exitCode = $null }
    $proc.Dispose()

    return [pscustomobject]@{
        TimedOut        = (-not $exited)
        ExitCode        = $exitCode
        DurationSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
        KilledPids      = @($killed)
        Drained         = $drained
    }
}
