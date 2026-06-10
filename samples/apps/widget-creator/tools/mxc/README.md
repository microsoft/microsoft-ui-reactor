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
2. a local mxc checkout — `mxc/src/target/<triple>/release/` then `mxc/sdk/bin/<arch>/`
   (so MXC developers iterating on the CLI get their freshest build)
3. **this vendored copy**, shipped in the app output at `mxc/<rid>/`

## Refreshing

Re-copy from an mxc SDK build:

```powershell
Copy-Item <mxc>/sdk/bin/arm64/* samples/apps/widget-creator/tools/mxc/win-arm64/ -Force
```

> These are prebuilt binaries from a separate repository. Keep them in sync with a
> known-good mxc build, and confirm redistribution is allowed before publishing.
