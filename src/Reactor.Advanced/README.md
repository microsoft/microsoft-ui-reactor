# Microsoft.UI.Reactor.Advanced

`Microsoft.UI.Reactor.Advanced` hosts optional Reactor components that depend on heavier native or graphics stacks. Its first surface is a Win2D canvas family for immediate-mode drawing inside a Reactor element tree.

The package is separate from `Microsoft.UI.Reactor` so apps that do not use Win2D keep their trim/AOT closure and native payload isolated.

## Links

- Guide: `docs/guide/win2d-canvas.md` (lands in Phase 3)
- Sample: `samples/apps/particle-storm/` (lands in Phase 2)

## Baseline Win2D trim/AOT warnings

Phase 1B baseline command:

```powershell
dotnet publish src/Reactor.Advanced/Reactor.Advanced.csproj -p:PublishTrimmed=true -p:Platform=x64 -r win-x64 -c Release
```

Current baseline: **0 warnings**. `Reactor.Advanced` is a library, so the project treats `PublishTrimmed` as a local project property and publishes as a trimmable/AOT-compatible library (`IsTrimmable=true`, `IsAotCompatible=true`) instead of invoking ILLink as if the library were an executable root.
