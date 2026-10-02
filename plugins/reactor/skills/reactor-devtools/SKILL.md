---
name: reactor-devtools
description: "REMOVED: `mur devtools` no longer works. Inspect and drive a running Reactor app with the WinUI DevTools in the winapp CLI (`winapp run . --devtools`) and the winui-devtools skill. Load this only to translate an old `mur devtools` command into its winapp equivalent."
---
# Reactor Devtools — removed; use the WinUI DevTools (winapp)

`mur devtools` has been **removed**. Every `mur devtools …` command now only prints
what to run instead and exits `1`. Reactor's developer tools are the **WinUI DevTools
in the winapp CLI** — nothing to add to the project:

```bash
winapp run . --devtools --detach   # add --watch to rebuild on rude edits and re-attach
winapp devtools inspect            # component tree, element kinds, keys, App.cs call sites
```

**Agents: load the `winui-devtools` skill** and call the winapp CLI with `--json`.
The MCP server is not carried over. Reactor selectors work in both `winapp devtools`
and `winapp ui`: `"<Counter>"`, `"<Counter>/Button"`, `key=t1`, `App.cs:67`.

| removed `mur devtools` | use instead |
|---|---|
| `mur devtools [project]` | `winapp run . --devtools` (`--watch` for hot reload with automatic re-attach) |
| `session list` / `session clean` | `winapp devtools list` / not needed |
| `version` | `winapp devtools call DevTools.negotiate` |
| `tree`, `ancestors` | `winapp devtools inspect [<selector> --ancestors]` |
| `properties`, `set-property`, `styles` | `winapp devtools get-property`, `set-property` |
| `resources`, `set-resource` | `winapp devtools get-resource`, `set-resource` |
| `state` | `winapp devtools get-state`, `set-state`, `rerender` (any component) |
| `fire` | `winapp devtools invoke-handler <selector> <event>` (e.g. `OnClick`) |
| `call references` | `winapp devtools get-property <selector>` (References section) |
| `click`, `type`, `focus`, `invoke`, `scroll`, `wait` | `winapp ui click`, `set-value`, `focus`, `invoke`, `scroll`, `wait-for` |
| `toggle`, `select`, `expand`, `collapse` | `winapp ui invoke <selector> --action toggle` (`select`, `expand`, `collapse`) |
| `screenshot`, `windows`, `windows.list`, `windows.activate` | `winapp ui screenshot -a <app>`, `winapp ui list-windows`, `winapp ui focus -w <hwnd>` |
| `logs` | `winapp devtools logs [--follow]` |
| `reload` | `winapp run . --devtools --watch` |
| `--component`, `components`, `switch`, `windows.open` | `winapp run . --devtools -- --preview <Name>` — the preview host is in the app, so for now the app must reference `Microsoft.UI.Reactor.Devtools` |
| `--print-config`, `call <tool>` | not carried over: the matching winapp command with `--json` |
| `windows.close`, `shutdown`, `call docking.*` | no tool equivalent: app behavior — drive the app's UI with `winapp ui` |

`mur devtools --help` prints the same table. The `Microsoft.UI.Reactor.Devtools`
package (the in-app host, including the component preview host) still exists but is
deprecated (build warning `REACTOR_DEVTOOLS_001`). It stays until winapp has its own
previewer, because the VS Code live preview and the Visual Studio embedded preview run on it.
