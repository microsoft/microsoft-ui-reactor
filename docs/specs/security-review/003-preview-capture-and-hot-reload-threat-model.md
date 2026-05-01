# Chunk 03 — Preview Capture Server + Hot Reload Threat Model

**Status:** Phase 2 — review complete
**Reviewed commit:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Reviewer:** security-review pass (auto)

---

## 1. Scope

| File | Lines | Role |
|---|---:|---|
| `src/Reactor/Hosting/PreviewCaptureServer.cs` | 368 | Loopback HTTP server — `/frame`, `/status`, `/focus`, `/components`, `/preview` |
| `src/Reactor/Hosting/HotReloadService.cs` | 23 | `MetadataUpdateHandler` — re-renders on .NET hot-reload notifications |
| `src/Reactor/Hosting/OverlayHostWiring.cs` | 369 | Shared wrapper Grid + Canvas + ContainerVisual for dev overlays |
| `src/Reactor/Hosting/ReconcileHighlightOverlay.cs` | 224 | Composition sprite overlay for mounted/modified element highlights |
| **Total** | **984** | |

Wiring (call sites cited in this doc):
- `src/Reactor/Hosting/ReactorApp.cs:386–399` — instantiates `PreviewCaptureServer`, binds `GetComponents` / `GetCurrentComponent` / `SwitchComponent`.
- `src/Reactor/Hosting/ReactorApp.cs:369–384` — `SwitchComponentCore` closure (the user-code dispatch path).
- `src/Reactor/Hosting/ReactorApp.cs:481–497` — `FindComponentType` reflection lookup.

---

## 2. Data-flow diagram

```
                    ┌─────────────────────────────────┐
                    │  VS Code extension webview      │
                    │  (Origin: vscode-webview://…)   │
                    └────────────┬────────────────────┘
                                 │ HTTP loopback (ephemeral port)
                                 ▼
   ┌────────────────────────────────────────────────────────────────────┐
   │  HttpListener  prefix = "http://localhost:{port}/"                 │
   │  (binds IPv4 + IPv6 loopback per HTTP.SYS resolution)              │
   ├────────────────────────────────────────────────────────────────────┤
   │  GET  /frame      ───►  latest JPEG bytes                          │
   │  GET  /status     ───►  {building,fps,port}                        │
   │  GET  /focus      ───►  SetForegroundWindow(_hwnd)                 │
   │  GET  /components ───►  enumerate AppDomain Component subclasses   │
   │  POST /preview    ───►  body.component → FindComponentType         │
   │                          → Activator.CreateInstance → host.Mount   │
   └────────────────────────────────────────────────────────────────────┘
                                 ▲
                                 │ Win32 PrintWindow @ Fps
                                 │ (UI thread DispatcherQueueTimer)
   ┌────────────────────────────────────────────────────────────────────┐
   │  WinUI window pixels  ──►  GDI Bitmap ──► JPEG ──► _latestFrame    │
   └────────────────────────────────────────────────────────────────────┘

   Hot reload (separate channel — not HTTP-reachable):
     .NET runtime  ──►  [MetadataUpdateHandler]  HotReloadService.UpdateApplication
                          └──►  ReactorApp.ActiveHost?.RequestRender()
```

---

## 3. Trust boundaries crossed

| Boundary | Direction | Stated assumption | Validity |
|---|---|---|---|
| Loopback HTTP listener (`localhost:port`) | inbound | "Loopback = trusted" (per §2 of the chunking doc) | **Tested below — fails for several threat actors:** any local process, any browser tab via CSRF, DNS-rebinding attacker. |
| .NET hot-reload metadata channel | inbound | Trusted — only the dotnet/VS host can deliver `MetadataUpdate` notifications | Holds. Not network-reachable. |
| WinUI window pixels (capture source) | "outbound" via `/frame` | Pixels = current dev's screen contents | **Side effect:** any process that can reach the port can pull screenshots of the dev's preview window. |

---

## 4. Asset inventory

| Asset | Why an attacker wants it |
|---|---|
| **Live screenshots of the preview window** (`/frame` JPEG stream) | The preview window is a real WinUI app the developer is iterating on. May contain test credentials, sample PII, draft markdown, AI-generated content, customer data being demoed, error dialogs with stack-trace tokens. |
| **Component list** (`/components`) | Discloses internal class names of every `Component`-subclass type loaded into the AppDomain (plus those of every referenced assembly). Useful for fingerprinting / targeted follow-ons. |
| **Foreground-grab capability** (`/focus`) | Lets a remote caller force the preview window to foreground — UI redress / focus-stealing primitive. |
| **Component-switch capability** (`/preview`) | Mount any `Component`-derived non-abstract type loaded in the AppDomain, by case-insensitive name. Triggers `Activator.CreateInstance(type)` → `host.Mount` → executes that component's constructor + `Render()`. |
| **Frame-capture timer / process** | DoS target: occupy CPU / memory; crash the host. |
| **Hot-reload re-render** (`HotReloadService`) | Re-renders trigger reconciler work; not directly callable from HTTP, so attacker reach is limited. |

---

## 5. STRIDE table

| # | Cat. | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding |
|---|---|---|---|---|---|---|---|
| S1 | **S**poofing | Any local process or user on the dev machine connects to `localhost:port` and impersonates the VS Code extension. | Local hostile process / other local user / sandboxed app. | Full access to all 5 endpoints. | High (default Windows: any user can connect to loopback ports). | None. Port is ephemeral but printed to stdout (`CAPTURE_PORT={Port}`) and trivially discoverable via `netstat`/`Get-NetTCPConnection`. | **F-01 (High).** No auth token, no Windows ACL on the port, no client-process check. |
| S2 | **S**poofing | Browser tab issues a GET/POST to `http://localhost:port/...` (CSRF-via-localhost). | Web page user has open. | `/focus` works (no preflight); `/preview` POST is reachable via `<form enctype="text/plain">` (no preflight); `/frame` and `/components` are reachable for non-cross-origin reads but the result image/JSON is not directly readable to the attacker page (CORS-blocked unless Origin is reflected). However, side-effect endpoints (`/focus`, `/preview`) execute regardless of Origin. | Medium (preview mode is a dev-only build, but the Reactor extension launches it as a routine `dotnet watch` activity, so dev's browser is plausibly running on the same host). | The `Origin` reflection on lines 158-167 is **CORS reflection only** — it never *rejects* requests on Origin mismatch. Side-effect handlers run before the Origin check is even consulted. | **F-02 (High).** `/preview` and `/focus` are CSRF-able from any browser tab. |
| S3 | **S**poofing | DNS rebinding: attacker page makes its own hostname resolve to `127.0.0.1` after initial fetch, then issues same-origin requests to `localhost:port`. | Web page. | Same as S2 but Origin matches — CORS reflection succeeds, attacker can read `/frame` JPEGs and `/components` list. | Low-medium (modern browsers' DNS pinning + Private Network Access reduce but do not eliminate). | None — no Host-header check on `localhost`. | **F-03 (Medium).** No defense against DNS-rebinding read of frames. |
| T1 | **T**ampering | `/preview` mounts arbitrary `Component`-subclass type by case-insensitive simple-name match. | Hostile local caller. | Switches the running preview to an attacker-chosen Component. The set of types is bounded to `Component` subclasses already loaded into the AppDomain (developer's own app + Reactor framework + samples), so this is **not** RCE-of-arbitrary-code — it can only execute constructors + `Render()` of existing types. | Medium. Constructors of dev components may have side effects (network calls, file writes, timers). Switching aborts the developer's current state. | None. No allow-list, no confirmation. | **F-04 (Medium).** Caller can trigger any in-AppDomain `Component`'s constructor + render side effects; potential for nuisance / state-loss / minor side-effect amplification. Not RCE. |
| T2 | **T**ampering | Hot-reload triggered by attacker. | Hostile local caller. | None — `HotReloadService.UpdateApplication` is an `[assembly: MetadataUpdateHandler]` callback only invoked by the .NET runtime when it applies a metadata update. It is not exposed over HTTP. | n/a | Channel inaccessible to network. | **No issue.** |
| I1 | **I**nfo disclosure | `/frame` returns JPEG of the entire preview window's client area. | S1/S2/S3 attackers above. | Screenshot of dev's app, possibly including secrets, in-progress code samples shown via `Md4cParser`, customer demo data, test credentials. | High once port reach is achieved. | None. Frames are continuously captured at `Fps` (default 10 fps) regardless of viewer count. | **F-05 (High when paired with F-01/F-03).** Screenshots are the most sensitive asset on this surface. |
| I2 | **I**nfo disclosure | `/components` enumerates every non-abstract `Component`-subclass full name in every loaded assembly, including referenced libraries. | S1/S2 attackers. | Reveals component class names. Filter at `ReactorApp.cs:504` excludes types in `Microsoft.UI.Reactor.*` namespace but **only by name prefix** — it does not exclude internal helper component types in user assemblies. | Medium. Largely fingerprinting value. | Implicit name-prefix filter on framework types only. | **F-06 (Low).** Discloses developer's component graph; not normally sensitive but useful for targeted social-engineering. |
| I3 | **I**nfo disclosure | `/status` exposes `fps` and `port`. | Same. | Trivial. | Low. | None. | **No issue (Info).** |
| I4 | **I**nfo disclosure | `Console.WriteLine($"CAPTURE_PORT={Port}")` (line 69). | Anyone reading the dev's stdout (CI logs, terminal scrollback, `dotnet watch` mirror, captured logs). | Reveals the port (and confirms preview mode active). | Low (port is already locally enumerable; main risk is *log persistence*). | None. | **F-07 (Low).** Port leaks into any log capture system. |
| D1 | **D**oS | `HandleSwitchComponent` reads the entire request body via `StreamReader.ReadToEnd()` (line 279) with no size cap. | Local attacker, browser tab. | `_ = Task.Run(() => HandleRequest(ctx))` per-connection, unbounded — an attacker can spawn N concurrent POSTs each streaming gigabytes. Each fills a ThreadPool task and a string allocation; OOM / thread-pool starvation. | Medium. | No `request.ContentLength64` cap; no MaxRequestLength; no concurrency cap. | **F-08 (Medium).** Unbounded request body on `/preview`. |
| D2 | **D**oS | Slow-loris on `HttpListener`. | Same. | `HttpListener` accepts unbounded concurrent contexts; each `HandleRequest` is a `Task.Run` that holds a thread until the client closes. | Medium. | None. | **F-09 (Medium).** No per-connection or aggregate concurrency cap. |
| D3 | **D**oS | Frame-capture timer runs every `1000/fps` ms on the UI dispatcher and allocates two `Bitmap`s + a `MemoryStream` of JPEG-encoded pixels each tick, regardless of whether a viewer is connected. | Local attacker; also normal idle behavior. | Constant CPU + GC pressure on the dev's app even when no client is reading. At 10 fps + a 1080p window this is several MB/s of ephemeral allocations. Worsens battery / fan / responsiveness during dev. | High under default config. | None — capture runs unconditionally once `Start()` is called. | **F-10 (Low/Medium).** No "pause when idle" / no on-demand capture. Quality-of-life issue more than a security one, but it's also a free DoS amplification primitive for an attacker who simply sits on the port. |
| D4 | **D**oS | `FindFreePort` (line 315-322) opens a `TcpListener` on port 0, captures the assigned port, closes the listener, then `HttpListener` re-binds. Time-of-check / time-of-use race. | Local racer (any local process). | Another local process can grab the port between Stop() and `_listener.Start()`, causing `HttpListenerException` and aborting the server. Worse: an attacker who wins the race **before** Reactor binds can listen on the same port and impersonate the capture server to the VS Code extension. | Low (small race window) but trivial to script. | None. | **F-11 (Medium).** Port-acquisition race; impersonation risk for the VS Code extension. |
| D5 | **D**oS | Capture errors are caught and logged, with a 1/100-rate dampener, but the `_captureErrorCount` only ever grows; no circuit-breaker / no auto-disable on persistent failure. | Bug / hostile window state. | Continuous error stream every tick. | Low. | Per-100 throttling on the *log line*, not the work. | **F-12 (Info).** No capture-error backoff. |
| E1 | **E**oP | Component switch to a Component whose constructor does something privileged (file write, network call, command exec). | Local caller via `/preview`. | Bounded to whatever the dev's own components do. The framework does not provide a privileged Component type that takes user input from the URL/body; the only attacker-controlled input to `Activator.CreateInstance` is the *type selection*, not constructor args. So this is "trigger any side effect any of your loaded Component constructors does," not arbitrary EoP. | Low–medium. | Type lookup constrained to subclasses of `Microsoft.UI.Reactor.Core.Component` already in the AppDomain; no external assembly load. | **F-13 (Info, but watch).** Pairs with F-04. If a dev defines a Component whose ctor wraps `Process.Start` or similar, it's reachable via `/preview`. |
| R | Repudiation | Switch / focus / frame-fetch events are not logged anywhere. | All. | No audit trail of what a hostile local caller did. | n/a | The MCP devtools server (Chunk 01/02) has a `DevtoolsLogger`; the capture server does not. | **F-14 (Low).** No audit log on the capture surface. |

---

## 6. Findings (severity-tagged)

### F-01 — No authentication on the loopback HTTP server  ·  **High**
**File:** `src/Reactor/Hosting/PreviewCaptureServer.cs:52-53, 153-205`
The server binds `http://localhost:{port}/` with **no token**, **no client-process check**, and **no Windows ACL** (`HttpListener` does not restrict by SID). The port is the only "secret," and it is printed to stdout (line 69). Any process running as any user on the machine — including a browser tab — can hit every endpoint.
**Recommendation:** issue a per-launch random bearer token, write it to a 0600 file in `%LOCALAPPDATA%\reactor-devtools\preview-{pid}.json` (or pass via a private channel to the VS Code extension), require `Authorization: Bearer …` on every request. Reject unauthenticated requests with 401.

### F-02 — `/preview` and `/focus` are CSRF-able  ·  **High**
**File:** `src/Reactor/Hosting/PreviewCaptureServer.cs:158-167, 236-250, 268-311`
The Origin check on lines 158-167 only *reflects* CORS headers — it never blocks the request. `/focus` is a GET so a `<img src="http://localhost:PORT/focus">` from any tab fires it. `/preview` is a POST but a `<form action="http://localhost:PORT/preview" method="POST" enctype="text/plain">` with `name='{"component":"Foo","x":'` `value='"}'` produces a body parsable as JSON and **does not trigger a CORS preflight** (simple request).
**Recommendation:** fail-closed Origin check — reject any request whose `Origin` is missing or not in the allow-list (`vscode-webview://*`, the extension's known origin). For state-changing endpoints (`/preview`, `/focus`), additionally require either the bearer token from F-01 or a non-simple `Content-Type: application/json` (which forces a preflight).

### F-03 — DNS-rebinding allows reading frames  ·  **Medium**
**File:** `src/Reactor/Hosting/PreviewCaptureServer.cs:153-205`
There is no `Host` header check. A page on `attacker.example` whose DNS rebinds to `127.0.0.1` becomes "same-origin" with `localhost:port` and can read `/frame` and `/components` content via fetch.
**Recommendation:** validate `Host: localhost:{port}` or `Host: 127.0.0.1:{port}` on every request; reject otherwise.

### F-04 — `/preview` mounts any in-AppDomain Component by case-insensitive name  ·  **Medium**
**Files:** `src/Reactor/Hosting/PreviewCaptureServer.cs:268-311`, `src/Reactor/Hosting/ReactorApp.cs:369-384, 481-497`
`SwitchComponent` → `FindComponentType(name)` walks every loaded assembly and returns the first non-abstract `Component` subclass whose simple name matches case-insensitively. `Activator.CreateInstance` invokes the parameterless ctor; `host.Mount` runs `Render()`. An attacker who reaches the endpoint (per F-01/F-02) can pick from the dev's full Component graph and trigger any side effects in their constructors / Render methods. Risk is bounded — it cannot load arbitrary types — but it is a non-trivial surface in a sample-rich repository.
**Recommendation:** restrict the switch list to types returned by `GetComponents` (what the extension is actually told about), enforced server-side. Optionally require the type be in the same primary assembly as the entry component.

### F-05 — Continuous screenshot leakage  ·  **High** (when paired with F-01/F-02/F-03)
**File:** `src/Reactor/Hosting/PreviewCaptureServer.cs:84-133, 207-222`
Frames are JPEG-encoded screenshots of the dev's preview window's client area. Any caller reaching `/frame` can exfiltrate everything visible — credentials in test forms, draft messages, customer-demo content, error dialogs containing tokens. Capture runs unconditionally at `Fps` (default 10) once `Start()` is called; there is no consent prompt and no on-screen indicator that capture is active.
**Recommendation:** behind F-01 (auth). In addition: (a) add an unmistakable on-window indicator while preview-mode is running ("Preview mode — VS Code is mirroring this window"); (b) suppress capture when the window is not foreground or when a configured "secret-mode" hook is invoked.

### F-06 — `/components` discloses the dev's full Component graph  ·  **Low**
**File:** `src/Reactor/Hosting/PreviewCaptureServer.cs:252-266`, `ReactorApp.cs:500-508`
Every non-`Microsoft.UI.Reactor.*` Component subclass in every loaded assembly is enumerated by simple name. Internal helper component types are returned. Useful for fingerprinting / targeted prompts.
**Recommendation:** opt-in list (mark exposed components with an attribute), or restrict to the entry assembly.

### F-07 — Capture port leaked to stdout  ·  **Low**
**File:** `src/Reactor/Hosting/PreviewCaptureServer.cs:68-70`
`Console.WriteLine($"CAPTURE_PORT={Port}")` is the extension's port-discovery channel, but it persists in any log scraper / terminal scrollback / CI capture. The port is not a secret if F-01 is fixed, so this becomes informational.
**Recommendation:** acceptable once F-01 is addressed; otherwise switch to a private file channel.

### F-08 — Unbounded request body on `/preview`  ·  **Medium**
**File:** `src/Reactor/Hosting/PreviewCaptureServer.cs:277-286`
```csharp
using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
    body = reader.ReadToEnd();
```
No `ContentLength64` cap and no read-size limit. A single POST of, say, 10 GB body forces the listener thread to allocate the entire string. Combined with F-09 (concurrency cap), this is straightforward OOM.
**Recommendation:** reject `ContentLength64 > 16 KiB` (the legitimate body is `{"component":"Name"}`); also enforce a `Content-Type: application/json` requirement.

### F-09 — No connection / concurrency cap  ·  **Medium**
**File:** `src/Reactor/Hosting/PreviewCaptureServer.cs:137-151`
`_ = Task.Run(() => HandleRequest(ctx))` fires for every accepted context; nothing limits the number of in-flight handlers. Slow-loris keeps threads pinned; `HttpListener` itself does not enforce a per-listener connection cap.
**Recommendation:** wrap accept loop in a `SemaphoreSlim` cap (e.g. 16 concurrent handlers) and add a per-handler cancellation timeout.

### F-10 — Capture timer runs unconditionally  ·  **Low/Medium**
**File:** `src/Reactor/Hosting/PreviewCaptureServer.cs:60-71, 84-133`
The dispatcher timer ticks at `Fps` (10 default) and allocates two GDI Bitmaps + a JPEG `MemoryStream` per tick whether or not anyone is reading. Causes constant GC churn on the dev's UI thread; an attacker holding open a connection without reading does not change this either way (capture is producer-only).
**Recommendation:** keep an "active reader" count; pause `_captureTimer` when zero. Bonus: only re-encode JPEG when the bitmap actually changed (compare hash of header band).

### F-11 — Port-acquisition race in `FindFreePort`  ·  **Medium**
**File:** `src/Reactor/Hosting/PreviewCaptureServer.cs:315-322, 60-63`
```csharp
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();   // <-- TOCTOU window opens here
return port;
// caller then constructs HttpListener and Start()s it on the same port
```
Between `listener.Stop()` and `_listener.Start()` (which is a syscall round-trip later), another local process can claim the port. Best case: server fails to start. Worst case: a hostile local process **wins the race and binds first**, then forwards requests to the real server (or doesn't), impersonating the capture server to the VS Code extension. Combined with the lack of token (F-01), the extension cannot tell.
**Recommendation:** keep the `TcpListener` from `FindFreePort` alive (or use the pattern from `DevtoolsMcpServer`'s port allocator) until `HttpListener` has bound. Better: have `HttpListener` itself bind to port 0 — but `System.Net.HttpListener` doesn't support that, so use Kestrel or a self-managed TCP listener.

### F-12 — No capture-error circuit-breaker  ·  **Info**
**File:** `src/Reactor/Hosting/PreviewCaptureServer.cs:127-133`
`_captureErrorCount` only ever grows; the timer keeps ticking and allocating. After N consecutive failures, the server should auto-disable.
**Recommendation:** stop the timer after, e.g., 100 consecutive errors; emit a single warning.

### F-13 — Component constructor side effects via `/preview`  ·  **Info**
**File:** `src/Reactor/Hosting/ReactorApp.cs:374-379` (closure called from `PreviewCaptureServer.cs:300`)
Pairs with F-04. Document for sample authors that a Component's parameterless ctor + `Render` is reachable via `/preview` whenever preview mode is active. Avoid privileged side-effects in component constructors.

### F-14 — No audit log on the capture surface  ·  **Low**
**File:** entire `PreviewCaptureServer.cs`
Unlike `DevtoolsMcpServer` (Chunk 01/02), this server has no `DevtoolsLogger`. Component switches and focus grabs are silent.
**Recommendation:** log every state-changing call (`/focus`, `/preview`) with timestamp + component name + remote endpoint to the same `DevtoolsLogger`.

### Notes on the Hot Reload service (`HotReloadService.cs`)
- `UpdateApplication` is invoked only by the .NET hot-reload pipeline; it is not reachable from the HTTP surface.
- `ReactorApp.ActiveHost` is read via `Volatile.Read` (`ReactorApp.cs:38-41`); call is null-safe; no unsafe code paths.
- Risk surface is limited to "the .NET hot-reload channel itself" (out-of-scope; trusted dependency).
- **One small concern (Info):** `UpdateApplication` does not check `updatedTypes` and re-renders on *every* metadata update, which under high-frequency dotnet-watch save bursts can cascade reconciler work; not a security finding.

### Notes on `OverlayHostWiring.cs` and `ReconcileHighlightOverlay.cs`
- These are dev-overlay rendering paths (visual chrome). They have no external input surface — they consume `UIElement` references already in the visual tree from the reconciler.
- `ReconcileHighlightOverlay.Show` (line 67) caps live sprites (`MaxLiveSprites = 500`) and per-flush sprites (`MaxSpritesPerFlush = 200`), guarded by `OverlayHostWiring.MaxPendingElements = 200` and 80 ms cooldown — DoS-resistant on this layer.
- `Dispose` paths in both files swallow exceptions (`try { … } catch { }`) which is appropriate for teardown but hides leaks; not a security finding.
- **No external attack surface.** These files reviewed as "in-scope but no STRIDE-relevant findings."

---

## 7. Open questions

1. **Is loopback a trust boundary on a Reactor dev's machine?** This chunk strongly suggests it is *not*, in three ways: any local process can connect (F-01), any browser tab can CSRF state-changing endpoints (F-02), DNS rebinding can read frames (F-03). The chunking doc §2 calls this out as the open question — for **this chunk** the answer is "loopback alone is insufficient; an authentication token plus an Origin/Host check is required."
2. **What is the policy on continuous capture without consent?** The framework offers no on-window indicator while `/frame` is being served. Compare with OS-level screen-share UIs that always indicate capture. (F-05.)
3. **Should `/components` be opt-in?** Currently it leaks every Component subclass in every loaded assembly — including sample helper components. (F-06.)
4. **Should preview mode be allowed in non-dev builds at all?** `--vscode-mode` is a CLI option; nothing in the framework prevents shipping a build that responds to it. Verify (Chunk 04 / Chunk 05) that the supervisor enforces `--vscode-mode` only in `dotnet watch run` paths.

---

## 8. Out-of-scope referrals

| Item | Belongs to |
|---|---|
| The VS Code extension's port-discovery, webview CSP, frame rendering, and `dotnet watch` subprocess launch — i.e. the *client* side of every endpoint reviewed here. | **Chunk 04** (VS Code extension). F-02 / F-05 / F-07 mitigations partly land there. |
| `DevtoolsMcpServer` Origin reflection, lockfile registry, and its own loopback HTTP listener. | **Chunk 01** (Devtools transport & dispatch). The auth-and-Origin pattern recommended for F-01/F-02 should be unified with whatever Chunk 01 settles on. |
| `DevtoolsTools.SwitchComponent` — same closure as `PreviewCaptureServer.SwitchComponent`. The MCP path is gated by Chunk 01's transport but the user-code dispatch is the same. | **Chunk 02** (Devtools tools). F-04 applies symmetrically there. |
| `mur` CLI's choice of whether to enable `--vscode-mode` and how the port flows into the extension. | **Chunk 05** (CLI ↔ devtools client). |
| `Activator.CreateInstance` of arbitrary types via reflection — generic concerns about parameterless-ctor side effects. | **Chunk 22** (Data system; reflection-based EoP). |
