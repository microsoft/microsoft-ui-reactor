# Vendored MXC sandbox runtime

Widget Creator runs each generated widget inside an [MXC](https://github.com/microsoft/mxc)
sandbox via the native `wxc-exec` CLI. MXC does not ship a NuGet package, so the
prebuilt binaries are vendored here and copied next to the app at build time
(into `mxc/<rid>/` in the output directory). This lets Widget Creator run
sandboxed straight after `git clone` + `dotnet run`, with no external mxc checkout.

## Layout

```
tools/mxc/
  win-arm64/   # ARM64 build of wxc-exec.exe + helper binaries
  win-x64/     # (add when an x64 build is available)
```

Each `<rid>` folder must contain the **full** runtime set, because `wxc-exec`
launches its helpers (sandbox daemon/guest, proxy shim) as siblings:

- `wxc-exec.exe`
- `wxc-host-prep.exe`
- `wxc-windows-sandbox-daemon.exe`
- `wxc-windows-sandbox-guest.exe`
- `winhttp-proxy-shim.exe`
- `wxc-test-proxy.exe`

## Resolution order

`MxcSandbox.ResolveWxcExec()` picks the first match of:

1. `WIDGET_CREATOR_WXC_EXEC` (explicit path) / `WIDGET_CREATOR_MXC_BIN` (bin dir)
2. **this vendored copy**, shipped in the app output at `mxc/<rid>/` (the default —
   pinned and known to auto-fall back off BaseContainer)
3. a local mxc checkout — `mxc/src/target/<triple>/release/` then `mxc/sdk/bin/<arch>/`
   — **only when `WIDGET_CREATOR_USE_LOCAL_MXC=1`** (so MXC developers iterating on
   the CLI can opt in to their freshest build). Off by default so a stale or
   uncontrolled local build cannot silently replace the pinned sandbox.

## Refreshing

The vendored set is currently **MXC SDK v0.7.0** (`v0.7.0-rc1`, `wxc-exec` reports
`0.7.0+0e02a72`), taken from the published release binaries.

Preferred — pull the arch folders straight from the GitHub release:

```powershell
gh release download v0.7.0-rc1 --repo microsoft/mxc --pattern mxc-release-binaries.zip
# the zip contains arm64/ and x64/ — copy the six binaries below out of each:
#   arm64/* -> samples/apps/widget-creator/tools/mxc/win-arm64/
#   x64/*   -> samples/apps/widget-creator/tools/mxc/win-x64/
```

Or re-copy from a local mxc SDK build:

```powershell
Copy-Item <mxc>/sdk/bin/arm64/* samples/apps/widget-creator/tools/mxc/win-arm64/ -Force
```

> **Integrity pins (C-3).** The app verifies these binaries against compiled-in
> SHA-256 hashes before running them (`Services/MxcBinaryManifest.cs`). Whenever you
> refresh the binaries you **must** recompute and update those hashes in the same
> change, or the sandbox will refuse to launch:
>
> ```powershell
> Get-FileHash samples\apps\widget-creator\tools\mxc\win-arm64\*.exe -Algorithm SHA256
> Get-FileHash samples\apps\widget-creator\tools\mxc\win-x64\*.exe   -Algorithm SHA256
> ```

> These are prebuilt binaries from a separate repository. Keep them in sync with a
> known-good mxc build, and confirm redistribution is allowed before publishing.
