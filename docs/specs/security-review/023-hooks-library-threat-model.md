# Chunk 23 — Hooks Library: Threat Model

**Status:** Phase 2 — review complete
**Reviewer:** security review pass
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3`
**Companion:** `000-chunking-and-threat-model.md` §9 / Chunk 23

---

## 1. Scope

`src/Reactor/Hooks/**` — 10 files, 1989 LOC total.

| File | LOC | Purpose |
|---|---:|---|
| `Pending.cs` | 77 | `Pending` element + component that hides a subtree behind a fallback while any descendant `UseResource`/`UseInfiniteResource` is loading. |
| `PendingScope.cs` | 79 | Token-keyed loading-state ref-count consumed by `Pending`. |
| `UseAnnounce.cs` | 102 | Screen-reader live-region announcement handle. |
| `UseDevtools.cs` | 27 | Returns whether devtools is enabled in the current process. |
| `UseElementFocus.cs` | 56 | Programmatic element focus via captured UI dispatcher. |
| `UseFocus.cs` | 215 | Form-field `FocusManager` + Tab-order / submit. |
| `UseFocusTrap.cs` | 147 | Modal/flyout focus trap via `LosingFocus` cancellation. |
| `UseInfiniteResource.cs` | 445 | Cursor-paged async data with cache + Pending-scope integration. |
| `UseMutation.cs` | 345 | Optimistic + dispatcher-marshalled async write hook. |
| `UseResource.cs` | 496 | Async fetch hook with cache, retry, focus-revalidation, Pending-scope. |

Out of scope (referrals at the bottom):
- `UseDevtools.cs` only consults `ReactorApp.DevtoolsEnabled`; the devtools transport / handlers are Chunks 01 + 02.
- `QueryCache`, `FocusRevalidationService`, `AsyncValue`, `InfiniteResource`, `Page<TItem,TCursor>` are part of Chunk 14 (reconciler & component model). Findings here only call out how the hooks *use* those types.
- `FocusManager` (the Reactor input layer used by `UseElementFocus`, in `Microsoft.UI.Reactor.Input`) is Chunk 16.

---

## 2. Data-flow diagram

```
                           +-------------------------------+
 Developer code (trusted)  |  ctx.UseResource / UseMutation|
 closures, deps[], mutator |  UseInfiniteResource / etc.   |
                           +---------------+---------------+
                                           |
                                           v
                        +------------------+-------------------+
                        |   per-hook State (UseRef-pinned)     |
                        |   - HookState<T> w/ CTS              |
                        |   - lazy GUID hookId                 |
                        |   - lambda Mutator/Fetcher refreshed |
                        +------------+-------------------------+
                                     |
                ctx.UseContext       |       ctx.UseContext
       AppContexts.QueryCache <------+------> AppContexts.PendingScope
       AppContexts.FocusRevalidation                 |
                |                                    |
                v                                    v
   +------------+-----------+              +---------+----------+
   |  QueryCache (Chunk 14) |              |  PendingScope      |
   |  Set/Get/Subscribe     |<-- SetLoad --|  refcount of tokens|
   |  EntryChanged event    |              |  Changed event     |
   +------------+-----------+              +---------+----------+
                                                     |
                                                     v
                                       Pending element re-renders
                                       (visibility flip — both
                                        subtrees stay mounted)

   Async path:
     fetcher(ct) / mutator(input, ct)  -->  Task<T>
       |                                     |
       | inline-sync fast path               | ContinueWith(ExecuteSynchronously)
       v                                     v
       cache.Set + LastValue=Data            IHookDispatcher.Post(Apply)
                                                    |
                                                    v
                                       FinishSuccess/Failure/Cancelled
                                       fires user OnSuccess/OnError
                                       cache.Invalidate(InvalidateKeys)
                                       rerenderTick(reducer)

   Focus path:
     UseElementFocus  -- captures DispatcherQueue at render --> closure
     UseFocus         -- fieldName -> Control map; .Focus() at call time
     UseFocusTrap     -- LosingFocus event handler with Cancel=true

   Devtools path:
     UseDevtools  -->  ReactorApp.DevtoolsEnabled  (process-wide static)
```

There is no I/O of any kind in the hooks library. No file, no socket, no shell — every "side effect" is in-process: cache mutation, dispatcher enqueue, event subscription, and (only in `UseFocusTrap`) cancellation of a WinUI focus event.

---

## 3. Trust boundaries crossed

| # | Boundary | Where | Assumption |
|---|---|---|---|
| 1 | Developer code → framework state | `UseResource` `fetcher`, `UseMutation` `mutator`, `OnSuccess`/`OnError`, `OnOptimistic`, `InvalidateKeys` strings | Trusted at compile-time. **Their *inputs* may be tainted runtime values** — the cache key, deps, page cursor, and mutation input can all originate from network / parsed UI input. |
| 2 | Render thread → arbitrary thread-pool | `task.ContinueWith(... ExecuteSynchronously)` in `UseResource.ScheduleCompletion` (`UseResource.cs:333`), `UseMutation.RunAsync` (`UseMutation.cs:229`), `UseInfiniteResource.RequestPage` (`UseInfiniteResource.cs:328`). Continuations re-enter the cache and (if no dispatcher) user callbacks. | Continuations marshal back via `IHookDispatcher.Post` if it is non-null; if null they run inline. Tests rely on the inline path. |
| 3 | Arbitrary thread → shared cache event handler | `_onEntryChanged` is bound to `cache.EntryChanged` (`UseResource.cs:424`). Mutation invalidations on a worker thread fire the handler on that worker thread. | `RequestRerender` is reducer-thread-safe (`threadSafe: true`) so cross-thread invocation is OK. |
| 4 | Hook → process-wide static | `UseDevtools` reads `ReactorApp.DevtoolsEnabled` (`UseDevtools.cs:26`). | Static is set once at `ReactorApp.Run` and never reset (except a test-only hook); a hook running before the static is set returns `false`. |
| 5 | Component → user-visible focus | `UseFocusTrap` cancels `LosingFocus` (`UseFocusTrap.cs:67`); `FocusManager` programmatically calls `Control.Focus` (`UseFocus.cs:50`). | The trap is gated by `IsActive`; the developer can leave `IsActive=true` indefinitely. |

The hooks **do not** cross any process / network / disk boundary.

---

## 4. Asset inventory

| Asset | What's worth attacking | Where it lives |
|---|---|---|
| `QueryCache` entries | Cached responses keyed by deps; mutation-invalidation paths can cause refetches | Chunk 14; hooks subscribe via `cache.Subscribe`/`Unsubscribe` |
| Per-hook `CancellationTokenSource` | Cancelling another component's in-flight fetch via stale closure capture | Per-hook `state.Cts` |
| `PendingScope` loading set | Wedge UI into permanent fallback / hide subtree | `PendingScope._loadingByToken` |
| Captured `DispatcherQueue` | Marshalling closures onto the UI thread | Captured at render in each hook |
| Focus | UX integrity — can a focus trap "kidnap" the keyboard? | `UseFocusTrap`, `UseFocus`, `UseElementFocus` |
| Process-wide `DevtoolsEnabled` | Decides whether dev-only UI is constructed | `ReactorApp` static |
| Closures + per-hook state | Memory growth → DoS | All hooks |

The framework user is the same principal as the framework, so confidentiality and integrity of these assets is "low-rated" from a STRIDE-attacker perspective. The realistic threats are **availability** (DoS through leaks / unbounded growth), **focus-trap UX denial**, and **logic correctness** that turns developer mistakes into non-recoverable UI.

---

## 5. STRIDE table

| # | Category | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding / recommendation |
|---|---|---|---|---|---|---|---|
| T1 | **Tampering** | A render-thread re-entry while a hook's `OnSuccess`/`OnError` is running mutates `_pendingCount`/`_lastValue` racily. | Developer-supplied callback re-enters reducer | UI-state corruption | Low | `MutationHookState` takes `_lock` for `_pendingCount`, `_error`, `_lastResult` (`UseMutation.cs:180-182`); `PendingScope` locks `_loadingByToken`. | OK — see F-04 about a narrow event-fire-outside-lock concern. |
| T2 | **Repudiation** | n/a | n/a | — | — | Not a goal. | OK — hooks do not log. |
| T3 | **Info disclosure** | Cache values cross thread without copying; if `T` is mutable, a worker thread can mutate after publish. | Two readers | Confidentiality / integrity of cache | Low | `QueryCache.Set` stores by reference; this is documented elsewhere. | Not a hooks issue (deferred to Chunk 14). |
| T4 | **Info disclosure** | `Guid.NewGuid()` lazy hookId (`UseResource.cs:120`, `UseInfiniteResource.cs:57`) is used as a cache-key prefix. If a developer omits an explicit `CacheKey`, two component *instances* of the same hook never share cache entries. | n/a | None — this is a property, not a leak. | n/a | Documented in code. | OK. |
| T5 | **Denial of service** | A hook keeps registering tokens with `PendingScope` so `_loadingByToken` grows without bound. | Hostile component logic / leak | Pending element wedged forever | Medium | `Dispose()` calls `Unregister` (`UseResource.cs:494`, `UseInfiniteResource.cs:443`). | See F-01 — `Dispose` is wired through `UseEffect(() => () => state.Dispose())`. If the component is created but never reaches `UseEffect` mount (impossible in normal flow but possible in error paths), the registration leaks. Low severity. |
| T6 | **DoS** | `UseInfiniteResource._deferredRequests` (a `SortedSet<int>`) is added to during cursor-chained fetches but only popped one-at-a-time on `CommitSuccess`. A user pulling far down a list can grow this set unboundedly until a page completes. | Developer code that calls `EnsureRange(0, N)` on a slow source | Memory growth proportional to N | Medium | None on the set itself. | **F-02** — bound `_deferredRequests` size; today nothing prevents `int.MaxValue`-sized accumulation. |
| T7 | **DoS** | `UseFocusTrap` with `IsActive=true` and a container that the user cannot reach (e.g. modal that's been visually obscured by a programming error) traps focus permanently. | Developer mistake (not malice) | User cannot tab out of an invisible region | Medium | None; `LosingFocus.Cancel = true` always blocks. | **F-03** — focus trap has no escape hatch (Esc, focus-loss timeout, or "if container Visibility=Collapsed/IsHitTestVisible=false then deactivate"). |
| T8 | **DoS** | `Mutation.RunAsync` fires `OnOptimistic` synchronously on the caller thread. If the developer kicks off N mutations in a tight loop, all optimistic callbacks run synchronously, blocking input. | Developer code | UI freeze | Low | None. | OK — documented behaviour. |
| T9 | **DoS** | `ScheduleCompletion`/page continuation uses `TaskContinuationOptions.ExecuteSynchronously` (`UseResource.cs:364`, `UseMutation.cs:257`, `UseInfiniteResource.cs:349`). If completion fires on a foreign thread that is critical (e.g. a low-priority pool thread), the entire dispatch chain inherits that thread's affinity until `Post`. | Worker-pool starvation | UI marshal latency | Low | `Post` immediately re-marshals to the dispatcher. | OK — the synchronous chunk is short. |
| T10 | **Elevation of privilege** | `UseMutation.OnOptimistic` runs synchronously, bypassing the dispatcher. A callback that throws *after* mutating shared state leaves that state half-applied (only the documented "throw before mutator" guard helps). | Developer error | Inconsistent state | Low | Doc says "if `OnOptimistic` throws, the mutator is never invoked". | OK — caveat is documented. |
| T11 | **EoP** | `UseDevtools` returning `true` causes a component to construct dev-only subtrees with elevated capabilities (e.g. invoking `DevtoolsMenuFactory`). If `ReactorApp.DevtoolsEnabled` is settable from outside the trusted boot path (e.g. by a sample app), retail UX could be unintentionally enabled. | Mistaken developer code | Dev menu visible in retail | Low | The static is set only inside `ReactorApp.Run` based on CLI args (`ReactorApp.cs:206`,`217`). | **F-05** — `ReactorApp.ResetDevtoolsEnabledForTests` (`ReactorApp.cs:549`) is `internal` but exposed if `InternalsVisibleTo` is granted to a sample. Worth a one-liner check. |
| T12 | **EoP** | `UseFocus.FocusField` programmatically focuses a control by name; if `_controls` were ever populated from untrusted input the caller could focus a button across boundary the user didn't intend. | n/a | n/a | Low | `Register`/`SetControl` are called only from element factories that the developer wrote. | OK. |
| T13 | **Tampering / DoS** | `cache.EntryChanged` is subscribed in `ResourceHookState` constructor (`UseResource.cs:424`) and unsubscribed in `Dispose`. If the cache outlives the component and the component constructed but didn't dispose, the closure pins the hook state and the cache event keeps firing. | Constructor exception path | Memory leak | Low | `Dispose` wired via `UseEffect`. See F-01. | See F-01. |
| T14 | **DoS** | `UseInfiniteResource._pageCts` Dictionary grows for every `RequestPage` and is removed in `CommitSuccess`/`ApplyError`. If a fetcher never completes (and never throws / never honours the token), `_pageCts` retains a `CancellationTokenSource` per requested page. | Slow upstream / buggy fetcher | Memory | Medium | `Dispose()` cancels all (`UseInfiniteResource.cs:432`). | Bounded by app lifetime; OK as long as `Dispose` runs. |
| T15 | **Spoofing** (low-impact) | Two unrelated `UseResource` calls with identical `deps` and same `CacheKey` *intentionally* share a cache entry — but the `hookId` prefix means a missing `CacheKey` produces *different* keys per hook slot. A developer expecting cross-hook sharing without setting `CacheKey` is silently mis-keyed. | Developer error | Functional bug, not security. | High likelihood, low impact | Documented in remarks. | OK — call out in dev docs. |
| T16 | **DoS** | `ResourceHookState.ScheduleRetry` allocates a `Timer` on every retry attempt and disposes it in the callback (`UseResource.cs:472-478`). The token check inside the timer can race with Dispose: if Dispose cancels just as the timer callback enters, `afterDelay` may dispatch `StartAttempt` *after* the state is disposed. | Race window | One stale fetch | Low | `StartAttempt` checks `IsDisposed` (`UseResource.cs:245`). | OK — guarded. |
| T17 | **Tampering** | `MutationHookState._unmountCts.Cancel()` in `Dispose` cancels in-flight mutation tokens, but `Dispose` also calls `_unmountCts.Dispose()` immediately after (`UseMutation.cs:342-343`). If a continuation references `_unmountCts.Token` after disposal it throws ObjectDisposedException. | Race window | Unhandled exception in finalizer-pool thread | Low | `RunAsync` creates a *linked* CTS up-front (`UseMutation.cs:212`); the linked token is captured locally in `ct`. After dispose, no fresh consumers. | OK — the local `ct` is the captured value; the disposed source is fine. |
| T18 | **Info disclosure** | `_loadingByToken` uses `this` of the hook state as the token (`UseInfiniteResource.cs:174`, `UseResource.cs:427`). Since `Dictionary<object,bool>` uses default equality, and the token is a private object, no external caller can spoof. | n/a | n/a | n/a | OK by design. | OK. |

---

## 6. Findings

Findings are file:line, severity is `Info / Low / Medium / High / Critical`. None of the findings rise to High because the hooks library has no I/O.

---

### F-01 — Hook teardown depends on `UseEffect` running. If a hook constructor throws or mounting bails out, `PendingScope.Register` and `cache.EntryChanged` subscriptions leak. **Severity: Low**

Both `ResourceHookState` (`src/Reactor/Hooks/UseResource.cs:407-428`) and `InfiniteHookState` (`src/Reactor/Hooks/UseInfiniteResource.cs:161-175`) do work in their constructors:

- `cache.EntryChanged += _onEntryChanged;` (`UseResource.cs:424`)
- `PendingScope?.Register(this, isLoading: true);` (`UseResource.cs:427`, `UseInfiniteResource.cs:174`)

The corresponding teardown is wired through:

```
ctx.UseEffect(() => () => state.Dispose());
```

(`UseResource.cs:141`, `UseInfiniteResource.cs:73`, `UseMutation.cs:137`)

`UseEffect` schedules its cleanup at **commit/unmount** time, not at construction. If anything between hook construction and commit throws (a sibling hook throws, a render-time exception, etc.) the cleanup lambda is never registered and the state object — including its event subscription and PendingScope token — never gets `Dispose`d. The cache then permanently fires `EntryChanged` into a dead handler that re-renders a torn component, and the PendingScope reports loading-forever for the orphaned token.

**Recommendation:** Move the side-effecting registrations *inside* the `UseEffect` body (so the cleanup is symmetrically registered), or wrap construction in try/catch and dispose on failure. The simpler fix: register the `EntryChanged` and `PendingScope` subscriptions inside the `UseEffect` mount step, returning the dispose lambda from the same closure.

---

### F-02 — `_deferredRequests` in `UseInfiniteResource` is unbounded. **Severity: Medium**

`src/Reactor/Hooks/UseInfiniteResource.cs:145, 284`

```csharp
private readonly SortedSet<int> _deferredRequests = new();
...
_deferredRequests.Add(pageIndex);
```

In the cursor-paged fallback (no `CursorFromPageIndex`), every `RequestPage(N)` for N where page N-1 isn't loaded yet **adds** to `_deferredRequests`. A consumer that calls `EnsureRange(0, 100_000)` on a slow data source will cause the set to grow toward 100,000 entries before any of them complete. Each `CommitSuccess` only pops `_deferredRequests.Min` (`:363-368`), so the set drains one-per-completed-page. There is no cap, and a hostile fetcher (or a misconfigured prefetcher) can grow the set arbitrarily.

`ApplyError` clears the set (`:380`), so the failure path bounds growth, but the success-path-on-slow-source does not.

**Recommendation:** Cap `_deferredRequests` at a configurable maximum (default `~100`), drop excess additions, and surface a diagnostic. Alternatively, cap at `options.MaxConcurrentRequests` if such a knob exists in `InfiniteResourceOptions`.

---

### F-03 — `UseFocusTrap` has no escape hatch. **Severity: Medium**

`src/Reactor/Hooks/UseFocusTrap.cs:52-69`

```csharp
private void OnLosingFocus(UIElement sender, LosingFocusEventArgs args)
{
    if (!_isActive || _container is null) return;
    var newFocus = args.NewFocusedElement as DependencyObject;
    if (newFocus is null) return;
    if (!IsDescendantOf(newFocus, _container))
    {
        args.Cancel = true;
        args.Handled = true;
    }
}
```

The trap blocks every `LosingFocus` whose new target is not a descendant of `_container`. There is **no** check for:

1. The container being `Visibility.Collapsed`, `IsHitTestVisible=false`, or `IsEnabled=false` — i.e. visually unreachable.
2. Container having no focusable descendant — `IsDescendantOf` of a non-descendant returns false even when `_container` itself contains nothing focusable, so focus is cancelled and goes nowhere.
3. Esc key explicitly releasing the trap.
4. The container being detached from the visual tree (the `LosingFocus` event still fires on a detached element until GC).

A real-world scenario: a developer renders a modal with `IsActive=true`, conditionally hides the modal with `Visibility=Collapsed` while `IsActive` is still true, the user Tabs — focus is cancelled and the user is keyboard-trapped on a hidden modal. This is an **accessibility regression** more than a security issue, but the Reactor design promises "essential for modal dialogs and flyouts" (`UseFocusTrap.cs:11`) and that promise is undermined.

A more security-relevant variant: a malicious developer who depends on Reactor (third-party component author) could ship a "always-active focus trap" component on a hidden surface to intentionally trap a screen reader / keyboard user.

**Recommendation:**
1. In `OnLosingFocus`, before cancelling, verify `_container.IsLoaded && _container.Visibility == Visibility.Visible && _container.IsHitTestVisible`. If any is false, do not cancel.
2. Document the Esc-key contract: the developer is responsible for handling Esc to flip `IsActive` to false, and the framework should plumb that into the default story.
3. If `IsDescendantOf` returns false because the new target is a top-level window (Alt-Tab to another window), do not cancel — the user is leaving the app, which is always allowed.

---

### F-04 — `PendingScope.Changed` event fires outside the lock with no defensive copy. **Severity: Low**

`src/Reactor/Hooks/PendingScope.cs:32-62`

```csharp
public void Register(object token, bool isLoading)
{
    lock (_lock) _loadingByToken[token] = isLoading;
    Changed?.Invoke();    // <-- fired outside lock
}
```

This is *intentionally* outside the lock to avoid re-entry deadlocks from a `Changed` handler that calls back into `PendingScope`. Two issues:

1. The `Changed?.Invoke()` reads `Changed` racily — between the null-check and the invoke, another thread could `-= handler` it. C# pattern-match `Changed?.Invoke()` compiles to a temp-load-then-call, so it is null-safe at the *delegate* level, but a removed handler can still execute one last time after `-=`. `PendingComponent` (`Pending.cs:60`) does `scope.Changed -= handler` in `UseEffect` cleanup; that handler then calls `tick(n => n + 1)` on the just-disposed `(_, tick)` reducer. The reducer's thread-safe path tolerates this.
2. If a handler throws, the exception propagates to the caller of `Register/SetLoading/Unregister`. Today every caller is internal Reactor code that's already in a try-block, but a future external subscriber could break the framework.

**Recommendation:** Wrap `Changed?.Invoke()` in try/catch that logs and swallows. This makes the event broadcast robust to misbehaving subscribers — a defensive practice given the `PendingScope` is exposed to user code via `AppContexts.PendingScope` context.

---

### F-05 — `ReactorApp.ResetDevtoolsEnabledForTests` is `internal` but `UseDevtools` reads the static unconditionally. **Severity: Info**

`src/Reactor/Hooks/UseDevtools.cs:25-26` returns `ReactorApp.DevtoolsEnabled` directly. The static is set in `ReactorApp.Run` (`ReactorApp.cs:206`/`:217`) and otherwise reset only by `ResetDevtoolsEnabledForTests` (`ReactorApp.cs:549`). The reset method is `internal` but visible to any assembly that has `InternalsVisibleTo` to `Microsoft.UI.Reactor`. As of this commit only test assemblies have it, so the immediate exposure is zero. Long-term, if a sample app ever obtains internal visibility, this becomes a way to flip dev-only UI on retail.

**Recommendation:** Verify `InternalsVisibleTo` declarations remain test-scoped, or move the reset method behind a conditional compilation symbol (`#if DEBUG` or `[Conditional]`).

---

### F-06 — `UseElementFocus` swallows `Exception` (not just `COMException`) when capturing the dispatcher. **Severity: Info**

`src/Reactor/Hooks/UseElementFocus.cs:36-38`

```csharp
Microsoft.UI.Dispatching.DispatcherQueue? uiQueue;
try { uiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(); }
catch { uiQueue = null; }
```

The two sister hooks (`UseResource.cs:172`, `UseMutation.cs:149`, `UseInfiniteResource.cs:118`) all catch the more specific `System.Runtime.InteropServices.COMException`. `UseElementFocus` catches `Exception` — it will swallow `OutOfMemoryException`, `ThreadAbortException`, etc.

**Recommendation:** Tighten to `catch (System.Runtime.InteropServices.COMException)` for symmetry and for general "let the runtime see fatal exceptions" hygiene.

---

### F-07 — `WindowsDispatcherHookDispatcher.Post` falls back to *inline* invocation when `TryEnqueue` returns false (dispatcher shut down). **Severity: Low**

`src/Reactor/Hooks/UseResource.cs:52-57`

```csharp
public void Post(Action action)
{
    if (_queue is null) { action(); return; }
    if (!_queue.TryEnqueue(() => action()))
        action(); // dispatcher shut down — fall back to inline
}
```

When the dispatcher has shut down (window closing, process exit), `TryEnqueue` returns false and the continuation runs inline on whatever thread fired the continuation. The continuation calls into hook state that was constructed on the UI thread; running it on an arbitrary thread can:

- Touch WinUI control properties from a non-UI thread (would normally throw, but during shutdown the threading checks may be bypassed).
- Race with `Dispose()` running on the UI thread.

Most of the hook state is guarded by locks or the dispose flag, but a finalizer-pool thread running `state.LastValue = ...` while another thread is in `state.Dispose()` is a real (low-impact) race.

**Recommendation:** Drop the inline fallback; when the dispatcher has shut down, the continuation is on a fast-shutdown path and can be silently dropped.

---

### F-08 — `UseFocus.FocusManager.Register` is O(N) per call (`List.Contains`). **Severity: Info**

`src/Reactor/Hooks/UseFocus.cs:25-29`

```csharp
public void Register(string fieldName)
{
    if (!_fieldOrder.Contains(fieldName))
        _fieldOrder.Add(fieldName);
}
```

The doc comment on the same method (`:23`) says *"Call on every render to maintain order"*. For a form with N fields, every render does N×N `string.Equals` comparisons. With a hostile or pathological generator producing forms of thousands of fields this is mild DoS — not a realistic security threat, but a performance trap that worsens under untrusted form content (e.g. a forms-from-JSON renderer).

**Recommendation:** Back `_fieldOrder` with both a `List<string>` and a `HashSet<string>` for O(1) `Contains`.

---

### F-09 — `Mutation.RunAsync` — `OnOptimistic` runs synchronously and unconditionally on the calling thread. **Severity: Info**

`src/Reactor/Hooks/UseMutation.cs:205-209`

The doc explicitly chooses synchronous-on-caller for the optimistic update so it lands in the "next frame". This is fine in normal use, but if `RunAsync` is invoked off the UI thread (e.g. from a `UseEffect` that ran on a worker thread, or from a finalizer), `OnOptimistic` will execute off the UI thread. User callbacks that touch WinUI controls will then throw. There is no documentation that pins `RunAsync` to the UI thread.

**Recommendation:** Either dispatch `OnOptimistic` through `_dispatcher` if the caller is off-thread, or document explicitly that `RunAsync` must be invoked from the dispatcher.

---

### F-10 — `UseInfiniteResource.HasLoadedPage` is the cache, not the resource. **Severity: Info**

`src/Reactor/Hooks/UseInfiniteResource.cs:385-390` defines "page loaded" as "cache.TryGet returns true". An external party (mutation invalidation, manual `cache.Invalidate`) can drop a page entry between sequential `RequestPage` calls, causing the cursor chain to break mid-flight. The deferred-request resumption logic (`:363-368`) re-checks `HasLoadedPage(next - 1)` which can return false even when the previous page completed, silently stalling the chain.

**Recommendation:** Track loaded pages in a hook-local set rather than relying on the cache.

---

### F-11 — `UseMutation.RunAsync` returns a faulted task when called after `Dispose`, but does NOT fire `OnError`. **Severity: Info**

`src/Reactor/Hooks/UseMutation.cs:194-199` returns a cancelled task on disposed-state. A caller awaiting that task will see `OperationCanceledException`, but `OnError` was not fired. Since the documented invariant is "`OnError` fires on completion-after-error", this is consistent — but the asymmetry (no callback at all post-dispose) is worth a comment. Not a bug.

---

## 7. Open questions

These are explicit questions for the team that the review could not resolve from code alone:

1. **Hook teardown ordering.** Does the reconciler guarantee `UseEffect` cleanup runs even if a sibling hook throws during render? (Drives whether F-01 is Low or Medium.)
2. **`AppContexts.PendingScope` leakage.** A user component can call `ctx.UseContext(AppContexts.PendingScope)` and capture the scope reference. Is this intentional? If a user retains the reference past unmount, they can call `scope.Register/Unregister` and skew the loading set arbitrarily.
3. **`AppContexts.QueryCache.DefaultValue`.** A process-wide default cache means two unrelated apps in the same process (rare, but possible in a host process) share cache entries by default. Confirm the host process always replaces the default at startup.
4. **`ReactorApp.DevtoolsEnabled` write surface.** Is the static guaranteed to be set *before* any component renders? A component that renders before `Run` would see `false` and never re-render when `Run` flips it (the static has no `INotifyPropertyChanged`).
5. **Focus-trap on multi-window apps.** The trap uses `VisualTreeHelper.GetParent`. What's the behaviour when the new focus target is in a different `Window` / `XamlRoot`? `GetParent` walks one tree only, so cross-window focus is currently *blocked* (every cross-window navigation cancels). Is that intended?
6. **`IHookDispatcher` injection.** The hook accepts an `IHookDispatcher? dispatcher = null` parameter from any caller (Mutation: `:99`, Resource: `:90`, Infinite: `:26`). Is this part of the public API a sample app could override? If so, a misbehaving dispatcher (`Post` calling its action twice, or never) breaks the hook's invariants. Typed contract for "Post must run exactly once" is undocumented.
7. **`Pending` element.** `PendingComponent.ShouldUpdate()` always returns true (`Pending.cs:76`). Is that needed for correctness, and does it imply a re-render storm if the parent re-renders frequently?

---

## 8. Out-of-scope referrals

| Surface | Owner chunk | Reason |
|---|---|---|
| `UseDevtools` reading `ReactorApp.DevtoolsEnabled` and the devtools menu / fire / state tools that flow from it | **Chunk 02** | The hook itself is one line; the trust decision lives in the devtools handlers. |
| `QueryCache` mechanics — entry sharing, eviction, `EntryChanged` invariants, `Set`/`Invalidate` thread-safety | **Chunk 14** | Hooks are consumers; cache is the asset. |
| `FocusRevalidationService` enroll/unenroll race, suppression, and signal source | **Chunk 14** (or possibly Chunk 16) | `UseResource` only enrolls; the service implementation owns correctness. |
| `Microsoft.UI.Reactor.Input.FocusManager.Focus` (the *input* manager called from `UseElementFocus`, distinct from this chunk's form-field `FocusManager`) | **Chunk 16** | Real focus-management primitives. |
| `AsyncValue<T>`, `InfiniteResource<T>`, `Page<TItem,TCursor>` types | **Chunk 14** | DTO surface; hooks consume them. |
| `Component.UseEffect`, `UseRef`, `UseReducer`, `UseState`, `UseContext` — the hook *plumbing* | **Chunk 14** | This chunk reviews hook *implementations*, not the hook engine. |

---

## 9. Summary

The hooks library is **mostly correctness-grade code** with no I/O surface and therefore no direct attacker reach. The realistic findings are:

- **F-02 (Medium)**: unbounded `_deferredRequests` growth in `UseInfiniteResource`.
- **F-03 (Medium)**: `UseFocusTrap` has no escape hatch and can wedge keyboard users on hidden modals.
- **F-01 (Low)**: hook teardown depends on `UseEffect` running; constructor-time registrations can leak.
- **F-04 (Low)**: `PendingScope.Changed` invokes user delegates with no exception guard.
- **F-07 (Low)**: dispatcher shutdown causes inline cross-thread continuation.

Everything else is informational — defensive code suggestions and developer-experience traps (F-08, F-09, F-10).

No critical or high findings. The chunk's threat profile is **availability + UX correctness**, not confidentiality or integrity.
