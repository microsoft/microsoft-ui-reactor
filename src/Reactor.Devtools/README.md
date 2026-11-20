# Microsoft.UI.Reactor.Devtools

> [!WARNING]
> **Deprecated.** Reactor's built-in DevTools are replaced by the **WinUI DevTools in the
> winapp CLI**, which need nothing in your project. The `mur devtools` CLI has been
> **removed** (each command now only prints its winapp equivalent and exits 1), and so have this
> package's MCP server and the in-app dev menu (`DevtoolsMenu`, `UseDevtools`).
>
> **What stays, and why:** this package — now only the component preview host, the preview
> capture server and the `--embed` handshake — keeps working, because
> the **VS Code live preview panel** and the **Visual Studio embedded preview** run on it and winapp
> has no previewer yet. If you use either preview, keep the package (Debug-only). It goes only once
> winapp has its own previewer. Builds that reference it outside Debug show
> warning `REACTOR_DEVTOOLS_001`
> (suppress with `<NoWarn>$(NoWarn);REACTOR_DEVTOOLS_001</NoWarn>` or
> `<ReactorSuppressDevtoolsDeprecationWarning>true</ReactorSuppressDevtoolsDeprecationWarning>`).
>
> **Migrate:**
> 1. If you don't use the VS Code live preview or the Visual Studio embedded preview, remove
>    `<PackageReference Include="Microsoft.UI.Reactor.Devtools" … />` from your project, and any
>    launch profile that passes `--devtools`. The `Reactor.DevtoolsSupport` switch is not
>    deprecated: `ReactorDiagnostics` turns it on in Debug and the WinUI DevTools need it for C#
>    call sites, so an explicit `<RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport" … />`
>    is redundant in Debug but harmless; don't set it to `false`.
> 2. Keep a Debug build (source mapping stays on in Debug through `Microsoft.UI.Reactor`).
> 3. Launch with `winapp run . --devtools on` (add `--watch` to rebuild on rude edits and re-attach automatically, `--detach` when an agent drives it).
> 4. AI agents: use the winapp CLI with `--json` and the `winui-devtools` skill. The MCP server
>    has been removed.
>
> | Reactor DevTools | use instead |
> |---|---|
> | `mur devtools [project]` | `winapp run . --devtools on` (add `--watch` for hot reload with automatic re-attach) |
> | `mur devtools session list` | `winapp devtools list` |
> | `version` | `winapp devtools call DevTools.negotiate` (reports the Reactor version) |
> | `tree`, `ancestors` | `winapp devtools inspect [<selector> --ancestors]` |
> | `properties`, `set-property`, `styles` | `winapp devtools get-property`, `set-property` |
> | `resources`, `set-resource` | `winapp devtools get-resource`, `set-resource` |
> | `state` (root component only) | `winapp devtools get-state`, `set-state`, `rerender` (any component) |
> | `fire` | `winapp devtools invoke-handler <selector> <event>` (for example `OnClick`) |
> | `click`, `type`, `focus`, `invoke`, `scroll`, `wait` | `winapp ui click`, `set-value`, `focus`, `invoke`, `scroll`, `wait-for` |
> | `toggle`, `select`, `expand`, `collapse` | `winapp ui invoke <selector> --action toggle\|select\|expand\|collapse` |
> | `screenshot`, `windows.list`, `windows.activate` | `winapp ui screenshot`, `winapp ui list-windows`, `winapp ui focus -w <hwnd>` |
> | In-app ⚡ dev menu | The winapp DevTools overlay toolbar |
> | `--print-config`, `call <tool>` / MCP endpoint | Not carried over (`winapp … --json` + `winui-devtools` skill) |
> | `windows.close`, `shutdown`, `call docking.*` | No tool equivalent: app behavior (drive the app's UI with `winapp ui`) |
> | `logs` | `winapp devtools logs [--follow]` |
> | `reload` | `winapp run . --devtools on --watch` |
> | References overlay (`call references`) | `winapp devtools get-property <selector>` (References section) |
> | `--component`, `components`, `switch`, `windows.open` | `winapp run . --devtools on -- --preview <Name>` — the preview host is in this package, so keep it referenced (Debug-only) for that |
> | VS Code panel, Visual Studio embedded preview | No winapp equivalent yet; they launch the app directly and need this package |
>
> Reactor apps work with `winapp devtools` selectors such as `"<Counter>/Button"`, `key=t1`
> and `App.cs:67`. `mur devtools --help` prints the full mapping.

**Deprecated in-app preview host for [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) — kept for the VS Code live preview and the Visual Studio embedded preview.**

## About

`Microsoft.UI.Reactor.Devtools` hosts a Reactor component in a preview window and, when an editor
asks for it, runs a loopback capture server that streams frames, lists and switches components,
returns the reference graph, and performs the Visual Studio `--embed` handshake. That is all it does
now: inspecting, editing and automating a running app is the WinUI DevTools in the winapp CLI.

It is designed strictly for the inner development loop — not for production. See the security model
below.

## How to Use

Reference the package Debug-only, with the build-time capability switch:

```xml
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <PackageReference Include="Microsoft.UI.Reactor.Devtools" />
  <RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport" Value="true" Trim="true" />
</ItemGroup>
```

The VS Code extension and the Visual Studio extension launch the app themselves
(`--devtools run --vscode` and `--devtools run --embed …`). To preview one component by hand,
alongside the WinUI DevTools:

```shell
winapp run . --devtools on -- --preview <ComponentName>
```

or, without winapp, `dotnet run -- --devtools run <ComponentName>`. `--devtools app`,
`--devtools screenshot` and `--devtools tree` have been removed; they print the winapp command to
use instead and exit 2.

## Security model

1. **Opt in at build time and launch time.** The `Reactor.DevtoolsSupport` switch is the build-time
   gate; `--devtools run` (or `--preview <Name>`) on the app's command line is the launch-time gate.
   The capture server starts only when an editor passes `--vscode` (or `--embed`).
2. **Loopback-only binding.** The capture server's `HttpListener` binds to `http://127.0.0.1:{port}/`
   and is never exposed on a non-loopback adapter.
3. **Per-launch bearer token.** Every request, including `/status`, needs the `CAPTURE_TOKEN` the app
   prints at startup. State-changing endpoints are POST-only with a JSON content type.

## Best Practices

- **Reference it Debug-only.** Release and AOT builds then carry no devtools surface at all.
- **Never ship it to end users.** It is a local development tool.
- **Keep it on loopback.** Don't expose the capture port through a reverse proxy, a remote-binding
  tunnel, or `0.0.0.0` container forwarding.

## Additional Documentation

- [Package internals](https://github.com/microsoft/microsoft-ui-reactor/blob/main/src/Reactor.Devtools/INTERNALS.md)
- [DevTools internals](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/guide/devtools-internals.md)
- [User guide](https://github.com/microsoft/microsoft-ui-reactor/tree/main/docs/guide)
## Related Packages

- [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) — the core declarative WinUI 3 framework (required).
- [`Microsoft.UI.Reactor.Advanced`](https://www.nuget.org/packages/Microsoft.UI.Reactor.Advanced) — optional Win2D/graphics components.

## Feedback & Contributing

`Microsoft.UI.Reactor.Devtools` is part of the open-source Reactor project. File issues, ask questions, and contribute on [GitHub](https://github.com/microsoft/microsoft-ui-reactor). See [CONTRIBUTING.md](https://github.com/microsoft/microsoft-ui-reactor/blob/main/CONTRIBUTING.md) to get started.

## Support Policy

This package is currently released as a preview and is provided under the [MIT License](https://github.com/microsoft/microsoft-ui-reactor/blob/main/LICENSE). APIs may change between preview releases.
