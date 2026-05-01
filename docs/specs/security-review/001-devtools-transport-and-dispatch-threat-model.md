# Chunk 01 — Devtools Transport & Dispatch: Threat Model

**Status:** Phase 2 — security review
**Reviewer scope:** the loopback HTTP listener, the stdio JSON-RPC loop, the
dispatch path, the lockfile registry, and the window-id allocator. The tool
*handlers* invoked through the dispatcher are out of scope (Chunk 02).

---

## 1. Scope

| File | LOC | Role |
|---|---:|---|
| `src/Reactor/Hosting/Devtools/DevtoolsMcpServer.cs` | 423 | HTTP listener, stdio bring-up, dispatcher hop, banner / lockfile emission, `GET /mcp` schema doc. |
| `src/Reactor/Hosting/Devtools/StdioMcpLoop.cs` | 107 | Newline-delimited JSON-RPC over stdin/stdout. |
| `src/Reactor/Hosting/Devtools/McpDispatcher.cs` | 236 | Pure JSON-RPC dispatch: parse, route MCP `initialize` / `tools/list` / `tools/call`, log calls. |
| `src/Reactor/Hosting/Devtools/JsonRpc.cs` | 50 | JSON-RPC envelope DTOs and error codes. |
| `src/Reactor/Hosting/Devtools/DevtoolsJsonContext.cs` | 18 | Source-gen JSON metadata for the envelopes. |
| `src/Reactor/Hosting/Devtools/LockfileRegistry.cs` | 216 | Server-side lockfile read/write/probe under `%TEMP%/reactor-devtools/`. |
| `src/Reactor/Hosting/Devtools/WindowIdAllocator.cs` | 93 | Pure title-to-slug id allocation. |
| `src/Reactor/Hosting/Devtools/WindowRegistry.cs` | 160 | WeakReference-keyed window table; HWND/bounds snapshot. |
| **Total** | **1303** | |

Reviewed at **`4623474cac6e5f2b64df2501636fd5f8491a1bc3`** (`main`).

Adjacent files referenced for control flow but reviewed only at the contract level:
`McpToolRegistry.cs` (delegate type and error class only), `ReactorApp.cs`
(`DeriveProjectIdentifier`, host bring-up; line 408–423, 532–542),
`Reactor.Cli/Devtools/LockfileReader.cs` and `EndpointDiscovery.cs` (for the
read side of the lockfile contract — full review belongs to Chunk 05).

---

## 2. Data-flow diagram

```
                    +---------------------------------+
  HTTP (loopback)   |  HttpListener prefix            |
  any local proc -->|  http://127.0.0.1:<ephem>/      |
  any browser tab   |   GET  /mcp  -> schema doc      |
                    |   POST /mcp  -> JSON-RPC body   |---+
                    +---------------------------------+   |
                                                          |
                    +---------------------------------+   |
  stdio (parent     |  StdioMcpLoop (one line in,     |   |
  process pipe) --->|   one JSON-RPC line out)        |---+
                    +---------------------------------+   |
                                                          v
                                            +---------------------------+
                                            |   McpDispatcher.Dispatch  |
                                            |   parse JSON              |
                                            |   route by `method`       |
                                            +-----+---------------------+
                                                  |
                            +---------------------+----------------------+
                            v                     v                      v
                  initialize / ping       tools/list (incl. selector  tools/call -> handler
                                          grammar + tree schema)         |
                                                                         v
                                                       (Chunk 02 — tool handlers
                                                        on UI dispatcher via
                                                        DevtoolsMcpServer.OnDispatcher)

  Persistence:                                +-------------------------------+
  AnnounceReady()      writes ----------->    | %TEMP%/reactor-devtools/       |
                                              |   <hash16>.json                |
                                              | { schema, endpoint, transport, |
                                              |   port, pid, buildTag,         |
                                              |   project, startedAt }         |
                                              +-------------------------------+
                                              ^
  Single-instance     reads + liveness-probes |
  check, CLI          (PID alive + GET /mcp   |
  endpoint discovery   schema-tag check)      |
```

Persisted state on disk:

* Lockfile path = `%TEMP%/reactor-devtools/<sha256-of-canonicalized-csproj-path[:8]>.json`
  (`LockfileRegistry.cs:65-78`).
* Lifetime = AnnounceReady -> server `Dispose` (best-effort delete; readers GC
  stale entries opportunistically).
* Atomicity = write-temp + `File.Move(..., overwrite: true)` (`LockfileRegistry.cs:114-124`).

---

## 3. Trust boundaries crossed

| # | Boundary | Direction | Assumption | Holds? |
|---|---|---|---|---|
| B1 | Loopback TCP socket | inbound | "Anything that can connect to `127.0.0.1` is the desktop user / their dev tooling." | **No** — see F1, F2, F3. Any local process running as the same user, and any browser tab the user has open, can reach the port. |
| B2 | stdin pipe of the host process | inbound | "The parent process speaking JSON-RPC is the trusted supervisor." | Holds for spawn-by-parent. Stdin is private to the process tree on Windows; unlike loopback there is no third-party reach. |
| B3 | `%TEMP%/reactor-devtools/*.json` | both | "Only this user / this tool writes lockfiles. Files are advisory; readers verify liveness before connecting." | **Partly** — `Path.GetTempPath()` is per-user on Windows so cross-user write is normally not possible, but **any other process running as the same user can plant or rewrite a lockfile** (F4). The CLI does not authenticate the lockfile contents (F5). |
| B4 | UI dispatcher | dispatcher hop | Tool handlers run on the UI thread with full app authority. | Out of scope here (Chunk 02 reviews handler authority); but the *unauthenticated path that reaches handlers* is in scope and is the central concern of B1. |
| B5 | Process exit-code | outbound | Sentinel exit 42 reaches the supervisor, which respawns. | Out of scope (Chunk 05). |

---

## 4. Asset inventory

What the chunk holds, and what an attacker could want from it.

* **A1 — Authority to invoke any registered MCP tool.** Reaching the
  dispatcher is equivalent to reaching every handler in Chunk 02 — synthetic
  input ("fire"), screenshot capture, log buffer reads, state inspection,
  property reads. The transport's authentication posture *is* the
  authentication posture of every tool.
* **A2 — Process metadata in banners and `GET /mcp`.** PID, port,
  build tag, transport, full tool inventory with input schemas,
  selector grammar, tree-schema version (`DevtoolsMcpServer.cs:142-150,
  367-391`).
* **A3 — Lockfile contents.** Endpoint URL, port, PID, build tag, **absolute
  project path** (`LockfileRegistry.cs:17-27` and the call site at
  `DevtoolsMcpServer.cs:167-177`). The project path is a deliberately
  identifying string (canonical full path of the .csproj or assembly
  location, `ReactorApp.cs:532-542`).
* **A4 — Server availability.** A wedged HTTP worker or memory exhaustion
  denies the developer their own tooling.
* **A5 — Window inventory.** HWND, bounds, title (`WindowRegistry.cs:73-92`).
  HWNDs are not secret, but exposing the bounds + title across a transport
  with no auth means another local process learns where the dev's app is on
  screen. The attack value is low compared to A1.

---

## 5. STRIDE table

| Category | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding / recommendation |
|---|---|---|---|---|---|---|
| **S — Spoofing** | Hostile local process connects to the loopback MCP port and impersonates the trusted CLI / VS Code extension. | Any process running as the same desktop user (malicious npm package, browser extension that can `fetch`, sandbox-escaped PWA, etc.). | Full A1 — synthetic input, screenshots, state reads. | High once the port is known; the port is leaked in the banner, the lockfile, and `GET /mcp` itself. | None. There is **no token, no auth header, no client-cert, no mutual handshake** — only the bind to `127.0.0.1`. | **F1 (Critical).** Add an unguessable per-session token; require it in an `Authorization` header (or `?token=` for `GET /mcp` if the schema doc must be reachable without). Persist the token in the lockfile next to the endpoint. |
| **S — Spoofing** | CSRF from a browser tab the user has open: hostile webpage POSTs JSON-RPC at `http://127.0.0.1:<port>/mcp` as a "simple" request. | Any HTTP origin the user visits. | Same as F1 — every tool call is reachable. | Medium — the attacker has to discover the port. The port range is ~16k entries and JS can sweep it. | The CORS preflight on `application/json` would block the *response*, but a `Content-Type: text/plain` body is a CORS-simple POST; the server does not check `Content-Type` (`McpDispatcher.cs:31`) or `Origin` / `Host`, so the side-effect runs even when the response is unreadable. | **F2 (High).** Reject requests whose `Content-Type` is not `application/json`; reject `Origin`/`Referer` not in an allowlist (e.g. local extension origins or *only* the absent-Origin case where there is no browser); send `Access-Control-Allow-Private-Network: false`. Combined with F1's token, defense in depth. |
| **S — Spoofing** | DNS rebinding: hostile site convinces browser that `attacker.com` resolves to `127.0.0.1`, then issues same-origin requests. | Any HTTP origin. | Same as F1. | Lower than F2 because typical browsers now block DNS rebind to RFC 1918 + loopback (Chrome's "Private Network Access"), but the protections are heuristic. | None. The server does not validate the `Host` header. A request with `Host: attacker.com` (post-rebind) is accepted. | **F3 (Medium).** Validate `Host` header is `127.0.0.1:<port>` or `localhost:<port>`. Cheap and orthogonal to F1. |
| **T — Tampering** | Hostile local process plants or rewrites a lockfile in `%TEMP%/reactor-devtools/` to redirect the CLI to an attacker-controlled HTTP endpoint. | Process running as the same user. | CLI sends source paths, possibly secrets in tool args, to the attacker; CLI receives attacker JSON that can carry tool-result payloads back into the developer's terminal. | Medium — requires the user to invoke `mur devtools` while the malicious lockfile is present and to lose the disambiguation race against the genuine lockfile. | The CLI's `IsLive()` probe (`LockfileReader.cs:67-104`) verifies PID is alive and that `GET <endpoint>` returns the schema tag. **The schema tag is a public constant (`reactor-devtools-mcp/1`) that any attacker can echo from their own listener bound to `127.0.0.1:<their port>`**. PID can be any live PID on the box; nothing ties the PID to the listener. | **F4 (High).** The lockfile authenticates *nothing*. Two fixes: (a) put the per-session token (F1) in the lockfile and have the CLI present it; mismatch = wrong session. (b) Have the CLI verify the listening socket's owning process matches `entry.Pid` (Windows: `GetExtendedTcpTable` over IPv4 loopback connections, match owning PID). |
| **T — Tampering** | Lockfile write race: a reader observes the file mid-write or a stale tmp file. | Concurrent CLI reader during server bring-up / reload. | Reader sees parse error, treats lockfile as stale, GCs it (`EndpointDiscovery.cs:73-78`) — could delete the live entry under the writer's feet. | Low — the writer recreates after reload; user sees a transient "no live session" if timing is unlucky. | Atomic-rename pattern at `LockfileRegistry.cs:114-124`. `File.Move(..., overwrite: true)` on the same NTFS volume is atomic at the rename boundary, but the implementation does **not** `fsync` the tmp file before the rename, so a crash between `WriteAllText` and `Move` can leave a `*.tmp` orphan. | **F6 (Low).** Acceptable today. Sweep `*.tmp` orphans during enumeration to avoid `%TEMP%` clutter. |
| **D — DoS** | Unbounded request body: attacker POSTs `Content-Length: 0xFFFFFFFF` of slow-trickled bytes; `StreamReader.ReadToEnd()` (`DevtoolsMcpServer.cs:267-268`) buffers everything in memory. | Any local process / browser. | Server OOM, app process dies. | High once an attacker has reach (which the previous spoof findings already grant). | None. No `MaxRequestSize`, no read timeout, no streaming parser. | **F7 (Medium).** Cap body size (e.g. 1 MiB — generous for JSON-RPC tool calls) and enforce a 10 s read deadline. `HttpListenerTimeoutManager` is the cheap knob; for the body cap, check `ContentLength64` up front and reject early; if absent, read into a length-limited buffer. |
| **D — DoS** | Unbounded concurrent connections: every accepted context spawns `Task.Run` (`DevtoolsMcpServer.cs:214`) with no semaphore. | Any local process. | Thread-pool exhaustion, dispatcher backlog (each handler hops to UI thread with a 5 s timeout, `OnDispatcher` `DevtoolsMcpServer.cs:302-331`), the UI thread itself becomes the bottleneck. | High under attack; medium incidentally (CI scripts hammering the endpoint). | Per-call dispatcher timeout caps each handler. No global concurrency cap. | **F8 (Medium).** Bound concurrent in-flight requests with a `SemaphoreSlim` (e.g. 16). Reject excess with 503. |
| **D — DoS** | Slow-loris on `HttpListener`: open many sockets, never send a body. | Any local process. | Listener queue saturated, legitimate CLI calls block. | Medium. | None. `HttpListener` has internal queue limits but they are large; the application itself sets no read or idle timeout. | Folded into **F7**. Use `HttpListenerTimeoutManager.IdleConnection` / `EntityBody`. |
| **D — DoS** | JSON parse cost on a deeply nested or huge payload. `JsonSerializer.Deserialize<JsonRpcRequest>` is then followed by tool handlers that may walk `Params` as `JsonElement`. | Any caller. | CPU spike, GC pressure. | Lower than F7 (the body cap mitigates most of it). | `System.Text.Json` enforces a default `MaxDepth=64` — that is a meaningful natural cap. | **F9 (Low/Info).** With F7 in place this is bounded. Worth a regression test on a 1 MiB payload of `[[[[…]]]]`. |
| **D — DoS** | JSON-RPC batch amplification. | Any caller. | n/a — **batches are not implemented** (`McpDispatcher.cs:31` deserializes a single object and rejects arrays at the type level). | n/a | The dispatcher's `JsonRpcRequest` is a single object, not an array. A batch payload becomes `JsonException` -> ParseError. | **F10 (Info).** No-op for now. If batch is added later, cap entries per batch. |
| **I — Info disclosure** | `tools/list` and `GET /mcp` reveal the tool inventory, selector grammar, build tag, port, **and PID** (`DevtoolsMcpServer.cs:142-150`, ready-event JSON). | Any caller (after spoof). | Reconnaissance; pid is the lockfile-spoof primitive (F4). | High once reachable. | None — the surfaces are explicitly self-describing by design. | **F11 (Info).** Acceptable in current trust model; if F1's token gates POST /mcp and `GET /mcp` is moved behind the same token, the inventory is no longer drive-by readable. |
| **I — Info disclosure** | Lockfile leaks absolute project path, PID, port, build tag to anything that can read `%TEMP%`. | Process running as same user. | Reconnaissance; reveals what the developer is working on. | Medium — `%TEMP%` is per-user on Windows, but per-user is *not* the same as per-process. | None. JSON is plaintext, no ACL hardening on the directory. | **F12 (Low).** Document the assumption. Optionally `File.SetUnixFileMode` no-op on Windows; consider creating the directory with explicit ACL `SE_DACL_PROTECTED` granting only the current user. |
| **R — Repudiation** | A tool call is invoked but no record is kept. | Local user troubleshooting; not really an attacker concern. | Operator can't tell what the agent ran. | n/a | `DevtoolsLogger.LogCall` records every dispatch with name, selector, latency, success (`McpDispatcher.cs:190-209`); also emits `ReactorEventSource` ETW events. Logging is at the dispatcher, before the wire writes the response. | **F13 (Info).** Adequate. ETW + per-call log is the right shape. |
| **E — Elevation** | Reaching the dispatcher equals reaching every Chunk 02 handler, several of which act on the UI thread (synthetic input, hot reload). | Folded into S/T above. | See A1. | High. | None at the transport. | All transport-side mitigations (F1–F4) are EoP fixes — without auth, every RPC is an EoP from "any local process" to "in-process control of the dev's WinUI app". |

---

## 6. Findings (concrete, file:line)

### F1 — Critical: loopback MCP endpoint has no authentication

`DevtoolsMcpServer.cs:67` binds an `HttpListener` on `http://127.0.0.1:<port>/`
and `HandleRequest` (`DevtoolsMcpServer.cs:220-286`) accepts every POST
without checking any header, token, or origin. `McpDispatcher.Dispatch`
(`McpDispatcher.cs:26-102`) then routes to a registered tool handler. The
trust model document explicitly flags loopback-trust as an assumption to
test; the test fails.

Any process on the host running as the same desktop user — including a
malicious npm/pip/cargo install, a compromised Electron app, or a sandbox
escape from a browser tab — can:

* enumerate ports 1024–65535 looking for `GET /mcp` returning
  `"schema": "reactor-devtools-mcp/1"` (the schema constant is public,
  `LockfileRegistry.cs:49`),
* `tools/list` to learn the inventory,
* `tools/call` any tool, including ones that synthesize input, capture
  screenshots, and read in-memory log buffers (Chunk 02).

**Recommendation:** generate a 256-bit token at server startup, write it
into the lockfile, require it in an `Authorization: Bearer <token>` header
on every `POST /mcp`. Reject without the header. The CLI already reads the
lockfile (`LockfileReader.cs:46-60`) and the wire client
(`McpCliClient.cs:14-23`) is the single chokepoint that needs to add the
header.

### F2 — High: CSRF from a browser tab against `POST /mcp`

`DevtoolsMcpServer.cs:259-285` accepts `POST /mcp` regardless of
`Content-Type` and regardless of `Origin` / `Referer`. The dispatcher only
requires the body be parseable JSON (`McpDispatcher.cs:31`).

A browser fetch with `mode: 'no-cors'` and `Content-Type: text/plain`
sending a JSON-RPC body is a CORS-simple request: the browser executes the
request and the server processes it (no preflight). The browser only blocks
the response. By that time `tools/call` already invoked the handler.

The CORS response that *is* set (`DevtoolsMcpServer.cs:227-229`)
hard-codes `Access-Control-Allow-Origin: http://127.0.0.1` — note: no port,
not the request's actual origin — which means it neither matches the
attacker's origin (good) nor any legitimate developer origin with a port
(also "good" by accident, but not by design). It does not gate the simple
POST path that doesn't trigger preflight in the first place.

**Recommendation:** at request time:

1. Reject `POST /mcp` whose `Content-Type` is not `application/json` (or
   `application/json-rpc`). This forces preflight and prevents the CSRF
   route entirely.
2. Reject requests carrying any `Origin` header except an allowlist (the
   CLI sends none; the VS Code extension uses `vscode-webview://` /
   `vscode-file://`).
3. Combined with F1's token, defense in depth.

### F3 — Medium: no `Host`-header validation enables DNS rebinding

`DevtoolsMcpServer.cs:220-286` does not consult `ctx.Request.UserHostName`
or the raw `Host` header. A request with `Host: attacker.example` post-DNS
rebind is accepted. Modern browsers' Private Network Access checks make
this harder than it used to be, but it is still a heuristic, not a
guarantee.

**Recommendation:** at the top of `HandleRequest`, after the OPTIONS short
circuit, require `ctx.Request.Headers["Host"]` to be `127.0.0.1:<Port>` or
`localhost:<Port>`; respond 421 Misdirected Request otherwise. Three lines.

### F4 — High: lockfile is unauthenticated; no proof endpoint owner == claimed PID

`LockfileRegistry.IsLive` (`LockfileRegistry.cs:177-215`) tests two
properties: (a) `Process.GetProcessById(pid).HasExited == false`, and (b)
`GET endpoint` returns a body whose top-level `schema` is the public
constant `McpSchemaTag = "reactor-devtools-mcp/1"`
(`LockfileRegistry.cs:49`).

Neither property links the PID to the listening socket:

* `PidAlive` only checks the PID names *some* live process — any unrelated
  process the attacker chooses works (e.g. `explorer.exe`).
* `HttpProbe` checks the body's `schema` field — an attacker who spawns
  `python -m http.server` returning `{"schema":"reactor-devtools-mcp/1"}`
  passes the probe trivially; the schema tag is a public constant.

The CLI side (`Reactor.Cli/Devtools/LockfileReader.cs:67-104`) implements
the same logic and inherits the same gap.

A hostile process running as the same user can:

1. Pick an ephemeral port, bind a fake "server" returning the right JSON,
2. Write `%TEMP%/reactor-devtools/<deterministic-hash>.json` pointing at
   that port with any live PID,
3. Wait for the developer to run `mur devtools`/extension to discover and
   connect.

The CLI would happily disambiguate to the attacker's session and forward
tool-call payloads (which can include selectors, fire-input arguments,
file paths) to the attacker.

**Recommendation:** combine with F1. The lockfile carries a freshly-minted
token; the CLI sends it; the *real* server validates it; the attacker's
fake server cannot generate the same token. Independently, on Windows the
listener-PID can be cross-checked via `GetExtendedTcpTable
(TCP_TABLE_OWNER_PID_LISTENER, AF_INET)` to confirm `entry.Pid` actually
owns the loopback listener at `entry.Port`.

### F5 — Medium: lockfile schema is unbounded — no size cap, no field validation, JSON-only check

`LockfileRegistry.TryRead` (`LockfileRegistry.cs:136-150`) calls
`File.ReadAllText(path)` then `JsonSerializer.Deserialize<LockfileEntry>`.
The CLI version (`Reactor.Cli/Devtools/LockfileReader.cs:46-60`) does the
same. Neither caps file size, validates `Endpoint` is an HTTP loopback
URL, validates `Port` is in range, or validates `Project` is a string the
caller is willing to display. A 1 GiB lockfile causes
`File.ReadAllText` to OOM the reader; a `Project` value of
`"[2J[H"` injects ANSI escapes into the CLI's `session list`
output (mild terminal-injection).

`HttpProbe` (`LockfileRegistry.cs:198-215`) passes `entry.Endpoint`
directly to `HttpClient.GetAsync`; if a hostile lockfile sets `Endpoint`
to a non-loopback URL, the server-side single-instance check
**fetches an arbitrary URL** (low-impact SSRF, no body returned to caller,
500 ms timeout caps the damage, but still unintended egress for a
"loopback-trust" subsystem). Same on the CLI side. The CLI uses the
endpoint for the `tools/call` POST too — the developer's tool arguments
go to whatever URL the lockfile names.

**Recommendation:**

* Cap lockfile size before read (e.g. 8 KiB; legitimate file is < 1 KiB).
* Validate `Endpoint` is `http://127.0.0.1:<port>/mcp` or `http://localhost:<port>/mcp`
  via `Uri.TryCreate` + host check before the probe, before the CLI
  connects, and before disambiguation displays.
* Sanitize / strip control characters from any lockfile string echoed to
  stdout (`Project`, `BuildTag`, `Endpoint`).

### F7 — Medium: unbounded request body (DoS)

`DevtoolsMcpServer.cs:266-268`:

```csharp
string body;
using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
    body = reader.ReadToEnd();
```

No size cap; no read deadline. A POST advertising
`Content-Length: 2147483647` and trickling bytes ties up the worker thread
and the buffer indefinitely. `HttpListener` has timeouts — they are *not*
configured here, so defaults apply (very lenient).

Note that `ctx.Request.ContentEncoding` is derived from the request's
`Content-Type` charset parameter — caller-controlled. `StreamReader`
honors this and can decode "gzip" bytes as UTF-32, etc. Encoding choice is
not a security bug here (the bytes go into a parser), but it is another
avenue for parsing-cost amplification.

**Recommendation:** read into a `MemoryStream` with a hard cap (e.g.
1 MiB); 413 on overflow. Set `_listener.TimeoutManager.EntityBody = TimeSpan.FromSeconds(10)`,
`IdleConnection = TimeSpan.FromSeconds(30)`. Pin the decoder to UTF-8.

### F8 — Medium: unbounded concurrent dispatch

`DevtoolsMcpServer.cs:202-216`:

```csharp
while (!_disposed && _listener.IsListening) {
    var ctx = await _listener.GetContextAsync();
    _ = Task.Run(() => HandleRequest(ctx));
}
```

No semaphore. Each accepted request becomes a thread-pool work item that
ultimately enqueues onto the UI dispatcher (`OnDispatcher`,
`DevtoolsMcpServer.cs:302-331`). The per-call 5-second dispatcher timeout
caps individual stalls but does not cap concurrent in-flight load.

**Recommendation:** wrap the body of `HandleRequest` in a
`SemaphoreSlim(maxParallel: 16)` gate; reject excess with 503. Combined
with F7's body cap, a single attacker cannot starve the listener.

### F11 — Info: `tools/list` exposes complete tool inventory + PID before any auth

`DevtoolsMcpServer.cs:142-150` (ready event includes `pid`),
`DevtoolsMcpServer.cs:367-391` (`GET /mcp` schema doc, includes the full
tool inventory and selector grammar). Today this is intentional — agents
are expected to introspect — but post-F1 the `GET /mcp` route should
require the same token as POST, otherwise the inventory is still
drive-by-readable for any process that can reach the port.

### F12 — Low: lockfile content includes absolute project path

`DevtoolsMcpServer.cs:174-176`, persisted to
`%TEMP%/reactor-devtools/<hash>.json`. Per-user temp on Windows, so cross-user
leakage is bounded by ACL on `%TEMP%` — but any code running as the same
user (browser sandbox escape, rogue dependency) reads it. Document this as
an explicit trust assumption; or, if hardening is desirable, write the
file with a discretionary ACL granting only the current user (`ProtectionType.SelfOnly`
equivalent — Windows does not have POSIX modes).

---

## 7. Open questions for the team

1. **Is loopback-trust the v1 contract, or aspirational?** The spec
   acknowledges "no authentication today" and frames it as needing review.
   F1 is the canonical answer and is cheap; the question is whether it
   ships now or after Chunk 02 completes.
2. **What is the legitimate origin set for the VS Code extension?** F2's
   allowlist needs a concrete answer (is it `vscode-webview://` schemes,
   or does the extension proxy through the host process so no Origin
   header reaches the server?).
3. **Is the schema tag ever rotated?** If yes, F4's recommended fix
   (token-in-lockfile) needs a versioning story; if no, the schema tag is
   a *public constant forever* and cannot be part of any authentication
   decision.
4. **Should `mur devtools` connect to a session whose lockfile names a
   different project?** Today the discovery is keyed by hash of project
   path (`LockfileRegistry.cs:65-78`), so cross-project mismatch isn't
   reachable through the normal path; it *is* reachable via
   `--endpoint`. Make it explicit in the spec.
5. **Single-instance on PID-only liveness for stdio sessions
   (`LockfileRegistry.cs:181`).** A reused PID after the original session
   exited but before lockfile cleanup runs would fool the check. Is the
   PID-reuse window worth a unique-startup-id pin?
6. **The supervisor exit-code-42 reload contract — does it preserve the
   per-session token across a reload, or mint a new one?** Affects whether
   long-lived agents need to re-read the lockfile after every reload.

---

## 8. Out-of-scope referrals

* **Chunk 02** owns every tool handler reachable through the dispatcher
  (`DevtoolsTools.cs`, `DevtoolsFireTool.cs`, `DevtoolsLogsTool.cs`,
  `DevtoolsStateTool.cs`, `DevtoolsPropertyTools.cs`,
  `DevtoolsUiaTools.cs`). The transport findings here amplify whatever
  authority each handler has; F1 is therefore a hard prerequisite for
  Chunk 02's mitigations to be meaningful.
* **Chunk 03** covers the preview-capture HTTP server and hot-reload —
  same loopback-trust problem on a parallel listener; the F1/F2/F3 fix
  pattern likely applies verbatim.
* **Chunk 05** owns the CLI's lockfile *consumer* logic (`DevtoolsSupervisor.cs`,
  `EndpointDiscovery.cs`, `LockfileReader.cs`, `McpCliClient.cs`,
  `SessionCommands.cs`). F4 and F5 partially live there too — the
  reader-side validation has to match whatever the writer adopts.
* **Chunk 04** owns the VS Code extension's HTTP client; F2's origin
  question depends on the extension's exact request shape.
* The **`OnDispatcher` 5 s timeout** at `DevtoolsMcpServer.cs:302-331`
  is a transport-side knob the chunk owns, but tool-side blocking
  patterns (long-running reads, pumping the UI thread) belong to Chunk 02.
