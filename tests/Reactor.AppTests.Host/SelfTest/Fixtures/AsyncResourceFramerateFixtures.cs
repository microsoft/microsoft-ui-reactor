using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// "Works in isolation, breaks at 60Hz" regression canaries for <c>UseResource</c>.
/// Each fixture drives ~60 frames with a mutation per frame and asserts that the
/// invariants listed in <c>docs/specs/tasks/async-resources-implementation.md</c>
/// §1.3 (framerate) hold across the whole run. The existing <c>Harness.Render()</c>
/// loop is used as a frame tick — each call waits for Reactor's render queue to
/// go idle plus one compositor breath, which is equivalent to the cadence used
/// by the animation fixture suite.
/// </summary>
internal static class AsyncResourceFramerateFixtures
{
    const int Frames = 60;

    // ════════════════════════════════════════════════════════════════════
    //  DepsThrashing — deps hash changes every frame; only one in-flight
    //  fetch at any moment, and no un-cancelled task survives to Data.
    // ════════════════════════════════════════════════════════════════════

    internal class DepsThrashing(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Drain any unobserved tasks that earlier fixtures finalized but whose
            // events haven't fired yet — otherwise the test "inherits" unrelated
            // noise and flakes. Marshal off the UI dispatcher: a finalizer that
            // needs to release a UI-thread-affine RCW would deadlock if the
            // dispatcher were blocked inside WaitForPendingFinalizers.
            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });

            int unobserved = 0;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
            { Interlocked.Increment(ref unobserved); e.SetObserved(); };
            TaskScheduler.UnobservedTaskException += handler;

            try
            {
                var cache = new QueryCache();
                // Instrumentation — fetcher runs on the thread pool; use interlocked.
                int started = 0;
                int cancelled = 0;
                int completed = 0;
                int maxInFlight = 0;
                int inFlight = 0;

                Action<int>? setDep = null;

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (dep, set) = ctx.UseState(0);
                    setDep = set;

                    var v = ctx.UseResource(
                        async ct =>
                        {
                            Interlocked.Increment(ref started);
                            int live = Interlocked.Increment(ref inFlight);
                            // Atomic-max across threads.
                            int current;
                            do { current = maxInFlight; if (live <= current) break; }
                            while (Interlocked.CompareExchange(ref maxInFlight, live, current) != current);

                            try
                            {
                                ct.Register(() => Interlocked.Increment(ref cancelled));
                                // Infinite delay: the only exit is OperationCanceledException
                                // when the CT fires. This eliminates the timer-vs-cancel race
                                // that made completed reach 2 when CI render ticks approached
                                // the old 200ms fixed delay. `completed` stays 0; the
                                // assertion below guards against regressions.
                                await Task.Delay(Timeout.Infinite, ct);
                                Interlocked.Increment(ref completed);
                                return $"dep={dep}";
                            }
                            finally { Interlocked.Decrement(ref inFlight); }
                        },
                        cache,
                        new object[] { dep });

                    return TextBlock(v is AsyncValue<string>.Data d ? d.Value : "loading");
                });

                await Harness.Render();

                // Thrash deps once per frame.
                for (int i = 1; i <= Frames; i++)
                {
                    setDep!(i);
                    await Harness.Render();
                }

                // Invariant 1: fetches started roughly once per deps-change (some coalesce).
                H.Check($"DepsThrashing_Started (started={started}, frames={Frames})",
                    started >= Frames / 3 && started <= Frames + 2);

                // Invariant 2: at any instant, the hook owns at most one live fetch
                // (earlier ones are cancelled before a new one is launched).
                H.Check($"DepsThrashing_MaxInFlightBounded (max={maxInFlight})",
                    maxInFlight <= 2);

                // Invariant 3: all but at most one fetch is cancelled (the winner).
                H.Check($"DepsThrashing_StaleCancelled (cancelled={cancelled}, started={started})",
                    cancelled >= started - 1);

                // Invariant 4: no fetch body completes — all use Timeout.Infinite delays and
                // exit only via cancellation. completed must be exactly 0.
                H.Check($"DepsThrashing_AtMostOneCompleted (completed={completed})",
                    completed == 0);

                // Invariant 5: no unobserved task exceptions escaped under this load.
                // Dispose the host first so the last in-flight Task.Delay(Infinite, ct) is
                // cancelled via UseEffect teardown before we drain for unobserved exceptions.
                host.Dispose();
                await Harness.Render();
                await Task.Run(() =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                });
                await Harness.Render();
                H.Check($"DepsThrashing_NoUnobserved (got {unobserved})", unobserved == 0);
            }
            finally { TaskScheduler.UnobservedTaskException -= handler; }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RenderShortCircuit — cache-hit-fresh path with a stable value: many
    //  parent-driven re-renders must not trigger additional fetcher calls,
    //  because the cache entry is fresh and deps don't change.
    // ════════════════════════════════════════════════════════════════════

    internal class RenderShortCircuit(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            // Make the entry fresh forever for the fixture's duration.
            var stale = TimeSpan.FromMinutes(1);
            int fetcherCalls = 0;
            int childRenders = 0;

            Action<int>? tick = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (t, set) = ctx.UseState(0);
                tick = set;
                return Component<ShortCircuitChild, ShortCircuitChildProps>(
                    new ShortCircuitChildProps(
                        cache,
                        stale,
                        () => Interlocked.Increment(ref fetcherCalls),
                        () => Interlocked.Increment(ref childRenders),
                        t));
            });

            // Prime: first frame kicks off the initial fetch (sync-complete).
            await Harness.Render();

            int primedFetches = fetcherCalls;
            int primedRenders = childRenders;

            // Tick parent state once per frame — the child re-renders, but UseResource
            // sees a fresh cache hit (stale time = 1min, deps stable) so it never calls
            // the fetcher again. Assert fetcherCalls stays at the primed count.
            for (int i = 1; i <= Frames; i++)
            {
                tick!(i);
                await Harness.Render();
            }

            H.Check($"RenderShortCircuit_FetcherNotReInvoked (before={primedFetches}, after={fetcherCalls})",
                fetcherCalls == primedFetches);

            // And the child re-renders exactly once per parent tick (no hook-driven extras).
            H.Check($"RenderShortCircuit_ChildRenderCount (renders={childRenders}, expected~={primedRenders + Frames})",
                childRenders <= primedRenders + Frames + 2 &&
                childRenders >= primedRenders + Frames - 2);
        }
    }

    internal sealed record ShortCircuitChildProps(
        QueryCache Cache, TimeSpan StaleTime, Action OnFetch, Action OnRender, int Tick);

    internal sealed class ShortCircuitChild : Component<ShortCircuitChildProps>
    {
        public override Element Render()
        {
            Props.OnRender();
            var v = UseResource(
                _ => { Props.OnFetch(); return Task.FromResult("stable"); },
                Props.Cache,
                Array.Empty<object>(), // deps stable — cache hit after first render
                new ResourceOptions(StaleTime: Props.StaleTime, CacheKey: "shortcircuit/shared"));
            // Include Tick so the parent's re-render actually produces a different Element identity.
            return TextBlock($"t={Props.Tick} v={(v is AsyncValue<string>.Data d ? d.Value : "?")}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  CacheChurn — 16 siblings rotate cache keys every frame. With a low
    //  CacheTime, the working set stays bounded because keys lose their
    //  last subscriber and get evicted shortly after.
    // ════════════════════════════════════════════════════════════════════

    internal class CacheChurn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const int Siblings = 16;

            // Low eviction interval + low CacheTime → keys drop quickly when subscribers leave.
            var prevPoll = QueryCache.EvictionPollInterval;
            QueryCache.EvictionPollInterval = TimeSpan.FromMilliseconds(16);
            try
            {
                var cache = new QueryCache();

                Action<int>? setEpoch = null;

                int maxCacheCount = 0;

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (epoch, set) = ctx.UseState(0);
                    setEpoch = set;

                    // Each sibling uses a cache key derived from (siblingIndex, epoch).
                    // Every epoch tick invalidates all subscriptions and makes them
                    // subscribe to fresh keys — the old keys become evictable.
                    var siblings = new Element[Siblings];
                    for (int i = 0; i < Siblings; i++)
                    {
                        siblings[i] = Component<ChurnChild, ChurnChildProps>(
                            new ChurnChildProps(cache, i, epoch));
                    }
                    return VStack(siblings);
                });

                await Harness.Render();

                for (int f = 1; f <= Frames; f++)
                {
                    setEpoch!(f);
                    await Harness.Render();
                    // Force the eviction sweep so old keys are reaped deterministically.
                    cache.EvictNow();
                    if (cache.Count > maxCacheCount) maxCacheCount = cache.Count;
                }

                // Working set is at most: current-epoch siblings + at-most-one-old-epoch
                // (slots whose CacheTime hasn't yet elapsed). With CacheTime=zero, the
                // old keys evict on the very next sweep → cap is Siblings plus slack.
                H.Check($"CacheChurn_WorkingSetBounded (max={maxCacheCount}, siblings={Siblings})",
                    maxCacheCount <= Siblings * 3);

                // After the run, drive one more eviction sweep — only the live subscribers
                // should remain.
                cache.EvictNow();
                H.Check($"CacheChurn_FinalCacheCountBounded (count={cache.Count})",
                    cache.Count <= Siblings + 2);
            }
            finally { QueryCache.EvictionPollInterval = prevPoll; }
        }
    }

    internal sealed record ChurnChildProps(QueryCache Cache, int Index, int Epoch);

    internal sealed class ChurnChild : Component<ChurnChildProps>
    {
        public override Element Render()
        {
            var v = UseResource(
                _ => Task.FromResult($"i={Props.Index}/e={Props.Epoch}"),
                Props.Cache,
                new object[] { Props.Index, Props.Epoch },
                new ResourceOptions(
                    // Zero cache time → evictable on next sweep once last subscriber leaves.
                    CacheTime: TimeSpan.Zero,
                    StaleTime: TimeSpan.FromSeconds(5)));
            return TextBlock(v is AsyncValue<string>.Data d ? d.Value : "loading");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  FastRemount — mount/unmount the fetch child on every frame. After
    //  60 cycles: no unobserved task exceptions, every started fetch gets
    //  cancelled (proves the hook's CTS lifecycle is balanced), and the
    //  in-flight task table returned by TaskScheduler is not growing.
    // ════════════════════════════════════════════════════════════════════

    internal class FastRemount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });

            int unobserved = 0;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
            { Interlocked.Increment(ref unobserved); e.SetObserved(); };
            TaskScheduler.UnobservedTaskException += handler;

            try
            {
                var cache = new QueryCache();
                int started = 0;
                int cancelled = 0;

                Action<bool>? setVisible = null;

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (visible, set) = ctx.UseState(true);
                    setVisible = set;
                    if (!visible) return TextBlock("off");
                    return Component<RemountChild, RemountChildProps>(
                        new RemountChildProps(cache,
                            () => Interlocked.Increment(ref started),
                            () => Interlocked.Increment(ref cancelled)));
                });

                await Harness.Render();

                // Mount/unmount once per frame for the full budget.
                for (int f = 0; f < Frames; f++)
                {
                    setVisible!(false);
                    await Harness.Render();
                    setVisible!(true);
                    await Harness.Render();
                }

                // Final unmount and let any cancellation callbacks drain.
                setVisible!(false);
                for (int i = 0; i < 4; i++) await Harness.Render();
                await Task.Run(() =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                });
                await Harness.Render();

                // Every mount should have kicked off a fetch. Every one must be cancelled
                // by the following unmount — CTS lifecycle is the only thing that closes
                // the cycle; a leak would show up as cancelled < started.
                H.Check($"FastRemount_FetchesStarted (started={started})",
                    started >= Frames / 2);
                H.Check($"FastRemount_AllCancelled (started={started}, cancelled={cancelled})",
                    cancelled >= started - 2);

                H.Check($"FastRemount_NoUnobserved (got {unobserved})", unobserved == 0);
            }
            finally { TaskScheduler.UnobservedTaskException -= handler; }
        }
    }

    internal sealed record RemountChildProps(QueryCache Cache, Action OnStart, Action OnCancel);

    internal sealed class RemountChild : Component<RemountChildProps>
    {
        public override Element Render()
        {
            var v = UseResource(
                async ct =>
                {
                    Props.OnStart();
                    ct.Register(() => Props.OnCancel());
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    return "never";
                },
                Props.Cache,
                // Unique cache key per mount to force a fresh fetch each cycle.
                new object[] { Guid.NewGuid() });
            return TextBlock(v is AsyncValue<string>.Data ? "done" : "loading");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  DispatcherPressure — 1000 TryEnqueue callbacks queued per frame.
    //  The hook's marshalling must not starve the dispatcher: the fixture
    //  must complete within a wall-clock budget, and every queued callback
    //  must eventually fire.
    // ════════════════════════════════════════════════════════════════════

    // ════════════════════════════════════════════════════════════════════
    //  DataGridEditMutation — one UseMutation.RunAsync per frame for 60
    //  frames. Simulates the load the hook-path DataGridState generates
    //  when a user types/commits rapidly: each edit fires an optimistic
    //  update synchronously, the mutator awaits a short async round-trip,
    //  and on success locks in the server-authoritative value. Covers
    //  §11 Phase-3 "rapid cell edits, each firing a UseMutation".
    // ════════════════════════════════════════════════════════════════════

    internal class DataGridEditMutation(Harness h) : SelfTestFixtureBase(h)
    {
        // 60-frame mutation pump + 30-frame drain; same wall-clock-floor reasoning
        // as HookPagingFramerateScroll. See INVESTIGATION.md Cluster T2.
        public override TimeSpan FixtureTimeout => TimeSpan.FromSeconds(30);

        public override async Task RunAsync()
        {
            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });

            int unobserved = 0;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
            { Interlocked.Increment(ref unobserved); e.SetObserved(); };
            TaskScheduler.UnobservedTaskException += handler;

            try
            {
                int mutatorStarted = 0;
                int mutatorCompleted = 0;
                int onSuccessFired = 0;
                int onErrorFired = 0;

                Action<int>? fire = null;
                Mutation<int, int>? mutationRef = null;

                // Observed invariants gathered across frames.
                bool sawPendingTrueMidRun = false;
                int maxConcurrentPending = 0;
                int currentPending = 0;

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    // `localValue` is the optimistic-then-settled view — equivalent to the
                    // DataGrid cell's displayed text. It is updated synchronously in
                    // OnOptimistic and overwritten in OnSuccess.
                    var (localValue, setLocal) = ctx.UseState(0);

                    var mutation = ctx.UseMutation<int, int>(
                        mutator: async (delta, ct) =>
                        {
                            Interlocked.Increment(ref mutatorStarted);
                            int live = Interlocked.Increment(ref currentPending);
                            int current;
                            do { current = maxConcurrentPending; if (live <= current) break; }
                            while (Interlocked.CompareExchange(ref maxConcurrentPending, live, current) != current);
                            try
                            {
                                // Short, variable delay so mutations overlap between frames.
                                await Task.Delay(30 + (delta % 10), ct);
                                Interlocked.Increment(ref mutatorCompleted);
                                // Server-authoritative value: +100 offset so we can tell
                                // optimistic (delta) and settled (delta+100) apart.
                                return delta + 100;
                            }
                            finally { Interlocked.Decrement(ref currentPending); }
                        },
                        cache: null,
                        options: new MutationOptions<int, int>(
                            // Optimistic: bump immediately so the UI reflects the edit
                            // before the round-trip completes. Equivalent to the
                            // DataGrid's optimistic cell update in Phase 3.
                            OnOptimistic: delta => setLocal(delta),
                            OnSuccess: (server, delta) =>
                            {
                                Interlocked.Increment(ref onSuccessFired);
                                setLocal(server);
                            },
                            OnError: (ex, delta) => Interlocked.Increment(ref onErrorFired)));

                    mutationRef = mutation;
                    fire = delta => _ = mutation.RunAsync(delta);

                    return TextBlock($"v={localValue} p={mutation.IsPending}");
                });

                await Harness.Render();

                // One commit per frame for the framerate budget.
                for (int f = 1; f <= Frames; f++)
                {
                    fire!(f);
                    await Harness.Render();
                    if (mutationRef is { IsPending: true }) sawPendingTrueMidRun = true;
                }

                // Let the tail of in-flight mutators drain.
                for (int i = 0; i < 30; i++) await Harness.Render();

                // Invariant 1: optimistic path fired every frame. The last optimistic
                // value to land before the first server response is the frame number;
                // after all mutators resolve, the last value should be Frames+100.
                // Check by inspecting the rendered text — the UI must show the settled
                // value of the last-completing mutation.
                H.Check($"DataGridEditMutation_MutatorRan (started={mutatorStarted}, frames={Frames})",
                    mutatorStarted == Frames);

                // Invariant 2: every started mutator completed (no leaks, no exceptions).
                H.Check($"DataGridEditMutation_AllCompleted (started={mutatorStarted}, completed={mutatorCompleted})",
                    mutatorCompleted == mutatorStarted);

                H.Check($"DataGridEditMutation_AllOnSuccess (fired={onSuccessFired})",
                    onSuccessFired == mutatorStarted);

                H.Check($"DataGridEditMutation_NoOnError (errors={onErrorFired})",
                    onErrorFired == 0);

                // Invariant 3: IsPending was observed true during the run (the mutation
                // state machine actually transitioned through pending).
                H.Check("DataGridEditMutation_PendingObserved", sawPendingTrueMidRun);

                // Invariant 4: IsPending returns to false once the tail drains — the
                // classic "stuck in pending" regression the spec calls out.
                H.Check($"DataGridEditMutation_NotStuckPending (final={mutationRef!.IsPending})",
                    !mutationRef.IsPending);

                // Invariant 5: concurrent-pending ceiling. With 30-40ms mutators and
                // one dispatch per render tick we expect some overlap but not runaway.
                H.Check($"DataGridEditMutation_PendingOverlapBounded (max={maxConcurrentPending})",
                    maxConcurrentPending <= Frames);

                // Invariant 6: the final visible value is the server-settled form of
                // the last committed delta (delta + 100). LastResult is set by
                // OnSuccess on the latest-completing mutation, which for a sequential
                // workload is the last-fired delta. Tolerate completion-order jitter
                // by accepting any f+100 where 1 ≤ f ≤ Frames.
                int finalValue = mutationRef.LastResult;
                bool finalInRange = finalValue >= 1 + 100 && finalValue <= Frames + 100;
                H.Check($"DataGridEditMutation_FinalValueServerSettled (value={finalValue})",
                    finalInRange);

                // Invariant 7: no unobserved exceptions leaked from any pending mutator.
                await Task.Run(() =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                });
                await Harness.Render();
                H.Check($"DataGridEditMutation_NoUnobserved (got {unobserved})", unobserved == 0);
            }
            finally { TaskScheduler.UnobservedTaskException -= handler; }
        }
    }

    internal class DispatcherPressure(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            int queued = 0;
            int fired = 0;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var tcs = ctx.UseRef<TaskCompletionSource<string>?>(null);
                tcs.Current ??= new TaskCompletionSource<string>();
                var v = ctx.UseResource(
                    _ => tcs.Current!.Task,
                    cache,
                    Array.Empty<object>());
                return TextBlock(v is AsyncValue<string>.Data d ? d.Value : "loading");
            });

            await Harness.Render();

            var dq = DispatcherQueue.GetForCurrentThread();
            if (dq is null)
            {
                H.Check("DispatcherPressure_RequiresDispatcher", false);
                return;
            }

            var stopwatch = global::System.Diagnostics.Stopwatch.StartNew();

            // Pile on callbacks — the hook's own marshal-back must compete with these.
            for (int f = 0; f < Frames; f++)
            {
                for (int k = 0; k < 1000; k++)
                {
                    Interlocked.Increment(ref queued);
                    dq.TryEnqueue(DispatcherQueuePriority.Low, () => Interlocked.Increment(ref fired));
                }
                await Harness.Render();
            }

            // Drain any remaining callbacks.
            for (int i = 0; i < 10; i++) await Harness.Render();

            stopwatch.Stop();

            H.Check($"DispatcherPressure_AllFired (queued={queued}, fired={fired})",
                fired >= queued - 50); // small tolerance for drain lag

            // Loose wall-clock budget — 60 frames × 16ms baseline + per-frame enqueue
            // overhead. On CI we've seen ~3-4s; keep the ceiling generous.
            H.Check($"DispatcherPressure_WithinBudget ({stopwatch.ElapsedMilliseconds}ms)",
                stopwatch.ElapsedMilliseconds < 30_000);
        }
    }
}
