# Chunk 04 — VS Code extension: threat model

**Status:** Phase 2 review, complete
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Reviewer date:** 2026-04-30
**Companion:** `000-chunking-and-threat-model.md` (sections 2, 3, and Chunk 04 in section 4)

---

## 1. Scope

The VS Code extension lives entirely in one TypeScript file plus a manifest:

| File | Lines | Notes |
|---|---:|---|
| `src/vscode-reactor/src/extension.ts` | 779 | All extension logic: command registration, child-process spawn, HTTP polling, webview HTML generation, regex-based C# component detection. |
| `src/vscode-reactor/package.json` | 44 | Manifest. Declares four commands (`reactor.preview`, `reactor.previewConnect`, `reactor.previewStop`, `reactor.previewFocus`). `activationEvents: []` (commands implicitly activate). |
| `src/vscode-reactor/tsconfig.json` | — | Build config, not in trust path. |
| `src/vscode-reactor/.vscode/launch.json` | — | Developer-only debug config; not shipped. |

The chunk is reviewed end-to-end. Total review-relevant code: ~780 LOC.

There are no unit tests, no `eval` calls, no dynamic `Function` constructors, and one `innerHTML` write in the webview script (line 592, analyzed below).

---

## 2. Data-flow diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          UNTRUSTED WORKSPACE                             │
│  - .cs files (regex-parsed for `: Component`)                            │
│  - .csproj files (path discovered by ancestor walk)                      │
│  - workspace root directory (becomes child-process cwd)                  │
└─────────────────────────────────────────────────────────────────────────┘
                                │
                                ▼ findAllComponentClasses (regex)
                                ▼ findCsprojFor (ancestor walk, fs.readdir)
                                │
┌─────────────────────────────────────────────────────────────────────────┐
│                      EXTENSION HOST (Node.js)                            │
│                                                                          │
│  startAutoPreview ─► launchPreviewProcess                                │
│                       │                                                  │
│                       ▼  cp.spawn("dotnet",                              │
│                                    ["watch", "run", "--project",        │
│                                     csprojPath, "--",                    │
│                                     "--devtools", "run", "--vscode"],    │
│                                    { cwd: workspaceRoot, ... })          │
│                       │                                                  │
│                       ▼ stdout sniff: /CAPTURE_PORT=(\d+)/                │
│                       ▼                                                  │
│                  http.get  /components                                   │
│                  http.post /preview {component}                          │
│                  http.post /focus                                        │
│                  http.get  /status   (webview-side)                      │
│                  http.get  /frame    (webview-side, JPEG)                │
└─────────────────────────────────────────────────────────────────────────┘
                                │                       ▲
                                ▼ webview.html =        │ postMessage
                                │   getWebviewHtml(...) │ (selectComponent)
                                ▼                       │
┌─────────────────────────────────────────────────────────────────────────┐
│                  WEBVIEW (Chromium iframe, scripts ON)                   │
│  - <img id="preview"> bound to fetch(localhost:PORT/frame).blob()        │
│  - JSON status polling                                                   │
│  - <select> dropdown ─► postMessage to extension ─► HTTP POST /preview   │
│  - innerHTML write on `updateComponents` message (handler exists; not    │
│    currently posted from the extension, but reachable via re-host)       │
└─────────────────────────────────────────────────────────────────────────┘
                                │
                                ▼ http on 127.0.0.1:PORT (loopback)
┌─────────────────────────────────────────────────────────────────────────┐
│           PreviewCaptureServer (Chunk 03, Reactor host process)          │
│           — out of scope for this chunk; responses are UNTRUSTED here    │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Trust boundaries crossed

| # | Boundary | Direction | Assumption |
|---|---|---|---|
| **B1** | Workspace contents → extension | inbound | Untrusted. Opening a malicious repo is an explicit threat (per master doc §2). `.cs` text, `.csproj` filenames, and the workspace root path can all be attacker-controlled. |
| **B2** | Extension → child process (`dotnet`) | outbound (effects) | The extension materializes argv and a `cwd`. Both are partially attacker-controlled. PATH lookup happens in the child's address space. |
| **B3** | Loopback HTTP server (Chunk 03) → extension | inbound | Loopback responses are *not* the same trust as our own code: any local process can bind a port, and the user-typed port path (`previewConnect`) lets the user point at an arbitrary local listener. Treat responses as untrusted bytes. |
| **B4** | Extension → webview HTML | outbound | Webview gets `enableScripts: true`. Anything we interpolate into the HTML or `postMessage` to the webview is in a script-execution context. |
| **B5** | Webview → extension (`onDidReceiveMessage`) | inbound | Webview content can in principle be influenced by anything that reaches the webview's DOM (including HTTP-server responses fetched from inside the webview). Messages from the webview must be treated as untrusted. |
| **B6** | User → port input (`previewConnect`) | inbound | User-supplied integer; validated for range only. |

---

## 4. Asset inventory

| Asset | Why it matters |
|---|---|
| **Code execution as the developer** | The extension spawns a child process. Anything that influences argv, cwd, or executable lookup yields RCE in the developer's user context. This is the highest-value asset by far. |
| **Webview script-execution context** | `enableScripts: true` plus no CSP means anything that can inject HTML/JS into the webview can run script. Reactor webviews are sandboxed (no Node access), but they can still issue arbitrary `fetch()` to loopback ports and `postMessage` back to the extension (which calls HTTP `/preview` POST). |
| **Output channel content** | Contains workspace paths, project names, component names, child-process stdout/stderr. Local-only, but ends up in user-shared bug reports. |
| **The "is the preview running" assumption** | If a hostile process can bind the port the extension expects, it gets to drive the webview's display and feed it crafted JSON. |
| **Active editor file paths** | Sent to Output channel and used to resolve `.csproj`. Paths leak into the channel. |

---

## 5. STRIDE table

| # | STRIDE | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding |
|---|---|---|---|---|---|---|---|
| T1 | **EoP** | Hostile workspace contains `dotnet.exe` (or `dotnet.bat`/`dotnet.cmd`) at the workspace root. `cp.spawn("dotnet", …, { cwd: workspaceRoot })` (line 187) on Windows resolves bare program names through `CreateProcessW`'s search order, which **includes the current directory before PATH**. | Open a malicious repo. | RCE as the developer. | **High** — single file in repo, no user interaction needed beyond invoking the preview command. | None. The executable name is bare (`"dotnet"`) and cwd is workspace-controlled. | **F1 (Critical).** |
| T2 | **EoP** | Argument injection through `csprojPath` or the workspace root. | Hostile workspace, e.g. weird filenames containing spaces, quotes, leading dashes (`--config`). | RCE / unintended `dotnet` flag (`--no-restore`, `--configuration`). | Low for RCE (Node `spawn` without `shell:true` passes argv directly to `CreateProcess`/`execve`, no shell parsing). Medium for unintended-flag injection if a `.csproj` is named `--something.csproj`. | argv array form is used (line 187), not a shell string. `--` separator (line 249) prevents the trailing flags from being consumed by `dotnet watch`. | **F2 (Low).** |
| T3 | **EoP** | Subprocess inherits the developer's full environment (no `env` override on `cp.spawn` at line 187). Hostile workspace can set `DOTNET_*` or `NUGET_*` via `.env`-style developer tooling that other extensions populate. | Hostile workspace with helper `.env`, *and* an extension that injects env vars. | Indirect, depends on other extensions. | Low. | None. | Document expectation; not a finding here. |
| T4 | **Tampering** | Webview has no Content-Security-Policy `<meta>` and `enableScripts` is true (line 411). Default webview CSP is permissive — inline script, `connect-src` to any HTTP, etc. | Anything reaching the webview HTML or DOM. Combined with T5/T7. | Webview script execution → drive `postMessage` → cause extension to POST to `/preview` with arbitrary `component` strings. | High that CSP is missing; impact bounded by what postMessage handler does. | None. The webview ships with no `<meta http-equiv="Content-Security-Policy">` and no nonce. | **F3 (High).** |
| T5 | **Tampering / XSS-in-webview** | `componentSelect.innerHTML = msg.components.map(c => '<option value="' + c + '"...)` (line 592–594) concatenates `c` into HTML without escaping. The handler runs on `updateComponents` messages from the extension. | Currently the extension never posts `updateComponents`, but `currentComponents` is sourced from the loopback HTTP `/components` response (line 273) which is untrusted per the trust model. If `updateComponents` is ever wired up — or if a future change reuses the webview-side helper — a hostile loopback responder injects script into the webview. | Webview script execution. | Latent now (handler exists but not posted). | The initial-render path *does* escape (line 449); only the dynamic-update path is unsafe. | **F4 (Medium).** Remove the dead handler or escape inside it; do not ship dead code that will be cargo-culted next time. |
| T6 | **Tampering** | Loopback HTTP responses parsed via `JSON.parse` (lines 722, 753) without size cap or schema. A hostile responder can return huge bodies, malformed JSON, or unexpected shapes. | Anything that can bind the loopback port faster than Reactor — including a hostile local process — *or* a user pointed to a wrong port via `previewConnect`. | DoS (unbounded body; the body string concatenation has no cap), or downstream confusion (`data.components` undefined → render crash). | Low for direct exploitation; mostly availability. | 5-second `setTimeout` (lines 729, 761). No size cap, no `Content-Length` enforcement, no shape validation. | **F5 (Low).** |
| T7 | **Tampering** | Webview's `fetch('http://localhost:' + PORT + '/frame')` (line 570) — the resolution of `localhost` is subject to whatever the user's `hosts` file or DNS says. DNS-rebinding from inside a Chromium webview against `localhost` is largely blocked by Chrome's Private Network Access rules, but a `hosts` entry pointing `localhost` elsewhere bypasses that. | Local attacker who can edit `hosts` (already an admin path) — low value. Browser-tab attacker — N/A inside webview. | Low. | Low. | None — `127.0.0.1` would be slightly stronger than `localhost`. | **F6 (Info).** |
| T8 | **Info disclosure** | Output channel emits absolute file paths (lines 110, 118, 184, 222), child-process stdout (line 199) and stderr (line 231), and a "telemetry" event name (line 257). Output channels are local-only by default but routinely pasted into bug reports. | Hostile bug-report request, or a developer accidentally sharing the channel. | Username/path disclosure, project names, possibly secrets in stdout if `dotnet watch` echoes them. | Medium. | None — there is no redaction. | **F7 (Low).** |
| T9 | **Info disclosure** | "Telemetry" event names (`reactor_preview_launch`, `reactor_devtools_launch`) are written *only* to the local output channel today (line 257) — comment claims an "upstream harness" reads it. No network egress. | N/A today. | None today; future risk if a real telemetry transport is added without redaction. | Low. | No PII in the event name itself. | **F8 (Info).** Document that this stub is local-only; future wiring must redact paths. |
| T10 | **DoS** | ReDoS on the C# component-class regex (line 130): `/class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*Component(?:<[^>]*>)?\b/g`. | Hostile `.cs` file in workspace. | Extension-host hang. | Low — analyzed below. The two `\s*` runs around `:` are not adjacent to overlapping quantifiers, `[^>]*` is bounded by `>`, and `\w+` is atomic-equivalent. No nested-quantifier construct triggers catastrophic backtracking. | Pattern is benign as written. | **F9 (Info).** Add a length cap or `String.prototype.matchAll` with timeout-by-byte as defence in depth. |
| T11 | **DoS** | `findCsprojFor` (line 143) walks every ancestor of the active file with `fs.promises.readdir`. A pathological deep tree on a slow filesystem stalls the command. | Hostile workspace. | Local hang. | Low. | None. The walk terminates at the filesystem root. | **F10 (Info).** Cap walk depth; the C# project root is rarely > 10 levels above a source file. |
| T12 | **DoS** | The webview's `refreshFrame` (line 602) re-arms via `setTimeout(..., 100)` even on success, no rate-limit, and creates a `URL.createObjectURL` per frame. `revokeObjectURL` only fires on `img.onload`, which may not fire if the img element is replaced quickly. | Server-side, but reachable if an attacker keeps the loopback port responsive. | Webview memory leak, eventual webview process kill. | Low. | `onload` handler revokes the URL (line 613). Race exists but the leak is bounded by the rate of new frames. | **F11 (Info).** |
| T13 | **Spoofing** | `previewConnect` (line 361) takes a user-typed port and just `fetch`es `/status`. No proof the listener is actually a Reactor instance. A hostile local process can bind the port and pretend. | User error / hostile local user. | Webview displays garbage; extension may POST `/preview` with attacker-controlled component names; attacker can drive the webview by serving a 200 to `/status`. | Low. | None beyond the `/status` JSON-parse. | **F12 (Low).** Validate response shape (`/status` should include a server-identifier field) before accepting. Coordinate with Chunk 03. |
| T14 | **Tampering / EoP** | `panel.webview.onDidReceiveMessage` (line 420) accepts `selectComponent` messages and POSTs `msg.name` directly into the loopback `/preview` endpoint with no validation. | Webview script (T4/T5) → extension → loopback POST. | The component-name string is sent verbatim to the Reactor process; impact depends on Chunk 03's handling. Not RCE here, but a path through the trust boundary that Chunk 03 must defend at. | Low directly; couples to Chunks 03/05. | None. | **F13 (Info).** Validate `msg.name` against the known `currentComponents` list before forwarding; do not relay arbitrary strings. |
| T15 | **EoP** | `cp.execFileSync("taskkill", ["/T","/F","/PID", pid.toString()], { stdio: "ignore" })` (line 337) on Windows. argv form, `pid` is a number from Node — safe. Falls back to `proc.kill()` on failure. | N/A. | None. | None. | argv form is used. | No finding. |
| T16 | **Tampering** | The `[preview]`-prefix sniff (line 202) inspects child-process stdout and, on a match, kills + relaunches with `--preview --vscode` legacy args. Stdout is emitted by the same child we spawned, so it's trusted in the spawn-once-per-fork model — but an attacker who can write to the child's stdout (e.g. via a hostile MSBuild target) flips the extension into a different command-line. | Hostile `.csproj` with a custom Target that writes `[preview]` to stdout. | Forces relaunch with legacy args. Functionally equivalent today; future asymmetry between the two paths could be exploited. | Low. | None. | **F14 (Info).** Sniff a more uniquely-prefixed marker emitted only from the Reactor host's preview entrypoint, e.g. `[reactor-preview-protocol-v1]`. |

---

## 6. Findings

### F1 — `cp.spawn("dotnet", …, { cwd: workspaceRoot })` is exploitable on Windows
**File:** `src/vscode-reactor/src/extension.ts:187` (and the `cwd` choice at line 174–176)
**Severity:** **Critical**

```ts
const workspaceRoot =
  vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ??
  path.dirname(csprojPath);
…
previewProcess = cp.spawn("dotnet", args, {
  cwd: workspaceRoot,
  stdio: ["ignore", "pipe", "pipe"],
});
```

`cp.spawn` with a bare program name and no `shell: true` calls Node's `uv_spawn`, which on Windows ultimately calls `CreateProcessW`. `CreateProcessW`'s search order for an unqualified program name **includes the current working directory before `%PATH%`** (after the calling-process directory and the system32 dir, but ahead of user PATH). Since `cwd` here is the (possibly hostile) workspace root, dropping a file named `dotnet.exe` (or `dotnet.bat`/`dotnet.cmd`, see CVE-2024-27980 for the `.bat` shell-fallback variant) into a malicious repo is sufficient to execute arbitrary code as soon as the developer opens the repo and runs **Reactor: Preview Component**.

This is the highest-impact finding in the chunk. It needs at least one of:

1. Resolve `dotnet` to an absolute path before spawn (look it up in `PATH` first via VS Code's `which`/`vscode.workspace.getConfiguration`, refusing to run if the resolved path lives inside the workspace).
2. Set `cwd` to something the workspace doesn't control (e.g. `path.dirname(csprojPath)` is *also* workspace-controlled — it must be something outside).
3. On Windows, set `process.env.NoDefaultCurrentDirectoryInExePath = "1"` for the spawn (or pass `env` accordingly), which suppresses CWD from the search order.

Recommend (1) + (3) together. (2) is hard because the project must build inside the repo.

### F2 — argv injection via project filenames (Low)
**File:** `extension.ts:248–251`

`buildDevtoolsArgs` interpolates `csprojPath` directly:

```ts
const tail = legacy ? ["--preview", "--vscode"] : ["--devtools", "run", "--vscode"];
return ["watch", "run", "--project", csprojPath, "--", ...tail];
```

`spawn` without `shell: true` does not invoke a shell, so quoting metacharacters do not yield RCE. But a `.csproj` named `--something.csproj` will appear *after* `--project` and is consumed as the project value (safe), whereas `extension.ts:149` (`entries.find((e) => e.endsWith(".csproj"))`) returns the first matching entry, so a maliciously named file like `-c.csproj` cannot affect dotnet's parsing because it follows `--project`. **Recommend** anyway: validate `csprojPath` is an absolute path under the workspace root and does not start with `-`, defence in depth.

### F3 — Webview ships no Content-Security-Policy
**File:** `extension.ts:458–665` (entire `getWebviewHtml`)
**Severity:** **High**

The HTML in `getWebviewHtml` has no `<meta http-equiv="Content-Security-Policy">`. The `createWebviewPanel` options (line 410–413) set `enableScripts: true` but do not constrain `localResourceRoots` (defaults to the workspace + extension dir, which exposes the workspace via `vscode-resource:`). There is no nonce, no `script-src`, no `connect-src`.

Recommend a strict CSP, e.g.:

```html
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none';
               img-src http://127.0.0.1:* blob:;
               connect-src http://127.0.0.1:*;
               style-src ${webview.cspSource} 'unsafe-inline';
               script-src 'nonce-${nonce}';">
```

…and a per-render `nonce` on the inline `<script>`. Pin the host to `127.0.0.1` rather than `localhost` (T7). Set `localResourceRoots: []` since the webview only fetches over HTTP. This dramatically narrows what F4 / a future XSS could do.

### F4 — Webview-side `innerHTML` write trusts loopback HTTP shape (Medium)
**File:** `extension.ts:591–595`

```js
if (msg.type === 'updateComponents' && componentSelect) {
  componentSelect.innerHTML = msg.components
    .map(c => '<option value="' + c + '"' + (c === msg.selected ? ' selected' : '') + '>' + c + '</option>')
    .join('');
}
```

`c` is concatenated into HTML without escaping. The extension currently does not post `updateComponents` (only `updateSelection` at line 307–310), so this is dead code. It will be wired up the next time someone adds dynamic component-list updates, and at that point `currentComponents` (sourced from the loopback `/components` response at line 273, which is untrusted per §3 B3) becomes a script-injection sink. Either delete the handler now or replace with `textContent`-based DOM construction.

### F5 — Unbounded HTTP body parsing in extension host (Low)
**File:** `extension.ts:710–768`

Both `httpGetJson` (line 718–720) and `httpPostJson` (line 749–751) accumulate bytes into a JS string with no cap. A hostile loopback responder (or a misdirected user-typed port) can stream arbitrary bytes until the 5-second timeout fires, allocating up to ~5s × bandwidth in V8 heap. Cap response bodies to e.g. 64 KiB (more than enough for `/components` and `/status`), enforce `Content-Type: application/json`, and reject non-JSON before parse.

### F6 — Webview hard-codes `localhost` rather than `127.0.0.1` (Info)
**File:** `extension.ts:570–572` (and `extension.ts:271, 295, 376, 692` on the extension-host side)

`localhost` resolves through whatever the OS does. The threat is small inside a webview (Chromium's PNA mostly handles it), but `127.0.0.1` is unambiguous and avoids `hosts`-file games. Pin loopback URLs to `127.0.0.1`.

### F7 — Output-channel disclosure of paths and child stdout (Low)
**File:** `extension.ts:110, 118, 184, 199, 222, 231, 281, 313, 322`

Workspace-absolute paths and the child process's full stdout/stderr are written to the **Reactor Preview** output channel verbatim. `dotnet watch` output can include exception stack traces with secret values from the running app. There is no redaction tier and no opt-out. Lower the default verbosity (paths → basenames; route raw child stdout to a separate, opt-in trace channel).

### F8 — `logTelemetry` is local-only today; document so it doesn't grow legs (Info)
**File:** `extension.ts:253–258`

The function only writes to the output channel. The comment says "Telemetry transport not wired here; the extension's upstream harness reads the output channel in dev." If a real transport is added later, the event names and any associated payload must be redaction-reviewed at that point. Add a comment marker (e.g. `// TODO(security):`) so a future PR doesn't quietly bolt on egress.

### F9 — Component-class regex is benign but not capped (Info)
**File:** `extension.ts:128–141`

```ts
const pattern = /class\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*Component(?:<[^>]*>)?\b/g;
```

I traced the pattern: `\w+` is greedy but each character class is disjoint from the surrounding `\s` and `<>`, the two `(?:<[^>]*>)?` groups are bounded by `>`, and the two `\s*` runs around `:` cannot interact (different-class anchor `:` between them). No catastrophic-backtracking pair (`(a*)*`, `(a|a)+`, etc.) appears. Stress-test sample: `("class " + "X".repeat(N)).repeat(K)` runs in O(N·K). **Defence in depth:** add a 1 MiB cap on the input passed to this regex (`text.slice(0, 1<<20)`) so a multi-megabyte hostile `.cs` file can't tie up the extension host even if a future edit introduces a backtracking pair.

### F10 — `findCsprojFor` walks to filesystem root unconditionally (Info)
**File:** `extension.ts:143–157`

Cap depth at 16 ancestors. The current loop always walks to `path.parse(dir).root`, calling `fs.readdir` on every parent. This is mostly a perf concern but pathological symlink targets in a parent dir can stall the walk.

### F11 — `URL.createObjectURL` reuse race in webview (Info)
**File:** `extension.ts:613–614`

`URL.revokeObjectURL` is bound to `img.onload`. If `img.src` is reassigned before the previous load fires, the previous blob URL leaks. The leak is bounded by frame rate × time; not a security issue, but worth fixing while you're here (use `img.decode().finally(revoke)`, or pre-generate the next URL and revoke on the *previous* swap).

### F12 — `previewConnect` does not authenticate the listener (Low)
**File:** `extension.ts:361–391`

After validating port range, the extension only verifies `/status` returns valid JSON. Any local process holding the port satisfies that. Coordinate with Chunk 03 to add a `serverId` (or a per-session token shown in the UI) and validate it here before binding `capturePort`.

### F13 — Webview-relayed component names are forwarded unchecked to loopback (Info)
**File:** `extension.ts:420–427`

`msg.name` from the webview is forwarded into `switchComponentViaHttp(msg.name)` (line 425), which JSON-encodes and POSTs it to `/preview`. Validate `msg.name` is one of `currentComponents` (or a reasonable component-name shape — `/^[A-Za-z_][A-Za-z0-9_]{0,127}$/`) before forwarding. Defence in depth for Chunk 03.

### F14 — Stdout-prefix sniff for legacy-arg fallback is a weak signal (Info)
**File:** `extension.ts:201–215`

A hostile MSBuild target (or any code that runs early in the user's program) can write `[preview]` to stdout to flip the extension into the legacy command line. The two paths happen to be equivalent today, but this is a stable foothold for a future asymmetry. Replace the sniff with a structured marker (e.g. `[reactor-host] protocol=devtools`) that only the Reactor host emits.

---

## 7. Open questions

1. **F1 — does VS Code's spawn helper de-Windows-CWD by default?** Some VS Code extensions use `vscode.tasks` instead of `cp.spawn`, which goes through the integrated terminal. Investigate whether routing through `vscode.tasks` (or `vscode.window.createTerminal({ shellPath: "dotnet" })`) inherits the same CWD-search bug. Spike before recommending a fix.
2. **F1 fix — `NoDefaultCurrentDirectoryInExePath` on Windows.** Confirm Node.js `cp.spawn`'s `env` propagates to `CreateProcessW`. (It does in libuv as of Node 20, but verify on the supported VS Code Node version.)
3. **CSP localResourceRoots.** Webview default `localResourceRoots` includes the workspace. Even though we never load `vscode-resource://` URIs in the current HTML, a future change could. Set explicitly to `[]` while the webview only consumes loopback HTTP.
4. **`previewConnect` UX.** Should the user-typed-port path even exist? It's a foot-gun (T13) and I am not sure it's used in practice. If the auto-launch flow always knows the port via stdout sniffing, retiring `previewConnect` removes one trust-boundary.
5. **DNS rebinding.** Chrome's Private Network Access blocks public→private fetches but does not block within-private fetches initiated from a webview. If the webview is ever loaded with a non-loopback URL (e.g. devtools relay through some other server), reassess.

---

## 8. Out-of-scope referrals

- **Chunk 03 (preview capture server):** every "loopback responder is untrusted" finding here (F4, F5, F12, F13) is bounded by what the server actually emits and accepts. Validate token-or-handshake design on the server side; the extension's response-shape validation should match.
- **Chunk 01 (devtools transport):** same loopback-trust model applies. F12 ("how do you know the listener is yours?") is the same question raised in Chunk 01 about lockfile→PID validation. Solve once.
- **Chunk 05 (CLI ↔ devtools client):** the CLI also spawns `dotnet` as a child process from a workspace. Re-check **F1** (CWD-in-PATH search on Windows) in Chunk 05 — same root cause, different file.
- **Supply-chain (selfhost):** the extension declares `@types/vscode` and `@types/node` as devDependencies only, ships compiled JS from `out/`, and has no runtime npm dependencies. No third-party runtime code crosses our trust boundary inside the extension. Out of scope here; flag for the release-pipeline review.
