# Swallowed-error audit — spec 044

Companion to [`docs/specs/044-tracing-and-logging-cleanup-design.md`](../044-tracing-and-logging-cleanup-design.md)
and the implementation task list at [`docs/specs/tasks/044-tracing-and-logging-cleanup-implementation.md`](../tasks/044-tracing-and-logging-cleanup-implementation.md).

This file is the permanent record of the spec §6.7 decision Microsoft.UI.Reactor (Reactor)
made at each `catch (Exception ex) { Debug.WriteLine(...); }` site
when the framework's diagnostics surfaces were migrated from
contributor-only `Debug.WriteLine` to release-visible
`Microsoft-UI-Reactor` ETW events.

## Verdict vocabulary

Every ledger row carries **exactly one** of these tokens in its
`Verdict` cell, spelled exactly as shown. The vocabulary is closed —
`SwallowedErrorAuditTests` fails on any token outside it, and on any
token that has ledger rows but no row in the distribution table.

| Token | Meaning |
|---|---|
| `Keep` | Broad `catch (Exception)` is correct and stays. Replace `Debug.WriteLine` with `DiagnosticLog.SwallowedError` but leave the catch shape alone. |
| `Narrow` | The catch filters on a specific exception type and/or HRESULT range (`catch (COMException ex) when (ex.HResult is HResults.X or HResults.Y)`). Anything outside the filter propagates. |
| `Propagate` | The catch is deleted; this is a bug-class failure the caller needs to see. |
| `TryFinally` | No catch at all: `try/finally` guarantees the framework's cleanup runs, and the exception still propagates. |
| `TryXxx` | The call site has a `bool TryX(out result)` shape underneath; thread the return code instead of catching. |
| `PromoteEvent` | The diagnostic graduates to a subsystem-specific `ReactorEventSource` event (e.g. `JumpListSaveFailed(int hr)`). |
| `Deleted` | Dead-defensive `try`/`catch` removed outright — the wrapped operation could not throw, so the catch was hiding nothing. |
| `Trace` | Pure-trace `Debug.WriteLine` that stays as-is under the spec §6.3 framework-internal carve-out. Not an error swallow; recorded so the site is accounted for rather than silently skipped. |

`TryFinally` and `Trace` are first-class tokens because the per-file
sections had already recorded both dispositions while the summary
table had no row for either — so those sites were uncountable by
construction (issue #959).

### `Keep` justifications

`Keep` is **not** a single justification. These four are permitted, and
every `Keep` row's `Notes` cell opens with the matching bold tag
(gated):

| Tag | Meaning |
|---|---|
| **sibling-independence** | The site sits in a loop whose invariant is forward progress: slot *i*'s failure must not block slots *i+1…n*. |
| **user-callback isolation** | The `try` wraps app code. Spec §6.7.3 — a faulty app delegate must not crash the framework's dispatch. |
| **fail-safe-to-default** | The operation's contract is "answer if possible, otherwise fall back"; any failure to answer resolves to the safe direction. |
| **framework-internal** | Spec §6.3 carve-out — the failure class is contributor-shaped, not user-shaped. |

Read the distribution table's `Keep` row as the union of these, not as
any one of them. Three are currently in use; **framework-internal** is
reserved for a §6.3 carve-out that stays a broad `Keep` rather than
becoming `Trace`, and no row uses it today.

Entries whose verdict has **shipped** also name the migration commit, so
the verdict is auditable against the working code. Entries still marked
`deferred` have no commit yet by definition — their Notes name the phase
the work is scheduled into instead.

## Ledger schema

Every per-file table below uses this header, exactly:

```
| Site(s) | Sites | Verdict | Status | Notes |
```

- **`Site(s)`** — what the row covers, in prose.
- **`Sites`** — a positive integer: how many swallow/diagnostic sites the row
  adjudicates. Rows that collapse several sites **declare** the collapse
  factor here rather than leaving it implicit.
- **`Verdict`** — one token from the vocabulary above, nothing else. Qualifiers
  (`+ DiagnosticLog`, `(annotated)`, `(partial)`) live in `Notes`.
- **`Status`** — `shipped` or `deferred`. A partial delivery counts as
  `deferred` until the recorded verdict is fully in the code; the part that
  did ship is described in `Notes`.
- **`Notes`** — free prose. For `Keep` rows it opens with a justification tag.

Cells are split on unescaped `|`. To write a literal pipe inside `Site(s)` or
`Notes` — a `catch` filter with an `or`-chain, say — escape it as `\|`, or the
row gains a column and the gate rejects it.

> **Scope discipline.** The spec scope (§44 task doc preamble) is
> *the minimum change required to make Reactor's release-build
> diagnostics visible to app developers*. The `Keep` migration alone
> delivers that — every error/HR-reporting `Debug.WriteLine` in
> `src/Reactor/` now routes through `DiagnosticLog` and lands on the
> ETW surface. The `Narrow` / `Propagate` / `TryXxx` /
> `PromoteEvent` verdicts are followups that gate on a
> subsystem subject-matter review.

---

## Verdict distribution (audit ledger)

> **Retrospective, and derived.** These are measured counts of what the
> per-file sections below adjudicated — not estimates, and not a
> current-state index of `src/`. The prospective planning estimates that
> preceded the migration live in
> [`044-tracing-and-logging-cleanup-design.md`](../044-tracing-and-logging-cleanup-design.md)
> §6.3 and §6.7.4 and are **not commensurable** with this table. Do not pair
> a number from there with a number from here.

<!-- distribution:begin -->

| Verdict | Sites | Shipped | Deferred |
|---|---|---|---|
| `Keep` | 24 | 24 | 0 |
| `Narrow` | 38 | 38 | 0 |
| `Propagate` | 7 | 7 | 0 |
| `TryFinally` | 7 | 7 | 0 |
| `TryXxx` | 10 | 0 | 10 |
| `PromoteEvent` | 26 | 18 | 8 |
| `Deleted` | 7 | 7 | 0 |
| `Trace` | 5 | 5 | 0 |

<!-- distribution:end -->

Spec §6.7.4 worry-threshold for `Propagate` is 20; we're at 7.

### Derivation

**The rule.** For each verdict token, `Sites` is the sum of the `Sites`
column over every ledger row carrying that token; `Shipped` and `Deferred`
are the same sum partitioned by the row's `Status`. Ledger rows are the
rows of every canonical-header table between the `<!-- ledger:begin -->`
and `<!-- ledger:end -->` markers — nothing else in this file counts.

**The definition.** This is a **cumulative audit ledger**: one count per
swallow site ever adjudicated, keyed by the verdict recorded at
adjudication time. It is not an inventory of today's `src/`. Consequences,
all deliberate:

- Entries for files that have since been deleted stay counted — see the
  retired `LayoutEtwConsumer.cs` section.
- `Propagate` and `Deleted` count catches that by construction no longer
  exist. That is the point: they record decisions, not code.
- The total never decreases. A row is only ever removed if it was recorded
  in error.

**Updating it.** Edit your per-file row, run

```bash
dotnet test tests/Reactor.Tests -p:Platform=x64 -p:SkipSignaturesGen=true -p:SkipReactorApiGen=true --filter-class "*SwallowedErrorAudit*"
```

and paste the recomputed table the failure message prints. The two skip
flags are the same pair CI passes, and they keep a ledger-only edit from
leaving anything else modified in your tree: `SkipSignaturesGen` stops
`Reactor.Cli` kicking off the nested `Reactor.SignaturesGen` build, and
`SkipReactorApiGen` stops that build's `EmitReactorApiTxt` step
rewriting `skills/reactor.api.txt` if it runs anyway. Do **not**
hand-increment a cell: `Sites` is a sum, and two branches incrementing
`37 → 38` from the same base produce identical edits that git
auto-resolves as agreement, silently dropping one increment. Making the
table derived removes that hazard — concurrent branches now conflict in
their own rows, which is a conflict you can see.

**Ledger-vs-source drift is expected and is not a counting bug.** The
ledger records adjudications; the code moves on. Verified examples in both
directions, as of this writing: `ReactorWindow.cs` carries 31
narrow-shaped `catch` clauses in source against 22 adjudicated below;
`LayoutEtwConsumer.cs` was removed wholesale in `25a753d2` (#507); and
several adjudicated operation labels (`UseEffect.cleanup`,
`RunCleanups.persistedSave`, `ConnectedAnimation.*`) no longer appear in
`src/`. Reconciling the ledger against today's source is a separate spec
§G9 sweep — it is *not* a repair to these counts, which are correct for
what they count.

### Historical snapshots (do not update)

Frozen figures from earlier passes. They are **not** the table above and
must not be reconciled with it — each is a correct statement about the
moment it describes. The gate's parser skips this section.

- **Audit pass 1** — recorded in
  [`tasks/044-tracing-and-logging-cleanup-implementation.md`](../tasks/044-tracing-and-logging-cleanup-implementation.md)
  §3.4: *56 Keep, 9 Narrow (6 shipped, 3 deferred), 0 Propagate, 10
  Replace-with-TryXxx, 18 Promote-to-typed-event.* The first pass migrated
  `Debug.WriteLine` → `DiagnosticLog.SwallowedError` with the catch shape
  unchanged, so nearly everything landed as "Keep".
- **Audit pass 2** — *the dramatic shift from "56 Keep" to "8 Keep + 12
  Propagate + 9 Deleted + 33 Narrow" came from applying the §6.7.2
  narrowing properly to `ReactorWindow.cs` and the related Hosting code.*
  The second pass applied the §6.7.2 rule that broad `catch (Exception)` is
  wrong almost everywhere it isn't sibling-independence or genuine
  fail-safe-to-default behavior.

---

## Method

Every site listed below was inspected against the template in spec
§6.7.1. For `Keep` verdicts the template is collapsed to the audit
trail (site → migration commit) plus the justification tag naming
which of the four `Keep` justifications applies. For `Narrow` /
`PromoteEvent` / `TryXxx` verdicts, the per-site context is included.

---

## File-grouped sites

Sites are grouped by source file in alphabetical order, matching
the inventory in §3.3 of the task doc. A heading's path must name a
file that exists, unless the heading carries a `(retired …)` marker.

<!-- ledger:begin -->

### `src/Reactor/Core/Localization/IntlAccessor.cs` — Phase C.3 (commit `7312ce73`)

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| `ResolvePattern` missing-key (×2 collapsed into 1) | 2 | `PromoteEvent` | shipped | `IntlMissingKey(key, locale, fellBack)` under `Keywords.Intl`. Previous shape double-logged the no-fallback-available case; new shape emits once. PII: key is developer-authored .resw identifier. |
| `Message` format failure | 1 | `Keep` | shipped | **fail-safe-to-default.** `LogCategory.Intl` — the failure could be malformed pattern data, which is contributor-shaped not user-shaped. |
| `RichMessage` format failure | 1 | `Keep` | shipped | **fail-safe-to-default.** Same as above. |

### `src/Reactor/Core/Navigation/NavigationDiagnostics.cs` — Phase C.2 (commit `e2a755b2`)

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| `OnNavigationRequested` | 1 | `PromoteEvent` | shipped | `NavigationRequested(routeTemplate)` under `Keywords.Navigation`. |
| `OnNavigationCompleted` | 1 | `PromoteEvent` | shipped | `NavigationCompleted(routeTemplate, durationMs)`. |
| `OnNavigationCancelled` | 1 | `PromoteEvent` | shipped | `NavigationCancelled(routeTemplate, reason)`. |
| `OnNavigationCacheHit` | 1 | `PromoteEvent` | shipped | `NavigationCacheHit(routeTemplate)`. Verbose-level. |
| `OnNavigationCacheMiss` | 1 | `PromoteEvent` | shipped | `NavigationCacheMiss(routeTemplate)`. Verbose-level. |
| `OnNavigationCacheEviction` | 1 | `PromoteEvent` | shipped | `NavigationCacheEvict(routeTemplate, reason)`. Verbose-level. |
| `OnTransitionStarted` | 1 | `PromoteEvent` | shipped | `NavigationTransitionStarted(routeTemplate)` (new event id 33). |
| `OnTransitionCompleted` | 1 | `PromoteEvent` | shipped | `NavigationTransitionCompleted(routeTemplate, durationMs)` (id 34). |
| `OnDeepLinkResolved` | 1 | `PromoteEvent` | shipped | `NavigationDeepLinkResolved(matched, routeCount)` (id 35). **PII (§6.2.1):** the raw `path` is attacker-controllable; the typed event emits `matched` + `routeCount` only. The `NavigationDiagnosticsEtwBridgeTests.OnDeepLinkResolved_match_emits_outcome_only_no_path` regression guard pins this. |

### `src/Reactor/Hosting/Persistence/JsonFileStore.cs` — Phase C.5 (commit `21e22e1c`)

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| Round-trip read success | 1 | `PromoteEvent` | shipped | Emits `PersistenceRead(storeKind: "json-file", sizeBytes)`. Storekind label — never the path (§6.2.1). |
| Round-trip write success | 1 | `PromoteEvent` | shipped | Emits `PersistenceWrite(...)`. Same PII discipline. |
| Read oversize | 1 | `PromoteEvent` | shipped | Emits `PersistenceRejected(storeKind, reason: "oversize")`. |
| `TryRead` narrow exceptions — `JsonException`, `FormatException`, `IOException`, `UnauthorizedAccessException` | 4 | `Narrow` | shipped | `catch (IOException) / catch (JsonException) ...` instead of `catch (Exception)`. Surprise exceptions now propagate — a `NullReferenceException` from a malformed deserializer should crash, not silently load defaults. One `catch` clause per named type. |
| `Write` narrow exceptions — `IOException`, `UnauthorizedAccessException` | 2 | `Narrow` | shipped | Same shape. |

### `src/Reactor/Hosting/Persistence/PackagedSettingsStore.cs` — Phase C.5

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| `TryRead` narrow exceptions — `InvalidOperationException`, `COMException`, `UnauthorizedAccessException`, `FormatException` | 4 | `Narrow` | shipped | The WinRT call surface throws `InvalidOperationException` (HR `0x80073D54`) on every unpackaged process; that's the actual failure class here, not `IOException`/`JsonException` as the spec's draft list said. Storekind `"packaged-settings"`. The `FormatException` base64 clause is on the **read** path (`PackagedSettingsStore.TryRead.base64`) — an earlier revision of this entry attributed it to the write path. |
| `Write` narrow exceptions — `InvalidOperationException`, `COMException`, `UnauthorizedAccessException` | 3 | `Narrow` | shipped | Same. |

### `src/Reactor/Hosting/Persistence/WindowPlacementCodec.cs` — Phase C.5

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| Win32 `GetWindowPlacement` failure | 1 | `PromoteEvent` | shipped | `DiagnosticLog.HResultFailed(LogCategory.Persistence, ..., GetLastError())`. |
| `IsPlausiblePlacement` reject | 1 | `PromoteEvent` | shipped | `PersistenceRejected("placement", reason)` with a short reason label. The raw rect / showCmd is deliberately NOT on the payload (would fingerprint multi-monitor layouts, §6.2.1). |
| `monitorCount` reject | 1 | `PromoteEvent` | shipped | Same. |
| `EndOfStreamException` reject | 1 | `PromoteEvent` | shipped | Same. |
| Outer catches — `Capture`, `Restore` | 2 | `Narrow` | shipped | Narrowed to `IOException`. One clause each on the capture and restore paths; the original entry said "Outer catches" without a count. |

### `src/Reactor/Core/Reconciler.cs` — Phase C.7b (commit `054c53ef`) + Phase C.8 (commit `21cd6ef9`)

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| Navigation lifecycle callback dispatch | 1 | `Keep` | shipped | **user-callback isolation** per §6.7.3. Already shipped in C.7b. |
| ConnectedAnimation `PrepareToAnimate` (mount path) | 1 | `Keep` | shipped | **fail-safe-to-default.** LogCategory.Reactor. §6.7.4 calls for "Promote + Narrow" — deferred along with the rest of 4.6. Counted once, under the verdict actually recorded here; the deferred promotion is tracked in the Phase 4.6 section and is **not** a second ledger entry. |
| ConnectedAnimation `PrepareToAnimate` (update path) | 1 | `Keep` | shipped | **fail-safe-to-default.** Same. |
| ConnectedAnimation `GetAnimation` | 1 | `Keep` | shipped | **fail-safe-to-default.** Same. |
| ConnectedAnimation `TryStart` | 1 | `Keep` | shipped | **fail-safe-to-default.** Same. |
| `ApplyThemeBindings` | 1 | `Keep` | shipped | **fail-safe-to-default.** LogCategory.Theme — the catch wraps a XAML `Style.Load` compile. Could narrow to `XamlParseException` in a follow-up. |

### `src/Reactor/Core/Reconciler.Mount.cs` — Phase C.8 (commit `21cd6ef9`)

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| `ContentDialog.ShowAsync + OnClosed` | 1 | `Keep` | shipped | **user-callback isolation** per §6.7.3 — the try wraps both `ShowAsync` AND the user-supplied `OnClosed` delegate. Cannot narrow without splitting the try-catch into two; deferred. |

### `src/Reactor/Core/RenderContext.cs` — Phase C.6 (commit `90d516b0`) + Phase C.9 narrowing

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| `UseCommand.ExecuteAsync` | 1 | `TryFinally` | shipped | Phase C.9: fire-and-forget `Task.Run` wraps the user action with `try { await asyncAction(); } finally { guardRef.Current = false; setIsExecuting(false); }`. The framework state is restored before unwind; the user's throw faults the Task and surfaces via `Task.UnobservedTaskException` rather than being swallowed under `SwallowedError`. The earlier "Keep + DiagnosticLog" shape was hiding user bugs — apps couldn't tell their command was broken without subscribing to ETW. |
| `UseCommand<T>.ExecuteAsync` | 1 | `TryFinally` | shipped | Same shape. |
| `UseEffect` cleanup (FlushEffects phase 1) | 1 | `Keep` | shipped | **sibling-independence** — slot i's failure must not block slots i+1…n in the same flush. The loop's invariant (forward progress through all cleanups) requires the broad catch. |
| `UseEffect` effect (FlushEffects phase 2) | 1 | `Keep` | shipped | **sibling-independence.** Same. |
| `RunCleanups.effectCleanup` | 1 | `Keep` | shipped | **sibling-independence.** Same. |
| `RunCleanups.persistedSave` | 1 | `Keep` | shipped | **sibling-independence** — persisted-slot independence. The try-catch wraps the user contact point (`IPersistedStateScope.Set`); the surrounding hook-iteration loop is outside. |

### `src/Reactor/Hosting/Etw/LayoutEtwConsumer.cs` — Phase C.7a (commit `b761a7a1`) — (retired: file removed in `25a753d2` / #507)

The layout-cost visualizer and its ETW infrastructure were deleted from the
repo. Under the cumulative-ledger definition these adjudications stay
counted — they record decisions that were genuinely made — and the heading
carries the `(retired …)` marker so the path-existence gate does not fire.

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| Error-swallow catches (provider start, session enable, parser, etc.) | 7 | `Keep` | shipped | **fail-safe-to-default.** LogCategory.LayoutCost. |
| Pure-trace `Debug.WriteLine` (session started / parser output / orphan cleanup) | 5 | `Trace` | shipped | Framework-internal per spec §6.3 carve-out. Kept as `Debug.WriteLine`; not an error swallow. |

### `src/Reactor/Hosting/FrameNavigation.cs` — added with the Frame-navigation access-violation fix

Both sites are new with that fix and are **fail-safe-to-default** per §6.7.2, not
sibling-independence. They are deliberately broad and the code comments say so —
note that `catch (Exception ex) when (ex is not A and not B)` still compiles to an
IL filter region with a nil `CatchType`, so the carve-outs exclude the two fatal
types without making it a narrowing. Same shape as the existing convention at
`Reconciler.cs:1795`, `ElementPool.cs:93` and `ObservableTreeTracker.cs:124`.

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| `CanResolvePageType` resolver probe | 1 | `Keep` | shipped | **fail-safe-to-default.** The method's contract is "true **only if** definitively resolvable". Any failure to answer means we cannot confirm, and returning `false` refuses the navigation — the safe direction, and the one that cannot produce the access violation. Expected types are `COMException` at the WinRT boundary and `InvalidOperationException` / `ArgumentException` from a generated or hand-written `IXamlMetadataProvider`; propagating anything else would convert a third-party provider's bug into a render-loop error **while the navigation is refused either way**, i.e. strictly worse for identical safety. `OutOfMemoryException` / `StackOverflowException` still propagate. |
| `TryNavigate` around `Frame.Navigate` | 1 | `Keep` | shipped | **user-callback isolation** (§6.7.3). What surfaces here is the **page constructor's** exception — arbitrary application code — and routing it into the element's declared `OnNavigationFailed` channel is the arm's entire purpose. Directly analogous to the `ContentDialog.ShowAsync + OnClosed` entry above. **Narrowing would reintroduce the defect this fix exists to remove:** an unanticipated page-constructor failure would escape the mount pass. **Coverage gap, stated rather than hidden:** reaching this arm needs a target that *resolves* but whose constructor then throws, i.e. a real `.xaml`-backed page. The selftest host ships no XAML, so every target it can offer is either refused before `Navigate` (code-only) or a framework type that constructs fine. The arm is therefore reasoned-about, not exercised — unlike the refusal path, which `FrameNav_CodeOnlyPageRefusedNotFatal` pins directly. |

### `src/Reactor/Hosting/ReactorWindow.cs` — Phase C.8 (commit `21cd6ef9`) + Phase C.9 narrowing

Phase C.8 migrated the file's `Debug.WriteLine` calls to `DiagnosticLog` with
the catch shape unchanged. Phase C.9 applies the actual §6.7.2 narrowing per
site. The rows below adjudicate 39 sites in total; the "29 sites" figure that
audit pass 2 quotes for this file counted only the `Debug.WriteLine` calls it
migrated, not the `Propagate`/`Deleted` sites where the whole `try` went away.

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| Pure-advisory user callbacks — `SizeChanged`, `StateChanged`, `Closing` | 3 | `Propagate` | shipped | try/catch deleted. User throw goes to dispatcher's UnhandledException; developer sees the bug. Previous swallow silently treated thrown `Closing` handler as "didn't cancel," which was worse than crashing. |
| User callback with framework cleanup after — `Closed?.Invoke` | 1 | `TryFinally` | shipped | User throw propagates AND `RemoveOwned` / `UnregisterWindow` / `Dispose` still run. Handles the limp-along case where the app set `Application.UnhandledException.Handled = true`. |
| WinUI AppWindow / Window API surface — `Title.set`, `Presenter.apply`, `IsShownInSwitchers.set`, `ExtendsContentIntoTitleBar.set`, `InitialResize`, `SetOwner`, `FirstDpiResize`, `Hide`, `Show`, `Close`, `SetSize`, `SetPosition`, `CenterOnScreen`, `ResolveCurrentState`, `TryApplyExeIconFallback`, `TryApplyInitialPlacement`, `ResolveOwnerDisplayArea` (17), plus all five event unsubscriptions in `Dispose` | 22 | `Narrow` | shipped | `catch (COMException ex) when (HResults.IsTeardownReentry(ex.HResult))` (the well-known `RPC_E_DISCONNECTED` / `E_HANDLE` / `RPC_E_SERVERFAULT` / `CO_E_OBJNOTCONNECTED` set). Anything outside that HR set propagates as a genuine bug. Source now carries 31 clauses of this shape in this file — the nine added after the C.9 pass are un-adjudicated and are the concrete example named under **Ledger-vs-source drift**. |
| Iteration sibling-independence — `IClosingGuard.CanClose()`, owned-window-cascade `child._window.Close()` | 2 | `Keep` | shipped | **sibling-independence.** Closing-guard fail-safe-to-cancel is documented behavior (spec 036 §3.4 test pins it); owned-cascade sibling independence is spec 036 §9. Both have inline comments naming the contract. Annotated in code. |
| Framework dispose chain — `_messageMonitor.Dispose()` → `_host.Dispose()` → `_persistedScope.Dispose()` → `_thumbnailToolbar?.Dispose()` | 4 | `TryFinally` | shipped | All four disposes run regardless of which throws; first exception propagates. No swallowing — a framework Dispose bug should surface. |
| Dead-defensive try/catch — `QueryDpiForWindow`, `WM_GETMINMAXINFO.apply`, `GetDpiForSystemFallback`, `NativeIcon.DestroyIcon`, `MonitorEnumeration.Snapshot`, `TryRestorePersistedPlacementCore`, `TrySavePersistedPlacement` | 7 | `Deleted` | shipped | Try deleted. The wrapped operations are P/Invokes on `nint` that can't throw at the marshal layer, or downstream calls that already narrow internally and return sentinel values. The outer try/catch was hiding nothing real. |

LogCategory.Hosting except for the two persistence-shaped placement
sites (LogCategory.Persistence) and the user-event sites which now
have no catch at all.

### `src/Reactor/Hosting/Shell/JumpListComInterop.cs` — Phase C.4 (commit `301593bc`)

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| `BeginList`, `AddUserTasks`, `AppendCategory`, `AppendKnownCategory.Recent`, `AppendKnownCategory.Frequent`, `CommitList` | 6 | `PromoteEvent` | deferred | Partially delivered: `DiagnosticLog.HResultFailed(LogCategory.Shell, "JumpList.<op>", hr)` has shipped, which meets the release-visibility goal. The recorded verdict — a subsystem-specific `JumpListSaveFailed(hr)` event — is deferred to Phase 4.6, so the row counts as `deferred` until it lands. |

### `src/Reactor/Hosting/Shell/ThumbnailToolbar.cs` — Phase C.4

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| `Update vs Add Buttons` | 1 | `PromoteEvent` | deferred | Same shape — `HResultFailed` shipped, typed `ThumbnailToolbarSetButtonsFailed` deferred to Phase 4.6. |

### `src/Reactor/Hosting/Shell/TrayFlyoutHostWindow.cs` — Phase C.4

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| `GetDpiForMonitor` | 1 | `PromoteEvent` | deferred | Same. |

### `src/Reactor.Advanced/Markdown/Md4cParser.Block.cs` — Phase C.1 (commit `79b27be6`)

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| `Debug.Fail("Unreachable")` sites | 4 | `Propagate` | shipped | Raised as `UnreachableException` — a release-visible crash. These are genuine state-machine impossibilities. The `Reconciler.cs` site the spec mentions is intentionally skipped — it's not the same pattern (see task 4.1). |

### `src/Reactor.Advanced/Docking/Native/DockHostLiveAnnouncer.cs` — spec 045 §2.22 focus fallback (PR #938)

First `Reactor.Advanced` entry in this audit. Not a `Debug.WriteLine`
migration — a **new** swallow site introduced alongside the R4 focus-hand-off
fix — but recorded here because §6.7 is the permanent record for framework
swallow sites and this one has a verdict worth pinning.

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| `TryFocus` focus hand-off | 1 | `Narrow` | shipped | Narrowed to `ArgumentException` / `COMException` / `InvalidOperationException`; anything else propagates. Emitted via `ReactorEventSource.Log.SwallowedError("Docking", …)` **directly**, not `DiagnosticLog` — the event source is already on the `AdvancedInternalSurfaceTests` allowlist (spec 062 §7) and docking already uses it from `DockLayoutSerializer`, whereas the helper would drag `DiagnosticLog` + `LogCategory` onto that allowlist for a byte-identical payload. Trade-off accepted: no DEBUG `Debug.WriteLine` mirror. |

**Failure modes the catch hides.** `FocusManager.FindFirstFocusableElement` /
`TryFocusAsync` / `Control.Focus` are WinRT projections invoked during a pane
close. `ArgumentException` is the parameter-validation class — and is exactly
what the pre-fix `TryMoveFocusAsync(Next, FindNextElementOptions)` pairing
threw on *every* hand-off. `COMException` covers interop failure below the
projection. `InvalidOperationException` covers element/visual-tree state, the
same class `PackagedSettingsStore` narrowed to on its WinRT surface.

**Why swallowing is right for those three, and only those three.** The
hand-off is an accessibility nicety on the pane-close path; a failed focus
move must not take the app down mid-close. But the *broad* catch was not
defensible: this site is the R4 bug — a fire-and-forget `_ =` discard meant an
`ArgumentException` fired on every close, focus never moved, and nobody saw it
for as long as the code existed. Applying §6.7.2 the way the ReactorWindow /
JsonFileStore second pass did, a surprise exception out of the focus stack is
a bug someone needs to see, not absorb. Hence Narrow rather than Keep.

**Guard.** `DockHostFocusFallbackTests.Announcer_narrows_the_focus_catch`
reads the method's exception regions out of IL and fails if a handler catches
`System.Exception`, so the narrowing cannot be quietly widened back.

### `src/Reactor/Hosting/Messaging/WindowMessageMonitor.cs` — Win32 `TryXxx` candidates, deferred (Phase 4.8)

Spec §6.7.4 calls for ~10 sites where `bool Try* (out int hr)` is the right
shape. Each already returns a `bool`, so the conversion is mechanical. None
have shipped yet because the `GetLastError` path still needs the
swallowed-error audit trail until the conversion lands. Tracked as task 4.8.
The two sibling files are listed under their own headings below so every
ledger row sits under the path it describes.

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| P/Invoke wrappers | 6 | `TryXxx` | deferred | Task 4.8. |

### `src/Reactor/Hosting/Persistence/MonitorEnumeration.cs` — Win32 `TryXxx` candidates, deferred (Phase 4.8)

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| `EnumDisplayMonitors`-shape callers | 2 | `TryXxx` | deferred | Task 4.8. |

### `src/Reactor/Hosting/WindowIcon.cs` — Win32 `TryXxx` candidates, deferred (Phase 4.8)

| Site(s) | Sites | Verdict | Status | Notes |
|---|---|---|---|---|
| HICON loaders | 2 | `TryXxx` | deferred | Task 4.8. |

<!-- ledger:end -->

---

## Not ledger entries

### `Reconciler.cs:~2635` — carved out

The spec §4.3 also mentioned 1 site in `Reconciler.cs:~2635`. Audit
note: that site is not a `Debug.Fail("Unreachable")` — its message
is `"ElementRef<{T}> attached to a {U}. Use ElementRef<U> or
untyped ElementRef."` — and the containing `AssertTypedRefMatch`
method is already `[Conditional("DEBUG")]`. Leaving as-is until a
reviewer requests a behavior change.

### Shell typed-event promotion — Deferred (Phase 4.6)

Spec §6.7.4 calls for ~15 sites in the Shell namespace to graduate
from the generic `HResultFailed` event to subsystem-specific typed
events. The Phase C.4 migration shipped the `HResultFailed` shape
for 8 of those, which delivers the release-visibility goal. The
typed events (`JumpListSaveFailed(hr)`,
`ThumbnailToolbarSetButtonsFailed(hr)`, etc.) are a downstream
ergonomic improvement — an MCP agent filtering on
`Keywords.Shell & EventName=JumpListSaveFailed` is more discoverable
than grepping `operation="JumpList.Begin"` strings. Tracked as task
4.6.

**This is work tracking, not a ledger section.** Those sites are already
counted once, under `PromoteEvent`, in the `JumpListComInterop.cs`,
`ThumbnailToolbar.cs` and `TrayFlyoutHostWindow.cs` rows above. The same
applies to the four ConnectedAnimation sites §6.7.4 wants promoted: they are
counted once under `Keep`, the verdict actually recorded for them. An earlier
revision of the distribution table counted both of those groups a second time
under "Promote to typed event — deferred", which is one of the ways its
figures stopped being reproducible.

---

## Audit completeness against §3.5

- [x] Every site in the §0.3 inventory maps to exactly one entry in
  this file or is explicitly carved out as framework-internal
  (`Debug.Assert`, pure trace prints).
- [x] Verdict distribution recorded at the top — now **derived** from the
  ledger rows and gated by
  `tests/Reactor.Tests/Docs/SwallowedErrorAuditTests.cs`, so it cannot
  silently drift (issue #959).
- [ ] Per-site line-by-line review by a second pair of eyes — invited
  via the PR that introduces this file.
- [x] No code changes in this PR — it's the audit's permanent home.
- [ ] Reconcile the ledger against today's `src/` — the §G9 sweep described
  under **Ledger-vs-source drift**. Separate from the counts, which are
  correct for what they count.
