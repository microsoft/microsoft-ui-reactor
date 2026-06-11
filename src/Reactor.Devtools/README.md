# Microsoft.UI.Reactor.Devtools

**Optional developer-loop devtools host for [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) — live element-tree inspection, hot reload, preview, and an agent-friendly MCP surface.**

## About

`Microsoft.UI.Reactor.Devtools` adds an opt-in, developer-loop tooling surface to a Reactor app. It hosts a loopback Model Context Protocol (MCP) server that exposes `reactor.*` tools (inspect the element tree, capture screenshots, click, read state, fire handlers) plus the `mur devtools` supervisor and a rolling per-call log.

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

Then launch the devtools supervisor for your app:

```shell
mur devtools run
```

To wire an agent (Claude Code, VS Code, GitHub Copilot) to the MCP surface, print a ready-to-paste config fragment:

```shell
mur devtools --print-config --mcp-port 5000
```

The tool prints the JSON fragment — it never writes to disk; you paste it into your agent's config yourself.

## Security model

The devtools surface is **developer-loop only** and ships with hard gates:

1. **Opt in at build time and launch time.** The `Reactor.DevtoolsSupport` MSBuild switch is the build-time gate; `mur devtools run` / `mur devtools app` is the session-time gate. Both must be present before the MCP server, capture server, logger, or in-app dev menu is enabled.
2. **Loopback-only binding.** The MCP `HttpListener` binds to `http://127.0.0.1:{port}/` and is never exposed on a non-loopback adapter.
3. **No authentication in v1.** Any local process can connect to the MCP port. Do **not** run `mur devtools` in an environment with untrusted local processes, do **not** enable `Reactor.DevtoolsSupport` in Release builds that ship to end users, and do **not** expose the MCP surface beyond localhost (reverse proxy, remote-binding SSH tunnel, or `0.0.0.0` container forwarding).

## Key Features

- **MCP tool surface** — `reactor.tree`, `reactor.screenshot`, `reactor.click`, `reactor.state`, `reactor.fire`, and more.
- **`mur devtools` supervisor** — launches and supervises the devtools session for your app.
- **Rolling call log** — one line per tool call to `%LOCALAPPDATA%/Reactor/devtools/{pid}.log`, rolling at 10 MB with five archives kept.
- **ETW trace correlation** — emits a `Microsoft-UI-Reactor` `EventSource` for correlated Reactor + WinUI timelines (see [`INTERNALS.md`](https://github.com/microsoft/microsoft-ui-reactor/blob/main/src/Reactor.Devtools/INTERNALS.md)).
- **Agent config generation** — `mur devtools --print-config` emits MCP config fragments for popular agents.

## Additional Documentation

- [Devtools internals & ETW guide](https://github.com/microsoft/microsoft-ui-reactor/blob/main/src/Reactor.Devtools/INTERNALS.md)
- [User guide](https://github.com/microsoft/microsoft-ui-reactor/tree/main/docs/guide)
- [Model Context Protocol](https://modelcontextprotocol.io/)

## Related Packages

- [`Microsoft.UI.Reactor`](https://www.nuget.org/packages/Microsoft.UI.Reactor) — the core declarative WinUI 3 framework (required).
- [`Microsoft.UI.Reactor.Advanced`](https://www.nuget.org/packages/Microsoft.UI.Reactor.Advanced) — optional Win2D/graphics components.
- [`Microsoft.UI.Reactor.ProjectTemplates`](https://www.nuget.org/packages/Microsoft.UI.Reactor.ProjectTemplates) — `dotnet new` templates.

## Feedback & Contributing

`Microsoft.UI.Reactor.Devtools` is part of the open-source Reactor project. File issues, ask questions, and contribute on [GitHub](https://github.com/microsoft/microsoft-ui-reactor). See [CONTRIBUTING.md](https://github.com/microsoft/microsoft-ui-reactor/blob/main/CONTRIBUTING.md) to get started.

## Support Policy

This package is currently released as a preview and is provided under the [MIT License](https://github.com/microsoft/microsoft-ui-reactor/blob/main/LICENSE). APIs may change between preview releases.
