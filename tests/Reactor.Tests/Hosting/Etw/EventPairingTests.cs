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
    private static RawLayoutEvent BeginAt(ulong id, LayoutEventKind kind, long t, float x, float y, int tid = 1)
        => new(id, kind, LayoutEventPhase.Begin, t, tid, x, y, 0, 0);
    private static RawLayoutEvent EndAt(ulong id, LayoutEventKind kind, long t, float x, float y, float w, float h, int tid = 1)
        => new(id, kind, LayoutEventPhase.End, t, tid, x, y, w, h);

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

    /// <summary>
    /// ETW Arrange events carry parent-relative offsets. The pairing layer
    /// accumulates offsets up the open Arrange stack so paired events
    /// surface root-relative rects to the attribution layer. Without this,
    /// deep elements with small offsets land on whichever Component happens
    /// to sit at the top-left of the screen.
    /// </summary>
    [Fact]
    public void NestedArrange_PairedRect_IsRootRelative()
    {
        var pairs = new List<PairedLayoutEvent>();
        var pairing = new EventPairing();
        pairing.Paired += p => pairs.Add(p);

        // Parent at root-origin (0, 0) with size 1000x1000.
        pairing.OnEvent(BeginAt(0xA, LayoutEventKind.Arrange, t: 0, x: 0, y: 0));
        // Child at parent-relative (50, 200), size 200x40.
        pairing.OnEvent(BeginAt(0xB, LayoutEventKind.Arrange, t: 10, x: 50, y: 200));
        // Grandchild at child-relative (10, 5), size 100x20.
        pairing.OnEvent(BeginAt(0xC, LayoutEventKind.Arrange, t: 20, x: 10, y: 5));
        pairing.OnEvent(EndAt(0xC, LayoutEventKind.Arrange, t: 30, x: 10, y: 5, w: 100, h: 20));
        pairing.OnEvent(EndAt(0xB, LayoutEventKind.Arrange, t: 40, x: 50, y: 200, w: 200, h: 40));
        pairing.OnEvent(EndAt(0xA, LayoutEventKind.Arrange, t: 50, x: 0, y: 0, w: 1000, h: 1000));

        Assert.Equal(3, pairs.Count);

        // Grandchild's root rect = parent (0,0) + child (50,200) + grandchild (10,5) = (60, 205).
        var grandchild = pairs[0];
        Assert.Equal(0xCUL, grandchild.ElementId);
        Assert.Equal(60f, grandchild.RectX);
        Assert.Equal(205f, grandchild.RectY);
        Assert.Equal(100f, grandchild.RectW);
        Assert.Equal(20f, grandchild.RectH);

        // Child's root rect = (0,0) + (50,200) = (50, 200).
        var child = pairs[1];
        Assert.Equal(0xBUL, child.ElementId);
        Assert.Equal(50f, child.RectX);
        Assert.Equal(200f, child.RectY);

        // Parent stays at origin.
        var parent = pairs[2];
        Assert.Equal(0xAUL, parent.ElementId);
        Assert.Equal(0f, parent.RectX);
        Assert.Equal(0f, parent.RectY);
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
