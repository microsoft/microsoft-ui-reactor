# Reactor Security Review: Chunking & Threat-Model Plan

**Status:** Draft — Phase 1 (scoping)
**Audience:** Reviewers performing scoped security audits of Reactor
**Companion docs:** Per-chunk threat models live alongside this file as `001-…` … `0NN-…` and are filled in during Phase 2.

---

## 1. Purpose

Reactor is a declarative C# framework for WinUI 3 desktop apps. It ships a core reconciler, a DSL, a CLI (`mur`), Roslyn analyzers/generators, a VS Code extension, a devtools server (MCP / JSON-RPC), and several sample apps — roughly **303** C# files in the core, **47** in the CLI, **31** across analyzers + generators, **12** in WinForms interop, and **208** TypeScript files in the VS Code extension.

That is too large to review as one unit. This document divides Reactor into **logical chunks** sized so a single reviewer can hold a chunk's data flows, trust boundaries, and STRIDE picture in their head, and do a focused pass without losing fidelity. Each chunk is self-contained: it has a defined input surface, a defined output/effect surface, an asserted trust boundary, and a non-overlapping scope.

Phase 1 (this doc) **defines the chunks**. Phase 2 spawns one threat-model document per chunk and runs the actual reviews against the code.

---

## 2. Trust model — assumptions that frame every chunk

Before chunking, the assumptions every chunk inherits:

| Boundary | Trust assumption |
|---|---|
| The desktop user (the one who launched the app) | Trusted. Already has the privileges the process runs as. |
| The local developer machine running `mur` / VS Code extension | Trusted. Loopback (`127.0.0.1`) and `%TEMP%` are treated as in-TCB by current design. |
| Source code, project files, and build inputs (`.cs`, `.resw`, `.csproj`, manifests) | Trusted at build time — they are what the developer authored. But **a malicious repo** opened by a developer is a real threat actor, so source generators and CLI tools must not allow a hostile repo to escalate beyond "code that compiles." |
| Markdown content rendered at runtime | Untrusted. Apps may render user-authored docs, README-style content, or downloaded files. |
| ICU / `.resw` strings rendered at runtime | Semi-trusted. Authored by app or translators, but ICU format strings can be malformed. |
| Preview-mode HTTP traffic, devtools JSON-RPC, lockfiles | Loopback-trusted. **No authentication today** — this is an explicit assumption that needs to be tested, not asserted, in review. |
| External services the CLI talks to (Azure OpenAI translate) | Untrusted on the response path. |
| Native FFI (Rust `reactorfs`, `iphlpapi`) | Trust the binary; review the marshaling boundary. |
| WinUI / WindowsAppSDK / .NET BCL | Trusted dependencies. |

**The single biggest open question for review** is whether "loopback = trusted" actually holds on a dev machine that has other local users, hostile browser tabs (CSRF / DNS-rebinding to `localhost`), or tools running with different privilege. Several chunks below force that question.

---

## 3. Chunking philosophy

Every chunk satisfies four criteria:

1. **Single trust boundary.** A chunk crosses at most one major boundary. If a subsystem spans two (e.g. CLI → external API), it is split.
2. **Bounded surface area.** Roughly ≤ ~3 KLOC and ≤ ~12 files of "review-relevant" code, ignoring large generated tables, ports of well-known algorithms, and pure data-class files.
3. **Reviewable in isolation.** A reviewer needs only this chunk's docs, its files, and the dependency contracts of its callers — not the whole framework.
4. **STRIDE-coherent.** The threats that matter for the chunk are concentrated in one or two STRIDE categories, so the threat model has a clear shape.

Where a single subsystem has both an external surface and a deep internal one (e.g. Devtools), it is split into "transport / dispatch" (external) and "tools" (internal handlers).

---

## 4. Tier-1 chunks — external attack surface (do first)

These chunks face untrusted or semi-trusted input over a transport. They are the highest-priority reviews.

### Chunk 01 — Devtools transport & dispatch

- **Scope:** `src/Reactor/Hosting/Devtools/DevtoolsMcpServer.cs`, `StdioMcpLoop.cs`, `McpDispatcher.cs`, `JsonRpc.cs`, `DevtoolsJsonContext.cs`, `LockfileRegistry.cs`, `WindowIdAllocator.cs`, `WindowRegistry.cs`
- **What it does:** Exposes Reactor's running app to a developer-tooling client over (a) a loopback HTTP listener bound to `127.0.0.1` on an ephemeral port, and (b) line-delimited JSON-RPC 2.0 over stdio. Dispatches MCP `tools/list` and `tools/call` requests.
- **External inputs:** JSON-RPC payloads (HTTP body or stdin lines), `Origin` / `Host` headers, lockfiles in `%TEMP%/reactor-devtools/*.json`.
- **External outputs:** JSON-RPC responses, lockfiles (PID, port, project path).
- **Primary STRIDE focus:**
  - **Spoofing / EoP:** any local process can connect to the loopback port; what does a hostile process get? Does the server use any auth, origin-check, or token? CSRF-via-`localhost` from a browser?
  - **Tampering:** lockfiles are untrusted (any local user can write to `%TEMP%`); does the CLI verify them before connecting? Is the atomic-write pattern actually atomic on Windows?
  - **DoS:** unbounded request size, unbounded concurrent connections, slow-loris on the HTTP listener, JSON parse cost, JSON-RPC batch amplification.
  - **Info disclosure:** what does `tools/list` reveal about the running app? PIDs, paths, component names.
- **Out of scope for this chunk:** the actual tool implementations (next chunk).

### Chunk 02 — Devtools tools (handlers)

- **Scope:** `src/Reactor/Hosting/Devtools/DevtoolsTools.cs`, `DevtoolsFireTool.cs`, `DevtoolsLogsTool.cs`, `DevtoolsStateTool.cs`, `DevtoolsPropertyTools.cs`, `DevtoolsUiaTools.cs`, `DevtoolsMenuFactory.cs`, `McpToolRegistry.cs`, `NodeRegistry.cs`, `NodeIdBuilder.cs`, `TreeWalker.cs`, `SelectorParser.cs`, `SelectorResolver.cs`, `ScreenshotCapture.cs`, `LogCaptureBuffer.cs`, `LogCaptureInstall.cs`, `DevtoolsLogger.cs`
- **What it does:** Implements the actual tools called via Chunk 01 — dispatching synthetic input ("fire"), reading state and properties, walking the tree, resolving CSS-like selectors, capturing screenshots, tailing logs.
- **External inputs:** Tool-call arguments (post-dispatch JSON), selector strings, node IDs.
- **Sensitive outputs:** Screenshots of the running window, in-memory log buffers, app state, property values.
- **Primary STRIDE focus:**
  - **Info disclosure:** screenshots and log buffers can contain user content, tokens in error messages, file paths. What is the policy on what crosses the wire?
  - **Tampering / EoP:** "fire" tools synthesize input — can a caller fire arbitrary clicks/text? On windows the user didn't consent to? Across security contexts (UAC-elevated child windows)?
  - **DoS:** selector parser complexity (regex/grammar), tree-walker cost on large trees, screenshot rate.
  - **Repudiation:** are tool invocations logged? Where, with what context?

### Chunk 03 — Preview capture server + hot reload

- **Scope:** `src/Reactor/Hosting/PreviewCaptureServer.cs`, `HotReloadService.cs`, `OverlayHostWiring.cs`, `ReconcileHighlightOverlay.cs`
- **What it does:** A loopback HTTP server (`/frame`, `/status`, `/focus`, `/components`, `/preview`) that streams JPEG frames of the running preview window to the VS Code extension and accepts component-switch POSTs. Hot-reload service wires .NET MetadataUpdate notifications into the reconciler.
- **External inputs:** HTTP GET/POST from extension; component-name strings.
- **External outputs:** Frame JPEGs, component lists, focus telemetry.
- **Primary STRIDE focus:**
  - **Spoofing:** identical loopback-trust questions as Chunk 01. Is there *any* token, or is the port the only secret?
  - **Tampering:** can a hostile local caller force a component switch / hot reload to arbitrary user code?
  - **Info disclosure:** frame content is a screenshot of the dev's app, possibly with secrets in it.
  - **DoS:** frame backpressure, MIME assumptions, request size limits.

### Chunk 04 — VS Code extension

- **Scope:** `src/vscode-reactor/src/extension.ts` and the rest of `src/vscode-reactor/src/**`, `package.json`
- **What it does:** Detects Reactor `Component`s in C# files, launches `dotnet watch run -- --preview …` as a child process, polls the preview-capture HTTP server, renders frames in a webview.
- **External inputs:** Untrusted workspace contents (C# files parsed by regex), user-supplied port number, HTTP responses from the preview server.
- **External outputs:** Subprocess invocation (command + args + cwd), webview HTML, file paths sent back to the editor.
- **Primary STRIDE focus:**
  - **EoP:** subprocess command injection — how are project paths, ports, component names quoted? Can a malicious workspace cause arbitrary command execution?
  - **Tampering:** webview content-security policy; is a malicious frame response able to inject script into the webview? (frames are images — verify that's the only thing rendered.)
  - **Info disclosure:** does the extension write secrets or paths into telemetry / output channel?
  - **DoS:** regex catastrophic backtracking on C# parsing.

### Chunk 05 — `mur` CLI ↔ devtools client

- **Scope:** `src/Reactor.Cli/Devtools/DevtoolsSupervisor.cs`, `DevtoolsVerbs.cs`, `EndpointDiscovery.cs`, `LockfileReader.cs`, `McpCliClient.cs`, `SessionCommands.cs`
- **What it does:** Discovers running devtools sessions via lockfiles, connects to their loopback HTTP endpoints, and supervises a `dotnet run` child process (respawning on exit-code-42).
- **External inputs:** Lockfile contents, HTTP responses from a devtools server, CLI args.
- **External outputs:** Subprocess invocation, HTTP requests to discovered endpoints.
- **Primary STRIDE focus:**
  - **Spoofing:** does the CLI verify the lockfile's claimed PID is the one listening on the port? Race conditions between PID reuse and connection.
  - **Tampering:** is lockfile JSON validated against a schema? Path-traversal on `project path`?
  - **EoP:** child-process arg quoting, environment passthrough, working-directory choice.

---

## 5. Tier-2 chunks — build-time integrity

These chunks process developer-authored or repo-authored input at build / CLI time. The threat actor is "a hostile repository" or "a hostile localization file" that the developer opens.

### Chunk 06 — Localization CLI

- **Scope:** `src/Reactor.Cli/Loc/ExtractCommand.cs`, `TranslateCommand.cs`, `ValidateCommand.cs`, `PruneCommand.cs`, `StatusCommand.cs`, `LocalizableStringScanner.cs`, `KeyNamer.cs`, `KeyedLocString.cs`, `LocalizableString.cs`, `ReswReader.cs`, `ReswWriter.cs`, `SourceRewriter.cs`, `InterpolationConverter.cs`, `LocCommand.cs`
- **What it does:** Walks `*.cs` source files under a user-supplied root, scans for localizable strings, rewrites source, reads/writes `.resw` XML.
- **External inputs:** User-supplied source/output paths, `.cs` files (parsed via Roslyn or regex), `.resw` XML.
- **External outputs:** Modified `.cs` files, written `.resw` XML.
- **Primary STRIDE focus:**
  - **Tampering:** path traversal / symlink following on `--source` and `--output`; rewriter writing outside the repo.
  - **DoS / Info disclosure:** XML parser exposure to XXE / billion-laughs / external DTDs.
  - **EoP via build:** can a malicious key in a `.resw` produce a generated identifier that breaks out of the literal context in emitted code? (Pairs with Chunk 08.)

### Chunk 07 — Translate command (external API egress)

- **Scope:** `src/Reactor.Cli/Loc/TranslateCommand.cs`, `AzureOpenAiProvider.cs`, `ITranslationProvider.cs`, `TranslationPrompt.cs`
- **What it does:** Sends source strings to Azure OpenAI, writes translated `.resw`.
- **External inputs:** Azure OpenAI HTTP responses (untrusted output of an LLM).
- **External outputs:** HTTP requests carrying source strings, credentials in headers.
- **Primary STRIDE focus:**
  - **Info disclosure:** what gets logged? Credentials, endpoint URLs, source strings (which may contain PII/secrets if a developer extracts them).
  - **Tampering:** does the provider validate the response is well-formed `.resw`-safe text, or paste raw model output that could carry XML/control chars into the file?
  - **Repudiation:** is the egress endpoint pinned, or is it user-configurable in a way that lets a hostile config exfiltrate strings to an attacker host?
  - **DoS / cost:** unbounded retries, unbounded concurrency, no per-run cap.

### Chunk 08 — Source generators & analyzers

- **Scope:** `src/Reactor.Localization.Generator/**` (including its `ReswParser.cs`), `src/Reactor.Analyzers/**`
- **What it does:** Roslyn `IIncrementalGenerator` consumes `.resw` AdditionalFiles and emits `Loc.g.cs`. Analyzers emit diagnostics on syntax trees.
- **External inputs:** `.resw` XML, source syntax trees.
- **External outputs:** Generated C# source compiled into the user's assembly; diagnostic messages.
- **Primary STRIDE focus:**
  - **EoP at build time:** any path where a `.resw` value is interpolated unescaped into emitted C# is direct code injection — a hostile localization PR becomes RCE in any CI that builds the repo. This is the single most important property to verify in this chunk.
  - **DoS:** generator running time on pathological `.resw` inputs.
- **Out of scope:** runtime use of the generated keys (Chunk 09).

### Chunk 09 — Docs CLI

- **Scope:** `src/Reactor.Cli/Docs/DocsCommand.cs`, `CompileCommand.cs`, `DocAssembler.cs`, `ImageProcessor.cs`, `ManifestParser.cs`, `ScreenshotCapture.cs`, `SnippetExtractor.cs`, `TemplateParser.cs`
- **What it does:** Reads YAML/JSON manifests, extracts code snippets from `.cs`, renders templates, captures screenshots.
- **External inputs:** Manifest files, source files, template strings, image files.
- **External outputs:** Compiled docs, image files written to disk, possibly an HTTP capture round-trip.
- **Primary STRIDE focus:**
  - **Tampering:** template injection (server-side template injection in whatever templating language is used); manifest path traversal on `include:` / `images:` style fields; SSRF if a manifest can specify URLs.
  - **DoS:** image processor exposure to malformed images.

---

## 6. Tier-3 chunks — parsers and deserializers (runtime untrusted-input)

These are pure parsing surfaces called at runtime from inputs that may not have been written by the app author.

### Chunk 10 — Markdown parser (md4c port)

- **Scope:** `src/Reactor/Markdown/Md4cParser.cs`, `Md4cParser.Block.cs`, `Md4cParser.Inline.cs`, `Md4cTypes.cs`, `Md4cEnums.cs`, `Md4cEntity.cs`, `Md4cUnicode.cs`, `Md4cHtml.cs`, `Md4cBuilder.cs`, `MarkdownBuilder.cs` (~10 KLOC; the actual review focus is the parser entry points and the HTML renderer, not the unicode tables)
- **What it does:** Parses CommonMark text, builds Reactor elements.
- **External inputs:** Arbitrary user-authored markdown.
- **Primary STRIDE focus:**
  - **DoS:** CommonMark is famously hostile — emphasis backtracking, deeply-nested lists, link-reference cycles, autolink edge cases. Compare review against md4c's known CVEs.
  - **Tampering / XSS-equivalent:** if `Md4cHtml.cs` is reachable from runtime renders, what's the sanitization story for raw HTML, `javascript:` URLs, autolink schemes? In a desktop framework "XSS" maps to "Reactor renders an attacker-controlled element with attacker-controlled props," which can become arbitrary navigation or arbitrary URL launch.
  - **EoP:** link / image URL schemes — does the renderer launch them? Are `file://`, `ms-appx://`, custom protocols filtered?

### Chunk 11 — ICU + locale formatting

- **Scope:** `src/Reactor/Core/Localization/IntlAccessor.cs`, `MessageCache.cs`, `MessageKey.cs`, `LocaleContext.cs`, `LocaleProviderElement.cs`, `IStringResourceProvider.cs`, `ReswResourceProvider.cs`, `PseudoLocalizer.cs`, `RtlHelper.cs`, `DateFormatOptions.cs`, `NumberFormatOptions.cs`, `ListFormatType.cs`
- **What it does:** Resolves message keys, parses ICU `{name, plural, ...}` format strings at runtime, applies number / date / list formatters.
- **External inputs:** ICU format strings from `.resw` (semi-trusted) plus untrusted argument values.
- **Primary STRIDE focus:**
  - **DoS:** parser behavior on malformed ICU; recursion depth on nested `select`/`plural`.
  - **Info disclosure / formatting bugs:** does an arg name collision let a translator pull values from an outer scope?
  - **Tampering:** RTL override codepoints in formatted output (homograph attacks) when the result is shown in trust-relevant UI.

### Chunk 12 — Other parsers

- **Scope:** `src/Reactor/Hosting/Devtools/SelectorParser.cs` + `SelectorResolver.cs`, `src/Reactor/Charting/PathDataParser.cs`, `src/Reactor/Core/Navigation/DeepLinkMap.cs` and any URL parsing under `src/Reactor/Core/Navigation/`
- **What it does:** Selector grammar (CSS-like), SVG `d=` path data, deep-link routing.
- **External inputs:** Selectors from devtools (also covered in Chunk 02 — review here for parser-internal threats), SVG path strings (from app or from data-driven charts), deep-link URIs.
- **Primary STRIDE focus:** DoS via complexity, parser confusion (does deep-link parsing match server-side parsing for the same URI? — confused-deputy / re-routing risk), unbounded backtracking.

### Chunk 13 — Navigation lifecycle and back-stack persistence

- **Scope:** `src/Reactor/Core/Navigation/NavigationStack.cs`, `NavigationLifecycle.cs`, `NavigationCache.cs`, `NavigationContext.cs`, `NavigationHandle.cs`, `NavigationTransition.cs`, `TransitionEngine.cs`, `NavigationDiagnostics.cs`, plus `src/Reactor/Core/PersistedStateCache.cs`
- **What it does:** Maintains nav state, serializes/deserializes back-stack across runs, exposes lifecycle guards.
- **External inputs:** Persisted state on disk (semi-trusted — written by us, but a hostile actor with disk access can mutate).
- **Primary STRIDE focus:** Tampering on persisted nav state (deserialization gadgets, type confusion), info disclosure of params in persisted state.

---

## 7. Tier-4 chunks — runtime framework core

Lower probability of direct attacker reach, but reviewable for memory-safety, concurrency, and trust-decision bugs.

### Chunk 14 — Reconciler & component model

- **Scope:** `src/Reactor/Core/Reconciler*.cs`, `Reconciler.Mount.cs`, `Reconciler.Update.cs`, `Reconciler.DragDrop.cs`, `Reconciler.Gestures.cs`, `Component.cs`, `Element.cs`, `ElementFactory.cs`, `ElementPool.cs`, `ChildReconciler.cs`, `ChildCollection.cs`, `RenderContext.cs`, `Context.cs`, `ContextScope.cs`, `ContextExtensions.cs`, `ChangeEchoSuppressor.cs`, `ObservableTreeTracker.cs`, `Observable.cs`, `QueryCache.cs`, `AsyncValue.cs`, `InfiniteResource.cs`, `ReactorFeatureFlags.cs`
- **What it does:** Diffs virtual element tree, mounts/unmounts WinUI controls, manages hooks/state.
- **Primary STRIDE focus:** Concurrency safety on hot paths; reentrancy through user effects; resource leaks → DoS; type confusion in the element pool. This chunk's threats are mostly availability and memory-safety, not confidentiality.

### Chunk 15 — Hosting, ETW, layout-cost overlay

- **Scope:** `src/Reactor/Hosting/ReactorApp.cs`, `ReactorHost.cs`, `ReactorHostControl.cs`, `PageHelper.cs`, `XamlInterop.cs`, `ReactorCoreXamlMetaDataProvider.cs`, `RenderStats.cs`, `Etw/**`, `LayoutCost/**`, `Core/Diagnostics/ReactorEventSource.cs`
- **What it does:** App bootstrap, ETW providers, layout-cost overlay rendering.
- **Primary STRIDE focus:** Info disclosure via ETW events (PII in payloads); DoS via uncapped event rings; trust of ETW consumer source (anyone with `LogonRights/SeSystemProfilePrivilege` on the machine).

### Chunk 16 — Input, focus, gestures, drag/drop

- **Scope:** `src/Reactor/Input/**`, `src/Reactor/Core/FocusRevalidationService.cs`, `Reconciler.DragDrop.cs`, `Reconciler.Gestures.cs`
- **What it does:** Wires WinUI input → Reactor handlers, drag payload marshaling.
- **Primary STRIDE focus:** Drag/drop trust boundary (apps receive payloads from arbitrary other processes — what types are deserialized?); focus trap correctness.

### Chunk 17 — Commanding & accessibility

- **Scope:** `src/Reactor/Core/Command.cs`, `CommandBindings.cs`, `CommandInterop.cs`, `StandardCommand.cs`, `AccessibilityScanner.cs`, `src/Reactor/Accessibility/**`
- **What it does:** Bundles label/icon/shortcut/action; exposes UIA properties.
- **Primary STRIDE focus:** Mostly correctness, but the accessibility surface is reachable by other automation processes and should be reviewed for unintended exposure.

---

## 8. Tier-5 chunks — native interop & unsafe code

### Chunk 18 — Sample-app native interop

- **Scope:** `samples/apps/netpulse/Native/IpHelper.cs`, `samples/apps/reactorfiles/Native/NativeFs.cs`, and any other `unsafe` / `LibraryImport` / `DllImport` in `samples/apps/**`
- **What it does:** P/Invoke to `iphlpapi.dll`; FFI to a custom `reactorfs` Rust DLL.
- **Primary STRIDE focus:** Struct layout / size assumptions, allocation lifetimes, Marshal pin safety, error-path leaks. These are samples and the threat is "a sample copied into a real app inherits a memory bug" — review for what's idiomatic and safe to copy.

### Chunk 19 — WinForms interop

- **Scope:** `src/Reactor.Interop.WinForms/**` (12 files)
- **What it does:** Hosts WinUI inside a WinForms app.
- **Primary STRIDE focus:** HWND lifetime, message-loop isolation, COM apartment crossings, win32 handle leaks.

---

## 9. Tier-6 chunks — large internal subsystems (lower attacker reach)

These are large, but their inputs come from the app developer's own code, not from external transports. They get a lighter pass focused on internal-bug classes (overflow, concurrency, panic in a port).

### Chunk 20 — Yoga port

- **Scope:** `src/Reactor/Yoga/**` (~10 files)
- **Notes:** Faithful port of Meta's Yoga. Review focus: integer overflow, NaN propagation in measurements, recursion depth on adversarial trees. Cross-reference Yoga's known CVEs.

### Chunk 21 — Charting / D3 port

- **Scope:** `src/Reactor/Charting/**` (D3 port + DSLs) — overlap with Chunk 12 for `PathDataParser.cs`
- **Notes:** Algorithm correctness and overflow on attacker-controlled chart data; SVG path parsing is in Chunk 12.

### Chunk 22 — Data system & controls

- **Scope:** `src/Reactor/Data/**`, `src/Reactor/Controls/{DataGrid,PropertyGrid,Editors,Validation,Virtualization,Formatting,MaskedTextBox,AutoSuggest}/**`
- **Notes:** Async data sources, reflection-based property grid, formatter chains. Review for reflection-based EoP (instantiating attacker-controlled types via metadata), validation bypass.

### Chunk 23 — Hooks library

- **Scope:** `src/Reactor/Hooks/**`
- **Notes:** Side-effect dispatch, focus management, devtools hook. Mostly correctness; the `UseDevtools` hook touches Chunk 02.

---

## 10. Out of scope for the security review

- **`tests/**`** — test code is not shipped; only review if a test fixture has become a runtime dependency.
- **`selfhost/**`** — distribution of the CLI binaries; supply-chain question, but covered by the release pipeline review separately.
- **`reviewer/**`** — internal PowerShell tooling for code review; not shipped to customers.
- **`skills/**`** — markdown documentation only.
- **Reactor 4.X-specific WinUI / WindowsAppSDK behavior** — treated as trusted dependency, not Reactor's review surface.
- **Sample apps' application logic** (TodoApp gameplay, wordpuzzle game logic, etc.) — sample, not framework. Their *native interop* is in Chunk 18; everything else is illustrative.

---

## 11. Suggested review order

The order encodes "where is an attacker most likely to reach Reactor" and "what dependencies must be reviewed first to make later reviews coherent."

1. **Chunk 01 + 02 + 03 + 04 + 05** — the entire devtools / preview surface together. These all share the loopback-trust assumption and one reviewer should hold the picture across them.
2. **Chunk 08** — source generator code-injection check. Cheap to do, very high impact if wrong.
3. **Chunk 06 + 07 + 09** — CLI build-time tooling.
4. **Chunk 10 + 11 + 12 + 13** — runtime parsers.
5. **Chunk 14 + 15 + 16 + 17** — framework runtime.
6. **Chunk 18 + 19** — native interop.
7. **Chunk 20 + 21 + 22 + 23** — large internal subsystems.

---

## 12. Per-chunk Phase-2 deliverable

Each chunk gets its own file in this directory, named `0NN-<chunk-slug>-threat-model.md`, with these sections:

1. **Scope** — exact file list, line-count, commit SHA reviewed.
2. **Data-flow diagram** — inputs, processing, outputs, persistence.
3. **Trust boundaries crossed** — and the assumption made at each.
4. **Asset inventory** — what's worth attacking (data, capabilities, integrity properties).
5. **STRIDE table** — one row per identified threat, with: category, threat, attacker model, impact, likelihood, current mitigation, finding/recommendation.
6. **Findings** — concrete bugs / weaknesses found during review, severity-tagged.
7. **Open questions** — assumptions to validate with the team (especially around the loopback-trust model).
8. **Out-of-scope referrals** — anything that surfaced and belongs to another chunk.

Phase 2 will create stub files for all 23 chunks and start filling them in priority order.
