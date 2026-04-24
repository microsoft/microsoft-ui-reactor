using Microsoft.UI.Reactor.Hosting.Etw;
using Microsoft.UI.Reactor.Hosting.LayoutCost;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting.LayoutCost;

public class LayoutCostAttributionTests
{
    private static PairedLayoutEvent ArrangeEnd(ulong id, long ticks, float x, float y, float w, float h)
        => new(id, LayoutEventKind.Arrange, InclusiveTicks: ticks, SelfTicks: ticks,
               RectX: x, RectY: y, RectW: w, RectH: h);

    [Fact]
    public void EventInsideComponentBounds_AttributedToComponent()
    {
        var ring = new LayoutEventRing();
        var pm = new PointerMap();
        var si = new SpatialIndex();
        var attr = new LayoutCostAttribution(ring, pm, si);

        var cid = new ComponentIdentity(1);
        attr.RegisterComponent(cid, "Outer", depth: 0);
        si.SetComponentBounds(cid, depth: 0, x: 0, y: 0, w: 100, h: 100);

        // Event at (50,50) — inside the Component bounds.
        ring.Publish(ArrangeEnd(0xA, ticks: 10_000, x: 45, y: 45, w: 10, h: 10));
        attr.Drain();

        var snap = attr.GetSnapshot();
        var target = Assert.Single(snap, s => s.Id.Value == 1);
        Assert.Equal(1, target.RenderedElementCount);
    }

    [Fact]
    public void EventOutsideAllComponents_AttributedToChrome()
    {
        var ring = new LayoutEventRing();
        var pm = new PointerMap();
        var si = new SpatialIndex();
        var attr = new LayoutCostAttribution(ring, pm, si);

        // No Components registered — event lands in the chrome bucket.
        ring.Publish(ArrangeEnd(0xB, ticks: 500, x: 1_000, y: 1_000, w: 10, h: 10));
        attr.Drain();

        var chrome = Assert.Single(attr.GetSnapshot(), s => s.Id.IsChrome);
        Assert.Equal(1, chrome.RenderedElementCount);
    }

    [Fact]
    public void InnermostComponent_Wins_DeepestDepth()
    {
        var ring = new LayoutEventRing();
        var pm = new PointerMap();
        var si = new SpatialIndex();
        var attr = new LayoutCostAttribution(ring, pm, si);

        var outer = new ComponentIdentity(1);
        var inner = new ComponentIdentity(2);
        attr.RegisterComponent(outer, "Outer", depth: 0);
        attr.RegisterComponent(inner, "Inner", depth: 1);
        si.SetComponentBounds(outer, 0, 0, 0, 200, 200);
        si.SetComponentBounds(inner, 1, 20, 20, 80, 80);

        // Event at (50,50) — inside both; should attribute to the inner.
        ring.Publish(ArrangeEnd(0xC, ticks: 5000, x: 45, y: 45, w: 10, h: 10));
        attr.Drain();

        var snap = attr.GetSnapshot();
        Assert.Equal(0, snap.First(s => s.Id.Value == 1).RenderedElementCount);
        Assert.Equal(1, snap.First(s => s.Id.Value == 2).RenderedElementCount);
    }

    // Spec §2.7: overlay chrome filter — events under an overlay-chrome
    // subtree never appear in any Component's rollup.
    [Fact]
    public void OverlayChromeElement_Filtered_NotAttributed()
    {
        var ring = new LayoutEventRing();
        var pm = new PointerMap();
        var si = new SpatialIndex();
        var attr = new LayoutCostAttribution(ring, pm, si);

        var cid = new ComponentIdentity(1);
        attr.RegisterComponent(cid, "C", 0);
        si.SetComponentBounds(cid, 0, 0, 0, 1_000, 1_000);
        attr.MarkOverlayChrome(0xDEAD);

        ring.Publish(ArrangeEnd(0xDEAD, ticks: 2_000, x: 10, y: 10, w: 10, h: 10));
        attr.Drain();

        var snap = attr.GetSnapshot();
        Assert.Equal(0, snap.First(s => s.Id.Value == 1).RenderedElementCount);
        Assert.Equal(0, snap.First(s => s.Id.IsChrome).RenderedElementCount);
    }

    [Fact]
    public void Cached_ElementIdLookup_IsReusedAcrossDrains()
    {
        var ring = new LayoutEventRing();
        var pm = new PointerMap();
        var si = new SpatialIndex();
        var attr = new LayoutCostAttribution(ring, pm, si);

        var cid = new ComponentIdentity(1);
        attr.RegisterComponent(cid, "C", 0);
        si.SetComponentBounds(cid, 0, 0, 0, 100, 100);

        // First event resolves spatially AND caches the binding on the pointer map.
        ring.Publish(ArrangeEnd(0xFACE, ticks: 1000, x: 10, y: 10, w: 10, h: 10));
        attr.Drain();
        Assert.True(pm.TryGetComponent(0xFACE, out var resolved));
        Assert.Equal(cid, resolved);

        // Second event in the same drain-tick: cache hits, attributes to the
        // same rollup so frame counts accumulate before CloseFrame runs.
        ring.Publish(ArrangeEnd(0xFACE, ticks: 500, x: 0, y: 0, w: 0, h: 0));
        ring.Publish(ArrangeEnd(0xFACE, ticks: 500, x: 0, y: 0, w: 0, h: 0));
        attr.Drain();

        var snap = attr.GetSnapshot();
        Assert.Equal(2, snap.First(s => s.Id.Value == 1).RenderedElementCount);
    }
}
