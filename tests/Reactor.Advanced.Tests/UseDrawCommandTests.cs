using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Reactor.Advanced.Win2D;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Advanced.Tests;

public sealed class UseDrawCommandTests
{
    [Fact]
    public void StableDeps_ReturnSameDelegate()
    {
        var ctx = new RenderContext();
        static void Draw(CanvasDrawingSession session, CanvasDrawEventArgs args, int state) { }

        HookTestHost.BeginRender(ctx);
        var first = ctx.UseDrawCommand(1, Draw, ["same"]);

        HookTestHost.BeginRender(ctx);
        var second = ctx.UseDrawCommand(2, Draw, ["same"]);

        Assert.Same(first, second);
    }

    [Fact]
    public void ChangedDeps_ReturnNewDelegate()
    {
        var ctx = new RenderContext();
        static void Draw(CanvasDrawingSession session, CanvasDrawEventArgs args, int state) { }

        HookTestHost.BeginRender(ctx);
        var first = ctx.UseDrawCommand(1, Draw, ["a"]);

        HookTestHost.BeginRender(ctx);
        var second = ctx.UseDrawCommand(1, Draw, ["b"]);

        Assert.NotSame(first, second);
    }

    [Fact]
    public void DelegateUsesStateCapturedWhenDepsChanged()
    {
        var ctx = new RenderContext();
        var observed = 0;
        void Draw(CanvasDrawingSession session, CanvasDrawEventArgs args, int state) => observed = state;

        HookTestHost.BeginRender(ctx);
        var command = ctx.UseDrawCommand(42, Draw, [42]);

        command(null!, null!);

        Assert.Equal(42, observed);
    }
}


