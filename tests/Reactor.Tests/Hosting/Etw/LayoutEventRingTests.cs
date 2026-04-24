using Microsoft.UI.Reactor.Hosting.Etw;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting.Etw;

public class LayoutEventRingTests
{
    private static PairedLayoutEvent E(ulong id) => new(
        id, LayoutEventKind.Measure, 100, 100, 0, 0, 0, 0);

    [Fact]
    public void Capacity_MustBePowerOfTwo()
    {
        Assert.Throws<ArgumentException>(() => new LayoutEventRing(100));
    }

    [Fact]
    public void Publish_BelowCapacity_NoDrops()
    {
        var ring = new LayoutEventRing(8);
        for (ulong i = 0; i < 5; i++) ring.Publish(E(i));
        Assert.Equal(0, ring.DroppedCount);
        Assert.Equal(5, ring.Depth);

        Span<PairedLayoutEvent> dest = stackalloc PairedLayoutEvent[16];
        int n = ring.Drain(dest);
        Assert.Equal(5, n);
        for (int i = 0; i < n; i++) Assert.Equal((ulong)i, dest[i].ElementId);
    }

    [Fact]
    public void Publish_BeyondCapacity_DropsOldest_KeepsNewest()
    {
        var ring = new LayoutEventRing(4);
        for (ulong i = 0; i < 10; i++) ring.Publish(E(i));
        Assert.Equal(6, ring.DroppedCount);
        Assert.Equal(4, ring.Depth);

        Span<PairedLayoutEvent> dest = stackalloc PairedLayoutEvent[8];
        int n = ring.Drain(dest);
        Assert.Equal(4, n);
        // After dropping 6 oldest, the remaining four should be ids 6..9.
        Assert.Equal(6UL, dest[0].ElementId);
        Assert.Equal(7UL, dest[1].ElementId);
        Assert.Equal(8UL, dest[2].ElementId);
        Assert.Equal(9UL, dest[3].ElementId);
    }

    [Fact]
    public void Interleaved_ProduceAndDrain_PreservesOrder()
    {
        var ring = new LayoutEventRing(8);
        Span<PairedLayoutEvent> dest = stackalloc PairedLayoutEvent[4];

        ring.Publish(E(1));
        ring.Publish(E(2));
        int n1 = ring.Drain(dest);
        Assert.Equal(2, n1);
        Assert.Equal(1UL, dest[0].ElementId);
        Assert.Equal(2UL, dest[1].ElementId);

        ring.Publish(E(3));
        ring.Publish(E(4));
        ring.Publish(E(5));
        int n2 = ring.Drain(dest);
        Assert.Equal(3, n2);
        Assert.Equal(3UL, dest[0].ElementId);
        Assert.Equal(5UL, dest[2].ElementId);
    }

    [Fact]
    public void StressProducerConsumer_1M_NoLossBeyondOverflow()
    {
        const int capacity = 1024;
        const int total = 1_000_000;
        var ring = new LayoutEventRing(capacity);

        long producedDone = 0;
        var producer = new Thread(() =>
        {
            for (ulong i = 0; i < total; i++) ring.Publish(E(i));
            Interlocked.Exchange(ref producedDone, 1);
        }) { IsBackground = true };

        long consumed = 0;
        var consumer = new Thread(() =>
        {
            var buf = new PairedLayoutEvent[256];
            while (Interlocked.Read(ref producedDone) == 0 || ring.Depth > 0)
            {
                int n = ring.Drain(buf);
                consumed += n;
            }
        }) { IsBackground = true };

        producer.Start();
        consumer.Start();
        Assert.True(producer.Join(TimeSpan.FromSeconds(30)));
        Assert.True(consumer.Join(TimeSpan.FromSeconds(30)));

        long produced = total;
        long dropped = ring.DroppedCount;
        Assert.Equal(produced, consumed + dropped);
    }
}
