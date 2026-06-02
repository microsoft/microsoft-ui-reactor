using Microsoft.Graphics.Canvas;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Advanced.Tests;

public sealed class UseCanvasResourcesTests
{
    [Fact]
    public async Task CreateRunsOnceAcrossRerenders_ForSameDeviceAndDelegates()
    {
        if (!TryGetSharedDevice(out _)) return;

        var ctx = new RenderContext();
        var createCount = 0;
        ValueTask<FakeResource> Create(CanvasDevice device)
        {
            createCount++;
            return ValueTask.FromResult(new FakeResource(device));
        }

        HookTestHost.BeginRender(ctx);
        var resources = ctx.UseCanvasResources(Create);
        HookTestHost.FlushEffects(ctx);
        await WaitUntil(() => resources.Current is not null);

        HookTestHost.BeginRender(ctx);
        var second = ctx.UseCanvasResources(Create);
        HookTestHost.FlushEffects(ctx);
        await Task.Yield();

        Assert.Same(resources, second);
        Assert.Equal(1, createCount);
        Assert.NotNull(resources.Current);
    }

    [Fact]
    public async Task CustomDisposeRunsOnUnmount()
    {
        if (!TryGetSharedDevice(out _)) return;

        var ctx = new RenderContext();
        var disposeCount = 0;

        HookTestHost.BeginRender(ctx);
        var resources = ctx.UseCanvasResources(
            static device => ValueTask.FromResult(new FakeResource(device)),
            resource =>
            {
                disposeCount++;
                resource.MarkDisposed();
            });
        HookTestHost.FlushEffects(ctx);
        await WaitUntil(() => resources.Current is not null);
        var created = resources.Current!;

        HookTestHost.RunCleanups(ctx);

        Assert.Null(resources.Current);
        Assert.Equal(1, disposeCount);
        Assert.True(created.Disposed);
    }

    [Fact]
    public async Task IDisposableFallbackRunsOnUnmount()
    {
        if (!TryGetSharedDevice(out _)) return;

        var ctx = new RenderContext();

        HookTestHost.BeginRender(ctx);
        var resources = ctx.UseCanvasResources(static device => ValueTask.FromResult(new FakeResource(device)));
        HookTestHost.FlushEffects(ctx);
        await WaitUntil(() => resources.Current is not null);
        var created = resources.Current!;

        HookTestHost.RunCleanups(ctx);

        Assert.Null(resources.Current);
        Assert.True(created.Disposed);
    }

    [Fact]
    public async Task UnmountCancelsInFlightCreateWithoutPublishingResource()
    {
        if (!TryGetSharedDevice(out _)) return;

        var ctx = new RenderContext();
        var pending = new TaskCompletionSource<FakeResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = new FakeResource(null);

        HookTestHost.BeginRender(ctx);
        var resources = ctx.UseCanvasResources(_ => new ValueTask<FakeResource>(pending.Task));
        HookTestHost.FlushEffects(ctx);

        HookTestHost.RunCleanups(ctx);
        pending.SetResult(created);

        await WaitUntil(() => created.Disposed);
        Assert.Null(resources.Current);
    }

    /// <summary>
    /// CanvasDevice.DeviceLost cannot be raised synthetically from a headless
    /// unit test. Phase 3 selftest fixtures cover real Win2D device/resource
    /// lifetime in a WinUI window.
    /// </summary>
    [Fact(Skip = "Synthetic CanvasDevice.DeviceLost cannot be raised headlessly; covered by Phase 3 selftests.")]
    public void DeviceLost_RecreatesResources()
    {
    }

    private static bool TryGetSharedDevice(out CanvasDevice? device)
    {
        try
        {
            device = CanvasDevice.GetSharedDevice();
            return true;
        }
        catch
        {
            device = null;
            return false;
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token);
        }
    }

    private sealed class FakeResource : IDisposable
    {
        public FakeResource(CanvasDevice? device) => Device = device;

        public CanvasDevice? Device { get; }

        public bool Disposed { get; private set; }

        public void MarkDisposed() => Disposed = true;

        public void Dispose() => Disposed = true;
    }
}

