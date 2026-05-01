# Chunk 05 — `mur` CLI ↔ devtools client

**Phase:** 2 — per-chunk threat model
**Reviewer:** Phase-2 security pass
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Companion:** `000-chunking-and-threat-model.md` §2 (trust model), §4 Chunk 05.

This chunk is the *client* half of the devtools loopback channel — the part of `mur` that discovers a running devtools session via lockfile, opens an HTTP endpoint to the loopback MCP server, and supervises a `dotnet run` child. The transport/dispatch on the *server* side is Chunk 01; the actual MCP tool implementations are Chunk 02. Findings here that imply server changes are referred forward.

---

## 1. Scope

| File | Lines | Role |
|---|---|---|
| `src/Reactor.Cli/Devtools/DevtoolsSupervisor.cs` | 348 | `mur devtools` top-level dispatch + child-process supervisor (respawn on exit-code-42) |
| `src/Reactor.Cli/Devtools/DevtoolsVerbs.cs` | 641 | One method per named verb (`tree`, `screenshot`, `fire`, `call`, …); emits JSON-RPC tool calls via `McpCliClient` |
| `src/Reactor.Cli/Devtools/EndpointDiscovery.cs` | 90 | `Resolve()`: `--endpoint` > lockfile scan > (future `--auto`) |
| `src/Reactor.Cli/Devtools/LockfileReader.cs` | 105 | Parses lockfiles in `%TEMP%/reactor-devtools/`, probes liveness |
| `src/Reactor.Cli/Devtools/McpCliClient.cs` | 89 | Synchronous JSON-RPC POST helper |
| `src/Reactor.Cli/Devtools/SessionCommands.cs` | 106 | `session list` / `session clean` |
| **Total** | **1379** | |

For trust-decision context this review also reads (but does not own) the server-side counterpart `src/Reactor/Hosting/Devtools/LockfileRegistry.cs` because it defines the same on-disk schema.

---

## 2. Data-flow diagram

```
                                                   (Chunk 04)
        +------------+        argv             +-------------------+
        | shell user |------------------------>|  mur (this chunk) |
        +------------+                         +---------+---------+
                                                         |
                  +---------+ A. SUPERVISOR PATH         | B. NAMED-VERB PATH
                  |  args   | (`mur devtools <proj>`)    | (`mur devtools tree`)
                  v         |                            v
       ParseArgs() ─> LaunchChild(project, comp, port)   EndpointDiscovery.Resolve()
                  |                                       |
                  |  ProcessStartInfo("dotnet")           |    +---- explicit --endpoint? -> use it
                  |  ArgumentList = [run, --project,      |    |
                  |   <project>, --, --devtools, run,     |    +---- else: LockfileReader.EnumerateAll()
                  |   <component?>, --mcp-port, <N>,      |          | %TEMP%/reactor-devtools/*.json
                  |   --devtools-project, <fullpath>]     |          | TryRead -> JsonDocument.Deserialize
                  |  WorkingDirectory = cwd               |          | IsLive(): PidAlive + HttpProbe(GET endpoint, schema tag)
                  v                                       |          v
              child `dotnet run` (Chunk 01 server)        |     filter Transport=="http" -> List<entry>
                  |                                       v
                  +-- exit 42? rebuild + relaunch    McpCliClient.InvokeTool / InvokeMethod
                  +-- other exit? propagate              | POST <entry.Endpoint>  (loopback URL from lockfile)
                                                         | Content-Type application/json, body = JSON-RPC
                                                         v
                                                   server response (Chunk 01) ---> EmitResult -> stdout/stderr

External outputs:
  - Subprocess invocation (LaunchChild, RunDotnetBuild)
  - HTTP POST/GET to discovered loopback endpoint
  - File writes via `--out <path>` for screenshot
  - Stdout / stderr for the human

External inputs:
  - argv (TRUSTED — comes from the user invoking mur)
  - Files in %TEMP%/reactor-devtools/*.json (UNTRUSTED — any local writer)
  - HTTP responses from the (claimed) loopback devtools server (UNTRUSTED for this review)
  - cwd (TRUSTED — user's shell cwd)
  - %PATH% / DOTNET_ROOT (TRUSTED — user's environment)
```

---

## 3. Trust boundaries crossed

| # | Boundary | Direction | Assumption today | Holds? |
|---|---|---|---|---|
| TB-1 | argv -> mur process | in | argv is trusted (per top-level model). | yes |
| TB-2 | `%TEMP%/reactor-devtools/*.json` -> CLI | in | Lockfiles are **untrusted** — any local user can write to `%TEMP%`. | only as far as the CLI defends. **Findings F-1, F-2, F-3, F-4** show defenses are weak. |
| TB-3 | (claimed) loopback HTTP endpoint -> CLI | bidi | Loopback is "trusted" by the framework's stated model. | challenged: any process running as the same user can bind a loopback port and claim a lockfile. **Findings F-1, F-2, F-3.** |
| TB-4 | CLI -> child `dotnet` process | out | CLI args (project path) are trusted -> `dotnet run` args are trusted. Child inherits full env / cwd. | mostly yes; **Findings F-7, F-8** for the supervisor's `--devtools-project` arg using untrusted `Path.GetFullPath`, and the working-directory choice. |
| TB-5 | CLI -> arbitrary URL via `--endpoint` | out | `--endpoint` is a trusted CLI arg per the top-level model. | yes for the URL itself, but it puts the CLI in HTTP egress mode against any host the user names — see F-9 for SSRF surface when a wrapping agent passes through `--endpoint`. |

---

## 4. Asset inventory

What's worth attacking on the CLI side:

| Asset | Why it matters |
|---|---|
| **The CLI's choice of endpoint** | If an attacker controls which URL the CLI POSTs to, they can either silently observe the user's `mur` activity (devtools traffic = lots of source / window data) or feed crafted MCP responses that the agent driving `mur` then acts on. |
| **The CLI's authority to reach loopback** | The CLI runs as the user. A hostile lockfile redirects that authority at any URL — same-machine port for now, but only because the CLI happens to write a `127.0.0.1` URL itself; the lockfile schema does not restrict the value (see F-1). |
| **The supervised child's argv / cwd / env** | `LaunchChild` inherits cwd, all env vars, and full search-path resolution of `dotnet`. A user-named project can be a path that contains a `.csproj` with `<Exec />` targets. (Out of scope: build-time RCE via `.csproj` is the developer's own problem and is the assumed trust model — but see F-8 for working-directory surprises that change *which* `.csproj` is built when ambiguous.) |
| **The MCP response body** | Used to drive a base64 PNG decode + arbitrary path write (`--out <path>`). See F-10. |
| **Stale lockfile cleanup** | `EnumerateAll` opportunistically `TryDelete`s files that fail to parse or fail liveness. This gives any reader the ability to silently delete *any* file matching `%TEMP%/reactor-devtools/*.json` (the only constraints are extension and directory). Low impact today but a useful primitive for a hostile co-tenant. See F-11. |

**Capabilities at risk:**
- Spoofing of "the running devtools session" -> attacker-in-the-middle for an agent driving `mur`.
- Local EoP via subprocess if argv quoting is wrong (verified: `ArgumentList` is correctly used, see Mitigations table — F-7 is a smaller variant).
- Unbounded local DoS / disk write via `--out`.

---

## 5. STRIDE table

| # | Cat | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding / recommendation |
|---|---|---|---|---|---|---|---|
| S1 | **Spoofing** | A hostile local process writes a lockfile pointing at an endpoint *it* hosts (still on `127.0.0.1`, different port), with a forged `pid` of an unrelated running `dotnet` process (or its own pid). The HTTP probe to that endpoint returns a forged `{"schema":"reactor-devtools-mcp/1"}`. The CLI accepts it as live and POSTs every subsequent JSON-RPC call to the attacker. | Local user-mode process running as the same user. (Multi-user dev box, hostile npm package run as the dev, etc.) | **High.** All devtools traffic — selectors, fire-event arguments, screenshot results, log output — goes to the attacker. The attacker also sees / shapes everything the agent driving `mur` then "knows about" the running app. Confused-deputy: an LLM agent acting on the response can be instructed via the response itself. | medium-high once an attacker is on the box | None on the client side. `LockfileReader.IsLive` (LockfileReader.cs:68-75) verifies *only* "pid is alive" + "endpoint serves the schema tag," neither of which proves the pid owns the port. | **F-1 (High)**, **F-2 (High)**. Bind the lockfile to a property only the real server can own (e.g. cookie value the server signs into a token returned by `GET /mcp`, or the kernel-truth answer of "which pid owns TCP port N"), and verify that during discovery. |
| S2 | **Spoofing** | PID-reuse race: lockfile names `pid=1234`. By the time the CLI calls `Process.GetProcessById(1234)` the original process has exited and Windows has handed out 1234 to a new, unrelated process. `PidAlive` returns true; `HttpProbe` is then made to the lockfile-supplied URL, which a co-tenant can have already taken over. | Local same-user process timing the race. | Same as S1 but smaller (depends on PID reuse + opportunistic port grab). | low absolute, but reproducible on a busy build agent | None. | **F-2** — same fix as S1; the only sound liveness check is "the port is owned by a process whose identity we recognize." |
| T1 | **Tampering** | Lockfile JSON deserialized with no schema validation. Hostile `endpoint` value steers the CLI to any URL. The CLI's discovery filter requires `Transport == "http"` (case-insensitive) but does not validate that `Endpoint` is `http://127.0.0.1:<port>/<path>`. A lockfile with `"endpoint":"http://10.0.0.5:8080/mcp"` (or `http://attacker.example/`) is accepted. | Any local writer (`%TEMP%` ACL is per-user, but a hostile co-tenant or compromised tool running as the same user qualifies). | **High.** Cross-machine egress; SSRF; data exfil to remote. | medium | `Schema` field exists but is **never checked** at the CLI in `LockfileReader.TryRead` (LockfileReader.cs:46-60); see F-3. | **F-3 (High)**: in `TryRead`, reject entries whose `Schema != SchemaTag`. In `EndpointDiscovery.FindLiveHttpSessions`, reject `Endpoint` that doesn't match `^http://127\.0\.0\.1:\d+/`. |
| T2 | **Tampering** | Lockfile `Project` field is rendered to console in `session list --pretty` (SessionCommands.cs:71-72) and the multi-session disambiguation message (EndpointDiscovery.cs:55). Untrusted text written to a Windows console can include ANSI-escape control sequences (newer Win10/11 conhost honors them) and zero-width / RTL override codepoints; the user is then asked to choose between displayed entries that don't say what they look like they say. | Any local writer. | Medium (UI confusion, social-engineered "pick the legit session"). | medium | None. | **F-4 (Medium)**: sanitize/escape `Project`, `Endpoint`, `BuildTag` before printing. At minimum strip C0 controls and bidi-override codepoints; clamp length. |
| T3 | **Tampering** | Lockfile `endpoint` smuggles credentials (`http://user:pass@127.0.0.1:N/`) which `HttpClient` happily honors and sends as `Authorization: Basic`. Today the server doesn't read those, but a future agent that *does* use `--endpoint` for an authenticated server would now leak supplied credentials to whoever wrote the lockfile. | Local writer. | Low today, latent. | low | None. | **F-5 (Low/Info)**: parse `Endpoint` as `Uri` and reject `UserInfo`. Same check on `--endpoint`. |
| T4 | **Tampering** | `McpCliClient.InvokeTool`/`Post` accepts any JSON body the (claimed) server returns. In `Screenshot()` (DevtoolsVerbs.cs:194-216) the `result.png` is base64-decoded and written to `outPath` *as-given*, with no validation that it is a path under cwd, no detection of NTFS streams (`foo.png:hidden`), no rejection of NUL bytes, no check that `outPath` doesn't traverse out via `..`. Combined with S1, an attacker-controlled response can write arbitrary bytes to any file the user is allowed to write to (via a relative path the user typed). | `--out` is user-supplied, so this is mostly self-harm; but combined with S1 the *content* is attacker-controlled and the user thinks they wrote a "screenshot." | Medium (not arbitrary-file-write because path is user-supplied, but content is attacker-controlled when paired with S1). | low alone, medium combined | None beyond OS ACLs. | **F-10 (Medium)**: validate `result.png` is a syntactically valid PNG before writing (magic-byte check is enough); refuse `outPath` containing NUL or alternate-stream `:`. |
| E1 | **EoP** | `LaunchChild` calls `Path.GetFullPath(project)` and embeds the result into the child's `--devtools-project` arg (DevtoolsSupervisor.cs:195). Because `Path.GetFullPath` resolves against cwd at call time, and `project` came from argv *or* from `FindDefaultProject` (a glob of the cwd), a cwd that contains a `.csproj` with a surprising name doesn't get rejected. There's no path-injection possible (good — `ArgumentList` quotes correctly), but the resolved path is also handed to the child as a "trusted project root" and the server then operates on it. | A developer who runs `mur devtools` in an unfamiliar directory. | Low (developer self-foot-shooting). | low | `ArgumentList` is used everywhere (DevtoolsSupervisor.cs:160-161, 208-209) — no shell, no quoting bugs. | **F-7 (Low/Info)**: validate that `Path.GetFullPath(project)` ends in `.csproj` and exists on disk. |
| E2 | **EoP** | `LaunchChild` and `RunDotnetBuild` use `WorkingDirectory = Directory.GetCurrentDirectory()` (DevtoolsSupervisor.cs:158, 206) and rely on `dotnet` being resolved from `%PATH%`. No `UseShellExecute=false` plus a fully-qualified executable path means `%PATH%` poisoning is the surface — a `dotnet.exe` planted earlier in `%PATH%` than the real one is launched. This is the standard "trust your `%PATH%`" assumption Reactor inherits, but it bears stating. | A developer with a poisoned `%PATH%` (extension, malicious package post-install). | Critical if exploited; standard assumption. | low | `UseShellExecute=false` is set, so cmd.exe fallbacks aren't in play. | **F-8 (Info)**: Document the assumption. Optional: resolve `dotnet` against `%DOTNET_ROOT%` first, then `%PATH%`, with a single deterministic lookup. |
| E3 | **EoP** | `Process.Start(psi)` with `psi` constructed via `new ProcessStartInfo("dotnet")` then `ArgumentList.Add(...)` (DevtoolsSupervisor.cs:153-167). On Windows, `ProcessStartInfo.ArgumentList` produces a properly Win32-quoted command line; this is the right API. **No bug here**, recorded as a positive. | n/a | n/a | n/a | n/a | **No finding** — kept in table to mark that the obvious foot-gun (`Arguments=` string concat) was not used. |
| E4 | **EoP** | `--args JSON` (`fire`, `call`) is parsed via `JsonDocument.Parse` and forwarded as the `arguments`/`params` of a JSON-RPC call (DevtoolsVerbs.cs:368-369, 498-499). The parsed JSON is *not* re-stringified by the CLI; it's serialized fresh by `McpCliClient`, so there's no JSON-injection. **Positive.** | n/a | n/a | n/a | n/a | **No finding.** |
| I1 | **Info** | When discovery returns multiple live sessions (EndpointDiscovery.cs:51-59) the disambiguation message is printed to **stderr** (via `ErrorMessage`) and includes raw `entry.Endpoint`, `entry.Pid`, `entry.Project` from possibly-attacker-written lockfiles. Same with `session list` (SessionCommands.cs:69-78). | n/a — the asset is the *user's screen*; see T2. | covered by T2. | n/a | None. | Covered by F-4. |
| I2 | **Info** | `mur devtools session list` prints the JSON of *every* lockfile it can read (SessionCommands.cs:78), including `Project` (full path on disk) and `BuildTag`. If a hostile lockfile-writer plants a junk entry that *passes* liveness (it can; see S1), they cause the CLI to display attacker-controlled JSON to the user. Same data-disclosure risk as I1, T2; on top, an agent piping `session list` output back into a tool will consume attacker text. | Local writer. | medium when piped into an agent. | medium | Liveness probe filters out trivially-broken entries but **does not** filter attacker-planted live-looking entries. | **F-6 (Medium)**: same fix as F-3 — endpoint allowlist (`http://127.0.0.1:*`) and schema-tag check at the parser level so attacker entries are dropped before list/disambiguation. |
| I3 | **Info** | `HttpProbe` issues an unauthenticated `GET <endpoint>` to whatever URL the lockfile says (LockfileReader.cs:87-104). With T1's vulnerability, that GET goes to an attacker-controlled host. The probe carries default `User-Agent` etc. — no secrets — so the only leakage is "this dev box is running mur and just looked at lockfile X." | Local writer who plants the lockfile. | Low. | high (every CLI invocation) | None. | Subsumed by F-3 (don't talk to off-loopback URLs). |
| I4 | **Info** | `McpCliClient.Post` returns the raw body of an unsuccessful response into the `HttpRequestException` message (McpCliClient.cs:74-76), which then gets printed to stderr by `DevtoolsVerbs.Run`'s catch (DevtoolsVerbs.cs:77). If the (claimed) server returns a 500 with attacker text, that text lands on the user's terminal. Bounded by S1/T1 (only matters if the endpoint is hostile). | Hostile endpoint operator. | Low (UI confusion). | low | None. | **F-12 (Low)**: clamp printed body to N bytes and strip controls. |
| D1 | **DoS** | `LockfileReader.EnumerateAll` (`EndpointDiscovery.FindLiveHttpSessions`) iterates **every** `*.json` file under `%TEMP%/reactor-devtools/`, calling `IsLive` on each. Each `IsLive` does a **synchronous** `HttpClient.GetAsync` with a **500ms** per-file timeout (LockfileReader.cs:92). A hostile co-tenant who plants 10000 lockfiles each pointing at a slow-loris endpoint (or 127.0.0.1:N where they hold the connection) will cause every `mur devtools <verb>` invocation to stall for ~10000 × 500ms = 83 minutes. | Same-user co-tenant. | High (denial of `mur` for the dev). | medium | Each request is bounded at 500ms but the *count* is unbounded. | **F-13 (Medium)**: cap `EnumerateAll` at, e.g., 64 files. Probe lockfiles in parallel with a single overall budget (e.g. 2s wall clock). |
| D2 | **DoS** | `JsonDocument.Parse(body)` on the discovery probe (LockfileReader.cs:96) and on every JSON-RPC response (McpCliClient.cs:81). No max body size. A hostile endpoint can stream a multi-GB body and starve the CLI. | Hostile endpoint owner (S1/T1). | Medium (stalls + memory). | low (only on a poisoned host) | `HttpClient` doesn't cap response size by default. | **F-14 (Low)**: bound `MaxResponseContentBufferSize` on both `HttpClient` instances. |
| D3 | **DoS** | Supervisor reload loop: child exits 42 -> rebuild -> respawn. There's no rate limit. A bug that causes the child to die quickly with code 42 would loop the supervisor with no backoff. | Self-induced. | Low (developer notices). | low | None — `RunDotnetBuild` blocks (synchronous), so the wall-clock is bounded by build time. | **F-15 (Info)**: add a min-interval check (e.g. refuse to re-launch if the previous run lived <5s) to avoid hot-spinning. |
| R1 | **Repud.** | The CLI doesn't log its outbound `endpoint` choice or `pid`/`project` of the chosen lockfile. If a developer runs `mur devtools tree --pretty` and the response is wrong, they have no record of which lockfile was used, which port was contacted, or which pid. | n/a | Low. | n/a | None. | **F-16 (Info)**: emit the chosen endpoint + pid to stderr at debug level (env-flagged), so post-incident triage can identify who owned the port. |

---

## 6. Findings

Severity scale: Critical / High / Medium / Low / Info.

### F-1 — No proof that the lockfile-claimed PID owns the lockfile-claimed port. **(High, Spoofing)**

**Where:** `src/Reactor.Cli/Devtools/LockfileReader.cs:68-104` — `IsLive` and `HttpProbe`.

`IsLive` checks two things:
1. `Process.GetProcessById(entry.Pid)` doesn't throw (LockfileReader.cs:81-82).
2. `GET entry.Endpoint` returns 2xx and the body has `{"schema":"reactor-devtools-mcp/1"}` (LockfileReader.cs:96-98).

Neither of these proves *the same process* satisfies both. Anyone running as the user can:
- Pick any live `dotnet.exe` PID off the system (or just spawn one).
- Bind their own server on `127.0.0.1:<some port>` that returns the schema tag in its `GET /mcp` response.
- Drop a lockfile in `%TEMP%/reactor-devtools/<arbitrary>.json` containing that pid + that port.

`mur devtools <verb>` will route every JSON-RPC call to the attacker.

**Recommendation:**

Two layers:

1. **Process-port binding check (Windows).** Use `iphlpapi.dll` (`GetExtendedTcpTable` with `TCP_TABLE_OWNER_PID_LISTENER`) to enumerate which PID owns the listening port; require `(entry.Pid, entry.Port)` to appear in that table before treating the entry as live. The framework already P/Invokes `iphlpapi` in the netpulse sample so the prior art exists.
2. **Token in lockfile.** When the server writes the lockfile, include a 32-byte random token; the server's `GET /mcp` returns the same token. Client compares. (This also defends against the case where the attacker won the race for the port and the real server has died — port-PID binding alone wouldn't catch that.)

Either alone is a real improvement; both together close the gap.

### F-2 — TOCTOU between `IsLive` probe and tool POST. **(High, Spoofing)**

**Where:** `EndpointDiscovery.cs:68-89` -> `DevtoolsVerbs.cs:39` -> `McpCliClient.Post`.

`FindLiveHttpSessions` probes the endpoint with a 500ms `GET`, then later `DevtoolsVerbs.Run` constructs an `McpCliClient` and POSTs to the *same string*. Between probe and POST, the real owner can exit and a co-tenant can grab the loopback port (the kernel SO_REUSEADDR semantics on Windows are surprising, but even without `SO_REUSEADDR` a port that the previous owner released is fair game).

Today this is a smaller variant of F-1. With F-1's port-pid-token mitigation, the same token must be carried on every POST (or each invocation must re-probe right before sending). The simplest fix: keep one `HttpClient` keyed to a fresh nonce returned by the probe; the server rejects requests with the wrong nonce.

**Recommendation:** require a per-session token (random, sent to the server out-of-band via the lockfile, validated on every JSON-RPC). Defends F-1 *and* F-2 with one mechanism.

### F-3 — Lockfile schema/endpoint not validated; arbitrary URL accepted. **(High, Tampering)**

**Where:** `src/Reactor.Cli/Devtools/LockfileReader.cs:46-60` (`TryRead`) and `EndpointDiscovery.cs:68-89` (`FindLiveHttpSessions`).

`TryRead` deserializes any JSON object that fits the shape of `LockfileEntry`; the `Schema` field is a plain string property with no equality check anywhere in the CLI. `Endpoint` is also unvalidated — the only filter is "Transport == http" at EndpointDiscovery.cs:84. A lockfile with `"endpoint":"http://example.com/"` is accepted; `"endpoint":"http://127.0.0.1:N/../path"` is accepted; `"endpoint":"file:///etc/passwd"` is *not* (because `Transport == http`), but `http://[::1]:N/` is.

Result: any `%TEMP%` writer steers the CLI's HTTP egress to anywhere they like, on or off the box.

**Recommendation:**

In `LockfileReader.TryRead`:
```csharp
if (entry.Schema != SchemaTag) { entry = null; return false; }
```

In `EndpointDiscovery.FindLiveHttpSessions` (and at the same place for `--endpoint` from argv if user-passed):
```csharp
if (!Uri.TryCreate(entry.Endpoint, UriKind.Absolute, out var uri)) continue;
if (uri.Scheme != "http") continue;
if (!IPAddress.TryParse(uri.Host, out var ip) || !IPAddress.IsLoopback(ip)) continue;
if (!string.IsNullOrEmpty(uri.UserInfo)) continue;  // F-5
```

(Note: `--endpoint` is a CLI arg and per the trust model is trusted, so the loopback constraint there is a defense-in-depth choice. But because some agents pass through user-typed strings unfiltered, it's worth the cycles.)

### F-4 — Unsanitized lockfile fields printed to user terminal. **(Medium, Tampering / UI confusion)**

**Where:**
- `EndpointDiscovery.cs:55` — disambiguation message includes `entry.Endpoint`, `entry.Pid`, `entry.Project`.
- `SessionCommands.cs:71-72` — `session list --pretty` row formatting.
- `SessionCommands.cs:77-78` — `session list` JSON output: serializes attacker-controlled strings unfiltered.
- `McpCliClient.cs:74-76` -> `DevtoolsVerbs.cs:77` — server error body printed unfiltered.

A hostile lockfile with `"project":"/Users/dev/Project1[2K[1Aspoofed"` paints over the previous terminal line. With CSI escapes, an attacker can also reposition the cursor and rewrite earlier output (the multi-session disambiguation prompt becomes "pick session 1: /Users/dev/Project1" while session 1 is actually the attacker's lockfile).

**Recommendation:** sanitize before display. A small helper:
```csharp
static string SafeForTerminal(string s, int max = 120)
{
    var sb = new StringBuilder(s.Length);
    foreach (var ch in s)
        sb.Append(char.IsControl(ch) || (ch >= '‪' && ch <= '‮') ? '?' : ch);
    return sb.Length > max ? sb.ToString(0, max) + "…" : sb.ToString();
}
```
Apply at every `Console.Write(...)` of attacker-controlled fields. For the JSON `session list` non-pretty path, refuse to emit entries whose fields fail the sanitizer (mark them `"warning":"contained control codes"`).

### F-5 — `Endpoint` userinfo not stripped. **(Low/Info, Tampering)**

**Where:** `LockfileReader.cs:87-104`, `McpCliClient.cs:65`.

`HttpClient` honors `Uri.UserInfo` and sends it as basic auth. Today no devtools server reads it, so the only impact is that an attacker-supplied lockfile can plant credentials in the user's HTTP client logs / proxy logs. Latent vulnerability if any future MCP server supports auth.

**Recommendation:** reject `Endpoint` URIs with `UserInfo`. (Folded into F-3 patch.)

### F-6 — `session list` exposes attacker JSON to whatever consumes it. **(Medium, Info disclosure / Confused-deputy)**

**Where:** `src/Reactor.Cli/Devtools/SessionCommands.cs:46-80`.

`session list` (default, non-`--pretty`) emits JSON Lines, one entry per lockfile. An LLM agent that runs `mur devtools session list` and parses the output then sees attacker-controlled `project`, `endpoint`, `buildTag`, `pid` fields. Combined with prompt-injection in the `project` string, this becomes a confused-deputy primitive against any agent that ingests `session list` output.

**Recommendation:** apply the F-3 endpoint filter and the F-4 sanitizer at this layer too. A lockfile that fails the filter gets dropped, not displayed.

### F-7 — `Path.GetFullPath(project)` not validated before being passed to child. **(Low/Info, EoP / correctness)**

**Where:** `src/Reactor.Cli/Devtools/DevtoolsSupervisor.cs:179-197` — `BuildChildArguments`.

`Path.GetFullPath(project)` is resolved against the *current directory at call time* and shoved into `--devtools-project`. The path is not checked to be `.csproj`, not checked to exist, not checked to be inside any specific root.

The argv is correctly passed via `ArgumentList` so there's no shell injection. The risk is mis-targeting: a developer typing `mur devtools ../foo.csproj` from a directory they did not expect launches a different project.

**Recommendation:**
```csharp
var full = Path.GetFullPath(project);
if (!full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
{
    Console.Error.WriteLine($"[mur devtools] not a .csproj: {project}");
    return 1;
}
```

### F-8 — Working directory and `dotnet` resolution. **(Info, EoP — assumed-trust documentation)**

**Where:** `DevtoolsSupervisor.cs:158, 206`.

`WorkingDirectory = Directory.GetCurrentDirectory()` and `new ProcessStartInfo("dotnet")` (unqualified) means the child resolves `dotnet` against the user's `%PATH%`, with the user's full environment, with cwd as wherever they ran from. This is the standard subprocess assumption and matches how `mur` is documented to work, but it's worth recording explicitly so it isn't *accidentally* relaxed in the future (e.g. someone adding `UseShellExecute = true`, or wrapping in `cmd /c`).

**Recommendation:** add a one-line code comment near the `ProcessStartInfo` constructions stating the trust assumption, and ensure no future change introduces `UseShellExecute=true` or `CreateProcess` with a string concat. A test in `Reactor.Cli.Tests` that asserts `LaunchChild`'s `psi` has `UseShellExecute == false` and uses `ArgumentList` (not `Arguments`) is cheap.

### F-9 — `--endpoint` accepts arbitrary URL; no loopback constraint. **(Low, Tampering)**

**Where:** `src/Reactor.Cli/Devtools/EndpointDiscovery.cs:35-36`.

`Resolve` returns whatever the user typed in `--endpoint` with no validation. Per top-level trust, CLI args are trusted, but the trust ends at the moment the value is forwarded to an automated agent. Real-world: an LLM agent that writes a script taking a `MUR_ENDPOINT` env var and passing it to `mur devtools --endpoint $MUR_ENDPOINT` re-introduces the F-3 surface from the user's environment.

**Recommendation:** apply the same allowlist (`http://127.0.0.1:*`) by default; require `--allow-remote-endpoint` flag (or env `MUR_ALLOW_REMOTE`) to opt out. Cheap, defense-in-depth.

### F-10 — `screenshot --out <path>` writes attacker-controlled bytes. **(Medium, Tampering — combined with F-1/F-3)**

**Where:** `src/Reactor.Cli/Devtools/DevtoolsVerbs.cs:194-216`.

Logic:
```csharp
var bytes = Convert.FromBase64String(pngEl.GetString()!);
File.WriteAllBytes(outPath, bytes);
```

The path is user-supplied (trust OK), but the bytes come from the (claimed) MCP server response. With F-1 unfixed, the bytes are attacker-controlled. With NTFS alternate data streams (`foo.png:hidden`) the user can be tricked into writing a hidden stream they won't notice; with a path containing a NUL the underlying API handling may surprise.

**Recommendation:**
- Validate magic bytes: first 8 bytes must be `89 50 4E 47 0D 0A 1A 0A`.
- Refuse `outPath` containing `\0` or `:` past the drive-letter colon (i.e. additional `:` segments).
- Cap `bytes.Length` at e.g. 64 MiB.

### F-11 — `EnumerateAll` opportunistically deletes parse-failing files. **(Low, Tampering)**

**Where:** `EndpointDiscovery.cs:73-77` and `SessionCommands.cs:53-56, 87-99`.

`FindLiveHttpSessions` and `RunClean` call `LockfileReader.TryDelete(path)` for any `.json` in `%TEMP%/reactor-devtools/` that fails to parse OR fails liveness. Since `TryRead` swallows all exceptions and the directory is `%TEMP%/reactor-devtools/`, a hostile co-tenant can plant junk `*.json` files in that directory and the next `mur` invocation deletes them — useful as "make my evidence go away" only if they were the ones who wrote it. Not a real privilege escalation, but a free file-delete primitive *limited to this one directory* exists.

The bigger risk: a *legitimate* lockfile that the CLI fails to parse (e.g. due to a future schema bump that the CLI hasn't been updated for) gets silently deleted, breaking discovery for an in-progress session.

**Recommendation:**
- Distinguish "parsed but stale" from "didn't parse" in deletion policy: only auto-delete when `IsLive` returned definitively `false` AND parse succeeded AND `Schema == SchemaTag`. Unknown schemas are left alone.
- This is also a good place to log (debug) the deletion so a misbehaving CLI is diagnosable.

### F-12 — Server-error body printed unbounded to stderr. **(Low, Info disclosure)**

**Where:** `src/Reactor.Cli/Devtools/McpCliClient.cs:74-76`.

```csharp
throw new HttpRequestException(
    $"MCP {_endpoint} returned HTTP {(int)resp.StatusCode} {resp.StatusCode}" +
    (string.IsNullOrWhiteSpace(body) ? "" : $": {body}"));
```

`body` is unbounded and prints raw to stderr via the verb's catch (DevtoolsVerbs.cs:77). With F-1 unfixed, an attacker controls `body`.

**Recommendation:** clamp to 512 bytes and run the F-4 sanitizer. (Combined fix.)

### F-13 — Lockfile-directory enumeration unbounded; per-file 500ms probe is serial. **(Medium, DoS)**

**Where:** `src/Reactor.Cli/Devtools/LockfileReader.cs:32-44` and the loops in `EndpointDiscovery.cs:71` / `SessionCommands.cs:51, 87`.

A co-tenant can plant N lockfiles, each pointing at a port that returns the schema tag after holding the connection open. Because the loop is serial and each `IsLive` waits up to 500ms (LockfileReader.cs:92), wall-clock for `mur devtools <verb>` becomes 500ms × N.

**Recommendation:**
- Cap N (`files.Take(64)` is plenty — there should never be more than a handful of running devtools sessions per user).
- Probe in parallel with `Task.WhenAll` and a 2-second total budget.
- Order by lockfile mtime, newest first, so even with the cap the most-recently-touched entries always win.

### F-14 — `HttpClient` response-buffer cap not set. **(Low, DoS)**

**Where:** `LockfileReader.cs:92` and `McpCliClient.cs:21`.

Default `HttpClient.MaxResponseContentBufferSize` is 2 GiB. A poisoned endpoint can stream 2 GiB.

**Recommendation:**
```csharp
new HttpClient { Timeout = ..., MaxResponseContentBufferSize = 16 * 1024 * 1024 }
```
(16 MiB is comfortable headroom for screenshot PNGs.)

### F-15 — Supervisor reload loop has no minimum interval. **(Info, DoS — self-induced)**

**Where:** `src/Reactor.Cli/Devtools/DevtoolsSupervisor.cs:129-148`.

`while (true) { LaunchChild ...; if (exitCode == 42) build + continue; ... }` — if a build bug causes the child to exit 42 immediately and the build returns success quickly, the loop hot-spins.

**Recommendation:** record `Stopwatch` around `LaunchChild`; if the child lived less than 1.5s and exited 42 three times in a row, abort with a clear message rather than respawn forever.

### F-16 — No audit of which lockfile / endpoint was selected. **(Info, Repudiation)**

**Where:** `EndpointDiscovery.cs:38-49`.

Single-live-session path returns the entry's endpoint without printing anything — the user/agent never sees the pid or project they ended up talking to. Post-incident, you can't tell which lockfile was authoritative.

**Recommendation:** when invoked under `MUR_DEVTOOLS_DEBUG=1` or `--verbose`, print the chosen `(endpoint, pid, project)` to stderr.

---

## 7. Open questions

1. **Loopback-trust model.** §2 of the chunking doc lists "loopback = trusted" as the assumption to challenge. F-1, F-3, F-9 challenge it directly: *any* same-user process can stand up a loopback HTTP server, so loopback alone provides zero authentication. **Question for the team:** does the threat model accept "any same-user process can MITM mur"? If yes, document it loudly. If no, F-1 + F-3 together are the minimum fix.
2. **`%TEMP%` ACLs in CI.** On a per-user dev box, `%TEMP%` is per-user. On a multi-tenant build agent (GitHub Actions self-hosted, Azure DevOps shared agents) this isn't always the case. Does the team consider that environment in scope for the devtools CLI? It affects whether F-3, F-13 are Medium or High.
3. **PID-port binding API.** F-1's recommended fix uses `GetExtendedTcpTable`. Is there appetite for an `iphlpapi` P/Invoke in `Reactor.Cli` (the netpulse sample has prior art but isn't shared code)? Alternative: ship a shared-secret token in the lockfile.
4. **Schema versioning.** `LockfileReader.SchemaTag = "reactor-devtools-lockfile/1"` is duplicated in both client and server. F-3 wants strict equality, but the team has stated a future schema bump is plausible. What's the migration story? Suggest: client accepts `lockfile/1` and any minor-version `lockfile/1.x`, rejects anything else; major bump requires CLI redeploy.
5. **`--endpoint` constraint.** Should the CLI default to refusing non-loopback `--endpoint` (F-9), with an opt-out flag? An LLM-agent caller is the realistic risk surface.
6. **Stale-file deletion authority.** Should `mur devtools session clean` cull files it can't parse / can't probe (current behavior), or only files matching the known schema? Current behavior = useful primitive for an attacker (F-11), but also keeps `%TEMP%` clean.

---

## 8. Out-of-scope referrals

| Concern surfaced here | Belongs to |
|---|---|
| Server-side authentication / origin checks / port binding | **Chunk 01 — Devtools transport & dispatch.** F-1's recommended token mechanism requires server cooperation (write the token into the lockfile, validate on every request). Chunk 01 owns that. |
| Sanitization of tool *responses* (screenshots, log entries) | **Chunk 02 — Devtools tools.** What goes into `result.png` and `result.entries[].text` is decided server-side. The CLI's only job is "don't render attacker bytes as if they were trusted output." |
| Server-side lockfile *write* path (atomic write, schema, deletion on dispose) | **Chunk 01.** Reviewed here only as a constraint on what the client can rely on. |
| `dotnet run` security (a malicious .csproj has full RCE on the dev) | Out of scope: assumed trust per top-level §2 ("Source code, project files, and build inputs … Trusted at build time"). F-7 / F-8 are the soft edges around that assumption. |
| `%PATH%` poisoning / `dotnet` resolution | Out of scope (host environment). Documented in F-8 as an inherited assumption. |
| VS Code extension's parallel "launch dotnet watch run" path | **Chunk 04 — VS Code extension.** Same threat shape (subprocess invocation) but on a different code path. |
| Selector parser DoS (selectors flow CLI -> server here) | **Chunk 12 — Other parsers.** The CLI just forwards selector strings; complexity is server-side. |
| Spec 025 lockfile contract evolution / schema versioning | **Spec 025.** This review records the contract bug that the schema field is *unchecked* on the read side; the spec should explicitly require it be checked. |

---

## Summary of findings

| ID | Severity | One-liner |
|---|---|---|
| F-1 | High | No proof the lockfile-named PID owns the lockfile-named port. |
| F-2 | High | TOCTOU between `IsLive` probe and `McpCliClient.Post`; PID/port reuse race. |
| F-3 | High | `LockfileEntry.Schema` and `Endpoint` not validated; arbitrary URL accepted. |
| F-4 | Medium | Untrusted lockfile fields printed to terminal unsanitized (control codes, RTL overrides). |
| F-5 | Low | `Endpoint` userinfo not stripped; latent basic-auth leakage. |
| F-6 | Medium | `session list` ships attacker-controlled JSON to downstream agents (confused-deputy / prompt injection). |
| F-7 | Low | `Path.GetFullPath(project)` not checked to be an existing `.csproj`. |
| F-8 | Info | Document subprocess trust assumption (`%PATH%`, env, cwd, `UseShellExecute=false`). |
| F-9 | Low | `--endpoint` accepts arbitrary URL; no loopback constraint by default. |
| F-10 | Medium | `screenshot --out` writes attacker-controlled bytes; no PNG validation; alternate-stream / NUL not refused. |
| F-11 | Low | Auto-delete-on-parse-fail is a usable file-delete primitive within `%TEMP%/reactor-devtools/`. |
| F-12 | Low | Server-error body printed unbounded; combined with F-1 lets attacker paint the user's terminal. |
| F-13 | Medium | Serial 500 ms probes over an unbounded directory listing — co-tenant DoS. |
| F-14 | Low | No `MaxResponseContentBufferSize` on either `HttpClient`. |
| F-15 | Info | Supervisor reload has no minimum interval. |
| F-16 | Info | No audit of selected `(endpoint, pid, project)` for post-incident triage. |

The cluster F-1 + F-2 + F-3 (+ supporting F-4, F-6, F-13) is the headline: **the CLI today gives any same-user local process the ability to silently impersonate the running devtools server.** The single highest-leverage fix is "lockfile carries a server-issued token, server validates it on every request" — that one mechanism closes F-1, F-2, and reduces F-3, F-13 to paper cuts.
