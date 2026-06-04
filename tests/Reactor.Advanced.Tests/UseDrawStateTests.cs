using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Advanced.Tests;

public sealed class UseDrawStateTests
{
    [Fact]
    public void InitRunsOnce_AndRefIdentityIsStableAcrossRerenders()
    {
        var ctx = new RenderContext();
        var initCount = 0;

        HookTestHost.BeginRender(ctx);
        var first = ctx.UseDrawState(() => new DrawState(++initCount));
        HookTestHost.FlushEffects(ctx);

        HookTestHost.BeginRender(ctx);
        var second = ctx.UseDrawState(() => new DrawState(++initCount));
        HookTestHost.FlushEffects(ctx);

        Assert.Equal(1, initCount);
        Assert.Same(first, second);
        Assert.Same(first.Current, second.Current);
        Assert.Equal(1, second.Current.Value);
    }

    [Fact]
    public void CurrentDisposable_IsDisposedOnUnmount()
    {
        var ctx = new RenderContext();
        var state = new DisposableDrawState();

        HookTestHost.BeginRender(ctx);
        var drawState = ctx.UseDrawState(() => state);
        HookTestHost.FlushEffects(ctx);

        HookTestHost.RunCleanups(ctx);

        Assert.Same(state, drawState.Current);
        Assert.True(state.Disposed);
    }

    private sealed record DrawState(int Value);

    private sealed class DisposableDrawState : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}

