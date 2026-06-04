# Reactor Devtools negative-resolution fixture

This project intentionally references `Microsoft.UI.Reactor.Hosting.Devtools.DevtoolsMcpServer` while referencing only core `Microsoft.UI.Reactor`.
It is not part of the solution build. Invoke it through the verifier:

```powershell
dotnet run --project tools\Reactor.MstatVerifier\Reactor.MstatVerifier.csproj -c Release -- negative-resolution tools\Reactor.DevtoolsNegativeResolutionFixture\Reactor.DevtoolsNegativeResolutionFixture.csproj
```

The verifier passes only when `dotnet build` fails with a type/namespace-not-found diagnostic, proving devtools implementation types are unavailable without `Microsoft.UI.Reactor.Devtools`.
