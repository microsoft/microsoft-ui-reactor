using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Internal;
using Xunit;
using WinUIDock = WinUI.Dock;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Verifies the upstream WinUI.Dock.DockTarget enum maps cleanly to the
/// Reactor-side DockTarget. A regression here would silently route drag
/// outcomes to the wrong landing — covered by static mapping tests so we
/// catch enum drift the moment we re-snapshot upstream.
/// </summary>
public class BehaviorBridgeMappingTests
{
    [Theory]
    [InlineData(WinUIDock.DockTarget.Center,      DockTarget.Center)]
    [InlineData(WinUIDock.DockTarget.SplitLeft,   DockTarget.SplitLeft)]
    [InlineData(WinUIDock.DockTarget.SplitTop,    DockTarget.SplitTop)]
    [InlineData(WinUIDock.DockTarget.SplitRight,  DockTarget.SplitRight)]
    [InlineData(WinUIDock.DockTarget.SplitBottom, DockTarget.SplitBottom)]
    [InlineData(WinUIDock.DockTarget.DockLeft,    DockTarget.DockLeft)]
    [InlineData(WinUIDock.DockTarget.DockTop,     DockTarget.DockTop)]
    [InlineData(WinUIDock.DockTarget.DockRight,   DockTarget.DockRight)]
    [InlineData(WinUIDock.DockTarget.DockBottom,  DockTarget.DockBottom)]
    public void MapTarget_RoundTrips_ThroughBehaviorBridge(
        WinUIDock.DockTarget upstream,
        DockTarget reactor)
    {
        var forward = BehaviorBridge.MapTarget(upstream);
        Assert.Equal(reactor, forward);

        var roundTrip = BehaviorBridge.UnmapTarget(forward);
        Assert.Equal(upstream, roundTrip);
    }

    [Fact]
    public void MapTarget_ExhaustsAllUpstreamValues()
    {
        // If upstream adds a new DockTarget value in a re-snapshot, this test
        // fails loudly (count mismatch) so we know to extend BehaviorBridge.
        var upstreamCount = global::System.Enum.GetValues<WinUIDock.DockTarget>().Length;
        var reactorCount = global::System.Enum.GetValues<DockTarget>().Length;
        Assert.Equal(upstreamCount, reactorCount);
    }
}
