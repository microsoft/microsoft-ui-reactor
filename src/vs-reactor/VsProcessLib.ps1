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
            & "$env:SystemRoot\System32\taskkill.exe" '/PID' $Process.Id '/T' '/F' 2>&1 | Out-Null
            # taskkill returns non-zero when the process is already gone. That
            # is not an error here, and letting it linger would leak into the
            # exit code of whichever script dot-sourced us.
            $global:LASTEXITCODE = 0
        }
    } catch {
        # Raced with a natural exit, or access denied on a process owned by
        # another user. Fall through to the wait, which reports the truth.
    }

    try { return $Process.WaitForExit($WaitSeconds * 1000) } catch { return $true }
}

# Kill every process named $Name whose id is not in $ExcludePids, tree and all.
# Returns the ids it killed.
#
# This is the belt to Stop-ProcessTreeSafely's braces: Kill($true) enumerates
# descendants at kill time, so a grandchild that has already re-parented can be
# missed. $ExcludePids carries the pre-start snapshot, so processes that were
# running before we started are never touched.
function Stop-NewProcessesByName {
    param(
        [Parameter(Mandatory)][string]$Name,
        [int[]]$ExcludePids = @(),
        [int]$WaitSeconds = 30
    )

    $killed = New-Object System.Collections.Generic.List[int]
    foreach ($p in @(Get-Process -Name $Name -ErrorAction SilentlyContinue)) {
        if ($ExcludePids -contains $p.Id) { continue }
        $killed.Add($p.Id) | Out-Null
        Stop-ProcessTreeSafely -Process $p -WaitSeconds $WaitSeconds | Out-Null
    }
    # Bare array, not `,$array` — see Get-ProcessIdsByName. Callers wrap with @().
    return $killed.ToArray()
}

# Poll until no process named $Name (excluding $ExcludePids) is left, or the
# timeout elapses. Returns $true when the name is clear.
#
# This poll is the actual guarantee the caller needs: it is what makes it
# impossible for the *next* Reinstall-Vsix.ps1 invocation to trip the
# "Visual Studio is running" guard on a process this one started.
function Wait-ProcessNameCleared {
    param(
        [Parameter(Mandatory)][string]$Name,
        [int[]]$ExcludePids = @(),
        [int]$TimeoutSeconds = 30
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        $remaining = @(Get-Process -Name $Name -ErrorAction SilentlyContinue |
            Where-Object { $ExcludePids -notcontains $_.Id })
        if ($remaining.Count -eq 0) { return $true }
        if ([DateTime]::UtcNow -ge $deadline) { return $false }
        Start-Sleep -Milliseconds 250
    }
}

<#
.SYNOPSIS
    Run `devenv /updateconfiguration` with a hard timeout that leaves nothing
    running behind it.

.OUTPUTS
    [pscustomobject] with:
      TimedOut        [bool]  the process did not exit within $TimeoutSeconds
      ExitCode        [int?]  exit code, or $null if it could not be read
      DurationSeconds [double] wall-clock time, measured with a Stopwatch
      KilledPids      [int[]] ids reaped by the post-timeout sweep
      Drained         [bool]  no $ProcessName process was left behind

.NOTES
    DurationSeconds is deliberately measured with a Stopwatch rather than
    ($proc.ExitTime - $proc.StartTime): ExitTime on a process that has not
    exited returns the 1601 epoch, which is how #1074 came to log "-13430263500.8s".
#>
function Invoke-DevenvUpdateConfiguration {
    param(
        [Parameter(Mandatory)][string]$DevenvPath,
        [string]$Arguments = '/updateconfiguration',
        [int]$TimeoutSeconds = 120,
        [string]$ProcessName = 'devenv',
        [int]$DrainTimeoutSeconds = 30
    )

    # Snapshot first. Reinstall-Vsix.ps1 refuses to run while any devenv is
    # alive, so in practice this is empty — but if a developer launches VS
    # mid-run we must not kill their IDE.
    $preexisting = @(Get-ProcessIdsByName -Name $ProcessName)

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $DevenvPath
    # .Arguments (not .ArgumentList) — see the compatibility note in the file header.
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = [System.Diagnostics.Process]::Start($psi)
    $exited = $proc.WaitForExit($TimeoutSeconds * 1000)

    $killed = @()
    $drained = $true
    if (-not $exited) {
        Stop-ProcessTreeSafely -Process $proc -WaitSeconds $DrainTimeoutSeconds | Out-Null
        $killed = @(Stop-NewProcessesByName -Name $ProcessName -ExcludePids $preexisting -WaitSeconds $DrainTimeoutSeconds)
        $drained = Wait-ProcessNameCleared -Name $ProcessName -ExcludePids $preexisting -TimeoutSeconds $DrainTimeoutSeconds
    }
    $sw.Stop()

    $exitCode = $null
    try { if ($proc.HasExited) { $exitCode = $proc.ExitCode } } catch { $exitCode = $null }

    return [pscustomobject]@{
        TimedOut        = (-not $exited)
        ExitCode        = $exitCode
        DurationSeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
        KilledPids      = @($killed)
        Drained         = $drained
    }
}
