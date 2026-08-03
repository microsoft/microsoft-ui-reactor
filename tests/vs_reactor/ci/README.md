# VS extension install-script tests

Headless tests for the PowerShell that installs the Reactor Preview VSIX. No
build, no `dotnet`, no Visual Studio: the `devenv` under test is a renamed copy
of `cmd.exe`, so the whole suite runs in seconds on a bare runner.

These exist because of [#1074](https://github.com/microsoft/microsoft-ui-reactor/issues/1074).
On Visual Studio **18.8**, `devenv /updateconfiguration` never returns. That is
upstream behaviour we cannot fix — but three defects in *our* scripts turned it
into a red `bootstrap.ps1 (windows-latest)` job, and those are what this suite
pins down.

## Files

| File | Role |
|---|---|
| `VsProcessLib.Tests.ps1` | Behavioural tests for `src/vs-reactor/VsProcessLib.ps1` against a real, genuinely-hanging, two-level process tree: tree kill, attributed sweep, drain poll, pid-reuse guards, `$LASTEXITCODE` hygiene. |
| `BootstrapExitCode.Tests.ps1` | Contract tests for the `bootstrap.ps1` ↔ `Reinstall-Vsix.ps1` exit-code handshake: the `$LASTEXITCODE` leak, the guarded `exit 1` / `exit 3` paths, and agreement between the code and both documented exit tables. |

The library under test lives in `src/vs-reactor/`, not here, because
`Reinstall-Vsix.ps1` dot-sources it at runtime — it ships with the script rather
than with the tests.

## What the assertions are worth

Every load-bearing oracle in this suite has been mutation-checked. A green run
means nothing on its own; what follows is the evidence that these tests can go
red.

| Mutation | Result |
|---|---|
| `taskkill /T` → bare `$Process.Kill()` (the original defect) | 2 fail — the inner fake survives |
| attributed sweep → name-based sweep (kills any same-named process) | 2 fail — the unrelated tree is killed, `Drained` lies |
| `if ($updateConfigTimedOut) { exit 3 }` → `if ($false) { ... }` | 1 fail — the code is bound to its guard, not merely present |
| drop the trailing `exit 0` from `bootstrap.ps1` | 2 fail |
| drop the `$global:LASTEXITCODE = 0` reset | 1 fail |
| drift the `TESTING.md` exit table (`3` → `4`) | 1 fail |
| re-inject a mid-line `CR` into `Reinstall-Vsix.ps1` | 1 fail |

That last row earns its place: a bare `LF` inserted into a `CRLF` file leaves a
mid-line carriage return that merges the next statement onto the brace line. It
is still valid PowerShell, so it survived the parse check *and* all fifty
behavioural assertions, and is near-invisible in a diff. It reached `main`'s
review queue once; now it cannot.

Two known limits, recorded rather than papered over:

- **The duration assertion does not discriminate.** The issue's `-13430263500.8s`
  comes from reading `Process.ExitTime` before the OS reaps a *heavy* process. A
  `cmd.exe` fake dies sub-millisecond, so swapping the `Stopwatch` back for
  `ExitTime - StartTime` does **not** redden the suite. The `Stopwatch` is kept
  because it cannot be wrong; the assertion is a bounds check on the symptom.
- **The `$LASTEXITCODE` reset in `Stop-ProcessTreeSafely` is not independently
  provable.** Forcing `taskkill`'s already-gone status means racing the
  `HasExited` check. The test asserts the boundary contract instead.

## Running locally

```pwsh
pwsh       -File tests/vs_reactor/ci/VsProcessLib.Tests.ps1
pwsh       -File tests/vs_reactor/ci/BootstrapExitCode.Tests.ps1

# ...and the host `mur upgrade` falls back to:
powershell -File tests/vs_reactor/ci/VsProcessLib.Tests.ps1
powershell -File tests/vs_reactor/ci/BootstrapExitCode.Tests.ps1
```

Each script prints `<n> passed, <n> failed` and exits non-zero on any failure.

**Windows PowerShell 5.1 is not optional.** `bootstrap.ps1` re-launches
`Reinstall-Vsix.ps1` with the *current* host, and `mur upgrade` falls back to
`powershell.exe` when `pwsh` is absent — so 5.1 is a shipped code path. It has
no `Process.Kill(bool)` and no `$IsWindows`, and it decodes BOM-less UTF-8 as
ANSI. Both suites therefore avoid PowerShell 6+ syntax and read source files
with an explicit UTF-8 encoding.

## Workflow

`.github/workflows/vs-reactor-lib-tests.yml` runs both suites under both hosts
on `windows-latest`, triggered by changes to `bootstrap.ps1`, the two
`src/vs-reactor/` scripts, `src/Reactor.Cli/Upgrade/UpgradeCommand.cs` (the
third consumer of the exit-code contract), or anything under this directory.

The tests spawn real processes and kill them by pid. They are safe to run on a
developer machine, and safe to run concurrently with each other: the fake is
named `reactor-fake-devenv-<pid>`, so each run only ever sees its own
processes, nothing is matched by the name `devenv` or `pwsh`, and the suite
tears its own processes down in a `finally` block.

The pid namespacing is not cosmetic. `Get-Process -Name` is machine-wide, so a
shared fake name lets two overlapping runs kill each other's fixtures — which
reproduces as "the process died early" failures that look like product bugs and
are not. That is the same attribution mistake the product fix above corrects, so
the harness is held to it too.
