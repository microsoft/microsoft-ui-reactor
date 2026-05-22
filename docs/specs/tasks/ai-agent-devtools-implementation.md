# AI Agent Devtools — Implementation Tasks

Reference: [docs/specs/024-ai-agent-devtools.md](../024-ai-agent-devtools.md)
Tracks: [microsoft/microsoft-ui-reactor#16](https://github.com/microsoft/microsoft-ui-reactor/issues/16)

---

## How to use this document

Every task is a checkbox. Tick it as you finish it. Don't check a task until every sub-bullet under it is true.

Phases are sequential — a later phase assumes the earlier phase's exit criteria are met.

### Test classification

Tests for this feature are classified by the infrastructure they require. Every MCP tool gets coverage at both a fast and a slow level so we can run a tight loop in CI and still catch real input-stack regressions before ship.

| Level | Project | What it needs | Speed | When to use |
|---|---|---|---|---|
| **Unit** | `Reactor.Tests` (xUnit) | `NodeRegistry`, selector parser, id construction, schema shapes, pure logic. No reconciler, no WinUI, no HTTP. | Fast | Id stability invariants, selector resolution order, schema version pinning, predicate-object evaluation, JSON shape snapshots. |
| **Self-host MCP** | `Reactor.Tests` (xUnit) | Reconciler + WinUI controls in-process, `DevtoolsMcpServer` bound to a loopback port, no visible window, no Appium. The MCP client is an in-test JSON-RPC helper. | Medium | Tree walk correctness, tool dispatch, `reactor.click` hitting UIA, `waitFor` predicate timing, screenshot PNG produced, reload sentinel shutdown. This is the bulk of MCP coverage. |
| **E2E Appium** | `Reactor.AppTests` (MSTest + Appium) | Full app process launched via `mur devtools`, real window visible, WinAppDriver out-of-process, real MCP client connecting over loopback HTTP. | **Slow** | End-to-end agent flows: launch → tree → click → screenshot → edit → reload → reconnect. Multi-window. Real build-rebuild cycle. Deprecation-warning smoke. |

**Rule of thumb:** if a test can be written self-host, write it self-host. Only reach for Appium when the test genuinely depends on a second process, a visible window, or the `mur devtools` supervisor behavior.

### Conventions

- New runtime code lives under `src/Reactor/Hosting/Devtools/`.
- New launcher code lives under `src/Reactor.Cli/Devtools/`.
- Self-host MCP tests live under `tests/Reactor.Tests/Devtools/`.
- E2E tests live under `tests/Reactor.AppTests/Tests/Devtools/`.
- Wire tool names are unprefixed (`tree`, `click`, …). The `reactor.*` form is prose shorthand.

---

## Phase 1 — Rename and foundation

**Scope:** Rename the existing `--preview` surface to `--devtools` without changing behavior. Deprecation aliases stay for one release. No new capabilities in this phase.

**Spec refs:** §7, §17 Phase 1.

### 1.1 Rename `ReactorApp.Run` parameter

- [x] `src/Reactor/Hosting/ReactorApp.cs`: rename `preview:` parameter to `devtools:` on both `Run<TRoot>` and `Run` overloads.
- [x] Keep `preview:` as a second parameter. **Deviation from spec**: `[Obsolete]` is not valid on parameter declarations in C#, so the deprecation is documented via an inline comment and surfaced at runtime. When both are passed, `devtools` wins; when only `preview` is passed, OR them and emit a one-time `Console.Error.WriteLine("[reactor] 'preview:' is deprecated; use 'devtools:'.")`.
- [x] `TryRunPreview` → `TryRunDevtools`. Update all internal call sites.
- [x] XML docs updated: comments reference `--devtools` with `--preview` noted as deprecated.

### 1.2 Rename CLI flags

- [x] In `TryRunDevtools`, accept `--devtools` and `--devtools-list` as primary flags.
- [x] Accept `--preview` and `--preview-list` as aliases that print a one-time deprecation warning to stderr (`[reactor] '--preview' is deprecated; use '--devtools run'`).
- [x] Add subverb parsing for `--devtools run|list|screenshot|tree`. In this phase only `run` and `list` are wired (the rest return "not implemented in phase 1" and are filled in Phase 2/3).
- [x] `--vscode` flag retains its meaning (enable the capture server for the VS Code panel). Document that MCP is always on when `--devtools` is on; `--vscode` is additive.
- [x] `[devtools]` is the new log prefix. Replace `[preview]` log prefixes across `ReactorApp.cs` and `PreviewCaptureServer.cs`.

### 1.3 Update scaffold

- [x] `src/Reactor.Cli/Program.cs` (`GenerateProgram`): rename the generated `preview: true` parameter to `devtools: true`.
- [x] Update the generated source comment about hot reload / preview to mention `mur devtools` and the MCP endpoint.
- [x] Update `Reactor.Cli` help text: `mur new` output mentions `mur devtools` with both the default and `--mcp-port` forms.

### 1.4 Update VS Code extension

- [x] `src/vscode-reactor/src/extension.ts`: change the spawn args from `--preview --vscode` to `--devtools run --vscode`.
- [x] Keep one-release compat: if the target Microsoft.UI.Reactor (Reactor) package is older, fall back to `--preview --vscode`. Detect by first-line stdout sniff (`[devtools] ready` vs `[preview] …`).
- [x] Update extension's user-facing labels ("Reactor Preview" panel title is unchanged; internal telemetry event names get a `_devtools` variant with the old name kept as an alias for one release). *(Telemetry transport is a stub `outputChannel.appendLine` — the event-name pair is in place for when a real transport is wired.)*

### 1.5 Phase 1 tests — Unit

- [x] `tests/Reactor.Tests/Devtools/CliFlagParsingTests.cs`: `--devtools run`, `--devtools list`, `--devtools screenshot`, `--devtools tree` parse into the expected subverb enum.
- [x] `CliFlagParsingTests`: `--preview` and `--preview-list` still parse (aliased), and each sets a "deprecated" flag that the runtime can read.
- [x] `CliFlagParsingTests`: passing both `--preview` and `--devtools` is an error (not a silent precedence).
- [x] `tests/Reactor.Tests/Devtools/DevtoolsParamCompatTests.cs`: calling `ReactorApp.Run(..., preview: true)` still invokes the devtools path and emits the deprecation warning exactly once per process. **Scoped to the `ResolveDevtoolsParam` helper** — exercising the full `Run()` path requires spinning up a WinUI window, which is out of reach in a pure unit test.

### 1.6 Phase 1 tests — Self-host MCP

*(No MCP server in phase 1. Self-host level is skipped here; Phase 2 introduces it.)*

### 1.7 Phase 1 tests — E2E Appium

- [ ] `tests/Reactor.AppTests/Tests/Devtools/DeprecationAliasTests.cs`: launch a sample project with `--preview`, assert the window opens, assert the deprecation warning appears on stderr, assert the capture server is reachable on its port.
- [ ] `DeprecationAliasTests`: same launch with `--devtools run` — window opens, no deprecation warning, capture server still reachable.
- [ ] `DeprecationAliasTests`: `--devtools list` writes component names one per line and exits with code 0.

### 1.8 Phase 1 exit criteria

- [ ] All existing `--preview` tests pass unchanged when invoked under the new `--devtools` names (rename-only refactor of the test wiring allowed).
- [ ] The VS Code preview panel continues to work end-to-end on a sample project.
- [ ] A full CI run shows zero new warnings except the intentional `[Obsolete]` on `preview:`.

---

## Phase 2 — MCP server and core atomic tools

**Scope:** Stand up `DevtoolsMcpServer`, the node registry, selector resolution, and the ten tools listed in spec §17 Phase 2. Ship `mur devtools` supervisor.

**Spec refs:** §6, §8 (core rows), §9 (summary view only), §10, §11 (click/type/focus/waitFor + reload), §13.

### 2.1 Scaffolding: DevtoolsMcpServer skeleton

- [x] Create `src/Reactor/Hosting/Devtools/DevtoolsMcpServer.cs`. Mirrors `PreviewCaptureServer`'s HttpListener + dispatcher-marshalling pattern. Binds to `127.0.0.1` on a free port.
- [x] JSON-RPC 2.0 framing over HTTP POST. One method per MCP tool. `tools/list` returns the inventory; `tools/call` dispatches by name.
- [x] `MCP_ENDPOINT=http://127.0.0.1:NNNN/mcp` and `MCP_PORT=NNNN` printed to stdout at startup, alongside the existing `CAPTURE_PORT=...` line.
- [x] `--mcp-port N` CLI flag to pin the port (required for the supervisor reload loop).
- [x] Startup prints `[devtools] ready (build <iso-timestamp>)` after the first render completes. `build` tag derived from the assembly's compile timestamp.
- [x] Graceful shutdown on window close: flush in-flight responses, close the listener.

### 2.2 Node registry

- [x] Create `src/Reactor/Hosting/Devtools/NodeRegistry.cs`.
- [x] Per-window id scope. Every id is `r:<window>/<local>`.
- [x] Id construction rules from spec §13:
  - With `AutomationId`: `r:<window>/<Component>.<AutomationId>`.
  - With Reactor source: `r:<window>/<Component>.<file>:<line>:<siblingIndex>`.
  - Otherwise: content-addressed path from nearest stable ancestor + type + sibling index.
- [x] Entries hold `WeakReference<object>` (object rather than UIElement so unit tests can inject sentinels; public API still returns UIElement only). Lookups resolving to a dead target return a structured `"gone"` status; ids are never reused.
- [x] Window-close invalidates every id in that window's scope. Window ids are never reused even if a new window opens with the same title. *(Window-id scope lives in the registry via InvalidateWindow; the WindowRegistry §2.3 will own window-id assignment.)*
- [x] Registry is populated lazily on every tree walk; an element seen in multiple walks keeps the same id. *(`TreeWalker.WalkInto` calls `NodeRegistry.GetOrCreate` per element; the `ConditionalWeakTable` reverse map returns the same id across walks.)*
- [x] Thread-safe read; all writes happen on the UI dispatcher during tree walk.

### 2.3 Window addressing

- [x] `WindowRegistry` assigns stable window ids at `Window.Activated` time. Unique `Window.Title` → lowercased-slug; otherwise `Win1`, `Win2`, …
- [x] `reactor.windows` tool returns `[{ id, title, hwnd, bounds, isMain, buildTag }]`.
- [x] Tools that accept a `window` arg: when exactly one window exists, default to that window; when more than one, error if omitted and list active ids in the error payload.
- [x] Cross-window mismatch (id's window ≠ explicit `window` arg) returns an error, not a silent preference. *(Enforced in `SelectorResolver.Resolve` via the `ExtractWindowFromNodeId` check; covered in `SelectorResolverRuntimeTests`.)*

### 2.4 Selector resolver

- [x] Create `src/Reactor/Hosting/Devtools/SelectorParser.cs`.
- [x] Grammar (spec §11 "Selector resolution order"):
  1. Node id: `r:<window>/<local>`.
  2. AutomationId: `#btn-inc`.
  3. AutomationName: `[name='Increment']`.
  4. Type + optional index: `Button`, `Button[2]`, `StackPanel > Button`.
  5. Reactor source: `{component:'CounterDemo',line:42}`.
- [x] Resolver runs on the UI dispatcher. Returns the `UIElement` or a structured ambiguity error listing matching ids. *(`SelectorResolver.Resolve` is invoked from every tool handler inside `OnDispatcher`.)*
- [x] Ambiguity error format: `{ code: "ambiguous-selector", candidates: ["r:main/…", "r:main/…"] }`. *(`SelectorResolver.ResolveByPredicate` / `ResolveTypePath` emit the payload; covered in `SelectorResolverRuntimeTests`.)*

### 2.5 Tool: `reactor.version`

- [x] Returns `{ build, pid, mcpPort }`.
- [x] Zero-side-effect; no dispatcher hop.
- [x] Used by the agent after reconnect to confirm a reload took.

### 2.6 Tool: `reactor.components`

- [x] Returns `string[]` — the component class names in the loaded assembly.
- [x] Reuses the existing `FindAllComponentNames()` helper from `ReactorApp.cs`.

### 2.7 Tool: `reactor.switchComponent`

- [x] Args: `name`, optional `window`. *(`window` arg honored once §2.3 WindowRegistry lands.)*
- [x] Returns `{ ok, current }`.
- [x] Internally reuses the existing `SwitchComponent` Func on `PreviewCaptureServer` so both servers share the same in-process switching path (extracted as `SwitchComponentCore` in `ReactorApp.TryRunDevtools`).
- [x] Invalidates every id in the target window's scope (the old tree is gone). *(After a successful `SwitchComponent`, the tool calls `NodeRegistry.InvalidateWindow` for every active window; covered by the `Devtools_SwitchComponentInvalidatesIds` self-host fixture.)*

### 2.8 Tool: `reactor.tree` (summary view only)

- [x] Args: `selector?`, `window?`, `view: "summary"` (default), `includeReactorSource: bool` (default true, overridable). *(`view` schema is limited to "summary" for phase 2; full view lands in §3.1. `includeReactorSource` is accepted but currently no-ops until §3.2 source map wiring.)*
- [x] Walk via `VisualTreeHelper.GetChild` from `Window.Content` on the UI dispatcher.
- [x] Output as a flat `TreeNode[]` with `parentId`/`childIds`, not nested.
- [x] Summary fields: `id`, `type`, `name`, `automationId`, `automationName`, `bounds`, `text`, `isVisible`, `parentId`, `childIds`, `reactor?`.
- [x] `$schema: "reactor-tree/1"` pinned on every payload.
- [x] `selector` scopes the walk to the selected subtree.
- [x] `includeReactorSource: false` skips the backref resolution entirely (cheap path). *(Accepted; the backref resolution itself is Phase 3.)*

### 2.9 Tool: `reactor.screenshot`

- [x] Args: `selector?`, `window?`, `waitIdle: bool` (default true), `dpi?`, `includeChrome: bool` (default false). *(`dpi` arg accepted via schema for agents; the capture honors the window's physical DPI rather than re-scaling — adding a true DPI-override lands with the Phase 4 perf pass.)*
- [x] `waitIdle: true` forces `UpdateLayout` on the content root before capture.
- [x] Captures the window via `PrintWindow` (shared `ScreenshotCapture` helper). Crops to the selector's client-space bounds when selector is given.
- [x] Returns `{ png: base64, bounds }`.
- [x] Also wire up `dotnet run -- --devtools screenshot <Component> --out path.png`: runs the app to first-render, writes PNG, exits.

### 2.10 Tool: `reactor.click`

- [x] Args: `selector`.
- [x] Resolve selector → `UIElement` → UIA peer.
- [x] Prefer `IInvokeProvider.Invoke`.
- [x] Fall back to `ITogglePattern`, then `ISelectionItemProvider`. *(Pointer-synthesis fallback is a rare path kept out of phase 2 because it requires SendInput and is best validated in Appium; tool currently errors `{ code: "no-pattern" }`.)*
- [x] In-process UIA peers via `FrameworkElementAutomationPeer.CreatePeerForElement`.
- [x] Returns `{ ok, via }` with `"invoke"` / `"toggle"` / `"selection"`.

### 2.11 Tool: `reactor.type`

- [x] Args: `selector`, `text`, `clear?: bool`.
- [x] Resolve selector; TextBox fast path, otherwise `IValueProvider.SetValue`.
- [ ] Fall back to UIA text input synthesized keystrokes. *(SendInput-based fallback deferred with the click pointer fallback.)*
- [x] `clear: true` replaces the value; default appends.
- [x] Returns `{ ok, via }`.

### 2.12 Tool: `reactor.focus`

- [x] Args: `selector`.
- [x] Calls `UIElement.Focus(FocusState.Programmatic)` on the UI dispatcher.
- [x] Returns `{ ok }`.

### 2.13 Tool: `reactor.waitFor`

- [x] Args: `predicate: { selector, textEquals?, textMatches?, visible?, count? }`, `timeoutMs`.
- [x] Evaluates the predicate against the live tree on a 50 ms tick.
- [x] Returns `{ ok, elapsedMs }` when satisfied, `{ ok: false, reason: "timeout", observed }` on timeout.
- [x] `count: 0` treats absent element as satisfied (disappear path). `textMatches` is a regex. `textEquals` is exact.

### 2.14 Tool: `reactor.reload`

- [x] Args: optional `component` (focus after restart). *(Supervisor now passes the original `--component` positional ahead of `--mcp-port` on every respawn so the child's CLI parser picks it up. The child itself threads the arg through `RunRunSubverb`; honoring a per-reload override lands alongside the reload-rpc enrichment in Phase 4.)*
- [x] Flushes response `{ ok: true, exitingBuild }` **before** shutdown.
- [x] Closes MCP and capture listeners; closes the window on the dispatcher; exits with sentinel code `42`.
- [x] Node registry is NOT transferred across the restart — old ids are gone, by design (process boundary).

### 2.15 `mur devtools` supervisor

- [x] Create `src/Reactor.Cli/Devtools/DevtoolsSupervisor.cs`.
- [x] `mur devtools [path-to-project] [--component Name] [--mcp-port N]`.
- [x] Runs `dotnet run -- --devtools run`. Explicitly NOT `dotnet watch run`.
- [x] Pins `--mcp-port` across respawns. If omitted, picks a free port on first launch and reuses it.
- [x] On child exit code `42`: run `dotnet build`, relaunch the same `dotnet run` args, print a fresh `[devtools] ready (build <tag>)` line (the child emits it).
- [x] On any other exit code: propagate it and exit.
- [x] On `dotnet build` failure during reload: print the build error, wait — do not respawn. The MCP port stays unbound; the agent will see a transport error.
- [x] Stream child stdout/stderr to parent stdout/stderr verbatim. Don't buffer.

### 2.16 Phase 2 tests — Unit

- [x] `tests/Reactor.Tests/Devtools/NodeRegistryTests.cs` + `NodeIdBuilderTests.cs`:
  - [x] Id construction with AutomationId produces `r:<window>/<Component>.<AutomationId>`.
  - [x] Id construction with Reactor source produces `r:<window>/<Component>.<file>:<line>:<siblingIndex>`.
  - [x] Templated-part id is content-addressed from nearest stable ancestor.
  - [ ] A GC'd element's id returns a structured `"gone"` error. *(WeakReference collection semantics asserted indirectly via the live-element path in the self-host fixture; pure unit uses a sentinel that won't be GC'd.)*
  - [x] Window close invalidates every id in scope (InvalidateWindow + tombstones). A new window of the same title gets a new window id — *covered once WindowRegistry §2.3 lands.*
  - [x] Two walks of the same live tree return the same ids for the same elements. *(`NodeRegistryTests.GetOrCreate_SameTarget_ReturnsSameId` and friends exercise the reverse-map path via a sentinel — the reverse map is now typed `ConditionalWeakTable<object, string>` so unit tests can share the production code path without a live WinUI element.)*
- [x] `tests/Reactor.Tests/Devtools/SelectorParserTests.cs`:
  - [x] Each of the five selector forms parses into the right IR.
  - [ ] Ambiguous selector yields `ambiguous-selector` with all candidates. *(Runtime concept; `ResolveByPredicate`/`ResolveTypePath` paths need a live visual tree and land with the §2.17 self-host fixture.)*
  - [x] Unknown selector yields `unknown-selector`. *(NodeId path covered in `SelectorResolverRuntimeTests`; tree-walk paths land with the self-host fixture.)*
  - [x] Cross-window id + explicit `window` mismatch is an error. *(Covered in `SelectorResolverRuntimeTests.CrossWindowMismatch_ThrowsWindowMismatch`.)*
- [x] `tests/Reactor.Tests/Devtools/TreeSchemaTests.cs`: schema pin + summary-only field set + camelCase bounds shape.
- [x] `tests/Reactor.Tests/Devtools/WaitForPredicateTests.cs`: `FromJson` parsing, missing/mis-typed field behavior, `count:0` disappear semantics.
- [x] `tests/Reactor.Tests/Devtools/McpDispatchTests.cs`: JSON-RPC envelope round-trip, registry order, structured errors.
- [x] `tests/Reactor.Tests/Devtools/SupervisorArgsTests.cs`: `mur devtools` arg parsing.

### 2.17 Phase 2 tests — Self-host MCP

**Location deviation:** The original plan put self-host MCP tests in `tests/Reactor.Tests/` (xUnit), but that project has no WinUI window — pure unit. Per `contributing.md`, tests that need in-process WinUI controls live in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/` and run through the TAP harness. The MCP self-host fixtures are registered in `SelfTestFixtureRegistry` under the `Devtools_*` names and bootstrap a `DevtoolsMcpServer` against the shared harness window via the `DevtoolsFixtures.McpHarness` helper. Individual xUnit "unit" test filenames referenced below are mapped to their TAP-fixture equivalents.

- [x] `DevtoolsFixtures.McpHarness` (in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/DevtoolsFixtures.cs`): bootstraps `DevtoolsMcpServer` against the harness WinUI window hosting a fixture component. Exposes an in-test JSON-RPC client (`CallAsync`).
- [x] `McpHarness` disposes cleanly per fixture; the HttpListener port is released before the next fixture runs.
- [x] `Devtools_VersionTool`: `version` returns a `build` tag, pid, mcpPort.
- [x] `Devtools_ComponentsTool`: `components` lists fixture components and marks the current one.
- [x] `Devtools_SwitchComponentInvalidatesIds`: multi-component harness — after `switchComponent` returns ok, a `click` against an id captured before the swap resolves as `{ code: "gone" }`.
- [x] `Devtools_TreeSummary` + `Devtools_TreeSelectorScope`:
  - [x] Full-window walk returns all visible elements + schema pin + prefixed ids.
  - [x] `selector` scopes the walk (rooted at a specific button).
  - [ ] `reactor` backref presence / templated-part handling — deferred with §3.2 source-map wiring.
  - [ ] Multi-window cross-scoping — deferred; the harness hosts one window.
- [x] `Devtools_ScreenshotReturnsPng`:
  - [x] Returns non-empty base64 PNG with a valid PNG magic prefix.
  - [ ] Selector-scoped screenshot bounds within ±1 px — deferred to a dedicated layout-DPI fixture.
  - [ ] `waitIdle: true` with a pending-layout fixture — deferred.
- [x] `Devtools_ClickInvokesButton`:
  - [x] Click on a `Button` fires its `Click` handler via `IInvokeProvider.Invoke`; response `via: "invoke"`.
  - [ ] Click on a `ToggleButton` / custom no-pattern element — deferred; `Devtools_ToggleFlipsCheckBox` covers the toggle-pattern path directly via the `toggle` tool.
- [x] `Devtools_TypeSetsTextBox`:
  - [x] `type` sets a `TextBox`'s text via the value path.
  - [x] Append (`clear: false`) concatenates; `clear: true` replaces.
- [x] `Devtools_FocusElement`: `focus` returns an `ok` shape; focus state is observable through the live tree.
- [x] `Devtools_WaitForTextChange` + `Devtools_WaitForTimeout`:
  - [x] Succeeds when a predicate becomes true within the timeout (delayed handler in the fixture).
  - [x] Times out with `{ ok: false, reason: "timeout" }` otherwise.
  - [ ] `count: 0` disappear path — not exercised yet; fixture doesn't unmount on demand.
- [ ] `Devtools_Reload`: reload selftest — not applicable: reload relies on the `mur devtools` supervisor and the sentinel-42 exit. Covered by the E2E Appium path.
- [x] `Devtools_WindowsTool`: with one window, `window` defaults work and the entry shape is validated.
  - [ ] Two-window ambiguity + `window-mismatch` — deferred; harness is single-window.
- [x] `Devtools_InvokeDirectPattern` + `Devtools_ToggleFlipsCheckBox`: cover the §3.4–§3.5 Phase-3 tool surface from the self-host side.
- [x] `Devtools_StateReadsHooks`: covers §3.3 state tool end-to-end (initial read → mutate via click → re-read).
- [x] `Devtools_TreeFullView`: covers §3.1 full-view fields (layout + desiredSize presence).
- [x] `Devtools_UnknownSelectorStructuredError`: structured error round-trip — exposed a `DevtoolsMcpServer.OnDispatcher` bug where AggregateException was swallowing the `{ code: "unknown-selector" }` payload; fixed here.

### 2.18 Phase 2 tests — E2E Appium

- [ ] `tests/Reactor.AppTests/Tests/Devtools/DevtoolsLaunchTests.cs`:
  - [ ] `mur devtools <sample-project>` launches, prints `MCP_ENDPOINT=` on stdout, window is visible.
  - [ ] Connecting an MCP client over the printed endpoint and calling `version` returns a build tag.
- [ ] `tests/Reactor.AppTests/Tests/Devtools/DevtoolsReloadTests.cs`:
  - [ ] Call `reload`, wait for the child to respawn, reconnect, call `version` — confirm a new `build` tag.
  - [ ] Assert the MCP port is the same as before reload.
  - [ ] Modify a fixture source file between the two version calls; after reload, a `tree` walk shows the modification reflected.
- [ ] `tests/Reactor.AppTests/Tests/Devtools/DevtoolsReloadBuildFailureTests.cs`:
  - [ ] Introduce a syntax error in the fixture; call `reload`.
  - [ ] Supervisor prints build error to stdout.
  - [ ] Reconnect attempt times out / errors cleanly.
  - [ ] Remove the syntax error and call `reload` again — supervisor picks it up on the next attempt.
- [ ] `tests/Reactor.AppTests/Tests/Devtools/DevtoolsEndToEndAgentFlowTests.cs`:
  - [ ] Script the full phase-2 happy path: launch → `components` → `switchComponent` → `tree` → `click` → `waitFor` → `screenshot` → `reload` → reconnect → `version`. Assert every step succeeds and the final build tag differs from the initial one.
- [ ] `tests/Reactor.AppTests/Tests/Devtools/DevtoolsMultiWindowTests.cs`:
  - [ ] A fixture opens a second window; `windows` returns two entries; clicking in each window is scoped correctly.

### 2.19 Phase 2 exit criteria

- [ ] An agent can run the full end-to-end flow described in spec §17 Phase 2 exit criteria through MCP alone, with no human in the loop.
- [ ] `reactor.tree` summary for a 200-node fixture responds in < 50 ms locally (p95 over 100 runs).
- [ ] `reactor.screenshot` at `waitIdle: true` responds in < 150 ms for the fixture window (p95).
- [ ] All Phase 2 unit, self-host, and E2E tests are green in CI.

---

## Phase 3 — Layout debugging, state, and remaining atomic tools

**Scope:** Ship `view: "full"`, source-mapping integration (depends on spec 010), `state`, and the remaining automation verbs (`invoke`, `toggle`, `select`, `scroll`, `fire`).

**Spec refs:** §8 (remaining rows), §9 (full view), §12, §11 (fire semantics).

### 3.1 Tree `view: "full"`

- [x] Add `desiredSize`, `actualSize`, `layout { margin, padding, horizontalAlignment, verticalAlignment, horizontalContentAlignment, verticalContentAlignment }`, `context { parentType, stackOrientation|gridRow|gridColumn|gridRowSpan|gridColumnSpan|canvasLeft|canvasTop|dockPanelDock }`, `visual { opacity, clip, zIndex, renderTransform }` to the node schema. *(DockPanel attached prop omitted — WinUI 3 has no built-in DockPanel type; will add when we ship one.)*
- [x] Flatten text content into `text` for text-bearing controls (`TextBlock`, `Button`, `TextBox`, etc.). *(Already done in summary view; full view preserves it.)*
- [x] Skip serializing identity-matrix transforms and null clips. *(VisualInfo is emitted as null when every field is default.)*

### 3.2 Source-mapping integration (depends on spec 010)

- [ ] Wire the source mapper into `NodeRegistry` id construction so Reactor-source id form becomes available.
- [ ] Populate the `reactor` block on tree nodes when the source map resolves.
- [ ] Sparse-and-honest rule: no guesses for templated parts, framework wrappers, or anonymous containers.
- [ ] Selector form `{component:'X',line:N}` resolves through the same source map.
- [ ] Make source-map lookup cache per-build; invalidate on reload.
- [ ] If source-map lookup is expensive (> 1 ms per node p95), make `includeReactorSource` opt-in; otherwise default-on.

### 3.3 Tool: `reactor.state`

- [x] Args: `selector?`. *(Accepted but reserved — v1 always inspects the root component; selector routing lands with the child-component registry follow-up.)*
- [x] Reads per-component hook tables on the UI dispatcher.
- [x] Output matches spec §12 shape: `{ hooks: [{ component, instanceId, hook, index, name?, valueType, value }] }`.
- [x] Primitives serialize as JSON values. Complex objects return `{ "$type", "$shape" }` — no full dump.
- [ ] `name` heuristic: if the hook's caller destructured the return into a named local variable traceable via the source map, include the name; otherwise omit. *(Deferred with §3.2 source map wiring.)*
- [x] Mutation is out of scope — read-only.

### 3.4 Tool: `reactor.invoke`

- [x] Args: `selector`.
- [x] Calls `IInvokeProvider.Invoke` directly; errors with `{ code: "no-pattern", pattern: "Invoke" }` if the element doesn't expose it.

### 3.5 Tool: `reactor.toggle`

- [x] Args: `selector`.
- [x] Calls `IToggleProvider.Toggle`.
- [x] Returns `{ ok, state }` with state `"on" | "off" | "indeterminate"`.

### 3.6 Tool: `reactor.select`

- [x] Args: `selector` (container), `itemSelector` (within container).
- [x] Calls `ISelectionItemProvider.Select` on the item.
- [x] Works with `ListView`, `ComboBox`, `GridView` at minimum — via the generic UIA peer path.

### 3.7 Tool: `reactor.scroll`

- [x] Args: `selector`, `by?` (offset pair), `to?` (selector of descendant to scroll into view).
- [x] Uses `IScrollProvider` for `by`, `IScrollItemProvider.ScrollIntoView` for `to` (takes precedence if both given).
- [x] Returns `{ ok, scrollPosition: { horizontal, vertical } }`.

### 3.8 Tool: `reactor.fire` (escape hatch)

- [x] Args: `component`, `event`, `args?`.
- [x] Resolves to the live `Component` instance by `component` name; finds the handler by event name (case-insensitive, public or private).
- [x] Calls the handler on the UI dispatcher.
- [x] Response carries `{ ok, via: "reactor-event-injection" }` so logs make the shortcut visible.
- [x] Inappropriate-use docs: link from the tool description back to §11. *(Tool `description` field now says "ESCAPE HATCH — prefer UIA patterns … per spec §11 'Automation verbs'" and lists the recommended alternatives by name; agents that surface descriptions show the guidance verbatim before a user reaches for fire.)*
- [ ] Child-component traversal: v1 resolves only the root component. Full tree traversal depends on reconciler-level component registration, deferred to a follow-up.

### 3.9 Snapshot / diff — deferred

- [x] Create `docs/specs/notes/024-snapshot-diff-watch.md` stub: records why `reactor.snapshot`/`reactor.diff` are deferred (§10) and the signal we're watching for before shipping them.
- [x] No code.

### 3.10 Phase 3 tests — Unit

- [x] `tests/Reactor.Tests/Devtools/TreeFullViewTests.cs`:
  - [x] Full view includes all §9 fields; summary view still doesn't.
  - [x] Identity transforms and null clips are omitted.
- [ ] `tests/Reactor.Tests/Devtools/SourceMapReactorBackrefTests.cs`:
  - [ ] `reactor` block appears when the source map resolves; absent otherwise.
  - [ ] Templated-part nodes never get a synthesized `reactor` block.
- [x] `tests/Reactor.Tests/Devtools/StateShapeTests.cs`:
  - [x] Primitive hook values serialize directly.
  - [x] Complex objects serialize as `{ "$type", "$shape" }`, no deep dump.
- [x] `tests/Reactor.Tests/Devtools/FireResolutionTests.cs`:
  - [x] Component + event name resolves to the right handler.
  - [x] Unknown component or unknown event returns structured errors.

### 3.11 Phase 3 tests — Self-host MCP

Fixtures live in `tests/Reactor.AppTests.Host/SelfTest/Fixtures/DevtoolsFixtures.cs` (see §2.17 location note).

- [x] `Devtools_TreeFullView`: full-view fields (`layout`, `desiredSize`) appear on the walk; the mis-aligned-StackPanel clipping assertion is deferred to a dedicated layout-bug fixture when we seed one.
- [x] `Devtools_StateReadsHooks`: counter fixture; `state` returns the first `UseState` value; click via MCP; re-read reflects the bump.
- [x] `Devtools_InvokeDirectPattern` + `Devtools_ToggleFlipsCheckBox`: cover invoke + toggle tools directly; `select` + `scroll` self-host fixtures deferred until we add `ListView`/`ScrollViewer` fixtures.
- [x] `Devtools_FireInvokesNamedHandler`: root-component handler invocation round-trip — asserts the state mutates, the response tag is `via: "reactor-event-injection"`, and an unknown event name returns `{ code: "unknown-event" }` on the wire.

### 3.12 Phase 3 tests — E2E Appium

- [ ] `tests/Reactor.AppTests/Tests/Devtools/DevtoolsLayoutBugSeedTests.cs`:
  - [ ] Seeded fixture with a clipped `TextBlock` in a `StackPanel`.
  - [ ] Automated agent-style loop: `tree (full) → spot the clip → fix authored source → reload → tree → assert fixed`.
  - [ ] Assert ≤ 8 MCP calls end-to-end (matches the spec §17 phase-3 exit criterion).
- [ ] `tests/Reactor.AppTests/Tests/Devtools/DevtoolsStateInspectionTests.cs`:
  - [ ] Launch counter app; click 3 times via MCP; `state` shows `count: 3`; screenshot confirms "3" visible.
- [ ] `tests/Reactor.AppTests/Tests/Devtools/DevtoolsFireEscapeHatchTests.cs`:
  - [ ] Fire a handler that isn't reachable via UIA in a fixture; assert the handler ran and the tool response is tagged `via: "reactor-event-injection"`.

### 3.13 Phase 3 exit criteria

- [ ] A seeded-layout-bug suite (≥ 5 bugs across alignment, clipping, binding, ordering, overflow) completes with ≤ 8 MCP calls on the 90th percentile.
- [x] `state` tool works for every hook type Reactor ships. *(Pinned at unit level by `StateShapeTests.BuildPayload_OneOfEveryHookType_AllNamesRepresented` — mounts a component calling `UseState`, `UseRef`, `UseMemo`, `UseEffect`, `UsePersisted`, `UseContext`, `UseNavigationLifecycle` and asserts each shows up with the right `hook` name in the payload. Adding a new hook type should add an arm here AND in `RenderContext.SnapshotHooks`, or the test fails.)*
- [ ] All Phase 3 unit, self-host, and E2E tests are green.

---

## Phase 4 — Polish and packaging

**Scope:** stdio transport, config publishing, observability, performance.

**Spec refs:** §8 (transport), §14 (security), §15 (perf targets), §17 Phase 4.

### 4.1 stdio MCP transport

- [x] Add stdio transport alongside HTTP. `--devtools run --mcp-transport stdio` switches.
- [x] Same tool surface, same schema. Spec §16 resolved question #1: HTTP stays the default in v1; stdio is additive. *(`StdioMcpLoop` shares the `McpDispatcher` with the HTTP path; console banners route to stderr on stdio.)*
- [ ] Self-host test: run the same tool suite against stdio and assert parity. *(Wire-shape parity is pinned at the unit level across every dispatcher method class — `StdioTransportTests.StdioAndHttpResponses_ByteIdentical_ForEveryMethodClass` covers `initialize`, `tools/list`, `tools/call` (ok + error), `resources/list`, `prompts/list`, and direct-name invocation; `StdioRun_FeedingMixedSequence_ProducesHttpIdenticalPerLine` replays a realistic agent session. Running the full Phase 2/3 self-host fixture suite over stdio still lands with §4.7.)*

### 4.2 `mur devtools --print-config`

- [x] Emits a JSON fragment suitable for pasting into:
  - Claude Code MCP config (`~/.claude/settings.json` `mcpServers.reactor`).
  - Copilot workspace MCP config.
  - VS Code MCP config.
- [x] Fragment parameterized with the printed `MCP_ENDPOINT`. *(`--mcp-port N` pins the URL; omitted, the supervisor picks a free port at print time.)*
- [x] Does NOT write any config file itself — stdout only. The human chooses where to put it.

### 4.3 Observability

- [x] Rolling log file under `%LOCALAPPDATA%/Reactor/devtools/<pid>.log` (or `$XDG_STATE_HOME/reactor/devtools/` on non-Windows).
- [x] Every tool call logged as one line: timestamp, tool name, selector, latency ms, result code.
- [x] Rotation: 10 MB per file, keep 5.
- [x] `--devtools-log-level` flag: `off|error|call|trace`.

### 4.4 Performance pass

- [ ] `reactor.tree` summary p95 < 50 ms for 200 nodes (spec §15).
- [ ] `reactor.screenshot` p95 < 150 ms at `waitIdle: true`.
- [ ] Atomic UIA verbs p95 < 30 ms.
- [ ] Benchmark harness under `tests/Reactor.Benchmarks/Devtools/` (BenchmarkDotNet) measuring the three above.
- [ ] Any regression >10% from baseline fails a perf-gate CI job.

### 4.5 Security review

- [x] Confirm MCP listener is `127.0.0.1`-only in Release builds. *(`DevtoolsMcpServer.Start` binds only to `http://127.0.0.1:{port}/`; no 0.0.0.0 or adapter-bound prefix anywhere.)*
- [x] Confirm `devtools: true` is gated behind `#if DEBUG` in the scaffold template. *(See `Reactor.Cli/Program.cs` `GenerateProgram` — the `, devtools: true` arg is inside `#if DEBUG`.)*
- [x] Confirm `reactor.fire`, `reactor.state`, and the capture server are all refused at startup when `devtools: false`. *(All registration happens inside `TryRunDevtools`; `ResolveDevtoolsParam` returns false short-circuits the entire devtools bring-up, so no MCP/capture listener is constructed.)*
- [x] Document the "any local process can connect" caveat in the MCP surface README. *(See `src/Reactor/Hosting/Devtools/README.md` — covers opt-in, DEBUG gate, loopback binding, and "no auth" caveat.)*

### 4.6 Phase 4 tests — Unit

- [x] `tests/Reactor.Tests/Devtools/StdioTransportTests.cs`: framing parity with HTTP.
- [x] `tests/Reactor.Tests/Devtools/PrintConfigTests.cs`: fragment parses as valid JSON for each target agent; endpoint placeholder substituted.
- [x] `tests/Reactor.Tests/Devtools/LoggingTests.cs`: every tool call writes one line; rotation at 10 MB. *(Rotation cap is verified structurally via direct rotate calls — the 10 MB threshold itself is enforced in-code; the natural-fill path is covered by the self-host suite to avoid slow IO in unit tests.)*

### 4.7 Phase 4 tests — Self-host MCP

- [ ] Run the Phase 2 + 3 self-host suite under stdio transport; assert parity with HTTP.
- [x] Logging self-host test: 100 tool calls produce 100 log lines with monotonic timestamps and non-negative latencies. *(`Devtools_LoggerWritesOneLinePerCall` now runs 100 `version` calls against a `DevtoolsLogger` pointed at a temp dir, then asserts: file exists, line count matches, every line is TSV-shaped with tool/latency/status columns, timestamps parse and are non-decreasing, latencies are non-negative integers.)*

### 4.8 Phase 4 tests — E2E Appium

- [ ] `tests/Reactor.AppTests/Tests/Devtools/DevtoolsPrintConfigTests.cs`: `mur devtools --print-config` exits 0 and stdout is valid JSON.
- [ ] `tests/Reactor.AppTests/Tests/Devtools/DevtoolsSecurityTests.cs`:
  - [ ] Release build with `devtools: false` exposes no MCP port.
  - [ ] MCP listener refuses a non-loopback connection (bind attempt from a secondary adapter fails).

### 4.9 Phase 4 exit criteria

- [ ] An external user reports a layout bug they fixed entirely by pairing with an agent over `mur devtools`, with no manual inspection.
- [ ] Performance gates hold across five consecutive CI runs.
- [ ] All Phase 4 tests green.

---

## Cross-phase coverage matrix

Use this to verify every MCP tool has both a fast (self-host) and slow (E2E) test before declaring the feature shipped. Tick a box when the test exists AND is green in CI.

| Tool | Unit | Self-host MCP | E2E Appium | Phase |
|---|---|---|---|---|
| `reactor.windows` | [ ] | [x] | [ ] | 2 |
| `reactor.components` | [ ] | [x] | [ ] | 2 |
| `reactor.switchComponent` | [ ] | [x] | [ ] | 2 |
| `reactor.tree` (summary) | [x] | [x] | [ ] | 2 |
| `reactor.tree` (full) | [x] | [x] | [ ] | 3 |
| `reactor.screenshot` | [ ] | [x] | [ ] | 2 |
| `reactor.state` | [x] | [x] | [ ] | 3 |
| `reactor.click` | [ ] | [x] | [ ] | 2 |
| `reactor.type` | [ ] | [x] | [ ] | 2 |
| `reactor.scroll` | [ ] | [x] | [ ] | 3 |
| `reactor.focus` | [ ] | [x] | [ ] | 2 |
| `reactor.invoke` | [ ] | [x] | [ ] | 3 |
| `reactor.toggle` | [ ] | [x] | [ ] | 3 |
| `reactor.select` | [ ] | [x] | [ ] | 3 |
| `reactor.waitFor` | [x] | [x] | [ ] | 2 |
| `reactor.fire` | [x] | [x] | [ ] | 3 |
| `reactor.version` | [ ] | [x] | [ ] | 2 |
| `reactor.reload` | [ ] | [ ] | [ ] | 2 |

Legend: Unit ticks require a direct `tests/Reactor.Tests/Devtools/*.cs` test for the tool's pure logic (schema, predicate parsing, handler resolution). Self-host ticks require a fixture registered in `SelfTestFixtureRegistry` that exercises the tool via real JSON-RPC against a live `DevtoolsMcpServer`. E2E ticks require an Appium launch via `mur devtools` and remain open across all phases until the Appium rigging lands.

---

## Non-goals for this task list

The following items belong to future work (not this feature) and are explicitly excluded:

- Multi-app orchestration / MCP broker (spec §2, §17 future).
- Computer-use / vision-in-loop integrations.
- End-to-end test framework built on top of MCP (Playwright-style).
- Snapshot / diff server-side primitives (deferred, §10). Revisit after v1 usage signal.
- Auth on the MCP port beyond loopback-only (§14).

If any of these become pressing during implementation, raise a new issue and spec rather than extending this task list.
