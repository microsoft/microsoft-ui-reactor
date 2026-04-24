using System.Collections.Generic;
using Microsoft.UI.Reactor.Hosting.Etw;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting.Etw;

public class EventPairingTests
{
    private static RawLayoutEvent Begin(ulong id, LayoutEventKind kind, long t, int tid = 1)
        => new(id, kind, LayoutEventPhase.Begin, t, tid, 0, 0, 0, 0);
    private static RawLayoutEvent End(ulong id, LayoutEventKind kind, long t, int tid = 1)
        => new(id, kind, LayoutEventPhase.End, t, tid, 0, 0, 0, 0);

    [Fact]
    public void ParentChild_ProducesTwoPairs_SelfExcludesChild()
    {
        var pairs = new List<PairedLayoutEvent>();
        var pairing = new EventPairing();
        pairing.Paired += p => pairs.Add(p);

        // parent: begin at 0, end at 100
        // child:  begin at 20, end at 80 (inclusive 60)
        pairing.OnEvent(Begin(0xA, LayoutEventKind.Measure, 0));
        pairing.OnEvent(Begin(0xB, LayoutEventKind.Measure, 20));
        pairing.OnEvent(End(0xB, LayoutEventKind.Measure, 80));
        pairing.OnEvent(End(0xA, LayoutEventKind.Measure, 100));

        Assert.Equal(2, pairs.Count);
        // First emitted is the child (inner pop).
        Assert.Equal(0xBUL, pairs[0].ElementId);
        Assert.Equal(60, pairs[0].InclusiveTicks);
        Assert.Equal(60, pairs[0].SelfTicks);

        Assert.Equal(0xAUL, pairs[1].ElementId);
        Assert.Equal(100, pairs[1].InclusiveTicks);
        Assert.Equal(40, pairs[1].SelfTicks);
    }

    [Fact]
    public void UnbalancedEnd_Dropped_NoException()
    {
        var pairs = new List<PairedLayoutEvent>();
        var pairing = new EventPairing();
        pairing.Paired += p => pairs.Add(p);

        pairing.OnEvent(End(0xC, LayoutEventKind.Measure, 50));
        Assert.Empty(pairs);
    }

    [Fact]
    public void MismatchEnd_FlushesStack_NoPair()
    {
        var pairs = new List<PairedLayoutEvent>();
        var pairing = new EventPairing();
        pairing.Paired += p => pairs.Add(p);

        pairing.OnEvent(Begin(0xA, LayoutEventKind.Measure, 0));
        pairing.OnEvent(Begin(0xB, LayoutEventKind.Measure, 10));
        pairing.OnEvent(End(0xC, LayoutEventKind.Measure, 20)); // mismatched
        pairing.OnEvent(End(0xA, LayoutEventKind.Measure, 30));

        // Stack was flushed on mismatch; nothing paired, and post-flush End is
        // treated as unbalanced.
        Assert.Empty(pairs);
    }

    [Fact]
    public void MeasureAndArrangeStacks_AreIndependent()
    {
        var pairs = new List<PairedLayoutEvent>();
        var pairing = new EventPairing();
        pairing.Paired += p => pairs.Add(p);

        pairing.OnEvent(Begin(0xA, LayoutEventKind.Measure, 0));
        pairing.OnEvent(Begin(0xA, LayoutEventKind.Arrange, 5));
        pairing.OnEvent(End(0xA, LayoutEventKind.Arrange, 10));
        pairing.OnEvent(End(0xA, LayoutEventKind.Measure, 20));

        Assert.Equal(2, pairs.Count);
        Assert.Equal(LayoutEventKind.Arrange, pairs[0].Kind);
        Assert.Equal(5, pairs[0].InclusiveTicks);
        Assert.Equal(LayoutEventKind.Measure, pairs[1].Kind);
        Assert.Equal(20, pairs[1].InclusiveTicks);
    }

    [Fact]
    public void DifferentThreads_DoNotShareState()
    {
        var pairs = new List<PairedLayoutEvent>();
        var pairing = new EventPairing();
        pairing.Paired += p => pairs.Add(p);

        pairing.OnEvent(Begin(0xA, LayoutEventKind.Measure, 0, tid: 1));
        pairing.OnEvent(Begin(0xB, LayoutEventKind.Measure, 5, tid: 2));
        pairing.OnEvent(End(0xA, LayoutEventKind.Measure, 10, tid: 1));
        pairing.OnEvent(End(0xB, LayoutEventKind.Measure, 15, tid: 2));

        Assert.Equal(2, pairs.Count);
        // Thread 1's A pops first at t=10.
        Assert.Equal(0xAUL, pairs[0].ElementId);
        Assert.Equal(0xBUL, pairs[1].ElementId);
    }
}
