---
name: reactor-devtools
description: >
  Drive a running Reactor app from code via the `mur devtools` CLI —
  inspect the visual tree, capture screenshots, click/type/scroll, read
  component state, and fire named handlers. Load this when diagnosing a
  visible bug (layout, contrast, wrong text), verifying a change landed,
  or doing any UI automation against a live build.
---

# Reactor Devtools — CLI-driven UI automation

The Reactor devtools let you look at a running debug-build app the way a
user does (screenshots, rendered text, layout bounds) and drive it the way
a user does (click, type, toggle, scroll). **Always prefer the `mur devtools`
CLI** — every action composes with shell pipes and `jq`, each invocation is
a complete audit record, and no MCP client setup is needed.

Under the hood the CLI talks JSON-RPC to a loopback HTTP endpoint the app
exposes. The MCP endpoint is still available at `http://127.0.0.1:PORT/mcp`
as a parity escape hatch, but reach for it only when the CLI can't express
what you need (structured args the CLI flattens, or another MCP client
that's already wired up).

Loopback-only — **DEBUG builds only**, never ship it. Auth is a per-launch
bearer token written into the lockfile; the CLI applies it transparently
during lockfile discovery, so you don't see it. **Don't pass `--endpoint`
for tool calls** — without the token the server returns 401. `--endpoint`
is only useful when you're crafting the `Authorization: Bearer …` header
yourself (curl, custom client).

## Getting `mur` on your PATH

`mur` resolves from PATH in both modes:
- **Skill kit (deployed):** `install-skill-kit.ps1` (shipped with the kit zip)
  prepends `<install>/bin/<arch>` to your user PATH.
- **Cloned repo (selfhost):** `dotnet build` of `src/Reactor.Cli` automatically
  mirrors the output to `<repo>/bin/<arch>/` (architecture is determined from
  `RuntimeIdentifier`). Add that directory to your PATH once.

If `mur --version` doesn't resolve, neither path was set up; commands below assume it does.

## Attaching to a running app

The app author enables devtools in their `Program.cs`:

```csharp
ReactorApp.Run<MyApp>("Title", width: 1200, height: 800
#if DEBUG
    , devtools: true
#endif
);
```

**Prefer attaching to an app that's already running.** Any devtools-enabled
app writes a lockfile to `%TEMP%/reactor-devtools/` on first render, and
`mur devtools <verb>` discovers it automatically — **no port, no config,
no parsing stdout.**

```bash
mur devtools session list --pretty   # confirm a session is up
```

Exit 0 with a row means you're good to go; every other verb will find it.
Exit 4 means no live session — you need to launch one yourself (next
section).

## Launching an app yourself

Two launch modes, from simplest to most featured:

### Plain `dotnet run` (the default)

```bash
dotnet run --project path/to/App.csproj -- --devtools run \
  > /tmp/app-stdout.log 2>&1 &
```

This is what you want in almost every AI session: spawn the app, drive it
with `mur devtools <verb>`, call `mur devtools shutdown` when you're done.
No supervisor machinery, no pinned port — the CLI finds the session via
the lockfile regardless.

### `mur devtools <project>` — the supervisor

Reach for this **only** when you need a stable MCP endpoint across
`reload` cycles:

```bash
mur devtools path/to/App.csproj --mcp-port 54931
```

The supervisor pins the port (reload-proof) and catches the child's exit
code 42 to rebuild and relaunch. Useful if you've wired an external MCP
client to `http://127.0.0.1:54931/mcp` and want it to survive code
edits. Overkill for one-shot automation.

## Discovering the tool surface

```bash
mur devtools call tools/list --pretty    # names + input schemas
mur devtools call tools/list | jq '.tools[].name'
```

`mur devtools --help` lists every named verb with one-line descriptions.

## CLI verb catalog

Each verb attaches to the running session via lockfile discovery; no flags
needed when only one session is active. Pass `--pretty` to any verb for
indented JSON.

| Verb | What it does |
|---|---|
| `version` | Build tag + pid + port — confirm the app is the one you expect. |
| `components` | Class names of every `Component` subclass; marks which is mounted. |
| `switch <name>` | Swap the root component by class name. **Invalidates all node ids.** |
| `reload [--component N]` | Rebuild + relaunch via the supervisor sentinel. Old ids dead. |
| `shutdown` | Close the app cleanly (supervisor exits 0). Releases the build output file lock. |
| `windows` | Active window ids, titles, bounds, currently-mounted component. |
| `windows.list` | Spec 036 §10. Per-window id, key, title, DIP size, DPI, state, isMain. Use this when you care about DPI / DIP size or the `key` column for `UseOpenWindow`-keyed lookups. |
| `windows.activate <id>` | Activate (focus) a window. Returns `{ ok, id }`. |
| `windows.close <id>` | Close a window. Honors `UseClosingGuard` / `Closing` subscribers — returns `{ ok: false, cancelled: true, id }` when a guard vetoed the close. |
| `windows.open <Component> [--title T] [--width W] [--height H] [--key K]` | Open a new top-level window mounting an allowlisted Component. The component name is gated by the same allowlist as `switchComponent`; rejected names return `unknown-component` with the available list. |
| `tree [--selector S] [--window W] [--view summary\|full]` | Dump the visual tree as JSON. `full` adds layout/automation/visual fields. |
| `screenshot [--selector S] [--out path]` | PNG of the window (or selector-cropped region). `--out path.png` writes to file; `--out -` streams bytes to stdout. |
| `click <selector>` | UIA click. Prefers Invoke → Toggle → SelectionItem; returns `via`. |
| `invoke` / `toggle` / `select` | Direct UIA pattern access. `select <container> <item>` auto-expands ComboBoxes. |
| `type <selector> <text> [--clear]` | Set text on a value-bearing control. |
| `focus <selector>` | Programmatic focus on a Control. |
| `scroll <selector> [--by H%,V%] [--to <item-selector>]` | Scroll by **percentage deltas** 0–100 (not pixels), or scroll an item into view. |
| `expand <selector>` / `collapse <selector>` | ExpandCollapse pattern (ComboBox popup, TreeViewItem, Expander). |
| `wait <selector> [--text X \| --text-matches RE \| --visible \| --count N] [--timeout MS]` | Poll a predicate until satisfied or timeout. |
| `state [--selector S]` | Dump every hook value (useState/useReducer/etc.) across mounted components. |
| `logs [--tail N] [--since SEQ] [--filter RE] [--source stdout\|stderr\|debug\|trace] [--follow]` | Drain captured `Debug.WriteLine` / `Trace.WriteLine` / `Console.Out` / `Console.Error`. Ring buffer installed at `--devtools run` startup — late-attaching agents still see startup output. `--follow` long-polls until Ctrl+C. |
| `fire <Component>.<event> [--args JSON]` | Call a NAMED METHOD on a live component by reflection. **Inline lambdas aren't reachable**. |
| `properties <selector> [--name PropName]` | Read dependency properties on an element. Omit `--name` to enumerate all DPs with values, types, and local-vs-default status. Supports attached properties via `Grid.Row` syntax. |
| `set-property <selector> <name> <value>` | Set a dependency property. Value is parsed from string (Thickness, CornerRadius, Brush hex `#RRGGBB`, enums, bool, double, int). |
| `resources [--selector S] [--scope element\|window\|app] [--filter RE]` | Browse ResourceDictionary entries. Walks element → ancestor elements → window → app (including MergedDictionaries and ThemeDictionaries). |
| `set-resource <key> <value> [--scope app\|window\|element] [--selector S]` | Set or add a XAML resource. Reports whether the write replaced an existing entry or created a new shadowing entry. |
| `styles <selector>` | Inspect the explicitly-assigned Style: TargetType, Setters (property + value), BasedOn chain. Returns `hasStyle: false` when only a default/theme style is active. |
| `ancestors <selector>` | Walk the visual tree upward — returns type, name, and automationId for each ancestor up to the root. |
| `call <tool\|method> [--args JSON]` | Generic JSON-RPC passthrough — parity escape hatch. |

### Session management

```bash
mur devtools session list             # JSONL, one live session per line
mur devtools session list --pretty    # human table
mur devtools session clean            # GC stale lockfiles
mur devtools session clean --dry-run  # show what would be removed
```

### Shared flags (before any verb)

- `--endpoint <url>` — skip lockfile discovery and talk to this endpoint.
  **Drops the bearer token** — the CLI has no `--token` flag, so verbs hit
  the endpoint unauthenticated and get 401. Only useful with `curl` plus a
  hand-built `Authorization` header, not for `mur devtools <verb>` calls.
- `--pretty` — indent JSON output.
- `--auto` — loopback port scan (slow; use only when lockfile discovery fails).

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Success. |
| 1 | Usage error (unknown flag, missing argument). |
| 2 | Transport error (endpoint unreachable, timeout). |
| 3 | Another devtools session is already active for this project. |
| 4 | No live devtools session found. |
| 5 | Tool returned a JSON-RPC error. stdout has the error envelope. |

## Selector grammar (5 forms)

1. **Node id** — `r:main/CounterDemo.SubmitButton` — copy from `tree`. Stable within a window's lifetime; invalidated by `switch` / `reload`.
2. **AutomationId** — `#btn-inc`. Matches `AutomationProperties.AutomationId` exactly.
3. **AutomationName** — `[name='Increment']` or `[name="+ 1"]`. Matches `AutomationProperties.Name` OR the visible caption of Buttons / TextBlocks / TextBoxes / ContentControls.
4. **TypePath** — `Button`, `Button[2]`, `StackPanel > Button`. Type name is `GetType().Name`. Index disambiguates.
5. **Reactor source** — `{component:'CounterDemo',line:42}`. Reserved (Phase 3).

`[name=…]` cannot be indexed — if it matches multiple, error is `ambiguous-selector` with all candidate ids listed; pick one by node id or prefix with a TypePath step.

## Typical workflows

### "Does the app look right?" — visual diagnosis

```bash
mur devtools screenshot --out /tmp/shot.png
mur devtools screenshot --selector "[name='Submit']" --out /tmp/btn.png
mur devtools tree --selector "#login-form" --view full --pretty
```

Full-view `tree` nodes carry `bounds`, `actualSize`, `desiredSize`,
`layout.margin`, `layout.padding`, `isVisible`, `isEnabled`,
`automationControlType`. That's usually enough to spot a margin collapse
or a zero-size child without running the app in a debugger.

### Diagnosing a layout issue

1. `mur devtools screenshot --out /tmp/shot.png` — confirm what's actually on screen.
2. `mur devtools tree --selector "<container>" --view full | jq '.nodes[] | select(.actualSize.width == 0)'` — find zero-sized children. A child with `actualSize:{width:0,height:0}` under a parent that's sized means the child isn't getting space (missing `Flex(grow:1)` on a ScrollView inside a FlexColumn is a classic).
3. Edit the Reactor code, then `mur devtools reload` (rebuild + relaunch) and re-screenshot.
4. `mur devtools wait "<selector>" --visible --timeout 2000` if the state is async — don't screenshot before mount.

### Diagnosing a contrast / color issue

1. `mur devtools screenshot --selector "<element>" --out /tmp/el.png` — pull the cropped PNG.
2. Decode the PNG and sample foreground/background pixels. Compute WCAG 2.1 ratio = `(L1 + 0.05) / (L2 + 0.05)` where L is relative luminance. Target ≥ 4.5:1 for body text, ≥ 3:1 for large text / UI chrome.
3. If low, check whether the color came from a Theme token (correct — rebind to the right token for the surface) or a hardcoded hex (wrong — replace with a `Theme.*` token; see `skills/design.md`).
4. Edit, `mur devtools reload`, re-screenshot, re-measure.

### Verifying a state-driven change

```bash
mur devtools click "[name='+ 1']"
mur devtools wait "[name='Current count: 1']" --timeout 1000
mur devtools state                    # confirm the underlying UseState value
```

`state` is particularly useful when the UI text doesn't obviously encode
the value (e.g. a slider position or a theme toggle).

### Reading debug output while the app runs

```bash
mur devtools logs --tail 50                     # last 50 lines, most recent first
mur devtools logs --source debug --pretty       # only Debug.WriteLine / Trace
mur devtools logs --filter "Nav.*cache" --tail 20
mur devtools logs --follow                      # stream until Ctrl+C
mur devtools logs --since 42                    # everything after seq 42
```

Pair with `state` and `tree` to diagnose state-driven bugs: paste a
`Debug.WriteLine` in the render path, reload, reproduce, then drain logs.
The buffer (4 MB default) is installed **before** component reflection so
it catches startup output too — attach late and you still see what booted.
`dropped` in the response reports ring overflow; bump via
`--devtools-logs-capacity <MB>` on the app's launch if you're losing
entries. Set `--devtools-logs off` on launch to disable capture entirely.

### Inspecting properties and styles

```bash
# Enumerate all dependency properties on an element
mur devtools properties "#my-button" --pretty

# Read a specific property (supports attached properties)
mur devtools properties "#my-button" --name Margin
mur devtools properties "#my-button" --name Grid.Row

# Mutate a property at runtime
mur devtools set-property "#my-button" Background "#FF0000"
mur devtools set-property "#my-button" Margin "10,5,10,5"
mur devtools set-property "#my-button" Visibility Collapsed

# Inspect the applied style (explicit only — theme/default styles return hasStyle:false)
mur devtools styles "#my-button" --pretty
```

`properties` reports `isLocal: true` when a value was set directly (via
code or XAML attribute) vs inherited from style/template/default. Use
`set-property` to hot-patch layout or color at runtime without a rebuild.

### Browsing resources and themes

```bash
# List all app-level resources
mur devtools resources --pretty

# Filter to color-related resources
mur devtools resources --filter "Color|Brush" --pretty

# Scope to a specific element's ResourceDictionary chain
mur devtools resources --selector "#my-panel" --scope element

# Override a resource at runtime
mur devtools set-resource ButtonBackground "#00FF00"
```

Resources are listed with their scope (`element`, `ancestor:Grid`, `app`,
`app/merged`, `app/theme:Light`, etc.) so you can trace where a value
came from. `set-resource` at a higher scope may **shadow** a value
defined in a merged/theme dictionary — check the `replaced` flag in the
response.

### Understanding the visual tree hierarchy

```bash
# Walk ancestors from an element to the root
mur devtools ancestors "#my-button" --pretty
```

Useful for understanding layout containers, resource scoping, and which
parent element is providing inherited property values.

### Driving the app from a script

For launch → drive → shutdown loops (stress tests, batch automation, CI):

1. Start the app yourself (`dotnet run … -- --devtools run &` or the built exe).
2. **Poll `mur devtools session list` until exit 0** — that's the only signal
   that lockfile discovery + the auth probe both succeed. Don't grep stdout
   for `MCP_ENDPOINT=`; that banner fires before the lockfile is written and
   before the server can authorize you.
3. Issue verbs without `--endpoint` so lockfile discovery applies the bearer
   token.
4. `mur devtools shutdown` — the child exits with code 0 within a second.
   Wait for the OS process to exit before relaunching against the same
   project, or the single-instance check (exit 3) will trip on the next iteration.
5. If a previous iteration was killed without `shutdown`, run
   `mur devtools session clean` to GC the stale lockfile.

### Cleaning up when done

```bash
mur devtools shutdown     # close the app; release build-output file locks
```

A running Reactor app holds open handles on its own build output, which
blocks `dotnet build` from overwriting the DLLs. Use `shutdown` between
rebuilds when you're iterating on the app's source. If the app was started
under `mur devtools <project>` (supervisor mode), `shutdown` also makes the
supervisor return 0 so the `mur.exe` binary frees up.

## Gotchas

1. **Both the build-time flag and a devtools launch are required.** The MCP server only starts if the app was compiled with `devtools: true` passed to `ReactorApp.Run(...)` (usually wrapped in `#if DEBUG`) **and** launched with either `mur devtools …` or `dotnet run -- --devtools run` directly. Either launch mode works — the supervisor is optional. Miss both legs (build flag + launch flag) and the app boots normally with no MCP port, no lockfile, no banner. If `mur devtools session list` reports exit 4 indefinitely, this is almost always why — check `Program.cs` first, then the launch command.
2. **`switch` and `reload` invalidate every node id.** Re-walk the tree after them; do not cache `r:…` ids across swaps.
3. **Popups aren't in the main visual tree.** `tree` walks `window.Content` — ComboBox dropdown items, flyouts, and context menus live in separate popup roots and won't show up. `select` auto-expands the container but item resolution through the main tree will still miss them. Prefer `ISelectionItemProvider` via a node-id that `tree` emitted while the popup was open, or switch to a selector that targets the SelectorItem ancestor directly.
4. **`fire` only sees declared methods.** Inline lambdas (`Button("+1", () => setCount(...))`) are unreachable. Hoist the handler to a method on the Component class when you need `fire` access.
5. **Scroll percent read-back can lag one call.** Right after `mur devtools scroll … --by 0,50`, the next verb may report `scrollPercent.vertical:0` before the engine settles. If the offset matters, call `scroll … --by 0,0` once more to read the settled value, or use `--to <item-selector>` (ScrollIntoView) which is deterministic.
6. **Use `wait` before asserting on async UI.** Many demos render on a dispatcher hop; a `screenshot` immediately after `click` may capture the pre-state.
7. **Devtools log is authoritative.** Every call lands in `%LOCALAPPDATA%\Reactor\devtools\{pid}.log` (tab-separated: ts, tool, selector, latency, ok/err, rpc code). Tail it to reconstruct a failed run.
8. **Single-instance per project.** Two `mur devtools` against the same `.csproj` is a hard error (exit 3). Use `mur devtools shutdown` or close the window to release the lockfile before starting another.
9. **Log capture starts inside `ReactorApp.Run()`.** Anything the app's `Program.cs` writes via `Console.WriteLine` / `Debug.WriteLine` **before** the `Run()` call is not captured — install happens as the first side-effect of the devtools bring-up, not at process start. Move diagnostic writes inside your root `Component.Render()`, an effect, or any code reached from `Run()` and they'll show up in `mur devtools logs`. The buffer is in-memory only in v1 — a crash takes it with it; attach early or run `--follow` if you need the final moments.
10. **`styles` only returns explicitly-assigned styles.** WinUI 3 `FrameworkElement.Style` is null when the element uses a theme/default style (implicit style). A null result does **not** mean the element is unstyled — it means the style was applied by the theme system, not by explicit assignment in XAML or code. There is no WinUI 3 API to inspect the resolved implicit style at runtime.
11. **`properties` isLocal semantics.** The `isLocal` flag uses `ReadLocalValue` which only distinguishes locally-set values from everything else — it cannot tell you whether a non-local value came from a style, animation, template, or default. A value with `isLocal: false` may still differ from the DP's default if it was set via a style or template binding.
12. **`set-resource` shadows, it doesn't merge.** Writing a resource at element scope creates a new entry in that element's ResourceDictionary — it does not modify the app-level or theme dictionary. The response includes `replaced: true` when overwriting an existing key at the same scope, or `replaced: false` when creating a new shadowing entry. Downstream elements that already resolved the old value won't update until they re-query.

## Raw MCP (escape hatch)

If you have to talk MCP directly — another MCP client, an existing script,
or a structured argument shape the CLI flattens — the endpoint is
discoverable from the lockfile:

```bash
ENDPOINT=$(mur devtools session list | jq -r '.endpoint')
curl -s $ENDPOINT -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call",
       "params":{"name":"click","arguments":{"selector":"[name=\"Save\"]"}}}'
```

`GET $ENDPOINT` returns the self-describing schema document (protocol
version, selector grammar, tool inventory). Prefer the CLI for everything
else.

## Spec + source pointers

- Specs: `docs/specs/024-ai-agent-devtools.md`, `docs/specs/025-devtools-cli-parity.md`, `docs/specs/036-window-design.md` (multi-window MCP surface §10)
- Server: `src/Reactor/Hosting/Devtools/DevtoolsMcpServer.cs`
- Tool registration: `DevtoolsTools.cs`, `DevtoolsUiaTools.cs`, `DevtoolsFireTool.cs`, `DevtoolsStateTool.cs`, `DevtoolsLogsTool.cs`, `DevtoolsPropertyTools.cs`
- Log capture: `LogCaptureBuffer.cs`, `LogCaptureInstall.cs`
- CLI verbs: `src/Reactor.Cli/Devtools/DevtoolsVerbs.cs`
- Selector grammar / parser: `SelectorParser.cs`
