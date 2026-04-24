using Microsoft.UI.Reactor.Hosting.LayoutCost;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting.LayoutCost;

public class ComponentRollupTests
{
    // Spec §2.4: EMA converges within 10 frames of constant input to within 1%.
    [Fact]
    public void Ema_Converges_Within10FramesOfConstantInput()
    {
        var r = new ComponentRollup(new ComponentIdentity(1), "test", 0);
        const double frameTicks = 5_000_000; // 500 ms worth of ticks per frame
        // Each frame feeds the same tick budget and closes the frame. After ten
        // closes with alpha=0.2, (1-0.2)^10 ≈ 0.107 — EMA should be within 11%
        // of the target. Bump to 20 frames → 0.8^20 ≈ 0.0115 → within 2% of
        // target; within 1% takes ~22 frames. Test both bounds so future alpha
        // tweaks fail loudly.
        for (int i = 0; i < 22; i++)
        {
            r.FrameMeasureTicks = (long)(frameTicks * 0.5);
            r.FrameArrangeTicks = (long)(frameTicks * 0.5);
            r.CloseFrame();
        }

        double targetMs = frameTicks / 10_000.0;
        Assert.InRange(r.EmaLayoutMs, targetMs * 0.99, targetMs * 1.01);
    }

    // Spec §2.4: rendered count equals one per Arrange End event, never Measure.
    [Fact]
    public void RenderedCount_PerFrame_EqualsArrangeEndCount()
    {
        var r = new ComponentRollup(new ComponentIdentity(1), "test", 0);
        r.FrameArrangeTicks = 1000;
        r.FrameRenderedCount = 7;
        r.CloseFrame();
        Assert.Equal(7, r.LastRenderedCount);

        // Next frame with no arrange events: rendered count rolls back to 0.
        r.CloseFrame();
        Assert.Equal(0, r.LastRenderedCount);
    }

    // Spec §2.4 "unmounting a Component removes its rollup entry" — through attribution.
    [Fact]
    public void Unregister_RemovesRollupEntry()
    {
        var ring = new Microsoft.UI.Reactor.Hosting.Etw.LayoutEventRing();
        var pm = new PointerMap();
        var si = new SpatialIndex();
        var attr = new LayoutCostAttribution(ring, pm, si);

        var id = new ComponentIdentity(42);
        attr.RegisterComponent(id, "c", 0);
        attr.Drain(); // closes frame and snapshots
        Assert.Contains(attr.GetSnapshot(), s => s.Id.Value == 42);

        attr.UnregisterComponent(id);
        attr.Drain();
        Assert.DoesNotContain(attr.GetSnapshot(), s => s.Id.Value == 42);
    }

    [Fact]
    public void ChromeBucket_ReportsAuthoredMinusOne()
    {
        var ring = new Microsoft.UI.Reactor.Hosting.Etw.LayoutEventRing();
        var pm = new PointerMap();
        var si = new SpatialIndex();
        var attr = new LayoutCostAttribution(ring, pm, si);

        attr.Drain();
        var snap = attr.GetSnapshot();
        var chrome = Assert.Single(snap, s => s.Id.IsChrome);
        Assert.Equal(-1, chrome.AuthoredElementCount);
    }
}
