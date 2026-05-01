# Chunk 14 — Reconciler & Component Model: Threat Model

**Status:** Draft — Phase 2 review
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Reviewer focus:** concurrency safety on hot paths; reentrancy through user effects; resource leaks; element-pool state leakage; weak-table lifetime correctness.

This chunk's threats are dominated by **availability** (DoS via leak, deadlock, livelock, runaway re-render) and **integrity** (cross-component state mixing through a recycled control). Confidentiality concerns are limited to one realistic info-disclosure path: the element pool can return a previously-mounted control whose subsystem state (UIA Name, focus rect, password text, etc.) was not cleared.

---

## 1. Scope

| File | Lines | Notes |
|---|---:|---|
| `src/Reactor/Core/Reconciler.cs` | 3467 | Orchestration, unmount, helpers, attached-state DP, event trampolines |
| `src/Reactor/Core/Reconciler.Mount.cs` | 2891 | Per-element mount; pool rent; event wiring |
| `src/Reactor/Core/Reconciler.Update.cs` | 3507 | Per-element update; bitmask diff; controlled-input snapback |
| `src/Reactor/Core/Reconciler.DragDrop.cs` | 335 | Drag/drop trampolines + transfer registry interop |
| `src/Reactor/Core/Reconciler.Gestures.cs` | 470 | Tap/double-tap/right-tap/holding |
| `src/Reactor/Core/Component.cs` | 195 | `Component`, `Component<TProps>`, props receiver/comparable interfaces |
| `src/Reactor/Core/Element.cs` | 2564 | Immutable element records, `ShallowEquals`, modifier surface |
| `src/Reactor/Core/ElementFactory.cs` | 141 | `IElementFactory` bridge for `ItemsRepeater` |
| `src/Reactor/Core/ElementPool.cs` | 344 | Per-type pool of `FrameworkElement` (cap 32 per type) |
| `src/Reactor/Core/ChildReconciler.cs` | 471 | Keyed (LIS) + unkeyed (positional) child diff |
| `src/Reactor/Core/ChildCollection.cs` | 101 | `IChildCollection` over `Panel.Children` / `ItemsControl.Items` |
| `src/Reactor/Core/RenderContext.cs` | 1168 | Hooks: state, effect, memo, ref, observable, navigation, command, locale |
| `src/Reactor/Core/Context.cs` | 29 | Context-API base type |
| `src/Reactor/Core/ContextScope.cs` | 52 | Stack of provider entries during traversal |
| `src/Reactor/Core/ContextExtensions.cs` | 20 | Helpers |
| `src/Reactor/Core/ChangeEchoSuppressor.cs` | 65 | Per-control "suppress next event" counter |
| `src/Reactor/Core/ObservableTreeTracker.cs` | 158 | Recursive INPC subscriber |
| `src/Reactor/Core/Observable.cs` | 51 | Single-cell INPC wrapper |
| `src/Reactor/Core/QueryCache.cs` | 366 | Process-wide TTL/refcount cache for `UseResource` |
| `src/Reactor/Core/AsyncValue.cs` | 72 | Closed-hierarchy async-state ADT |
| `src/Reactor/Core/InfiniteResource.cs` | 448 | Paged resource with LRU page cache |
| `src/Reactor/Core/ReactorFeatureFlags.cs` | 76 | Mutable static feature gates |
| **Total** | **~17 KLOC** | |

`tests/`, samples, and DSL packs are out of scope.

---

## 2. Data-flow diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│  App developer's Component.Render() / FuncElement render delegate    │
│                  (TRUSTED — author's own C# code)                    │
└───────────────────────────┬──────────────────────────────────────────┘
                            │ returns Element tree (immutable records)
                            ▼
                    ┌──────────────────┐
                    │  Reconciler      │
                    │   .Reconcile()   │── reentrant: Update() may
                    │                  │   recursively call Mount/Unmount
                    └────┬─────────────┘
                         │
       ┌─────────────────┼─────────────────────┐
       ▼                 ▼                     ▼
  Mount.cs           Update.cs              Unmount/Pool
  (creates RCWs)     (mutates RCWs +        (RunCleanups,
                      diffs hooks)           ElementPool.Return)
       │                 │                     │
       └────────┬────────┴────────────┬────────┘
                ▼                     ▼
   ┌─────────────────────┐    ┌──────────────────────┐
   │  WinUI control tree │    │  ElementPool         │
   │  (FrameworkElement) │◄───│  (per-type Stack)    │
   └─────────┬───────────┘    └──────────────────────┘
             │ events (Click, TextChanged, …)
             ▼
   ┌─────────────────────────────┐
   │ Trampoline → GetElementTag  │ (ReactorAttached.StateProperty
   │   → user delegate           │  on the native DependencyObject)
   └─────────────────────────────┘
             │
             ▼  ChangeEchoSuppressor.ShouldSuppress() guards programmatic-write echo
        user code
             │
             ▼
   setState / setStateThreadSafe (RenderContext.UseState)
             │
             ▼
   requestRerender → Reconcile() at top of subtree (UI thread)
```

**Side data structures shared across reconcile passes:**
- `_componentNodes` (`Dictionary<UIElement, ComponentNode>`) — UI-thread only.
- `_pool._pools` (`Dictionary<Type, Stack<FrameworkElement>>`) — UI-thread only, no synchronization.
- `_styleCache` (process-wide `ConcurrentDictionary`).
- `_compositorTainted`, `_poolableWireFlags`, `_managedResourceKeys`, `_dndStates`, `_gestureStates`, `_counters` — all `ConditionalWeakTable<UIElement, …>` static, UI-thread by convention.
- `DragData._transfers` — process-wide `Dictionary<Guid, DragData>` under `_transfersLock`, **strong references**.
- `QueryCache._slots` — `ConcurrentDictionary<string, Slot>`, per-slot lock for mutations, optional dispatcher callback.

---

## 3. Trust boundaries crossed

| # | Boundary | Direction | Trust assumption |
|---|---|---|---|
| 1 | Developer source code → reconciler | in | Trusted at build time. A bug here is an availability bug, not an attack. |
| 2 | UI thread ↔ background thread (`UseState(threadSafe:true)`, `Task.Run` in `UseCommand`, `INotifyPropertyChanged.PropertyChanged` from any thread) | both | Reconciler internals **must not** be touched off-thread. The hook layer marshals via `DispatcherQueue.TryEnqueue`. Unmarshalled access is a real bug. |
| 3 | Drag/drop payload from another OS process | in | `Reconciler.DragDrop.cs` lines 186–224: the transfer registry path (`reactor/transfer-id`) is same-process; any `Properties` value can be forged by a hostile peer. The reconciler treats `idObj is string` and falls back to `BuildViewBackedDragData` if `Resolve` fails — no spoofing-driven type confusion observed, but Chunk 16 owns this boundary. |
| 4 | Persisted state (`UsePersisted`) | in | `RenderContext.UsePersisted` reads from `PersistedStateCache` which Chunk 13 reviews — but reconciler treats the cached value as already-typed `T`. A type-mismatch in the cache surfaces as `InvalidCastException` inside the user's component on first read. |
| 5 | Pool-recycled controls returned to a different component instance | within process | The single most interesting boundary in this chunk — see Findings F-001 and F-002. |
| 6 | Loopback / devtools (`SnapshotHooks`) | out | `RenderContext.SnapshotHooks` (line 1031) hands the live boxed hook value to the devtools layer. That's already in Chunk 02, but the reconciler is the source. |

---

## 4. Asset inventory

| Asset | Why an attacker would care | Where it lives |
|---|---|---|
| Component hook state (UseState, UseRef cells, persisted keys) | Cross-component info disclosure if an element pool returns a control still attached to the previous component's state | `RenderContext._hooks` (instance), `_componentNodes` (`Reconciler.cs:28`) |
| In-flight drag payload (text, files, custom typed payload) | Same-process targets resolve typed payloads via `_transfers` GUID; a forged `Properties[reactor/transfer-id]` from a peer process could reach `DragData.Resolve` | `DragData._transfers` (`DragData.cs:276`) |
| `QueryCache` slots | Pattern-invalidation prefix is O(n) over keys; cache holds whatever the developer's fetcher returned (potentially PII) | `QueryCache._slots` |
| WinUI control state surviving pool round-trip | Stale `Text`, `Password`, focus, AutomationName, attached resources can leak across components | `ElementPool._pools` |
| Process-wide static caches (`_styleCache`, `_managedResourceKeys`, etc.) | Unbounded growth → DoS | `Reconciler.cs:39`, `Reconciler.cs:3027` |
| Component re-render queue depth | Unbounded recursion / retrigger via user `setState` inside `Render()` would freeze the UI thread | `RenderContext._requestRerender`, callers throughout |
| Trampoline closures retained on poolable controls | Permanent retention by WinUI subscription list — cannot be detached | `EventHandlerState` (`Reconciler.cs:2443`), `PoolableWireFlags` (`Reconciler.cs:342`) |

---

## 5. STRIDE table

| # | STRIDE | Threat | Attacker model | Impact | Likelihood | Current mitigation | Action |
|---|---|---|---|---|---|---|---|
| T-1 | **Tampering / I-D** | Pool-recycled `TextBox` retains `Password`-like content, focus, or selection from previous component when handed to a new one | Author bug + co-located components | High (cross-component info disclosure) | Med | `ElementPool.CleanElement` resets `Text`, `PlaceholderText`, alignment, accessibility props (`ElementPool.cs:195–326`); `PasswordBox` is **not** poolable | See F-001 |
| T-2 | **Tampering** | Stale `Tag`/`ReactorState.Element` on a pooled `Button` causes the *previous* component's `OnClick` to fire after rent | Pool reuse | High | Low (lots of guard rails) | `SetElementTag` BEFORE first programmatic write (Mount.cs:428); `ChangeEchoSuppressor` for value-bearing writes; trampolines re-read `Tag` on every fire | See F-002 |
| D-1 | **DoS** | Unbounded recursion: a `setState` inside a synchronously-running `useEffect` cleanup or user effect schedules a rerender that immediately reruns the same component | Author bug | High (UI freeze) | Med | None — `_requestRerender` is invoked synchronously and the reconciler does not detect cycles | See F-003 |
| D-2 | **DoS** | `ObservableTreeTracker` recursively subscribes to property graphs; `OnNestedPropertyChanged` re-walks from root and resubscribes on every property change | Author bug + a chatty INPC graph | High (CPU, allocation pressure) | Med | Cycle protection via `_visiting`; `DispatcherQueue.TryEnqueue` for off-thread; reflection cache | See F-004 |
| D-3 | **DoS** | `ElementPool` per-type stacks not synchronized; concurrent rent/return from a non-UI thread (or reentrant rent during Mount running effects) corrupts the stack | Author bug | Med | Low (UI-thread invariant assumed but not enforced) | None at the pool layer | See F-005 |
| I-1 | **Info disclosure** | `EventHandlerState` trampolines retain user closures forever — `ClearCurrentHandlers` only nulls the `Current*` fields, the trampoline-`Action` field still holds onto the closure-captured component state until the WinUI control is GC'd | Author bug + long-lived control | Low | Low | `DetachReactorState` (`Reconciler.cs:317`) clears `state.Events = null` for non-pooled controls but does not clear the actual trampoline delegate fields | See F-006 |
| I-2 | **Info disclosure** | `DragData._transfers` keyed by GUID; if a hostile in-process actor learns the GUID (it travels in `DataPackage.Properties` to other apps), that actor can call `Resolve` to retrieve the typed payload | Hostile in-process code | Low — same-trust-zone | Low | `Resolve` uses GUID lookup; collision unlikely | Tracked under Chunk 16 |
| T-3 | **Tampering** | `ElementPool` returns a control from `_pools[T]` without verifying the control is actually parentless, leading to `COMException` (mitigated) or attached-property bleed-through (Grid.Row, Canvas.Left, FlexPanel.Grow) | Pool reuse | Med (visual glitch / assertion) | Med | `CleanElement` clears Flex attached props (lines 234–242); explicit `ForceDetach` round-trip; **but Grid.Row/Column, Canvas.Left/Top are NOT cleared** | See F-007 |
| D-4 | **DoS / cost** | `QueryCache.InvalidatePattern` is O(n) over all keys for every prefix invalidation; a developer wiring a chatty source can pin one CPU | Author bug | Low | Med | Documented in `QueryCache.cs:138` | Add: cap or telemetry |
| R-1 | **Repudiation** | Component render exception is swallowed and replaced with a `TextBlock("⚠ Render error: ...")` (Reconciler.cs:673) — the message is not redacted | Indirect (a renderer that bubbled exception text including paths/secrets to the visible UI) | Low | Low | `_logger.LogError` separately | See F-008 |
| EoP-1 | **EoP at design time** | `ObservableTreeTracker` uses reflection on developer types in trim-unsafe mode (`UnconditionalSuppressMessage IL2026/IL2070`) — does not affect attacker reach but means trimming defects manifest as silent missed subscriptions | Author bug | Low | Low | Suppressed warnings | Document |

---

## 6. Findings

Numbered, file:line, severity, specific. These are the highest-impact concerns; this document deliberately does not enumerate every smaller defect.

### F-001 · ElementPool: Tag-keyed state survives rent/return for `Button`/`TextBox`/`ToggleSwitch` — `PoolableWireFlags` and `EventHandlerState` retain previous-mount references

**Severity:** Medium (integrity / availability; info disclosure under specific component shapes)
**Location:** `src/Reactor/Core/ElementPool.cs:62-65, 117-150, 195-326`; `src/Reactor/Core/Reconciler.cs:336-353, 2443-2531`

The pool intentionally retains WinUI event subscriptions across rent/return for `Button`, `TextBox`, and `ToggleSwitch` (`ElementPool.cs:62–65` "Interactive controls — safe to pool because the Tag-based event pattern reads the current element from Tag at invocation time"). Three side data structures persist:

- `_poolableWireFlags` (`Reconciler.cs:350`) — `ConditionalWeakTable<FrameworkElement, PoolableWireFlags>`. Survives pool round-trips by design.
- `_dndStates` (`Reconciler.DragDrop.cs:34`) — same pattern.
- `_gestureStates` (`Reconciler.Gestures.cs:59`) — same pattern.
- `EventHandlerState` (`Reconciler.cs:2443`) — lives on `ReactorAttached.StateProperty` on the native DO, holding all `Current*Tapped`, `Current*KeyDown`, etc. closures.

`CleanElement` (`ElementPool.cs:195–326`) does not call `state.Events?.ClearCurrentHandlers()`. It only nulls `state.Element` via `Reconciler.ClearElementTag` (line 198). Therefore on the **rent** path, the new component's `Mount` calls `EnsureXxxSubscribed` which sees `flags.ButtonClick == true` and **does not re-attach** but also **does not refresh `Current*`**. `Current*` is refreshed only when the new element actually has the corresponding handler set. If the new element has no `OnClick`, the trampoline still fires and reads `GetElementTag` which is now the new element — `ButtonElement`'s `OnClick` is null → safe no-op. **However**, for `TextBox` lifecycle the picture is more nuanced: `EnsureTextFieldWiring` (`Reconciler.Mount.cs:450`) gates on `tf.OnChanged is null` (line 452) and silently returns. If a previous mount wired `TextChanged` and the next mount also wires `TextChanged`, the trampoline already exists and just dispatches via `Tag` — fine. But the closure captured by the trampoline includes `requestRerender` via the closure on line 467: that delegate captures the **previous component's** rerender callback for as long as the trampoline lives. The Tag refresh swaps the *element*, not the *captured closure context*.

Risk: the "stale rerender" path in `EnsureTextFieldWiring` (line 467: `requestRerender();`) — that `requestRerender` is the one captured at the *first* `Mount` of this control. After pool round-trip and re-mount under a different parent component, `TextChanged` will fire `Tag.OnChanged?.Invoke(text)` (current element, fine) followed by the **previous** `requestRerender` which schedules a render of the **previous** component subtree — which may already be unmounted. Because component nodes are removed from `_componentNodes` on unmount (`Reconciler.cs:843`), the rerender will land in `ReconcileComponent` and exit early at line 553 with a `LogWarning`. So this is bounded — but it is a real cross-component coupling that the pool design comment does not mention.

**Recommendations:**
1. In `CleanElement`, call `Reconciler.ClearElementTag` *and* invoke `state.Events?.ClearCurrentHandlers()` — or accept that `Events` only fires through `Tag` and the captured closures live until GC.
2. Refresh the captured `requestRerender` on every rent: e.g. store `requestRerender` on `EventHandlerState` as a mutable field and have the trampoline read it via `state.CurrentRerender` instead of capturing.
3. Document in `ElementPool.PoolableTypes` that adding a new poolable type requires auditing every event trampoline that captures non-`Tag` closure state.

### F-002 · ElementPool: `Grid.Row/Column`, `Canvas.Left/Top`, `RelativePanel.*` attached properties NOT cleared in `CleanElement`

**Severity:** Medium (correctness; possible rare info-leak via positioning revealing previous-component layout)
**Location:** `src/Reactor/Core/ElementPool.cs:195-326`

`CleanElement` clears `FlexPanel.Grow/Shrink/Basis/AlignSelf/Position/Left/Top/Right/Bottom` (lines 234–242) but does not clear `WinUI.Controls.Grid.Row`, `Grid.Column`, `Grid.RowSpan`, `Grid.ColumnSpan`, `Canvas.Left`, `Canvas.Top`, `Canvas.ZIndex`, `RelativePanel.*`. A `TextBlock` rented from the pool may carry attached values from its previous use. A subsequent mount that does *not* call `.Grid(row, column)` on the same `TextBlock` will inherit the previous (row, column).

The new mount path applies only the modifiers/setters the developer specified; it does not zero attached values. Combined with the keyed reconciler's reuse of `TextBlock` instances inside `Grid` cells, a navigation flip can place a control at the wrong cell for one frame.

**Recommendation:** clear all known attached-property kinds in `CleanElement`, or move attached-property cleanup into the reconciler's mount path (always clear before applying). Prefer the latter — a generic "clear all attached DPs we ever set" registry keyed off `ReactorAttached`.

### F-003 · `requestRerender` invoked synchronously inside `Render()` and inside effect cleanup — no reentrancy guard

**Severity:** High (DoS — UI freeze)
**Location:** `src/Reactor/Core/RenderContext.cs:98, 109, 159, 172, 220, 230` (state setters); `src/Reactor/Core/Reconciler.cs:708-715` (`CreateComponentRerender`); `RenderContext.cs:933-976` (`FlushEffects`)

A user setter that calls `setState` synchronously inside `useEffect`'s cleanup or inside the effect body will fire `_requestRerender()` synchronously. `FlushEffects` runs **inside** `ReconcileComponent` (`Reconciler.cs:642`), which runs inside `Reconcile`, which is mid-traversal. The synchronous `requestRerender` re-enters `ReconcileComponent` with `node.SelfTriggered = true` (`Reconciler.cs:712`), bypassing memo, and renders the same component again. If the effect always sets state, infinite recursion will exhaust the stack and `StackOverflowException` will tear down the process (StackOverflow is explicitly rethrown — see e.g. `Reconciler.cs:663`).

Even short of stack overflow, two consecutive `setState` calls in one render flush twice — quadratic in pathological cases.

**Mitigations present:** debug `AssertUIThread` (`RenderContext.cs:33`) catches off-thread setter calls in DEBUG only. There is no Release-mode reentrancy guard, no "scheduled-render" coalescing inside the reconciler, and no max-depth check in `_debugReconcileDepth`. The depth counter is incremented but never compared against a bound (`Reconciler.cs:443`).

**Recommendations:**
1. Add a configurable max-reentrancy depth (e.g. 50) in `Reconcile` that throws a clear "Render loop detected — component X called setState during render" error.
2. Coalesce `requestRerender` calls fired *during* a reconcile pass into a single deferred render at the top of the stack.
3. Document the rule (don't `setState` during render or cleanup) in `RenderContext.UseEffect` summary.

### F-004 · `ObservableTreeTracker` uses reflection on every nested INPC change; no depth bound; `_visiting` reused across `OnNestedPropertyChanged` calls

**Severity:** Medium (DoS via author error, correctness on cycles)
**Location:** `src/Reactor/Core/ObservableTreeTracker.cs:16-158`

`SyncSubscriptions` walks the entire reachable INPC graph via reflection (`GetInpcCandidateProperties`) and resubscribes from scratch. `OnNestedPropertyChanged` re-runs this on **every property change** that reaches a subscribed object (line 134/139). In a graph with N reachable INPC nodes, a chatty UI binding produces N dictionary mutations and a full reflection walk per change.

`Walk` (`ObservableTreeTracker.cs:88`) also uses an instance-level `_visiting` `HashSet` that is `.Clear()`-ed at the top of `SyncSubscriptions` (line 53). If `OnNestedPropertyChanged` arrives on the dispatcher *while a previous SyncFromRoot is still running* (it cannot, because both marshal to the dispatcher and run sequentially) — but if a property getter on the developer's type itself fires `PropertyChanged`, the reentrant `OnNestedPropertyChanged → SyncFromRoot → SyncSubscriptions → Walk` will reset `_visiting` mid-walk and may skip live nodes or re-enter cycles. The catch in `Walk` (line 103) silently swallows getter exceptions but does not guard against self-firing during `prop.GetValue`.

**Recommendations:**
1. Replace `_visiting` with a stack-local `HashSet` so reentrant walks are safe.
2. Add a depth/node-count cap (e.g. 1024 nodes) and raise a `Debug.WriteLine` warning beyond it.
3. Add a fast-path check that compares the property identity that fired against the subscribed property set; only resync if the changed property is itself an INPC reference (line 123 already screens by `prop.PropertyType.IsValueType` but still resyncs on every change — that resync is the cost driver).
4. Add a `DispatcherQueue` null-check **including** the case where `_dispatcherQueue` was captured on a different thread than the one that constructed the tracker (current code uses the construction-time dispatcher).

### F-005 · `ElementPool._pools` is `Dictionary<>`, not concurrent — UI-thread invariant assumed but unenforced

**Severity:** Medium (DoS via author bug)
**Location:** `src/Reactor/Core/ElementPool.cs:67, 104-150`

`_pools` is a plain `Dictionary<Type, Stack<FrameworkElement>>`. Both `TryRent` (line 104) and `Return` (line 117) mutate it with no lock. The implicit invariant is "UI thread only," but the type signatures don't enforce that and the `Reconciler` is exposed via `public ElementPool Pool { get; }` (`Reconciler.cs:187`). A developer who calls `Pool.Return(...)` from a `Task.Run` finalizer or background dispose runs into corruption — silent in Release, NullReferenceException in Debug, and possibly a control that escapes `_pools.Values.Clear()` and remains live forever.

`_pool` is also touched via `_pool.TryRent` from `Reconciler.Mount.cs` (40+ call sites) and `_pool.Return` via `UnmountAndPool` (`Reconciler.cs:1000`). All current call sites are dispatcher-bound, but the API surface invites misuse.

`UseCommand` (`RenderContext.cs:837, 878`) explicitly issues `Task.Run` and resolves continuations on the captured dispatcher via `setIsExecuting(true)`. That uses `threadSafe: true` on the `IsExecuting` `UseState` cell. The `setIsExecuting` setter takes `lock(h.Lock)` for the value, then calls `_requestRerender?.Invoke()` — and `_requestRerender` is the component-rerender wrapper which calls back into the reconciler. **`_requestRerender` is invoked off the UI thread.** The reconciler is not marshalled.

Inspect `CreateComponentRerender` (`Reconciler.cs:708`): it sets `node.SelfTriggered = true` then invokes the parent `requestRerender`. If that ultimately reaches `Reconcile()` synchronously off-thread, every `_componentNodes` access races with an in-progress UI-thread reconcile pass.

**Recommendations:**
1. Either lock `_pools` with the same UI-thread guard or document that `Reconciler.Pool` is a UI-thread API and assert via `AssertUIThread` in DEBUG.
2. Audit the `_requestRerender` chain so `setState(threadSafe:true)` always marshals via `DispatcherQueue.TryEnqueue` before re-entering the reconciler. The current `UseCommand` finally-block (`RenderContext.cs:849, 891`) does not.
3. Consider making the UI-thread-required surfaces explicitly DEBUG-asserted via the existing `AssertUIThread` pattern at the reconciler level (currently only at the hook setter level).

### F-006 · Trampoline closures retain references after `DetachReactorState` — only `Current*` fields nulled, not `*Trampoline` delegates

**Severity:** Low (latent memory retention, not exploitable)
**Location:** `src/Reactor/Core/Reconciler.cs:317-324, 2492-2521`

`DetachReactorState` clears `state.Events = null` (line 323). `ClearCurrentHandlers` (line 2498) sets the `Current*` user delegates to null. **Neither path nulls the `*Trampoline` delegate fields** (lines 2470–2490). The comment at line 2495–2497 acknowledges this: "Trampoline delegate fields are left intact — they're rooted by WinUI's subscription list and can't be detached here."

Because `EventHandlerState` is reachable from `ReactorState.Events` and that wrapper lives on the native DP, after `state.Events = null` the trampoline delegates are still rooted by WinUI's event subscription list. The trampolines capture `state` itself by closure (`Reconciler.cs:2570+`), so the captured `state` still holds (now-null) `Current*` fields. Net result: each trampoline takes ~24 references' worth of object retention until the WinUI control is destroyed. Not a leak per se — bounded by control lifetime — but unhelpful for long-lived controls (Windows that never close).

**Recommendation:** since the trampolines all read `state.CurrentXxx`, breaking the closure on `state` would let `state` be GC'd. Pass `state` via a weak reference or move the trampoline target into a static method that looks up the state via `ReactorAttached.StateProperty`. Net code complexity vs. benefit — defer unless a long-running app shows leak telemetry.

### F-007 · `ChangeEchoSuppressor` counter has no upper bound; mismatched Begin/Should pairs accumulate forever

**Severity:** Low (latent state corruption; possible suppression of real user events)
**Location:** `src/Reactor/Core/ChangeEchoSuppressor.cs:33-65`

`BeginSuppress` increments `Counter.Value`; `ShouldSuppress` decrements. The contract (line 24–28) says "Pair it 1:1 with the write." If a Reactor `Update` path calls `BeginSuppress` and then the WinUI control fails to raise the expected event (control disposed, event suppressed by parent, etc.), the counter never decrements. Subsequent **legitimate** user-driven events will be suppressed in FIFO order. An attacker can't drive this, but a developer-visible bug surfaces as "user typed X but onChange never fired."

`Counter.Value` is `int` so wraparound is theoretical.

The CWT-keyed table itself is fine (weak keys, so pool return/GC cleans up).

**Recommendation:** add a per-call timeout (e.g. zero out the counter on every UI-thread quiescence) or make `ShouldSuppress` consume *only* if the suppression is "fresh" (timestamped). Lower priority; defer until an actual report.

### F-008 · Render errors echo exception messages into the UI tree without redaction

**Severity:** Low (info-disclosure surface — exception messages can carry secrets)
**Location:** `src/Reactor/Core/Reconciler.cs:673`

```csharp
newChildElement = new TextBlockElement($"⚠ Render error: {ex.Message}");
```

`ex.Message` may contain file paths, connection strings, query parameters, or other context the framework chose to embed. The visible UI is the developer's app surface, but if the app screenshots itself (preview server / devtools / accessibility automation in Chunks 02/03), the message could traverse boundaries.

The same pattern appears for navigation lifecycle (`Reconciler.cs:3400`, `Reconciler.cs:3424`) — those `Debug.WriteLine` so they only go to the debugger.

**Recommendation:** in Release, render `"⚠ Render error: <type>"` only, or surface the full message via `_logger` which is more controllable. Pair this with the redaction policy that Chunk 02 needs to define for log-buffer streaming.

---

## 7. Open questions

1. **UI-thread invariant scope.** Is the entire `Reconciler` class meant to be UI-thread-only? The public surface (`Pool`, `Reconcile`, `RegisterType`, `UpdateChild`, `UnmountChild`) does not assert, and `setState(threadSafe:true)` reaches `_requestRerender → CreateComponentRerender` without an obvious dispatcher hop. Confirm with the team whether the `threadSafe` setter is supposed to marshal to the UI thread before invoking the rerender, or whether it currently relies on the user's `setState` call site being on the UI thread already.
2. **`requestRerender` capture lifetime in poolable controls.** The `EnsureTextFieldWiring` trampoline (`Reconciler.Mount.cs:467`) captures `requestRerender`. Across pool rent/return cycles is the captured rerender the *original* component's rerender, the most recent renter's, or refreshed each rent? Reading the code, it is the original — F-001 documents the behavior, but is that intentional?
3. **`ElementPool` unbounded across types.** `MaxPerType = 32` caps per-type, but the per-type *Type* set is unbounded. With registered custom types and templated lists, this could grow. Should `_pools` itself be capped or LRU-evicted?
4. **`_styleCache` lifetime.** Process-wide `ConcurrentDictionary` (`Reconciler.cs:39`) keyed by composed string; `ClearStyleCache` is called on theme change (line 65). If a developer constructs styles dynamically with high-cardinality `ThemeRef.ResourceKey` values, the cache grows without bound. Is bounded growth acceptable here?
5. **`PersistedStateCache` interaction.** `UsePersisted` writes to a `PersistedStateCache` reviewed under Chunk 13. Reconciler trusts that cache returns typed `T` — what is the contract on cache poisoning? (Chunk 13's review should clarify.)
6. **Drag/drop transfer-id forgery.** Cross-process drag where the source process injects a `reactor/transfer-id` GUID matching one in the target's `_transfers` registry — bounded by GUID entropy but a same-tenant scenario warrants Chunk 16's confirmation.
7. **`InfiniteResource` callback re-entrancy.** `ItemAt` (`InfiniteResource.cs:129`) releases the lock before invoking `_pageRequestedCallback`. If that callback synchronously calls back into `ApplyPageResult` or `MarkPageInFlight` (e.g. from a synchronously-completing fake fetcher in tests), the lock is reacquired safely — but `ClearAllPages` (line 314) does not invalidate any `_pageRequestedCallback`-pending tasks. A pending fetch that completes after `ClearAllPages` would `ApplyPageResult` into a freshly-cleared resource, reintroducing the page.

---

## 8. Out-of-scope referrals

| Concern | Owner |
|---|---|
| Drag/drop payload trust boundary (other-process source) | **Chunk 16** — Input, focus, gestures, drag/drop |
| Devtools snapshot of hook values | **Chunk 02** — Devtools tools |
| `PersistedStateCache` deserialization safety | **Chunk 13** — Navigation lifecycle and back-stack persistence |
| `XamlReader.Load` of `SharedContentControlTemplate` (`Reconciler.cs:217`) — XAML loader on a hardcoded literal, but verify no code path lets a developer-controlled string reach this | Chunk 14 (this doc — verified safe: literal only) |
| ETW / `ReactorEventSource` payload PII | **Chunk 15** — Hosting, ETW, layout-cost overlay |
| Component naming via reflection in `SnapshotHooks` | **Chunk 02** consumer; reconciler is producer |
| `UseCommand` async work / `Task.Run` lifetimes | Touches **Chunk 23** (Hooks) and this chunk |
| Yoga / FlexPanel attached properties; F-002's clearing policy | Cross-cuts **Chunk 20** but the fix lives in the reconciler |

---

## Appendix: review notes that did not become findings

- `ChildReconciler.ComputeLIS` (`ChildReconciler.cs:377`) — patience-sorting LIS, O(n log n), bounded by child count. Author-supplied keys, not adversarial input. No DoS concern beyond "very large lists"; that's an authoring problem.
- `QueryCache.Subscribe/Unsubscribe` (`QueryCache.cs:170, 194`) — uses `while(true)` retry against `IsEvicted`. The retry is bounded because every retry observes a fresh slot and `EvictNow` only evicts under `SubscriberCount==0`; an active subscribe blocks eviction.
- `ContextScope` (`ContextScope.cs:8-52`) — single-threaded by construction (UI-thread reconcile). `_stack` mutation during render correctness depends on push/pop being balanced; `MountXxx` push/pop sites should be audited mechanically but no defect found in spot-checks.
- `Observable<T>` (`Observable.cs:25`) — `PropertyChanged` invocation is not synchronized with reads of `_value`. A subscriber on a different thread reading `Value` while `set` is executing sees the post-write value (atomic for reference types, torn for non-atomic value types like `decimal` / large structs). Consistent with the documented "minimal" posture.
- `FeatureFlags` (`ReactorFeatureFlags.cs`) — plain mutable statics, not synchronized. Documented as set-at-bootstrap; reading them on hot paths is fine because they're `bool` (atomic). No issue.
- `_typeRegistry` (`Reconciler.cs:32`) — `Dictionary<Type, ITypeRegistration>`. `RegisterType` mutates without lock. Typically called once at app start. If a developer calls it post-bootstrap from a background thread it races. Document or convert to `ConcurrentDictionary`.
