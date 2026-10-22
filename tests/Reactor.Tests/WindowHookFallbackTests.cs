using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 036 §3.3 / §7.1 — window hooks resolve the window from the owner the
/// host's reconciler records on each <see cref="RenderContext"/>. A context
/// begun with no owner (the unit-test scenario; tray flyouts and
/// <c>ReactorHostControl</c> embeds in production) must return the documented
/// fallback values. Verifying this without a XAML Application context proves
/// the no-owner path is robust.
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

    // Issue #1396 — the no-window branch must reserve the same hook slots, with the same
    // state types, as the window branch (UseState(T) + UseEffect), so one context can cross
    // the no-window ↔ window transition without a hook-order mismatch. The window branch
    // needs a live ReactorWindow, so its shape is pinned here from the source and the
    // transition itself is exercised by the WindowHookOwner selftests.
    public static TheoryData<string, Type> WindowBranchShapes() => new()
    {
        { nameof(RenderContext.UseWindowState), typeof(WindowState) },
        { nameof(RenderContext.UseIsActive), typeof(bool) },
        { nameof(RenderContext.UseDpi), typeof(uint) },
        { nameof(RenderContext.UseWindowSize), typeof((double, double)) },
        { nameof(RenderContext.UseWindowPosition), typeof((double, double)) },
        { nameof(RenderContext.UseIsCovered), typeof(bool) },
    };

    [Theory]
    [MemberData(nameof(WindowBranchShapes))]
    public void No_Window_Branch_Reserves_The_Window_Branch_Slots(string hook, Type stateType)
    {
        var ctx = NewRenderContext();
        switch (hook)
        {
            case nameof(RenderContext.UseWindowState): ctx.UseWindowState(); break;
            case nameof(RenderContext.UseIsActive): ctx.UseIsActive(); break;
            case nameof(RenderContext.UseDpi): ctx.UseDpi(); break;
            case nameof(RenderContext.UseWindowSize): ctx.UseWindowSize(); break;
            case nameof(RenderContext.UseWindowPosition): ctx.UseWindowPosition(); break;
            case nameof(RenderContext.UseIsCovered): ctx.UseIsCovered(); break;
        }

        var slots = ctx.SnapshotHooks();
        Assert.Equal(new[] { "useState", "useEffect" }, slots.Select(s => s.Hook).ToArray());
        Assert.Equal(stateType, slots[0].ValueType);
    }
}
