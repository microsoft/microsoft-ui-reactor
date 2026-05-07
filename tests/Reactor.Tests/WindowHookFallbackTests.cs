using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 036 §3.3 / §7.1 — hooks that resolve through
/// <c>ReactorApp.ActiveHostInternal?.OwningWindow</c> must return their
/// documented fallback values when no host is registered (the unit-test
/// scenario, the tray-flyout scenario in production). Verifying this
/// without a XAML Application context proves the no-host path is robust.
/// </summary>
public class WindowHookFallbackTests
{
    private static RenderContext NewRenderContext()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        return ctx;
    }

    [Fact]
    public void UseWindow_Returns_Null_Outside_A_Window()
    {
        var ctx = NewRenderContext();
        Assert.Null(ctx.UseWindow());
    }

    [Fact]
    public void UseWindowSize_Returns_Zero_Tuple_Outside_A_Window()
    {
        var ctx = NewRenderContext();
        var (w, h) = ctx.UseWindowSize();
        Assert.Equal(0d, w);
        Assert.Equal(0d, h);
    }

    [Fact]
    public void UseDpi_Returns_System_Or_96_Outside_A_Window()
    {
        var ctx = NewRenderContext();
        var dpi = ctx.UseDpi();
        // System DPI is always >= 96 on Windows; 96 is the baseline fallback.
        Assert.True(dpi >= 96, $"Expected DPI >= 96, got {dpi}");
    }

    [Fact]
    public void UseWindowState_Returns_Normal_Outside_A_Window()
    {
        var ctx = NewRenderContext();
        Assert.Equal(WindowState.Normal, ctx.UseWindowState());
    }

    [Fact]
    public void UseIsActive_Returns_True_Outside_A_Window()
    {
        // The tray-flyout fallback is "active" while shown — see spec §7.1.
        var ctx = NewRenderContext();
        Assert.True(ctx.UseIsActive());
    }

    [Fact]
    public void UseClosingGuard_Is_Noop_Outside_A_Window()
    {
        var ctx = NewRenderContext();
        bool guardCalled = false;
        ctx.UseClosingGuard(() => { guardCalled = true; return true; });
        // Without an OwningWindow there's no Closing event source to drive
        // the guard; it must still register cleanly so the surrounding
        // component code stays portable between window / flyout contexts.
        Assert.False(guardCalled);
    }

    [Fact]
    public void UseBreakpoint_Returns_False_Outside_A_Window()
    {
        var ctx = NewRenderContext();
        Assert.False(ctx.UseBreakpoint(640));
    }
}
