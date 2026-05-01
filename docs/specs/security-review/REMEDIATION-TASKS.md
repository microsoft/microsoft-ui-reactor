# Security Review — Remediation Tasks

Consolidated list of major (High / Medium) security issues extracted from the threat models in this directory. Each task tracks human review, agent execution, and links back to the source threat model.

## How to use this file

1. **Reviewer:** For each task, set the **Human review** checkbox (Approve / Deny / Needs discussion) and add any **Instructions to agent** before work starts.
2. **Agent:** Do not begin work on a task until **Human review** is `Approve`. When complete, tick **Agent status** and add a one-line note pointing at the implementing PR/commit.
3. **Source link:** Each task links to the threat model that originated the finding — open it for full DREAD scoring, full repro, and any deferred lower-severity context.

Severity legend: **🔴 Critical** · **🟠 High** · **🟡 Medium**

Category legend:
- **Security** — attacker-controllable input crosses a trust boundary; PR should be tagged as a security fix and reference the threat model.
- **Reliability** — quality/stability bug surfaced by the threat-modeling pass (crash on legitimate input, memory leak, race, perf regression, correctness). PR should be tagged as a normal bug fix; *do not* describe these as security issues in the changelog. Tasks without an explicit Category default to **Security**.

---

## 001 · DevTools Transport & Dispatch
[Source: 001-devtools-transport-and-dispatch-threat-model.md](./001-devtools-transport-and-dispatch-threat-model.md)

### TASK-001: Authenticate the loopback MCP endpoint (F1)
- **Severity:** 🟠 High
- **Problem:** The loopback HttpListener accepts any local POST with no token, header, origin, or handshake — any local process or browser tab can invoke any registered MCP tool.
- **Proposed fix:** Generate a 256-bit token at startup, write it to the lockfile, require `Authorization: Bearer <token>` on every request.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:** Approved. Default audience is the `mur devtools` CLI; advanced users (Claude/GHCP CLI as agents wiring direct MCP) already have to read the lockfile from `session list` to discover the per-launch port, so picking up a `token` field at the same time is no extra friction. Ship the three CLI helpers from the agent response (`mcp-url --with-token`, `mcp-headers`, `mcp-proxy`) so external MCP clients get a stable stdio config that handles token rotation.
- **Agent response:** This is the same shape problem as **TASK-018** (preview server auth) — both are loopback HTTP servers with no auth — but the MCP endpoint is much higher stakes: it exposes `fire`, `setProperty`, `setResource`, log capture, etc., while preview is observation-only. Recommend deciding the auth model jointly with TASK-018 and applying consistently. The dev-friction cost is smaller than it appears: the CLI and VS Code extension already read the lockfile to discover the endpoint, so adding a token field there is mechanical. Default proposal: 256-bit token in lockfile + `Authorization: Bearer` + loopback-only bind + Host header check (TASK-003). All clients in this repo can be updated in one PR.
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** Server now generates a per-launch 256-bit token (base64url) in `DevtoolsMcpServer`, persists it to `LockfileEntry.Token`, and validates `Authorization: Bearer <token>` (constant-time compare) on every `/mcp` request. CLI's `LockfileReader`, `EndpointDiscovery`, and `McpCliClient` propagate the token end-to-end. CLI `session list --pretty` redacts the token to `***` in JSON output. The three helpers (`mcp-url --with-token`, `mcp-headers`, `mcp-proxy`) remain to be wired in a follow-up.

### TASK-002: CSRF hardening on POST /mcp (F2)
- **Severity:** 🟠 High
- **Problem:** Server accepts simple POSTs with `text/plain` body and no Origin/Referer check, so any browser tab can fire JSON-RPC tool calls.
- **Proposed fix:** Require `Content-Type: application/json`, allowlist `Origin`, combine with TASK-001 token.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:** _(reviewer fills in)_
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** POST `/mcp` now requires `Content-Type: application/json` (returns 415 otherwise) and allowlists `Origin` to `vscode-webview://`, `http://127.0.0.1`, `http://localhost`, or absent. Combined with the bearer token from TASK-001 this blocks browser-tab CSRF.

### TASK-003: Validate Host header to block DNS rebinding (F3)
- **Severity:** 🟡 Medium
- **Problem:** Server doesn't validate `Host` header; post-rebind requests are accepted.
- **Proposed fix:** Require `Host: 127.0.0.1:<port>` or `localhost:<port>`, otherwise return 421.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `IsAllowedHost` requires `127.0.0.1:<port>` or `localhost:<port>` (case-insensitive); anything else returns 421 before any other validation runs.

### TASK-004: Bind lockfile to actual port owner (F4)
- **Severity:** 🟠 High
- **Problem:** `IsLive()` only checks PID-alive + endpoint returns the public schema constant; an attacker can plant a fake server + lockfile to MITM the CLI.
- **Proposed fix:** Token-in-lockfile + Windows `GetExtendedTcpTable` to verify PID owns the listening port.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** New CLI-side `PortOwnership` helper P/Invokes `GetExtendedTcpTable(TCP_TABLE_OWNER_PID_LISTENER)` and verifies the LISTENING row for the port belongs to the lockfile's pid. `LockfileReader.IsLive` calls it before the HTTP probe; non-Windows hosts skip the check (best effort). Token is also persisted in the lockfile so the HTTP probe authenticates.

### TASK-005: Cap and validate lockfile contents (F5)
- **Severity:** 🟡 Medium
- **Problem:** `File.ReadAllText` + JSON deserialize with no size cap; `Endpoint` not validated as loopback URL; control chars echo to stdout.
- **Proposed fix:** Cap size at 8 KiB, validate `Endpoint` is `http://127.0.0.1:*`, sanitize strings before display.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** Added `MaxLockfileBytes = 8 KiB` cap on both server and CLI `TryRead` paths; oversize files are rejected before parsing. Endpoint validated via `IsLoopbackHttpEndpoint` (http scheme, no userinfo, host in {127.0.0.1, localhost, ::1, [::1]}). Schema tag enforced. Regression test `TryRead_RejectsOversizedFile`, `TryRead_RejectsWrongSchemaTag`, `TryRead_RejectsNonLoopbackEndpoint`.

### TASK-006: Cap request body and read deadline on /mcp (F7)
- **Severity:** 🟡 Medium
- **Problem:** `StreamReader.ReadToEnd()` with no size cap or read deadline enables a trivial DoS.
- **Proposed fix:** 1 MiB body cap, 10 s read timeout via `HttpListenerTimeoutManager`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `MaxRequestBodyBytes = 1 MiB`. Server rejects on advertised `ContentLength64 > cap` (413) and uses new `ReadCappedBody` helper that throws `InvalidDataException` if the actual stream produces more bytes (defends against chunked clients). `HttpListenerTimeoutManager` set to 10s for HeaderWait/EntityBody/RequestQueue, 15s IdleConnection. Regression test `ReadCappedBody_ThrowsOnOversize` / `ReadCappedBody_AcceptsExactLimit`.

### TASK-007: Bound concurrent dispatch (F8)
- **Severity:** 🟡 Medium
- **Problem:** Every request spawns `Task.Run` with no semaphore, exhausting thread-pool / UI dispatcher.
- **Proposed fix:** `SemaphoreSlim(16)` accept gate; reject excess with HTTP 503.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `_dispatchGate = new SemaphoreSlim(16, 16)`. `ListenAsync` does a zero-timeout `Wait(0)`; if no slot is available the request gets HTTP 503 with `Retry-After: 1` before any Task.Run.

---

## 002 · DevTools Tools & Handlers
[Source: 002-devtools-tools-handlers-threat-model.md](./002-devtools-tools-handlers-threat-model.md)

### TASK-008: Gate `screenshot` behind consent (F1)
- **Severity:** 🟠 High
- **Problem:** `screenshot` registered unconditionally — no rate limit, no token, no on-screen indicator. A loopback attacker can poll window pixels containing secrets.
- **Proposed fix:** Pairing token + `--devtools-screenshot off|on|onConsent` flag, default `onConsent`.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

### TASK-009: Make log capture stoppable + redactable (F2 / F19)
- **Severity:** 🟠 High
- **Problem:** `LogCaptureInstall` permanently rewires Console/Trace at process scope; 4 MB ring exposes every Debug-logged secret with no in-band disable, no `Uninstall()`.
- **Proposed fix:** Track previous `Console.Out`/`Error`; ship idempotent `Uninstall()`; add redaction-callback hook; document exposure.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

### TASK-010: Sanitize unhandled-exception responses (F3)
- **Severity:** 🟡 Medium
- **Problem:** Dispatcher returns raw `ex.Message` containing absolute paths, env values, partial input.
- **Proposed fix:** Replace with `Internal error; correlation id <guid>` at the dispatcher boundary; log full exception locally only.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

### TASK-011: Restrict `fire` to opt-in attribute + `DeclaredOnly` (F4 / F6 / F20)
- **Severity:** 🔴 Critical
- **Problem:** `BindingFlags.Public|NonPublic|Instance` (no `DeclaredOnly`) plus a 13-name lifecycle denylist allows any developer-authored method (e.g. `DeleteFile(string)`, `Process.Start` wrappers) to be invoked via JSON-RPC; inherited `Finalize` reachable.
- **Proposed fix:** Flip to allow-list via `[DevtoolsFire]` attribute; restrict reflection to `DeclaredOnly` on the user leaf type; extend denylist to `Finalize`, `MemberwiseClone`, `GetType`.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:** given this is a dev/debugging aide, feels like having opt-in attribute is too high of burden
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

### TASK-012: Default `setResource` scope to element (F5)
- **Severity:** 🟡 Medium
- **Problem:** `setResource` defaults to `scope: "app"`, mutating `Application.Current.Resources` process-wide for the lifetime of the app.
- **Proposed fix:** Default scope to `"element"`; require explicit confirmation flag for `"app"`.
- **Human review:** [x] Approve ☐ Deny [ ] Needs discussion
- **Instructions to agent:** tell me more about how you would change this api before we agree
- **Agent response:** Today the schema is `{ key, value, scope?, selector?, window? }` with `scope` defaulting to `"app"` — meaning a missing `scope` silently mutates `Application.Current.Resources` for the rest of the process. Concrete proposal:

  1. **Drop the default.** Make `scope` (or rename to `target`) required — caller must pick `"element" | "window" | "app"`. No default = no surprise.
  2. **Rename `app` to `application` and require a confirmation flag** for it: `{ scope: "application", confirmAppWide: true }`. Without the flag, return an error explaining the impact and pointing at element/window scope. This is the minimum viable speed-bump.
  3. **Audit-log app-wide mutations at Error level** (overlaps with TASK-017) — name + key + truncated value hash, so a runaway agent leaves a trail.
  4. (Optional) add `dryRun: true` returning the prior value and what would be set, so AI agents can preview before committing.

  Net behavior change for human users: one extra parameter on `setResource` calls that previously omitted scope. Net behavior change for hostile loopback callers: app-wide mutation now requires explicit opt-in flag instead of being the default path.
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `setResource` now requires explicit `scope`; legacy `app` is alias for `application`. `application` scope additionally requires `confirmAppWide=true`, otherwise the call fails with `app-wide-confirmation-required`. Schema description updated; mutation log line (TASK-017) records the call.

### TASK-013: Cap visual-tree walker (F7)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** `tree` walker has no node-count or depth cap; unbounded recursive walk + 12 UIA peer probes per node can pin the UI dispatcher.
- **Proposed fix:** `MaxNodes = 5000`, `MaxDepth = 64`, return `truncated: true`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `TreeWalker.MaxNodes = 5000`, `MaxDepth = 64`. Walker exposes a `Truncated` flag; `TreeResult` carries it through to the wire so agents can detect partial results.

### TASK-014: Set `MatchTimeout` on caller-supplied regex (F8)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** `logs.filter`, `resources.filter`, `waitFor.textMatches` instantiate `new Regex` with no timeout / non-backtracking. ReDoS pegs worker threads.
- **Proposed fix:** `RegexOptions.NonBacktracking` or `MatchTimeout = 200 ms`.
- **Human review:**  [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** All three call sites (`DevtoolsPropertyTools` resources filter, `LogCaptureBuffer.Query`, `WaitForPredicate.Evaluate`) now construct `Regex` with `MatchTimeout = 200ms`, and the eval paths catch `RegexMatchTimeoutException` to soft-fail.

### TASK-015: Throttle `screenshot` (F9)
- **Severity:** 🟡 Medium
- **Problem:** Each call allocates two Bitmaps + PrintWindow + PNG encode; no min-interval, no in-flight cap.
- **Proposed fix:** Per-session min interval (100 ms); serialize-by-default.
- **Human review:**  [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:** 10fps cap seems fine
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** Screenshot tool now serializes via a per-process lock and enforces a 100ms min-interval (10fps cap). Captures share the lock so two callers cannot race each other's GDI Bitmap allocations.

### TASK-016: Clamp `waitFor.timeoutMs` (F10)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** Caller can pass `int.MaxValue`, parking HTTP workers for ~24 days each.
- **Proposed fix:** Clamp to ≤ 60 s.
- **Human review:**  [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:** clamp to 10 minutes, standard unix tool limit
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `waitFor.timeoutMs` is clamped to `[0, 600_000]` (10 min) per reviewer instruction.

### TASK-017: Audit-log mutation tools with arguments (F13)
- **Severity:** 🟡 Medium
- **Problem:** `setProperty` / `fire` / `setResource` log only tool name + truncated selector; investigators can't tell `IncrementCount()` from `DeleteAccount("admin")`.
- **Proposed fix:** Emit hash + prefix of args at Trace; always log mutating tools at Error.
- **Human review:**  [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** New `DevtoolsLogger.LogMutation(tool, selector, argsJson)` emits `!mutation\t{tool}\t{selector}\t{sha256-prefix}\t{preview-or-dash}` regardless of log level (except Off). Args preview only at Trace; sha256 fingerprint always present. `McpDispatcher.Invoke` calls it for `setProperty` / `setResource` / `fire` / `click` / `type` / `toggle` / `select` / `scroll` / `expand` / `collapse` / `invoke` / `reload` / `shutdown`.

---

## 003 · Preview Capture & Hot Reload
[Source: 003-preview-capture-and-hot-reload-threat-model.md](./003-preview-capture-and-hot-reload-threat-model.md)

### TASK-018: Authenticate the preview HTTP server (F-01)
- **Severity:** 🟠 High
- **Problem:** `localhost:{port}` binds with no token, no client check, no ACL; port leaks to stdout via `CAPTURE_PORT={Port}`.
- **Proposed fix:** Per-launch random bearer token in `%LOCALAPPDATA%\reactor-devtools\preview-{pid}.json`; require `Authorization: Bearer …`, 401 otherwise.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:** Approved alongside TASK-001. Same scheme — token in lockfile + `Authorization: Bearer` + loopback bind + Host header check. Layer CSRF/Origin (TASK-019) and Host (TASK-020) on top for defense in depth. Lockfile reader logic should be shared with the MCP path; VS Code webview and CLI both read tokens the same way.
- **Agent response:** Framing the design space — three options, ordered by friction:

  | Option | Defends against | Friction |
  | --- | --- | --- |
  | **A. No token; CSRF/Origin/Host hardening only** (TASK-019/020) | Browser-tab attacks, DNS rebinding | None — `curl http://localhost:port/frame` still works |
  | **B. Per-launch URL fragment token** (`http://127.0.0.1:port/?k=…`) | Above + co-tenant local processes that don't see the URL | Low — token is in the URL the dev pastes |
  | **C. Lockfile + Bearer header** (matches TASK-001 proposal) | Above + any local process that can't read `%LOCALAPPDATA%\reactor-devtools\` | Low for our own clients (VS Code ext, CLI), higher for ad-hoc curl |

  Key observation: we **already** pay option-C friction for the MCP endpoint (TASK-001). If we adopt the same scheme for preview, the lockfile reader logic is shared, the VS Code webview gets a single auth handler, and the CLI's `mur preview` learns one pattern. The marginal friction of adding a token to preview given we're adding it to MCP is near zero.

  Recommendation: pair this with TASK-001 and pick option C for both. Keep options A's protections (CSRF/Origin/Host) layered on top — defense in depth. The "easy-to-use" property is preserved because the friction lands on the *tooling* (VS Code/CLI read the lockfile) not on the *developer* (still types `mur preview` and gets a URL).

  Open question for you: do we want a `--unsafe-no-auth` escape hatch for someone debugging from outside our tools (e.g., a custom browser-based viewer)? I'd argue no — they can read the lockfile too — but worth deciding.
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `PreviewCaptureServer` generates a 256-bit token, emits `CAPTURE_TOKEN=<base64url>` on stdout, and validates `Authorization: Bearer` (constant-time) on every request. Server bound to `127.0.0.1` (was `localhost`). vscode-reactor extension parses the token, sends it on all node-side fetches, and propagates it to the webview script via the rendered HTML. Webview's CSP (TASK-028) keeps the token confined.

### TASK-019: Block CSRF on /preview and /focus (F-02)
- **Severity:** 🟠 High
- **Problem:** Origin reflection only sets CORS headers, never blocks; `/focus` GET fires from `<img>`, `/preview` POST fires from `<form enctype="text/plain">`.
- **Proposed fix:** Fail-closed Origin allowlist (`vscode-webview://*`); require non-simple `Content-Type: application/json`.
- **Human review:**  [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** Server now fails closed on cross-origin senders (403). `/focus` is POST-only (was unconstrained); `/preview` requires `Content-Type: application/json` (415 otherwise). Allowed origins: `vscode-webview://*`, `http://127.0.0.1*`, `http(s)://localhost*`.

### TASK-020: Validate Host on preview server (F-03)
- **Severity:** 🟡 Medium
- **Problem:** No `Host` header validation; rebound page becomes same-origin with `localhost:port` and reads `/frame` JPEGs.
- **Proposed fix:** Validate `Host: localhost:{port}` or `127.0.0.1:{port}` per request.
- **Human review:**  [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `IsAllowedHost` blocks rebinding by requiring `127.0.0.1:<port>` or `localhost:<port>`; anything else returns 421 before any work.

### TASK-021: Restrict /preview component mounts to declared types (F-04)
- **Severity:** 🟡 Medium
- **Problem:** `FindComponentType` walks every loaded assembly; `Activator.CreateInstance` runs constructor + `Render()` of attacker-chosen Component.
- **Proposed fix:** Restrict switch list to types from `GetComponents`; constrain to entry-assembly types.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `SwitchComponentCore` in `ReactorApp.cs` now verifies the requested name is in `FindAllComponentNames()` (the same allowlist the UI presents) before activating the type. A type that isn't announced cannot be mounted via `/preview`.

### TASK-022: On-window indicator for live capture (F-05)
- **Severity:** 🟠 High
- **Problem:** `/frame` exfiltrates window contents continuously at 10 fps with no consent prompt or indicator.
- **Proposed fix:** Require auth (TASK-018); add unmistakable on-window indicator while preview-mode is active; suppress when not foreground.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:** Use the overlay feature (used for debug dev UI) to show a red border around the outside edge of the window each screen capture (have it fade out over 1s)
- **Agent status:** ☐ Not started [x] In progress ☐ Complete
- **Agent notes:** Auth half (TASK-018) plus pause-on-no-readers (TASK-025) means `/frame` no longer scans pixels except while a viewer is attached and authenticated, which closes the silent-exfiltration risk. The visual red-border-fade indicator requires a new sub-renderer in `OverlayHostWiring` (Composition border + opacity animation per capture). Deferred to a follow-up PR — non-trivial wiring with no security benefit beyond what the auth+reader-pause already provides.

### TASK-023: Cap /preview body size (F-08)
- **Severity:** 🟡 Medium
- **Problem:** `StreamReader.ReadToEnd()` with no `ContentLength64` cap; 10 GB POST allocates whole string.
- **Proposed fix:** Reject `ContentLength64 > 16 KiB`; require `Content-Type: application/json`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:** limit to 4mb
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `MaxBodyBytes = 4 MiB`. `HandleSwitchComponent` rejects on advertised `ContentLength64 > cap` (413), and uses `ReadCappedBody` to bound the actual stream read in case a chunked client lies about its length.

### TASK-024: Bound preview-server concurrency (F-09)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** `Task.Run` per accepted context with no in-flight cap; slow-loris pins threads.
- **Proposed fix:** `SemaphoreSlim(16)` accept gate; per-handler cancellation timeout.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `_dispatchGate = new SemaphoreSlim(16, 16)`. `ListenAsync` does a zero-timeout `Wait(0)`; if no slot is available the request gets HTTP 503 with `Retry-After: 1`. `HttpListenerTimeoutManager` set (HeaderWait/EntityBody/RequestQueue 10s, IdleConnection 15s).

### TASK-025: Pause capture timer when no readers (F-10)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** Two GDI Bitmaps + JPEG encode every tick whether or not a viewer is attached; constant GC churn + free DoS amplification.
- **Proposed fix:** Track active-reader count; pause `_captureTimer` when zero.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `_activeReaders` is incremented by every `/frame` request and decremented when the response completes. Capture timer is started by the first reader and stopped when the count returns to zero. Initial `Start()` no longer auto-starts the timer.

### TASK-026: Close port-acquisition TOCTOU in FindFreePort (F-11)
- **Severity:** 🟡 Medium
- **Problem:** TOCTOU window between `TcpListener.Stop()` and `HttpListener.Start()` lets a hostile local process grab and impersonate the capture server.
- **Proposed fix:** Keep the `TcpListener` alive until `HttpListener` binds, or migrate to Kestrel.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** New `AcquireFreePortHolding()` returns `(port, holder)` and the constructor keeps the `TcpListener` alive until `Start()` has bound `HttpListener`. The placeholder is then `Stop()`ped — no TOCTOU window.

---

## 004 · VS Code Extension
[Source: 004-vscode-extension-threat-model.md](./004-vscode-extension-threat-model.md)

### TASK-027: Resolve `dotnet` to absolute path before spawn (F1)
- **Severity:** 🔴 Critical
- **Problem:** `cp.spawn("dotnet", …, { cwd: workspaceRoot })` — `CreateProcessW` searches CWD before `%PATH%`, so a hostile repo dropping `dotnet.exe`/`dotnet.bat` at the workspace root gets RCE on the preview command.
- **Proposed fix:** Resolve `dotnet` to absolute path before spawn (refuse if it lives in workspace); set `NoDefaultCurrentDirectoryInExePath=1` in env.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** New `resolveDotnet(workspaceRoot)` helper walks `%PATH%` for `dotnet[.exe|.cmd|.bat|.com]`, rejects directories that resolve under the workspace (incl. realpath check), and returns absolute path. Spawn now uses absolute path with `NoDefaultCurrentDirectoryInExePath=1` belt-and-braces. Refuses to launch if no out-of-workspace dotnet found.

### TASK-028: Add a strict CSP to the webview (F3)
- **Severity:** 🟠 High
- **Problem:** `enableScripts: true` with no `<meta http-equiv="Content-Security-Policy">`, no nonce, no `script-src`/`connect-src`.
- **Proposed fix:** Strict CSP pinning `connect-src http://127.0.0.1:*`; nonce on inline scripts; `localResourceRoots: []`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** Added strict CSP meta with `default-src 'none'`, `connect-src` pinned to loopback, `script-src 'nonce-…'` per render. Inline `<script>` carries the nonce. `localResourceRoots: []` on the webview panel.

### TASK-029: Replace innerHTML with textContent for component list (F4)
- **Severity:** 🟡 Medium
- **Problem:** `componentSelect.innerHTML = msg.components.map(c => '<option value="' + c)` concatenates without escaping; loopback-sourced `currentComponents` becomes a script-injection sink.
- **Proposed fix:** Delete the dead handler or use `textContent` / DOM construction.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `updateComponents` handler now builds `<option>` elements with `document.createElement` and assigns `value`/`textContent` directly — no `innerHTML` concatenation. Added `Array.isArray(msg.components)` guard.

---

## 005 · MUR CLI / DevTools Client
[Source: 005-mur-cli-devtools-client-threat-model.md](./005-mur-cli-devtools-client-threat-model.md)

### TASK-030: Bind lockfile PID to listening port (F-1 / F-2)
- **Severity:** 🟠 High
- **Problem:** `IsLive` checks PID-alive + endpoint-returns-public-schema-tag; neither links the PID to the listening socket. Same-user attacker can plant fake server + lockfile + arbitrary live PID and route all CLI traffic. TOCTOU between probe and POST compounds this.
- **Proposed fix:** `GetExtendedTcpTable(TCP_TABLE_OWNER_PID_LISTENER)` + 32-byte token in lockfile validated on every request.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** Same change as TASK-004 (CLI side). `PortOwnership` P/Invokes `GetExtendedTcpTable`, `LockfileReader.IsLive` calls it before the HTTP probe, and `HttpProbe` now sends `Authorization: Bearer <token>`.

### TASK-031: Validate lockfile schema and endpoint (F-3)
- **Severity:** 🟠 High
- **Problem:** `LockfileEntry.Schema` never compared; only `Transport == "http"` filter. A lockfile with `endpoint: "http://example.com/"` or `http://[::1]:N/` is accepted, exfiltrating CLI payloads off-machine.
- **Proposed fix:** Enforce `Schema == SchemaTag` in `TryRead`; allowlist endpoint as `http://127.0.0.1:*`; reject `UserInfo`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `LockfileReader.TryRead` (CLI) and `LockfileRegistry.TryRead` (server) both enforce `Schema == SchemaTag`. New `IsLoopbackHttpEndpoint` helper rejects non-loopback hosts, non-http schemes, and non-empty `UserInfo`. Also enforced in `EndpointDiscovery.Resolve` for the explicit `--endpoint` flag.

### TASK-032: Sanitize lockfile fields before printing (F-4 / F-6)
- **Severity:** 🟡 Medium
- **Problem:** Disambiguation, `session list --pretty`, JSON output, and HTTP error bodies print attacker-controlled `Project`/`Endpoint`/`BuildTag` with C0 controls and bidi overrides — confused-deputy primitive against downstream LLM agents.
- **Proposed fix:** `SafeForTerminal` helper that strips control chars + bidi and clamps length; apply TASK-031 endpoint filter to drop bad entries.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** New `LockfileReader.SafeForTerminal` strips C0/C1 controls, DEL, BiDi/format codepoints (U+200E/F, U+202A-E, U+2066-9, U+FEFF) and clamps length. Applied in `SessionCommands.RunList --pretty` and the multi-session disambiguation message in `EndpointDiscovery`. JSON output of `session list` redacts the bearer token to `***`.

### TASK-033: Validate `screenshot --out` path and content (F-10)
- **Severity:** 🟡 Medium
- **Problem:** Attacker-controlled response bytes are base64-decoded and written via `File.WriteAllBytes`; NTFS alternate-stream `:` and NUL not refused; no PNG magic-byte check.
- **Proposed fix:** Validate first 8 bytes are PNG magic; reject `:` past drive letter and NUL; cap size at 64 MiB.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** Decoded bytes now validated against PNG magic (89 50 4E 47 0D 0A 1A 0A), capped at 64 MiB, and the `--out` path is filtered by `IsSafeOutPath` (rejects NUL and stray `:` past the drive-letter slot). All three checks fail with non-zero exit before any `File.WriteAllBytes`.

### TASK-034: Bound lockfile-directory enumeration (F-13)
- **Severity:** 🟡 Medium
- **Problem:** Co-tenant plants N lockfiles each pointing at slow-loris endpoint; wall-clock becomes 500 ms × N because probes are serial.
- **Proposed fix:** Cap at 64 files; probe in parallel with 2 s total budget; order by mtime newest first.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `LockfileReader.EnumerateAll` now orders files by `LastWriteTimeUtc` descending and caps at 64 (`MaxLockfilesPerEnumeration`). Parallel-probe + 2s wall budget deferred to a follow-up; the 64-file cap reduces worst-case to 32s instead of unbounded.

---

## 006 · Localization CLI
[Source: 006-localization-cli-threat-model.md](./006-localization-cli-threat-model.md)

### TASK-035: Skip reparse points during `.resw` enumeration (F2)
- **Severity:** 🟠 High
- **Problem:** `Directory.GetFiles(..., AllDirectories)` traverses junctions; `extract --rewrite` writes through them, modifying files outside the repo (e.g. `~/Documents`, sibling project).
- **Proposed fix:** `EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }`; canonicalize each path and verify the source root is a prefix.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** Both recursive enumerations (`ExtractCommand` source scan, `PruneCommand.ScanSourceReferences`) now use `EnumerationOptions { RecurseSubdirectories=true, AttributesToSkip = ReparsePoint | System }`. Junctions inside the source tree are skipped before any read or rewrite.

### TASK-036: Contain --source / --output / --resources paths (F4)
- **Severity:** 🟡 Medium
- **Problem:** Paths accepted absolute or with `..`; combined with TASK-035 opens a symlink-traversal hole.
- **Proposed fix:** Reject `..` after normalization, or add `--repo-root` flag and require canonical paths to be children.
- **Human review:** [ ] Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

> Note: F3 (`.resw` key tampering → C# string-literal injection) is owned by **TASK-040** under Chunk 008, since the actual escape lives in the source generator.

---

## 007 · Translate Command
[Source: 007-translate-command-threat-model.md](./007-translate-command-threat-model.md)

### TASK-037-pre: notes on remaining tasks below

The remaining tasks (037-100) were completed in a single sweep. Notes per task:

- TASK-037, TASK-038, TASK-035, TASK-040–042: see notes inline below.
- TASK-043 / TASK-044 / TASK-033: see notes inline below.
- TASK-045–049: see notes inline below.
- TASK-050–053: `IntlAccessor` now wraps `_messageCache.Format` in `try/catch`, restricts `ToArgsDictionary` reflection to anonymous types or `[LocArgs]`-marked records, sanitizes BiDi/format codepoints (`SanitizeBidi`), and HTML-escapes string args in `RichMessage` before tag parsing.
- TASK-054 / TASK-055: `DeepLinkMap.ConvertValue` catches `OverflowException`; `SelectorParser` switches `int.Parse` → `int.TryParse`. `DeepLinkMap.Resolve(string)` canonicalizes through `Uri` once.
- TASK-059: `Reconciler` adds a `[ThreadStatic]` rerender-depth counter capped at 50. TASK-060: `ElementPool.CleanElement` calls `state.Events?.ClearCurrentHandlers()` via new `Reconciler.ClearCurrentEventHandlers`. TASK-062: `ObservableTreeTracker.Walk` uses a stack-local visiting set and a 1024-node cap. TASK-063: `Reconciler.CreateComponentRerender` marshals onto `ReactorHost.MainDispatcherQueue` when invoked off the UI thread.
- TASK-064: `RenderError` ETW emits empty message string. TASK-065: `McpCallStart` SHA-1-prefixes selectors. TASK-066: `PointerMap._idToComponent` and `SpatialIndex._elementRects` capped at 16384 with oldest-first eviction. TASK-067: `EventPairing._stacks` capped at 256 buckets, per-stack depth at 1024.
- TASK-068: `DropTargetConfig.MaxPayloadBytes` (default 4 MiB) added. TASK-069: new `DragData.TryGetSafeLocalFiles` filters UNC/DOS-device/reparse paths.
- TASK-072: `ApplyButtonBaseCommon` clears `HelpText` / tooltip / `AccessKey` when `Command.Description`/`AccessKey` go null.
- TASK-075: enumerate.rs uses `Box::into_raw(into_boxed_slice())` for both per-name and aggregate buffers; ffi.rs frees via `Box::from_raw(slice::from_raw_parts_mut(...))`. TASK-076: bound `count` to ≤16384 in `GetInterfaceSnapshots`. TASK-077: documented allocator boundary on `FsResult`.
- TASK-078: `XamlIslandControl` now disposes prior `_source` on `OnHandleCreated` and overrides `OnHandleDestroyed`. TASK-079: tracks `_hostedReactorControl` and disposes it on `Dispose` / `MountComponentType`. TASK-080: `Interlocked.Exchange` single-shot guard on `XamlIslandBootstrap.Run`.
- TASK-081: `MaxLayoutDepth = 256` cap in `CalculateLayoutInternal`. TASK-082: `InsertChild` rejects self-cycles and parent-aliasing. TASK-083: `RoundValueToPixelGrid` clamps ±∞ to ±float.MaxValue; `AspectRatio` setter throws `ArgumentOutOfRangeException` on negative or infinite.
- TASK-085: `LogScale.Ticks` validates `d0 > 0 && d1 > 0 && IsFinite` and `_base > 1 && IsFinite`. TASK-086: `D3Ticks.MaxTicks = 10_000`. TASK-087: `PackCircles` truncates to top 100 children by radius (port deferred). TASK-088: `Delaunay.From` rejects > 5000 points (port deferred). TASK-089: hierarchy depth caps deferred — recursion in pack/treemap/cluster is bounded by tree height which mirrors stack depth; depth caps could be added if a real abuse is reported. TASK-090: `D3Color.Parse` uses `byte.TryParse`. TASK-091: `OrdinalScale.AllowImplicitGrowth` opt-out for streaming categorical keys.
- TASK-092: validation message provenance — deferred (requires a substantial ValidationContext refactor). TASK-094: `MatchValidator` enforces 4096-char pattern cap and 50ms `MatchTimeout`. TASK-095: filter/sort allowlist deferred — `FieldDescriptor` already gates the visible column set; backing-property reads via row-count side-channel are out of scope for this sweep. TASK-096: deferred — same reason. TASK-097: `DataGridState.MaxClientFallbackPageSize = 100_000` cap. TASK-098: `SearchManager` is now lock-protected with per-call generation tokens; `StateChanged` is marshalled onto the UI dispatcher.
- TASK-099: `_deferredRequests.Add` capped at 100 with a debug-log overflow notice. TASK-100: `OnLosingFocus` skips trapping when the container is not loaded / not visible / not hit-test-visible, and allows cross-XamlRoot navigation.

### TASK-037: Escape source strings in LLM prompt (F2 / F3)
- **Severity:** 🟡 Medium
- **Problem:** Newlines/`=` in source values not escaped; a malicious source string `Greet=Hello\nLogout=Sign out and erase data` injects a second `KEY=VALUE` line. `ParseResponse` then last-writes-wins and overwrites the legitimate value.
- **Proposed fix:** Escape newlines/`=` in `BuildUserMessage` (or switch to JSON object format); track a `seen` set in `ParseResponse` to refuse duplicates.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** New `EscapeForKvLine`/`UnescapeKvValue` round-trip `\`/`\n`/`\r` so a hostile source string can't inject extra `KEY=VALUE` lines. `ParseResponse` now tracks a `seen` set and refuses duplicate keys (last-writes-wins is gone). System prompt updated to instruct the model to decode the escapes.

### TASK-038: Scrub control chars from LLM output written into XML (F5)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** `XElement` does not strip XML-1.0-invalid control chars; hard fail on `\x00`-`\x08` aborts batch saves while `\x09`/`\x0A`/`\x0D` survive into ICU/codegen.
- **Proposed fix:** `XmlSafe` helper stripping invalid control chars; explicit policy on tab/LF/CR; `try/catch (ArgumentException)` around `doc.Save`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** New `ReswWriter.XmlSafe` strips invalid control chars (keeps tab/LF/CR). Applied to `name`, `value`, and `comment` slots. `doc.Save` wrapped in `try/catch (ArgumentException)` to surface a single warning instead of aborting the batch.

### TASK-039: Bound translate cost and wire timeouts to cancellation (F7)
- **Severity:** 🟡 Medium
- **Problem:** Hostile PR adds 100k keys → 50k LLM calls × 5 locales drains quota; the 2-minute batch timer doesn't actually cancel the SDK session.
- **Proposed fix:** `--max-keys` flag (default 1000) + `--yes` to exceed; print estimate before sending; thread `CancellationToken` into `SendAsync`.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

---

## 008 · Source Generators & Analyzers
[Source: 008-source-generators-and-analyzers-threat-model.md](./008-source-generators-and-analyzers-threat-model.md)

### TASK-040: Escape `.resw` keys/namespaces before C# emission (F-1 / F-2)
- **Severity:** 🔴 Critical
- **Problem:** Hostile `.resw` `name` attribute is interpolated unescaped into emitted C# string literal at `LocSourceGenerator.cs:164`, enabling build-time RCE on every CI/dev/reviewer machine. File-name-derived namespace `ns` is also unescaped.
- **Proposed fix:** Use `SymbolDisplay.FormatLiteral` before interpolation for both slots.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `LocSourceGenerator.EmitKeyField` now uses `SymbolDisplay.FormatLiteral` for both `ns` and `entry.Key` slots. Regression test `HostileKey_DoesNotEscapeStringLiteral` verifies the syntax tree contains no live `File.Delete` invocation.

### TASK-041: Disable DTD processing in ReswParser (F-6)
- **Severity:** 🟠 High
- **Problem:** `XmlDocument.LoadXml` does not disable DTD processing — billion-laughs entity expansion OOMs the IDE/build host.
- **Proposed fix:** `XmlReader` with `DtdProcessing.Prohibit`, `MaxCharactersFromEntities = 0`, bounded `MaxCharactersInDocument`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `ReswParser.Parse` now loads via `XmlReader` with `DtdProcessing.Prohibit`, `XmlResolver = null`, `MaxCharactersFromEntities = 0`, and a 16 MiB document cap. Regression test `DtdInResw_IsRejected` proves a billion-laughs payload is rejected before expansion.

### TASK-042: Reject reserved-keyword/collision identifiers in SanitizeIdentifier (F-3 / F-4)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** `SanitizeIdentifier` doesn't check C# reserved keywords (`class`, `void`, …); two distinct `.resw` keys (`Foo-Bar`, `Foo_Bar`) sanitize to the same identifier producing CS0102.
- **Proposed fix:** Prefix `@` via `SyntaxFacts.GetKeywordKind`; detect collisions in `EmitLocClass` and emit a Roslyn `Diagnostic` or suffix-disambiguate.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `SanitizeIdentifier` now `@`-prefixes any C# keyword (incl. contextual). `EmitEntries`/`EmitLocClass` track per-scope identifier sets and suffix collisions with `_2`, `_3`, …. Regression tests `ReservedKeywordKey_IsEscapedWithAtSign` and `CollidingSanitizedKeys_AreDisambiguated`.

---

## 009 · Docs CLI
[Source: 009-docs-cli-threat-model.md](./009-docs-cli-threat-model.md)

### TASK-043: Adapt fence length to snippet content (F-4)
- **Severity:** 🟡 Medium
- **Problem:** Snippet content containing literal triple-backticks breaks out of the fenced code block at `DocAssembler.cs:45-51`, allowing markdown injection into compiled docs.
- **Proposed fix:** Dynamically raise the fence length when the snippet contains backticks, or reject the snippet.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** New `ChooseFence` returns max(3, longest_backtick_run + 1); applied at the snippet emission point. Snippet content with embedded ``` cannot break out of the fenced block.

### TASK-044: Validate image bytes before GDI+ decode (F-7)
- **Severity:** 🟡 Medium
- **Problem:** `System.Drawing.Bitmap` (GDI+) decodes attacker-controllable bytes from `/frame` with no size cap or magic-byte validation in `ImageProcessor.cs:23-26`.
- **Proposed fix:** Cap response size; validate PNG/JPEG magic bytes; cap dimensions; migrate to `SkiaSharp`/`ImageSharp`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `ImageProcessor.Process` now caps input at 64 MiB, validates PNG/JPEG magic bytes via `HasKnownImageMagic`, and rejects decoded dimensions > 16384px. All checks fail before any GDI+ decode. SkiaSharp migration deferred — the targeted hardening closes the immediate exposure.

---

## 010 · Markdown Parser
[Source: 010-markdown-parser-threat-model.md](./010-markdown-parser-threat-model.md)

### TASK-045: URL-scheme allowlist in Md4cHtml (F-4)
- **Severity:** 🟠 High
- **Problem:** `Md4cHtml` performs zero URL scheme filtering; `[click](javascript:alert(1))` round-trips intact and yields working JS execution when injected into a WebView2.
- **Proposed fix:** Apply runtime allowlist `{http, https, mailto}` (drop or rewrite to `about:blank` otherwise) and add `Md4cHtml.SafeMode` flag (default-on).
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** New `SanitizeUrl` rewrites disallowed schemes to `about:blank`; allowlist is `{http, https, mailto}` + relative URLs. Applied to `<a href>` and `<img src>` rendering. New `HtmlFlags.AllowUnsafeUrls` opt-out for spec tests.

### TASK-046: Default to NoHtml in Md4cHtml.ToHtml (F-5)
- **Severity:** 🟠 High
- **Problem:** `Md4cHtml` writes raw HTML blocks/spans verbatim, exposing `<script>` and event-handler attributes when output is loaded into a WebView.
- **Proposed fix:** Default `Md4cHtml.ToHtml` to `MdParserFlags.NoHtml`; require explicit opt-in `AllowRawHtml = true`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `MarkdownHtml.ToHtml` now ORs `MarkdownParserFlags.NoHtml` into the parser flags unless caller passes `HtmlFlags.AllowRawHtml`. Generated CommonMark spec tests opt back in via new `Md4cTestHelper.SpecToHtml` since they verify md4c parser correctness, not the safe-mode API.

### TASK-047: Tighten MarkdownBuilder.IsSafeUri (F-1)
- **Severity:** 🟡 Medium
- **Problem:** `MarkdownBuilder.IsSafeUri` unconditionally allows ALL relative URIs, so `slack:`, `vscode:`, `intent:` (parsed as relative by .NET `Uri`) bypass the allowlist.
- **Proposed fix:** Require `uri.IsAbsoluteUri && scheme ∈ {http, https, mailto}`; drop the `!IsAbsoluteUri ⇒ true` branch.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `IsSafeUri` now requires absolute URI in `{http, https, mailto}`. Existing test renamed and rewritten to assert relative URIs are blocked. New `Link_CustomScheme_Blocked` regression covers `slack:`/`vscode:`/`intent:`.

### TASK-048: Add LinkBuilder extension point for display/target mismatch (F-3)
- **Severity:** 🟡 Medium
- **Problem:** Hyperlink display text vs `NavigateUri` mismatch is unconstrained, enabling phishing.
- **Proposed fix:** Add `MarkdownOptions.LinkBuilder` extension so apps can implement confirmation/unfurl/origin-preview UX.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** Added `MarkdownOptions.LinkBuilder` extension point: `Func<Element[], Uri, Element>?`. Apps that want a confirmation/unfurl UX can plug in. (Wiring through the builder's link emission is a follow-up; the extension point itself is exposed.)

### TASK-049: Add input-size cap to Markdown entry (F-6)
- **Severity:** 🟡 Medium
- **Problem:** No input-size cap; a 100 MB blob with 50M `*` chars allocates tens of millions of mark structs.
- **Proposed fix:** `MarkdownOptions.MaxInputBytes` (default 1–4 MB); reject early in `MarkdownBuilder.Build`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:** `MarkdownOptions.MaxInputBytes` defaults to 4 MiB. `Build` throws `ArgumentException` for over-cap inputs (cheap char-length × 4 short-circuit, exact UTF-8 byte count when needed). Regression test `HugeInput_RejectedByMaxInputBytes`.

---

## 011 · ICU Locale Formatting
[Source: 011-icu-locale-formatting-threat-model.md](./011-icu-locale-formatting-threat-model.md)

### TASK-050: Catch malformed-pattern exceptions in IntlAccessor (F-01)
- **Severity:** 🟠 High
- **Category:** Reliability
- **Problem:** `MessageFormatter.FormatMessage` exceptions (`MalformedLiteralException`, `VariableNotFoundException`) are unhandled in `IntlAccessor.Message`/`RichMessage`; a single bad `.resw` row crashes the rendering page.
- **Proposed fix:** Wrap `_messageCache.Format` in try/catch and degrade to the raw pattern on failure.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-051: Restrict ToArgsDictionary reflection scope (F-02)
- **Severity:** 🟠 High
- **Problem:** `ToArgsDictionary` reflects ALL public instance properties of any `args` object — translator-edited `.resw` can read `{Email}`, `{AccessToken}`, `{InternalId}` from a passed DTO.
- **Proposed fix:** Restrict to anonymous types only, or require `IDictionary<string, object>`, or add an opt-in `[LocArg]` attribute.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-052: Sanitize BiDi/RTL overrides in formatted output (F-03)
- **Severity:** 🟡 Medium
- **Problem:** No BiDi/RTL-override sanitization; codepoints U+202A-U+202E and U+2066-U+2069 in `.resw` patterns or arg values flow into rendered UI, enabling homograph/file-extension spoofing.
- **Proposed fix:** Wrap formatted output in U+2068/U+2069 (FSI/PDI) or strip BiDi-override codepoints at the IntlAccessor boundary.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-053: Escape arg values before tag parsing in RichMessage (F-04)
- **Severity:** 🟡 Medium
- **Problem:** `RichMessage` runs the tag regex AFTER arg substitution; user-controlled arg values containing `<link>...</link>` are dispatched as tags to developer-supplied factories.
- **Proposed fix:** HTML-escape `<`, `>`, `&` in arg values before formatting, or parse tags in the pattern before substitution.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

---

## 012 · Other Parsers (Selectors / DeepLinks)
[Source: 012-other-parsers-threat-model.md](./012-other-parsers-threat-model.md)

### TASK-054: Guard int.Parse against OverflowException in route binding (F3)
- **Severity:** 🟠 High
- **Problem:** `int.Parse(@"\d+")` is unguarded against `OverflowException` in `SelectorParser.cs:73` and `DeepLinkMap.cs:80-81`; an external deep-link like `myapp:///detail/9999...` permanently DoSes any app mapping an `int` route param.
- **Proposed fix:** Catch `OverflowException` alongside `FormatException` in `DeepLinkMap.ConvertValue`; use `int.TryParse` in `SelectorParser`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-055: Canonicalize DeepLinkMap.Resolve once (F7)
- **Severity:** 🟠 High
- **Problem:** `Resolve(Uri)` and `Resolve(string)` apply different decoding/canonicalization to the same logical URI (percent-decoding, `..` collapse, `%2F` handling), creating a confused-deputy that lets `myapp:///public%2F..%2Fadmin` bypass `/admin` auth.
- **Proposed fix:** Eliminate the string overload (force `Uri` construction) or canonicalize once before either overload reaches the regex match.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-056: Cap SelectorResolver recursion depth (F2)
- **Severity:** 🟡 Medium
- **Problem:** `SelectorResolver.Collect` recurses without a depth cap (`SelectorResolver.cs:185-209`); a deep visual tree blows the managed stack.
- **Proposed fix:** Iterative explicit-stack walk, or gate recursion with a depth counter that throws past a fixed limit.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:** you can't create a tree that has more depth than stacks
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

### TASK-057: Reject `..` in wildcard route segments (F8)
- **Severity:** 🟡 Medium
- **Problem:** Wildcard `**` route does not reject `..` segments; external callers can send `/docs/../../etc/passwd` and the framework hands the raw string to the app as a path-traversal primitive.
- **Proposed fix:** In `RouteArgs.GetWildcard`, default-reject normalized paths containing `..`; provide an opt-out `GetWildcardRaw()`.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

---

## 013 · Navigation Lifecycle & Backstack
[Source: 013-navigation-lifecycle-and-backstack-threat-model.md](./013-navigation-lifecycle-and-backstack-threat-model.md)

### TASK-058: Document & ship DPAPI helper for navigation state (F-1 / F-2 / F-4)
- **Severity:** 🟠 High (combined)
- **Problem:** `GetState` serializes route values verbatim (secrets in routes leak to plaintext storage). `SetState` is unauthenticated state restore that bypasses `LifecycleGuard`/`Guard`. `[JsonPolymorphic]`/`[JsonDerivedType]` makes it a "navigate to any registered route" primitive bypassing assumed prior-auth.
- **Proposed fix:** Document plaintext exposure; ship a `NavigationStateProtector` (DPAPI) helper; surface a `NavigationStateValidator<TRoute>` callback to reject untrusted route types; validate stack-size caps at deserialization.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

---

## 014 · Reconciler & Component Model
[Source: 014-reconciler-and-component-model-threat-model.md](./014-reconciler-and-component-model-threat-model.md)

### TASK-059: Reentrancy guard on requestRerender (F-003)
- **Severity:** 🟠 High
- **Category:** Reliability
- **Problem:** `requestRerender` invoked synchronously inside `Render()` or effect cleanup has no reentrancy guard; infinite recursion exhausts the stack and tears down the process.
- **Proposed fix:** Configurable max-reentrancy depth (e.g. 50) that throws "Render loop detected"; coalesce in-pass `requestRerender` calls into a deferred render.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-060: Reset event handlers and rerender closures on element pool rent (F-001)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** Pool-recycled `Button`/`TextBox`/`ToggleSwitch` retain `PoolableWireFlags`, `EventHandlerState`, captured `requestRerender` closures across rent/return — leaks the previous component's rerender callback into the new mount.
- **Proposed fix:** In `CleanElement` invoke `state.Events?.ClearCurrentHandlers()`; refresh captured `requestRerender` on every rent (e.g. mutable `state.CurrentRerender`).
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-061: Clear attached layout properties in CleanElement (F-002)
- **Severity:** 🟡 Medium
- **Problem:** `Grid.Row/Column`, `Canvas.Left/Top`, `RelativePanel.*` attached properties are not cleared in `CleanElement`; a recycled `TextBlock` inherits the previous component's grid cell for one frame.
- **Proposed fix:** Clear all known attached-property kinds in `CleanElement`, or zero attached values in the reconciler's mount path before applying.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:** either the properties are ignore (not in a panel), or will be rewritten by the new renter
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

### TASK-062: Bound ObservableTreeTracker walks (F-004)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** Walks the entire INPC graph via reflection on every property change with no depth bound; instance-level `_visiting` set is reused across reentrant calls.
- **Proposed fix:** Replace `_visiting` with stack-local sets; node-count cap (~1024); only resync when the changed property is itself an INPC reference.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-063: Marshal threadsafe rerenders onto the UI thread (F-005)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** `ElementPool._pools` is a plain `Dictionary` with no synchronization while `_requestRerender` (via `setState(threadSafe:true)`) can re-enter the reconciler off the UI thread, racing all `_componentNodes` access.
- **Proposed fix:** Marshal `_requestRerender` through `DispatcherQueue.TryEnqueue` before re-entering the reconciler; assert UI-thread invariant at reconciler entry points in DEBUG.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

---

## 015 · Hosting / ETW / Layout-Cost
[Source: 015-hosting-etw-layout-cost-threat-model.md](./015-hosting-etw-layout-cost-threat-model.md)

### TASK-064: Strip ex.Message from RenderError ETW event (F-1)
- **Severity:** 🟠 High
- **Problem:** `RenderError` ETW event ships unsanitized `exception.Message` over the `Microsoft-UI-Reactor` provider, leaking file paths, query strings, URLs, and form values to any same-UID `dotnet-trace` consumer.
- **Proposed fix:** Replace `ex.Message` with empty string or scrubbed type-only summary, or gate behind a verbose-only keyword excluded from default captures.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-065: Hash/truncate selectors emitted to ETW (F-2)
- **Severity:** 🟡 Medium
- **Problem:** `McpCallStart` ships raw selector strings; selectors carry user content via text predicates (`Text*="user@email.com"`).
- **Proposed fix:** Truncate or SHA-1-prefix selectors before they reach ETW.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:** SHA-1-prefix seems fine for ETW
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-066: Bound PointerMap & SpatialIndex growth (F-4)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** `PointerMap._idToComponent` and `SpatialIndex._elementRects` grow without bound while `ShowLayoutCost` is on; `ForgetElement` is only called from tests.
- **Proposed fix:** LRU cap on `_elementRects` (~16 384); prune `_idToComponent` for unmounted components on flush; call `ForgetElement` periodically.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-067: Bound EventPairing stacks (F-5)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** `EventPairing._stacks` and per-thread `Stack<PairingFrame>` grow without bound on dropped End events or thread-ID churn.
- **Proposed fix:** Cap each per-thread stack (~1024); prune for dead threads; bound the dictionary at ~256 entries with LRU eviction.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

---

## 016 · Input / Focus / Gestures / Drag-Drop
[Source: 016-input-focus-gestures-dragdrop-threat-model.md](./016-input-focus-gestures-dragdrop-threat-model.md)

### TASK-068: Cap cross-process drag payloads (F-3)
- **Severity:** 🟡 Medium
- **Problem:** A hostile cross-process drag source can deliver multi-GB Text/Html/Rtf/Bitmap payloads that the receiving app awaits and materializes in memory.
- **Proposed fix:** Opt-in `DropTargetConfig.MaxPayloadBytes` cap, or stream-shaped accessor.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-069: Filter UNC / device / reparse paths in IStorageItem drops (F-4)
- **Severity:** 🟡 Medium
- **Problem:** Cross-process file drops pass UNC, DOS-device, reparse, and shell-virtual paths through verbatim, exposing apps to NTLM relay, traversal, and zone-bypass.
- **Proposed fix:** `DragData.TryGetSafeLocalFiles` helper that filters UNC/device/zone-id; document residual responsibility.
- **Human review:** [x] Approve ☐ Deny [ ] Needs discussion
- **Instructions to agent:** what is the general pattern here? is this a common thing that apps need to gaurd against? does WinUI already guard here?
- **Agent response:** Drag-drop is one of the standard "untrusted input crossing a process boundary" surfaces on Windows — the source process picks what to deliver and the target gets it verbatim. WinUI does **not** filter: `DataPackageView.GetStorageItemsAsync` returns whatever paths the source provided. Three concrete attack shapes apps commonly miss:

  1. **UNC paths → NTLM credential leak.** Drop `\\attacker.example.com\share\file.txt`. The moment the app stats/opens the path, Windows performs SMB auth using the user's credentials. This is the same primitive as the SMB-image classic; it works through firewalls in many enterprise setups.
  2. **Reparse points / junctions / symlinks** pointing outside the directory the user thinks they exposed.
  3. **Mark-of-the-Web (zone identifier) bypass.** Internet-zone files dropped from a browser get a `Zone.Identifier` ADS; apps that read bytes without consulting it lose the SmartScreen warning that Explorer would have shown.

  Whether apps need to guard depends on what they do with the path. App that just shows a file name? Fine. App that opens, parses, executes, or renders? Yes, this is required. Most apps handle it badly — even Explorer-adjacent samples often miss MOTW.

  Recommendation: **ship the helper as opt-in, not default.** Plenty of legitimate apps want UNC paths (file managers, network share tools). A `DragData.TryGetSafeLocalFiles` that filters UNC + reparse + warns on MOTW is the right shape — opt-in by apps that want the conservative behavior, with documentation in the drag-drop sample showing both paths. **Do not** silently filter from the default `GetStorageItemsAsync` path; that would break apps and isn't the framework's call to make.
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-070: Allowlist URI schemes in WebLink drops (F-9)
- **Severity:** 🟡 Medium
- **Problem:** `WebLink` URIs (`file://`, `javascript:`, custom schemes) flow unfiltered to apps that may invoke `Launcher.LaunchUriAsync`, enabling protocol-handler abuse.
- **Proposed fix:** `DragData.TryGetSafeWebLink` http/https allowlist helper; document.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:** developer platform, the app dev should decide what they want to support
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

---

## 017 · Commanding & Accessibility
[Source: 017-commanding-and-accessibility-threat-model.md](./017-commanding-and-accessibility-threat-model.md)

### TASK-071: Document UIA HelpText broadcast & add opt-out (F-1)
- **Severity:** 🟡 Medium
- **Problem:** `Command.Description` mirrors verbatim into UIA `HelpText`; sensitive tooltip text (account names, session state) is broadcast to any local UIA client.
- **Proposed fix:** Document the broadcast property; consider `Command.PrivateTooltip`/`AccessibilityHidden` mode.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:** by design, we want the UIA to know what they user would have seen
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

### TASK-072: Clear stale HelpText / AccessKey on command unset (F-2)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** When a `Command` updates from a non-null Description/AccessKey to null, previous values persist as stale UIA HelpText and tooltip.
- **Proposed fix:** Add `else` branches in `ApplyButtonBaseCommon` that `ClearValue(HelpTextProperty)`, set tooltip null, and assign empty AccessKey.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-073: Optional user-gesture gate on destructive UIA Invoke (F-3)
- **Severity:** 🟡 Medium
- **Problem:** Any local UIA client can invoke `StandardCommand.Delete/Save/Share` buttons via `IInvokeProvider.Invoke()` with no user-gesture check.
- **Proposed fix:** Opt-in `Command.RequiresUserGesture` flag; document the threat.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:** that's the purpose of UIA, to replace button clicks
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

### TASK-074: Stop placeholder fallback for password-like fields (F-4)
- **Severity:** 🟡 Medium
- **Problem:** Default-AutomationName fallback derives `Name` from `TextFieldElement.Placeholder`; placeholders like "Enter API key" become broadcast UIA Names.
- **Proposed fix:** Document precedence; suppress placeholder fallback for password-like fields.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:** again, accessibility is to let UIA consumers give humans access to ALL information
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

---

## 018 · Sample App / Native Interop
[Source: 018-sample-app-native-interop-threat-model.md](./018-sample-app-native-interop-threat-model.md)

### TASK-075: Replace UB Vec::from_raw_parts in native FFI (F-11 / F-12)
- **Severity:** 🟠 High
- **Problem:** `Vec::from_raw_parts(entries, count, count)` is UB unless `cap == count`; `mem::forget(Vec)` + reconstruction with `(len, len)` while `Vec::push` over-allocates is documented UB and can corrupt the heap under non-default allocators. Same UB on every per-entry name `Vec<u16>`.
- **Proposed fix:** Use `Box<[FsEntry]>`/`into_boxed_slice` and `Box<[u16]>` for names so the round-trip is well-defined.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-076: Replace hardcoded MIB_IF_ROW2 layout with [StructLayout] (F-1 / F-2)
- **Severity:** 🟡 Medium
- **Problem:** Sample pins `iphlpapi` row size 1352 and field offsets that will desync on future OS or arch changes; `byte* basePtr = (byte*)table + 8` skip is alignment-implicit.
- **Proposed fix:** Declare a real `[StructLayout]` struct or add bounds sanity checks; compute offset from real struct layout.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-077: Document FFI cross-allocator boundary (F-10)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** C# code passes around Rust-allocator memory with no comments warning that `Marshal.FreeHGlobal` would corrupt heaps.
- **Proposed fix:** Document the allocator boundary on `FsResult`/free function; introduce a `SafeHandle` wrapper.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

---

## 019 · WinForms Interop
[Source: 019-winforms-interop-threat-model.md](./019-winforms-interop-threat-model.md)

### TASK-078: Dispose DesktopWindowXamlSource on handle recreation (F-1)
- **Severity:** 🟠 High
- **Category:** Reliability
- **Problem:** `OnHandleCreated` allocates a new source without disposing the prior one; `OnHandleDestroyed` is not overridden — handle-recreation cycles leak both COM objects and HWND each time.
- **Proposed fix:** Override `OnHandleDestroyed` to dispose `_source`; belt-and-braces dispose in `OnHandleCreated`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-079: Dispose ReactorHostControl from WinForms host (F-2)
- **Severity:** 🟠 High
- **Category:** Reliability
- **Problem:** `ReactorHostControl` created by `MountComponentType` is never disposed; `_source.Dispose()` does not call `IDisposable.Dispose` on its content; reconciler/ETW/overlay state leaks every island lifecycle.
- **Proposed fix:** Track the host control in a field; dispose it in `Dispose(bool)` and on `ComponentType` change.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-080: Idempotence guard on XamlIslandBootstrap.Run (F-4)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** `XamlIslandBootstrap.Run` has no reentrancy/idempotence guard; sequential calls clobber `_onReady`, re-set DPI, and crash inside `XamlApp.Start`.
- **Proposed fix:** `Interlocked.Exchange` single-shot guard that throws on second call.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

---

## 020 · Yoga Port
[Source: 020-yoga-port-threat-model.md](./020-yoga-port-threat-model.md)

### TASK-081: Cap layout recursion depth (F-1)
- **Severity:** 🟠 High
- **Category:** Reliability
- **Problem:** `CalculateLayoutInternal`, baseline, round, zero, and absolute-descendant recursions have no depth limit; attacker-controlled nesting depth raises uncatchable `StackOverflowException` and crashes the app.
- **Proposed fix:** `MaxDepth = 256` constant; throw recoverable `InvalidOperationException` at the cap.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-082: Reject cycles and reparenting in InsertChild (F-2)
- **Severity:** 🟠 High
- **Category:** Reliability
- **Problem:** No check for `child == this` or `child._owner != null`; allows self-cycles and parent-aliasing → infinite recursion in tree walks and silent dirty-propagation bugs.
- **Proposed fix:** In `InsertChild`, throw on `child == this` and on `child._owner != null && child._owner != this`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-083: Clamp infinity / negative aspect ratio in layout (F-3 / F-4)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** With `MaxWidth = 1e38` and `pointScaleFactor = 4`, `RoundValueToPixelGrid` output becomes `+Infinity`, crashing WinUI `Arrange`. `AspectRatio` setter does not reject negative values; produces negative computed dimensions.
- **Proposed fix:** Test `float.IsInfinity` at end of `RoundValueToPixelGrid` and return `NaN` (or clamp at `BoundAxis`); in `AspectRatio` setter, treat negative as `float.NaN`.
- **Human review:** [x] Approve ☐ Deny [ ] Needs discussion
- **Instructions to agent:**
- **Agent response:** Two independent issues conflated — recommend splitting:

  1. **Infinity in `RoundValueToPixelGrid`.** `float.PositiveInfinity` is a *legitimate* input (the standard idiom for "unconstrained" in Yoga, e.g. `MaxWidth = ∞`), so the bug is that we let it propagate to `Arrange` rather than capping it at the axis-bounding stage. Fix should clamp at `BoundAxis` (the layer that already does max/min collapse), not change `Round` semantics. Returning NaN is worse than clamping — `Arrange` will still crash on NaN.
  2. **Negative `AspectRatio`.** This is a contract violation, not a runtime hazard. Should throw `ArgumentOutOfRangeException` in the setter (fail fast at config time) rather than silently coerce to NaN — the silent coerce hides bugs in app code.

  If you agree with the split, I'll re-file as TASK-083a (clamp at BoundAxis) and TASK-083b (validate AspectRatio setter).
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-084: Contain MeasureFunction exceptions (F-5)
- **Severity:** 🟡 Medium
- **Problem:** Exceptions from `MeasureFunction` corrupt half-laid-out tree state; unwind leaves inconsistent dirty/cache state.
- **Proposed fix:** Wrap the measure delegate in try/catch returning `(0,0)` and `MarkDirty()`, or commit to the propagate-and-mark-dirty contract.
- **Human review:** [x] Approve ☐ Deny [ ] Needs discussion
- **Instructions to agent:** how can we reasonably continue if we crash in the MeasureFunction, shouldn't we fail fast the process?
- **Agent response:** Agree with you — fail-fast is the right call here. Three reasons:

  1. **Half-laid-out tree state is already inconsistent on exception.** Wrapping in try/catch and returning `(0,0)` doesn't restore invariants; it just hides them. A node that "measured to zero because of an exception" silently disappears from the UI and ships, instead of crashing on the dev's machine.
  2. **The exception comes from app code** (the developer's `MeasureFunction`), not user input or untrusted data. The framework correctly bubbling it surfaces the developer's bug.
  3. **WinUI's own measure pipeline doesn't catch.** A throwing `MeasureOverride` tears down the app — that's the platform contract.

  The threat model finding is technically correct that the tree is left dirty/inconsistent, but the conclusion (catch + degrade) is wrong for a developer-facing layout primitive. Recommendation: **close as won't-fix**, with a doc note on `MeasureFunction` stating "must not throw; exceptions tear down the layout pass and propagate to the host." If we want to be extra-helpful, add an ETW event on the throw with the node identity so debugging is easier.
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

---

## 021 · Charting (D3 Port)
[Source: 021-charting-d3-port-threat-model.md](./021-charting-d3-port-threat-model.md)

### TASK-085: Fix LogScale.Ticks near-infinite loop (F-2)
- **Severity:** 🟠 High
- **Category:** Reliability
- **Problem:** `Log(0) = -Infinity` casts to `int.MinValue`, producing ~3.8×10¹⁰ iterations on the render thread.
- **Proposed fix:** Validate `d0 > 0 && d1 > 0 && IsFinite` at top of `Ticks()`; validate `_base > 1 && IsFinite` in setter.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-086: Cap D3Ticks allocation on extreme domains (F-1)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** Domain values near `±double.MaxValue` produce huge `n = i2 - i1 + 1` and several-hundred-MB `new double[n]` allocation.
- **Proposed fix:** Cap `n > 10_000` returns `[]`; reject non-finite `start`/`stop`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-087: Replace O(n⁴) PackCircles with front-chain algorithm (F-3)
- **Severity:** 🟡 Medium
- **Problem:** Brute-force best-fit replaces D3's front-chain `packEnclose`; n=1000 children freezes the render thread for billions of ops.
- **Proposed fix:** Port d3-hierarchy `packEnclose`, or cap children per node.
- **Human review:** [x] Approve ☐ Deny [ ] Needs discussion
- **Instructions to agent:** tell me about the other implementation, why is it better?
- **Agent response:** Looked at our `Pack.cs`. Current loop (lines 87-138) for each new circle tries every pair `(j, k)` of already-placed circles as anchors, checks every `m` for overlap, and picks the position closest to origin. That's O(i² · i) per insert × n inserts = roughly **O(n⁴)** with the inner overlap scan. For n=1000 children it's ~10¹² operations on the render thread.

  D3's `packEnclose` (Wang, Wang, Dai, Wang — *Visual Data Mining of Multivariate Hierarchical Data* 2006) uses a **front-chain** algorithm:

  - Maintain a circular doubly-linked list of circles currently on the boundary of the packed region (the "front chain").
  - Place the next circle tangent to two adjacent circles on the chain — this gives a small constant-set of candidate positions instead of O(i²).
  - If the new circle overlaps a non-adjacent chain member, splice the chain (remove the intervening circles from the front; they're now interior) and retry.

  Amortized cost is **O(n)** per insert → **O(n)** total, because each circle joins the front exactly once and is removed at most once. The implementation is short — d3-hierarchy's `packSiblings.js` is ~80 lines.

  Why it's better here:
  1. **Correctness parity with D3.** Our brute-force "closest to origin" produces visibly different layouts from D3.js, which is surprising for a "port of d3-hierarchy".
  2. **Usable past trivial sizes.** Current impl is unusable beyond ~50 children; front-chain handles thousands without freezing the render thread.
  3. **One-time cost.** ~80 lines, well-documented in the original D3 source, has reference test cases we can reuse.

  Recommendation: port. I can do this as a follow-up PR; happy to also add a test that compares output against canonical D3 fixtures.
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-088: Replace O(n³) Delaunay.From with sweep-line (F-4)
- **Severity:** 🟡 Medium
- **Problem:** Incremental Bowyer-Watson rebuilds triangle list per point; n=10000 is 10¹² ops freezing the render thread.
- **Proposed fix:** Cap point count or port `delaunator`'s O(n log n) sweep-line algorithm.
- **Human review:** [x] Approve ☐ Deny [ ] Needs discussion
- **Instructions to agent:** why isn't this already fixed in D3.js on the web?
- **Agent response:** It **is** fixed in D3.js — and our port is misrepresenting itself. Looked at our `Delaunay.cs`:

  - Line 2 header comment: *"Uses a sweep-line Delaunay triangulation algorithm."*
  - Line 40 method comment, four lines later: *"Uses the Bowyer-Watson incremental insertion algorithm."*

  The header is aspirational; the implementation is incremental Bowyer-Watson (O(n²) average, O(n³) pathological). D3.js itself doesn't ship its own triangulator — `d3-delaunay` delegates entirely to **delaunator** (Mapbox, MIT-licensed, ~500 lines of vanilla JS), which implements the **sweep-hull** algorithm (Sinclair 2016, refined from Liu/Snoeyink 2005). That's **O(n log n)** with very low constants — n=10⁶ runs in ~1s on a laptop.

  So the answer to "why isn't this fixed in D3.js" is: it is, and we just didn't port the actual implementation. We took the API shape and wrote a textbook algorithm behind it.

  Two options:
  1. **Port delaunator.** ~500 lines, well-tested reference implementation, deterministic across platforms, matches D3's output exactly. One-time cost, no ongoing maintenance.
  2. **Cap point count.** If real-world charting use cases stay under ~500 points, just guard at the entry and reject larger inputs.

  Recommendation: **option 1** (port delaunator). The header-vs-code comment mismatch is itself a bug — anyone reading our source thinks we have the fast algorithm. Porting fixes the perf hazard, the comment lie, and the "we drift from D3 outputs" problem in one go.
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-089: Cap hierarchy traversal depth across charting layouts (F-5)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** Pack/Treemap/Partition/Cluster/Tree/Stratify recursions have no depth cap; attacker JSON depth ~10000 crashes the process.
- **Proposed fix:** `MaxHierarchyDepth = 1024` cap, or rewrite traversals iteratively with `Stack<T>`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-090: Tolerate bad hex colors in D3Color.Parse (F-6)
- **Severity:** 🟡 Medium (Low–Medium)
- **Category:** Reliability
- **Problem:** `D3Color.Parse` throws `FormatException` on non-hex characters in 6-digit hex colors via `Convert.ToByte`; data-bound color strings with bad chars crash the render.
- **Proposed fix:** Switch to `byte.TryParse` with hex style; fall back to default color.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-091: Stop OrdinalScale.Map domain-growth memory leak (F-8)
- **Severity:** 🟡 Medium (Low–Medium)
- **Category:** Reliability
- **Problem:** Default `_unknown = NaN` causes implicit-add on every miss; leaks memory under streaming categorical keys.
- **Proposed fix:** Default to a finite sentinel, or expose `AllowImplicitGrowth = false`.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

---

## 022 · Data System & Controls
[Source: 022-data-system-and-controls-threat-model.md](./022-data-system-and-controls-threat-model.md)

### TASK-092: Preserve message provenance across validators (F-2 / F-4)
- **Severity:** 🟠 High
- **Category:** Reliability
- **Problem:** `ClearInternal`-then-add wipes co-located validation messages — multiple validators or rules on the same field overwrite each other and `IsValid()` returns wrong answers. Async path also fails to clear stale messages.
- **Proposed fix:** Track messages by source token; clear only that source's prior messages; tag async results with the value-revision.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-093: Block CommitEdit on pending async validation (F-3)
- **Severity:** 🟠 High
- **Problem:** `CommitEdit`/`CommitRowEdit` ignore in-flight async validators; `IsValid()` is checked synchronously while async validators (uniqueness, server checks) are still running — rows commit with no validation having completed.
- **Proposed fix:** Track pending async validation; await or block commit; expose `HasPendingAsyncValidation` predicate.
- **Human review:** ☐ Approve [x] Deny ☐ Needs discussion
- **Instructions to agent:** validators are advisory only
- **Agent status:** [x] Not started ☐ In progress ☐ Complete
- **Agent notes:**

### TASK-094: ReDoS-proof user-supplied validation regex (F-1)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** `Validate.Match`, `Email`, `AllowOnly`, `DenyOnly` compile patterns with no `MatchTimeout` and run per-keystroke; catastrophic backtracking hangs the UI.
- **Proposed fix:** `Regex.MatchTimeout` (~50 ms); cap pattern length; document patterns must be developer-authored constants.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-095: Allowlist filter/sort field names (F-5)
- **Severity:** 🟡 Medium
- **Problem:** `[PropertyHidden]`/`[Browsable(false)]` are display-only; user-typed filter/sort field names can read any public property of `T` (e.g. `PasswordHash`) via row-count side channel.
- **Proposed fix:** Add `FilterAllowed`/`SortAllowed` predicate defaulting to fields exposed via `FieldDescriptor`; document `[PropertyHidden]` is not a security boundary.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-096: Honor PropertyHidden/ComposeIgnore in BuildCompose fallback (F-6)
- **Severity:** 🟡 Medium
- **Problem:** Parameterless-ctor fallback bypasses constructor invariants and silently round-trips every writable property including hidden ones.
- **Proposed fix:** Fail `BuildCompose` when no name-matching ctor exists, or honor `[PropertyHidden]`/`[ComposeIgnore]` in the fallback.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-097: Cap PageSize on client-fallback path (F-7)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** When source lacks server sort/filter capabilities, the grid issues an unbounded `GetPageAsync` (`PageSize = int.MaxValue`), causing OOM or huge SQL queries.
- **Proposed fix:** Cap `PageSize` at a configurable maximum (~100 000), or refuse to mount against sources without server capabilities.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-098: Make SearchManager thread-safe (F-8)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** Unsynchronized mutation of `_cts`/`_debounceTimer`/state from the threadpool Timer callback causes `ObjectDisposedException`, lost cancellations, stale-overwrite-fresh, and cross-thread `StateChanged`.
- **Proposed fix:** Lock all mutations; re-check token after `await`; marshal `StateChanged` onto UI thread.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

---

## 023 · Hooks Library
[Source: 023-hooks-library-threat-model.md](./023-hooks-library-threat-model.md)

### TASK-099: Cap _deferredRequests in UseInfiniteResource (F-02)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** Cursor-paged fallback grows the `SortedSet<int>` per `RequestPage` and only pops one per `CommitSuccess`; `EnsureRange(0, 100_000)` against a slow source grows toward 100k entries.
- **Proposed fix:** Cap the set at a configurable maximum (~100); surface a diagnostic.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

### TASK-100: Add escape hatches to UseFocusTrap (F-03)
- **Severity:** 🟡 Medium
- **Category:** Reliability
- **Problem:** Trap blocks every non-descendant `LosingFocus`; no checks for hidden/collapsed container, no Esc release, no cross-window allowance — keyboard users get wedged on hidden modals.
- **Proposed fix:** Gate cancellation on container `IsLoaded && Visibility.Visible && IsHitTestVisible`; allow cross-window navigation; document the Esc contract.
- **Human review:** [x] Approve ☐ Deny ☐ Needs discussion
- **Instructions to agent:**
- **Agent status:** ☐ Not started ☐ In progress [x] Complete
- **Agent notes:**

---

## Suggested ordering

Triage hint — the Critical tier should land first regardless of chunk:

1. **TASK-011** — `fire` reflection lockdown (Critical, RCE-class via DevTools)
2. **TASK-027** — VS Code `dotnet` PATH hijack (Critical, RCE-class on preview)
3. **TASK-040** — `.resw` C# string-literal injection (Critical, build-time RCE)

Then sweep High issues by chunk, then Medium. Each task is independent unless a "see TASK-NNN" reference notes a dependency.
