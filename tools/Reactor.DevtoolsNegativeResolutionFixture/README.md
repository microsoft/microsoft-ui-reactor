# Reactor Devtools negative-resolution fixture

This project intentionally references `Microsoft.UI.Reactor.Hosting.Devtools.DevtoolsMcpServer` while referencing only core `Microsoft.UI.Reactor`.
It is not part of the solution build. Invoke it through the verifier:

```powershell
dotnet run --project tools\Reactor.MstatVerifier\Reactor.MstatVerifier.csproj -c Release -- negative-resolution tools\Reactor.DevtoolsNegativeResolutionFixture\Reactor.DevtoolsNegativeResolutionFixture.csproj
```

The verifier passes only when `dotnet build` fails with a type/namespace-not-found diagnostic, proving devtools implementation types are unavailable without `Microsoft.UI.Reactor.Devtools`.

## NativeAOT status — intentionally excluded (do not migrate)

This project is deliberately **not** NativeAOT-publishable, and it must stay that
way. It is a **negative-compilation fixture**: its `Program.cs` references
`Microsoft.UI.Reactor.Hosting.Devtools.DevtoolsMcpServer` (an `internal` type that
lives in the separate `Microsoft.UI.Reactor.Devtools` assembly) while referencing
only core `Microsoft.UI.Reactor`. The contract, asserted by the
`devtools-trim-mstat` CI job, is that `dotnet build` **fails** with `CS0246`/`CS0234`.

NativeAOT publishing requires a successful `compile → IL → ILC → native link`.
This project fails at the very first step — the C# compile — so ILC never runs.
It cannot be made AOT-publishable without making it *compile*, which would destroy
its purpose, flip the negative-resolution verifier to a failure, and break the
`devtools-trim-mstat` CI lane (spec 051, [issue #497](https://github.com/microsoft/microsoft-ui-reactor/issues/497)).

This is why the project is excluded from `Reactor.slnx`, carries
`ReactorSkipAotAnalysis=true` (opting out of the repo's AOT analyzer), and is
tracked as a **documented blocker** — not a to-do — for the repo-wide NativeAOT
effort in [issue #70](https://github.com/microsoft/microsoft-ui-reactor/issues/70).

Repro (any of these — all fail before ILC):

```powershell
# 1. The fixture's designed contract (CI relies on this failing):
dotnet build tools\Reactor.DevtoolsNegativeResolutionFixture\Reactor.DevtoolsNegativeResolutionFixture.csproj -p:Platform=x64 --nologo
#   -> Program.cs(3,12): error CS0246: The type or namespace name 'DevtoolsMcpServer' could not be found

# 2. A plain publish (prerequisite for AOT) fails identically at compile:
dotnet publish tools\Reactor.DevtoolsNegativeResolutionFixture\Reactor.DevtoolsNegativeResolutionFixture.csproj -r win-x64 -p:Platform=x64 -c Release --nologo
#   -> same CS0246
```

If you are working through issue #70: **skip this project.** The correct outcome
here is a documented blocker, not a migration.
