# Chunk 15 — Hosting, ETW, Layout-Cost Overlay — Threat Model

**Phase:** 2 (per-chunk review)
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Reviewer pass:** Deep STRIDE + line-level read of every `WriteEvent` call site and every unbounded collection in the layout-cost data pipeline.

---

## 1. Scope

| File | Lines | Role |
|---|---:|---|
| `src/Reactor/Hosting/ReactorApp.cs` | 738 | App bootstrap, `--devtools` subverb dispatch, `RunOnSta`, `ReactorApplication`, unhandled-exception hook, screenshot/list output paths. |
| `src/Reactor/Hosting/ReactorHost.cs` | 765 | Per-window host: render loop, ETW pipeline lifecycle, overlay wiring, error fallback that surfaces `ex.Message` into the UI. |
| `src/Reactor/Hosting/ReactorHostControl.cs` | 582 | Embeddable variant of `ReactorHost`. Duplicates the ETW lifecycle. |
| `src/Reactor/Hosting/PageHelper.cs` | 87 | Frame-navigation helpers; passes `NavigationEventArgs.Parameter` to `IPropsReceiver`. |
| `src/Reactor/Hosting/XamlInterop.cs` | 101 | `XamlPageElement` / `XamlHostElement` reverse-embedding registrations. |
| `src/Reactor/Hosting/ReactorCoreXamlMetaDataProvider.cs` | 149 | Hand-rolled `IXamlMetadataProvider` for AOT — schema-only stubs. |
| `src/Reactor/Hosting/RenderStats.cs` | 46 | Read-only struct; no I/O. |
| `src/Reactor/Hosting/Etw/LayoutEvents.cs` | 56 | Plain DTOs (`RawLayoutEvent`, `PairedLayoutEvent`). |
| `src/Reactor/Hosting/Etw/LayoutEtwConsumer.cs` | 551 | In-process realtime ETW consumer for `Microsoft-Windows-XAML`; orphan-session cleanup; payload-schema histogram. |
| `src/Reactor/Hosting/Etw/EventPairing.cs` | 150 | Per-(thread, kind) `Begin`/`End` pairing; emits `PairedLayoutEvent`. |
| `src/Reactor/Hosting/Etw/LayoutEventRing.cs` | 99 | Drop-oldest single-lock ring buffer (capacity 65 536, power of two). |
| `src/Reactor/Hosting/LayoutCost/LayoutCostAttribution.cs` | 334 | UI-thread aggregator; resolves Component owner; closes the frame and snapshots. |
| `src/Reactor/Hosting/LayoutCost/LayoutCostOverlay.cs` | 174 | Composition-only overlay renderer (no DirectWrite, no XAML descendants). |
| `src/Reactor/Hosting/LayoutCost/PointerMap.cs` | 69 | `ulong elementId → ComponentIdentity` lookup; no eviction. |
| `src/Reactor/Hosting/LayoutCost/SpatialIndex.cs` | 77 | `ulong elementId → rect` map and component-bounds map; `ForgetElement` not wired. |
| `src/Reactor/Hosting/LayoutCost/ComponentRollup.cs` | 77 | Per-component mutable accumulators + EMA. |
| `src/Reactor/Hosting/LayoutCost/ComponentSnapshot.cs` | 46 | Immutable snapshot record. |
| `src/Reactor/Hosting/LayoutCost/ComponentOutlineVisual.cs` | 86 | Composition rectangle outline. |
| `src/Reactor/Hosting/LayoutCost/ColorRamps.cs` | 52 | Color tables. |
| `src/Reactor/Hosting/LayoutCost/MeterAnchor.cs` | 74 | Badge placement math. |
| `src/Reactor/Hosting/LayoutCost/MeterMath.cs` | 66 | Bucket math for sparkline. |
| `src/Reactor/Hosting/LayoutCost/MeterVisual.cs` | 182 | Sparkline composition primitives. |
| `src/Reactor/Hosting/LayoutCost/MeterVisualPool.cs` | 90 | Pool of badge visuals. |
| `src/Reactor/Hosting/LayoutCost/SurfaceThrough.cs` | 40 | Hit-test helper. |
| `src/Reactor/Hosting/LayoutCost/ILayoutCostReporter.cs` | 35 | Reporter interface. |
| `src/Reactor/Core/Diagnostics/ReactorEventSource.cs` | 208 | The managed `EventSource` named **`Microsoft-UI-Reactor`** — the cross-privilege payload boundary. |

Total ≈ 4 934 LOC across 26 files. `OverlayHostWiring.cs` (370 LOC) is technically Chunk 03's spec landing pad but is unavoidable here because every `LayoutCostAttribution.Drain` call is reached through it.

---

## 2. Data-flow diagram

```
                              ┌──────────────────────────────────────┐
   Reactor render ──Reconcile──▶│  ReactorEventSource.Log.WriteEvent │
   Render() / setState /        │  (managed EventSource,             │
   MCP dispatch /               │  provider name "Microsoft-UI-      │
   Component lifecycle          │  Reactor", GUID auto-derived)      │
                              └────────────────┬─────────────────────┘
                                               │  EventPipe + classic ETW
                                               ▼
                                ┌──────────── PRIVILEGE BOUNDARY ────────────┐
                                │  ETW consumers on the same machine:        │
                                │  - logman / xperf / PerfView (admin or     │
                                │    member of Performance Log Users)        │
                                │  - dotnet-trace (any local user — same    │
                                │    UID as the producer; EventPipe is       │
                                │    in-process and not gated by ETW ACLs)   │
                                └────────────────────────────────────────────┘


   Microsoft-Windows-XAML ──ETW realtime──▶  TraceEventSession
   provider (native, in WinUI's            "Reactor.LayoutCost.{pid}"
   xaml.dll)                                LayoutEtwConsumer
                                                    │
                                                    │  data.ProcessID == pid
                                                    │  data.EventName starts with
                                                    │  "MeasureElement"/"ArrangeElement"
                                                    ▼
                                           RawLayoutEvent
                                                    │ ETW callback thread
                                                    ▼
                                              EventPairing
                                                    │  per-(threadId, kind) stack
                                                    ▼
                                            PairedLayoutEvent
                                                    │
                                                    ▼
                                           LayoutEventRing (drop-oldest, 64K)
                                                    │
                                                    │  UI thread (DispatcherQueue,
                                                    │  Low priority, ~30 Hz)
                                                    ▼
                                       LayoutCostAttribution.Drain
                                                    │
                                                    │  PointerMap / SpatialIndex
                                                    ▼
                                       ComponentSnapshot[] ──▶ LayoutCostOverlay
                                                                 (Composition only —
                                                                  no XAML, no text)
```

Persistence:
- `LayoutEtwConsumer` creates a kernel ETW realtime session named `Reactor.LayoutCost.{pid}`. The session is registered with `StopOnDispose = true` and via `RegisterProcessExitHookOnce` (`LayoutEtwConsumer.cs:533`) for unmanaged-shutdown leak protection.
- ETW session names are a kernel namespace; orphan sessions from crashed Reactor processes are picked up and stopped on next start (`CloseOrphanSessions`, `LayoutEtwConsumer.cs:471`).
- `--devtools list --out <path>` writes a plain-text component-name file (`ReactorApp.cs:300`).
- `--devtools screenshot --out <path>` writes a PNG of the window (`ReactorApp.cs:265`).
- No filesystem persistence in the ETW or layout-cost paths beyond Debug.WriteLine.

---

## 3. Trust boundaries crossed

| # | Boundary | Direction | Assumption today |
|---|---|---|---|
| **B1** | Reactor process → ETW / EventPipe consumers | outbound | Any local user with `SeSystemProfilePrivilege` (admin / Performance Log Users / users in default `EventLog`-aware groups), or any process running as the **same UID** for EventPipe, can read every `WriteEvent` payload. Payloads must therefore be PII-safe by construction. |
| **B2** | XAML/`Microsoft-Windows-XAML` ETW provider → `LayoutEtwConsumer` | inbound | The XAML provider is treated as trusted; we filter by `data.ProcessID == _processId` so other-process events are dropped, but we still **decode payloads** from the kernel before that filter (`LayoutEtwConsumer.cs:255-264` runs on every event the session delivers). |
| **B3** | ETW realtime session namespace (kernel-global) | shared | Any other process can create a session named `Reactor.LayoutCost.{otherPid}`, and our orphan-session cleanup will stop it whenever the indicated pid is gone (`LayoutEtwConsumer.cs:471-498`). PID reuse/spoofing is the local-process trust model. |
| **B4** | ETW callback thread → UI thread (via `LayoutEventRing` and `DispatcherQueue.TryEnqueue`) | internal | Producer/consumer-thread separation via lock-protected ring; correct as long as `EventPairing` only runs on the producer thread. |
| **B5** | Filesystem (`--devtools screenshot --out`, `--devtools list --out`) | outbound | The CLI argument is supplied by the developer at the local terminal — trusted under the chunking-doc trust model. No path normalization, no traversal check, but the input is in TCB. |
| **B6** | Native `user32!SetProcessDpiAwarenessContext` | outbound | DllImport with no marshaled string — value is a constant `nint`. Out of review interest. |

The only review-relevant boundaries are **B1**, **B2**, and **B3**.

---

## 4. Asset inventory

What's worth attacking?

1. **PII / user content carried in ETW payloads** (B1). The provider is `Microsoft-UI-Reactor`. Anything we ship in a `WriteEvent` argument leaks at session capture time. Existing payloads to audit, by event ID:
   - 1, 2 — Reconcile boundaries (root element type name, integers).
   - 3, 4 — `ComponentRender` (component class name + a free-form `trigger` string).
   - 5 — `StateChange` (hook kind, value type name, bool).
   - 6 — `ComponentUnmount` (component class name).
   - 7, 8 — `McpCall` (tool name + **selector string**).
   - 9 — `RenderError` (component name, exception type, **`exception.Message`** — see F-1).
   - 10, 11 — `EffectsFlush` (component name, integer).
   - 12, 13 — `ChildReconcile` (integers).
   - 14, 15 — `EventTrampolineAttached` / `Dispatch` (event name, control type name).
2. **Process integrity**:
   - The ETW realtime session is a privileged kernel object. Orphan-session cleanup must not stop a session a different live-pid Reactor process owns — handled correctly by the pid-alive check.
   - Memory growth in the layout-cost pipeline (rings, dictionaries, stacks). A long-running app that flips `ShowLayoutCost` on can be made to allocate without bound.
3. **Attribution correctness** is a feature, not an asset; the overlay being wrong does not cross a trust boundary. Skipped.

---

## 5. STRIDE table

| # | Category | Threat | Attacker model | Impact | Likelihood | Mitigation today | Finding |
|---|---|---|---|---|---|---|---|
| T1 | **I**nfo disclosure | `RenderError` event ships `exception.Message` to the **`Microsoft-UI-Reactor`** ETW provider; exception messages routinely contain user-supplied data, file paths, query values, and stack-trace fragments. Any local consumer with ETW privilege (or any local process under the same UID for EventPipe) reads them. | Local low-priv user (or hostile local process under same UID) | Cross-user PII leak on a multi-user box. EventPipe needs no privilege, only same UID. | Med-High in non-DEBUG builds where unhandled exceptions are common | None — `RenderError` is wired at `Reconciler.cs:670-671` with the raw `ex.Message`. The event level is `Error`, so it surfaces even at default capture level. | **F-1 (High)** below. |
| T2 | **I**nfo disclosure | `McpCallStart(toolName, selector)` ships the raw selector to ETW. Selectors can include text predicates derived from the running app's UI — e.g. `Text*="user@email.com"`. | Local ETW consumer | Selector contents leak to any consumer of the `Microsoft-UI-Reactor` provider. | Med — only fires when devtools is active; devtools sessions are dev-mode only. | None. `McpDispatcher.cs:178` writes `selector ?? string.Empty`. | **F-2 (Med)** below. |
| T3 | **I**nfo disclosure | Class-name leakage. `ComponentRenderStart`, `ComponentUnmount`, `EffectsFlushStart`, `EventTrampolineAttached` all carry a developer-authored class name. On a packaged in-house app these names can be IP-sensitive ("PaymentApprovalDialog"). | Local ETW consumer | Same as above; class names are not user PII but are app-internal info. | Low–Med | Documented implicitly by the EventSource design — class names are the standard EventSource convention. | **F-3 (Info)** below. |
| T4 | **D**oS / memory growth | `PointerMap._idToComponent` and `SpatialIndex._elementRects` keyed by ETW `ElementId` (`ulong`) grow without bound. `ForgetElement` exists but is **not called from production code** (only from tests). A long-running app with high element churn (tab switches, virtualized list scroll, route changes) accumulates dead entries indefinitely. | Co-located process or stress test | RAM growth — eventually OOM. | Med (only when `ShowLayoutCost` is on; the flag is dev-mode). | None. | **F-4 (Med, conditional on flag)** below. |
| T5 | **D**oS / memory growth | `EventPairing._stacks` is `Dictionary<(int threadId, LayoutEventKind kind), Stack<PairingFrame>>` and is **never pruned** for thread IDs that have died. Long-running apps with thread-pool churn or custom-thread creation grow this dictionary forever. Per-thread stacks themselves can grow unbounded if a hostile / buggy XAML provider sends Begins without matching Ends. | Co-located process or local; combined with B2, a hostile in-proc native loader could synthesize Begin events. | RAM growth, eventually OOM; per-stack allocation. | Low-Med — XAML provider behavior in practice well-formed; thread churn is the realistic vector. | None. `Reset()` (line 145-149) only runs on consumer Stop. | **F-5 (Med, conditional)** below. |
| T6 | **D**oS / log spam | `Debug.WriteLine` calls in the ETW callback thread fire on every event after the diagnostic clock interval. With high event volume the histogram lock and `OrderByDescending().Take(15).ToArray()` runs from a kernel callback thread every 2 s. | High event volume | UI-thread starvation unlikely (Debug.WriteLine is sink-dependent), but allocations on the ETW callback thread are real. | Low | Lock + `< 2000 ms` guard. | Acceptable. |
| T7 | **T**ampering | Orphan session takeover — another process names a session `Reactor.LayoutCost.{somePid}`; our cleanup stops it whenever `somePid` doesn't correspond to a live process. | Local non-admin can't create realtime ETW sessions in the global namespace, so this requires Performance Log Users / admin — already privileged. | Low | Cross-pid kill is gated by `IsProcessAlive` and the prefix match. Pid reuse is a brief race. | Acceptable; documented assumption. | |
| T8 | **T**ampering / EoP | `--devtools screenshot --out <path>` writes attacker-supplied path with `File.WriteAllBytes` (no normalization, no traversal check). | A hostile workspace cannot inject the CLI flag — but a wrapping script or VS Code extension could. | Med if the supervisor passes through user-supplied strings. | None. `ReactorApp.cs:246-265`. | The CLI path is treated as TCB by the chunking-doc trust model; flagged as low-priority **F-7** for explicit acknowledgement. | |
| T9 | **S**poofing | The XAML provider GUID is a hardcoded constant (`531A35AB-…`); a local process **can register an EventSource with the same GUID** because GUIDs are first-come-first-serve only at the kernel level for *trace controllers*, not for providers — multiple providers under the same GUID raise from each registering process. Our consumer would see synthesized events. Filtered by `data.ProcessID == _processId` on line 245. | Local hostile process under same UID | Could feed crafted Begin events to drive `EventPairing._stacks` growth (see F-5). | Low — relies on producer being **in our process** because the pid filter is exact. Out-of-process synthesis is filtered out. | The pid filter is correct and load-bearing. | Acceptable; documented via T9 here. |
| T10 | **R**epudiation | ETW events do not carry the user identity beyond the producer pid — the consumer learns the pid, OS records the user. No additional context that helps attribution beyond that. | n/a | n/a | n/a | n/a | n/a |
| T11 | **I**nfo disclosure | `LayoutEtwConsumer.LogPayloadSchemaOnce` (line 426-449) calls `Debug.WriteLine` with payload **field names and value types** (not values) of every (task, opcode) pair seen. Field names can leak XAML-internal element identifiers if WinUI changes its provider schema. | Anyone reading dbgview / attached debugger | Low — field-name leak only, not values. | Low | One-shot per (task, opcode). | Acceptable. |
| T12 | **D**oS | `LayoutEventRing` capacity is fixed at 65 536 (`LayoutEventRing.cs:22`) and drop-oldest. `Capacity` is a `const int`, not configurable — fine. The `lock` per Publish/Drain is documented as cheap; producer is a single thread. | n/a | n/a | n/a | Acceptable. | n/a |
| T13 | **I**nfo disclosure | `ShowErrorFallback` (`ReactorHost.cs:735-749`, `ReactorHostControl.cs:526-540`) constructs a TextBlock with `Text = $"Render error: {ex.GetType().Name}: {ex.Message}"` and `IsTextSelectionEnabled = true`. The user can copy this. **The exception message can include user data.** Worse: the message can be screenshot by the devtools `screenshot` tool (Chunk 02). | The window's user (trusted) and any agent with screenshot access (Chunk 02). | Cross-trust-boundary leak only via Chunk 02; otherwise the user already trusts what's on their own screen. | Low–Med depending on Chunk 02's screenshot policy. | None. | **F-6 (Info)** below. |

---

## 6. Findings

### F-1 (High) — `RenderError` ships unsanitized `exception.Message` over the `Microsoft-UI-Reactor` ETW provider

**Location.**
- Producer: `src/Reactor/Core/Reconciler.cs:663-672`
  ```csharp
  catch (Exception ex) when (_errorBoundaryDepth == 0 && ex is not OutOfMemoryException and not StackOverflowException)
  {
      _logger.LogError(ex, "Component Render() threw: {ComponentName}", newEl.GetType().Name);
      if (Diagnostics.ReactorEventSource.Log.IsEnabled(
              global::System.Diagnostics.Tracing.EventLevel.Error,
              Diagnostics.ReactorEventSource.Keywords.Errors))
      {
          Diagnostics.ReactorEventSource.Log.RenderError(
              componentName ?? newEl.GetType().Name, ex.GetType().Name, ex.Message);
      }
      newChildElement = new TextBlockElement($"⚠ Render error: {ex.Message}");
  }
  ```
- Event definition: `src/Reactor/Core/Diagnostics/ReactorEventSource.cs:201-207`.

**Why it's a finding.** `ex.Message` for a typical render-time exception in a real app routinely contains:
- File paths (`FileNotFoundException`).
- Query strings or partial SQL (`InvalidOperationException` raised by data layers, query-cache errors thrown inside `Render()`).
- User-typed text echoed back from `ArgumentException`-style APIs.
- HTTP URL fragments and query parameters from `HttpRequestException`.
- Form-field values from `FormatException` / `OverflowException`.

The `Microsoft-UI-Reactor` provider runs at process scope. **EventPipe is the dangerous channel here**: any local process running as the same user can `dotnet-trace collect --process-id <pid> --providers Microsoft-UI-Reactor` and grab the full message stream **with no privilege** (the file source comment at lines 17-18 advertises exactly this). On a multi-user box, classic ETW capture additionally leaks to admins / Performance Log Users.

The chunking-doc trust table treats "the desktop user (the one who launched the app)" as trusted. But:
- A multi-user terminal-services box is exactly the case where another user can have admin / PLU privilege without being the app's user.
- A separate non-admin program running under the same UID **is an EoP target** if the Reactor app itself elevated (UAC) — it would not have, but the code does not assume the producer is unprivileged.
- "PII-by-construction" is the only viable mitigation for a managed ETW provider that runs under default capture levels.

**Severity.** High. The exception-message channel is the single highest-bandwidth PII leak in the framework, it fires at `EventLevel.Error` (which the default `dotnet-trace` profile enables), and the producer is unconditionally on (only the IsEnabled guard suppresses).

**Recommendation.**
1. Replace `ex.Message` with `string.Empty` in the ETW emission, or with a ToString-truncated stack-frame summary scrubbed to method names only.
2. If the message must travel, gate it behind a separate verbose-only keyword (e.g. `Keywords.ErrorsVerbose`) so default captures don't include it.
3. Document in the EventSource summary that no `WriteEvent` argument may be a runtime user-content string.
4. Apply the same fix to the `OnUnhandledException` handler at `ReactorApp.cs:692-699` which logs the full message — that one currently routes only to a `NullLogger`, but anyone replacing the logger inherits the leak.

---

### F-2 (Medium) — `McpCallStart` ships the raw selector string, which can carry user content

**Location.** `src/Reactor/Hosting/Devtools/McpDispatcher.cs:176-178`:
```csharp
var selector = TryReadSelector(@params);
if (traceMcp)
    ReactorEventSource.Log.McpCallStart(name, selector ?? string.Empty);
```
Event: `src/Reactor/Core/Diagnostics/ReactorEventSource.cs:123-130`.

**Why it's a finding.** Devtools selectors include text predicates: `Text*="…"`, `Name="…"`, `aria-label*="…"`. When a remote agent (Chunk 02) issues a "find by content" tool call, the selector contains a substring of *user-visible content*, which on a real app is user content — passwords masked as text, error messages, email addresses, customer names.

Selectors flow through Chunk 02's MCP dispatcher, which Chunk 01 has already noted is loopback-trusted with no auth. Adding ETW as a *second* exfiltration channel on top of an unauthenticated loopback channel doesn't increase blast radius for a same-user attacker, but it does for **other-user** ETW consumers.

**Severity.** Medium. Only when devtools is active.

**Recommendation.** Truncate or hash selectors before they hit ETW. Since the selector parser is bounded in length (Chunk 12), an SHA-1 prefix would suffice for grouping in trace timelines and fully decouples the channel.

---

### F-3 (Info) — Class-name leakage in `ComponentRenderStart`/`Stop`/`Unmount`/`Effects*`/`EventTrampoline*`

**Location.** `ReactorEventSource.cs:86-99, 115-119, 145-159, 184-197`. All of these accept developer-authored class names (`component.GetType().Name`) and pass them through `WriteEvent` unchanged.

**Why it's a finding.** Class names are not user-PII but are application-internal. For an in-house app named `PayrollAdjustmentDialog` or `KycPanel`, the class name discloses what the user is interacting with right now to anyone with the trace. This is a known property of EventSource-based diagnostics and is the standard managed convention; not actionable unless the project takes an explicit "no app-internal names in ETW" stance.

**Severity.** Info — flagged for visibility, not for fix.

**Recommendation.** Document explicitly that class names are part of the disclosure surface. Optional: provide an opt-out (`ReactorFeatureFlags.RedactClassNamesInEtw`) that emits a stable hash instead of the name.

---

### F-4 (Medium) — `PointerMap._idToComponent` and `SpatialIndex._elementRects` grow without bound

**Location.**
- `src/Reactor/Hosting/LayoutCost/PointerMap.cs:30, 48-51` — `_idToComponent.Clear()` only on full clear; no per-id eviction.
- `src/Reactor/Hosting/LayoutCost/SpatialIndex.cs:23, 26-29, 41` — `_elementRects` is added to on every Arrange, never removed in production. `ForgetElement` (line 41) has its only caller in `tests/Reactor.Tests/Hosting/LayoutCost/SpatialIndexTests.cs:47` (verified via grep).

**Why it's a finding.** Every WinUI element observed during an Arrange in a long-running session leaves an entry. Tab switches, virtualized list scroll, route changes, modal dialogs — each creates and destroys hundreds to thousands of elements; their ETW `ElementId`s (kernel handle integers) never repeat in practice and are never reclaimed. Over an 8-hour dev session with `ShowLayoutCost` on, this monotonically accumulates RAM. The flag is dev-only, so production is not exposed, but a long-running selftest harness is.

**Severity.** Medium when `ShowLayoutCost` is on; otherwise N/A.

**Recommendation.**
1. Add an LRU cap to `_elementRects` (e.g. 16 384 entries, drop oldest on insert).
2. Periodically prune `_idToComponent` for ids whose component has been unmounted. The current `UnregisterComponent` (`LayoutCostAttribution.cs:74`) calls `_spatial.RemoveComponent(id)` but does **not** walk `_idToComponent` to remove ids that were attributed to that component (the comment on `PointerMap.Untrack` explicitly says it's not safe; that needs a different design — e.g. piggyback on a flush-time prune that drops ids whose owner is no longer in `_rollups`).
3. Optionally call `SpatialIndex.ForgetElement` from a periodic flush.

---

### F-5 (Medium, conditional) — `EventPairing._stacks` and per-thread `Stack<PairingFrame>` grow without bound

**Location.** `src/Reactor/Hosting/Etw/EventPairing.cs:38, 45-79`.

**Why it's a finding.**
1. `_stacks` is keyed by `(int threadId, LayoutEventKind kind)`. Thread IDs are reused, but new threads get new IDs and the dictionary is never pruned outside `Reset()` (only called on consumer stop). Long-running apps with thread-pool growth or custom-threaded layout (XAML's parallel layout when enabled) accumulate entries.
2. The per-thread `Stack<PairingFrame>` has no max-depth cap. If the XAML provider ever ships a Begin without an End — manifest mismatch on a Windows update, dropped event under high pressure (the consumer thread already comments on this case at line 84-87), or a synthesized event from another in-process EventSource (T9) — the stack grows monotonically.

The current "log mismatched End once and `stack.Clear()`" path (line 95-102) handles dropped Begins; it does **not** handle dropped Ends, which is the unbounded direction.

**Severity.** Medium when `ShowLayoutCost` is on. The realistic vector is Begin-without-End from a manifest skew on a future Windows release; not an attack today, but the lack of any cap means a single skew-day causes runaway memory.

**Recommendation.**
1. Cap each per-thread stack at e.g. 1024 frames; on overflow, log once and clear.
2. Periodically prune `_stacks` entries whose threadId is no longer alive (compare against `Process.GetCurrentProcess().Threads`).
3. Bound `_stacks` itself at a reasonable count (e.g. 256 entries) and evict LRU.

---

### F-6 (Info) — `ShowErrorFallback` renders `ex.Message` into a selectable TextBlock

**Location.**
- `src/Reactor/Hosting/ReactorHost.cs:743-748`
- `src/Reactor/Hosting/ReactorHostControl.cs:534-539`
  ```csharp
  Text = $"Render error: {ex.GetType().Name}: {ex.Message}",
  TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
  IsTextSelectionEnabled = true,
  ```

**Why it's a finding.** The desktop user is in TCB so showing them the error is fine. The cross-trust concern is the **screenshot** path in Chunk 02 (`DevtoolsTools` `screenshot` tool) — an MCP agent with screenshot capability captures whatever's visible, including this TextBlock. Combined with the loopback-trust open question for Chunks 01/02, this becomes "unauthenticated local agent reads exception messages." Tagged as Info because the primary leak channel is the screenshot tool, which Chunk 02 already owns.

**Severity.** Info; cross-references Chunk 02.

**Recommendation.** Have the error fallback show only `ex.GetType().Name` plus a short prefix of the message, or omit the message entirely in `ReactorFeatureFlags.RedactErrorMessages` mode (also useful for production releases that ship with this fallback enabled). Document the Chunk 02 cross-link.

---

### F-7 (Low) — `--devtools screenshot --out` and `--devtools list --out` write to attacker-controllable paths with no normalization

**Location.** `ReactorApp.cs:265, 300`.
```csharp
File.WriteAllBytes(outPath, capture.Png);
…
File.WriteAllLines(options.ListOutputPath, names);
```

**Why it's a finding.** Both paths come from `DevtoolsCliOptions` which is parsed from `Environment.GetCommandLineArgs()`. The chunking-doc trust model says "the local developer machine running `mur` / VS Code extension is trusted" so this is in TCB — but the `mur devtools` supervisor or VS Code extension may forward strings that originated in a workspace file or webview. There is no path normalization, no UNC-share rejection, no symlink check. A `--out \\attacker\share\out.png` or `--out C:\Windows\System32\drivers\etc\hosts.png` would be written verbatim.

**Severity.** Low because the surface is dev-only, but the property "screenshot outputs are written to attacker-controllable paths" is worth recording explicitly.

**Recommendation.** Reject UNC paths (`\\?\UNC\…`, `\\server\…`), resolve `..` segments, and require the parent directory exist as a non-symlink before write. Pair with Chunk 05's CLI ↔ devtools client review — that chunk owns the supervisor that passes flags through.

---

## 7. Open questions

These need a yes/no from the team before the next chunk pass closes.

1. **What's the policy on PII in `Microsoft-UI-Reactor` ETW payloads?** The current code emits exception messages, selector strings, and class names — three different channels with three different leak profiles. A one-line policy ("no `WriteEvent` arg may carry runtime user data; class names are out of scope") closes the entire question.
2. **Is EventPipe (no-privilege, same-UID) considered a trust boundary?** The `dotnet-trace` example in the EventSource summary (`ReactorEventSource.cs:17-18`) advertises the path but doesn't note that any local process under the same UID can read every event. Either the trust model says "same-UID = trusted" (in which case document and stop worrying) or it says "no" (in which case F-1, F-2 escalate and an "include user content" keyword needs to be invented and gated).
3. **`ShowLayoutCost` lifetime contract.** The flag currently is documented as "post-init flips require a host restart" (`ReactorHost.cs:62-64`), but the code at `ReactorHost.cs:268-284` *does* support flag toggling. The unbounded-collection findings (F-4, F-5) only matter under the toggleable interpretation; if the contract is genuinely "set at init only", a single static lifetime simplifies the cleanup story.
4. **Is the `Microsoft-Windows-XAML` provider GUID stable across SDKs?** `LayoutEtwConsumer.cs:34-35` hardcodes it; the file's comment at lines 38-44 already documents that task IDs aren't stable. If the GUID itself ever changes the consumer silently produces nothing — fine for a dev tool, worth a one-line explicit acceptance.
5. **What happens if a hostile in-process EventSource with the same provider name registers first?** Managed `EventSource` enforces unique provider names per process, but a NativeAOT-loaded plugin or a TraceLogging provider with the same name could conflict. Out of immediate scope, worth a follow-up.
6. **`ReactorApplication.OnUnhandledException` static `Func<Exception, bool>` callback.** That's a process-wide singleton hook (`ReactorApp.cs:679`); a single mis-set callback can swallow every exception. Not a security issue but a foot-gun worth marking.

---

## 8. Out-of-scope referrals

| Item | Belongs to |
|---|---|
| `OverlayHostWiring`, `ReconcileHighlightOverlay` (the highlight overlay sub-renderer touched in render loop). | Touched here for completeness; primary review is implicit in Chunk 14 (reconciler) — call out for that chunk to verify the highlight buffer's `MaxPendingElements = 200` cap is sufficient. |
| `DevtoolsLogger`, `LogCaptureBuffer`, `LogCaptureInstall`, `DevtoolsMcpServer.IsAnotherSessionActive` — invoked from `ReactorApp.RunRunSubverb`. | Already covered by Chunk 02 (handlers) and Chunk 01 (transport). F-1 and F-2 cross-reference. |
| `ScreenshotCapture.CaptureWindow` — invoked from screenshot subverb. | Chunk 02. |
| Path-normalization for `--out` flags. | Chunk 05 (`mur` CLI ↔ devtools client) is the right place to enforce this — F-7 logged here for handoff. |
| The fact that `ReactorApp.FindComponentType` does case-insensitive `Assembly.GetTypes()` walks across all loaded assemblies (devtools-only). | Chunk 14 (reconciler / component model) — type-resolution semantics. |
| `XamlInterop.XamlPageElement.PageType` — accepts a `Type` and calls `Frame.Navigate(el.PageType, el.Parameter)`. The `Type` and `Parameter` come from app code (in TCB), but if Chunk 13 (navigation lifecycle) decides to accept `PageType` from persisted state, this is a deserialization gadget. | Chunk 13. |
| `PageHelper.Mount` passes `e.Parameter` (untrusted under deep-link routing) to `IPropsReceiver.SetProps`. | Chunk 13. |
| `ReactorCoreXamlMetaDataProvider.CoreXamlType.CreateFromString` calls `Enum.Parse(UnderlyingType, input, ignoreCase: true)`. Inputs come from XAML markup (build-time, trusted). | Out of scope. |

---

## Summary of actionable findings

| ID | Severity | Title |
|---|---|---|
| F-1 | High | `RenderError` ETW event ships unsanitized `exception.Message`. |
| F-2 | Med | `McpCallStart` ETW event ships raw selector string. |
| F-3 | Info | Component class names leak through ETW (documented design). |
| F-4 | Med (conditional) | `PointerMap` / `SpatialIndex` per-element dictionaries grow without bound; `ForgetElement` not wired in production. |
| F-5 | Med (conditional) | `EventPairing._stacks` and per-thread stacks grow without bound on dropped Ends or thread-ID churn. |
| F-6 | Info | `ShowErrorFallback` renders full `ex.Message`; readable via Chunk 02 screenshot tool. |
| F-7 | Low | `--devtools screenshot --out` / `--devtools list --out` write attacker-controllable paths with no normalization. |

F-1 is the sole High and is mechanical to fix. F-4 and F-5 are the right size for a follow-up "layout-cost session lifetime + bounded-state" change. The rest are documentation or design-policy items rather than code fixes.
