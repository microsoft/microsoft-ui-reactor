# Tracing and Logging Cleanup — Implementation Tasks

Derived from: `docs/specs/044-tracing-and-logging-cleanup-design.md`
Tracking bug: [microsoft/microsoft-ui-reactor#323](https://github.com/microsoft/microsoft-ui-reactor/issues/323)

> **Scope discipline.** The point of this work is **the minimum change required to make Reactor's release-build diagnostics visible to app developers.** The headline deliverables are: the `DiagnosticLog` helper, expanded `ReactorEventSource` coverage, the swallowed-error audit + migration, an in-process `EventListener` subscription helper, a `reactor.logs` MCP wire-up, and the docs page that ties it together. Everything else in spec 044 (sibling `ILogger` package, `IDevtoolsConsole` abstraction, Roslyn analyzer CI gate) is **deferred** — captured at the bottom under "Deferred / out of scope" so the next contributor knows it exists, but it does not block closing #323.

Conventions:
- Core diagnostics types live under `src/Reactor/Core/Diagnostics/` (new folder): `DiagnosticLog.cs`, `LogCategory.cs`, `HResults.cs`. `ReactorEventSource.cs` already exists under `src/Reactor/Core/` and stays there.
- Public subscription API (`ReactorTrace`, `ReactorEvent`) lives under `src/Reactor/Diagnostics/` (new folder) to match the public namespace `Microsoft.UI.Reactor.Diagnostics`.
- Unit tests under `tests/Reactor.Tests/Diagnostics/` (new folder). The `ReactorTraceCollector` test harness lives there too so production code stays free of it.
- The audit lives at `docs/specs/044/swallowed-error-audit.md` (new folder under `docs/specs/`).
- The user guide page is generated — edit `docs/_pipeline/templates/diagnostics.md.dt`, then run `mur docs compile`. Never hand-edit `docs/guide/diagnostics.md`.
- Public API additions need XML doc comments (no `CS1591`).
- Code must compile under `Reactor.slnx` warnings-as-errors **and** `IsAotCompatible=true` with trim warnings promoted to errors (the core Reactor library already enforces this — see spec §12).

A task is "done" only when:
1. Code compiles clean under `Reactor.slnx` warnings-as-errors and AOT/trim-warnings-as-errors.
2. Public API surface has XML doc comments.
3. Tests cover both the happy path and the documented failure modes for that task.
4. `dotnet test tests/Reactor.Tests` and `dotnet test tests/Reactor.SelfTests` are green.

---

## Phase 0 — Decisions captured & scaffolding

### 0.1 Resolve the spec's open questions before code starts

- [x] Confirm **Q2** (sibling `Microsoft.UI.Reactor.Logging.Extensions` package): **deferred** — Phase E is out of scope for this implementation. Record the deferral in the spec header.
- [x] Confirm **Q3** (`Debug.WriteLine` Roslyn analyzer): **deferred** — Phase C-gate is out of scope. Record the deferral in the spec header.
- [x] Confirm **Q4** (`Trace.WriteLine` listener fate): **leave as-is** in `LogCaptureInstall`. Record in spec §10.
- [x] Confirm the public namespace for `ReactorTrace` / `ReactorEvent`: `Microsoft.UI.Reactor.Diagnostics`. Spec §6.4 already implies this; commit it in the spec header so Phase D doesn't revisit.

### 0.2 New files — empty placeholders compile first, populated later

- [x] Create `src/Reactor/Core/Diagnostics/` folder and `LogCategory.cs` with the enum from spec §6.1 (`Reactor, Hosting, Persistence, Navigation, Intl, Theme, Shell, LayoutCost, Devtools, Markdown`).
- [x] Create `src/Reactor/Core/Diagnostics/HResults.cs` with the named constants from spec §6.7 (start with the ones listed; grow during audit).
- [x] Create `src/Reactor/Core/Diagnostics/DiagnosticLog.cs` with method signatures only (`SwallowedError`, `HResultFailed`) — bodies wired in 1.1.
- [x] Create `src/Reactor/Diagnostics/` folder and `ReactorTrace.cs` + `ReactorEvent.cs` shells with the public API surface from spec §6.4 (signatures + XML doc, body in Phase D).
- [x] Verify `Reactor.slnx` builds clean with these placeholders.

### 0.3 Audit baseline — inventory every site touched by Phase C

- [ ] Run a tree-wide grep for `Debug\.WriteLine`, `Debug\.Fail`, `Debug\.Assert`, and `catch \(Exception` in `src/Reactor/`. Save the raw output under `docs/specs/044/inventory-pre.txt` (not checked in long-term; reference only during the audit).
- [ ] Cross-check the count against spec §4.2 ("~150 sites across 47 files", "~80 swallowed-error sites", "~20 HR-diagnostic sites"). If counts diverge significantly, note in the audit PR.
- [ ] List each `Debug.Fail("Unreachable")` site (spec §4.3 calls out 4 in `Md4cParser` + 1 in `Reconciler.cs`). Confirm count.

---

## Phase A — `DiagnosticLog` helper + generic events (1 PR)

Closes the foundation. Once this lands, every catch site in Phase C can route through the two generics regardless of subsystem-specific event coverage.

### 1.1 Populate `DiagnosticLog`

- [x] Implement `DiagnosticLog.SwallowedError(LogCategory, string operation, Exception)` per spec §6.1. The public method is **not** `[Conditional]`; only the DEBUG mirror is.
- [x] Implement `DiagnosticLog.HResultFailed(LogCategory, string operation, int hr)` per spec §6.1.
- [x] Confirm `ex.Message` is **never** placed on the ETW payload (PII discipline, spec §6.2.1). Only `ex.GetType().Name`.
- [x] Confirm both helpers gate with `ReactorEventSource.Log.IsEnabled(...)` at the call site before invoking the EventSource method (matches existing convention, spec §6.2).

### 1.2 Add the two generic events to `ReactorEventSource`

- [x] Add `Keywords.Errors` events `SwallowedError(string category, string operation, string exceptionType)` and `HResultFailed(string category, string operation, int hr)` to `src/Reactor/Core/ReactorEventSource.cs`. Level `Warning`, keyword `Errors`.
- [x] Each event method internally re-checks `IsEnabled(EventLevel.Warning, Keywords.Errors)` before calling `WriteEvent` (existing convention).
- [x] Add an `EventAttribute` with a stable, unique `EventId` for each (use next free IDs after the current 15).

### 1.3 Add the six new keywords

- [x] Extend `ReactorEventSource.Keywords` with `Hosting (0x80)`, `Persistence (0x100)`, `Navigation (0x200)`, `Intl (0x400)`, `Theme (0x800)`, `Shell (0x1000)` per spec §6.2.
- [x] No bit overlaps; total stays within `EventKeywords` ulong range.

### 1.4 Phase A tests

- [x] Add `tests/Reactor.Tests/Diagnostics/DiagnosticLogTests.cs`:
  - [x] `SwallowedError` writes the exception **type** but not the **message** to the ETW payload.
  - [x] `HResultFailed` writes the HR as an `int` and the category as the `LogCategory` enum's `ToString()`.
  - [x] Both helpers are no-ops (cost-of-disabled) when keyword `Errors` is disabled — assert via an `EventListener` that does not enable the keyword.
  - [ ] DEBUG-only mirror is exercised by a `[Conditional("DEBUG")]`-aware test (or a test that runs only under DEBUG via `#if DEBUG`).

### 1.5 Phase A acceptance

- [x] `dotnet build Reactor.slnx` clean (warnings-as-errors, AOT/trim).
- [x] `dotnet test tests/Reactor.Tests` green.
- [x] No existing call sites changed — Phase A is **additive only**.

---

## Phase B — Expand `ReactorEventSource` subsystem coverage (1 PR)

Adds the typed events that the §6.7 "Promote to typed event" verdicts will use. Until these land, Phase C migrates broad catches through the two generics from Phase A.

### 2.1 Add Hosting events

- [x] `WindowOpened(string windowType, long hwnd)` — Informational, Hosting.
- [x] `WindowClosed(string windowType, long hwnd)` — Informational, Hosting.
- [x] `WindowDpiChanged(string windowType, int oldDpi, int newDpi)` — Informational, Hosting.
- [x] `BackdropMaterializationFailed(string kind, string exceptionType)` — Warning, Hosting.
- [x] PII review: `windowType` is the C# type name (developer-authored, OK). Window titles are **not** emitted (spec §6.2.1).

### 2.2 Add Persistence events

- [x] `PersistenceRead(string storeKind, int sizeBytes)` — Informational, Persistence.
- [x] `PersistenceWrite(string storeKind, int sizeBytes)` — Informational, Persistence.
- [x] `PersistenceRejected(string storeKind, string reason)` — Warning, Persistence (oversize, corrupt, schema mismatch).
- [x] PII review: file paths are **not** emitted; use a `storeKind` label (`"settings"`, `"placement"`, etc.).

### 2.3 Add Navigation events

- [x] `NavigationRequested(string routeTemplate)` — Informational, Navigation.
- [x] `NavigationCompleted(string routeTemplate, double durationMs)` — Informational, Navigation.
- [x] `NavigationCancelled(string routeTemplate, string reason)` — Informational, Navigation.
- [x] `NavigationCacheHit(string routeTemplate)` — Verbose, Navigation.
- [x] `NavigationCacheMiss(string routeTemplate)` — Verbose, Navigation.
- [x] `NavigationCacheEvict(string routeTemplate, string reason)` — Verbose, Navigation.
- [x] PII review: **route template** (`/users/{id}`) only, never the instantiated path (spec §6.2.1).

### 2.4 Add Intl event

- [x] `IntlMissingKey(string key, string locale, bool fellBack)` — Warning, Intl.
- [x] PII review: keys are developer-authored static identifiers (OK).

### 2.5 Add Theme event

- [x] `ThemeApplyFailed(string targetType, string exceptionType)` — Warning, Theme.

### 2.6 Reserve event IDs / verify ordering

- [x] All new events get sequential `EventId`s after the Phase A additions.
- [x] Update the `ReactorEventSource` `EventId` allocation comment so future additions know the next free ID.

### 2.7 Phase B tests

- [x] Add `tests/Reactor.Tests/Diagnostics/ReactorEventSourceCoverageTests.cs`:
  - [x] One smoke test per new event that fires it and asserts the captured payload via an in-test `EventListener`.
  - [ ] Each event is allocation-free when its keyword is disabled (use `BenchmarkDotNet`-style allocation check, or assert `IsEnabled == false → no GC alloc` via `GC.GetAllocatedBytesForCurrentThread()` delta).
- [x] Add a single end-to-end test that enables `Keywords.Hosting | Persistence | Navigation | Intl | Theme | Errors`, fires one of each, and verifies all are captured (regression guard against keyword-bit overlap).

### 2.8 Phase B acceptance

- [x] `dotnet build Reactor.slnx` clean.
- [x] `dotnet test tests/Reactor.Tests` green.
- [x] Each new event has its PII policy decision documented inline (a `// PII:` comment on the event method or in a section comment).

---

## Phase C-audit — Swallowed-error audit (1 PR, docs only)

Pure documentation. No code changes. **Required before Phase C ships any migration.**

### 3.1 Create the audit file scaffold

- [x] Created `docs/specs/044/` folder.
- [x] Created `docs/specs/044/swallowed-error-audit.md` with the explanatory preamble (cross-references to spec §6.7) plus the five-verdict template.
- [x] Sites grouped by source file in alphabetical order.

### 3.2 Populate one entry per site (~80 entries)

For each `catch (Exception ex) { Debug.WriteLine(...); }` site in `src/Reactor/`, add a section using the template from spec §6.7.1. Required fields per entry:

- [ ] **Site (before)** — copy of the current catch block.
- [ ] **Operation** — the platform/SDK call inside the `try`.
- [ ] **Caller contract** — who calls this and when.
- [ ] **Observed/expected failure modes** — at the HRESULT / Win32 code level, not just exception type.
- [ ] **What we explicitly do NOT want to swallow** — the bug-class exceptions we're now happy to let propagate.
- [ ] **Why we swallow the listed cases** — single-paragraph justification.
- [ ] **Verdict** — exactly one of: Keep / Narrow / Propagate / Replace with `TryXxx` / Promote to typed event.
- [ ] **Site (after)** — the proposed post-migration code (for Keep verdicts this is just the existing shape rewritten over `DiagnosticLog.SwallowedError`).
- [ ] **Risk** — one line on what could break.
- [ ] **Owner / PR / Status** — `☐ migrated  ☐ verdict shipped` checkboxes.

### 3.3 Group entries by file per first-pass categorization (spec §6.7.4)

- [ ] `ReactorWindow.cs` swallows (~20 entries, dispose / AppWindow lifecycle, mostly Keep / Narrow).
- [ ] Shell COM calls — `JumpList*`, `ThumbnailToolbar*`, `Tray*` (~15 entries, mostly Promote to typed event).
- [ ] Win32 P/Invoke reporters — `WindowMessageMonitor`, etc. (~10 entries, mostly Replace with `TryXxx`).
- [ ] Persistence — `JsonFileStore`, `PackagedSettingsStore`, `WindowPlacementCodec` (~8 entries, Narrow to `IOException`/`JsonException`/`UnauthorizedAccessException`).
- [ ] Connected animations — `PrepareToAnimate` / `GetAnimation` / `TryStart` (~4 entries, Promote + Narrow).
- [ ] Backdrop / Theme application (~3 entries, Narrow only).
- [ ] User-callback isolation — `RenderContext` effect cleanups, command handlers, lifecycle hooks (~10 entries, all Keep per §6.7.3, but with explicit "what user contract this fulfills" notes).
- [ ] `Reconciler` swallows not covered above (residual ~10 entries).

### 3.4 Sanity-check verdict distribution

- [x] Verdict counts at the top of the audit file: 56 Keep, 9 Narrow (6 shipped, 3 deferred), 0 Propagate, 10 Replace-with-TryXxx (all deferred to 4.8), 18 Promote-to-typed-event (9 shipped, 9 deferred to 4.6).
- [x] Propagate count is 0 — well under the spec §6.7.4 worry threshold of 20.

### 3.5 Phase C-audit acceptance

- [ ] The audit file is reviewed line-by-line by a second pair of eyes (rubber-duck pass at minimum). _(Pending PR review.)_
- [x] Every site in the inventory maps to exactly one entry in the audit file or is explicitly carved out as framework-internal (Debug.Assert, pure trace prints).
- [x] No code changes in this PR — the audit file is its own commit.

---

## Phase C — Migrate `Debug.WriteLine` call sites (split across PRs by category)

Mechanical migration driven by the audit. Each PR maps to one row of spec §6.3 / §6.7.4. Sites with verdict ≠ Keep land their fix in the same PR.

### 4.1 PR: `Debug.Fail("Unreachable")` → `throw new UnreachableException(...)`

- [x] Replace 4 sites in `src/Reactor/Markdown/Md4cParser.Block.cs` (spec §4.3).
- [ ] Replace 1 site in `src/Reactor/Core/Reconciler.cs:~2635` (spec §4.3). **Skipped intentionally:** that site is not a `Debug.Fail("Unreachable")` pattern — its message is `"ElementRef<{T}> attached to a {U}. Use ElementRef<U> or untyped ElementRef."` — and the whole containing `AssertTypedRefMatch` method is already `[Conditional("DEBUG")]`. Re-asking the reviewer in a follow-up if a behavior change is desired.
- [x] Verify each replaced site is genuinely unreachable in tests (`UnreachableException` is Release-visible; we don't want it to fire in real code).

### 4.2 PR: `NavigationDiagnostics` Debug.WriteLines → typed events

- [x] Replace the 9 sites in `src/Reactor/Core/Navigation/NavigationDiagnostics.cs` with the Phase B navigation events (`NavigationRequested`, `NavigationCompleted`, `NavigationCacheHit`, etc.). The six direct mappings (Requested / Completed / Cancelled / CacheHit / CacheMiss / CacheEviction) reuse Phase B events 25-30. Three additional events were added to `ReactorEventSource` (IDs 33-35) to cover `TransitionStarted`, `TransitionCompleted`, and `DeepLinkResolved`. DeepLink intentionally drops the `path` payload (attacker-controllable per §6.2.1) and emits only `matched` + `routeCount`.
- [x] Confirm `NavigationDiagnostics` callers continue to function (no behavior change in DEBUG; new visibility in Release). Existing `NavigationDiagnosticsCoverageTests` keeps verifying the public C# event subscribers (8 tests). New `NavigationDiagnosticsEtwBridgeTests` (7 tests) exercises every `OnX` entry point and asserts the typed ETW event lands on a `Keywords.Navigation` listener with the expected payload. `OnDeepLinkResolved_match_emits_outcome_only_no_path` is the explicit §6.2.1 PII regression guard.

### 4.3 PR: `IntlAccessor` missing-key warnings → `IntlMissingKey`

- [x] Replace the 4 sites in `src/Reactor/Core/Localization/IntlAccessor.cs` with the Phase B Intl events. The 2 missing-key sites in `ResolvePattern` collapse to a single typed `IntlMissingKey(key, locale, fellBack)` emission — the previous shape double-logged on the no-fallback-available path, which the new shape fixes. The 2 format-failure catches in `Message` and `RichMessage` route through `DiagnosticLog.SwallowedError(LogCategory.Intl, ...)` because they are exception swallows, not missing-key reports. PII (§6.2.1): MessageKey is namespace + key from .resw — developer-authored only.

### 4.4 PR: HResult diagnostics → `DiagnosticLog.HResultFailed`

- [x] Replace the `Debug.WriteLine($"... HR=0x{hr:X8}");` sites in the Shell hosting code. Actual inventory found 8 sites (the spec's "~20" estimate counted candidates in `WindowMessageMonitor`/`ReactorWindow` that don't actually use the HR format — those land in 4.5 instead): 6 in `JumpListComInterop.cs` (BeginList / AddUserTasks / AppendCategory / AppendKnownCategory.Recent / AppendKnownCategory.Frequent / CommitList), 1 in `ThumbnailToolbar.cs` (Update vs Add Buttons), 1 in `TrayFlyoutHostWindow.cs` (GetDpiForMonitor). The `AppendCategory` site dropped the user-named category string from the op label (developer-authored but unbounded → safer to fold into the typed Shell event in 4.6).
- [ ] Each replacement references its audit entry by file path + line via a `// AUDIT: docs/specs/044/swallowed-error-audit.md#...` comment if also wrapped in a catch. _(deferred to the audit PR: the migrated sites are NOT inside the broader catch arms — they are HR-return-value checks, not exception swallows.)_

### 4.5 PR: ReactorWindow swallowed-error migration

- [x] Phase C.8 migrated all 29 sites to `DiagnosticLog.SwallowedError(LogCategory.Hosting, ...)`. Two persistence-placement sites route to `LogCategory.Persistence`. `using System.Diagnostics` removed.
- [x] **Narrowing landed in Phase C.9.** Spec §6.7.2 properly applied. Added `HResults.IsTeardownReentry(int hr)` helper (covers `RPC_E_DISCONNECTED`, `E_HANDLE`, `RPC_E_SERVERFAULT`, `CO_E_OBJNOTCONNECTED`). 17 WinUI API sites now use `catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))`. 3 user-callback sites (`SizeChanged`, `StateChanged`, `Closing`) had their try/catch deleted — user throws propagate to the dispatcher. 1 user-callback site with framework cleanup after it (`Closed?.Invoke`) converted to try/finally so cleanup runs but the user's exception still surfaces. Dispose chain converted to nested try/finally. 7 dead-defensive try/catch blocks deleted (P/Invokes on `nint` that can't throw at the marshal layer, or downstream calls that already narrow internally).
- [x] Two broad `catch (Exception)` blocks remain — both genuine iteration sibling-independence per §6.7.3: `IClosingGuard.CanClose()` (fail-safe-to-cancel, spec 036 §3.4 test) and owned-window cascade (spec 036 §9). Both have inline comments naming the contract.

### 4.6 PR: Shell COM-call promotion to typed events

- [ ] For the ~15 Shell sites with verdict "Promote to typed event", add the typed event to `ReactorEventSource` (specifically scoped — `JumpListSaveFailed`, `ThumbnailToolbarSetButtonsFailed`, etc., each with `int hr` payload).
- [ ] Migrate each catch to the new typed event + narrow HRESULT filter.
- [ ] Update the audit entry from `☐ migrated` to `☑ migrated  ☑ verdict shipped`.

### 4.7 PR: Persistence narrowing

- [x] Migrate `JsonFileStore`, `PackagedSettingsStore`, `WindowPlacementCodec` swallows to narrow `catch (IOException)`, `catch (JsonException)`, `catch (UnauthorizedAccessException)` plus `DiagnosticLog.SwallowedError(LogCategory.Persistence, ...)`.
  - `JsonFileStore` narrowed to `IOException` / `UnauthorizedAccessException` (+ retained `JsonException` / `FormatException`); surprise exceptions now propagate. Happy-path round-trips additionally emit Phase B `PersistenceRead` / `PersistenceWrite` with a `"json-file"` `storeKind` (NEVER the path, per §6.2.1).
  - `PackagedSettingsStore` narrowed to `InvalidOperationException` / `COMException` / `UnauthorizedAccessException` (+ `FormatException` on the base64 path). Note: the spec's narrow list was IOException/JsonException/UnauthorizedAccessException, but the WinRT call surface throws InvalidOperationException (0x80073D54) on every unpackaged process and COMException for store-level errors — those are the actual swallow types here. Storekind is `"packaged-settings"`.
  - `WindowPlacementCodec` Win32 `GetWindowPlacement` failure now routes through `DiagnosticLog.HResultFailed` with the `GetLastError` value. `IsPlausiblePlacement`, `monitorCount` and `EndOfStreamException` reject paths now emit typed `PersistenceRejected("placement", reason)` with short reason labels — the raw rect / showCmd are deliberately NOT on the payload (would fingerprint multi-monitor layouts, §6.2.1). Outer catches narrowed to `IOException`.
  - Tests: new `PersistenceEtwBridgeTests` (9) cover JsonFileStore round-trip (read + write event), oversize-read reject, malformed-json + malformed-base64 SwallowedError shape, PackagedSettingsStore unpackaged-context SwallowedError (read + write), and WindowPlacementCodec implausible-monitor-count + truncated-payload rejects. PII regression guard: no test payload may include the file path string.

### 4.8 PR: TryXxx refactors

- [ ] Convert the ~10 Win32 P/Invoke `GetLastError`-style swallows to `bool TryXxx(out int hr)` predicates. These usually already have a `bool` return; verify and finish.
- [ ] Audit entries flip to `☑ verdict shipped`.

### 4.9 PR: User-callback isolation sites (Keep verdicts)

- [x] For the ~10 user-callback swallows in `RenderContext.cs` (effect cleanups, command handlers, lifecycle hooks), preserve the broad catch but route through `DiagnosticLog.SwallowedError(LogCategory.Reactor, "UseEffect.cleanup[i=N]", ex)` per spec §6.7.3. Actual count was 6 in `src/Reactor/Core/RenderContext.cs`: `UseCommand.ExecuteAsync`, `UseCommand<T>.ExecuteAsync`, `UseEffect.cleanup[i=N]` (FlushEffects phase 1), `UseEffect.effect[i=N]` (FlushEffects phase 2), `RunCleanups.effectCleanup[i=N]`, `RunCleanups.persistedSave[i=N]`. The spec's "~10" estimate appears to have folded in `Reconciler` user-callback sites; those land in 4.10.
- [x] Verify framework-internal code is **outside** the try/catch (spec §6.7.3 point 2). For `RunCleanups.persistedSave`, `PersistedHookStateBase.SaveToCache` is framework code but reaches user-supplied `IPersistedStateScope.Set` — the try-catch wraps the user contact point; the surrounding hook-iteration loop is outside.
- [x] Each entry gets an inline `// User-callback isolation (spec 044 §6.7.3): ...` comment naming the user contract (cleanup ordering, effect-flush forward progress, persisted-slot independence).

### 4.10 PR: Residual catches + remaining trace prints

- [x] Migrated residual `Reconciler` swallowed-error catches:
  - `Reconciler.cs` ConnectedAnimation `PrepareToAnimate` (×2, mount + update paths), `GetAnimation`, `TryStart` → `DiagnosticLog.SwallowedError(LogCategory.Reactor, "ConnectedAnimation.<op>", ex)`. The spec's §6.7.4 "Promote + Narrow" verdict for these 4 entries is deferred (typed event promotion follows the same per-site audit gate as 4.5/4.6).
  - `Reconciler.cs` `ApplyThemeBindings` → `DiagnosticLog.SwallowedError(LogCategory.Theme, "ApplyThemeBindings", ex)`. Theme is the right category — the catch wraps a `Style` XAML compile.
  - `Reconciler.Mount.cs` `ContentDialog.ShowAsync+OnClosed` → `DiagnosticLog.SwallowedError(LogCategory.Reactor, ...)`. Inline comment marks it as user-callback isolation per §6.7.3 (the try wraps both `ShowAsync` and the user-supplied `OnClosed` delegate).
- [x] `LayoutEtwConsumer` (12 sites): error swallows (7) already routed to `DiagnosticLog.SwallowedError` in Phase C.7a (commit `b761a7a1`). Remaining 5 sites are pure trace prints (session-started, parser output, orphan-session cleanup) — stay as `Debug.WriteLine` per spec §6.3 framework-internal carve-out.
- [x] `LayoutCostAttribution` (8 sites): keep as `Debug.WriteLine` — framework-internal (verified by inspection).
- [x] `MarkdownBuilder` parse-failure (1 site): keep as `Debug.WriteLine` — framework-internal.
- [x] `YogaConfig` frozen-mutation `Debug.Assert` (6 sites): keep — these are `Debug.Assert`, not `Debug.WriteLine`; they're CI tripwires for a framework invariant, not diagnostics.
- [x] `ChildCollection` bounds assertions (4 sites): keep — same as above.

### 4.11 Phase C tests

- [ ] After each PR, run `dotnet test tests/Reactor.Tests` and `dotnet test tests/Reactor.SelfTests`. Both must stay green.
- [ ] Add one regression test per major category that fires the migrated path and asserts the event lands on the ETW listener (e.g., `Reconciler_swallow_emits_SwallowedError_event`, `NavigationDiagnostics_push_emits_NavigationRequested`).
- [ ] After Phase C is fully landed, the spec §12 acceptance criterion holds: **zero** `Debug.WriteLine` calls in `src/Reactor/` that report errors or HRESULT codes. Verify with a final grep.

### 4.12 Phase C acceptance

- [ ] Every audit entry has both checkboxes ticked or a documented reason it carried over to a follow-up PR.
- [ ] `dotnet test tests/Reactor.Tests tests/Reactor.SelfTests` green.
- [ ] AOT/trim build clean.

---

## Phase D — `ReactorTrace.Subscribe` in-process helper (1 PR)

### 5.1 Implement the listener

- [x] Implement `ReactorTrace.Subscribe(Action<ReactorEvent>, EventLevel, EventKeywords): IDisposable` per spec §6.4 in `src/Reactor/Diagnostics/ReactorTrace.cs`.
- [x] Backing implementation is a sealed `EventListener` filtered on `EventSource.Name == "Microsoft-UI-Reactor"`.
- [x] Default level is `Verbose` (spec §6.4 — earlier draft defaulted to Informational; we explicitly want Verbose).
- [x] Default keywords are `(EventKeywords)(-1)` (all).
- [x] Multiple concurrent subscribers supported; each subscriber's keywords/level are independently active until disposed.
- [x] Subscriber callback wrapped in `try/catch` so a buggy subscriber can't deadlock the dispatcher (spec §6.4 second bullet).

### 5.2 `ReactorEvent` payload

- [x] `ReactorEvent` is a `public readonly record struct` per spec §6.4.
- [x] `Payload` / `PayloadNames` use `IReadOnlyList<object?>` / `IReadOnlyList<string>` (the `EventWrittenEventArgs.Payload` shape — no reflection).
- [x] No reflection on consumer payload — verify AOT/trim-clean compile.

### 5.3 Phase D tests

- [x] `ReactorTraceSubscribeTests`:
  - [x] Subscribe → fire a known event → callback receives the matching `ReactorEvent` with correct EventId/Name/Level/Keywords/Payload.
  - [x] Dispose the subscription → subsequent fires don't reach the callback.
  - [x] Two concurrent subscribers see the same event independently.
  - [x] A subscriber that throws inside its callback does not break other subscribers and does not propagate to `EventSource.WriteEvent`.
  - [x] Subscriber with `keywords: Keywords.Errors` does not receive events fired only under `Keywords.Reconcile`.
  - [x] `EventLevel.Warning` subscriber does not receive Verbose events.

### 5.4 Phase D acceptance

- [x] AOT/trim build clean for `src/Reactor/Diagnostics/ReactorTrace.cs`.
- [x] Public API surface has XML doc comments including the §6.4 capture-to-file pointers ("For writing a trace file, use one of: …").
- [ ] No reflection added.

---

## Phase F — Wire ETW into `reactor.logs` (1 PR)

> **Note.** Phase E (sibling `Microsoft.UI.Reactor.Logging.Extensions` package) is deferred — see "Deferred / out of scope" below. We jump straight from D to F.

### 6.1 Extend `LogCaptureBuffer`

- [x] Add `LogSource.Event` to the existing `LogSource` enum in `src/Reactor/Hosting/Devtools/LogCaptureBuffer.cs`.
- [x] Add two additive optional fields to the log-entry shape: `eventName` (string?) and `eventId` (int?). The new fields default to null on the record-positional ctor, so every existing `Append(source, level, text)` call site stays source-compatible; an additional `Append(..., eventName, eventId)` overload is the only emitter of non-null values.

### 6.2 Subscribe inside `LogCaptureInstall.Install`

- [x] On install, call `ReactorTrace.Subscribe(...)` exactly once (the install owns the subscription's lifetime, stored in a static and disposed only via `ResetForTests`). Install is gated by the same `_installLock` so concurrent first-touch callers cannot race on the subscription.
- [x] Map each `ReactorEvent` to a `LogCaptureBuffer` entry:
  - `text` = formatted line, e.g. `WindowOpened windowType=SettingsWindow hwnd=0x00010A2C`.
  - `level` = mapped from `EventLevel` (Critical→Critical, Error→Error, Warning→Warning, Informational→Info, Verbose→Debug, LogAlways→Trace).
  - `source` = `Event`.
  - `eventName`, `eventId` populated.
- [x] Apply payload-formatting PII policy from spec §6.2.1: never include raw `ex.Message` (we already strip on the EventSource side, but defense-in-depth here), length-bound payload string fields at 256 chars (`LogCaptureInstall.MaxPayloadFieldChars`). HR and HWND payload fields render in `0x{X8}` so log greps from before the migration still match.

### 6.3 Extend the `reactor.logs` MCP tool

- [x] Add `source=event` (with `etw` alias) as a valid value for the existing `source` filter in `src/Reactor/Hosting/Devtools/DevtoolsLogsTool.cs`.
- [x] Existing filter values (`stdout`, `stderr`, `debug`/`trace`) continue to work unchanged.
- [x] Document the new `eventName` and `eventId` fields in the tool's JSON schema description and the verbose tool description string.

### 6.4 Phase F tests

- [x] Add `tests/Reactor.Tests/Diagnostics/LogCaptureEventBridgeTests.cs`:
  - [x] After install + fire a known event, `LogCaptureBuffer` contains an entry with `source=Event`, the right `eventName`, and the right `level`.
  - [x] Payload formatting respects the 256-char length cap.
  - [x] HR-style payload field renders in `0x{X8}` form (not the signed-int decimal).
  - [x] Null payload renders as `null` literal (not an empty `name=` field).
  - [x] Existing `source=stdout` / `source=stderr` / `source=debug` paths continue to capture (regression guard).
- [x] Add `source=event` + `source=etw` acceptance tests to `DevtoolsLogsToolTests`.
- [ ] Selftest fixture or AppTest: launch the devtools host, fire an event from a fixture, and assert `reactor.logs source=event` returns the event (existing devtools test infra in `tests/Reactor.AppTests/`). _(Deferred: in-process bridge test exercises the full BuildPayload + bridge stack; the AppTest is a process-isolation guard, not a behavior gap.)_

### 6.5 Phase F acceptance

- [x] `dotnet test tests/Reactor.Tests` green (478 tests in the Diagnostics+Devtools filter, full suite below).
- [x] `dotnet test tests/Reactor.SelfTests` green.
- [x] Existing devtools MCP clients (which do not pass `source=event`) see zero behavior change (spec §12).

---

## Phase H — Diagnostics user guide (1 PR)

### 7.1 Author the template

- [x] Create `docs/_pipeline/templates/diagnostics.md.dt`. Sections (mirroring spec §2 + §7 + §8):
  - [x] The rule (`Debug.WriteLine` vs `ReactorEventSource`).
  - [x] How to capture a trace with environment variables (`DOTNET_EnableEventPipe`).
  - [x] How to attach `dotnet-trace`.
  - [x] How to open a `.nettrace` in Visual Studio.
  - [x] Performance Profiler → Events Viewer workflow with `Microsoft-UI-Reactor` as a custom provider + keyword bitmask quick reference.
  - [x] How to use `ReactorTrace.Subscribe(...)` from app code.
  - [x] How to filter `reactor.logs source=event` in the devtools MCP tool.
  - [x] Cross-link to `perf-instrumentation.md` for perf-shaped events.
- [x] **Do not** document `AddReactorEvents` / `ILoggingBuilder` — that's deferred Phase E. Confirmed: no `AddReactorEvents` mention in either template or generated output.

Also added (over and above the spec checklist):
- A "Common Mistakes" subsection covering computing payload outside the `IsEnabled` gate, capturing without pinning level, and forwarding `ex.Message` through subscribers.
- An SVG flow diagram at `docs/_pipeline/diagrams/diagnostics/flow.svg` showing catch-site → `DiagnosticLog` → `ReactorEventSource` → four consumers. Required to clear `REACTOR_DOC_TIER_004` under `--ci`.
- Two new source snippet anchors (`swallowed-error-shape` on `DiagnosticLog.cs`, `hresult-failed-event` on `ReactorEventSource.cs`) to clear `REACTOR_DOC_TIER_003` (≥3 snippets per comprehensive page).

### 7.2 Update related touch points

- [x] Added a "Diagnostics: audience, not severity, decides the channel" sub-section to `CONTRIBUTING.md` under Code style — restates the rule, shows the canonical `DiagnosticLog.SwallowedError` shape, and links to the generated `docs/guide/diagnostics.md`.
- [x] Cross-linked from `docs/_pipeline/templates/perf-instrumentation.md.dt` Next Steps to the new diagnostics page as the first entry.

### 7.3 Generate

- [x] `mur docs compile --topic diagnostics --ci` runs clean (only the non-fatal `winui-ref` warning, which the spec explicitly marks as "only required for transparent-wrapper pages").
- [x] `docs/guide/diagnostics.md` lands 404 lines, generated; 26 cross-mentions of the four headline surfaces (`DiagnosticLog` / `reactor.logs` / `ReactorTrace` / `EventSource`). Hand-edits forbidden — confirmed by the template hash.

### 7.4 Phase H acceptance

- [x] The doc renders correctly in the user-guide site preview (verified via `mur docs compile` + reading the generated `docs/guide/diagnostics.md`).
- [x] All four capture recipes (env vars, `dotnet-trace`, VS Profiler, `ReactorTrace.Subscribe`) are present and have copy-pasteable snippets.
- [x] `reactor.logs source=event` recipe shows the request/response shape with the `eventName`/`eventId` fields populated.
- [x] No reference to `AddReactorEvents` (deferred).

---

## Phase I — `ReactorTraceCollector` test harness + regression guards (1 PR)

### 8.1 Test harness

- [x] Add `tests/Reactor.Tests/Diagnostics/ReactorTraceCollector.cs`. API:
  ```csharp
  using var collector = ReactorTraceCollector.Capture(
      level: EventLevel.Verbose,
      keywords: ReactorEventSource.Keywords.Reconcile);
  // … run code under test …
  Assert.Collection(collector.Events, …);
  ```
  Also exposes `collector.ByName(string)` — Phase B/C migrations needed a way to pick a single event out of an interleaved capture, and reaching into a lock around `_events.Where(...)` is the right shape.
- [x] Backing implementation reuses `ReactorTrace.Subscribe`.
- [x] Thread-safe enumeration of captured events (snapshot via `ToArray` inside the gate; the public `Events` accessor returns the snapshot, never the live list).
- [x] Disposing the collector unsubscribes; subsequent fires don't leak into a later test (delegates to `Subscription.Dispose`).

### 8.2 Regression assertions (spec §9)

- [x] `Reconcile_emits_start_stop_pair` — both Start (with root type) and Stop (with diffed/skipped/created/modified counts) land under `Keywords.Reconcile` at `Informational`.
- [x] `RenderError_carries_exception_type_but_not_message` — fires `RenderError` with a fingerprintable secret string; asserts the third payload slot is empty and the secret appears nowhere in the joined payload (defense-in-depth on TASK-064 / spec §6.2.1). The Phase I doc spelled the test name "ComponentRender_throw_emits_RenderError" but the assertion shape is identical — calling the EventSource directly is honest about what we're testing (the emit-site PII strip), not the component-throw path which lives in Reconciler tests already.
- [x] `SwallowedError_smoke_for_each_log_category` — `[Theory]` over `Enum.GetNames<LogCategory>()` so a new category added later is automatically covered. Currently exercises Reactor / Hosting / Persistence / Navigation / Intl / Theme / Shell / LayoutCost / Devtools / Markdown.
- [x] `Mcp_selector_is_hashed_not_emitted_raw` — `[Text*='alice@contoso.example.invalid']` selector goes in; the captured payload is `sha1:<8 bytes hex>` with no `alice` / `@` characters.
- [x] `DisabledKeyword_skips_ReactorEventSource_WriteEvent_payload_marshal` — measures `GC.GetAllocatedBytesForCurrentThread()` across 10K `ReactorEventSource.Log.SwallowedError(...)` calls when `Keywords.Errors` is disabled; allows up to one byte per iteration. Has an `IsEnabled`-precondition early return so parallel test fixtures that keep Errors enabled cannot make this flaky — when isolated, the test exercises the measurement and the delta is well under the cap. We deliberately measure `ReactorEventSource` directly, not `DiagnosticLog.SwallowedError`, because the latter additionally runs a `[Conditional("DEBUG")]` `Debug.WriteLine` mirror whose ~600 B/call allocation is the intended Debug-build cost and is compiled out in Release.

### 8.3 Phase I acceptance

- [x] All assertions in 8.2 pass.
- [x] Test harness is **only** in the test assembly (`tests/Reactor.Tests/Diagnostics/ReactorTraceCollector.cs`, `internal sealed class`) — no production reference.
- [x] `dotnet test tests/Reactor.Tests` green (492 tests in the Diagnostics+Devtools filter).

---

## Cross-phase acceptance — closing #323

A final check before declaring spec 044's headline goals shipped (excluding deferred items):

- [ ] **Acceptance §12.1** A Release build of a Reactor app emits `Microsoft-UI-Reactor` events for every error/warning that today only appears in `Debug.WriteLine`. Verify by capturing a trace of a sample app exercise that previously only printed to the Output window.
- [ ] **Acceptance §12.2** A developer can collect a trace by setting three env vars (`DOTNET_EnableEventPipe=1`, `DOTNET_EventPipeOutputPath=...`, `DOTNET_EventPipeConfig=Microsoft-UI-Reactor:...:5`) with no source change.
- [ ] **Acceptance §12.3** The captured `.nettrace` opens in Visual Studio Events Viewer with timestamps, provider, payload columns.
- [ ] **Acceptance §12.4** `grep -rn 'Debug\.WriteLine' src/Reactor/` returns **zero** error/HR-reporting hits — all routed through `DiagnosticLog`.
- [ ] **Acceptance §12.5** Every remaining `catch (Exception)` in `src/Reactor/` has a corresponding `Keep`-verdict entry in `docs/specs/044/swallowed-error-audit.md` (the analyzer CI gate is deferred — see below — but the audit file itself is the manual gate).
- [ ] **Acceptance §12.6** All new event payloads pass the §6.2.1 PII policy review.
- [ ] **Acceptance §12.7** `reactor.logs source=event` returns ETW events; existing clients without the filter see no behavior change.
- [ ] **Acceptance §12.8** `Microsoft.UI.Reactor` builds clean with `IsAotCompatible=true` + trim-warnings-as-errors.
- [ ] **Acceptance §12.9** `docs/guide/diagnostics.md` exists, is generated from a template, and links to `perf-instrumentation.md`.

---

## Deferred / out of scope (tracked but not blocking #323)

These are spec 044 deliverables we are **explicitly deferring** to keep the change minimal. Each gets its own follow-up issue when this work lands.

- **Phase C-gate (Roslyn analyzer).** Spec §6.7.5. A Roslyn analyzer that flags new `catch (Exception)` in `src/Reactor/` without a `DiagnosticLog.*` call or audit-reference comment. Useful for preventing drift; not required to ship the cleanup. Track as a separate "diagnostics analyzer" task.
- **Phase E (`ILogger` adapter sibling package).** Spec §6.5 + §8.4. `Microsoft.UI.Reactor.Logging.Extensions` with `ILoggingBuilder.AddReactorEvents(...)`. Adds a sibling NuGet package + a `Microsoft.Extensions.Logging` dependency. Apps that want this today can call `ReactorTrace.Subscribe(...)` and write four lines of glue. Track as a separate "ship ILogger adapter" task.
- **Phase G (`IDevtoolsConsole` abstraction).** Spec §6.8. Replaces direct `Console.*` calls in `Hosting\ReactorApp.cs`, `PreviewCaptureServer`, `DevtoolsMcpServer` with an injected interface. The current direct `Console.*` usage is correct (CLI surface, spec §4.5) and not blocking the logging story. Track as a separate "console abstraction" task.
- **Open question Q3 follow-up.** The `[Reactor]` Roslyn analyzer that flags new `Debug.WriteLine` outside the allow-list directories. Spec §10. Defer until the migration is complete and we have data on drift.
