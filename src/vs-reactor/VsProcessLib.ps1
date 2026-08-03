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

    COMPATIBILITY: these functions must stay Windows PowerShell 5.1 clean.
    bootstrap.ps1 re-launches Reinstall-Vsix.ps1 with `(Get-Process -Id
    $PID).Path`, and `mur upgrade` falls back to powershell.exe when pwsh is
    absent. That rules out `Process.Kill([bool])` (.NET 5+) and
    `ProcessStartInfo.ArgumentList` (.NET Core only) as unconditional calls —
    see Stop-ProcessTreeSafely for the reflection probe + taskkill fallback.
#>

# Ids of every running process with the given name. Callers wrap with @() —
# returning a bare array (rather than the `,$array` idiom) keeps the shape
# predictable for 0, 1 and N matches.
function Get-ProcessIdsByName {
    param([Parameter(Mandatory)][string]$Name)

    return @(Get-Process -Name $Name -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
}

# Kill a process tree with `taskkill /T /F`. Split out from Stop-ProcessTreeSafely
# so the Windows PowerShell 5.1 path can be tested directly: under pwsh the
# reflection probe below always finds Kill([bool]), so this branch would
# otherwise never execute in CI.
function Stop-ProcessTreeViaTaskkill {
    param([Parameter(Mandatory)][int]$Id)

    & "$env:SystemRoot\System32\taskkill.exe" '/PID' $Id '/T' '/F' 2>&1 | Out-Null
    # taskkill returns non-zero when the process is already gone. That is not an
    # error here, and letting it linger would leak into the exit code of
    # whichever script dot-sourced us — which is defect 3 of #1074.
    $global:LASTEXITCODE = 0
}

# Terminate a process AND its descendants, then block until it is really gone.
# Returns $true when the process exited within $WaitSeconds.
function Stop-ProcessTreeSafely {
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [int]$WaitSeconds = 30
    )

    try { if ($Process.HasExited) { return $true } } catch { return $true }

    # .NET 5+ (pwsh) has Kill($entireProcessTree). Windows PowerShell 5.1 runs
    # on .NET Framework, which only has the single-process Kill() — the exact
    # overload that caused #1074. Probe by reflection and fall back to
    # `taskkill /T /F`, which walks the tree on every supported Windows.
    $killTree = $Process.GetType().GetMethod('Kill', [type[]]@([bool]))
    try {
        if ($killTree) {
            $killTree.Invoke($Process, @($true)) | Out-Null
        } else {
            Stop-ProcessTreeViaTaskkill -Id $Process.Id
        }
    } catch {
        # Raced with a natural exit, or access denied on a process owned by
        # another user. Fall through to the wait, which reports the truth.
    }

    try { return $Process.WaitForExit($WaitSeconds * 1000) } catch { return $true }
}

# Every descendant of $ParentId, to any depth, via the Win32_Process parent link.
#
# MUST be called while the tree is still alive. Windows never re-parents an
# orphan — when a parent dies, its children keep pointing at the now-dead pid —
# so once the root is killed the ancestry is unrecoverable.
#
# Returns $null (not @()) when CIM is unavailable, so a caller can tell
# "no descendants" apart from "could not tell".
function Get-DescendantProcessIds {
    param([Parameter(Mandatory)][int]$ParentId)

    try {
        $all = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop |
            Select-Object -Property ProcessId, ParentProcessId)
    } catch {
        return $null
    }

    $found = New-Object System.Collections.Generic.List[int]
    $frontier = New-Object System.Collections.Generic.List[int]
    $frontier.Add($ParentId) | Out-Null
    while ($frontier.Count -gt 0) {
        $current = $frontier[0]
        $frontier.RemoveAt(0)
        foreach ($p in $all) {
            if ($p.ParentProcessId -ne $current) { continue }
            $childId = [int]$p.ProcessId
            if ($childId -eq $current -or $found.Contains($childId)) { continue }
            $found.Add($childId) | Out-Null
            $frontier.Add($childId) | Out-Null
        }
    }
    # Bare array, not `,$array` — see Get-ProcessIdsByName. Callers wrap with @().
    return $found.ToArray()
}

# Kill the given pids, tree and all. Returns the ids it actually killed.
#
# $RequireName guards against pid reuse: between the ancestry snapshot and the
# kill, a pid we recorded may have exited and been handed to something
# unrelated. Killing by pid alone would then terminate an innocent process.
function Stop-ProcessIds {
    param(
        [int[]]$Ids = @(),
        [string]$RequireName,
        [int]$WaitSeconds = 30
    )

    $killed = New-Object System.Collections.Generic.List[int]
    foreach ($id in $Ids) {
        $p = Get-Process -Id $id -ErrorAction SilentlyContinue
        if (-not $p) { continue }
        if ($RequireName -and $p.Name -ne $RequireName) { continue }
        $killed.Add($id) | Out-Null
        Stop-ProcessTreeSafely -Process $p -WaitSeconds $WaitSeconds | Out-Null
    }
    # Bare array, not `,$array` — see Get-ProcessIdsByName. Callers wrap with @().
    return $killed.ToArray()
}

# Poll until none of $Ids is running any more, or the timeout elapses.
# Returns $true when they are all gone.
#
# This poll is the actual guarantee the caller needs: it is what makes it
# impossible for the *next* Reinstall-Vsix.ps1 invocation to trip the
# "Visual Studio is running" guard on a process this one started.
function Wait-ProcessIdsCleared {
    param(
        [int[]]$Ids = @(),
        [int]$TimeoutSeconds = 30
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        $alive = @($Ids | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
        if ($alive.Count -eq 0) { return $true }
        if ([DateTime]::UtcNow -ge $deadline) { return $false }
        Start-Sleep -Milliseconds 250
    }
}

# The Reinstall-Vsix.ps1 exit-code contract, in one place because three parties
# depend on it: Reinstall-Vsix.ps1 emits it, and bootstrap.ps1 and `mur upgrade`
# (UpgradeCommand.cs) both branch on it.
#
#   0  VSIX installed and `devenv /updateconfiguration` completed.
#   3  VSIX installed, but /updateconfiguration did not complete — it timed out,
#      or was skipped (duplicate installs detected, or devenv.exe missing).
#
# Install failures exit 1 at their failure site and never reach here.
function Get-VsixReinstallExitCode {
    param([bool]$UpdateConfigIncomplete)

    if ($UpdateConfigIncomplete) { return 3 }
    return 0
}

<#
.SYNOPSIS
    Run `devenv /updateconfiguration` with a hard timeout that leaves nothing
    of ours running behind it.

.OUTPUTS
    [pscustomobject] with:
      TimedOut        [bool]  the process did not exit within $TimeoutSeconds
      ExitCode        [int?]  exit code, or $null if it could not be read
      DurationSeconds [double] wall-clock time, measured with a Stopwatch
      KilledPids      [int[]] ids reaped after the timeout (the launched process
                              and its descendants)
      Drained         [bool]  nothing we started is still running
      ForeignPids     [int[]] processes named $ProcessName that we did NOT start
                              and deliberately left alone

.NOTES
    DurationSeconds is deliberately measured with a Stopwatch rather than
    ($proc.ExitTime - $proc.StartTime): ExitTime on a process that has not
    exited returns the 1601 epoch, which is how #1074 came to log "-13430263500.8s".

    Only processes we can prove are ours — the launched process and its
    descendants, captured from the live ancestry before the kill — are ever
    terminated. An earlier revision swept every same-named process outside a
    pre-start snapshot; because Reinstall-Vsix.ps1 refuses to run while any
    devenv is alive, that snapshot is virtually always empty, which made the
    sweep a "kill every devenv" with /F. A developer who opened Visual Studio
    during the timeout window would have lost unsaved work. Anything we cannot
    attribute to ourselves is now reported in ForeignPids instead of killed.
#>
function Invoke-DevenvUpdateConfiguration {
    param(
        [Parameter(Mandatory)][string]$DevenvPath,
        [string]$Arguments = '/updateconfiguration',
        [ValidateRange(1, 86400)][int]$TimeoutSeconds = 120,
        [string]$ProcessName = 'devenv',
        [ValidateRange(1, 86400)][int]$DrainTimeoutSeconds = 30
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
    $ours = @($proc.Id)
    $drained = $true
    if (-not $exited) {
        # Capture ancestry BEFORE killing anything — see Get-DescendantProcessIds.
        # $null means CIM could not answer; treat that as "no extra ids" rather
        # than falling back to a name sweep that could hit someone's IDE.
        $descendants = Get-DescendantProcessIds -ParentId $proc.Id
        if ($null -ne $descendants) { $ours = @($ours) + @($descendants) }

        Stop-ProcessTreeSafely -Process $proc -WaitSeconds $DrainTimeoutSeconds | Out-Null
        $killed = @(Stop-ProcessIds -Ids @($ours) -RequireName $ProcessName -WaitSeconds $DrainTimeoutSeconds)
        $drained = Wait-ProcessIdsCleared -Ids @($ours) -TimeoutSeconds $DrainTimeoutSeconds
    }
    $sw.Stop()

    $exitCode = $null
    try { if ($proc.HasExited) { $exitCode = $proc.ExitCode } } catch { $exitCode = $null }

    # Same-named processes that are not ours. Reported, never killed: on a
    # developer's machine this is their IDE.
    $foreign = @(Get-ProcessIdsByName -Name $ProcessName | Where-Object { @($ours) -notcontains $_ })

    return [pscustomobject]@{
        TimedOut        = (-not $exited)
        ExitCode        = $exitCode
        DurationSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
        KilledPids      = @($killed)
        Drained         = $drained
        ForeignPids     = @($foreign)
    }
}
