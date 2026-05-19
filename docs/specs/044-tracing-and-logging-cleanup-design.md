# 044 — Tracing and Logging Cleanup

| | |
|---|---|
| **Status** | Draft — 2026-05-19 |
| **Owner** | @codemonkeychris |
| **Related** | [Issue #323](https://github.com/microsoft/microsoft-ui-reactor/issues/323), [023](023-perf-insight-tools.md) (perf insight tools), [024](024-ai-agent-devtools.md) (AI agent devtools), [032](032-layout-cost-overlay-design.md) (layout-cost overlay), [`docs/guide/perf-instrumentation.md`](../guide/perf-instrumentation.md) |

## 1. Summary

Reactor today has three working logging surfaces — `Microsoft-UI-Reactor` (managed ETW/EventPipe), `Debug.WriteLine`, and devtools log capture (`reactor.logs` MCP tool) — but no rule for which to use where. The result is **~150 `Debug.WriteLine` call sites** spread across 47 files, most of which carry information end-developers would benefit from in production but which are stripped from Release builds because `Debug.WriteLine` is `[Conditional("DEBUG")]`.

This spec establishes the rule, classifies the existing call sites, fills the ETW coverage gaps, and ships the connective tissue (a one-call in-process capture API, a Visual Studio workflow, an `ILogger` adapter) so that an app developer can see Reactor's diagnostics without a custom `EventListener` subclass and a PerfView install.

## 2. The rule

> **`Debug.WriteLine` is for diagnostics useful *exclusively* while changing Reactor source code. If the information could plausibly help diagnose a customer app, support issue, production repro, or devtools session — even at `Verbose` — it goes through `ReactorEventSource`.**

Corollaries:

- **Audience, not severity, decides the channel.** A trace event can be `Verbose`; it still belongs on ETW if a downstream app developer might want it.
- **Release-build observability is non-negotiable.** `Debug.WriteLine` vanishes in Release. Anything that should help a customer debug a shipped app must use ETW.
- **`Trace.WriteLine` is not adopted** for emission. We standardize on `ReactorEventSource` for app-developer signal and `Debug.*` for framework-builder signal. `Trace` listeners remain in `LogCaptureInstall` only because they happen to capture our own `Debug.*` output for devtools.
- **`Debug.Assert` / `Debug.Fail` / `throw new UnreachableException`** for invariants. `Debug.Fail("Unreachable")` is a release-build no-op and should be replaced with `throw new UnreachableException(...)`.
- **`Console.Out` / `Console.Error`** are CLI surfaces. Library code never writes to them outside `--devtools` subcommands. The `IDevtoolsConsole` abstraction in §6 enforces this.

## 3. Goals / non-goals

### 3.1 Goals

- **G1.** Codify the rule above in one place every contributor reads (`docs/guide/diagnostics.md`, contributor guide, `.editorconfig` analyzer hook). --> this needs to be updated via the docs/_pipelinem docs/guide is output
- **G2.** Classify every existing `Debug.WriteLine` site (~150) into *keep-as-debug* (framework-internal) or *promote-to-ETW* (app-relevant), then execute the promotions.
- **G3.** Expand `ReactorEventSource` keywords/events to cover the categories that today only live in `Debug.WriteLine`: Hosting, Persistence, Navigation, Intl, Theme, Shell.
- **G4.** Document — not invent — the existing dotnet trace-capture story end-to-end: the `DOTNET_EnableEventPipe`/`DOTNET_EventPipeOutputPath`/`DOTNET_EventPipeConfig` env-var path (zero app code), `dotnet-trace collect`, Visual Studio's Performance Profiler "Events Viewer", and double-click-a-`.nettrace`. Reactor doesn't need its own file-capture API — the runtime already has three.
- **G5.** Ship a thin in-process subscription helper — `ReactorTrace.Subscribe(...)` — used *internally* by the `ILogger` adapter (G6) and the devtools bridge (G7). This is a managed `EventListener` (no external NuGet, AOT-clean). It is **not** a file-capture API.
- **G6.** Ship an optional `ILogger` bridge so apps using `Microsoft.Extensions.Logging` see framework events in their existing pipeline (Serilog/Seq/AppInsights) without writing an `EventListener`.
- **G7.** Wire the in-process `EventListener` into the devtools `reactor.logs` ring buffer so the MCP `logs` tool returns ETW events alongside Console/Debug/Trace.
- **G8.** Add a small CI/test harness that asserts high-value events fire (e.g., reconcile Start/Stop pairing, RenderError on component throw).
- **G9.** **Re-justify every swallowed-error site as part of the migration.** Each of the ~80 `catch (Exception ex) { Debug.WriteLine(...); }` sites gets a written entry in a sidecar audit file (`docs/specs/044/swallowed-error-audit.md`) explaining the failure modes the catch is hiding, why swallowing is the right answer (or why it isn't), and a verdict: **Keep**, **Narrow**, **Propagate**, **Replace with `TryXxx`**, or **Promote to typed event**. No site is migrated to `DiagnosticLog.SwallowedError` without a corresponding audit entry. Sites with verdict ≠ Keep get a fix in the same PR.

### 3.2 Non-goals

- **NG1.** Replacing `ReactorEventSource` with `Microsoft.Extensions.Logging` as the primary emit API. ETW/EventPipe is keyword-gated, AOT-friendly, allocation-free when disabled, and gives us PerfView/dotnet-trace for free. `ILogger` is a *consumer* of our ETW events, not a substitute.
- **NG2.** A new in-process trace-file API. The runtime already ships three ways to capture EventPipe to disk (env vars, `dotnet-trace`, VS Events Viewer); Reactor reuses them, doesn't reinvent them. We do not take a dependency on `Microsoft.Diagnostics.NETCore.Client`.
- **NG3.** Reworking the layout-cost ETW consumer (`LayoutEtwConsumer`). That code reads the *native* `Microsoft-Windows-XAML` provider; it's orthogonal to our own emission story.
- **NG4.** Adding new performance instrumentation beyond filling the coverage gaps. Spec [023](023-perf-insight-tools.md) covers perf-shaped events; this spec covers the general logging story.
- **NG5.** Telemetry to a Microsoft endpoint. Everything stays on-box; the customer decides whether to attach a listener.

## 4. Current state (audit)

Inventory taken 2026-05-19. See issue #323 for raw counts.

### 4.1 `ReactorEventSource` (managed ETW/EventPipe — `Microsoft-UI-Reactor`)

- **15 events**, 7 keywords (`Reconcile`, `Render`, `State`, `Mcp`, `Lifecycle`, `Errors`, `EventDispatch`).
- Properly gated with `IsEnabled` at the call site and inside each event method.
- PII-aware: `RenderError` strips `ex.Message`; `McpCallStart` SHA-1-fingerprints selectors.
- **Coverage gaps:** Hosting (window open/close/DPI), Persistence (save/read/oversize), Navigation (route push/cache hit), Intl (missing key), Theme (apply failure), Shell (JumpList/Tray/ThumbnailToolbar HR codes), Backdrop (materialization failure).

### 4.2 `Debug.WriteLine` (~150 sites across 47 files)

Dominant patterns:

1. **Swallowed-error reporter.** `catch (Exception ex) { Debug.WriteLine($"[Reactor] X failed: {ex.Message}"); }` — ~80 sites. Heaviest in `ReactorWindow`, `Reconciler`, `Shell\*`, `Persistence\*`, `BackdropApplier`, `ReactorApp`, `JumpList*`, `RenderContext` effect cleanup.
2. **P/Invoke / COM HR diagnostic.** `Debug.WriteLine($"[Reactor] BeginList HR=0x{hr:X8}");` — ~20 sites in `Shell\*`, `WindowMessageMonitor`, `ReactorWindow`.
3. **Subsystem trace.** `NavigationDiagnostics` (9 sites — route push/cache hit/transition start), `LayoutCostAttribution` (8 sites — pipeline construction), `LayoutEtwConsumer` (12 sites — orphan-session cleanup), `IntlAccessor` (4 sites — missing keys), `PersistedStateCache` (5 sites — capacity/dispose).
4. **Framework-internal warnings.** `MarkdownBuilder` parse failures, `Md4cParser` assertions, `YogaConfig` frozen-mutation guards. These stay.

### 4.3 `Debug.Assert` / `Debug.Fail`

Used appropriately in `Md4cParser.*`, `YogaConfig`, `ChildCollection`, `WindowMessageMonitor`, `Reconciler`. Two anti-patterns to fix:
- `Debug.Fail("Unreachable")` in `Md4cParser.Block.cs` (4 sites) and `Reconciler.cs` (1 site) — replace with `throw new UnreachableException(...)`.

### 4.4 `Trace.*`

Zero emission sites. The only consumer is `BufferTraceListener` in `LogCaptureInstall.cs` (captures our own `Debug.*` output for devtools). Keep as-is.

### 4.5 `Console.Out` / `Console.Error`

CLI (`Reactor.Cli\*`) + `Hosting\ReactorApp.cs` `--devtools` subcommands + `PreviewCaptureServer` + `DevtoolsMcpServer`. Bracket-prefixed (`[reactor]`, `[devtools]`, `[devtools:capture]`, `[devtools:mcp]`). One stdio-JSON-RPC framing concern is already handled by `LogCaptureInstall.Install(forwardConsole: false)`.

### 4.6 `ILogger` / `Microsoft.Extensions.Logging`

Not adopted by the framework. Mentioned only in `LogCaptureInstall.cs` (one-line incidental) and `Check\Telemetry.cs` (CLI). No adapter from `ReactorEventSource` exists.

## 5. Channel taxonomy

Three channels, three audiences, three lifetimes:

| Channel | Audience | Build | Off-by-default consumer | Used for |
|---|---|---|---|---|
| `Debug.WriteLine` / `Debug.Assert` / `Debug.Fail` | Reactor framework contributors | DEBUG only | VS Output window via `DefaultTraceListener` | Internal invariants, ad-hoc tracing, "this branch shouldn't fire" guards |
| `ReactorEventSource` (`Microsoft-UI-Reactor`) | App developers, ops, devtools agents | All configs | dotnet-trace, PerfView, VS profiler, `EventListener`, our `ReactorTrace.CaptureToFile` (§7) | Anything an app developer might want to see: swallowed errors, P/Invoke failures, lifecycle events, navigation, intl misses |
| `ILogger` adapter (opt-in) | App developers using `Microsoft.Extensions.Logging` | All configs | The app's existing `ILoggerProvider` (Serilog/Seq/AppInsights/Console) | Same payload as ETW, routed into the app's structured log pipeline |

### 5.1 Decision flow for a new call site

```
Is this useful only when debugging Reactor itself?
├── Yes → Debug.WriteLine / Debug.Assert
└── No  → ReactorEventSource.Log.<Event>(...)
          (the ILogger adapter and reactor.logs MCP tool both inherit it for free)
```

## 6. Implementation

### 6.1 Phase A — Standardize the call-site helpers

Add `Microsoft.UI.Reactor.Core.Diagnostics.DiagnosticLog` — a thin helper that wraps the most common patterns so individual `catch` blocks shrink to one line and route to ETW (release-visible) and Debug (dev-loop) automatically.

> **Important:** The public helpers are **not** `[Conditional]`. The whole point is that they emit in Release. Only the DEBUG mirror (which can include the raw exception message because it lands in the dev's local Output window) is `[Conditional]`.

```csharp
namespace Microsoft.UI.Reactor.Core.Diagnostics;

internal static class DiagnosticLog
{
    /// <summary>
    /// Logs a swallowed exception that the framework chose not to propagate.
    /// Always emits to ReactorEventSource (Errors keyword); in DEBUG also
    /// writes a richer line to Debug.WriteLine for the contributor's
    /// Output window.
    /// </summary>
    public static void SwallowedError(LogCategory category, string operation, Exception ex)
    {
        if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Errors))
        {
            // ex.Message is intentionally NOT included on the ETW payload
            // (PII discipline — same rule as RenderError). The exception
            // type is enough to triage class; the DEBUG mirror below
            // includes the message because that channel is dev-machine-only.
            ReactorEventSource.Log.SwallowedError(
                category.ToString(), operation, ex.GetType().Name);
        }
        DebugSwallowedError(category, operation, ex);
    }

    public static void HResultFailed(LogCategory category, string operation, int hr)
    {
        if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Errors))
            ReactorEventSource.Log.HResultFailed(category.ToString(), operation, hr);
        DebugHResult(category, operation, hr);
    }

    [Conditional("DEBUG")]
    private static void DebugSwallowedError(LogCategory category, string operation, Exception ex)
        => Debug.WriteLine($"[{category}] {operation} failed: {ex.GetType().Name}: {ex.Message}");

    [Conditional("DEBUG")]
    private static void DebugHResult(LogCategory category, string operation, int hr)
        => Debug.WriteLine($"[{category}] {operation} HR=0x{hr:X8}");
}

internal enum LogCategory
{
    Reactor, Hosting, Persistence, Navigation, Intl, Theme, Shell, LayoutCost, Devtools, Markdown
}
```

`LogCategory` replaces the stringly-typed `[Reactor.X]` prefixes. Enforced at compile-time; no typos.

Phase A also lands the two generic events (`SwallowedError`, `HResultFailed`) on `ReactorEventSource` that the helper above calls. The subsystem-specific events from §6.2 land in Phase B; the migration in Phase C is unblocked because every catch site can route through the two generics regardless.

### 6.2 Phase B — Expand `ReactorEventSource` coverage

Add four new keywords and the events to populate them:

```csharp
public static class Keywords
{
    // existing
    public const EventKeywords Reconcile     = (EventKeywords)0x1;
    public const EventKeywords Render        = (EventKeywords)0x2;
    public const EventKeywords State         = (EventKeywords)0x4;
    public const EventKeywords Mcp           = (EventKeywords)0x8;
    public const EventKeywords Lifecycle     = (EventKeywords)0x10;
    public const EventKeywords Errors        = (EventKeywords)0x20;
    public const EventKeywords EventDispatch = (EventKeywords)0x40;
    // new
    public const EventKeywords Hosting       = (EventKeywords)0x80;   // Window/HWND/DPI/Backdrop
    public const EventKeywords Persistence   = (EventKeywords)0x100;  // settings store, placement
    public const EventKeywords Navigation    = (EventKeywords)0x200;  // route push, cache, transitions
    public const EventKeywords Intl          = (EventKeywords)0x400;  // missing keys, fallback, format
    public const EventKeywords Theme         = (EventKeywords)0x800;  // theme apply, bindings
    public const EventKeywords Shell         = (EventKeywords)0x1000; // JumpList/Tray/ThumbnailToolbar
}
```

New events (illustrative — full table in implementation PR):

| Event | Keyword | Level | Source today |
|---|---|---|---|
| `SwallowedError(category, op, exType)` | Errors | Warning | Used by `DiagnosticLog.SwallowedError` |
| `HResultFailed(category, op, hr)` | Errors | Warning | Used by `DiagnosticLog.HResultFailed` |
| `WindowOpened/Closed/DpiChanged` | Hosting | Informational | `ReactorWindow` Debug.WriteLines |
| `PersistenceRead/Write/Rejected` | Persistence | Informational | `JsonFileStore`, `PackagedSettingsStore`, `WindowPlacementCodec` |
| `NavigationRequested/Completed/Cancelled` | Navigation | Informational | `NavigationDiagnostics` Debug.WriteLines |
| `NavigationCacheHit/Miss/Evict` | Navigation | Verbose | `NavigationDiagnostics` |
| `IntlMissingKey(key, locale, fellBack)` | Intl | Warning | `IntlAccessor` |
| `ThemeApplyFailed(target, exType)` | Theme | Warning | `Reconciler` ThemeBindings |
| `BackdropMaterializationFailed(kind, exType)` | Hosting | Warning | `BackdropApplier` |

Every event method gates with `IsEnabled` both at the call site and inside the method, per existing convention.

### 6.2.1 Payload PII policy (applies to every new event)

The existing `RenderError` strips `ex.Message`; the existing `McpCallStart` hashes the selector. The new coverage areas introduce strings that could carry PII, so every new event MUST follow:

| Source field | Allowed on ETW payload? | Notes |
|---|---|---|
| `Exception.Message` | No | Type name only (`ex.GetType().Name`). Message can carry paths, env, partial form values. |
| `Exception.StackTrace` | No | Captured by the profiler at session-config time if needed; not by us. |
| File paths | No (or hashed) | Replace with a short fingerprint or kind label (`"settings.json"` → `"settings"`). |
| `Window.Title` | No (or hashed) | Window titles often contain document names. Emit a SHA-1 fingerprint or the window/component type instead. |
| Navigation route values | Pattern only | Emit the route template (`/users/{id}`), not the instantiated path. |
| Intl resource keys | Yes | Keys are static developer-authored identifiers, not user data. |
| HRESULTs, Win32 error codes, integer enums | Yes | No PII risk. |
| Type names, component names | Yes | Always developer-authored. |
| Counts, durations, IDs synthesized by the framework | Yes | No PII risk. |
| Free-form strings of unknown provenance | Hashed via `HashSelectorForEtw`-style helper | Document the input source inline. |

Payloads should also be length-bounded (typical cap: 256 chars) before being formatted into the `reactor.logs` text representation (§6.6) so a rogue caller can't pump megabytes through the ring buffer.

### 6.3 Phase C — Migrate `Debug.WriteLine` call sites

Apply the rule from §2 mechanically:

| Site class | Action | Approx count |
|---|---|---|
| `catch (...) { Debug.WriteLine(... ex.Message ...); }` | → `DiagnosticLog.SwallowedError(...)` | ~80 |
| `Debug.WriteLine($"... HR=0x{hr:X8}");` | → `DiagnosticLog.HResultFailed(...)` | ~20 |
| `NavigationDiagnostics.*` Debug.WriteLines | → `ReactorEventSource.Log.Navigation*` | 9 |
| `IntlAccessor` missing-key warnings | → `ReactorEventSource.Log.IntlMissingKey(...)` | 4 |
| `LayoutCostAttribution` info logs | Keep as Debug — framework-internal | 8 |
| `LayoutEtwConsumer` session errors | Mixed: errors → SwallowedError; trace prints stay Debug | 12 |
| `MarkdownBuilder` parse failure | Keep — framework-internal | 1 |
| `Md4cParser` `Debug.Fail("Unreachable")` | → `throw new UnreachableException(...)` | 4 |
| `Reconciler.cs:2635` `Debug.Fail` | → `throw new UnreachableException(...)` | 1 |
| `YogaConfig` frozen-mutation `Debug.Assert` | Keep — framework-internal invariant | 6 |
| `ChildCollection` bounds assertions | Keep — framework-internal invariant | 4 |

After the migration, a Release build of an app sees Reactor errors in `dotnet-trace` / PerfView / Visual Studio diagnostic tools / `reactor.logs` MCP tool. A DEBUG build additionally sees Reactor-internal traces in the VS Output window.

### 6.4 Phase D — In-process subscription helper (`ReactorTrace.Subscribe`)

We deliberately do **not** ship a file-capture API. The .NET runtime already exposes three of them (see §7), and writing a valid `.nettrace` from inside the same process requires the EventPipe diagnostics IPC plumbing in `Microsoft.Diagnostics.NETCore.Client` — a heavyweight dependency that's overkill for our scenarios and isn't trim-clean.

What we *do* need in-process is a structured callback hook so the `ILogger` adapter (§6.5) and the devtools bridge (§6.6) can stay decoupled from `EventListener` internals. That's `ReactorTrace.Subscribe`:

```csharp
namespace Microsoft.UI.Reactor.Diagnostics;

public static class ReactorTrace
{
    /// <summary>
    /// Subscribes to Microsoft-UI-Reactor events in-process. The callback
    /// fires on the EventSource emission thread (often the UI dispatcher);
    /// keep work minimal and never throw. Dispose the returned token to
    /// detach. Multiple concurrent subscribers are supported; each enables
    /// the matching keywords/level for as long as it is alive.
    ///
    /// For writing a trace file, use one of:
    ///   • DOTNET_EnableEventPipe / DOTNET_EventPipeOutputPath env vars
    ///   • dotnet-trace collect --providers Microsoft-UI-Reactor
    ///   • Visual Studio Performance Profiler → Events Viewer
    /// See docs/guide/diagnostics.md.
    /// </summary>
    public static IDisposable Subscribe(
        Action<ReactorEvent> onEvent,
        EventLevel level = EventLevel.Verbose,
        EventKeywords keywords = (EventKeywords)(-1));
}

public readonly record struct ReactorEvent(
    int EventId,
    string EventName,
    EventLevel Level,
    EventKeywords Keywords,
    DateTime TimestampUtc,
    int ThreadId,
    IReadOnlyList<object?> Payload,
    IReadOnlyList<string> PayloadNames);
```

Notes on the contract:

- **Default level is `Verbose`.** Subscribers that don't want state writes / per-event-trampoline noise should pass a stricter level. (Earlier draft defaulted to `Informational`, which would silently drop `StateChange` and `EventTrampoline*`.)
- **The callback runs on the emission thread.** That can be the UI dispatcher, a thread-pool thread, or whatever raised the event. The doc-comment says "keep work minimal and never throw"; the implementation wraps the user callback in a `try/catch` so a buggy subscriber can't deadlock the dispatcher.
- **AOT-clean.** `EventListener` subclasses don't require reflection on the consumer side; we use the payload accessors on `EventWrittenEventArgs` directly.
- **Lifetime.** Subscribers are process-lifetime until disposed. Multiple concurrent subscribers are fine, but each broad subscriber enables the matching keywords on the EventSource, raising hot-path cost. We document this and call out that the devtools install creates one shared subscriber, not one per consumer.
- **AOT/trim acceptance:** This file must compile clean against `IsAotCompatible=true` with trim warnings promoted to errors (see §12).

Implementation is a sealed `EventListener` filtered on `EventSource.Name == "Microsoft-UI-Reactor"`, forwarding `OnEventWritten` into the callback. Approx 50 lines.

### 6.5 Phase E — `ILogger` adapter (opt-in)

Registered via `ILoggingBuilder`, not `ILoggerFactory`. The provider owns a single shared `EventListener`; the listener's lifetime is tied to the provider, which is disposed with the host. This avoids the "register it twice and double the cost" footgun.

```csharp
namespace Microsoft.UI.Reactor.Diagnostics;

public static class ReactorLoggingExtensions
{
    /// <summary>
    /// Registers an ILoggerProvider that forwards Microsoft-UI-Reactor
    /// events into the host's logging pipeline. Call once per app at
    /// startup (typically in ConfigureLogging).
    ///
    /// Logger category resolution:
    ///   • For generic events (SwallowedError, HResultFailed), the
    ///     category payload field becomes the logger category
    ///     ("Reactor.Hosting", "Reactor.Shell", ...).
    ///   • For subsystem-specific events, the category derives from
    ///     the primary keyword on the event metadata.
    ///   • Events with multiple keywords use the highest-bit keyword
    ///     that matches a known LogCategory; otherwise "Reactor".
    ///
    /// Severity: EventLevel → LogLevel directly (Critical→Critical,
    /// Error→Error, Warning→Warning, Informational→Information,
    /// Verbose→Debug, LogAlways→Trace). Keywords do not influence
    /// severity.
    /// </summary>
    public static ILoggingBuilder AddReactorEvents(
        this ILoggingBuilder builder,
        EventLevel minimumLevel = EventLevel.Informational,
        EventKeywords keywords = (EventKeywords)(-1));
}
```

The provider catches every exception from downstream `ILogger` sinks; a Serilog file-sink hiccup must never reach the EventSource emission thread.

Lives in a separate sub-package (`Microsoft.UI.Reactor.Logging.Extensions`) so the core package stays free of the `Microsoft.Extensions.Logging` dependency.

### 6.6 Phase F — Wire ETW into `reactor.logs`

`LogCaptureInstall.Install` gains a `LogSource.Event` source and registers exactly one `EventListener` (via `ReactorTrace.Subscribe`) that appends each ETW event into `LogCaptureBuffer` as a structured line:

```
2026-05-19T16:42:01.123Z  [event:Hosting]  WindowOpened windowType=SettingsWindow hwnd=0x00010A2C
2026-05-19T16:42:01.901Z  [event:Errors]   SwallowedError category=Shell op=JumpList.SaveAsync exType=COMException
```

The `reactor.logs` MCP tool gains `source=event` as a filter alongside the existing `stdout`/`stderr`/`debug`. To preserve the existing MCP response shape (non-breaking for current devtools clients), the `text` field carries the formatted line; the entry's existing `level` field is populated from the EventLevel mapping; and the entry gains two **additive, optional** fields — `eventName` and `eventId` — which existing clients ignore and new ones can filter on.

Payload formatting follows the PII policy in §6.2.1 (no `Window.Title` raw, no exception messages, no route values; payload strings length-bounded at 256 chars before stringification).

### 6.7 Phase C-audit — Re-justify every swallowed error

The dominant `Debug.WriteLine` pattern (~80 sites) is the broad-catch swallow:

```csharp
try { _appWindow.Close(); }
catch (Exception ex) { Debug.WriteLine($"[Reactor] Close failed: {ex.Message}"); }
```

This pattern is dangerous for three independent reasons:

1. **Bug concealment.** A broad `catch (Exception)` hides everything from `NullReferenceException` to `OutOfMemoryException`. Real bugs in our usage of WinUI (and real bugs in WinUI itself) get downgraded to a `Debug.WriteLine` line in DEBUG and nothing at all in Release.
2. **AI-authored drift.** Many of these catches were added defensively by agents (including me) to "make tests pass" or "be safe". Almost none have a written justification on the line. We should assume some fraction are unnecessary until proven otherwise.
3. **Performance.** Throwing an exception is expensive — `Exception.StackTrace` capture, EH unwind, JIT EH region bookkeeping. A catch around a hot path that swallows a *commonly-thrown* exception (`COMException` from a closed window, `Win32Exception` from a torn-down HWND) silently turns the steady-state path into the exceptional path. Some of these belong as `TryXxx` predicates instead.

**Type-level filtering is not enough.** `catch (COMException)` is still too broad — a `COMException` carrying `E_INVALIDARG` (we built a bad call) is a bug we want to surface, while one carrying `RPC_E_DISCONNECTED` (the window already went away under us) is the expected failure mode we want to swallow. **Filters must reach down to specific HRESULTs / Win32 codes** unless the audit entry justifies otherwise.

To keep the filters readable, define a small `Microsoft.UI.Reactor.Core.Diagnostics.HResults` static class with named constants:

```csharp
internal static class HResults
{
    public const int RPC_E_DISCONNECTED       = unchecked((int)0x80010108);
    public const int E_HANDLE                 = unchecked((int)0x80070006);
    public const int RPC_E_SERVERFAULT        = unchecked((int)0x80010105);
    public const int CO_E_OBJNOTCONNECTED     = unchecked((int)0x800401FD);
    public const int TYPE_E_ELEMENTNOTFOUND   = unchecked((int)0x8002802B);
    public const int ERROR_FILE_NOT_FOUND     = unchecked((int)0x80070002);
    public const int ERROR_ACCESS_DENIED      = unchecked((int)0x80070005);
    // …populated as the audit identifies real codes
}
```

The cleanest forcing function is the `Debug.WriteLine` → `DiagnosticLog.SwallowedError` migration. No site moves to the helper without a corresponding entry in the sidecar audit. The audit file is a real PR artifact — checked in, reviewed line-by-line, expected to grow during Phase C and **shrink** by Phase D as Propagate/Narrow/TryXxx verdicts ship their fixes.

#### 6.7.1 Audit file format

`docs/specs/044/swallowed-error-audit.md` — one section per file, one subsection per site:

```markdown
### src/Reactor/Hosting/ReactorWindow.cs:826 — `Close()` swallow

- **Site (before):**
  ```csharp
  try { _appWindow.Close(); }
  catch (Exception ex) { Debug.WriteLine($"[Reactor] Close failed: {ex.Message}"); }
  ```
- **Operation:** `Microsoft.UI.Windowing.AppWindow.Close()` from `ReactorWindow.Close()`.
- **Caller contract:** Public method on `ReactorWindow`. Called from app code, the close-button handler, and the dispose path. Idempotent from the caller's POV.
- **Observed/expected failure modes (HRESULT-level):**
  - `RPC_E_DISCONNECTED` (`0x80010108`) — AppWindow proxy already torn down (race with `WM_CLOSE`).
  - `CO_E_OBJNOTCONNECTED` (`0x800401FD`) — proxy released before the call landed.
- **What we explicitly do NOT want to swallow:**
  - `E_INVALIDARG`, `E_POINTER` from a `COMException` — those mean we built a bad call; that's a Reactor bug.
  - `NullReferenceException`, `InvalidOperationException` not on the listed HRESULT path — bugs in our state machine.
  - `OutOfMemoryException`, `StackOverflowException` — never swallow.
- **Why we swallow the listed cases:** Close is the terminal lifecycle operation; the listed disconnect races are inherent to the AppWindow proxy lifetime. Propagating would break both user-authored dispose chains and our own teardown, neither of which has a meaningful recovery.
- **Verdict:** **Narrow to specific HRESULTs.**
- **Site (after):**
  ```csharp
  try { _appWindow.Close(); }
  catch (COMException ex) when (ex.HResult is HResults.RPC_E_DISCONNECTED or HResults.CO_E_OBJNOTCONNECTED)
  {
      DiagnosticLog.SwallowedError(LogCategory.Hosting, "Window.Close", ex);
  }
  ```
- **Risk:** Low — an unobserved HRESULT now surfaces as a real bug, which is the desired outcome.
- **Owner:** @codemonkeychris  •  **PR:** #XXX  •  **Status:** ☐ migrated  ☐ verdict shipped
```

Required fields: **Site (before)**, **Operation** (what platform/SDK call is in the try), **Caller contract** (who calls this, when), **Observed/expected failure modes** (HRESULT or Win32 code level — not just type), **What we explicitly do NOT want to swallow** (the bug-class exceptions we're now happy to let propagate), **Why we swallow the listed cases**, **Verdict**, **Site (after)**, **Risk**, **Owner/PR/Status**.

The "What we explicitly do NOT want to swallow" field is load-bearing. It turns the audit from a justification into a *contract* — anything not on the listed HRESULTs/codes is, by design, a real bug.

#### 6.7.2 Verdicts

Every site lands in exactly one bucket:

| Verdict | Meaning | Resulting code |
|---|---|---|
| **Keep** | Broad swallow is correct (dispose path, finalizer-equivalent, user-callback isolation — see §6.7.3). Justification is written and the code shape is unchanged. | `catch (Exception ex) { DiagnosticLog.SwallowedError(category, op, ex); }` — typically with a `// AUDIT: ...` comment |
| **Narrow** | The catch should match specific HRESULTs / Win32 codes only. Anything else is a real bug we want to surface. | `catch (COMException ex) when (ex.HResult is HResults.RPC_E_DISCONNECTED or HResults.CO_E_OBJNOTCONNECTED)` — never bare `catch (COMException)` |
| **Propagate** | The catch was defensive paranoia / AI slop. Remove it; let the exception bubble. | (catch deleted) |
| **Replace with `TryXxx`** | The hot-path exception is the steady-state; refactor to a predicate or `bool TryDo(...)` that doesn't throw. | `if (TryClose(out var hr)) ... else DiagnosticLog.HResultFailed(...)` |
| **Promote to typed event** | The site deserves its own `ReactorEventSource` event with structured payload, not the generic `SwallowedError`. | `ReactorEventSource.Log.WindowCloseFailed(hr, windowType);` |

Sites with verdict ≠ **Keep** ship their fix in the same PR as the migration. The audit entry's **Status** transitions from `migrated` to `verdict shipped`.

A bare `catch (COMException)` or `catch (Win32Exception)` without an HRESULT/`NativeErrorCode` `when` filter is **not** a valid Narrow result — it's the type-level mistake we're trying to eliminate. The audit reviewer rejects it.

#### 6.7.3 The "user-callback isolation" carve-out

A non-trivial subset of swallows is around user-provided callbacks (effect cleanups, command handlers, lifecycle hooks like `onNavigatedTo`). Examples in `RenderContext.cs:1366–1427`:

```csharp
catch (Exception ex) { Debug.WriteLine($"[Reactor] Effect cleanup at index {i} threw: {ex}"); }
```

For these, swallowing **is** the correct framework behavior: an app's faulty effect must not crash the reconciler, and propagating would mean a single buggy `UseEffect` brings down the whole render loop. These sites have verdict **Keep**, but the audit entry must still:

1. Spell out *which user contract* the callback fulfills (so a future reader knows what they'd break by changing this).
2. Confirm the swallow does **not** suppress framework-internal exceptions — only the user delegate's body. If the catch is around `userCallback(); _internalCleanup();`, the `_internalCleanup()` call belongs **outside** the try/catch.
3. Emit the exception via `DiagnosticLog.SwallowedError(category: Reactor, op: "UseEffect.cleanup[i=N]", ex)` so the app developer can see in their `.nettrace` *that* their callback threw — even though we kept running. This is exactly the kind of signal that gets lost in DEBUG-only `Debug.WriteLine`.

#### 6.7.4 First-pass categorization (~80 sites)

Rough triage from the audit (refined in the actual report):

| Category | Approx count | Likely verdict |
|---|---|---|
| Dispose / teardown best-effort (`ReactorWindow` 60% of its sites; tray icon dispose; host dispose) | ~20 | Keep — but each entry must list the specific HRESULTs/types it expects |
| Shell COM calls (JumpList, ThumbnailToolbar, Taskbar) — documented "may fail on non-shell SKUs / packaged-app boundaries" | ~15 | Promote to typed event with HR payload + HRESULT-filtered catch |
| Win32 P/Invoke `GetLastError()`-style reporters | ~10 | Replace with `TryXxx` predicate (most have a `bool` return already) |
| AppWindow / Window subsystem (resize, position, presenter, title) | ~12 | Narrow to specific HRESULTs (RPC_E_DISCONNECTED, CO_E_OBJNOTCONNECTED) — AppWindow lifecycle is well-defined; an unexpected HR is a Reactor bug |
| User-callback isolation (effects, command handlers, onNavigatedTo/From, ClosingGuard) | ~10 | Keep — see §6.7.3 |
| Persistence (`JsonFileStore`, `PackagedSettingsStore`, `WindowPlacementCodec`) | ~8 | Narrow to `IOException`/`JsonException`/`UnauthorizedAccessException` only — never bare `Exception` |
| ConnectedAnimation `PrepareToAnimate` / `GetAnimation` / `TryStart` | ~4 | Promote to typed event; narrow to specific HRESULTs |
| Backdrop / Theme application | ~3 | Narrow to specific HRESULTs only; NRE/ArgumentNull here means we built a bad call |

Expected outcomes: ~30 sites stay broad-catch (justified), ~30 narrow, ~10 propagate (delete the catch), ~10 become `TryXxx` or typed events. The "propagate" bucket is the AI-slop hypothesis test — if it's < 5, the codebase is healthier than we feared; if it's > 20, we have a real problem to surface upstream.

#### 6.7.5 CI gate

After Phase C lands, add a Roslyn analyzer check that flags any new `catch (Exception)` (or bare `catch (COMException)` / `catch (Win32Exception)` without a `when` HRESULT filter) in `src/Reactor/` that does not call into `DiagnosticLog.*` or rethrow. New broad swallows must come with a new audit-file entry referenced by a `// AUDIT: docs/specs/044/swallowed-error-audit.md#...` comment on the catch. This makes the rule durable past the cleanup.

### 6.8 Phase G — Console abstraction (CLI separation)

A small `IDevtoolsConsole` interface plus the default `SystemDevtoolsConsole` so `Hosting\ReactorApp.cs`, `PreviewCaptureServer`, and `DevtoolsMcpServer` stop calling `Console.*` directly. Tests can install a `BufferDevtoolsConsole`. Out of scope for the first PR; tracked here so we don't reintroduce direct `Console` usage in library code.

## 7. Visual Studio workflow

> **"Is there an easy way in Visual Studio to see the ETW logs?"**

Yes — the **Performance Profiler → Events Viewer** is the primary path. The Diagnostic Tools window during F5 debugging shows BCL events by default but does not reliably surface arbitrary custom `EventSource` providers without configuration, so we lead with the Profiler route.

### 7.1 Primary path: Performance Profiler → Events Viewer

1. **Debug → Performance Profiler…** (Alt+F2).
2. **Show all tools** → check **Events Viewer**. Uncheck the others (CPU, allocation) unless you need them — they add noise.
3. Click the gear next to **Events Viewer** → **Settings…** → **Additional Providers** → add `Microsoft-UI-Reactor` (provider name; leave GUID blank — VS resolves it via EventPipe). Optionally set a keyword bitmask:
   - `0x20` = errors only
   - `0x3F` = reconcile + render + state + mcp + lifecycle + errors
   - `0xFFFFFFFFFFFFFFFF` = everything
4. Click **Start**. Repro the issue. Stop.
5. The output `.diagsession` opens in VS with the event list, timestamps, provider, and payload columns.

### 7.2 Open a `.nettrace` file captured elsewhere

Once a `.nettrace` exists (see §8 for how), **File → Open → File…** → pick the file. VS opens it in the Performance Profiler Events Viewer with the event list, timestamps, and payload columns. Callstacks are present only if the original capture session was configured to record stacks; do not assume they're always there.

This is the right workflow for production / customer-collected / CI repro traces.

### 7.3 Diagnostic Tools while debugging (limited)

The Diagnostic Tools window (Ctrl+Alt+F2 during F5) has an Events tab that shows BCL events. It can be configured to show additional EventSource providers, but the configuration UX is version-dependent and less discoverable than §7.1. We document §7.1 as the canonical path and note this one only as a "you may also see it here" hint.

### 7.4 PerfView / xperf

Still the right tool when you also need `Microsoft-Windows-XAML` (native ETW, not visible in EventPipe-only tooling). Already documented in [`docs/guide/perf-instrumentation.md`](../guide/perf-instrumentation.md).

## 8. The "one command" capture recipe

> **"Is there an easy dotnet call to capture the logs for an app that would bring these all together?"**

The .NET runtime already ships three ways to capture EventPipe to disk. We **document and recommend** them; Reactor does not add a new one.

### 8.1 Easiest: runtime environment variables (zero app code)

```bat
set DOTNET_EnableEventPipe=1
set DOTNET_EventPipeOutputPath=reactor.nettrace
set DOTNET_EventPipeConfig=Microsoft-UI-Reactor:0xFFFFFFFFFFFFFFFF:5
MyApp.exe
```

- `0xFFFFFFFFFFFFFFFF` is "all keywords"; substitute a narrower mask for less noise.
- `5` is `EventLevel.Verbose`. Use `4` for Informational, `3` for Warning, `2` for Error.
- On exit, `reactor.nettrace` is written. Double-click to open in Visual Studio.

This is the answer to the user's question. No `using` block, no code change, works against any pre-built Reactor app — customer apps included.

### 8.2 Attach to a running app: `dotnet-trace`

```
dotnet tool install -g dotnet-trace
dotnet-trace collect --name MyApp --providers Microsoft-UI-Reactor
```

Or by PID:

```
dotnet-trace collect -p <pid> --providers Microsoft-UI-Reactor:0x3F:Informational
```

Press Enter to stop; outputs a `.nettrace`. Same VS open story.

### 8.3 Visual Studio Performance Profiler

See §7.1 — the same flow, but you collect from inside VS instead of a separate file.

### 8.4 When you actually want `ILogger`

For apps using `Microsoft.Extensions.Logging`, the in-process bridge (§6.5) is the right answer:

```csharp
hostBuilder.ConfigureLogging(b =>
{
    b.AddSerilog();
    b.AddReactorEvents(minimumLevel: EventLevel.Warning);
});
```

Now Reactor warnings/errors flow into the existing Serilog/Seq/AppInsights sinks alongside the app's own log lines — no `.nettrace` file involved.

## 9. Testing

A small in-process listener harness, `ReactorTraceCollector`, asserts events fired during a unit test:

```csharp
[Fact]
public void Reconcile_emits_start_stop_pair()
{
    using var collector = ReactorTraceCollector.Capture(
        keywords: ReactorEventSource.Keywords.Reconcile);

    var host = new TestHost(() => Text("hi"));
    host.Render();

    Assert.Collection(collector.Events,
        e => Assert.Equal("ReconcileStart", e.EventName),
        e => Assert.Equal("ReconcileStop", e.EventName));
}
```

Covers regression guards for:

- Reconcile Start/Stop pairing
- ComponentRender error → `RenderError` event fires
- `SwallowedError` fires for known catch sites (one test per category)
- MCP `selector` payload is hashed (no raw selector text)
- Disabled-keyword path doesn't allocate (Stopwatch / type-name lookup guarded)

## 10. Open questions

- **Q1.** ~~Do we accept a NuGet dependency on `Microsoft.Diagnostics.NETCore.Client`?~~ **Resolved.** No — we don't ship a file-capture API at all. The runtime's env-vars / `dotnet-trace` / VS Profiler cover the file case (see §8); `ReactorTrace.Subscribe` covers the in-process case. Closed.
- **Q2.** Should `ReactorLoggingExtensions` live in `Microsoft.UI.Reactor` or a sibling `Microsoft.UI.Reactor.Logging.Extensions` package? Leaning toward the sibling so the core package stays free of `Microsoft.Extensions.Logging`.
- **Q3.** Do we add a `[Reactor]` Roslyn analyzer that flags new `Debug.WriteLine` outside the allow-list directories (`Markdown/`, `Yoga/`, `Core/Internal/`)? Cheap to add, prevents drift, may be noisy for incremental contributors. Defer until the migration is complete.
- **Q4.** `Trace.WriteLine` — leave the listener in `LogCaptureInstall` or remove now that we standardize on `ReactorEventSource`? Leave it: third-party libs Reactor depends on may still emit, and the cost is one listener.
- **Q5.** ~~Should the `ILogger` adapter map our `Errors` keyword to `LogLevel.Error` or use the EventSource `Level`?~~ **Resolved.** Use `Level` (the authoritative severity); keyword drives the logger *category*, not severity. Documented in §6.5.

## 11. Phases and rollout

| Phase | Scope | Estimated PRs |
|---|---|---|
| **A** | `DiagnosticLog` helper + `LogCategory` enum + 6 new keywords + the two generic events (`SwallowedError`, `HResultFailed`) so Phase C can migrate against the generics immediately | 1 PR |
| **B** | Subsystem-specific events on `ReactorEventSource` (WindowOpened, PersistenceRead/Write, Navigation*, IntlMissingKey, ThemeApplyFailed, BackdropMaterializationFailed) + §6.2.1 PII policy enforcement | 1 PR |
| **C-audit** | Stand up `docs/specs/044/swallowed-error-audit.md` with one entry per site (~80). Pure documentation PR — no code changes yet. Establishes the per-site verdicts and unblocks Phase C. | 1 PR |
| **C** | Migrate ~150 `Debug.WriteLine` sites by category. One PR per category. **Each swallowed-error site that lands in this phase must reference its audit entry; sites with verdict ≠ Keep ship their fix (narrow / propagate / TryXxx / typed event) in the same PR.** | 5–6 PRs |
| **C-gate** | Roslyn analyzer that flags new `catch (Exception)` in `src/Reactor/` not routed through `DiagnosticLog.*` or referencing an audit entry (§6.7.5) | 1 PR |
| **D** | `ReactorTrace.Subscribe` in core (no file API — see NG2) | 1 PR |
| **E** | `Microsoft.UI.Reactor.Logging.Extensions` sibling package + `AddReactorEvents` `ILoggingBuilder` adapter | 1 PR |
| **F** | Wire `EventListener` into `LogCaptureInstall` + `reactor.logs` MCP tool (additive fields only) | 1 PR |
| **G** | `IDevtoolsConsole` abstraction (deferred; tracked, not blocking) | 1 PR |
| **H** | `docs/guide/diagnostics.md` (the rule, VS Events Viewer workflow, env-var capture recipe, `dotnet-trace` recipe, `ILogger` recipe) | 1 PR |
| **I** | `ReactorTraceCollector` test harness + the regression assertions in §9 | 1 PR |

Phases A, B, C-audit, C, C-gate close the issue body of #323 (the audit). D–F deliver the in-process convenience. H–I lock in the change.

## 12. Acceptance criteria

- A Release build of a Reactor app emits `Microsoft-UI-Reactor` events for every error/warning that today only appears in `Debug.WriteLine`.
- A developer can collect a trace by setting three environment variables before launching the app — no source change required.
- That trace opens in Visual Studio and shows reconcile/render/state/error events with timestamps, provider, and payload columns. Callstacks are available when the capturing tool was configured to record them (not asserted by us).
- The Reactor source tree contains **zero** `Debug.WriteLine` calls that report errors or HRESULT codes — they are all routed through `DiagnosticLog`.
- **Every remaining `catch (Exception)` in `src/Reactor/` has a corresponding entry in `docs/specs/044/swallowed-error-audit.md` with verdict = Keep and a written justification.** The audit file is review-blocking; PRs that introduce a new broad catch without an audit entry fail CI (§6.7.5).
- The audit's verdict distribution is checked in alongside the file; the team has visibility into the "AI slop" share (Propagate verdicts) as a quality metric.
- All new event payloads pass the §6.2.1 PII policy review (no raw `ex.Message`, no raw window titles, no instantiated route values, no raw file paths).
- `reactor.logs` MCP tool returns ETW events when `source=event` is passed. Existing clients that don't pass `source=event` see no behavior change (the new fields are additive and optional).
- The core `Microsoft.UI.Reactor` assembly continues to build clean with `IsAotCompatible=true` and trim warnings promoted to errors after the new diagnostics code lands.
- `docs/guide/diagnostics.md` is the single onboarding page; it links into `perf-instrumentation.md` for the perf-shaped events and into the architecture overview for the design rationale.
