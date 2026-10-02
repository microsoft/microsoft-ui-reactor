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
        Assert.Equal(4, pumps);
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
