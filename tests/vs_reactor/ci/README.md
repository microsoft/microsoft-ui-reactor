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
| `BootstrapFeedResolver.Tests.ps1` | Behavioural tests for user-scoped Microsoft npm/NuGet proxy discovery, public-default preservation, explicit overrides, and generated internal-only NuGet config. |
| `VsixFeed.Tests.ps1` | Behavioural tests for the NuGet feed plumbing on the VSIX path (`bootstrap.ps1` → `Reinstall-Vsix.ps1` → `Build-Vsix.ps1` → MSBuild), driven through a stub MSBuild that records its own command line. |

The Windows App Runtime identity rule (`tools/WindowsAppRuntimeId.ps1`) is guarded from
C# instead, in `tests/Reactor.Tests/WinAppSDKReferenceGuardTests.cs`, because it is keyed
to `Directory.Build.props` — a file that does not appear in this workflow's trigger paths,
so a version bump alone would not run these suites. Those mutations are recorded there:
making the version comparison always pass, or making the props reader return `null`, each
redden 2.

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
| `Build-Vsix.ps1` stops emitting a feed property (the VSIX-feed defect) | 3 fail — proxy, `-NuGetConfig`, and `-NuGetSource` all stop reaching MSBuild |
| `Reinstall-Vsix.ps1` stops forwarding `-NuGetConfig` | 1 fail |
| `bootstrap.ps1` stops forwarding the resolved source | 1 fail |
| attributed sweep → name-based sweep (kills any same-named process) | 2 fail — the unrelated tree is killed, `Drained` lies |
| `if ($updateConfigTimedOut) { exit 3 }` → `if ($false) { ... }` | 1 fail — the code is bound to its guard, not merely present |
| drop the trailing `exit 0` from `bootstrap.ps1` | 2 fail |
| drop the `$global:LASTEXITCODE = 0` reset | 1 fail |
| drift the `TESTING.md` exit table (`3` → `4`) | 1 fail |
| re-inject a mid-line `CR` into `Reinstall-Vsix.ps1` | 1 fail |
| re-inject an em dash into a **double**-quoted `Write-Host` string in `bootstrap.ps1` | 1 fail — the file stops loading under Windows PowerShell 5.1 |
| re-inject an arrow into a **single**-quoted `Write-Host` string in `bootstrap.ps1` | 1 fail — different character, different quote style, same defect |
| re-inject an em dash into a **single**-quoted string | 0 fail — correctly ignored; `U+201D` does not close a single-quoted literal |
| `Build-Vsix.ps1` stops validating a missing `-MSBuildPath` | 1 fail |

The AST mutations were re-run under **Windows PowerShell 5.1** as well, not just
`pwsh`, because a 5.1 leg that cannot fail is worse than no 5.1 leg: it reports
coverage it does not have. Dropping the trailing `exit 0` reddens 2 there and
neutering the `exit 3` guard reddens 1, both exiting non-zero — so the second
half of the CI matrix is load-bearing.

That leg only works because both suites read source files with
`[System.IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)`. These files are
BOM-less UTF-8 containing em dashes, and 5.1 decodes such files as the system
ANSI codepage, so every non-ASCII character arrives mangled. An AST assertion
built on that corrupted tree can pass vacuously. Do not "simplify" those reads.

The same decoding is a *product* bug whenever the non-ASCII sits inside a string
literal rather than a comment: CP1252 renders the trailing byte as a smart quote,
which PowerShell accepts as a delimiter, so the literal closes early and
cascades. Which characters bite depends on the quote style — an em dash yields
`U+201D` and breaks a *double*-quoted string, an arrow yields `U+2019` and breaks
a *single*-quoted one. Two em dashes in `Write-Host` arguments used to give
`bootstrap.ps1` **6 parse errors** under 5.1 and 0 under `pwsh` — meaning
`powershell.exe -File bootstrap.ps1` could not execute a single line, on a path
5.1 genuinely ships (bootstrap re-launches `Reinstall-Vsix.ps1` with the
*current* host, and `mur upgrade` falls back to `powershell.exe`). Those literals
are ASCII now, and section 8 re-parses every shipped script through CP1252 so
they stay that way. Non-ASCII in comments is still fine, and the guard says so:
it re-parses rather than banning bytes.

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
- **`KilledPids` recording only confirmed kills is not covered either.** The
  branch it protects needs a kill that *fails*, and every kill in these tests
  succeeds; reverting to record-before-kill leaves the suite green. Forcing a
  failed kill means either a protected process or a zero-length wait race, and a
  flaky assertion is worse than an acknowledged gap. It is kept because a log
  claiming it reaped a pid that is still running is the same broken-instrument
  class as the `-425 years` duration.

## Running locally

```pwsh
pwsh       -File tests/vs_reactor/ci/VsProcessLib.Tests.ps1
pwsh       -File tests/vs_reactor/ci/BootstrapExitCode.Tests.ps1
pwsh       -File tests/vs_reactor/ci/BootstrapFeedResolver.Tests.ps1
pwsh       -File tests/vs_reactor/ci/VsixFeed.Tests.ps1

# ...and the host `mur upgrade` falls back to:
powershell -File tests/vs_reactor/ci/VsProcessLib.Tests.ps1
powershell -File tests/vs_reactor/ci/BootstrapExitCode.Tests.ps1
powershell -File tests/vs_reactor/ci/BootstrapFeedResolver.Tests.ps1
powershell -File tests/vs_reactor/ci/VsixFeed.Tests.ps1
```

Each script prints `<n> passed, <n> failed` and exits non-zero on any failure.

**Windows PowerShell 5.1 is not optional.** `bootstrap.ps1` re-launches
`Reinstall-Vsix.ps1` with the *current* host, and `mur upgrade` falls back to
`powershell.exe` when `pwsh` is absent — so 5.1 is a shipped code path. It has
no `Process.Kill(bool)` and no `$IsWindows`, and it decodes BOM-less UTF-8 as
ANSI. Both suites therefore avoid PowerShell 6+ syntax and read source files
with an explicit UTF-8 encoding.

## Workflow

`.github/workflows/vs-reactor-lib-tests.yml` runs every suite under both hosts
on `windows-latest`, triggered by changes to `bootstrap.ps1`, the
`src/vs-reactor/` scripts, `tools/BootstrapFeedResolver.ps1`,
`src/Reactor.Cli/Upgrade/UpgradeCommand.cs` (the third consumer of the
exit-code contract), or anything under this directory.

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
