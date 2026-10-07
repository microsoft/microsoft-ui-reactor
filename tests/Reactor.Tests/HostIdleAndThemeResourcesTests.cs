using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Render-loop idle waits (<c>ReactorHost</c> / <c>ReactorHostControl</c>
/// <c>IsIdle</c> + <c>WaitForIdleAsync</c>) and <see cref="Theme.NotifyResourcesChanged"/>.
/// Hosts need a live WinUI runtime, so the shared idle loop is driven through a fake
/// dispatcher and the theme notification through fake listeners; the hosts' own pieces
/// are thin field reads and a <c>RequestRender</c> call.
/// </summary>
[Collection("ThemeResolutionCache")]
public sealed partial class HostIdleAndThemeResourcesTests
{
    // ── RenderLoopIdle ───────────────────────────────────────────────

    private sealed class FakeDispatcher
    {
        private readonly Queue<DispatcherQueueHandler> _queue = new();
        public List<DispatcherQueuePriority> Priorities { get; } = new();
        public int RefuseAfter { get; set; } = int.MaxValue;
        public int Enqueued { get; private set; }

        public bool TryEnqueue(DispatcherQueuePriority priority, DispatcherQueueHandler handler)
        {
            if (Enqueued >= RefuseAfter) return false;
            Enqueued++;
            Priorities.Add(priority);
            _queue.Enqueue(handler);
            return true;
        }

        public bool PumpOne()
        {
            if (_queue.Count == 0) return false;
            _queue.Dequeue()();
            return true;
        }
    }

    private static Task Wait(Func<bool> isIdle, FakeDispatcher d, int maxYields = 50)
        => RenderLoopIdle.WaitAsync(isIdle, d.TryEnqueue, maxYields, static () => "test");

    [Fact]
    public void Idle_AlreadyIdle_CompletesWithoutTouchingTheDispatcher()
    {
        var d = new FakeDispatcher();

        var task = Wait(() => true, d);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(0, d.Enqueued);
    }

    [Fact]
    public void Idle_CompletesOnlyOnceTheHostSettles_YieldingAtLowPriority()
    {
        var d = new FakeDispatcher();
        bool idle = false;

        var task = Wait(() => idle, d);
        Assert.False(task.IsCompleted);

        Assert.True(d.PumpOne());
        Assert.True(d.PumpOne());
        Assert.False(task.IsCompleted);

        idle = true;
        Assert.True(d.PumpOne());

        Assert.True(task.IsCompletedSuccessfully);
        Assert.All(d.Priorities, p => Assert.Equal(DispatcherQueuePriority.Low, p));
        Assert.False(d.PumpOne());
    }

    [Fact]
    public void Idle_GivesUpAfterTheYieldCap()
    {
        var d = new FakeDispatcher();

        var task = Wait(() => false, d, maxYields: 3);
        int pumps = 0;
        while (d.PumpOne()) pumps++;

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(3, pumps);
    }

    [Fact]
    public void Idle_ZeroCap_CompletesWithoutYielding()
    {
        var d = new FakeDispatcher();

        var task = Wait(() => false, d, maxYields: 0);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.False(d.PumpOne());
    }

    [Fact]
    public void Idle_RefusedInitialEnqueue_CompletesInsteadOfHanging()
    {
        var d = new FakeDispatcher { RefuseAfter = 0 };

        var task = Wait(() => false, d);

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void Idle_RefusedLaterEnqueue_CompletesInsteadOfHanging()
    {
        var d = new FakeDispatcher { RefuseAfter = 1 };

        var task = Wait(() => false, d);
        Assert.False(task.IsCompleted);
        Assert.True(d.PumpOne());

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void BothHosts_ExposeTheSameIdleContract()
    {
        // Compile-time parity: same member names, types and default argument on both hosts.
        Func<ReactorHost, bool> hostIdle = h => h.IsIdle;
        Func<ReactorHostControl, bool> controlIdle = c => c.IsIdle;
        Func<ReactorHost, Task> hostWait = h => h.WaitForIdleAsync();
        Func<ReactorHostControl, Task> controlWait = c => c.WaitForIdleAsync();
        Func<ReactorHostControl, int, Task> controlWaitCapped = (c, n) => c.WaitForIdleAsync(maxYields: n);

        Assert.All(new Delegate[] { hostIdle, controlIdle, hostWait, controlWait, controlWaitCapped }, Assert.NotNull);
    }

    // ── Theme.NotifyResourcesChanged ─────────────────────────────────

    private sealed class FakeListener : IThemeResourceListener
    {
        public int Notified { get; private set; }
        public void OnThemeResourcesChanged() => Notified++;
    }

    [Fact]
    public void NotifyResourcesChanged_ClearsTheResolutionCache()
    {
        ThemeRef.SeedResolutionCacheForTest("BrandBrush", "Light");
        ThemeRef.SeedResolutionCacheForTest("BrandBrush", "Dark");
        Assert.True(ThemeRef.ResolutionCacheCountForTest >= 2);

        Theme.NotifyResourcesChanged();

        Assert.Equal(0, ThemeRef.ResolutionCacheCountForTest);
    }

    [Fact]
    public void ResourceRefresh_IsTakenOnlyByTheHostRootPass()
    {
        var reconciler = new Reconciler();
        reconciler.RequestResourceRefresh();

        // An out-of-band top-level reconcile (ElementFactory realizing a row) must leave it pending.
        reconciler.Reconcile(null, EmptyElement.Instance, null, static () => { });
        Assert.True(reconciler.ResourceRefreshPendingForTest);

        reconciler.BeginRootPass();
        Assert.False(reconciler.ResourceRefreshPendingForTest);
        Assert.True(reconciler.ResourceRefreshArmedForTest);

        reconciler.Reconcile(null, EmptyElement.Instance, null, static () => { });
        Assert.False(reconciler.ResourceRefreshArmedForTest);
        reconciler.EndRootPass();
        Assert.False(reconciler.ResourceRefreshPendingForTest);

        // A root pass that aborts before reconciling hands the request back.
        reconciler.RequestResourceRefresh();
        reconciler.BeginRootPass();
        reconciler.EndRootPass();
        Assert.True(reconciler.ResourceRefreshPendingForTest);
        Assert.False(reconciler.ResourceRefreshArmedForTest);
    }

    [Fact]
    public void ResolutionPublishedAfterAnInvalidation_IsNotServed()
    {
        // A resolve that read the dictionaries before the invalidation and publishes after it.
        int generationAtResolveStart = ThemeRef.ResolutionGenerationForTest;
        Theme.NotifyResourcesChanged();
        ThemeRef.SeedResolutionCacheForTest("LateBrush", "Light", generationAtResolveStart);

        Assert.False(ThemeRef.IsResolutionCachedForTest("LateBrush", "Light"));

        ThemeRef.SeedResolutionCacheForTest("FreshBrush", "Light");
        Assert.True(ThemeRef.IsResolutionCachedForTest("FreshBrush", "Light"));
        ThemeRef.InvalidateResolutionCache();
    }

    [Fact]
    public void NotifyResourcesChanged_ReachesEveryRegisteredHost()
    {
        var a = new FakeListener();
        var b = new FakeListener();
        ThemeResourceListeners.Register(a);
        ThemeResourceListeners.Register(b);
        try
        {
            Theme.NotifyResourcesChanged();

            Assert.Equal(1, a.Notified);
            Assert.Equal(1, b.Notified);
        }
        finally
        {
            ThemeResourceListeners.Unregister(a);
            ThemeResourceListeners.Unregister(b);
        }
    }

    [Fact]
    public void NotifyResourcesChanged_SkipsUnregisteredHosts()
    {
        var kept = new FakeListener();
        var disposed = new FakeListener();
        ThemeResourceListeners.Register(kept);
        ThemeResourceListeners.Register(disposed);
        ThemeResourceListeners.Unregister(disposed);
        try
        {
            Theme.NotifyResourcesChanged();

            Assert.Equal(1, kept.Notified);
            Assert.Equal(0, disposed.Notified);
            Assert.False(ThemeResourceListeners.IsRegisteredForTest(disposed));
        }
        finally
        {
            ThemeResourceListeners.Unregister(kept);
        }
    }

    [Fact]
    public void NotifyResourcesChanged_AThrowingHostDoesNotStopTheOthers()
    {
        var before = new FakeListener();
        var failing = new ThrowingListener("one");
        var after = new FakeListener();
        ThemeResourceListeners.Register(before);
        ThemeResourceListeners.Register(failing);
        ThemeResourceListeners.Register(after);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(Theme.NotifyResourcesChanged);

            Assert.Equal("one", ex.Message);
            Assert.Equal(1, before.Notified);
            Assert.Equal(1, after.Notified);
        }
        finally
        {
            ThemeResourceListeners.Unregister(before);
            ThemeResourceListeners.Unregister(failing);
            ThemeResourceListeners.Unregister(after);
        }
    }

    [Fact]
    public void NotifyResourcesChanged_SeveralThrowingHostsAreAggregated()
    {
        var a = new ThrowingListener("a");
        var b = new ThrowingListener("b");
        var ok = new FakeListener();
        ThemeResourceListeners.Register(a);
        ThemeResourceListeners.Register(ok);
        ThemeResourceListeners.Register(b);
        try
        {
            var ex = Assert.Throws<AggregateException>(Theme.NotifyResourcesChanged);

            Assert.Equal(["a", "b"], ex.InnerExceptions.Select(e => e.Message).ToArray());
            Assert.Equal(1, ok.Notified);
        }
        finally
        {
            ThemeResourceListeners.Unregister(a);
            ThemeResourceListeners.Unregister(ok);
            ThemeResourceListeners.Unregister(b);
        }
    }

    private sealed class ThrowingListener(string message) : IThemeResourceListener
    {
        public void OnThemeResourcesChanged() => throw new InvalidOperationException(message);
    }

    [Fact]
    public void IsolateForTest_HidesOthersAndRestoresThem()
    {
        var other = new FakeListener();
        var kept = new FakeListener();
        var keptThenDisposed = new FakeListener();
        ThemeResourceListeners.Register(other);
        ThemeResourceListeners.Register(kept);
        ThemeResourceListeners.Register(keptThenDisposed);
        try
        {
            using (ThemeResourceListeners.IsolateForTest(kept, keptThenDisposed))
            {
                ThemeResourceListeners.Unregister(keptThenDisposed);
                Theme.NotifyResourcesChanged();
                Assert.Equal(0, other.Notified);
                Assert.Equal(1, kept.Notified);
            }

            Assert.True(ThemeResourceListeners.IsRegisteredForTest(other));
            Assert.True(ThemeResourceListeners.IsRegisteredForTest(kept));
            // A kept listener that unregistered during the scope is not resurrected.
            Assert.False(ThemeResourceListeners.IsRegisteredForTest(keptThenDisposed));
        }
        finally
        {
            ThemeResourceListeners.Unregister(other);
            ThemeResourceListeners.Unregister(kept);
            ThemeResourceListeners.Unregister(keptThenDisposed);
        }
    }

    [Fact]
    public void Listeners_DoNotKeepHostsAlive()
    {
        var weak = RegisterUnreferencedListener();

        for (int i = 0; i < 5 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive, "The theme listener list rooted a host nothing else references.");
    }

    [Fact]
    public void Register_IsIdempotent()
    {
        var listener = new FakeListener();
        ThemeResourceListeners.Register(listener);
        ThemeResourceListeners.Register(listener);
        try
        {
            Theme.NotifyResourcesChanged();

            Assert.Equal(1, listener.Notified);
        }
        finally
        {
            ThemeResourceListeners.Unregister(listener);
        }
    }

    [Fact]
    public void NotifyAll_CompactsCollectedListeners()
    {
        var weak = RegisterUnreferencedListener();
        for (int i = 0; i < 5 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.False(weak.IsAlive);
        int before = ThemeResourceListeners.EntryCountForTest();

        int notified = ThemeResourceListeners.NotifyAll();

        Assert.True(before > notified, $"expected a dead entry to compact: before={before}, live={notified}");
        Assert.Equal(notified, ThemeResourceListeners.EntryCountForTest());
    }

    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference RegisterUnreferencedListener()
    {
        var listener = new FakeListener();
        ThemeResourceListeners.Register(listener);
        return new WeakReference(listener);
    }

    [Theory]
    [InlineData(typeof(ReactorHost))]
    [InlineData(typeof(ReactorHostControl))]
    public void BothHosts_ListenForResourceChanges(Type host)
        => Assert.True(typeof(IThemeResourceListener).IsAssignableFrom(host));
}

[CollectionDefinition("ThemeResolutionCache", DisableParallelization = true)]
public sealed class ThemeResolutionCacheCollection;
