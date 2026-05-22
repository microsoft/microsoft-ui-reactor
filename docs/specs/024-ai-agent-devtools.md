# 024 — AI Agent Devtools

**Status:** Draft
**Date:** 2026-04-18
**Author:** Chris Anderson
**Tracks:** [microsoft/microsoft-ui-reactor#16](https://github.com/microsoft/microsoft-ui-reactor/issues/16)

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Goals and Non-Goals](#2-goals-and-non-goals)
3. [Personas](#3-personas)
4. [State of the World](#4-state-of-the-world)
5. [Design Principles](#5-design-principles)
6. [Architecture](#6-architecture)
7. [CLI Surface](#7-cli-surface)
8. [MCP Tool Inventory](#8-mcp-tool-inventory)
9. [Visual Tree Design](#9-visual-tree-design)
10. [Screenshot Design](#10-screenshot-design)
11. [Automation Design](#11-automation-design)
12. [State Inspection](#12-state-inspection)
13. [Node Identity and Selectors](#13-node-identity-and-selectors)
14. [Security and Scoping](#14-security-and-scoping)
15. [Success Metrics](#15-success-metrics)
16. [Open Questions](#16-open-questions)
17. [Implementation Phases](#17-implementation-phases)

---

## 1. Problem Statement

When an AI agent authors a Microsoft.UI.Reactor (Reactor) app, the build → view → iterate loop is lopsided. The agent writes code fluently but has no effective way to *see* what the running app looks like or behaves like. Today the `--preview` feature launches the app, captures frames, and hands them to the VS Code extension — but a single JPEG is not enough signal for an agent to reason about layout, overflow, state transitions, or the outcome of an interaction. The loop "make a change → verify visually → correct" still requires a human in the middle, which defeats the point of agentic coding.

The right fix is not a richer image feed. It is a structured, stateful, agent-native surface that exposes what a WinUI app *is* — the rendered visual tree with real post-layout sizes, the state behind it, and a set of verbs to drive it — as a live session the agent can query and act on over many turns without paying cold-start cost per question.

This spec designs that surface.

## 2. Goals and Non-Goals

### Goals

- **Close the perception gap.** Give an agent the same (or better) observability a developer gets from the debugger, the live visual tree, and a screenshot — in one protocol, callable in a single turn.
- **Session over one-shot.** A devtools-enabled Reactor app hosts a persistent MCP server. The agent launches once, asks many questions, takes many actions. No process-per-query.
- **Real WinUI, not a Reactor abstraction.** The visual tree the agent sees is the actual rendered `UIElement` tree, with post-layout bounds, templated parts exposed, and Reactor source mapping layered on *as metadata* where we can resolve it. Layout bugs live in the template layer — we have to show it.
- **UIA is the automation stack.** We already get UIA for free on every rendered WinUI control (per spec 006). We drive the app through the same surface screen readers use, not through pixel coordinates or synthesized input.
- **Fine-grained atomic tools.** One MCP tool per capability (`screenshot`, `tree`, `click`, `type`, `waitFor`, …). Agents compose their own loops. No magic "step and diff" bundles in v1.
- **Protocol first, CLI is a thin launcher.** The contract is the MCP tool set. The CLI exists to start the devtools process and for one-shot scripted captures, not as the agent's primary interface.
- **Stable node IDs across turns.** The agent should be able to say "click `r:main/Counter.btn-inc`" three turns after it first saw that id and have it mean the same thing, as long as the node still exists.

### Non-Goals

- **Not computer-use.** No screen-pixel reasoning, no coordinate-based input as the primary path, no vision model in the loop. UIA-shaped verbs are sufficient for the apps Reactor can author today.
- **Not a general WinUI inspector.** Live Visual Tree, Inspect.exe, and Accessibility Insights already exist. We ship *just enough* of that surface to be useful to an agent, reusing UIA where possible.
- **Not replacing the VS Code preview panel.** The developer-facing live thumbnail stays. It and the MCP server share the same hosting process and capture infrastructure.
- **Not end-to-end test infrastructure.** Playwright-style assertions, retries, video recording, and flake management are someone else's spec. We expose the primitives; a test framework could sit on top.
- **Not multi-app orchestration (v1).** Each Reactor process hosts its own MCP server. A broker that fans out across many apps is a later consideration (§17).

## 3. Personas

### P1 — AI coding agent (primary)

A Claude / Copilot / internal agent iterating on a Reactor app. Runs headless. Communicates exclusively through structured tool calls. Needs: launch the app, get a tree it can query with stable references, take a screenshot of a specific element, click a button, observe, repeat. Has no patience for cold-start cost and no ability to interpret raw pixels well enough to replace a structured tree.

### P2 — Reactor developer with an agent pair (secondary)

A human developer with Claude Code or Copilot as a pair. Runs `mur devtools` to start the app. The agent connects to the MCP endpoint and takes over the inspection/iteration loop while the developer reads the diff and steers. The human occasionally peeks at the preview window to sanity-check.

### P3 — Reactor developer running solo (tertiary)

No agent involved. Uses `mur devtools` for the live preview and the VS Code panel, exactly as `--preview` works today. The renaming and the expanded capability set should not make their life worse.

### Non-Personas

- End-user automation (RPA). UIA is the same protocol they'd use, but the surface we design here is scoped to *development-time* inspection.
- CI test runners for shipping apps. Possible future consumer; not what this spec optimizes for.

## 4. State of the World

| Capability | Exists today | Agent-usable? |
|---|---|---|
| `--preview` launches app + preview | `src/Reactor/Hosting/ReactorApp.cs:109` | Partial — VS Code panel only |
| HTTP capture server on localhost | `src/Reactor/Hosting/PreviewCaptureServer.cs` | Yes, but undocumented, extension-private |
| `--preview-list` component enumeration | `ReactorApp.cs:115` | Yes, single line of text output |
| Component switching (`POST /preview`) | `PreviewCaptureServer.cs` | Yes, but not exposed as an agent tool |
| Visual tree dump | **Missing** | No |
| Automation / click / type | **Missing** | No |
| State inspection (hook values) | **Missing** | No |
| Source mapping (Reactor element → file:line) | Planned — [spec 010](010-source-mapping-design.md) | N/A — this spec consumes it |
| UIA passthrough on rendered controls | Free from WinUI — [spec 006](006-accessibility-design.md) | Yes, but no client wiring |
| MCP server | **Missing** | N/A |

The foundation is more than half there. The pieces that don't exist (tree dump, automation, state inspection, MCP wrapper) compose cleanly on top of what's already in `Reactor/Hosting/`.

## 5. Design Principles

1. **The rendered tree is the source of truth.** Not the Reactor authoring model, not the UIA tree, not a serialized XAML representation. `VisualTreeHelper` walked from the window's root on the UI thread, post-layout, post-transform. Reactor and UIA metadata is overlaid onto those nodes, never substituted for them.
2. **Reactor backrefs are sparse and honest.** A `reactor:{component,file,line}` block appears only when source mapping can resolve it. Templated parts, framework wrappers, and anonymous containers have no backref. We do not guess.
3. **Every tool is atomic.** One capability, one tool. No `step` that combines action + observation, no `reactor.magicFix`. Agents compose; we provide primitives.
4. **Selectors are UIA-shaped.** `AutomationId`, `Name`, type + index. Server-issued node IDs are the stable handle *after* the agent has seen the tree. Pixel coordinates are not selectors.
5. **The CLI is a launcher, not an API.** The agent talks to MCP. The CLI talks to developers. A one-shot scripted capture is the exception, not the shape.
6. **Stable IDs beat clever selectors.** An `id` the agent saw in a previous tree must still mean the same node (or be explicitly gone). Re-templating, theme changes, and re-layouts must not shuffle the id space.
7. **Don't hide the template layer.** Showing only the Reactor-authored elements would let the tree lie about what ended up on screen. The point is to debug layout; layout lives in the template.

## 6. Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Agent (Claude Code, Copilot, …)                                 │
└─────────────────────────────────────────────────────────────────┘
                     │ MCP (stdio or loopback HTTP)
                     ▼
┌─────────────────────────────────────────────────────────────────┐
│ Reactor app process (devtools mode enabled)                     │
│                                                                 │
│  ┌──────────────┐   ┌──────────────────┐   ┌─────────────────┐  │
│  │ MCP server   │──▶│ Devtools service │──▶│ UI thread       │  │
│  │ (fine atoms) │   │ (dispatch,       │   │ - VisualTree    │  │
│  └──────────────┘   │  node registry,  │   │ - PrintWindow   │  │
│  ┌──────────────┐   │  UIA client,     │   │ - UIA automation│  │
│  │ HTTP server  │◀─▶│  source map)     │   │ - Hook graph    │  │
│  │ (preview UI) │   └──────────────────┘   └─────────────────┘  │
│  └──────────────┘                                               │
└─────────────────────────────────────────────────────────────────┘
                     ▲
                     │ HTTP /preview, /components (existing)
                     │
┌─────────────────────────────────────────────────────────────────┐
│ VS Code extension                                               │
└─────────────────────────────────────────────────────────────────┘
```

**Hosting model: in-process MCP per app.** Each Reactor process that enables devtools hosts its own MCP server. The agent launches the app (directly or via `mur devtools`), reads the endpoint from stdout (the process already prints `CAPTURE_PORT=nnnn`; we add `MCP_PORT=nnnn`), and configures its MCP client to that endpoint. Process dies → server dies. No broker, no proxy, no orchestration.

**Two servers, one process.** The existing `PreviewCaptureServer` keeps serving the VS Code extension unchanged. The new MCP surface lives in a sibling component — likely `Reactor/Hosting/DevtoolsMcpServer.cs` — sharing the `DispatcherQueue`, the source map, and a single **node registry** (§13) that issues and resolves stable ids.

**No `dotnet watch`.** Devtools mode explicitly runs under `dotnet run`, not `dotnet watch run`. Watch's rude-edit restarts would kill the MCP server and the node registry mid-session with no in-band notification to the agent; the agent's cached tree ids would silently stop meaning anything. Instead of fighting that, we make **process replacement an explicit, agent-driven event**: the agent calls `reactor.reload` (§8) when it wants a fresh build, and composes that with its own edit cycle. Hot Reload at the developer's edit level keeps working as a separate feature — it just isn't how the devtools session picks up source changes. See §11.

**The `mur devtools` launcher is a supervisor, not a proxy.** It runs `dotnet run -- --devtools run`, pins the MCP port across invocations (via `--mcp-port`), and respawns the child when the child exits with the reload sentinel code. MCP tool calls never traverse the launcher — the agent's MCP client talks directly to the in-process server in the Reactor app. The launcher exists so that `reactor.reload` has somewhere to relaunch from; it does not forward, translate, or buffer tool calls.

**Transport.** MCP over loopback HTTP (JSON-RPC) in v1. stdio transport is a later consideration once we have enough session signal to know whether port-pinning over HTTP is painful in practice; both transports would expose the same tool set.

**Threading.** Every read of the visual tree and every automation action must run on the UI dispatcher. The MCP listener is async and off-thread; it marshals through `DispatcherQueue` (same pattern `PreviewCaptureServer` already uses for `PrintWindow`). Callers see ordinary async tools; the thread hop is invisible.

### Multi-window addressing

A Reactor process can host multiple top-level `Microsoft.UI.Xaml.Window` instances. One MCP server per process serves all of them. Windows are addressed by a **stable window id** assigned at window creation (derived from `Window.Title` when unique, else `Win<N>` with `N` monotonically increasing).

**Ids encode their window.** Every node id carries its window: `r:<window-id>/<component-local-id>`. Example: `r:main/Counter.btn-inc`, `r:settings/Form.save`. An agent that received an id in a previous turn can pass it to any tool without also passing a `window` argument; the tool resolves the window from the id.

**Tools that take no id take a `window` arg.** `reactor.tree`, `reactor.screenshot`, and `reactor.switchComponent` accept `window` explicitly. When only one window exists, `window` defaults to that single window. Once a second window is open, omitting `window` on an ambiguous call returns an error listing the active windows — we do not silently pick one.

**Cross-window mismatch is an error.** If a tool receives both an id and a `window` arg and they disagree (`reactor.click("r:main/#btn", {window:"settings"})`), the server returns an error rather than preferring one over the other. This eliminates a whole class of "I meant that button in the other window" mistakes.

**ContentDialog, Popup, Flyout are not windows.** They appear as nodes within their owner window's visual tree — which is how WinUI models them — and are driven by selectors into that tree, not by a separate addressing scheme. Ids look like `r:main/.../ContentDialog#confirmDialog`.

**Window lifecycle is observed, not notified.** The agent polls `reactor.windows()` to see what's open. Consistent with the reload model (§11): the server does not push in-band lifecycle notifications; the agent owns observation cadence.

## 7. CLI Surface

The existing `--preview` flag is renamed to `--devtools`. `--preview-list` and `--vscode` are folded into subverbs.

### Proposed shape

```
dotnet run -- --devtools run [ComponentName]     # launch app + devtools servers (default)
dotnet run -- --devtools list                    # list components, exit
dotnet run -- --devtools screenshot <Component> [--selector S] [--out path]
dotnet run -- --devtools tree <Component> [--selector S] [--format json|text]
```

And a `mur`-level launcher for the agent-facing common case:

```
mur devtools [path-to-project] [--component Name] [--mcp-port N]
```

`mur devtools` runs `dotnet run -- --devtools run` under the hood (explicitly *not* `dotnet watch run` — see §6 and §11), prints the MCP endpoint to stdout in a parseable form, and supervises restarts:

```
[devtools] ready (build 2026-04-18T14:22:09Z)
MCP_ENDPOINT=http://localhost:54931/mcp
MCP_PORT=54931
CAPTURE_PORT=54932
```

When the child exits with the reload sentinel code (`42`, emitted by `reactor.reload`), the launcher rebuilds and respawns the child on the same `MCP_PORT`. Each ready line carries a new `build` tag so the agent can confirm its view of the world is post-reload. If the child exits with any other code, the launcher exits too — it's a supervisor for reload, not a crash-loop restart manager.

### Migration from `--preview`

- `--preview` becomes a deprecated alias for `--devtools run` that prints a one-time deprecation notice. Keep through one release; remove in the next.
- `--preview-list` → `--devtools list`. Same alias treatment.
- `--vscode` → `--devtools run --vscode` (the `--vscode` flag keeps its meaning: turn on the capture server for the VS Code panel). The MCP server is always on when `--devtools` is on; `--vscode` is an additional opt-in for the human-facing preview.
- The scaffolded template in `Reactor.Cli/Program.cs` (`GenerateProgram`) is updated: the `preview: true` parameter to `ReactorApp.Run` is renamed to `devtools: true`, with the source comment updated. The runtime flag read in `TryRunPreview` is renamed correspondingly.

### What the CLI does *not* do

- No `mur devtools click`, no `mur devtools tree --follow`. An agent driving the app talks MCP; the CLI is a launcher. Exposing every tool as a CLI verb doubles the surface area we have to maintain and slows agents down per call.
- No daemon mode. `mur devtools` is a foreground process tied to the app's lifetime.

## 8. MCP Tool Inventory

Fine atomic tools, one capability each. **Wire names are unprefixed** (`tree`, `click`, `screenshot`, …): the MCP plugin name (`reactor`) already provides the namespace (clients see `mcp__reactor__tree`, not `mcp__reactor__reactor.tree`). The `reactor.` prefix used throughout this spec is informal shorthand for prose; it is not part of the tool name on the wire.

| Tool | Purpose | Key arguments | Returns |
|---|---|---|---|
| `reactor.windows` | List top-level windows in the process | — | `WindowInfo[]` |
| `reactor.components` | List component classes in the loaded assembly | — | `string[]` |
| `reactor.switchComponent` | Switch a window's preview to a different component | `name`, `window?` | `{ ok, current }` |
| `reactor.tree` | Dump the rendered visual tree | `selector?`, `window?`, `view: "summary" \| "full"`, `includeReactorSource: bool` | `TreeNode[]` (§9) |
| `reactor.screenshot` | Capture a PNG | `selector?`, `window?`, `waitIdle: bool`, `dpi?`, `includeChrome: bool` | `{ png: base64, bounds }` |
| `reactor.state` | Inspect reactive state | `selector?` | `{ hooks: HookValue[] }` (§12) |
| `reactor.click` | UIA invoke or synthesized click | `selector` | `{ ok }` |
| `reactor.type` | UIA-shaped text input | `selector`, `text`, `clear?: bool` | `{ ok }` |
| `reactor.scroll` | Scroll container by offset or to element | `selector`, `by?`, `to?` | `{ ok, scrollPosition }` |
| `reactor.focus` | Move focus to element | `selector` | `{ ok }` |
| `reactor.invoke` | UIA `IInvokeProvider.Invoke` directly | `selector` | `{ ok }` |
| `reactor.toggle` | UIA `IToggleProvider.Toggle` | `selector` | `{ ok, state }` |
| `reactor.select` | UIA selection on `ListView`-like controls | `selector`, `itemSelector` | `{ ok }` |
| `reactor.waitFor` | Block until a predicate over the tree is true | `predicate`, `timeoutMs` | `{ ok, elapsedMs }` |
| `reactor.fire` | **Escape hatch:** invoke a Reactor event handler directly | `component`, `event`, `args?` | `{ ok }` |
| `reactor.version` | Report current build tag and process id | — | `{ build, pid, mcpPort }` |
| `reactor.reload` | Rebuild and relaunch the Reactor process; invalidate the node registry | `component?` | `{ ok, exitingBuild }` (response flushed *before* shutdown) |

### Notes on the inventory

- **`reactor.waitFor` takes a predicate object**, not arbitrary code: `{selector, textEquals?, textMatches?, visible?, count?}`. Agents compose simple predicates; we resist embedding a scripting language.
- **`reactor.fire` is the opinionated escape hatch.** Pitch: when UIA path is correct, use `reactor.click` — you exercise the real input stack, so event-wiring bugs are caught. When UIA cannot reach a Reactor handler (custom-drawn element, headless scenario, deterministic test flow), `reactor.fire` calls the handler directly by component name + event name. It is documented as bypassing the input stack, and the response includes a `via: "reactor-event-injection"` flag so agents (and humans reading logs) can see they took the shortcut.
- **`reactor.windows` shape.** Returns `[{ id, title, hwnd, bounds, isMain, buildTag }]` where `buildTag` matches the current process build (so the agent can correlate windows across reloads). `id` is the stable window id used in every node id for that window.
- **`window` arg is inferred when unambiguous.** Tools that can take a `window` default to the sole window when exactly one exists. Once a second window appears, omitting `window` on a call that needs one is an error listing the active window ids.
- **No `reactor.step`.** We debated a single tool that combines action + idle wait + tree/screenshot diff. Rejected for v1: it forces one heavy payload every call, it's harder to cache/replay, and it hides which primitive actually failed. An agent can run `click` → `waitFor` → `tree` in three calls; a library wrapper can provide the convenience.
- **Everything takes an optional `selector`.** Where that's meaningless (`reactor.components`) it's absent. Where it's optional (`reactor.screenshot` without a selector = full window) the default is the most forgiving choice.
- **`reactor.reload` is the one tool that tears down the session.** The response is flushed before the process exits with sentinel code `42`; the `mur devtools` supervisor rebuilds and respawns on the same port with a new build tag. The agent's MCP client sees its HTTP connection drop, reconnects to the pinned port, and calls `reactor.version` to confirm the new build. Any `id` the agent was holding from a previous turn is dead after a reload — its `WeakReference` target is gone with the old process. This is the contract, not a bug: reload means "forget what you knew." See §11 for full semantics.

## 9. Visual Tree Design

### Source of truth

`VisualTreeHelper.GetChild` walked from `Window.Content`, on the UI dispatcher. This yields the real `UIElement` tree after layout runs, including templated parts. A Reactor `Button` appears as (typically) `Button → ContentPresenter → TextBlock`, each with its own rendered rect. This is deliberate: layout bugs live in the template.

### Node schema

```jsonc
{
  "$schema": "reactor-tree/1",         // schema version, pinned on every payload
  "window": "main",                    // stable window id this node belongs to
  "id": "r:main/Counter.btn-inc",      // stable handle; window-scoped, see §13
  "type": "Button",                    // UIElement runtime type, unqualified
  "typeFullName": "Microsoft.UI.Xaml.Controls.Button",
  "name": "IncrementButton",           // x:Name if set, else null
  "tag": null,                         // ToString of Tag if a primitive, else null
  "automationId": "btn-inc",
  "automationName": "Increment",
  "automationControlType": "Button",   // UIA control type
  "isVisible": true,                   // Visibility==Visible && actual size > 0
  "isEnabled": true,
  "isKeyboardFocusable": true,

  "bounds": {                          // rendered rect in window client coords
    "x": 120, "y": 80, "width": 96, "height": 32
  },
  "desiredSize": { "width": 96, "height": 32 },
  "actualSize":  { "width": 96, "height": 32 },

  "layout": {                          // default-on layout-debug fields
    "margin":  [4, 4, 4, 4],
    "padding": [8, 4, 8, 4],
    "horizontalAlignment": "Left",
    "verticalAlignment":   "Center",
    "horizontalContentAlignment": "Center",
    "verticalContentAlignment":   "Center"
  },

  "context": {                         // constraint context from parent
    "parentType": "StackPanel",
    "stackOrientation": "Vertical"
    // or Grid.Row/Column/RowSpan/ColumnSpan, Canvas.Left/Top, DockPanel.Dock, etc.
  },

  "visual": {                          // default-on visual-debug fields
    "opacity": 1.0,
    "clip": null,                      // { x,y,w,h } if Clip is set
    "zIndex": 0,
    "renderTransform": null            // matrix or named transform if non-identity
  },

  "text": "Increment",                 // flattened text content if any
  "childIds": ["r:main/node-231", "r:main/node-232"],
  "parentId": "r:main/Counter.root",

  "reactor": {                         // optional, sparse; present only when source map resolves
    "component": "CounterDemo",
    "file": "Program.cs",
    "line": 42,
    "authoredType": "Button"           // the Reactor-level factory name
  }
}
```

Serialized as a **flat array** (`TreeNode[]`) with `parentId`/`childIds` for navigation, rather than a nested object. Flat arrays are cheaper to diff across turns, easier to filter, and let the agent request a subtree by passing a `selector` without the server having to reshape a nested structure.

### Views

- **`view: "summary"`** (default): `id`, `type`, `name`, `automationId`, `automationName`, `bounds`, `text`, `isVisible`, `parentId`, `childIds`, `reactor?`. This is what an agent reaches for most often; it's the cheapest in tokens.
- **`view: "full"`**: everything above. Requested when the agent is chasing a specific layout bug.

A future `view: "diff"` could compare against a named snapshot the agent captured earlier (§17, phase 3).

### Reactor source mapping

The `reactor` block is filled by the source mapper from spec 010 when a node's construction site is traceable. For nodes that come out of a `DataTemplate`, a framework `ControlTemplate`, or a WinUI-internal wrapper, `reactor` is omitted. We do not synthesize a reasonable guess; agents rely on the presence or absence of the backref to decide whether to edit source or just report a rendered-layer observation.

### What we deliberately don't expose (yet)

- Full `DataContext` values. Shape only (type name). Arbitrary object dumps are a privacy and serialization pit.
- Full binding expressions. We include the fact that a property is bound and the path, not the resolved value at every node.
- Per-node style dictionaries. Effective foreground/background colors can be derived when needed via a dedicated tool; flooding every node with style info is not worth the weight.

## 10. Screenshot Design

### Determinism

`reactor.screenshot` accepts `waitIdle: bool` (default `true`). When set, the server:

1. Forces a layout/render pass via `UpdateLayout` + a dispatcher round-trip at `Low` priority.
2. Waits one frame via `CompositionTarget.Rendering`.
3. Captures via `PrintWindow` (same path as `PreviewCaptureServer`).

Result: the agent does not see half-laid-out frames. For animation-heavy screens, the agent can set `waitIdle: false` and accept a potentially mid-transition capture.

### Per-component framing

`selector` narrows the capture to an element's rendered bounds, not the whole window. Internally we still capture the window via `PrintWindow` and crop to the node's bounds from the registry; this is cheaper and more reliable than trying to render one element in isolation.

### One-shot CLI mode

`dotnet run -- --devtools screenshot <Component> --out path.png` runs the app to first-render-complete, captures, and exits. No interactive window, no MCP server, no capture server. Useful for agents that just want a PNG per edit without a persistent session, and for CI.

### Diffing

**Deferred.** The hypothesis is that the agent is better at caching `{tree, png}` pairs in its own turn context and diffing them itself than we are at committing to a server-side diff format up front. Phase 3 keeps `reactor.snapshot(name)` and `reactor.diff(nameA, nameB)` on the roadmap as named, explicitly-deferred features — we reconsider after observing what client-side diffing patterns agents actually adopt in v1. If they consistently reimplement the same server-side primitive, we ship it; if they don't, we drop it from the roadmap rather than add surface area nobody uses.

## 11. Automation Design

### UIA is the bus

Every rendered WinUI control exposes a UIA peer (per spec 006 §UIA Tree — *Passthrough*). `reactor.click`, `reactor.type`, `reactor.toggle`, `reactor.select`, `reactor.scroll`, `reactor.focus`, `reactor.invoke` all resolve the selector to a UIA element and call the appropriate UIA pattern provider. No synthesized `SendInput`, no pixel coordinates.

Implementation note: we host a UIA *client* inside the Reactor process (not an out-of-process client) — `UIAutomationClient` against the app's own HWND. This avoids cross-process boundary costs and works identically to what Accessibility Insights does when pointed at the window.

### What `reactor.click` actually does

1. Resolve selector → node registry entry → `UIElement` on UI thread.
2. Fetch the UIA element for that HWND + runtime id.
3. Prefer `IInvokeProvider.Invoke` when available.
4. Fall back to `ITogglePattern` or `ISelectionItemProvider` if the control advertises those instead.
5. Fall back to a dispatched pointer-press + release synthesized through the UIA automation client if none of the patterns apply.

### Reactor event injection (`reactor.fire`)

The escape hatch. Calls a Reactor event handler directly by component name + event name, bypassing UIA and the input stack. Appropriate when:

- The control is custom-drawn and has no usable UIA peer.
- A test-style flow needs deterministic handler invocation without caring about input plumbing.
- The agent is verifying state transitions, not input wiring.

Inappropriate when the agent is debugging *why* a click isn't working — use `reactor.click` for that, so the input path actually runs. The tool response carries `via: "reactor-event-injection"` so logs and traces make the shortcut visible.

### Selector resolution order

For every tool that takes a selector, the server resolves in this order:

1. **Explicit node id** — `"r:main/Counter.btn-inc"`. Fastest, stable across turns, window-scoped. The preferred form once the agent has a tree in hand.
2. **Automation id** — `"#btn-inc"`.
3. **Automation name** — `"[name='Increment']"`.
4. **Type + optional index** — `"Button"`, `"Button[2]"`, or `"StackPanel > Button"`.
5. **Reactor source** — `"{component:'CounterDemo',line:42}"` for the rare case the agent wants to select by authored location. Resolved through the same source map that populates `reactor` on tree nodes.

Ambiguous selectors return an error with the matching node ids, so the agent can disambiguate on the next call instead of silently getting the first hit.

### Reload semantics

`reactor.reload` is the agent's explicit primitive for picking up source changes. It exists because devtools mode deliberately does not use `dotnet watch` (§6): we do not want process replacement to happen behind the agent's back.

**Lifecycle of a reload call:**

1. Agent edits source files.
2. Agent calls `reactor.reload` (optionally with a `component` arg to switch focus after restart).
3. Server flushes the response `{ ok: true, exitingBuild: "<old-tag>" }` synchronously.
4. Server closes MCP and capture listeners; window closes on the dispatcher; process exits with sentinel code `42`.
5. `mur devtools` supervisor sees exit code 42, runs `dotnet build`, relaunches `dotnet run -- --devtools run` on the same pinned `MCP_PORT`.
6. New process prints `[devtools] ready (build <new-tag>)` to stdout.
7. Agent's MCP client, which has been seeing a dropped connection since step 4, reconnects and calls `reactor.version`. A `build` different from `exitingBuild` confirms the reload took. Agent invalidates its cached tree and re-walks.

**What the contract does *not* include:**

- No automatic file watching. The agent decides when to reload; the server never reloads unilaterally. This keeps the session's tree-id space reasoning under the agent's control.
- No background rebuild during normal tool calls. Build errors surface on the next reload, not mid-session.
- No guarantee of continuity for `id`s across a reload. Every `r:` id from before the reload is gone, by design. Agents that want to re-find the same element after a reload re-resolve by `AutomationId` or `reactor` source backref, not by old handle.

**Build failures.** If `dotnet build` fails in step 5, the supervisor prints the error to stdout and waits. The MCP port stays unbound. The agent's reconnect attempt fails; the next `reactor.version` call returns a transport error. This is correct behavior — the agent needs to see the build error and fix it. A future polish could add a lightweight "build-status" side channel on a separate port, but it is not in v1.

**Why not signal reload over MCP itself instead of dropping the connection?** We considered a graceful "server going down" MCP notification before shutdown. Two problems: most MCP clients don't surface notifications from an about-to-close server cleanly, and the connection drop is authoritative signal regardless. Keeping the model simple — old process gone, new process up on the same port, agent reconciles via `reactor.version` — means we don't owe the agent any in-band lifecycle protocol.

## 12. State Inspection

`reactor.state` returns the reactive state visible to one or more components. Per-component selector keeps payloads small:

```jsonc
{
  "hooks": [
    {
      "component": "CounterDemo",
      "instanceId": "r:main/Counter.root",
      "hook": "useState",
      "index": 0,
      "name": "count",               // if destructured into a named local; heuristic
      "valueType": "System.Int32",
      "value": 3                     // primitive JSON; objects get shape, not full dump
    }
  ]
}
```

The implementation reads from Reactor's hook table for each `Component` instance on the UI dispatcher. For complex object values we return `{ "$type": "Foo", "$shape": { ... } }` rather than a full serialization, matching the tree's policy on `DataContext`.

**Mutation is out of scope for v1.** We expose state as a read surface. An agent that wants to force state into a shape should use `reactor.fire` (an event handler that mutates state is the intended path) or restart the app with a different scenario. We revisit after we have real agent-usage signal.

## 13. Node Identity and Selectors

### The node registry

A single `NodeRegistry` lives in the devtools service. Every tree walk writes entries into it keyed by a stable id; every tool that takes a selector reads through it.

### Id construction

Ids are constructed to be stable across re-renders, theme changes, and layout passes — but to die honestly when the underlying node leaves the visual tree. Every id is window-scoped: the full form is `r:<window-id>/<component-local-id>`.

- **With AutomationId:** `r:<window>/<Component>.<AutomationId>`. E.g. `r:main/Counter.btn-inc`.
- **With Reactor source:** `r:<window>/<Component>.<file>:<line>:<siblingIndex>`.
- **Otherwise (templated part, anonymous container):** content-addressed by the path from the nearest ancestor with a stable id, plus the type and sibling index. E.g. `r:main/Counter.btn-inc/ContentPresenter/TextBlock`.

A window closing invalidates every id in its scope; those ids return a structured "gone" error and are never reused even if a new window with the same title reopens (a new window id is assigned).

Each registry entry holds a `WeakReference<UIElement>`. When the underlying node is GC'd, the id starts returning a structured "gone" error; we do not silently remap it to a different node even if a new node would get the same constructed id.

### Why not just use UIA runtime ids?

UIA runtime ids are stable for a live element but are not meaningful off-thread or across serialization cleanly. They're also opaque to the agent — an id like `42-17-8923` is harder to reason about than `r:main/Counter.btn-inc` when the agent is composing a follow-up call from an LLM turn ago. We use UIA runtime ids *internally* for resolution, and expose the constructed `r:` ids outward.

## 14. Security and Scoping

- **Localhost only.** The MCP HTTP listener binds to `127.0.0.1`. Same as `PreviewCaptureServer`.
- **Devtools is opt-in.** `ReactorApp.Run(..., devtools: true)` is required, and it's wrapped in `#if DEBUG` in the scaffold. A Release build with `devtools: false` has no MCP server, no capture server, no extra HTTP listeners.
- **No auth in v1.** Any process on the same machine can connect to the MCP port. Acceptable for a dev-loop tool; we revisit if devtools ever ship enabled in Release or across machines.
- **`reactor.fire` authority.** The tool can reach any Reactor event handler. That's intentional for a devtools surface, and gated by the same opt-in flag. We document that enabling devtools in a production-like environment gives local processes full handler-invocation authority, and we refuse to turn it on by default in Release builds.
- **No data-dump escape hatches.** No "serialize the entire `DataContext` tree" tool. Agents can ask for shapes and bindings; extracting a user's live data through the devtools surface should not be a one-call operation.

## 15. Success Metrics

- **Time to first observation.** From `mur devtools <path>` to the agent's first successful `reactor.tree` call: under 5 seconds on a warm build, under 12 on a cold build.
- **Per-call latency.** `reactor.tree` under 50 ms for a 200-node app; `reactor.screenshot` under 150 ms at `waitIdle: true`; atomic UIA verbs under 30 ms.
- **Iteration loop length.** Number of MCP calls an agent makes to diagnose a known layout bug (e.g. a clipped `TextBlock` in a `StackPanel`): median ≤ 4, 90th percentile ≤ 8. We measure this by replaying captured transcripts against seeded bugs.
- **Human-free success rate.** Percentage of end-to-end tasks ("make the counter button red and verify the click still increments") an agent completes using only MCP tools, no human inspection step. Target > 70% on a seeded task set by phase 3.
- **Unused surface.** Any MCP tool not called across 30 consecutive agent sessions is a candidate for removal. The point is a small, well-used contract, not completeness.

## 16. Open Questions

### Resolved

1. **MCP transport for v1: loopback HTTP or stdio?**
   **Resolved: HTTP.** Loopback HTTP (JSON-RPC) in v1. Survives process replacement on a pinned port without forcing the launcher to proxy MCP framing. Revisit stdio if reconnect UX proves painful in practice.

2. **Multi-window apps.**
   **Resolved: supported in v1, top-level Windows only, ids encode window.** One MCP server per process serves all windows. Node ids are `r:<window>/<local-id>`; tools that take no id take an optional `window` arg that defaults to the sole window when exactly one exists. ContentDialog / Popup / Flyout are *not* windows — they appear as nodes inside their owner window's tree. See §6 "Multi-window addressing."

3. **Where does `reactor.waitFor` live?**
   **Resolved: ship the full predicate object.** `waitFor({selector, textEquals?, textMatches?, visible?, count?, timeoutMs})`. One round-trip beats N polling calls; we'll see whether compound predicates ("A *and* B") emerge as a real need, and grow toward a minimal combinator language only if they do.

4. **Tree schema versioning.**
   **Resolved: explicit version on every payload.** `"$schema": "reactor-tree/1"` on every tree response. Agents pin on the schema string; additive-only changes inside a version; breaking changes bump the version and old versions remain supported for one release.

5. **Snapshot storage.**
   **Resolved: deferred.** Hypothesis: the agent's own turn context is a better cache than a server-side store, and a server-side diff format is premature until we see what agents actually do client-side. `reactor.snapshot` / `reactor.diff` stay on the roadmap (§10, phase 3) as explicitly-deferred features; we reconsider after v1 usage.

6. **MCP tool naming.**
   **Resolved: drop the inner `reactor.` prefix.** Wire names are `tree`, `click`, `screenshot`, … The plugin name (`reactor`) carries the namespace; clients see `mcp__reactor__tree`. The `reactor.*` shorthand in this spec is prose-only. See §8.

7. **Source-map latency.**
   **Resolved: defer until spec 010 lands.** If resolution is expensive per-node, make `includeReactorSource` opt-in; until we can measure, assume default-on.

### Still open

*(None at draft time — new questions land here as they surface during phase 1–2 implementation.)*

## 17. Implementation Phases

### Phase 1 — Rename and foundation

- Rename `--preview` → `--devtools run`; `--preview-list` → `--devtools list`; update `ReactorApp.Run(..., preview:)` parameter to `devtools:`.
- Keep `--preview` and `preview:` as deprecated aliases with a one-time warning for one release.
- Update the scaffold template in `Reactor.Cli/Program.cs`.
- Update the VS Code extension to spawn `--devtools run --vscode` instead of `--preview --vscode`.
- No behavior change; just naming and plumbing.

**Exit criteria:** All existing `--preview` tests pass under the new names. The VS Code preview panel continues to work.

### Phase 2 — MCP server + atomic tools (core surface)

- Add `DevtoolsMcpServer` sibling to `PreviewCaptureServer`.
- Print `MCP_ENDPOINT=...` on startup.
- Ship: `reactor.components`, `reactor.switchComponent`, `reactor.tree` (summary view), `reactor.screenshot`, `reactor.click`, `reactor.type`, `reactor.focus`, `reactor.waitFor`, `reactor.version`, `reactor.reload`.
- `mur devtools` supervisor loop: pins `MCP_PORT`, respawns on exit code 42, exits on any other code. Runs `dotnet run`, not `dotnet watch run`.
- Node registry with stable id construction.
- Selector resolution (id, automationId, automationName, type+index).
- `mur devtools` launcher in `Reactor.Cli`.

**Exit criteria:** An agent can launch `mur devtools <path>`, list components, switch to one, dump the tree, click a button, wait for a state change, screenshot the result, edit source, call `reactor.reload`, reconnect, and confirm the new build via `reactor.version` — all through MCP, without a human in the loop.

### Phase 3 — Layout debugging + tree `full` view

- Add `view: "full"` to `reactor.tree` with `desiredSize`, `actualSize`, `layout`, `context`, `visual` blocks.
- Source mapping integration (depends on spec 010 landing).
- Add `reactor.state`.
- Add `reactor.invoke`, `reactor.toggle`, `reactor.select`, `reactor.scroll`, `reactor.fire`.
- `reactor.snapshot(name)` and `reactor.diff(nameA, nameB)` — **deferred.** Kept on the roadmap but not shipped until we've observed client-side diffing patterns in v1 usage and identified a concrete gap (§10).

**Exit criteria:** An agent can diagnose a seeded layout bug (clipped text, wrong alignment, missing binding) using only tree + screenshot calls, with ≤ 8 MCP calls on the 90th percentile of seeded cases.

### Phase 4 — Polish and packaging

- stdio MCP transport as an alternative to HTTP.
- `mur devtools` publishes the MCP config fragment for popular agents (Claude Code settings, Copilot workspace, VS Code MCP) on demand: `mur devtools --print-config`.
- Observability: log every tool call (name, selector, latency, result) to a rolling file so agent transcripts are auditable.
- Performance pass: target < 50 ms `reactor.tree` for 200-node apps.

**Exit criteria:** First external user reports a layout bug they fixed entirely by pairing with an agent over `mur devtools`, with no manual inspection.
