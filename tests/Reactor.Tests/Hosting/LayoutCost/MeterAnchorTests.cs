using Microsoft.UI.Reactor.Hosting.LayoutCost;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting.LayoutCost;

public class MeterAnchorTests
{
    private const float CanvasW = 1000f;
    private const float CanvasH = 800f;

    [Fact]
    public void TinySubtree_BelowMinDimension_SuppressesBadge()
    {
        // 30 px tall Component — spec §3.6 "a 30 px tall Component does not get a badge".
        Assert.False(MeterAnchor.TryComputePosition(
            subtreeX: 100, subtreeY: 100, subtreeW: 200, subtreeH: 30,
            canvasW: CanvasW, canvasH: CanvasH,
            out _, out _));
    }

    [Fact]
    public void NarrowSubtree_BelowMinDimension_SuppressesBadge()
    {
        Assert.False(MeterAnchor.TryComputePosition(
            subtreeX: 0, subtreeY: 0, subtreeW: 20, subtreeH: 400,
            canvasW: CanvasW, canvasH: CanvasH,
            out _, out _));
    }

    [Fact]
    public void NormalSubtree_AnchorsAtTopRight_WithInwardOffset()
    {
        Assert.True(MeterAnchor.TryComputePosition(
            subtreeX: 100, subtreeY: 50, subtreeW: 400, subtreeH: 300,
            canvasW: CanvasW, canvasH: CanvasH,
            out var x, out var y));
        // Anchor = right edge of subtree - BadgeWidth - InwardOffsetX
        Assert.Equal(100f + 400f - MeterAnchor.BadgeWidth - MeterAnchor.InwardOffsetX, x);
        Assert.Equal(50f, y);
    }

    [Fact]
    public void NearRightEdge_BadgeStaysFullyOnScreen()
    {
        // Subtree sits against canvas right edge — badge must be clamped in.
        Assert.True(MeterAnchor.TryComputePosition(
            subtreeX: 900, subtreeY: 100, subtreeW: 200, subtreeH: 200,
            canvasW: CanvasW, canvasH: CanvasH,
            out var x, out _));
        Assert.True(x + MeterAnchor.BadgeWidth <= CanvasW,
            $"badge right edge {x + MeterAnchor.BadgeWidth} overflowed canvas {CanvasW}");
    }

    [Fact]
    public void SubtreeFullyOffCanvas_Suppressed()
    {
        Assert.False(MeterAnchor.TryComputePosition(
            subtreeX: 2000, subtreeY: 100, subtreeW: 200, subtreeH: 200,
            canvasW: CanvasW, canvasH: CanvasH,
            out _, out _));
    }

    [Fact]
    public void NegativeOrigin_Clipped_NotPushedOffCanvas()
    {
        // Subtree origin slightly negative — computed anchor is inside canvas.
        Assert.True(MeterAnchor.TryComputePosition(
            subtreeX: -10, subtreeY: -10, subtreeW: 200, subtreeH: 200,
            canvasW: CanvasW, canvasH: CanvasH,
            out var x, out var y));
        Assert.True(x >= 0 && y >= 0);
    }
}
