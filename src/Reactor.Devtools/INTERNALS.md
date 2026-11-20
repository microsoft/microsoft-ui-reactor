# Reactor Devtools — In-App Preview Host

> **Deprecated.** This package is replaced by the WinUI DevTools in the winapp CLI
> (`winapp run . --devtools on`). It stays, and keeps working, only for the component preview host,
> the capture server and the `--embed` handshake that the VS Code live preview and the Visual
> Studio embedded preview need, until winapp has its own previewer. The `mur devtools` CLI, this
> package's MCP server (with its lockfile, call log and `--mcp-*` / `--devtools-log*` flags) and the
> in-app dev menu (`DevtoolsMenu`, `UseDevtools`) have been **removed**.

This folder implements the `--devtools run` / `--preview` runtime path: `DevtoolsHost` mounts the
previewed component, and with `--vscode` (implied by `--embed`) `PreviewCaptureServer` serves
`/frame`, `/status`, `/focus`, `/components`, `/preview`, `/references` and, in embed mode,
`/hwnd` and `/embed/*`. `TreeWalker`, `NodeIdBuilder` and `ReferenceOverlay` remain only to build
the `/references` payload.

## Security model

The preview host is **developer-loop only**. It is not a production endpoint and ships with three
hard gates:

1. **Opt in at build time and launch time.**
   `<RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport" Value="true" Trim="true" />`
   is the build-time capability gate; `--devtools run` (or `--preview <Name>`) is the launch-time
   gate, and the capture server starts only with `--vscode` or `--embed`. Samples keep the switch
   Debug-conditional so Release/AOT builds have no devtools surface at all.
2. **Loopback-only binding.** The capture server's `HttpListener` binds to
   `http://127.0.0.1:{port}/` and is never exposed on a non-loopback adapter.
3. **Per-launch bearer token.** Every request, including `/status`, must carry
   `Authorization: Bearer <CAPTURE_TOKEN>`; the token is printed on stdout at startup for the
   launching editor. State-changing endpoints (`/focus`, `/preview`, `/embed/*`) are POST-only and
   `/preview` requires `application/json`, so a browser page can't drive them with a simple
   cross-origin request.
## Wire protocol versioning

Embedded-preview HTTP endpoints advertise their protocol in `/status` as
`"embed-v1"`. Any breaking change to the embed endpoints must bump the suffix
(for example, `embed-v2`), and the Visual Studio extension must reject unknown
protocol values rather than guessing compatibility.

## ETW trace correlation

Reactor emits a TraceLogging-style `EventSource` at `Microsoft-UI-Reactor`
(see `../Reactor/Core/Diagnostics/ReactorEventSource.cs`). Hot-path emit sites are
guarded by call-site `IsEnabled()` checks, so when no listener is attached
the disabled path does no Stopwatch, type-name, or event-method work beyond
a single word-sized flag read. Events cover reconcile passes, component
render start/stop with trigger (self/parent), effects flush, child
reconcile, state writes, unmounts, and render errors. (The MCP call start/stop
events and the `Mcp` keyword are reserved but no longer emitted.)

Keywords: `Reconcile=0x1`, `Render=0x2`, `State=0x4`, `Mcp=0x8`,
`Lifecycle=0x10`, `Errors=0x20`. Full keyword mask = `0x3F`.

Provider GUIDs:

- `Microsoft-UI-Reactor` → `{2BA6BC23-ABF9-56DE-3922-8CC701F16EDE}` (name-hashed by EventSource)
- `Microsoft-Windows-XAML` → `{531A35AB-63CE-4BCF-AA98-F88C7A89E455}` (WinUI 3 core, native ETW)

### Required: capture via `xperf` (not `dotnet-trace`)

The WinUI core provider is a **native ETW provider** emitted from WinUI's C++
code. `dotnet-trace` uses the managed EventPipe transport and **cannot
surface native ETW providers** — it will silently drop `Microsoft-Windows-XAML`
events and you will get a Reactor-only trace. To get the correlated
Reactor + XAML timeline you need a classic ETW session via `xperf`, `wpr`,
`logman`, or PerfView.

Recipe (xperf ships with the Windows Performance Toolkit at
`C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\xperf.exe`):

```bat
set XPERF="C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\xperf.exe"

%XPERF% -start ReactorSession ^
    -on "2BA6BC23-ABF9-56DE-3922-8CC701F16EDE:0x3F:5+531A35AB-63CE-4BCF-AA98-F88C7A89E455:0x9240:5" ^
    -f trace.etl -BufferSize 1024 -MinBuffers 32 -MaxBuffers 256

REM …launch / exercise the app…

%XPERF% -stop ReactorSession
```

The `0x9240` mask on `Microsoft-Windows-XAML` enables **Layout + Rendering +
Input + DComp** keywords. Verbose level (`5`) is recommended so the full
frame-timing / composition events come through.

### Viewing the trace

`.etl` opens natively in **PerfView** (github.com/microsoft/perfview) and
**WPA** (`C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\wpa.exe`).
PerfView is recommended for CLR + ETW correlation; WPA for composition /
GPU / CPU-sampling views.

For a web-based timeline (Perfetto / Firefox Profiler / Speedscope /
`chrome://tracing`) the ETW stream needs to be converted to Chromium JSON
— `dotnet-trace convert --format Chromium` only handles CPU samples, not
custom `EventSource` events, so a custom `.etl`-to-Chromium converter is
required. One is tracked separately; when it lands, swap the `.etl` path
into it and the resulting JSON drops into <https://ui.perfetto.dev/> directly.
