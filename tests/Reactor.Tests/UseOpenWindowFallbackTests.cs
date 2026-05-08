using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 036 §4.3 — <see cref="RenderContext.UseOpenWindow"/> in a unit-test
/// context (no XAML Application). The fallback path must not crash and must
/// keep the hook slot count stable across renders so a hook-order exception
/// never surfaces from a parameterless probe. Live multi-window behavior is
/// covered by the selftest fixtures in <c>Reactor.AppTests.Host</c>.
/// </summary>
public class UseOpenWindowFallbackTests
{
    private sealed class DummyChild : Component
    {
        public override Element Render() => new TextBlockElement("");
    }

    private static RenderContext NewRenderContext()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        return ctx;
    }

    [Fact]
    public void UseOpenWindow_Returns_Null_Outside_A_Window()
    {
        var ctx = NewRenderContext();
        var spec = new WindowSpec { Title = "secondary" };
        var window = ctx.UseOpenWindow("settings", spec, () => new DummyChild());
        Assert.Null(window);
    }

    [Fact]
    public void UseOpenWindow_Throws_On_Null_Spec()
    {
        var ctx = NewRenderContext();
        Assert.Throws<ArgumentNullException>(
            () => ctx.UseOpenWindow("k", null!, () => new DummyChild()));
    }

    [Fact]
    public void UseOpenWindow_Throws_On_Null_Factory()
    {
        var ctx = NewRenderContext();
        var spec = new WindowSpec();
        Assert.Throws<ArgumentNullException>(
            () => ctx.UseOpenWindow("k", spec, null!));
    }

    [Fact]
    public void UseOpenWindow_Hook_Slots_Stay_Stable_Across_Renders()
    {
        // Two consecutive renders with the same key must reuse the same
        // hook slots (no HookOrderException). Stability is the hook-order
        // contract; the actual window object is null in this fixture.
        var ctx = NewRenderContext();
        var spec = new WindowSpec();
        ctx.UseOpenWindow("settings", spec, () => new DummyChild());

        ctx.BeginRender(() => { });
        ctx.UseOpenWindow("settings", spec, () => new DummyChild());

        // Same again with a different key — still must not throw.
        ctx.BeginRender(() => { });
        ctx.UseOpenWindow("about", spec, () => new DummyChild());
    }
}
