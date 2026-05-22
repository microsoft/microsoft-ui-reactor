# Microsoft.UI.Reactor — Security Threat Model

| | |
|---|---|
| **Document owner** | Chris Anderson (`andersonch@microsoft.com`) |
| **Last updated** | 2026-05-21 |
| **Repository / commit** | `microsoft/microsoft-ui-reactor` @ `main` (`2335e75f` baseline) |
| **Audience** | Microsoft internal security review, incoming contributors, downstream consumers performing their own threat models |
| **Status** | Living document — update on architectural change, new trust boundary, or new external surface |
| **Related** | [`SECURITY.md`](../../SECURITY.md) (vulnerability reporting), [`docs/specs/`](../specs/) (design specs) |

## 1. Purpose and scope

This document is the high-level threat model for the Microsoft.UI.Reactor framework. It is intended as the engineering input to Microsoft's internal security review and the durable artifact future reviewers reference.

**In scope:** the security posture of the framework as built and shipped from this repo — what Microsoft.UI.Reactor (Reactor) *itself* does at runtime, what surfaces it exposes to the operating system and other processes, and the trust assumptions baked into those surfaces.

**Out of scope:**

- The internal correctness of layout (Yoga), markdown parsing (md4c), or chart rendering (D3). These are pure-managed source ports of well-known OSS, audited as application code by the Microsoft toolchain (CodeQL, BinSkim, planned SharpFuzz harness — see [§9](#9-out-of-scope-non-surfaces-with-rationale)). They have no network surface, no privilege transition, and no cross-machine reach; correctness defects are not security threats in the trust model below.
- Security posture of *consuming apps* built on Reactor. Apps inherit Reactor's runtime trust profile but make their own choices about what data to handle, what to persist, what to expose to networks, etc. Each app owns its own threat model.
- Security posture of WinUI / Windows App SDK 2.0.1 (the actual control surface for keyboard, IME, paste, drag-drop, accessibility, font rendering, etc.). Reactor materializes real WinUI controls — fuzzing, IME, and rendering-pipeline security inherits from the Windows App SDK shipping vehicle.

## 2. Executive summary

Reactor is a managed-only C# framework for building WinUI 3 desktop apps. **It has no production network surface, no privilege boundary, no cross-machine reach, no kernel surface, and no secret handling.** It runs inside the consuming app's process at the consuming app's trust level.

The only sockets the framework ever opens are two **opt-in, loopback-only, bearer-token-gated dev-time HTTP servers** (`PreviewCaptureServer`, `DevtoolsMcpServer`) plus a stdio MCP variant. These are off by default in production builds; a consuming app must explicitly pass `--preview` or `--devtools` to expose them, they bind to `127.0.0.1` only, and every request is authenticated by a 256-bit per-launch random token. The dev CLI (`mur`) is developer-invoked.

There is no auto-updater, no telemetry endpoint, no license check, no crypto stored at rest, no key management, and no PKI implementation. `System.Security.Cryptography.RandomNumberGenerator` is used exclusively to generate the per-launch bearer tokens above.

The most attacker-influenced text Reactor parses in-process is CommonMark markdown (`Md4cParser`) and SVG path-data strings (`PathDataParser`). Both run inside the host app's trust level — *not* a formal trust boundary, but practical hardening (memory safety from managed code, in-tree SharpFuzz coverage) is documented in [§7](#7-attack-surfaces).

## 3. What Reactor is, in one paragraph

Reactor is a React-style declarative virtual-element-tree reconciler that targets the Windows App SDK / WinUI 3. The framework projects from a developer's element tree onto real WinUI XAML controls (`TextBlock`, `Button`, `NavigationView`, etc.) — it does not paint its own pixels, does not implement its own input pipeline, and does not bundle its own runtime. Distribution is GitHub-public-NuGet only: one framework package (`Microsoft.UI.Reactor`), one template package, and a "skill kit" zip wrapper around a framework-dependent `mur.exe` developer CLI. Status: **experimental**, MIT-licensed, no committed GA date as of this writing.

## 4. Deployment model

```
Developer machine
+----------------------------------------------------------------+
|                                                                |
|  Consuming app (developer-authored EXE)                        |
|  +----------------------------------------------------------+  |
|  |  Process: user-mode, host's trust level                  |  |
|  |                                                          |  |
|  |  +-----------------+   +-------------------------------+ |  |
|  |  | Reactor.dll     |   | WinUI 3 controls (WindowsApp  | |  |
|  |  | + Analyzers.dll |-->| SDK 2.0.1)                    | |  |
|  |  | + Localization  |   | - keyboard / IME / paste      | |  |
|  |  |   .Generator    |   | - accessibility / TextBox     | |  |
|  |  +-----------------+   | - font/glyph rendering        | |  |
|  |          |             +-------------------------------+ |  |
|  |          |  (--preview / --devtools, opt-in)             |  |
|  |          v                                                |  |
|  |  +----------------------------------------------------+  |  |
|  |  | PreviewCaptureServer  |  DevtoolsMcpServer         |  |  |
|  |  | http://127.0.0.1:N    |  http://127.0.0.1:M  or    |  |  |
|  |  | bearer token          |  stdio  (bearer token)     |  |  |
|  |  +----------------------------------------------------+  |  |
|  |                ^                          ^               |  |
|  +----------------|--------------------------|---------------+  |
|                   | localhost only           | localhost/stdio  |
|                   |                          |                  |
|       +-----------+-------------+   +--------+---------+        |
|       | VS Code "Reactor        |   | mur CLI          |        |
|       | Preview" extension      |   | (dev tool)       |        |
|       | (vscode-webview origin) |   |                  |        |
|       +-------------------------+   +------------------+        |
+----------------------------------------------------------------+
```

Three things to notice:

1. **Reactor.dll runs inside the consuming app's process.** It has no separate process, no service, no daemon.
2. The dev-time HTTP servers and the `mur` CLI are *not* shipped to end users. They are developer-loop tooling; in a production app build the developer simply doesn't pass `--preview` / `--devtools` and the servers never instantiate.
3. There is no arrow leaving the box. **There is no Reactor process that talks to the network or to another machine.**

### 4.1 Artifacts produced

| Artifact | Format | Native code? | Signed? | Notes |
|---|---|---|---|---|
| `Microsoft.UI.Reactor.<v>.nupkg` | NuGet, MSIL only | No | Not yet (tracked separately) | Framework |
| `Microsoft.UI.Reactor.<v>.snupkg` | Symbols | No | n/a | |
| `Microsoft.UI.Reactor.Templates.*.nupkg` | NuGet | No | Not yet | `dotnet new` templates |
| `reactor-skill-kit-<v>.zip` | Zip | No (mur.exe is framework-dependent .NET 10) | Not yet | Contains `bin/{x64,arm64}/mur.exe`, `install-skill-kit.ps1` |

No MSI, no MSIX, no Appx, no Authenticode-signed bundle. **Codesigning of NuGet and `mur.exe` is a known compliance gap** — tracked under the BinSkim/SDL stream, not in this threat model.

### 4.2 What the framework does NOT do

These are facts about the shipped framework. Each was verified during the 2026-05-21 compliance audit.

- **No outbound HTTP / HTTPS / sockets.** Repo-wide grep for `HttpClient`, `WebClient`, `Socket`, `TcpClient`, `UdpClient` in `src/Reactor/` returns only the two loopback `HttpListener` servers in [§7.1](#71-preview-capture-server) and [§7.2](#72-devtools-mcp-server) plus their unit tests.
- **No telemetry endpoint, no update server, no license check, no call-home.** There is no analytics or crash-reporting transmission. (Diagnostics traces use `EventSource` / `TraceEvent`; they emit to ETW locally.)
- **No data-at-rest encryption.** The framework holds no secrets, persists no credentials, manages no keys.
- **No authentication library.** No DRM, no IPsec, no VPN, no TLS termination, no certificate validation logic.
- **No kernel surface.** P/Invoke is limited to stock Windows DLLs (`user32`, `shell32`, etc. — 15 files under `src/Reactor/Hosting/`, all for window/shell interop like `PrintWindow`, `SetForegroundWindow`, jump-list COM). No custom drivers, no native helper binaries, no `Wow64DisableWow64FsRedirection`-style tricks.
- **No process spawning of user-controlled paths.** `mur` invokes `dotnet` and tools from `PATH`; no `ShellExecute` of attacker-controlled URIs.
- **No file-system access outside the host app's cwd / user profile.** No system-wide install. `install-skill-kit.ps1` writes to `~/.claude/skills/reactor` by default and refuses common system/data roots ([§7.3](#73-skill-kit-installer)).

## 5. Assets and what's worth protecting

| Asset | Where it lives | Who can reach it | Threat if compromised |
|---|---|---|---|
| Consuming-app process integrity | Host EXE memory | Same process, same user | Same as any in-process library defect — code execution at host's trust level |
| Dev-time loopback ports (preview / devtools) | Localhost, kernel-owned port table | Same-user processes that can read the lockfile or stdout banner | Read of preview frames; remote-control of the developer's running app window (focus steal, component switch, MCP tool invocation) |
| Per-launch bearer tokens | Process memory + per-user tempdir lockfile + dev stdout | Same-user, same-machine | Same as above — token *is* the dev surface's auth |
| Source-ported OSS (Yoga, md4c, D3) | Repo source, compiled into Reactor.dll | Anyone who can submit a PR | Smuggled defect in a parser — runs at consuming-app trust level |

**There are no cross-user assets, no remote credentials, no Azure resources, and no end-user PII handled by the framework.**

## 6. Trust boundaries

A "trust boundary" here is a place where data or control crosses from one identity / privilege level / process / machine to another. Reactor has exactly **two** trust boundaries in the production runtime, and both are off-by-default.

### 6.1 Boundary A: VS Code extension ↔ PreviewCaptureServer (loopback)

| Side | Identity / privilege |
|---|---|
| Client | Same user, possibly the `vscode-webview://` origin running inside VS Code Electron |
| Server | Same user, in-process inside the developer's app, same trust level |

Crossing requires:

- A connection to `127.0.0.1:<random port>` (Host header must match `127.0.0.1` or `localhost` — DNS rebinding rejected with 421).
- A valid `Authorization: Bearer <token>` header (constant-time comparison against per-launch 256-bit random).
- For state mutations: `POST` only + `Content-Type: application/json` (blocks `<form>`-shaped CSRF).
- Origin (if present) must parse via `Uri` and be one of: `http://127.0.0.1`, `http://localhost`, `https://localhost`, or `vscode-webview://*`.

### 6.2 Boundary B: External MCP client ↔ DevtoolsMcpServer (loopback HTTP or stdio)

| Side | Identity / privilege |
|---|---|
| Client | An AI agent harness (Claude Code, Copilot CLI, etc.) running same-user |
| Server | Same user, in-process inside developer's app |

Same shape as Boundary A: loopback-only HTTP (`http://127.0.0.1:<port>/mcp`) with per-launch 256-bit bearer token, 1 MiB body cap, 16-way dispatch gate, 10 s I/O timeouts. The stdio variant inherits its trust from the parent-process pipe (the parent that spawned `mur` is by definition same-user same-machine and already trusted with the child's stdin/stdout).

**There is no third trust boundary.** No remote endpoint, no privilege transition, no AppContainer crossing, no user-mode ↔ kernel-mode transition. The framework runs entirely at the host app's trust level.

## 7. Attack surfaces

### 7.1 Preview capture server

**File:** `src/Reactor/Hosting/PreviewCaptureServer.cs`
**Activation:** opt-in via `--preview` flag on the consuming app or via `mur preview`.
**Surface:** loopback HTTP listener at `http://127.0.0.1:<random port>`.

**Endpoints:**

- `GET /frame` — returns the latest JPEG-encoded screenshot of the WinUI window (captured via `PrintWindow`).
- `GET /status` — fps/port introspection.
- `GET /components` — list of registered preview components.
- `POST /focus` — brings the developer's app window to foreground.
- `POST /preview` — switches the previewed component by name.

**Mitigations implemented:**

| Threat | Mitigation | Source |
|---|---|---|
| Remote attacker reaches the port | Listener binds to `127.0.0.1` only; loopback is not reachable off-box on default Windows network configurations | `PreviewCaptureServer.cs:75` |
| Same-machine other-user attack | Loopback is per-machine; segregation between user accounts on Windows means another local user could reach it but lacks the bearer token (token never written outside the process owner's stdout/lockfile) | `PreviewCaptureServer.cs:64`, `GenerateToken` |
| Token brute-force | 256-bit random from `RandomNumberGenerator.Fill`; constant-time compare; per-launch (no replay across runs) | `PreviewCaptureServer.cs:82`, `BearerMatches` lines 303–314 |
| DNS rebinding (web-page tricks browser into requesting a `127.0.0.1` URL with attacker-controlled Host) | Host header allow-list enforced before auth: rejects with 421 anything other than `127.0.0.1:<port>` / `localhost:<port>` | `IsAllowedHost` line 316 |
| Cross-origin browser CSRF | (a) Origin allow-list parsed via `Uri` (not `StartsWith`); (b) state-mutating endpoints require POST + `Content-Type: application/json` (not a CORS "simple request") | `IsAllowedOrigin` line 324, `HandleSwitchComponent` line 432 |
| Reflected XSS via `<img src=…/focus>` | `/focus` is POST-only (returns 405 for GET) | `HandleFocus` line 392 |
| Slow-loris / connection exhaustion | `HeaderWait`, `EntityBody`, `IdleConnection`, `RequestQueue` all bounded at 10–15 s | `Start` lines 95–103 |
| Threadpool exhaustion | 16-way dispatch gate; over-quota requests get HTTP 503 with `Retry-After: 1` | `_dispatchGate` line 34, `ListenAsync` lines 203–215 |
| Body-bomb | 4 MiB hard cap; over-cap requests get HTTP 413 before the body is read fully | `MaxBodyBytes` line 38, `HandleSwitchComponent` lines 451–456 |
| Port TOCTOU steal between `FindFreePort` and `HttpListener.Start` | `TcpListener` kept alive across the handoff; placeholder released only after `HttpListener` binds | `AcquireFreePortHolding` line 518, `Start` lines 109–110 |

**Residual risk:**

- **Same-user same-machine compromise.** If another process is already running as the developer's user, it can read the lockfile or stdout and obtain the bearer token. This is consistent with the Windows trust model — that attacker already has read access to the developer's process memory, browser cookies, SSH keys, etc., so this surface adds no meaningful privilege. The dev server is not a security boundary against a co-resident-same-user attacker.
- **Frame capture as side channel.** The capture is a screenshot of the developer's preview window; any other process running as the developer can already screenshot the same window via the same Win32 APIs. No new disclosure.

### 7.2 Devtools MCP server

**File:** `src/Reactor/Hosting/Devtools/DevtoolsMcpServer.cs`
**Activation:** opt-in via `--devtools` on the consuming app or via `mur devtools`. Off in production.
**Surface:** loopback HTTP at `http://127.0.0.1:<port>/mcp` OR stdio.

**Endpoints:** JSON-RPC 2.0 `tools/list` and `tools/call` over `/mcp`. Tools are AI-agent affordances: inspect the running element tree, dispatch synthetic input, request screenshots, etc. (See `src/Reactor/Hosting/Devtools/Tools/` for the inventory.)

**Mitigations:** identical shape to [§7.1](#71-preview-capture-server) — per-launch bearer (line 80), 1 MiB body cap (`MaxRequestBodyBytes` line 55), 16-way dispatch gate (line 43), 10 s I/O timeouts (lines 115–122), loopback bind (line 84). Plus:

- **Single-instance lockfile** per project (`LockfileRegistry.PathFor(projectIdentifier)` — per-user tempdir) prevents two dev sessions racing on the same MCP endpoint and provides the channel by which the bearer token reaches the client (`IsAnotherSessionActive` lines 94–106).
- **Stdio transport** uses raw `Console.OpenStandardInput / Output` so the JSON-RPC framing isn't corrupted by app log writes (lines 145–146). Trust derives from the parent process owning the pipe.

**Residual risk:** same as [§7.1](#71-preview-capture-server) — bearer token is a same-user-same-machine secret. The MCP tools can drive the running app: inspect state, synthesize input, switch components. **An attacker who has the token has full control of the dev app's UI thread.** This is by design — that's what devtools are for — and is no worse than the same attacker already having `OpenProcess(PROCESS_VM_READ)` rights on the dev process they share a user with.

### 7.3 Skill-kit installer

**File:** `tools/install-skill-kit.ps1`
**Activation:** developer-invoked from an extracted release zip. Never auto-runs.

**Behavior:**

- Verifies it was launched from inside the extracted kit (looks for `SKILL.md` sibling).
- Verifies a .NET 10 runtime is installed; warns but doesn't fail if missing.
- Copies kit contents to `~/.claude/skills/reactor` (or user-provided `-Path`).
- Prepends `bin\<arch>` to the **user** PATH (not system PATH).

**Mitigations:**

- **Refuses install into dangerous roots** (`%USERPROFILE%`, `%SystemRoot%`, `Program Files`, Desktop, Documents, Downloads, any path < 12 chars, drive root, the kit's own directory): lines 56–79. This prevents a `Remove-Item -Recurse -Force` of real data via a typo'd `-Path`.
- **No network activity.** No download, no fetch, no upgrade probe. Everything operates on already-extracted local files.
- **No elevation.** Modifies user-scope PATH; no `Start-Process -Verb RunAs`, no scheduled task install, no Start menu / Taskbar / Recommended-Pinned modification.

**Residual risk:** standard PowerShell-script-from-the-internet risk applies — the developer must trust the source they downloaded the kit from. Authenticode signing of the zip would mitigate; tracked separately under the SDL signing workstream.

### 7.4 Markdown rendering

**File:** `src/Reactor/Markdown/MarkdownHtml.cs` (renderer) + `src/Reactor/Markdown/Md4cParser.cs` (parser).
**Activation:** consuming-app developer calls `Markdown(text)` element with attacker-influenced content.

**Mitigations:**

- **Default safe mode:** raw HTML is stripped (`NoHtml` parser flag added automatically unless caller opts in via `HtmlFlags.AllowRawHtml`). `MarkdownHtml.cs:74`.
- **URL scheme allow-list** on `href` / `src`: only `http`, `https`, `mailto` survive; everything else (`javascript:`, `data:`, `vbscript:`, `file:`, `about:` other than `blank`) rewritten to `about:blank`. `MarkdownHtml.cs:35`. Regression test: `tests/Reactor.Tests/Markdown/SanitizeUrlTests.cs`.
- **Managed-memory parser.** Pure-C# port of md4c; the unmanaged-memory exploitation classes that affect the upstream C parser do not apply.

**Residual risk:**

- A logic defect in the URL sanitizer (e.g., scheme-confusion via tab/newline) could let through a dangerous URL. Mitigation: `SanitizeUrlTests` covers the OWASP XSS filter-evasion cheat sheet entries that apply to URL parsing.
- A logic defect in `Md4cParser` could cause infinite loop / OOM on adversarial input. Mitigation today: ~2,200 xUnit tests including 590 Yoga-style fixture cases and the upstream md4c spec tests. Mitigation in flight: a SharpFuzz harness (`tests/Reactor.Fuzz/`) covers `MarkdownHtml.Render` on every PR via the `fuzz-smoke` CI job and is being onboarded for continuous fuzzing.
- Markdown is rendered into the consuming-app's tree; the consuming app decides whether to display attacker-influenced markdown at all. **The threat is the consuming app's choice to render untrusted markdown, not the framework's. Reactor's job is to make the default rendering safe.**

### 7.5 SVG path data parser

**File:** `src/Reactor/Charting/PathDataParser.cs`.
**Activation:** chart authors pass SVG-mini-language path strings.

**Mitigations:** pure managed code; bounds-checked indexing via `Span<char>`; no unsafe memory access. Same SharpFuzz follow-up applies.

**Residual risk:** same as §7.4 — malformed input could provoke an infinite loop or pathological allocation. Not a trust boundary; runs at host trust level.

### 7.6 `mur` CLI

**File:** `src/Reactor.Cli/`.
**Activation:** developer command line.
**Behavior:** spawns the consuming app under `dotnet run` or attaches to a running session via the MCP/Preview endpoints above.

**Mitigations:**

- All inter-process talk is to the **localhost** Preview / MCP servers using the **per-launch token** the child app emitted on stdout.
- No network outbound, no auto-update.
- No SUID/elevation; runs as the invoking developer.

**Residual risk:** if `mur` were ever extended to follow URL-style attach targets (e.g., `mur attach https://...`), the threat model expands to cover network identity. **As of the audit date, no such target exists.** Adding one would require revisiting this document.

## 8. Threat enumeration against the internal security-review intake

The Microsoft-internal security-review intake form gave succinct answers about Reactor's security posture. This section expands each answer with evidence:

| Intake answer | Verified by | Evidence |
|---|---|---|
| "Library code executes in the consuming-domain WinUI/.NET app process, typically unelevated user-mode" | Code review of `ReactorApp.Run` and `ReactorWindow` lifecycle | No `RunAs`, no manifest requesting elevation. Reactor inherits the host process's integrity level. |
| "CLI/debug tooling runs as developer-invoked user-mode process" | Manifest review of `mur.exe`, install script behavior | No elevation, no scheduled-task install. |
| "CLI can talk to app as a debugging tool using MCP protocol over HTTP, domain-validated for localhost, with token auth" | `PreviewCaptureServer.cs`, `DevtoolsMcpServer.cs` | Listener prefix bound to `http://127.0.0.1:<port>/`; Host header allow-list at `IsAllowedHost`; bearer constant-time compare at `BearerMatches`. |
| "No outbound network communication beyond the above" | Repo-wide grep | Only `System.Net.*` usage in `src/Reactor/` is the two `HttpListener` servers above and an `HttpClient` *in CLI Docs subsystem* that targets `127.0.0.1:<port>` of a preview server `mur` itself spawned. |
| "No unsupported or third-party components" | `ThirdPartyNoticeText.txt`, `Directory.Build.props`, `nuget.config` | Five Microsoft.* / MIT NuGet deps; three source-ported MIT/ISC OSS components (Yoga, md4c, D3); no checked-in `.nupkg` binaries; `nuget.config` clears inherited feeds. |
| "No prior threat models / security reviews" | Repo audit | This document is the first formal one. README references a 275-finding internal code review — engineering review, not a threat model. |

## 9. Out-of-scope non-surfaces (with rationale)

Microsoft's internal Security Assessment policy lists six trust-boundary triggers that escalate a service into in-depth review. Reactor matches **none** of them. Each is documented here so future reviewers can verify the scope-down quickly:

| Trigger | Applicable? | Why not |
|---|---|---|
| Azure Host OS Impact | No | No Azure surface; no VM-tenant data handling. |
| Azure Service / Tenant Impact | No | No service operated by Reactor. |
| Azure Infrastructure Impact | No | No infrastructure. |
| Two network endpoints across a trust boundary | No | Only sockets are loopback dev servers; same-user same-machine; bearer-token-gated. No remote endpoint exists. |
| Sandboxed (AppContainer) → non-sandboxed | No | Reactor is a library; runs at whatever integrity level the host EXE has and does not bridge. No COM/IPC bridge to a higher-privilege helper. |
| User-mode → kernel-mode | No | No kernel surface. P/Invoke targets only stock Windows DLLs (`user32`, etc.); no custom driver, no IOCTL surface. |

## 10. Supply chain

| Channel | Risk | Mitigation |
|---|---|---|
| `nuget.org` for direct & transitive deps | Compromise of upstream feed → poisoned dep gets pulled at restore | `nuget.config` clears inherited feeds and pins to `nuget.org` + `local-nupkgs/`. Dependabot enabled (`.github/dependabot.yml`). CI `vulnerable-packages` job runs [`dotnet list package --vulnerable`](https://learn.microsoft.com/dotnet/core/tools/dotnet-list-package) on every PR and fails on High/Critical findings. |
| Source-ported OSS (Yoga / md4c / D3) | Upstream defect ported in by hand without notice | `ThirdPartyNoticeText.txt` lists each; `cgmanifest.json` registers them with Component Governance so upstream advisories surface internally. |
| GitHub repo → NuGet publish | Compromised CI builds malicious package | `.github/workflows/release.yml` builds in `windows-latest` runner; BinSkim (PR #365) and CodeQL provide additional verification before publish. |
| `GitHub.Copilot.SDK 0.1.32` transitive | Ships a third-party native `copilot.exe` (`runtimes/<rid>/native/`) without Control Flow Guard | Known; tracked under the BinSkim follow-up. Not Reactor-authored. The binary is only present in the `mur` CLI publish artifact, not in the framework NuGet. |
| Developer-downloaded skill-kit zip | Tampering between Microsoft and developer | Authenticode signing of the zip is a known compliance gap; tracked under the SDL signing workstream. |

## 11. Known open items / questions for internal security review

1. **Authenticode codesigning** of the NuGet package and `mur.exe` is not yet wired into `release.yml`. Recommendation needed on the signing posture for an experimental public NuGet (Microsoft Trusted Signing? per-release manual signing? defer until first stable?).
2. **`copilot.exe` BinSkim findings** (BA2008, no CFG) are upstream from `GitHub.Copilot.SDK`. Now scoped out of the BinSkim scan via the `runtimes/<rid>/native/` exclude in `release.yml`. Open question: should we also escalate upstream to the Copilot SDK team for a hardened build?
3. **Continuous fuzzing.** A SharpFuzz harness for `MarkdownHtml.Render` / `Md4cParser` and `PathDataParser` is in-tree (`tests/Reactor.Fuzz/`) with a smoke-test CI gate. Onboarding to a continuous-fuzzing service is the planned next step; open question is whether onboarding is required for the *experimental* release or can be deferred to first GA.
4. **Devtools MCP authorization model.** Today: bearer token, full-tool access. Open question is whether the same-user-same-machine threat model below it (single-developer-machine, AI agent driving their own running app) needs anything beyond bearer auth (e.g., per-tool capability tokens) before broader adoption.
5. **Scope-down sign-off.** Confirm that Reactor's runtime profile — no production network surface, no privilege transition — places it outside the in-depth-review trigger criteria and that any future surface change requires this document to be re-reviewed.

## 12. References

- [`README.md`](../../README.md) — high-level framing, experimental status note
- [`AGENTS.md`](../../AGENTS.md) — repo conventions, test tiers, AOT posture
- [`SECURITY.md`](../../SECURITY.md) — Microsoft vulnerability reporting policy
- [`ThirdPartyNoticeText.txt`](../../ThirdPartyNoticeText.txt) — OSS attributions for Yoga / md4c / D3 and NuGet deps
- [`src/Reactor/Hosting/PreviewCaptureServer.cs`](../../src/Reactor/Hosting/PreviewCaptureServer.cs) — dev preview server
- [`src/Reactor/Hosting/Devtools/DevtoolsMcpServer.cs`](../../src/Reactor/Hosting/Devtools/DevtoolsMcpServer.cs) — devtools MCP server
- [`src/Reactor/Markdown/MarkdownHtml.cs`](../../src/Reactor/Markdown/MarkdownHtml.cs) — Markdown URL sanitizer + safe-mode renderer
- [`tools/install-skill-kit.ps1`](../../tools/install-skill-kit.ps1) — installer with guard-rails
- [`tests/Reactor.Tests/Markdown/SanitizeUrlTests.cs`](../../tests/Reactor.Tests/Markdown/SanitizeUrlTests.cs) — URL-sanitizer regression tests
- [`tests/Reactor.Fuzz/`](../../tests/Reactor.Fuzz/) — SharpFuzz harnesses for `MarkdownHtml.Render` and `PathDataParser.ParseTokens`

## 13. Change log

| Date | Author | Change |
|---|---|---|
| 2026-05-21 | Chris Anderson | Initial draft for internal security-review intake. Captures runtime baseline at commit `2335e75f`. |
