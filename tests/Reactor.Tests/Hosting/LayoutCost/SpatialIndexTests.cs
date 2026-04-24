using Microsoft.UI.Reactor.Hosting.LayoutCost;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting.LayoutCost;

public class SpatialIndexTests
{
    [Fact]
    public void AttributeByPoint_ReturnsNull_WhenNoBoundsContainPoint()
    {
        var si = new SpatialIndex();
        si.SetComponentBounds(new ComponentIdentity(1), depth: 0, x: 0, y: 0, w: 10, h: 10);

        Assert.Null(si.AttributeByPoint(100, 100));
    }

    [Fact]
    public void AttributeByPoint_DeepestWins()
    {
        var si = new SpatialIndex();
        var outer = new ComponentIdentity(1);
        var inner = new ComponentIdentity(2);
        si.SetComponentBounds(outer, depth: 0, x: 0, y: 0, w: 100, h: 100);
        si.SetComponentBounds(inner, depth: 3, x: 10, y: 10, w: 40, h: 40);

        Assert.Equal(inner, si.AttributeByPoint(20, 20));
    }

    [Fact]
    public void RemoveComponent_DropsIt_FromAttribution()
    {
        var si = new SpatialIndex();
        var c = new ComponentIdentity(9);
        si.SetComponentBounds(c, 0, 0, 0, 100, 100);
        si.RemoveComponent(c);
        Assert.Null(si.AttributeByPoint(50, 50));
    }

    [Fact]
    public void ElementRect_RoundTrips()
    {
        var si = new SpatialIndex();
        si.RecordElementRect(0xA, 1, 2, 3, 4);
        Assert.True(si.TryGetElementRect(0xA, out var r));
        Assert.Equal((1f, 2f, 3f, 4f), (r.x, r.y, r.w, r.h));

        si.ForgetElement(0xA);
        Assert.False(si.TryGetElementRect(0xA, out _));
    }
}
