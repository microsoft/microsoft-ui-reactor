# Chunk 02 — Devtools Tools (Handlers): Threat Model

**Phase:** 2 — STRIDE + code review
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Companion docs:** `000-chunking-and-threat-model.md` (trust model), `001-…` (Chunk 01 — transport & dispatch).

---

## 1. Scope

| File | Lines |
|---|--:|
| `src/Reactor/Hosting/Devtools/DevtoolsTools.cs` | 293 |
| `src/Reactor/Hosting/Devtools/DevtoolsFireTool.cs` | 216 |
| `src/Reactor/Hosting/Devtools/DevtoolsLogsTool.cs` | 105 |
| `src/Reactor/Hosting/Devtools/DevtoolsStateTool.cs` | 167 |
| `src/Reactor/Hosting/Devtools/DevtoolsPropertyTools.cs` | 686 |
| `src/Reactor/Hosting/Devtools/DevtoolsUiaTools.cs` | 900 |
| `src/Reactor/Hosting/Devtools/DevtoolsMenuFactory.cs` | 98 |
| `src/Reactor/Hosting/Devtools/McpToolRegistry.cs` | 68 |
| `src/Reactor/Hosting/Devtools/NodeRegistry.cs` | 146 |
| `src/Reactor/Hosting/Devtools/NodeIdBuilder.cs` | 68 |
| `src/Reactor/Hosting/Devtools/TreeWalker.cs` | 444 |
| `src/Reactor/Hosting/Devtools/SelectorParser.cs` | 92 |
| `src/Reactor/Hosting/Devtools/SelectorResolver.cs` | 264 |
| `src/Reactor/Hosting/Devtools/ScreenshotCapture.cs` | 122 |
| `src/Reactor/Hosting/Devtools/LogCaptureBuffer.cs` | 185 |
| `src/Reactor/Hosting/Devtools/LogCaptureInstall.cs` | 220 |
| `src/Reactor/Hosting/Devtools/DevtoolsLogger.cs` | 176 |
| **Total** | **4,250** |

Out of scope but referenced: `McpDispatcher.cs`, `DevtoolsMcpServer.cs` (Chunk 01), `WindowRegistry.cs` (Chunk 01).

---

## 2. Data-flow diagram

```
                +---------------------------------------+
   JSON-RPC --> | McpDispatcher.Invoke (Chunk 01)       |
   tools/call   | - looks up handler in McpToolRegistry |
                | - reads `selector` for logging        |
                | - times call, may emit ETW            |
                +---------------------------------------+
                              |
                              v
        +-----------------------------------------+
        | tool handler (this chunk)               |
        |  - reads JsonElement params             |
        |  - calls server.OnDispatcher(...) to    |
        |    cross to UI thread (5 s timeout)     |
        +-----------------------------------------+
            |             |              |              |              |
            v             v              v              v              v
   SelectorResolver  TreeWalker    Reflection on    PrintWindow   LogCaptureBuffer
   /SelectorParser   + NodeRegistry Component/DP   (GDI bitmap)  (in-proc ring,
                                   fields                          ~4 MB)
            |             |              |              |              |
            +-------------+--------------+--------------+--------------+
                                         |
                                         v
                           +-------------------------------+
                           | result object → JSON (camelCase)
                           | back through McpDispatcher    |
                           | DevtoolsLogger.LogCall writes |
                           |   one TSV line to             |
                           |   %LOCALAPPDATA%\Reactor\     |
                           |   devtools\<pid>.log          |
                           |   (10 MB rotate, keep 5)      |
                           +-------------------------------+
```

**Persistence:**
- `%LOCALAPPDATA%/Reactor/devtools/<pid>.log` (Windows) / `$XDG_STATE_HOME/reactor/devtools/<pid>.log` — `DevtoolsLogger.cs:43-66`. ACL: default user-profile DACL. Mode `FileMode.Create`, share `FileShare.Read`.
- Log capture buffer: in-memory only (`LogCaptureBuffer.cs`).
- Screenshot bytes: ephemeral; only handed to the wire as base64.

**External inputs the handlers consume:**
- `selector` strings (free-form, `SelectorParser.Parse`, `SelectorParser.cs:42`).
- `name`, `value`, `text`, `key`, `filter`, `component`, `event`, `args[]` JSON values.
- `windowId`, `since`, `tail`, `waitMs`, `timeoutMs` numerics.
- For `tools/call` containing free-form `args` arrays passed to reflection-invoked component methods (`DevtoolsFireTool.cs:194-205`).

**Sensitive outputs:**
- Window screenshot PNG (`ScreenshotCapture.CaptureWindow`).
- Captured stdout/stderr/Debug/Trace (`LogCaptureBuffer`).
- Hook values shapes ($type, $shape) for the root component (`DevtoolsStateTool.ShapeValue`).
- Every dependency property + style + resource + visual subtree.

---

## 3. Trust boundaries crossed

| Boundary | Assumption | Holds? |
|---|---|---|
| Loopback HTTP / stdio (Chunk 01) → tool handlers | Caller is the local developer's tooling. **No authentication, no origin token.** | **Challenged.** Any local process — including a hostile browser tab using DNS-rebinding to `127.0.0.1` and any local user account — can reach `tools/call`. The whole tool surface inherits this. |
| Tool handler ↔ UI dispatcher | `OnDispatcher` (`DevtoolsMcpServer.cs:302`) marshals work, 5 s timeout. | Holds for liveness, but means HTTP workers block on UI thread; combined with no concurrency cap (Chunk 01) gives a DoS handle. |
| Reflection over user `Component` types | `fire` reaches public + non-public methods on the root component (`DevtoolsFireTool.cs:165`). Allow-list is method *names* via `ForbiddenMethods` (`DevtoolsFireTool.cs:177-192`). | **Partial.** The forbidden-set is a name-based denylist on lifecycle / hook helpers; it does not gate access to private business-logic methods, internal helpers, security-sensitive APIs, or P/Invoke wrappers a developer authored on the component. |
| WinUI dependency-property surface | `setProperty` and `setResource` parse strings and write any DP a developer's elements expose. | Holds the *type* boundary but not the semantic one — see findings below. |
| Process boundary (other windows / UAC / Win32) | `PrintWindow` only captures the Reactor process's HWND. | Holds: `WindowNative.GetWindowHandle(window)` (`ScreenshotCapture.cs:20`) returns the in-process window. Capture of other apps' HWNDs is not exposed. UIA peers operate on in-process automation peers, not cross-process UIA, so `click`/`type` synthesize input only against in-process elements. |
| FileSystem (`%LOCALAPPDATA%\Reactor\devtools\<pid>.log`) | DACL = user profile = trusted. | Holds for confidentiality in single-user box; on a multi-user box the file is readable by all users in the default `Users` group depending on profile policy — not in scope for this chunk's threat surface, but called out. |

---

## 4. Asset inventory

**Assets (data):**
- A1: Window pixels (screenshot) — may include user mail, terminal output, secrets pasted into Reactor controls, or anything else on screen.
- A2: stdout/stderr/Debug/Trace — typically contain stack traces with file paths, exception messages, request URIs, sometimes credentials a careless app prints in Debug.
- A3: Component reactive state shape (`$type`/`$shape`) — leaks app-internal class names, public surface, type fullnames.
- A4: Dependency-property values for every element — text, image URIs, theme tokens, computed bindings.
- A5: Tool-call log on disk (`<pid>.log`) — selector + tool name + timing.

**Assets (capabilities):**
- C1: Synthesize automation-driven input on any in-process UI element (`click`, `toggle`, `type`, `select`, `expand`, `collapse`, `focus`, `scroll`).
- C2: Mutate any dependency property (`setProperty`).
- C3: Mutate Application/Window/element ResourceDictionary (`setResource`).
- C4: Reflectively invoke arbitrary public/non-public methods on the root `Component` (minus a 13-name lifecycle denylist) with caller-controlled args (`fire`).
- C5: Switch the root component, request reload (process exit 42), or request shutdown (process exit 0).
- C6: Capture window pixels at any rate.
- C7: Drain log buffer and long-poll (≤ 30 s).

**Integrity properties:**
- I1: Synthetic input is indistinguishable from human input on the application side (no provenance tag on the WinUI input pipeline).
- I2: Tool-call logs persist a record of which selectors were touched. They do NOT log the JSON args, the result payload, or the caller identity (no caller identity exists post-Chunk 01).

---

## 5. STRIDE table

| # | Category | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding / recommendation |
|---|---|---|---|---|---|---|---|
| T1 | **Information disclosure** | Hostile local process / hijacked browser tab calls `screenshot` and exfiltrates window pixels containing credentials, e‑mail, IP. | Any local process / browser via DNS-rebind. | High (privacy/IP). | High when devtools enabled. | Devtools opt-in via `--devtools run`; loopback only; 5 s dispatcher timeout. **No rate limit, no consent prompt, no auth.** | Finding F1. Add per-session pairing token + capability-gate `screenshot` so "devtools enabled" ≠ "screenshot enabled by default." |
| T2 | **Information disclosure** | `logs` returns captured stdout/stderr/Debug containing secrets the app printed in dev mode. | Same as T1. | Medium-High (secrets, access tokens, OAuth refresh tokens are commonly Debug-logged). | High. | `logs-disabled` error path when buffer is null (`DevtoolsLogsTool.cs:51`); 30 s wait clamp (`:83`); 4 MB ring. **No redaction, no opt-out at runtime, no allow-list of sources.** | Finding F2. Document explicitly "what enters Debug enters this buffer" and add an opt-out (`--devtools-logs off`) plus an in-memory redaction hook for token/key patterns. |
| T3 | **Information disclosure** | Stack traces / exception messages leak file paths and parameter values back via `JsonRpcError.Message = ex.Message` — see `McpDispatcher.cs:99`. | Same as T1. | Medium (path leaks, type names). | Medium. | None — bare `ex.Message` returned. | Finding F3. Strip / redact unhandled-exception messages on the wire at the dispatcher boundary (Chunk 01-flavored, but tool handlers contribute the surface). |
| T4 | **Tampering / EoP** | `fire` reflectively invokes any non-lifecycle method on root component, with attacker-controlled JSON args. | Local attacker reaching dispatcher. | **High** if the developer's component has a public/private method like `DeleteAccount(string id)`, `LaunchProcess(string cmd)`, or any method that wraps `Process.Start` / file I/O / network calls. | High when devtools enabled and a real app is hosted. | Lifecycle denylist (`ForbiddenMethods`, `DevtoolsFireTool.cs:177`); ResolveTarget fails if root not loaded. **No allow-list, no signature filter, no consent.** | Finding F4 (Critical). The denylist is exhaustive only for lifecycle helpers — any user-authored method on the root component is reachable, plus *base-class methods* via `BindingFlags.Instance` without `DeclaredOnly`. |
| T5 | **Tampering** | `setProperty` / `setResource` stomp critical resources (theme brushes, focus visuals, AutomationProperties.AutomationId for Windows accessibility hooks, popup security flags). | Same as T1. | Medium (UI confusion, focus traps, anti-spoofing chrome removed). | Medium. | None (any DP / any key). | Finding F5. Out of band: ship a property allow-list or "danger" warning for properties WinUI security-sensitive surfaces depend on. |
| T6 | **Tampering / EoP (synthetic input)** | `click` / `invoke` / `toggle` / `select` / `type` / `expand` / `focus` / `scroll` synthesize automation actions with no provenance. A local attacker fires "OK" on a confirmation dialog the user just opened, or `type`s into a password textbox. | Same as T1. | High if app exposes destructive confirmations. | High when devtools enabled. | Programmatic-only (no real Win32 input injection); requires UIA peer. | Finding F6. Document loud-and-clear: "devtools mode is equivalent to letting the caller drive your UI." Pair token + audited mode are the right answer. |
| T7 | **DoS** | `tree` walked over a deep / wide visual tree pins the UI dispatcher; combined with `view=full` it does a peer probe per node × 12 patterns (`TreeWalker.cs:259-273`). | Same as T1. | Medium (UI freeze for 5 s before timeout, repeatable). | High. | 5 s dispatcher timeout (`OnDispatcher`). No node-count cap. No depth cap. | Finding F7. Add `MaxNodes` / `MaxDepth` guards inside `TreeWalker.WalkInto`. Today nothing prevents an attacker-supplied selector that resolves to a deeply-nested element from generating a 100k-node payload. |
| T8 | **DoS** | Selector regex / type-path complexity. `SelectorParser.cs` regexes are anchored and bounded. But callers supply free-form regex via `logs.filter` (`DevtoolsLogsTool.cs:34`) and `resources.filter` (`DevtoolsPropertyTools.cs:147,169`) — passed directly to `new Regex(...)` with **no timeout**. | Same as T1. | Medium (CPU pin per-call; combined with no concurrency cap → wider DoS). | High. | `logs.filter` invalid → silent fallback to substring (`LogCaptureBuffer.cs:115-120`); resources filter invalid → tool error. **No `RegexOptions.NonBacktracking`, no `MatchTimeout`.** | Finding F8. Set `Regex.MatchTimeout` (e.g. 200 ms) or use `RegexOptions.NonBacktracking` for caller-supplied filter regexes. |
| T9 | **DoS** | `screenshot` invoked at high rate. Each call allocates two `Bitmap`s, runs `PrintWindow`, encodes PNG. No throttle; 10 s tool timeout (`DevtoolsUiaTools.cs:520`). | Same as T1. | Medium (CPU + GDI + UI thread churn). | Medium. | `OnDispatcher` 10 s, no concurrency cap. | Finding F9. Add a min-interval per session, or a max-in-flight gate. Cheaper than full rate limiting. |
| T10 | **DoS** | `waitFor` inside a single tool call sleeps + dispatcher-pings every 50 ms up to caller-controlled `timeoutMs` (default 5 s). Many parallel `waitFor` calls saturate dispatcher. (`DevtoolsUiaTools.cs:756-762`.) | Same as T1. | Medium. | Medium. | None on `timeoutMs` (`DevtoolsTools.ReadInt(@params, "timeoutMs") ?? 5000`, `:752`). | Finding F10. Clamp `timeoutMs` to a sane maximum (e.g. 60 s) the way `logs.waitMs` is clamped to 30 s. |
| T11 | **DoS / EoP** | `tools/list` enumerates `MergedDictionaries` and `ThemeDictionaries` recursively (`DevtoolsPropertyTools.cs:648-658`). Resource dictionaries can be cyclic in pathological apps. | Same as T1. | Medium (stack-overflow / hang). | Low. | None. | Finding F11. Add a visited set to `CollectResources`. |
| T12 | **DoS** | `state` and `properties` enumerate all hooks / DPs and `ToString()` arbitrary objects (`DevtoolsPropertyTools.cs:454,491`; `DevtoolsStateTool.cs:138`). A custom `ToString()` may run user code, deadlock, or be expensive. | Same as T1. | Low–Medium. | Medium. | Try/catch around individual reads (`DevtoolsPropertyTools.cs:454`); none on `ToString`. | Finding F12. Wrap `value.ToString()` in `try`/`catch` and a length cap. |
| T13 | **Repudiation** | A tool call that mutates app state (`fire`, `setProperty`, `setResource`, `switchComponent`, `reload`, `shutdown`, `type`, `click`, `toggle`, `select`) is logged to `<pid>.log` only when `--devtools-log-level >= call`. The default is `Call`. The log records `tool`, `selector` (truncated 80), `latencyMs`, `ok/err`, `code`. **It does NOT record arguments**, so `setProperty {selector:"…",name:"Width",value:"…malicious…"}` only shows the selector. | Local attacker reaching dispatcher; insider-investigation perspective. | Medium. | Medium. | TSV log with rotation; truncates collapsed newlines (`DevtoolsLogger.cs:173`). | Finding F13. Log a hash or short prefix of args for mutating tools at `Trace` level — and consider promoting `setProperty`/`setResource`/`fire`/`shutdown`/`reload` to always-log even at `Error` level. |
| T14 | **Spoofing (selector confusion)** | `[name='X']` matches `AutomationProperties.Name` OR the visible caption (`SelectorResolver.cs:248-254`). An attacker who can plant a hidden `Button` with caption `"OK"` (e.g. via `setResource` on a control template, or via a malicious component the app loads) can force `click [name='OK']` to land on the planted control. | Same as T1. | Low (requires combining tools). | Low. | Pruning + ambiguity error (`SelectorResolver.cs:128`). | Info-level — call out as a chained risk. |
| T15 | **EoP via `setResource` to `app` scope** | `Application.Current.Resources["BackgroundBrush"] = …`. Mutates *process-wide* resources, can affect every window (including hidden) and survives `switchComponent`. (`DevtoolsPropertyTools.cs:262`.) | Same as T1. | Low–Medium. | Low. | None. | Finding F15. Default `setResource` scope to `element` not `app`; require explicit opt-in for app scope. |
| T16 | **Tampering on attached DP discovery** | `DevtoolsPropertyTools.FindTypeByName` (`:413`) does `assembly.GetType(...)` against multiple WinUI namespaces with caller-controlled type name ("Grid.Row" → `ownerName="Grid"`). Type name is not validated to be a control — `Application`, `Window`, `DependencyProperty` are reachable. Combined with `el.SetValue(dp, parsed)` this could let an attacker poke unexpected attached properties. | Same as T1. | Low. | Low. | Field type filter `field.FieldType == typeof(DependencyProperty)` (`:385`). | Info-level. The DP type filter does the heavy lifting; flag if surface widens. |
| T17 | **Info disclosure via tree** | `tree view=full` returns `TypeFullName` for every node, including third-party libraries linked into the app (`TreeWalker.cs:221`). Reveals dependency footprint. | Same as T1. | Low. | High when devtools enabled. | None. | Info-level — accept as design choice; document. |
| T18 | **Resource leak / DoS** | `ScreenshotCapture.CaptureWindow` allocates `Bitmap` + obtains `HDC` via `g.GetHdc()`; on exception path before `g.ReleaseHdc(hdc)` (`ScreenshotCapture.cs:42-45`) the HDC leaks. Same goes for the `windowBmp`/`outBmp` allocations on PrintWindow failure: `using` covers them, but the `g.GetHdc/ReleaseHdc` pairing is not in a `try`/`finally`. | Same as T1. | Low-Medium (handle-table exhaustion → DoS). | Low. | `using` on Bitmaps/Graphics. **No `try/finally` around HDC.** | Finding F18. Wrap GetHdc/ReleaseHdc in try/finally. |
| T19 | **Tampering** | `LogCaptureInstall.Install` swaps process `Console.Out` / `Console.Error` and adds a `BufferTraceListener`. The Tee captures `Debug.WriteLine` output that may contain secrets the app emits — and *also* lets any tool caller pull them later. There is no "stop capturing" path; install is one-way and process-lifetime. | Same as T2. | Medium. | High when log capture is enabled. | None. | Finding F19. Add an Uninstall path (or a process-wide "redact while capturing" hook) so a host can stop log capture without restarting. |
| T20 | **EoP** | `DevtoolsFireTool.FindHandler` uses `BindingFlags.Public \| NonPublic \| Instance` without `DeclaredOnly` (`DevtoolsFireTool.cs:165-169`). Methods inherited from base `Component` and `Object` are reachable by name (e.g. `Equals`, `GetHashCode`, `ToString` survive the lifecycle denylist). Worse, any framework `internal`/`private` method on `Component` that isn't on the denylist is invocable. | Same as T1. | Medium (trigger framework methods out-of-cycle). | Low–Medium. | Lifecycle denylist (incomplete). | Finding F20. Restrict to `DeclaredOnly` on the user `Component` subclass *or* use a method-attribute opt-in (`[DevtoolsFire]`) rather than denylist. |
| T21 | **EoP via long-running args** | `DevtoolsFireTool.ExtractArgs` returns `JsonElementToClr` shapes — primitives + `RawText` for object/array. The handler is invoked with `MethodInfo.Invoke` (`DevtoolsFireTool.cs:65`) — argument coercion is whatever the BCL does. Mismatched signatures throw `TargetParameterCountException` rather than `TargetInvocationException`, which is *not* caught by the explicit `catch (TargetInvocationException)` (`:67`); it surfaces as `InternalError` with `ex.Message`. | Same as T1. | Low. | Low. | catches `TargetInvocationException` only. | Finding F21. Catch the broader `Exception` from `Invoke` with a structured payload, both for security (don't leak BCL stack frames) and for usability. |

---

## 6. Findings

### F1 — Screenshot capability has no consent surface — **High**
**Location:** `DevtoolsUiaTools.cs:461-521`, `ScreenshotCapture.cs:18-89`.
The `screenshot` tool is registered unconditionally whenever `--devtools run` is on. No rate limit, no per-session pairing token, no on-screen affordance during capture. A loopback attacker (CSRF/DNS-rebind from a browser tab, hostile local process) can poll screenshots at the tool timeout cadence and exfiltrate anything the user types or pastes into the running app. Inherits Chunk 01's loopback-trust assumption — but unlike `tree`/`logs`, screenshot output is the *raw image* of the window, which is the most concentrated information leak in this chunk.
**Recommendation:** require a pairing token (Chunk 01-level fix); add a flag (`--devtools-screenshot off|on|onConsent`) that defaults to `onConsent` for "production-like" devtools sessions.

### F2 — Log capture has no opt-out, no redaction, no stop button — **High**
**Location:** `LogCaptureInstall.cs:180-213`, `DevtoolsLogsTool.cs:49-104`.
`LogCaptureInstall.Install` rewires `Console.Out`, `Console.Error`, and `Trace.Listeners` for the lifetime of the process; the result is a 4 MB ring of every line a developer's `Debug.WriteLine`, `Console.WriteLine`, or unhandled-exception writeback emits. The `logs` tool reads the whole ring (subject to `since`/`tail`/`source`/regex filters). There is no in-band way to disable capture once installed; `--devtools-logs off` on the command line is the only path. There is no redaction — token-shaped strings flow through verbatim.
**Recommendation:** ship a `LogCaptureInstall.Uninstall()` path; add a redaction-callback knob; document loudly the secrets exposure.

### F3 — Unhandled-exception messages return verbatim — **Medium**
**Location:** `McpDispatcher.cs:99` (`new JsonRpcError { Message = ex.Message }`).
The catch-all returns `ex.Message`, which on a tool that uses `Path.Combine` / file I/O / parsing typically contains absolute paths, environment variable values, or partial input. A handler that throws is a one-shot probe surface for environmental info.
**Recommendation:** at the dispatcher boundary, replace `ex.Message` with a sanitized string (e.g. "Internal error; correlation id <guid>") and log the full stack to `<pid>.log`.

### F4 — `fire` reflectively invokes user methods with caller-controlled args — **Critical** (when an app exposes risky methods)
**Location:** `DevtoolsFireTool.cs:127-170, 194-205`.
`FindHandler` walks `BindingFlags.Public | NonPublic | Instance` (no `DeclaredOnly`) on the root component's type and matches by case-insensitive name. The denylist (`ForbiddenMethods`) covers 13 framework lifecycle / hook helpers — nothing else. Any developer method on the component that performs file I/O, process spawn, network egress, or DB writes is reachable by a JSON-RPC caller. `ExtractArgs` deserializes JSON args into primitives + raw-text fallback (`:207-215`); `MethodInfo.Invoke` then performs BCL coercion. The framework cannot know which user methods are "safe."
**Concrete example:** a `Component` that exposes `internal void DeleteFile(string path) => File.Delete(path);` is callable as `tools/call fire {component:"…",event:"DeleteFile",args:["C:\\Users\\…\\.aws\\credentials"]}`.
**Recommendation:** flip from denylist to allow-list — require an opt-in attribute (`[DevtoolsFire]`) on user methods. Until then, restrict to `BindingFlags.DeclaredOnly` on the user's leaf type and warn loudly in docs that `fire` is equivalent to "the caller can drive any of your component's named methods."

### F5 — `setResource` defaults to `scope: "app"` — **Medium**
**Location:** `DevtoolsPropertyTools.cs:231` (`var scope = DevtoolsTools.ReadString(@params, "scope") ?? "app";`).
A caller who omits `scope` mutates `Application.Current.Resources`, which lives for the lifetime of the process and affects every window. Combined with the lack of pairing in Chunk 01 a single round-trip can reskin the app or replace key brushes with transparent values that mask other UI.
**Recommendation:** default scope to `"element"` (or require an explicit value); reject `"app"` unless the request also carries a confirmation flag.

### F6 — `fire` allow-list is by method name only and lifecycle denylist drops case — **Medium**
**Location:** `DevtoolsFireTool.cs:155-169` and `:177-192`.
`ForbiddenMethods` is an `OrdinalIgnoreCase` `HashSet`, so `Render`, `RENDER`, `render` all reject — good. But the denylist is far from exhaustive: framework methods inherited from `Component`/`Object` (`Equals`, `GetHashCode`, `MemberwiseClone`, `Finalize`) are reachable because `FindHandler` does not exclude inherited methods (no `DeclaredOnly`). `Finalize` is `protected` — but `BindingFlags.NonPublic` includes it, and an explicit invocation of `Finalize` corrupts the GC contract.
**Recommendation:** require `DeclaredOnly` on the user-component leaf type AND add `Finalize`, `MemberwiseClone`, `GetType` to the denylist.

### F7 — `tree` walker has no node-count or depth cap — **Medium**
**Location:** `TreeWalker.cs:115-197`.
`Walk` is unbounded recursive on `VisualTreeHelper.GetChildrenCount`. A complex page with virtualization disabled (or an attacker-supplied selector that resolves to the root) produces a flat `List<TreeNode>` of every UI element. Combined with `view=full`, each node also runs 12 UIA peer pattern probes (`TreeWalker.cs:259-273`), creates a full peer object tree, etc. Pinning the UI dispatcher for several seconds is straightforward.
**Recommendation:** soft caps: `MaxNodes = 5000`, `MaxDepth = 64`, return a `truncated: true` marker.

### F8 — Caller-supplied regex has no `MatchTimeout` — **Medium**
**Location:** `LogCaptureBuffer.cs:114` (`new Regex(filterRegex, RegexOptions.CultureInvariant)`); `DevtoolsPropertyTools.cs:169` (`new Regex(filter, RegexOptions.IgnoreCase)`); `DevtoolsUiaTools.cs:868` (`Regex.IsMatch(text ?? string.Empty, pred.TextMatches)`).
None of these set `RegexOptions.NonBacktracking` or `MatchTimeout`. A pathological pattern (`(a+)+$` against a long line) pegs the worker thread for seconds. `waitFor`'s loop also evaluates this on the dispatcher (`DevtoolsUiaTools.cs:758-764`), so each iteration can hang the UI.
**Recommendation:** `RegexOptions.NonBacktracking` (works for `IsMatch`) or `Regex.MatchTimeout = TimeSpan.FromMilliseconds(200)`. Tag the failure as `regex-timeout` so callers can refine.

### F9 — `screenshot` unthrottled — **Medium**
**Location:** `DevtoolsUiaTools.cs:461-521`, `ScreenshotCapture.cs`.
Each call allocates two `Bitmap`s, runs `PrintWindow`, and PNG-encodes. No min-interval and no in-flight cap. Combined with Chunk 01's lack of concurrency limits, a tight loop of `screenshot` calls drives sustained CPU + GDI usage. A 10 s `OnDispatcher` timeout protects liveness but not throughput.
**Recommendation:** per-session min interval (e.g. 100 ms) and a serialize-by-default policy.

### F10 — `waitFor.timeoutMs` is unbounded — **Low–Medium**
**Location:** `DevtoolsUiaTools.cs:752` (`int timeoutMs = DevtoolsTools.ReadInt(@params, "timeoutMs") ?? 5000;`).
Caller can pass `2_147_483_647`. The handler runs in HTTP-worker thread spinning a 50 ms `Thread.Sleep` loop and dispatcher hops. Combined with no concurrency cap (Chunk 01), N parallel `waitFor` calls each pinning a worker for ~24 days is a trivial DoS handle on the listener.
**Recommendation:** clamp to ≤ 60 s.

### F11 — `CollectResources` lacks a visited-set; cyclic `MergedDictionaries` recurse forever — **Low**
**Location:** `DevtoolsPropertyTools.cs:627-660`.
WinUI permits resource dictionaries to reference each other through `MergedDictionaries`. There is no validation that the chain is acyclic. A pathological app (or a `setResource` that adds a self-reference) sends `resources` into infinite recursion → stack overflow → process crash.
**Recommendation:** track visited `ResourceDictionary` instances by reference identity.

### F12 — `value.ToString()` runs user code without isolation — **Low–Medium**
**Location:** `DevtoolsPropertyTools.cs:491` (`_ => value.ToString()` in `FormatValue`); `DevtoolsStateTool.cs:103` (`return value.ToString()` for enums); reflection paths in `EnumerateDependencyProperties` (`:454-455`).
A custom `ToString()` overload in app code can throw, deadlock, allocate memory, or run arbitrary logic on the UI dispatcher. Some paths are guarded by `try { ... } catch { value = "<error>"; }`, but `FormatValue` is not. A property whose value's `ToString()` deadlocks pins the dispatcher until the 5 s timeout.
**Recommendation:** wrap `ToString()` in `try/catch` and a length cap (e.g. 4 KB).

### F13 — Mutation-tool log entries don't include arguments — **Medium**
**Location:** `DevtoolsLogger.LogCall` (`:83-93`); `McpDispatcher.Invoke:190-210`.
The TSV log captures `tool`, `selector` (truncated to 80 chars), latency, status, code. It does NOT capture the JSON args. After-the-fact, an investigator cannot tell whether `fire` invoked `IncrementCount()` or `DeleteAccount("admin")`. `setProperty`/`setResource` similarly hide the property name and new value.
**Recommendation:** at `Trace` level emit a hash + prefix of `args`; for the highest-impact tools (`fire`, `setProperty`, `setResource`, `setResource@app`, `shutdown`, `reload`) emit unconditionally even at `Error` level.

### F14 — Spoofing via selector — **Info**
`[name='OK']` matches `AutomationProperties.Name` OR the visible caption (`SelectorResolver.cs:248-254`). Combined with `setProperty` to plant a control with caption `"OK"`, a chained call can land synthetic input on a planted target. Practical exploitability requires already-tampering primitives, so **info-only**.

### F15 — `tools/list` advertises every tool to any caller — **Info**
The dispatcher returns the full tool inventory plus inline `_selectorGrammar` (`McpDispatcher.cs:62-75`). This is by design for MCP discovery, but it gives any process that reaches the listener a complete capability map — including the description text where we explain how to abuse `fire` (the docs say "ESCAPE HATCH" verbatim). Not a flaw, but it strengthens the case for Chunk 01-level pairing.

### F16 — `FindTypeByName` enumerates well-known WinUI namespaces — **Info**
`DevtoolsPropertyTools.cs:413-430` only searches for `DependencyProperty`-typed static fields, so the surface is constrained to actual DPs. The DP-type filter prevents arbitrary type loading. Documenting as info because future feature creep (e.g. parsing `OwnerType.Foo` to support more attached properties without the field-type guard) would widen this to a tampering vector.

### F17 — `tree view=full` exposes `TypeFullName` for every node — **Info**
`TreeWalker.cs:221`. Reveals private libraries linked into the app. Useful for legitimate debugging; document as design choice.

### F18 — `g.GetHdc()` not balanced under exception — **Low**
**Location:** `ScreenshotCapture.cs:42-45`.
```csharp
IntPtr hdc = g.GetHdc();
PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
g.ReleaseHdc(hdc);
```
If `PrintWindow` throws (DllImport throws for SEH access violations), `ReleaseHdc` is skipped and the HDC leaks. Repeated leaks exhaust the per-process GDI handle quota (default 10k) → process becomes unable to render → silent crash.
**Recommendation:** `try { hdc = g.GetHdc(); PrintWindow(...); } finally { if (hdc != IntPtr.Zero) g.ReleaseHdc(hdc); }`.

### F19 — Log capture cannot be uninstalled — **Medium**
**Location:** `LogCaptureInstall.cs:180-213`.
Once `Install` swaps `Console.Out`/`Console.Error` and adds the trace listener, the only escape is process exit. `ResetForTests` only clears the static field, not the actual `Console.SetOut` redirection. A devtools session that toggles off mid-run still captures into the buffer the next session can read.
**Recommendation:** track the previous `Console.Out` and add an idempotent `Uninstall()` that restores it and removes the trace listener.

### F20 — Reflection in `fire` discovers inherited methods — **Medium**
**Location:** `DevtoolsFireTool.cs:165-169`.
Without `DeclaredOnly`, `FindHandler` matches against the entire base chain — `Component`, `object`, any framework hooks the user authored. That widens the attack surface beyond "user-authored handlers." `ListReachableHandlers` uses `DeclaredOnly` (`:129`) and `:130-135`, so the error-path hint is *narrower than what's actually invocable* — agents see one set, attackers can hit a broader set. Inconsistency is its own bug.
**Recommendation:** apply `DeclaredOnly` to `FindHandler` so the error path and the resolution path agree.

### F21 — `fire` exception handling misses `TargetParameterCountException` — **Low**
**Location:** `DevtoolsFireTool.cs:64-73`.
The catch is `TargetInvocationException` only. `MethodInfo.Invoke` raises `TargetParameterCountException` when arg count doesn't match, and `ArgumentException` when arg conversion fails. Both currently fall through to `McpDispatcher.cs:99` and surface raw `ex.Message`. Not a security bug per se, but the leak of "Method takes 3 parameters; got 2" exposes method signature info that the structured `unknown-event` path was specifically designed to avoid leaking.
**Recommendation:** widen the catch to `Exception ex when ex is TargetInvocationException or TargetParameterCountException or ArgumentException` and translate to `handler-arg-mismatch` with no signature details.

### F22 — `NodeRegistry` keeps tombstones forever — **Low (DoS)**
**Location:** `NodeRegistry.cs:37, 86-88, 100-104`.
Tombstones accumulate on element collection or window switch and are never evicted. A long-running session that mounts/unmounts many components grows `_tombstones` indefinitely. Each call to `Resolve` does a `HashSet.Contains` on a potentially huge set — fine for hashes, but unbounded memory.
**Recommendation:** cap tombstones (e.g. LRU at 100k) and document that `gone` may degrade to `unknown` after the cap.

### F23 — `LogCaptureBuffer.Append` truncates per-entry to ~2 MB but does not reject NUL/control chars — **Info**
**Location:** `LogCaptureBuffer.cs:64-73`.
A log line containing ANSI control sequences, NUL, or VT100 escapes flows through unchanged and lands in agent-side renderers (terminals, web UIs). Chunk 04 (VS Code extension) is downstream consumer; check there whether log rendering is escaped.

---

## 7. Open questions

1. **Is the loopback-trust assumption testable?** The existing `--mcp-port` model assumes "anything on `127.0.0.1` is the developer." DNS-rebind / hostile local processes invalidate that. Decision needed at the chunk-01 level — every finding in this chunk presumes that decision lands.
2. **Is `fire` intended as a permanent surface?** The description says "escape hatch" but the v1 implementation is open-by-default. If it's a stopgap, doc'ing it as `--devtools-fire on|off|attribute` (default `attribute`) would close F4/F6/F20.
3. **What does the project want to advertise as "production-safe"?** A retail build with `ReactorApp.DevtoolsEnabled=false` and no `--devtools` flag avoids almost the entire surface. Is the threat model "developer machines only" or do we expect ad-hoc devtools enablement on field installations?
4. **Is `setResource` to `app` scope ever the intent?** No internal callsite needs it (resource hot-reload is a separate path); F5 may be a free fix.
5. **Should `tree view=full` be authenticated separately?** It is the cheapest way to map the entire component graph — useful for fingerprinting an app.
6. **Are tool invocations meant to be auditable?** F13 hinges on whether `<pid>.log` is intended as a forensic trail (then args matter) or just a performance trace.

---

## 8. Out-of-scope referrals

- **Caller authentication / origin checking / pairing tokens** — Chunk 01 (`DevtoolsMcpServer`, `McpDispatcher`). Findings F1, F2, F6, F9 hinge on Chunk 01 closing the loopback question.
- **`SelectorParser` parser-internal DoS / grammar correctness** — also covered in **Chunk 12** (parsers). The handler-side risks (cost on the live tree, regex without timeout) are scoped here.
- **Log file ACL on multi-user machines** — out of band; default `LocalAppData` DACL governs.
- **VS Code rendering of log lines (control-char escaping)** — Chunk 04.
- **`mur` CLI's surfacing of error messages from the dispatcher** — Chunk 05.
- **Hot-reload + `RequestReload`'s exit-42 behavior** — wiring is in Chunk 01 + Chunk 05; the tool just calls `ctx.RequestReload()`.
- **Native interop in `ScreenshotCapture`** — minimal P/Invoke surface; reviewed inline (F18). Wider native review in Chunk 18.
