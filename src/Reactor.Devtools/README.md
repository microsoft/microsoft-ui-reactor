# Microsoft.UI.Reactor.Devtools

> [!WARNING]
> **Deprecated.** Reactor's built-in DevTools are replaced by the **WinUI DevTools in the
> winapp CLI**, which need nothing in your project. The `mur devtools` CLI has already been
> **removed** (each command now only prints its winapp equivalent and exits 1). This package —
> the in-app host, including the component preview host — and the `Reactor.DevtoolsSupport`
> switch remain until the preview host is decided, then go too. Builds that reference it show
> warning `REACTOR_DEVTOOLS_001`
> (suppress with `<NoWarn>$(NoWarn);REACTOR_DEVTOOLS_001</NoWarn>` or
> `<ReactorSuppressDevtoolsDeprecationWarning>true</ReactorSuppressDevtoolsDeprecationWarning>`).
>
> **Migrate:**
> 1. Remove `<PackageReference Include="Microsoft.UI.Reactor.Devtools" … />` and
>    `<RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport" … />` from your project,
>    and any launch profile that passes `--devtools`.
> 2. Keep a Debug build (source mapping stays on in Debug through `Microsoft.UI.Reactor`).
> 3. Launch with `winapp run . --devtools` (add `--watch` to rebuild on rude edits and re-attach automatically, `--detach` when an agent drives it).
> 4. AI agents: use the winapp CLI with `--json` and the `winui-devtools` skill. The MCP server
>    is not carried over.
>
> | Reactor DevTools | use instead |
> |---|---|
> | `mur devtools [project]` | `winapp run . --devtools` (add `--watch` for hot reload with automatic re-attach) |
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
> | `reload` | `winapp run . --devtools --watch` |
> | References overlay (`call references`) | `winapp devtools get-property <selector>` (References section) |
> | `--component`, `components`, `switch`, `windows.open` | `winapp run . --devtools -- --preview <Name>` — the preview host is in this package, so keep it referenced (Debug-only) for that |
> | VS Code panel, Visual Studio embedded preview | No winapp equivalent yet; they launch the app directly and need this package |
>
> Reactor apps work with `winapp devtools` selectors such as `"<Counter>/Button"`, `key=t1`
> and `App.cs:67`. `mur devtools --help` prints the full mapping.

**Optional developer-loop devtools host for [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) — live element-tree inspection, hot reload, preview, and an agent-friendly MCP surface.**

## About

`Microsoft.UI.Reactor.Devtools` adds an opt-in, developer-loop tooling surface to a Reactor app. It hosts a loopback Model Context Protocol (MCP) server that exposes `reactor.*` tools (inspect the element tree, capture screenshots, click, read state, fire handlers) and a rolling per-call log. The `mur devtools` CLI that used to drive it has been removed.

It is designed strictly for the inner development loop — not for production endpoints. See the security model below.

## How to Use

Install the package into your Reactor app (typically as a Debug-only reference):

```shell
dotnet add package Microsoft.UI.Reactor.Devtools
```

Enable the build-time capability gate (keep it Debug-conditional so Release/AOT builds carry no devtools surface):

```xml
<ItemGroup Condition="'$(Configuration)' == 'Debug'">
  <RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport" Value="true" Trim="true" />
</ItemGroup>
```

Then launch the app with the in-app host. The component preview host runs alongside the WinUI DevTools:

```shell
winapp run . --devtools -- --preview <ComponentName>
```

or, without winapp, `dotnet run -- --devtools run`. (`mur devtools run` and `mur devtools --print-config` have been removed.)

## Security model

The devtools surface is **developer-loop only** and ships with hard gates:

1. **Opt in at build time and launch time.** The `Reactor.DevtoolsSupport` MSBuild switch is the build-time gate; `--devtools run` / `--devtools app` (or `--preview <Name>`) on the app's command line is the session-time gate. Both must be present before the MCP server, capture server, logger, or in-app dev menu is enabled.
2. **Loopback-only binding.** The MCP `HttpListener` binds to `http://127.0.0.1:{port}/` and is never exposed on a non-loopback adapter.
3. **No authentication in v1.** Any local process can connect to the MCP port. Do **not** run a devtools-enabled app in an environment with untrusted local processes, do **not** enable `Reactor.DevtoolsSupport` in Release builds that ship to end users, and do **not** expose the MCP surface beyond localhost (reverse proxy, remote-binding SSH tunnel, or `0.0.0.0` container forwarding).

## Key Features

- **MCP tool surface** — `reactor.tree`, `reactor.screenshot`, `reactor.click`, `reactor.state`, `reactor.fire`, and more.
- **Rolling call log** — one line per tool call to `%LOCALAPPDATA%/Reactor/devtools/{pid}.log`, rolling at 10 MB with five archives kept.
- **ETW trace correlation** — emits a `Microsoft-UI-Reactor` `EventSource` for correlated Reactor + WinUI timelines (see [`INTERNALS.md`](https://github.com/microsoft/microsoft-ui-reactor/blob/main/src/Reactor.Devtools/INTERNALS.md)).

## Best Practices

- **Reference it Debug-only.** Wrap the `PackageReference` and the `Reactor.DevtoolsSupport` switch in a `'$(Configuration)' == 'Debug'` condition so Release and AOT builds carry no devtools surface at all.
- **Never ship devtools to end users.** The MCP port has no authentication in v1; treat it as a local development tool only.
- **Keep it on loopback.** Don't expose the MCP port through a reverse proxy, remote-binding tunnel, or `0.0.0.0` container forwarding.
- **Run only with trusted local processes.** Any process on the same machine can call the MCP tools while the session is active.

## Additional Documentation

- [Devtools internals & ETW guide](https://github.com/microsoft/microsoft-ui-reactor/blob/main/src/Reactor.Devtools/INTERNALS.md)
- [User guide](https://github.com/microsoft/microsoft-ui-reactor/tree/main/docs/guide)
- [Model Context Protocol](https://modelcontextprotocol.io/)

## Related Packages

- [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) — the core declarative WinUI 3 framework (required).
- [`Microsoft.UI.Reactor.Advanced`](https://www.nuget.org/packages/Microsoft.UI.Reactor.Advanced) — optional Win2D/graphics components.

## Feedback & Contributing

`Microsoft.UI.Reactor.Devtools` is part of the open-source Reactor project. File issues, ask questions, and contribute on [GitHub](https://github.com/microsoft/microsoft-ui-reactor). See [CONTRIBUTING.md](https://github.com/microsoft/microsoft-ui-reactor/blob/main/CONTRIBUTING.md) to get started.

## Support Policy

This package is currently released as a preview and is provided under the [MIT License](https://github.com/microsoft/microsoft-ui-reactor/blob/main/LICENSE). APIs may change between preview releases.
