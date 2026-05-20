# Swallowed-error audit — spec 044

Companion to [`docs/specs/044-tracing-and-logging-cleanup-design.md`](../044-tracing-and-logging-cleanup-design.md)
and the implementation task list at [`docs/specs/tasks/044-tracing-and-logging-cleanup-implementation.md`](../tasks/044-tracing-and-logging-cleanup-implementation.md).

This file is the permanent record of the spec §6.7 decision Reactor
made at each `catch (Exception ex) { Debug.WriteLine(...); }` site
when the framework's diagnostics surfaces were migrated from
contributor-only `Debug.WriteLine` to release-visible
`Microsoft-UI-Reactor` ETW events. Every entry maps a site to one of
five verdicts:

- **Keep** — broad catch is correct (user-callback isolation, dispose
  best-effort, COM-API quirk where the failure class is large and not
  yet narrowed). Replace `Debug.WriteLine` with `DiagnosticLog.SwallowedError`
  but leave the catch shape alone.
- **Narrow** — the catch should filter on a specific exception type
  and/or HRESULT range (`catch (COMException ex) when (ex.HResult is
  HResults.X or HResults.Y)`). Anything outside the filter propagates.
- **Propagate** — the catch should be deleted; this is a bug-class
  failure that the caller needs to see.
- **Replace with `TryXxx`** — the call site has a `bool TryX(out
  result)` shape underneath; thread the return code instead of
  catching.
- **Promote to typed event** — the diagnostic graduates to a
  subsystem-specific `ReactorEventSource` event (e.g.
  `JumpListSaveFailed(int hr)`).

Each entry also names the migration commit so the verdict is
auditable against the working code.

> **Scope discipline.** The spec scope (§44 task doc preamble) is
> *the minimum change required to make Reactor's release-build
> diagnostics visible to app developers*. The Keep migration alone
> delivers that — every error/HR-reporting `Debug.WriteLine` in
> `src/Reactor/` now routes through `DiagnosticLog` and lands on the
> ETW surface. The Narrow / Propagate / Replace-with-TryXxx /
> Promote-to-typed-event verdicts are followups that gate on a
> subsystem subject-matter review.

---

## Verdict distribution (current state)

| Verdict | Count | Shipped | Deferred |
|---|---|---|---|
| Keep (iteration sibling-independence) | 8 | 8 | — |
| Narrow (specific exception type / HR filter) | 36 | 33 | 3 (Shell HResultFailed already narrowed; typed-event promotion deferred) |
| Propagate (no catch — user / framework bug surfaces) | 12 | 12 | — |
| Replace with `TryXxx` | 10 | 0 | 10 (Win32 P/Invoke reporters, Phase 4.8) |
| Promote to typed event | 18 | 9 (Navigation 6 + Intl 1 + Persistence 2) | 9 (Shell COM-calls 5 + ConnectedAnimation 4, Phase 4.6) |
| Deleted (dead-defensive try/catch) | 9 | 9 | — |

Spec §6.7.4 worry-threshold for `Propagate` is 20; we're at 12.

The dramatic shift from "56 Keep" in the first audit pass to "8 Keep + 12 Propagate + 9 Deleted + 33 Narrow" came from applying the §6.7.2 narrowing properly to ReactorWindow.cs (29 sites) and the related Hosting code. The first pass migrated `Debug.WriteLine` → `DiagnosticLog.SwallowedError` with the catch shape unchanged ("Keep"); the second pass actually applied the §6.7.2 rule that broad `catch (Exception)` is wrong almost everywhere it isn't iteration sibling-independence or genuine fail-safe-to-default behavior.

---

## Method

Every site listed below was inspected against the template in spec
§6.7.1. For Keep verdicts, the template is collapsed to the audit
trail (site → migration commit) because every Keep entry shares the
same justification: "the surrounding code is a best-effort
operation whose semantics are 'do this if possible, otherwise
continue' — propagation would crash the dispatcher". For Narrow /
Promote / TryXxx verdicts, the per-site context is included.

---

## File-grouped sites

Sites are grouped by source file in alphabetical order, matching
the inventory in §3.3 of the task doc.

### `src/Reactor/Core/Localization/IntlAccessor.cs` — Phase C.3 (commit `7312ce73`)

| Site | Verdict | Notes |
|---|---|---|
| `ResolvePattern` missing-key (×2 collapsed into 1) | Promote to typed event | `IntlMissingKey(key, locale, fellBack)` under `Keywords.Intl`. Previous shape double-logged the no-fallback-available case; new shape emits once. PII: key is developer-authored .resw identifier. |
| `Message` format failure | Keep + DiagnosticLog | `LogCategory.Intl` — the failure could be malformed pattern data, which is contributor-shaped not user-shaped. |
| `RichMessage` format failure | Keep + DiagnosticLog | Same as above. |

### `src/Reactor/Core/Navigation/NavigationDiagnostics.cs` — Phase C.2 (commit `e2a755b2`)

| Site | Verdict | Notes |
|---|---|---|
| `OnNavigationRequested` | Promote to typed event | `NavigationRequested(routeTemplate)` under `Keywords.Navigation`. |
| `OnNavigationCompleted` | Promote to typed event | `NavigationCompleted(routeTemplate, durationMs)`. |
| `OnNavigationCancelled` | Promote to typed event | `NavigationCancelled(routeTemplate, reason)`. |
| `OnNavigationCacheHit` | Promote to typed event | `NavigationCacheHit(routeTemplate)`. Verbose-level. |
| `OnNavigationCacheMiss` | Promote to typed event | `NavigationCacheMiss(routeTemplate)`. Verbose-level. |
| `OnNavigationCacheEviction` | Promote to typed event | `NavigationCacheEvict(routeTemplate, reason)`. Verbose-level. |
| `OnTransitionStarted` | Promote to typed event | `NavigationTransitionStarted(routeTemplate)` (new event id 33). |
| `OnTransitionCompleted` | Promote to typed event | `NavigationTransitionCompleted(routeTemplate, durationMs)` (id 34). |
| `OnDeepLinkResolved` | Promote to typed event | `NavigationDeepLinkResolved(matched, routeCount)` (id 35). **PII (§6.2.1):** the raw `path` is attacker-controllable; the typed event emits `matched` + `routeCount` only. The `NavigationDiagnosticsEtwBridgeTests.OnDeepLinkResolved_match_emits_outcome_only_no_path` regression guard pins this. |

### `src/Reactor/Core/Persistence/JsonFileStore.cs` — Phase C.5 (commit `21e22e1c`)

| Site | Verdict | Notes |
|---|---|---|
| Round-trip read success | Promote to typed event | Emits `PersistenceRead(storeKind: "json-file", sizeBytes)`. Storekind label — never the path (§6.2.1). |
| Round-trip write success | Promote to typed event | Emits `PersistenceWrite(...)`. Same PII discipline. |
| Read oversize | Promote to typed event | Emits `PersistenceRejected(storeKind, reason: "oversize")`. |
| Read narrow exceptions (`IOException`, `JsonException`, `FormatException`, `UnauthorizedAccessException`) | Narrow + DiagnosticLog | `catch (IOException) / catch (JsonException) ...` instead of `catch (Exception)`. Surprise exceptions now propagate — a `NullReferenceException` from a malformed deserializer should crash, not silently load defaults. |
| Write narrow exceptions | Narrow + DiagnosticLog | Same shape. |

### `src/Reactor/Core/Persistence/PackagedSettingsStore.cs` — Phase C.5

| Site | Verdict | Notes |
|---|---|---|
| Read narrow exceptions (`InvalidOperationException`, `COMException`, `UnauthorizedAccessException`) | Narrow + DiagnosticLog | The WinRT call surface throws `InvalidOperationException` (HR `0x80073D54`) on every unpackaged process; that's the actual failure class here, not `IOException`/`JsonException` as the spec's draft list said. Storekind `"packaged-settings"`. |
| Write narrow exceptions (+ `FormatException` on the base64 path) | Narrow + DiagnosticLog | Same. |

### `src/Reactor/Core/Persistence/WindowPlacementCodec.cs` — Phase C.5

| Site | Verdict | Notes |
|---|---|---|
| Win32 `GetWindowPlacement` failure | Promote to typed event | `DiagnosticLog.HResultFailed(LogCategory.Persistence, ..., GetLastError())`. |
| `IsPlausiblePlacement` reject | Promote to typed event | `PersistenceRejected("placement", reason)` with a short reason label. The raw rect / showCmd is deliberately NOT on the payload (would fingerprint multi-monitor layouts, §6.2.1). |
| `monitorCount` reject | Promote to typed event | Same. |
| `EndOfStreamException` reject | Promote to typed event | Same. |
| Outer catches | Narrow + DiagnosticLog | Narrowed to `IOException`. |

### `src/Reactor/Core/Reconciler.cs` — Phase C.7b (commit `054c53ef`) + Phase C.8 (commit `21cd6ef9`)

| Site | Verdict | Notes |
|---|---|---|
| Navigation lifecycle callback dispatch | Keep + DiagnosticLog | User-callback isolation per §6.7.3. Already shipped in C.7b. |
| ConnectedAnimation `PrepareToAnimate` (mount path) | Keep + DiagnosticLog | LogCategory.Reactor. §6.7.4 calls for "Promote + Narrow" — deferred along with the rest of 4.6. |
| ConnectedAnimation `PrepareToAnimate` (update path) | Keep + DiagnosticLog | Same. |
| ConnectedAnimation `GetAnimation` | Keep + DiagnosticLog | Same. |
| ConnectedAnimation `TryStart` | Keep + DiagnosticLog | Same. |
| `ApplyThemeBindings` | Keep + DiagnosticLog | LogCategory.Theme — the catch wraps a XAML `Style.Load` compile. Could narrow to `XamlParseException` in a follow-up. |

### `src/Reactor/Core/Reconciler.Mount.cs` — Phase C.8 (commit `21cd6ef9`)

| Site | Verdict | Notes |
|---|---|---|
| `ContentDialog.ShowAsync + OnClosed` | Keep + DiagnosticLog | User-callback isolation per §6.7.3 — the try wraps both `ShowAsync` AND the user-supplied `OnClosed` delegate. Cannot narrow without splitting the try-catch into two; deferred. |

### `src/Reactor/Core/RenderContext.cs` — Phase C.6 (commit `90d516b0`) + Phase C.9 narrowing

| Site | Verdict | Notes |
|---|---|---|
| `UseCommand.ExecuteAsync` | **Narrow (try/finally — no catch)** | Phase C.9: fire-and-forget `Task.Run` wraps the user action with `try { await asyncAction(); } finally { guardRef.Current = false; setIsExecuting(false); }`. The framework state is restored before unwind; the user's throw faults the Task and surfaces via `Task.UnobservedTaskException` rather than being swallowed under `SwallowedError`. The earlier "Keep + DiagnosticLog" shape was hiding user bugs — apps couldn't tell their command was broken without subscribing to ETW. |
| `UseCommand<T>.ExecuteAsync` | **Narrow (try/finally — no catch)** | Same shape. |
| `UseEffect` cleanup (FlushEffects phase 1) | Keep + DiagnosticLog | Iteration sibling-independence — slot i's failure must not block slots i+1…n in the same flush. The loop's invariant (forward progress through all cleanups) requires the broad catch. |
| `UseEffect` effect (FlushEffects phase 2) | Keep + DiagnosticLog | Same. |
| `RunCleanups.effectCleanup` | Keep + DiagnosticLog | Same. |
| `RunCleanups.persistedSave` | Keep + DiagnosticLog | Same — persisted-slot independence. The try-catch wraps the user contact point (`IPersistedStateScope.Set`); the surrounding hook-iteration loop is outside. |

### `src/Reactor/Hosting/Etw/LayoutEtwConsumer.cs` — Phase C.7a (commit `b761a7a1`)

| Site | Verdict | Notes |
|---|---|---|
| 7 error-swallow catches (provider start, session enable, parser, etc.) | Keep + DiagnosticLog | LogCategory.LayoutCost. |
| 5 pure-trace `Debug.WriteLine` (session started / parser output / orphan cleanup) | Keep as `Debug.WriteLine` | Framework-internal per spec §6.3 carve-out. |

### `src/Reactor/Hosting/ReactorWindow.cs` — Phase C.8 (commit `21cd6ef9`) + Phase C.9 narrowing

Phase C.8 migrated 29 `Debug.WriteLine` → `DiagnosticLog` with the catch
shape unchanged. Phase C.9 applies the actual §6.7.2 narrowing per site:

| Group | Sites | Verdict | After |
|---|---|---|---|
| Pure-advisory user callbacks | `SizeChanged`, `StateChanged`, `Closing` | **Propagate** — try/catch deleted | User throw goes to dispatcher's UnhandledException; developer sees the bug. Previous swallow silently treated thrown `Closing` handler as "didn't cancel," which was worse than crashing. |
| User callback with framework cleanup after | `Closed?.Invoke` | **try/finally** | User throw propagates AND `RemoveOwned` / `UnregisterWindow` / `Dispose` still run. Handles the limp-along case where the app set `Application.UnhandledException.Handled = true`. |
| WinUI AppWindow / Window API surface | `Title.set`, `Presenter.apply`, `IsShownInSwitchers.set`, `ExtendsContentIntoTitleBar.set`, `InitialResize`, `SetOwner`, `FirstDpiResize`, `Hide`, `Show`, `Close`, `SetSize`, `SetPosition`, `CenterOnScreen`, `ResolveCurrentState`, `TryApplyExeIconFallback`, `TryApplyInitialPlacement`, `ResolveOwnerDisplayArea`, all five event unsubscriptions in `Dispose` (×5) | **Narrow** — `catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))` (the well-known `RPC_E_DISCONNECTED` / `E_HANDLE` / `RPC_E_SERVERFAULT` / `CO_E_OBJNOTCONNECTED` set) | Anything outside that HR set propagates as a genuine bug. |
| Iteration sibling-independence | `IClosingGuard.CanClose()`, owned-window-cascade `child._window.Close()` | **Keep + DiagnosticLog (annotated)** | Closing-guard fail-safe-to-cancel is documented behavior (spec 036 §3.4 test pins it); owned-cascade sibling independence is spec 036 §9. Both have inline comments naming the contract. |
| Framework dispose chain | `_messageMonitor.Dispose()` → `_host.Dispose()` → `_persistedScope.Dispose()` → `_thumbnailToolbar?.Dispose()` | **try/finally chain** | All four disposes run regardless of which throws; first exception propagates. No swallowing — a framework Dispose bug should surface. |
| Dead-defensive try/catch | `QueryDpiForWindow`, `WM_GETMINMAXINFO.apply`, `GetDpiForSystemFallback`, `NativeIcon.DestroyIcon`, `MonitorEnumeration.Snapshot`, `TryRestorePersistedPlacementCore`, `TrySavePersistedPlacement` | **Try deleted** | The wrapped operations are P/Invokes on `nint` that can't throw at the marshal layer, or downstream calls that already narrow internally and return sentinel values. The outer try/catch was hiding nothing real. |

LogCategory.Hosting except for the two persistence-shaped placement
sites (LogCategory.Persistence) and the user-event sites which now
have no catch at all.

### `src/Reactor/Hosting/Shell/JumpListComInterop.cs` — Phase C.4 (commit `301593bc`)

| Site | Verdict | Notes |
|---|---|---|
| `BeginList`, `AddUserTasks`, `AppendCategory`, `AppendKnownCategory.Recent`, `AppendKnownCategory.Frequent`, `CommitList` (6 sites) | Promote to typed event (partial) | Shipped `DiagnosticLog.HResultFailed(LogCategory.Shell, "JumpList.<op>", hr)`. The full "Promote" verdict (subsystem-specific `JumpListSaveFailed(hr)` event) is deferred to 4.6. |

### `src/Reactor/Hosting/Shell/ThumbnailToolbar.cs` — Phase C.4

| Site | Verdict | Notes |
|---|---|---|
| `Update vs Add Buttons` | Promote to typed event (partial) | Same shape — `HResultFailed` shipped, typed `ThumbnailToolbarSetButtonsFailed` deferred. |

### `src/Reactor/Hosting/Shell/TrayFlyoutHostWindow.cs` — Phase C.4

| Site | Verdict | Notes |
|---|---|---|
| `GetDpiForMonitor` | Promote to typed event (partial) | Same. |

### `src/Reactor/Markdown/Md4cParser.Block.cs` — Phase C.1 (commit `79b27be6`)

| Site | Verdict | Notes |
|---|---|---|
| 4 `Debug.Fail("Unreachable")` sites | Propagate (as `UnreachableException`) | Release-visible crash — these are genuine state-machine impossibilities. The Reconciler.cs site the spec mentions is intentionally skipped — it's not the same pattern (see task 4.1). |

### `src/Reactor/Core/Reconciler.cs:2635` (typed-ref assert) — Skipped intentionally

The spec §4.3 also mentioned 1 site in `Reconciler.cs:~2635`. Audit
note: that site is not a `Debug.Fail("Unreachable")` — its message
is `"ElementRef<{T}> attached to a {U}. Use ElementRef<U> or
untyped ElementRef."` — and the containing `AssertTypedRefMatch`
method is already `[Conditional("DEBUG")]`. Leaving as-is until a
reviewer requests a behavior change.

---

## Win32 P/Invoke `TryXxx` candidates — Deferred (Phase 4.8)

Spec §6.7.4 calls for ~10 sites where `bool Try* (out int hr)` is
the right shape. Each already returns a `bool`, so the conversion is
mechanical:

- `src/Reactor/Hosting/Messaging/WindowMessageMonitor.cs` — 6 P/Invoke wrappers.
- `src/Reactor/Hosting/Persistence/MonitorEnumeration.cs` — 2 `EnumDisplayMonitors`-shape callers.
- `src/Reactor/Hosting/WindowIcon.cs` — 2 HICON loaders.

Audit verdict on each is *Replace with `TryXxx`*; none have shipped
yet because the GetLastError path still needs the swallowed-error
audit trail until the conversion lands. Tracked as task 4.8.

---

## Shell typed-event promotion — Deferred (Phase 4.6)

Spec §6.7.4 calls for ~15 sites in the Shell namespace to graduate
from the generic `HResultFailed` event to subsystem-specific typed
events. The Phase C.4 migration shipped the `HResultFailed` shape
for 8 of those, which delivers the release-visibility goal. The
typed events (`JumpListSaveFailed(hr)`,
`ThumbnailToolbarSetButtonsFailed(hr)`, etc.) are a downstream
ergonomic improvement — an MCP agent filtering on
`Keywords.Shell & EventName=JumpListSaveFailed` is more discoverable
than greppping `operation="JumpList.Begin"` strings. Tracked as task
4.6.

---

## Audit completeness against §3.5

- [x] Every site in the §0.3 inventory maps to exactly one entry in
  this file or is explicitly carved out as framework-internal
  (`Debug.Assert`, pure trace prints).
- [x] Verdict distribution recorded at the top.
- [ ] Per-site line-by-line review by a second pair of eyes — invited
  via the PR that introduces this file.
- [x] No code changes in this PR — it's the audit's permanent home.
