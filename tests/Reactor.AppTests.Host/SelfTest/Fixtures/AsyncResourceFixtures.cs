using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selfhost coverage for <c>UseResource</c> running against a real WinUI dispatcher.
/// Each fixture mounts a real <c>ReactorHost</c>, drives the hook through its
/// lifecycle (mount, resolve, deps-change, unmount), and asserts observable UI text.
///
/// Design note: the unit suite in <c>Reactor.Tests</c> already covers hook-state
/// machine correctness with a synthetic dispatcher. These fixtures target the
/// integration surface only — dispatcher marshalling, real re-render scheduling,
/// and unmount-during-fetch cleanup under a live dispatcher queue.
/// </summary>
internal static class AsyncResourceFixtures
{
    // ════════════════════════════════════════════════════════════════════
    //  BasicResolve — single fetch, Loading → Data transition over frames
    // ════════════════════════════════════════════════════════════════════

    internal class BasicResolve(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Isolated cache keeps this fixture from colliding with others when
            // run back-to-back in the same process.
            var cache = new QueryCache();
            var tcs = new TaskCompletionSource<string>();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var value = ctx.UseResource(_ => tcs.Task, cache, Array.Empty<object>());
                return TextBlock(Describe(value));
            });

            await Harness.Render();

            // First frame: fetcher is pending → Loading.
            H.Check("AsyncResource_BasicResolve_LoadingVisible",
                H.FindText("loading") is not null);

            tcs.SetResult("hello from fetcher");
            await Harness.Render();

            H.Check("AsyncResource_BasicResolve_DataVisible",
                H.FindText("data: hello from fetcher") is not null);

            H.Check("AsyncResource_BasicResolve_LoadingGone",
                H.FindText("loading") is null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  SyncCompleteNoFlash — Task.FromResult never shows Loading
    // ════════════════════════════════════════════════════════════════════

    internal class SyncCompleteNoFlash(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            // Render a few frames; capture per-frame observation of the Loading text.
            bool sawLoading = false;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var v = ctx.UseResource(
                    _ => Task.FromResult("ready"),
                    cache,
                    Array.Empty<object>());
                if (v is AsyncValue<string>.Loading) sawLoading = true;
                return TextBlock(Describe(v));
            });

            // Render several frames; the hook should stabilize on Data immediately.
            for (int i = 0; i < 5; i++) await Harness.Render();

            H.Check("AsyncResource_SyncCompleteNoFlash_NeverLoading", !sawLoading);
            H.Check("AsyncResource_SyncCompleteNoFlash_DataVisible",
                H.FindText("data: ready") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  StaleWhileRevalidate — prior value stays on screen during refetch
    // ════════════════════════════════════════════════════════════════════

    internal class StaleWhileRevalidate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            // Cache clock we can advance past StaleTime.
            var now = DateTime.UtcNow;
            cache.UtcNow = () => now;

            int invocation = 0;
            var gates = new[] { new TaskCompletionSource<string>(), new TaskCompletionSource<string>() };

            Action<int>? setEpoch = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (epoch, set) = ctx.UseState(0);
                setEpoch = set;

                var v = ctx.UseResource(
                    _ =>
                    {
                        int idx = invocation++;
                        if (idx >= gates.Length) return Task.FromResult($"overflow:{idx}");
                        return gates[idx].Task;
                    },
                    cache,
                    new object[] { epoch },
                    new ResourceOptions(
                        StaleTime: TimeSpan.FromSeconds(1),
                        CacheKey: "swr/shared"));
                return TextBlock(Describe(v));
            });

            // First fetch resolves → Data("v1").
            gates[0].SetResult("v1");
            await Harness.Render();
            H.Check("AsyncResource_SWR_InitialData", H.FindText("data: v1") is not null);

            // Advance clock past StaleTime so the entry is stale on the next mount.
            now = now.AddSeconds(5);

            // Force a new deps value → the hook sees a fresh cache key, so it would
            // go Loading. To exercise SWR we instead invalidate, which keeps the key
            // and triggers a Reloading(previous) path. Use a refresh via invalidation.
            cache.Invalidate("swr/shared");
            // Bump epoch to cause a re-render that reconciles against the cache.
            setEpoch!(1);
            await Harness.Render();

            // The refetch is in-flight (gates[1] unresolved). Hook should now show
            // Reloading(previous), keeping the old value visible.
            H.Check("AsyncResource_SWR_PreviousStillVisible",
                H.FindTextContaining("v1") is not null);
            H.Check("AsyncResource_SWR_Reloading",
                H.FindTextContaining("reloading") is not null);

            gates[1].SetResult("v2");
            await Harness.Render();

            H.Check("AsyncResource_SWR_NewData", H.FindText("data: v2") is not null);
            H.Check("AsyncResource_SWR_NoReloading", H.FindTextContaining("reloading") is null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  DepsChangeCancel — rapid deps changes; only the last result lands
    // ════════════════════════════════════════════════════════════════════

    internal class DepsChangeCancel(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            // Each deps value gets its own gate so we can control per-fetch completion.
            var gates = new Dictionary<int, TaskCompletionSource<string>>();
            int cancelled = 0;

            Action<int>? setDep = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (dep, set) = ctx.UseState(0);
                setDep = set;

                var v = ctx.UseResource(
                    ct =>
                    {
                        if (!gates.TryGetValue(dep, out var g))
                        {
                            g = new TaskCompletionSource<string>();
                            gates[dep] = g;
                        }
                        ct.Register(() => Interlocked.Increment(ref cancelled));
                        return g.Task;
                    },
                    cache,
                    new object[] { dep });
                return TextBlock($"dep={dep}:{Describe(v)}");
            });

            await Harness.Render();

            // Drive deps-change several times before any fetcher completes.
            for (int i = 1; i <= 10; i++)
            {
                setDep!(i);
                await Harness.Render();
            }

            // We've spun up 11 deps (0..10). All but the latest should have been cancelled.
            // (Some fetchers may not have actually started if deps flipped between renders —
            // at minimum we expect several cancellations.)
            H.Check($"AsyncResource_DepsChange_Cancellations (got {cancelled})", cancelled >= 5);

            // Now resolve only the latest gate.
            if (gates.TryGetValue(10, out var latest)) latest.SetResult("final");
            // Stale gates resolving late should be dropped by the hook.
            if (gates.TryGetValue(0, out var stale0)) stale0.TrySetResult("stale-0");
            if (gates.TryGetValue(5, out var stale5)) stale5.TrySetResult("stale-5");

            await Harness.Render();
            await Harness.Render();

            H.Check("AsyncResource_DepsChange_LatestWins",
                H.FindTextContaining("dep=10:data: final") is not null);
            H.Check("AsyncResource_DepsChange_NoStaleFlash",
                H.FindTextContaining("stale-0") is null &&
                H.FindTextContaining("stale-5") is null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  UnmountDuringFetch — repeated remount-with-fetch cycles; no leaks
    // ════════════════════════════════════════════════════════════════════

    internal class UnmountDuringFetch(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Subscribe to unobserved exceptions for the whole fixture — a late fetcher
            // that throws after unmount without being observed would fail here.
            int unobserved = 0;
            EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) =>
            {
                Interlocked.Increment(ref unobserved);
                e.SetObserved();
            };
            TaskScheduler.UnobservedTaskException += handler;

            try
            {
                var cache = new QueryCache();
                Action<bool>? setVisible = null;
                int fetchStarts = 0;
                int fetchesCancelled = 0;

                var host = H.CreateHost();
                host.Mount(ctx =>
                {
                    var (visible, set) = ctx.UseState(true);
                    setVisible = set;

                    if (!visible) return TextBlock("hidden");

                    // Child component does the fetch.
                    return Factories.Component<FetchChild, FetchChildProps>(
                        new FetchChildProps(cache, () => Interlocked.Increment(ref fetchStarts),
                            () => Interlocked.Increment(ref fetchesCancelled)));
                });

                // Prime: the first render captures setVisible via UseState.
                await Harness.Render();

                // Mount/unmount 25 times with in-flight fetches each time.
                for (int i = 0; i < 25; i++)
                {
                    setVisible!(true);
                    await Harness.Render();
                    setVisible!(false);
                    await Harness.Render();
                }

                // Each mount should have kicked off a fetch, and unmount should cancel it.
                H.Check($"AsyncResource_UnmountDuringFetch_FetchesStarted (got {fetchStarts})",
                    fetchStarts >= 20);
                H.Check($"AsyncResource_UnmountDuringFetch_CancellationsPropagated (got {fetchesCancelled})",
                    fetchesCancelled >= fetchStarts - 2);

                // Give any lingering continuations one more frame to settle and force a GC
                // to trigger finalization of any tasks that might throw unobserved.
                await Harness.Render();
                await Task.Run(() =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                });
                await Harness.Render();

                H.Check($"AsyncResource_UnmountDuringFetch_NoUnobserved (got {unobserved})",
                    unobserved == 0);
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= handler;
            }
        }

        internal sealed record FetchChildProps(QueryCache Cache, Action OnStart, Action OnCancel);

        internal sealed class FetchChild : Component<FetchChildProps>
        {
            public override Element Render()
            {
                var props = Props;
                var v = UseResource(
                    async ct =>
                    {
                        props.OnStart();
                        ct.Register(props.OnCancel);
                        // Never completes within a test frame — unmount must cancel.
                        await Task.Delay(TimeSpan.FromSeconds(30), ct);
                        return "never";
                    },
                    props.Cache,
                    // Make the cache key unique per mount so we always start fresh.
                    new object[] { Guid.NewGuid() });
                return TextBlock(Describe(v));
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  FocusRevalidate — simulate a window-activation sweep; the enrolled
    //  hook invalidates, re-renders, and a fresh fetch lands.
    // ════════════════════════════════════════════════════════════════════

    internal class FocusRevalidate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            var baseTime = DateTime.UtcNow;
            cache.UtcNow = () => baseTime;

            var service = new FocusRevalidationService(cache)
            {
                ThrottleWindow = TimeSpan.Zero,
                UtcNow = () => baseTime,
            };

            FocusChild.Reset();

            var host = H.CreateHost();
            host.Mount(ctx =>
                Factories.Component<FocusChild, FocusChildProps>(new FocusChildProps(cache))
                    .Provide(AppContexts.FocusRevalidation, (FocusRevalidationService?)service));

            await Harness.Render();

            H.Check($"FocusRevalidate_InitialFetch (count={FocusChild.Fetches})", FocusChild.Fetches == 1);
            H.Check("FocusRevalidate_InitialDataVisible",
                H.FindText("data: v1") is not null);

            // Advance past StaleTime and fire a revalidation sweep.
            baseTime = baseTime.AddSeconds(1);
            var invalidated = service.RevalidateNow();
            H.Check($"FocusRevalidate_SweepInvalidated (count={invalidated.Count})",
                invalidated.Count == 1);

            // The hook's EntryChanged subscription triggers a re-render; next render
            // sees cache miss, refetches, and — because the previous value was Data —
            // passes through Reloading on its way to new Data.
            await Harness.Render();
            await Harness.Render();

            H.Check($"FocusRevalidate_RefetchHappened (count={FocusChild.Fetches})",
                FocusChild.Fetches == 2);
            H.Check("FocusRevalidate_NewDataVisible",
                H.FindText("data: v2") is not null);
        }
    }

    internal sealed record FocusChildProps(QueryCache Cache);

    internal sealed class FocusChild : Component<FocusChildProps>
    {
        static int _fetches;
        public static int Fetches => Volatile.Read(ref _fetches);
        public static void Reset() => Volatile.Write(ref _fetches, 0);

        public override Element Render()
        {
            var v = UseResource(
                _ => Task.FromResult($"v{Interlocked.Increment(ref _fetches)}"),
                Props.Cache,
                Array.Empty<object>(),
                new ResourceOptions(
                    CacheKey: "focus/target",
                    StaleTime: TimeSpan.FromMilliseconds(100),
                    RefetchOnWindowFocus: true));
            return TextBlock(v switch
            {
                AsyncValue<string>.Loading => "loading",
                AsyncValue<string>.Data d => $"data: {d.Value}",
                AsyncValue<string>.Reloading r => $"reloading: {r.Previous}",
                AsyncValue<string>.Error e => $"error: {e.Exception.Message}",
                _ => "?",
            });
        }

        protected internal override bool ShouldUpdate(FocusChildProps? oldProps, FocusChildProps? newProps) => true;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Shared helpers
    // ════════════════════════════════════════════════════════════════════

    static string Describe<T>(AsyncValue<T> v) => v switch
    {
        AsyncValue<T>.Loading => "loading",
        AsyncValue<T>.Data d => $"data: {d.Value}",
        AsyncValue<T>.Reloading r => $"reloading: {r.Previous}",
        AsyncValue<T>.Error e => $"error: {e.Exception.Message}",
        _ => "?",
    };
}
