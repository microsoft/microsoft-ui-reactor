
# DevTools Internals

What is left of Reactor's own devtools is the preview host in the deprecated
`Microsoft.UI.Reactor.Devtools` package. The VS Code live preview and the Visual
Studio embedded preview run on it. Everything else is the WinUI DevTools in the
winapp CLI.

The Microsoft.UI.Reactor (Reactor) preview host mounts a single component, or the
whole app, in a window. When an editor asks for it, the host runs a loopback
capture server that streams frames, lists components, switches the previewed
component, draws the reference graph, and hands the window to Visual Studio for
embedding. Inspecting and editing a running app, UI automation, logs, and agents
are the job of the WinUI DevTools (`winapp run . --devtools on`), which attach
from outside the app and need nothing in your project. The preview host is
zero-cost in retail: without the `Reactor.DevtoolsSupport` switch the package's
code is trimmed and a `--devtools` argument only prints how to enable it.

> **Deprecated.** This page describes the in-app preview host in the
> `Microsoft.UI.Reactor.Devtools` package. Its MCP server, its in-app dev menu
> (`DevtoolsMenu`, `UseDevtools`) and the `mur devtools` CLI have been **removed**; use
> the WinUI DevTools in the winapp CLI (`winapp run . --devtools on`) — see
> [Devtools](devtools.md#move-from-mur-devtools) for the mapping. The package stays
> (deprecated) until winapp has its own previewer, because the VS Code live preview and
> the Visual Studio embedded preview run on it; this page goes with it. The
> `Microsoft-UI-Reactor` ETW provider stays.

[Devtools](devtools.md) is the user-facing companion; this is the internals
view.

![The in-app preview host: the VS Code live preview, the Visual Studio embedded preview and the doc screenshot pipeline launch the app, read CAPTURE_PORT and CAPTURE_TOKEN, and call the bearer-authed PreviewCaptureServer, which hops to the UI thread to capture frames, switch the previewed component or walk the reference graph. The MCP server, the in-app dev menu and mur devtools are removed in favour of the winapp CLI.](images/devtools-internals/preview-host.svg)

## Reference

| Subsystem | Owner type | Source | Gate |
|---|---|---|---|
| Host registration | `ModuleInit` → `ReactorDevtoolsBootstrap.Register` | `src/Reactor.Devtools/ModuleInit.cs` | Package referenced |
| Launch parsing | `DevtoolsCliParser` | `src/Reactor/Hosting/Devtools/DevtoolsCliParser.cs` | `--devtools run`/`list`, `--preview`, `--preview-list` |
| Preview host | `DevtoolsHost` | `src/Reactor.Devtools/DevtoolsHost.cs` | Build-time `Reactor.DevtoolsSupport` switch |
| Capture server | `PreviewCaptureServer` | `src/Reactor.Devtools/PreviewCaptureServer.cs` | `--vscode` (implied by `--embed`) + per-launch bearer token |
| Reference graph | `ReferenceOverlay` | `src/Reactor.Devtools/ReferenceOverlay.cs` | `GET /references` |
| Overlays | `ReactorFeatureFlags` | `src/Reactor/Core/` | Set from code |

## The gate

```csharp
var options = DevtoolsCliParser.Parse(args);

var error = DevtoolsCliParser.RemovedSubverbMessage(args)
    ?? (options.PreviewAndDevtoolsConflict ? "pass either --devtools or --preview, not both." : null)
    ?? options.EmbedValidationError;
if (!string.IsNullOrEmpty(error))
{
    Console.Error.WriteLine($"[reactor] {error}");
    Environment.ExitCode = 2;
    if (exitOnUnavailable) Environment.Exit(2);
    return true;
}

if (DevtoolsCliParser.RemovedAppVerbNotice(args) is { } appNotice)
    Console.Error.WriteLine($"[reactor] {appNotice}");

if (options.Subverb is null) return false;

if (DevtoolsCliParser.RemovedFlagsNotice(args) is { } ignored)
    Console.Error.WriteLine($"[reactor] {ignored}");

if (ReactorFeatures.IsDevtoolsSupported && ReactorDevtoolsBootstrap.Current is { } host)
{
    var request = new ReactorDevtoolsBootRequest(
        options,
        title,
        options.WindowWidth ?? width,
        options.WindowHeight ?? height,
        fullScreen,
        hostRoot,
        hostRootFactory,
        rootRenderFunc,
        configure)
    {
        RootMountSite = rootMountSite,
    };
    // Checked here, not in the package: a Devtools package from another release can fail to
    // JIT TryHandleCommandLine against this core and would crash before its own check ran.
    if (DevtoolsPackageMismatchMessage(host.GetType().Assembly) is { } mismatch)
    {
        Console.Error.WriteLine(mismatch);
        Environment.ExitCode = 2;
        if (exitOnUnavailable) Environment.Exit(2);
        return true;
    }
    return host.TryHandleCommandLine(request);
}

EmitDevtoolsNotAvailableMessage();
Environment.ExitCode = 2;
if (exitOnUnavailable) Environment.Exit(2);
return true;
```

Two signals combine. The build-time signal is the `Reactor.DevtoolsSupport`
runtime host configuration switch in the app project (`ReactorFeatures.IsDevtoolsSupported`);
ship a Release binary without it and the package's code is trimmed away. The
launch-time signal is the command line: `--devtools run [<Component>]` or
`--preview <Component>` runs the preview host, `--devtools list` /
`--preview-list` writes the component list and exits. `ReactorApp.Run` parses the
arguments with `DevtoolsCliParser` before it creates a window. With no devtools
argument it returns at once and the app starts normally; with one but without the
switch or the package, it prints how to enable them and exits `2`.

The verbs that went with the MCP server and the dev menu — `--devtools app`,
`--devtools screenshot`, `--devtools tree` — are still recognised, so they don't
fall through to `run` with the verb read as a component name. `app` used to run
the app with the dev menu, so it is ignored with a notice and the app runs
normally; `screenshot` and `tree` print the winapp command to use and exit `2`.
So does winapp's own `--devtools on|off|headless` when it is passed to the app
instead of to `winapp run`.

```csharp
/// <summary>
/// The error for a <c>--devtools</c> value the app no longer accepts, or null: the headless
/// verbs removed with the MCP server, and winapp's own <c>on</c> / <c>off</c> /
/// <c>headless</c>, which belong on <c>winapp run</c>. Without this they would fall through to
/// <c>run</c> with the value read as a component name.
/// </summary>
internal static string? RemovedSubverbMessage(string[] args) => TokenAfterDevtools(args) switch
{
    "screenshot" => "'--devtools screenshot' was removed. Use: winapp ui screenshot -a <app>",
    "tree" => "'--devtools tree' was removed. Use: winapp devtools inspect",
    var value when value is "on" or "off" or "headless" =>
        $"'--devtools {value}' is a winapp option, not an app argument. Use: winapp run . --devtools {value}",
    _ => null,
};

/// <summary>
/// The notice for <c>--devtools app</c>, or null. It used to run the app with the in-app dev
/// menu. The menu is gone, so the app now runs normally (a launch profile that passes it keeps
/// working) and the notice names the replacement.
/// </summary>
internal static string? RemovedAppVerbNotice(string[] args) => TokenAfterDevtools(args) == "app"
    ? "Ignoring '--devtools app': the in-app dev menu was removed, so the app runs normally. Inspect it with the WinUI DevTools: winapp run . --devtools on"
    : null;
```

`ReactorApp.DevtoolsEnabled` is true while the preview host runs. Source
mapping keys on it, which is how the VS Code panel can jump from an element to its
call site.

## The preview host

`DevtoolsHost` registers itself from a module initializer, so referencing the
package is enough; `ReactorDevtools.EnsureRegistered()` runs the registration
explicitly, before `ReactorApp.Run`, for apps that must not depend on module-initializer
order (the NativeAOT sample does this). On `run` it mounts the requested
component (or the app's root when none is named) in a window sized by the app's
`ReactorApp.Run` call, or by `--width`/`--height`/`--x`/`--y` when given.

## The capture server

When an editor launches the app with `--vscode` (the VS Code panel does this;
`--embed` implies it), the host starts `PreviewCaptureServer`, a loopback
`HttpListener` on a free port. It prints `CAPTURE_PORT=<port>` and
`CAPTURE_TOKEN=<token>` on stdout and requires `Authorization: Bearer <token>`
on every request, including `/status`:

```csharp
// SECURITY (TASK-018): require bearer auth on every endpoint.
if (!BearerMatches(ctx.Request.Headers["Authorization"]))
{
    response.StatusCode = 401;
    response.Headers.Add("WWW-Authenticate", "Bearer realm=\"reactor-preview\"");
    response.Close();
    return;
}
```

> **Caveat:** The bearer token is the only thing between the capture server and any other
> process running as the same user: the port is loopback-only but discoverable.
> Don't log `CAPTURE_TOKEN` anywhere persistent, don't relax the check for
> "read-only" endpoints such as `/frame` (a frame can show anything on screen), and
> keep state-changing endpoints (`/focus`, `/preview`, `/embed/*`) POST-only, and
> `/preview` and `/embed/*` on `application/json`, so a browser page can't drive
> them with a simple cross-origin request (the required `Authorization` header
> already forces a preflight).

| Endpoint | Used by | What it does |
|---|---|---|
| `GET /frame` | VS Code panel | Latest captured frame as JPEG (the doc screenshot pipeline captures with winapp instead) |
| `GET /status` | Both extensions | Liveness, port, frame rate and embed protocol version |
| `POST /focus` | VS Code panel | Brings the preview window forward |
| `GET /components` | Both extensions | The app's previewable components |
| `POST /preview` | VS Code panel, doc screenshot pipeline | Switches the previewed component |
| `GET /references` | VS Code panel | The reference graph (below) |
| `GET /hwnd`, `POST /embed/*` | Visual Studio | The `--embed` handshake: the window handle, then ack, resize, move and release (embed mode only) |

## The reference-graph overlay

`GET /references` walks the visual tree and returns the app's reactive reference
graph: the reference edges every control declares through
`descriptor.Reference`/`.ReferenceList`, the `binding.Reference` bridge, and the
modifier-level edges (`.LabeledBy`, `.DescribedBy`, `.FlowsTo`/`.FlowsFrom`,
`.XYFocusUp/Down/Left/Right`). Each edge is emitted as
`{from, to, label, slot, kind, resolved}` keyed to node ids, plus diagnostics for
**cycles** and **unresolved** (perpetually-null) references. Cycles are a supported
topology — push resolution converges on them by design — so they are reported
informationally, not as errors. The edges live in each control's own
`ReactorState.ReferenceEdges` bag; `ReferenceOverlay` reads those bags after the
`TreeWalker` records its element→id map, so it never re-walks or re-renders. The
build hops the UI dispatcher first, because reading the per-control reference
state touches attached dependency properties that only the UI thread may read.
The payload serializes through a source-generated `JsonSerializerContext`, so it
stays NativeAOT-safe.

## Overlays

The reconcile-highlight overlay is driven by
`ReactorFeatureFlags.HighlightReconcileChanges`, a simple `static bool` property
an app sets from code (for example from a Debug-only menu item). The overlay paints
a brief flash on every element the reconciler patched on the most recent render. It
does not allocate per frame when its flag is off.

## Patterns

### Previewing a component from a script

Launch the app the way the doc screenshot pipeline does — `--preview <Component>
--vscode` — and read `CAPTURE_PORT` and `CAPTURE_TOKEN` from stdout. Then use
`GET /frame` with the bearer token for a JPEG of the window (the doc pipeline itself
takes its pixels with winapp's screen capture instead), and
`POST /preview` with `{"component":"<Name>"}` to switch components without a
relaunch. For anything beyond pixels — the tree, properties, clicks — use the
WinUI DevTools (`winapp run . --devtools on`, then `winapp devtools inspect` or
`winapp ui click`) instead of extending the capture server.

### Adding an endpoint

Don't, unless the VS Code or Visual Studio preview needs it: the package is
deprecated and goes away once winapp has its own previewer. If a preview does need
one, follow the existing shape — route it in the `switch` after the bearer check,
reject the wrong HTTP method with `405`, hop the UI dispatcher before touching
WinUI state, and serialize through a source-generated `JsonSerializerContext` so
the payload stays NativeAOT-safe.

## Common Mistakes

### Expecting the preview host to inspect the app

The preview host renders and streams; it does not inspect. Component trees,
properties, state, handlers and UI automation are the WinUI DevTools:
`winapp run . --devtools on`, then `winapp devtools inspect` or `winapp ui ...`.

### Shipping the switch in Release

`Reactor.DevtoolsSupport`, with the package referenced, keeps the preview host in the
binary. Reference the package in Debug only so Release/AOT binaries trim it; a
reference outside Debug gets build warning `REACTOR_DEVTOOLS_001`.

## Tips

**The token is printed, not discovered.** There is no lockfile any more. A client
that connects to an already-running preview reads `CAPTURE_PORT` and
`CAPTURE_TOKEN` from that process's output.

**`Observable<bool>` is the dev-flag pattern.** Plain `static bool` fields don't
notify, so toggling them doesn't re-render components that observe them. The
Debug-only menu in [Dev Tooling](dev-tooling.md) uses
`static readonly Observable<bool>` fields so the [`UseObservable`](hooks.md) hook
picks up changes.

## Next Steps

- **[Dev Tooling](dev-tooling.md)** — The WinUI DevTools and the inner-loop story.
- **[Visual Studio Embedded Preview](vs-extension.md)** — The `--embed` client.
- **[Perf Instrumentation](perf-instrumentation.md)** — The ETW provider.
- **[Reconciliation](reconciliation.md)** — How the reconcile-highlight overlay observes patches.
- **[Focus and Input Internals](focus-and-input-internals.md)** — Previous under-the-hood page.