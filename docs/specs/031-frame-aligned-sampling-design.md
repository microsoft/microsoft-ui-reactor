# Frame-Aligned Sampling — Design Spec

## Overview

A new hook, `UseSample`, that lets a component consume a high-frequency data source but re-render at most every N display frames. Intended for live-telemetry scenarios: a 50 Hz stream feeding a readout that only needs to tick at ~20 Hz, where the user would rather wait for the next data point than thrash the UIElement tree at producer rate.

The contract is **frame-aligned** ("at most every N frames"), not wall-clock ("at most N Hz"). Frame-aligned cadence is visually stable on a compositor-paced renderer and falls out cleanly from Microsoft.UI.Reactor's existing render loop (`ReactorHost.cs:218–240`), which is already dispatcher- and priority-driven. Wall-clock throttles introduce jitter because 20 Hz doesn't cleanly divide 60 Hz.

Semantics: **most recent value wins** (eventual consistency). Intermediate values are dropped, not queued. Producer-thread sampling avoids paying a dispatcher hop per dropped event.

---

## Goals

1. **One idiomatic primitive.** `UseSample<T>(IObservable<T>, framesPer, initial)` and a sibling `UseSampled<T>` for `INotifyPropertyChanged`, fitting alongside `UseObservable` in `RenderContext.cs:415`.
2. **Frame-aligned cadence.** Sampling decisions reference a monotonic frame counter exposed by the host, not wall-clock time.
3. **Free interactive bypass.** User-driven state (`useState`, event handlers) is unaffected; only data flowing through the sample hook is gated.
4. **Free animation pass-through.** Transitions interpolate between committed samples. The sample rate controls *data writes*, not *visual refresh*.
5. **Producer-thread drop.** Events arriving on background threads are coalesced into a pending slot before dispatcher marshaling — one UI-thread commit per batch, not per event.
6. **Virtual clock for tests.** The selftest harness exposes a controllable frame clock so sampling behavior is deterministic in unit tests.
7. **Stress validation.** `StressPerf.Reactor` gains a CLI flag to enable sampling over its existing 30 Hz snapshot path, so we can measure real reconciler/FPS/allocation impact.

## Non-goals

- **Not** a replacement for the existing `DispatcherQueuePriority.Low` self-throttling in `ReactorHost.cs:238`. That's a separate, system-wide coalescing mechanism and stays as-is.
- **Not** a per-element or per-control throttle modifier. Element-side throttling would save only the reconciler write, not the render work; it's strictly worse than hook-side for the asked-for use case. (See [Alternatives](#alternatives-considered).)
- **Not** incremental-collection coalescing (`INotifyCollectionChanged` / `ObservableCollection<T>`). Snapshot-valued collections (`T = StockItem[]`) work transparently; incremental coalescing is a v2 sibling hook — see [Collection-valued sources](#collection-valued-sources).
- **Not** a subtree render boundary (`<Throttled framesPer=…>`). Deferred to v2. The hook covers the stated use case; the boundary is additive.
- **Not** a back-pressure mechanism that propagates consumer rate back to the producer. `UseSample` always drops on the consumer side, even when the source is pull-based (`IAsyncEnumerable<T>`).
- **Not** a global "UI is saturated" circuit-breaker. Out of scope; orthogonal.

---

## Motivation

Reactor already batches well when the dispatcher is saturated: `DispatcherQueuePriority.Low` re-enqueue (`ReactorHost.cs:238`) coalesces rapid `RequestRender()` calls into per-frame passes, and `ObservableTreeTracker.cs:114–116` marshals cross-thread notifications onto the UI dispatcher. What's missing is an opt-in *consumer rate ceiling*: a way for a component to say "I don't care about every tick; commit at most every N frames and drop the rest."

Current domain-specific workarounds — `FocusRevalidationService.ThrottleWindow` (30 s), `ChartLiveAnnouncer.DebounceMs` (400 ms), `AutoSuggestElement.DebounceMs` (300 ms) — are all bespoke. None of them are reusable from component code.

The target scenario is a live readout driven by a 50 Hz source (sensor data, telemetry stream, tick data). Without sampling, a 50 Hz producer forces a full render + reconcile pass 50 times a second, even when the user can't meaningfully perceive that rate. With `UseSample(source, framesPer: 3)`, the component renders roughly every third frame (~20 Hz at 60 Hz refresh), the reconciler walks the tree at that same rate, and the UIElement writes likewise fall to ~20 Hz. Intermediate values are silently dropped; the committed value is always the most recent one the producer emitted.

---

## Design

### API surface

```csharp
// Primary primitive.
// framesPer: 1 = no throttling (fast path; hook degenerates to UseObservable).
// framesPer: 0 or negative = ArgumentOutOfRangeException.
T UseSample<T>(IObservable<T> source, int framesPer, T initial = default);

// Sibling for INotifyPropertyChanged, mirroring UseObservable's shape.
T UseSampled<T>(
    Func<T> getter,
    INotifyPropertyChanged source,
    int framesPer);
```

Deferred (v2): `UseSampledCollection<T>(ObservableCollection<T>, int framesPer)` for `INotifyCollectionChanged` sources. See [Collection-valued sources](#collection-valued-sources).

### Semantics

- **First value is free.** The first value emitted after mount commits immediately; no frame gate applies to it. Ensures the UI doesn't stay blank for up to `framesPer−1` frames.
- **Most recent wins.** Between sample points the hook retains only the latest pending value. Older values are dropped. `T` equality is not considered — the pending slot is overwritten unconditionally.
- **Frame alignment.** A commit is eligible when `currentFrame − lastCommittedFrame ≥ framesPer`. `currentFrame` is a monotonic counter advanced once per render pass by the host.
- **`framesPer: 1` fast path.** Hook doesn't register sampling state; acts as a passthrough equivalent to `UseObservable`. Prevents paying any cost when sampling is disabled.
- **Interactive bypass is automatic.** User events route through `UseState` / event handlers. Only data flowing through `UseSample` is gated. No explicit bypass mechanism required.
- **Animation pass-through is automatic.** Transitions live on the reconciler / composition side and interpolate between committed values. Sampling rate affects data-write frequency, not visual refresh. Sampling a value at 20 Hz that has a 200 ms transition still renders smoothly at display rate.

### Producer-thread drop

Events arriving on a background thread should be coalesced before dispatcher marshaling, not after.

```
On event T value arriving (any thread):
    prior = Interlocked.Exchange(ref _pending, value)
    if (prior == null) {
        // We won the race to schedule the commit.
        dispatcher.TryEnqueue(CommitLatest, priority: Low)
    }
    // Otherwise: just overwrote the pending slot, no new dispatch.

On CommitLatest (UI thread):
    var latest = Interlocked.Exchange(ref _pending, null);
    if (latest is null || !_alive) return;
    if (currentFrame - _lastCommittedFrame >= _framesPer) {
        _committed = latest;
        _lastCommittedFrame = currentFrame;
        RequestRender();
    } else {
        // Not eligible yet; stash back and schedule next eligible frame.
        Interlocked.Exchange(ref _pending, latest);
        ScheduleCommitForFrame(_lastCommittedFrame + _framesPer);
    }
```

Properties of this design:
- Exactly one dispatcher hop per batch of events between commits, regardless of producer rate.
- Ordering is preserved trivially — only one commit is ever in flight per hook.
- `T` is boxed if it's a value type (the pending slot is `object?`). For reference types, no extra allocation.
- `_alive` is a volatile flag checked on the UI thread, flipped on unmount to guard fire-after-dispose races.

### Frame clock integration

A new abstraction `IFrameClock` exposes a monotonic `CurrentFrame` counter. Production binds to a host-owned counter incremented at the start of each render pass in `ReactorHost.cs` (adjacent to the existing render loop at line 218). Tests bind to a `VirtualFrameClock` that advances under explicit control.

```csharp
public interface IFrameClock
{
    int CurrentFrame { get; }
    void OnFrameAdvanced(Action callback);  // optional; used by deferred-commit rescheduling
}
```

The hook reads `CurrentFrame` on each event arrival and commit attempt. When a commit is deferred (value arrived mid-window), the hook registers a one-shot callback via `OnFrameAdvanced` to retry at the next frame boundary. The implementation can be a simple subscriber list drained at the start of each render pass.

### Dispose / unmount

On unmount, the hook must:
1. Set `_alive = false` (guards any in-flight `CommitLatest`).
2. Unsubscribe from the observable / `INotifyPropertyChanged` source.
3. Cancel any pending `OnFrameAdvanced` registration.
4. `Interlocked.Exchange(ref _pending, null)` to drop retained references (important if `T` is a large snapshot).

---

## Collection-valued sources

Two distinct cases, with different coalescing requirements:

### Flavor A — Snapshot-valued (supported by `UseSample`)

`T = StockItem[]`, producer replaces the whole collection each tick. `StressPerf.Reactor/Program.cs:45,97` is exactly this shape: `UseState(source.Snapshot())` + `setData(src.Snapshot())` on a 33 ms timer. "Most recent wins" is unambiguous for snapshot arrays — dropping intermediate snapshots is safe because each snapshot is a complete state.

`UseSample<StockItem[]>(snapshotObservable, framesPer: 3)` works with no special handling. At 4,900 cells × 30 Hz, this drops the render+reconcile rate to ~10 Hz (every 3 frames at 60 Hz refresh), which is the expected win.

**Caveat to document clearly:** `UseSample` reduces the *frequency* of renders, not the *per-render cost*. The reconciler still walks all 4,900 cells on each committed sample, because `src.Snapshot()` returns a fresh array of fresh records with no referential equality. Per-item memoization / diff-by-changed-indices is a separate concern (`StockDataSource.Update` already returns changed indices but no consumer exists). Out of scope for this spec — do not bundle.

### Flavor B — Incremental (v2, via `UseSampledCollection`)

`ObservableCollection<T>` / `INotifyCollectionChanged`, producer emits Add / Remove / Replace / Move events. "Most recent wins" does **not** work here: dropping an Add that sits between a Remove and a Replace corrupts the consumer's view of the collection.

The correct coalescing pattern: maintain an internal mirror, apply every event to the mirror, and emit a snapshot of the mirror at sample ticks. Event-stream drops become event-stream-application drops — the mirror state stays consistent, and the consumer sees the sampled mirror state.

Deferred to v2. The asked-for use case (high-frequency telemetry) is Flavor A.

---

## Async interaction

Reactor already marshals cross-thread notifications via `ObservableTreeTracker.cs:128–140`. `UseSample` extends this with four async-specific guarantees:

- **Producer-thread drop** (see above): sampling decisions happen before the dispatcher hop, so a 50 Hz background producer causes ~20 dispatcher hops per second instead of 50.
- **Ordering preservation**: the "post only when pending was null" pattern ensures at most one commit is scheduled at a time; back-to-back events never cause two commits to race.
- **Unmount safety**: `_alive` flag + pending-slot null + registration cancellation. Any in-flight event arriving after unmount is dropped cleanly.
- **Pull-source handling**: for `IAsyncEnumerable<T>` adapters, drops happen consumer-side only. Producer iteration is not slowed. Matches the user-stated intent ("wait for the next data update").

### Re-entrancy

If a commit fires while a render is in flight (e.g., because `RequestRender()` is called from within a render function), we rely on Reactor's existing `RequestRender()` idempotency — it's CAS-gated per frame via `_renderPending` (`ReactorHost.cs:218–240`). No additional re-entrancy handling is needed beyond calling `RequestRender()` rather than synchronously invoking the render loop.

---

## Testing strategy

### Unit tests (virtual clock)

Add `VirtualFrameClock` to the selftest harness. Selftest hooks inject it via `IFrameClock`. Test matrix:

- `framesPer: 1` — every value commits immediately.
- `framesPer: 3` — 10 pushed values with frame clock not advancing commit exactly one (the latest); advancing 3 frames and pushing again commits the new latest.
- First value is free — mount + push one value + do not advance frame clock + assert committed.
- Most-recent-wins — push A, push B, push C without advancing; commit equals C.
- Interactive bypass — component uses `UseSample(fast)` + `UseState(x)`; setting x inside an event handler commits x immediately, independent of frame clock state.
- Unmount mid-pending — push value, unmount before frame advances, assert no commit fires, no exceptions, reference is released.
- Background-thread push — push values from a pool thread; assert exactly one commit per window, ordering preserved.
- `framesPer: 0` / negative — `ArgumentOutOfRangeException` at hook construction.

### Stress validation

Add a CLI flag to `StressPerf.Reactor`:

```
StressPerf.Reactor --sample-frames 3
```

When set, wraps the `setData(src.Snapshot())` path in a `UseSample`. Compare against the current baseline:
- FPS (from existing `CompositionTarget.Rendering` counter).
- Reconcile / tree-build / effects phase timings (from existing `PerfTracker.RecordPhases`).
- Allocation rate (GC counters).

Acceptance target: with `--sample-frames 3`, reconcile phase time per second drops by ≥50% vs. baseline at equivalent producer rate, with no visible correctness regressions (latest price still shown, no stuck cells).

---

## Alternatives considered

### Element-side modifier (`.Throttle(framesPer: 3)`)

```csharp
Label(fastValue).Throttle(framesPer: 3)  // NOT proposed
```

Rejected. Lets the component re-render at producer rate and only skips the UIElement write. Strictly more expensive than hook-side sampling, which also skips the render pass and the reconciler walk. Loses the main cost-saving property.

### Subtree boundary (`<Throttled framesPer=3>…</Throttled>`)

```xml
<Throttled framesPer={3}>
  <Label text={fastValue} />
</Throttled>
```

Deferred to v2. Does save the render work (unlike the element modifier), and composes (nested boundaries take the min). But it's additive over the hook — same underlying machinery, just packaged as a render-gating component. Ship the hook first; add the boundary if the shape is actually requested.

### Wall-clock Hz (`hz: 20`)

Rejected during brainstorming. 20 Hz doesn't cleanly divide 60 Hz, so a wall-clock sampler produces jitter (some frames commit, some don't, with no stable pattern). `framesPer` is the honest parameter on a compositor-paced renderer.

### Rx `Sample` / `Throttle` operators as a thin `UseObservable` wrapper

Implementation option, not a semantic alternative. Wrapping `source.Sample(TimeSpan.FromMilliseconds(50))` and feeding the result into `UseObservable` would work, but:
- Pulls in a Rx dependency or a hand-rolled reimplementation.
- Uses wall-clock timing, not frame alignment.
- Doesn't give us the "first value is free" or frame-clock-driven deferred commit behavior.

Skip.

---

## Open issues

1. **Display refresh rate discovery.** `framesPer` is honest on a 60 Hz display but misleading on 120 Hz / 240 Hz / variable-refresh displays — `framesPer: 3` on 120 Hz is 40 Hz, not 20 Hz. Options: (a) document the dependency and leave it to the caller; (b) expose a `displayHz` hint from the host and provide a `UseSampleHz` convenience overload that rounds internally; (c) always normalize to a canonical 60 Hz reference. Leaning: (a) for v1 — the primitive stays honest; add (b) later if it's actually a pain point.

2. **Boxing for value-typed `T`.** The pending slot uses `Interlocked.Exchange(ref object?, …)`, which boxes value types. At high rates with large structs (e.g., `T = StockItem[]` where the array reference doesn't box, but a hypothetical `T = struct SensorReading`) this could measurably allocate. Alternatives: a generic `Interlocked.Exchange<T>` overload (reference-only), or a per-hook typed `Volatile.Write` + lock on value paths. Benchmark-driven decision — revisit if `--sample-frames` stress runs show allocation spikes.

3. **Equality-based skip.** Should `UseSample` skip the commit entirely if the latest value equals the previously committed one? Pro: saves a no-op render. Con: requires `IEquatable<T>` or `EqualityComparer<T>.Default` (which boxes structs, defeats the point). Leaning: no — keep the primitive simple; a caller can wrap with `DistinctUntilChanged` on their observable if they want that. But worth a one-line note in the docs.

4. **Multiple consumers of one source, different rates.** If two components both `UseSample(sameSource, different framesPer)`, each maintains its own pending slot and subscribes independently. Is that OK, or do we want a shared "sampled publisher" that dedupes subscriptions? v1: independent subscriptions (simpler; matches observable contract). Revisit if it shows up as a real cost.

5. **`UseSampled` for `INotifyPropertyChanged` — pull or push?** `UseObservable` (`RenderContext.cs:415`) subscribes to `PropertyChanged` and calls `getter()` inside the handler. For sampling, should we (a) call `getter()` on every notification and store the result in the pending slot (matches observable semantics, but runs the getter at producer rate), or (b) only call `getter()` on commit (cheaper if getter is expensive, but breaks if the object is mutated out from under us between notification and commit)? Leaning: (a) — matches `UseObservable` and avoids the mutation-race footgun.

6. **Deferred-commit rescheduling mechanism.** Spec sketches `IFrameClock.OnFrameAdvanced(callback)` as a one-shot subscriber list drained per render. Concrete question: should this be a general host primitive (useful beyond sampling — animation frame callbacks, frame-aligned effects) or kept private to the sampling hook? General is more reusable but widens the host API surface. Probably worth pulling out into a public `IFrameClock` member since the shape is generic.

7. **Interaction with `DispatcherQueuePriority.Low` re-enqueue.** The existing render loop already re-enqueues at Low priority (`ReactorHost.cs:238`) to avoid starving layout/paint. When `UseSample` posts a commit, does it use the same priority or a higher one? Using Low risks deferring sample commits indefinitely under UI-thread pressure (possibly desirable — the whole point is "drop if we can't keep up"); using Normal could starve the existing low-priority passes. Leaning: Low, matching the render loop. Document the implication: under sustained overload, sample commits can skew later than their frame budget implies.

8. **Selftest harness clock wiring.** `VirtualFrameClock` needs to be injectable at selftest-host construction time. The current host has `OnRenderComplete = …` assigned after mount (`StressPerf.Reactor/Program.cs:64`); the clock likely needs to come in earlier. Concrete integration point TBD — depends on how the selftest host currently constructs `ReactorHost`.

9. **Metric / diagnostic surface.** Should the hook expose commit-rate / drop-rate counters for devtools? A real 50 Hz source with `framesPer: 3` would show ~60% drops; seeing that in devtools is valuable confirmation that sampling is engaged. Leaning: yes, but defer — add a `IReactorDiagnostics` surface when devtools grows sampling instrumentation generally. Not v1.

10. **Naming.** `UseSample` vs. `UseSampled` vs. `UseThrottled` vs. `UseCoalesced`. `UseSample` matches Rx `Sample` semantics (periodic tick pulls latest). `UseSampled` is currently the `INotifyPropertyChanged` sibling; the naming asymmetry is slightly awkward. Alternative: rename the `INPC` variant to `UseSampleProperty`. Bikeshed worth having before v1 ships because it's public API.

---

## Scope for v1

In:
- `UseSample<T>(IObservable<T>, int framesPer, T initial)` primitive.
- `UseSampled<T>(Func<T>, INotifyPropertyChanged, int framesPer)` `INPC` sibling.
- `IFrameClock` abstraction + host-owned production implementation.
- `VirtualFrameClock` + selftest fixtures.
- `StressPerf.Reactor` `--sample-frames N` CLI flag + baseline measurement.
- One documentation page under `docs/guide/` or `docs/reference/` on sampling semantics.

Out (deferred / follow-up):
- `UseSampledCollection<T>` for `INotifyCollectionChanged` (Flavor B).
- `<Throttled framesPer=…>` subtree render boundary.
- Display-refresh-rate discovery / `UseSampleHz` convenience overload.
- Devtools commit-rate / drop-rate counters.
- Global "UI saturated" back-pressure signal.

---

## Files anticipated (non-binding)

- `src/Reactor/Core/Hooks/UseSample.cs` (new).
- `src/Reactor/Core/RenderContext.cs` — register hook entry points.
- `src/Reactor/Core/IFrameClock.cs` (new) + production `FrameClock` wired into `ReactorHost.cs:218–240`.
- `selfhost/…` — `VirtualFrameClock` + fixtures.
- `tests/stress_perf/StressPerf.Shared/CliOptions.cs` — add `--sample-frames`.
- `tests/stress_perf/StressPerf.Reactor/Program.cs` — wrap `setData` path.
- `docs/guide/sampling.md` or `docs/reference/sampling.md` (new).
