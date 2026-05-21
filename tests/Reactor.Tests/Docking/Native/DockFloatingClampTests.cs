using Microsoft.UI.Reactor.Docking.Native;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking.Native;

/// <summary>
/// Spec 045 §2.6 / §2.25 — multi-display clamp for restored floating
/// window bounds. The clamp ships separately from <c>DockFloatingWindow</c>
/// so tests can exercise the algorithm against synthetic display sets
/// without standing up a real WinUI host.
/// </summary>
public class DockFloatingClampTests
{
    private static readonly DockDisplay Primary = new(X: 0, Y: 0, Width: 1920, Height: 1080);
    private static readonly DockDisplay Secondary = new(X: 1920, Y: 0, Width: 1920, Height: 1080);

    [Fact]
    public void Clamp_BoundsFullyOnPrimary_Unchanged()
    {
        var saved = new DockFloatingBounds(X: 100, Y: 100, Width: 640, Height: 480);
        var result = DockFloatingClamp.Clamp(saved, new[] { Primary });
        Assert.Equal(saved, result);
    }

    [Fact]
    public void Clamp_FarOffScreen_RecentersOnPrimary()
    {
        var saved = new DockFloatingBounds(X: 10000, Y: 10000, Width: 480, Height: 320);
        var result = DockFloatingClamp.Clamp(saved, new[] { Primary });
        // Recenter: x = (1920 - 480) / 2 = 720, y = (1080 - 320) / 2 = 380.
        Assert.Equal(720, result.X, 1);
        Assert.Equal(380, result.Y, 1);
        Assert.Equal(480, result.Width);
        Assert.Equal(320, result.Height);
    }

    [Fact]
    public void Clamp_PartiallyVisibleAtTopEdge_KeptIfMinVisibleMet()
    {
        // 200x300 window mostly above the top edge — only 200x150 visible.
        var saved = new DockFloatingBounds(X: 200, Y: -150, Width: 200, Height: 300);
        var result = DockFloatingClamp.Clamp(saved, new[] { Primary });
        Assert.Equal(saved, result); // visible chunk meets the floor (200x150).
    }

    [Fact]
    public void Clamp_PartiallyVisibleTooSmall_Recentered()
    {
        // 600x600 mostly off the bottom-right; only ~20x20 visible at corner.
        var saved = new DockFloatingBounds(X: 1900, Y: 1060, Width: 600, Height: 600);
        var result = DockFloatingClamp.Clamp(saved, new[] { Primary });
        Assert.NotEqual(saved, result);
        // Recentered position: window clamped to (1920-64) x (1080-64) max,
        // size unchanged because 600 < 1856 / 1016.
        Assert.True(result.X >= 0);
        Assert.True(result.Y >= 0);
    }

    [Fact]
    public void Clamp_SecondaryDisplayVisible_BoundsKept()
    {
        var saved = new DockFloatingBounds(X: 2200, Y: 200, Width: 640, Height: 480);
        var result = DockFloatingClamp.Clamp(saved, new[] { Primary, Secondary });
        Assert.Equal(saved, result);
    }

    [Fact]
    public void Clamp_EmptyDisplays_ReturnsUnchanged()
    {
        var saved = new DockFloatingBounds(X: 10000, Y: 10000, Width: 480, Height: 320);
        var result = DockFloatingClamp.Clamp(saved, Array.Empty<DockDisplay>());
        Assert.Equal(saved, result);
    }

    [Fact]
    public void Clamp_OversizeWindow_ClampedAndCentered()
    {
        var saved = new DockFloatingBounds(X: -5000, Y: -5000, Width: 4000, Height: 4000);
        var result = DockFloatingClamp.Clamp(saved, new[] { Primary });
        // Width clamped to (1920 - 64) = 1856; height clamped to (1080 - 64) = 1016.
        Assert.True(result.Width <= 1856);
        Assert.True(result.Height <= 1016);
        // Recentered.
        Assert.True(result.X >= 0 && result.X + result.Width <= 1920);
        Assert.True(result.Y >= 0 && result.Y + result.Height <= 1080);
    }

    [Fact]
    public void Clamp_NegativeOrZeroSize_FloorsToMinVisible()
    {
        var saved = new DockFloatingBounds(X: 0, Y: 0, Width: 0, Height: 0);
        var result = DockFloatingClamp.Clamp(saved, new[] { Primary });
        Assert.True(result.Width >= DockFloatingClamp.MinVisibleWidth);
        Assert.True(result.Height >= DockFloatingClamp.MinVisibleHeight);
    }

    [Fact]
    public void IsSufficientlyVisible_OffScreen_ReturnsFalse()
    {
        var saved = new DockFloatingBounds(X: 10000, Y: 10000, Width: 480, Height: 320);
        Assert.False(DockFloatingClamp.IsSufficientlyVisible(saved, new[] { Primary }));
    }

    [Fact]
    public void IsSufficientlyVisible_OnScreen_ReturnsTrue()
    {
        var saved = new DockFloatingBounds(X: 100, Y: 100, Width: 480, Height: 320);
        Assert.True(DockFloatingClamp.IsSufficientlyVisible(saved, new[] { Primary }));
    }
}
