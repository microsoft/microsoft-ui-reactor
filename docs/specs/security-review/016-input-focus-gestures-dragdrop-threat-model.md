# Chunk 16 — Input, focus, gestures, drag/drop

**Status:** Phase 2 — review complete
**Reviewed commit SHA:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Reviewer scope:** STRIDE + code review with focus on the drag/drop trust boundary (cross-process payloads from arbitrary other Windows processes), focus revalidation correctness, and gesture-state lifetime / re-entrancy.

---

## 1. Scope

| File | LOC | Role |
|---|---|---|
| `src/Reactor/Input/DragConfigs.cs` | 72 | Immutable `DragSourceConfig` / `DropTargetConfig` records + `DragOperationNegotiation` (Move > Copy > Link). |
| `src/Reactor/Input/DragData.cs` | 370 | Drag payload container; format-entry registry; eager + lazy provider plumbing; same-process `_transfers` GUID registry; `DataPackage` write/read adapters. |
| `src/Reactor/Input/DragOperations.cs` | 32 | `[Flags] DragOperations` enum + `DragEndContext` record. |
| `src/Reactor/Input/DragTargetArgs.cs` | 67 | Drop-callback argument; `DragUIOverrideHandle`. |
| `src/Reactor/Input/FocusManager.cs` | 56 | `ElementRef` + imperative `Focus` / `FocusAsync` helpers. |
| `src/Reactor/Input/GestureConfigs.cs` | 78 | `PanGestureConfig` / `PinchGestureConfig` / `RotateGestureConfig` / `LongPressGestureConfig`. |
| `src/Reactor/Input/Gestures.cs` | 100 | Public `GesturePhase`, `PanAxis`, `PanGesture` / `PinchGesture` / `RotateGesture` / `LongPressGesture` records. |
| `src/Reactor/Core/FocusRevalidationService.cs` | 121 | Window-activation → `QueryCache.Invalidate` for hooks that opted in via `RefetchOnWindowFocus`. |
| `src/Reactor/Core/Reconciler.DragDrop.cs` | 335 | Per-element drag/drop trampoline state; `OnDragStarting`, `OnDragEnter/Over/Leave/Drop`, `OnDropCompleted`; `ResolveDragData` / `BuildViewBackedDragData`. |
| `src/Reactor/Core/Reconciler.Gestures.cs` | 470 | Per-element gesture trampoline state; manipulation pipeline (Started/Delta/Completed/InertiaStarting); long-press touch + mouse-emulation timer. |
| **Total** | **1701** | |

**Out-of-band but referenced for context:**

- `src/Reactor/Elements/ElementExtensions.cs:380–484` — fluent `OnDragStart` / `OnDrop<T,TPayload>` / `OnDrop<T>` / `OnDragEnter` / `OnDragOver` / `OnDragLeave` / `DraggableWhen` extension methods (call sites that produce the configs reviewed here).
- `src/Reactor/Core/Reconciler.cs:2314–2317` — `ApplyGestureHandlers` / `ApplyDragDropHandlers` are invoked here from the modifier-application path.
- `src/Reactor/Hosting/ReactorHost.cs:141–159` — wires `Window.Activated` to `FocusRevalidationService.RevalidateNow`.
- `src/Reactor/Hooks/UseResource.cs:404–428` — opt-in enrollment for focus revalidation via `ResourceOptions.RefetchOnWindowFocus`.
- `src/Reactor/Core/QueryCache.cs:359–365` — `AppContexts.FocusRevalidation` ambient context.

**Out of scope (covered by other chunks but cross-referenced in §8):**
- `src/Reactor/Hooks/UseFocusTrap.cs` — *Tab*-cycle focus trap (Chunk 23 — Hooks). Surfaced by the chunk brief's mention of "focus trap correctness"; one finding referred there.
- WinUI / WindowsAppSDK XAML drag/drop primitives (`DataPackage`, `DataPackageView`, `IStorageItem`) — trusted dependency.
- The Windows shell / OLE marshaling layer (`ms-data` clipboard formats, `IDataObject` lifetime) — trusted dependency.

---

## 2. Data-flow diagram

```
                ┌──────────────────────────────────────────────┐
                │ Application code                              │
                │ (.OnDragStart, .OnDrop<T>, .OnDrop<T,TPayload>,│
                │  .OnPan, .OnPinch, .OnLongPress, ElementRef)  │
                └────────────────┬─────────────────────────────┘
                                 │ (in-proc, trusted)
                                 ▼
                ┌──────────────────────────────────────────────┐
                │ ElementModifiers { DragSource, DropTarget,    │
                │   Pan, Pinch, Rotate, LongPress }             │
                └────────────────┬─────────────────────────────┘
                                 │
                                 ▼
            ┌────────────────────────────────────────────────────┐
            │ Reconciler.DragDrop / Reconciler.Gestures          │
            │  • ConditionalWeakTable<FE, DragDropState|         │
            │     GestureState>                                   │
            │  • Stable trampolines attached once per element     │
            │  • AddHandler(handledEventsToo:true) so Button/Ctrl │
            │     internal handling does not silence us          │
            └────────────────┬───────────────────────────────────┘
                             │
                             ▼
        ┌───────────────────────────────────────────────────────┐
        │ WinUI input pipeline (UIElement events)                │
        │  • DragStarting / DropCompleted (source)               │
        │  • DragEnter / DragOver / DragLeave / Drop (target)    │
        │  • ManipulationStarted / Delta / Completed / Inertia   │
        │  • Holding (touch/pen) + PointerPressed/Moved/Released │
        │     (mouse emulation, dispatcher timer)                │
        └────────────────┬───────────────────────────┬──────────┘
                         │                           │
   ────── PROCESS BOUNDARY (drag only) ──────        │
                         │                           │
       ┌─────────────────┴─────────┐                 │
       │ Cross-process source       │                 │
       │ (Notepad, Word, Explorer,  │                 │
       │  any local desktop app)    │                 │
       │                            │                 │
       │  • DataPackage formats:    │                 │
       │     Text, WebLink, Html,   │                 │
       │     Rtf, StorageItems,     │                 │
       │     Bitmap, custom (any)   │                 │
       │  • DataPackage.Properties  │                 │
       │     (string→object dict)   │                 │
       └────────────┬───────────────┘                 │
                    │                                 │
                    ▼                                 │
   ┌───────────────────────────────────────┐          │
   │ ResolveDragData (DragDrop.cs:186)     │          │
   │  1. If Properties[reactor/transfer-id] │          │
   │     parses + Resolve() hits, return    │          │
   │     in-memory DragData (same proc).    │          │
   │  2. Else BuildViewBackedDragData →     │          │
   │     wrap DataPackageView with lazy     │          │
   │     async providers (Text, WebLink,    │          │
   │     Html, Rtf, StorageItems, Bitmap).  │          │
   └────────────┬──────────────────────────┘          │
                │                                     │
                ▼                                     ▼
        ┌──────────────────────────────────────────────────┐
        │ App callback                                      │
        │  • DropTargetConfig.OnDrop / TypedDrop / Enter/   │
        │     Over/Leave                                    │
        │  • Pan/Pinch/Rotate/LongPress callbacks            │
        └───────────────────────────────────────────────────┘

Window-activation revalidation (orthogonal flow):

  Window.Activated  (state != Deactivated, FeatureFlag ON)
        │
        ▼
  FocusRevalidationService.RevalidateNow()
        │   throttle 30 s
        ▼
  snapshot _enrolled keys → for each stale → QueryCache.Invalidate
        │
        ▼
  EntryChanged fires → UseResource hook re-renders → app refetches
```

---

## 3. Trust boundaries crossed

| Boundary | Direction | Assumption / Reality |
|---|---|---|
| **Same Reactor process ↔ another desktop process (drag)** | Inbound when we are a drop target (any desktop app may be the source); outbound when the user drags out to e.g. Notepad/Explorer. | **The source process is fully untrusted.** It writes arbitrary `DataPackage` formats and arbitrary `DataPackage.Properties` keys/values. Reactor's drop-side code must treat both the format catalog and every byte of payload as adversarial. |
| **Application code ↔ Reactor input layer** | App callbacks run in-proc on the UI dispatcher. | App-supplied callbacks are trusted code (they are the app developer's). The framework does not isolate them. |
| **Window focus state ↔ data-fetch policy** | `Window.Activated` triggers cache invalidation. | Activation source is the Windows compositor — trusted. The throttle and feature flag are guards against unintended refetch storms, not against an attacker. |
| **Input device → element** | WinUI delivers pointer / manipulation / hold events. | WinUI is a trusted dependency. Pointer device type, position, and modifier-key state are trusted as reported. |

Reactor’s drag/drop layer is the **only** place in this chunk where data crosses a real adversary boundary.

---

## 4. Asset inventory

What's worth attacking through this surface:

1. **Process integrity.** A hostile drag source dropping into a Reactor-built app is the textbook case for an OLE / clipboard exploit: malformed Bitmap, oversized Text/Html/Rtf to OOM the receiver, malicious `IStorageItem` paths leading the app to open files outside its consent surface (UNC, `\\?\GLOBALROOT\…`, junctions), large filename lists.
2. **Confidentiality of in-app drag data.** Same-process `DragData` payloads stored in the static `_transfers` registry must not be readable by other processes (they aren't — by reference, not serialization), but the *spoofing* risk is "can a cross-process source claim to be us and bypass our validation?"
3. **Capability of the drop callback.** `OnDrop<T,TPayload>` deserializes a *typed* payload — if a cross-process source could produce a payload that satisfies the type-match check, it would gain a code path the developer probably reserved for in-app sources.
4. **Memory bounds.** Both directions: lazy fetch of unbounded text/HTML/RTF/bitmap payloads can OOM. Dispatcher-timer leak from gesture mouse-emulation if pointer events are not delivered as expected.
5. **Cache freshness.** `FocusRevalidationService` is the single bottleneck for "force refetch on focus." If a hostile actor can either bypass the throttle (free-running refetch storm → DoS, cost amplification on metered APIs) or *prevent* a sweep (forced staleness in security-relevant data), the consumer is affected.
6. **Focus integrity.** `FocusManager.Focus` is purely declarative; the only assets it touches are WinUI's own focus state. Trap behaviour is in `UseFocusTrap` (Chunk 23) and is referred, not assessed.

---

## 5. STRIDE table

| # | Cat | Threat | Attacker | Impact | Likelihood | Current mitigation | Finding |
|---|---|---|---|---|---|---|---|
| 1 | **S** | Hostile cross-process source forges `Properties[reactor/transfer-id]` to a known GUID and intercepts in-flight typed payload from a Reactor-internal drag. | Local malicious process. | Type-confused payload reaches a `OnDrop<T,TPayload>` callback under attacker influence. | Low — `_transfers` GUID is `Guid.NewGuid()` (128-bit, unguessable). | `DragData.cs:281` uses cryptographically random GUID; cross-process guess success ≈ 2⁻¹²⁸. | F-1 (informational). Add a defense-in-depth process-id check before honoring transfer-id. |
| 2 | **S** | Hostile source declares `reactor/typed/<KnownType>` as a `DataPackage.Properties` entry, hoping `TryGetTypedPayload<TKnownType>` returns true on the cross-process side. | Any local process. | Typed-drop callback runs with attacker-supplied payload. | Effectively 0 — cross-process path goes through `BuildViewBackedDragData` (`Reconciler.DragDrop.cs:201`) which only registers the six standard formats. Typed entries are never populated cross-process. | Hardcoded format whitelist in `BuildViewBackedDragData`. | F-2 (good — document the invariant). |
| 3 | **T** | Hostile source delivers a multi-GB Text/Html/Rtf/Bitmap; app awaits `GetTextAsync` and OOMs. | Any local process. | DoS of the Reactor app process (and possibly system if swap is exhausted). | Medium — there is no Reactor-level cap; WinUI does not impose a documented one. | None at the Reactor layer. Apps must defensively cap. | **F-3 (Medium)**. |
| 4 | **T** | Hostile source delivers `StorageItems` with paths that, if the app blindly opens them, escape the user's consent (UNC `\\attacker.tld\share\…`, `\\?\GLOBALROOT\…`, junctions / mount points crossing security domains, `con` / `nul` device names). | Any local process. | App-defined: confused deputy; app reads / persists / displays unintended content. | App-dependent; the framework forwards `IStorageItem`, leaving open the developer footgun. | None — Reactor never inspects paths. | **F-4 (Medium)** — guidance/sanitization helper missing. |
| 5 | **R** | Drop event triggers app side-effects with no log; no tamper-evident trail of who was the source. | Any. | Repudiation around *what was dropped from where*. | App-dependent. | None at the Reactor layer; `DragData.OriginProcessId` (`DragData.cs:52`) is captured but never surfaced cross-process. | F-5 — surface `Properties[reactor/proc-id]` to apps. |
| 6 | **I** | Same-process DragData GUID + payload could in principle be exfiltrated if the *registry* were accessible across processes. | N/A in current implementation. | None — `_transfers` is process-local. | None. | Process-local static dictionary. | OK. |
| 7 | **I** | Modifier-key state delivered to drop callbacks (`DragTargetArgs.Modifiers`) lets a callback infer what the user is holding. | App is in-proc — already trusted with this information. | None outside the app. | None. | None needed. | OK. |
| 8 | **D** | Hostile source declares thousands of custom formats; `BuildViewBackedDragData` foreach over `view.AvailableFormats` enumerates all of them at every drag-enter / drag-over / drop event. | Any local process. | Repeated per-event cost; combined with the unbounded-payload threat (#3), an amplifier. | Low — work per format is one branch + a delegate allocation. | None. WinUI imposes platform-level format-count limits but they are not documented. | **F-6 (Low)**. |
| 9 | **D** | Long-press mouse-emulation timer is armed on `PointerPressed` and disarmed on `PointerReleased` / `PointerCaptureLost`. If the gesture config is *replaced* (re-render with `LongPress = null`) while a press is in-flight, the timer is not stopped. | App developer (footgun, not adversary). | Stale timer fires `OnTriggered(GesturePhase.Began)` after the gesture was logically removed. | Low. | `state.LongPress` re-assigned to null at `Reconciler.Gestures.cs:131`; the timer-tick lambda *does* read `state.LongPress is not { } liveCfg` (line 246) and bails — but the trampoline trampoline still records pressed-time state. | F-7 — minor cleanup, not security. |
| 10 | **D** | `OnDragStarting` registers a transfer GUID at `Reconciler.DragDrop.cs:133`, then calls `data.PopulatePackage(e.Data)` at line 149. If `PopulatePackage` throws, WinUI may or may not invoke `DropCompleted`; on a missed `DropCompleted` the entry leaks in `_transfers` for process lifetime. | App developer (lazy provider throws). | Memory leak — a leaked `DragData` keeps any captured payload (including bitmaps) live. | Low — most provider lambdas don't throw synchronously inside `PopulatePackage`; only the eager-write branch reaches `package.SetText` etc. on the synchronous path. | None — no try/finally around the `Register` ↔ `OnDropCompleted` window. | **F-8 (Low–Medium)**. |
| 11 | **D** | `FocusRevalidationService.RevalidateNow` re-enters via `EntryChanged → Unenroll`. The implementation already snapshots `_enrolled` outside the lock, but `_lastSweepUtc` is updated *before* invalidation runs — so a synchronous re-entry from inside the foreach is throttled out and harmless. | None. | None. | — | `FocusRevalidationService.cs:80–89`. | OK — verified safe. |
| 12 | **E** | Cross-process drag source supplies `WebLink` of `file://`, `ms-appx://`, `javascript:`, custom protocols; an app naively passes the `Uri` to `Launcher.LaunchUriAsync` and arbitrary URI handlers fire. | Any local process. | App-defined; potentially arbitrary URI-handler invocation. | App-dependent. | None. The framework hands the `Uri` through unmodified. | **F-9 (Medium)** — guidance-level, but the guide example does not warn. |
| 13 | **E** | Source-side custom-format payload is a sentinel string (`Properties[fmt] = fmt`) for unknown eager formats (`DragData.cs:359–362`); a cross-process consumer reading the property treats it as the actual data. | Any cross-process consumer that we drag *into*. | The cross-process consumer sees a string equal to the format-id rather than the payload — confusing but not a payload leak. | Low. | This is only a risk if a developer relies on cross-process consumption of *non-standard* formats. | F-10 — document. |
| 14 | **T** | Lazy `DataProviderHandler` (`DragData.cs:317–339`) runs on `Task.Run` and writes `request.SetData(value)` from a worker thread. WinUI's `DataProviderRequest` is not documented as thread-safe. | App developer; manifests as race only with concurrent reads. | Possible WinUI-internal corruption; manifests as crash, not RCE. | Low. | `try/catch` around the resolve swallows exceptions but doesn't synchronize. | F-11 — review whether `DataProviderRequest.SetData` requires UI-thread affinity. |
| 15 | **E** | `Guid.TryParseExact(idStr, "N", out var id)` on attacker-supplied `Properties[reactor/transfer-id]` does not validate `idObj is string`. (Already does — line 190.) | — | — | — | Already type-checks `idObj is string`. | OK. |

---

## 6. Findings

Severity scale: **Critical** / **High** / **Medium** / **Low** / **Informational**.

### F-1 — `ResolveDragData` does not verify the same-process invariant before honoring `Properties[reactor/transfer-id]` *(Informational)*

**Location:** `src/Reactor/Core/Reconciler.DragDrop.cs:186–199`.

The same-process fast path:

```csharp
if (e.DataView.Properties.TryGetValue(DragData.TransferIdFormatId, out var idObj)
    && idObj is string idStr
    && Guid.TryParseExact(idStr, "N", out var id))
{
    var registered = DragData.Resolve(id);
    if (registered is not null) return registered;
}
```

A cross-process source can write any string at `reactor/transfer-id` (the `DataPackage.Properties` dictionary is propagated across the boundary). Today this is safe because the GUID is unguessable (128-bit `Guid.NewGuid()` at `DragData.cs:281`) and `Resolve` returns `null` on miss, falling through to the cross-process path. But the design relies on the GUID's unguessability rather than on identity verification.

Reactor *already* writes `Properties[reactor/proc-id] = OriginProcessId` at `Reconciler.DragDrop.cs:142–143`. **It is never consulted on the receive side.** Consulting it before trusting `reactor/transfer-id` would convert this from a probabilistic defense to an authoritative one:

```csharp
// Defense in depth: only honor the in-memory registry when proc-id matches.
if (e.DataView.Properties.TryGetValue(DragData.ProcIdFormatId, out var pidObj)
    && pidObj is string pidStr
    && int.TryParse(pidStr, out var pid)
    && pid == Process.GetCurrentProcess().Id
    && e.DataView.Properties.TryGetValue(DragData.TransferIdFormatId, out var idObj)
    …)
```

**Recommendation:** add the proc-id check as a belt-and-suspenders measure. Cost is one extra dictionary lookup per drop.

### F-2 — Cross-process `BuildViewBackedDragData` correctly excludes typed payloads *(Good — document as invariant)*

**Location:** `src/Reactor/Core/Reconciler.DragDrop.cs:201–224`.

`BuildViewBackedDragData` only adds entries for the six WinUI-standard formats (`Text`, `WebLink`, `Html`, `Rtf`, `StorageItems`, `Bitmap`). It deliberately does *not* iterate `view.AvailableFormats` for `reactor/typed/...` keys. As a result, `TryGetTypedPayload<T>` (`DragData.cs:96–106`) on a cross-process drag always returns `false`, and `OnDrop<T,TPayload>` (the typed convenience overload) silently no-ops. **This is the single most important security property of this chunk** and is currently maintained only by the format whitelist in `BuildViewBackedDragData`.

**Recommendation:** add an XML-doc remark on `TryGetTypedPayload` and on `BuildViewBackedDragData` calling out the invariant, plus a unit test that asserts a `DataPackageView` containing `Properties["reactor/typed/Foo"]` produces a `DragData` for which `TryGetTypedPayload<Foo>` returns `false`.

### F-3 — No size cap on cross-process Text / Html / Rtf / Bitmap payloads *(Medium)*

**Location:** `src/Reactor/Core/Reconciler.DragDrop.cs:201–224` (provider construction) and `src/Reactor/Input/DragData.cs:200–224` (await sites).

`BuildViewBackedDragData` registers async providers like:

```csharp
data.WithText(async ct => await view.GetTextAsync().AsTask(ct).ConfigureAwait(false));
```

A hostile cross-process source can write an arbitrarily large string (gigabytes); the receiving app awaits `GetTextAsync()` and materializes the entire payload in managed memory before the user callback runs. Same for `GetHtmlFormatAsync` / `GetRtfAsync` / `GetBitmapAsync`. There is no documented WinUI cap and Reactor adds none. The framework guidance (`docs/guide/input-and-gestures.md:312–340`) shows `args.Data.TryGetText(out var text)` with no size handling.

**Recommendation:** add an opt-in cap on the `DataPackageView`-backed providers (e.g. `DropTargetConfig.MaxPayloadBytes` defaulting to a few MB), or at minimum a chunked-read helper plus prominent guide warning. Consider streaming the text via a `Stream`-shaped accessor rather than `Task<string>`.

### F-4 — No path / scheme validation on cross-process `IStorageItem` payloads *(Medium)*

**Location:** `src/Reactor/Core/Reconciler.DragDrop.cs:214–219`; `src/Reactor/Input/DragData.cs:175–186, 204–216`.

Cross-process file drops produce `IStorageItem` instances that include UNC paths (`\\server\share\...`), DOS-device paths (`\\?\...`, `\\?\GLOBALROOT\...`), reparse-point traversals, and shell virtual items (`shell:::{GUID}`). Reactor's accessors (`TryGetFiles`, `GetFilesAsync`) hand the items through verbatim. An app that calls `StorageFile.OpenAsync(...)` on the result, or extracts `.Path` and feeds it to `File.ReadAllBytes`, exposes itself to:

- UNC paths to attacker-controlled hosts (file-handle hangs, NTLM-relay exposure if the path is opened with the user's credentials).
- Path traversal where the dragged "file" is actually a junction pointing outside an expected sandbox.
- "MotW" / Mark-of-the-Web inheritance: file dragged from a low-integrity browser into a higher-integrity app may carry zone identifiers; consumer must check.
- Empty or pathological filenames (e.g. trailing spaces, alternate data streams `file.txt:hidden`).

Reactor cannot make the policy decision for the app, but it should provide a sanitization helper and document the threat. The current guide shows no such warning.

**Recommendation:** add a `DragData.TryGetSafeLocalFiles(out IReadOnlyList<StorageFile>)` helper that:
1. Drops any item whose `.Path` is null/empty.
2. Drops any item with a UNC or DOS-device prefix unless an opt-in flag is set.
3. Optionally checks zone-identifier MotW.
4. Document the residual app responsibility (canonicalize → allowlist root → reject anything escaping).

### F-5 — Cross-process origin (`reactor/proc-id`) is captured but never surfaced *(Low)*

**Location:** `src/Reactor/Input/DragData.cs:52, 60–61`; `src/Reactor/Core/Reconciler.DragDrop.cs:142–143, 186–199`.

`DragData.OriginProcessId` is set on the source side and copied into `DataPackage.Properties[reactor/proc-id]`. On the target side, neither `BuildViewBackedDragData` nor `DragTargetArgs` exposes it. An app that wants to log "drop arrived from PID X" or "drop arrived from a *different* process so we should treat the payload as untrusted" cannot do so without raw `DataPackageView` access.

**Recommendation:** add `DragTargetArgs.SourceProcessId` (nullable int — null when not advertised) and populate it from `Properties[reactor/proc-id]` (string-typed and bounded-length parsed). Useful for both audit logs and per-source policy.

### F-6 — `BuildViewBackedDragData` enumerates *every* format and registers a provider *(Low)*

**Location:** `src/Reactor/Core/Reconciler.DragDrop.cs:204`.

```csharp
foreach (var format in view.AvailableFormats)
```

If a hostile source declares many formats (the WinUI ceiling is undocumented but tests of OLE clipboards in the wild have seen >1000 entries), this runs a string compare + delegate allocation per format on every `DragEnter` / `DragOver` / `DragLeave` / `Drop` (because `ResolveDragData` calls `BuildViewBackedDragData` each time at line 198). Same drag → many events → many allocations. Combined with F-3, this is a multiplier.

**Recommendation:** memoize `BuildViewBackedDragData` per `DataPackageView` (weak keyed) or build it once on `DragEnter` and cache for the duration of the drag session via `DragDropState`.

### F-7 — Long-press mouse-emulation timer survives a config-to-null transition *(Informational)*

**Location:** `src/Reactor/Core/Reconciler.Gestures.cs:131, 237–258`.

`ApplyGestureHandlers` overwrites `state.LongPress = m.LongPress` (line 131), but if the new modifier set has `LongPress = null` while a `LongPressMouseTimer` is armed, the timer is *not* stopped. The timer-tick lambda (line 245) does check `if (state.LongPress is not { } liveCfg) return;` so the gesture is correctly suppressed, but the timer itself runs once needlessly.

Not a security issue — there's no leak (the lambda captures `state` and `timer`, both reachable while the element is live; both eligible for GC when the element is collected because `_gestureStates` is `ConditionalWeakTable`). Worth fixing for cleanliness.

**Recommendation:** when `m.LongPress` is null and `oldM?.LongPress` is non-null, call `state.LongPressMouseTimer?.Stop()` and clear `state.LongPressMouseArmed`.

### F-8 — `OnDragStarting` registers the transfer before populating the package; an exception leaks the entry *(Low–Medium)*

**Location:** `src/Reactor/Core/Reconciler.DragDrop.cs:114–150`.

```csharp
var transferId = DragData.Register(data);
state.ActiveTransferId = transferId;

var allowed = src.AllowedOperations ?? DragOperations.All;
e.AllowedOperations = ToWinUI(allowed);

e.Data.Properties[DragData.TransferIdFormatId] = transferId.ToString("N");
e.Data.Properties[DragData.ProcIdFormatId] =
    data.OriginProcessId.ToString(global::System.Globalization.CultureInfo.InvariantCulture);

data.PopulatePackage(e.Data);  // ← may throw on a buggy app provider
```

If `PopulatePackage` throws, WinUI is responsible for whether `DropCompleted` still fires. The contract is unclear; on platforms where it does not fire, `OnDropCompleted` never runs and `DragData.Unregister` is never called, leaking the registered entry — and the captured `DragData` keeps any payload alive (e.g. a multi-MB bitmap reference).

**Recommendation:** wrap registration → eager-population in `try { … } catch { DragData.Unregister(transferId); state.ActiveTransferId = Guid.Empty; throw; }`. Also consider keying registrations to weak references with a finalizer-driven sweep.

### F-9 — Unfiltered `Uri` returned to drop callbacks; no scheme allowlist *(Medium — guidance gap)*

**Location:** `src/Reactor/Input/DragData.cs:172, 201`; `src/Reactor/Core/Reconciler.DragDrop.cs:208–209`.

`TryGetUri(out Uri value)` and `GetUriAsync` return whatever the source wrote. WinUI's `DataPackageView.GetWebLinkAsync` is documented to return any `Uri`, including `file://`, `javascript:`, custom schemes, `ms-appx://`, etc. The framework guide (`docs/guide/input-and-gestures.md`) does not warn the developer to allowlist schemes before passing the `Uri` to `Launcher.LaunchUriAsync`. An app that does `Launcher.LaunchUriAsync(args.Data.GetUriAsync().Result)` is one drag away from arbitrary protocol-handler invocation.

**Recommendation:** add a `DragData.TryGetSafeWebLink(out Uri uri)` helper that returns true only for `http`/`https` (configurable allowlist), and document the threat in the input-and-gestures guide.

### F-10 — Custom-format eager write uses a self-referential sentinel *(Informational)*

**Location:** `src/Reactor/Input/DragData.cs:359–362`.

```csharp
if (!package.Properties.ContainsKey(formatId))
    package.Properties[formatId] = formatId;
```

For an unknown eager format, the source side writes the *format-id string* as the value. A cross-process consumer that asks for the data sees the format-id literal rather than the user's payload. Not a security flaw — consumers have no way to extract the real value because cross-process custom formats are typed-payload-only, which is gated to same-process by F-2 — but it would surprise a developer expecting cross-process custom-format support.

**Recommendation:** either remove the sentinel write (so unknown formats are simply absent on the cross-process boundary) or emit a debug warning at first occurrence, with a `<remarks>` explaining.

### F-11 — Lazy provider's `request.SetData` runs on a worker thread *(Informational)*

**Location:** `src/Reactor/Input/DragData.cs:317–338`.

```csharp
package.SetDataProvider(formatId, request =>
{
    var deferral = request.GetDeferral();
    _ = global::System.Threading.Tasks.Task.Run(async () =>
    {
        try
        {
            var resolved = await localEntry.ResolveAsync(default).ConfigureAwait(false);
            if (resolved is not null)
                WriteResolvedToRequest(request, formatId, resolved);
        }
        …
    });
});
```

`DataProviderRequest.SetData` is called from a `Task.Run` worker. WinUI does not document `DataProviderRequest` as thread-safe. The `DataPackage` itself is created on the UI thread; whether `SetData` from a non-UI thread is supported is an open question. The exception path swallows everything, so a thread-affinity violation would manifest as silent failure (no payload delivered) rather than a crash.

**Recommendation:** confirm with WinUI docs whether `SetData` requires UI-thread affinity; if so, marshal back via the dispatcher before `SetData`. If not, document the contract.

### F-12 — `e.Modifiers` (`DragDropModifiers`) is forwarded raw; no semantic mapping *(Informational)*

**Location:** `src/Reactor/Input/DragTargetArgs.cs:38, 59`; `src/Reactor/Core/Reconciler.DragDrop.cs:241`.

The `DragTargetArgs.Modifiers` property exposes `Windows.ApplicationModel.DataTransfer.DragDrop.DragDropModifiers` directly. This is a semi-sealed WinUI enum (LeftButton/RightButton/MiddleButton/Shift/Control/Alt). It's not a security issue, but it leaks an unsanitized WinUI enum across what is otherwise a Reactor-shaped public surface — a point worth tracking for API consistency.

**Recommendation:** wrap as a Reactor-flavored enum, or document the type choice.

### F-13 — `FocusRevalidationService.Enroll` accepts arbitrary keys with no namespace or length cap *(Informational)*

**Location:** `src/Reactor/Core/FocusRevalidationService.cs:51–55`.

```csharp
public void Enroll(string key)
{
    if (string.IsNullOrEmpty(key)) return;
    lock (_lock) _enrolled.Add(key);
}
```

The set is unbounded; a buggy hook in a tight loop (or malformed cache-key derivation) could grow `_enrolled` without limit. Each sweep iterates the whole set. This is in-process / app-author-induced — not crossing a trust boundary — but a defensive cap (`HashSet<string>` with a sanity ceiling, or refusing keys longer than a few KB) would catch the runaway-key class of bug.

**Recommendation:** cap at a high but finite size (e.g. 10⁵ entries) and log when the cap is hit.

### F-14 — `RevalidateNowForce` resets `_lastSweepUtc = MinValue` and is publicly callable *(Informational)*

**Location:** `src/Reactor/Core/FocusRevalidationService.cs:107–111`.

`RevalidateNowForce` is documented "diagnostic / test-only — production code paths should go through `RevalidateNow`." It is `public`. Apps that call it on every focus event re-create the refetch storm the throttle was designed to prevent.

**Recommendation:** either move it to an `internal` test seam (and expose for tests via `InternalsVisibleTo`) or rename to make the warning impossible to miss (e.g. `ForceRevalidateBypassingThrottle_DiagnosticOnly`).

### F-15 — `FocusManager.Focus` and `FocusAsync` accept a null `target` and silently return false *(Informational)*

**Location:** `src/Reactor/Input/FocusManager.cs:38–55`.

```csharp
public static bool Focus(ElementRef target, FocusState state = FocusState.Programmatic)
{
    if (target?._current is not { } fe) return false;
    …
}
```

The signature is non-nullable but null is silently tolerated. Consistency is fine; documenting that `null` returns `false` (rather than `ArgumentNullException`) prevents surprised callers.

**Recommendation:** documentation only.

---

## 7. Open questions

1. **Does WinUI's `DataPackageView.GetTextAsync` (and friends) impose any size cap?** Spec literature is silent. If a cap exists and is generous (say, MB-scale) we still want a Reactor-level cap; if there is no cap we *need* one. (Drives F-3.)
2. **Is `DataProviderRequest.SetData` thread-safe?** Affects F-11.
3. **Does `DropCompleted` reliably fire when `DragStarting` throws after assigning `e.AllowedOperations` and `e.Data.Properties`?** Affects F-8 — informs whether the leak is theoretical or real.
4. **Should the framework provide a "trusted drag source" gate**, separate from `DraggableWhen`, where the *target* can require `Properties[reactor/proc-id] == ours` before unwrapping a typed payload? Today the type-system whitelist in `BuildViewBackedDragData` (F-2) is the only barrier; a defense-in-depth target-side check seems cheap.
5. **Should `RefetchOnWindowFocus` propagate per-cache instead of per-process?** Today `AppContexts.FocusRevalidation.DefaultValue` (`QueryCache.cs:364–365`) is a single static service bound to `QueryCache.DefaultValue`. Multi-window or multi-host apps may end up cross-invalidating. (Correctness, not security.)
6. **Should `OnDragLeave` skip `e.AcceptedOperation` propagation?** `InvokeTargetCallback` writes it unconditionally (`Reconciler.DragDrop.cs:254`). For DragLeave it is meaningless; for DragEnter / DragOver it is the negotiation surface. Behavior is harmless but conceptually noisy.
7. **`AddHandler(handledEventsToo: true)`** is used for both the drop-side (`Reconciler.DragDrop.cs:88, 93, 98, 103`) and long-press pointer events (`Reconciler.Gestures.cs:184, 189, 194, 199`). This means a **handled** event still drives our trampolines — including events handled by a *third-party* `Control` subclass for its own reasons. Are there scenarios where a `Control`'s "I marked this handled because the input was unsafe" expectation is violated? Worth a security sanity check on the `Button`/`HyperlinkButton`/`Control` overrides.

---

## 8. Out-of-scope referrals

- **`UseFocusTrap` (Chunk 23 — Hooks).** `src/Reactor/Hooks/UseFocusTrap.cs:42–69` only attaches a `LosingFocus` cancel-handler. Implications worth surfacing in Chunk 23:
  - The trap is a *cycle helper for keyboard Tab*; it is **not** a trap against programmatic `TryFocusAsync` from outside the container, against UIA-driven focus moves, or against pointer clicks that happen to also be focus moves.
  - The handler comment says "if Tab was going forward past the last element, move focus to the first focusable child" — but the implementation just sets `args.Cancel = true` and `args.Handled = true`. Focus does *not* cycle; it stays on the current element. The discrepancy between comment and behavior is worth fixing in Chunk 23.
  - The trap relies on `VisualTreeHelper.GetParent` walking up to the container; popups / flyouts / window content-dialogs that are not in the same visual tree will report "not a descendant" and the trap will cancel focus moves to legitimate targets.
- **Cross-process drop opening files (path/UNC/zone-id concerns) at the app layer.** Belongs equally with **Chunk 22 — Data system & controls** (the `FilePicker`-style controls there will face the same path-sanitization question) and Chunk 18 (sample-app native interop, especially `reactorfiles`). Track F-4's helper proposal there.
- **`Launcher.LaunchUriAsync` callers (F-9).** No call site exists in this chunk; the threat materializes at app-callback level. Track in Chunk 12 (parsers) for the deep-link side and in app-author guidance.
- **`Window.Activated` event (`ReactorHost.cs:151`)** — focus revalidation wiring. Chunk 15 (Hosting) holds the activation hook itself; F-13/F-14 are wholly within Chunk 16's `FocusRevalidationService`.
- **`UseResource` enrollment path (`UseResource.cs:404–428`)** — the consumer of `FocusRevalidationService.Enroll`. Belongs to Chunk 23 (Hooks). Verifying that `Unenroll` runs on hook dispose is Chunk 23's responsibility; nothing here prevents leaking enrolled keys if a hook fails to unenroll on unmount.
