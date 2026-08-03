# VS extension script tests

Headless, dependency-free PowerShell tests for the scripts that install the
Reactor Preview VSIX. No build, no `dotnet`, no Visual Studio — the "devenv"
under test is a renamed copy of `cmd.exe`.

These guard [issue #1074](https://github.com/microsoft/microsoft-ui-reactor/issues/1074):
on Visual Studio 18.8 `devenv /updateconfiguration` never returns, and three
defects in our own scripts turned that upstream hang into a red
`bootstrap.ps1` CI job.

| Defect | Symptom | Guarded by |
|---|---|---|
| `Process.Kill()` (no argument) killed only the top `devenv` and returned without waiting | An orphaned `devenv` tripped the next run's "Visual Studio is running" guard | `VsProcessLib.Tests.ps1` cases 3, 5 |
| Duration read from `ExitTime` on a live process | Logged `-13430263500.8s` (≈ −425 years) | `VsProcessLib.Tests.ps1` case 5 (bounds check — see the limit below) |
| `Write-Host` doesn't clear `$LASTEXITCODE` | The VSIX child's `1` leaked into `bootstrap.yml`'s `if ($LASTEXITCODE -ne 0) { throw }` | `BootstrapExitCode.Tests.ps1` cases 2–3 |

## Files

| File | Role |
|---|---|
| `VsProcessLib.Tests.ps1` | Behavioural tests for `src/vs-reactor/VsProcessLib.ps1` — timeout, process-tree kill, ownership attribution, drain, and the exit-code mapping. Spawns real hanging process trees. |
| `BootstrapExitCode.Tests.ps1` | Reproduces the `$LASTEXITCODE` leak in-process, then asserts the structural contract between `bootstrap.ps1`, `Reinstall-Vsix.ps1`, and `mur upgrade`. |

Both scripts exit non-zero on any failed assertion and skip cleanly on
non-Windows.

## Running locally

```powershell
pwsh tests/vs_reactor/ci/VsProcessLib.Tests.ps1
pwsh tests/vs_reactor/ci/BootstrapExitCode.Tests.ps1
```

Each takes well under a minute. They spawn processes named
`reactor-fake-devenv` and clean them up in a `finally` block; a deliberately
distinct name keeps them from ever targeting `pwsh` or a real `devenv`.

## Workflow

| Workflow | Trigger | Runner |
|---|---|---|
| `.github/workflows/vs-reactor-lib-tests.yml` | `push` / `pull_request` touching `bootstrap.ps1`, `src/vs-reactor/{VsProcessLib,Reinstall-Vsix}.ps1`, `tests/vs_reactor/ci/**`, or the workflow itself | `windows-latest` |

`windows-latest` rather than the cheaper `ubuntu-latest` used by the sibling
`*-lib-tests` workflows, because every behaviour under test — process-tree
kill, `taskkill`, `Win32_Process` ancestry, `Get-Process` by name — is
Windows-specific.

## Why the tests spawn real processes

A mock can't reproduce the defect. `Kill()` vs `Kill($true)` differ only in
what happens to a *real* child process, so the fake is a copy of `cmd.exe`
renamed `reactor-fake-devenv.exe`, launched as
`/c "<fake>" /c ping -n 600 127.0.0.1`. That builds a genuine two-level,
same-named, hanging tree. Case 2 is the positive control: it asserts the
harness really produced ≥ 2 processes, because if it only ever made one, every
"tree" assertion below it would be satisfiable by a single-process kill and
would prove nothing.

## Ownership attribution

The riskiest thing these scripts do is kill a process called `devenv` — which
is also the name of the developer's IDE. Ownership is therefore established
from the `Win32_Process` parent chain, captured **while the tree is still
alive** (Windows does not re-parent orphans, so after the root dies the
ancestry is unrecoverable). Anything not attributable to us is reported in
`ForeignPids` and left alone.

Case 8 asserts this end-to-end through `Invoke-DevenvUpdateConfiguration`, not
just against the helpers. That distinction is load-bearing: an earlier revision
of these tests exercised the helpers directly, and swapping the ancestry lookup
for a name match left the whole suite green while the product would have
`/F`-killed an open IDE.

## Known limit, stated rather than papered over

The `-425 years` duration is a race on reading `Process.ExitTime` before the OS
reaps a *heavy* process. A `cmd.exe` fake dies sub-millisecond, so the race
never opens and swapping the `Stopwatch` back for `ExitTime - StartTime` does
**not** redden this suite. The Stopwatch is kept because it cannot be wrong,
but the duration assertion in case 5 is a bounds check on the symptom, not a
discriminator between the two implementations. The orphan, attribution, and
exit-code assertions **are** mutation-verified.
